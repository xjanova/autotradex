namespace AutoTradeX.Core.Models;

/// <summary>
/// State of a crypto transfer between exchanges
/// สถานะการโอนเหรียญระหว่างกระดาน
/// </summary>
public enum TransferState
{
    /// <summary>
    /// No transfer in progress
    /// ไม่มีการโอนกำลังดำเนินการ
    /// </summary>
    None = 0,

    /// <summary>
    /// Waiting to initiate withdrawal
    /// รอเริ่มถอนเหรียญ
    /// </summary>
    Pending = 1,

    /// <summary>
    /// Withdrawal initiated, waiting for exchange processing
    /// เริ่มถอนแล้ว รอ exchange ดำเนินการ
    /// </summary>
    Withdrawing = 2,

    /// <summary>
    /// On blockchain, waiting for confirmations
    /// อยู่บน blockchain รอการยืนยัน
    /// </summary>
    InTransit = 3,

    /// <summary>
    /// Arrived at destination exchange, pending credit
    /// ถึงกระดานปลายทางแล้ว รอเครดิตเข้าบัญชี
    /// </summary>
    Depositing = 4,

    /// <summary>
    /// Transfer complete, ready to sell
    /// โอนสำเร็จ พร้อมขาย
    /// </summary>
    Completed = 5,

    /// <summary>
    /// Transfer failed
    /// โอนล้มเหลว
    /// </summary>
    Failed = 6,

    /// <summary>
    /// Transfer cancelled by user
    /// ผู้ใช้ยกเลิกการโอน
    /// </summary>
    Cancelled = 7
}

/// <summary>
/// Tracks the status of a crypto transfer for Transfer Mode arbitrage
/// ติดตามสถานะการโอนเหรียญสำหรับโหมดโอนจริง
/// </summary>
public class TransferStatus
{
    /// <summary>
    /// Unique identifier for this transfer
    /// รหัสเฉพาะของการโอนนี้
    /// </summary>
    public string TransferId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Current state of the transfer
    /// สถานะปัจจุบันของการโอน
    /// </summary>
    public TransferState State { get; set; } = TransferState.None;

    /// <summary>
    /// Execution type (Auto or Manual)
    /// รูปแบบการโอน (อัตโนมัติหรือโอนเอง)
    /// </summary>
    public TransferExecutionType ExecutionType { get; set; } = TransferExecutionType.Auto;

    /// <summary>
    /// Asset being transferred (e.g., "BTC", "ETH")
    /// เหรียญที่โอน
    /// </summary>
    public string Asset { get; set; } = string.Empty;

    /// <summary>
    /// Amount being transferred
    /// จำนวนที่โอน
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Source exchange name
    /// ชื่อกระดานต้นทาง
    /// </summary>
    public string FromExchange { get; set; } = string.Empty;

    /// <summary>
    /// Destination exchange name
    /// ชื่อกระดานปลายทาง
    /// </summary>
    public string ToExchange { get; set; } = string.Empty;

    /// <summary>
    /// Network used for transfer (e.g., "BTC", "ERC20", "TRC20")
    /// เครือข่ายที่ใช้โอน
    /// </summary>
    public string Network { get; set; } = string.Empty;

    /// <summary>
    /// Withdrawal ID from source exchange
    /// รหัสถอนจากกระดานต้นทาง
    /// </summary>
    public string? WithdrawalId { get; set; }

    /// <summary>
    /// Blockchain transaction hash
    /// Transaction hash บน blockchain
    /// </summary>
    public string? TransactionHash { get; set; }

    /// <summary>
    /// Deposit ID at destination exchange
    /// รหัสฝากที่กระดานปลายทาง
    /// </summary>
    public string? DepositId { get; set; }

    /// <summary>
    /// Current number of blockchain confirmations
    /// จำนวนการยืนยันปัจจุบัน
    /// </summary>
    public int Confirmations { get; set; }

    /// <summary>
    /// Required number of confirmations
    /// จำนวนการยืนยันที่ต้องการ
    /// </summary>
    public int RequiredConfirmations { get; set; }

