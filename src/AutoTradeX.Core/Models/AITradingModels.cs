/*
 * ============================================================================
 * AutoTrade-X - AI Trading Models
 * ============================================================================
 * AI-powered single-exchange trading with smart strategies
 * ============================================================================
 */

namespace AutoTradeX.Core.Models;

/// <summary>
/// AI Trading Mode - Type of AI strategy
/// โหมดการเทรด AI - ประเภทของกลยุทธ์ AI
/// </summary>
public enum AITradingMode
{
    /// <summary>
    /// Scalping - Quick trades with small profits
    /// Scalping - เทรดเร็วกำไรน้อย
    /// </summary>
    Scalping,

    /// <summary>
    /// Momentum - Follow the trend
    /// Momentum - ตามเทรนด์
    /// </summary>
    Momentum,

    /// <summary>
    /// Mean Reversion - Buy low, sell high
    /// Mean Reversion - ซื้อถูกขายแพง
    /// </summary>
    MeanReversion,

    /// <summary>
    /// Breakout - Trade on price breakouts
    /// Breakout - เทรดเมื่อราคาทะลุแนวต้าน/แนวรับ
    /// </summary>
    Breakout,

    /// <summary>
    /// Grid Trading - Place orders at intervals
    /// Grid Trading - วางออเดอร์เป็นช่วงๆ
    /// </summary>
    GridTrading,

    /// <summary>
    /// Smart DCA - Dollar cost averaging with AI timing
    /// Smart DCA - เฉลี่ยต้นทุนด้วยจังหวะ AI
    /// </summary>
    SmartDCA,

    /// <summary>
    /// Custom - User-defined strategy
    /// Custom - กลยุทธ์ที่ผู้ใช้กำหนด
    /// </summary>
    Custom
}

/// <summary>
/// AI Trading Position Status
/// สถานะ Position ของ AI Trading
/// </summary>
public enum AIPositionStatus
{
    /// <summary>
    /// No position - Waiting for entry
    /// ไม่มี Position - รอจังหวะเข้า
    /// </summary>
    None,

    /// <summary>
    /// Pending entry - Order placed, waiting for fill
    /// รอเข้า Position - วางออเดอร์แล้ว รอ fill
    /// </summary>
    PendingEntry,

    /// <summary>
    /// In position - Holding
    /// อยู่ใน Position - ถืออยู่
    /// </summary>
    InPosition,

    /// <summary>
    /// Pending exit - Exit order placed
    /// รอออก Position - วางออเดอร์ออกแล้ว
    /// </summary>
    PendingExit,

    /// <summary>
    /// Stopped - Emergency stop triggered
    /// หยุดฉุกเฉิน - ถูก Stop
    /// </summary>
    EmergencyStopped
}

/// <summary>
/// AI Signal Strength
/// ความแรงของสัญญาณ AI
/// </summary>
public enum AISignalStrength
{
    /// <summary>
    /// No signal
    /// ไม่มีสัญญาณ
    /// </summary>
    None = 0,

    /// <summary>
    /// Weak signal - Low confidence
    /// สัญญาณอ่อน - ความมั่นใจต่ำ
    /// </summary>
    Weak = 25,

    /// <summary>
    /// Moderate signal - Medium confidence
    /// สัญญาณปานกลาง - ความมั่นใจกลาง
    /// </summary>
    Moderate = 50,

    /// <summary>
    /// Strong signal - High confidence
    /// สัญญาณแรง - ความมั่นใจสูง
    /// </summary>
    Strong = 75,

    /// <summary>
    /// Very strong signal - Very high confidence
    /// สัญญาณแรงมาก - ความมั่นใจสูงมาก
    /// </summary>
    VeryStrong = 100
}

