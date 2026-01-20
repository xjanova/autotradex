/*
 * ============================================================================
 * AutoTrade-X - Balance History Service (SQLite)
 * ============================================================================
 * Handles saving and loading balance history to/from SQLite database
 * ============================================================================
 */

using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;

namespace AutoTradeX.Infrastructure.Data;

public interface IBalanceHistoryService
{
    Task SaveBalanceSnapshotAsync(string exchange, AccountBalance balance);
    Task SaveBalanceSnapshotAsync(string exchange, string asset, decimal total, decimal available, decimal? valueUSDT = null);
    Task<List<BalanceHistoryRecord>> GetBalanceHistoryAsync(string? exchange = null, string? asset = null, DateTime? fromDate = null, int limit = 1000);
    Task<BalanceHistoryRecord?> GetLatestBalanceAsync(string exchange, string asset);
    Task<Dictionary<string, decimal>> GetCurrentBalancesAsync(string exchange);
    Task<List<BalanceSummary>> GetBalanceSummaryAsync();
    Task CleanupOldRecordsAsync(int retentionDays = 90);
}

public class BalanceHistoryService : IBalanceHistoryService
{
    private readonly IDatabaseService _db;
    private readonly ILoggingService _logger;

    public BalanceHistoryService(IDatabaseService databaseService, ILoggingService logger)
    {
        _db = databaseService;
        _logger = logger;
    }

    public async Task SaveBalanceSnapshotAsync(string exchange, AccountBalance balance)
    {
        foreach (var asset in balance.Assets)
        {
            await SaveBalanceSnapshotAsync(exchange, asset.Key, asset.Value.Total, asset.Value.Available);
        }
    }

