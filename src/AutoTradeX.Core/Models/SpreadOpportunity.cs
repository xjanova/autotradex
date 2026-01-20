// AutoTrade-X v1.0.0

namespace AutoTradeX.Core.Models;

public enum ArbitrageDirection { BuyA_SellB, BuyB_SellA, None }

public class SpreadOpportunity
{
    public string Symbol { get; set; } = string.Empty;
    public ArbitrageDirection Direction { get; set; } = ArbitrageDirection.None;

    // Exchange A prices
    public decimal ExchangeA_BidPrice { get; set; }
    public decimal ExchangeA_AskPrice { get; set; }
    public decimal ExchangeA_BidQuantity { get; set; }
    public decimal ExchangeA_AskQuantity { get; set; }

    // Exchange B prices
    public decimal ExchangeB_BidPrice { get; set; }
    public decimal ExchangeB_AskPrice { get; set; }
    public decimal ExchangeB_BidQuantity { get; set; }
    public decimal ExchangeB_AskQuantity { get; set; }

    // Spread calculations
    public decimal SpreadBuyA_SellB => ExchangeA_AskPrice > 0
        ? (ExchangeB_BidPrice - ExchangeA_AskPrice) / ExchangeA_AskPrice * 100 : 0;

    public decimal SpreadBuyB_SellA => ExchangeB_AskPrice > 0
        ? (ExchangeA_BidPrice - ExchangeB_AskPrice) / ExchangeB_AskPrice * 100 : 0;

    public decimal BestSpreadPercentage => Math.Max(SpreadBuyA_SellB, SpreadBuyB_SellA);

    // Fees
    public decimal ExchangeA_FeePercent { get; set; }
    public decimal ExchangeB_FeePercent { get; set; }
    public decimal TotalFeePercent => ExchangeA_FeePercent + ExchangeB_FeePercent;

    // Profit calculations
    public decimal NetSpreadPercentage => BestSpreadPercentage - TotalFeePercent;
    public decimal SuggestedQuantity { get; set; }

    public decimal BuyPrice => Direction switch
    {
        ArbitrageDirection.BuyA_SellB => ExchangeA_AskPrice,
        ArbitrageDirection.BuyB_SellA => ExchangeB_AskPrice,
        _ => 0
    };

    public decimal SellPrice => Direction switch
    {
        ArbitrageDirection.BuyA_SellB => ExchangeB_BidPrice,
        ArbitrageDirection.BuyB_SellA => ExchangeA_BidPrice,
        _ => 0
    };

    public decimal ExpectedNetProfitQuote { get; set; }

    public decimal ExpectedNetProfitPercent => BuyPrice > 0 && SuggestedQuantity > 0
        ? (ExpectedNetProfitQuote / (BuyPrice * SuggestedQuantity)) * 100 : 0;

    // Trade conditions
    public bool HasPositiveSpread => NetSpreadPercentage > 0;
    public bool MeetsMinSpread { get; set; }
    public bool MeetsMinProfit { get; set; }
    public bool HasSufficientLiquidity { get; set; }
    public bool HasSufficientBalance { get; set; }

    public bool ShouldTrade => HasPositiveSpread && MeetsMinSpread && MeetsMinProfit
                               && HasSufficientLiquidity && HasSufficientBalance
                               && Direction != ArbitrageDirection.None;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Remarks { get; set; }

    public override string ToString() =>
        $"{Symbol}: Dir={Direction}, Spread={BestSpreadPercentage:F4}%, Net={NetSpreadPercentage:F4}%, " +
        $"Profit={ExpectedNetProfitQuote:F4} USDT, Trade={ShouldTrade}";
}
