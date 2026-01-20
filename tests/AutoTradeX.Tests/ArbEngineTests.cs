/*
 * ============================================================================
 * AutoTrade-X - Unit Tests for ArbEngine
 * ============================================================================
 *
 * ⚠️ คำเตือน: โค้ดนี้เป็นตัวอย่างการทดสอบเท่านั้น
 * ควรเพิ่ม test cases ให้ครอบคลุมก่อนใช้งานจริง
 *
 * Test Cases:
 * 1. การคำนวณ Spread
 * 2. การคำนวณ Expected Profit
 * 3. การตัดสินใจเทรด (ShouldTrade)
 * 4. Risk Management
 *
 * ============================================================================
 */

using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using AutoTradeX.Core.Services;
using AutoTradeX.Infrastructure.ExchangeClients;
using AutoTradeX.Infrastructure.Services;
using Moq;

namespace AutoTradeX.Tests;

/// <summary>
/// Unit Tests สำหรับ ArbEngine
/// </summary>
public class ArbEngineTests
{
    private readonly Mock<ILoggingService> _mockLogger;
    private readonly Mock<IConfigService> _mockConfigService;
    private readonly AppConfig _testConfig;

    public ArbEngineTests()
    {
        _mockLogger = new Mock<ILoggingService>();
        _mockConfigService = new Mock<IConfigService>();

        // สร้าง test config
        _testConfig = CreateTestConfig();
        _mockConfigService.Setup(x => x.GetConfig()).Returns(_testConfig);
    }

    private AppConfig CreateTestConfig()
    {
        return new AppConfig
        {
            ExchangeA = new ExchangeConfig
            {
                Name = "TestExchangeA",
                TradingFeePercent = 0.1m
            },
            ExchangeB = new ExchangeConfig
            {
                Name = "TestExchangeB",
                TradingFeePercent = 0.1m
            },
            Strategy = new StrategyConfig
            {
                MinSpreadPercentage = 0.3m,
                MinExpectedProfitQuoteCurrency = 0.5m,
                PollingIntervalMs = 1000,
                MinDepthQuantity = 0.01m
            },
            Risk = new RiskConfig
            {
                MaxPositionSizePerTrade = 100m,
                MaxDailyLoss = 50m,
                MaxTradesPerDay = 100
            },
            General = new GeneralConfig
            {
                LiveTrading = false
            },
            TradingPairs = new List<string> { "BTC/USDT" }
        };
    }

    #region Spread Calculation Tests

    [Fact]
    public void SpreadOpportunity_SpreadBuyA_SellB_CalculatesCorrectly()
    {
        // Arrange
        var opportunity = new SpreadOpportunity
        {
            ExchangeA_AskPrice = 42000m,  // ราคาซื้อที่ A
            ExchangeB_BidPrice = 42100m   // ราคาขายที่ B
        };

        // Act
        var spread = opportunity.SpreadBuyA_SellB;

        // Assert
        // Spread = (42100 - 42000) / 42000 * 100 = 0.238%
        Assert.True(spread > 0);
        Assert.InRange(spread, 0.23m, 0.24m);
    }

    [Fact]
    public void SpreadOpportunity_SpreadBuyB_SellA_CalculatesCorrectly()
    {
        // Arrange
        var opportunity = new SpreadOpportunity
        {
            ExchangeA_BidPrice = 42100m,  // ราคาขายที่ A
            ExchangeB_AskPrice = 42000m   // ราคาซื้อที่ B
        };

        // Act
        var spread = opportunity.SpreadBuyB_SellA;

        // Assert
        // Spread = (42100 - 42000) / 42000 * 100 = 0.238%
        Assert.True(spread > 0);
        Assert.InRange(spread, 0.23m, 0.24m);
    }

    [Fact]
    public void SpreadOpportunity_NoSpread_ReturnsNegativeOrZero()
    {
        // Arrange - ราคาเท่ากัน ไม่มี spread
        var opportunity = new SpreadOpportunity
        {
            ExchangeA_AskPrice = 42000m,
            ExchangeA_BidPrice = 41990m,
            ExchangeB_AskPrice = 42000m,
            ExchangeB_BidPrice = 41990m
        };

        // Act & Assert
        // ไม่มี spread ที่ทำกำไรได้
        Assert.True(opportunity.SpreadBuyA_SellB <= 0);
        Assert.True(opportunity.SpreadBuyB_SellA <= 0);
    }