    /// <summary>
    /// Fee charged by source exchange for withdrawal
    /// ค่าธรรมเนียมถอนจากกระดานต้นทาง
    /// </summary>
    public decimal? TransferFee { get; set; }

    /// <summary>
    /// Network fee (gas fee, etc.)
    /// ค่าธรรมเนียมเครือข่าย (gas fee ฯลฯ)
    /// </summary>
    public decimal? NetworkFee { get; set; }

    /// <summary>
    /// Price when buy order was filled (for P&L tracking)
    /// ราคาตอนที่คำสั่งซื้อเสร็จ (สำหรับติดตามกำไร/ขาดทุน)
    /// </summary>
    public decimal PriceAtBuy { get; set; }

    /// <summary>
    /// Current price at destination exchange (for unrealized P&L)
    /// ราคาปัจจุบันที่กระดานปลายทาง (สำหรับกำไร/ขาดทุนที่ยังไม่รับรู้)
    /// </summary>
    public decimal? CurrentPrice { get; set; }

    /// <summary>
    /// Time when transfer was initiated
    /// เวลาที่เริ่มโอน
    /// </summary>
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Time when transfer was completed (or failed/cancelled)
    /// เวลาที่โอนเสร็จ (หรือล้มเหลว/ยกเลิก)
    /// </summary>
    public DateTime? CompletedTime { get; set; }

    /// <summary>
    /// Error message if transfer failed
    /// ข้อความแสดงข้อผิดพลาดถ้าโอนล้มเหลว
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Manual transfer instructions (for Manual mode)
    /// คำแนะนำการโอนเอง (สำหรับโหมดโอนเอง)
    /// </summary>
    public ManualTransferInstructions? ManualInstructions { get; set; }

    #region Calculated Properties

    /// <summary>
    /// Unrealized P&L based on current price
    /// กำไร/ขาดทุนที่ยังไม่รับรู้ ตามราคาปัจจุบัน
    /// </summary>
    public decimal UnrealizedPnL => CurrentPrice.HasValue
        ? (CurrentPrice.Value - PriceAtBuy) * Amount
        : 0;

    /// <summary>
    /// Unrealized P&L percentage
    /// เปอร์เซ็นต์กำไร/ขาดทุนที่ยังไม่รับรู้
    /// </summary>
    public decimal UnrealizedPnLPercent => PriceAtBuy > 0 && CurrentPrice.HasValue
        ? (CurrentPrice.Value - PriceAtBuy) / PriceAtBuy * 100
        : 0;

    /// <summary>
    /// Total fees (transfer + network)
    /// ค่าธรรมเนียมรวม
    /// </summary>
    public decimal TotalFees => (TransferFee ?? 0) + (NetworkFee ?? 0);

    /// <summary>
    /// Duration of transfer so far
    /// ระยะเวลาโอนจนถึงตอนนี้
    /// </summary>
    public TimeSpan Duration => (CompletedTime ?? DateTime.UtcNow) - StartTime;

    /// <summary>
    /// Is the transfer currently active (not completed/failed/cancelled)
    /// การโอนยังดำเนินการอยู่หรือไม่
    /// </summary>
    public bool IsActive => State != TransferState.None
                         && State != TransferState.Completed
                         && State != TransferState.Failed
                         && State != TransferState.Cancelled;

    /// <summary>
    /// Progress percentage (0-100)
    /// เปอร์เซ็นต์ความคืบหน้า (0-100)
    /// </summary>
    public int ProgressPercent => State switch
    {
        TransferState.None => 0,
        TransferState.Pending => 10,
        TransferState.Withdrawing => 25,
        TransferState.InTransit => 25 + (RequiredConfirmations > 0 ? (Confirmations * 50 / RequiredConfirmations) : 50),
        TransferState.Depositing => 85,
        TransferState.Completed => 100,
        TransferState.Failed => 0,
        TransferState.Cancelled => 0,
        _ => 0
    };

    #endregion

    #region State Display Helpers

