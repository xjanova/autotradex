/*
 * ============================================================================
 * AutoTrade-X - Trade History Persistence Service (SQLite)
 * ============================================================================
 * Handles saving and loading trade history to/from SQLite database
 * ============================================================================
 */

using System.Text.Json;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Infrastructure.Data;

namespace AutoTradeX.Infrastructure.Services;

public interface ITradeHistoryService
{
    Task SaveTradeAsync(TradeHistoryEntry trade);
    Task<List<TradeHistoryEntry>> GetTradesAsync(TradeHistoryFilter? filter = null);
    Task<TradeHistoryStats> GetStatsAsync(DateTime? fromDate = null, DateTime? toDate = null);
    Task<List<TradeHistoryEntry>> GetRecentTradesAsync(int count = 50);
    Task ExportToCsvAsync(string filePath, TradeHistoryFilter? filter = null);
    Task ClearHistoryAsync();
    Task<int> GetTotalTradeCountAsync();
    Task<DailyPnLRecord> GetTodayPnLAsync();
    Task<List<DailyPnLRecord>> GetDailyPnLHistoryAsync(int days = 30);
    Task<List<string>> GetDistinctSymbolsAsync();
    Task<List<string>> GetDistinctExchangesAsync();
    Task<int> CleanupOldDataAsync(int keepDays = 90);
    Task<long> GetDatabaseSizeAsync();
}

public class TradeHistoryService : ITradeHistoryService
{
    private readonly IDatabaseService _db;
    private readonly ILoggingService _logger;

    public TradeHistoryService(IDatabaseService databaseService, ILoggingService logger)
    {
        _db = databaseService;
        _logger = logger;
    }

    public async Task SaveTradeAsync(TradeHistoryEntry trade)
    {
        try
        {
            // บันทึกเทรด
            await _db.ExecuteAsync(@"
                INSERT OR REPLACE INTO Trades
                (Id, Timestamp, Symbol, BuyExchange, SellExchange, BuyPrice, SellPrice,
                 SpreadPercent, TradeAmount, PnL, Fee, Status, ExecutionTimeMs, Metadata)
                VALUES
                (@Id, @Timestamp, @Symbol, @BuyExchange, @SellExchange, @BuyPrice, @SellPrice,
                 @SpreadPercent, @TradeAmount, @PnL, @Fee, @Status, @ExecutionTimeMs, @Metadata)",
                new
                {
                    trade.Id,
                    Timestamp = trade.Timestamp.ToString("o"),
                    trade.Symbol,
                    trade.BuyExchange,
                    trade.SellExchange,
                    trade.BuyPrice,
                    trade.SellPrice,
                    trade.SpreadPercent,
                    trade.TradeAmount,
                    trade.PnL,
                    trade.Fee,
                    trade.Status,
                    trade.ExecutionTimeMs,
                    Metadata = JsonSerializer.Serialize(trade.Metadata)
                });

            // อัปเดต Daily PnL
            await UpdateDailyPnLAsync(trade);

            _logger.LogInfo("TradeHistory", $"Trade saved: {trade.Symbol} - PnL: ${trade.PnL:F2}");
        }
        catch (Exception ex)
        {
            _logger.LogError("TradeHistory", $"Error saving trade: {ex.Message}");
            throw;
        }
    }

