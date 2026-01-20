// AutoTrade-X v1.0.0

namespace AutoTradeX.Core.Models;

public class AssetBalance
{
    public string Asset { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public decimal Available { get; set; }
    public decimal Locked => Total - Available;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public override string ToString() => $"{Asset}: Available={Available:F8}, Locked={Locked:F8}, Total={Total:F8}";
}

public class AccountBalance
{
    public string Exchange { get; set; } = string.Empty;
    public Dictionary<string, AssetBalance> Assets { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public decimal GetAvailable(string asset) =>
        Assets.TryGetValue(asset.ToUpperInvariant(), out var balance) ? balance.Available : 0;

    public decimal GetTotal(string asset) =>
        Assets.TryGetValue(asset.ToUpperInvariant(), out var balance) ? balance.Total : 0;

    public bool HasSufficientBalance(string asset, decimal requiredAmount) =>
        GetAvailable(asset) >= requiredAmount;
}
