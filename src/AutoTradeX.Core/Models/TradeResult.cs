// AutoTrade-X v1.0.0

namespace AutoTradeX.Core.Models;

public enum TradeResultStatus { Success, PartialSuccess, OneSideFailed, BothFailed, Cancelled, Error }

public class TradeResult
{
    public string TradeId { get; set; } = Guid.NewGuid().ToString("N");
    public string Symbol { get; set; } = string.Empty;
    public ArbitrageDirection Direction { get; set; }
    public TradeResultStatus Status { get; set; }
    public Order? BuyOrder { get; set; }
    public Order? SellOrder { get; set; }
    public SpreadOpportunity? Opportunity { get; set; }

    // Financial results
    public decimal ActualBuyValue => BuyOrder?.FilledValue ?? 0;
    public decimal ActualSellValue => SellOrder?.FilledValue ?? 0;
    public decimal BuyFee => BuyOrder?.Fee ?? 0;
    public decimal SellFee => SellOrder?.Fee ?? 0;
    public decimal TotalFees => BuyFee + SellFee;
    public decimal GrossPnL => ActualSellValue - ActualBuyValue;
    public decimal NetPnL { get; set; }
    public decimal PnLPercentage => ActualBuyValue > 0 ? (NetPnL / ActualBuyValue) * 100 : 0;

    // Timing
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }
    public long DurationMs => EndTime.HasValue ? (long)(EndTime.Value - StartTime).TotalMilliseconds : 0;

    // Error handling
    public string? ErrorMessage { get; set; }
    public List<string> ErrorDetails { get; set; } = new();
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage) || ErrorDetails.Count > 0;

    // Metadata
    public string? Notes { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public bool IsFullySuccessful => Status == TradeResultStatus.Success;

    public override string ToString() =>
        $"[{TradeId}] {Symbol} {Direction}: Status={Status}, NetPnL={NetPnL:F4} USDT ({PnLPercentage:F4}%), Duration={DurationMs}ms";
}

public class DailyPnL
{
    public DateOnly Date { get; set; }
    public int TotalTrades { get; set; }
    public int SuccessfulTrades { get; set; }
    public int FailedTrades { get; set; }
    public decimal TotalNetPnL { get; set; }
    public decimal TotalProfit { get; set; }
    public decimal TotalLoss { get; set; }
    public decimal TotalFees { get; set; }
    public decimal TotalVolume { get; set; }
    public decimal WinRate => TotalTrades > 0 ? (decimal)SuccessfulTrades / TotalTrades * 100 : 0;
    public decimal AveragePnLPerTrade => TotalTrades > 0 ? TotalNetPnL / TotalTrades : 0;
}