    private async Task UpdateDailyPnLAsync(TradeHistoryEntry trade)
    {
        var date = trade.Timestamp.Date.ToString("yyyy-MM-dd");
        var isProfit = trade.PnL > 0;

        await _db.ExecuteAsync(@"
            INSERT INTO DailyPnL (Date, TotalTrades, SuccessfulTrades, FailedTrades, TotalNetPnL, TotalProfit, TotalLoss, TotalFees, TotalVolume)
            VALUES (@Date, 1, @Success, @Failed, @PnL, @Profit, @Loss, @Fee, @Volume)
            ON CONFLICT(Date) DO UPDATE SET
                TotalTrades = TotalTrades + 1,
                SuccessfulTrades = SuccessfulTrades + @Success,
                FailedTrades = FailedTrades + @Failed,
                TotalNetPnL = TotalNetPnL + @PnL,
                TotalProfit = TotalProfit + @Profit,
                TotalLoss = TotalLoss + @Loss,
                TotalFees = TotalFees + @Fee,
                TotalVolume = TotalVolume + @Volume",
            new
            {
                Date = date,
                Success = isProfit ? 1 : 0,
                Failed = isProfit ? 0 : 1,
                PnL = trade.PnL,
                Profit = isProfit ? trade.PnL : 0,
                Loss = !isProfit ? Math.Abs(trade.PnL) : 0,
                Fee = trade.Fee,
                Volume = trade.TradeAmount
            });
    }

    public async Task<List<TradeHistoryEntry>> GetTradesAsync(TradeHistoryFilter? filter = null)
    {
        var sql = "SELECT * FROM Trades WHERE 1=1";
        var parameters = new Dictionary<string, object>();

        if (filter != null)
        {
            if (filter.FromDate.HasValue)
            {
                sql += " AND Timestamp >= @FromDate";
                parameters["FromDate"] = filter.FromDate.Value.ToString("o");
            }

            if (filter.ToDate.HasValue)
            {
                sql += " AND Timestamp <= @ToDate";
                parameters["ToDate"] = filter.ToDate.Value.ToString("o");
            }

            if (!string.IsNullOrEmpty(filter.Symbol))
            {
                sql += " AND Symbol = @Symbol";
                parameters["Symbol"] = filter.Symbol;
            }

            if (!string.IsNullOrEmpty(filter.BuyExchange))
            {
                sql += " AND BuyExchange = @BuyExchange";
                parameters["BuyExchange"] = filter.BuyExchange;
            }

            if (!string.IsNullOrEmpty(filter.SellExchange))
            {
                sql += " AND SellExchange = @SellExchange";
                parameters["SellExchange"] = filter.SellExchange;
            }

            if (filter.OnlyProfitable.HasValue && filter.OnlyProfitable.Value)
            {
                sql += " AND PnL > 0";
            }

            if (filter.MinPnL.HasValue)
            {
                sql += " AND PnL >= @MinPnL";
                parameters["MinPnL"] = filter.MinPnL.Value;
            }

            if (filter.MaxPnL.HasValue)
            {
                sql += " AND PnL <= @MaxPnL";
                parameters["MaxPnL"] = filter.MaxPnL.Value;
            }
        }

        sql += " ORDER BY Timestamp DESC";

        var dbRecords = await _db.QueryAsync<TradeDbRecord>(sql, parameters);

        return dbRecords.Select(r => new TradeHistoryEntry
        {
            Id = r.Id,
            Timestamp = DateTime.Parse(r.Timestamp),
            Symbol = r.Symbol,
            BuyExchange = r.BuyExchange,
            SellExchange = r.SellExchange,
            BuyPrice = (decimal)r.BuyPrice,
            SellPrice = (decimal)r.SellPrice,
            SpreadPercent = (decimal)r.SpreadPercent,
            TradeAmount = (decimal)r.TradeAmount,
            PnL = (decimal)r.PnL,
            Fee = (decimal)r.Fee,
            Status = r.Status,
            ExecutionTimeMs = r.ExecutionTimeMs,
            Metadata = string.IsNullOrEmpty(r.Metadata)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(r.Metadata) ?? new()
        }).ToList();
    }

