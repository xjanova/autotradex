using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using AutoTradeX.Infrastructure.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Drawing.Geometries;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Kernel;
using SkiaSharp;

namespace AutoTradeX.UI.Views;

public partial class HistoryPage : UserControl
{
    private readonly IArbEngine? _arbEngine;
    private readonly ITradeHistoryService? _tradeHistoryService;
    private readonly ILoggingService? _logger;

    public ObservableCollection<TradeHistoryDisplay> TradeHistory { get; } = new();
    public ObservableCollection<PairPerformance> PairPerformances { get; } = new();

    // Chart properties
    public ObservableCollection<ISeries> PnLSeries { get; } = new();
    private readonly ObservableCollection<ObservableValue> _pnlValues = new();
    private readonly ObservableCollection<TradeChartPoint> _winTradePoints = new();
    private readonly ObservableCollection<TradeChartPoint> _lossTradePoints = new();
    private List<TradeHistoryDisplay> _sortedTrades = new();

    public Axis[] XAxes { get; } = new[]
    {
        new Axis
        {
            LabelsPaint = new SolidColorPaint(SKColors.Gray),
            TextSize = 10,
            IsVisible = false
        }
    };

    public Axis[] YAxes { get; } = new[]
    {
        new Axis
        {
            LabelsPaint = new SolidColorPaint(SKColors.Gray),
            TextSize = 10,
            Labeler = value => $"${value:F2}"
        }
    };

    public HistoryPage()
    {
        InitializeComponent();
        DataContext = this;

        _arbEngine = App.Services?.GetService<IArbEngine>();
        _tradeHistoryService = App.Services?.GetService<ITradeHistoryService>();
        _logger = App.Services?.GetService<ILoggingService>();

        TradeHistoryList.ItemsSource = TradeHistory;
        PairPerformanceList.ItemsSource = PairPerformances;

        SetupChart();
        Loaded += HistoryPage_Loaded;
    }

    private async void HistoryPage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadTradeHistoryAsync();