/// <summary>
/// AI Trading Signal
/// สัญญาณการเทรดจาก AI
/// </summary>
public class AITradingSignal
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Symbol { get; set; } = "";
    public string Exchange { get; set; } = "";

    /// <summary>
    /// Signal type: Buy, Sell, Hold
    /// ประเภทสัญญาณ: ซื้อ, ขาย, ถือ
    /// </summary>
    public string SignalType { get; set; } = "Hold";

    /// <summary>
    /// Signal strength (0-100)
    /// ความแรงสัญญาณ (0-100)
    /// </summary>
    public AISignalStrength Strength { get; set; } = AISignalStrength.None;

    /// <summary>
    /// Confidence score (0-100)
    /// คะแนนความมั่นใจ (0-100)
    /// </summary>
    public int Confidence { get; set; } = 0;

    /// <summary>
    /// Entry price recommendation
    /// ราคาแนะนำสำหรับเข้า
    /// </summary>
    public decimal? RecommendedEntryPrice { get; set; }

    /// <summary>
    /// Target price
    /// ราคาเป้าหมาย
    /// </summary>
    public decimal? TargetPrice { get; set; }

    /// <summary>
    /// Stop loss price
    /// ราคา Stop Loss
    /// </summary>
    public decimal? StopLossPrice { get; set; }

    /// <summary>
    /// Expected profit percentage
    /// กำไรที่คาดหวัง (%)
    /// </summary>
    public decimal ExpectedProfitPercent { get; set; }

    /// <summary>
    /// Risk/Reward ratio
    /// อัตราส่วน Risk/Reward
    /// </summary>
    public decimal RiskRewardRatio { get; set; }

    /// <summary>
    /// Time to execute (estimated hold time in minutes)
    /// เวลาในการเทรด (นาที)
    /// </summary>
    public int EstimatedHoldTimeMinutes { get; set; }

    /// <summary>
    /// AI reasoning explanation
    /// คำอธิบายเหตุผลจาก AI
    /// </summary>
    public string Reasoning { get; set; } = "";

    /// <summary>
    /// Indicators used for this signal
    /// Indicators ที่ใช้ในสัญญาณนี้
    /// </summary>
    public List<IndicatorValue> Indicators { get; set; } = new();
}

/// <summary>
/// Indicator value used in AI analysis
/// ค่า Indicator ที่ใช้ในการวิเคราะห์ AI
/// </summary>
public class IndicatorValue
{
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    public decimal Value { get; set; }
    public string Status { get; set; } = ""; // "Bullish", "Bearish", "Neutral"
    public string Description { get; set; } = "";
}

/// <summary>
/// AI Trading Position
/// Position ของ AI Trading
/// </summary>
public class AITradingPosition
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Symbol { get; set; } = "";
    public string Exchange { get; set; } = "";
    public AIPositionStatus Status { get; set; } = AIPositionStatus.None;

    /// <summary>
    /// Entry time
    /// เวลาเข้า Position
    /// </summary>
    public DateTime? EntryTime { get; set; }

    /// <summary>
    /// Entry price
    /// ราคาเข้า Position
    /// </summary>
    public decimal EntryPrice { get; set; }

    /// <summary>
    /// Current price
    /// ราคาปัจจุบัน
    /// </summary>
    public decimal CurrentPrice { get; set; }

    /// <summary>
    /// Position size (in base asset)
    /// ขนาด Position (หน่วยเหรียญ)
    /// </summary>
    public decimal Size { get; set; }

    /// <summary>
    /// Position value (in quote asset)
    /// มูลค่า Position (หน่วย USDT)
    /// </summary>
    public decimal Value { get; set; }

    /// <summary>
    /// Unrealized PnL
    /// กำไร/ขาดทุนที่ยังไม่รับรู้
    /// </summary>
    public decimal UnrealizedPnL { get; set; }

    /// <summary>
    /// Unrealized PnL percentage
    /// กำไร/ขาดทุนที่ยังไม่รับรู้ (%)
    /// </summary>
    public decimal UnrealizedPnLPercent { get; set; }

    /// <summary>
    /// Take profit price
    /// ราคา Take Profit
    /// </summary>
    public decimal? TakeProfitPrice { get; set; }

    /// <summary>
    /// Stop loss price
    /// ราคา Stop Loss
    /// </summary>
    public decimal? StopLossPrice { get; set; }

    /// <summary>
    /// Trailing stop distance (%)
    /// ระยะ Trailing Stop (%)
    /// </summary>
    public decimal? TrailingStopPercent { get; set; }

    /// <summary>
    /// Strategy used for this position
    /// กลยุทธ์ที่ใช้สำหรับ Position นี้
    /// </summary>
    public AITradingMode Strategy { get; set; }

    /// <summary>
    /// Order IDs associated with this position
    /// รายการ Order ID ที่เกี่ยวข้อง
    /// </summary>
    public List<string> OrderIds { get; set; } = new();
}

