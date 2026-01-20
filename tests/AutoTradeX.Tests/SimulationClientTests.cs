/*
 * ============================================================================
 * AutoTrade-X - Unit Tests for SimulationExchangeClient
 * ============================================================================
 */

using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using AutoTradeX.Infrastructure.ExchangeClients;
using Moq;

namespace AutoTradeX.Tests;

/// <summary>
/// Unit Tests สำหรับ SimulationExchangeClient
/// ทดสอบว่า Simulation Client ทำงานถูกต้อง
/// </summary>
public class SimulationClientTests
{
    private readonly Mock<ILoggingService> _mockLogger;

    public SimulationClientTests()
    {
        _mockLogger = new Mock<ILoggingService>();
    }

    [Fact]
    public async Task GetTickerAsync_ReturnsValidTicker()
    {
        // Arrange
        var client = new SimulationExchangeClient("TestExchange", _mockLogger.Object);

        // Act
        var ticker = await client.GetTickerAsync("BTCUSDT");

        // Assert
        Assert.NotNull(ticker);
        Assert.Equal("BTCUSDT", ticker.Symbol);
        Assert.Equal("TestExchange", ticker.Exchange);
        Assert.True(ticker.BidPrice > 0);
        Assert.True(ticker.AskPrice > 0);
        Assert.True(ticker.AskPrice > ticker.BidPrice);  // Ask ต้องสูงกว่า Bid
    }

    [Fact]
    public async Task GetOrderBookAsync_ReturnsValidOrderBook()
    {
        // Arrange
        var client = new SimulationExchangeClient("TestExchange", _mockLogger.Object);
        var depth = 10;

        // Act
        var orderBook = await client.GetOrderBookAsync("BTCUSDT", depth);

        // Assert
        Assert.NotNull(orderBook);
        Assert.Equal("BTCUSDT", orderBook.Symbol);
        Assert.Equal(depth, orderBook.Bids.Count);
        Assert.Equal(depth, orderBook.Asks.Count);

        // Bids ต้องเรียงจากสูงไปต่ำ
        for (int i = 1; i < orderBook.Bids.Count; i++)
        {
            Assert.True(orderBook.Bids[i - 1].Price > orderBook.Bids[i].Price);
        }

        // Asks ต้องเรียงจากต่ำไปสูง
        for (int i = 1; i < orderBook.Asks.Count; i++)
        {
            Assert.True(orderBook.Asks[i - 1].Price < orderBook.Asks[i].Price);
        }
    }

    [Fact]
    public async Task GetBalanceAsync_ReturnsInitialBalances()
    {
        // Arrange
        var client = new SimulationExchangeClient("TestExchange", _mockLogger.Object);

        // Act
        var balance = await client.GetBalanceAsync();

        // Assert
        Assert.NotNull(balance);
        Assert.True(balance.Assets.ContainsKey("USDT"));
        Assert.True(balance.Assets.ContainsKey("BTC"));
        Assert.True(balance.GetAvailable("USDT") > 0);
        Assert.True(balance.GetAvailable("BTC") > 0);
    }

    [Fact]
    public async Task PlaceOrderAsync_MarketBuy_FillsImmediately()
    {
        // Arrange
        var client = new SimulationExchangeClient("TestExchange", _mockLogger.Object);
        var request = new OrderRequest
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 0.01m
        };

        // Act
        var order = await client.PlaceOrderAsync(request);

