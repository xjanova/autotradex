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
                BidPrice = decimal.Parse(ticker.Bid1Price),
                AskPrice = decimal.Parse(ticker.Ask1Price),
                BidQuantity = decimal.Parse(ticker.Bid1Size),
                AskQuantity = decimal.Parse(ticker.Ask1Size),
                LastPrice = decimal.Parse(ticker.LastPrice),
                Volume24h = decimal.Parse(ticker.Volume24h),
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
                    decimal.TryParse(bid[0], out var price) &&
                    decimal.TryParse(bid[1], out var quantity))
                {
                    orderBook.Bids.Add(new OrderBookEntry(price, quantity));
                }
            }

            // Parse asks
            foreach (var ask in response.Result.A)
            {
                if (ask.Length >= 2 &&
                    decimal.TryParse(ask[0], out var price) &&
                    decimal.TryParse(ask[1], out var quantity))
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
                        BidPrice = decimal.Parse(ticker.Bid1Price),
                        AskPrice = decimal.Parse(ticker.Ask1Price),
                        BidQuantity = decimal.Parse(ticker.Bid1Size),
                        AskQuantity = decimal.Parse(ticker.Ask1Size),
                        LastPrice = decimal.Parse(ticker.LastPrice),
                        Volume24h = decimal.Parse(ticker.Volume24h),
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

    #endregion

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
                var available = decimal.Parse(coin.AvailableToWithdraw);
                var locked = decimal.Parse(coin.Locked);
                var total = decimal.Parse(coin.WalletBalance);

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
            httpResponse.EnsureSuccessStatusCode();

            var response = await httpResponse.Content.ReadFromJsonAsync<BybitResponse<BybitOrderResult>>(_jsonOptions, cancellationToken);

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
            RequestedQuantity = decimal.Parse(order.Qty),
            FilledQuantity = decimal.Parse(order.CumExecQty),
            RequestedPrice = string.IsNullOrEmpty(order.Price) ? null : decimal.Parse(order.Price),
            AverageFilledPrice = string.IsNullOrEmpty(order.AvgPrice) || order.AvgPrice == "0" ? null : decimal.Parse(order.AvgPrice),
            Fee = string.IsNullOrEmpty(order.CumExecFee) ? 0 : decimal.Parse(order.CumExecFee),
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(order.CreatedTime)).UtcDateTime,
            UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(order.UpdatedTime)).UtcDateTime
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

internal class BybitTimeResult
{
    [JsonPropertyName("timeSecond")]
    public string TimeSecond { get; set; } = "";

    [JsonPropertyName("timeNano")]
    public string TimeNano { get; set; } = "";
}

#endregion
