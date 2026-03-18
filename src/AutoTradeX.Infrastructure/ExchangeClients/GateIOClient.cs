/*
 * ============================================================================
 * AutoTrade-X - Gate.io Exchange Client
 * ============================================================================
 * Real implementation for Gate.io Spot API v4
 * Documentation: https://www.gate.io/docs/developers/apiv4/
 * ============================================================================
 */

using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoTradeX.Infrastructure.ExchangeClients;

public class GateIOClient : BaseExchangeClient
{
    public override string ExchangeName => "Gate.io";

    public GateIOClient(ExchangeConfig config, ILoggingService logger)
        : base(config, logger)
    {
    }

    #region Market Data (Public APIs)

    public override async Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedSymbol = NormalizeSymbol(symbol);
            var response = await GetAsync<List<GateIOTickerData>>(
                $"/api/v4/spot/tickers?currency_pair={normalizedSymbol}",
                cancellationToken);

            if (response == null || response.Count == 0)
            {
                throw new Exception($"Failed to get ticker for {symbol}");
            }

            var data = response[0];

            return new Ticker
            {
                Symbol = symbol,
                Exchange = ExchangeName,
                BidPrice = decimal.Parse(data.HighestBid ?? "0", CultureInfo.InvariantCulture),
                AskPrice = decimal.Parse(data.LowestAsk ?? "0", CultureInfo.InvariantCulture),
                BidQuantity = 0, // Gate.io ticker doesn't include quantities
                AskQuantity = 0,
                LastPrice = decimal.Parse(data.Last ?? "0", CultureInfo.InvariantCulture),
                Volume24h = decimal.Parse(data.BaseVolume ?? "0", CultureInfo.InvariantCulture),
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"GetTickerAsync error for {symbol}: {ex.Message}");
            throw;
        }
    }

    public override async Task<OrderBook> GetOrderBookAsync(string symbol, int depth = 20, CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedSymbol = NormalizeSymbol(symbol);
            var response = await GetAsync<GateIOOrderBookData>(
                $"/api/v4/spot/order_book?currency_pair={normalizedSymbol}&limit={Math.Min(depth, 100)}",
                cancellationToken);

            if (response == null)
            {
                throw new Exception($"Failed to get order book for {symbol}");
            }

            var orderBook = new OrderBook
            {
                Symbol = symbol,
                Exchange = ExchangeName,
                Timestamp = DateTime.UtcNow
            };

            foreach (var bid in response.Bids ?? Array.Empty<string[]>())
            {
                if (bid.Length >= 2)
                {
                    orderBook.Bids.Add(new OrderBookEntry(
                        decimal.Parse(bid[0], CultureInfo.InvariantCulture),
                        decimal.Parse(bid[1], CultureInfo.InvariantCulture)
                    ));
                }
            }

            foreach (var ask in response.Asks ?? Array.Empty<string[]>())
            {
                if (ask.Length >= 2)
                {
                    orderBook.Asks.Add(new OrderBookEntry(
                        decimal.Parse(ask[0], CultureInfo.InvariantCulture),
                        decimal.Parse(ask[1], CultureInfo.InvariantCulture)
                    ));
                }
            }

            return orderBook;
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"GetOrderBookAsync error for {symbol}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Get ALL tickers from Gate.io in one API call
    /// Endpoint: GET /api/v4/spot/tickers (without currency_pair returns all)
    /// </summary>
    public override async Task<Dictionary<string, Ticker>> GetAllTickersAsync(
        string? quoteAsset = null,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, Ticker>();

        try
        {
            _logger.LogInfo(ExchangeName, "GetAllTickersAsync: Fetching all tickers...");

            var response = await GetAsync<List<GateIOTickerData>>(
                "/api/v4/spot/tickers",
                cancellationToken);

            if (response == null || response.Count == 0)
            {
                _logger.LogWarning(ExchangeName, "GetAllTickersAsync: No data returned from API");
                return result;
            }

            _logger.LogInfo(ExchangeName, $"GetAllTickersAsync: Got {response.Count} tickers from API");

            foreach (var data in response)
            {
                var symbol = data.CurrencyPair ?? "";

                // Filter by quote asset if specified (e.g., "USDT")
                // Gate.io format: BASE_QUOTE (e.g., BTC_USDT)
                if (!string.IsNullOrEmpty(quoteAsset))
                {
                    if (!symbol.EndsWith($"_{quoteAsset}", StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                // Skip pairs with zero volume (inactive)
                var volume = decimal.TryParse(data.BaseVolume, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
                var lastPrice = decimal.TryParse(data.Last, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0;

                if (volume <= 0 && lastPrice <= 0)
                    continue;

                result[symbol] = new Ticker
                {
                    Symbol = symbol,
                    Exchange = ExchangeName,
                    BidPrice = decimal.TryParse(data.HighestBid, NumberStyles.Any, CultureInfo.InvariantCulture, out var bid) ? bid : 0,
                    AskPrice = decimal.TryParse(data.LowestAsk, NumberStyles.Any, CultureInfo.InvariantCulture, out var ask) ? ask : 0,
                    BidQuantity = 0,
                    AskQuantity = 0,
                    LastPrice = lastPrice,
                    Volume24h = volume,
                    Timestamp = DateTime.UtcNow
                };
            }

            _logger.LogInfo(ExchangeName, $"GetAllTickersAsync: Returning {result.Count} active tickers");
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"GetAllTickersAsync error: {ex.Message}");
        }

        return result;
    }

    #endregion

    #region Connection Test

    /// <summary>
    /// Test connection to Gate.io API with proper validation
    /// </summary>
    public override async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInfo(ExchangeName, "Testing Gate.io API connection...");

            // Step 1: Test public API - get server time
            var serverTimeResponse = await GetAsync<GateIOServerTime>(
                "/api/v4/spot/time",
                cancellationToken);

            if (serverTimeResponse == null)
            {
                _logger.LogWarning(ExchangeName, "Failed to get Gate.io server time");
                return false;
            }

            _logger.LogInfo(ExchangeName, "Gate.io public API reachable");

            // Step 2: Test ticker API with BTC_USDT
            var ticker = await GetTickerAsync("BTC_USDT", cancellationToken);
            if (ticker == null)
            {
                _logger.LogWarning(ExchangeName, "Failed to get BTC_USDT ticker");
                return false;
            }

            _logger.LogInfo(ExchangeName, $"BTC_USDT: ${ticker.LastPrice:N2}");

            // Step 3: If API credentials are configured, test private API
            if (HasCredentials())
            {
                try
                {
                    var balance = await GetBalanceAsync(cancellationToken);
                    if (balance != null)
                    {
                        _logger.LogInfo(ExchangeName, "API credentials verified successfully");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ExchangeName, $"API credential test failed: {ex.Message}");
                    // Still return true if public API works
                }
            }

            IsConnected = true;
            _logger.LogInfo(ExchangeName, "Gate.io connection test successful");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"Connection test failed: {ex.Message}");
            IsConnected = false;
            return false;
        }
    }

    #endregion

    #region API Permissions

    /// <summary>
    /// Get API key permissions from Gate.io
    /// Gate.io ไม่มี API สำหรับเช็ค permissions โดยตรง
    /// ต้องลองเรียก API แล้วดูผลลัพธ์
    /// </summary>
    public override async Task<ApiPermissionInfo> GetApiPermissionsAsync(CancellationToken cancellationToken = default)
    {
        var permissions = new ApiPermissionInfo();

        try
        {
            if (!HasCredentials())
            {
                permissions.AdditionalInfo = "ไม่ได้ตั้งค่า API Key";
                return permissions;
            }

            // ลองดึง balance - ถ้าได้แสดงว่ามี Read permission
            try
            {
                var balance = await GetBalanceAsync(cancellationToken);
                permissions.CanRead = balance != null;
                _logger.LogInfo(ExchangeName, "API Read permission verified");
            }
            catch
            {
                permissions.CanRead = false;
            }

            // Gate.io API permissions ต้องดูจากการตั้งค่าบนเว็บ
            if (permissions.CanRead)
            {
                // สมมติว่ามี Trade ถ้า Read ได้
                permissions.CanTrade = true;
                permissions.AdditionalInfo = "กรุณาตรวจสอบสิทธิ์ Trade/Withdraw ที่ gate.io";
            }

            _logger.LogInfo(ExchangeName, $"API Permissions - Read: {permissions.CanRead}, Trade: {permissions.CanTrade} (assumed)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ExchangeName, $"Failed to get API permissions: {ex.Message}");
            permissions.AdditionalInfo = $"ไม่สามารถตรวจสอบสิทธิ์: {ex.Message}";
        }

        return permissions;
    }

    #endregion

    public override async Task<List<PriceCandle>> GetKlinesAsync(string symbol, string interval = "1m", int limit = 100, CancellationToken cancellationToken = default)
    {
        var candles = new List<PriceCandle>();
        try
        {
            var normalizedSymbol = NormalizeSymbol(symbol);

            // Gate.io uses: 10s, 1m, 5m, 15m, 30m, 1h, 4h, 8h, 1d, 7d, 30d
            var gateInterval = interval switch
            {
                "1m" => "1m",
                "5m" => "5m",
                "15m" => "15m",
                "30m" => "30m",
                "1h" => "1h",
                "4h" => "4h",
                "1d" => "1d",
                _ => "1m"
            };

            var clampedLimit = Math.Min(limit, 1000); // Gate.io max 1000

            var response = await GetAsync<List<List<string>>>(
                $"/api/v4/spot/candlesticks?currency_pair={normalizedSymbol}&interval={gateInterval}&limit={clampedLimit}",
                cancellationToken);

            if (response == null || response.Count == 0) return candles;

            // Gate.io returns: [timestamp, volume, close, high, low, open, amount]
            foreach (var kline in response)
            {
                if (kline.Count < 6) continue;
                candles.Add(new PriceCandle
                {
                    Time = DateTimeOffset.FromUnixTimeSeconds(long.Parse(kline[0], CultureInfo.InvariantCulture)).UtcDateTime,
                    Volume = decimal.Parse(kline[1], CultureInfo.InvariantCulture),
                    Close = decimal.Parse(kline[2], CultureInfo.InvariantCulture),
                    High = decimal.Parse(kline[3], CultureInfo.InvariantCulture),
                    Low = decimal.Parse(kline[4], CultureInfo.InvariantCulture),
                    Open = decimal.Parse(kline[5], CultureInfo.InvariantCulture)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"GetKlinesAsync error for {symbol}: {ex.Message}");
        }
        return candles;
    }

    #region Account Data (Private APIs)

    public override async Task<AccountBalance> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!HasCredentials())
            {
                throw new InvalidOperationException($"{ExchangeName}: API credentials not configured. Please configure API keys in Settings.");
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var endpoint = "/api/v4/spot/accounts";
            var headers = CreateAuthHeaders("GET", endpoint, "", "", timestamp);

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<List<GateIOAccountBalance>>(_jsonOptions, cancellationToken);

            if (result == null)
            {
                throw new Exception("Failed to get account balance");
            }

            var balance = new AccountBalance
            {
                Exchange = ExchangeName,
                Timestamp = DateTime.UtcNow,
                Assets = new Dictionary<string, AssetBalance>()
            };

            foreach (var asset in result)
            {
                var available = decimal.Parse(asset.Available ?? "0", CultureInfo.InvariantCulture);
                var locked = decimal.Parse(asset.Locked ?? "0", CultureInfo.InvariantCulture);

                if (available > 0 || locked > 0)
                {
                    balance.Assets[asset.Currency] = new AssetBalance
                    {
                        Asset = asset.Currency,
                        Available = available,
                        Total = available + locked
                    };
                }
            }

            return balance;
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"GetBalanceAsync error: {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Order Management (Private APIs)

    public override async Task<Order> PlaceOrderAsync(OrderRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!HasCredentials())
            {
                throw new Exception($"API credentials not configured for {ExchangeName}");
            }

            var normalizedSymbol = NormalizeSymbol(request.Symbol);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var endpoint = "/api/v4/spot/orders";

            var orderData = new Dictionary<string, object>
            {
                ["currency_pair"] = normalizedSymbol,
                ["side"] = request.Side == OrderSide.Buy ? "buy" : "sell",
                ["type"] = request.Type == OrderType.Market ? "market" : "limit",
                ["amount"] = request.Quantity.ToString("F8"),
                ["time_in_force"] = "gtc"
            };

            if (!string.IsNullOrEmpty(request.ClientOrderId))
            {
                orderData["text"] = $"t-{request.ClientOrderId}";
            }

            if (request.Type == OrderType.Limit && request.Price.HasValue)
            {
                orderData["price"] = request.Price.Value.ToString("F8");
            }

            var body = JsonSerializer.Serialize(orderData, _jsonOptions);
            var headers = CreateAuthHeaders("POST", endpoint, "", body, timestamp);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            foreach (var header in headers)
            {
                httpRequest.Headers.Add(header.Key, header.Value);
            }

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Order placement failed: {responseContent}");
            }

            var result = JsonSerializer.Deserialize<GateIOOrderResponse>(responseContent, _jsonOptions);

            if (result == null)
            {
                throw new Exception("Failed to place order");
            }

            return new Order
            {
                OrderId = result.Id ?? "",
                ClientOrderId = result.Text ?? "",
                Exchange = ExchangeName,
                Symbol = request.Symbol,
                Side = request.Side,
                Type = request.Type,
                Status = MapOrderStatus(result.Status),
                RequestedQuantity = request.Quantity,
                FilledQuantity = decimal.Parse(result.FilledTotal ?? "0", CultureInfo.InvariantCulture),
                RequestedPrice = request.Price,
                AverageFilledPrice = !string.IsNullOrEmpty(result.AvgDealPrice) ? decimal.Parse(result.AvgDealPrice, CultureInfo.InvariantCulture) : 0,
                Fee = decimal.Parse(result.Fee ?? "0", CultureInfo.InvariantCulture),
                FeeCurrency = result.FeeCurrency ?? "USDT",
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(result.CreateTime ?? "0", CultureInfo.InvariantCulture)).UtcDateTime,
                UpdatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"PlaceOrderAsync error: {ex.Message}");
            throw;
        }
    }

    public override async Task<Order> CancelOrderAsync(string symbol, string orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!HasCredentials())
            {
                throw new Exception($"API credentials not configured for {ExchangeName}");
            }

            var normalizedSymbol = NormalizeSymbol(symbol);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var endpoint = $"/api/v4/spot/orders/{orderId}?currency_pair={normalizedSymbol}";
            var headers = CreateAuthHeaders("DELETE", $"/api/v4/spot/orders/{orderId}", $"currency_pair={normalizedSymbol}", "", timestamp);

            using var request = new HttpRequestMessage(HttpMethod.Delete, endpoint);
            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            return new Order
            {
                OrderId = orderId,
                Exchange = ExchangeName,
                Symbol = symbol,
                Status = OrderStatus.Cancelled,
                UpdatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"CancelOrderAsync error: {ex.Message}");
            throw;
        }
    }

    public override async Task<Order> GetOrderAsync(string symbol, string orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!HasCredentials())
            {
                throw new Exception($"API credentials not configured for {ExchangeName}");
            }

            var normalizedSymbol = NormalizeSymbol(symbol);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var endpoint = $"/api/v4/spot/orders/{orderId}?currency_pair={normalizedSymbol}";
            var headers = CreateAuthHeaders("GET", $"/api/v4/spot/orders/{orderId}", $"currency_pair={normalizedSymbol}", "", timestamp);

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<GateIOOrderResponse>(_jsonOptions, cancellationToken);

            if (result == null)
            {
                throw new Exception($"Failed to get order {orderId}");
            }

            return new Order
            {
                OrderId = result.Id ?? "",
                ClientOrderId = result.Text ?? "",
                Exchange = ExchangeName,
                Symbol = symbol,
                Side = result.Side == "buy" ? OrderSide.Buy : OrderSide.Sell,
                Type = result.Type == "market" ? OrderType.Market : OrderType.Limit,
                Status = MapOrderStatus(result.Status),
                RequestedQuantity = decimal.Parse(result.Amount ?? "0", CultureInfo.InvariantCulture),
                FilledQuantity = decimal.Parse(result.FilledTotal ?? "0", CultureInfo.InvariantCulture),
                RequestedPrice = !string.IsNullOrEmpty(result.Price) ? decimal.Parse(result.Price, CultureInfo.InvariantCulture) : null,
                AverageFilledPrice = !string.IsNullOrEmpty(result.AvgDealPrice) ? decimal.Parse(result.AvgDealPrice, CultureInfo.InvariantCulture) : 0,
                Fee = decimal.Parse(result.Fee ?? "0", CultureInfo.InvariantCulture),
                FeeCurrency = result.FeeCurrency ?? "USDT",
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(result.CreateTime ?? "0", CultureInfo.InvariantCulture)).UtcDateTime,
                UpdatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"GetOrderAsync error: {ex.Message}");
            throw;
        }
    }

    public override async Task<List<Order>> GetOpenOrdersAsync(string? symbol = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!HasCredentials())
            {
                return new List<Order>();
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var queryString = symbol != null ? $"currency_pair={NormalizeSymbol(symbol)}&status=open" : "status=open";
            var endpoint = $"/api/v4/spot/orders?{queryString}";
            var headers = CreateAuthHeaders("GET", "/api/v4/spot/orders", queryString, "", timestamp);

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<List<GateIOOrderResponse>>(_jsonOptions, cancellationToken);

            if (result == null)
            {
                return new List<Order>();
            }

            return result.Select(data => new Order
            {
                OrderId = data.Id ?? "",
                ClientOrderId = data.Text ?? "",
                Exchange = ExchangeName,
                Symbol = data.CurrencyPair?.Replace("_", "/") ?? "",
                Side = data.Side == "buy" ? OrderSide.Buy : OrderSide.Sell,
                Type = data.Type == "market" ? OrderType.Market : OrderType.Limit,
                Status = MapOrderStatus(data.Status),
                RequestedQuantity = decimal.Parse(data.Amount ?? "0", CultureInfo.InvariantCulture),
                FilledQuantity = decimal.Parse(data.FilledTotal ?? "0", CultureInfo.InvariantCulture),
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(data.CreateTime ?? "0", CultureInfo.InvariantCulture)).UtcDateTime
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"GetOpenOrdersAsync error: {ex.Message}");
            return new List<Order>();
        }
    }

    #endregion

    #region Helper Methods

    private string NormalizeSymbol(string symbol)
    {
        // Convert "BTC/USDT" to "BTC_USDT" (Gate.io format)
        return symbol.Replace("/", "_").ToUpperInvariant();
    }

    private Dictionary<string, string> CreateAuthHeaders(string method, string path, string query, string body, string timestamp)
    {
        var apiKey = GetApiKey();
        var apiSecret = GetApiSecret();

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
        {
            throw new Exception("API credentials not configured");
        }

        // Hash body with SHA512
        var bodyHash = ComputeSHA512Hash(body);

        // Create signature string
        var signatureString = $"{method}\n{path}\n{query}\n{bodyHash}\n{timestamp}";

        // Sign with HMAC SHA512
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(apiSecret));
        var signature = BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureString))).Replace("-", "").ToLowerInvariant();

        return new Dictionary<string, string>
        {
            ["KEY"] = apiKey,
            ["SIGN"] = signature,
            ["Timestamp"] = timestamp,
            ["Content-Type"] = "application/json"
        };
    }

    private string ComputeSHA512Hash(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            // Empty string hash
            using var sha = SHA512.Create();
            var hash = sha.ComputeHash(Array.Empty<byte>());
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        using var sha512 = SHA512.Create();
        var hashBytes = sha512.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }

    private OrderStatus MapOrderStatus(string status)
    {
        return status switch
        {
            "open" => OrderStatus.Pending,
            "closed" => OrderStatus.Filled,
            "cancelled" or "canceled" => OrderStatus.Cancelled,
            _ => OrderStatus.Error
        };
    }

    #endregion
}

