// AutoTrade-X v1.0.0 - Event Arguments

using AutoTradeX.Core.Interfaces;

namespace AutoTradeX.Core.Models;

/// <summary>
/// Event arguments สำหรับ Engine status changes
/// </summary>
public class EngineStatusEventArgs : EventArgs
{
    public EngineStatus Status { get; }
    public string? Message { get; }
    public DateTime Timestamp { get; }

    public EngineStatusEventArgs(EngineStatus status, string? message = null)
    {
        Status = status;
        Message = message;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// สถานะของ Engine (simplified)
/// </summary>
public enum EngineStatus
{
    Idle,
    Starting,
    Running,
    Paused,
    Stopping,
    Stopped,
    Error
}

/// <summary>
/// Event arguments สำหรับ Trade completed
/// </summary>
public class TradeCompletedEventArgs : EventArgs
{
    public TradeResult Result { get; }
    public DateTime Timestamp { get; }

    public TradeCompletedEventArgs(TradeResult result)
    {
        Result = result;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// Event arguments สำหรับ Opportunity found
/// </summary>
public class OpportunityEventArgs : EventArgs
{
    public SpreadOpportunity Opportunity { get; }
    public TradingPair Pair { get; }
    public DateTime Timestamp { get; }

    public OpportunityEventArgs(SpreadOpportunity opportunity, TradingPair pair)
    {
        Opportunity = opportunity;
        Pair = pair;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// Event arguments สำหรับ Price updates
/// </summary>
public class PriceUpdateEventArgs : EventArgs
{
    public string Exchange { get; }
    public string Symbol { get; }
    public Ticker Ticker { get; }
    public DateTime Timestamp { get; }

    public PriceUpdateEventArgs(string exchange, string symbol, Ticker ticker)
    {
        Exchange = exchange;
        Symbol = symbol;
        Ticker = ticker;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// Event arguments สำหรับ Errors
/// </summary>
public class EngineErrorEventArgs : EventArgs
{
    public string Message { get; }
    public Exception? Exception { get; }
    public string? Source { get; }
    public DateTime Timestamp { get; }

    public EngineErrorEventArgs(string message, Exception? exception = null, string? source = null)
    {
        Message = message;
        Exception = exception;
        Source = source;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// Event arguments สำหรับ Balance Pool updates
/// </summary>
public class BalancePoolUpdateEventArgs : EventArgs
{
    public decimal TotalValueUSDT { get; }
    public decimal RealizedPnL { get; }
    public decimal RealizedPnLPercent { get; }
    public decimal CurrentDrawdown { get; }
    public Dictionary<string, AssetBalanceInfo> AssetBalances { get; }
    public DateTime Timestamp { get; }

    public BalancePoolUpdateEventArgs(
        decimal totalValueUSDT,
        decimal realizedPnL,
        decimal realizedPnLPercent,
        decimal currentDrawdown,
        Dictionary<string, AssetBalanceInfo> assetBalances)
    {
        TotalValueUSDT = totalValueUSDT;
        RealizedPnL = realizedPnL;
        RealizedPnLPercent = realizedPnLPercent;
        CurrentDrawdown = currentDrawdown;
        AssetBalances = assetBalances;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// Asset balance info for events
/// </summary>
public class AssetBalanceInfo
{
    public string Asset { get; set; } = string.Empty;
    public decimal ExchangeA { get; set; }
    public decimal ExchangeB { get; set; }
    public decimal Total => ExchangeA + ExchangeB;
    public decimal ValueUSDT { get; set; }
    public decimal ChangeFromInitial { get; set; }
}

/// <summary>
/// Event arguments สำหรับ Emergency protection triggers
/// </summary>
public class EmergencyProtectionEventArgs : EventArgs
{
    public EmergencyTriggerType TriggerType { get; }
    public string Message { get; }
    public decimal CurrentValue { get; }
    public decimal Threshold { get; }
    public EmergencyActionType RecommendedAction { get; }
    public DateTime Timestamp { get; }

    public EmergencyProtectionEventArgs(
        EmergencyTriggerType triggerType,
        string message,
        decimal currentValue,
        decimal threshold,
        EmergencyActionType recommendedAction)
    {
        TriggerType = triggerType;
        Message = message;
        CurrentValue = currentValue;
        Threshold = threshold;
        RecommendedAction = recommendedAction;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// Emergency trigger types
/// </summary>
public enum EmergencyTriggerType
{
    None,
    MaxDrawdown,
    MaxDailyLoss,
    ConsecutiveLosses,
    RapidLoss,
    CriticalImbalance,
    BalanceDiscrepancy,
    ExchangeError
}

/// <summary>
/// Emergency action types
/// </summary>
public enum EmergencyActionType
{
    None,
    AlertOnly,
    PauseTrading,
    StopTrading,
    EmergencyHedge,
    ForceRebalance
}
