/*
 * ============================================================================
 * AutoTrade-X - Cross-Exchange Arbitrage Trading Bot
 * ============================================================================
 * ⚠️ Educational/Experimental Only - No profit guarantee
 * ============================================================================
 */

using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace AutoTradeX.Infrastructure.Services;

/// <summary>
/// FileLoggingService - Implementation ของ ILoggingService
///
/// หน้าที่หลัก:
/// 1. บันทึก Log ลงไฟล์รายวัน
/// 2. เก็บ Log ล่าสุดใน memory สำหรับแสดงใน UI
/// 3. รองรับหลายระดับ (Debug, Info, Warning, Error, Critical)
/// 4. ลบ Log เก่าอัตโนมัติ
/// </summary>
public class FileLoggingService : ILoggingService
{
    private readonly string _logDirectory;
    private readonly ConcurrentQueue<LogEntry> _recentLogs = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;

    private const int MaxRecentLogs = 1000;
    private StreamWriter? _currentWriter;
    private string? _currentLogFile;

    public LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public event EventHandler<LogEntry>? LogAdded;

    public FileLoggingService(string logDirectory = "logs", LogLevel minimumLevel = LogLevel.Info)
    {
        _logDirectory = logDirectory;
        MinimumLevel = minimumLevel;

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // สร้าง directory ถ้ายังไม่มี
        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }
    }

    #region Basic Logging

    /// <summary>
    /// Log ข้อความหลัก
    /// </summary>
    public void Log(
        LogLevel level,
        string category,
        string message,
        Exception? exception = null,
        Dictionary<string, object>? properties = null)
    {
        // ตรวจสอบ minimum level
        if (level < MinimumLevel) return;

        var entry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Category = category,
            Message = message,
            Exception = exception,
            Properties = properties
        };

        // เพิ่มเข้า queue
        _recentLogs.Enqueue(entry);
        while (_recentLogs.Count > MaxRecentLogs)
        {
            _recentLogs.TryDequeue(out _);
        }

        // เขียนลงไฟล์ (async)
        _ = WriteToFileAsync(entry);

        // Fire event
        LogAdded?.Invoke(this, entry);
    }

    public void LogDebug(string category, string message)
        => Log(LogLevel.Debug, category, message);

    public void LogInfo(string category, string message)
        => Log(LogLevel.Info, category, message);

    public void LogWarning(string category, string message)
        => Log(LogLevel.Warning, category, message);

    public void LogError(string category, string message, Exception? exception = null)
        => Log(LogLevel.Error, category, message, exception);

    public void LogCritical(string category, string message, Exception? exception = null)
        => Log(LogLevel.Critical, category, message, exception);

    #endregion

    #region Trading-Specific Logging

    /// <summary>
    /// Log เมื่อพบโอกาส Arbitrage
    /// </summary>
    public void LogOpportunityFound(SpreadOpportunity opportunity)
    {
        var props = new Dictionary<string, object>
        {
            ["symbol"] = opportunity.Symbol,
            ["direction"] = opportunity.Direction.ToString(),
            ["spreadPercent"] = opportunity.BestSpreadPercentage,
            ["netSpreadPercent"] = opportunity.NetSpreadPercentage,
            ["expectedProfit"] = opportunity.ExpectedNetProfitQuote,
            ["quantity"] = opportunity.SuggestedQuantity,
            ["shouldTrade"] = opportunity.ShouldTrade
        };

        Log(
            opportunity.ShouldTrade ? LogLevel.Info : LogLevel.Debug,
            "Opportunity",
            $"Found: {opportunity.Symbol} {opportunity.Direction} Spread={opportunity.NetSpreadPercentage:F4}% ExpectedProfit={opportunity.ExpectedNetProfitQuote:F4}",
            properties: props
        );
    }

    /// <summary>
    /// Log เมื่อส่งคำสั่งซื้อ/ขาย
    /// </summary>
    public void LogOrderPlaced(OrderRequest request, string exchange)
    {
        var props = new Dictionary<string, object>
        {
            ["exchange"] = exchange,
            ["clientOrderId"] = request.ClientOrderId,
            ["symbol"] = request.Symbol,
            ["side"] = request.Side.ToString(),
            ["type"] = request.Type.ToString(),
            ["quantity"] = request.Quantity,
            ["price"] = request.Price ?? 0
        };

        Log(
            LogLevel.Info,
            "Order",
            $"Placed: [{exchange}] {request.Side} {request.Quantity} {request.Symbol} @ {request.Price?.ToString("F8") ?? "MARKET"}",
            properties: props
        );
    }

    /// <summary>
    /// Log เมื่อ Order แมตช์
    /// </summary>
    public void LogOrderFilled(Order order)
    {
        var props = new Dictionary<string, object>
        {
            ["exchange"] = order.Exchange,
            ["orderId"] = order.OrderId,
            ["symbol"] = order.Symbol,
            ["side"] = order.Side.ToString(),
            ["status"] = order.Status.ToString(),
            ["filledQty"] = order.FilledQuantity,
            ["avgPrice"] = order.AverageFilledPrice ?? 0,
            ["fee"] = order.Fee
        };

        var level = order.Status == OrderStatus.Filled ? LogLevel.Info : LogLevel.Warning;

        Log(
            level,
            "Order",
            $"Filled: [{order.Exchange}] {order.OrderId} {order.Side} {order.FilledQuantity}/{order.RequestedQuantity} @ {order.AverageFilledPrice:F8} Status={order.Status}",
            properties: props
        );
    }

    /// <summary>
    /// Log ผลลัพธ์เทรด
    /// </summary>
    public void LogTradeResult(TradeResult result)
    {
        var props = new Dictionary<string, object>
        {
            ["tradeId"] = result.TradeId,
            ["symbol"] = result.Symbol,
            ["direction"] = result.Direction.ToString(),
            ["status"] = result.Status.ToString(),
            ["netPnL"] = result.NetPnL,
            ["pnLPercent"] = result.PnLPercentage,
            ["buyValue"] = result.ActualBuyValue,
            ["sellValue"] = result.ActualSellValue,
            ["totalFees"] = result.TotalFees,
            ["durationMs"] = result.DurationMs
        };

        var level = result.IsFullySuccessful ? LogLevel.Info : LogLevel.Warning;
        var emoji = result.NetPnL >= 0 ? "+" : "";

        Log(
            level,
            "Trade",
            $"Completed: [{result.TradeId}] {result.Symbol} {result.Direction} Status={result.Status} PnL={emoji}{result.NetPnL:F4} ({result.PnLPercentage:F4}%) Duration={result.DurationMs}ms",
            properties: props
        );
    }

    /// <summary>
    /// Log ข้อมูล Balance
    /// </summary>
    public void LogBalanceUpdate(AccountBalance balance)
    {
        var summary = string.Join(", ", balance.Assets
            .Where(a => a.Value.Total > 0)
            .Select(a => $"{a.Key}={a.Value.Available:F8}"));

        Log(
            LogLevel.Debug,
            "Balance",
            $"[{balance.Exchange}] {summary}"
        );
    }

    #endregion

    #region File Operations

    /// <summary>
    /// เขียน Log ลงไฟล์
    /// </summary>
    private async Task WriteToFileAsync(LogEntry entry)
    {
        await _writeLock.WaitAsync();
        try
        {
            var logFile = GetLogFilePath();

            // ถ้าเป็นวันใหม่ สร้างไฟล์ใหม่
            if (_currentLogFile != logFile)
            {
                _currentWriter?.Dispose();
                _currentWriter = new StreamWriter(logFile, append: true, Encoding.UTF8)
                {
                    AutoFlush = true
                };
                _currentLogFile = logFile;
            }

            // เขียน log
            await _currentWriter!.WriteLineAsync(FormatLogEntry(entry));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to write log: {ex.Message}");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Format log entry
    /// </summary>
    private string FormatLogEntry(LogEntry entry)
    {
        var sb = new StringBuilder();
        sb.Append($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] ");
        sb.Append($"[{entry.Level,-8}] ");
        sb.Append($"[{entry.Category,-15}] ");
        sb.Append(entry.Message);

        if (entry.Exception != null)
        {
            sb.AppendLine();
            sb.Append($"  Exception: {entry.Exception.GetType().Name}: {entry.Exception.Message}");
            if (entry.Exception.StackTrace != null)
            {
                sb.AppendLine();
                sb.Append($"  StackTrace: {entry.Exception.StackTrace}");
            }
        }

        if (entry.Properties != null && entry.Properties.Count > 0)
        {
            var json = JsonSerializer.Serialize(entry.Properties, _jsonOptions);
            sb.Append($" | {json}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// ดึง path ของ log file วันนี้
    /// </summary>
    private string GetLogFilePath()
    {
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        return Path.Combine(_logDirectory, $"autotradex_{date}.log");
    }

    /// <summary>
    /// Flush buffer
    /// </summary>
    public async Task FlushAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            if (_currentWriter != null)
            {
                await _currentWriter.FlushAsync();
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// ดึง Log ล่าสุด
    /// </summary>
    public IReadOnlyList<LogEntry> GetRecentLogs(int count = 100, LogLevel? minLevel = null)
    {
        var logs = _recentLogs.ToArray().TakeLast(count);

        if (minLevel.HasValue)
        {
            logs = logs.Where(l => l.Level >= minLevel.Value);
        }

        return logs.ToList();
    }

    /// <summary>
    /// ลบ Log เก่า
    /// </summary>
    public async Task CleanupOldLogsAsync(int olderThanDays = 30)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-olderThanDays);

        await Task.Run(() =>
        {
            var logFiles = Directory.GetFiles(_logDirectory, "autotradex_*.log");

            foreach (var file in logFiles)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTimeUtc < cutoffDate)
                    {
                        fileInfo.Delete();
                        Log(LogLevel.Info, "Cleanup", $"Deleted old log file: {file}");
                    }
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Warning, "Cleanup", $"Failed to delete log file: {file}", ex);
                }
            }
        });
    }

    #endregion

    public void Dispose()
    {
        _currentWriter?.Dispose();
        _writeLock?.Dispose();
    }
}