    public async Task SaveBalanceSnapshotAsync(string exchange, string asset, decimal total, decimal available, decimal? valueUSDT = null)
    {
        try
        {
            await _db.ExecuteAsync(@"
                INSERT INTO Balances (Timestamp, Exchange, Asset, Total, Available, ValueUSDT)
                VALUES (@Timestamp, @Exchange, @Asset, @Total, @Available, @ValueUSDT)",
                new
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Exchange = exchange,
                    Asset = asset.ToUpperInvariant(),
                    Total = total,
                    Available = available,
                    ValueUSDT = valueUSDT
                });
        }
        catch (Exception ex)
        {
            _logger.LogError("BalanceHistory", $"Error saving balance: {ex.Message}");
        }
    }

    public async Task<List<BalanceHistoryRecord>> GetBalanceHistoryAsync(
        string? exchange = null,
        string? asset = null,
        DateTime? fromDate = null,
        int limit = 1000)
    {
        var sql = "SELECT * FROM Balances WHERE 1=1";
        var parameters = new Dictionary<string, object>();

        if (!string.IsNullOrEmpty(exchange))
        {
            sql += " AND Exchange = @Exchange";
            parameters["Exchange"] = exchange;
        }

        if (!string.IsNullOrEmpty(asset))
        {
            sql += " AND Asset = @Asset";
            parameters["Asset"] = asset.ToUpperInvariant();
        }

        if (fromDate.HasValue)
        {
            sql += " AND Timestamp >= @FromDate";
            parameters["FromDate"] = fromDate.Value.ToString("o");
        }

        sql += " ORDER BY Timestamp DESC LIMIT @Limit";
        parameters["Limit"] = limit;

        var records = await _db.QueryAsync<BalanceDbRecord>(sql, parameters);

        return records.Select(r => new BalanceHistoryRecord
        {
            Id = r.Id,
            Timestamp = DateTime.Parse(r.Timestamp),
            Exchange = r.Exchange,
            Asset = r.Asset,
            Total = (decimal)r.Total,
            Available = (decimal)r.Available,
            ValueUSDT = r.ValueUSDT.HasValue ? (decimal)r.ValueUSDT.Value : null
        }).ToList();
    }

    public async Task<BalanceHistoryRecord?> GetLatestBalanceAsync(string exchange, string asset)
    {
        var record = await _db.QueryFirstOrDefaultAsync<BalanceDbRecord>(@"
            SELECT * FROM Balances
            WHERE Exchange = @Exchange AND Asset = @Asset
            ORDER BY Timestamp DESC LIMIT 1",
            new { Exchange = exchange, Asset = asset.ToUpperInvariant() });

        if (record == null) return null;

        return new BalanceHistoryRecord
        {
            Id = record.Id,
            Timestamp = DateTime.Parse(record.Timestamp),
            Exchange = record.Exchange,
            Asset = record.Asset,
            Total = (decimal)record.Total,
            Available = (decimal)record.Available,
            ValueUSDT = record.ValueUSDT.HasValue ? (decimal)record.ValueUSDT.Value : null
        };
    }

    public async Task<Dictionary<string, decimal>> GetCurrentBalancesAsync(string exchange)
    {
        // ดึงยอดล่าสุดของแต่ละ asset
        var records = await _db.QueryAsync<BalanceDbRecord>(@"
            SELECT b.* FROM Balances b
            INNER JOIN (
                SELECT Asset, MAX(Timestamp) as MaxTime
                FROM Balances
                WHERE Exchange = @Exchange
                GROUP BY Asset
            ) latest ON b.Asset = latest.Asset AND b.Timestamp = latest.MaxTime
            WHERE b.Exchange = @Exchange",
            new { Exchange = exchange });

        return records.ToDictionary(
            r => r.Asset,
            r => (decimal)r.Available
        );
    }

    public async Task<List<BalanceSummary>> GetBalanceSummaryAsync()
    {
        var records = await _db.QueryAsync<BalanceSummaryDbRecord>(@"
            SELECT
                Exchange,
                Asset,
                MAX(Total) as Total,
                MAX(Available) as Available,
                MAX(Timestamp) as LastUpdated,
                COUNT(*) as RecordCount
            FROM Balances
            GROUP BY Exchange, Asset
            ORDER BY Exchange, Asset");

        return records.Select(r => new BalanceSummary
        {
            Exchange = r.Exchange,
            Asset = r.Asset,
            Total = (decimal)r.Total,
            Available = (decimal)r.Available,
            LastUpdated = DateTime.Parse(r.LastUpdated),
            RecordCount = r.RecordCount
        }).ToList();
    }

    public async Task CleanupOldRecordsAsync(int retentionDays = 90)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays).ToString("o");

        var deleted = await _db.ExecuteAsync(
            "DELETE FROM Balances WHERE Timestamp < @CutoffDate",
            new { CutoffDate = cutoffDate });

        if (deleted > 0)
        {
            _logger.LogInfo("BalanceHistory", $"Cleaned up {deleted} old balance records");
        }
    }
}

// Database record classes
internal class BalanceDbRecord
{
    public int Id { get; set; }
    public string Timestamp { get; set; } = "";
    public string Exchange { get; set; } = "";
    public string Asset { get; set; } = "";
    public double Total { get; set; }
    public double Available { get; set; }
    public double? ValueUSDT { get; set; }
}

internal class BalanceSummaryDbRecord
{
    public string Exchange { get; set; } = "";
    public string Asset { get; set; } = "";
    public double Total { get; set; }
    public double Available { get; set; }
    public string LastUpdated { get; set; } = "";
    public int RecordCount { get; set; }
}

// Public models
public class BalanceHistoryRecord
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Exchange { get; set; } = "";
    public string Asset { get; set; } = "";
    public decimal Total { get; set; }
    public decimal Available { get; set; }
    public decimal Locked => Total - Available;
    public decimal? ValueUSDT { get; set; }
}

public class BalanceSummary
{
    public string Exchange { get; set; } = "";
    public string Asset { get; set; } = "";
    public decimal Total { get; set; }
    public decimal Available { get; set; }
    public decimal Locked => Total - Available;
    public DateTime LastUpdated { get; set; }
    public int RecordCount { get; set; }
}
