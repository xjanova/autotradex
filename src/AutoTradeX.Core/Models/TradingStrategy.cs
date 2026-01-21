namespace AutoTradeX.Core.Models;

/// <summary>
/// AI Trading Strategy configuration
/// </summary>
public class TradingStrategy
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Default Strategy";
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Entry Conditions
    public EntryConditions Entry { get; set; } = new();

    // Exit Conditions
    public ExitConditions Exit { get; set; } = new();

    // Risk Management
    public RiskManagement Risk { get; set; } = new();

    // AI Settings
    public AISettings AI { get; set; } = new();

    // Advanced Settings
    public AdvancedSettings Advanced { get; set; } = new();
}

public class EntryConditions
{
    /// <summary>
    /// Minimum spread percentage to enter trade
    /// </summary>
    public decimal MinSpreadPercent { get; set; } = 0.15m;

    /// <summary>
    /// Maximum spread to avoid suspicious prices
    /// </summary>
    public decimal MaxSpreadPercent { get; set; } = 5.0m;

    /// <summary>
    /// Minimum volume in quote currency (e.g., USDT)
    /// </summary>
    public decimal MinVolume24h { get; set; } = 100000m;

    /// <summary>
    /// Required spread confirmation time in seconds
    /// </summary>
    public int SpreadConfirmationSeconds { get; set; } = 3;

    /// <summary>
    /// Number of price checks before entry
    /// </summary>
    public int RequiredConfirmations { get; set; } = 2;

    /// <summary>
    /// Enable momentum check (price trending in favorable direction)
    /// </summary>
    public bool CheckMomentum { get; set; } = true;

    /// <summary>
    /// Momentum lookback period in minutes
    /// </summary>
    public int MomentumPeriodMinutes { get; set; } = 5;

    /// <summary>
    /// Avoid entry during high volatility
    /// </summary>
    public bool AvoidHighVolatility { get; set; } = true;

    /// <summary>
    /// Max volatility percentage to allow entry
    /// </summary>
    public decimal MaxVolatilityPercent { get; set; } = 2.0m;

    /// <summary>
    /// Check order book depth before entry
    /// </summary>
    public bool CheckOrderBookDepth { get; set; } = true;

    /// <summary>
    /// Minimum order book depth (quote currency)
    /// </summary>
    public decimal MinOrderBookDepth { get; set; } = 10000m;
}

public class ExitConditions
{
    /// <summary>
    /// Take profit percentage
    /// </summary>
    public decimal TakeProfitPercent { get; set; } = 0.5m;

    /// <summary>
    /// Stop loss percentage
    /// </summary>
    public decimal StopLossPercent { get; set; } = 0.3m;

    /// <summary>
    /// Enable trailing stop
    /// </summary>
    public bool EnableTrailingStop { get; set; } = false;

    /// <summary>
    /// Trailing stop activation percentage
    /// </summary>
    public decimal TrailingStopActivation { get; set; } = 0.3m;

    /// <summary>
    /// Trailing stop distance percentage
    /// </summary>
    public decimal TrailingStopDistance { get; set; } = 0.1m;

    /// <summary>
    /// Maximum hold time in minutes (0 = unlimited)
    /// </summary>
    public int MaxHoldTimeMinutes { get; set; } = 30;

    /// <summary>
    /// Exit if spread drops below this percentage
    /// </summary>
    public decimal MinExitSpread { get; set; } = 0.05m;

    /// <summary>
    /// Enable partial profit taking
    /// </summary>
    public bool EnablePartialExit { get; set; } = false;

    /// <summary>
    /// Percentage to exit at first target
    /// </summary>
    public decimal PartialExitPercent { get; set; } = 50m;

    /// <summary>
    /// First partial exit target
    /// </summary>
    public decimal PartialExitTarget { get; set; } = 0.3m;
}

public class RiskManagement
{
    /// <summary>
    /// Maximum position size in quote currency
    /// </summary>
    public decimal MaxPositionSize { get; set; } = 1000m;

    /// <summary>
    /// Maximum percentage of balance per trade
    /// </summary>
    public decimal MaxBalancePercentPerTrade { get; set; } = 10m;

    /// <summary>
    /// Maximum daily loss in quote currency
    /// </summary>
    public decimal MaxDailyLoss { get; set; } = 100m;

    /// <summary>
    /// Maximum consecutive losses before pause
    /// </summary>
    public int MaxConsecutiveLosses { get; set; } = 3;

    /// <summary>
    /// Pause duration after max losses (minutes)
    /// </summary>
    public int PauseAfterLossesMinutes { get; set; } = 30;

    /// <summary>
    /// Maximum open positions
    /// </summary>
    public int MaxOpenPositions { get; set; } = 3;

    /// <summary>
    /// Maximum trades per hour
    /// </summary>
    public int MaxTradesPerHour { get; set; } = 10;

    /// <summary>
    /// Minimum time between trades (seconds)
    /// </summary>
    public int MinTimeBetweenTrades { get; set; } = 30;

    /// <summary>
    /// Enable drawdown protection
    /// </summary>
    public bool EnableDrawdownProtection { get; set; } = true;

    /// <summary>
    /// Maximum drawdown percentage before pause
    /// </summary>
    public decimal MaxDrawdownPercent { get; set; } = 5m;
}

public class AISettings
{
    /// <summary>
    /// Enable AI-powered trade decisions
    /// </summary>
    public bool EnableAI { get; set; } = true;