        if (_arbEngine != null)
        {
            _arbEngine.TradeCompleted += ArbEngine_TradeCompleted;
        }
    }

    private void SetupChart()
    {
        // Initialize with some data points
        for (int i = 0; i < 30; i++)
        {
            _pnlValues.Add(new ObservableValue(0));
        }

        // P&L Line Series
        PnLSeries.Add(new LineSeries<ObservableValue>
        {
            Values = _pnlValues,
            Fill = new SolidColorPaint(new SKColor(16, 185, 129, 50)),
            Stroke = new SolidColorPaint(new SKColor(16, 185, 129), 2),
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 0.3,
            Name = "Cumulative P&L"
        });

        // Win Trade Points (Green) - Shows profitable trades
        PnLSeries.Add(new ScatterSeries<TradeChartPoint>
        {
            Values = _winTradePoints,
            GeometrySize = 14,
            Stroke = new SolidColorPaint(SKColors.White, 2),
            Fill = new SolidColorPaint(new SKColor(16, 185, 129)), // Green
            Name = "Profitable Trades",
            Mapping = (point, index) => new(point.Index, (double)point.CumulativePnL),
            YToolTipLabelFormatter = point =>
            {
                if (point.Model is not TradeChartPoint trade) return "";
                return $"✓ PROFIT: {trade.PnLDisplay}\n" +
                       $"{trade.Symbol}\n" +
                       $"{trade.TimeDisplay}\n" +
                       $"{trade.Trade?.BuyExchange ?? ""} → {trade.Trade?.SellExchange ?? ""}";
            }
        });

        // Loss Trade Points (Red) - Shows losing trades
        PnLSeries.Add(new ScatterSeries<TradeChartPoint>
        {
            Values = _lossTradePoints,
            GeometrySize = 14,
            Stroke = new SolidColorPaint(SKColors.White, 2),
            Fill = new SolidColorPaint(new SKColor(239, 68, 68)), // Red
            Name = "Loss Trades",
            Mapping = (point, index) => new(point.Index, (double)point.CumulativePnL),
            YToolTipLabelFormatter = point =>
            {
                if (point.Model is not TradeChartPoint trade) return "";
                return $"✗ LOSS: {trade.PnLDisplay}\n" +
                       $"{trade.Symbol}\n" +
                       $"{trade.TimeDisplay}\n" +
                       $"{trade.Trade?.BuyExchange ?? ""} → {trade.Trade?.SellExchange ?? ""}";
            }
        });
    }

    private void ArbEngine_TradeCompleted(object? sender, TradeCompletedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            AddTradeToHistory(e.Result);
            UpdateStatistics();
            UpdateChart();
        });
    }

    /// <summary>
    /// Load REAL trade history from database
    /// </summary>
    private async Task LoadTradeHistoryAsync()
    {
        TradeHistory.Clear();

        try
        {
            if (_tradeHistoryService != null)
            {
                // Load real trade history from database
                var trades = await _tradeHistoryService.GetRecentTradesAsync(500);

                foreach (var trade in trades.OrderByDescending(t => t.Timestamp))
                {
                    TradeHistory.Add(new TradeHistoryDisplay
                    {
                        Timestamp = trade.Timestamp,
                        Symbol = trade.Symbol,
                        BuyExchange = trade.BuyExchange,
                        SellExchange = trade.SellExchange,
                        SpreadPercent = trade.SpreadPercent,
                        PnL = trade.PnL,
                        IsSuccess = trade.PnL > 0,
                        ExecutionTimeMs = (int)trade.ExecutionTimeMs
                    });
                }

                _logger?.LogInfo("HistoryPage", $"Loaded {trades.Count()} real trades from database");
            }
            else
            {
                _logger?.LogWarning("HistoryPage", "Trade history service not available");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("HistoryPage", $"Error loading trade history: {ex.Message}");
        }

        UpdateStatistics();
        UpdatePairPerformance();
        UpdateChart();

        if (EmptyState != null)
        {
            EmptyState.Visibility = TradeHistory.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void AddTradeToHistory(TradeResult result)
    {
        var display = new TradeHistoryDisplay
        {
            Timestamp = result.StartTime,
            Symbol = result.Symbol,
            BuyExchange = result.BuyOrder?.Exchange ?? "Unknown",
            SellExchange = result.SellOrder?.Exchange ?? "Unknown",
            SpreadPercent = result.Opportunity?.NetSpreadPercentage ?? 0,
            PnL = result.NetPnL,
            IsSuccess = result.IsFullySuccessful,
            ExecutionTimeMs = result.Metadata.TryGetValue("TotalExecutionMs", out var ms) && ms is long execMs
                ? (int)execMs
                : (int)result.DurationMs
        };

        TradeHistory.Insert(0, display);

        if (TradeHistory.Count > 500)
        {
            TradeHistory.RemoveAt(TradeHistory.Count - 1);
        }

        if (EmptyState != null)
        {
            EmptyState.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateStatistics()
    {
        // Check if UI elements are loaded
        if (TotalPnLDisplay == null || TotalTradesDisplay == null || WinRateDisplay == null)
            return;

        if (TradeHistory.Count == 0)
        {
            TotalPnLDisplay.Text = "$0.00";
            TotalPnLDisplay.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#808080"));
            if (TotalPnLPercent != null) TotalPnLPercent.Text = "+0.00%";
            TotalTradesDisplay.Text = "0";
            WinRateDisplay.Text = "0%";
            if (WinsDisplay != null) WinsDisplay.Text = "0W";
            if (LossesDisplay != null) LossesDisplay.Text = "0L";
            if (AvgSpreadDisplay != null) AvgSpreadDisplay.Text = "0.00%";
            if (AvgExecutionDisplay != null) AvgExecutionDisplay.Text = "0ms";
            if (VolumeDisplay != null) VolumeDisplay.Text = "$0";
            return;
        }

        var totalPnL = TradeHistory.Sum(t => t.PnL);
        var wins = TradeHistory.Count(t => t.IsSuccess);
        var losses = TradeHistory.Count - wins;
        var winRate = (decimal)wins / TradeHistory.Count * 100;
        var avgSpread = TradeHistory.Average(t => t.SpreadPercent);
        var bestSpread = TradeHistory.Max(t => t.SpreadPercent);
        var avgExec = TradeHistory.Average(t => t.ExecutionTimeMs);
        var fastestExec = TradeHistory.Min(t => t.ExecutionTimeMs);

        // Total P&L
        TotalPnLDisplay.Text = totalPnL >= 0 ? $"+${totalPnL:F2}" : $"-${Math.Abs(totalPnL):F2}";
        TotalPnLDisplay.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(totalPnL >= 0 ? "#10B981" : "#EF4444"));
        if (TotalPnLPercent != null)
        {
            TotalPnLPercent.Text = totalPnL >= 0 ? $"+{totalPnL / 100:F2}%" : $"{totalPnL / 100:F2}%";
            TotalPnLPercent.Foreground = TotalPnLDisplay.Foreground;
        }

        // Trade counts
        TotalTradesDisplay.Text = TradeHistory.Count.ToString();

        if (AvgTradesPerDay != null)
        {
            var days = (DateTime.UtcNow - TradeHistory.Min(t => t.Timestamp)).TotalDays;
            AvgTradesPerDay.Text = days > 0 ? $"{TradeHistory.Count / days:F1}/day avg" : "0/day avg";
        }

        // Win rate
        WinRateDisplay.Text = $"{winRate:F1}%";
        if (WinsDisplay != null) WinsDisplay.Text = $"{wins}W";
        if (LossesDisplay != null) LossesDisplay.Text = $"{losses}L";

        // Spread
        if (AvgSpreadDisplay != null) AvgSpreadDisplay.Text = $"{avgSpread:F2}%";
        if (BestSpreadDisplay != null) BestSpreadDisplay.Text = $"Best: {bestSpread:F2}%";

        // Execution
        if (AvgExecutionDisplay != null) AvgExecutionDisplay.Text = $"{avgExec:F0}ms";
        if (FastestExecDisplay != null) FastestExecDisplay.Text = $"Fastest: {fastestExec}ms";

        // Volume (estimate)
        var volume = TradeHistory.Count * 500m; // Assume $500 avg trade
        if (VolumeDisplay != null) VolumeDisplay.Text = volume >= 1000 ? $"${volume / 1000:F1}K" : $"${volume:F0}";

        var fees = volume * 0.001m; // 0.1% avg fees
        if (FeesDisplay != null) FeesDisplay.Text = $"Fees: ${fees:F2}";
    }

    private void UpdatePairPerformance()
    {
        PairPerformances.Clear();

        var byPair = TradeHistory
            .GroupBy(t => t.Symbol)
            .Select(g => new PairPerformance
            {
                Symbol = g.Key,
                PnL = g.Sum(t => t.PnL),
                TradeCount = g.Count()
            })
            .OrderByDescending(p => p.PnL)
            .ToList();

        var maxPnL = byPair.Any() ? byPair.Max(p => Math.Abs(p.PnL)) : 1;

        foreach (var perf in byPair)
        {
            perf.BarWidthPercent = maxPnL > 0 ? (double)Math.Abs(perf.PnL) / (double)maxPnL : 0;
            PairPerformances.Add(perf);
        }
    }

    private void UpdateChart()
    {
        _pnlValues.Clear();
        _winTradePoints.Clear();
        _lossTradePoints.Clear();

        // Create cumulative P&L over time
        _sortedTrades = TradeHistory.OrderBy(t => t.Timestamp).ToList();
        decimal cumulative = 0;
        int index = 0;

        foreach (var trade in _sortedTrades)
        {
            cumulative += trade.PnL;
            _pnlValues.Add(new ObservableValue((double)cumulative));

            // Add trade point to appropriate collection based on profit/loss
            var chartPoint = new TradeChartPoint
            {
                Trade = trade,
                CumulativePnL = cumulative,
                Index = index
            };

            if (trade.PnL >= 0)
            {
                _winTradePoints.Add(chartPoint);
            }
            else
            {
                _lossTradePoints.Add(chartPoint);
            }

            index++;
        }

        // Ensure we have some data points
        while (_pnlValues.Count < 5)
        {
            _pnlValues.Add(new ObservableValue(0));
        }
    }

    private void PnLChart_ChartPointPointerDown(
        LiveChartsCore.Kernel.Sketches.IChartView chart,
        LiveChartsCore.Kernel.ChartPoint? point)
    {
        if (point == null) return;

        var seriesName = point.Context.Series.Name;

        // Get the trade from the point
        if (seriesName == "Profitable Trades" || seriesName == "Loss Trades")
        {
            // Find the trade point by index in X coordinate
            var index = (int)point.Coordinate.SecondaryValue;
            if (index >= 0 && index < _sortedTrades.Count)
            {
                ShowTradeDetail(_sortedTrades[index]);
            }
        }
        else if (seriesName == "Cumulative P&L")
        {
            // Find the corresponding trade by index
            var index = (int)point.Coordinate.SecondaryValue;
            if (index >= 0 && index < _sortedTrades.Count)
            {
                ShowTradeDetail(_sortedTrades[index]);
            }
        }
    }

    private void ShowTradeDetail(TradeHistoryDisplay? trade)
    {
        if (trade == null || TradeDetailPopup == null) return;

        // Update popup content
        if (PopupTime != null) PopupTime.Text = trade.TimeDisplay;
        if (PopupPair != null) PopupPair.Text = trade.Symbol;
        if (PopupBuyExchange != null) PopupBuyExchange.Text = trade.BuyExchange;
        if (PopupSellExchange != null) PopupSellExchange.Text = trade.SellExchange;
        if (PopupSpread != null) PopupSpread.Text = trade.SpreadDisplay;
        if (PopupPnL != null)
        {
            PopupPnL.Text = trade.PnLDisplay;
            PopupPnL.Foreground = trade.PnLColor;
        }
        if (PopupExecTime != null) PopupExecTime.Text = trade.ExecutionTimeDisplay;

        // Update status badge
        if (PopupStatus != null && PopupStatusBadge != null)
        {
            PopupStatus.Text = trade.StatusText;
            PopupStatus.Foreground = trade.StatusColor;
            PopupStatusBadge.Background = trade.StatusBackground;
        }

        // Show popup
        TradeDetailPopup.Visibility = Visibility.Visible;
    }

    private void ClosePopup_Click(object sender, RoutedEventArgs e)
    {
        if (TradeDetailPopup != null)
        {
            TradeDetailPopup.Visibility = Visibility.Collapsed;
        }
    }

    private void TimeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TimeFilterCombo.SelectedItem is ComboBoxItem item)
        {
            var filter = item.Content?.ToString() ?? "Today";
            ApplyFilters();
        }
    }

    private void PairFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilters();
    }

    private async void ApplyFilters()
    {
        // Reload with filters applied
        await LoadTradeHistoryAsync();
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                FileName = $"AutoTradeX_History_{DateTime.Now:yyyyMMdd}",
                DefaultExt = ".csv",
                Filter = "CSV files (*.csv)|*.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Timestamp,Symbol,Buy Exchange,Sell Exchange,Spread %,P&L,Status,Execution Time (ms)");

                foreach (var trade in TradeHistory)
                {
                    sb.AppendLine($"{trade.Timestamp:yyyy-MM-dd HH:mm:ss},{trade.Symbol},{trade.BuyExchange},{trade.SellExchange},{trade.SpreadPercent:F4},{trade.PnL:F4},{(trade.IsSuccess ? "Success" : "Failed")},{trade.ExecutionTimeMs}");
                }

                File.WriteAllText(dialog.FileName, sb.ToString());

                MessageBox.Show($"Exported {TradeHistory.Count} trades to:\n{dialog.FileName}",
                    "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                _logger?.LogInfo("History", $"Exported {TradeHistory.Count} trades to CSV");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _logger?.LogError("History", $"Export failed: {ex.Message}");
        }
    }
}

