/*
 * ============================================================================
 * AutoTrade-X - Cross-Exchange Arbitrage Trading Bot
 * ============================================================================
 * Bybit Exchange Client
 * API Documentation: https://bybit-exchange.github.io/docs/v5/intro
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

/// <summary>
/// Bybit Exchange Client (V5 API)
/// Implements full Bybit Spot API integration
/// </summary>
public class BybitClient : BaseExchangeClient
{
    public override string ExchangeName => "Bybit";

    // Bybit V5 API endpoints
    private const string TickerEndpoint = "/v5/market/tickers";
    private const string OrderBookEndpoint = "/v5/market/orderbook";
    private const string AccountEndpoint = "/v5/account/wallet-balance";
    private const string OrderEndpoint = "/v5/order/create";
    private const string CancelOrderEndpoint = "/v5/order/cancel";
    private const string OrderInfoEndpoint = "/v5/order/realtime";
    private const string ServerTimeEndpoint = "/v5/market/time";

    private const int RecvWindow = 5000;

    public BybitClient(ExchangeConfig config, ILoggingService logger)
        : base(config, logger)
    {
    }

    #region Market Data

    public override async Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await GetAsync<BybitResponse<BybitTickerResult>>(
                $"{TickerEndpoint}?category=spot&symbol={symbol}",
                cancellationToken);

            if (response?.Result?.List == null || response.Result.List.Count == 0)
            {
                throw new Exception($"Failed to get ticker for {symbol}");
            }

            var ticker = response.Result.List[0];

            return new Ticker
            {
                Symbol = symbol,
                Exchange = ExchangeName,
                BidPrice = decimal.Parse(ticker.Bid1Price, CultureInfo.InvariantCulture),
                AskPrice = decimal.Parse(ticker.Ask1Price, CultureInfo.InvariantCulture),
                BidQuantity = decimal.Parse(ticker.Bid1Size, CultureInfo.InvariantCulture),
                AskQuantity = decimal.Parse(ticker.Ask1Size, CultureInfo.InvariantCulture),
                LastPrice = decimal.Parse(ticker.LastPrice, CultureInfo.InvariantCulture),
                Volume24h = decimal.Parse(ticker.Volume24h, CultureInfo.InvariantCulture),
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"GetTickerAsync failed for {symbol}: {ex.Message}");
            throw;
        }
    }

    public override async Task<OrderBook> GetOrderBookAsync(string symbol, int depth = 20, CancellationToken cancellationToken = default)
    {
        try
        {
            // Bybit supports: 1, 25, 50, 100, 200
            var validDepth = depth switch
            {
                <= 1 => 1,
                <= 25 => 25,
                <= 50 => 50,
                <= 100 => 100,
                _ => 200
            };

            var response = await GetAsync<BybitResponse<BybitOrderBookResult>>(
                $"{OrderBookEndpoint}?category=spot&symbol={symbol}&limit={validDepth}",
                cancellationToken);

            if (response?.Result == null)
            {
                throw new Exception($"Failed to get order book for {symbol}");
            }

            var orderBook = new OrderBook
            {
                Symbol = symbol,
                Exchange = ExchangeName,
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(response.Result.Ts).UtcDateTime
            };

            // Parse bids
            foreach (var bid in response.Result.B)
            {
                if (bid.Length >= 2 &&
                    decimal.TryParse(bid[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var price) &&
                    decimal.TryParse(bid[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var quantity))
                {
                    orderBook.Bids.Add(new OrderBookEntry(price, quantity));
                }
            }

            // Parse asks
            foreach (var ask in response.Result.A)
            {
                if (ask.Length >= 2 &&
                    decimal.TryParse(ask[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var price) &&
                    decimal.TryParse(ask[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var quantity))
                {
                    orderBook.Asks.Add(new OrderBookEntry(price, quantity));
                }
            }

            return orderBook;
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"GetOrderBookAsync failed for {symbol}: {ex.Message}");
            throw;
        }
    }

    public override async Task<Dictionary<string, Ticker>> GetTickersAsync(
        IEnumerable<string> symbols,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get all spot tickers
            var response = await GetAsync<BybitResponse<BybitTickerResult>>(
                $"{TickerEndpoint}?category=spot",
                cancellationToken);

            var result = new Dictionary<string, Ticker>();
            var symbolSet = symbols.ToHashSet();

            if (response?.Result?.List != null)
            {
                foreach (var ticker in response.Result.List.Where(t => symbolSet.Contains(t.Symbol)))
                {
                    result[ticker.Symbol] = new Ticker
                    {
                        Symbol = ticker.Symbol,
                        Exchange = ExchangeName,
                        BidPrice = decimal.Parse(ticker.Bid1Price, CultureInfo.InvariantCulture),
                        AskPrice = decimal.Parse(ticker.Ask1Price, CultureInfo.InvariantCulture),
                        BidQuantity = decimal.Parse(ticker.Bid1Size, CultureInfo.InvariantCulture),
                        AskQuantity = decimal.Parse(ticker.Ask1Size, CultureInfo.InvariantCulture),
                        LastPrice = decimal.Parse(ticker.LastPrice, CultureInfo.InvariantCulture),
                        Volume24h = decimal.Parse(ticker.Volume24h, CultureInfo.InvariantCulture),
                        Timestamp = DateTime.UtcNow
                    };
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"GetTickersAsync failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Get ALL tickers from Bybit in one API call
    /// Endpoint: GET /v5/market/tickers?category=spot
    /// </summary>
    public override async Task<Dictionary<string, Ticker>> GetAllTickersAsync(
        string? quoteAsset = null,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, Ticker>();

        try
        {
            _logger.LogInfo(ExchangeName, "GetAllTickersAsync: Fetching all tickers...");

            var response = await GetAsync<BybitResponse<BybitTickerResult>>(
                $"{TickerEndpoint}?category=spot",
                cancellationToken);

            if (response?.Result?.List == null || response.Result.List.Count == 0)
            {
                _logger.LogWarning(ExchangeName, "GetAllTickersAsync: No data returned from API");
                return result;
            }

            _logger.LogInfo(ExchangeName, $"GetAllTickersAsync: Got {response.Result.List.Count} tickers from API");

            foreach (var data in response.Result.List)
            {
                var symbol = data.Symbol;

                // Filter by quote asset if specified (e.g., "USDT")
                // Bybit format: BASEUSDT (e.g., BTCUSDT)
                if (!string.IsNullOrEmpty(quoteAsset))
                {
                    if (!symbol.EndsWith(quoteAsset, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                // Skip pairs with zero volume (inactive)
                var volume = decimal.TryParse(data.Volume24h, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
                var lastPrice = decimal.TryParse(data.LastPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0;

                if (volume <= 0 && lastPrice <= 0)
                    continue;

                result[symbol] = new Ticker
                {
                    Symbol = symbol,
                    Exchange = ExchangeName,
                    BidPrice = decimal.TryParse(data.Bid1Price, NumberStyles.Any, CultureInfo.InvariantCulture, out var bid) ? bid : 0,
                    AskPrice = decimal.TryParse(data.Ask1Price, NumberStyles.Any, CultureInfo.InvariantCulture, out var ask) ? ask : 0,
                    BidQuantity = decimal.TryParse(data.Bid1Size, NumberStyles.Any, CultureInfo.InvariantCulture, out var bidSz) ? bidSz : 0,
                    AskQuantity = decimal.TryParse(data.Ask1Size, NumberStyles.Any, CultureInfo.InvariantCulture, out var askSz) ? askSz : 0,
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

    #region API Permissions

    /// <summary>
    /// Get API key permissions from Bybit
    /// Bybit /v5/user/query-api endpoint returns API key info
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
            var queryString = "";
            var headers = CreateAuthHeaders(timestamp, queryString, "");

            using var request = new HttpRequestMessage(HttpMethod.Get, "/v5/user/query-api");
            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<BybitResponse<BybitApiKeyInfo>>(_jsonOptions, cancellationToken);
                if (result?.Result != null)
                {
                    permissions.CanRead = true;
                    // Bybit permissions object
                    var perms = result.Result.Permissions;
                    if (perms != null)
                    {
                        permissions.CanTrade = perms.Spot?.Contains("SpotTrade") ?? false;
                        permissions.CanWithdraw = perms.Wallet?.Contains("Withdraw") ?? false;
                        permissions.CanDeposit = perms.Wallet?.Contains("AccountTransfer") ?? false;
                    }
                    permissions.IpRestriction = result.Result.IpRestrictions;
                }
            }
            else
            {
                // Fallback: try get balance
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
                permissions.AdditionalInfo = "กรุณาตรวจสอบสิทธิ์ที่ bybit.com";
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
            // Bybit uses BTCUSDT format (no separator)
            var normalizedSymbol = symbol.Replace("/", "").Replace("-", "").ToUpperInvariant();

            // Bybit V5 uses: 1, 3, 5, 15, 30, 60, 120, 240, 360, 720, D, W, M
            var bybitInterval = interval switch
            {
                "1m" => "1",
                "3m" => "3",
                "5m" => "5",
                "15m" => "15",
                "30m" => "30",
                "1h" => "60",
                "4h" => "240",
                "1d" => "D",
                _ => "1"
            };

            var clampedLimit = Math.Min(limit, 1000); // Bybit max 1000

            var response = await GetAsync<BybitResponse<BybitKlineResult>>(
                $"/v5/market/kline?category=spot&symbol={normalizedSymbol}&interval={bybitInterval}&limit={clampedLimit}",
                cancellationToken);

            if (response?.Result?.List == null || response.Result.List.Count == 0) return candles;

            foreach (var kline in response.Result.List)
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

            // Bybit returns newest first, reverse to chronological order
            candles.Reverse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"GetKlinesAsync error for {symbol}: {ex.Message}");
        }
        return candles;
    }

    #region Account Data

    public override async Task<AccountBalance> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        if (!HasCredentials())
        {
            _logger.LogWarning(ExchangeName, "API credentials not configured");
            throw new Exception("API credentials not configured");
        }

        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var queryString = "accountType=UNIFIED";

            var headers = CreateAuthHeaders(timestamp, queryString, "");

            var request = new HttpRequestMessage(HttpMethod.Get, $"{AccountEndpoint}?{queryString}");
            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
            httpResponse.EnsureSuccessStatusCode();

            var response = await httpResponse.Content.ReadFromJsonAsync<BybitResponse<BybitWalletResult>>(_jsonOptions, cancellationToken);

            if (response?.Result?.List == null || response.Result.List.Count == 0)
            {
                throw new Exception("Failed to get account balance");
            }

            var balance = new AccountBalance
            {
                Exchange = ExchangeName,
                Timestamp = DateTime.UtcNow,
                Assets = new Dictionary<string, AssetBalance>()
            };

            var wallet = response.Result.List[0];
            foreach (var coin in wallet.Coin)
            {
                var available = decimal.Parse(coin.AvailableToWithdraw, CultureInfo.InvariantCulture);
                var locked = decimal.Parse(coin.Locked, CultureInfo.InvariantCulture);
                var total = decimal.Parse(coin.WalletBalance, CultureInfo.InvariantCulture);

                if (total > 0)
                {
                    balance.Assets[coin.CoinName] = new AssetBalance
                    {
                        Asset = coin.CoinName,
                        Available = available,
                        Total = total
                    };
                }
            }

            return balance;
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"GetBalanceAsync failed: {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Order Management

    public override async Task<Order> PlaceOrderAsync(OrderRequest request, CancellationToken cancellationToken = default)
    {
        if (!HasCredentials())
        {
            throw new Exception("API credentials not configured");
        }

        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var orderRequest = new BybitOrderRequest
            {
                Category = "spot",
                Symbol = request.Symbol,
                Side = request.Side == OrderSide.Buy ? "Buy" : "Sell",
                OrderType = request.Type == OrderType.Market ? "Market" : "Limit",
                Qty = request.Quantity.ToString("G29")
            };

            if (!string.IsNullOrEmpty(request.ClientOrderId))
            {
                orderRequest.OrderLinkId = request.ClientOrderId;
            }

            if (request.Type == OrderType.Limit && request.Price.HasValue)
            {
                orderRequest.Price = request.Price.Value.ToString("G29");
                orderRequest.TimeInForce = "GTC";
            }

            var body = JsonSerializer.Serialize(orderRequest, _jsonOptions);
            var headers = CreateAuthHeaders(timestamp, "", body);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, OrderEndpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            foreach (var header in headers)
            {
                httpRequest.Headers.Add(header.Key, header.Value);
            }

            var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            httpResponse.EnsureSuccessStatusCode();

            var response = JsonSerializer.Deserialize<BybitResponse<BybitOrderResult>>(responseContent, _jsonOptions);

            if (response?.RetCode != 0)
            {
                throw new Exception($"Bybit order failed (code {response?.RetCode}): {response?.RetMsg}. Response: {responseContent}");
            }

            if (response?.Result == null)
            {
                throw new Exception($"Failed to place order: {responseContent}");
            }

            return new Order
            {
                OrderId = response.Result.OrderId,
                ClientOrderId = response.Result.OrderLinkId,
                Exchange = ExchangeName,
                Symbol = request.Symbol,
                Side = request.Side,
                Type = request.Type,
                Status = OrderStatus.Open,
                RequestedQuantity = request.Quantity,
                RequestedPrice = request.Price,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"PlaceOrderAsync failed: {ex.Message}");
            throw;
        }
    }

    public override async Task<Order> CancelOrderAsync(string symbol, string orderId, CancellationToken cancellationToken = default)
    {
        if (!HasCredentials())
        {
            throw new Exception("API credentials not configured");
        }

        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var cancelRequest = new
            {
                category = "spot",
                symbol,
                orderId
            };

            var body = JsonSerializer.Serialize(cancelRequest, _jsonOptions);
            var headers = CreateAuthHeaders(timestamp, "", body);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, CancelOrderEndpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            foreach (var header in headers)
            {
                httpRequest.Headers.Add(header.Key, header.Value);
            }

            var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var cancelContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            httpResponse.EnsureSuccessStatusCode();

            var response = JsonSerializer.Deserialize<BybitResponse<BybitOrderResult>>(cancelContent, _jsonOptions);

            if (response?.RetCode != 0)
            {
                throw new Exception($"Bybit cancel failed (code {response?.RetCode}): {response?.RetMsg}");
            }

            return new Order
            {
                OrderId = orderId,
                Symbol = symbol,
                Exchange = ExchangeName,
                Status = OrderStatus.Cancelled,
                UpdatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"CancelOrderAsync failed: {ex.Message}");
            throw;
        }
    }

    public override async Task<Order> GetOrderAsync(string symbol, string orderId, CancellationToken cancellationToken = default)
    {
        if (!HasCredentials())
        {
            throw new Exception("API credentials not configured");
        }

        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var queryString = $"category=spot&symbol={symbol}&orderId={orderId}";

            var headers = CreateAuthHeaders(timestamp, queryString, "");

            var request = new HttpRequestMessage(HttpMethod.Get, $"{OrderInfoEndpoint}?{queryString}");
            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
            httpResponse.EnsureSuccessStatusCode();

            var response = await httpResponse.Content.ReadFromJsonAsync<BybitResponse<BybitOrderListResult>>(_jsonOptions, cancellationToken);

            if (response?.Result?.List == null || response.Result.List.Count == 0)
            {
                throw new Exception($"Order {orderId} not found");
            }

            var order = response.Result.List[0];
            return MapBybitOrder(order);
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"GetOrderAsync failed: {ex.Message}");
            throw;
        }
    }

    public override async Task<List<Order>> GetOpenOrdersAsync(string? symbol = null, CancellationToken cancellationToken = default)
    {
        if (!HasCredentials())
        {
            throw new Exception("API credentials not configured");
        }

        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var queryString = "category=spot&openOnly=1";
            if (!string.IsNullOrEmpty(symbol))
            {
                queryString += $"&symbol={symbol}";
            }

            var headers = CreateAuthHeaders(timestamp, queryString, "");

            var request = new HttpRequestMessage(HttpMethod.Get, $"{OrderInfoEndpoint}?{queryString}");
            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
            httpResponse.EnsureSuccessStatusCode();

            var response = await httpResponse.Content.ReadFromJsonAsync<BybitResponse<BybitOrderListResult>>(_jsonOptions, cancellationToken);

            return response?.Result?.List?.Select(MapBybitOrder).ToList() ?? new List<Order>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"GetOpenOrdersAsync failed: {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Connection

    public override async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await GetAsync<BybitResponse<BybitTimeResult>>(ServerTimeEndpoint, cancellationToken);
            if (response?.RetCode == 0)
            {
                _logger.LogInfo(ExchangeName, $"Connection test successful. Server time: {response.Result?.TimeSecond}");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ExchangeName, $"Connection test failed: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Helper Methods

    private Dictionary<string, string> CreateAuthHeaders(long timestamp, string queryString, string body)
    {
        var apiKey = GetApiKey() ?? "";
        var signPayload = $"{timestamp}{apiKey}{RecvWindow}{queryString}{body}";
        var signature = SignRequest(signPayload);

        return new Dictionary<string, string>
        {
            ["X-BAPI-API-KEY"] = apiKey,
            ["X-BAPI-SIGN"] = signature,
            ["X-BAPI-SIGN-TYPE"] = "2",
            ["X-BAPI-TIMESTAMP"] = timestamp.ToString(),
            ["X-BAPI-RECV-WINDOW"] = RecvWindow.ToString()
        };
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

    private Order MapBybitOrder(BybitOrderInfo order)
    {
        return new Order
        {
            OrderId = order.OrderId,
            ClientOrderId = order.OrderLinkId,
            Exchange = ExchangeName,
            Symbol = order.Symbol,
            Side = order.Side.ToUpperInvariant() == "BUY" ? OrderSide.Buy : OrderSide.Sell,
            Type = order.OrderType.ToUpperInvariant() == "MARKET" ? OrderType.Market : OrderType.Limit,
            Status = MapOrderStatus(order.OrderStatus),
            RequestedQuantity = decimal.Parse(order.Qty, CultureInfo.InvariantCulture),
            FilledQuantity = decimal.Parse(order.CumExecQty, CultureInfo.InvariantCulture),
            RequestedPrice = string.IsNullOrEmpty(order.Price) ? null : decimal.Parse(order.Price, CultureInfo.InvariantCulture),
            AverageFilledPrice = string.IsNullOrEmpty(order.AvgPrice) || order.AvgPrice == "0" ? null : decimal.Parse(order.AvgPrice, CultureInfo.InvariantCulture),
            Fee = string.IsNullOrEmpty(order.CumExecFee) ? 0 : decimal.Parse(order.CumExecFee, CultureInfo.InvariantCulture),
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(order.CreatedTime, CultureInfo.InvariantCulture)).UtcDateTime,
            UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(order.UpdatedTime, CultureInfo.InvariantCulture)).UtcDateTime
        };
    }

    private OrderStatus MapOrderStatus(string status)
    {
        return status.ToUpperInvariant() switch
        {
            "NEW" or "CREATED" => OrderStatus.Open,
            "PARTIALLYFILLED" or "PARTIALLY_FILLED" => OrderStatus.PartiallyFilled,
            "FILLED" => OrderStatus.Filled,
            "CANCELLED" or "CANCELED" => OrderStatus.Cancelled,
            "REJECTED" => OrderStatus.Rejected,
            "EXPIRED" => OrderStatus.Expired,
            _ => OrderStatus.Pending
        };
    }

    #endregion
}

#region Bybit API Response Models

internal class BybitResponse<T>
{
    [JsonPropertyName("retCode")]
    public int RetCode { get; set; }

    [JsonPropertyName("retMsg")]
    public string RetMsg { get; set; } = "";

    [JsonPropertyName("result")]
    public T? Result { get; set; }

    [JsonPropertyName("time")]
    public long Time { get; set; }
}

internal class BybitTickerResult
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("list")]
    public List<BybitTickerInfo> List { get; set; } = new();
}

internal class BybitTickerInfo
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = "";

    [JsonPropertyName("bid1Price")]
    public string Bid1Price { get; set; } = "0";

    [JsonPropertyName("bid1Size")]
    public string Bid1Size { get; set; } = "0";

    [JsonPropertyName("ask1Price")]
    public string Ask1Price { get; set; } = "0";

    [JsonPropertyName("ask1Size")]
    public string Ask1Size { get; set; } = "0";

    [JsonPropertyName("lastPrice")]
    public string LastPrice { get; set; } = "0";

    [JsonPropertyName("volume24h")]
    public string Volume24h { get; set; } = "0";
}

internal class BybitOrderBookResult
{
    [JsonPropertyName("s")]
    public string Symbol { get; set; } = "";

    [JsonPropertyName("b")]
    public List<string[]> B { get; set; } = new(); // Bids

    [JsonPropertyName("a")]
    public List<string[]> A { get; set; } = new(); // Asks

    [JsonPropertyName("ts")]
    public long Ts { get; set; }

    [JsonPropertyName("u")]
    public long U { get; set; }
}

internal class BybitWalletResult
{
    [JsonPropertyName("list")]
    public List<BybitWalletInfo> List { get; set; } = new();
}

internal class BybitWalletInfo
{
    [JsonPropertyName("accountType")]
    public string AccountType { get; set; } = "";

    [JsonPropertyName("coin")]
    public List<BybitCoinInfo> Coin { get; set; } = new();
}

internal class BybitCoinInfo
{
    [JsonPropertyName("coin")]
    public string CoinName { get; set; } = "";

    [JsonPropertyName("walletBalance")]
    public string WalletBalance { get; set; } = "0";

    [JsonPropertyName("availableToWithdraw")]
    public string AvailableToWithdraw { get; set; } = "0";

    [JsonPropertyName("locked")]
    public string Locked { get; set; } = "0";
}

internal class BybitOrderRequest
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = "spot";

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = "";

    [JsonPropertyName("side")]
    public string Side { get; set; } = "";

    [JsonPropertyName("orderType")]
    public string OrderType { get; set; } = "";

    [JsonPropertyName("qty")]
    public string Qty { get; set; } = "";

    [JsonPropertyName("price")]
    public string? Price { get; set; }

    [JsonPropertyName("timeInForce")]
    public string? TimeInForce { get; set; }

    [JsonPropertyName("orderLinkId")]
    public string? OrderLinkId { get; set; }
}

internal class BybitOrderResult
{
    [JsonPropertyName("orderId")]
    public string OrderId { get; set; } = "";

    [JsonPropertyName("orderLinkId")]
    public string OrderLinkId { get; set; } = "";
}

internal class BybitOrderListResult
{
    [JsonPropertyName("list")]
    public List<BybitOrderInfo> List { get; set; } = new();
}

internal class BybitOrderInfo
{
    [JsonPropertyName("orderId")]
    public string OrderId { get; set; } = "";

    [JsonPropertyName("orderLinkId")]
    public string OrderLinkId { get; set; } = "";

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = "";

    [JsonPropertyName("side")]
    public string Side { get; set; } = "";

    [JsonPropertyName("orderType")]
    public string OrderType { get; set; } = "";

    [JsonPropertyName("price")]
    public string Price { get; set; } = "";

    [JsonPropertyName("qty")]
    public string Qty { get; set; } = "0";

    [JsonPropertyName("cumExecQty")]
    public string CumExecQty { get; set; } = "0";

    [JsonPropertyName("cumExecFee")]
    public string CumExecFee { get; set; } = "0";

    [JsonPropertyName("avgPrice")]
    public string AvgPrice { get; set; } = "";

    [JsonPropertyName("orderStatus")]
    public string OrderStatus { get; set; } = "";

    [JsonPropertyName("createdTime")]
    public string CreatedTime { get; set; } = "0";

    [JsonPropertyName("updatedTime")]
    public string UpdatedTime { get; set; } = "0";
}

internal class BybitKlineResult
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = "";

    [JsonPropertyName("list")]
    public List<List<string>> List { get; set; } = new();
}

internal class BybitTimeResult
{
    [JsonPropertyName("timeSecond")]
    public string TimeSecond { get; set; } = "";

    [JsonPropertyName("timeNano")]
    public string TimeNano { get; set; } = "";
}

internal class BybitApiKeyInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("readOnly")]
    public int ReadOnly { get; set; }

    [JsonPropertyName("ips")]
    public string? IpRestrictions { get; set; }

    [JsonPropertyName("permissions")]
    public BybitPermissions? Permissions { get; set; }
}

internal class BybitPermissions
{
    [JsonPropertyName("ContractTrade")]
    public List<string>? ContractTrade { get; set; }

    [JsonPropertyName("Spot")]
    public List<string>? Spot { get; set; }

    [JsonPropertyName("Wallet")]
    public List<string>? Wallet { get; set; }

    [JsonPropertyName("Options")]
    public List<string>? Options { get; set; }

    [JsonPropertyName("Exchange")]
    public List<string>? Exchange { get; set; }
}

#endregion
