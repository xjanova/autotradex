/*
 * ============================================================================
 * AutoTrade-X - Market Regime Detector
 * ============================================================================
 * Pure multi-timeframe trend + regime analysis from price candles.
 * No network access — the caller supplies candles per timeframe.
 * ============================================================================
 */

using AutoTradeX.Core.Models;

namespace AutoTradeX.Core.Services;

/// <summary>
/// วิเคราะห์เทรนด์หลาย timeframe และจำแนกสภาวะตลาด (Market Regime)
/// </summary>
public static class MarketRegimeDetector
{
    /// <summary>
    /// วิเคราะห์เทรนด์ของ 1 timeframe จากแท่งเทียน
    /// ใช้ EMA9/EMA21 + ความชัน + RSI + ตำแหน่งราคา
    /// </summary>
    public static TimeframeTrend AnalyzeTimeframe(string interval, List<PriceCandle> candles)
    {
        var trend = new TimeframeTrend { Interval = interval };
        if (candles == null || candles.Count < 25)
        {
            trend.Description = $"{interval}: ข้อมูลไม่พอ";
            return trend;
        }

        var currentPrice = candles[^1].Close;
        var emaFast = Ema(candles, 9);
        var emaSlow = Ema(candles, 21);
        var rsi = Rsi(candles, 14);
        var atr = Atr(candles, 14);

        trend.EmaFast = emaFast;
        trend.EmaSlow = emaSlow;
        trend.RSI = rsi;
        trend.AtrPercent = currentPrice > 0 ? atr / currentPrice * 100 : 0;

        // EMA separation as % of price — measures trend strength
        var emaSeparation = currentPrice > 0
            ? (emaFast - emaSlow) / currentPrice * 100
            : 0;

        // Slope: compare EMA21 now vs 5 candles ago
        var emaSlowPrev = Ema(candles.Take(candles.Count - 5).ToList(), 21);
        var slope = emaSlowPrev > 0 ? (emaSlow - emaSlowPrev) / emaSlowPrev * 100 : 0;

        int score = 0;
        if (emaSeparation > 0.05m) score += 40;
        else if (emaSeparation < -0.05m) score -= 40;

        if (slope > 0.05m) score += 30;
        else if (slope < -0.05m) score -= 30;

        if (rsi > 55) score += 15;
        else if (rsi < 45) score -= 15;

        if (currentPrice > emaSlow) score += 15;
        else if (currentPrice < emaSlow) score -= 15;

        trend.Direction = score >= 30 ? 1 : score <= -30 ? -1 : 0;
        trend.Strength = Math.Min(100, Math.Abs(score));
        trend.Description = trend.Direction switch
        {
            1 => $"{interval}: ขาขึ้น (แรง {trend.Strength}%)",
            -1 => $"{interval}: ขาลง (แรง {trend.Strength}%)",
            _ => $"{interval}: ไซด์เวย์"
        };

        return trend;
    }

    /// <summary>
    /// รวมเทรนด์หลาย timeframe เป็น confluence score + market regime
    /// timeframe ยาวมีน้ำหนักมากกว่า (15m×1, 1h×2, 4h×3)
    /// </summary>
    public static MultiTimeframeAnalysis Combine(string exchange, string symbol, List<TimeframeTrend> trends)
    {
        var analysis = new MultiTimeframeAnalysis
        {
            Exchange = exchange,
            Symbol = symbol,
            Trends = trends
        };

        if (trends.Count == 0)
        {
            analysis.RegimeDescription = "ไม่มีข้อมูล timeframe";
            return analysis;
        }

        decimal weightedSum = 0, weightTotal = 0;
        for (int i = 0; i < trends.Count; i++)
        {
            // Later entries = longer timeframes = higher weight
            var weight = i + 1;
            weightedSum += trends[i].Direction * trends[i].Strength * weight;
            weightTotal += weight;
        }

        analysis.ConfluenceScore = weightTotal > 0
            ? Math.Clamp((int)(weightedSum / weightTotal), -100, 100)
            : 0;

        // Average volatility across timeframes (ATR% of the shortest TF dominates intraday risk)
        var avgAtrPercent = trends.Where(t => t.AtrPercent > 0).Select(t => t.AtrPercent).DefaultIfEmpty(0).Average();
        var isHighVol = avgAtrPercent > 1.5m;

        var score = analysis.ConfluenceScore;
        analysis.Regime = score switch
        {
            >= 60 => MarketRegime.StrongUptrend,
            >= 25 => MarketRegime.Uptrend,
            <= -60 => MarketRegime.StrongDowntrend,
            <= -25 => MarketRegime.Downtrend,
            _ => isHighVol ? MarketRegime.HighVolatility : MarketRegime.Sideways
        };

        var upCount = trends.Count(t => t.Direction == 1);
        var downCount = trends.Count(t => t.Direction == -1);
        analysis.RegimeDescription = analysis.Regime switch
        {
            MarketRegime.StrongUptrend => $"ขาขึ้นแรง — {upCount}/{trends.Count} timeframe ชี้ขึ้นพร้อมกัน",
            MarketRegime.Uptrend => $"ขาขึ้น — {upCount}/{trends.Count} timeframe ชี้ขึ้น",
            MarketRegime.StrongDowntrend => $"ขาลงแรง — {downCount}/{trends.Count} timeframe ชี้ลงพร้อมกัน",
            MarketRegime.Downtrend => $"ขาลง — {downCount}/{trends.Count} timeframe ชี้ลง",
            MarketRegime.HighVolatility => $"ผันผวนสูง (ATR {avgAtrPercent:F2}%) ทิศทางไม่ชัด",
            _ => "ไซด์เวย์ — แกว่งในกรอบ ไม่มีเทรนด์ชัดเจน"
        };

        return analysis;
    }