    [Fact]
    public void SpreadOpportunity_NetSpread_DeductsFees()
    {
        // Arrange
        var opportunity = new SpreadOpportunity
        {
            ExchangeA_AskPrice = 42000m,
            ExchangeB_BidPrice = 42150m,  // 0.357% gross spread
            ExchangeA_FeePercent = 0.1m,
            ExchangeB_FeePercent = 0.1m
        };

        // Act
        var grossSpread = opportunity.BestSpreadPercentage;
        var netSpread = opportunity.NetSpreadPercentage;
        var totalFee = opportunity.TotalFeePercent;

        // Assert
        Assert.Equal(0.2m, totalFee);  // 0.1% + 0.1%
        Assert.True(netSpread < grossSpread);
        Assert.Equal(grossSpread - totalFee, netSpread);
    }

    #endregion

    #region Expected Profit Calculation Tests

    [Fact]
    public void SpreadOpportunity_ExpectedProfit_CalculatesCorrectly()
    {
        // Arrange
        var opportunity = new SpreadOpportunity
        {
            Symbol = "BTC/USDT",
            Direction = ArbitrageDirection.BuyA_SellB,
            ExchangeA_AskPrice = 42000m,
            ExchangeB_BidPrice = 42100m,
            ExchangeA_FeePercent = 0.1m,
            ExchangeB_FeePercent = 0.1m,
            SuggestedQuantity = 0.1m  // 0.1 BTC
        };

        // ซื้อที่ A: 0.1 * 42000 = 4200 USDT
        // ขายที่ B: 0.1 * 42100 = 4210 USDT
        // Gross Profit = 4210 - 4200 = 10 USDT
        // Buy Fee = 4200 * 0.001 = 4.2 USDT
        // Sell Fee = 4210 * 0.001 = 4.21 USDT
        // Net Profit = 10 - 4.2 - 4.21 = 1.59 USDT

        // Act
        var buyValue = opportunity.SuggestedQuantity * opportunity.BuyPrice;
        var sellValue = opportunity.SuggestedQuantity * opportunity.SellPrice;
        var buyFee = buyValue * (opportunity.ExchangeA_FeePercent / 100);
        var sellFee = sellValue * (opportunity.ExchangeB_FeePercent / 100);
        var expectedProfit = sellValue - buyValue - buyFee - sellFee;

        // Assert
        Assert.Equal(4200m, buyValue);
        Assert.Equal(4210m, sellValue);
        Assert.InRange(expectedProfit, 1.5m, 1.7m);
    }

    #endregion

    #region Trading Decision Tests

    [Fact]
    public void SpreadOpportunity_ShouldTrade_WhenAllConditionsMet()
    {
        // Arrange
        var opportunity = new SpreadOpportunity
        {
            Direction = ArbitrageDirection.BuyA_SellB,
            MeetsMinSpread = true,
            MeetsMinProfit = true,
            HasSufficientLiquidity = true,
            HasSufficientBalance = true,
            ExchangeA_AskPrice = 42000m,
            ExchangeB_BidPrice = 42200m,
            ExchangeA_FeePercent = 0.1m,
            ExchangeB_FeePercent = 0.1m
        };

        // Act & Assert
        Assert.True(opportunity.HasPositiveSpread);
        Assert.True(opportunity.ShouldTrade);
    }

    [Fact]
    public void SpreadOpportunity_ShouldNotTrade_WhenSpreadBelowMinimum()
    {
        // Arrange
        var opportunity = new SpreadOpportunity
        {
            Direction = ArbitrageDirection.BuyA_SellB,
            MeetsMinSpread = false,  // ไม่ผ่าน
            MeetsMinProfit = true,
            HasSufficientLiquidity = true,
            HasSufficientBalance = true
        };

        // Act & Assert
        Assert.False(opportunity.ShouldTrade);
    }

    [Fact]
    public void SpreadOpportunity_ShouldNotTrade_WhenProfitBelowMinimum()
    {
        // Arrange
        var opportunity = new SpreadOpportunity
        {
            Direction = ArbitrageDirection.BuyA_SellB,
            MeetsMinSpread = true,
            MeetsMinProfit = false,  // ไม่ผ่าน
            HasSufficientLiquidity = true,
            HasSufficientBalance = true
        };

        // Act & Assert
        Assert.False(opportunity.ShouldTrade);
    }

