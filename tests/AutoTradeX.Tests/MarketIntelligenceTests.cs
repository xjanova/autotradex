/*
 * ============================================================================
 * AutoTrade-X - Unit Tests for Market Intelligence components
 * ============================================================================
 * Tests the pure-logic layer: SentimentAnalyzer, MarketRegimeDetector,
 * and symbol parsing. No network access required.
 * ============================================================================
 */

using AutoTradeX.Core.Models;
using AutoTradeX.Core.Services;

namespace AutoTradeX.Tests;

/// <summary>
/// Unit Tests สำหรับ SentimentAnalyzer — วิเคราะห์ Sentiment หัวข้อข่าว
/// </summary>
public class SentimentAnalyzerTests
{
    [Fact]
    public void ScoreHeadline_BullishNews_ReturnsPositiveScore()
    {
        var (score, matched) = SentimentAnalyzer.ScoreHeadline("Bitcoin surges to all-time high after ETF approval");

        Assert.True(score > 0, $"Expected positive score, got {score}");
        Assert.Contains("all-time high", matched);
    }

    [Fact]
    public void ScoreHeadline_BearishNews_ReturnsNegativeScore()
    {
        var (score, matched) = SentimentAnalyzer.ScoreHeadline("Exchange hacked: $100M stolen, withdrawals frozen");

        Assert.True(score < 0, $"Expected negative score, got {score}");
        Assert.Contains("hacked", matched);
    }

    [Fact]
    public void ScoreHeadline_NeutralNews_ReturnsZero()
    {
        var (score, _) = SentimentAnalyzer.ScoreHeadline("Weekly market analysis: what to watch this week");

        Assert.Equal(0, score);
    }

    [Fact]
    public void ScoreHeadline_EmptyString_ReturnsZero()
    {
        var (score, matched) = SentimentAnalyzer.ScoreHeadline("");

        Assert.Equal(0, score);
        Assert.Empty(matched);
    }

    [Fact]
    public void ScoreHeadline_WordBoundary_BanDoesNotMatchInsideBank()
    {
        // "ban" must not match inside "bank" / "banking"
        var (score, matched) = SentimentAnalyzer.ScoreHeadline("Major bank launches banking services");

        Assert.DoesNotContain("ban", matched);
        Assert.Equal(0, score);
    }

    [Fact]
    public void ScoreHeadline_ScoreIsClampedToRange()
    {
        var (score, _) = SentimentAnalyzer.ScoreHeadline(
            "Rug pull exit scam: exchange hacked, bankruptcy collapse, fraud ponzi crash plunge banned delisted");

        Assert.InRange(score, -100, 100);
    }

    [Fact]
    public void IsAboutCoin_BitcoinAliases_Match()
    {
        Assert.True(SentimentAnalyzer.IsAboutCoin("Bitcoin price rises", "BTC"));
        Assert.True(SentimentAnalyzer.IsAboutCoin("BTC breaks resistance", "BTC"));
        Assert.False(SentimentAnalyzer.IsAboutCoin("Ethereum upgrade complete", "BTC"));
    }

    [Fact]
    public void IsAboutCoin_WordBoundary_SolDoesNotMatchInsideSolution()
    {
        Assert.False(SentimentAnalyzer.IsAboutCoin("New solution for payments announced", "SOL"));
        Assert.True(SentimentAnalyzer.IsAboutCoin("Solana network upgrade complete", "SOL"));
    }

    [Fact]
    public void Summarize_RecentNewsWeighted_MoreThanOldNews()
    {
        var now = DateTime.UtcNow;
        var articles = new List<NewsArticle>
        {
            // Recent strongly bearish
            new() { Title = "a", SentimentScore = -40, PublishedAt = now.AddHours(-1) },
            // Old strongly bullish (47h — weight decayed to ~0.27)
            new() { Title = "b", SentimentScore = 40, PublishedAt = now.AddHours(-47) },
        };

        var summary = SentimentAnalyzer.Summarize("BTC", articles, now);

        // Recent bearish news should dominate → negative overall
        Assert.True(summary.MarketScore < 0, $"Expected negative, got {summary.MarketScore}");
        Assert.Equal(1, summary.BullishCount);
        Assert.Equal(1, summary.BearishCount);
    }

