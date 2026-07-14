/*
 * ============================================================================
 * AutoTrade-X - Market Intelligence Models
 * ============================================================================
 * Models for news sentiment, fear & greed, historical statistics,
 * and multi-timeframe market regime analysis
 * ============================================================================
 */

namespace AutoTradeX.Core.Models;

/// <summary>
/// Fear &amp; Greed Index data (from alternative.me)
/// ดัชนีความกลัว/ความโลภของตลาด
/// </summary>
public class FearGreedData
{
    /// <summary>ค่าดัชนี 0-100 (0 = Extreme Fear, 100 = Extreme Greed)</summary>
    public int Value { get; set; }

    /// <summary>คำอธิบาย เช่น "Extreme Fear", "Greed"</summary>
    public string Classification { get; set; } = "";

    /// <summary>ค่าเมื่อวาน (ใช้ดูทิศทาง)</summary>
    public int? YesterdayValue { get; set; }

    /// <summary>ค่าเฉลี่ย 7 วันย้อนหลัง</summary>
    public decimal? WeekAverage { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>true เมื่อดัชนีอยู่โซนสุดขั้ว (&lt; 20 หรือ &gt; 80)</summary>
    public bool IsExtreme => Value < 20 || Value > 80;
}

/// <summary>
/// News article with sentiment score
/// ข่าวพร้อมคะแนน Sentiment
/// </summary>
public class NewsArticle
{
    public string Title { get; set; } = "";
    public string Source { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTime PublishedAt { get; set; }

    /// <summary>คะแนน Sentiment ของข่าวนี้ (-100 ถึง +100)</summary>
    public int SentimentScore { get; set; }

    /// <summary>คำสำคัญที่ตรวจพบในหัวข้อข่าว</summary>
    public List<string> MatchedKeywords { get; set; } = new();

    /// <summary>true ถ้าข่าวเกี่ยวข้องกับเหรียญที่กำลังเทรดโดยตรง</summary>
    public bool IsCoinSpecific { get; set; }
}

/// <summary>
/// Aggregated news sentiment for a coin
/// สรุป Sentiment ข่าวรวมสำหรับเหรียญ
/// </summary>
public class NewsSentimentSummary
{
    /// <summary>สัญลักษณ์เหรียญ เช่น BTC</summary>
    public string BaseAsset { get; set; } = "";

    /// <summary>ข่าวทั้งหมดที่วิเคราะห์ (เรียงใหม่สุดก่อน)</summary>
    public List<NewsArticle> Articles { get; set; } = new();

    /// <summary>คะแนนรวมตลาด (-100 ถึง +100) ถ่วงน้ำหนักตามความใหม่ของข่าว</summary>
    public int MarketScore { get; set; }

    /// <summary>คะแนนเฉพาะเหรียญนี้ (-100 ถึง +100) — null ถ้าไม่มีข่าวเฉพาะเหรียญ</summary>
    public int? CoinScore { get; set; }

    public int BullishCount { get; set; }
    public int BearishCount { get; set; }
    public int NeutralCount { get; set; }

    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

