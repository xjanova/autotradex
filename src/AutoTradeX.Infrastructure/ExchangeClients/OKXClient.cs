/*
 * ============================================================================
 * AutoTrade-X - OKX Exchange Client
 * ============================================================================
 * Real implementation for OKX Spot API
 * Documentation: https://www.okx.com/docs-v5/en/
 * ============================================================================
 */

using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AutoTradeX.Infrastructure.ExchangeClients;

public class OKXClient : BaseExchangeClient
{
    public override string ExchangeName => "OKX";
    private string? _passphrase;

    public OKXClient(ExchangeConfig config, ILoggingService logger)
        : base(config, logger)
    {
        // OKX requires additional passphrase — try config-based env var first, then fallback
        _passphrase = Environment.GetEnvironmentVariable("AUTOTRADEX_OKX_PASSPHRASE");
        if (string.IsNullOrEmpty(_passphrase))
        {
            _passphrase = Environment.GetEnvironmentVariable("AUTOTRADEX_OKX_API_KEY_PASSPHRASE");
        }
    }

    #region Market Data (Public APIs)

    public override async Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedSymbol = NormalizeSymbol(symbol);
            var response = await GetAsync<OKXResponse<List<OKXTickerData>>>(
                $"/api/v5/market/ticker?instId={normalizedSymbol}",
                cancellationToken);

            if (response?.Data == null || response.Data.Count == 0)
            {
                throw new Exception($"Failed to get ticker for {symbol}");
            }

            var data = response.Data[0];

