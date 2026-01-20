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
                BidPrice = decimal.Parse(data.HighestBid ?? "0"),
                AskPrice = decimal.Parse(data.LowestAsk ?? "0"),
                BidQuantity = 0, // Gate.io ticker doesn't include quantities
                AskQuantity = 0,
                LastPrice = decimal.Parse(data.Last ?? "0"),
                Volume24h = decimal.Parse(data.BaseVolume ?? "0"),
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
                        decimal.Parse(bid[0]),
                        decimal.Parse(bid[1])
                    ));
                }
            }

            foreach (var ask in response.Asks ?? Array.Empty<string[]>())
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
                var available = decimal.Parse(asset.Available ?? "0");
                var locked = decimal.Parse(asset.Locked ?? "0");

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
                OrderId = result.Id,
                ClientOrderId = result.Text,
                Exchange = ExchangeName,
                Symbol = request.Symbol,
                Side = request.Side,
                Type = request.Type,
                Status = MapOrderStatus(result.Status),
                RequestedQuantity = request.Quantity,
                FilledQuantity = decimal.Parse(result.FilledTotal ?? "0"),
                RequestedPrice = request.Price,
                AverageFilledPrice = !string.IsNullOrEmpty(result.AvgDealPrice) ? decimal.Parse(result.AvgDealPrice) : 0,
                Fee = decimal.Parse(result.Fee ?? "0"),
                FeeCurrency = result.FeeCurrency ?? "USDT",
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(result.CreateTime ?? "0")).UtcDateTime,
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
                OrderId = result.Id,
                ClientOrderId = result.Text,
                Exchange = ExchangeName,
                Symbol = symbol,
                Side = result.Side == "buy" ? OrderSide.Buy : OrderSide.Sell,
                Type = result.Type == "market" ? OrderType.Market : OrderType.Limit,
                Status = MapOrderStatus(result.Status),
                RequestedQuantity = decimal.Parse(result.Amount ?? "0"),
                FilledQuantity = decimal.Parse(result.FilledTotal ?? "0"),
                RequestedPrice = !string.IsNullOrEmpty(result.Price) ? decimal.Parse(result.Price) : null,
                AverageFilledPrice = !string.IsNullOrEmpty(result.AvgDealPrice) ? decimal.Parse(result.AvgDealPrice) : 0,
                Fee = decimal.Parse(result.Fee ?? "0"),
                FeeCurrency = result.FeeCurrency ?? "USDT",
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(result.CreateTime ?? "0")).UtcDateTime,
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
                OrderId = data.Id,
                ClientOrderId = data.Text,
                Exchange = ExchangeName,
                Symbol = data.CurrencyPair?.Replace("_", "/") ?? "",
                Side = data.Side == "buy" ? OrderSide.Buy : OrderSide.Sell,
                Type = data.Type == "market" ? OrderType.Market : OrderType.Limit,
                Status = MapOrderStatus(data.Status),
                RequestedQuantity = decimal.Parse(data.Amount ?? "0"),
                FilledQuantity = decimal.Parse(data.FilledTotal ?? "0"),
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(data.CreateTime ?? "0")).UtcDateTime
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

#region Gate.io API Response Models

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