    /// <summary>
    /// AI confidence threshold (0-100)
    /// </summary>
    public int MinConfidenceScore { get; set; } = 70;

    /// <summary>
    /// Use market sentiment analysis
    /// </summary>
    public bool UseMarketSentiment { get; set; } = true;

    /// <summary>
    /// Weight for sentiment in decision (0-100)
    /// </summary>
    public int SentimentWeight { get; set; } = 30;

    /// <summary>
    /// Use pattern recognition
    /// </summary>
    public bool UsePatternRecognition { get; set; } = true;

    /// <summary>
    /// Enable adaptive learning
    /// </summary>
    public bool EnableAdaptiveLearning { get; set; } = true;

    /// <summary>
    /// Learning rate for adaptive system
    /// </summary>
    public decimal LearningRate { get; set; } = 0.1m;

    /// <summary>
    /// Use historical data for prediction
    /// </summary>
    public bool UseHistoricalPrediction { get; set; } = true;

    /// <summary>
    /// Historical lookback period (hours)
    /// </summary>
    public int HistoricalLookbackHours { get; set; } = 24;

    /// <summary>
    /// AI model type
    /// </summary>
    public AIModelType ModelType { get; set; } = AIModelType.Balanced;
}

public enum AIModelType
{
    Conservative,  // Lower risk, fewer trades
    Balanced,      // Default balanced approach
    Aggressive,    // Higher risk, more trades
    ScalpingMode,  // Very fast, small profits
    Custom         // User-defined parameters
}

public class AdvancedSettings
{
    /// <summary>
    /// Enable slippage protection
    /// </summary>
    public bool EnableSlippageProtection { get; set; } = true;

    /// <summary>
    /// Maximum allowed slippage percentage
    /// </summary>
    public decimal MaxSlippagePercent { get; set; } = 0.1m;

    /// <summary>
    /// Use limit orders instead of market
    /// </summary>
    public bool UseLimitOrders { get; set; } = false;

    /// <summary>
    /// Limit order offset from current price
    /// </summary>
    public decimal LimitOrderOffset { get; set; } = 0.02m;

    /// <summary>
    /// Order timeout in seconds
    /// </summary>
    public int OrderTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Retry failed orders
    /// </summary>
    public bool RetryFailedOrders { get; set; } = true;

    /// <summary>
    /// Maximum order retries
    /// </summary>
    public int MaxOrderRetries { get; set; } = 2;

    /// <summary>
    /// Split large orders
    /// </summary>
    public bool SplitLargeOrders { get; set; } = true;

    /// <summary>
    /// Maximum single order size
    /// </summary>
    public decimal MaxSingleOrderSize { get; set; } = 500m;

    /// <summary>
    /// Enable fee optimization
    /// </summary>
    public bool EnableFeeOptimization { get; set; } = true;

    /// <summary>
    /// Prefer exchanges with lower fees
    /// </summary>
    public bool PreferLowerFeeExchanges { get; set; } = true;

    /// <summary>
    /// Trading hours restriction (empty = 24/7)
    /// </summary>
    public List<TradingHours> TradingHoursRestriction { get; set; } = new();
}

public class TradingHours
{
    public DayOfWeek Day { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
}

/// <summary>
/// Trading Project containing multiple trading pairs
/// </summary>
public class TradingProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Project";
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Trading pairs in this project (max 10)
    /// </summary>
    public List<ProjectTradingPair> TradingPairs { get; set; } = new();

    /// <summary>
    /// Default strategy for new pairs
    /// </summary>
    public string DefaultStrategyId { get; set; } = "";

    /// <summary>
    /// Project-level settings
    /// </summary>
    public ProjectSettings Settings { get; set; } = new();
}

public class ProjectTradingPair
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Symbol { get; set; } = "";
    public string BaseAsset { get; set; } = "";
    public string QuoteAsset { get; set; } = "USDT";
    public string ExchangeA { get; set; } = "";
    public string ExchangeB { get; set; } = "";

    /// <summary>
    /// Assigned strategy ID
    /// </summary>
    public string StrategyId { get; set; } = "";

    /// <summary>
    /// Trade amount for this pair
    /// </summary>
    public decimal TradeAmount { get; set; } = 100m;

    /// <summary>
    /// Is this pair enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Priority (1 = highest)
    /// </summary>
    public int Priority { get; set; } = 5;

    /// <summary>
    /// Custom settings override
    /// </summary>
    public PairOverrideSettings? OverrideSettings { get; set; }
}

public class PairOverrideSettings
{
    public decimal? MinSpreadPercent { get; set; }
    public decimal? MaxPositionSize { get; set; }
    public decimal? TakeProfitPercent { get; set; }
    public decimal? StopLossPercent { get; set; }
}

public class ProjectSettings
{
    /// <summary>
    /// Total maximum investment across all pairs
    /// </summary>
    public decimal TotalMaxInvestment { get; set; } = 10000m;

    /// <summary>
    /// Daily profit target
    /// </summary>
    public decimal DailyProfitTarget { get; set; } = 100m;

    /// <summary>
    /// Daily loss limit
    /// </summary>
    public decimal DailyLossLimit { get; set; } = 50m;

    /// <summary>
    /// Auto-rebalance between pairs
    /// </summary>
    public bool AutoRebalance { get; set; } = false;

    /// <summary>
    /// Rebalance threshold percentage
    /// </summary>
    public decimal RebalanceThreshold { get; set; } = 20m;
}
