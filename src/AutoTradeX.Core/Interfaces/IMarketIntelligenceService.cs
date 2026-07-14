/*
 * ============================================================================
 * AutoTrade-X - Market Intelligence Service Interface
 * ============================================================================
 * Aggregates external market intelligence: crypto news sentiment,
 * Fear & Greed index, and historical coin statistics from trusted sources.
 * ============================================================================
 */

using AutoTradeX.Core.Models;

namespace AutoTradeX.Core.Interfaces;

/// <summary>
/// IMarketIntelligenceService - ดึงและวิเคราะห์ข้อมูลตลาดจากแหล่งภายนอก
/// (ข่าว, Fear &amp; Greed, สถิติย้อนหลัง) เพื่อประกอบการตัดสินใจเทรด
///
/// ทุก method คืน null เมื่อดึงข้อมูลไม่ได้ — ผู้เรียกต้องทำงานต่อได้เสมอ
/// (intelligence เป็นข้อมูลเสริม ห้าม block การเทรดเมื่อ network ล่ม)
/// </summary>
public interface IMarketIntelligenceService
{
    /// <summary>
    /// ดึงข้อมูลอัจฉริยะครบชุดสำหรับเหรียญ (มี cache ภายใน — เรียกถี่ได้)
    /// </summary>
    /// <param name="baseAsset">สัญลักษณ์เหรียญ เช่น "BTC"</param>
    Task<MarketIntelligence?> GetIntelligenceAsync(string baseAsset, CancellationToken cancellationToken = default);

    /// <summary>
    /// ดึงดัชนี Fear &amp; Greed ปัจจุบัน (cache 30 นาที)
    /// </summary>
    Task<FearGreedData?> GetFearGreedIndexAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// ดึงข่าวคริปโตล่าสุดพร้อมวิเคราะห์ Sentiment (cache 10 นาที)
    /// </summary>
    /// <param name="baseAsset">สัญลักษณ์เหรียญสำหรับกรองข่าวเฉพาะเหรียญ</param>
    Task<NewsSentimentSummary?> GetNewsSentimentAsync(string baseAsset, CancellationToken cancellationToken = default);

    /// <summary>
    /// ดึงสถิติย้อนหลัง 30 วันของเหรียญจาก CoinGecko (cache 30 นาที)
    /// </summary>
    Task<CoinHistoricalStats?> GetCoinStatsAsync(string baseAsset, CancellationToken cancellationToken = default);
}
