// AutoTrade-X v1.0.0

using AutoTradeX.Core.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AutoTradeX.UI.ViewModels;

/// <summary>
/// TradingPairViewModel - ViewModel for displaying trading pair info in DataGrid
/// </summary>
public class TradingPairViewModel : INotifyPropertyChanged
{
    private readonly TradingPair _pair;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    public TradingPairViewModel(TradingPair pair)
    {
        _pair = pair ?? throw new ArgumentNullException(nameof(pair));
    }

    // ========== Basic Info ==========

    public string Symbol => _pair.Symbol;

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    private string _status = "Idle";
    public string Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
                OnPropertyChanged(nameof(StatusColor));
        }
    }

    public string StatusColor => Status switch
    {
        "Idle" => "#666680",
        "Opportunity" => "#F59E0B",
        "Trading" => "#3B82F6",
        "Error" => "#EF4444",
        _ => "#666680"
    };

    // ========== Exchange A ==========

    private decimal _exchangeA_Bid;
    public decimal ExchangeA_Bid
    {
        get => _exchangeA_Bid;
        set
        {
            if (SetProperty(ref _exchangeA_Bid, value))
                OnPropertyChanged(nameof(ExchangeA_Display));
        }
    }

    private decimal _exchangeA_Ask;
    public decimal ExchangeA_Ask
    {
        get => _exchangeA_Ask;
        set
        {
            if (SetProperty(ref _exchangeA_Ask, value))
                OnPropertyChanged(nameof(ExchangeA_Display));
        }
    }

    public string ExchangeA_Display => $"{ExchangeA_Bid:F2} / {ExchangeA_Ask:F2}";

    // ========== Exchange B ==========

    private decimal _exchangeB_Bid;
    public decimal ExchangeB_Bid
    {
        get => _exchangeB_Bid;
        set
        {
            if (SetProperty(ref _exchangeB_Bid, value))
                OnPropertyChanged(nameof(ExchangeB_Display));
        }
    }

    private decimal _exchangeB_Ask;
    public decimal ExchangeB_Ask
    {
        get => _exchangeB_Ask;
        set
        {
            if (SetProperty(ref _exchangeB_Ask, value))
                OnPropertyChanged(nameof(ExchangeB_Display));
        }
    }

    public string ExchangeB_Display => $"{ExchangeB_Bid:F2} / {ExchangeB_Ask:F2}";

    // ========== Spread & Profit ==========

    private decimal _spreadPercent;
    public decimal SpreadPercent
    {
        get => _spreadPercent;
        set
        {
            if (SetProperty(ref _spreadPercent, value))
            {
                OnPropertyChanged(nameof(SpreadDisplay));
                OnPropertyChanged(nameof(SpreadColor));
            }
        }
    }

    public string SpreadDisplay => $"{SpreadPercent:F4}%";
    public string SpreadColor => SpreadPercent > 0 ? "#10B981" : "#EF4444";

    private decimal _expectedProfit;
    public decimal ExpectedProfit
    {
        get => _expectedProfit;
        set
        {
            if (SetProperty(ref _expectedProfit, value))
            {
                OnPropertyChanged(nameof(ExpectedProfitDisplay));
                OnPropertyChanged(nameof(ProfitColor));
            }
        }
    }

    public string ExpectedProfitDisplay => ExpectedProfit > 0
        ? $"+{ExpectedProfit:F4} USDT"
        : $"{ExpectedProfit:F4} USDT";

    public string ProfitColor => ExpectedProfit > 0 ? "#10B981" : "#666680";

    // ========== Direction ==========

    private string _direction = "-";
    public string Direction
    {
        get => _direction;
        set => SetProperty(ref _direction, value);
    }

    private bool _shouldTrade;
    public bool ShouldTrade
    {
        get => _shouldTrade;
        set => SetProperty(ref _shouldTrade, value);
    }

    // ========== Today Stats ==========

    private int _todayTradeCount;
    public int TodayTradeCount
    {
        get => _todayTradeCount;
        set => SetProperty(ref _todayTradeCount, value);
    }

    private decimal _todayPnL;
    public decimal TodayPnL
    {
        get => _todayPnL;
        set
        {
            if (SetProperty(ref _todayPnL, value))
            {
                OnPropertyChanged(nameof(TodayPnLDisplay));
                OnPropertyChanged(nameof(TodayPnLColor));
            }
        }
    }

    public string TodayPnLDisplay => TodayPnL >= 0
        ? $"+{TodayPnL:F4} USDT"
        : $"{TodayPnL:F4} USDT";

    public string TodayPnLColor => TodayPnL >= 0 ? "#10B981" : "#EF4444";

    // ========== Update Methods ==========

    public void UpdateFromModel(TradingPair pair)
    {
        if (pair.TickerA != null)
        {
            ExchangeA_Bid = pair.TickerA.BidPrice;
            ExchangeA_Ask = pair.TickerA.AskPrice;
        }

        if (pair.TickerB != null)
        {
            ExchangeB_Bid = pair.TickerB.BidPrice;
            ExchangeB_Ask = pair.TickerB.AskPrice;
        }

        if (pair.CurrentOpportunity != null)
        {
            SpreadPercent = pair.CurrentOpportunity.NetSpreadPercentage;
            ExpectedProfit = pair.CurrentOpportunity.ExpectedNetProfitQuote;
            Direction = pair.CurrentOpportunity.Direction.ToString();
            ShouldTrade = pair.CurrentOpportunity.ShouldTrade;
        }

        Status = pair.Status.ToString();
        IsEnabled = pair.IsEnabled;
        TodayTradeCount = pair.TodayTradeCount;
        TodayPnL = pair.TodayPnL;
    }
}
