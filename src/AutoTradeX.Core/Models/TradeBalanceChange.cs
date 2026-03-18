namespace AutoTradeX.Core.Models;

/// <summary>
/// Tracks actual balance changes from a trade for real P&L calculation
/// ติดตามการเปลี่ยนแปลงยอดจริงจากการเทรดเพื่อคำนวณกำไร/ขาดทุนจริง
/// </summary>
public class TradeBalanceChange
{
    /// <summary>
    /// Trade ID this balance change is associated with
    /// รหัสการเทรดที่การเปลี่ยนแปลงยอดนี้เกี่ยวข้อง
    /// </summary>
    public string TradeId { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when balance snapshot was taken before trade
    /// เวลาที่บันทึกยอดก่อนเทรด
    /// </summary>
    public DateTime SnapshotBeforeTime { get; set; }

    /// <summary>
    /// Timestamp when balance snapshot was taken after trade
    /// เวลาที่บันทึกยอดหลังเทรด
    /// </summary>
    public DateTime SnapshotAfterTime { get; set; }

    /// <summary>
    /// Base asset symbol (e.g., "BTC", "ETH")
    /// สัญลักษณ์เหรียญหลัก
    /// </summary>
    public string BaseAsset { get; set; } = string.Empty;

    /// <summary>
    /// Quote asset symbol (e.g., "USDT", "BUSD")
    /// สัญลักษณ์เหรียญอ้างอิง
    /// </summary>
    public string QuoteAsset { get; set; } = string.Empty;

    #region Exchange A Balances (Buy Side)

    /// <summary>
    /// Quote currency balance on Exchange A BEFORE trade (e.g., USDT on Binance)
    /// ยอด Quote บน Exchange A ก่อนเทรด (เช่น USDT บน Binance)
    /// </summary>
    public decimal ExchangeA_QuoteBefore { get; set; }

    /// <summary>
    /// Quote currency balance on Exchange A AFTER trade
    /// ยอด Quote บน Exchange A หลังเทรด
    /// </summary>
    public decimal ExchangeA_QuoteAfter { get; set; }

    /// <summary>
    /// Base currency balance on Exchange A BEFORE trade (e.g., BTC on Binance)
    /// ยอด Base บน Exchange A ก่อนเทรด (เช่น BTC บน Binance)
    /// </summary>
    public decimal ExchangeA_BaseBefore { get; set; }

    /// <summary>
    /// Base currency balance on Exchange A AFTER trade
    /// ยอด Base บน Exchange A หลังเทรด
    /// </summary>
    public decimal ExchangeA_BaseAfter { get; set; }

    #endregion

    #region Exchange B Balances (Sell Side)

    /// <summary>
    /// Quote currency balance on Exchange B BEFORE trade (e.g., USDT on KuCoin)
    /// ยอด Quote บน Exchange B ก่อนเทรด (เช่น USDT บน KuCoin)
    /// </summary>
    public decimal ExchangeB_QuoteBefore { get; set; }

    /// <summary>
    /// Quote currency balance on Exchange B AFTER trade
    /// ยอด Quote บน Exchange B หลังเทรด
    /// </summary>
    public decimal ExchangeB_QuoteAfter { get; set; }

    /// <summary>
    /// Base currency balance on Exchange B BEFORE trade (e.g., BTC on KuCoin)
    /// ยอด Base บน Exchange B ก่อนเทรด (เช่น BTC บน KuCoin)
    /// </summary>
    public decimal ExchangeB_BaseBefore { get; set; }

    /// <summary>
    /// Base currency balance on Exchange B AFTER trade
    /// ยอด Base บน Exchange B หลังเทรด
    /// </summary>
    public decimal ExchangeB_BaseAfter { get; set; }

    #endregion

    #region Exchange Names

    /// <summary>
    /// Name of Exchange A (buy side)
    /// ชื่อ Exchange A (ฝั่งซื้อ)
    /// </summary>
    public string ExchangeAName { get; set; } = string.Empty;

    /// <summary>
    /// Name of Exchange B (sell side)
    /// ชื่อ Exchange B (ฝั่งขาย)
    /// </summary>
    public string ExchangeBName { get; set; } = string.Empty;

    #endregion

    #region Calculated Changes

    /// <summary>
    /// Change in quote currency on Exchange A (negative = spent USDT to buy)
    /// การเปลี่ยนแปลง Quote บน Exchange A (ลบ = ใช้ USDT ซื้อ)
    /// </summary>
    public decimal ExchangeA_QuoteChange => ExchangeA_QuoteAfter - ExchangeA_QuoteBefore;

    /// <summary>
    /// Change in base currency on Exchange A (positive = received crypto from buy)
    /// การเปลี่ยนแปลง Base บน Exchange A (บวก = ได้รับ crypto จากการซื้อ)
    /// </summary>
    public decimal ExchangeA_BaseChange => ExchangeA_BaseAfter - ExchangeA_BaseBefore;

    /// <summary>
    /// Change in quote currency on Exchange B (positive = received USDT from sell)
    /// การเปลี่ยนแปลง Quote บน Exchange B (บวก = ได้รับ USDT จากการขาย)
    /// </summary>
    public decimal ExchangeB_QuoteChange => ExchangeB_QuoteAfter - ExchangeB_QuoteBefore;

    /// <summary>
    /// Change in base currency on Exchange B (negative = spent crypto to sell)
    /// การเปลี่ยนแปลง Base บน Exchange B (ลบ = ใช้ crypto ขาย)
    /// </summary>
    public decimal ExchangeB_BaseChange => ExchangeB_BaseAfter - ExchangeB_BaseBefore;

    /// <summary>
    /// Total change in quote currency across both exchanges
    /// การเปลี่ยนแปลง Quote รวมทั้งสองกระดาน
    /// </summary>
    public decimal TotalQuoteChange => ExchangeA_QuoteChange + ExchangeB_QuoteChange;

    /// <summary>
    /// Total change in base currency across both exchanges
    /// การเปลี่ยนแปลง Base รวมทั้งสองกระดาน
    /// </summary>
    public decimal TotalBaseChange => ExchangeA_BaseChange + ExchangeB_BaseChange;

    #endregion

    #region P&L Calculation

    /// <summary>
    /// Real profit in quote currency (calculated from actual balance changes)
    /// กำไรจริงในหน่วย Quote (คำนวณจากการเปลี่ยนแปลงยอดจริง)
    /// </summary>
    public decimal RealProfitQuote { get; set; }

    /// <summary>
    /// Total fees paid (both buy and sell fees)
    /// ค่าธรรมเนียมรวมที่จ่าย (ทั้งซื้อและขาย)
    /// </summary>
    public decimal TotalFees { get; set; }

    /// <summary>
    /// Transfer fees (for Transfer Mode only)
    /// ค่าธรรมเนียมโอน (สำหรับโหมดโอนจริงเท่านั้น)
    /// </summary>
    public decimal TransferFees { get; set; }

    /// <summary>
    /// Price used for base asset valuation (for P&L calculation)
    /// ราคาที่ใช้ในการคำนวณมูลค่า Base (สำหรับคำนวณกำไร/ขาดทุน)
    /// </summary>
    public decimal BaseAssetPrice { get; set; }

    /// <summary>
    /// Net profit after all fees
    /// กำไรสุทธิหลังหักค่าธรรมเนียมทั้งหมด
    /// </summary>
    public decimal NetProfitQuote => RealProfitQuote - TotalFees - TransferFees;

    /// <summary>
    /// Net profit percentage based on total trade value
    /// เปอร์เซ็นต์กำไรสุทธิจากมูลค่าเทรดรวม
    /// </summary>
    public decimal NetProfitPercent { get; set; }

    #endregion

    #region Total Portfolio Value

    /// <summary>
    /// Total portfolio value BEFORE trade (in quote currency)
    /// มูลค่าพอร์ตรวมก่อนเทรด (ในหน่วย Quote)
    /// </summary>
    public decimal TotalValueBefore => (ExchangeA_QuoteBefore + ExchangeB_QuoteBefore)
                                     + (ExchangeA_BaseBefore + ExchangeB_BaseBefore) * BaseAssetPrice;

    /// <summary>
    /// Total portfolio value AFTER trade (in quote currency)
    /// มูลค่าพอร์ตรวมหลังเทรด (ในหน่วย Quote)
    /// </summary>
    public decimal TotalValueAfter => (ExchangeA_QuoteAfter + ExchangeB_QuoteAfter)
                                    + (ExchangeA_BaseAfter + ExchangeB_BaseAfter) * BaseAssetPrice;

    /// <summary>
    /// Portfolio value change (should match RealProfitQuote approximately)
    /// การเปลี่ยนแปลงมูลค่าพอร์ต (ควรใกล้เคียงกับ RealProfitQuote)
    /// </summary>
    public decimal PortfolioValueChange => TotalValueAfter - TotalValueBefore;

    #endregion

    #region Helper Methods

    /// <summary>
    /// Calculate real P&L from balance snapshots
    /// คำนวณกำไร/ขาดทุนจริงจากยอดที่บันทึก
    /// </summary>
    /// <param name="currentPrice">Current price of base asset in quote currency</param>
    public void CalculateRealPnL(decimal currentPrice)
    {
        BaseAssetPrice = currentPrice;

        // For Dual-Balance mode: the net effect should be positive USDT if profitable
        // Quote change: we spent USDT on A (negative) and received USDT on B (positive)
        // Base change: we received crypto on A (positive) and spent crypto on B (negative)
        // If executed correctly, base change should net to ~0 and quote change should be positive (profit)

        // Calculate real profit as the net change in quote currency
        // Plus any remaining base currency valued at current price
        RealProfitQuote = TotalQuoteChange + (TotalBaseChange * currentPrice);

        // Calculate profit percentage
        if (TotalValueBefore > 0)
        {
            NetProfitPercent = (NetProfitQuote / TotalValueBefore) * 100;
        }
    }

    /// <summary>
    /// Create a summary string of balance changes
    /// สร้างข้อความสรุปการเปลี่ยนแปลงยอด
    /// </summary>
    public string GetSummary()
    {
        return $"Trade {TradeId}:\n" +
               $"  {ExchangeAName}: {QuoteAsset} {ExchangeA_QuoteChange:+0.00;-0.00}, {BaseAsset} {ExchangeA_BaseChange:+0.00000000;-0.00000000}\n" +
               $"  {ExchangeBName}: {QuoteAsset} {ExchangeB_QuoteChange:+0.00;-0.00}, {BaseAsset} {ExchangeB_BaseChange:+0.00000000;-0.00000000}\n" +
               $"  Net: {QuoteAsset} {TotalQuoteChange:+0.00;-0.00}, {BaseAsset} {TotalBaseChange:+0.00000000;-0.00000000}\n" +
               $"  Real Profit: {RealProfitQuote:+0.00;-0.00} {QuoteAsset} ({NetProfitPercent:+0.00;-0.00}%)";
    }

    /// <summary>
    /// Create summary in Thai
    /// สร้างข้อความสรุปภาษาไทย
    /// </summary>
    public string GetSummaryThai()
    {
        return $"เทรด {TradeId}:\n" +
               $"  {ExchangeAName}: {QuoteAsset} {ExchangeA_QuoteChange:+0.00;-0.00}, {BaseAsset} {ExchangeA_BaseChange:+0.00000000;-0.00000000}\n" +
               $"  {ExchangeBName}: {QuoteAsset} {ExchangeB_QuoteChange:+0.00;-0.00}, {BaseAsset} {ExchangeB_BaseChange:+0.00000000;-0.00000000}\n" +
               $"  สุทธิ: {QuoteAsset} {TotalQuoteChange:+0.00;-0.00}, {BaseAsset} {TotalBaseChange:+0.00000000;-0.00000000}\n" +
               $"  กำไรจริง: {RealProfitQuote:+0.00;-0.00} {QuoteAsset} ({NetProfitPercent:+0.00;-0.00}%)";
    }

    #endregion
}

/// <summary>
/// Snapshot of balances for a trading pair on both exchanges
/// ภาพรวมยอดสำหรับคู่เทรดบนทั้งสองกระดาน
/// </summary>
public class TradingPairBalanceSnapshot
{
    /// <summary>
    /// Timestamp of snapshot
    /// เวลาที่ถ่ายภาพรวม
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Trading pair symbol (e.g., "BTC/USDT")
    /// สัญลักษณ์คู่เทรด
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Base asset (e.g., "BTC")
    /// </summary>
    public string BaseAsset { get; set; } = string.Empty;

