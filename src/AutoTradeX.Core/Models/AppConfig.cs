// AutoTrade-X v1.0.0

namespace AutoTradeX.Core.Models;

public class AppConfig
{
    public ExchangeConfig ExchangeA { get; set; } = new();
    public ExchangeConfig ExchangeB { get; set; } = new();
    public StrategyConfig Strategy { get; set; } = new();
    public RiskConfig Risk { get; set; } = new();
    public GeneralConfig General { get; set; } = new();
    public List<string> TradingPairs { get; set; } = new() { "BTC/USDT", "ETH/USDT" };

    /// <summary>
    /// Validate configuration and return list of errors.
    /// Returns empty list if valid.
    /// ตรวจสอบการตั้งค่าและคืนรายการข้อผิดพลาด คืนรายการว่างถ้าถูกต้อง
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();

        // Exchange validation
        if (ExchangeA == null) errors.Add("ExchangeA config is null");
        if (ExchangeB == null) errors.Add("ExchangeB config is null");

        if (ExchangeA != null)
        {
            if (ExchangeA.TradingFeePercent < 0 || ExchangeA.TradingFeePercent > 10)
                errors.Add($"ExchangeA fee {ExchangeA.TradingFeePercent}% is out of valid range (0-10%)");
            if (ExchangeA.TimeoutMs < 1000 || ExchangeA.TimeoutMs > 60000)
                errors.Add($"ExchangeA timeout {ExchangeA.TimeoutMs}ms is out of valid range (1000-60000ms)");
            if (ExchangeA.RateLimitPerSecond < 1 || ExchangeA.RateLimitPerSecond > 100)
                errors.Add($"ExchangeA rate limit {ExchangeA.RateLimitPerSecond} is out of valid range (1-100)");
        }

        if (ExchangeB != null)
        {
            if (ExchangeB.TradingFeePercent < 0 || ExchangeB.TradingFeePercent > 10)
                errors.Add($"ExchangeB fee {ExchangeB.TradingFeePercent}% is out of valid range (0-10%)");
            if (ExchangeB.TimeoutMs < 1000 || ExchangeB.TimeoutMs > 60000)
                errors.Add($"ExchangeB timeout {ExchangeB.TimeoutMs}ms is out of valid range (1000-60000ms)");
        }

        // Strategy validation
        if (Strategy != null)
        {
            if (Strategy.MinSpreadPercentage < 0 || Strategy.MinSpreadPercentage > 50)
                errors.Add($"Min spread {Strategy.MinSpreadPercentage}% is out of valid range (0-50%)");
            if (Strategy.MinExpectedProfitQuoteCurrency < 0)
                errors.Add("Min expected profit cannot be negative");
            if (Strategy.PollingIntervalMs < 100)
                errors.Add($"Polling interval {Strategy.PollingIntervalMs}ms is too low (min 100ms)");
        }

        // Risk validation
        if (Risk != null)
        {
            if (Risk.MaxPositionSizePerTrade <= 0)
                errors.Add("Max position size per trade must be positive");
            if (Risk.MaxDailyLoss <= 0)
                errors.Add("Max daily loss must be positive");
            if (Risk.MaxBalancePercentPerTrade <= 0 || Risk.MaxBalancePercentPerTrade > 100)
                errors.Add($"Max balance % per trade {Risk.MaxBalancePercentPerTrade}% is out of valid range (0-100%)");
            if (Risk.MaxDrawdownPercent <= 0 || Risk.MaxDrawdownPercent > 100)
                errors.Add($"Max drawdown {Risk.MaxDrawdownPercent}% is out of valid range (0-100%)");
            if (Risk.EmergencyStopLossPercent <= 0 || Risk.EmergencyStopLossPercent > 100)
                errors.Add($"Emergency stop loss {Risk.EmergencyStopLossPercent}% is out of valid range (0-100%)");
            if (Risk.MaxConsecutiveLosses < 1)
                errors.Add("Max consecutive losses must be at least 1");
        }

        // Trading pairs validation
        if (TradingPairs == null || TradingPairs.Count == 0)
            errors.Add("At least one trading pair is required");
        else
        {
            foreach (var pair in TradingPairs)
            {
                if (string.IsNullOrWhiteSpace(pair) || !pair.Contains('/'))
                    errors.Add($"Invalid trading pair format: '{pair}'. Expected format: 'BTC/USDT'");
            }
        }

        return errors;
    }

    /// <summary>
    /// Whether the configuration is valid for trading
    /// </summary>
    public bool IsValid => Validate().Count == 0;
}

public class ExchangeConfig
{
    public string Name { get; set; } = "ExchangeA";
    public string ApiBaseUrl { get; set; } = "https://api.exchange.com";
    public string? WebSocketUrl { get; set; }
    public string ApiKeyEnvVar { get; set; } = "EXCHANGE_A_API_KEY";
    public string ApiSecretEnvVar { get; set; } = "EXCHANGE_A_API_SECRET";
    public string? PassphraseEnvVar { get; set; }
    public decimal TradingFeePercent { get; set; } = 0.1m;
    public int RateLimitPerSecond { get; set; } = 10;
    public int TimeoutMs { get; set; } = 10000;
    public int MaxRetries { get; set; } = 3;
    public bool IsEnabled { get; set; } = true;
}

