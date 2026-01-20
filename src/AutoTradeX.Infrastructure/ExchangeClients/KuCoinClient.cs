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
                BidPrice = decimal.Parse(data.BestBid ?? "0"),
                AskPrice = decimal.Parse(data.BestAsk ?? "0"),
                BidQuantity = decimal.Parse(data.BestBidSize ?? "0"),
                AskQuantity = decimal.Parse(data.BestAskSize ?? "0"),
                LastPrice = decimal.Parse(data.Price ?? "0"),
                Volume24h = stats?.Data != null ? decimal.Parse(stats.Data.Vol ?? "0") : 0,
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
                        decimal.Parse(bid[0]),
                        decimal.Parse(bid[1])
                    ));
                }
            }

            foreach (var ask in response.Data.Asks ?? Array.Empty<string[]>())
            {
                if (ask.Length >= 2)
                {
                    orderBook.Asks.Add(new OrderBookEntry(
                        decimal.Parse(ask[0]),
                        decimal.Parse(ask[1])
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

    #endregion

    #region Account Data (Private APIs)

    public override async Task<AccountBalance> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!HasCredentials())
            {
                _logger.LogWarning(ExchangeName, "API credentials not configured. Using demo balance.");
                return GetDemoBalance();
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
                var available = group.Sum(a => decimal.Parse(a.Available ?? "0"));
                var holds = group.Sum(a => decimal.Parse(a.Holds ?? "0"));

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

            if (result?.Data == null)
            {
                throw new Exception("Failed to place order");
            }

            return new Order
            {
                OrderId = result.Data.OrderId,
                ClientOrderId = orderData["clientOid"].ToString(),
                Exchange = ExchangeName,
                Symbol = request.Symbol,
                Side = request.Side,
                Type = request.Type,
                Status = OrderStatus.Pending, // KuCoin returns only order ID, need to query for status
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
                OrderId = data.Id,
                ClientOrderId = data.ClientOid,
                Exchange = ExchangeName,
                Symbol = symbol,
                Side = data.Side == "buy" ? OrderSide.Buy : OrderSide.Sell,
                Type = data.Type == "market" ? OrderType.Market : OrderType.Limit,
                Status = MapOrderStatus(data),
                RequestedQuantity = decimal.Parse(data.Size ?? "0"),
                FilledQuantity = decimal.Parse(data.DealSize ?? "0"),
                RequestedPrice = !string.IsNullOrEmpty(data.Price) ? decimal.Parse(data.Price) : null,
                AverageFilledPrice = !string.IsNullOrEmpty(data.DealFunds) && decimal.Parse(data.DealSize ?? "0") > 0
                    ? decimal.Parse(data.DealFunds) / decimal.Parse(data.DealSize)
                    : 0,
                Fee = decimal.Parse(data.Fee ?? "0"),
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
                OrderId = data.Id,
                ClientOrderId = data.ClientOid,
                Exchange = ExchangeName,
                Symbol = data.Symbol,
                Side = data.Side == "buy" ? OrderSide.Buy : OrderSide.Sell,
                Type = data.Type == "market" ? OrderType.Market : OrderType.Limit,
                Status = MapOrderStatus(data),
                RequestedQuantity = decimal.Parse(data.Size ?? "0"),
                FilledQuantity = decimal.Parse(data.DealSize ?? "0"),
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
            return decimal.Parse(order.DealSize ?? "0") > 0
                ? OrderStatus.PartiallyFilled
                : OrderStatus.Pending;
        }

        if (order.CancelExist)
        {
            return OrderStatus.Cancelled;
        }

        return decimal.Parse(order.DealSize ?? "0") >= decimal.Parse(order.Size ?? "0")
            ? OrderStatus.Filled
            : OrderStatus.Error;
    }

    private AccountBalance GetDemoBalance()
    {
        return new AccountBalance
        {
            Exchange = ExchangeName,
            Timestamp = DateTime.UtcNow,
            Assets = new Dictionary<string, AssetBalance>
            {
                ["USDT"] = new AssetBalance { Asset = "USDT", Total = 5000m, Available = 5000m },
                ["BTC"] = new AssetBalance { Asset = "BTC", Total = 0.1m, Available = 0.1m },
                ["ETH"] = new AssetBalance { Asset = "ETH", Total = 2m, Available = 2m }
            }
        };
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

#endregion