    public async Task<TradeHistoryStats> GetStatsAsync(DateTime? fromDate = null, DateTime? toDate = null)
    {
        var filter = new TradeHistoryFilter
        {
            FromDate = fromDate ?? DateTime.UtcNow.AddDays(-30),
            ToDate = toDate ?? DateTime.UtcNow
        };

        var trades = await GetTradesAsync(filter);

        if (trades.Count == 0)
        {
            return new TradeHistoryStats();
        }

        var profitable = trades.Where(t => t.PnL > 0).ToList();
        var losses = trades.Where(t => t.PnL < 0).ToList();

        return new TradeHistoryStats
        {
            TotalTrades = trades.Count,
            TotalPnL = trades.Sum(t => t.PnL),
            WinningTrades = profitable.Count,
            LosingTrades = losses.Count,
            WinRate = trades.Count > 0 ? (decimal)profitable.Count / trades.Count * 100 : 0,
            AverageWin = profitable.Count > 0 ? profitable.Average(t => t.PnL) : 0,
            AverageLoss = losses.Count > 0 ? Math.Abs(losses.Average(t => t.PnL)) : 0,
            LargestWin = profitable.Count > 0 ? profitable.Max(t => t.PnL) : 0,
            LargestLoss = losses.Count > 0 ? Math.Abs(losses.Min(t => t.PnL)) : 0,
            AverageSpread = trades.Average(t => t.SpreadPercent),
            AverageExecutionTime = trades.Average(t => t.ExecutionTimeMs),
            TotalVolume = trades.Sum(t => t.TradeAmount),
            ProfitFactor = losses.Sum(t => Math.Abs(t.PnL)) > 0
                ? profitable.Sum(t => t.PnL) / losses.Sum(t => Math.Abs(t.PnL))
                : profitable.Sum(t => t.PnL),
            MaxDrawdown = CalculateMaxDrawdown(trades),
            ByPairStats = CalculateByPairStats(trades)
        };
    }

    public async Task<List<TradeHistoryEntry>> GetRecentTradesAsync(int count = 50)
    {
        var dbRecords = await _db.QueryAsync<TradeDbRecord>(
            "SELECT * FROM Trades ORDER BY Timestamp DESC LIMIT @Count",
            new { Count = count });

        return dbRecords.Select(r => new TradeHistoryEntry
        {
            Id = r.Id,
            Timestamp = DateTime.Parse(r.Timestamp),
            Symbol = r.Symbol,
            BuyExchange = r.BuyExchange,
            SellExchange = r.SellExchange,
            BuyPrice = (decimal)r.BuyPrice,
            SellPrice = (decimal)r.SellPrice,
            SpreadPercent = (decimal)r.SpreadPercent,
            TradeAmount = (decimal)r.TradeAmount,
            PnL = (decimal)r.PnL,
            Fee = (decimal)r.Fee,
            Status = r.Status,
            ExecutionTimeMs = r.ExecutionTimeMs
        }).ToList();
    }

    public async Task<int> GetTotalTradeCountAsync()
    {
        return await _db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Trades");
    }

    public async Task<DailyPnLRecord> GetTodayPnLAsync()
    {
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        var record = await _db.QueryFirstOrDefaultAsync<DailyPnLRecord>(
            "SELECT * FROM DailyPnL WHERE Date = @Date",
            new { Date = today });

        return record ?? new DailyPnLRecord { Date = today };
    }

    public async Task<List<DailyPnLRecord>> GetDailyPnLHistoryAsync(int days = 30)
    {
        var fromDate = DateTime.UtcNow.AddDays(-days).Date.ToString("yyyy-MM-dd");
        var records = await _db.QueryAsync<DailyPnLRecord>(
            "SELECT * FROM DailyPnL WHERE Date >= @FromDate ORDER BY Date DESC",
            new { FromDate = fromDate });

        return records.ToList();
    }