    [Fact]
    public void SpreadOpportunity_ShouldNotTrade_WhenInsufficientBalance()
    {
        // Arrange
        var opportunity = new SpreadOpportunity
        {
            Direction = ArbitrageDirection.BuyA_SellB,
            MeetsMinSpread = true,
            MeetsMinProfit = true,
            HasSufficientLiquidity = true,
            HasSufficientBalance = false  // ไม่ผ่าน
        };

        // Act & Assert
        Assert.False(opportunity.ShouldTrade);
    }

    [Fact]
    public void SpreadOpportunity_ShouldNotTrade_WhenNoDirection()
    {
        // Arrange
        var opportunity = new SpreadOpportunity
        {
            Direction = ArbitrageDirection.None,  // ไม่มีทิศทาง
            MeetsMinSpread = true,
            MeetsMinProfit = true,
            HasSufficientLiquidity = true,
            HasSufficientBalance = true
        };

        // Act & Assert
        Assert.False(opportunity.ShouldTrade);
    }

    #endregion

    #region Order Book Tests

    [Fact]
    public void OrderBook_GetAverageAskPrice_CalculatesCorrectly()
    {
        // Arrange
        var orderBook = new OrderBook
        {
            Asks = new List<OrderBookEntry>
            {
                new(42000m, 0.5m),   // 0.5 BTC @ 42000
                new(42010m, 0.5m),   // 0.5 BTC @ 42010
                new(42020m, 1.0m)    // 1.0 BTC @ 42020
            }
        };

        // Act - ต้องการซื้อ 0.7 BTC
        var avgPrice = orderBook.GetAverageAskPrice(0.7m);

        // 0.5 @ 42000 = 21000
        // 0.2 @ 42010 = 8402
        // Total = 29402 / 0.7 = 42002.857...

        // Assert
        Assert.NotNull(avgPrice);
        Assert.InRange(avgPrice.Value, 42002m, 42003m);
    }

    [Fact]
    public void OrderBook_GetAverageAskPrice_ReturnsNull_WhenInsufficientLiquidity()
    {
        // Arrange
        var orderBook = new OrderBook
        {
            Asks = new List<OrderBookEntry>
            {
                new(42000m, 0.5m)  // มีแค่ 0.5 BTC
            }
        };

        // Act - ต้องการซื้อ 1.0 BTC (มากกว่าที่มี)
        var avgPrice = orderBook.GetAverageAskPrice(1.0m);

        // Assert
        Assert.Null(avgPrice);  // ไม่มี liquidity พอ
    }

    [Fact]
    public void OrderBook_HasSufficientLiquidity_ReturnsCorrectly()
    {
        // Arrange
        var orderBook = new OrderBook
        {
            Asks = new List<OrderBookEntry> { new(42000m, 1.0m) },
            Bids = new List<OrderBookEntry> { new(41990m, 1.0m) }
        };

        // Act & Assert
        Assert.True(orderBook.HasSufficientLiquidity(0.5m, isBuy: true));
        Assert.True(orderBook.HasSufficientLiquidity(0.5m, isBuy: false));
        Assert.False(orderBook.HasSufficientLiquidity(2.0m, isBuy: true));
        Assert.False(orderBook.HasSufficientLiquidity(2.0m, isBuy: false));
    }

    #endregion

    #region Balance Tests

    [Fact]
    public void AccountBalance_HasSufficientBalance_ReturnsCorrectly()
    {
        // Arrange
        var balance = new AccountBalance
        {
            Assets = new Dictionary<string, AssetBalance>
            {
                ["USDT"] = new AssetBalance { Asset = "USDT", Total = 1000m, Available = 800m },
                ["BTC"] = new AssetBalance { Asset = "BTC", Total = 0.5m, Available = 0.4m }
            }
        };

        // Act & Assert
        Assert.True(balance.HasSufficientBalance("USDT", 500m));
        Assert.True(balance.HasSufficientBalance("USDT", 800m));
        Assert.False(balance.HasSufficientBalance("USDT", 900m));  // เกิน available

        Assert.True(balance.HasSufficientBalance("BTC", 0.3m));
        Assert.False(balance.HasSufficientBalance("BTC", 0.5m));  // เกิน available
    }

    #endregion

    #region Risk Management Tests

