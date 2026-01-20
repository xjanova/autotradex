// AutoTrade-X v1.0.0
// Balance Pool Service - Real P&L Tracking from Actual Wallet Balances

using AutoTradeX.Core.Models;

namespace AutoTradeX.Core.Interfaces;

/// <summary>
/// IBalancePoolService - Interface for tracking real P&L from actual wallet balances
///
/// This service manages the "Profit Pool" concept:
/// - Tracks actual balances across both exchanges
/// - Calculates real P&L from wallet changes
/// - Provides emergency protection triggers
/// - Monitors for imbalances and loss conditions
/// </summary>
public interface IBalancePoolService
{
    /// <summary>
    /// Current combined balance pool state
    /// </summary>
    BalancePoolSnapshot CurrentSnapshot { get; }

    /// <summary>
    /// Initial balance snapshot when trading started
    /// </summary>
    BalancePoolSnapshot InitialSnapshot { get; }

    /// <summary>
    /// Real-time total PnL based on actual wallet balances
    /// </summary>
    decimal RealizedPnL { get; }

    /// <summary>
    /// Current drawdown from peak balance
    /// </summary>
    decimal CurrentDrawdown { get; }

    /// <summary>
    /// Maximum drawdown observed during session
    /// </summary>
    decimal MaxDrawdown { get; }

    /// <summary>
    /// Initialize the balance pool with current exchange balances
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Update balances from both exchanges
    /// </summary>
    Task UpdateBalancesAsync(CancellationToken ct = default);

    /// <summary>
    /// Record a trade result and update internal tracking
    /// </summary>
    void RecordTrade(TradeResult result);

    /// <summary>
    /// Calculate real PnL based on current vs initial balances
    /// </summary>
    BalancePoolPnL CalculateRealPnL();

    /// <summary>
    /// Check if emergency protection should be triggered
    /// </summary>
    EmergencyProtectionCheck CheckEmergencyProtection();

    /// <summary>
    /// Get balance status for a specific asset across both exchanges
    /// </summary>
    AssetPoolStatus GetAssetStatus(string asset);

    /// <summary>
    /// Get all tracked asset statuses
    /// </summary>
    IReadOnlyDictionary<string, AssetPoolStatus> GetAllAssetStatuses();

    /// <summary>
    /// Calculate the optimal rebalance needed to restore target ratios
    /// </summary>
    RebalanceRecommendation CalculateRebalance();

    /// <summary>
    /// Get historical snapshots
    /// </summary>
    IReadOnlyList<BalancePoolSnapshot> GetHistory(int count = 100);

    /// <summary>
    /// Event fired when balance is updated
    /// </summary>
    event EventHandler<BalanceUpdateEventArgs>? BalanceUpdated;

    /// <summary>
    /// Event fired when emergency condition is detected
    /// </summary>
    event EventHandler<EmergencyEventArgs>? EmergencyTriggered;

    /// <summary>
    /// Event fired when rebalance is recommended
    /// </summary>
    event EventHandler<RebalanceEventArgs>? RebalanceRecommended;
}

/// <summary>
/// Snapshot of balance pool at a point in time
/// </summary>
public class BalancePoolSnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public AccountBalance ExchangeA { get; set; } = new();
    public AccountBalance ExchangeB { get; set; } = new();

    /// <summary>
    /// Combined balances across both exchanges by asset
    /// </summary>
    public Dictionary<string, CombinedAssetBalance> CombinedBalances { get; set; } = new();

    /// <summary>
    /// Total value in quote currency (USDT)
    /// </summary>
    public decimal TotalValueUSDT { get; set; }

    /// <summary>
    /// Peak total value during session
    /// </summary>
    public decimal PeakValueUSDT { get; set; }
}

/// <summary>
/// Combined balance for an asset across both exchanges
/// </summary>
public class CombinedAssetBalance
{
    public string Asset { get; set; } = string.Empty;
    public decimal ExchangeA_Total { get; set; }
    public decimal ExchangeA_Available { get; set; }
    public decimal ExchangeB_Total { get; set; }
    public decimal ExchangeB_Available { get; set; }
    public decimal TotalBalance => ExchangeA_Total + ExchangeB_Total;
    public decimal TotalAvailable => ExchangeA_Available + ExchangeB_Available;
    public decimal ValueUSDT { get; set; }

