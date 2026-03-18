// AutoTrade-X v1.0.0
// Exchange Trading Fees Configuration

namespace AutoTradeX.Core.Models;

/// <summary>
/// Trading fee configuration for each exchange
/// ค่าธรรมเนียมการเทรดสำหรับแต่ละกระดานเทรด
///
/// Data sources (2024-2025):
/// - Binance: https://www.binance.com/en/fee/schedule
/// - OKX: https://www.okx.com/fees
/// - Bybit: https://www.bybit.com/en/help-center/article/Trading-Fee-Structure/
/// - Bitkub: https://www.bitkub.com/fee/cryptocurrency
/// - Gate.io: https://www.gate.io/fee
/// - KuCoin: https://www.kucoin.com/vip/level
/// </summary>
public static class ExchangeFees
{
    /// <summary>
    /// Get default trading fee (taker fee) for an exchange
    /// ดึงค่าธรรมเนียมเทรดเริ่มต้น (taker fee) สำหรับแต่ละกระดานเทรด
    /// </summary>
    public static decimal GetDefaultTakerFee(string exchangeName)
    {
        return exchangeName.ToLowerInvariant() switch
        {
            // Binance: 0.1% (Standard), 0.075% with BNB discount
            "binance" => 0.10m,

            // OKX: 0.08% maker / 0.1% taker (Standard)
            "okx" => 0.10m,

            // Bybit: 0.1% maker / 0.1% taker (Standard)
            "bybit" => 0.10m,

            // KuCoin: 0.1% maker / 0.1% taker (Standard)
            "kucoin" => 0.10m,

            // Gate.io: 0.2% taker (Standard), can be reduced with GT token
            "gate.io" or "gateio" or "gate" => 0.20m,

            // Bitkub (Thailand): 0.25% flat fee for all trades
            "bitkub" => 0.25m,

            // Default
            _ => 0.10m
        };
    }

    /// <summary>
    /// Get default maker fee for an exchange
    /// ดึงค่าธรรมเนียม maker เริ่มต้นสำหรับแต่ละกระดานเทรด
    /// </summary>
    public static decimal GetDefaultMakerFee(string exchangeName)
    {
        return exchangeName.ToLowerInvariant() switch
        {
            // Binance: 0.1% (Standard)
            "binance" => 0.10m,

            // OKX: 0.08% maker (Standard) - lower than taker
            "okx" => 0.08m,

            // Bybit: 0.1% maker (Standard)
            "bybit" => 0.10m,

            // KuCoin: 0.1% maker (Standard)
            "kucoin" => 0.10m,

            // Gate.io: 0.2% maker (Standard)
            "gate.io" or "gateio" or "gate" => 0.20m,

            // Bitkub (Thailand): 0.25% flat fee
            "bitkub" => 0.25m,

            // Default
            _ => 0.10m
        };
    }