    [Fact]
    public void Summarize_CoinSpecificScore_SeparateFromMarketScore()
    {
        var now = DateTime.UtcNow;
        var articles = new List<NewsArticle>
        {
            new() { Title = "coin bad", SentimentScore = -50, PublishedAt = now, IsCoinSpecific = true },
            new() { Title = "market good 1", SentimentScore = 40, PublishedAt = now, IsCoinSpecific = false },
            new() { Title = "market good 2", SentimentScore = 40, PublishedAt = now, IsCoinSpecific = false },
        };

        var summary = SentimentAnalyzer.Summarize("XYZ", articles, now);

        Assert.NotNull(summary.CoinScore);
        Assert.True(summary.CoinScore < 0);
        Assert.Equal(summary.CoinScore, summary.EffectiveScore); // coin score wins when present
    }

    [Fact]
    public void Summarize_NoCoinNews_CoinScoreIsNull()
    {
        var articles = new List<NewsArticle>
        {
            new() { Title = "market", SentimentScore = 20, PublishedAt = DateTime.UtcNow, IsCoinSpecific = false },
        };

        var summary = SentimentAnalyzer.Summarize("BTC", articles);

        Assert.Null(summary.CoinScore);
        Assert.Equal(summary.MarketScore, summary.EffectiveScore);
    }
}

/// <summary>
/// Unit Tests สำหรับ MarketRegimeDetector — วิเคราะห์เทรนด์และ regime
/// </summary>
public class MarketRegimeDetectorTests
{
    private static List<PriceCandle> MakeCandles(decimal start, decimal step, int count)
    {
        var candles = new List<PriceCandle>();
        var price = start;
        var time = DateTime.UtcNow.AddMinutes(-count);
        for (int i = 0; i < count; i++)
        {
            var open = price;
            var close = price + step;
            candles.Add(new PriceCandle
            {
                Time = time.AddMinutes(i),
                Open = open,
                High = Math.Max(open, close) * 1.001m,
                Low = Math.Min(open, close) * 0.999m,
                Close = close,
                Volume = 1000
            });
            price = close;
        }
        return candles;
    }

    [Fact]
    public void AnalyzeTimeframe_RisingPrices_DetectsUptrend()
    {
        var candles = MakeCandles(100m, 0.5m, 60);

        var trend = MarketRegimeDetector.AnalyzeTimeframe("1h", candles);

        Assert.Equal(1, trend.Direction);
        Assert.True(trend.Strength > 0);
    }

    [Fact]
    public void AnalyzeTimeframe_FallingPrices_DetectsDowntrend()
    {
        var candles = MakeCandles(200m, -0.5m, 60);

        var trend = MarketRegimeDetector.AnalyzeTimeframe("1h", candles);

        Assert.Equal(-1, trend.Direction);
    }

    [Fact]
    public void AnalyzeTimeframe_FlatPrices_DetectsSideways()
    {
        var candles = MakeCandles(100m, 0m, 60);

        var trend = MarketRegimeDetector.AnalyzeTimeframe("1h", candles);

        Assert.Equal(0, trend.Direction);
    }

    [Fact]
    public void AnalyzeTimeframe_InsufficientData_ReturnsNeutral()
    {
        var candles = MakeCandles(100m, 1m, 10); // < 25 candles

        var trend = MarketRegimeDetector.AnalyzeTimeframe("1h", candles);

        Assert.Equal(0, trend.Direction);
        Assert.Equal(0, trend.Strength);
    }