    /// <summary>
    /// Quote asset (e.g., "USDT")
    /// </summary>
    public string QuoteAsset { get; set; } = string.Empty;

    /// <summary>
    /// Exchange A name
    /// </summary>
    public string ExchangeA { get; set; } = string.Empty;

    /// <summary>
    /// Exchange B name
    /// </summary>
    public string ExchangeB { get; set; } = string.Empty;

    /// <summary>
    /// Available quote currency on Exchange A
    /// </summary>
    public decimal ExchangeA_QuoteAvailable { get; set; }

    /// <summary>
    /// Available base currency on Exchange A
    /// </summary>
    public decimal ExchangeA_BaseAvailable { get; set; }

    /// <summary>
    /// Available quote currency on Exchange B
    /// </summary>
    public decimal ExchangeB_QuoteAvailable { get; set; }

    /// <summary>
    /// Available base currency on Exchange B
    /// </summary>
    public decimal ExchangeB_BaseAvailable { get; set; }

    /// <summary>
    /// Current price of base asset
    /// </summary>
    public decimal CurrentPrice { get; set; }

    /// <summary>
    /// Total value in quote currency
    /// </summary>
    public decimal TotalValueQuote =>
        (ExchangeA_QuoteAvailable + ExchangeB_QuoteAvailable) +
        (ExchangeA_BaseAvailable + ExchangeB_BaseAvailable) * CurrentPrice;
}

/// <summary>
/// Result of checking if balances are ready for Dual-Balance mode
/// ผลการตรวจสอบว่ายอดพร้อมสำหรับโหมดสองกระเป๋าหรือไม่
/// </summary>
public class DualBalanceReadiness
{
    /// <summary>
    /// Whether trading is possible with current balances
    /// สามารถเทรดได้ด้วยยอดปัจจุบันหรือไม่
    /// </summary>
    public bool IsReady { get; set; }