            return new Ticker
            {
                Symbol = symbol,
                Exchange = ExchangeName,
                BidPrice = decimal.Parse(data.BidPx ?? "0", CultureInfo.InvariantCulture),
                AskPrice = decimal.Parse(data.AskPx ?? "0", CultureInfo.InvariantCulture),
                BidQuantity = decimal.Parse(data.BidSz ?? "0", CultureInfo.InvariantCulture),
                AskQuantity = decimal.Parse(data.AskSz ?? "0", CultureInfo.InvariantCulture),
                LastPrice = decimal.Parse(data.Last ?? "0", CultureInfo.InvariantCulture),
                Volume24h = decimal.Parse(data.Vol24h ?? "0", CultureInfo.InvariantCulture),
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
            var response = await GetAsync<OKXResponse<List<OKXOrderBookData>>>(
                $"/api/v5/market/books?instId={normalizedSymbol}&sz={Math.Min(depth, 400)}",
                cancellationToken);

            if (response?.Data == null || response.Data.Count == 0)
            {
                throw new Exception($"Failed to get order book for {symbol}");
            }

            var data = response.Data[0];
            var orderBook = new OrderBook
            {
                Symbol = symbol,
                Exchange = ExchangeName,
                Timestamp = DateTime.UtcNow
            };

            foreach (var bid in data.Bids ?? Array.Empty<string[]>())
            {
                if (bid.Length >= 2)
                {
                    orderBook.Bids.Add(new OrderBookEntry(
                        decimal.Parse(bid[0], CultureInfo.InvariantCulture),
                        decimal.Parse(bid[1], CultureInfo.InvariantCulture)
                    ));
                }
            }

            foreach (var ask in data.Asks ?? Array.Empty<string[]>())
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
    /// Get ALL tickers from OKX in one API call
    /// Endpoint: GET /api/v5/market/tickers?instType=SPOT
    /// </summary>
    public override async Task<Dictionary<string, Ticker>> GetAllTickersAsync(
        string? quoteAsset = null,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, Ticker>();

        try
        {
            _logger.LogInfo(ExchangeName, "GetAllTickersAsync: Fetching all tickers...");

            var response = await GetAsync<OKXResponse<List<OKXTickerData>>>(
                "/api/v5/market/tickers?instType=SPOT",
                cancellationToken);

            if (response?.Data == null || response.Data.Count == 0)
            {
                _logger.LogWarning(ExchangeName, "GetAllTickersAsync: No data returned from API");
                return result;
            }

            _logger.LogInfo(ExchangeName, $"GetAllTickersAsync: Got {response.Data.Count} tickers from API");

            foreach (var data in response.Data)
            {
                var symbol = data.InstId ?? "";

                // Filter by quote asset if specified (e.g., "USDT")
                // OKX format: BASE-QUOTE (e.g., BTC-USDT)
                if (!string.IsNullOrEmpty(quoteAsset))
                {
                    if (!symbol.EndsWith($"-{quoteAsset}", StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                // Skip pairs with zero volume (inactive)
                var volume = decimal.TryParse(data.Vol24h, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
                var lastPrice = decimal.TryParse(data.Last, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0;

                if (volume <= 0 && lastPrice <= 0)
                    continue;

                result[symbol] = new Ticker
                {
                    Symbol = symbol,
                    Exchange = ExchangeName,
                    BidPrice = decimal.TryParse(data.BidPx, NumberStyles.Any, CultureInfo.InvariantCulture, out var bid) ? bid : 0,
                    AskPrice = decimal.TryParse(data.AskPx, NumberStyles.Any, CultureInfo.InvariantCulture, out var ask) ? ask : 0,
                    BidQuantity = decimal.TryParse(data.BidSz, NumberStyles.Any, CultureInfo.InvariantCulture, out var bidSz) ? bidSz : 0,
                    AskQuantity = decimal.TryParse(data.AskSz, NumberStyles.Any, CultureInfo.InvariantCulture, out var askSz) ? askSz : 0,
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
    /// Test connection to OKX API with proper validation
    /// </summary>
    public override async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInfo(ExchangeName, "Testing OKX API connection...");

            // Step 1: Test public API - get server time
            var serverTimeResponse = await GetAsync<OKXResponse<List<OKXServerTime>>>(
                "/api/v5/public/time",
                cancellationToken);

            if (serverTimeResponse?.Data == null || serverTimeResponse.Data.Count == 0)
            {
                _logger.LogWarning(ExchangeName, "Failed to get OKX server time");
                return false;
            }

            _logger.LogInfo(ExchangeName, "OKX public API reachable");

            // Step 2: Test ticker API with BTC-USDT
            var ticker = await GetTickerAsync("BTC-USDT", cancellationToken);
            if (ticker == null)
            {
                _logger.LogWarning(ExchangeName, "Failed to get BTC-USDT ticker");
                return false;
            }

            _logger.LogInfo(ExchangeName, $"BTC-USDT: ${ticker.LastPrice:N2}");

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
            _logger.LogInfo(ExchangeName, "OKX connection test successful");
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
    /// Get API key permissions from OKX
    /// OKX /api/v5/account/config returns permission info
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

            // OKX API config endpoint returns permissions
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var endpoint = "/api/v5/account/config";
            var headers = CreateAuthHeaders("GET", endpoint, "", timestamp);

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OKXResponse<List<OKXConfigData>>>(_jsonOptions, cancellationToken);
                if (result?.Data != null && result.Data.Count > 0)
                {
                    var config = result.Data[0];
                    permissions.CanRead = true;
                    // OKX permission values: read_only, trade, withdraw
                    permissions.CanTrade = config.Perm?.Contains("trade") ?? false;
                    permissions.CanWithdraw = config.Perm?.Contains("withdraw") ?? false;
                    permissions.IpRestriction = config.Ip;
                }
            }
            else
            {
                // ลองดึง balance - ถ้าได้แสดงว่ามี Read permission
                try
                {
                    var balance = await GetBalanceAsync(cancellationToken);
                    permissions.CanRead = balance != null;
                    permissions.CanTrade = true; // assume if can read
                }
                catch
                {
                    permissions.CanRead = false;
                }
                permissions.AdditionalInfo = "กรุณาตรวจสอบสิทธิ์ที่ okx.com";
            }

            _logger.LogInfo(ExchangeName, $"API Permissions - Read: {permissions.CanRead}, Trade: {permissions.CanTrade}, Withdraw: {permissions.CanWithdraw}");
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

            // OKX uses: 1m, 5m, 15m, 30m, 1H, 4H, 1D
            var okxInterval = interval switch
            {
                "1m" => "1m",
                "5m" => "5m",
                "15m" => "15m",
                "30m" => "30m",
                "1h" => "1H",
                "4h" => "4H",
                "1d" => "1D",
                _ => "1m"
            };

            var clampedLimit = Math.Min(limit, 300); // OKX max 300

            var response = await GetAsync<OKXResponse<List<List<string>>>>(
                $"/api/v5/market/candles?instId={normalizedSymbol}&bar={okxInterval}&limit={clampedLimit}",
                cancellationToken);

            if (response?.Data == null || response.Data.Count == 0) return candles;

            foreach (var kline in response.Data)
            {
                if (kline.Count < 6) continue;
                candles.Add(new PriceCandle
                {
                    Time = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(kline[0], CultureInfo.InvariantCulture)).UtcDateTime,
                    Open = decimal.Parse(kline[1], CultureInfo.InvariantCulture),
                    High = decimal.Parse(kline[2], CultureInfo.InvariantCulture),
                    Low = decimal.Parse(kline[3], CultureInfo.InvariantCulture),
                    Close = decimal.Parse(kline[4], CultureInfo.InvariantCulture),
                    Volume = decimal.Parse(kline[5], CultureInfo.InvariantCulture)
                });
            }

            // OKX returns newest first, reverse to chronological order
            candles.Reverse();
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

            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var endpoint = "/api/v5/account/balance";
            var headers = CreateAuthHeaders("GET", endpoint, "", timestamp);

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OKXResponse<List<OKXAccountBalance>>>(_jsonOptions, cancellationToken);

            if (result?.Data == null || result.Data.Count == 0)
            {
                throw new Exception("Failed to get account balance");
            }

            var balance = new AccountBalance
            {
                Exchange = ExchangeName,
                Timestamp = DateTime.UtcNow,
                Assets = new Dictionary<string, AssetBalance>()
            };

            foreach (var detail in result.Data[0].Details ?? new List<OKXBalanceDetail>())
            {
                var available = decimal.Parse(detail.AvailBal ?? "0", CultureInfo.InvariantCulture);
                var frozen = decimal.Parse(detail.FrozenBal ?? "0", CultureInfo.InvariantCulture);

                if (available > 0 || frozen > 0)
                {
                    balance.Assets[detail.Ccy] = new AssetBalance
                    {
                        Asset = detail.Ccy,
                        Available = available,
                        Total = available + frozen
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
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var endpoint = "/api/v5/trade/order";

            var orderData = new Dictionary<string, object>
            {
                ["instId"] = normalizedSymbol,
                ["tdMode"] = "cash", // Spot trading
                ["side"] = request.Side == OrderSide.Buy ? "buy" : "sell",
                ["ordType"] = request.Type == OrderType.Market ? "market" : "limit",
                ["sz"] = request.Quantity.ToString("F8")
            };

            if (!string.IsNullOrEmpty(request.ClientOrderId))
            {
                orderData["clOrdId"] = request.ClientOrderId;
            }

            if (request.Type == OrderType.Limit && request.Price.HasValue)
            {
                orderData["px"] = request.Price.Value.ToString("F8");
            }

            var body = JsonSerializer.Serialize(orderData, _jsonOptions);
            var headers = CreateAuthHeaders("POST", endpoint, body, timestamp);

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

            var result = JsonSerializer.Deserialize<OKXResponse<List<OKXOrderResponse>>>(responseContent, _jsonOptions);

            if (result?.Data == null || result.Data.Count == 0)
            {
                throw new Exception($"Failed to place order: {result?.Msg}");
            }

            var orderResult = result.Data[0];
            if (orderResult.SCode != "0")
            {
                throw new Exception($"Order failed: {orderResult.SMsg}");
            }

            return new Order
            {
                OrderId = orderResult.OrdId ?? "",
                ClientOrderId = orderResult.ClOrdId ?? "",
                Exchange = ExchangeName,
                Symbol = request.Symbol,
                Side = request.Side,
                Type = request.Type,
                Status = OrderStatus.Pending,
                RequestedQuantity = request.Quantity,
                FilledQuantity = 0,
                RequestedPrice = request.Price,
                AverageFilledPrice = request.Price ?? 0,
                CreatedAt = DateTime.UtcNow,
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
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var endpoint = "/api/v5/trade/cancel-order";

            var cancelData = new Dictionary<string, object>
            {
                ["instId"] = normalizedSymbol,
                ["ordId"] = orderId
            };

            var body = JsonSerializer.Serialize(cancelData, _jsonOptions);
            var headers = CreateAuthHeaders("POST", endpoint, body, timestamp);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            foreach (var header in headers)
            {
                httpRequest.Headers.Add(header.Key, header.Value);
            }

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
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
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var endpoint = $"/api/v5/trade/order?instId={normalizedSymbol}&ordId={orderId}";
            var headers = CreateAuthHeaders("GET", endpoint, "", timestamp);

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OKXResponse<List<OKXOrderDetail>>>(_jsonOptions, cancellationToken);

            if (result?.Data == null || result.Data.Count == 0)
            {
                throw new Exception($"Failed to get order {orderId}");
            }

            var data = result.Data[0];

            return new Order
            {
                OrderId = data.OrdId ?? "",
                ClientOrderId = data.ClOrdId ?? "",
                Exchange = ExchangeName,
                Symbol = symbol,
                Side = data.Side == "buy" ? OrderSide.Buy : OrderSide.Sell,
                Type = data.OrdType == "market" ? OrderType.Market : OrderType.Limit,
                Status = MapOrderStatus(data.State),
                RequestedQuantity = decimal.Parse(data.Sz ?? "0", CultureInfo.InvariantCulture),
                FilledQuantity = decimal.Parse(data.AccFillSz ?? "0", CultureInfo.InvariantCulture),
                RequestedPrice = !string.IsNullOrEmpty(data.Px) ? decimal.Parse(data.Px, CultureInfo.InvariantCulture) : null,
                AverageFilledPrice = !string.IsNullOrEmpty(data.AvgPx) ? decimal.Parse(data.AvgPx, CultureInfo.InvariantCulture) : 0,
                Fee = decimal.Parse(data.Fee ?? "0", CultureInfo.InvariantCulture),
                FeeCurrency = data.FeeCcy ?? "USDT",
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(data.CTime ?? "0", CultureInfo.InvariantCulture)).UtcDateTime,
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

            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var endpoint = symbol != null
                ? $"/api/v5/trade/orders-pending?instId={NormalizeSymbol(symbol)}&instType=SPOT"
                : "/api/v5/trade/orders-pending?instType=SPOT";

            var headers = CreateAuthHeaders("GET", endpoint, "", timestamp);

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OKXResponse<List<OKXOrderDetail>>>(_jsonOptions, cancellationToken);

            if (result?.Data == null)
            {
                return new List<Order>();
            }

            return result.Data.Select(data => new Order
            {
                OrderId = data.OrdId ?? "",
                ClientOrderId = data.ClOrdId ?? "",
                Exchange = ExchangeName,
                Symbol = data.InstId ?? "",
                Side = data.Side == "buy" ? OrderSide.Buy : OrderSide.Sell,
                Type = data.OrdType == "market" ? OrderType.Market : OrderType.Limit,
                Status = MapOrderStatus(data.State),
                RequestedQuantity = decimal.Parse(data.Sz ?? "0", CultureInfo.InvariantCulture),
                FilledQuantity = decimal.Parse(data.AccFillSz ?? "0", CultureInfo.InvariantCulture),
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(data.CTime ?? "0", CultureInfo.InvariantCulture)).UtcDateTime
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
        // Convert "BTC/USDT" to "BTC-USDT" (OKX format)
        return symbol.Replace("/", "-").ToUpperInvariant();
    }

    private Dictionary<string, string> CreateAuthHeaders(string method, string endpoint, string body, string timestamp)
    {
        var apiKey = GetApiKey();
        var apiSecret = GetApiSecret();

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
        {
            throw new Exception("API credentials not configured");
        }

        // Create signature string: timestamp + method + requestPath + body
        var signatureString = timestamp + method + endpoint + body;

        // Sign with HMAC SHA256 and Base64 encode
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureString)));

        return new Dictionary<string, string>
        {
            ["OK-ACCESS-KEY"] = apiKey,
            ["OK-ACCESS-SIGN"] = signature,
            ["OK-ACCESS-TIMESTAMP"] = timestamp,
            ["OK-ACCESS-PASSPHRASE"] = _passphrase ?? "",
            ["Content-Type"] = "application/json"
        };
    }

    private OrderStatus MapOrderStatus(string state)
    {
        return state switch
        {
            "live" => OrderStatus.Pending,
            "partially_filled" => OrderStatus.PartiallyFilled,
            "filled" => OrderStatus.Filled,
            "canceled" or "cancelled" => OrderStatus.Cancelled,
            _ => OrderStatus.Error
        };
    }

    #endregion
}

#region OKX API Response Models

internal class OKXResponse<T>
{
    public string Code { get; set; } = "";
    public string? Msg { get; set; }
    public T? Data { get; set; }
}

internal class OKXServerTime
{
    public string? Ts { get; set; }
}

internal class OKXTickerData
{
    public string? InstId { get; set; }
    public string? Last { get; set; }
    public string? BidPx { get; set; }
    public string? AskPx { get; set; }
    public string? BidSz { get; set; }
    public string? AskSz { get; set; }
    public string? Vol24h { get; set; }
    public string? VolCcy24h { get; set; }
    public string? Ts { get; set; }
}

internal class OKXOrderBookData
{
    public string[][] Bids { get; set; } = Array.Empty<string[]>();
    public string[][] Asks { get; set; } = Array.Empty<string[]>();
    public string? Ts { get; set; }
}

internal class OKXAccountBalance
{
    public string? TotalEq { get; set; }
    public List<OKXBalanceDetail>? Details { get; set; }
}

internal class OKXBalanceDetail
{
    public string Ccy { get; set; } = "";
    public string? AvailBal { get; set; }
    public string? FrozenBal { get; set; }
    public string? Eq { get; set; }
}

internal class OKXOrderResponse
{
    public string OrdId { get; set; } = "";
    public string? ClOrdId { get; set; }
    public string SCode { get; set; } = "";
    public string? SMsg { get; set; }
}

internal class OKXOrderDetail
{
    public string OrdId { get; set; } = "";
    public string? ClOrdId { get; set; }
    public string InstId { get; set; } = "";
    public string Side { get; set; } = "";
    public string OrdType { get; set; } = "";
    public string? Px { get; set; }
    public string? Sz { get; set; }
    public string? AccFillSz { get; set; }
    public string? AvgPx { get; set; }
    public string State { get; set; } = "";
    public string? Fee { get; set; }
    public string? FeeCcy { get; set; }
    public string? CTime { get; set; }
}

internal class OKXConfigData
{
    public string? Uid { get; set; }
    public string? AcctLv { get; set; }
    public string? PosMode { get; set; }
    public string? Perm { get; set; }  // read_only, trade, withdraw
    public string? Ip { get; set; }    // IP whitelist
}

#endregion
