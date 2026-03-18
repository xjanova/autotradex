namespace AutoTradeX.Core.Models;

/// <summary>
/// Arbitrage execution mode - defines how the trade pair is executed
/// โหมดการทำ Arbitrage - กำหนดวิธีการเทรดคู่เหรียญ
/// </summary>
public enum ArbitrageExecutionMode
{
    /// <summary>
    /// Dual-Balance Mode (โหมดสองกระเป๋า)
    /// - Uses existing balances on BOTH exchanges
    /// - Buy on Exchange A using quote currency (USDT) already on A
    /// - Sell on Exchange B using base currency (BTC) already on B
    /// - Instant execution, no transfer delays
    /// - Requires pre-funded balances on both sides
    ///
    /// ใช้ยอดเงินที่มีอยู่แล้วบนทั้งสองกระดานพร้อมกัน
    /// ซื้อที่กระดาน A (ใช้ USDT ที่มี) + ขายที่กระดาน B (ใช้ Crypto ที่มี)
    /// เร็วมาก ไม่ต้องรอ blockchain
    /// </summary>
    DualBalance = 0,

    /// <summary>
    /// Transfer Mode (โหมดโอนจริง)
    /// - Traditional arbitrage with actual crypto transfer
    /// - Buy on Exchange A → Withdraw → Deposit to B → Sell
    /// - Takes time (blockchain confirmation)
    /// - Can start with funds on just one exchange
    /// - Higher risk due to transfer delays and price changes
    ///
    /// Arbitrage แบบดั้งเดิม - ซื้อ → โอน → ขาย
    /// ต้องรอ blockchain ยืนยัน
    /// ใช้เงินทุนน้อยกว่า (เริ่มจากกระดานเดียวได้)
    /// </summary>
    Transfer = 1
}

/// <summary>
/// Transfer execution type for Transfer Mode
/// รูปแบบการโอนสำหรับโหมดโอนจริง
/// </summary>
public enum TransferExecutionType
{
    /// <summary>
    /// Auto Transfer - Bot handles withdrawal/deposit via API
    /// โอนอัตโนมัติ - บอทถอน/ฝากให้อัตโนมัติผ่าน API
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Manual Transfer - Bot notifies, user transfers manually
    /// โอนเอง - บอทแจ้งเตือน ผู้ใช้โอนเองผ่านเว็บ exchange
    /// </summary>
    Manual = 1
}

/// <summary>
/// Information about an arbitrage mode for display and logic
/// ข้อมูลโหมด Arbitrage สำหรับแสดงผลและตรรกะการทำงาน
/// </summary>
public class ArbitrageModeInfo
{
    public ArbitrageExecutionMode Mode { get; set; }

    /// <summary>
    /// English name of the mode
    /// </summary>
    public string EnglishName { get; set; } = string.Empty;

    /// <summary>
    /// Thai name of the mode
    /// ชื่อภาษาไทย
    /// </summary>
    public string ThaiName { get; set; } = string.Empty;

    /// <summary>
    /// Short description in English
    /// </summary>
    public string ShortDescription { get; set; } = string.Empty;

    /// <summary>
    /// Short description in Thai
    /// คำอธิบายสั้นภาษาไทย
    /// </summary>
    public string ShortDescriptionThai { get; set; } = string.Empty;

    /// <summary>
    /// Detailed description in English
    /// </summary>
    public string DetailedDescription { get; set; } = string.Empty;

    /// <summary>
    /// Detailed description in Thai
    /// คำอธิบายละเอียดภาษาไทย
    /// </summary>
    public string DetailedDescriptionThai { get; set; } = string.Empty;

    /// <summary>
    /// Icon/emoji for the mode
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// Whether this mode requires pre-funded balances on both exchanges
    /// ต้องมีเงินทุนบนทั้งสองกระดานหรือไม่
    /// </summary>
    public bool RequiresPreFundedBothSides { get; set; }

    /// <summary>
    /// Whether this mode involves actual crypto transfer between exchanges
    /// มีการโอนเหรียญจริงระหว่างกระดานหรือไม่
    /// </summary>
    public bool InvolvesTransfer { get; set; }

    /// <summary>
    /// Estimated execution time in milliseconds
    /// เวลาโดยประมาณในการดำเนินการ (มิลลิวินาที)
    /// </summary>
    public decimal EstimatedExecutionTimeMs { get; set; }