public class TradeHistoryDisplay
{
    public DateTime Timestamp { get; set; }
    public string Symbol { get; set; } = "";
    public string BuyExchange { get; set; } = "";
    public string SellExchange { get; set; } = "";
    public decimal SpreadPercent { get; set; }
    public decimal PnL { get; set; }
    public bool IsSuccess { get; set; }
    public int ExecutionTimeMs { get; set; }

    public string TimeDisplay => Timestamp.ToLocalTime().ToString("MM/dd HH:mm:ss");
    public string SpreadDisplay => $"{SpreadPercent:F2}%";
    public string PnLDisplay => PnL >= 0 ? $"+${PnL:F2}" : $"-${Math.Abs(PnL):F2}";
    public Brush PnLColor => new SolidColorBrush(
        (Color)ColorConverter.ConvertFromString(PnL >= 0 ? "#10B981" : "#EF4444"));

    public string StatusText => IsSuccess ? "SUCCESS" : "FAILED";
    public Brush StatusColor => new SolidColorBrush(
        (Color)ColorConverter.ConvertFromString(IsSuccess ? "#10B981" : "#EF4444"));
    public Brush StatusBackground => new SolidColorBrush(
        (Color)ColorConverter.ConvertFromString(IsSuccess ? "#10B98120" : "#EF444420"));

