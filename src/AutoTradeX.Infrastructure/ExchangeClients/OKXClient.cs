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
        // OKX requires additional passphrase
        _passphrase = Environment.GetEnvironmentVariable("AUTOTRADEX_OKX_PASSPHRASE");
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
                BidPrice = decimal.Parse(data.BidPx ?? "0"),
                AskPrice = decimal.Parse(data.AskPx ?? "0"),
                BidQuantity = decimal.Parse(data.BidSz ?? "0"),
                AskQuantity = decimal.Parse(data.AskSz ?? "0"),
                LastPrice = decimal.Parse(data.Last ?? "0"),
                Volume24h = decimal.Parse(data.Vol24h ?? "0"),
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
                        decimal.Parse(bid[0]),
                        decimal.Parse(bid[1])
                    ));
                }
            }

            foreach (var ask in data.Asks ?? Array.Empty<string[]>())
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
                var available = decimal.Parse(detail.AvailBal ?? "0");
                var frozen = decimal.Parse(detail.FrozenBal ?? "0");

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
                OrderId = orderResult.OrdId,
                ClientOrderId = orderResult.ClOrdId,
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
                OrderId = data.OrdId,
                ClientOrderId = data.ClOrdId,
                Exchange = ExchangeName,
                Symbol = symbol,
                Side = data.Side == "buy" ? OrderSide.Buy : OrderSide.Sell,
                Type = data.OrdType == "market" ? OrderType.Market : OrderType.Limit,
                Status = MapOrderStatus(data.State),
                RequestedQuantity = decimal.Parse(data.Sz ?? "0"),
                FilledQuantity = decimal.Parse(data.AccFillSz ?? "0"),
                RequestedPrice = !string.IsNullOrEmpty(data.Px) ? decimal.Parse(data.Px) : null,
                AverageFilledPrice = !string.IsNullOrEmpty(data.AvgPx) ? decimal.Parse(data.AvgPx) : 0,
                Fee = decimal.Parse(data.Fee ?? "0"),
                FeeCurrency = data.FeeCcy ?? "USDT",
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(data.CTime ?? "0")).UtcDateTime,
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
                OrderId = data.OrdId,
                ClientOrderId = data.ClOrdId,
                Exchange = ExchangeName,
                Symbol = data.InstId,
                Side = data.Side == "buy" ? OrderSide.Buy : OrderSide.Sell,
                Type = data.OrdType == "market" ? OrderType.Market : OrderType.Limit,
                Status = MapOrderStatus(data.State),
                RequestedQuantity = decimal.Parse(data.Sz ?? "0"),
                FilledQuantity = decimal.Parse(data.AccFillSz ?? "0"),
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(data.CTime ?? "0")).UtcDateTime
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

#region OKX API Response Models

internal class OKXResponse<T>
{
    public string Code { get; set; } = "";
    public string? Msg { get; set; }
    public T? Data { get; set; }
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

#endregion