    /// <summary>
    /// Advantages of this mode (English)
    /// </summary>
    public string[] Advantages { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Advantages of this mode (Thai)
    /// ข้อดีของโหมดนี้
    /// </summary>
    public string[] AdvantagesThai { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Disadvantages of this mode (English)
    /// </summary>
    public string[] Disadvantages { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Disadvantages of this mode (Thai)
    /// ข้อเสียของโหมดนี้
    /// </summary>
    public string[] DisadvantagesThai { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Get Dual-Balance Mode info
    /// ข้อมูลโหมดสองกระเป๋า
    /// </summary>
    public static ArbitrageModeInfo DualBalance => new()
    {
        Mode = ArbitrageExecutionMode.DualBalance,
        EnglishName = "Dual-Balance Mode",
        ThaiName = "โหมดสองกระเป๋า",
        ShortDescription = "Instant execution using existing balances",
        ShortDescriptionThai = "เทรดทันที ใช้ยอดที่มีอยู่",
        DetailedDescription = "Uses pre-existing balances on BOTH exchanges. Buy using USDT on Exchange A, simultaneously sell using crypto on Exchange B. No blockchain transfer needed.",
        DetailedDescriptionThai = "ใช้ยอดเงินที่มีอยู่แล้วบนทั้งสองกระดานพร้อมกัน\nซื้อด้วย USDT บนกระดาน A พร้อมกับขายด้วย Crypto บนกระดาน B\nไม่ต้องโอนผ่าน blockchain",
        Icon = "⚡",
        RequiresPreFundedBothSides = true,
        InvolvesTransfer = false,
        EstimatedExecutionTimeMs = 500,
        Advantages = new[]
        {
            "Instant execution (<1 second)",
            "No transfer fees",
            "No price slippage from delays",
            "Can trade multiple times per minute"
        },
        AdvantagesThai = new[]
        {
            "เทรดทันที (ไม่ถึง 1 วินาที)",
            "ไม่เสียค่าโอน",
            "ไม่มีความเสี่ยงราคาเปลี่ยนระหว่างรอ",
            "เทรดได้หลายครั้งต่อนาที"
        },
        Disadvantages = new[]
        {
            "Requires capital on both exchanges",
            "Balances become imbalanced over time",
            "May need periodic manual rebalancing"
        },
        DisadvantagesThai = new[]
        {
            "ต้องมีเงินทุนบนทั้งสองกระดาน",
            "ยอดจะไม่สมดุลหลังเทรดหลายครั้ง",
            "อาจต้องปรับสมดุลเป็นระยะ"
        }
    };

    /// <summary>
    /// Get Transfer Mode info
    /// ข้อมูลโหมดโอนจริง
    /// </summary>
    public static ArbitrageModeInfo Transfer => new()
    {
        Mode = ArbitrageExecutionMode.Transfer,
        EnglishName = "Transfer Mode",
        ThaiName = "โหมดโอนจริง",
        ShortDescription = "Traditional arbitrage with crypto transfer",
        ShortDescriptionThai = "Arbitrage แบบดั้งเดิม โอนจริง",
        DetailedDescription = "Traditional arbitrage: Buy on Exchange A, withdraw crypto, deposit to Exchange B, then sell. Involves actual blockchain transfer.",
        DetailedDescriptionThai = "Arbitrage แบบดั้งเดิม:\nซื้อที่กระดาน A → ถอนเหรียญ → ฝากเข้ากระดาน B → ขาย\nมีการโอนจริงผ่าน blockchain",
        Icon = "🔄",
        RequiresPreFundedBothSides = false,
        InvolvesTransfer = true,
        EstimatedExecutionTimeMs = 600000, // ~10 minutes typical
        Advantages = new[]
        {
            "Only need funds on one exchange",
            "Can work with smaller capital",
            "Natural rebalancing through transfers"
        },
        AdvantagesThai = new[]
        {
            "ใช้เงินทุนบนกระดานเดียวได้",
            "เริ่มต้นด้วยทุนน้อยได้",
            "ยอดปรับสมดุลโดยอัตโนมัติจากการโอน"
        },
        Disadvantages = new[]
        {
            "Transfer fees eat into profit",
            "Blockchain confirmation delays",
            "Price may change during transfer",
            "Risk of stuck transactions"
        },
        DisadvantagesThai = new[]
        {
            "เสียค่าโอน ลดกำไร",
            "ต้องรอ blockchain ยืนยัน",
            "ราคาอาจเปลี่ยนระหว่างรอโอน",
            "เสี่ยงธุรกรรมค้าง"
        }
    };

    /// <summary>
    /// Get all available modes
    /// รับข้อมูลโหมดทั้งหมดที่มี
    /// </summary>
    public static ArbitrageModeInfo[] AllModes => new[] { DualBalance, Transfer };

    /// <summary>
    /// Get mode info by mode type
    /// รับข้อมูลโหมดตามประเภท
    /// </summary>
    public static ArbitrageModeInfo GetModeInfo(ArbitrageExecutionMode mode)
    {
        return mode switch
        {
            ArbitrageExecutionMode.DualBalance => DualBalance,
            ArbitrageExecutionMode.Transfer => Transfer,
            _ => DualBalance
        };
    }
}

/// <summary>
/// Information about transfer execution type
/// ข้อมูลรูปแบบการโอน
/// </summary>
public class TransferExecutionTypeInfo
{
    public TransferExecutionType Type { get; set; }
    public string EnglishName { get; set; } = string.Empty;
    public string ThaiName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DescriptionThai { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;

    public static TransferExecutionTypeInfo Auto => new()
    {
        Type = TransferExecutionType.Auto,
        EnglishName = "Auto Transfer",
        ThaiName = "โอนอัตโนมัติ",
        Description = "Bot handles withdrawal and deposit automatically via exchange API",
        DescriptionThai = "บอทถอนและฝากให้อัตโนมัติผ่าน API ของ exchange",
        Icon = "🤖"
    };

    public static TransferExecutionTypeInfo Manual => new()
    {
        Type = TransferExecutionType.Manual,
        EnglishName = "Manual Transfer",
        ThaiName = "โอนเอง",
        Description = "Bot notifies you when to transfer, you handle it manually via exchange website",
        DescriptionThai = "บอทแจ้งเตือนเมื่อถึงเวลาโอน คุณโอนเองผ่านเว็บ exchange",
        Icon = "👤"
    };

    public static TransferExecutionTypeInfo[] AllTypes => new[] { Auto, Manual };

    public static TransferExecutionTypeInfo GetTypeInfo(TransferExecutionType type)
    {
        return type switch
        {
            TransferExecutionType.Auto => Auto,
            TransferExecutionType.Manual => Manual,
            _ => Auto
        };
    }
}