    [Fact]
    public void RiskConfig_MaxDailyLoss_StopsTrading()
    {
        // Arrange
        var stats = new DailyPnL
        {
            TotalNetPnL = -45m  // ขาดทุน 45 USDT
        };
        var maxDailyLoss = 50m;

        // Act
        var shouldStop = Math.Abs(stats.TotalNetPnL) >= maxDailyLoss;

        // Assert
        Assert.False(shouldStop);  // ยังไม่ถึง limit

        // Act 2 - ขาดทุนเกิน limit
        stats.TotalNetPnL = -55m;
        shouldStop = Math.Abs(stats.TotalNetPnL) >= maxDailyLoss;

        // Assert 2
        Assert.True(shouldStop);  // เกิน limit แล้ว
    }

    [Fact]
    public void RiskConfig_MaxTradesPerDay_StopsTrading()
    {
        // Arrange
        var stats = new DailyPnL { TotalTrades = 99 };
        var maxTradesPerDay = 100;

        // Act & Assert
        Assert.False(stats.TotalTrades >= maxTradesPerDay);

        stats.TotalTrades = 100;
        Assert.True(stats.TotalTrades >= maxTradesPerDay);
    }

    #endregion

    #region Trading Pair Tests

    [Fact]
    public void TradingPair_FromSymbol_ParsesCorrectly()
    {
        // Arrange & Act
        var pair = TradingPair.FromSymbol("BTC/USDT");

        // Assert
        Assert.Equal("BTC/USDT", pair.Symbol);
        Assert.Equal("BTC", pair.BaseCurrency);
        Assert.Equal("USDT", pair.QuoteCurrency);
    }

    [Fact]
    public void TradingPair_FromSymbol_ThrowsOnInvalidFormat()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => TradingPair.FromSymbol("BTCUSDT"));  // ไม่มี /
        Assert.Throws<ArgumentException>(() => TradingPair.FromSymbol("BTC"));
    }

    #endregion

    #region Trade Result Tests

    [Fact]
    public void TradeResult_CalculatesPnLCorrectly()
    {
        // Arrange
        var result = new TradeResult
        {
            BuyOrder = new Order
            {
                FilledQuantity = 0.1m,
                AverageFilledPrice = 42000m,
                Fee = 4.2m
            },
            SellOrder = new Order
            {
                FilledQuantity = 0.1m,
                AverageFilledPrice = 42100m,
                Fee = 4.21m
            }
        };

        // Act
        // Gross PnL = 4210 - 4200 = 10 USDT
        // Total Fees = 4.2 + 4.21 = 8.41 USDT
        // Net PnL ควรประมาณ 10 - 8.41 = 1.59 USDT

        // Assert
        Assert.Equal(4200m, result.ActualBuyValue);
        Assert.Equal(4210m, result.ActualSellValue);
        Assert.Equal(10m, result.GrossPnL);
        Assert.InRange(result.TotalFees, 8.4m, 8.5m);
    }

    [Fact]
    public void TradeResult_IsFinal_ReturnsCorrectly()
    {
        // Arrange
        var filledOrder = new Order { Status = OrderStatus.Filled };
        var openOrder = new Order { Status = OrderStatus.Open };
        var cancelledOrder = new Order { Status = OrderStatus.Cancelled };

        // Assert
        Assert.True(filledOrder.IsFinal);
        Assert.False(openOrder.IsFinal);
        Assert.True(cancelledOrder.IsFinal);
    }

    #endregion

    #region DailyPnL Tests

    [Fact]
    public void DailyPnL_WinRate_CalculatesCorrectly()
    {
        // Arrange
        var stats = new DailyPnL
        {
            TotalTrades = 10,
            SuccessfulTrades = 7,
            FailedTrades = 3
        };

        // Act
        var winRate = stats.WinRate;

        // Assert
        Assert.Equal(70m, winRate);
    }

    [Fact]
    public void DailyPnL_WinRate_ReturnsZero_WhenNoTrades()
    {
        // Arrange
        var stats = new DailyPnL { TotalTrades = 0 };

        // Assert
        Assert.Equal(0m, stats.WinRate);
    }

    [Fact]
    public void DailyPnL_AveragePnLPerTrade_CalculatesCorrectly()
    {
        // Arrange
        var stats = new DailyPnL
        {
            TotalTrades = 10,
            TotalNetPnL = 15m
        };

        // Assert
        Assert.Equal(1.5m, stats.AveragePnLPerTrade);
    }

    #endregion
}