/// <summary>
/// AI Trading Session Stats
/// สถิติ Session ของ AI Trading
/// </summary>
public class AITradingSessionStats
{
    public DateTime SessionStart { get; set; } = DateTime.UtcNow;
    public DateTime? SessionEnd { get; set; }

    /// <summary>
    /// Total trades in session
    /// จำนวนเทรดทั้งหมดใน Session
    /// </summary>
    public int TotalTrades { get; set; }

    /// <summary>
    /// Winning trades
    /// เทรดที่ชนะ
    /// </summary>
    public int WinningTrades { get; set; }

    /// <summary>
    /// Losing trades
    /// เทรดที่แพ้
    /// </summary>
    public int LosingTrades { get; set; }

    /// <summary>
    /// Win rate (%)
    /// อัตราชนะ (%)
    /// </summary>
    public decimal WinRate => TotalTrades > 0 ? (decimal)WinningTrades / TotalTrades * 100 : 0;

    /// <summary>
    /// Total realized PnL
    /// กำไร/ขาดทุนที่รับรู้แล้วทั้งหมด
    /// </summary>
    public decimal TotalRealizedPnL { get; set; }

    /// <summary>
    /// Current unrealized PnL
    /// กำไร/ขาดทุนที่ยังไม่รับรู้ปัจจุบัน
    /// </summary>
    public decimal CurrentUnrealizedPnL { get; set; }

    /// <summary>
    /// Gross profit
    /// กำไรรวม (เฉพาะเทรดที่กำไร)
    /// </summary>
    public decimal GrossProfit { get; set; }

    /// <summary>
    /// Gross loss
    /// ขาดทุนรวม (เฉพาะเทรดที่ขาดทุน)
    /// </summary>
    public decimal GrossLoss { get; set; }

    /// <summary>
    /// Profit factor
    /// Profit Factor = GrossProfit / GrossLoss
    /// </summary>
    public decimal ProfitFactor => GrossLoss != 0 ? Math.Abs(GrossProfit / GrossLoss) : GrossProfit > 0 ? 999 : 0;

    /// <summary>
    /// Average profit per trade
    /// กำไรเฉลี่ยต่อเทรด
    /// </summary>
    public decimal AverageProfit => TotalTrades > 0 ? TotalRealizedPnL / TotalTrades : 0;

    /// <summary>
    /// Largest win
    /// กำไรสูงสุด
    /// </summary>
    public decimal LargestWin { get; set; }

    /// <summary>
    /// Largest loss
    /// ขาดทุนสูงสุด
    /// </summary>
    public decimal LargestLoss { get; set; }

    /// <summary>
    /// Current consecutive wins
    /// ชนะติดต่อกันปัจจุบัน
    /// </summary>
    public int CurrentConsecutiveWins { get; set; }

    /// <summary>
    /// Current consecutive losses
    /// แพ้ติดต่อกันปัจจุบัน
    /// </summary>
    public int CurrentConsecutiveLosses { get; set; }

    /// <summary>
    /// Max consecutive wins
    /// ชนะติดต่อกันสูงสุด
    /// </summary>
    public int MaxConsecutiveWins { get; set; }

    /// <summary>
    /// Max consecutive losses
    /// แพ้ติดต่อกันสูงสุด
    /// </summary>
    public int MaxConsecutiveLosses { get; set; }

    /// <summary>
    /// Peak realized PnL (for drawdown calculation)
    /// กำไรสูงสุดที่เคยถึง (สำหรับคำนวณ Drawdown)
    /// </summary>
    public decimal PeakPnL { get; set; }

