using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using LiveChartsCore.Defaults;
using Microsoft.Win32;
using SkiaSharp;
using Microsoft.Extensions.DependencyInjection;
using AutoTradeX.Infrastructure.Services;

namespace AutoTradeX.UI.Views;

public partial class AnalyticsPage : UserControl
{
    private ITradeHistoryService? _tradeHistoryService;
    private List<TradeHistoryEntry> _currentTrades = new();
    private Border? _selectedTimeButton;
    private string _currentTimeRange = "1D";
    private string? _selectedSymbol = null;
    private bool _isLoading = false;

    public AnalyticsPage()
    {
        InitializeComponent();
        Loaded += AnalyticsPage_Loaded;
    }

    private async void AnalyticsPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Get service from App
        _tradeHistoryService = App.Services.GetService<ITradeHistoryService>();

        _selectedTimeButton = Time1D;

        // Set default date range (1 day)
        FromDatePicker.SelectedDate = DateTime.Now.AddDays(-1);
        ToDatePicker.SelectedDate = DateTime.Now;

        await LoadSymbolsAsync();
        await LoadDataAsync();
        await UpdateDatabaseSizeAsync();
    }

    private async Task LoadSymbolsAsync()
    {
        if (_tradeHistoryService == null) return;

        try
        {
            var symbols = await _tradeHistoryService.GetDistinctSymbolsAsync();

            PairComboBox.Items.Clear();
            PairComboBox.Items.Add(new ComboBoxItem { Content = "All Pairs", IsSelected = true });

            foreach (var symbol in symbols)
            {
                PairComboBox.Items.Add(new ComboBoxItem { Content = symbol });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading symbols: {ex.Message}");
        }
    }

    private async Task LoadDataAsync()
    {
        if (_tradeHistoryService == null || _isLoading) return;

        _isLoading = true;
        LoadingOverlay.Visibility = Visibility.Visible;

        try
        {
            var filter = new TradeHistoryFilter
            {
                FromDate = FromDatePicker.SelectedDate,
                ToDate = ToDatePicker.SelectedDate?.AddDays(1), // Include full day
                Symbol = _selectedSymbol
            };

            _currentTrades = await _tradeHistoryService.GetTradesAsync(filter);

            // Update UI
            UpdateCharts();
            UpdateTradeList();
            UpdateStats();

            // Update header
            CurrentPairText.Text = _selectedSymbol ?? "All Pairs";
            FilteredTradeCount.Text = _currentTrades.Count.ToString();

            // Show/hide no data message
            NoDataOverlay.Visibility = _currentTrades.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (_currentTrades.Count > 0)
            {
                var exchanges = _currentTrades
                    .SelectMany(t => new[] { t.BuyExchange, t.SellExchange })
                    .Distinct()
                    .Take(3);
                CurrentExchangeText.Text = string.Join(", ", exchanges);
                ExchangeBadge.Visibility = Visibility.Visible;
            }
            else
            {
                ExchangeBadge.Visibility = Visibility.Collapsed;
            }

            SubtitleText.Text = $"Showing data from {FromDatePicker.SelectedDate:MMM dd} to {ToDatePicker.SelectedDate:MMM dd, yyyy}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading data: {ex.Message}");
            MessageBox.Show($"Error loading trade data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isLoading = false;
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateCharts()
    {
        if (_currentTrades.Count == 0)
        {
            PriceChart.Series = Array.Empty<ISeries>();
            PnLChart.Series = Array.Empty<ISeries>();
            return;
        }

        SetupPriceChart();
        SetupPnLChart();
    }

    private void SetupPriceChart()
    {
        var orderedTrades = _currentTrades.OrderBy(t => t.Timestamp).ToList();
        var buyPoints = new List<ObservablePoint>();
        var sellPoints = new List<ObservablePoint>();

        var minTime = orderedTrades.First().Timestamp;
        var maxTime = orderedTrades.Last().Timestamp;
        var timeRange = (maxTime - minTime).TotalHours;
        if (timeRange < 1) timeRange = 24; // Minimum 24 hours range

        foreach (var trade in orderedTrades)
        {
            var x = (trade.Timestamp - minTime).TotalHours;
            var avgPrice = (double)((trade.BuyPrice + trade.SellPrice) / 2);

            buyPoints.Add(new ObservablePoint(x, (double)trade.BuyPrice));
            sellPoints.Add(new ObservablePoint(x, (double)trade.SellPrice));
        }

        var series = new ISeries[]
        {
            new ScatterSeries<ObservablePoint>
            {
                Values = buyPoints,
                Name = "Buy Price",
                Stroke = new SolidColorPaint(new SKColor(16, 185, 129)) { StrokeThickness = 2 },
                Fill = new SolidColorPaint(new SKColor(16, 185, 129)),
                GeometrySize = 10
            },
            new ScatterSeries<ObservablePoint>
            {
                Values = sellPoints,
                Name = "Sell Price",
                Stroke = new SolidColorPaint(new SKColor(239, 68, 68)) { StrokeThickness = 2 },
                Fill = new SolidColorPaint(new SKColor(239, 68, 68)),
                GeometrySize = 10
            }
        };

        PriceChart.Series = series;

        var capturedMinTime = minTime;
        var capturedTimeRange = timeRange;

        PriceChart.XAxes = new Axis[]
        {
            new Axis
            {
                Labeler = value =>
                {
                    try
                    {
                        if (value >= 0 && value <= capturedTimeRange * 1.1)
                            return capturedMinTime.AddHours(value).ToString("MM/dd HH:mm");
                        return "";
                    }
                    catch { return ""; }
                },
                LabelsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 100)),
                SeparatorsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 20))
                {
                    StrokeThickness = 1,
                    PathEffect = new DashEffect(new float[] { 3, 3 })
                },
                TextSize = 10
            }
        };

        PriceChart.YAxes = new Axis[]
        {
            new Axis
            {
                Labeler = value => $"${value:N2}",
                LabelsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 100)),
                SeparatorsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 20))
                {
                    StrokeThickness = 1,
                    PathEffect = new DashEffect(new float[] { 3, 3 })
                },
                TextSize = 10,
                Position = LiveChartsCore.Measure.AxisPosition.End
            }
        };
    }

    private void SetupPnLChart()
    {
        var orderedTrades = _currentTrades.OrderBy(t => t.Timestamp).ToList();
        var pnlPoints = new List<ObservablePoint>();

        decimal cumulativePnL = 0;
        var minTime = orderedTrades.First().Timestamp;

        foreach (var trade in orderedTrades)
        {
            cumulativePnL += trade.PnL;
            var x = (trade.Timestamp - minTime).TotalHours;
            pnlPoints.Add(new ObservablePoint(x, (double)cumulativePnL));
        }

        var finalPnL = cumulativePnL;
        var lineColor = finalPnL >= 0 ? new SKColor(16, 185, 129) : new SKColor(239, 68, 68);
        var fillColor = finalPnL >= 0 ? new SKColor(16, 185, 129, 40) : new SKColor(239, 68, 68, 40);

        var series = new ISeries[]
        {
            new LineSeries<ObservablePoint>
            {
                Values = pnlPoints,
                Name = "Cumulative P&L",
                Stroke = new SolidColorPaint(lineColor) { StrokeThickness = 2 },
                Fill = new SolidColorPaint(fillColor),
                GeometrySize = 0,
                LineSmoothness = 0.3
            }
        };

        PnLChart.Series = series;

        var timeRange = orderedTrades.Count > 1
            ? (orderedTrades.Last().Timestamp - minTime).TotalHours
            : 24;
        var capturedMinTime = minTime;

        PnLChart.XAxes = new Axis[]
        {
            new Axis
            {
                Labeler = value =>
                {
                    try
                    {
                        if (value >= 0 && value <= timeRange * 1.1)
                            return capturedMinTime.AddHours(value).ToString("MM/dd");
                        return "";
                    }
                    catch { return ""; }
                },
                LabelsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 80)),
                SeparatorsPaint = new SolidColorPaint(SKColors.Transparent),
                TextSize = 9
            }
        };

        PnLChart.YAxes = new Axis[]
        {
            new Axis
            {
                Labeler = value => $"${value:N2}",
                LabelsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 80)),
                SeparatorsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 15)),
                TextSize = 9
            }
        };
    }

    private void UpdateTradeList()
    {
        var displayItems = _currentTrades
            .OrderByDescending(t => t.Timestamp)
            .Take(50)
            .Select(t => new TradeDisplayItem(t))
            .ToList();

        TradeMarkersList.ItemsSource = displayItems;
        TradeCountText.Text = $"({_currentTrades.Count} trades)";
    }

    private void UpdateStats()
    {
        if (_currentTrades.Count == 0)
        {
            TotalTradesText.Text = "0";
            WinRateText.Text = "0%";
            AvgProfitText.Text = "$0.00";
            BestTradeText.Text = "$0.00";
            WorstTradeText.Text = "$0.00";
            TotalPnLText.Text = "$0.00";
            return;
        }

        var profitable = _currentTrades.Where(t => t.PnL > 0).ToList();
        var winRate = (double)profitable.Count / _currentTrades.Count * 100;
        var totalPnL = _currentTrades.Sum(t => t.PnL);
        var avgPnL = _currentTrades.Average(t => t.PnL);
        var bestTrade = _currentTrades.Max(t => t.PnL);
        var worstTrade = _currentTrades.Min(t => t.PnL);

        TotalTradesText.Text = _currentTrades.Count.ToString();
        WinRateText.Text = $"{winRate:F1}%";
        WinRateText.Foreground = GetPnLBrush(winRate >= 50 ? 1 : -1);

        AvgProfitText.Text = FormatPnL(avgPnL);
        AvgProfitText.Foreground = GetPnLBrush(avgPnL);

        BestTradeText.Text = FormatPnL(bestTrade);
        BestTradeText.Foreground = GetPnLBrush(bestTrade);

        WorstTradeText.Text = FormatPnL(worstTrade);
        WorstTradeText.Foreground = GetPnLBrush(worstTrade);

        TotalPnLText.Text = FormatPnL(totalPnL);
        TotalPnLText.Foreground = GetPnLBrush(totalPnL);
    }

    private async Task UpdateDatabaseSizeAsync()
    {
        if (_tradeHistoryService == null) return;

        try
        {
            var sizeBytes = await _tradeHistoryService.GetDatabaseSizeAsync();
            var sizeKB = sizeBytes / 1024.0;
            var sizeMB = sizeKB / 1024.0;

            DbSizeText.Text = sizeMB >= 1 ? $"{sizeMB:F1} MB" : $"{sizeKB:F0} KB";
        }
        catch { }
    }

    private static string FormatPnL(decimal pnl)
    {
        return pnl >= 0 ? $"+${pnl:F2}" : $"-${Math.Abs(pnl):F2}";
    }

    private static Brush GetPnLBrush(decimal pnl)
    {
        return pnl >= 0
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
    }

    private async void TimeRange_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string range)
        {
            // Reset previous selection
            if (_selectedTimeButton != null)
            {
                _selectedTimeButton.Background = Brushes.Transparent;
                if (_selectedTimeButton.Child is TextBlock prevText)
                    prevText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80FFFFFF"));
            }

            // Set new selection
            _selectedTimeButton = border;
            border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED"));
            if (border.Child is TextBlock text)
                text.Foreground = Brushes.White;

            _currentTimeRange = range;

            // Update date pickers
            var now = DateTime.Now;
            ToDatePicker.SelectedDate = now;

            FromDatePicker.SelectedDate = range switch
            {
                "1D" => now.AddDays(-1),
                "7D" => now.AddDays(-7),
                "30D" => now.AddDays(-30),
                "90D" => now.AddDays(-90),
                "ALL" => now.AddYears(-10),
                _ => now.AddDays(-1)
            };

            await LoadDataAsync();
        }
    }

    private async void PairComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PairComboBox.SelectedItem is ComboBoxItem item)
        {
            var content = item.Content?.ToString();
            _selectedSymbol = content == "All Pairs" ? null : content;
            await LoadDataAsync();
        }
    }

    private async void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FromDatePicker.SelectedDate.HasValue && ToDatePicker.SelectedDate.HasValue)
        {
            // Reset quick time selection
            if (_selectedTimeButton != null)
            {
                _selectedTimeButton.Background = Brushes.Transparent;
                if (_selectedTimeButton.Child is TextBlock prevText)
                    prevText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80FFFFFF"));
                _selectedTimeButton = null;
            }

            await LoadDataAsync();
        }
    }

    private async void Refresh_Click(object sender, MouseButtonEventArgs e)
    {
        await LoadSymbolsAsync();
        await LoadDataAsync();
        await UpdateDatabaseSizeAsync();
    }

    private async void CleanupData_Click(object sender, MouseButtonEventArgs e)
    {
        if (_tradeHistoryService == null) return;

        var result = MessageBox.Show(
            "This will delete trade data older than 90 days.\n\nA backup will be created before deletion.\n\nContinue?",
            "Cleanup Old Data",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            LoadingOverlay.Visibility = Visibility.Visible;

            var deleted = await _tradeHistoryService.CleanupOldDataAsync(90);

            await LoadDataAsync();
            await UpdateDatabaseSizeAsync();

            MessageBox.Show(
                $"Cleanup completed!\n\nDeleted {deleted} old trade records.\nBackup was saved automatically.",
                "Cleanup Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error during cleanup: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void TradeMarker_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.DataContext is TradeDisplayItem item)
        {
            ShowTradeDetail(item.Trade);
        }
    }

    private void ShowTradeDetail(TradeHistoryEntry trade)
    {
        var isProfit = trade.PnL >= 0;

        PopupPnLBadge.Background = isProfit
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2010B981"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20EF4444"));

        PopupPnLIcon.Text = isProfit ? "📈" : "📉";
        PopupPnLText.Text = FormatPnL(trade.PnL);
        PopupPnLText.Foreground = GetPnLBrush(trade.PnL);

        PopupSymbol.Text = trade.Symbol;
        PopupSpread.Text = $"{trade.SpreadPercent:F4}%";
        PopupBuyExchange.Text = trade.BuyExchange;
        PopupSellExchange.Text = trade.SellExchange;
        PopupBuyPrice.Text = $"${trade.BuyPrice:N4}";
        PopupSellPrice.Text = $"${trade.SellPrice:N4}";
        PopupAmount.Text = $"{trade.TradeAmount:F6}";
        PopupFee.Text = $"${trade.Fee:F4}";
        PopupTime.Text = trade.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        PopupExecution.Text = $"{trade.ExecutionTimeMs}ms";

        TradeDetailPopup.Visibility = Visibility.Visible;
    }

    private void ClosePopup_Click(object sender, MouseButtonEventArgs e)
    {
        TradeDetailPopup.Visibility = Visibility.Collapsed;
    }

    private void ExportCSV_Click(object sender, MouseButtonEventArgs e)
    {
        if (_currentTrades.Count == 0)
        {
            MessageBox.Show("No trades to export.", "Export CSV", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"AutoTradeX_Trades_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            Title = "Export Trades to CSV"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                ExportTradesToCSV(dialog.FileName);
                MessageBox.Show($"Successfully exported {_currentTrades.Count} trades to:\n{dialog.FileName}",
                    "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ExportTradesToCSV(string filePath)
    {
        var csv = new StringBuilder();

        // Header row
        csv.AppendLine("Timestamp,Symbol,Buy Exchange,Sell Exchange,Buy Price,Sell Price,Amount,Spread %,P&L,Fee,Execution Time (ms),Status");

        // Data rows
        foreach (var trade in _currentTrades.OrderByDescending(t => t.Timestamp))
        {
            csv.AppendLine(string.Join(",",
                EscapeCsvField(trade.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")),
                EscapeCsvField(trade.Symbol),
                EscapeCsvField(trade.BuyExchange),
                EscapeCsvField(trade.SellExchange),
                trade.BuyPrice.ToString("F8"),
                trade.SellPrice.ToString("F8"),
                trade.TradeAmount.ToString("F8"),
                trade.SpreadPercent.ToString("F4"),
                trade.PnL.ToString("F4"),
                trade.Fee.ToString("F4"),
                trade.ExecutionTimeMs.ToString(),
                EscapeCsvField(trade.Status)
            ));
        }

        // Add summary section
        csv.AppendLine();
        csv.AppendLine("--- Summary ---");
        csv.AppendLine($"Total Trades,{_currentTrades.Count}");
        csv.AppendLine($"Total P&L,{_currentTrades.Sum(t => t.PnL):F4}");
        csv.AppendLine($"Total Fees,{_currentTrades.Sum(t => t.Fee):F4}");
        csv.AppendLine($"Winning Trades,{_currentTrades.Count(t => t.PnL > 0)}");
        csv.AppendLine($"Losing Trades,{_currentTrades.Count(t => t.PnL <= 0)}");
        if (_currentTrades.Count > 0)
        {
            csv.AppendLine($"Win Rate,{(decimal)_currentTrades.Count(t => t.PnL > 0) / _currentTrades.Count * 100:F2}%");
            csv.AppendLine($"Average P&L,{_currentTrades.Average(t => (double)t.PnL):F4}");
            csv.AppendLine($"Best Trade,{_currentTrades.Max(t => t.PnL):F4}");
            csv.AppendLine($"Worst Trade,{_currentTrades.Min(t => t.PnL):F4}");
        }
        csv.AppendLine($"Export Date,{DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
    }

    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field)) return "";

        // Escape quotes by doubling them, and wrap in quotes if contains comma, quote, or newline
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
}

public class TradeDisplayItem
{
    public TradeHistoryEntry Trade { get; }

    public TradeDisplayItem(TradeHistoryEntry trade)
    {
        Trade = trade;
    }

    public string Symbol => Trade.Symbol;
    public string ExchangeRoute => $"{Trade.BuyExchange} → {Trade.SellExchange}";
    public string TimeDisplay => Trade.Timestamp.ToString("MM/dd HH:mm");
    public string PnLDisplay => Trade.PnL >= 0 ? $"+${Trade.PnL:F2}" : $"-${Math.Abs(Trade.PnL):F2}";
    public string SpreadDisplay => $"{Trade.SpreadPercent:F3}%";
    public string PnLIcon => Trade.PnL >= 0 ? "📈" : "📉";

    public Brush PnLColor => Trade.PnL >= 0
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"))
        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));

    public Brush PnLBackground => Trade.PnL >= 0
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2010B981"))
        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20EF4444"));
}