    [Fact]
    public void Combine_AllUptrends_StrongUptrendRegime()
    {
        var trends = new List<TimeframeTrend>
        {
            new() { Interval = "15m", Direction = 1, Strength = 80 },
            new() { Interval = "1h", Direction = 1, Strength = 90 },
            new() { Interval = "4h", Direction = 1, Strength = 85 },
        };

        var analysis = MarketRegimeDetector.Combine("binance", "BTC/USDT", trends);

        Assert.True(analysis.ConfluenceScore >= 60);
        Assert.Equal(MarketRegime.StrongUptrend, analysis.Regime);
        Assert.True(analysis.IsTrending);
    }

    [Fact]
    public void Combine_AllDowntrends_StrongDowntrendRegime()
    {
        var trends = new List<TimeframeTrend>
        {
            new() { Interval = "15m", Direction = -1, Strength = 80 },
            new() { Interval = "1h", Direction = -1, Strength = 90 },
            new() { Interval = "4h", Direction = -1, Strength = 85 },
        };

        var analysis = MarketRegimeDetector.Combine("binance", "BTC/USDT", trends);

        Assert.True(analysis.ConfluenceScore <= -60);
        Assert.Equal(MarketRegime.StrongDowntrend, analysis.Regime);
    }

    [Fact]
    public void Combine_MixedTrends_LongerTimeframeWeighsMore()
    {
        // 15m up but 4h strongly down — 4h has 3x weight, should pull negative
        var trends = new List<TimeframeTrend>
        {
            new() { Interval = "15m", Direction = 1, Strength = 60 },
            new() { Interval = "1h", Direction = 0, Strength = 0 },
            new() { Interval = "4h", Direction = -1, Strength = 90 },
        };

        var analysis = MarketRegimeDetector.Combine("binance", "BTC/USDT", trends);

        Assert.True(analysis.ConfluenceScore < 0, $"Expected negative confluence, got {analysis.ConfluenceScore}");
    }

    [Fact]
    public void Combine_NoTrends_ReturnsNeutral()
    {
        var analysis = MarketRegimeDetector.Combine("binance", "BTC/USDT", new List<TimeframeTrend>());

        Assert.Equal(0, analysis.ConfluenceScore);
        Assert.Equal(MarketRegime.Sideways, analysis.Regime);
    }

    [Fact]
    public void RecommendStrategy_Downtrend_RecommendsSmartDCA()
    {
        var analysis = new MultiTimeframeAnalysis { Regime = MarketRegime.Downtrend };

        var mode = MarketRegimeDetector.RecommendStrategy(analysis, 2_000_000);

        Assert.Equal(AITradingMode.SmartDCA, mode);
    }

    [Fact]
    public void RecommendStrategy_StrongUptrendHighVolume_RecommendsBreakout()
    {
        var analysis = new MultiTimeframeAnalysis { Regime = MarketRegime.StrongUptrend };

        var mode = MarketRegimeDetector.RecommendStrategy(analysis, 5_000_000);

        Assert.Equal(AITradingMode.Breakout, mode);
    }

    [Fact]
    public void RecommendStrategy_SidewaysHighVolume_RecommendsGrid()
    {
        var analysis = new MultiTimeframeAnalysis { Regime = MarketRegime.Sideways };

        var mode = MarketRegimeDetector.RecommendStrategy(analysis, 1_000_000);

        Assert.Equal(AITradingMode.GridTrading, mode);
    }
}

/// <summary>
/// Unit Tests สำหรับ symbol parsing ใน AITradingService
/// </summary>
public class BaseAssetParsingTests
{
    [Theory]
    [InlineData("BTC/USDT", "BTC")]
    [InlineData("ETH/THB", "ETH")]
    [InlineData("BTCUSDT", "BTC")]
    [InlineData("SOLUSDC", "SOL")]
    [InlineData("KUBTHB", "KUB")]
    [InlineData("DOGEUSD", "DOGE")]
    [InlineData("btc/usdt", "BTC")]
    [InlineData("", "")]
    public void GetBaseAsset_ParsesCorrectly(string symbol, string expected)
    {
        Assert.Equal(expected, Core.Services.AITradingService.GetBaseAsset(symbol));
    }
}
