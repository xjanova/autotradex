/*
 * ============================================================================
 * AutoTrade-X - Bitkub Exchange Client
 * ============================================================================
 * Real implementation for Bitkub API (Thailand Exchange)
 * Documentation: https://github.com/bitkub/bitkub-official-api-docs
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

public class BitkubClient : BaseExchangeClient
{
    public override string ExchangeName => "Bitkub";

    private readonly ICurrencyConverterService? _currencyConverter;

    /// <summary>
    /// Indicates that Bitkub uses THB as base currency
    /// </summary>
    public bool UsesThb => true;

    public BitkubClient(ExchangeConfig config, ILoggingService logger, ICurrencyConverterService? currencyConverter = null)
        : base(config, logger)
    {
        _currencyConverter = currencyConverter;
    }

    /// <summary>
    /// Get THB/USDT exchange rate (for display purposes)
    /// </summary>
    public async Task<decimal> GetThbUsdtRateAsync(CancellationToken cancellationToken = default)
    {
        if (_currencyConverter != null)
        {
            return await _currencyConverter.GetThbUsdtRateAsync(cancellationToken);
        }

        // Fallback: Fetch directly from Bitkub
        try
        {
            var response = await GetAsync<Dictionary<string, BitkubTickerData>>(
                "/api/market/ticker",
                cancellationToken);

            if (response != null && response.TryGetValue("THB_USDT", out var usdtTicker))
            {
                return usdtTicker.Last;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ExchangeName, $"Failed to get THB/USDT rate: {ex.Message}");
        }

        return 35.0m; // Default fallback
    }

    /// <summary>
    /// Convert THB price to USDT equivalent for comparison with other exchanges
    /// </summary>
    public async Task<decimal> ConvertThbToUsdtAsync(decimal thbAmount, CancellationToken cancellationToken = default)
    {
        var rate = await GetThbUsdtRateAsync(cancellationToken);
        if (rate == 0) return 0;
        return thbAmount / rate;
    }

    /// <summary>
    /// Convert USDT price to THB for display
    /// </summary>
    public async Task<decimal> ConvertUsdtToThbAsync(decimal usdtAmount, CancellationToken cancellationToken = default)
    {
        var rate = await GetThbUsdtRateAsync(cancellationToken);
        return usdtAmount * rate;
    }

    #region Market Data (Public APIs)

    public override async Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedSymbol = NormalizeSymbol(symbol);
            var response = await GetAsync<Dictionary<string, BitkubTickerData>>(
                "/api/market/ticker",
                cancellationToken);

            if (response == null || !response.TryGetValue(normalizedSymbol, out var data))
            {
                throw new Exception($"Failed to get ticker for {symbol}");
            }

            return new Ticker
            {
                Symbol = symbol,
                Exchange = ExchangeName,
                BidPrice = data.HighestBid,
                AskPrice = data.LowestAsk,
                BidQuantity = 0, // Bitkub ticker doesn't include quantities
                AskQuantity = 0,
                LastPrice = data.Last,
                Volume24h = data.BaseVolume,
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
            var response = await GetAsync<BitkubOrderBookResponse>(
                $"/api/market/books?sym={normalizedSymbol}&lmt={Math.Min(depth, 100)}",
                cancellationToken);

            if (response?.Result == null)
            {
                throw new Exception($"Failed to get order book for {symbol}");
            }

            var orderBook = new OrderBook
            {
                Symbol = symbol,
                Exchange = ExchangeName,
                Timestamp = DateTime.UtcNow
            };

            foreach (var bid in response.Result.Bids ?? Array.Empty<decimal[]>())
            {
                if (bid.Length >= 2)
                {
                    orderBook.Bids.Add(new OrderBookEntry(bid[0], bid[1]));
                }
            }

            foreach (var ask in response.Result.Asks ?? Array.Empty<decimal[]>())
            {
                if (ask.Length >= 2)
                {
                    orderBook.Asks.Add(new OrderBookEntry(ask[0], ask[1]));
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
    /// Get multiple tickers in a single API call (efficient for Bitkub)
    /// Bitkub's /api/market/ticker returns ALL tickers in one call
    /// Supports both formats: "THB_BTC" and "BTC/THB"
    /// </summary>
    public override async Task<Dictionary<string, Ticker>> GetTickersAsync(
        IEnumerable<string> symbols,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, Ticker>();

        try
        {
            // Get ALL tickers in one API call (Bitkub returns all pairs at once)
            var response = await GetAsync<Dictionary<string, BitkubTickerData>>(
                "/api/market/ticker",
                cancellationToken);

            if (response == null || response.Count == 0)
            {
                _logger.LogWarning(ExchangeName, "GetTickersAsync: No data returned from API");
                return result;
            }

            _logger.LogInfo(ExchangeName, $"GetTickersAsync: Got {response.Count} tickers from API");

            foreach (var symbol in symbols)
            {
                try
                {
                    // Normalize the symbol to Bitkub format (THB_XXX)
                    var normalizedSymbol = NormalizeSymbol(symbol);

                    if (response.TryGetValue(normalizedSymbol, out var data))
                    {
                        var ticker = new Ticker
                        {
                            Symbol = normalizedSymbol, // Use Bitkub format for consistency
                            Exchange = ExchangeName,
                            BidPrice = data.HighestBid,
                            AskPrice = data.LowestAsk,
                            BidQuantity = 0,
                            AskQuantity = 0,
                            LastPrice = data.Last,
                            Volume24h = data.BaseVolume,
                            Timestamp = DateTime.UtcNow
                        };

                        // Store with the original symbol as key (to match what scanner expects)
                        result[symbol] = ticker;
                    }
                    else
                    {
                        _logger.LogWarning(ExchangeName, $"Symbol {symbol} (normalized: {normalizedSymbol}) not found in response");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ExchangeName, $"Error processing symbol {symbol}: {ex.Message}");
                    // Continue with next symbol instead of failing all
                }
            }

            _logger.LogInfo(ExchangeName, $"GetTickersAsync: Returning {result.Count} tickers");
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"GetTickersAsync error: {ex.Message}");
            // Return empty result instead of throwing
        }

        return result;
    }

    /// <summary>
    /// Get ALL tickers from Bitkub - no symbol filter needed
    /// Bitkub's /api/market/ticker returns all pairs at once
    /// </summary>
    public override async Task<Dictionary<string, Ticker>> GetAllTickersAsync(
        string? quoteAsset = null,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, Ticker>();

        try
        {
            _logger.LogInfo(ExchangeName, "GetAllTickersAsync: Fetching all tickers...");

            var response = await GetAsync<Dictionary<string, BitkubTickerData>>(
                "/api/market/ticker",
                cancellationToken);

            if (response == null || response.Count == 0)
            {
                _logger.LogWarning(ExchangeName, "GetAllTickersAsync: No data returned from API");
                return result;
            }

            _logger.LogInfo(ExchangeName, $"GetAllTickersAsync: Got {response.Count} tickers from API");

            foreach (var kvp in response)
            {
                var symbol = kvp.Key;  // Format: THB_XXX
                var data = kvp.Value;

                // Filter by quote asset if specified (Bitkub uses THB as quote)
                if (!string.IsNullOrEmpty(quoteAsset))
                {
                    // For Bitkub, symbol format is "THB_XXX"
                    if (!symbol.StartsWith($"{quoteAsset}_", StringComparison.OrdinalIgnoreCase) &&
                        !symbol.EndsWith($"_{quoteAsset}", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                // Skip pairs with zero volume (inactive/delisted)
                if (data.BaseVolume <= 0 && data.Last <= 0)
                    continue;

                result[symbol] = new Ticker
                {
                    Symbol = symbol,
                    Exchange = ExchangeName,
                    BidPrice = data.HighestBid,
                    AskPrice = data.LowestAsk,
                    BidQuantity = 0,
                    AskQuantity = 0,
                    LastPrice = data.Last,
                    Volume24h = data.BaseVolume,
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

    public override async Task<List<PriceCandle>> GetKlinesAsync(string symbol, string interval = "1m", int limit = 100, CancellationToken cancellationToken = default)
    {
        var candles = new List<PriceCandle>();
        try
        {
            var normalizedSymbol = NormalizeSymbol(symbol);

            // Bitkub /tradingview/history uses resolution: 1, 5, 15, 60, 240, 1D
            var resolution = interval switch
            {
                "1m" => "1",
                "5m" => "5",
                "15m" => "15",
                "30m" => "30",
                "1h" => "60",
                "4h" => "240",
                "1d" => "1D",
                _ => "1"
            };

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

            var to = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var from = to - (limit * intervalSeconds);

            var response = await GetAsync<BitkubTradingViewHistory>(
                $"/tradingview/history?symbol={normalizedSymbol}&resolution={resolution}&from={from}&to={to}",
                cancellationToken);

            if (response?.T == null || response.T.Count == 0) return candles;

            for (int i = 0; i < response.T.Count; i++)
            {
                candles.Add(new PriceCandle
                {
                    Time = DateTimeOffset.FromUnixTimeSeconds(response.T[i]).UtcDateTime,
                    Open = i < response.O.Count ? response.O[i] : 0,
                    High = i < response.H.Count ? response.H[i] : 0,
                    Low = i < response.L.Count ? response.L[i] : 0,
                    Close = i < response.C.Count ? response.C[i] : 0,
                    Volume = i < response.V.Count ? response.V[i] : 0
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"GetKlinesAsync error for {symbol}: {ex.Message}");
        }
        return candles;
    }

    #endregion

    #region Connection Test

    /// <summary>
    /// Test connection to Bitkub API - uses THB_BTC ticker (Bitkub's most popular pair)
    /// Also tests API credentials if provided
    /// </summary>
    public override async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInfo(ExchangeName, "Testing connection...");

            // Step 1: Test public API - get server status
            var statusResponse = await GetAsync<List<BitkubServerStatus>>(
                "/api/status",
                cancellationToken);

            if (statusResponse == null || statusResponse.Count == 0)
            {
                _logger.LogWarning(ExchangeName, "Failed to get server status");
                return false;
            }

            _logger.LogInfo(ExchangeName, $"Public API OK ({statusResponse.Count} services)");

            // Step 2: Test ticker API with THB_BTC
            var tickerResponse = await GetAsync<Dictionary<string, BitkubTickerData>>(
                "/api/market/ticker",
                cancellationToken);

            if (tickerResponse == null || !tickerResponse.ContainsKey("THB_BTC"))
            {
                _logger.LogWarning(ExchangeName, "Failed to get THB_BTC ticker");
                return false;
            }

            _logger.LogInfo(ExchangeName, $"THB_BTC: {tickerResponse["THB_BTC"].Last:N2} THB");

            // Step 3: If API credentials are configured, test private API
            if (HasCredentials())
            {
                _logger.LogInfo(ExchangeName, "Testing API credentials...");
                try
                {
                    var balance = await GetBalanceAsync(cancellationToken);
                    if (balance != null)
                    {
                        _logger.LogInfo(ExchangeName, $"API verified! Found {balance.Assets.Count} assets");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ExchangeName, $"Credentials test failed: {ex.Message}");
                    IsConnected = false;
                    return false;
                }
            }
            else
            {
                _logger.LogWarning(ExchangeName, "No credentials - skipping private API test");
            }

            IsConnected = true;
            _logger.LogInfo(ExchangeName, "Connection test passed!");
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
    /// Get API key permissions from Bitkub
    /// Bitkub ไม่มี API สำหรับเช็ค permissions โดยตรง
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

            // Bitkub API key permissions ต้องดูจากการตั้งค่าบนเว็บ
            // ไม่มี API สำหรับเช็คโดยตรง - แจ้งให้ผู้ใช้ตรวจสอบเอง
            if (permissions.CanRead)
            {
                permissions.AdditionalInfo = "กรุณาตรวจสอบสิทธิ์ Trade/Withdraw ที่ bitkub.com/api";
                // สมมติว่ามี Trade ถ้า Read ได้ (Bitkub API key ปกติมี Trade)
                permissions.CanTrade = true;
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

    #region Account Data (Private APIs)

    public override async Task<AccountBalance> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInfo(ExchangeName, "=== GetBalanceAsync START ===");

            // Debug: Check what env var names we're looking for
            _logger.LogInfo(ExchangeName, $"Looking for env var: {_config.ApiKeyEnvVar}");

            var apiKey = GetApiKey();
            var apiSecret = GetApiSecret();

            _logger.LogInfo(ExchangeName, $"API Key found: {!string.IsNullOrEmpty(apiKey)}");
            _logger.LogInfo(ExchangeName, $"API Secret found: {!string.IsNullOrEmpty(apiSecret)}");

            if (!HasCredentials())
            {
                _logger.LogError(ExchangeName, "No credentials found in environment variables!");
                throw new InvalidOperationException($"{ExchangeName}: API credentials not configured. Please configure API keys in Settings and click Save.");
            }

            _logger.LogInfo(ExchangeName, $"API Key: {apiKey?.Substring(0, Math.Min(8, apiKey?.Length ?? 0))}...");

            // IMPORTANT: Use server timestamp, not local time
            var timestamp = await GetServerTimestampAsync(cancellationToken);
            _logger.LogInfo(ExchangeName, $"Server timestamp: {timestamp}");

            var path = "/api/v3/market/wallet";

            // According to Bitkub API docs:
            // Wallet endpoint is POST but requires NO body
            // Signing string = timestamp + method + path (no body)
            var signature = SignRequestV3(timestamp, "POST", path, "", "");
            _logger.LogInfo(ExchangeName, $"Signing string: {timestamp}POST{path}");

            using var request = new HttpRequestMessage(HttpMethod.Post, path);

            // Add required headers as per Bitkub docs
            request.Headers.Add("Accept", "application/json");
            AddBitkubAuthHeaders(request, timestamp, signature);

            _logger.LogInfo(ExchangeName, "Sending wallet request...");
            var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogInfo(ExchangeName, $"Response Status: {response.StatusCode}");
            _logger.LogInfo(ExchangeName, $"Response: {responseContent}");

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"API call failed: {response.StatusCode} - {responseContent}");
            }

            var result = JsonSerializer.Deserialize<BitkubWalletResponse>(responseContent, _jsonOptions);

            if (result?.Error != 0)
            {
                var errorCode = result?.Error ?? -1;
                var errorMsg = GetBitkubErrorMessage(errorCode);
                _logger.LogError(ExchangeName, $"API Error {errorCode}: {errorMsg}");

                // Provide more specific error messages for common issues
                var userFriendlyMsg = errorCode switch
                {
                    5 => $"IP not allowed - กรุณาเพิ่ม IP ของคุณใน whitelist ที่ bitkub.com/api (error {errorCode})",
                    6 => $"Invalid signature - ตรวจสอบ API Secret ว่าถูกต้อง (error {errorCode})",
                    3 => $"Invalid API key - ตรวจสอบ API Key ว่าถูกต้อง (error {errorCode})",
                    8 => $"Invalid timestamp - เวลาของเครื่องไม่ตรง (error {errorCode})",
                    52 => $"Invalid permission - API Key ไม่มีสิทธิ์อ่าน Wallet (error {errorCode})",
                    _ => $"Bitkub API Error {errorCode}: {errorMsg}"
                };

                throw new Exception(userFriendlyMsg);
            }

            _logger.LogInfo(ExchangeName, "Success! Parsing balance...");

            var balance = new AccountBalance
            {
                Exchange = ExchangeName,
                Timestamp = DateTime.UtcNow,
                Assets = new Dictionary<string, AssetBalance>()
            };

            if (result?.Result != null)
            {
                foreach (var kvp in result.Result)
                {
                    var available = kvp.Value;
                    if (available > 0)
                    {
                        balance.Assets[kvp.Key.ToUpperInvariant()] = new AssetBalance
                        {
                            Asset = kvp.Key.ToUpperInvariant(),
                            Available = available,
                            Total = available
                        };
                    }
                }
            }

            _logger.LogInfo(ExchangeName, $"Balance loaded: {balance.Assets.Count} assets");
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
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Bitkub uses different endpoints for buy/sell
            var path = request.Side == OrderSide.Buy
                ? "/api/v3/market/place-bid"
                : "/api/v3/market/place-ask";

            var orderData = new Dictionary<string, object>
            {
                ["sym"] = normalizedSymbol,
                ["amt"] = request.Quantity,
                ["rat"] = request.Type == OrderType.Market ? 0 : (request.Price ?? 0),
                ["typ"] = request.Type == OrderType.Market ? "market" : "limit"
            };

            if (!string.IsNullOrEmpty(request.ClientOrderId))
            {
                orderData["client_id"] = request.ClientOrderId;
            }

            var body = JsonSerializer.Serialize(orderData, _jsonOptions);
            var signature = SignRequestV3(timestamp, "POST", path, "", body);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            AddBitkubAuthHeaders(httpRequest, timestamp, signature);

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Order placement failed: {responseContent}");
            }

            var result = JsonSerializer.Deserialize<BitkubOrderResponse>(responseContent, _jsonOptions);

            if (result?.Error != 0)
            {
                throw new Exception($"Order failed: Error {result?.Error}");
            }

            return new Order
            {
                OrderId = result?.Result?.Id ?? "",
                ClientOrderId = request.ClientOrderId,
                Exchange = ExchangeName,
                Symbol = request.Symbol,
                Side = request.Side,
                Type = request.Type,
                Status = OrderStatus.Pending,
                RequestedQuantity = request.Quantity,
                FilledQuantity = 0,
                RequestedPrice = request.Price,
                AverageFilledPrice = request.Price ?? 0,
                Fee = result?.Result?.Fee ?? 0,
                FeeCurrency = "THB",
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
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var path = "/api/v3/market/cancel-order";

            // Bitkub requires knowing the order side to cancel
            // Try "buy" first, then "sell" if that fails
            foreach (var side in new[] { "buy", "sell" })
            {
                try
                {
                    var cancelData = new Dictionary<string, object>
                    {
                        ["sym"] = normalizedSymbol,
                        ["id"] = orderId,
                        ["sd"] = side
                    };

                    var body = JsonSerializer.Serialize(cancelData, _jsonOptions);
                    var signature = SignRequestV3(timestamp, "POST", path, "", body);

                    using var request = new HttpRequestMessage(HttpMethod.Post, path)
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json")
                    };

                    AddBitkubAuthHeaders(request, timestamp, signature);

                    var response = await _httpClient.SendAsync(request, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        return new Order
                        {
                            OrderId = orderId,
                            Exchange = ExchangeName,
                            Symbol = symbol,
                            Status = OrderStatus.Cancelled,
                            UpdatedAt = DateTime.UtcNow
                        };
                    }
                }
                catch
                {
                    if (side == "sell") throw; // Both sides failed
                }
            }

            throw new Exception($"Failed to cancel order {orderId}: both buy and sell sides failed");
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
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var path = "/api/v3/market/order-info";

            var requestData = new Dictionary<string, object>
            {
                ["sym"] = normalizedSymbol,
                ["id"] = orderId
            };

            var body = JsonSerializer.Serialize(requestData, _jsonOptions);
            var signature = SignRequestV3(timestamp, "POST", path, "", body);

            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            AddBitkubAuthHeaders(request, timestamp, signature);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<BitkubOrderInfoResponse>(_jsonOptions, cancellationToken);

            if (result?.Error != 0 || result?.Result == null)
            {
                throw new Exception($"Failed to get order {orderId}");
            }

            var data = result.Result;

            return new Order
            {
                OrderId = data.Id,
                Exchange = ExchangeName,
                Symbol = symbol,
                Side = data.Side == "buy" ? OrderSide.Buy : OrderSide.Sell,
                Type = data.Type == "market" ? OrderType.Market : OrderType.Limit,
                Status = MapOrderStatus(data.Status),
                RequestedQuantity = data.Amount,
                FilledQuantity = data.Receive,
                RequestedPrice = data.Rate > 0 ? data.Rate : null,
                AverageFilledPrice = data.Rate,
                Fee = data.Fee,
                FeeCurrency = "THB",
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(data.Ts).UtcDateTime,
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

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var path = "/api/v3/market/my-open-orders";
            var requestData = new Dictionary<string, object>();

            if (symbol != null)
            {
                requestData["sym"] = NormalizeSymbol(symbol);
            }

            var body = JsonSerializer.Serialize(requestData, _jsonOptions);
            var signature = SignRequestV3(timestamp, "POST", path, "", body);

            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            AddBitkubAuthHeaders(request, timestamp, signature);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<BitkubOpenOrdersResponse>(_jsonOptions, cancellationToken);

            if (result?.Error != 0 || result?.Result == null)
            {
                return new List<Order>();
            }

            return result.Result.Select(data => new Order
            {
                OrderId = data.Id,
                Exchange = ExchangeName,
                Symbol = data.Sym?.Replace("THB_", "") + "/THB",
                Side = data.Side == "buy" ? OrderSide.Buy : OrderSide.Sell,
                Type = data.Type == "market" ? OrderType.Market : OrderType.Limit,
                Status = OrderStatus.Open,
                RequestedQuantity = data.Amount,
                RequestedPrice = data.Rate > 0 ? data.Rate : null,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(data.Ts).UtcDateTime
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
        // Convert "BTC/THB" to "THB_BTC" (Bitkub format)
        var parts = symbol.ToUpperInvariant().Split('/');
        if (parts.Length == 2)
        {
            return $"{parts[1]}_{parts[0]}";
        }
        return symbol.Replace("/", "_").ToUpperInvariant();
    }

    /// <summary>
    /// Get server timestamp from Bitkub API
    /// ดึง timestamp จาก server Bitkub (สำคัญมาก - ต้องใช้ server time ไม่ใช่ local time)
    /// </summary>
    private async Task<long> GetServerTimestampAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/v3/servertime", cancellationToken);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            // Response is just a number: 1699381086593
            if (long.TryParse(content.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var serverTime))
            {
                return serverTime;
            }

            // Fallback to local time if parsing fails
            _logger.LogWarning(ExchangeName, $"Failed to parse server time: {content}, using local time");
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ExchangeName, $"Failed to get server time: {ex.Message}, using local time");
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    /// <summary>
    /// Create signature for Bitkub API v3
    /// Signature = HMAC-SHA256(timestamp + method + path + query + payload, secret)
    /// </summary>
    private string SignRequestV3(long timestamp, string method, string path, string query, string payload)
    {
        var secret = GetApiSecret();
        if (string.IsNullOrEmpty(secret))
        {
            throw new Exception("API secret not configured");
        }

        // Build signature string: timestamp + method + path + query + payload
        var signatureString = $"{timestamp}{method}{path}";
        if (!string.IsNullOrEmpty(query))
        {
            signatureString += query;
        }
        if (!string.IsNullOrEmpty(payload))
        {
            signatureString += payload;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureString));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// Legacy signature method - signs only payload (deprecated but kept for compatibility)
    /// </summary>
    private string SignRequest(string payload)
    {
        var secret = GetApiSecret();
        if (string.IsNullOrEmpty(secret))
        {
            throw new Exception("API secret not configured");
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// Add Bitkub API v3 authentication headers to request
    /// </summary>
    private void AddBitkubAuthHeaders(HttpRequestMessage request, long timestamp, string signature)
    {
        request.Headers.Add("X-BTK-APIKEY", GetApiKey());
        request.Headers.Add("X-BTK-TIMESTAMP", timestamp.ToString());
        request.Headers.Add("X-BTK-SIGN", signature);
    }

    private OrderStatus MapOrderStatus(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "unfilled" => OrderStatus.Pending,
            "partially_filled" => OrderStatus.PartiallyFilled,
            "filled" => OrderStatus.Filled,
            "cancelled" or "canceled" => OrderStatus.Cancelled,
            _ => OrderStatus.Error
        };
    }

    /// <summary>
    /// Get human-readable error message from Bitkub error code
    /// แปลง error code เป็นข้อความที่อ่านเข้าใจได้
    /// </summary>
    private static string GetBitkubErrorMessage(int errorCode)
    {
        return errorCode switch
        {
            0 => "Success",
            1 => "Invalid JSON payload",
            2 => "Missing X-BTK-APIKEY header",
            3 => "Invalid API key",
            4 => "API pending for activation",
            5 => "IP not allowed - กรุณาเพิ่ม IP ของคุณใน whitelist ที่ bitkub.com",
            6 => "Invalid signature - ลายเซ็นไม่ถูกต้อง",
            7 => "Missing timestamp header",
            8 => "Invalid timestamp - timestamp ไม่ถูกต้อง",
            9 => "Invalid user",
            10 => "Invalid parameter",
            11 => "Invalid symbol",
            12 => "Invalid amount",
            13 => "Invalid rate",
            14 => "Improper rate",
            15 => "Amount too low",
            16 => "Failed to get balance",
            17 => "Wallet is empty",
            18 => "Insufficient balance",
            19 => "Failed to insert order into db",
            20 => "Failed to deduct balance",
            21 => "Invalid order for cancellation",
            22 => "Invalid side",
            23 => "Failed to update order status",
            24 => "Invalid order for lookup",
            25 => "KYC required",
            30 => "Limit exceeds",
            40 => "Pending withdrawal exists",
            41 => "Invalid currency for withdrawal",
            42 => "Address is not whitelisted",
            43 => "Failed to deduct crypto",
            44 => "Failed to create withdrawal record",
            45 => "Nonce has to be numeric",
            46 => "Invalid nonce",
            47 => "Withdrawal limit exceeded",
            48 => "Invalid bank account",
            49 => "Bank limit exceeded",
            50 => "Pending withdrawal exists",
            51 => "Withdrawal is under maintenance",
            52 => "Invalid permission - API key ไม่มีสิทธิ์เข้าถึง endpoint นี้",
            53 => "Invalid internal address",
            54 => "Address has been deprecated",
            55 => "Cancel only mode",
            56 => "User has been suspended from purchasing",
            57 => "User has been suspended from selling",
            90 => "Server is busy - กรุณาลองใหม่อีกครั้ง",
            _ => $"Unknown error code: {errorCode}"
        };
    }

    #endregion
}

#region Bitkub API Response Models

internal class BitkubTickerData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("last")]
    public decimal Last { get; set; }

    [JsonPropertyName("lowestAsk")]
    public decimal LowestAsk { get; set; }

    [JsonPropertyName("highestBid")]
    public decimal HighestBid { get; set; }

    [JsonPropertyName("percentChange")]
    public decimal PercentChange { get; set; }

    [JsonPropertyName("baseVolume")]
    public decimal BaseVolume { get; set; }

    [JsonPropertyName("quoteVolume")]
    public decimal QuoteVolume { get; set; }

    [JsonPropertyName("isFrozen")]
    public int IsFrozen { get; set; }

    [JsonPropertyName("high24hr")]
    public decimal High24hr { get; set; }

    [JsonPropertyName("low24hr")]
    public decimal Low24hr { get; set; }
}

internal class BitkubOrderBookResponse
{
    [JsonPropertyName("error")]
    public int Error { get; set; }

    [JsonPropertyName("result")]
    public BitkubOrderBookData? Result { get; set; }
}

internal class BitkubOrderBookData
{
    [JsonPropertyName("asks")]
    public decimal[][] Asks { get; set; } = Array.Empty<decimal[]>();

    [JsonPropertyName("bids")]
    public decimal[][] Bids { get; set; } = Array.Empty<decimal[]>();
}

internal class BitkubWalletResponse
{
    [JsonPropertyName("error")]
    public int Error { get; set; }

    [JsonPropertyName("result")]
    public Dictionary<string, decimal>? Result { get; set; }
}

internal class BitkubOrderResponse
{
    [JsonPropertyName("error")]
    public int Error { get; set; }

    [JsonPropertyName("result")]
    public BitkubOrderResult? Result { get; set; }
}

internal class BitkubOrderResult
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("hash")]
    public string? Hash { get; set; }

    [JsonPropertyName("typ")]
    public string? Typ { get; set; }

    [JsonPropertyName("amt")]
    public decimal Amount { get; set; }

    [JsonPropertyName("rat")]
    public decimal Rate { get; set; }

    [JsonPropertyName("fee")]
    public decimal Fee { get; set; }

    [JsonPropertyName("cre")]
    public decimal Credit { get; set; }

    [JsonPropertyName("rec")]
    public decimal Receive { get; set; }

    [JsonPropertyName("ts")]
    public long Ts { get; set; }
}

internal class BitkubOrderInfoResponse
{
    [JsonPropertyName("error")]
    public int Error { get; set; }

    [JsonPropertyName("result")]
    public BitkubOrderInfo? Result { get; set; }
}

internal class BitkubOrderInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("side")]
    public string Side { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("rate")]
    public decimal Rate { get; set; }

    [JsonPropertyName("fee")]
    public decimal Fee { get; set; }

    [JsonPropertyName("credit")]
    public decimal Credit { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("receive")]
    public decimal Receive { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("ts")]
    public long Ts { get; set; }
}

internal class BitkubOpenOrdersResponse
{
    [JsonPropertyName("error")]
    public int Error { get; set; }

    [JsonPropertyName("result")]
    public List<BitkubOpenOrder>? Result { get; set; }
}

internal class BitkubOpenOrder
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("sym")]
    public string? Sym { get; set; }

    [JsonPropertyName("side")]
    public string Side { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("rate")]
    public decimal Rate { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("ts")]
    public long Ts { get; set; }
}

/// <summary>
/// Bitkub server status response from /api/status
/// </summary>
internal class BitkubTradingViewHistory
{
    [JsonPropertyName("t")]
    public List<long> T { get; set; } = new(); // Timestamps

    [JsonPropertyName("o")]
    public List<decimal> O { get; set; } = new(); // Open

    [JsonPropertyName("h")]
    public List<decimal> H { get; set; } = new(); // High

    [JsonPropertyName("l")]
    public List<decimal> L { get; set; } = new(); // Low

    [JsonPropertyName("c")]
    public List<decimal> C { get; set; } = new(); // Close

    [JsonPropertyName("v")]
    public List<decimal> V { get; set; } = new(); // Volume

    [JsonPropertyName("s")]
    public string? S { get; set; } // Status
}

internal class BitkubServerStatus
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

#endregion
