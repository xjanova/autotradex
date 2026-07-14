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
            // v3 depth ใช้ format "BTC_THB" (BASE_QUOTE) และคืน [[price, amount], ...]
            // ห้ามใช้ legacy /api/market/depth หรือ /api/market/books — คืนข้อมูลค้าง (stale)
            // และ books มีโครงสร้าง [order_id(string), ts, volume, rate, amount] ที่ parse ไม่ได้
            var normalizedSymbol = NormalizeTradingViewSymbol(symbol);
            var response = await GetAsync<BitkubOrderBookResponse>(
                $"/api/v3/market/depth?sym={normalizedSymbol}&lmt={Math.Min(depth, 100)}",
                cancellationToken);

            if (response?.Result == null || response.Error != 0)
            {
                throw new Exception($"Failed to get order book for {symbol} (error={response?.Error})");
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
            // TradingView endpoint ใช้ "BTC_THB" (BASE_QUOTE) — กลับด้านกับ market API!
            var normalizedSymbol = NormalizeTradingViewSymbol(symbol);

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
            // ห้าม log API key/response เต็มๆ — ข้อมูล sensitive
            if (!HasCredentials())
            {
                _logger.LogError(ExchangeName, "No credentials found in environment variables!");
                throw new InvalidOperationException($"{ExchangeName}: API credentials not configured. Please configure API keys in Settings and click Save.");
            }

            // IMPORTANT: Use server timestamp, not local time
            var timestamp = await GetServerTimestampAsync(cancellationToken);

            // v4 endpoint — /api/v3/market/wallet ถูกถอดออกแล้ว (26 พ.ค. 2026)
            // v4 คืน available + reserved แยกกัน (v3 เดิมเห็นแค่ available)
            var path = "/api/v4/wallet/balances";
            var signature = SignRequestV3(timestamp, "GET", path, "", "");

            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("Accept", "application/json");
            AddBitkubAuthHeaders(request, timestamp, signature);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"API call failed: {response.StatusCode} - {responseContent}");
            }

            var result = JsonSerializer.Deserialize<BitkubV4BalancesResponse>(responseContent, _jsonOptions);

            if (result == null || result.Code != "0")
            {
                var msg = result?.Message ?? responseContent;
                throw new Exception($"Bitkub wallet error: {msg}");
            }

            var balance = new AccountBalance
            {
                Exchange = ExchangeName,
                Timestamp = DateTime.UtcNow,
                Assets = new Dictionary<string, AssetBalance>()
            };

            foreach (var entry in result.Data ?? new List<BitkubV4Balance>())
            {
                var available = ParseDecimalOrZero(entry.Available);
                var total = ParseDecimalOrZero(entry.Total);
                if (total <= 0) total = available + ParseDecimalOrZero(entry.Reserved);

                if (total > 0 && !string.IsNullOrEmpty(entry.Currency))
                {
                    var asset = entry.Currency.ToUpperInvariant();
                    balance.Assets[asset] = new AssetBalance
                    {
                        Asset = asset,
                        Available = available,
                        Total = total
                    };
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

            // v3 trading endpoints ใช้ format "btc_thb" (base_quote ตัวเล็ก)
            // — คนละแบบกับ legacy market API ("THB_BTC")
            var normalizedSymbol = NormalizeV3Symbol(request.Symbol);
            // spec บังคับใช้ timestamp จาก /api/v3/servertime — clock drift = error 8
            var timestamp = await GetServerTimestampAsync(cancellationToken);

            // Bitkub uses different endpoints for buy/sell
            var path = request.Side == OrderSide.Buy
                ? "/api/v3/market/place-bid"
                : "/api/v3/market/place-ask";

            // หน่วยของ amt ต่างกันตามฝั่ง (ตาม spec):
            //  - place-bid (BUY): amt = จำนวนเงิน THB ที่จะใช้ซื้อ
            //  - place-ask (SELL): amt = จำนวนเหรียญที่จะขาย
            // แอปส่ง Quantity เป็นจำนวนเหรียญเสมอ → ฝั่ง buy ต้องแปลงเป็น THB
            decimal amt;
            if (request.Side == OrderSide.Buy)
            {
                var price = request.Price ?? 0;
                if (price <= 0)
                {
                    // Market buy: ใช้ราคา ask ปัจจุบันคำนวณจำนวนเงิน
                    var ticker = await GetTickerAsync(request.Symbol, cancellationToken);
                    price = ticker?.AskPrice > 0 ? ticker.AskPrice : ticker?.LastPrice ?? 0;
                    if (price <= 0)
                    {
                        throw new Exception($"Cannot determine price for market buy of {request.Symbol}");
                    }
                }
                amt = Math.Round(request.Quantity * price, 2);
            }
            else
            {
                amt = request.Quantity;
            }

            var orderData = new Dictionary<string, object>
            {
                ["sym"] = normalizedSymbol,
                // spec: "no trailing zero" — G29 ตัด trailing zeros ของ decimal scale
                ["amt"] = decimal.Parse(amt.ToString("G29", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
                ["rat"] = request.Type == OrderType.Market
                    ? 0
                    : decimal.Parse((request.Price ?? 0).ToString("G29", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
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
                var code = result?.Error ?? -1;
                throw new Exception($"Order failed: Error {code} - {GetBitkubErrorMessage(code)}");
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

            // cancel-order (ตาม doc ปัจจุบัน) ยังใช้ format "thb_btc" (quote_base ตัวเล็ก)
            var normalizedSymbol = NormalizeSymbol(symbol).ToLowerInvariant();
            var path = "/api/v3/market/cancel-order";

            // Bitkub requires knowing the order side to cancel
            // Try "buy" first, then "sell" if the order isn't found on the buy side
            int lastError = -1;
            foreach (var side in new[] { "buy", "sell" })
            {
                var timestamp = await GetServerTimestampAsync(cancellationToken);
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
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

                // Bitkub คืน HTTP 200 พร้อม {"error": N} เสมอ — ต้องเช็ค error ใน body
                // (ห้ามถือว่า HTTP 200 = cancel สำเร็จ — ออเดอร์อาจยังค้างบนกระดาน!)
                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<BitkubErrorOnlyResponse>(responseContent, _jsonOptions);
                    if (result?.Error == 0)
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
                    lastError = result?.Error ?? -1;
                    // error 21 = invalid order for cancellation → อาจเป็นอีกฝั่ง ลองต่อ
                    if (lastError != 21) break;
                }
            }

            throw new Exception($"Failed to cancel order {orderId}: error {lastError} - {GetBitkubErrorMessage(lastError)}");
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

            // order-info เป็น GET + query params (ไม่ใช่ POST!) และบังคับส่ง sd (side)
            // interface ไม่มี side → ลอง buy ก่อน ถ้าไม่เจอลอง sell
            var normalizedSymbol = NormalizeV3Symbol(symbol);
            var path = "/api/v3/market/order-info";

            foreach (var side in new[] { "buy", "sell" })
            {
                var timestamp = await GetServerTimestampAsync(cancellationToken);
                var query = $"?sym={normalizedSymbol}&id={orderId}&sd={side}";
                var signature = SignRequestV3(timestamp, "GET", path, query, "");

                using var request = new HttpRequestMessage(HttpMethod.Get, path + query);
                AddBitkubAuthHeaders(request, timestamp, signature);

                var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var result = await response.Content.ReadFromJsonAsync<BitkubOrderInfoResponse>(_jsonOptions, cancellationToken);
                if (result?.Error != 0 || result?.Result == null)
                {
                    continue; // ไม่เจอในฝั่งนี้ — ลองอีกฝั่ง
                }

                var data = result.Result;
                var isBuy = side == "buy";

                // หน่วยของ amount/filled ขึ้นกับฝั่ง (spec): buy = THB, sell = จำนวนเหรียญ
                // แปลงกลับเป็นจำนวนเหรียญ (base) ให้ตรง convention ของแอป
                var requestedBase = isBuy && data.Rate > 0 ? data.Amount / data.Rate : data.Amount;
                var filledBase = isBuy && data.Rate > 0 ? data.Filled / data.Rate : data.Filled;

                // เวลาสร้างออเดอร์: ใช้ timestamp ของ fill แรกจาก history (ms)
                var createdAt = data.History?.Count > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(data.History[0].Timestamp).UtcDateTime
                    : DateTime.UtcNow;

                return new Order
                {
                    OrderId = data.Id,
                    Exchange = ExchangeName,
                    Symbol = symbol,
                    Side = isBuy ? OrderSide.Buy : OrderSide.Sell,
                    Type = OrderType.Limit,
                    Status = MapOrderStatus(data.Status, data.PartialFilled),
                    RequestedQuantity = requestedBase,
                    FilledQuantity = filledBase,
                    RequestedPrice = data.Rate > 0 ? data.Rate : null,
                    AverageFilledPrice = data.Rate,
                    Fee = data.Fee,
                    FeeCurrency = "THB",
                    CreatedAt = createdAt,
                    UpdatedAt = DateTime.UtcNow
                };
            }

            throw new Exception($"Failed to get order {orderId} (not found on either side)");
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

            // my-open-orders เป็น GET + query และบังคับส่ง sym
            if (symbol == null)
            {
                _logger.LogWarning(ExchangeName, "GetOpenOrdersAsync requires a symbol on Bitkub — returning empty list");
                return new List<Order>();
            }

            var timestamp = await GetServerTimestampAsync(cancellationToken);
            var path = "/api/v3/market/my-open-orders";
            var query = $"?sym={NormalizeV3Symbol(symbol)}";
            var signature = SignRequestV3(timestamp, "GET", path, query, "");

            using var request = new HttpRequestMessage(HttpMethod.Get, path + query);
            AddBitkubAuthHeaders(request, timestamp, signature);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<BitkubOpenOrdersResponse>(_jsonOptions, cancellationToken);

            if (result?.Error != 0 || result?.Result == null)
            {
                if (result?.Error is int err and not 0)
                {
                    _logger.LogWarning(ExchangeName, $"my-open-orders error {err}: {GetBitkubErrorMessage(err)}");
                }
                return new List<Order>();
            }

            return result.Result.Select(data => new Order
            {
                OrderId = data.Id,
                Exchange = ExchangeName,
                // response ไม่มี field sym — ใช้ symbol ที่ส่งเข้ามา
                Symbol = symbol,
                Side = data.Side == "buy" ? OrderSide.Buy : OrderSide.Sell,
                Type = data.Type == "market" ? OrderType.Market : OrderType.Limit,
                Status = OrderStatus.Open,
                // amount ฝั่ง buy เป็น THB — แปลงเป็นจำนวนเหรียญด้วย rate
                RequestedQuantity = data.Side == "buy" && data.Rate > 0 ? data.Amount / data.Rate : data.Amount,
                RequestedPrice = data.Rate > 0 ? data.Rate : null,
                // ts เป็น milliseconds (เช่น 1702543272000) — ไม่ใช่ seconds
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(data.Ts).UtcDateTime
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
        // Bitkub legacy market API format: "THB_BTC" (QUOTE_BASE)
        // รองรับ input ทุกแบบ: "BTC/THB", "BTCTHB", "THB_BTC"
        var (baseAsset, quoteAsset) = SplitSymbol(symbol);
        if (quoteAsset.Length == 0) return symbol.ToUpperInvariant();

        // ถ้า input มาแบบ Bitkub เดิมอยู่แล้ว (THB_BTC — quote นำหน้า) SplitSymbol
        // จะให้ base="THB" ซึ่งเป็น fiat — สลับกลับให้ถูก
        if (baseAsset == "THB") return $"{baseAsset}_{quoteAsset}";
        return $"{quoteAsset}_{baseAsset}";
    }

    /// <summary>
    /// v3 trading endpoints (place-bid/place-ask/order-info/my-open-orders)
    /// ใช้ format "btc_thb" — base_quote ตัวพิมพ์เล็ก
    /// </summary>
    private string NormalizeV3Symbol(string symbol)
    {
        return NormalizeTradingViewSymbol(symbol).ToLowerInvariant();
    }

    /// <summary>Parse ตัวเลขจาก JSON string แบบไม่ throw ("" หรือ null → 0)</summary>
    private static decimal ParseDecimalOrZero(string? value)
    {
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result : 0m;
    }

    /// <summary>
    /// Bitkub TradingView chart API ใช้ format กลับด้านกับ market API: "BTC_THB" (BASE_QUOTE)
    /// </summary>
    private string NormalizeTradingViewSymbol(string symbol)
    {
        var (baseAsset, quoteAsset) = SplitSymbol(symbol);
        if (quoteAsset.Length == 0) return symbol.ToUpperInvariant();
        if (baseAsset == "THB") return $"{quoteAsset}_{baseAsset}"; // input was THB_BTC
        return $"{baseAsset}_{quoteAsset}";
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

    /// <summary>
    /// Bitkub มี status แค่ 3 ค่า: filled / unfilled / cancelled
    /// partial fill เป็น boolean แยกต่างหาก (ไม่ใช่ status "partially_filled")
    /// </summary>
    private OrderStatus MapOrderStatus(string status, bool partialFilled = false)
    {
        return status.ToLowerInvariant() switch
        {
            "unfilled" => partialFilled ? OrderStatus.PartiallyFilled : OrderStatus.Open,
            "filled" => OrderStatus.Filled,
            "cancelled" or "canceled" => partialFilled ? OrderStatus.PartiallyFilled : OrderStatus.Cancelled,
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

/// <summary>
/// order-info result — หน่วยของ amount/filled ขึ้นกับฝั่ง: buy = THB, sell = เหรียญ
/// (response ไม่มี side/type/receive/ts — side มาจาก query ที่เราส่งเอง)
/// </summary>
internal class BitkubOrderInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("rate")]
    public decimal Rate { get; set; }

    [JsonPropertyName("fee")]
    public decimal Fee { get; set; }

    [JsonPropertyName("credit")]
    public decimal Credit { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("filled")]
    public decimal Filled { get; set; }

    [JsonPropertyName("remaining")]
    public decimal Remaining { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("partial_filled")]
    public bool PartialFilled { get; set; }

    [JsonPropertyName("history")]
    public List<BitkubOrderHistoryEntry>? History { get; set; }
}

internal class BitkubOrderHistoryEntry
{
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("rate")]
    public decimal Rate { get; set; }

    [JsonPropertyName("fee")]
    public decimal Fee { get; set; }

    /// <summary>milliseconds เช่น 1702466375000</summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
}

/// <summary>Response ที่มีแค่ error field เช่น cancel-order</summary>
internal class BitkubErrorOnlyResponse
{
    [JsonPropertyName("error")]
    public int Error { get; set; }
}

/// <summary>GET /api/v4/wallet/balances — ตัวเลขทุกตัวเป็น JSON string</summary>
internal class BitkubV4BalancesResponse
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public List<BitkubV4Balance>? Data { get; set; }
}

internal class BitkubV4Balance
{
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "";

    [JsonPropertyName("available")]
    public string? Available { get; set; }

    [JsonPropertyName("reserved")]
    public string? Reserved { get; set; }

    [JsonPropertyName("total")]
    public string? Total { get; set; }
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