    public async Task ExportToCsvAsync(string filePath, TradeHistoryFilter? filter = null)
    {
        var trades = await GetTradesAsync(filter);

        var csvLines = new List<string>
        {
            "Timestamp,Symbol,BuyExchange,SellExchange,BuyPrice,SellPrice,SpreadPercent,TradeAmount,PnL,Fee,Status,ExecutionTimeMs"
        };

        foreach (var trade in trades)
        {
            csvLines.Add($"{trade.Timestamp:yyyy-MM-dd HH:mm:ss},{trade.Symbol},{trade.BuyExchange},{trade.SellExchange},{trade.BuyPrice},{trade.SellPrice},{trade.SpreadPercent:F4},{trade.TradeAmount},{trade.PnL:F2},{trade.Fee:F4},{trade.Status},{trade.ExecutionTimeMs}");
        }

        await File.WriteAllLinesAsync(filePath, csvLines);
        _logger.LogInfo("TradeHistory", $"Exported {trades.Count} trades to {filePath}");
    }

    public async Task ClearHistoryAsync()
    {
        // Backup before clearing (export to CSV)
        var backupPath = Path.Combine(
            Path.GetDirectoryName(_db.DatabasePath) ?? "",
            $"trades_backup_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        await ExportToCsvAsync(backupPath);

        await _db.ExecuteAsync("DELETE FROM Trades");
        await _db.ExecuteAsync("DELETE FROM DailyPnL");

        _logger.LogInfo("TradeHistory", $"Trade history cleared. Backup saved to: {backupPath}");
    }

    private decimal CalculateMaxDrawdown(List<TradeHistoryEntry> trades)
    {
        if (trades.Count == 0) return 0;

        var orderedTrades = trades.OrderBy(t => t.Timestamp).ToList();
        decimal peak = 0;
        decimal maxDrawdown = 0;
        decimal runningPnL = 0;

        foreach (var trade in orderedTrades)
        {
            runningPnL += trade.PnL;

            if (runningPnL > peak)
            {
                peak = runningPnL;
            }

            var drawdown = peak - runningPnL;
            if (drawdown > maxDrawdown)
            {
                maxDrawdown = drawdown;
            }
        }

        return maxDrawdown;
    }

    private Dictionary<string, PairStats> CalculateByPairStats(List<TradeHistoryEntry> trades)
    {
        return trades
            .GroupBy(t => t.Symbol)
            .ToDictionary(
                g => g.Key,
                g => new PairStats
                {
                    Symbol = g.Key,
                    TotalTrades = g.Count(),
                    TotalPnL = g.Sum(t => t.PnL),
                    WinRate = g.Count() > 0 ? (decimal)g.Count(t => t.PnL > 0) / g.Count() * 100 : 0,
                    AverageSpread = g.Average(t => t.SpreadPercent),
                    TotalVolume = g.Sum(t => t.TradeAmount)
                }
            );
    }

    public async Task<List<string>> GetDistinctSymbolsAsync()
    {
        var records = await _db.QueryAsync<SymbolRecord>(
            "SELECT DISTINCT Symbol FROM Trades ORDER BY Symbol");
        return records.Select(r => r.Symbol).ToList();
    }

    public async Task<List<string>> GetDistinctExchangesAsync()
    {
        var buyExchanges = await _db.QueryAsync<ExchangeRecord>(
            "SELECT DISTINCT BuyExchange as Exchange FROM Trades");
        var sellExchanges = await _db.QueryAsync<ExchangeRecord>(
            "SELECT DISTINCT SellExchange as Exchange FROM Trades");

        return buyExchanges.Select(r => r.Exchange)
            .Union(sellExchanges.Select(r => r.Exchange))
            .Distinct()
            .OrderBy(e => e)
            .ToList();
    }

    public async Task<int> CleanupOldDataAsync(int keepDays = 90)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-keepDays).ToString("o");

