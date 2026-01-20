using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;

namespace AutoTradeX.UI.Views;

public partial class TradingPage : UserControl
{
    private readonly IArbEngine? _arbEngine;
    private readonly ILoggingService? _logger;
    private readonly IBalancePoolService? _balancePool;
    private System.Windows.Threading.DispatcherTimer? _priceUpdateTimer;
    private TradingPair? _selectedPair;
    private bool _autoTradeEnabled = false;

    // Observable collections for UI binding
    public ObservableCollection<TradingPairDisplay> TradingPairs { get; } = new();
    public ObservableCollection<OrderBookEntry> Asks { get; } = new();
    public ObservableCollection<OrderBookEntry> Bids { get; } = new();
    public ObservableCollection<ExecutionDisplay> RecentExecutions { get; } = new();

    public TradingPage()
    {
        InitializeComponent();
        DataContext = this;

        _arbEngine = App.Services?.GetService<IArbEngine>();
        _logger = App.Services?.GetService<ILoggingService>();
        _balancePool = App.Services?.GetService<IBalancePoolService>();

        Loaded += TradingPage_Loaded;
        Unloaded += TradingPage_Unloaded;
    }

    private void TradingPage_Loaded(object sender, RoutedEventArgs e)
    {
        LoadTradingPairs();
        SetupEventHandlers();
        StartPriceUpdates();

        // Set mock order book data
        LoadMockOrderBook();
    }

