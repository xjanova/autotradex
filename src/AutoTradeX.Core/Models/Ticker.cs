// AutoTrade-X v1.0.0

namespace AutoTradeX.Core.Models;

public class Ticker
{
    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public decimal BidPrice { get; set; }
    public decimal BidQuantity { get; set; }
    public decimal AskPrice { get; set; }
    public decimal AskQuantity { get; set; }
    public decimal LastPrice { get; set; }
    public decimal Volume24h { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public decimal SpreadPercentage => BidPrice > 0
        ? (AskPrice - BidPrice) / BidPrice * 100
        : 0;

    public decimal MidPrice => (BidPrice + AskPrice) / 2;

    public override string ToString() =>
        $"[{Exchange}] {Symbol}: Bid={BidPrice:F8}, Ask={AskPrice:F8}, Spread={SpreadPercentage:F4}%";
}