    /// <summary>
    /// Get display text for current state (English)
    /// </summary>
    public string StateDisplayText => State switch
    {
        TransferState.None => "Not Started",
        TransferState.Pending => "Pending",
        TransferState.Withdrawing => "Withdrawing",
        TransferState.InTransit => $"In Transit ({Confirmations}/{RequiredConfirmations})",
        TransferState.Depositing => "Depositing",
        TransferState.Completed => "Completed",
        TransferState.Failed => "Failed",
        TransferState.Cancelled => "Cancelled",
        _ => "Unknown"
    };

    /// <summary>
    /// Get display text for current state (Thai)
    /// ข้อความแสดงสถานะปัจจุบัน (ภาษาไทย)
    /// </summary>
    public string StateDisplayTextThai => State switch
    {
        TransferState.None => "ยังไม่เริ่ม",
        TransferState.Pending => "รอดำเนินการ",
        TransferState.Withdrawing => "กำลังถอน",
        TransferState.InTransit => $"กำลังโอน ({Confirmations}/{RequiredConfirmations})",
        TransferState.Depositing => "กำลังฝาก",
        TransferState.Completed => "สำเร็จ",
        TransferState.Failed => "ล้มเหลว",
        TransferState.Cancelled => "ยกเลิก",
        _ => "ไม่ทราบ"
    };

    /// <summary>
    /// Get icon for current state
    /// </summary>
    public string StateIcon => State switch
    {
        TransferState.None => "○",
        TransferState.Pending => "⏳",
        TransferState.Withdrawing => "📤",
        TransferState.InTransit => "🔄",
        TransferState.Depositing => "📥",
        TransferState.Completed => "✓",
        TransferState.Failed => "✗",
        TransferState.Cancelled => "⊘",
        _ => "?"
    };

    #endregion
}

/// <summary>
/// Instructions for manual transfer (when user transfers themselves)
/// คำแนะนำสำหรับการโอนเอง (เมื่อผู้ใช้โอนเอง)
/// </summary>
public class ManualTransferInstructions
{
    /// <summary>
    /// Deposit address at destination exchange
    /// ที่อยู่กระเป๋าสำหรับฝากที่กระดานปลายทาง
    /// </summary>
    public string DepositAddress { get; set; } = string.Empty;

    /// <summary>
    /// Network to use for transfer
    /// เครือข่ายที่ต้องใช้ในการโอน
    /// </summary>
    public string Network { get; set; } = string.Empty;

    /// <summary>
    /// Memo/Tag if required (for coins like XRP, EOS)
    /// Memo/Tag ถ้าต้องการ (สำหรับเหรียญเช่น XRP, EOS)
    /// </summary>
    public string? Memo { get; set; }

    /// <summary>
    /// Minimum deposit amount
    /// จำนวนฝากขั้นต่ำ
    /// </summary>
    public decimal MinDepositAmount { get; set; }

    /// <summary>
    /// URL to source exchange withdrawal page
    /// ลิงก์ไปยังหน้าถอนของกระดานต้นทาง
    /// </summary>
    public string? SourceWithdrawalUrl { get; set; }

    /// <summary>
    /// URL to destination exchange deposit page
    /// ลิงก์ไปยังหน้าฝากของกระดานปลายทาง
    /// </summary>
    public string? DestinationDepositUrl { get; set; }

    /// <summary>
    /// Warning message to display (e.g., "Double-check the address!")
    /// ข้อความเตือนที่จะแสดง
    /// </summary>
    public string WarningMessage { get; set; } = "กรุณาตรวจสอบ address และ network ก่อนโอน!";

    /// <summary>
    /// Whether user has confirmed they've initiated the transfer
    /// ผู้ใช้ยืนยันว่าเริ่มโอนแล้วหรือยัง
    /// </summary>
    public bool UserConfirmedTransfer { get; set; }

    /// <summary>
    /// Time when user confirmed transfer
    /// เวลาที่ผู้ใช้ยืนยันการโอน
    /// </summary>
    public DateTime? UserConfirmedTime { get; set; }
}
