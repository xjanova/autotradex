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
    private readonly IConnectionStatusService? _connectionStatusService;
    private readonly ILicenseService? _licenseService;
    private System.Windows.Threading.DispatcherTimer? _priceUpdateTimer;
    private System.Windows.Threading.DispatcherTimer? _aiScannerTimer;
    private System.Windows.Threading.DispatcherTimer? _statusBarTimer;
    private readonly Random _hyperRandom = new();  // Used for splash screen animation
    private bool _isBotRunning = false;
    private bool _isBotPaused = false;
    private bool _canStartTrading = false;
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
        _connectionStatusService = App.Services?.GetService<IConnectionStatusService>();
        _licenseService = App.Services?.GetService<ILicenseService>();

        // Subscribe to Connection Status events
        if (_connectionStatusService != null)
        {
            _connectionStatusService.ConnectionStatusChanged += ConnectionStatus_Changed;
        }

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

        // Subscribe to License events (for Demo Mode reminders)
        if (_licenseService is LicenseService licenseServiceImpl)
        {
            licenseServiceImpl.DemoModeReminder += LicenseService_DemoModeReminder;
        }

        // Set Dashboard as default active tab
        Loaded += MainWindow_Loaded;
    }

    #region Demo Mode Warning

    private void LicenseService_DemoModeReminder(object? sender, DemoModeReminderEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            ShowDemoModeWarning(e.Config);
        });
    }

    private void ShowDemoModeWarning(DemoModeConfig config)
    {
        _logger?.LogInfo("License", "Showing Demo Mode warning dialog");

        // Check if early bird discount is available
        var earlyBirdInfo = (_licenseService as LicenseService)?.EarlyBirdInfo;
        var hasEarlyBird = earlyBirdInfo?.Eligible ?? false;

        // Create custom styled dialog
        var dialog = new Window
        {
            Title = "Demo Mode",
            Width = 450,
            Height = hasEarlyBird ? 380 : 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Colors.Transparent)
        };

        // Create dialog content with dark theme
        var border = new Border
        {
            Background = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#FF1A0A2E"),
                (Color)ColorConverter.ConvertFromString("#FF0A0A1A"),
                45),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#40F59E0B")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 30,
                Opacity = 0.5
            }
        };

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        if (hasEarlyBird)
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // For discount banner
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header
        var headerBorder = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20F59E0B")),
            Padding = new Thickness(20, 15, 20, 15)
        };
        var headerStack = new StackPanel { Orientation = Orientation.Horizontal };
        headerStack.Children.Add(new TextBlock
        {
            Text = "⚠️",
            FontSize = 24,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        });
        headerStack.Children.Add(new TextBlock
        {
            Text = "Demo Mode Active",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")),
            VerticalAlignment = VerticalAlignment.Center
        });
        headerBorder.Child = headerStack;
        Grid.SetRow(headerBorder, 0);
        mainGrid.Children.Add(headerBorder);

        // Content
        var contentStack = new StackPanel { Margin = new Thickness(24, 16, 24, 8) };
        contentStack.Children.Add(new TextBlock
        {
            Text = config.DemoMessage,
            FontSize = 14,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        contentStack.Children.Add(new TextBlock
        {
            Text = "ในโหมด Demo คุณสามารถ:",
            FontSize = 13,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B0B0B0")),
            Margin = new Thickness(0, 0, 0, 8)
        });

        var features = new[]
        {
            "✓ ดูโอกาสในการเทรด Arbitrage",
            "✓ เชื่อมต่อกับ Exchange",
            "✗ ไม่สามารถเทรดจริงได้",
            "✗ ไม่สามารถใช้ Auto-Trading ได้"
        };

        foreach (var feature in features)
        {
            var isEnabled = feature.StartsWith("✓");
            contentStack.Children.Add(new TextBlock
            {
                Text = feature,
                FontSize = 12,
                Foreground = new SolidColorBrush(isEnabled
                    ? (Color)ColorConverter.ConvertFromString("#10B981")
                    : (Color)ColorConverter.ConvertFromString("#EF4444")),
                Margin = new Thickness(12, 2, 0, 2)
            });
        }

        Grid.SetRow(contentStack, 1);
        mainGrid.Children.Add(contentStack);

        // Early Bird Discount Banner (if eligible)
        if (hasEarlyBird && earlyBirdInfo != null)
        {
            var discountBanner = CreateEarlyBirdBanner(earlyBirdInfo, () =>
            {
                dialog.Close();
                ShowLicenseDialog();
            });
            Grid.SetRow(discountBanner, 2);
            mainGrid.Children.Add(discountBanner);
        }

        // Buttons
        var buttonStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(24, 8, 24, 20)
        };

        var buyButton = new Button
        {
            Content = hasEarlyBird ? "🎁 ซื้อตอนนี้ ลด 20%!" : "🔑 Activate License",
            Padding = new Thickness(20, 10, 20, 10),
            Margin = new Thickness(0, 0, 10, 0),
            Background = hasEarlyBird
                ? new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString("#F59E0B"),
                    (Color)ColorConverter.ConvertFromString("#D97706"),
                    90)
                : new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString("#8B5CF6"),
                    (Color)ColorConverter.ConvertFromString("#7C3AED"),
                    90),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        buyButton.Click += (s, e) =>
        {
            dialog.Close();
            ShowLicenseDialog();
        };

        var laterButton = new Button
        {
            Content = "Later",
            Padding = new Thickness(20, 10, 20, 10),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20FFFFFF")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B0B0B0")),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        laterButton.Click += (s, e) => dialog.Close();

        buttonStack.Children.Add(buyButton);
        buttonStack.Children.Add(laterButton);
        Grid.SetRow(buttonStack, hasEarlyBird ? 3 : 2);
        mainGrid.Children.Add(buttonStack);

        border.Child = mainGrid;
        dialog.Content = border;

        // Allow dragging the dialog
        border.MouseLeftButtonDown += (s, e) => dialog.DragMove();

        dialog.ShowDialog();
    }

    /// <summary>
    /// Creates an attractive early bird discount banner
    /// </summary>
    private Border CreateEarlyBirdBanner(EarlyBirdInfo earlyBird, Action onBuyClick)
    {
        var banner = new Border
        {
            Margin = new Thickness(16, 0, 16, 8),
            Padding = new Thickness(16, 12, 16, 12),
            CornerRadius = new CornerRadius(12),
            Background = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#302563EB"),
                (Color)ColorConverter.ConvertFromString("#307C3AED"),
                135),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#507C3AED")),
            BorderThickness = new Thickness(1)
        };

        var stack = new StackPanel();

        // Title row with emoji and discount
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock
        {
            Text = "🎉",
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        titleRow.Children.Add(new TextBlock
        {
            Text = $"Early Bird Discount: ลด {earlyBird.DiscountPercent}%!",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")),
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(titleRow);

        // Message
        stack.Children.Add(new TextBlock
        {
            Text = earlyBird.Message,
            FontSize = 12,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 8)
        });

        // Countdown / Days remaining
        var daysText = new TextBlock
        {
            Text = $"⏰ เหลือเวลาอีก {earlyBird.DaysRemaining} วัน เท่านั้น!",
            FontSize = 11,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
            FontWeight = FontWeights.SemiBold
        };
        stack.Children.Add(daysText);

        // Savings highlight
        var savingsStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        savingsStack.Children.Add(new TextBlock
        {
            Text = "💰 Lifetime License: ",
            FontSize = 12,
            Foreground = Brushes.White
        });
        savingsStack.Children.Add(new TextBlock
        {
            Text = "฿4,990",
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#808080")),
            TextDecorations = TextDecorations.Strikethrough
        });
        savingsStack.Children.Add(new TextBlock
        {
            Text = " → ฿3,992 (ประหยัด ฿998!)",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"))
        });
        stack.Children.Add(savingsStack);

        banner.Child = stack;
        banner.MouseLeftButtonUp += (s, e) => onBuyClick();
        banner.Cursor = Cursors.Hand;

        return banner;
    }

    /// <summary>
    /// Buy License button click handler - Opens LicenseDialog
    /// </summary>
    private void BuyLicenseButton_Click(object sender, MouseButtonEventArgs e)
    {
        ShowLicenseDialog();
    }

    /// <summary>
    /// Show the License Dialog for activation/purchase
    /// </summary>
    private void ShowLicenseDialog()
    {
        try
        {
            _logger?.LogInfo("UI", "Opening License Dialog");
            var licenseDialog = new LicenseDialog
            {
                Owner = this
            };
            var result = licenseDialog.ShowDialog();

            // Refresh UI after dialog closes
            if (result == true || licenseDialog.LicenseActivated)
            {
                _logger?.LogInfo("UI", "License activated, refreshing UI");
                UpdateModeIndicator();
                UpdateEditionDisplay();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("UI", $"Error showing license dialog: {ex.Message}");
        }
    }

    /// <summary>
    /// Update the visibility of Buy License button based on license status
    /// </summary>
    private void UpdateBuyButtonVisibility()
    {
        try
        {
            if (BuyLicenseButton != null)
            {
                // Hide buy button if licensed
                var isLicensed = _licenseService?.IsLicensed ?? false;
                BuyLicenseButton.Visibility = isLicensed
                    ? Visibility.Collapsed
                    : Visibility.Visible;

                // Show Early Bird discount text if not licensed (trial or demo)
                if (!isLicensed && EarlyBirdText != null)
                {
                    var license = _licenseService?.CurrentLicense;
                    var earlyBird = license?.EarlyBird;
                    var trialDays = _licenseService?.GetTrialDaysRemaining() ?? 0;

                    // Show Early Bird if:
                    // 1. Server sent EarlyBird info and eligible, OR
                    // 2. User is in trial period (all trial users get Early Bird by default)
                    if ((earlyBird != null && earlyBird.Eligible) ||
                        (license?.Status == LicenseStatus.Trial && trialDays > 0))
                    {
                        var discountPercent = earlyBird?.DiscountPercent ?? 20; // Default 20%
                        EarlyBirdText.Text = trialDays > 0
                            ? $"ลด {discountPercent}%! เหลือ {trialDays} วัน"
                            : $"ลด {discountPercent}%!";
                        EarlyBirdText.Visibility = Visibility.Visible;
                        _logger?.LogInfo("UI", $"Early Bird displayed: {discountPercent}% off, {trialDays} days remaining");
                    }
                    else
                    {
                        EarlyBirdText.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("UI", $"Error updating buy button: {ex.Message}");
        }
    }

    #endregion

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

            // Log trade completion
            _logger?.LogInfo("UI", $"Trade completed - Profit: {e.Result.NetPnL >= 0}");
        });
    }

    private void BalancePool_BalanceUpdated(object? sender, BalanceUpdateEventArgs e)
    {
        // Note: Balance UI updates are now handled by DashboardPage
        // This handler is kept for status bar P&L update only
        Dispatcher.Invoke(() =>
        {
            _todayPnL = e.PnL.TotalPnLUSDT;
        });
    }

    private void BalancePool_EmergencyTriggered(object? sender, EmergencyEventArgs e)
    {
        // Note: Emergency UI updates are now handled by DashboardPage
        // This handler is kept for critical emergency actions only
        Dispatcher.Invoke(() =>
        {
            var check = e.Check;

            // Auto-pause on critical
            if (check.RecommendedAction == EmergencyAction.StopTrading ||
                check.RecommendedAction == EmergencyAction.PauseTrading)
            {
                _arbEngine?.Pause();
                _logger?.LogCritical("Emergency", $"Trading paused: {check.Message}");
            }
        });
    }

    private void UpdateTradeStatsUI(TradeResult result)
    {
        // Note: Trade stats UI updates are now handled by DashboardPage
        // This method is kept for internal state tracking only
        _logger?.LogInfo("Trade", $"Trade stats updated - P&L: ${_todayPnL:F2}, Trades: {_todayTradeCount}");
    }

    private void ArbEngine_OpportunityFound(object? sender, OpportunityEventArgs e)
    {
        // Note: Opportunity UI updates are now handled by DashboardPage
        // This handler is kept for internal state tracking only
        Dispatcher.Invoke(() =>
        {
            // Track spread history internally
            _spreadHistory.Add(e.Opportunity.NetSpreadPercentage);
            if (_spreadHistory.Count > MaxSpreadHistory)
                _spreadHistory.RemoveAt(0);

            _logger?.LogInfo("Opportunity", $"Spread: {e.Opportunity.NetSpreadPercentage:F2}%");
        });
    }

    private void ArbEngine_PriceUpdated(object? sender, PriceUpdateEventArgs e)
    {
        // Note: Price UI updates are now handled by DashboardPage
        // This handler is kept for internal state tracking only
        Dispatcher.Invoke(() =>
        {
            _scanCount++;
            var isBinance = e.Exchange.Contains("A") || e.Exchange.ToLower().Contains("binance");

            if (isBinance)
            {
                _lastBinancePrice = e.Ticker.LastPrice;
            }
            else
            {
                _lastKuCoinPrice = e.Ticker.LastPrice;
            }
        });
    }

    private void ArbEngine_ErrorOccurred(object? sender, EngineErrorEventArgs e)
    {
        // Note: Error UI updates are now handled by DashboardPage
        Dispatcher.Invoke(() =>
        {
            _logger?.LogError("Engine", $"Error: {e.Message}");
        });
    }

    #endregion

    #region UI Update Methods

    private void UpdateBotStatusUI(EngineStatus status, string? message)
    {
        // Note: Bot status UI updates are now handled by DashboardPage
        // This method updates the status bar only
        var (statusText, statusColor) = status switch
        {
            EngineStatus.Running => ("RUNNING", "#10B981"),
            EngineStatus.Paused => ("PAUSED", "#F59E0B"),
            EngineStatus.Error => ("ERROR", "#EF4444"),
            _ => ("IDLE", "#808080")
        };

        // Update status bar
        if (StatusBarBotStatus != null)
        {
            StatusBarBotStatus.Text = statusText;
            StatusBarBotStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(statusColor));
        }
        if (StatusBarBotDot != null)
            StatusBarBotDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(statusColor));

        _logger?.LogInfo("UI", $"Bot status: {status} - {message}");
    }

    #endregion

    #region Connection Status

    private void ConnectionStatus_Changed(object? sender, ConnectionStatusChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _canStartTrading = _connectionStatusService?.CanStartTrading ?? false;
            UpdateConnectionStatusUI(e.Status);
        });
    }

    private void UpdateConnectionStatusUI(ConnectionStatusSnapshot status)
    {
        // Update status bar connection indicator
        if (StatusBarConnectionText != null)
        {
            var connectedCount = status.ConnectedExchangeCount;
            var totalExchanges = 2; // Exchange A and B

            StatusBarConnectionText.Text = connectedCount > 0
                ? $"{connectedCount}/{totalExchanges} Connected"
                : "Not Connected";

            var color = status.OverallHealth switch
            {
                ConnectionHealth.Connected => "#10B981",
                ConnectionHealth.Partial => "#F59E0B",
                _ => "#EF4444"
            };
            StatusBarConnectionText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }

        if (StatusBarConnectionDot != null)
        {
            var color = status.OverallHealth switch
            {
                ConnectionHealth.Connected => "#10B981",
                ConnectionHealth.Partial => "#F59E0B",
                _ => "#EF4444"
            };
            StatusBarConnectionDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }

        // Update exchange count
        if (StatusBarExchangeCount != null)
        {
            StatusBarExchangeCount.Text = $" • {status.Exchanges.Count} Exchanges";
        }

        _logger?.LogInfo("Connection", $"Status updated: {status.ConnectedExchangeCount} exchanges connected");
    }

    private async Task CheckConnectionsAndUpdateUIAsync()
    {
        if (_connectionStatusService == null) return;

        var status = await _connectionStatusService.CheckAllConnectionsAsync();
        _canStartTrading = _connectionStatusService.CanStartTrading;
        UpdateConnectionStatusUI(status);
    }

    #endregion

    #region Bot Control

    private async void StartBot_Click(object sender, RoutedEventArgs e)
    {
        if (_arbEngine == null || _isBotRunning)
            return;

        // Check Demo Mode - Block real trading
        if (_licenseService is LicenseService licenseService && licenseService.IsDemoMode)
        {
            // Fetch demo mode config but don't use it directly here (for logging only)
            var demoConfig = licenseService.GetDemoModeConfig();
            _ = demoConfig; // Suppress unused warning

            var result = MessageBox.Show(
                "คุณกำลังใช้งาน Demo Mode\n\n" +
                "⚠️ ไม่สามารถเทรดจริงได้ในโหมดนี้\n" +
                "กรุณา Activate License เพื่อใช้งานการเทรด\n\n" +
                "ต้องการไป Activate License หรือไม่?",
                "Demo Mode - ไม่สามารถเทรดได้",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                NavigateToPage("Settings");
            }

            _logger?.LogWarning("Bot", "Start blocked - Demo Mode active, real trading not allowed");
            return;
        }

        // Check if licensed and action is allowed
        if (_licenseService is LicenseService ls && !ls.IsActionAllowed("execute_trade"))
        {
            MessageBox.Show(
                "ไม่สามารถเริ่มเทรดได้\n\n" +
                "License ของคุณไม่อนุญาตให้ทำการเทรด\n" +
                "กรุณาตรวจสอบสถานะ License",
                "License Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _logger?.LogWarning("Bot", "Start blocked - License does not allow trading");
            return;
        }

        // Check prerequisites before starting
        if (_connectionStatusService != null)
        {
            var missingPrereqs = _connectionStatusService.GetMissingPrerequisites();
            if (missingPrereqs.Count > 0)
            {
                var message = "ไม่สามารถเริ่มเทรดได้ กรุณาตั้งค่าก่อน:\n\n" +
                              string.Join("\n", missingPrereqs.Select(p => "• " + p)) +
                              "\n\nไปที่หน้า Settings เพื่อตั้งค่า API Key";

                MessageBox.Show(message, "กรุณาตั้งค่า Exchange ก่อน",
                    MessageBoxButton.OK, MessageBoxImage.Warning);

                _logger?.LogWarning("Bot", $"Start blocked - missing prerequisites: {string.Join(", ", missingPrereqs)}");
                return;
            }
        }

        // Verify API connections are valid (authenticated)
        var config = _configService?.GetConfig();
        if (config?.General.LiveTrading == true)
        {
            var result = MessageBox.Show(
                "คุณกำลังจะเริ่ม LIVE TRADING\n\n" +
                "⚠️ จะใช้เงินจริงในการเทรด\n" +
                "ตรวจสอบว่า API Key มีสิทธิ์ถูกต้อง\n\n" +
                "ต้องการดำเนินการต่อหรือไม่?",
                "ยืนยันการเทรดจริง",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                _logger?.LogInfo("Bot", "Live trading start cancelled by user");
                return;
            }
        }

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

    private async void InitializeMainApp()
    {
        try
        {
            // Set default tab
            SetActiveTab(DashboardTab);

            // Update edition text based on license status
            UpdateEditionDisplay();

            // Update mode indicator based on config
            UpdateModeIndicator();

            // Initialize UI state
            UpdateBotStatusUI(EngineStatus.Idle, "Ready to start");

            // Check API connections and show status
            await CheckConnectionsAndUpdateUIAsync();

            // Start connection monitoring (check every 60 seconds)
            _connectionStatusService?.StartMonitoring(TimeSpan.FromSeconds(60));

            // Load dashboard stats from history
            LoadDashboardStats();

            // Start AI Scanner
            StartAIScanner();

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

            // Log startup status
            var config = _configService?.GetConfig();
            var mode = config?.General.LiveTrading == true ? "LIVE" : "DEMO";
            _logger?.LogInfo("App", $"AutoTrade-X started in {mode} mode");

            if (!_canStartTrading)
            {
                var missing = _connectionStatusService?.GetMissingPrerequisites() ?? new List<string>();
                if (missing.Count > 0)
                {
                    _logger?.LogWarning("App", $"Not ready to trade: {string.Join(", ", missing)}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"InitializeMainApp error: {ex.Message}");
        }
    }

    private void UpdateModeIndicator()
    {
        try
        {
            var config = _configService?.GetConfig();
            var isLive = config?.General.LiveTrading ?? false;
            var isLicensed = _licenseService?.IsLicensed ?? false;
            var license = _licenseService?.CurrentLicense;

            // Check if licensed with lifetime
            if (isLicensed && license != null && license.IsLifetime)
            {
                // Show golden LIFETIME badge
                if (ModeIndicator != null)
                {
                    ModeIndicator.Background = new LinearGradientBrush(
                        (Color)ColorConverter.ConvertFromString("#FFD700"), // Gold
                        (Color)ColorConverter.ConvertFromString("#DAA520"), // GoldenRod
                        0);
                }

                if (ModeText != null)
                {
                    ModeText.Text = "LIFETIME";
                    ModeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A0A2E")); // Dark text
                }

                if (ModeDetail != null)
                {
                    ModeDetail.Text = " • PRO";
                    ModeDetail.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A0A2E"));
                }

                _logger?.LogInfo("UI", "License mode: LIFETIME (golden badge)");
            }
            // Check if licensed (monthly/yearly)
            else if (isLicensed && license != null)
            {
                // Show licensed badge (purple/blue)
                var licenseTypeText = license.LicenseType?.ToUpperInvariant() switch
                {
                    "YEARLY" => "YEARLY",
                    "MONTHLY" => "MONTHLY",
                    _ => "LICENSED"
                };

                if (ModeIndicator != null)
                {
                    ModeIndicator.Background = new LinearGradientBrush(
                        (Color)ColorConverter.ConvertFromString("#7C3AED"),
                        (Color)ColorConverter.ConvertFromString("#2563EB"),
                        0);
                }

                if (ModeText != null)
                {
                    ModeText.Text = licenseTypeText;
                    ModeText.Foreground = Brushes.White;
                }

                if (ModeDetail != null)
                {
                    ModeDetail.Text = " • PRO";
                    ModeDetail.Foreground = Brushes.White;
                }

                _logger?.LogInfo("UI", $"License mode: {licenseTypeText}");
            }
            else
            {
                // Demo/Trial mode - show orange badge
                if (ModeIndicator != null)
                {
                    ModeIndicator.Background = new LinearGradientBrush(
                        (Color)ColorConverter.ConvertFromString("#F59E0B"),
                        (Color)ColorConverter.ConvertFromString("#D97706"),
                        0);
                }

                if (ModeText != null)
                {
                    ModeText.Text = "DEMO MODE";
                    ModeText.Foreground = Brushes.White;
                }

                if (ModeDetail != null)
                {
                    ModeDetail.Text = " • $10,000";
                    ModeDetail.Foreground = Brushes.White;
                }

                _logger?.LogInfo("UI", $"Trading mode: {(isLive ? "LIVE" : "DEMO")} (not licensed)");
            }

            // Update Buy License button visibility
            UpdateBuyButtonVisibility();
        }
        catch (Exception ex)
        {
            _logger?.LogError("UI", $"Error updating mode indicator: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the edition text based on license status
    /// Demo Version = Not licensed (trial or no license)
    /// Pro Edition = Licensed
    /// </summary>
    private void UpdateEditionDisplay()
    {
        if (EditionText == null) return;

        var isLicensed = _licenseService?.IsLicensed ?? false;
        var license = _licenseService?.CurrentLicense;

        if (isLicensed && license != null && license.Status == LicenseStatus.Valid)
        {
            // Licensed - Show Pro Edition with tier
            var tierText = license.Tier switch
            {
                LicenseTier.Pro => "PRO EDITION",
                LicenseTier.Enterprise => "ENTERPRISE EDITION",
                _ => "LICENSED"
            };
            EditionText.Text = $"by Xman Studio • {tierText}";
            EditionText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")); // Gold
            _logger?.LogInfo("License", $"Running with {tierText}");
        }
        else
        {
            // Not licensed or trial - Show Demo Version
            EditionText.Text = "by Xman Studio • DEMO VERSION";
            EditionText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#808080")); // Gray
            _logger?.LogInfo("License", "Running in DEMO VERSION (trial or unlicensed)");
        }

        // Update Buy License button visibility
        UpdateBuyButtonVisibility();
    }

    /// <summary>
    /// Checks if the app is running in demo mode (not licensed)
    /// In demo mode, real wallet access is blocked
    /// </summary>
    public bool IsDemoMode => !(_licenseService?.IsLicensed ?? false) ||
                               _licenseService?.CurrentLicense?.Status != LicenseStatus.Valid;

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
        // Updated for 600x500 splash canvas (center at 300, 250)
        var targets = new (double x, double y)[]
        {
            (0, 0), (600, 0), (0, 500), (600, 500),
            (300, 0), (300, 500), (0, 250), (600, 250)
        };

        var target = targets[_hyperRandom.Next(targets.Length)];
        var duration = TimeSpan.FromMilliseconds(400 + _hyperRandom.Next(300));

        star.SetValue(Canvas.LeftProperty, 300.0);
        star.SetValue(Canvas.TopProperty, 250.0);

        var storyboard = new Storyboard();

        var moveX = new DoubleAnimation(300, target.x, duration);
        var moveY = new DoubleAnimation(250, target.y, duration);
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

        // Fade out loading panel first
        var fadeOutLoading = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
        LoadingPanel.BeginAnimation(OpacityProperty, fadeOutLoading);

        await Task.Delay(300);

        // Simple fade out animation (no shrink/move)
        var duration = TimeSpan.FromMilliseconds(1000);
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };

        // Fade out the splash content smoothly
        var fadeOutContent = new DoubleAnimation(1, 0, duration) { EasingFunction = easing };
        SplashContent.BeginAnimation(OpacityProperty, fadeOutContent);

        // Fade out splash overlay at the same time
        var fadeOutOverlay = new DoubleAnimation(1, 0, duration) { EasingFunction = easing };
        SplashOverlay.BeginAnimation(OpacityProperty, fadeOutOverlay);

        await Task.Delay(1000);

        // Hide splash completely
        SplashOverlay.Visibility = Visibility.Collapsed;
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
        // Note: Price UI updates are now handled by DashboardPage
        // This method is kept for status bar updates only
        if (_coinDataService == null) return;

        try
        {
            var btcPrice = await _coinDataService.GetPriceAsync("bitcoin");
            if (btcPrice > 0)
            {
                _lastBinancePrice = btcPrice;
                _lastKuCoinPrice = btcPrice * (1 - 0.0015m);
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
        else if (e.LeftButton == MouseButtonState.Pressed)
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
        }
        else
        {
            // Desktop/tablet mode - show status bar
            if (StatusBarCenter != null)
                StatusBarCenter.Visibility = Visibility.Visible;
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

    private void StrategyTab_Click(object sender, MouseButtonEventArgs e)
    {
        SetActiveTab(StrategyTab);
        ShowPage("Strategy");
    }

    private void ProjectsTab_Click(object sender, MouseButtonEventArgs e)
    {
        SetActiveTab(ProjectsTab);
        ShowPage("Projects");
    }

    private void AnalyticsTab_Click(object sender, MouseButtonEventArgs e)
    {
        SetActiveTab(AnalyticsTab);
        ShowPage("Analytics");
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
        StrategyContent.Visibility = Visibility.Collapsed;
        ProjectsContent.Visibility = Visibility.Collapsed;
        AnalyticsContent.Visibility = Visibility.Collapsed;
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
            case "Strategy":
                StrategyContent.Visibility = Visibility.Visible;
                break;
            case "Projects":
                ProjectsContent.Visibility = Visibility.Visible;
                break;
            case "Analytics":
                AnalyticsContent.Visibility = Visibility.Visible;
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

    /// <summary>
    /// Public method to navigate to a page from other components
    /// </summary>
    public void NavigateToPage(string pageName)
    {
        // Find and set the corresponding tab
        Border? tab = pageName switch
        {
            "Dashboard" => DashboardTab,
            "Trading" => TradingTab,
            "Scanner" => ScannerTab,
            "History" => HistoryTab,
            "Strategy" => StrategyTab,
            "Projects" => ProjectsTab,
            "Analytics" => AnalyticsTab,
            "Settings" => SettingsTab,
            _ => null
        };

        if (tab != null)
        {
            SetActiveTab(tab);
        }

        ShowPage(pageName);
    }

    #endregion

    #region Dashboard Data Methods

    private void UpdateRecentTradesDisplay()
    {
        // Note: Recent trades display is now handled by DashboardPage
        // This method is kept for internal state tracking only
    }

    private async void LoadDashboardStats()
    {
        // Note: Dashboard stats UI updates are now handled by DashboardPage
        // This method loads stats for internal tracking only
        if (_tradeHistory == null) return;

        try
        {
            var todayStats = await _tradeHistory.GetStatsAsync(DateTime.Today, DateTime.Now);

            _todayTradeCount = todayStats.TotalTrades;
            _successfulTrades = todayStats.WinningTrades;
            _todayPnL = todayStats.TotalPnL;

            _logger?.LogInfo("Stats", $"Loaded stats - P&L: ${_todayPnL:F2}, Trades: {_todayTradeCount}");
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