    /// <summary>
    /// Get exchange fee info with both maker and taker fees
    /// ดึงข้อมูลค่าธรรมเนียมทั้ง maker และ taker
    /// </summary>
    public static ExchangeFeeInfo GetFeeInfo(string exchangeName)
    {
        var name = exchangeName.ToLowerInvariant();
        return name switch
        {
            "binance" => new ExchangeFeeInfo
            {
                ExchangeName = "Binance",
                MakerFeePercent = 0.10m,
                TakerFeePercent = 0.10m,
                DiscountToken = "BNB",
                DiscountPercent = 25m, // 25% off when paying with BNB
                Notes = "Use BNB to pay fees for 25% discount"
            },
            "okx" => new ExchangeFeeInfo
            {
                ExchangeName = "OKX",
                MakerFeePercent = 0.08m,
                TakerFeePercent = 0.10m,
                DiscountToken = "OKB",
                DiscountPercent = 10m,
                Notes = "Holding OKB reduces fees"
            },
            "bybit" => new ExchangeFeeInfo
            {
                ExchangeName = "Bybit",
                MakerFeePercent = 0.10m,
                TakerFeePercent = 0.10m,
                DiscountToken = null,
                DiscountPercent = 0m,
                Notes = "VIP levels reduce fees based on trading volume"
            },
            "kucoin" => new ExchangeFeeInfo
            {
                ExchangeName = "KuCoin",
                MakerFeePercent = 0.10m,
                TakerFeePercent = 0.10m,
                DiscountToken = "KCS",
                DiscountPercent = 20m,
                Notes = "Holding KCS reduces fees up to 20%"
            },
            "gate.io" or "gateio" or "gate" => new ExchangeFeeInfo
            {
                ExchangeName = "Gate.io",
                MakerFeePercent = 0.20m,
                TakerFeePercent = 0.20m,
                DiscountToken = "GT",
                DiscountPercent = 25m,
                Notes = "Use GT token to pay fees for discount"
            },
            "bitkub" => new ExchangeFeeInfo
            {
                ExchangeName = "Bitkub",
                MakerFeePercent = 0.25m,
                TakerFeePercent = 0.25m,
                DiscountToken = null,
                DiscountPercent = 0m,
                QuoteCurrency = "THB",
                Notes = "Thailand exchange - uses THB as quote currency"
            },
            _ => new ExchangeFeeInfo
            {
                ExchangeName = exchangeName,
                MakerFeePercent = 0.10m,
                TakerFeePercent = 0.10m,
                Notes = "Default fee structure"
            }
        };
    }

    /// <summary>
    /// Calculate total trading cost including fees
    /// คำนวณต้นทุนการเทรดทั้งหมดรวมค่าธรรมเนียม
    /// </summary>
    /// <param name="tradeAmount">Trade amount in quote currency</param>
    /// <param name="feePercent">Fee percentage (e.g., 0.1 for 0.1%)</param>
    /// <returns>Total cost including fee</returns>
    public static decimal CalculateTotalCost(decimal tradeAmount, decimal feePercent)
    {
        var fee = tradeAmount * (feePercent / 100m);
        return tradeAmount + fee;
    }

    /// <summary>
    /// Calculate net revenue after fees
    /// คำนวณรายได้สุทธิหลังหักค่าธรรมเนียม
    /// </summary>
    public static decimal CalculateNetRevenue(decimal tradeAmount, decimal feePercent)
    {
        var fee = tradeAmount * (feePercent / 100m);
        return tradeAmount - fee;
    }

    /// <summary>
    /// Calculate minimum profitable spread between two exchanges
    /// คำนวณ spread ขั้นต่ำที่จะทำกำไรได้ระหว่างสองกระดานเทรด
    /// </summary>
    /// <param name="exchange1">First exchange name</param>
    /// <param name="exchange2">Second exchange name</param>
    /// <param name="isMakerOnBoth">Whether using maker orders on both sides</param>
    /// <returns>Minimum spread percentage needed to profit</returns>
    public static decimal CalculateMinProfitableSpread(string exchange1, string exchange2, bool isMakerOnBoth = false)
    {
        decimal fee1, fee2;

        if (isMakerOnBoth)
        {
            fee1 = GetDefaultMakerFee(exchange1);
            fee2 = GetDefaultMakerFee(exchange2);
        }
        else
        {
            // Assume taker on buy side, maker on sell side (common arbitrage scenario)
            fee1 = GetDefaultTakerFee(exchange1);
            fee2 = GetDefaultMakerFee(exchange2);
        }

        // Total fees = buy fee + sell fee
        // Need at least this spread to break even
        return fee1 + fee2;
    }

    /// <summary>
    /// Get minimum order amount required by exchange
    /// ดึงจำนวนเงินขั้นต่ำที่กระดานเทรดกำหนด
    /// </summary>
    /// <param name="exchangeName">Exchange name</param>
    /// <returns>Minimum order amount in quote currency (USDT/THB)</returns>
    public static decimal GetMinimumOrderAmount(string exchangeName)
    {
        return exchangeName.ToLowerInvariant() switch
        {
            // Binance: 10 USDT minimum for spot trading
            "binance" => 10m,

            // OKX: 10 USDT minimum
            "okx" => 10m,

            // Bybit: 10 USDT minimum for spot
            "bybit" => 10m,

            // KuCoin: 10 USDT minimum
            "kucoin" => 10m,

            // Gate.io: 10 USDT minimum
            "gate.io" or "gateio" or "gate" => 10m,

            // Bitkub: 10 THB minimum (but recommend higher for practical trading)
            "bitkub" => 10m,

            // Default: 10 USDT
            _ => 10m
        };
    }

