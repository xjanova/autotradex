/*
 * ============================================================================
 * AutoTrade-X - Cross-Exchange Arbitrage Trading Bot
 * ============================================================================
 *
 * ⚠️ คำเตือน: นี่คือ Simulation Client สำหรับทดสอบเท่านั้น
 * ไม่ได้เชื่อมต่อกับ Exchange จริง
 *
 * ============================================================================
 */

using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using System.Collections.Concurrent;

namespace AutoTradeX.Infrastructure.ExchangeClients;

/// <summary>
/// SimulationExchangeClient - Client จำลองสำหรับทดสอบ
///
/// ไม่ยิงไป API จริง แต่จำลอง:
/// - ราคาที่ผันผวนรอบค่าเริ่มต้น
/// - Order book ที่สร้างขึ้น
/// - Balance ที่กำหนดไว้
/// - Order execution ที่มี delay จำลอง
///
/// ใช้สำหรับ:
/// - ทดสอบ logic ของ ArbEngine
/// - Paper trading
/// - Unit testing
/// </summary>
public class SimulationExchangeClient : IExchangeClient
{
    private readonly ILoggingService _logger;
    private readonly string _exchangeName;
    private readonly Random _random = new();

    // ราคาฐานสำหรับแต่ละ symbol
    private readonly ConcurrentDictionary<string, decimal> _basePrices = new();

    // Balance จำลอง
    private readonly ConcurrentDictionary<string, AssetBalance> _balances = new();

    // Orders ที่ยังเปิดอยู่
    private readonly ConcurrentDictionary<string, Order> _openOrders = new();

    // ประวัติ Orders
    private readonly ConcurrentQueue<Order> _orderHistory = new();

    // ค่า config สำหรับ simulation
    private decimal _feePercent = 0.1m;
    private decimal _priceVolatility = 0.001m; // 0.1% volatility
    private int _fillDelayMs = 100; // delay ก่อน order fill

    public string ExchangeName => _exchangeName;
    public bool IsConnected { get; private set; }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="exchangeName">ชื่อ exchange (เช่น "SimExchangeA")</param>
    /// <param name="logger">Logging service</param>
    /// <param name="isExchangeA">เป็น Exchange A หรือ B (มีผลต่อราคาเริ่มต้น)</param>
    public SimulationExchangeClient(string exchangeName, ILoggingService logger, bool isExchangeA = true)
    {
        _exchangeName = exchangeName;
        _logger = logger;

        // กำหนดราคาฐานเริ่มต้น (Exchange A และ B มีราคาต่างกันเล็กน้อย)
        var priceMultiplier = isExchangeA ? 1.0m : 1.001m; // B แพงกว่า A เล็กน้อย

        _basePrices["BTCUSDT"] = 42000m * priceMultiplier;
        _basePrices["ETHUSDT"] = 2200m * priceMultiplier;
        _basePrices["BNBUSDT"] = 300m * priceMultiplier;
        _basePrices["SOLUSDT"] = 100m * priceMultiplier;
        _basePrices["XRPUSDT"] = 0.5m * priceMultiplier;

        // กำหนด Balance เริ่มต้น
        InitializeBalances();

        _logger.LogInfo(_exchangeName, $"Simulation client initialized (isExchangeA={isExchangeA})");
    }

    /// <summary>
    /// กำหนด Balance เริ่มต้น
    /// </summary>
    private void InitializeBalances()
    {
        _balances["USDT"] = new AssetBalance { Asset = "USDT", Total = 10000m, Available = 10000m };
        _balances["BTC"] = new AssetBalance { Asset = "BTC", Total = 0.5m, Available = 0.5m };
        _balances["ETH"] = new AssetBalance { Asset = "ETH", Total = 5m, Available = 5m };
        _balances["BNB"] = new AssetBalance { Asset = "BNB", Total = 10m, Available = 10m };
        _balances["SOL"] = new AssetBalance { Asset = "SOL", Total = 20m, Available = 20m };
        _balances["XRP"] = new AssetBalance { Asset = "XRP", Total = 1000m, Available = 1000m };
    }

    #region Market Data

    /// <summary>
    /// ดึงข้อมูล Ticker
    /// จำลองราคาที่ผันผวนรอบค่าฐาน
    /// </summary>
    public Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalizedSymbol = NormalizeSymbol(symbol);

