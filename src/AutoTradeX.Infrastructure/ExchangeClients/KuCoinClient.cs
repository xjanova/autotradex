/*
 * ============================================================================
 * AutoTrade-X - KuCoin Exchange Client
 * ============================================================================
 * Real implementation for KuCoin Spot API
 * Documentation: https://docs.kucoin.com/
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

public class KuCoinClient : BaseExchangeClient
{
    public override string ExchangeName => "KuCoin";
    private string? _passphrase;

    public KuCoinClient(ExchangeConfig config, ILoggingService logger)
        : base(config, logger)
    {
        // KuCoin requires additional passphrase
        _passphrase = Environment.GetEnvironmentVariable($"{_config.ApiKeyEnvVar}_PASSPHRASE");
    }

    #region Market Data (Public APIs)

    public override async Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedSymbol = NormalizeSymbol(symbol);
            var response = await GetAsync<KuCoinResponse<KuCoinTickerData>>(
                $"/api/v1/market/orderbook/level1?symbol={normalizedSymbol}",
                cancellationToken);

            if (response?.Data == null)
            {
                throw new Exception($"Failed to get ticker for {symbol}");
            }

            var data = response.Data;

            // Get 24h stats
            var stats = await GetAsync<KuCoinResponse<KuCoin24hStats>>(
                $"/api/v1/market/stats?symbol={normalizedSymbol}",
                cancellationToken);

            return new Ticker
            {
                Symbol = symbol,
                Exchange = ExchangeName,
                BidPrice = decimal.Parse(data.BestBid ?? "0", CultureInfo.InvariantCulture),
                AskPrice = decimal.Parse(data.BestAsk ?? "0", CultureInfo.InvariantCulture),
                BidQuantity = decimal.Parse(data.BestBidSize ?? "0", CultureInfo.InvariantCulture),
                AskQuantity = decimal.Parse(data.BestAskSize ?? "0", CultureInfo.InvariantCulture),
                LastPrice = decimal.Parse(data.Price ?? "0", CultureInfo.InvariantCulture),
                Volume24h = stats?.Data != null ? decimal.Parse(stats.Data.Vol ?? "0", CultureInfo.InvariantCulture) : 0,
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
            
            // KuCoin has depth options: 20 or 100
            var depthParam = depth <= 20 ? "20" : "100";
            var response = await GetAsync<KuCoinResponse<KuCoinOrderBookData>>(
                $"/api/v1/market/orderbook/level2_{depthParam}?symbol={normalizedSymbol}",
                cancellationToken);

            if (response?.Data == null)
            {
                throw new Exception($"Failed to get order book for {symbol}");
            }

            var orderBook = new OrderBook
            {
                Symbol = symbol,
                Exchange = ExchangeName,
                Timestamp = DateTime.UtcNow
            };

            foreach (var bid in response.Data.Bids ?? Array.Empty<string[]>())
            {
                if (bid.Length >= 2)
                {
                    orderBook.Bids.Add(new OrderBookEntry(
                        decimal.Parse(bid[0], CultureInfo.InvariantCulture),
                        decimal.Parse(bid[1], CultureInfo.InvariantCulture)
                    ));
                }
            }

            foreach (var ask in response.Data.Asks ?? Array.Empty<string[]>())
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
    /// Get ALL tickers from KuCoin in one API call
    /// Endpoint: GET /api/v1/market/allTickers
    /// </summary>
    public override async Task<Dictionary<string, Ticker>> GetAllTickersAsync(
        string? quoteAsset = null,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, Ticker>();

        try
        {
            _logger.LogInfo(ExchangeName, "GetAllTickersAsync: Fetching all tickers...");

            var response = await GetAsync<KuCoinResponse<KuCoinAllTickersResponse>>(
                "/api/v1/market/allTickers",
                cancellationToken);

            if (response?.Data?.Ticker == null || response.Data.Ticker.Count == 0)
            {
                _logger.LogWarning(ExchangeName, "GetAllTickersAsync: No data returned from API");
                return result;
            }

            _logger.LogInfo(ExchangeName, $"GetAllTickersAsync: Got {response.Data.Ticker.Count} tickers from API");

            foreach (var data in response.Data.Ticker)
            {
                var symbol = data.Symbol ?? "";

                // Filter by quote asset if specified (e.g., "USDT")
                // KuCoin format: BASE-QUOTE (e.g., BTC-USDT)
                if (!string.IsNullOrEmpty(quoteAsset))
                {
                    if (!symbol.EndsWith($"-{quoteAsset}", StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                // Skip pairs with zero volume (inactive)
                var volume = decimal.TryParse(data.Vol, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
                var lastPrice = decimal.TryParse(data.Last, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0;

                if (volume <= 0 && lastPrice <= 0)
                    continue;

                result[symbol] = new Ticker
                {
                    Symbol = symbol,
                    Exchange = ExchangeName,
                    BidPrice = decimal.TryParse(data.Buy, NumberStyles.Any, CultureInfo.InvariantCulture, out var bid) ? bid : 0,
                    AskPrice = decimal.TryParse(data.Sell, NumberStyles.Any, CultureInfo.InvariantCulture, out var ask) ? ask : 0,
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
    /// Test connection to KuCoin API with proper validation
    /// </summary>
    public override async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInfo(ExchangeName, "Testing KuCoin API connection...");

            // Step 1: Test public API - get server time
            var serverTimeResponse = await GetAsync<KuCoinResponse<long>>(
                "/api/v1/timestamp",
                cancellationToken);

            if (serverTimeResponse?.Data == null)
            {
                _logger.LogWarning(ExchangeName, "Failed to get KuCoin server time");
                return false;
            }

            _logger.LogInfo(ExchangeName, "KuCoin public API reachable");

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
            _logger.LogInfo(ExchangeName, "KuCoin connection test successful");
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
    /// Get API key permissions from KuCoin
    /// KuCoin ต้องใช้ API เพื่อตรวจสอบ sub-account info หรือลองเรียก API ดู
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

            // ถ้า Read ได้ ลอง check trade permission โดยดู API key info
            if (permissions.CanRead)
            {
                try
                {
                    // KuCoin /api/v1/user/api-key endpoint ดึงข้อมูล API key
                    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                    var endpoint = "/api/v1/user/api-key";
                    var headers = CreateAuthHeaders("GET", endpoint, "", timestamp);

                    using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                    foreach (var header in headers)
                    {
                        request.Headers.Add(header.Key, header.Value);
                    }

                    var response = await _httpClient.SendAsync(request, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        var result = await response.Content.ReadFromJsonAsync<KuCoinResponse<KuCoinApiKeyInfo>>(_jsonOptions, cancellationToken);
                        if (result?.Data != null)
                        {
                            permissions.CanTrade = result.Data.Permission?.Contains("Trade") ?? false;
                            permissions.CanWithdraw = result.Data.Permission?.Contains("Withdraw") ?? false;
                            permissions.IpRestriction = result.Data.IpWhitelist;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ExchangeName, $"Failed to get API key info: {ex.Message}");
                    // สมมติว่ามี Trade ถ้า Read ได้
                    permissions.CanTrade = true;
                    permissions.AdditionalInfo = "กรุณาตรวจสอบสิทธิ์ที่ kucoin.com";
                }
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

            // KuCoin uses different interval format: 1min, 5min, 15min, 1hour, 4hour, 1day
            var kucoinInterval = interval switch
            {
                "1m" => "1min",
                "5m" => "5min",
                "15m" => "15min",
                "30m" => "30min",
                "1h" => "1hour",
                "4h" => "4hour",
                "1d" => "1day",
                _ => "1min"
            };

            var clampedLimit = Math.Min(limit, 1500);

            // KuCoin requires startAt and endAt as Unix timestamps in seconds
            var endAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var intervalSeconds = interval switch
            {
                "1m" => 60L,
                "5m" => 300L,
                "15m" => 900L,
                "30m" => 1800L,
                "1h" => 3600L,
                "4h" => 14400L,
                "1d" => 86400L,
                _ => 60L
            };
            var startAt = endAt - (clampedLimit * intervalSeconds);

            var response = await GetAsync<KuCoinResponse<List<List<string>>>>(
                $"/api/v1/market/candles?type={kucoinInterval}&symbol={normalizedSymbol}&startAt={startAt}&endAt={endAt}",
                cancellationToken);

            if (response?.Data == null || response.Data.Count == 0) return candles;

            foreach (var kline in response.Data)
            {
                if (kline.Count < 7) continue;
                candles.Add(new PriceCandle
                {
                    Time = DateTimeOffset.FromUnixTimeSeconds(long.Parse(kline[0], CultureInfo.InvariantCulture)).UtcDateTime,
                    Open = decimal.Parse(kline[1], CultureInfo.InvariantCulture),
                    Close = decimal.Parse(kline[2], CultureInfo.InvariantCulture),
                    High = decimal.Parse(kline[3], CultureInfo.InvariantCulture),
                    Low = decimal.Parse(kline[4], CultureInfo.InvariantCulture),
                    Volume = decimal.Parse(kline[6], CultureInfo.InvariantCulture)
                });
            }

            // KuCoin returns newest first, reverse to chronological order
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

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var endpoint = "/api/v1/accounts";
            var headers = CreateAuthHeaders("GET", endpoint, "", timestamp);

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<KuCoinResponse<List<KuCoinAccountBalance>>>(_jsonOptions, cancellationToken);

            if (result?.Data == null)
            {
                throw new Exception("Failed to get account balance");
            }

            var balance = new AccountBalance
            {
                Exchange = ExchangeName,
                Timestamp = DateTime.UtcNow,
                Assets = new Dictionary<string, AssetBalance>()
            };

            // Group by currency and sum trade accounts
            var grouped = result.Data
                .Where(a => a.Type == "trade") // Only trading accounts
                .GroupBy(a => a.Currency);

            foreach (var group in grouped)
            {
                var available = group.Sum(a => decimal.Parse(a.Available ?? "0", CultureInfo.InvariantCulture));
                var holds = group.Sum(a => decimal.Parse(a.Holds ?? "0", CultureInfo.InvariantCulture));

                if (available > 0 || holds > 0)
                {
                    balance.Assets[group.Key] = new AssetBalance
                    {
                        Asset = group.Key,
                        Available = available,
                        Total = available + holds
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
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var endpoint = "/api/v1/orders";

            var orderData = new Dictionary<string, object>
            {
                ["clientOid"] = request.ClientOrderId ?? Guid.NewGuid().ToString("N"),
                ["side"] = request.Side == OrderSide.Buy ? "buy" : "sell",
                ["symbol"] = normalizedSymbol,
                ["type"] = request.Type == OrderType.Market ? "market" : "limit"
            };

            if (request.Type == OrderType.Market)
            {
                if (request.Side == OrderSide.Buy)
                {
                    // For market buy, use funds (quote currency amount)
                    orderData["funds"] = request.Quantity.ToString("F8");
                }
                else
                {
                    orderData["size"] = request.Quantity.ToString("F8");
                }
            }
            else
            {
                orderData["size"] = request.Quantity.ToString("F8");
                if (request.Price.HasValue)
                {
                    orderData["price"] = request.Price.Value.ToString("F8");
                }
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

            var result = JsonSerializer.Deserialize<KuCoinResponse<KuCoinOrderResponse>>(responseContent, _jsonOptions);

            if (result?.Code != "200000")
            {
                throw new Exception($"KuCoin order failed (code {result?.Code}): {result?.Msg}. Response: {responseContent}");
            }

            if (result?.Data == null)
            {
                throw new Exception("Failed to place order");
            }

            return new Order
            {
                OrderId = result.Data.OrderId,
                ClientOrderId = orderData["clientOid"]?.ToString() ?? "",
                Exchange = ExchangeName,
                Symbol = request.Symbol,
                Side = request.Side,
                Type = request.Type,
                Status = OrderStatus.Open, // KuCoin returns only order ID; Open is more accurate than Pending
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

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var endpoint = $"/api/v1/orders/{orderId}";
            var headers = CreateAuthHeaders("DELETE", endpoint, "", timestamp);

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

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var endpoint = $"/api/v1/orders/{orderId}";
            var headers = CreateAuthHeaders("GET", endpoint, "", timestamp);

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<KuCoinResponse<KuCoinOrderDetail>>(_jsonOptions, cancellationToken);

            if (result?.Data == null)
            {
                throw new Exception($"Failed to get order {orderId}");
            }

            var data = result.Data;

            return new Order
            {
                OrderId = data.Id ?? "",
                ClientOrderId = data.ClientOid ?? "",
                Exchange = ExchangeName,
                Symbol = symbol,
                Side = data.Side == "buy" ? OrderSide.Buy : OrderSide.Sell,
                Type = data.Type == "market" ? OrderType.Market : OrderType.Limit,
                Status = MapOrderStatus(data),
                RequestedQuantity = decimal.Parse(data.Size ?? "0", CultureInfo.InvariantCulture),
                FilledQuantity = decimal.Parse(data.DealSize ?? "0", CultureInfo.InvariantCulture),
                RequestedPrice = !string.IsNullOrEmpty(data.Price) ? decimal.Parse(data.Price, CultureInfo.InvariantCulture) : null,
                AverageFilledPrice = !string.IsNullOrEmpty(data.DealFunds) && decimal.Parse(data.DealSize ?? "0", CultureInfo.InvariantCulture) > 0
                    ? decimal.Parse(data.DealFunds, CultureInfo.InvariantCulture) / decimal.Parse(data.DealSize ?? "1", CultureInfo.InvariantCulture)
                    : 0,
                Fee = decimal.Parse(data.Fee ?? "0", CultureInfo.InvariantCulture),
                FeeCurrency = data.FeeCurrency ?? "USDT",
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(data.CreatedAt).UtcDateTime,
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

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var endpoint = symbol != null
                ? $"/api/v1/orders?status=active&symbol={NormalizeSymbol(symbol)}"
                : "/api/v1/orders?status=active";

            var headers = CreateAuthHeaders("GET", endpoint, "", timestamp);

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<KuCoinResponse<KuCoinOrderListResponse>>(_jsonOptions, cancellationToken);

            if (result?.Data?.Items == null)
            {
                return new List<Order>();
            }

            return result.Data.Items.Select(data => new Order
            {
                OrderId = data.Id ?? "",
                ClientOrderId = data.ClientOid ?? "",
                Exchange = ExchangeName,
                Symbol = data.Symbol ?? "",
                Side = data.Side == "buy" ? OrderSide.Buy : OrderSide.Sell,
                Type = data.Type == "market" ? OrderType.Market : OrderType.Limit,
                Status = MapOrderStatus(data),
                RequestedQuantity = decimal.Parse(data.Size ?? "0", CultureInfo.InvariantCulture),
                FilledQuantity = decimal.Parse(data.DealSize ?? "0", CultureInfo.InvariantCulture),
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(data.CreatedAt).UtcDateTime
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
        // Convert "BTC/USDT" to "BTC-USDT" (KuCoin format)
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

        // Create signature string: timestamp + method + endpoint + body
        var signatureString = timestamp + method + endpoint + body;

        // Sign with HMAC SHA256
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureString)));

        // Sign passphrase
        var passphraseSignature = "";
        if (!string.IsNullOrEmpty(_passphrase))
        {
            using var passphraseHmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret));
            passphraseSignature = Convert.ToBase64String(passphraseHmac.ComputeHash(Encoding.UTF8.GetBytes(_passphrase)));
        }

        return new Dictionary<string, string>
        {
            ["KC-API-KEY"] = apiKey,
            ["KC-API-SIGN"] = signature,
            ["KC-API-TIMESTAMP"] = timestamp,
            ["KC-API-PASSPHRASE"] = passphraseSignature,
            ["KC-API-KEY-VERSION"] = "2" // Version 2 uses signed passphrase
        };
    }

    private OrderStatus MapOrderStatus(KuCoinOrderDetail order)
    {
        if (order.IsActive)
        {
            return decimal.Parse(order.DealSize ?? "0", CultureInfo.InvariantCulture) > 0
                ? OrderStatus.PartiallyFilled
                : OrderStatus.Pending;
        }

        if (order.CancelExist)
        {
            return OrderStatus.Cancelled;
        }

        return decimal.Parse(order.DealSize ?? "0", CultureInfo.InvariantCulture) >= decimal.Parse(order.Size ?? "0", CultureInfo.InvariantCulture)
            ? OrderStatus.Filled
            : OrderStatus.Error;
    }

    #endregion
}

#region KuCoin API Response Models

internal class KuCoinResponse<T>
{
    public string Code { get; set; } = "";
    public T? Data { get; set; }
    public string? Msg { get; set; }
}

internal class KuCoinTickerData
{
    public string? Sequence { get; set; }
    public string? BestBid { get; set; }
    public string? BestBidSize { get; set; }
    public string? BestAsk { get; set; }
    public string? BestAskSize { get; set; }
    public string? Price { get; set; }
    public string? Size { get; set; }
    public long Time { get; set; }
}

internal class KuCoin24hStats
{
    public string? Symbol { get; set; }
    public string? Buy { get; set; }
    public string? Sell { get; set; }
    public string? ChangeRate { get; set; }
    public string? ChangePrice { get; set; }
    public string? High { get; set; }
    public string? Low { get; set; }
    public string? Vol { get; set; }
    public string? VolValue { get; set; }
    public string? Last { get; set; }
}

internal class KuCoinAllTickersResponse
{
    public long Time { get; set; }
    public List<KuCoinAllTickerItem> Ticker { get; set; } = new();
}

internal class KuCoinAllTickerItem
{
    public string? Symbol { get; set; }
    public string? SymbolName { get; set; }
    public string? Buy { get; set; }
    public string? Sell { get; set; }
    public string? ChangeRate { get; set; }
    public string? ChangePrice { get; set; }
    public string? High { get; set; }
    public string? Low { get; set; }
    public string? Vol { get; set; }
    public string? VolValue { get; set; }
    public string? Last { get; set; }
    public string? AveragePrice { get; set; }
    public string? TakerFeeRate { get; set; }
    public string? MakerFeeRate { get; set; }
    public string? TakerCoefficient { get; set; }
    public string? MakerCoefficient { get; set; }
}

internal class KuCoinOrderBookData
{
    public long Sequence { get; set; }
    public string[][] Bids { get; set; } = Array.Empty<string[]>();
    public string[][] Asks { get; set; } = Array.Empty<string[]>();
}

internal class KuCoinAccountBalance
{
    public string Id { get; set; } = "";
    public string Currency { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Balance { get; set; }
    public string? Available { get; set; }
    public string? Holds { get; set; }
}

internal class KuCoinOrderResponse
{
    public string OrderId { get; set; } = "";
}

internal class KuCoinOrderDetail
{
    public string Id { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string? ClientOid { get; set; }
    public string Side { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Price { get; set; }
    public string? Size { get; set; }
    public string? DealFunds { get; set; }
    public string? DealSize { get; set; }
    public string? Fee { get; set; }
    public string? FeeCurrency { get; set; }
    public bool IsActive { get; set; }
    public bool CancelExist { get; set; }
    public long CreatedAt { get; set; }
}

internal class KuCoinOrderListResponse
{
    public List<KuCoinOrderDetail>? Items { get; set; }
    public int CurrentPage { get; set; }
    public int PageSize { get; set; }
    public int TotalNum { get; set; }
    public int TotalPage { get; set; }
}

internal class KuCoinApiKeyInfo
{
    public string? Permission { get; set; }
    public string? IpWhitelist { get; set; }
    public string? Remark { get; set; }
}

#endregion
