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
/// LogLevel - ระดับความสำคัญของ Log
/// </summary>
public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}

/// <summary>
/// LogEntry - ข้อมูล Log หนึ่งรายการ
/// </summary>
public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public LogLevel Level { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
    public Dictionary<string, object>? Properties { get; set; }

    public override string ToString()
    {
        var ex = Exception != null ? $" | Exception: {Exception.Message}" : "";
        return $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level}] [{Category}] {Message}{ex}";
    }
}

/// <summary>
/// ILoggingService - Interface สำหรับ Logging
///
/// หน้าที่หลัก:
/// 1. บันทึก Log ทุกเหตุการณ์สำคัญ
/// 2. บันทึกลงไฟล์รายวัน
/// 3. รองรับหลายระดับ (Debug, Info, Warning, Error)
/// </summary>
public interface ILoggingService : IDisposable
{
    /// <summary>
    /// ระดับ Log ขั้นต่ำที่จะบันทึก
    /// </summary>
    LogLevel MinimumLevel { get; set; }

    /// <summary>
    /// Event เมื่อมี Log ใหม่ (สำหรับแสดงใน UI)
    /// </summary>
    event EventHandler<LogEntry>? LogAdded;

    // ========== Basic Logging ==========

    /// <summary>
    /// Log ข้อความ
    /// </summary>
    void Log(LogLevel level, string category, string message, Exception? exception = null, Dictionary<string, object>? properties = null);

    /// <summary>
    /// Log Debug
    /// </summary>
    void LogDebug(string category, string message);

    /// <summary>
    /// Log Info
    /// </summary>
    void LogInfo(string category, string message);

    /// <summary>
    /// Log Warning
    /// </summary>
    void LogWarning(string category, string message);

    /// <summary>
    /// Log Error
    /// </summary>
    void LogError(string category, string message, Exception? exception = null);

    /// <summary>
    /// Log Critical
    /// </summary>
    void LogCritical(string category, string message, Exception? exception = null);

    // ========== Trading-Specific Logging ==========

    /// <summary>
    /// Log เมื่อพบโอกาส Arbitrage
    /// </summary>
    void LogOpportunityFound(SpreadOpportunity opportunity);

    /// <summary>
    /// Log เมื่อส่งคำสั่งซื้อ/ขาย
    /// </summary>
    void LogOrderPlaced(OrderRequest request, string exchange);

    /// <summary>
    /// Log เมื่อ Order แมตช์
    /// </summary>
    void LogOrderFilled(Order order);

    /// <summary>
    /// Log ผลลัพธ์เทรด
    /// </summary>
    void LogTradeResult(TradeResult result);

    /// <summary>
    /// Log ข้อมูล Balance
    /// </summary>
    void LogBalanceUpdate(AccountBalance balance);

    // ========== File Management ==========

    /// <summary>
    /// Flush log buffer ลงไฟล์
    /// </summary>
    Task FlushAsync();

    /// <summary>
    /// ดึง Log ล่าสุด
    /// </summary>
    /// <param name="count">จำนวนที่ต้องการ</param>
    /// <param name="minLevel">ระดับขั้นต่ำ</param>
    IReadOnlyList<LogEntry> GetRecentLogs(int count = 100, LogLevel? minLevel = null);

    /// <summary>
    /// ลบ Log เก่า
    /// </summary>
    /// <param name="olderThanDays">เก่ากว่ากี่วัน</param>
    Task CleanupOldLogsAsync(int olderThanDays = 30);
}
