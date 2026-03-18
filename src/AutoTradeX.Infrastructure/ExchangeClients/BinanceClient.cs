/*
 * ============================================================================
 * AutoTrade-X - Binance Exchange Client
 * ============================================================================
 * Real implementation for Binance Spot API
 * Documentation: https://binance-docs.github.io/apidocs/spot/en/
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

public class BinanceClient : BaseExchangeClient
{
    public override string ExchangeName => "Binance";

    public BinanceClient(ExchangeConfig config, ILoggingService logger)
        : base(config, logger)
    {
        // Set Binance API key header
        var apiKey = GetApiKey();
        if (!string.IsNullOrEmpty(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("X-MBX-APIKEY", apiKey);
        }
    }

    #region Market Data (Public APIs - No authentication required)

    public override async Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedSymbol = NormalizeSymbol(symbol);
            var response = await GetAsync<BinanceTickerResponse>(
                $"/api/v3/ticker/bookTicker?symbol={normalizedSymbol}",
                cancellationToken);

            if (response == null)
            {
                throw new Exception($"Failed to get ticker for {symbol}");
            }

            // Get 24h stats for volume and last price
            var stats24h = await GetAsync<Binance24hResponse>(
                $"/api/v3/ticker/24hr?symbol={normalizedSymbol}",
                cancellationToken);

            return new Ticker
            {
                Symbol = symbol,
                Exchange = ExchangeName,
                BidPrice = decimal.Parse(response.BidPrice, CultureInfo.InvariantCulture),
                AskPrice = decimal.Parse(response.AskPrice, CultureInfo.InvariantCulture),
                BidQuantity = decimal.Parse(response.BidQty, CultureInfo.InvariantCulture),
                AskQuantity = decimal.Parse(response.AskQty, CultureInfo.InvariantCulture),
                LastPrice = stats24h != null ? decimal.Parse(stats24h.LastPrice, CultureInfo.InvariantCulture) : decimal.Parse(response.BidPrice, CultureInfo.InvariantCulture),
                Volume24h = stats24h != null ? decimal.Parse(stats24h.Volume, CultureInfo.InvariantCulture) : 0,
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
            var response = await GetAsync<BinanceOrderBookResponse>(
                $"/api/v3/depth?symbol={normalizedSymbol}&limit={Math.Min(depth, 5000)}",
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

            foreach (var bid in response.Bids)
            {
                if (bid.Length >= 2)
                {
                    orderBook.Bids.Add(new OrderBookEntry(
                        decimal.Parse(bid[0], CultureInfo.InvariantCulture),
                        decimal.Parse(bid[1], CultureInfo.InvariantCulture)
                    ));
                }
            }

            foreach (var ask in response.Asks)
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
    /// Get ALL tickers from Binance in one API call
    /// Endpoint: GET /api/v3/ticker/24hr (without symbol param returns all)
    /// </summary>
    public override async Task<Dictionary<string, Ticker>> GetAllTickersAsync(
        string? quoteAsset = null,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, Ticker>();

        try
        {
            _logger.LogInfo(ExchangeName, "GetAllTickersAsync: Fetching all tickers...");

            // Get all 24hr tickers in one call
            var response = await GetAsync<List<Binance24hResponse>>(
                "/api/v3/ticker/24hr",
                cancellationToken);

            if (response == null || response.Count == 0)
            {
                _logger.LogWarning(ExchangeName, "GetAllTickersAsync: No data returned from API");
                return result;
            }

            _logger.LogInfo(ExchangeName, $"GetAllTickersAsync: Got {response.Count} tickers from API");

            foreach (var data in response)
            {
                var symbol = data.Symbol;

                // Filter by quote asset if specified (e.g., "USDT")
                if (!string.IsNullOrEmpty(quoteAsset))
                {
                    if (!symbol.EndsWith(quoteAsset, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                // Skip pairs with zero volume (inactive)
                var volume = decimal.TryParse(data.Volume, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
                var lastPrice = decimal.TryParse(data.LastPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0;

                if (volume <= 0 && lastPrice <= 0)
                    continue;

                result[symbol] = new Ticker
                {
                    Symbol = symbol,
                    Exchange = ExchangeName,
                    BidPrice = 0, // 24hr ticker doesn't include bid/ask
                    AskPrice = 0,
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

    /// <summary>
    /// Get Klines (Candlestick) data from Binance
    /// Endpoint: GET /api/v3/klines
    /// </summary>
    public override async Task<List<PriceCandle>> GetKlinesAsync(
        string symbol, string interval = "1m", int limit = 100, CancellationToken cancellationToken = default)
    {
        var candles = new List<PriceCandle>();

        try
        {
            var normalizedSymbol = NormalizeSymbol(symbol);
            var clampedLimit = Math.Min(limit, 500);

            var response = await GetAsync<List<List<JsonElement>>>(
                $"/api/v3/klines?symbol={normalizedSymbol}&interval={interval}&limit={clampedLimit}",
                cancellationToken);

            if (response == null || response.Count == 0)
            {
                _logger.LogWarning(ExchangeName, $"GetKlinesAsync: No data returned for {symbol} ({interval})");
                return candles;
            }

            // Binance klines format: [openTime, open, high, low, close, volume, closeTime, ...]
            foreach (var kline in response)
            {
                if (kline.Count < 6) continue;

                candles.Add(new PriceCandle
                {
                    Time = DateTimeOffset.FromUnixTimeMilliseconds(kline[0].GetInt64()).UtcDateTime,
                    Open = decimal.Parse(kline[1].GetString() ?? "0", CultureInfo.InvariantCulture),
                    High = decimal.Parse(kline[2].GetString() ?? "0", CultureInfo.InvariantCulture),
                    Low = decimal.Parse(kline[3].GetString() ?? "0", CultureInfo.InvariantCulture),
                    Close = decimal.Parse(kline[4].GetString() ?? "0", CultureInfo.InvariantCulture),
                    Volume = decimal.Parse(kline[5].GetString() ?? "0", CultureInfo.InvariantCulture)
                });
            }

            _logger.LogInfo(ExchangeName, $"GetKlinesAsync: Got {candles.Count} candles for {symbol} ({interval})");
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
    /// Test connection to Binance API with proper validation
    /// </summary>
    public override async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInfo(ExchangeName, "Testing Binance API connection...");

            // Step 1: Test public API - get server time
            var serverTimeResponse = await GetAsync<BinanceServerTime>(
                "/api/v3/time",
                cancellationToken);

            if (serverTimeResponse == null)
            {
                _logger.LogWarning(ExchangeName, "Failed to get Binance server time");
                return false;
            }

            _logger.LogInfo(ExchangeName, "Binance public API reachable");

            // Step 2: Test ticker API with BTCUSDT
            var ticker = await GetTickerAsync("BTCUSDT", cancellationToken);
            if (ticker == null)
            {
                _logger.LogWarning(ExchangeName, "Failed to get BTCUSDT ticker");
                return false;
            }

            _logger.LogInfo(ExchangeName, $"BTCUSDT: ${ticker.LastPrice:N2}");

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
            _logger.LogInfo(ExchangeName, "Binance connection test successful");
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
    /// Get API key permissions from Binance account info
    /// Binance returns canTrade, canWithdraw, canDeposit in account response
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

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var queryString = $"timestamp={timestamp}";
            var signature = SignRequest(queryString);

            var response = await GetAsync<BinanceAccountResponse>(
                $"/api/v3/account?{queryString}&signature={signature}",
                cancellationToken);

            if (response != null)
            {
                permissions.CanRead = true; // ถ้าดึงข้อมูลได้แสดงว่า read ได้
                permissions.CanTrade = response.CanTrade;
                permissions.CanWithdraw = response.CanWithdraw;
                permissions.CanDeposit = response.CanDeposit;

                _logger.LogInfo(ExchangeName, $"API Permissions - Read: {permissions.CanRead}, Trade: {permissions.CanTrade}, Withdraw: {permissions.CanWithdraw}, Deposit: {permissions.CanDeposit}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ExchangeName, $"Failed to get API permissions: {ex.Message}");
            permissions.AdditionalInfo = $"ไม่สามารถตรวจสอบสิทธิ์: {ex.Message}";
        }

        return permissions;
    }

    #endregion

    #region Account Data (Private APIs - Authentication required)

    public override async Task<AccountBalance> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!HasCredentials())
            {
                throw new InvalidOperationException($"{ExchangeName}: API credentials not configured. Please configure API keys in Settings.");
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var queryString = $"timestamp={timestamp}";
            var signature = SignRequest(queryString);

            var response = await GetAsync<BinanceAccountResponse>(
                $"/api/v3/account?{queryString}&signature={signature}",
                cancellationToken);

            if (response == null)
            {
                throw new Exception("Failed to get account balance");
            }

            var balance = new AccountBalance
            {
                Exchange = ExchangeName,
                Timestamp = DateTime.UtcNow,
                Assets = new Dictionary<string, AssetBalance>()
            };

            foreach (var asset in response.Balances)
            {
                var free = decimal.Parse(asset.Free, CultureInfo.InvariantCulture);
                var locked = decimal.Parse(asset.Locked, CultureInfo.InvariantCulture);

                if (free > 0 || locked > 0)
                {
                    balance.Assets[asset.Asset] = new AssetBalance
                    {
                        Asset = asset.Asset,
                        Available = free,
                        Total = free + locked
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
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var parameters = new Dictionary<string, string>
            {
                ["symbol"] = normalizedSymbol,
                ["side"] = request.Side == OrderSide.Buy ? "BUY" : "SELL",
                ["type"] = request.Type == OrderType.Market ? "MARKET" : "LIMIT",
                ["quantity"] = request.Quantity.ToString("F8"),
                ["timestamp"] = timestamp.ToString()
            };

            if (!string.IsNullOrEmpty(request.ClientOrderId))
            {
                parameters["newClientOrderId"] = request.ClientOrderId;
            }

            if (request.Type == OrderType.Limit && request.Price.HasValue)
            {
                parameters["price"] = request.Price.Value.ToString("F8");
                parameters["timeInForce"] = "GTC"; // Good Till Cancel
            }

            var queryString = string.Join("&", parameters.Select(p => $"{p.Key}={p.Value}"));
            var signature = SignRequest(queryString);

            var response = await PostSignedAsync<BinanceOrderResponse>(
                $"/api/v3/order?{queryString}&signature={signature}",
                cancellationToken);

            if (response == null)
            {
                throw new Exception("Failed to place order");
            }

            return new Order
            {
                OrderId = response.OrderId.ToString(),
                ClientOrderId = response.ClientOrderId,
                Exchange = ExchangeName,
                Symbol = request.Symbol,
                Side = request.Side,
                Type = request.Type,
                Status = MapOrderStatus(response.Status),
                RequestedQuantity = request.Quantity,
                FilledQuantity = decimal.Parse(response.ExecutedQty, CultureInfo.InvariantCulture),
                RequestedPrice = request.Price,
                AverageFilledPrice = !string.IsNullOrEmpty(response.AvgPrice) && decimal.Parse(response.AvgPrice, CultureInfo.InvariantCulture) > 0
                    ? decimal.Parse(response.AvgPrice, CultureInfo.InvariantCulture)
                    : request.Price ?? 0,
                Fee = CalculateFee(response),
                FeeCurrency = "USDT",
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(response.TransactTime).UtcDateTime,
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
            var queryString = $"symbol={normalizedSymbol}&orderId={orderId}&timestamp={timestamp}";
            var signature = SignRequest(queryString);

            var response = await DeleteAsync<BinanceOrderResponse>(
                $"/api/v3/order?{queryString}&signature={signature}",
                cancellationToken);

            if (response == null)
            {
                throw new Exception($"Failed to cancel order {orderId}");
            }

            return new Order
            {
                OrderId = response.OrderId.ToString(),
                ClientOrderId = response.ClientOrderId,
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
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var queryString = $"symbol={normalizedSymbol}&orderId={orderId}&timestamp={timestamp}";
            var signature = SignRequest(queryString);

            var response = await GetAsync<BinanceOrderResponse>(
                $"/api/v3/order?{queryString}&signature={signature}",
                cancellationToken);

            if (response == null)
            {
                throw new Exception($"Failed to get order {orderId}");
            }

            return new Order
            {
                OrderId = response.OrderId.ToString(),
                ClientOrderId = response.ClientOrderId,
                Exchange = ExchangeName,
                Symbol = symbol,
                Side = response.Side == "BUY" ? OrderSide.Buy : OrderSide.Sell,
                Type = response.Type == "MARKET" ? OrderType.Market : OrderType.Limit,
                Status = MapOrderStatus(response.Status),
                RequestedQuantity = decimal.Parse(response.OrigQty, CultureInfo.InvariantCulture),
                FilledQuantity = decimal.Parse(response.ExecutedQty, CultureInfo.InvariantCulture),
                RequestedPrice = !string.IsNullOrEmpty(response.Price) ? decimal.Parse(response.Price, CultureInfo.InvariantCulture) : null,
                AverageFilledPrice = !string.IsNullOrEmpty(response.AvgPrice) ? decimal.Parse(response.AvgPrice, CultureInfo.InvariantCulture) : 0,
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(response.Time).UtcDateTime,
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
            var queryString = symbol != null
                ? $"symbol={NormalizeSymbol(symbol)}&timestamp={timestamp}"
                : $"timestamp={timestamp}";
            var signature = SignRequest(queryString);

            var response = await GetAsync<List<BinanceOrderResponse>>(
                $"/api/v3/openOrders?{queryString}&signature={signature}",
                cancellationToken);

            if (response == null)
            {
                return new List<Order>();
            }

            return response.Select(r => new Order
            {
                OrderId = r.OrderId.ToString(),
                ClientOrderId = r.ClientOrderId,
                Exchange = ExchangeName,
                Symbol = r.Symbol,
                Side = r.Side == "BUY" ? OrderSide.Buy : OrderSide.Sell,
                Type = r.Type == "MARKET" ? OrderType.Market : OrderType.Limit,
                Status = MapOrderStatus(r.Status),
                RequestedQuantity = decimal.Parse(r.OrigQty, CultureInfo.InvariantCulture),
                FilledQuantity = decimal.Parse(r.ExecutedQty, CultureInfo.InvariantCulture),
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(r.Time).UtcDateTime
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
        // Convert "BTC/USDT" to "BTCUSDT"
        return symbol.Replace("/", "").Replace("-", "").ToUpperInvariant();
    }

    private string SignRequest(string data)
    {
        var secret = GetApiSecret();
        if (string.IsNullOrEmpty(secret))
        {
            throw new Exception("API secret not configured");
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private async Task<T?> PostSignedAsync<T>(string endpoint, CancellationToken cancellationToken)
    {
        // Binance sends order params as query string, body is empty (correct for their API)
        var response = await _httpClient.PostAsync(endpoint, null, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Binance POST failed ({response.StatusCode}): {content}");
        }
        return JsonSerializer.Deserialize<T>(content, _jsonOptions);
    }

    private async Task<T?> DeleteAsync<T>(string endpoint, CancellationToken cancellationToken)
    {
        var response = await _httpClient.DeleteAsync(endpoint, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Binance DELETE failed ({response.StatusCode}): {content}");
        }
        return JsonSerializer.Deserialize<T>(content, _jsonOptions);
    }

    private OrderStatus MapOrderStatus(string status)
    {
        return status switch
        {
            "NEW" => OrderStatus.Pending,
            "PARTIALLY_FILLED" => OrderStatus.PartiallyFilled,
            "FILLED" => OrderStatus.Filled,
            "CANCELED" or "CANCELLED" => OrderStatus.Cancelled,
            "PENDING_CANCEL" => OrderStatus.Pending,
            "REJECTED" => OrderStatus.Rejected,
            "EXPIRED" => OrderStatus.Expired,
            _ => OrderStatus.Error
        };
    }

    private decimal CalculateFee(BinanceOrderResponse response)
    {
        if (response.Fills != null && response.Fills.Count > 0)
        {
            return response.Fills.Sum(f => decimal.Parse(f.Commission, CultureInfo.InvariantCulture));
        }
        // Estimate fee: 0.1% for maker/taker
        var qty = decimal.Parse(response.ExecutedQty, CultureInfo.InvariantCulture);
        var price = !string.IsNullOrEmpty(response.AvgPrice) && decimal.Parse(response.AvgPrice, CultureInfo.InvariantCulture) > 0
            ? decimal.Parse(response.AvgPrice, CultureInfo.InvariantCulture)
            : !string.IsNullOrEmpty(response.Price) ? decimal.Parse(response.Price, CultureInfo.InvariantCulture) : 0;
        return qty * price * 0.001m;
    }

    #endregion
}

#region Binance API Response Models

internal class BinanceServerTime
{
    public long ServerTime { get; set; }
}

internal class BinanceTickerResponse
{
    public string Symbol { get; set; } = "";
    public string BidPrice { get; set; } = "0";
    public string BidQty { get; set; } = "0";
    public string AskPrice { get; set; } = "0";
    public string AskQty { get; set; } = "0";
}

internal class Binance24hResponse
{
    public string Symbol { get; set; } = "";
    public string LastPrice { get; set; } = "0";
    public string Volume { get; set; } = "0";
    public string PriceChange { get; set; } = "0";
    public string PriceChangePercent { get; set; } = "0";
}

internal class BinanceOrderBookResponse
{
    public long LastUpdateId { get; set; }
    public string[][] Bids { get; set; } = Array.Empty<string[]>();
    public string[][] Asks { get; set; } = Array.Empty<string[]>();
}

internal class BinanceAccountResponse
{
    public string MakerCommission { get; set; } = "0";
    public string TakerCommission { get; set; } = "0";
    public bool CanTrade { get; set; }
    public bool CanWithdraw { get; set; }
    public bool CanDeposit { get; set; }
    public List<BinanceAssetBalance> Balances { get; set; } = new();
}

internal class BinanceAssetBalance
{
    public string Asset { get; set; } = "";
    public string Free { get; set; } = "0";
    public string Locked { get; set; } = "0";
}

internal class BinanceOrderResponse
{
    public long OrderId { get; set; }
    public string ClientOrderId { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Side { get; set; } = "";
    public string Type { get; set; } = "";
    public string Status { get; set; } = "";
    public string Price { get; set; } = "0";
    public string OrigQty { get; set; } = "0";
    public string ExecutedQty { get; set; } = "0";
    public string AvgPrice { get; set; } = "0";
    public long Time { get; set; }
    public long TransactTime { get; set; }
    public List<BinanceFill>? Fills { get; set; }
}

internal class BinanceFill
{
    public string Price { get; set; } = "0";
    public string Qty { get; set; } = "0";
    public string Commission { get; set; } = "0";
    public string CommissionAsset { get; set; } = "";
}

#endregion