    /// <summary>
    /// Max drawdown (%)
    /// Drawdown สูงสุด (%)
    /// </summary>
    public decimal MaxDrawdownPercent { get; set; }

    /// <summary>
    /// Total fees paid
    /// ค่า Fee ทั้งหมด
    /// </summary>
    public decimal TotalFees { get; set; }

    /// <summary>
    /// Average trade duration (minutes)
    /// ระยะเวลาเทรดเฉลี่ย (นาที)
    /// </summary>
    public double AverageTradeDurationMinutes { get; set; }
}

/// <summary>
/// AI Strategy Configuration for a specific exchange/pair
/// การตั้งค่ากลยุทธ์ AI สำหรับกระดาน/คู่เทรดที่เฉพาะเจาะจง
/// </summary>
public class AIStrategyConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Default AI Strategy";
    public string Exchange { get; set; } = "";
    public string Symbol { get; set; } = "";
    public AITradingMode Mode { get; set; } = AITradingMode.Scalping;
    public bool IsEnabled { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Entry Settings / ตั้งค่าการเข้า
    public decimal MinConfidenceToEnter { get; set; } = 70; // 0-100
    public decimal MinProfitTargetPercent { get; set; } = 0.5m;
    public int MaxOpenPositions { get; set; } = 1;

    // Trade Size Settings / ตั้งค่าขนาดเทรด
    public decimal TradeAmountUSDT { get; set; } = 100;
    public decimal MaxTradeAmountUSDT { get; set; } = 1000;
    public bool UsePercentageOfBalance { get; set; } = false;
    public decimal BalancePercentage { get; set; } = 10; // % of balance

    // Exit Settings / ตั้งค่าการออก
    public decimal TakeProfitPercent { get; set; } = 1.0m;
    public decimal StopLossPercent { get; set; } = 0.5m;
    public bool EnableTrailingStop { get; set; } = true;
    public decimal TrailingStopActivationPercent { get; set; } = 0.5m;
    public decimal TrailingStopDistancePercent { get; set; } = 0.3m;
    public int MaxHoldTimeMinutes { get; set; } = 60;

    // Risk Management / จัดการความเสี่ยง
    public decimal MaxDailyLossUSDT { get; set; } = 50;
    public decimal MaxDailyLossPercent { get; set; } = 5;
    public int MaxConsecutiveLosses { get; set; } = 3;
    public int PauseAfterLossesMinutes { get; set; } = 30;
    public int MaxTradesPerHour { get; set; } = 10;
    public decimal MaxDrawdownPercent { get; set; } = 10;

    // Emergency Stop / หยุดฉุกเฉิน
    public bool EnableEmergencyStop { get; set; } = true;
    public decimal EmergencyStopLossPercent { get; set; } = 2.0m;
    public bool AutoCloseOnEmergency { get; set; } = true;

    // Timing / การจับเวลา
    public bool TradingHoursEnabled { get; set; } = false;
    public TimeSpan TradingStartTime { get; set; } = TimeSpan.FromHours(0);
    public TimeSpan TradingEndTime { get; set; } = TimeSpan.FromHours(24);
    public List<DayOfWeek> TradingDays { get; set; } = new()
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    };

    // Advanced / ขั้นสูง
    public bool UseMarketOrders { get; set; } = true;
    public decimal LimitOrderOffsetPercent { get; set; } = 0.05m;
    public int OrderTimeoutSeconds { get; set; } = 30;
    public bool RetryFailedOrders { get; set; } = true;
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// Fee percentage per trade (e.g., 0.1 = 0.1%). Per-exchange configurable.
    /// ค่าธรรมเนียมต่อการเทรด (เช่น 0.1 = 0.1%) ตั้งค่าได้ตาม exchange
    /// Default values by exchange: Binance=0.1%, KuCoin=0.1%, OKX=0.08%,
    /// Bybit=0.1%, Gate.io=0.2%, Bitkub=0.25%
    /// </summary>
    public decimal FeePercent { get; set; } = 0.1m;
}