public class StrategyConfig
{
    public decimal MinSpreadPercentage { get; set; } = 0.3m;
    public decimal MinExpectedProfitQuoteCurrency { get; set; } = 0.5m;
    public int PollingIntervalMs { get; set; } = 1000;
    public bool UseWebSocket { get; set; } = false;
    public string OrderType { get; set; } = "Market";
    public decimal LimitOrderSlippagePercent { get; set; } = 0.05m;
    public int OrderFillTimeoutMs { get; set; } = 5000;
    public string PartialFillStrategy { get; set; } = "CancelRemaining";
    public string OneSideFailStrategy { get; set; } = "Hedge";
    public int OrderBookDepth { get; set; } = 20;
    public decimal MinDepthQuantity { get; set; } = 0.1m;

    // ========== Arbitrage Mode Settings / การตั้งค่าโหมด Arbitrage ==========

    /// <summary>
    /// Default arbitrage execution mode
    /// โหมดการทำ Arbitrage เริ่มต้น
    /// </summary>
    public ArbitrageExecutionMode DefaultExecutionMode { get; set; } = ArbitrageExecutionMode.DualBalance;

    /// <summary>
    /// Default transfer execution type for Transfer Mode
    /// รูปแบบการโอนเริ่มต้นสำหรับโหมดโอนจริง
    /// </summary>
    public TransferExecutionType DefaultTransferType { get; set; } = TransferExecutionType.Manual;

    /// <summary>
    /// For Transfer Mode: minimum spread to initiate (higher due to transfer risk)
    /// สำหรับโหมดโอนจริง: spread ขั้นต่ำในการเริ่ม (สูงกว่าเพราะมีความเสี่ยงจากการโอน)
    /// </summary>
    public decimal TransferModeMinSpread { get; set; } = 1.0m;

    /// <summary>
    /// For Transfer Mode: maximum wait time for transfer completion (minutes)
    /// สำหรับโหมดโอนจริง: เวลารอสูงสุดสำหรับการโอนสำเร็จ (นาที)
    /// </summary>
    public int TransferModeMaxWaitMinutes { get; set; } = 60;

    /// <summary>
    /// For Transfer Mode: cancel sell order if price drops below this % from buy price
    /// สำหรับโหมดโอนจริง: ยกเลิกคำสั่งขายถ้าราคาลดลงต่ำกว่า % นี้จากราคาซื้อ
    /// </summary>
    public decimal TransferModeStopLossPercent { get; set; } = 2.0m;

    /// <summary>
    /// For Transfer Mode: sell immediately when deposit confirmed (vs wait for price improvement)
    /// สำหรับโหมดโอนจริง: ขายทันทีเมื่อฝากยืนยัน (vs รอราคาดีขึ้น)
    /// </summary>
    public bool TransferModeSellImmediately { get; set; } = true;

    /// <summary>
    /// Preferred network for transfers (e.g., "BTC", "ERC20", "TRC20", "BEP20")
    /// เครือข่ายที่ต้องการใช้ในการโอน
    /// </summary>
    public string PreferredTransferNetwork { get; set; } = "Auto";
}

public class RiskConfig
{
    public decimal MaxPositionSizePerTrade { get; set; } = 100m;
    public decimal MaxDailyLoss { get; set; } = 50m;
    public int MaxTradesPerDay { get; set; } = 100;
    public int MaxTradesPerHour { get; set; } = 20;
    public int MinTimeBetweenTradesMs { get; set; } = 5000;
    public int MaxConsecutiveLosses { get; set; } = 5;
    public decimal MaxBalancePercentPerTrade { get; set; } = 10m;
    public decimal MinBalanceRequired { get; set; } = 50m;

    // Emergency protection settings
    public decimal MaxDrawdownPercent { get; set; } = 5m;
    public decimal EmergencyStopLossPercent { get; set; } = 10m;
    public bool EnableEmergencyProtection { get; set; } = true;
    public decimal RebalanceThresholdPercent { get; set; } = 30m;
    public int RapidLossWindowTrades { get; set; } = 5;
    public decimal RapidLossThresholdPercent { get; set; } = 1m;
}

public class GeneralConfig
{
    public bool LiveTrading { get; set; } = false;
    public string LogDirectory { get; set; } = "logs";
    public string LogLevel { get; set; } = "Info";
    public int LogRetentionDays { get; set; } = 30;
    public bool DarkTheme { get; set; } = true;
    public bool EnableNotifications { get; set; } = true;
    public bool PlaySoundOnTrade { get; set; } = false;
    public string Version { get; set; } = "1.0.0";
}
