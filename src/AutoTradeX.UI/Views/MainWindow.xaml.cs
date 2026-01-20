using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Microsoft.Extensions.DependencyInjection;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using AutoTradeX.Infrastructure.Services;

namespace AutoTradeX.UI.Views;

public partial class MainWindow : Window
{
    private Border? _activeTab;
    private readonly ICoinDataService? _coinDataService;
    private readonly ILoggingService? _logger;
    private readonly IArbEngine? _arbEngine;
    private readonly IConfigService? _configService;
    private readonly IBalancePoolService? _balancePool;
    private readonly ITradeHistoryService? _tradeHistory;
    private readonly INotificationService? _notificationService;
    private System.Windows.Threading.DispatcherTimer? _priceUpdateTimer;
    private System.Windows.Threading.DispatcherTimer? _aiScannerTimer;
    private System.Windows.Threading.DispatcherTimer? _statusBarTimer;
    private bool _isBotRunning = false;
    private bool _isBotPaused = false;
    private CancellationTokenSource? _botCancellationTokenSource;

    // Stats tracking
    private int _todayTradeCount = 0;
    private int _successfulTrades = 0;
    private decimal _todayPnL = 0;
    private int _scanCount = 0;

    // Spread history for chart
    private readonly List<decimal> _spreadHistory = new();
    private const int MaxSpreadHistory = 20;

    // AI Scanner opportunities
    private readonly List<ArbitrageOpportunityInfo> _aiOpportunities = new();

    // Last prices for flash animation
    private decimal _lastBinancePrice = 0;
    private decimal _lastKuCoinPrice = 0;

    public MainWindow()
    {
        InitializeComponent();

        _coinDataService = App.Services?.GetService<ICoinDataService>();
        _logger = App.Services?.GetService<ILoggingService>();
        _arbEngine = App.Services?.GetService<IArbEngine>();
        _configService = App.Services?.GetService<IConfigService>();
        _balancePool = App.Services?.GetService<IBalancePoolService>();
        _tradeHistory = App.Services?.GetService<ITradeHistoryService>();
        _notificationService = App.Services?.GetService<INotificationService>();

        // Subscribe to ArbEngine events
        if (_arbEngine != null)
        {
            _arbEngine.StatusChanged += ArbEngine_StatusChanged;
            _arbEngine.TradeCompleted += ArbEngine_TradeCompleted;
            _arbEngine.OpportunityFound += ArbEngine_OpportunityFound;
            _arbEngine.PriceUpdated += ArbEngine_PriceUpdated;
            _arbEngine.ErrorOccurred += ArbEngine_ErrorOccurred;
        }

        // Subscribe to BalancePool events
        if (_balancePool != null)
        {
            _balancePool.BalanceUpdated += BalancePool_BalanceUpdated;
            _balancePool.EmergencyTriggered += BalancePool_EmergencyTriggered;
        }

        // Subscribe to Notification events
        if (_notificationService != null)
        {
            _notificationService.NotificationReceived += NotificationService_NotificationReceived;
        }

        // Set Dashboard as default active tab
        Loaded += MainWindow_Loaded;
    }