/// <summary>
/// AI Trade Result
/// ผลการเทรดของ AI
/// </summary>
public class AITradeResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string PositionId { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Exchange { get; set; } = "";
    public AITradingMode Strategy { get; set; }

    public DateTime EntryTime { get; set; }
    public DateTime ExitTime { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal Size { get; set; }

    public decimal GrossPnL { get; set; }
    public decimal Fees { get; set; }
    public decimal NetPnL { get; set; }
    public decimal PnLPercent { get; set; }

    public bool IsWin => NetPnL > 0;
    public TimeSpan Duration => ExitTime - EntryTime;

    public string ExitReason { get; set; } = ""; // "TakeProfit", "StopLoss", "TrailingStop", "Manual", "Emergency", "Timeout"

    public AITradingSignal? EntrySignal { get; set; }
    public AITradingSignal? ExitSignal { get; set; }
}

/// <summary>
/// Price candle for chart display
/// แท่งเทียนสำหรับแสดงกราฟ
/// </summary>
public class PriceCandle
{
    public DateTime Time { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }

    public bool IsBullish => Close >= Open;
    public decimal BodySize => Math.Abs(Close - Open);
    public decimal UpperWick => High - Math.Max(Open, Close);
    public decimal LowerWick => Math.Min(Open, Close) - Low;
}

/// <summary>
/// Real-time market data for AI analysis
/// ข้อมูลตลาดแบบ Real-time สำหรับการวิเคราะห์ AI
/// </summary>
public class AIMarketData
{
    public string Symbol { get; set; } = "";
    public string Exchange { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public decimal CurrentPrice { get; set; }
    public decimal BidPrice { get; set; }
    public decimal AskPrice { get; set; }
    public decimal Spread { get; set; }
    public decimal SpreadPercent { get; set; }

    public decimal Volume24h { get; set; }
    public decimal VolumeChange24h { get; set; }
    public decimal PriceChange24h { get; set; }
    public decimal PriceChangePercent24h { get; set; }

    public decimal High24h { get; set; }
    public decimal Low24h { get; set; }

    public List<PriceCandle> RecentCandles { get; set; } = new();

    // Calculated indicators
    public decimal? RSI { get; set; }
    public decimal? MACD { get; set; }
    public decimal? MACDSignal { get; set; }
    public decimal? MACDHistogram { get; set; }
    public decimal? EMA9 { get; set; }
    public decimal? EMA21 { get; set; }
    public decimal? SMA50 { get; set; }
    public decimal? SMA200 { get; set; }
    public decimal? BollingerUpper { get; set; }
    public decimal? BollingerMiddle { get; set; }
    public decimal? BollingerLower { get; set; }
    public decimal? ATR { get; set; }
    public decimal? Volatility { get; set; }
}

/// <summary>
/// Information about AI Trading mode
/// ข้อมูลเกี่ยวกับโหมด AI Trading
/// </summary>
public static class AITradingModeInfo
{
    public static (string EnglishName, string ThaiName, string Description, string Icon) GetModeInfo(AITradingMode mode)
    {
        return mode switch
        {
            AITradingMode.Scalping => ("Scalping", "สกัลปิง", "Quick trades with small profits, high frequency", "⚡"),
            AITradingMode.Momentum => ("Momentum", "โมเมนตัม", "Follow the trend, ride the wave", "📈"),
            AITradingMode.MeanReversion => ("Mean Reversion", "คืนสู่ค่าเฉลี่ย", "Buy oversold, sell overbought", "🔄"),
            AITradingMode.Breakout => ("Breakout", "ทะลุแนว", "Trade on price breakouts", "🚀"),
            AITradingMode.GridTrading => ("Grid Trading", "กริด", "Place orders at intervals", "📊"),
            AITradingMode.SmartDCA => ("Smart DCA", "DCA อัจฉริยะ", "Dollar cost averaging with AI timing", "💰"),
            AITradingMode.Custom => ("Custom", "กำหนดเอง", "User-defined strategy", "⚙️"),
            _ => ("Unknown", "ไม่ทราบ", "", "❓")
        };
    }

