// AutoTrade-X v1.0.0

namespace AutoTradeX.Core.Models;

public enum OrderSide { Buy, Sell }

public enum OrderType { Market, Limit, ImmediateOrCancel, FillOrKill }

public enum OrderStatus { Pending, Open, PartiallyFilled, Filled, Cancelled, Rejected, Expired, Error }

public class OrderRequest
{
    public string ClientOrderId { get; set; } = Guid.NewGuid().ToString("N");
    public string Symbol { get; set; } = string.Empty;
    public OrderSide Side { get; set; }
    public OrderType Type { get; set; }
    public decimal Quantity { get; set; }
    public decimal? Price { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public class Order
{
    public string OrderId { get; set; } = string.Empty;
    public string ClientOrderId { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public OrderSide Side { get; set; }
    public OrderType Type { get; set; }
    public OrderStatus Status { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal FilledQuantity { get; set; }
    public decimal RemainingQuantity => RequestedQuantity - FilledQuantity;
    public decimal? RequestedPrice { get; set; }
    public decimal? AverageFilledPrice { get; set; }
    public decimal FilledValue => FilledQuantity * (AverageFilledPrice ?? 0);
    public decimal Fee { get; set; }
    public string FeeCurrency { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? ErrorMessage { get; set; }

    public bool IsFinal => Status is OrderStatus.Filled or OrderStatus.Cancelled
                            or OrderStatus.Rejected or OrderStatus.Expired or OrderStatus.Error;

    public decimal FillPercentage => RequestedQuantity > 0 ? (FilledQuantity / RequestedQuantity) * 100 : 0;

    public override string ToString() =>
        $"[{Exchange}] {OrderId}: {Side} {Symbol} Filled={FilledQuantity}/{RequestedQuantity} @ {AverageFilledPrice:F8} Status={Status}";
}