    private void NotificationService_NotificationReceived(object? sender, NotificationEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // Could show toast notification or update notification badge
            _logger?.LogInfo("Notification", $"New notification: {e.Notification.Title}");
        });
    }

    #region ArbEngine Event Handlers

    private void ArbEngine_StatusChanged(object? sender, EngineStatusEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _isBotRunning = e.Status == EngineStatus.Running || e.Status == EngineStatus.Paused;
            _isBotPaused = e.Status == EngineStatus.Paused;
            UpdateBotStatusUI(e.Status, e.Message);
        });
    }

    private void ArbEngine_TradeCompleted(object? sender, TradeCompletedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _logger?.LogInfo("Trade", $"Trade completed: {e.Result.Symbol} - PnL: ${e.Result.NetPnL:F2}");

            // Update trade stats
            _todayTradeCount++;
            if (e.Result.NetPnL > 0)
                _successfulTrades++;
            _todayPnL += e.Result.NetPnL;

            // Update UI stats
            UpdateTradeStatsUI(e.Result);

            // Record in balance pool
            _balancePool?.RecordTrade(e.Result);

            // Save to trade history
            if (_tradeHistory != null)
            {
                var historyEntry = new TradeHistoryEntry
                {
                    Id = e.Result.TradeId,
                    Timestamp = e.Result.StartTime,
                    Symbol = e.Result.Symbol,
                    BuyExchange = e.Result.BuyOrder?.Exchange ?? "Unknown",
                    SellExchange = e.Result.SellOrder?.Exchange ?? "Unknown",
                    BuyPrice = e.Result.BuyOrder?.AverageFilledPrice ?? 0,
                    SellPrice = e.Result.SellOrder?.AverageFilledPrice ?? 0,
                    TradeAmount = e.Result.ActualBuyValue,
                    SpreadPercent = e.Result.Opportunity?.BestSpreadPercentage ?? 0,
                    PnL = e.Result.NetPnL,
                    Fee = e.Result.TotalFees,
                    ExecutionTimeMs = e.Result.DurationMs,
                    Status = e.Result.Status.ToString()
                };
                _ = _tradeHistory.SaveTradeAsync(historyEntry);
            }

            // Send notification
            var buyExchange = e.Result.BuyOrder?.Exchange ?? "Unknown";
            var sellExchange = e.Result.SellOrder?.Exchange ?? "Unknown";
            _notificationService?.NotifyTrade(e.Result.Symbol, e.Result.NetPnL,
                $"{buyExchange} → {sellExchange}");

            // Update recent trades display
            UpdateRecentTradesDisplay();

            // Show trade completion animation
            ShowTradeCompletedAnimation(e.Result.NetPnL >= 0);
        });
    }

    private void BalancePool_BalanceUpdated(object? sender, BalanceUpdateEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // Update Real P&L display with animation
            if (RealPnLDisplay != null)
            {
                var pnlText = e.PnL.TotalPnLUSDT >= 0
                    ? $"+${e.PnL.TotalPnLUSDT:F2}"
                    : $"-${Math.Abs(e.PnL.TotalPnLUSDT):F2}";
                RealPnLDisplay.Text = pnlText;
                var pnlColor = e.PnL.TotalPnLUSDT >= 0 ? "#10B981" : "#EF4444";
                RealPnLDisplay.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pnlColor));
            }

            // Update total balance
            if (TotalBalanceDisplay != null)
                TotalBalanceDisplay.Text = $"Total: ${e.Snapshot.TotalValueUSDT:F2}";

            // Update exchange balance displays
            if (e.Snapshot.CombinedBalances != null)
            {
                if (e.Snapshot.CombinedBalances.TryGetValue("USDT", out var usdtBalance))
                {
                    if (ExchangeABalance != null)
                        ExchangeABalance.Text = $"{usdtBalance.ExchangeA_Total:N2}";
                    if (ExchangeBBalance != null)
                        ExchangeBBalance.Text = $"{usdtBalance.ExchangeB_Total:N2}";
                }
            }

            // Update drawdown with color coding
            if (DrawdownDisplay != null && _balancePool != null)
            {
                var drawdown = _balancePool.CurrentDrawdown;
                DrawdownDisplay.Text = $"-{drawdown:F2}%";
                var drawdownColor = drawdown > 3 ? "#EF4444" : drawdown > 1 ? "#F59E0B" : "#10B981";
                DrawdownDisplay.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(drawdownColor));

                // Update emergency dot color
                if (EmergencyDot != null)
                    EmergencyDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(drawdownColor));
            }
        });
    }

    private void BalancePool_EmergencyTriggered(object? sender, EmergencyEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var check = e.Check;

            // Update emergency status
            var statusText = check.Reason switch
            {
                EmergencyTriggerReason.MaxDrawdownExceeded => "CRITICAL",
                EmergencyTriggerReason.MaxLossExceeded => "CRITICAL",
                EmergencyTriggerReason.ConsecutiveLosses => "Warning",
                EmergencyTriggerReason.RapidLossRate => "Warning",
                EmergencyTriggerReason.CriticalImbalance => "Warning",
                _ => "Normal"
            };

            if (EmergencyStatusDisplay != null)
            {
                EmergencyStatusDisplay.Text = statusText;
                var statusColor = statusText switch
                {
                    "CRITICAL" => "#EF4444",
                    "Warning" => "#F59E0B",
                    _ => "#10B981"
                };
                EmergencyStatusDisplay.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(statusColor));
            }

            if (EmergencyDot != null)
            {
                var dotColor = statusText switch
                {
                    "CRITICAL" => "#EF4444",
                    "Warning" => "#F59E0B",
                    _ => "#10B981"
                };
                EmergencyDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dotColor));
            }

            // Auto-pause on critical
            if (check.RecommendedAction == EmergencyAction.StopTrading ||
                check.RecommendedAction == EmergencyAction.PauseTrading)
            {
                _arbEngine?.Pause();
                _logger?.LogCritical("Emergency", $"Trading paused: {check.Message}");
                MessageBox.Show($"Trading paused due to:\n{check.Message}", "Emergency Protection",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    private void UpdateTradeStatsUI(TradeResult result)
    {
        // Update today P&L
        if (TodayPnLDisplay != null)
        {
            TodayPnLDisplay.Text = _todayPnL >= 0 ? $"+${_todayPnL:F2}" : $"-${Math.Abs(_todayPnL):F2}";
            TodayPnLDisplay.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(_todayPnL >= 0 ? "#10B981" : "#EF4444"));
        }

        // Update trade count
        if (TradeCountDisplay != null)
            TradeCountDisplay.Text = $"{_todayTradeCount} trades";

        // Update win rate
        if (WinRateDisplay != null && _todayTradeCount > 0)
        {
            var winRate = (decimal)_successfulTrades / _todayTradeCount * 100;
            WinRateDisplay.Text = $"{winRate:F1}%";
        }

        // Update win/loss display
        if (WinLossDisplay != null)
            WinLossDisplay.Text = $"{_successfulTrades}W / {_todayTradeCount - _successfulTrades}L";

        // Update execution speed
        if (ExecutionSpeedDisplay != null && result.Metadata.TryGetValue("TotalExecutionMs", out var execMs))
        {
            ExecutionSpeedDisplay.Text = $"{execMs}ms";
            // Color based on speed (green < 500ms, yellow < 1000ms, red >= 1000ms)
            var speedColor = execMs is long ms
                ? (ms < 500 ? "#10B981" : ms < 1000 ? "#F59E0B" : "#EF4444")
                : "#00D4FF";
            ExecutionSpeedDisplay.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(speedColor));
        }
    }

    private void ArbEngine_OpportunityFound(object? sender, OpportunityEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // Update spread display
            if (SpreadPercent != null)
                SpreadPercent.Text = $"{e.Opportunity.NetSpreadPercentage:F2}%";
            if (SpreadAmount != null)
                SpreadAmount.Text = $"${e.Opportunity.ExpectedNetProfitQuote:F2}";

            // Update spread history chart
            UpdateSpreadHistoryChart(e.Opportunity.NetSpreadPercentage);

            // Show opportunity animation
            if (e.Opportunity.ShouldTrade)
            {
                ShowTradingAnimation();
                UpdateTradingStatus("TRADING", "#10B981", true);
            }
            else if (e.Opportunity.HasPositiveSpread)
            {
                UpdateTradingStatus("OPPORTUNITY", "#F59E0B", false);
            }
            else
            {
                UpdateTradingStatus("SCANNING", "#00D4FF", false);
            }
        });
    }

    private void UpdateSpreadHistoryChart(decimal spreadPercent)
    {
        // Add to history
        _spreadHistory.Add(spreadPercent);
        if (_spreadHistory.Count > MaxSpreadHistory)
            _spreadHistory.RemoveAt(0);

        if (_spreadHistory.Count < 2) return;

        // Calculate stats
        var min = _spreadHistory.Min();
        var max = _spreadHistory.Max();
        var avg = _spreadHistory.Average();

        // Update stat labels
        if (SpreadHistoryMin != null)
            SpreadHistoryMin.Text = $"Min: {min:F2}%";
        if (SpreadHistoryMax != null)
            SpreadHistoryMax.Text = $"Max: {max:F2}%";
        if (SpreadHistoryAvg != null)
            SpreadHistoryAvg.Text = $"Avg: {avg:F2}%";

        // Update chart polyline
        if (SpreadChartLine != null && SpreadChartCanvas != null)
        {
            var canvasWidth = SpreadChartCanvas.ActualWidth > 0 ? SpreadChartCanvas.ActualWidth : 200;
            var canvasHeight = SpreadChartCanvas.ActualHeight > 0 ? SpreadChartCanvas.ActualHeight : 40;

            var points = new PointCollection();
            var range = max - min;
            if (range == 0) range = 1; // Prevent division by zero

            for (int i = 0; i < _spreadHistory.Count; i++)
            {
                var x = (double)i / (MaxSpreadHistory - 1) * canvasWidth;
                var normalizedValue = (double)((_spreadHistory[i] - min) / range);
                var y = canvasHeight - (normalizedValue * canvasHeight * 0.8 + canvasHeight * 0.1);
                points.Add(new Point(x, y));
            }

            SpreadChartLine.Points = points;

            // Color based on trend
            if (_spreadHistory.Count > 1)
            {
                var trend = _spreadHistory[^1] - _spreadHistory[^2];
                var color = trend >= 0 ? "#10B981" : "#EF4444";
                SpreadChartLine.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            }
        }
    }

    private void ArbEngine_PriceUpdated(object? sender, PriceUpdateEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _scanCount++;
            var isBinance = e.Exchange.Contains("A") || e.Exchange.ToLower().Contains("binance");

            // Update price displays based on exchange
            if (isBinance)
            {
                if (BinancePrice != null)
                {
                    var newPrice = e.Ticker.LastPrice;
                    BinancePrice.Text = $"${newPrice:N2}";

                    // Flash animation on price change
                    if (_lastBinancePrice != 0 && newPrice != _lastBinancePrice)
                    {
                        var isUp = newPrice > _lastBinancePrice;
                        ShowPriceFlash(BinancePriceFlash, isUp);
                    }
                    _lastBinancePrice = newPrice;
                }

                // Update bid/ask
                if (BinanceBid != null)
                    BinanceBid.Text = $"${e.Ticker.BidPrice:N2}";
                if (BinanceAsk != null)
                    BinanceAsk.Text = $"${e.Ticker.AskPrice:N2}";
            }
            else
            {
                if (KuCoinPrice != null)
                {
                    var newPrice = e.Ticker.LastPrice;
                    KuCoinPrice.Text = $"${newPrice:N2}";

                    // Flash animation on price change
                    if (_lastKuCoinPrice != 0 && newPrice != _lastKuCoinPrice)
                    {
                        var isUp = newPrice > _lastKuCoinPrice;
                        ShowPriceFlash(KuCoinPriceFlash, isUp);
                    }
                    _lastKuCoinPrice = newPrice;
                }

                // Update bid/ask
                if (KuCoinBid != null)
                    KuCoinBid.Text = $"${e.Ticker.BidPrice:N2}";
                if (KuCoinAsk != null)
                    KuCoinAsk.Text = $"${e.Ticker.AskPrice:N2}";
            }

            // Update scan stats
            if (ScanCountText != null)
                ScanCountText.Text = $"Scans: {_scanCount}";
            if (LastScanTime != null)
                LastScanTime.Text = DateTime.Now.ToString("HH:mm:ss");

            // Update last check time
            if (LastCheckTime != null)
                LastCheckTime.Text = $"Scanning {e.Symbol} • Last check: {DateTime.Now:HH:mm:ss}";
        });
    }

    private void ShowPriceFlash(Border? flashBorder, bool isUp)
    {
        if (flashBorder == null) return;

        var flashColor = isUp ? Color.FromArgb(0x40, 0x10, 0xB9, 0x81) : Color.FromArgb(0x40, 0xEF, 0x44, 0x44);

        var animation = new ColorAnimation
        {
            From = flashColor,
            To = Colors.Transparent,
            Duration = TimeSpan.FromMilliseconds(500),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        flashBorder.Background = new SolidColorBrush(flashColor);
        ((SolidColorBrush)flashBorder.Background).BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    private void ArbEngine_ErrorOccurred(object? sender, EngineErrorEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _logger?.LogError("Engine", $"Error: {e.Message}");
            UpdateTradingStatus("ERROR", "#EF4444", false);
        });
    }

    #endregion

    #region UI Update Methods

    private void UpdateBotStatusUI(EngineStatus status, string? message)
    {
        // Update status text and colors
        var (statusText, statusColor, isActive) = status switch
        {
            EngineStatus.Idle => ("Bot is Idle", "#808080", false),
            EngineStatus.Starting => ("Bot is Starting...", "#F59E0B", true),
            EngineStatus.Running => ("Bot is Running", "#10B981", true),
            EngineStatus.Paused => ("Bot is Paused", "#F59E0B", false),
            EngineStatus.Stopping => ("Bot is Stopping...", "#F59E0B", false),
            EngineStatus.Stopped => ("Bot is Stopped", "#808080", false),
            EngineStatus.Error => ("Bot Error", "#EF4444", false),
            _ => ("Unknown Status", "#808080", false)
        };

        BotStatusText.Text = statusText;

        // Update status indicator color
        var color = (Color)ColorConverter.ConvertFromString(statusColor);
        StatusGlowBrush.Color = color;
        StatusGlowOuterBrush.Color = color;

        // Update button visibility
        if (status == EngineStatus.Running || status == EngineStatus.Paused)
        {
            StartButton.Visibility = Visibility.Collapsed;
            PauseButton.Visibility = Visibility.Visible;
            StopButton.Visibility = Visibility.Visible;
            PauseButton.Content = status == EngineStatus.Paused ? "▶ Resume" : "⏸ Pause";
        }
        else
        {
            StartButton.Visibility = Visibility.Visible;
            PauseButton.Visibility = Visibility.Collapsed;
            StopButton.Visibility = Visibility.Collapsed;
        }

        // Start/stop status pulse animation
        if (isActive)
        {
            StartStatusPulseAnimation();
        }
        else
        {
            StopStatusPulseAnimation();
        }

        // Update trading status indicator
        if (status == EngineStatus.Running)
        {
            UpdateTradingStatus("SCANNING", "#00D4FF", true);
            ShowDataFlowAnimation();
        }
        else if (status == EngineStatus.Paused)
        {
            UpdateTradingStatus("PAUSED", "#F59E0B", false);
            HideAnimations();
        }
        else
        {
            UpdateTradingStatus("IDLE", "#808080", false);
            HideAnimations();
        }

        _logger?.LogInfo("UI", $"Bot status: {status} - {message}");
    }

    private void UpdateTradingStatus(string status, string colorHex, bool showPulse)
    {
        TradingStatusLabel.Text = status;

        var color = (Color)ColorConverter.ConvertFromString(colorHex);
        TradingStatusLabel.Foreground = new SolidColorBrush(color);
        TradingStatusBorder.Background = new SolidColorBrush(Color.FromArgb(0x20, color.R, color.G, color.B));

        // Update status dot color
        if (StatusDot != null)
            StatusDot.Fill = new SolidColorBrush(color);

        // Update glow effect on spread indicator
        SpreadGlowEffect.Color = color;

        // Update Live indicator based on status
        if (LiveDot != null && LiveText != null)
        {
            if (status == "TRADING" || status == "SCANNING" || status == "OPPORTUNITY")
            {
                LiveDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                LiveText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                LiveText.Text = "LIVE";
            }
            else if (status == "PAUSED")
            {
                LiveDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                LiveText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                LiveText.Text = "PAUSED";
            }
            else
            {
                LiveDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#808080"));
                LiveText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#808080"));
                LiveText.Text = "IDLE";
            }
        }

        if (showPulse && status == "TRADING")
        {
            StartTradingPulseAnimation();
        }
    }

    private void ShowTradeCompletedAnimation(bool isProfit)
    {
        // Flash the spread border based on profit/loss
        var flashColor = isProfit ? Color.FromRgb(0x10, 0xB9, 0x81) : Color.FromRgb(0xEF, 0x44, 0x44);
        var originalColor = (Color)ColorConverter.ConvertFromString("#7C3AED");

        // Glow color animation
        var colorAnimation = new ColorAnimation
        {
            From = flashColor,
            To = originalColor,
            Duration = TimeSpan.FromSeconds(0.5),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(2),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        TradingGlow.BeginAnimation(SolidColorBrush.ColorProperty, colorAnimation);

        // Flash glow effect
        var glowAnimation = new ColorAnimation
        {
            From = flashColor,
            To = originalColor,
            Duration = TimeSpan.FromSeconds(0.3),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3)
        };
        SpreadGlowEffect.BeginAnimation(DropShadowEffect.ColorProperty, glowAnimation);

        // Scale animation for spread border
        var scaleUp = new DoubleAnimation
        {
            From = 1,
            To = 1.05,
            Duration = TimeSpan.FromSeconds(0.15),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(2)
        };

        if (SpreadBorder.RenderTransform is ScaleTransform st)
        {
            st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleUp);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleUp);
        }
        else
        {
            var transform = new ScaleTransform(1, 1);
            SpreadBorder.RenderTransform = transform;
            SpreadBorder.RenderTransformOrigin = new Point(0.5, 0.5);
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleUp);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleUp);
        }

        // Flash exchange borders
        var exchangeFlashColor = isProfit
            ? Color.FromArgb(0x40, 0x10, 0xB9, 0x81)
            : Color.FromArgb(0x40, 0xEF, 0x44, 0x44);

        FlashExchangeBorder(ExchangeABorder, exchangeFlashColor);
        FlashExchangeBorder(ExchangeBBorder, exchangeFlashColor);
    }

    private void FlashExchangeBorder(Border border, Color flashColor)
    {
        if (border == null) return;

        var originalBrush = border.Background?.Clone() as Brush ?? new SolidColorBrush(Colors.Transparent);

        var flashBrush = new SolidColorBrush(flashColor);
        border.Background = flashBrush;

        var animation = new ColorAnimation
        {
            From = flashColor,
            To = Colors.Transparent,
            Duration = TimeSpan.FromMilliseconds(500),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        animation.Completed += (s, e) =>
        {
            // Restore original gradient
            if (border.Name == "ExchangeABorder")
            {
                border.Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0x15, 0xF3, 0xBA, 0x2F), 0),
                        new GradientStop(Color.FromArgb(0x08, 0xF3, 0xBA, 0x2F), 1)
                    }
                };
            }
            else
            {
                border.Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0x15, 0x23, 0xAF, 0x91), 0),
                        new GradientStop(Color.FromArgb(0x08, 0x23, 0xAF, 0x91), 1)
                    }
                };
            }
        };

        flashBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    #endregion

    #region Animation Control

    private void StartStatusPulseAnimation()
    {
        try
        {
            var scaleAnimation = new DoubleAnimation
            {
                From = 1,
                To = 1.5,
                Duration = TimeSpan.FromSeconds(0.8),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

            var opacityAnimation = new DoubleAnimation
            {
                From = 1,
                To = 0.3,
                Duration = TimeSpan.FromSeconds(0.8),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

            StatusGlowOuter.BeginAnimation(WidthProperty, new DoubleAnimation
            {
                From = 14,
                To = 28,
                Duration = TimeSpan.FromSeconds(1),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });

            StatusGlowOuter.BeginAnimation(HeightProperty, new DoubleAnimation
            {
                From = 14,
                To = 28,
                Duration = TimeSpan.FromSeconds(1),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });

            StatusGlowOuter.BeginAnimation(OpacityProperty, new DoubleAnimation
            {
                From = 0.6,
                To = 0,
                Duration = TimeSpan.FromSeconds(1),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });
        }
        catch { }
    }

    private void StopStatusPulseAnimation()
    {
        try
        {
            StatusGlowOuter.BeginAnimation(WidthProperty, null);
            StatusGlowOuter.BeginAnimation(HeightProperty, null);
            StatusGlowOuter.BeginAnimation(OpacityProperty, null);
            StatusGlowOuter.Width = 14;
            StatusGlowOuter.Height = 14;
            StatusGlowOuter.Opacity = 1;
        }
        catch { }
    }

    private void ShowDataFlowAnimation()
    {
        ConnectionLines.Visibility = Visibility.Visible;

        // Animate the left-to-center dots (Binance -> Center)
        AnimateDataFlowDotLeftToCenter(DataFlowDot1, 0, "#F3BA2F");
        AnimateDataFlowDotLeftToCenter(DataFlowDot2, 0.5, "#00D4FF");

        // Animate the center-to-right dots (Center -> KuCoin)
        AnimateDataFlowDotCenterToRight(DataFlowDot3, 0.3, "#23AF91");
        AnimateDataFlowDotCenterToRight(DataFlowDot4, 0.8, "#7C3AED");
    }

    private void AnimateDataFlowDotLeftToCenter(Ellipse dot, double delay, string colorHex)
    {
        var animation = new DoubleAnimation
        {
            From = -10,
            To = 80,
            Duration = TimeSpan.FromSeconds(1.5),
            BeginTime = TimeSpan.FromSeconds(delay),
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        var opacityAnimation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromSeconds(0.3),
            BeginTime = TimeSpan.FromSeconds(delay),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };

        dot.BeginAnimation(Canvas.LeftProperty, animation);
        dot.BeginAnimation(OpacityProperty, opacityAnimation);
    }

    private void AnimateDataFlowDotCenterToRight(Ellipse dot, double delay, string colorHex)
    {
        var animation = new DoubleAnimation
        {
            From = 90,
            To = 180,
            Duration = TimeSpan.FromSeconds(1.5),
            BeginTime = TimeSpan.FromSeconds(delay),
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        var opacityAnimation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromSeconds(0.3),
            BeginTime = TimeSpan.FromSeconds(delay),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };

        dot.BeginAnimation(Canvas.LeftProperty, animation);
        dot.BeginAnimation(OpacityProperty, opacityAnimation);
    }

    private void ShowTradingAnimation()
    {
        TradingArrowCanvas.Visibility = Visibility.Visible;
        StartTradingPulseAnimation();

        // Animate the arrow
        var arrowAnimation = new DoubleAnimation
        {
            From = 0,
            To = 120,
            Duration = TimeSpan.FromSeconds(1.5),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };

        TradingArrow.BeginAnimation(Canvas.LeftProperty, arrowAnimation);
    }

    private void StartTradingPulseAnimation()
    {
        // Animate pulse ring 1 (innermost, cyan)
        var opacityAnimation1 = new DoubleAnimation
        {
            From = 0.8,
            To = 0,
            Duration = TimeSpan.FromSeconds(1.5),
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        var scaleAnimation1 = new DoubleAnimation
        {
            From = 80,
            To = 140,
            Duration = TimeSpan.FromSeconds(1.5),
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        TradingPulse1.BeginAnimation(OpacityProperty, opacityAnimation1);
        TradingPulse1.BeginAnimation(WidthProperty, scaleAnimation1);
        TradingPulse1.BeginAnimation(HeightProperty, scaleAnimation1);

        // Animate pulse ring 2 (middle, purple)
        var opacityAnimation2 = new DoubleAnimation
        {
            From = 0.6,
            To = 0,
            Duration = TimeSpan.FromSeconds(1.5),
            BeginTime = TimeSpan.FromSeconds(0.3),
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        var scaleAnimation2 = new DoubleAnimation
        {
            From = 100,
            To = 160,
            Duration = TimeSpan.FromSeconds(1.5),
            BeginTime = TimeSpan.FromSeconds(0.3),
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        TradingPulse2.BeginAnimation(OpacityProperty, opacityAnimation2);
        TradingPulse2.BeginAnimation(WidthProperty, scaleAnimation2);
        TradingPulse2.BeginAnimation(HeightProperty, scaleAnimation2);

        // Animate pulse ring 3 (outermost, green)
        var opacityAnimation3 = new DoubleAnimation
        {
            From = 0.4,
            To = 0,
            Duration = TimeSpan.FromSeconds(1.5),
            BeginTime = TimeSpan.FromSeconds(0.6),
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        var scaleAnimation3 = new DoubleAnimation
        {
            From = 120,
            To = 180,
            Duration = TimeSpan.FromSeconds(1.5),
            BeginTime = TimeSpan.FromSeconds(0.6),
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        TradingPulse3.BeginAnimation(OpacityProperty, opacityAnimation3);
        TradingPulse3.BeginAnimation(WidthProperty, scaleAnimation3);
        TradingPulse3.BeginAnimation(HeightProperty, scaleAnimation3);

        // Animate the glow color (cycles through colors)
        var glowAnimation = new ColorAnimation
        {
            From = (Color)ColorConverter.ConvertFromString("#00D4FF"),
            To = (Color)ColorConverter.ConvertFromString("#10B981"),
            Duration = TimeSpan.FromSeconds(1),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };

        TradingGlow.BeginAnimation(SolidColorBrush.ColorProperty, glowAnimation);

        // Animate status dot
        var statusDotAnimation = new DoubleAnimation
        {
            From = 0.3,
            To = 1,
            Duration = TimeSpan.FromSeconds(0.5),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        StatusDot.BeginAnimation(OpacityProperty, statusDotAnimation);
    }

    private void HideAnimations()
    {
        try
        {
            ConnectionLines.Visibility = Visibility.Collapsed;
            TradingArrowCanvas.Visibility = Visibility.Collapsed;

            // Stop data flow dot animations
            DataFlowDot1.BeginAnimation(Canvas.LeftProperty, null);
            DataFlowDot1.BeginAnimation(OpacityProperty, null);
            DataFlowDot2.BeginAnimation(Canvas.LeftProperty, null);
            DataFlowDot2.BeginAnimation(OpacityProperty, null);
            DataFlowDot3.BeginAnimation(Canvas.LeftProperty, null);
            DataFlowDot3.BeginAnimation(OpacityProperty, null);
            DataFlowDot4.BeginAnimation(Canvas.LeftProperty, null);
            DataFlowDot4.BeginAnimation(OpacityProperty, null);

            TradingArrow.BeginAnimation(Canvas.LeftProperty, null);

            // Stop pulse animations
            TradingPulse1.BeginAnimation(OpacityProperty, null);
            TradingPulse1.BeginAnimation(WidthProperty, null);
            TradingPulse1.BeginAnimation(HeightProperty, null);

            TradingPulse2.BeginAnimation(OpacityProperty, null);
            TradingPulse2.BeginAnimation(WidthProperty, null);
            TradingPulse2.BeginAnimation(HeightProperty, null);

            TradingPulse3.BeginAnimation(OpacityProperty, null);
            TradingPulse3.BeginAnimation(WidthProperty, null);
            TradingPulse3.BeginAnimation(HeightProperty, null);

            TradingGlow.BeginAnimation(SolidColorBrush.ColorProperty, null);
            StatusDot.BeginAnimation(OpacityProperty, null);

            // Reset pulse values
            TradingPulse1.Opacity = 0;
            TradingPulse2.Opacity = 0;
            TradingPulse3.Opacity = 0;
            TradingPulse1.Width = TradingPulse1.Height = 130;
            TradingPulse2.Width = TradingPulse2.Height = 150;
            TradingPulse3.Width = TradingPulse3.Height = 170;

            // Reset status dot
            StatusDot.Opacity = 1;
            StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#808080"));
        }
        catch { }
    }

    #endregion

    #region Bot Control

    private async void StartBot_Click(object sender, RoutedEventArgs e)
    {
        if (_arbEngine == null || _isBotRunning)
            return;

        try
        {
            _botCancellationTokenSource = new CancellationTokenSource();
            _logger?.LogInfo("Bot", "Starting arbitrage bot...");

            // Run StartAsync without awaiting to prevent blocking
            _ = Task.Run(async () =>
            {
                try
                {
                    await _arbEngine.StartAsync(_botCancellationTokenSource.Token);
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        _logger?.LogError("Bot", $"Bot stopped with error: {ex.Message}");
                    });
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError("Bot", $"Failed to start bot: {ex.Message}");
        }
    }

    private async void StopBot_Click(object sender, RoutedEventArgs e)
    {
        if (_arbEngine == null || !_isBotRunning)
            return;

        try
        {
            _botCancellationTokenSource?.Cancel();
            await _arbEngine.StopAsync();
            _logger?.LogInfo("Bot", "Arbitrage bot stopped");
        }
        catch (Exception ex)
        {
            _logger?.LogError("Bot", $"Error stopping bot: {ex.Message}");
        }
    }

    private void PauseBot_Click(object sender, RoutedEventArgs e)
    {
        if (_arbEngine == null)
            return;

        if (!_isBotPaused)
        {
            _arbEngine.Pause();
            _logger?.LogInfo("Bot", "Bot paused");
        }
        else
        {
            _arbEngine.Resume();
            _logger?.LogInfo("Bot", "Bot resumed");
        }
    }

    #endregion

    #region Window Events

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Start Splash Screen Animation first
            StartSplashAnimation();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MainWindow_Loaded error: {ex.Message}");
        }
    }

    private void InitializeMainApp()
    {
        try
        {
            // Set default tab
            SetActiveTab(DashboardTab);

            // Update mode indicator based on config
            UpdateModeIndicator();

            // Initialize UI state
            UpdateBotStatusUI(EngineStatus.Idle, "Ready to start");

            // Load dashboard stats from history
            LoadDashboardStats();

            // Start AI Scanner
            StartAIScanner();

            // Start Hyperdrive Animation
            StartHyperdriveAnimation();

            // Start price updates after a delay to let UI load first
            _priceUpdateTimer = new System.Windows.Threading.DispatcherTimer();
            _priceUpdateTimer.Interval = TimeSpan.FromSeconds(3);
            _priceUpdateTimer.Tick += PriceUpdateTimer_Tick;
            _priceUpdateTimer.Start();

            // Start status bar timer for time and memory updates
            _statusBarTimer = new System.Windows.Threading.DispatcherTimer();
            _statusBarTimer.Interval = TimeSpan.FromSeconds(1);
            _statusBarTimer.Tick += StatusBarTimer_Tick;
            _statusBarTimer.Start();

            // Initial status bar update
            UpdateStatusBar();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"InitializeMainApp error: {ex.Message}");
        }
    }

    private void UpdateModeIndicator()
    {
        var config = _configService?.GetConfig();
        var isLive = config?.General.LiveTrading ?? false;
        _logger?.LogInfo("UI", $"Trading mode: {(isLive ? "LIVE" : "DEMO")}");
    }

    #region Splash Screen Animation

    private System.Windows.Threading.DispatcherTimer? _splashTimer;
    private System.Windows.Threading.DispatcherTimer? _splashHyperTimer;
    private int _splashProgress = 0;
    private readonly string[] _loadingMessages = new[]
    {
        "Initializing...",
        "Loading exchange connections...",
        "Connecting to market data...",
        "Preparing arbitrage engine...",
        "Loading configuration...",
        "Almost ready..."
    };

    private async void StartSplashAnimation()
    {
        try
        {
            // Start splash hyperdrive animation
            StartSplashHyperdrive();

            // Animate loading progress
            _splashTimer = new System.Windows.Threading.DispatcherTimer();
            _splashTimer.Interval = TimeSpan.FromMilliseconds(50);
            _splashTimer.Tick += SplashTimer_Tick;
            _splashTimer.Start();

            // Wait for loading to complete (simulated)
            await Task.Delay(3000);

            // Stop splash animations
            _splashTimer?.Stop();
            _splashHyperTimer?.Stop();

            // Animate logo shrink and move to corner
            await AnimateSplashToCorner();

            // Initialize main app
            InitializeMainApp();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Splash animation error: {ex.Message}");
            // Fallback: just hide splash and init
            if (SplashOverlay != null) SplashOverlay.Visibility = Visibility.Collapsed;
            InitializeMainApp();
        }
    }

    private void SplashTimer_Tick(object? sender, EventArgs e)
    {
        _splashProgress += 2;

        if (_splashProgress > 100)
        {
            _splashProgress = 100;
            _splashTimer?.Stop();
            return;
        }

        // Update progress bar
        if (LoadingProgress != null)
        {
            LoadingProgress.Width = _splashProgress * 2; // Max 200px
        }

        // Update loading text
        if (LoadingText != null)
        {
            var msgIndex = Math.Min(_splashProgress / 20, _loadingMessages.Length - 1);
            LoadingText.Text = _loadingMessages[msgIndex];
        }
    }

    private void StartSplashHyperdrive()
    {
        var splashLines = new Line?[] { SplashLine1, SplashLine2, SplashLine3, SplashLine4,
                                        SplashLine5, SplashLine6, SplashLine7, SplashLine8,
                                        SplashLine9, SplashLine10 };
        var splashStars = new Ellipse?[] { SplashStar1, SplashStar2, SplashStar3, SplashStar4 };

        _splashHyperTimer = new System.Windows.Threading.DispatcherTimer();
        _splashHyperTimer.Interval = TimeSpan.FromMilliseconds(80);
        int frame = 0;

        _splashHyperTimer.Tick += (s, e) =>
        {
            frame++;

            // Animate random line
            if (frame % 3 == 0)
            {
                var line = splashLines[_hyperRandom.Next(splashLines.Length)];
                if (line != null)
                {
                    var fadeIn = new DoubleAnimation(0, 0.7 + _hyperRandom.NextDouble() * 0.3, TimeSpan.FromMilliseconds(100));
                    fadeIn.Completed += (s2, e2) =>
                    {
                        var fadeOut = new DoubleAnimation(line.Opacity, 0, TimeSpan.FromMilliseconds(400 + _hyperRandom.Next(200)));
                        line.BeginAnimation(OpacityProperty, fadeOut);
                    };
                    line.BeginAnimation(OpacityProperty, fadeIn);
                }
            }

            // Animate random star
            if (frame % 5 == 0)
            {
                var star = splashStars[_hyperRandom.Next(splashStars.Length)];
                if (star != null && star.Opacity < 0.1)
                {
                    AnimateSplashStar(star);
                }
            }
        };

        _splashHyperTimer.Start();
    }

    private void AnimateSplashStar(Ellipse star)
    {
        var targets = new (double x, double y)[]
        {
            (0, 0), (400, 0), (0, 300), (400, 300),
            (200, 0), (200, 300), (0, 150), (400, 150)
        };

        var target = targets[_hyperRandom.Next(targets.Length)];
        var duration = TimeSpan.FromMilliseconds(400 + _hyperRandom.Next(300));

        star.SetValue(Canvas.LeftProperty, 200.0);
        star.SetValue(Canvas.TopProperty, 150.0);

        var storyboard = new Storyboard();

        var moveX = new DoubleAnimation(200, target.x, duration);
        var moveY = new DoubleAnimation(150, target.y, duration);
        moveX.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        moveY.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn };

        Storyboard.SetTarget(moveX, star);
        Storyboard.SetTargetProperty(moveX, new PropertyPath(Canvas.LeftProperty));
        Storyboard.SetTarget(moveY, star);
        Storyboard.SetTargetProperty(moveY, new PropertyPath(Canvas.TopProperty));

        var fadeIn = new DoubleAnimation(0, 0.9, TimeSpan.FromMilliseconds(60));
        var fadeOut = new DoubleAnimation(0.9, 0, TimeSpan.FromMilliseconds(100));
        fadeOut.BeginTime = duration - TimeSpan.FromMilliseconds(100);

        Storyboard.SetTarget(fadeIn, star);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
        Storyboard.SetTarget(fadeOut, star);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));

        storyboard.Children.Add(moveX);
        storyboard.Children.Add(moveY);
        storyboard.Children.Add(fadeIn);
        storyboard.Children.Add(fadeOut);

        storyboard.Begin();
    }

    private async Task AnimateSplashToCorner()
    {
        if (SplashContent == null || SplashOverlay == null || LoadingPanel == null) return;

        // Calculate target position (top-left corner)
        var windowWidth = ActualWidth;
        var windowHeight = ActualHeight;

        // Target: scale to ~0.4 and move to top-left
        var targetScale = 0.35;
        var targetX = -(windowWidth / 2) + 180;  // Move to left
        var targetY = -(windowHeight / 2) + 120; // Move to top

        // Fade out loading panel first
        var fadeOutLoading = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
        LoadingPanel.BeginAnimation(OpacityProperty, fadeOutLoading);

        await Task.Delay(300);

        // Animate scale and position
        var duration = TimeSpan.FromMilliseconds(800);
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var scaleXAnim = new DoubleAnimation(1, targetScale, duration) { EasingFunction = easing };
        var scaleYAnim = new DoubleAnimation(1, targetScale, duration) { EasingFunction = easing };
        var translateXAnim = new DoubleAnimation(0, targetX, duration) { EasingFunction = easing };
        var translateYAnim = new DoubleAnimation(0, targetY, duration) { EasingFunction = easing };

        SplashScale?.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
        SplashScale?.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
        SplashTranslate?.BeginAnimation(TranslateTransform.XProperty, translateXAnim);
        SplashTranslate?.BeginAnimation(TranslateTransform.YProperty, translateYAnim);

        // Fade out splash overlay
        var fadeOutOverlay = new DoubleAnimation(1, 0, duration) { EasingFunction = easing };
        fadeOutOverlay.BeginTime = TimeSpan.FromMilliseconds(400);
        SplashOverlay.BeginAnimation(OpacityProperty, fadeOutOverlay);

        await Task.Delay(1200);

        // Hide splash completely
        SplashOverlay.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region Hyperdrive Animation

    private System.Windows.Threading.DispatcherTimer? _hyperdriveTimer;
    private readonly Random _hyperRandom = new();

    private void StartHyperdriveAnimation()
    {
        try
        {
            // Create continuous hyperdrive animation
            _hyperdriveTimer = new System.Windows.Threading.DispatcherTimer();
            _hyperdriveTimer.Interval = TimeSpan.FromMilliseconds(100);
            _hyperdriveTimer.Tick += HyperdriveTimer_Tick;
            _hyperdriveTimer.Start();

            // Start initial streak animations
            AnimateHyperdriveStreaks();
            AnimateStarParticles();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Hyperdrive animation error: {ex.Message}");
        }
    }

    private int _hyperdriveFrame = 0;

    private void HyperdriveTimer_Tick(object? sender, EventArgs e)
    {
        _hyperdriveFrame++;

        // Trigger new streak animation every few frames
        if (_hyperdriveFrame % 8 == 0)
        {
            AnimateRandomStreak();
        }

        // Trigger particle animation every few frames
        if (_hyperdriveFrame % 12 == 0)
        {
            AnimateRandomParticle();
        }
    }

    private void AnimateHyperdriveStreaks()
    {
        // Animate all hyperdrive lines with staggered timing
        var lines = new[] { HyperLine1, HyperLine2, HyperLine3, HyperLine4,
                           HyperLine5, HyperLine6, HyperLine7, HyperLine8,
                           HyperLine9, HyperLine10, HyperLine11, HyperLine12,
                           HyperLine13, HyperLine14, HyperLine15, HyperLine16 };

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i] == null) continue;

            var delay = TimeSpan.FromMilliseconds(i * 150 + _hyperRandom.Next(100));
            var duration = TimeSpan.FromMilliseconds(800 + _hyperRandom.Next(400));

            var storyboard = new Storyboard();

            // Fade in and out
            var fadeIn = new DoubleAnimation(0, 0.6 + _hyperRandom.NextDouble() * 0.4, TimeSpan.FromMilliseconds(150));
            var fadeOut = new DoubleAnimation(0.6 + _hyperRandom.NextDouble() * 0.4, 0, duration);
            fadeOut.BeginTime = TimeSpan.FromMilliseconds(150);

            Storyboard.SetTarget(fadeIn, lines[i]);
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
            Storyboard.SetTarget(fadeOut, lines[i]);
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));

            storyboard.Children.Add(fadeIn);
            storyboard.Children.Add(fadeOut);
            storyboard.BeginTime = delay;

            storyboard.Completed += (s, e) =>
            {
                // Restart with random delay for continuous effect
                var restartDelay = TimeSpan.FromMilliseconds(_hyperRandom.Next(500, 2000));
                var restartTimer = new System.Windows.Threading.DispatcherTimer { Interval = restartDelay };
                restartTimer.Tick += (s2, e2) =>
                {
                    restartTimer.Stop();
                    AnimateHyperdriveStreaks();
                };
                restartTimer.Start();
            };

            storyboard.Begin();
        }
    }

    private void AnimateRandomStreak()
    {
        var lines = new Line?[] { HyperLine1, HyperLine2, HyperLine3, HyperLine4,
                                  HyperLine5, HyperLine6, HyperLine7, HyperLine8,
                                  HyperLine9, HyperLine10, HyperLine11, HyperLine12,
                                  HyperLine13, HyperLine14, HyperLine15, HyperLine16 };

        var line = lines[_hyperRandom.Next(lines.Length)];
        if (line == null) return;

        var duration = TimeSpan.FromMilliseconds(400 + _hyperRandom.Next(300));

        var fadeIn = new DoubleAnimation(0, 0.5 + _hyperRandom.NextDouble() * 0.5, TimeSpan.FromMilliseconds(100));
        fadeIn.Completed += (s, e) =>
        {
            var fadeOut = new DoubleAnimation(line.Opacity, 0, duration);
            line.BeginAnimation(OpacityProperty, fadeOut);
        };

        line.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void AnimateStarParticles()
    {
        var particles = new[] { StarParticle1, StarParticle2, StarParticle3,
                               StarParticle4, StarParticle5, StarParticle6 };

        // Define target positions (edges of the canvas, radiating from center)
        var targets = new (double x, double y)[]
        {
            (0, 0), (280, 0), (0, 160), (280, 160),
            (140, 0), (140, 160), (0, 80), (280, 80)
        };

        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i] == null) continue;

            var particle = particles[i];
            var target = targets[_hyperRandom.Next(targets.Length)];
            var delay = TimeSpan.FromMilliseconds(i * 200 + _hyperRandom.Next(300));
            var duration = TimeSpan.FromMilliseconds(600 + _hyperRandom.Next(400));

            var storyboard = new Storyboard();

            // Move from center to edge
            var moveX = new DoubleAnimation(140, target.x, duration);
            var moveY = new DoubleAnimation(80, target.y, duration);
            moveX.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            moveY.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn };

            Storyboard.SetTarget(moveX, particle);
            Storyboard.SetTargetProperty(moveX, new PropertyPath(Canvas.LeftProperty));
            Storyboard.SetTarget(moveY, particle);
            Storyboard.SetTargetProperty(moveY, new PropertyPath(Canvas.TopProperty));

            // Fade in then out
            var fadeIn = new DoubleAnimation(0, 0.8, TimeSpan.FromMilliseconds(100));
            var fadeOut = new DoubleAnimation(0.8, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.BeginTime = duration - TimeSpan.FromMilliseconds(200);

            Storyboard.SetTarget(fadeIn, particle);
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
            Storyboard.SetTarget(fadeOut, particle);
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));

            storyboard.Children.Add(moveX);
            storyboard.Children.Add(moveY);
            storyboard.Children.Add(fadeIn);
            storyboard.Children.Add(fadeOut);
            storyboard.BeginTime = delay;

            storyboard.Completed += (s, e) =>
            {
                // Reset position and restart
                particle.SetValue(Canvas.LeftProperty, 140.0);
                particle.SetValue(Canvas.TopProperty, 80.0);

                var restartDelay = TimeSpan.FromMilliseconds(_hyperRandom.Next(300, 1500));
                var restartTimer = new System.Windows.Threading.DispatcherTimer { Interval = restartDelay };
                restartTimer.Tick += (s2, e2) =>
                {
                    restartTimer.Stop();
                    AnimateSingleParticle(particle);
                };
                restartTimer.Start();
            };

            storyboard.Begin();
        }
    }

    private void AnimateRandomParticle()
    {
        var particles = new Ellipse?[] { StarParticle1, StarParticle2, StarParticle3,
                                         StarParticle4, StarParticle5, StarParticle6 };

        var particle = particles[_hyperRandom.Next(particles.Length)];
        if (particle == null || particle.Opacity > 0.1) return; // Skip if already animating

        AnimateSingleParticle(particle);
    }

    private void AnimateSingleParticle(Ellipse particle)
    {
        var targets = new (double x, double y)[]
        {
            (0, 0), (280, 0), (0, 160), (280, 160),
            (140, 0), (140, 160), (0, 80), (280, 80),
            (50, 0), (230, 0), (50, 160), (230, 160)
        };

        var target = targets[_hyperRandom.Next(targets.Length)];
        var duration = TimeSpan.FromMilliseconds(500 + _hyperRandom.Next(400));

        // Reset to center
        particle.SetValue(Canvas.LeftProperty, 140.0);
        particle.SetValue(Canvas.TopProperty, 80.0);

        var storyboard = new Storyboard();

        // Move from center to edge with acceleration
        var moveX = new DoubleAnimation(140, target.x, duration);
        var moveY = new DoubleAnimation(80, target.y, duration);
        moveX.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        moveY.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn };

        Storyboard.SetTarget(moveX, particle);
        Storyboard.SetTargetProperty(moveX, new PropertyPath(Canvas.LeftProperty));
        Storyboard.SetTarget(moveY, particle);
        Storyboard.SetTargetProperty(moveY, new PropertyPath(Canvas.TopProperty));

        // Fade
        var fadeIn = new DoubleAnimation(0, 0.9, TimeSpan.FromMilliseconds(80));
        var fadeOut = new DoubleAnimation(0.9, 0, TimeSpan.FromMilliseconds(150));
        fadeOut.BeginTime = duration - TimeSpan.FromMilliseconds(150);

        Storyboard.SetTarget(fadeIn, particle);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
        Storyboard.SetTarget(fadeOut, particle);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));

        storyboard.Children.Add(moveX);
        storyboard.Children.Add(moveY);
        storyboard.Children.Add(fadeIn);
        storyboard.Children.Add(fadeOut);

        storyboard.Begin();
    }

    #endregion

    private void StatusBarTimer_Tick(object? sender, EventArgs e)
    {
        UpdateStatusBar();
    }

    private void UpdateStatusBar()
    {
        try
        {
            // Update time (UTC)
            if (StatusBarTime != null)
            {
                StatusBarTime.Text = DateTime.UtcNow.ToString("HH:mm:ss");
            }

            // Update memory usage
            if (StatusBarMemory != null)
            {
                var process = System.Diagnostics.Process.GetCurrentProcess();
                var memoryMB = process.WorkingSet64 / (1024 * 1024);
                StatusBarMemory.Text = $"{memoryMB} MB";

                // Color code based on memory usage
                var memoryColor = memoryMB switch
                {
                    > 500 => "#EF4444",  // Red - high usage
                    > 300 => "#F59E0B",  // Yellow - medium usage
                    _ => "#00D4FF"       // Cyan - normal
                };
                StatusBarMemory.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(memoryColor));
            }

            // Update P&L display
            if (StatusBarPnL != null)
            {
                StatusBarPnL.Text = _todayPnL >= 0 ? $"+${_todayPnL:F2}" : $"-${Math.Abs(_todayPnL):F2}";
                StatusBarPnL.Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(_todayPnL >= 0 ? "#10B981" : "#EF4444"));
            }

            // Update trades count
            if (StatusBarTrades != null)
            {
                StatusBarTrades.Text = _todayTradeCount.ToString();
            }

            // Update bot status
            UpdateStatusBarBotStatus();
        }
        catch { }
    }

    private void UpdateStatusBarBotStatus()
    {
        if (StatusBarBotStatus == null || StatusBarBotDot == null) return;

        string status;
        string color;

        if (_isBotRunning)
        {
            if (_isBotPaused)
            {
                status = "PAUSED";
                color = "#F59E0B";
            }
            else
            {
                status = "RUNNING";
                color = "#10B981";
            }
        }
        else
        {
            status = "IDLE";
            color = "#808080";
        }

        StatusBarBotStatus.Text = status;
        StatusBarBotStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        StatusBarBotDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    private async void PriceUpdateTimer_Tick(object? sender, EventArgs e)
    {
        _priceUpdateTimer!.Interval = TimeSpan.FromSeconds(30);
        await UpdatePricesAsync();
    }

    private async Task UpdatePricesAsync()
    {
        if (_coinDataService == null) return;

        try
        {
            var btcPrice = await _coinDataService.GetPriceAsync("bitcoin");
            if (btcPrice > 0)
            {
                Dispatcher.Invoke(() =>
                {
                    if (BinancePrice != null)
                        BinancePrice.Text = $"${btcPrice:N2}";
                    if (KuCoinPrice != null)
                    {
                        var kucoinPrice = btcPrice * (1 - 0.0015m);
                        KuCoinPrice.Text = $"${kucoinPrice:N2}";

                        var spread = Math.Abs(btcPrice - kucoinPrice);
                        var spreadPercent = (spread / btcPrice) * 100;
                        if (SpreadPercent != null)
                            SpreadPercent.Text = $"{spreadPercent:F2}%";
                        if (SpreadAmount != null)
                            SpreadAmount.Text = $"${spread:N2}";
                    }

                    if (LastCheckTime != null && !_isBotRunning)
                        LastCheckTime.Text = $"Price updated: {DateTime.Now:HH:mm:ss}";
                });

                _logger?.LogInfo("Prices", $"BTC price updated: ${btcPrice:N2}");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("Prices", $"Error updating prices: {ex.Message}");
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeButton_Click(sender, e);
        }
        else
        {
            DragMove();
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Responsive layout adjustments
        double width = e.NewSize.Width;

        // Adjust for mobile-like layout (< 800px)
        if (width < 800)
        {
            // Hide center status bar badges on small screens
            if (StatusBarCenter != null)
                StatusBarCenter.Visibility = Visibility.Collapsed;

            // Adjust column widths for mobile
            if (LeftColumn != null) LeftColumn.Width = new GridLength(0);
            if (RightColumn != null) RightColumn.Width = new GridLength(0);
        }
        else if (width < 1100)
        {
            // Tablet mode - show essential items
            if (StatusBarCenter != null)
                StatusBarCenter.Visibility = Visibility.Visible;

            if (LeftColumn != null) LeftColumn.Width = new GridLength(240);
            if (RightColumn != null) RightColumn.Width = new GridLength(0);
        }
        else
        {
            // Desktop mode - show everything
            if (StatusBarCenter != null)
                StatusBarCenter.Visibility = Visibility.Visible;

            if (LeftColumn != null) LeftColumn.Width = new GridLength(280);
            if (RightColumn != null) RightColumn.Width = new GridLength(320);
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _priceUpdateTimer?.Stop();
        _aiScannerTimer?.Stop();
        _statusBarTimer?.Stop();
        HideAnimations();

        if (_isBotRunning && _arbEngine != null)
        {
            try
            {
                _botCancellationTokenSource?.Cancel();
                await _arbEngine.StopAsync();
            }
            catch { }
        }

        // No explicit save needed - data is auto-saved

        Close();
    }

    #endregion

    #region Navigation

    private void DashboardTab_Click(object sender, MouseButtonEventArgs e)
    {
        SetActiveTab(DashboardTab);
        ShowPage("Dashboard");
    }

    private void TradingTab_Click(object sender, MouseButtonEventArgs e)
    {
        SetActiveTab(TradingTab);
        ShowPage("Trading");
    }

    private void ScannerTab_Click(object sender, MouseButtonEventArgs e)
    {
        SetActiveTab(ScannerTab);
        ShowPage("Scanner");
    }

    private void HistoryTab_Click(object sender, MouseButtonEventArgs e)
    {
        SetActiveTab(HistoryTab);
        ShowPage("History");
    }

    private void SettingsTab_Click(object sender, MouseButtonEventArgs e)
    {
        SetActiveTab(SettingsTab);
        ShowPage("Settings");
    }

    private void ShowPage(string pageName)
    {
        // Hide all pages
        DashboardContent.Visibility = Visibility.Collapsed;
        TradingContent.Visibility = Visibility.Collapsed;
        ScannerContent.Visibility = Visibility.Collapsed;
        HistoryContent.Visibility = Visibility.Collapsed;
        SettingsContent.Visibility = Visibility.Collapsed;

        // Show the selected page
        switch (pageName)
        {
            case "Dashboard":
                DashboardContent.Visibility = Visibility.Visible;
                break;
            case "Trading":
                TradingContent.Visibility = Visibility.Visible;
                break;
            case "Scanner":
                ScannerContent.Visibility = Visibility.Visible;
                break;
            case "History":
                HistoryContent.Visibility = Visibility.Visible;
                break;
            case "Settings":
                SettingsContent.Visibility = Visibility.Visible;
                break;
        }
    }

    private void SetActiveTab(Border tab)
    {
        if (_activeTab != null)
        {
            _activeTab.Background = Brushes.Transparent;
            var prevText = _activeTab.Child as TextBlock;
            if (prevText != null)
            {
                prevText.FontWeight = FontWeights.Medium;
                prevText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80FFFFFF"));
            }
        }

        _activeTab = tab;
        _activeTab.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20FFFFFF"));
        var text = _activeTab.Child as TextBlock;
        if (text != null)
        {
            text.FontWeight = FontWeights.SemiBold;
            text.Foreground = Brushes.White;
        }
    }

    #endregion

    #region Dashboard Data Methods

    private async void UpdateRecentTradesDisplay()
    {
        // Get recent trades from history service
        if (_tradeHistory == null) return;

        try
        {
            var recentTrades = await _tradeHistory.GetRecentTradesAsync(5);

            // Update the Recent Trades list in Dashboard
            // Note: In a full implementation, you would bind this to a data template
            // For now, the static data in XAML serves as placeholder
        }
        catch (Exception ex)
        {
            _logger?.LogError("Dashboard", $"Error loading recent trades: {ex.Message}");
        }
    }

    private async void LoadDashboardStats()
    {
        if (_tradeHistory == null) return;

        try
        {
            // Load today's stats
            var todayStats = await _tradeHistory.GetStatsAsync(DateTime.Today, DateTime.Now);

            _todayTradeCount = todayStats.TotalTrades;
            _successfulTrades = todayStats.WinningTrades;
            _todayPnL = todayStats.TotalPnL;

            // Update UI
            if (TodayPnLDisplay != null)
            {
                TodayPnLDisplay.Text = _todayPnL >= 0 ? $"+${_todayPnL:F2}" : $"-${Math.Abs(_todayPnL):F2}";
                TodayPnLDisplay.Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(_todayPnL >= 0 ? "#10B981" : "#EF4444"));
            }

            if (TradeCountDisplay != null)
                TradeCountDisplay.Text = $"{_todayTradeCount} trades";

            if (WinRateDisplay != null && _todayTradeCount > 0)
            {
                WinRateDisplay.Text = $"{todayStats.WinRate:F1}%";
            }

            if (WinLossDisplay != null)
                WinLossDisplay.Text = $"{_successfulTrades}W / {todayStats.LosingTrades}L";

            if (DrawdownDisplay != null)
                DrawdownDisplay.Text = $"-{todayStats.MaxDrawdown:F2}%";
        }
        catch (Exception ex)
        {
            _logger?.LogError("Dashboard", $"Error loading dashboard stats: {ex.Message}");
        }
    }

    private async void StartAIScanner()
    {
        // Start AI scanner timer for periodic opportunity scanning
        _aiScannerTimer = new System.Windows.Threading.DispatcherTimer();
        _aiScannerTimer.Interval = TimeSpan.FromSeconds(10);
        _aiScannerTimer.Tick += async (s, e) => await ScanForOpportunities();
        _aiScannerTimer.Start();

        // Initial scan
        await ScanForOpportunities();
    }

    private async Task ScanForOpportunities()
    {
        if (_coinDataService == null) return;

        try
        {
            var symbols = new[] { "bitcoin", "ethereum", "solana", "ripple", "dogecoin" };
            var opportunities = new List<ArbitrageOpportunityInfo>();

            foreach (var symbol in symbols)
            {
                var price = await _coinDataService.GetPriceAsync(symbol);
                if (price > 0)
                {
                    // Simulate spread calculation between exchanges
                    var random = new Random();
                    var spreadPercent = (decimal)(random.NextDouble() * 0.3); // 0-0.3%

                    var exchanges = new[] { "Binance", "KuCoin", "OKX", "Bybit", "Gate.io", "Bitkub" };
                    var buyExchange = exchanges[random.Next(exchanges.Length)];
                    var sellExchange = exchanges[random.Next(exchanges.Length)];
                    while (sellExchange == buyExchange)
                        sellExchange = exchanges[random.Next(exchanges.Length)];

                    opportunities.Add(new ArbitrageOpportunityInfo
                    {
                        Symbol = symbol.ToUpper().Substring(0, Math.Min(3, symbol.Length)) + "/USDT",
                        SpreadPercent = spreadPercent,
                        BuyExchange = buyExchange,
                        SellExchange = sellExchange,
                        Priority = spreadPercent >= 0.15m ? "HIGH" : spreadPercent >= 0.1m ? "MEDIUM" : "LOW"
                    });
                }
            }

            // Sort by spread and take top 3
            _aiOpportunities.Clear();
            _aiOpportunities.AddRange(opportunities.OrderByDescending(o => o.SpreadPercent).Take(3));

            // Notify if high spread opportunity found
            var highOpportunity = _aiOpportunities.FirstOrDefault(o => o.Priority == "HIGH");
            if (highOpportunity != null)
            {
                _notificationService?.NotifyOpportunity(highOpportunity.Symbol, highOpportunity.SpreadPercent,
                    $"{highOpportunity.BuyExchange} → {highOpportunity.SellExchange}");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("AIScanner", $"Error scanning opportunities: {ex.Message}");
        }
    }

    #endregion
}

/// <summary>
/// Model for AI Scanner opportunities
/// </summary>
public class ArbitrageOpportunityInfo
{
    public string Symbol { get; set; } = "";
    public decimal SpreadPercent { get; set; }
    public string BuyExchange { get; set; } = "";
    public string SellExchange { get; set; } = "";
    public string Priority { get; set; } = "LOW";
}