    /// <summary>
    /// Get detailed strategy information including how it works and tips
    /// รับข้อมูลกลยุทธ์แบบละเอียด รวมถึงวิธีการทำงานและเคล็ดลับ
    /// </summary>
    public static StrategyInfo GetDetailedInfo(AITradingMode mode)
    {
        return mode switch
        {
            AITradingMode.Scalping => new StrategyInfo
            {
                Name = "Scalping",
                ThaiName = "สกัลปิง",
                Icon = "⚡",
                ShortDescription = "เทรดเร็ว กำไรน้อยแต่บ่อย",
                HowItWorks = "ใช้ RSI สุดขั้ว (< 25 หรือ > 75) เพื่อหาจุดกลับตัวระยะสั้น " +
                            "ตรวจสอบ Spread ให้แคบพอ และใช้ Volume ยืนยัน",
                Indicators = new[] { "RSI (หลัก)", "Spread", "Volume" },
                TargetProfit = "0.3% - 0.5%",
                StopLoss = "0.2% - 0.3%",
                HoldTime = "1-5 นาที",
                RiskLevel = "ปานกลาง-สูง",
                BestMarketCondition = "ตลาดที่มี Volatility ปานกลาง และ Volume สูง",
                Tips = new[]
                {
                    "เลือกคู่เทรดที่มี Spread แคบ (< 0.1%)",
                    "ควรมี Volume สูงเพื่อให้ entry/exit ได้เร็ว",
                    "ตั้ง Stop Loss แน่นเพราะกำไรต่อเทรดน้อย",
                    "ไม่เหมาะกับช่วงข่าวใหญ่หรือตลาดผันผวนมาก"
                },
                VolatilityPreference = "ปานกลาง (1-3%)"
            },

            AITradingMode.Momentum => new StrategyInfo
            {
                Name = "Momentum",
                ThaiName = "โมเมนตัม",
                Icon = "📈",
                ShortDescription = "ตามเทรนด์ ให้แรงส่งพาไป",
                HowItWorks = "ใช้ EMA Crossover (9/21) + MACD เพื่อยืนยันเทรนด์ " +
                            "เข้าเมื่อราคาอยู่เหนือ SMA50 และ Indicators ทั้งหมดชี้ขึ้น",
                Indicators = new[] { "EMA 9/21 (หลัก)", "MACD", "SMA50", "ATR" },
                TargetProfit = "2x ATR (ประมาณ 2-4%)",
                StopLoss = "1x ATR (ประมาณ 1-2%)",
                HoldTime = "15-60 นาที",
                RiskLevel = "ปานกลาง",
                BestMarketCondition = "ตลาดที่มีเทรนด์ชัดเจน (Trending Market)",
                Tips = new[]
                {
                    "รอให้ EMA9 ตัด EMA21 ก่อนเข้า",
                    "MACD ต้องอยู่เหนือ Signal Line เพื่อยืนยัน",
                    "ใช้ ATR คำนวณ TP/SL แทนค่าคงที่",
                    "ไม่เหมาะกับตลาด Sideways"
                },
                VolatilityPreference = "ปานกลาง-สูง (2-5%)"
            },

            AITradingMode.MeanReversion => new StrategyInfo
            {
                Name = "Mean Reversion",
                ThaiName = "คืนสู่ค่าเฉลี่ย",
                Icon = "🔄",
                ShortDescription = "ซื้อถูกขายแพง เมื่อราคาเบี่ยงเบนมาก",
                HowItWorks = "ใช้ Bollinger Bands หาจุดที่ราคาเบี่ยงเบนจากค่าเฉลี่ย " +
                            "ซื้อเมื่อราคาแตะ Lower Band + RSI Oversold | ขายเมื่อราคากลับสู่ Middle Band",
                Indicators = new[] { "Bollinger Bands (หลัก)", "RSI", "Distance from Mean" },
                TargetProfit = "กลับสู่ BB Middle (ประมาณ 1-2%)",
                StopLoss = "ต่ำกว่า BB Lower (ประมาณ 0.5-1%)",
                HoldTime = "10-30 นาที",
                RiskLevel = "ปานกลาง",
                BestMarketCondition = "ตลาด Sideways หรือเมื่อราคาเบี่ยงเบนผิดปกติ",
                Tips = new[]
                {
                    "รอให้ราคาแตะ BB Lower + RSI < 30 เพื่อความมั่นใจ",
                    "เป้าหมายคือ BB Middle ไม่ใช่ BB Upper",
                    "อย่าสวน Trend ใหญ่ - ตรวจสอบ SMA200 ก่อน",
                    "ระวังตลาดที่เทรนด์แรง อาจไม่กลับสู่ค่าเฉลี่ย"
                },
                VolatilityPreference = "ต่ำ-ปานกลาง (1-2%)"
            },

            AITradingMode.GridTrading => new StrategyInfo
            {
                Name = "Grid Trading",
                ThaiName = "กริด",
                Icon = "📊",
                ShortDescription = "วางออเดอร์เป็นช่วงๆ ทำกำไรจากการแกว่ง",
                HowItWorks = "แบ่งช่วงราคาเป็น Grid ด้วย ATR " +
                            "ซื้อที่แนวรับ (Support Grid) | ขายที่แนวต้าน (Resistance Grid)",
                Indicators = new[] { "ATR (Grid Size)", "Support/Resistance Levels", "Volatility", "Volume" },
                TargetProfit = "0.5% ต่อ Grid Level",
                StopLoss = "2 Grid Levels ด้านล่าง",
                HoldTime = "แตกต่างกัน (นาที - ชั่วโมง)",
                RiskLevel = "ต่ำ-ปานกลาง",
                BestMarketCondition = "ตลาด Sideways ที่แกว่งในกรอบ",
                Tips = new[]
                {
                    "เลือกคู่เทรดที่ Volatility ต่ำ (< 2%)",
                    "Grid ทำงานดีในตลาด Range-bound",
                    "ต้องการ Liquidity สูงเพื่อ Fill ออเดอร์ได้",
                    "ระวังเมื่อราคาหลุดกรอบ - อาจขาดทุนต่อเนื่อง"
                },
                VolatilityPreference = "ต่ำ (< 2%)"
            },

            AITradingMode.Breakout => new StrategyInfo
            {
                Name = "Breakout",
                ThaiName = "ทะลุแนว",
                Icon = "🚀",
                ShortDescription = "เทรดเมื่อราคาทะลุแนวต้าน/รับ",
                HowItWorks = "รอราคาทะลุ BB Upper พร้อม Volume เพิ่มขึ้น > 30% " +
                            "ใช้ MACD และ SMA200 ยืนยันแรงส่ง",
                Indicators = new[] { "Bollinger Bands (หลัก)", "Volume", "MACD", "SMA200" },
                TargetProfit = "3x ATR (ประมาณ 3-6%)",
                StopLoss = "ต่ำกว่าจุด Breakout",
                HoldTime = "30-120 นาที",
                RiskLevel = "สูง",
                BestMarketCondition = "หลังจากตลาด Consolidate นานและ Volume เพิ่มขึ้น",
                Tips = new[]
                {
                    "Volume ต้องเพิ่มขึ้นอย่างน้อย 30% เพื่อยืนยัน Breakout",
                    "ระวัง False Breakout - รอ Candle ปิดเหนือแนวต้านก่อน",
                    "เป้าหมายควรใหญ่ (3x ATR) เพราะ Breakout มักวิ่งแรง",
                    "ตั้ง Stop Loss ใต้จุด Breakout ทันที"
                },
                VolatilityPreference = "กำลังเพิ่มขึ้น (หลัง Squeeze)"
            },

            AITradingMode.SmartDCA => new StrategyInfo
            {
                Name = "Smart DCA",
                ThaiName = "DCA อัจฉริยะ",
                Icon = "💰",
                ShortDescription = "เฉลี่ยต้นทุนด้วยจังหวะ AI",
                HowItWorks = "ใช้ RSI + ราคาเทียบ SMA200 + BB Position " +
                            "หาจังหวะซื้อเฉลี่ยที่ดีกว่าการซื้อตามรอบเวลา",
                Indicators = new[] { "RSI", "SMA200 (Long-term)", "Bollinger Position" },
                TargetProfit = "ระยะยาว (10%+)",
                StopLoss = "ไม่ใช้ (สะสมระยะยาว)",
                HoldTime = "ระยะยาว (สัปดาห์ - เดือน)",
                RiskLevel = "ต่ำ",
                BestMarketCondition = "ทุกสภาวะตลาด (เหมาะกับการสะสม)",
                Tips = new[]
                {
                    "ซื้อเมื่อ RSI < 40 และราคาต่ำกว่า SMA200",
                    "ไม่ต้อง Stop Loss เพราะเป็นการสะสมระยะยาว",
                    "แบ่งเงินเป็นหลายส่วน อย่าซื้อทั้งหมดในครั้งเดียว",
                    "เหมาะสำหรับเหรียญที่มั่นใจในระยะยาว"
                },
                VolatilityPreference = "ไม่จำกัด (สะสมระยะยาว)"
            },

            _ => new StrategyInfo
            {
                Name = "Custom",
                ThaiName = "กำหนดเอง",
                Icon = "⚙️",
                ShortDescription = "กลยุทธ์ที่ผู้ใช้กำหนดเอง",
                HowItWorks = "ผู้ใช้กำหนด Indicators และเงื่อนไขเอง",
                Indicators = new[] { "กำหนดเอง" },
                TargetProfit = "กำหนดเอง",
                StopLoss = "กำหนดเอง",
                HoldTime = "กำหนดเอง",
                RiskLevel = "แตกต่างกัน",
                BestMarketCondition = "ขึ้นอยู่กับการตั้งค่า",
                Tips = new[] { "ทดสอบกลยุทธ์ก่อนใช้งานจริง" },
                VolatilityPreference = "กำหนดเอง"
            }
        };
    }

