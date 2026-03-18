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

    // ========== Arbitrage Mode Properties / คุณสมบัติโหมด Arbitrage ==========

    /// <summary>
    /// Which execution mode was used for this trade
    /// โหมดการทำ Arbitrage ที่ใช้สำหรับเทรดนี้
    /// </summary>
    public ArbitrageExecutionMode ExecutionMode { get; set; } = ArbitrageExecutionMode.DualBalance;

    /// <summary>
    /// For Transfer Mode: transfer details and status
    /// สำหรับโหมดโอนจริง: รายละเอียดและสถานะการโอน
    /// </summary>
    public TransferStatus? TransferDetails { get; set; }

    /// <summary>
    /// Balance changes from this trade (for real P&L tracking)
    /// การเปลี่ยนแปลงยอดจากการเทรดนี้ (สำหรับติดตามกำไร/ขาดทุนจริง)
    /// </summary>
    public TradeBalanceChange? BalanceChange { get; set; }

    // Financial results
    public decimal ActualBuyValue => BuyOrder?.FilledValue ?? 0;
    public decimal ActualSellValue => SellOrder?.FilledValue ?? 0;
    public decimal BuyFee => BuyOrder?.Fee ?? 0;
    public decimal SellFee => SellOrder?.Fee ?? 0;
    public decimal TotalFees => BuyFee + SellFee;
    public decimal GrossPnL => ActualSellValue - ActualBuyValue;
    public decimal NetPnL { get; set; }
    public decimal PnLPercentage => ActualBuyValue > 0 ? (NetPnL / ActualBuyValue) * 100 : 0;

    /// <summary>
    /// Real P&L calculated from actual wallet balance changes
    /// กำไร/ขาดทุนจริงคำนวณจากการเปลี่ยนแปลงยอดกระเป๋าจริง
    /// </summary>
    public decimal RealPnL => BalanceChange?.NetProfitQuote ?? NetPnL;

    /// <summary>
    /// Whether this trade's P&L has been verified against actual balances
    /// กำไร/ขาดทุนของการเทรดนี้ถูกยืนยันกับยอดจริงหรือยัง
    /// </summary>
    public bool IsPnLVerified => BalanceChange != null;

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

    /// <summary>
    /// Whether this is a Transfer Mode trade with transfer in progress
    /// การเทรดนี้เป็นโหมดโอนจริงที่มีการโอนกำลังดำเนินการอยู่หรือไม่
    /// </summary>
    public bool IsTransferInProgress => ExecutionMode == ArbitrageExecutionMode.Transfer
                                      && TransferDetails?.IsActive == true;

    public override string ToString() =>
        $"[{TradeId}] {Symbol} {Direction} ({ExecutionMode}): Status={Status}, NetPnL={NetPnL:F4} USDT ({PnLPercentage:F4}%), " +
        $"RealPnL={RealPnL:F4} USDT, Duration={DurationMs}ms";

    /// <summary>
    /// Get summary in Thai
    /// รับข้อความสรุปภาษาไทย
    /// </summary>
    public string ToStringThai()
    {
        var modeText = ExecutionMode == ArbitrageExecutionMode.DualBalance
            ? "โหมดสองกระเป๋า"
            : "โหมดโอนจริง";
        var statusText = Status switch
        {
            TradeResultStatus.Success => "สำเร็จ",
            TradeResultStatus.PartialSuccess => "สำเร็จบางส่วน",
            TradeResultStatus.OneSideFailed => "ฝั่งเดียวล้มเหลว",
            TradeResultStatus.BothFailed => "ล้มเหลวทั้งคู่",
            TradeResultStatus.Cancelled => "ยกเลิก",
            TradeResultStatus.Error => "ผิดพลาด",
            _ => "ไม่ทราบ"
        };
        return $"[{TradeId}] {Symbol} ({modeText}): สถานะ={statusText}, กำไรสุทธิ={NetPnL:F4} USDT ({PnLPercentage:F4}%), " +
               $"กำไรจริง={RealPnL:F4} USDT, ระยะเวลา={DurationMs}ms";
    }
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