        if (!_basePrices.TryGetValue(normalizedSymbol, out var basePrice))
        {
            basePrice = 100m; // default
        }

        // จำลองราคาที่ผันผวน
        var randomFactor = 1 + ((decimal)_random.NextDouble() - 0.5m) * 2 * _priceVolatility;
        var currentPrice = basePrice * randomFactor;

        // Spread ระหว่าง bid/ask (0.05% - 0.1%)
        var spreadPercent = 0.0005m + (decimal)_random.NextDouble() * 0.0005m;
        var spread = currentPrice * spreadPercent;

        var ticker = new Ticker
        {
            Symbol = symbol,
            Exchange = _exchangeName,
            BidPrice = currentPrice - spread / 2,
            AskPrice = currentPrice + spread / 2,
            BidQuantity = (decimal)(_random.NextDouble() * 10 + 1),
            AskQuantity = (decimal)(_random.NextDouble() * 10 + 1),
            LastPrice = currentPrice,
            Volume24h = (decimal)(_random.NextDouble() * 1000000),
            Timestamp = DateTime.UtcNow
        };

        return Task.FromResult(ticker);
    }

    /// <summary>
    /// ดึงข้อมูล Order Book
    /// </summary>
    public Task<OrderBook> GetOrderBookAsync(string symbol, int depth = 20, CancellationToken cancellationToken = default)
    {
        var normalizedSymbol = NormalizeSymbol(symbol);

        if (!_basePrices.TryGetValue(normalizedSymbol, out var basePrice))
        {
            basePrice = 100m;
        }

        var orderBook = new OrderBook
        {
            Symbol = symbol,
            Exchange = _exchangeName,
            Timestamp = DateTime.UtcNow
        };

        // สร้าง Bids (ราคาซื้อ) - เรียงจากสูงไปต่ำ
        for (int i = 0; i < depth; i++)
        {
            var priceOffset = basePrice * (0.0001m * (i + 1));
            orderBook.Bids.Add(new OrderBookEntry
            {
                Price = basePrice - priceOffset,
                Quantity = (decimal)(_random.NextDouble() * 5 + 0.5)
            });
        }

        // สร้าง Asks (ราคาขาย) - เรียงจากต่ำไปสูง
        for (int i = 0; i < depth; i++)
        {
            var priceOffset = basePrice * (0.0001m * (i + 1));
            orderBook.Asks.Add(new OrderBookEntry
            {
                Price = basePrice + priceOffset,
                Quantity = (decimal)(_random.NextDouble() * 5 + 0.5)
            });
        }

        return Task.FromResult(orderBook);
    }

    public Task<Dictionary<string, Ticker>> GetTickersAsync(
        IEnumerable<string> symbols,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, Ticker>();
        foreach (var symbol in symbols)
        {
            result[symbol] = GetTickerAsync(symbol, cancellationToken).Result;
        }
        return Task.FromResult(result);
    }

    #endregion

    #region Account Data

    /// <summary>
    /// ดึงข้อมูล Balance
    /// </summary>
    public Task<AccountBalance> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        var balance = new AccountBalance
        {
            Exchange = _exchangeName,
            Timestamp = DateTime.UtcNow,
            Assets = new Dictionary<string, AssetBalance>(_balances)
        };

        return Task.FromResult(balance);
    }

    public Task<AssetBalance> GetAssetBalanceAsync(string asset, CancellationToken cancellationToken = default)
    {
        var upperAsset = asset.ToUpperInvariant();
        if (_balances.TryGetValue(upperAsset, out var balance))
        {
            return Task.FromResult(balance);
        }

        return Task.FromResult(new AssetBalance { Asset = upperAsset });
    }

    #endregion

    #region Order Management

    /// <summary>
    /// สร้าง Order จำลอง
    /// จำลอง execution ที่มี delay และ fee
    /// </summary>
    public async Task<Order> PlaceOrderAsync(OrderRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInfo(_exchangeName, $"Placing order: {request.Side} {request.Quantity} {request.Symbol}");

        // จำลอง network delay
        await Task.Delay(_fillDelayMs, cancellationToken);

        // ตรวจสอบ balance
        if (!ValidateBalance(request))
        {
            throw new Exception("Insufficient balance for order");
        }

        // สร้าง order
        var order = new Order
        {
            OrderId = Guid.NewGuid().ToString("N"),
            ClientOrderId = request.ClientOrderId,
            Exchange = _exchangeName,
            Symbol = request.Symbol,
            Side = request.Side,
            Type = request.Type,
            RequestedQuantity = request.Quantity,
            RequestedPrice = request.Price,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // จำลอง fill
        var ticker = await GetTickerAsync(request.Symbol, cancellationToken);
        var fillPrice = request.Side == OrderSide.Buy ? ticker.AskPrice : ticker.BidPrice;

        // ถ้าเป็น Limit order ตรวจสอบราคา
        if (request.Type == OrderType.Limit && request.Price.HasValue)
        {
            if (request.Side == OrderSide.Buy && request.Price.Value < fillPrice)
            {
                // ราคา limit ต่ำกว่าราคาตลาด - ไม่ fill
                order.Status = OrderStatus.Open;
                _openOrders[order.OrderId] = order;
                return order;
            }
            else if (request.Side == OrderSide.Sell && request.Price.Value > fillPrice)
            {
                // ราคา limit สูงกว่าราคาตลาด - ไม่ fill
                order.Status = OrderStatus.Open;
                _openOrders[order.OrderId] = order;
                return order;
            }
        }

        // Fill order
        order.FilledQuantity = request.Quantity;
        order.AverageFilledPrice = fillPrice;
        order.Fee = order.FilledValue * (_feePercent / 100);
        order.FeeCurrency = request.Symbol.EndsWith("USDT") ? "USDT" : "USD";
        order.Status = OrderStatus.Filled;
        order.UpdatedAt = DateTime.UtcNow;

        // อัพเดท balance
        UpdateBalanceAfterOrder(request, order);

        _orderHistory.Enqueue(order);
        _logger.LogInfo(_exchangeName, $"Order filled: {order}");

        return order;
    }

    /// <summary>
    /// ยกเลิก Order
    /// </summary>
    public Task<Order> CancelOrderAsync(string symbol, string orderId, CancellationToken cancellationToken = default)
    {
        if (_openOrders.TryRemove(orderId, out var order))
        {
            order.Status = OrderStatus.Cancelled;
            order.UpdatedAt = DateTime.UtcNow;
            _orderHistory.Enqueue(order);
            _logger.LogInfo(_exchangeName, $"Order cancelled: {orderId}");
            return Task.FromResult(order);
        }

        throw new Exception($"Order not found: {orderId}");
    }

    /// <summary>
    /// ดึงสถานะ Order
    /// </summary>
    public Task<Order> GetOrderAsync(string symbol, string orderId, CancellationToken cancellationToken = default)
    {
        if (_openOrders.TryGetValue(orderId, out var order))
        {
            return Task.FromResult(order);
        }

        // ค้นหาใน history
        var historicalOrder = _orderHistory.FirstOrDefault(o => o.OrderId == orderId);
        if (historicalOrder != null)
        {
            return Task.FromResult(historicalOrder);
        }

        throw new Exception($"Order not found: {orderId}");
    }

    /// <summary>
    /// ดึง Open Orders
    /// </summary>
    public Task<List<Order>> GetOpenOrdersAsync(string? symbol = null, CancellationToken cancellationToken = default)
    {
        var orders = _openOrders.Values.ToList();
        if (!string.IsNullOrEmpty(symbol))
        {
            orders = orders.Where(o => o.Symbol == symbol).ToList();
        }
        return Task.FromResult(orders);
    }

    #endregion

    #region Balance Management

    /// <summary>
    /// ตรวจสอบว่ามี balance พอหรือไม่
    /// </summary>
    private bool ValidateBalance(OrderRequest request)
    {
        var symbol = NormalizeSymbol(request.Symbol);
        var baseCurrency = GetBaseCurrency(symbol);
        var quoteCurrency = GetQuoteCurrency(symbol);

        if (request.Side == OrderSide.Buy)
        {
            // ต้องมี quote currency พอ
            var requiredQuote = request.Quantity * (request.Price ?? GetEstimatedPrice(symbol));
            return _balances.TryGetValue(quoteCurrency, out var balance) && balance.Available >= requiredQuote;
        }
        else
        {
            // ต้องมี base currency พอ
            return _balances.TryGetValue(baseCurrency, out var balance) && balance.Available >= request.Quantity;
        }
    }

    /// <summary>
    /// อัพเดท balance หลัง order fill
    /// </summary>
    private void UpdateBalanceAfterOrder(OrderRequest request, Order order)
    {
        var symbol = NormalizeSymbol(request.Symbol);
        var baseCurrency = GetBaseCurrency(symbol);
        var quoteCurrency = GetQuoteCurrency(symbol);

        if (request.Side == OrderSide.Buy)
        {
            // ได้ base, เสีย quote + fee
            if (_balances.TryGetValue(baseCurrency, out var baseBalance))
            {
                baseBalance.Total += order.FilledQuantity;
                baseBalance.Available += order.FilledQuantity;
            }

            if (_balances.TryGetValue(quoteCurrency, out var quoteBalance))
            {
                var cost = order.FilledValue + order.Fee;
                quoteBalance.Total -= cost;
                quoteBalance.Available -= cost;
            }
        }
        else
        {
            // ได้ quote, เสีย base + fee
            if (_balances.TryGetValue(baseCurrency, out var baseBalance))
            {
                baseBalance.Total -= order.FilledQuantity;
                baseBalance.Available -= order.FilledQuantity;
            }

            if (_balances.TryGetValue(quoteCurrency, out var quoteBalance))
            {
                var received = order.FilledValue - order.Fee;
                quoteBalance.Total += received;
                quoteBalance.Available += received;
            }
        }
    }

    #endregion

    #region Connection

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        IsConnected = true;
        _logger.LogInfo(_exchangeName, "Simulation client connected");
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        _logger.LogInfo(_exchangeName, "Simulation client disconnected");
        return Task.CompletedTask;
    }

    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    #endregion

    #region Configuration

    /// <summary>
    /// ตั้งค่า fee สำหรับ simulation
    /// </summary>
    public void SetFeePercent(decimal feePercent)
    {
        _feePercent = feePercent;
    }

    /// <summary>
    /// ตั้งค่าความผันผวนของราคา
    /// </summary>
    public void SetPriceVolatility(decimal volatility)
    {
        _priceVolatility = volatility;
    }

    /// <summary>
    /// ตั้งค่า fill delay
    /// </summary>
    public void SetFillDelayMs(int delayMs)
    {
        _fillDelayMs = delayMs;
    }

    /// <summary>
    /// ตั้งราคาฐานสำหรับ symbol
    /// </summary>
    public void SetBasePrice(string symbol, decimal price)
    {
        _basePrices[NormalizeSymbol(symbol)] = price;
    }

    /// <summary>
    /// ตั้ง balance
    /// </summary>
    public void SetBalance(string asset, decimal total, decimal available)
    {
        _balances[asset.ToUpperInvariant()] = new AssetBalance
        {
            Asset = asset.ToUpperInvariant(),
            Total = total,
            Available = available
        };
    }

    /// <summary>
    /// รีเซ็ต balance เป็นค่าเริ่มต้น
    /// </summary>
    public void ResetBalances()
    {
        InitializeBalances();
    }

    #endregion

    #region Helpers

    private string NormalizeSymbol(string symbol)
    {
        return symbol.Replace("/", "").ToUpperInvariant();
    }

    private string GetBaseCurrency(string normalizedSymbol)
    {
        // สมมุติว่า symbol อยู่ในรูปแบบ "BTCUSDT"
        if (normalizedSymbol.EndsWith("USDT")) return normalizedSymbol.Replace("USDT", "");
        if (normalizedSymbol.EndsWith("USD")) return normalizedSymbol.Replace("USD", "");
        if (normalizedSymbol.EndsWith("BTC")) return normalizedSymbol.Replace("BTC", "");
        return normalizedSymbol.Substring(0, 3);
    }

    private string GetQuoteCurrency(string normalizedSymbol)
    {
        if (normalizedSymbol.EndsWith("USDT")) return "USDT";
        if (normalizedSymbol.EndsWith("USD")) return "USD";
        if (normalizedSymbol.EndsWith("BTC")) return "BTC";
        return "USDT";
    }

    private decimal GetEstimatedPrice(string normalizedSymbol)
    {
        return _basePrices.TryGetValue(normalizedSymbol, out var price) ? price : 100m;
    }

    public void Dispose()
    {
        // Nothing to dispose
    }

    #endregion
}