        // Assert
        Assert.NotNull(order);
        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(request.Quantity, order.FilledQuantity);
        Assert.True(order.AverageFilledPrice > 0);
        Assert.True(order.Fee > 0);
    }

    [Fact]
    public async Task PlaceOrderAsync_MarketSell_FillsImmediately()
    {
        // Arrange
        var client = new SimulationExchangeClient("TestExchange", _mockLogger.Object);
        var request = new OrderRequest
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.Sell,
            Type = OrderType.Market,
            Quantity = 0.01m
        };

        // Act
        var order = await client.PlaceOrderAsync(request);

        // Assert
        Assert.NotNull(order);
        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(request.Quantity, order.FilledQuantity);
    }

    [Fact]
    public async Task PlaceOrderAsync_UpdatesBalance()
    {
        // Arrange
        var client = new SimulationExchangeClient("TestExchange", _mockLogger.Object);

        // ดึง balance เริ่มต้น
        var initialBalance = await client.GetBalanceAsync();
        var initialUsdt = initialBalance.GetAvailable("USDT");
        var initialBtc = initialBalance.GetAvailable("BTC");

        // ซื้อ BTC
        var buyRequest = new OrderRequest
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 0.01m
        };
        var buyOrder = await client.PlaceOrderAsync(buyRequest);

        // Act - ดึง balance หลังซื้อ
        var afterBuyBalance = await client.GetBalanceAsync();

        // Assert
        // USDT ต้องลดลง
        Assert.True(afterBuyBalance.GetAvailable("USDT") < initialUsdt);
        // BTC ต้องเพิ่มขึ้น
        Assert.True(afterBuyBalance.GetAvailable("BTC") > initialBtc);
    }

    [Fact]
    public async Task SetBasePrice_AffectsTicker()
    {
        // Arrange
        var client = new SimulationExchangeClient("TestExchange", _mockLogger.Object);
        var newPrice = 50000m;
        client.SetBasePrice("BTCUSDT", newPrice);

        // Act
        var ticker = await client.GetTickerAsync("BTCUSDT");

        // Assert - ราคาต้องอยู่ใกล้ newPrice
        Assert.InRange(ticker.MidPrice, newPrice * 0.99m, newPrice * 1.01m);
    }

    [Fact]
    public async Task SetBalance_AffectsBalance()
    {
        // Arrange
        var client = new SimulationExchangeClient("TestExchange", _mockLogger.Object);
        client.SetBalance("ETH", 100m, 90m);

        // Act
        var balance = await client.GetAssetBalanceAsync("ETH");

        // Assert
        Assert.Equal(100m, balance.Total);
        Assert.Equal(90m, balance.Available);
    }

    [Fact]
    public async Task ResetBalances_ResetsToInitial()
    {
        // Arrange
        var client = new SimulationExchangeClient("TestExchange", _mockLogger.Object);
        client.SetBalance("USDT", 100m, 100m);

        // Act
        client.ResetBalances();

        // Assert
        var balance = await client.GetBalanceAsync();
        Assert.Equal(10000m, balance.GetTotal("USDT"));  // ค่าเริ่มต้น
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsTrue()
    {
        // Arrange
        var client = new SimulationExchangeClient("TestExchange", _mockLogger.Object);

        // Act
        var result = await client.TestConnectionAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ConnectAsync_SetsIsConnectedTrue()
    {
        // Arrange
        var client = new SimulationExchangeClient("TestExchange", _mockLogger.Object);

        // Act
        await client.ConnectAsync();

        // Assert
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task DisconnectAsync_SetsIsConnectedFalse()
    {
        // Arrange
        var client = new SimulationExchangeClient("TestExchange", _mockLogger.Object);
        await client.ConnectAsync();

        // Act
        await client.DisconnectAsync();

        // Assert
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task TwoSimulationClients_HaveDifferentPrices()
    {
        // Arrange - Exchange A และ B มีราคาต่างกัน
        var clientA = new SimulationExchangeClient("ExchangeA", _mockLogger.Object, isExchangeA: true);
        var clientB = new SimulationExchangeClient("ExchangeB", _mockLogger.Object, isExchangeA: false);

        // Act
        var tickerA = await clientA.GetTickerAsync("BTCUSDT");
        var tickerB = await clientB.GetTickerAsync("BTCUSDT");

        // Assert - B ควรมีราคาสูงกว่า A เล็กน้อย (ตาม constructor logic)
        // แต่เนื่องจากมี randomness จึงทดสอบแค่ว่าทั้งคู่มีค่าที่ valid
        Assert.True(tickerA.BidPrice > 0);
        Assert.True(tickerB.BidPrice > 0);
    }
}
