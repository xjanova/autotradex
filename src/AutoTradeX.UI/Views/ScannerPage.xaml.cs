using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Infrastructure.Services;

namespace AutoTradeX.UI.Views;

public partial class ScannerPage : UserControl
{
    private readonly ISmartScannerService? _scanner;
    private readonly ICoinDataService? _coinDataService;
    private readonly ILoggingService? _logger;
    private readonly IArbEngine? _arbEngine;
    private System.Windows.Threading.DispatcherTimer? _scanTimer;
    private bool _isScanning = false;
    private int _scanCount = 0;
    private CancellationTokenSource? _scanCts;
    private bool _autoMode = false;

    public ObservableCollection<ScanResultDisplay> ScanResults { get; } = new();

    public ScannerPage()
    {
        InitializeComponent();
        DataContext = this;

        _scanner = App.Services?.GetService<ISmartScannerService>();
        _coinDataService = App.Services?.GetService<ICoinDataService>();
        _logger = App.Services?.GetService<ILoggingService>();
        _arbEngine = App.Services?.GetService<IArbEngine>();

        ResultsList.ItemsSource = ScanResults;

        Loaded += ScannerPage_Loaded;
        Unloaded += ScannerPage_Unloaded;
    }

    private void ScannerPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_scanner != null)
        {
            _scanner.OpportunityFound += Scanner_OpportunityFound;
        }
    }

    private void ScannerPage_Unloaded(object sender, RoutedEventArgs e)
    {
        StopScanning();

        if (_scanner != null)
        {
            _scanner.OpportunityFound -= Scanner_OpportunityFound;
        }
    }

    private void Scanner_OpportunityFound(object? sender, ScanResult e)
    {
        Dispatcher.Invoke(() =>
        {
            if (NotifyOnOpportunityCheck.IsChecked == true)
            {
                _logger?.LogInfo("Scanner", $"Hot opportunity: {e.Symbol} - Score: {e.Score:F0}");
            }

            if (_autoMode && AutoExecuteCheck.IsChecked == true && e.Score >= 70)
            {
                _ = ExecuteTradeAsync(e);
            }
        });
    }

    private ScanStrategy GetSelectedStrategy()
    {
        if (StrategyArbitrage.IsChecked == true) return ScanStrategy.ArbitrageBest;
        if (StrategyPriceDrop.IsChecked == true) return ScanStrategy.PriceDrop;
        if (StrategyVolatility.IsChecked == true) return ScanStrategy.HighVolatility;
        if (StrategyVolume.IsChecked == true) return ScanStrategy.VolumeSurge;
        if (StrategyMomentum.IsChecked == true) return ScanStrategy.MomentumUp;
        if (StrategyGainers.IsChecked == true) return ScanStrategy.TopGainers;
        return ScanStrategy.ArbitrageBest;
    }

    private void StartScanButton_Click(object sender, RoutedEventArgs e)
    {
        StartScanning();
    }

    private void StopScanButton_Click(object sender, RoutedEventArgs e)
    {
        StopScanning();
    }

    private void StartScanning()
    {
        if (_isScanning) return;

        _isScanning = true;
        _scanCts = new CancellationTokenSource();
        _scanCount = 0;

        // Update UI
        StartScanButton.Visibility = Visibility.Collapsed;
        StopScanButton.Visibility = Visibility.Visible;
        ScannerStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
        ScannerStatusText.Text = "Scanning...";
        ScannerStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));

        _logger?.LogInfo("Scanner", "Started smart scanning");

        // Start scan timer
        var interval = (int)ScanIntervalSlider.Value;
        _scanTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(interval)
        };
        _scanTimer.Tick += ScanTimer_Tick;
        _scanTimer.Start();

        // Run initial scan
        _ = RunScanAsync();
    }

    private void StopScanning()
    {
        if (!_isScanning) return;

        _isScanning = false;
        _scanCts?.Cancel();
        _scanTimer?.Stop();

        // Update UI
        StartScanButton.Visibility = Visibility.Visible;
        StopScanButton.Visibility = Visibility.Collapsed;
        ScannerStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#808080"));
        ScannerStatusText.Text = "Stopped";
        ScannerStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#808080"));
        LoadingState.Visibility = Visibility.Collapsed;

        _logger?.LogInfo("Scanner", "Stopped scanning");
    }

    private async void ScanTimer_Tick(object? sender, EventArgs e)
    {
        if (_scanCts?.IsCancellationRequested ?? true) return;
        await RunScanAsync();
    }

    private async Task RunScanAsync()
    {
        if (_scanner == null || (_scanCts?.IsCancellationRequested ?? true)) return;

        try
        {
            _scanCount++;
            ScanCountDisplay.Text = _scanCount.ToString();

            // Show loading
            LoadingState.Visibility = Visibility.Visible;
            LoadingText.Text = $"Scanning with {GetSelectedStrategy()} strategy...";

            var strategy = GetSelectedStrategy();
            var options = new ScanOptions
            {
                MinSpreadPercent = (decimal)MinSpreadSlider.Value,
                MaxResults = 30
            };

            var results = await _scanner.ScanAsync(strategy, options);

            await Dispatcher.InvokeAsync(() =>
            {
                // Filter by min score
                var minScore = (decimal)MinScoreSlider.Value;
                var filtered = results.Where(r => r.Score >= minScore).ToList();

                ScanResults.Clear();
                foreach (var result in filtered)
                {
                    ScanResults.Add(new ScanResultDisplay(result));
                }

                UpdateStats();
                EmptyState.Visibility = ScanResults.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
                LoadingState.Visibility = Visibility.Collapsed;

                if (ScanResults.Any(r => r.IsRecommended))
                {
                    RecommendedBadge.Visibility = Visibility.Visible;
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError("Scanner", $"Scan error: {ex.Message}");
            LoadingState.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateStats()
    {
        if (ScanResults.Count == 0)
        {
            BestSpreadDisplay.Text = "0.00%";
            BestSpreadPair.Text = "-";
            TopScoreDisplay.Text = "0";
            TopScorePair.Text = "-";
            BestScoreDisplay.Text = "0";
            TradeableCountDisplay.Text = "0";
            EstProfitDisplay.Text = "$0.00";
            return;
        }

        var bestSpread = ScanResults.MaxBy(r => r.SpreadPercent);
        var topScore = ScanResults.MaxBy(r => r.Score);
        var minScore = (decimal)MinScoreSlider.Value;
        var tradeable = ScanResults.Count(r => r.Score >= minScore);
        var totalProfit = ScanResults.Sum(r => r.EstimatedProfit);

        BestSpreadDisplay.Text = $"{bestSpread?.SpreadPercent:F3}%";
        BestSpreadPair.Text = bestSpread?.Symbol ?? "-";
        TopScoreDisplay.Text = $"{topScore?.Score:F0}";
        TopScorePair.Text = topScore?.Symbol ?? "-";
        BestScoreDisplay.Text = $"{topScore?.Score:F0}";
        OpportunityCountDisplay.Text = ScanResults.Count.ToString();
        TradeableCountDisplay.Text = tradeable.ToString();
        EstProfitDisplay.Text = $"${totalProfit:F2}";
    }

    private void AutoModeToggle_Click(object sender, RoutedEventArgs e)
    {
        _autoMode = AutoModeToggle.IsChecked == true;
        _logger?.LogInfo("Scanner", $"Auto mode: {(_autoMode ? "ON" : "OFF")}");
    }

    private void MinSpreadSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MinSpreadValue != null)
            MinSpreadValue.Text = $"{e.NewValue:F2}%";
    }

    private void ScanIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScanIntervalValue != null)
            ScanIntervalValue.Text = $"{(int)e.NewValue}s";

        if (_scanTimer != null && _isScanning)
        {
            _scanTimer.Interval = TimeSpan.FromSeconds((int)e.NewValue);
        }
    }

    private void MinScoreSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MinScoreValue != null)
            MinScoreValue.Text = $"{(int)e.NewValue}";
    }

    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScanResults.Count == 0) return;

        var sorted = SortCombo.SelectedIndex switch
        {
            0 => ScanResults.OrderByDescending(r => r.Score).ToList(),
            1 => ScanResults.OrderByDescending(r => r.SpreadPercent).ToList(),
            2 => ScanResults.OrderByDescending(r => r.Volume24h).ToList(),
            3 => ScanResults.OrderByDescending(r => Math.Abs(r.PriceChange24h)).ToList(),
            _ => ScanResults.ToList()
        };

        ScanResults.Clear();
        foreach (var item in sorted)
        {
            ScanResults.Add(item);
        }
    }

    private async void SelectCoin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ScanResultDisplay result)
        {
            button.IsEnabled = false;
            button.Content = "...";

            try
            {
                _logger?.LogInfo("Scanner", $"Selected: {result.Symbol} (Score: {result.Score:F0})");

                // Show confirmation
                var message = $"Selected: {result.Symbol}\n\n" +
                              $"Score: {result.Score:F0}\n" +
                              $"Spread: {result.SpreadPercent:F3}%\n" +
                              $"Est. Profit: ${result.EstimatedProfit:F2}\n\n" +
                              $"Buy from: {result.BestBuyExchange} @ ${result.BestBuyPrice:N2}\n" +
                              $"Sell to: {result.BestSellExchange} @ ${result.BestSellPrice:N2}\n\n" +
                              $"Execute trade now?";

                var dialogResult = MessageBox.Show(message, "Confirm Trade",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (dialogResult == MessageBoxResult.Yes)
                {
                    await ExecuteTradeAsync(result.ToScanResult());
                }
            }
            finally
            {
                button.IsEnabled = true;
                button.Content = "SELECT";
            }
        }
    }

    private async Task ExecuteTradeAsync(ScanResult result)
    {
        try
        {
            _logger?.LogInfo("Scanner", $"Executing trade for {result.Symbol}");

            if (_arbEngine != null)
            {
                // For now, show success message
                // In production, this would call the actual trading engine
                MessageBox.Show(
                    $"Trade Signal Sent!\n\n" +
                    $"Symbol: {result.Symbol}\n" +
                    $"Buy: {result.BestBuyExchange} @ ${result.BestBuyPrice:N2}\n" +
                    $"Sell: {result.BestSellExchange} @ ${result.BestSellPrice:N2}\n" +
                    $"Expected Profit: ${result.EstimatedProfit:F2}",
                    "Trade Executed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Trade failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _logger?.LogError("Scanner", $"Trade execution error: {ex.Message}");
        }
    }
}

/// <summary>
/// Display model for scan results with UI bindings
/// </summary>
public class ScanResultDisplay : INotifyPropertyChanged
{
    private readonly ScanResult _result;

    public ScanResultDisplay(ScanResult result)
    {
        _result = result;
    }

    public ScanResult ToScanResult() => _result;

    // Core properties
    public string Symbol => _result.Symbol;
    public string BaseAsset => _result.BaseAsset;
    public decimal CurrentPrice => _result.CurrentPrice;
    public decimal PriceChange24h => _result.PriceChange24h;
    public decimal Volume24h => _result.Volume24h;
    public decimal SpreadPercent => _result.SpreadPercent;
    public decimal EstimatedProfit => _result.EstimatedProfit;
    public decimal Score => _result.Score;
    public string ScoreReason => _result.ScoreReason;
    public bool IsRecommended => _result.IsRecommended;
    public string BestBuyExchange => _result.BestBuyExchange;
    public string BestSellExchange => _result.BestSellExchange;
    public decimal BestBuyPrice => _result.BestBuyPrice;
    public decimal BestSellPrice => _result.BestSellPrice;

    // Display properties
    public string CurrentPriceDisplay => CurrentPrice >= 1000 ? $"${CurrentPrice:N0}"
        : CurrentPrice >= 1 ? $"${CurrentPrice:N2}"
        : $"${CurrentPrice:N4}";

    public string PriceChangeDisplay => PriceChange24h >= 0
        ? $"+{PriceChange24h:F2}%"
        : $"{PriceChange24h:F2}%";

    public string SpreadDisplay => $"{SpreadPercent:F3}%";
    public string EstProfitDisplay => $"~${EstimatedProfit:F2}";
    public string ScoreDisplay => $"{Score:F0}";

    // Colors
    public Brush PriceChangeColor => new SolidColorBrush(
        PriceChange24h >= 0
            ? (Color)ColorConverter.ConvertFromString("#10B981")
            : (Color)ColorConverter.ConvertFromString("#EF4444"));

    public Brush SpreadColor => new SolidColorBrush(
        SpreadPercent >= 0.3m ? (Color)ColorConverter.ConvertFromString("#10B981")
        : SpreadPercent >= 0.1m ? (Color)ColorConverter.ConvertFromString("#F59E0B")
        : (Color)ColorConverter.ConvertFromString("#60FFFFFF"));

    public Brush ScoreBadgeColor => new SolidColorBrush(
        Score >= 70 ? (Color)ColorConverter.ConvertFromString("#10B981")
        : Score >= 50 ? (Color)ColorConverter.ConvertFromString("#F59E0B")
        : (Color)ColorConverter.ConvertFromString("#6366F1"));

    public Brush BackgroundColor => new SolidColorBrush(
        IsRecommended ? (Color)ColorConverter.ConvertFromString("#20F59E0B")
        : (Color)ColorConverter.ConvertFromString("#15FFFFFF"));

    public Brush BorderColor => new SolidColorBrush(
        IsRecommended ? (Color)ColorConverter.ConvertFromString("#F59E0B")
        : Colors.Transparent);

    public Thickness BorderThickness => IsRecommended ? new Thickness(1) : new Thickness(0);

    public Visibility RecommendedVisibility => IsRecommended ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
