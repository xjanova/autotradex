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
                BidPrice = decimal.Parse(response.BidPrice),
                AskPrice = decimal.Parse(response.AskPrice),
                BidQuantity = decimal.Parse(response.BidQty),
                AskQuantity = decimal.Parse(response.AskQty),
                LastPrice = stats24h != null ? decimal.Parse(stats24h.LastPrice) : decimal.Parse(response.BidPrice),
                Volume24h = stats24h != null ? decimal.Parse(stats24h.Volume) : 0,
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
                        decimal.Parse(bid[0]),
                        decimal.Parse(bid[1])
                    ));
                }
            }

            foreach (var ask in response.Asks)
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
                var free = decimal.Parse(asset.Free);
                var locked = decimal.Parse(asset.Locked);

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
                FilledQuantity = decimal.Parse(response.ExecutedQty),
                RequestedPrice = request.Price,
                AverageFilledPrice = !string.IsNullOrEmpty(response.AvgPrice) && decimal.Parse(response.AvgPrice) > 0
                    ? decimal.Parse(response.AvgPrice)
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
                RequestedQuantity = decimal.Parse(response.OrigQty),
                FilledQuantity = decimal.Parse(response.ExecutedQty),
                RequestedPrice = !string.IsNullOrEmpty(response.Price) ? decimal.Parse(response.Price) : null,
                AverageFilledPrice = !string.IsNullOrEmpty(response.AvgPrice) ? decimal.Parse(response.AvgPrice) : 0,
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
                RequestedQuantity = decimal.Parse(r.OrigQty),
                FilledQuantity = decimal.Parse(r.ExecutedQty),
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
        var response = await _httpClient.PostAsync(endpoint, null, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
    }

    private async Task<T?> DeleteAsync<T>(string endpoint, CancellationToken cancellationToken)
    {
        var response = await _httpClient.DeleteAsync(endpoint, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
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
            return response.Fills.Sum(f => decimal.Parse(f.Commission));
        }
        // Estimate fee: 0.1% for maker/taker
        var qty = decimal.Parse(response.ExecutedQty);
        var price = !string.IsNullOrEmpty(response.AvgPrice) && decimal.Parse(response.AvgPrice) > 0
            ? decimal.Parse(response.AvgPrice)
            : !string.IsNullOrEmpty(response.Price) ? decimal.Parse(response.Price) : 0;
        return qty * price * 0.001m;
    }

    #endregion
}

#region Binance API Response Models

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