    /// <summary>คะแนนที่ใช้ตัดสินใจ: ใช้คะแนนเหรียญถ้ามี ไม่งั้นใช้คะแนนตลาด</summary>
    public int EffectiveScore => CoinScore ?? MarketScore;
}

/// <summary>
/// Historical statistics for a coin (from CoinGecko)
/// สถิติย้อนหลังของเหรียญจากแหล่งข้อมูลที่เชื่อถือได้
/// </summary>
public class CoinHistoricalStats
{
    public string BaseAsset { get; set; } = "";
    public string CoinGeckoId { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>อันดับ Market Cap (1 = ใหญ่สุด)</summary>
    public int MarketCapRank { get; set; }

    /// <summary>เปลี่ยนแปลงราคา 7 วัน (%)</summary>
    public decimal PriceChange7d { get; set; }

    /// <summary>เปลี่ยนแปลงราคา 30 วัน (%)</summary>
    public decimal PriceChange30d { get; set; }

    /// <summary>ความผันผวนรายวันเฉลี่ย 30 วัน (stddev ของ daily returns, %)</summary>
    public decimal DailyVolatility30d { get; set; }

    /// <summary>Max Drawdown ใน 30 วัน (%) — ค่าบวก เช่น 15 = เคยร่วง 15% จากจุดสูงสุด</summary>
    public decimal MaxDrawdown30d { get; set; }

    /// <summary>ระยะห่างจาก All-Time High (%) — ค่าลบ เช่น -35 = ต่ำกว่า ATH อยู่ 35%</summary>
    public decimal AthDistancePercent { get; set; }

    /// <summary>Sharpe Ratio 30 วัน (return/volatility) — ยิ่งสูงยิ่งดี</summary>
    public decimal SharpeRatio30d { get; set; }

    /// <summary>สัดส่วน Volume 24h ต่อ Market Cap — ยิ่งสูง = สภาพคล่องดี</summary>
    public decimal VolumeToMarketCap { get; set; }

    /// <summary>ราคาต่ำสุด 30 วัน (ใช้เป็นแนวรับอ้างอิง)</summary>
    public decimal Low30d { get; set; }

    /// <summary>ราคาสูงสุด 30 วัน (ใช้เป็นแนวต้านอ้างอิง)</summary>
    public decimal High30d { get; set; }

    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Market regime classification
/// การจำแนกสภาวะตลาด
/// </summary>
public enum MarketRegime
{
    /// <summary>ขาขึ้นแรง — เทรนด์ชัด volume หนุน</summary>
    StrongUptrend,

    /// <summary>ขาขึ้น</summary>
    Uptrend,

    /// <summary>ไซด์เวย์/แกว่งในกรอบ</summary>
    Sideways,

    /// <summary>ขาลง</summary>
    Downtrend,

    /// <summary>ขาลงแรง</summary>
    StrongDowntrend,

    /// <summary>ผันผวนสูงผิดปกติ ทิศทางไม่ชัด</summary>
    HighVolatility
}

/// <summary>
/// Trend analysis result for a single timeframe
/// ผลวิเคราะห์เทรนด์ของ 1 timeframe
/// </summary>
public class TimeframeTrend
{
    /// <summary>ช่วงเวลา เช่น "15m", "1h", "4h"</summary>
    public string Interval { get; set; } = "";

    /// <summary>ทิศทาง: +1 ขึ้น, 0 ไซด์เวย์, -1 ลง</summary>
    public int Direction { get; set; }

    /// <summary>ความแรงของเทรนด์ 0-100</summary>
    public int Strength { get; set; }

    public decimal? RSI { get; set; }
    public decimal? EmaFast { get; set; }
    public decimal? EmaSlow { get; set; }

    /// <summary>ATR เทียบราคา (%) ของ timeframe นี้</summary>
    public decimal AtrPercent { get; set; }

    public string Description { get; set; } = "";
}

/// <summary>
/// Multi-timeframe analysis result
/// ผลวิเคราะห์หลาย timeframe รวมกัน
/// </summary>
public class MultiTimeframeAnalysis
{
    public string Symbol { get; set; } = "";
    public string Exchange { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>เทรนด์ของแต่ละ timeframe</summary>
    public List<TimeframeTrend> Trends { get; set; } = new();

    /// <summary>
    /// คะแนนความสอดคล้อง -100 ถึง +100
    /// (+100 = ทุก timeframe ขึ้นพร้อมกัน, -100 = ลงพร้อมกัน)
    /// </summary>
    public int ConfluenceScore { get; set; }

    /// <summary>สภาวะตลาดโดยรวม</summary>
    public MarketRegime Regime { get; set; } = MarketRegime.Sideways;

    /// <summary>คำอธิบายภาษาไทย</summary>
    public string RegimeDescription { get; set; } = "";

    /// <summary>true ถ้าตลาดมีเทรนด์ชัดเจน (ขึ้นหรือลง)</summary>
    public bool IsTrending =>
        Regime is MarketRegime.StrongUptrend or MarketRegime.Uptrend
                or MarketRegime.Downtrend or MarketRegime.StrongDowntrend;
}

/// <summary>
/// Complete market intelligence snapshot combining all external data
/// ภาพรวมข้อมูลอัจฉริยะทั้งหมด: ข่าว + Fear/Greed + สถิติย้อนหลัง
/// </summary>
public class MarketIntelligence
{
    /// <summary>สัญลักษณ์เหรียญ เช่น BTC</summary>
    public string BaseAsset { get; set; } = "";

    public FearGreedData? FearGreed { get; set; }
    public NewsSentimentSummary? News { get; set; }
    public CoinHistoricalStats? Stats { get; set; }

    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// คะแนนรวม -100 ถึง +100 จากทุกแหล่งข้อมูล
    /// (ข่าว 50% + Fear/Greed 30% + สถิติ 20%)
    /// </summary>
    public int CompositeScore { get; set; }

    /// <summary>สรุปภาษาไทยสั้นๆ สำหรับแสดงใน UI</summary>
    public string Summary { get; set; } = "";

    /// <summary>true ถ้ามีข้อมูลอย่างน้อย 1 แหล่ง</summary>
    public bool HasAnyData => FearGreed != null || News != null || Stats != null;
}