    /// <summary>
    /// Get recommended minimum balance to start AI trading
    /// ดึงยอดเงินขั้นต่ำที่แนะนำสำหรับเริ่ม AI Trading
    /// Should be higher than minimum order to allow for multiple trades
    /// </summary>
    /// <param name="exchangeName">Exchange name</param>
    /// <returns>Recommended minimum balance in quote currency</returns>
    public static decimal GetRecommendedMinBalance(string exchangeName)
    {
        return exchangeName.ToLowerInvariant() switch
        {
            // Need enough for multiple trades + buffer
            "binance" => 50m,     // 50 USDT
            "okx" => 50m,         // 50 USDT
            "bybit" => 50m,       // 50 USDT
            "kucoin" => 50m,      // 50 USDT
            "gate.io" or "gateio" or "gate" => 100m,  // 100 USDT (higher fees)
            "bitkub" => 500m,     // 500 THB (~$15 USD)
            _ => 50m
        };
    }

    /// <summary>
    /// Get minimum trade amount to cover fees (approximate)
    /// คำนวณจำนวนเงินขั้นต่ำในการเทรดเพื่อให้คุ้มค่าธรรมเนียม
    /// </summary>
    /// <param name="exchangeName">Exchange name</param>
    /// <param name="targetProfitPercent">Target profit percentage (e.g., 0.5 for 0.5%)</param>
    /// <returns>Minimum trade amount recommendation in USD</returns>
    public static decimal GetRecommendedMinTradeAmount(string exchangeName, decimal targetProfitPercent = 0.5m)
    {
        var feeInfo = GetFeeInfo(exchangeName);
        var totalFee = feeInfo.TakerFeePercent * 2; // Buy + Sell

        // If target profit is less than total fees, not profitable
        if (targetProfitPercent <= totalFee)
        {
            return -1; // Indicate not profitable
        }

        // Recommend minimum $50-100 for most exchanges to make fees worthwhile
        return exchangeName.ToLowerInvariant() switch
        {
            "bitkub" => 2000m,    // ~2000 THB minimum (~$57 USD)
            "gate.io" or "gateio" => 100m, // Higher fees need larger trades
            _ => 50m              // Most exchanges: $50 minimum recommended
        };
    }
}

/// <summary>
/// Exchange fee information model
/// </summary>
public class ExchangeFeeInfo
{
    public string ExchangeName { get; set; } = "";
    public decimal MakerFeePercent { get; set; }
    public decimal TakerFeePercent { get; set; }
    public string? DiscountToken { get; set; }
    public decimal DiscountPercent { get; set; }
    public string QuoteCurrency { get; set; } = "USDT";
    public string? Notes { get; set; }

    /// <summary>
    /// Get effective fee with discount applied
    /// </summary>
    public decimal GetEffectiveTakerFee(bool hasDiscount = false)
    {
        if (hasDiscount && DiscountPercent > 0)
        {
            return TakerFeePercent * (1 - DiscountPercent / 100m);
        }
        return TakerFeePercent;
    }

    /// <summary>
    /// Get effective maker fee with discount applied
    /// </summary>
    public decimal GetEffectiveMakerFee(bool hasDiscount = false)
    {
        if (hasDiscount && DiscountPercent > 0)
        {
            return MakerFeePercent * (1 - DiscountPercent / 100m);
        }
        return MakerFeePercent;
    }

    public override string ToString()
    {
        return $"{ExchangeName}: Maker {MakerFeePercent}% / Taker {TakerFeePercent}%";
    }
}
