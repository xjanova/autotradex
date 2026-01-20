// AutoTrade-X v1.0.0

namespace AutoTradeX.Core.Models;

public enum PairStatus { Idle, Opportunity, Trading, Disabled, Error }

public class TradingPair
{
    public string Symbol { get; set; } = string.Empty;
    public string BaseCurrency { get; set; } = string.Empty;
    public string QuoteCurrency { get; set; } = string.Empty;
    public string ExchangeA_Symbol { get; set; } = string.Empty;
    public string ExchangeB_Symbol { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public PairStatus Status { get; set; } = PairStatus.Idle;
    public Ticker? TickerA { get; set; }
    public Ticker? TickerB { get; set; }
    public SpreadOpportunity? CurrentOpportunity { get; set; }
    public int TodayTradeCount { get; set; }
    public decimal TodayPnL { get; set; }
    public string? LastError { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public decimal MinOrderSize { get; set; } = 0.0001m;
    public int PricePrecision { get; set; } = 8;
    public int QuantityPrecision { get; set; } = 8;

    public static TradingPair FromSymbol(string symbol)
    {
        var parts = symbol.Split('/');
        if (parts.Length != 2)
            throw new ArgumentException($"Invalid symbol format: {symbol}. Expected format: BASE/QUOTE");

        return new TradingPair
        {
            Symbol = symbol,
            BaseCurrency = parts[0].ToUpperInvariant(),
            QuoteCurrency = parts[1].ToUpperInvariant(),
            ExchangeA_Symbol = symbol.Replace("/", ""),
            ExchangeB_Symbol = symbol.Replace("/", "")
        };
    }

    public override string ToString() =>
        $"{Symbol} [{Status}]: A={TickerA?.BidPrice:F4}/{TickerA?.AskPrice:F4}, B={TickerB?.BidPrice:F4}/{TickerB?.AskPrice:F4}";
}
