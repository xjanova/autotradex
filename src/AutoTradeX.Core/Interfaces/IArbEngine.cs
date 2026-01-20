/*
 * ============================================================================
 * AutoTrade-X - Cross-Exchange Arbitrage Trading Bot
 * ============================================================================
 * ⚠️ Educational/Experimental Only - No profit guarantee
 * ============================================================================
 */

using AutoTradeX.Core.Models;

namespace AutoTradeX.Core.Interfaces;

/// <summary>
/// IArbEngine - Interface สำหรับ Engine ที่ทำ Arbitrage
///
/// หน้าที่หลัก:
/// 1. ตรวจหาโอกาส Arbitrage จากราคาของทั้งสอง Exchange
/// 2. ตรวจสอบเงื่อนไขก่อนเทรด (balance, depth, risk limits)
/// 3. Execute การเทรดทั้งสองฝั่งพร้อมกัน
/// 4. จัดการ error และ partial fill
/// </summary>
public interface IArbEngine : IDisposable
{
    // ========== State Properties ==========

    /// <summary>
    /// บอทกำลังทำงานอยู่หรือไม่
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// สถานะปัจจุบันของ Engine
    /// </summary>
    ArbEngineStatus Status { get; }

    /// <summary>
    /// กำไร/ขาดทุนสะสมวันนี้
    /// </summary>
    decimal TodayPnL { get; }

    /// <summary>
    /// จำนวนเทรดวันนี้
    /// </summary>
    int TodayTradeCount { get; }

    /// <summary>
    /// error ล่าสุด
    /// </summary>
    string? LastError { get; }

    // ========== Events ==========

    /// <summary>
    /// Event เมื่อพบโอกาส Arbitrage ใหม่
    /// </summary>
    event EventHandler<OpportunityEventArgs>? OpportunityFound;

    /// <summary>
    /// Event เมื่อเทรดเสร็จสิ้น
    /// </summary>
    event EventHandler<TradeCompletedEventArgs>? TradeCompleted;

    /// <summary>
    /// Event เมื่อสถานะเปลี่ยน
    /// </summary>
    event EventHandler<EngineStatusEventArgs>? StatusChanged;

    /// <summary>
    /// Event เมื่อเกิด error
    /// </summary>
    event EventHandler<EngineErrorEventArgs>? ErrorOccurred;

    /// <summary>
    /// Event เมื่อราคาอัพเดท
    /// </summary>
    event EventHandler<PriceUpdateEventArgs>? PriceUpdated;

    /// <summary>
    /// Event เมื่อ Balance Pool อัพเดท
    /// </summary>
    event EventHandler<BalancePoolUpdateEventArgs>? BalancePoolUpdated;

    /// <summary>
    /// Event เมื่อเกิด Emergency (เช่น ขาดทุนหนัก, imbalance)
    /// </summary>
    event EventHandler<EmergencyProtectionEventArgs>? EmergencyTriggered;

    // ========== Core Operations ==========

    /// <summary>
    /// เริ่มการทำงานของ Engine
    /// จะเริ่ม polling ราคาและตรวจหาโอกาส Arbitrage
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// หยุดการทำงานของ Engine
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// หยุดชั่วคราว
    /// </summary>
    void Pause();

    /// <summary>
    /// ทำงานต่อหลังจากหยุดชั่วคราว
    /// </summary>
    void Resume();

    /// <summary>
    /// คำนวณโอกาส Arbitrage จากข้อมูลปัจจุบัน
    /// </summary>
    /// <param name="pair">คู่เทรดที่ต้องการคำนวณ</param>
    /// <returns>ผลการคำนวณโอกาส</returns>
    Task<SpreadOpportunity> AnalyzeOpportunityAsync(TradingPair pair, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute Arbitrage Trade
    /// ส่งคำสั่งซื้อและขายพร้อมกัน (หรือใกล้เคียงมากที่สุด)
    /// </summary>
    /// <param name="opportunity">โอกาสที่ต้องการเทรด</param>
    /// <returns>ผลลัพธ์การเทรด</returns>
    Task<TradeResult> ExecuteArbitrageAsync(SpreadOpportunity opportunity, CancellationToken cancellationToken = default);

    // ========== Configuration ==========

    /// <summary>
    /// อัพเดท config ขณะทำงาน
    /// </summary>
    void UpdateConfig(AppConfig config);

    /// <summary>
    /// ดึง config ปัจจุบัน
    /// </summary>
    AppConfig GetCurrentConfig();

    // ========== Trading Pairs Management ==========

    /// <summary>
    /// เพิ่มคู่เทรดที่ต้องการติดตาม
    /// </summary>
    void AddTradingPair(TradingPair pair);

    /// <summary>
    /// ลบคู่เทรดออก
    /// </summary>
    void RemoveTradingPair(string symbol);

    /// <summary>
    /// ดึงรายการคู่เทรดทั้งหมด
    /// </summary>
    IReadOnlyList<TradingPair> GetTradingPairs();

    // ========== Statistics ==========

    /// <summary>
    /// ดึงสถิติรายวัน
    /// </summary>
    DailyPnL GetTodayStats();

    /// <summary>
    /// ดึงประวัติเทรด
    /// </summary>
    /// <param name="count">จำนวนที่ต้องการ</param>
    IReadOnlyList<TradeResult> GetTradeHistory(int count = 100);

    /// <summary>
    /// รีเซ็ตสถิติรายวัน (ใช้ตอนเริ่มวันใหม่)
    /// </summary>
    void ResetDailyStats();
}

/// <summary>
/// สถานะของ Arb Engine
/// </summary>
public enum ArbEngineStatus
{
    /// <summary>ยังไม่เริ่ม</summary>
    Idle,
    /// <summary>กำลังเริ่มต้น</summary>
    Starting,
    /// <summary>กำลังทำงาน - รอโอกาส</summary>
    Running,
    /// <summary>กำลังเทรด</summary>
    Trading,
    /// <summary>หยุดชั่วคราว</summary>
    Paused,
    /// <summary>หยุดทำงาน</summary>
    Stopped,
    /// <summary>หยุดเพราะเกิน Risk Limit</summary>
    StoppedByRiskLimit,
    /// <summary>Error</summary>
    Error
}