    /// <summary>
    /// Available quote currency on buy side (Exchange A)
    /// ยอด Quote ที่ใช้ได้บนฝั่งซื้อ (Exchange A)
    /// </summary>
    public decimal BuySideQuoteAvailable { get; set; }

    /// <summary>
    /// Required quote currency for the trade
    /// ยอด Quote ที่ต้องการสำหรับเทรด
    /// </summary>
    public decimal BuySideQuoteRequired { get; set; }

    /// <summary>
    /// Whether buy side has sufficient balance
    /// ฝั่งซื้อมียอดเพียงพอหรือไม่
    /// </summary>
    public bool BuySideReady => BuySideQuoteAvailable >= BuySideQuoteRequired;

    /// <summary>
    /// Available base currency on sell side (Exchange B)
    /// ยอด Base ที่ใช้ได้บนฝั่งขาย (Exchange B)
    /// </summary>
    public decimal SellSideBaseAvailable { get; set; }

    /// <summary>
    /// Required base currency for the trade
    /// ยอด Base ที่ต้องการสำหรับเทรด
    /// </summary>
    public decimal SellSideBaseRequired { get; set; }

    /// <summary>
    /// Whether sell side has sufficient balance
    /// ฝั่งขายมียอดเพียงพอหรือไม่
    /// </summary>
    public bool SellSideReady => SellSideBaseAvailable >= SellSideBaseRequired;