        // Backup before cleanup
        var backupPath = Path.Combine(
            Path.GetDirectoryName(_db.DatabasePath) ?? "",
            $"trades_cleanup_backup_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        var oldTradesFilter = new TradeHistoryFilter
        {
            ToDate = DateTime.UtcNow.AddDays(-keepDays)
        };
        await ExportToCsvAsync(backupPath, oldTradesFilter);

        // Delete old trades
        var deletedTrades = await _db.ExecuteAsync(
            "DELETE FROM Trades WHERE Timestamp < @CutoffDate",
            new { CutoffDate = cutoffDate });

        // Delete old daily PnL
        var cutoffDateStr = DateTime.UtcNow.AddDays(-keepDays).Date.ToString("yyyy-MM-dd");
        await _db.ExecuteAsync(
            "DELETE FROM DailyPnL WHERE Date < @CutoffDate",
            new { CutoffDate = cutoffDateStr });

        // Vacuum to reclaim space
        await _db.ExecuteAsync("VACUUM");

        _logger.LogInfo("TradeHistory", $"Cleanup completed. Deleted {deletedTrades} trades older than {keepDays} days. Backup: {backupPath}");

        return deletedTrades;
    }

    public async Task<long> GetDatabaseSizeAsync()
    {
        try
        {
            var fileInfo = new FileInfo(_db.DatabasePath);
            return fileInfo.Exists ? fileInfo.Length : 0;
        }
        catch
        {
            return 0;
        }
    }
}

// Database record classes
internal class SymbolRecord
{
    public string Symbol { get; set; } = "";
}

internal class ExchangeRecord
{
    public string Exchange { get; set; } = "";
}

internal class TradeDbRecord
{
    public string Id { get; set; } = "";
    public string Timestamp { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string BuyExchange { get; set; } = "";
    public string SellExchange { get; set; } = "";
    public double BuyPrice { get; set; }
    public double SellPrice { get; set; }
    public double SpreadPercent { get; set; }
    public double TradeAmount { get; set; }
    public double PnL { get; set; }
    public double Fee { get; set; }
    public string Status { get; set; } = "";
    public long ExecutionTimeMs { get; set; }
    public string? Metadata { get; set; }
}

// Models
public class TradeHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Symbol { get; set; } = "";
    public string BuyExchange { get; set; } = "";
    public string SellExchange { get; set; } = "";
    public decimal BuyPrice { get; set; }
    public decimal SellPrice { get; set; }
    public decimal SpreadPercent { get; set; }
    public decimal TradeAmount { get; set; }
    public decimal PnL { get; set; }
    public decimal Fee { get; set; }
    public string Status { get; set; } = "Completed";
    public long ExecutionTimeMs { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class TradeHistoryFilter
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? Symbol { get; set; }
    public string? BuyExchange { get; set; }
    public string? SellExchange { get; set; }
    public bool? OnlyProfitable { get; set; }
    public decimal? MinPnL { get; set; }
    public decimal? MaxPnL { get; set; }
}

public class TradeHistoryStats
{
    public int TotalTrades { get; set; }
    public decimal TotalPnL { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public decimal WinRate { get; set; }
    public decimal AverageWin { get; set; }
    public decimal AverageLoss { get; set; }
    public decimal LargestWin { get; set; }
    public decimal LargestLoss { get; set; }
    public decimal AverageSpread { get; set; }
    public double AverageExecutionTime { get; set; }
    public decimal TotalVolume { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal MaxDrawdown { get; set; }
    public Dictionary<string, PairStats> ByPairStats { get; set; } = new();
}

public class PairStats
{
    public string Symbol { get; set; } = "";
    public int TotalTrades { get; set; }
    public decimal TotalPnL { get; set; }
    public decimal WinRate { get; set; }
    public decimal AverageSpread { get; set; }
    public decimal TotalVolume { get; set; }
}

public class DailyPnLRecord
{
    public string Date { get; set; } = "";
    public int TotalTrades { get; set; }
    public int SuccessfulTrades { get; set; }
    public int FailedTrades { get; set; }
    public double TotalNetPnL { get; set; }
    public double TotalProfit { get; set; }
    public double TotalLoss { get; set; }
    public double TotalFees { get; set; }
    public double TotalVolume { get; set; }
    public double WinRate => TotalTrades > 0 ? (double)SuccessfulTrades / TotalTrades * 100 : 0;
}