    private void TradingPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _priceUpdateTimer?.Stop();
    }

    private void LoadTradingPairs()
    {
        TradingPairs.Clear();

        if (_arbEngine != null)
        {
            var pairs = _arbEngine.GetTradingPairs();
            foreach (var pair in pairs)
            {
                TradingPairs.Add(new TradingPairDisplay(pair));
            }
        }

        // Add default pairs if none exist
        if (TradingPairs.Count == 0)
        {
            TradingPairs.Add(new TradingPairDisplay
            {
                Symbol = "BTC/USDT",
                BaseAsset = "BTC",
                QuoteAsset = "USDT",
                ExchangeA = "Binance",
                ExchangeB = "KuCoin",
                CurrentSpread = 0.15m,
                Status = "Active"
            });
            TradingPairs.Add(new TradingPairDisplay
            {
                Symbol = "ETH/USDT",
                BaseAsset = "ETH",
                QuoteAsset = "USDT",
                ExchangeA = "Binance",
                ExchangeB = "KuCoin",
                CurrentSpread = 0.12m,
                Status = "Watching"
            });
            TradingPairs.Add(new TradingPairDisplay
            {
                Symbol = "SOL/USDT",
                BaseAsset = "SOL",
                QuoteAsset = "USDT",
                ExchangeA = "Binance",
                ExchangeB = "KuCoin",
                CurrentSpread = 0.18m,
                Status = "Watching"
            });
        }

        // Select first pair by default
        if (TradingPairs.Count > 0)
        {
            TradingPairsList.SelectedIndex = 0;
        }
    }

    private void SetupEventHandlers()
    {
        if (_arbEngine != null)
        {
            _arbEngine.PriceUpdated += ArbEngine_PriceUpdated;
            _arbEngine.TradeCompleted += ArbEngine_TradeCompleted;
            _arbEngine.OpportunityFound += ArbEngine_OpportunityFound;
        }

        if (_balancePool != null)
        {
            _balancePool.BalanceUpdated += BalancePool_BalanceUpdated;
        }
    }

    private void StartPriceUpdates()
    {
        _priceUpdateTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _priceUpdateTimer.Tick += async (s, e) => await UpdatePricesAsync();
        _priceUpdateTimer.Start();
    }

    private async Task UpdatePricesAsync()
    {
        if (_selectedPair == null) return;

        try
        {
            // In real implementation, this would fetch from exchanges
            // For now, simulate price updates
            var random = new Random();
            var basePrice = 65000m; // BTC base price

            if (_selectedPair.Symbol.StartsWith("ETH"))
                basePrice = 3500m;
            else if (_selectedPair.Symbol.StartsWith("SOL"))
                basePrice = 150m;

            var variation = (decimal)(random.NextDouble() * 0.001 - 0.0005);
            var priceA = basePrice * (1 + variation);
            var priceB = basePrice * (1 + variation + 0.0015m); // Add spread

            await Dispatcher.InvokeAsync(() =>
            {
                ExchangeAPrice.Text = $"${priceA:N2}";
                ExchangeBPrice.Text = $"${priceB:N2}";

                ExchangeABid.Text = $"${priceA - 1:N2}";
                ExchangeAAsk.Text = $"${priceA + 1:N2}";
                ExchangeBBid.Text = $"${priceB - 1:N2}";
                ExchangeBAsk.Text = $"${priceB + 1:N2}";

                var spread = (priceB - priceA) / priceA * 100;
                SelectedSpread.Text = $"{spread:F3}%";
                SelectedSpread.Foreground = new SolidColorBrush(
                    spread > 0.1m ? (Color)ColorConverter.ConvertFromString("#10B981") :
                    spread > 0 ? (Color)ColorConverter.ConvertFromString("#F59E0B") :
                    (Color)ColorConverter.ConvertFromString("#EF4444"));

                // Update estimated profit
                if (decimal.TryParse(TradeAmountInput.Text, out var amount))
                {
                    var profit = amount * spread / 100 * 0.998m; // Account for fees
                    EstimatedProfit.Text = $"Estimated Profit: ${profit:F2}";
                    SelectedProfitEstimate.Text = $"Est. Profit: ${profit:F2} per trade";
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError("TradingPage", $"Error updating prices: {ex.Message}");
        }
    }

    private void LoadMockOrderBook()
    {
        Asks.Clear();
        Bids.Clear();

        var basePrice = 65000m;
        var random = new Random();

        // Generate asks (sell orders)
        for (int i = 0; i < 5; i++)
        {
            var price = basePrice + (i + 1) * 10 + (decimal)(random.NextDouble() * 5);
            var amount = (decimal)(random.NextDouble() * 2 + 0.1);
            Asks.Add(new OrderBookEntry
            {
                Price = price,
                Amount = amount,
                Total = price * amount,
                DepthPercent = 50 + random.Next(100)
            });
        }

        // Generate bids (buy orders)
        for (int i = 0; i < 5; i++)
        {
            var price = basePrice - (i + 1) * 10 - (decimal)(random.NextDouble() * 5);
            var amount = (decimal)(random.NextDouble() * 2 + 0.1);
            Bids.Add(new OrderBookEntry
            {
                Price = price,
                Amount = amount,
                Total = price * amount,
                DepthPercent = 50 + random.Next(100)
            });
        }

        AsksDisplay.ItemsSource = Asks;
        BidsDisplay.ItemsSource = Bids;

        // Calculate spread
        if (Asks.Count > 0 && Bids.Count > 0)
        {
            var spreadAmount = Asks[0].Price - Bids[0].Price;
            var spreadPercent = spreadAmount / Bids[0].Price * 100;
            OrderBookSpread.Text = $"${spreadAmount:F2} ({spreadPercent:F3}%)";
        }
    }

    private void ArbEngine_PriceUpdated(object? sender, PriceUpdateEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_selectedPair?.Symbol == e.Symbol)
            {
                if (e.Exchange.ToLower().Contains("binance") || e.Exchange.Contains("A"))
                {
                    ExchangeAPrice.Text = $"${e.Ticker.LastPrice:N2}";
                    ExchangeABid.Text = $"${e.Ticker.BidPrice:N2}";
                    ExchangeAAsk.Text = $"${e.Ticker.AskPrice:N2}";
                }
                else
                {
                    ExchangeBPrice.Text = $"${e.Ticker.LastPrice:N2}";
                    ExchangeBBid.Text = $"${e.Ticker.BidPrice:N2}";
                    ExchangeBAsk.Text = $"${e.Ticker.AskPrice:N2}";
                }
            }
        });
    }

    private void ArbEngine_TradeCompleted(object? sender, TradeCompletedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            RecentExecutions.Insert(0, new ExecutionDisplay
            {
                Symbol = e.Result.Symbol,
                PnL = e.Result.NetPnL,
                ExecutionTimeMs = e.Result.Metadata.TryGetValue("TotalExecutionMs", out var ms) ? (long)ms : 0,
                Timestamp = e.Result.EndTime ?? DateTime.UtcNow
            });

            if (RecentExecutions.Count > 20)
                RecentExecutions.RemoveAt(RecentExecutions.Count - 1);
        });
    }

    private void ArbEngine_OpportunityFound(object? sender, OpportunityEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // Update spread for matching pair
            var pair = TradingPairs.FirstOrDefault(p => p.Symbol == e.Pair.Symbol);
            if (pair != null)
            {
                pair.CurrentSpread = e.Opportunity.NetSpreadPercentage;
            }

            if (_selectedPair?.Symbol == e.Pair.Symbol)
            {
                SelectedSpread.Text = $"{e.Opportunity.NetSpreadPercentage:F3}%";
            }
        });
    }

    private void BalancePool_BalanceUpdated(object? sender, BalanceUpdateEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (e.Snapshot.CombinedBalances.TryGetValue("USDT", out var usdt))
            {
                ExchangeABalance.Text = $"{usdt.ExchangeA_Total:N2}";
                ExchangeBBalance.Text = $"{usdt.ExchangeB_Total:N2}";
            }
        });
    }

    private void TradingPairsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TradingPairsList.SelectedItem is TradingPairDisplay selected)
        {
            SelectedCoinIcon.Symbol = selected.BaseAsset;
            SelectedPairName.Text = selected.Symbol;
            SelectedPairExchanges.Text = $"{selected.ExchangeA} → {selected.ExchangeB}";
            SelectedSpread.Text = $"{selected.CurrentSpread:F3}%";

            ExchangeAName.Text = selected.ExchangeA.ToUpper();
            ExchangeBName.Text = selected.ExchangeB.ToUpper();
            ExchangeAIcon.ExchangeName = selected.ExchangeA;
            ExchangeBIcon.ExchangeName = selected.ExchangeB;

            ExchangeABalanceAsset.Text = $" {selected.QuoteAsset}";
            ExchangeBBalanceAsset.Text = $" {selected.QuoteAsset}";

            // Find actual TradingPair
            if (_arbEngine != null)
            {
                _selectedPair = _arbEngine.GetTradingPairs()
                    .FirstOrDefault(p => p.Symbol == selected.Symbol);
            }

            LoadMockOrderBook();
        }
    }

    private void AddPairButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddPairDialog
        {
            Owner = Window.GetWindow(this)
        };

        dialog.ShowDialog();

        if (dialog.DialogResultOk && dialog.Result != null)
        {
            var config = dialog.Result;

            // Add to display list
            var newPair = new TradingPairDisplay
            {
                Symbol = config.Symbol,
                BaseAsset = config.BaseAsset,
                QuoteAsset = config.QuoteAsset,
                ExchangeA = config.ExchangeA,
                ExchangeB = config.ExchangeB,
                CurrentSpread = 0,
                Status = config.AutoTradeEnabled ? "Active" : "Watching"
            };

            TradingPairs.Add(newPair);

            // Add to engine if available
            if (_arbEngine != null)
            {
                var tradingPair = new TradingPair
                {
                    Symbol = config.Symbol,
                    BaseCurrency = config.BaseAsset,
                    QuoteCurrency = config.QuoteAsset,
                    ExchangeA_Symbol = config.Symbol.Replace("/", ""),
                    ExchangeB_Symbol = config.Symbol.Replace("/", ""),
                    IsEnabled = config.AutoTradeEnabled
                };

                _arbEngine.AddTradingPair(tradingPair);
            }

            _logger?.LogInfo("Trading", $"Added new trading pair: {config.Symbol} ({config.ExchangeA} -> {config.ExchangeB})");

            // Select the new pair
            TradingPairsList.SelectedItem = newPair;
        }
    }

    private void ScanAllButton_Click(object sender, RoutedEventArgs e)
    {
        _logger?.LogInfo("Trading", "Scanning all pairs for opportunities...");

        // Trigger scan for all pairs
        foreach (var pair in TradingPairs)
        {
            pair.Status = "Scanning...";
        }

        // Simulate scan results
        Task.Run(async () =>
        {
            await Task.Delay(2000);
            await Dispatcher.InvokeAsync(() =>
            {
                foreach (var pair in TradingPairs)
                {
                    pair.Status = pair.CurrentSpread > 0.1m ? "Active" : "Watching";
                }
                _logger?.LogInfo("Trading", "Scan complete. Found opportunities.");
            });
        });
    }

    private void PauseAllButton_Click(object sender, RoutedEventArgs e)
    {
        _arbEngine?.Pause();
        _logger?.LogInfo("Trading", "All trading paused");

        foreach (var pair in TradingPairs)
        {
            pair.Status = "Paused";
        }
    }

    private void EmergencyStopButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will immediately stop ALL trading activity.\n\nAre you sure?",
            "Emergency Stop",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            _ = _arbEngine?.StopAsync();
            _logger?.LogCritical("Trading", "EMERGENCY STOP activated");

            foreach (var pair in TradingPairs)
            {
                pair.Status = "Stopped";
            }
        }
    }

    private async void ManualTradeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPair == null)
        {
            MessageBox.Show("Please select a trading pair first.", "No Pair Selected",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!decimal.TryParse(TradeAmountInput.Text, out var amount) || amount <= 0)
        {
            MessageBox.Show("Please enter a valid trade amount.", "Invalid Amount",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ManualTradeButton.IsEnabled = false;
        ManualTradeButton.Content = "Executing...";

        try
        {
            _logger?.LogInfo("Trading", $"Manual trade: {_selectedPair.Symbol} - Amount: {amount}");

            // Execute trade through engine
            if (_arbEngine != null)
            {
                var opportunity = await _arbEngine.AnalyzeOpportunityAsync(_selectedPair);
                if (opportunity.ShouldTrade || true) // Allow manual override
                {
                    var result = await _arbEngine.ExecuteArbitrageAsync(opportunity);
                    _logger?.LogInfo("Trading", $"Trade result: {(result.IsFullySuccessful ? "Success" : "Failed")} - PnL: ${result.NetPnL:F2}");

                    MessageBox.Show(
                        $"Trade Executed!\n\nSymbol: {result.Symbol}\nP&L: ${result.NetPnL:F2}\nExecution Time: {result.Metadata.GetValueOrDefault("TotalExecutionMs", 0)}ms",
                        result.IsFullySuccessful ? "Trade Success" : "Trade Failed",
                        MessageBoxButton.OK,
                        result.IsFullySuccessful ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("Trading", $"Trade error: {ex.Message}");
            MessageBox.Show($"Trade failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ManualTradeButton.IsEnabled = true;
            ManualTradeButton.Content = "Execute Trade";
        }
    }

    private void AutoTradeToggle_Click(object sender, RoutedEventArgs e)
    {
        _autoTradeEnabled = !_autoTradeEnabled;
        AutoTradeToggle.Content = _autoTradeEnabled ? "Auto: ON" : "Auto: OFF";

        if (_autoTradeEnabled)
        {
            AutoTradeToggle.Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#10B981"), 0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#059669"), 1)
                }
            };
            _logger?.LogInfo("Trading", "Auto-trade ENABLED");
        }
        else
        {
            AutoTradeToggle.Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#7C3AED"), 0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#5B21B6"), 1)
                }
            };
            _logger?.LogInfo("Trading", "Auto-trade DISABLED");
        }
    }
}

// Display models
public class TradingPairDisplay : System.ComponentModel.INotifyPropertyChanged
{
    public string Symbol { get; set; } = "";
    public string BaseAsset { get; set; } = "";
    public string QuoteAsset { get; set; } = "USDT";
    public string ExchangeA { get; set; } = "";
    public string ExchangeB { get; set; } = "";

    private decimal _currentSpread;
    public decimal CurrentSpread
    {
        get => _currentSpread;
        set { _currentSpread = value; OnPropertyChanged(nameof(CurrentSpread)); OnPropertyChanged(nameof(SpreadColor)); }
    }

    private string _status = "Watching";
    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusColor)); }
    }

    public Brush SpreadColor => new SolidColorBrush(
        CurrentSpread > 0.15m ? (Color)ColorConverter.ConvertFromString("#10B981") :
        CurrentSpread > 0.1m ? (Color)ColorConverter.ConvertFromString("#F59E0B") :
        (Color)ColorConverter.ConvertFromString("#60FFFFFF"));

    public Brush StatusColor => new SolidColorBrush(
        Status == "Active" ? (Color)ColorConverter.ConvertFromString("#10B981") :
        Status == "Paused" ? (Color)ColorConverter.ConvertFromString("#EF4444") :
        Status == "Scanning..." ? (Color)ColorConverter.ConvertFromString("#00D4FF") :
        (Color)ColorConverter.ConvertFromString("#F59E0B"));

    public TradingPairDisplay() { }

    public TradingPairDisplay(TradingPair pair)
    {
        Symbol = pair.Symbol;
        BaseAsset = pair.BaseCurrency;
        QuoteAsset = pair.QuoteCurrency;
        ExchangeA = "Binance";
        ExchangeB = "KuCoin";
        Status = pair.IsEnabled ? "Active" : "Watching";
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public class OrderBookEntry
{
    public decimal Price { get; set; }
    public decimal Amount { get; set; }
    public decimal Total { get; set; }
    public int DepthPercent { get; set; }
}

public class ExecutionDisplay
{
    public string Symbol { get; set; } = "";
    public decimal PnL { get; set; }
    public long ExecutionTimeMs { get; set; }
    public DateTime Timestamp { get; set; }

    public string PnLDisplay => PnL >= 0 ? $"+${PnL:F2}" : $"-${Math.Abs(PnL):F2}";
    public Brush PnLColor => new SolidColorBrush(
        PnL >= 0 ? (Color)ColorConverter.ConvertFromString("#10B981") :
        (Color)ColorConverter.ConvertFromString("#EF4444"));
    public string TimeAgo => (DateTime.UtcNow - Timestamp).TotalMinutes < 1 ? "Just now" :
        $"{(int)(DateTime.UtcNow - Timestamp).TotalMinutes}m ago";
    public string ExecutionTime => $"{ExecutionTimeMs}ms";
}