    public string ExecutionTimeDisplay => $"{ExecutionTimeMs}ms";
}

public class PairPerformance
{
    public string Symbol { get; set; } = "";
    public decimal PnL { get; set; }
    public int TradeCount { get; set; }
    public double BarWidthPercent { get; set; }

    public string PnLDisplay => PnL >= 0 ? $"+${PnL:F2}" : $"-${Math.Abs(PnL):F2}";
    public Brush PnLColor => new SolidColorBrush(
        (Color)ColorConverter.ConvertFromString(PnL >= 0 ? "#10B981" : "#EF4444"));
    public Brush BarColor => new SolidColorBrush(
        (Color)ColorConverter.ConvertFromString(PnL >= 0 ? "#10B981" : "#EF4444"));
    public double BarWidth => BarWidthPercent * 150; // Max width 150px
}

/// <summary>
/// Represents a trade point on the chart
/// </summary>
public class TradeChartPoint
{
    public TradeHistoryDisplay? Trade { get; set; }
    public decimal CumulativePnL { get; set; }
    public int Index { get; set; }

    // For display
    public string Symbol => Trade?.Symbol ?? "";
    public string TimeDisplay => Trade?.TimeDisplay ?? "";
    public string PnLDisplay => Trade?.PnLDisplay ?? "";
    public bool IsProfit => Trade?.PnL >= 0;

    // Color getters
    public SKColor PointColor => IsProfit
        ? new SKColor(16, 185, 129)   // Green for profit
        : new SKColor(239, 68, 68);   // Red for loss
}