    /// <summary>
    /// Ratio of balance on Exchange A (0.0 to 1.0)
    /// </summary>
    public decimal DistributionRatio => TotalBalance > 0 ? ExchangeA_Total / TotalBalance : 0.5m;
}

/// <summary>
/// Real P&L calculation result
/// </summary>
public class BalancePoolPnL
{
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// P&L by individual assets
    /// </summary>
    public Dictionary<string, AssetPnL> AssetPnLs { get; set; } = new();

    /// <summary>
    /// Total P&L in quote currency (USDT)
    /// </summary>
    public decimal TotalPnLUSDT { get; set; }

    /// <summary>
    /// Total P&L percentage
    /// </summary>
    public decimal TotalPnLPercent { get; set; }

    /// <summary>
    /// Breakdown by exchange
    /// </summary>
    public decimal ExchangeA_PnLUSDT { get; set; }
    public decimal ExchangeB_PnLUSDT { get; set; }

    /// <summary>
    /// Is overall in profit
    /// </summary>
    public bool IsProfit => TotalPnLUSDT > 0;
}

/// <summary>
/// P&L for a single asset
/// </summary>
public class AssetPnL
{
    public string Asset { get; set; } = string.Empty;
    public decimal InitialBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public decimal ChangeAmount => CurrentBalance - InitialBalance;
    public decimal ChangePercent => InitialBalance > 0 ? (ChangeAmount / InitialBalance) * 100 : 0;
    public decimal ValueChangeUSDT { get; set; }
}

/// <summary>
/// Status of a single asset in the pool
/// </summary>
public class AssetPoolStatus
{
    public string Asset { get; set; } = string.Empty;
    public CombinedAssetBalance Current { get; set; } = new();
    public CombinedAssetBalance Initial { get; set; } = new();
    public AssetPnL PnL { get; set; } = new();

    /// <summary>
    /// Is this asset critically imbalanced
    /// </summary>
    public bool IsCriticalImbalance { get; set; }

    /// <summary>
    /// Recommended action for this asset
    /// </summary>
    public string? RecommendedAction { get; set; }
}

/// <summary>
/// Emergency protection check result
/// </summary>
public class EmergencyProtectionCheck
{
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    public bool ShouldTrigger { get; set; }
    public EmergencyTriggerReason Reason { get; set; } = EmergencyTriggerReason.None;
    public string Message { get; set; } = string.Empty;
    public decimal CurrentLoss { get; set; }
    public decimal Threshold { get; set; }
    public EmergencyAction RecommendedAction { get; set; } = EmergencyAction.None;
}

/// <summary>
/// Reasons for emergency trigger
/// </summary>
public enum EmergencyTriggerReason
{
    None,
    MaxDrawdownExceeded,
    MaxLossExceeded,
    CriticalImbalance,
    RapidLossRate,
    ConsecutiveLosses,
    ExchangeError,
    BalanceDiscrepancy
}

/// <summary>
/// Recommended emergency action
/// </summary>
public enum EmergencyAction
{
    None,
    PauseTrading,
    StopTrading,
    EmergencyHedge,
    RebalanceImmediate,
    AlertOnly
}

/// <summary>
/// Rebalance recommendation
/// </summary>
public class RebalanceRecommendation
{
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRebalanceNeeded { get; set; }
    public RebalanceUrgency Urgency { get; set; } = RebalanceUrgency.None;
    public List<RebalanceAction> Actions { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// Urgency level for rebalance
/// </summary>
public enum RebalanceUrgency
{
    None,
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Single rebalance action
/// </summary>
public class RebalanceAction
{
    public string Asset { get; set; } = string.Empty;
    public string FromExchange { get; set; } = string.Empty;
    public string ToExchange { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
}

// Event Args

public class BalanceUpdateEventArgs : EventArgs
{
    public BalancePoolSnapshot Snapshot { get; }
    public BalancePoolPnL PnL { get; }

    public BalanceUpdateEventArgs(BalancePoolSnapshot snapshot, BalancePoolPnL pnl)
    {
        Snapshot = snapshot;
        PnL = pnl;
    }
}

public class EmergencyEventArgs : EventArgs
{
    public EmergencyProtectionCheck Check { get; }

    public EmergencyEventArgs(EmergencyProtectionCheck check)
    {
        Check = check;
    }
}

public class RebalanceEventArgs : EventArgs
{
    public RebalanceRecommendation Recommendation { get; }

    public RebalanceEventArgs(RebalanceRecommendation recommendation)
    {
        Recommendation = recommendation;
    }
}
