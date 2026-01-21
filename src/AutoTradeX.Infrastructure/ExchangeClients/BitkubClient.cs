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

    #endregion

    #region Account Data (Private APIs)

    public override async Task<AccountBalance> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!HasCredentials())
            {
                throw new InvalidOperationException($"{ExchangeName}: API credentials not configured. Please configure API keys in Settings.");
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var requestBody = new Dictionary<string, object>
            {
                ["ts"] = timestamp
            };

            var body = JsonSerializer.Serialize(requestBody, _jsonOptions);
            var signature = SignRequest(body);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v3/market/wallet")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("X-BTK-APIKEY", GetApiKey());
            request.Headers.Add("X-BTK-SIGN", signature);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<BitkubWalletResponse>(_jsonOptions, cancellationToken);

            if (result?.Error != 0)
            {
                throw new Exception($"Failed to get balance: Error {result?.Error}");
            }

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
            var endpoint = request.Side == OrderSide.Buy
                ? "/api/v3/market/place-bid"
                : "/api/v3/market/place-ask";

            var orderData = new Dictionary<string, object>
            {
                ["sym"] = normalizedSymbol,
                ["amt"] = request.Quantity,
                ["rat"] = request.Type == OrderType.Market ? 0 : (request.Price ?? 0),
                ["typ"] = request.Type == OrderType.Market ? "market" : "limit",
                ["ts"] = timestamp
            };

            if (!string.IsNullOrEmpty(request.ClientOrderId))
            {
                orderData["client_id"] = request.ClientOrderId;
            }

            var body = JsonSerializer.Serialize(orderData, _jsonOptions);
            var signature = SignRequest(body);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            httpRequest.Headers.Add("X-BTK-APIKEY", GetApiKey());
            httpRequest.Headers.Add("X-BTK-SIGN", signature);

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

            var cancelData = new Dictionary<string, object>
            {
                ["sym"] = normalizedSymbol,
                ["id"] = orderId,
                ["sd"] = "buy", // We'll need to know the side to cancel
                ["ts"] = timestamp
            };

            var body = JsonSerializer.Serialize(cancelData, _jsonOptions);
            var signature = SignRequest(body);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v3/market/cancel-order")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("X-BTK-APIKEY", GetApiKey());
            request.Headers.Add("X-BTK-SIGN", signature);

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
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var requestData = new Dictionary<string, object>
            {
                ["sym"] = normalizedSymbol,
                ["id"] = orderId,
                ["ts"] = timestamp
            };

            var body = JsonSerializer.Serialize(requestData, _jsonOptions);
            var signature = SignRequest(body);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v3/market/order-info")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("X-BTK-APIKEY", GetApiKey());
            request.Headers.Add("X-BTK-SIGN", signature);

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
            var requestData = new Dictionary<string, object>
            {
                ["ts"] = timestamp
            };

            if (symbol != null)
            {
                requestData["sym"] = NormalizeSymbol(symbol);
            }

            var body = JsonSerializer.Serialize(requestData, _jsonOptions);
            var signature = SignRequest(body);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v3/market/my-open-orders")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("X-BTK-APIKEY", GetApiKey());
            request.Headers.Add("X-BTK-SIGN", signature);

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

#endregion
