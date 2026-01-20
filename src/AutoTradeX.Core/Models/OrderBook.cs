// AutoTrade-X v1.0.0

namespace AutoTradeX.Core.Models;

public class OrderBookEntry
{
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal Total => Price * Quantity;

    public OrderBookEntry() { }
    public OrderBookEntry(decimal price, decimal quantity) { Price = price; Quantity = quantity; }
}

public class OrderBook
{
    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public List<OrderBookEntry> Bids { get; set; } = new();
    public List<OrderBookEntry> Asks { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public long? SequenceNumber { get; set; }

    public decimal BestBidPrice => Bids.Count > 0 ? Bids[0].Price : 0;
    public decimal BestBidQuantity => Bids.Count > 0 ? Bids[0].Quantity : 0;
    public decimal BestAskPrice => Asks.Count > 0 ? Asks[0].Price : 0;
    public decimal BestAskQuantity => Asks.Count > 0 ? Asks[0].Quantity : 0;
    public decimal SpreadPercentage => BestBidPrice > 0 ? (BestAskPrice - BestBidPrice) / BestBidPrice * 100 : 0;
    public decimal MidPrice => (BestBidPrice + BestAskPrice) / 2;

    public decimal GetBidDepthQuantity(decimal priceDepthPercent)
    {
        if (BestBidPrice == 0 || Bids.Count == 0) return 0;
        var minPrice = BestBidPrice * (1 - priceDepthPercent / 100);
        return Bids.Where(b => b.Price >= minPrice).Sum(b => b.Quantity);
    }

    public decimal GetAskDepthQuantity(decimal priceDepthPercent)
    {
        if (BestAskPrice == 0 || Asks.Count == 0) return 0;
        var maxPrice = BestAskPrice * (1 + priceDepthPercent / 100);
        return Asks.Where(a => a.Price <= maxPrice).Sum(a => a.Quantity);
    }

    public decimal? GetAverageAskPrice(decimal quantity)
    {
        if (Asks.Count == 0 || quantity <= 0) return null;
        decimal totalCost = 0, remainingQty = quantity;
        foreach (var ask in Asks)
        {
            var fillQty = Math.Min(remainingQty, ask.Quantity);
            totalCost += fillQty * ask.Price;
            remainingQty -= fillQty;
            if (remainingQty <= 0) break;
        }
        return remainingQty > 0 ? null : totalCost / quantity;
    }

    public decimal? GetAverageBidPrice(decimal quantity)
    {
        if (Bids.Count == 0 || quantity <= 0) return null;
        decimal totalValue = 0, remainingQty = quantity;
        foreach (var bid in Bids)
        {
            var fillQty = Math.Min(remainingQty, bid.Quantity);
            totalValue += fillQty * bid.Price;
            remainingQty -= fillQty;
            if (remainingQty <= 0) break;
        }
        return remainingQty > 0 ? null : totalValue / quantity;
    }

    public bool HasSufficientLiquidity(decimal quantity, bool isBuy) =>
        isBuy ? GetAverageAskPrice(quantity) != null : GetAverageBidPrice(quantity) != null;
}