    /// <summary>
    /// Exchange A name
    /// </summary>
    public string ExchangeAName { get; set; } = string.Empty;

    /// <summary>
    /// Exchange B name
    /// </summary>
    public string ExchangeBName { get; set; } = string.Empty;

    /// <summary>
    /// Quote asset symbol
    /// </summary>
    public string QuoteAsset { get; set; } = string.Empty;

    /// <summary>
    /// Base asset symbol
    /// </summary>
    public string BaseAsset { get; set; } = string.Empty;

    /// <summary>
    /// Maximum tradeable quantity based on available balances
    /// ปริมาณสูงสุดที่เทรดได้ตามยอดที่มี
    /// </summary>
    public decimal MaxTradeableQuantity { get; set; }

    /// <summary>
    /// Reason if not ready
    /// เหตุผลถ้ายังไม่พร้อม
    /// </summary>
    public string? NotReadyReason { get; set; }

    /// <summary>
    /// Thai reason if not ready
    /// เหตุผลภาษาไทยถ้ายังไม่พร้อม
    /// </summary>
    public string? NotReadyReasonThai { get; set; }

    /// <summary>
    /// Get status message for Exchange A
    /// </summary>
    public string GetExchangeAStatus()
    {
        if (BuySideReady)
            return $"✓ {BuySideQuoteAvailable:N2} {QuoteAsset}";
        else
            return $"✗ {BuySideQuoteAvailable:N2} / {BuySideQuoteRequired:N2} {QuoteAsset}";
    }

    /// <summary>
    /// Get status message for Exchange B
    /// </summary>
    public string GetExchangeBStatus()
    {
        if (SellSideReady)
            return $"✓ {SellSideBaseAvailable:N8} {BaseAsset}";
        else
            return $"✗ {SellSideBaseAvailable:N8} / {SellSideBaseRequired:N8} {BaseAsset}";
    }
}