    /// <summary>
    /// Recommend strategy based on current market volatility
    /// แนะนำกลยุทธ์ตามความผันผวนของตลาดปัจจุบัน
    /// </summary>
    public static AITradingMode RecommendStrategy(decimal volatilityPercent, bool isTrending, decimal volume24h)
    {
        // High volatility (>3%) + Trending = Momentum or Breakout
        if (volatilityPercent >= 3 && isTrending)
        {
            return volume24h > 1000000 ? AITradingMode.Breakout : AITradingMode.Momentum;
        }

        // Low volatility (<1%) + Sideways = Grid or Mean Reversion
        if (volatilityPercent < 1 && !isTrending)
        {
            return volume24h > 500000 ? AITradingMode.GridTrading : AITradingMode.MeanReversion;
        }

        // Medium-low volatility (1-2%) + Sideways = Mean Reversion
        if (volatilityPercent >= 1 && volatilityPercent < 2 && !isTrending)
        {
            return AITradingMode.MeanReversion;
        }

        // Medium volatility (1-3%) + Trending or neutral = Scalping
        if (volatilityPercent >= 1 && volatilityPercent < 3)
        {
            return AITradingMode.Scalping;
        }

        // High volatility + Not trending = Smart DCA (uncertain conditions)
        return AITradingMode.SmartDCA;
    }
}

/// <summary>
/// Detailed strategy information
/// ข้อมูลกลยุทธ์แบบละเอียด
/// </summary>
public class StrategyInfo
{
    public string Name { get; set; } = "";
    public string ThaiName { get; set; } = "";
    public string Icon { get; set; } = "";
    public string ShortDescription { get; set; } = "";
    public string HowItWorks { get; set; } = "";
    public string[] Indicators { get; set; } = Array.Empty<string>();
    public string TargetProfit { get; set; } = "";
    public string StopLoss { get; set; } = "";
    public string HoldTime { get; set; } = "";
    public string RiskLevel { get; set; } = "";
    public string BestMarketCondition { get; set; } = "";
    public string[] Tips { get; set; } = Array.Empty<string>();
    public string VolatilityPreference { get; set; } = "";
}