    /// <summary>
    /// แนะนำกลยุทธ์ที่เหมาะสมที่สุดกับ regime ปัจจุบัน
    /// </summary>
    public static AITradingMode RecommendStrategy(MultiTimeframeAnalysis analysis, decimal volume24h)
    {
        return analysis.Regime switch
        {
            MarketRegime.StrongUptrend => volume24h > 1_000_000 ? AITradingMode.Breakout : AITradingMode.Momentum,
            MarketRegime.Uptrend => AITradingMode.Momentum,
            MarketRegime.Sideways => volume24h > 500_000 ? AITradingMode.GridTrading : AITradingMode.MeanReversion,
            MarketRegime.HighVolatility => AITradingMode.Scalping,
            // ขาลง: ไม่ไล่ซื้อ — DCA เก็บของถูกเท่านั้น (signal จะเข้มงวดเอง)
            MarketRegime.Downtrend => AITradingMode.SmartDCA,
            MarketRegime.StrongDowntrend => AITradingMode.SmartDCA,
            _ => AITradingMode.Scalping
        };
    }

    #region Self-contained indicator math (pure, no state)

    private static decimal Ema(List<PriceCandle> candles, int period)
    {
        if (candles.Count == 0) return 0;
        if (candles.Count < period) return candles[^1].Close;

        var multiplier = 2m / (period + 1);
        var ema = candles.Take(period).Average(c => c.Close);
        foreach (var candle in candles.Skip(period))
        {
            ema = (candle.Close - ema) * multiplier + ema;
        }
        return ema;
    }

    private static decimal Rsi(List<PriceCandle> candles, int period)
    {
        if (candles.Count < period + 1) return 50;

        decimal avgGain = 0, avgLoss = 0;
        for (int i = 1; i <= period; i++)
        {
            var change = candles[i].Close - candles[i - 1].Close;
            if (change > 0) avgGain += change; else avgLoss += Math.Abs(change);
        }
        avgGain /= period;
        avgLoss /= period;

        for (int i = period + 1; i < candles.Count; i++)
        {
            var change = candles[i].Close - candles[i - 1].Close;
            var gain = change > 0 ? change : 0;
            var loss = change < 0 ? Math.Abs(change) : 0;
            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
        }

        if (avgLoss == 0 && avgGain == 0) return 50;
        if (avgLoss == 0) return 100;
        var rs = avgGain / avgLoss;
        return 100 - (100 / (1 + rs));
    }

    private static decimal Atr(List<PriceCandle> candles, int period)
    {
        if (candles.Count < period + 1) return 0;

        var trueRanges = new List<decimal>();
        for (int i = 1; i < candles.Count; i++)
        {
            var tr = Math.Max(candles[i].High - candles[i].Low,
                     Math.Max(Math.Abs(candles[i].High - candles[i - 1].Close),
                              Math.Abs(candles[i].Low - candles[i - 1].Close)));
            trueRanges.Add(tr);
        }

        var atr = trueRanges.Take(period).Average();
        for (int i = period; i < trueRanges.Count; i++)
        {
            atr = (atr * (period - 1) + trueRanges[i]) / period;
        }
        return atr;
    }

    #endregion
}