#region Gate.io API Response Models

internal class GateIOServerTime
{
    [JsonPropertyName("server_time")]
    public long ServerTime { get; set; }
}

internal class GateIOTickerData
{
    [JsonPropertyName("currency_pair")]
    public string? CurrencyPair { get; set; }

    [JsonPropertyName("last")]
    public string? Last { get; set; }

    [JsonPropertyName("highest_bid")]
    public string? HighestBid { get; set; }

    [JsonPropertyName("lowest_ask")]
    public string? LowestAsk { get; set; }

    [JsonPropertyName("base_volume")]
    public string? BaseVolume { get; set; }

    [JsonPropertyName("quote_volume")]
    public string? QuoteVolume { get; set; }

    [JsonPropertyName("change_percentage")]
    public string? ChangePercentage { get; set; }
}

internal class GateIOOrderBookData
{
    [JsonPropertyName("asks")]
    public string[][] Asks { get; set; } = Array.Empty<string[]>();

    [JsonPropertyName("bids")]
    public string[][] Bids { get; set; } = Array.Empty<string[]>();

    [JsonPropertyName("current")]
    public long Current { get; set; }

    [JsonPropertyName("update")]
    public long Update { get; set; }
}

internal class GateIOAccountBalance
{
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "";

    [JsonPropertyName("available")]
    public string? Available { get; set; }

    [JsonPropertyName("locked")]
    public string? Locked { get; set; }
}

internal class GateIOOrderResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("currency_pair")]
    public string? CurrencyPair { get; set; }

    [JsonPropertyName("side")]
    public string Side { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("amount")]
    public string? Amount { get; set; }

    [JsonPropertyName("price")]
    public string? Price { get; set; }

    [JsonPropertyName("filled_total")]
    public string? FilledTotal { get; set; }

    [JsonPropertyName("avg_deal_price")]
    public string? AvgDealPrice { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("fee")]
    public string? Fee { get; set; }

    [JsonPropertyName("fee_currency")]
    public string? FeeCurrency { get; set; }

    [JsonPropertyName("create_time")]
    public string? CreateTime { get; set; }
}

#endregion
