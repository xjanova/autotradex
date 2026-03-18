/*
 * ============================================================================
 * AutoTrade-X - AI Trading Service Interface
 * ============================================================================
 * Interface for AI-powered single-exchange trading
 * ============================================================================
 */

using AutoTradeX.Core.Models;

namespace AutoTradeX.Core.Interfaces;

/// <summary>
/// IAITradingService - Interface for AI Trading operations
/// </summary>
public interface IAITradingService
{
    /// <summary>
    /// Service status
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Current active exchange
    /// </summary>
    string? ActiveExchange { get; }

    /// <summary>
    /// Current active symbol
    /// </summary>
    string? ActiveSymbol { get; }

    /// <summary>
    /// Start AI trading for a specific exchange and symbol
    /// </summary>
    Task StartAsync(string exchange, string symbol, AIStrategyConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop AI trading
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Pause AI trading (keep monitoring but don't trade)
    /// </summary>
    void Pause();

    /// <summary>
    /// Resume AI trading
    /// </summary>
    void Resume();

    /// <summary>
    /// Emergency stop - Close all positions immediately
    /// </summary>
    Task EmergencyStopAsync();

    /// <summary>
    /// Get current market data with indicators
    /// </summary>
    Task<AIMarketData?> GetMarketDataAsync(string exchange, string symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current AI signal for a symbol
    /// </summary>
    Task<AITradingSignal?> GetCurrentSignalAsync(string exchange, string symbol, AIStrategyConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current position
    /// </summary>
    AITradingPosition? GetCurrentPosition();

    /// <summary>
    /// Get session statistics
    /// </summary>
    AITradingSessionStats GetSessionStats();

    /// <summary>
    /// Get trade history for current session
    /// </summary>
    List<AITradeResult> GetTradeHistory();

    /// <summary>
    /// Execute a manual trade based on AI signal
    /// </summary>
    Task<AITradeResult?> ExecuteManualTradeAsync(AITradingSignal signal, decimal amount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Close current position manually
    /// </summary>
    Task<AITradeResult?> ClosePositionAsync(string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update strategy configuration on the fly
    /// </summary>
    void UpdateConfig(AIStrategyConfig config);

    /// <summary>
    /// Get price candles for chart
    /// </summary>
    Task<List<PriceCandle>> GetCandlesAsync(string exchange, string symbol, string interval = "1m", int limit = 100, CancellationToken cancellationToken = default);

    // Events
    event EventHandler<AISignalEventArgs>? SignalGenerated;
    event EventHandler<AIPositionEventArgs>? PositionOpened;
    event EventHandler<AIPositionEventArgs>? PositionClosed;
    event EventHandler<AITradeEventArgs>? TradeCompleted;
    event EventHandler<AIMarketDataEventArgs>? MarketDataUpdated;
    event EventHandler<AIEmergencyEventArgs>? EmergencyTriggered;
}

/// <summary>
/// Event args for AI signal
/// </summary>
public class AISignalEventArgs : EventArgs
{
    public AITradingSignal Signal { get; set; } = null!;
}

/// <summary>
/// Event args for AI position
/// </summary>
public class AIPositionEventArgs : EventArgs
{
    public AITradingPosition Position { get; set; } = null!;
}

/// <summary>
/// Event args for AI trade
/// </summary>
public class AITradeEventArgs : EventArgs
{
    public AITradeResult Trade { get; set; } = null!;
}

/// <summary>
/// Event args for market data
/// </summary>
public class AIMarketDataEventArgs : EventArgs
{
    public AIMarketData Data { get; set; } = null!;
}

/// <summary>
/// Event args for emergency
/// </summary>
public class AIEmergencyEventArgs : EventArgs
{
    public string Reason { get; set; } = "";
    public decimal? LossAmount { get; set; }
    public decimal? LossPercent { get; set; }
}
