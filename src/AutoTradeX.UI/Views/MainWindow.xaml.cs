using System.IO;
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
    private IConnectionStatusService? _connectionStatusService;  // Not readonly - may be set later in InitializeMainApp
    private readonly ILicenseService? _licenseService;
    private IApiCredentialsService? _apiCredentialsService;  // Not readonly - may be set later in InitializeMainApp
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
        _apiCredentialsService = App.Services?.GetService<IApiCredentialsService>();

        // NOTE: Connection Status subscription moved to MainWindow_Loaded
        // because App.Services may be null at this point (MainWindow is created before App.OnStartup)

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
        Closing += MainWindow_Closing;
    }

    /// <summary>
    /// Cleanup all event subscriptions and timers on window close
    /// ป้องกัน memory leak และ crash ขณะ shutdown
    /// </summary>
    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            // Stop all timers
            _priceUpdateTimer?.Stop();
            _aiScannerTimer?.Stop();
            _statusBarTimer?.Stop();

            // Cancel bot if running
            _botCancellationTokenSource?.Cancel();

            // Unsubscribe from ArbEngine events
            if (_arbEngine != null)
            {
                _arbEngine.StatusChanged -= ArbEngine_StatusChanged;
                _arbEngine.TradeCompleted -= ArbEngine_TradeCompleted;
                _arbEngine.OpportunityFound -= ArbEngine_OpportunityFound;
                _arbEngine.PriceUpdated -= ArbEngine_PriceUpdated;
                _arbEngine.ErrorOccurred -= ArbEngine_ErrorOccurred;
            }

            // Unsubscribe from BalancePool events
            if (_balancePool != null)
            {
                _balancePool.BalanceUpdated -= BalancePool_BalanceUpdated;
                _balancePool.EmergencyTriggered -= BalancePool_EmergencyTriggered;
            }

            // Unsubscribe from Notification events
            if (_notificationService != null)
            {
                _notificationService.NotificationReceived -= NotificationService_NotificationReceived;
            }

            // Unsubscribe from License events
            if (_licenseService is LicenseService licenseServiceImpl)
            {
                licenseServiceImpl.DemoModeReminder -= LicenseService_DemoModeReminder;
            }

            // Unsubscribe from ConnectionStatus events
            if (_connectionStatusService != null)
            {
                _connectionStatusService.ConnectionStatusChanged -= ConnectionStatus_Changed;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("MainWindow", $"Error during cleanup: {ex.Message}");
        }
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

                // Log activation button visibility
                _logger?.LogInfo("UI", $"Activate button visibility: {(isLicensed ? "Hidden" : "Visible")}");
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
        _logger?.LogInfo("MainWindow", $"=== ConnectionStatus_Changed EVENT RECEIVED ===");
        _logger?.LogInfo("MainWindow", $"  ChangedExchange: {e.ChangedExchange}");
        _logger?.LogInfo("MainWindow", $"  Message: {e.Message}");
        _logger?.LogInfo("MainWindow", $"  ConnectedExchangeCount: {e.Status.ConnectedExchangeCount}");
        _logger?.LogInfo("MainWindow", $"  Total Exchanges in Status: {e.Status.Exchanges.Count}");

        foreach (var ex in e.Status.Exchanges)
        {
            _logger?.LogInfo("MainWindow", $"    {ex.Key}: IsConnected={ex.Value.IsConnected}, HasValidCredentials={ex.Value.HasValidCredentials}");
        }

        Dispatcher.Invoke(() =>
        {
            _logger?.LogInfo("MainWindow", "Updating UI on dispatcher thread...");
            _canStartTrading = _connectionStatusService?.CanStartTrading ?? false;
            UpdateConnectionStatusUI(e.Status);
            _logger?.LogInfo("MainWindow", "UI update completed");

            // DEBUG: Show message box to confirm event was received
            System.Diagnostics.Debug.WriteLine($"[DEBUG] Event received for {e.ChangedExchange}! Connected: {e.Status.ConnectedExchangeCount}");
        });
    }

    private void UpdateConnectionStatusUI(ConnectionStatusSnapshot status)
    {
        // Update status bar with connected exchange logos
        UpdateConnectedExchangeLogos(status);

        _logger?.LogInfo("Connection", $"Status updated: {status.ConnectedExchangeCount} exchanges connected");
    }

    /// <summary>
    /// Update the status bar to show logos of connected exchanges
    /// อัพเดท status bar ให้แสดงโลโก้ของ exchange ที่เชื่อมต่อได้
    /// </summary>
    private void UpdateConnectedExchangeLogos(ConnectionStatusSnapshot status)
    {
        if (ConnectedExchangeLogos == null)
        {
            _logger?.LogWarning("MainWindow", "ConnectedExchangeLogos is null!");
            return;
        }

        ConnectedExchangeLogos.Children.Clear();

        _logger?.LogInfo("MainWindow", $"UpdateConnectedExchangeLogos: Total exchanges in status: {status.Exchanges.Count}");
        foreach (var ex in status.Exchanges)
        {
            _logger?.LogInfo("MainWindow", $"  - {ex.Key}: IsConnected={ex.Value.IsConnected}, HasValidCredentials={ex.Value.HasValidCredentials}");
        }

        var connectedExchanges = status.Exchanges
            .Where(e => e.Value.IsConnected && e.Value.HasValidCredentials)
            .Select(e => e.Key)
            .ToList();

        _logger?.LogInfo("MainWindow", $"Connected exchanges count: {connectedExchanges.Count}");

        if (connectedExchanges.Count == 0)
        {
            // Show "No exchanges connected" message
            if (NoConnectionText != null)
            {
                NoConnectionText.Visibility = Visibility.Visible;
            }
            return;
        }

        // Hide the no connection message
        if (NoConnectionText != null)
        {
            NoConnectionText.Visibility = Visibility.Collapsed;
        }

        // Add logo for each connected exchange
        foreach (var exchangeName in connectedExchanges)
        {
            var logoContainer = CreateExchangeLogoElement(exchangeName);
            if (logoContainer != null)
            {
                ConnectedExchangeLogos.Children.Add(logoContainer);
            }
        }
    }

    /// <summary>
    /// Create a small exchange logo element for the status bar
    /// สร้างโลโก้ exchange ขนาดเล็กสำหรับ status bar
    /// </summary>
    private Border? CreateExchangeLogoElement(string exchangeName)
    {
        try
        {
            var logoPath = GetExchangeLogoPath(exchangeName);
            if (string.IsNullOrEmpty(logoPath)) return null;

            var image = new Image
            {
                Width = 18,
                Height = 18,
                Stretch = System.Windows.Media.Stretch.Uniform,
                ToolTip = exchangeName
            };

            // Load logo from resources
            try
            {
                var uri = new Uri(logoPath, UriKind.RelativeOrAbsolute);
                image.Source = new System.Windows.Media.Imaging.BitmapImage(uri);
            }
            catch
            {
                // Fallback: show text initial
                return CreateTextLogoFallback(exchangeName);
            }

            var border = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20FFFFFF")),
                Margin = new Thickness(2, 0, 2, 0),
                ToolTip = $"{exchangeName} - Connected",
                Child = image,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Add green dot indicator
            var grid = new Grid();
            grid.Children.Add(border);

            var greenDot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -1, -1)
            };
            grid.Children.Add(greenDot);

            var containerBorder = new Border
            {
                Child = grid,
                Margin = new Thickness(1, 0, 1, 0)
            };

            return containerBorder;
        }
        catch
        {
            return CreateTextLogoFallback(exchangeName);
        }
    }

    /// <summary>
    /// Create a text-based fallback logo when image is not available
    /// </summary>
    private Border CreateTextLogoFallback(string exchangeName)
    {
        var initial = exchangeName.Length > 0 ? exchangeName[0].ToString().ToUpper() : "?";

        var textBlock = new TextBlock
        {
            Text = initial,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        return new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED")),
            Margin = new Thickness(2, 0, 2, 0),
            ToolTip = $"{exchangeName} - Connected",
            Child = textBlock,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    /// <summary>
    /// Get the logo path for an exchange
    /// </summary>
    private string GetExchangeLogoPath(string exchangeName)
    {
        var name = exchangeName.ToLower().Replace(".", "").Replace(" ", "");
        return $"pack://application:,,,/Assets/Exchanges/{name}.png";
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

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // CRITICAL: Wait for App.Services to be ready
            // App.OnStartup is async void, so WPF may create MainWindow before services are ready
            // รอให้ App.Services พร้อมก่อนเริ่ม - เพราะ OnStartup เป็น async void
            int waitCount = 0;
            while (App.Services == null && waitCount < 50) // Max 5 seconds
            {
                await Task.Delay(100);
                waitCount++;
            }

            if (App.Services == null)
            {
                _logger?.LogError("MainWindow", "App.Services is still null after waiting!");
                MessageBox.Show("ไม่สามารถเริ่มต้นระบบได้ กรุณาลองใหม่", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

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
            // CRITICAL: Re-get services now that App.Services is ready
            // MainWindow constructor runs before App.OnStartup, so services were null
            if (_connectionStatusService == null)
            {
                _connectionStatusService = App.Services?.GetService<IConnectionStatusService>();
                _logger?.LogInfo("MainWindow", $"Got ConnectionStatusService: {_connectionStatusService != null}");
            }
            if (_apiCredentialsService == null)
            {
                _apiCredentialsService = App.Services?.GetService<IApiCredentialsService>();
            }

            // Subscribe to Connection Status events NOW (after App.Services is ready)
            if (_connectionStatusService != null)
            {
                _connectionStatusService.ConnectionStatusChanged += ConnectionStatus_Changed;
                _logger?.LogInfo("MainWindow", "Subscribed to ConnectionStatusChanged event");
            }
            else
            {
                _logger?.LogError("MainWindow", "ConnectionStatusService is STILL null! Cannot subscribe to events.");
            }

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

            // CRITICAL: Tell DashboardPage to refresh exchange statuses
            // because it was created before Splash verified the connections
            // แจ้ง DashboardPage ให้ refresh status เพราะมันถูกสร้างก่อน Splash verify เสร็จ
            if (DashboardContent != null)
            {
                _logger?.LogInfo("MainWindow", "Notifying DashboardPage to refresh exchange statuses...");
                DashboardContent.RefreshExchangeStatuses();
            }

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

            // Animate loading progress (phase 1: initialization)
            _splashTimer = new System.Windows.Threading.DispatcherTimer();
            _splashTimer.Interval = TimeSpan.FromMilliseconds(50);
            _splashTimer.Tick += SplashTimer_Tick;
            _splashTimer.Start();

            // Wait for initial loading (phase 1)
            await Task.Delay(2000);

            // Phase 2: Connection check - integrated into splash
            _splashTimer?.Stop();
            await RunConnectionCheckInSplash();

            // Stop splash animations
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

    /// <summary>
    /// Run connection check integrated into splash screen
    /// ตรวจสอบ connection ภายใน splash screen
    /// </summary>
    private async Task RunConnectionCheckInSplash()
    {
        try
        {
            _logger?.LogInfo("Startup", "Starting connection check in splash...");

            // CRITICAL: Re-get services now that App.Services is ready
            // MainWindow constructor runs before App.OnStartup, so services were null
            // ต้อง re-get services เพราะ MainWindow constructor รันก่อน App.OnStartup
            if (_apiCredentialsService == null)
            {
                _apiCredentialsService = App.Services?.GetService<IApiCredentialsService>();
                _logger?.LogInfo("Startup", $"Re-got ApiCredentialsService: {_apiCredentialsService != null}");
            }
            if (_connectionStatusService == null)
            {
                _connectionStatusService = App.Services?.GetService<IConnectionStatusService>();
                _logger?.LogInfo("Startup", $"Re-got ConnectionStatusService: {_connectionStatusService != null}");
            }

            // Show connection check panel
            if (ConnectionCheckPanel != null)
            {
                ConnectionCheckPanel.Visibility = Visibility.Visible;
            }

            // Update loading text
            if (LoadingText != null)
            {
                LoadingText.Text = "Checking Exchange Connections...";
            }

            // Initialize exchange list for splash
            var exchanges = InitializeSplashExchangeList();
            if (SplashExchangeList != null)
            {
                SplashExchangeList.ItemsSource = exchanges;
            }

            int connectedCount = 0;
            int totalConfigured = 0;

            // Check each exchange
            foreach (var exchange in exchanges)
            {
                // Update to checking state
                exchange.Status = SplashCheckStatus.Checking;
                exchange.StatusMessage = "กำลังตรวจสอบ...";
                exchange.StatusIcon = "🔄";
                exchange.StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                exchange.IconBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30F59E0B"));

                await Task.Delay(100); // Small delay for UI update

                try
                {
                    // Check if credentials are configured - ใช้ database เป็นหลัก
                    bool hasCredentials = false;

                    if (_apiCredentialsService != null)
                    {
                        // ตรวจสอบจาก database โดยตรง
                        hasCredentials = await _apiCredentialsService.HasCredentialsAsync(exchange.Name);
                        _logger?.LogInfo("ConnectionCheck", $"{exchange.Name}: HasCredentials from DB = {hasCredentials}");

                        // ถ้ามี credentials ใน DB ให้ load เข้า env vars ก่อน test
                        if (hasCredentials)
                        {
                            var creds = await _apiCredentialsService.GetCredentialsAsync(exchange.Name);
                            if (creds != null)
                            {
                                SetExchangeEnvVars(exchange.Name, creds.ApiKey, creds.ApiSecret, creds.Passphrase);
                                _logger?.LogInfo("ConnectionCheck", $"{exchange.Name}: Loaded credentials to env vars");
                            }
                        }
                    }
                    else
                    {
                        // Fallback to old method if service not available
                        _logger?.LogWarning("ConnectionCheck", "ApiCredentialsService is NULL - using fallback");
                        hasCredentials = HasExchangeCredentials(exchange.Name);
                        _logger?.LogInfo("ConnectionCheck", $"{exchange.Name}: HasCredentials from legacy = {hasCredentials}");
                    }

                    if (!hasCredentials)
                    {
                        // Not configured
                        exchange.Status = SplashCheckStatus.NotConfigured;
                        exchange.StatusMessage = "ยังไม่ได้ตั้งค่า";
                        exchange.StatusIcon = "➖";
                        exchange.StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60FFFFFF"));
                        exchange.IconBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20FFFFFF"));

                        _logger?.LogInfo("ConnectionCheck", $"{exchange.Name}: Not configured");
                        continue;
                    }

                    totalConfigured++;

                    // Test actual connection
                    if (_connectionStatusService != null)
                    {
                        var status = await _connectionStatusService.CheckExchangeConnectionAsync(exchange.Name);

                        if (status.IsConnected && status.HasValidCredentials)
                        {
                            exchange.Status = SplashCheckStatus.Connected;
                            exchange.StatusMessage = $"เชื่อมต่อสำเร็จ ({status.Latency}ms)";
                            exchange.StatusIcon = "✓";
                            exchange.StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                            exchange.IconBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2010B981"));

                            connectedCount++;
                            _logger?.LogInfo("ConnectionCheck", $"{exchange.Name}: Connected ({status.Latency}ms)");

                            // Mark as verified
                            _connectionStatusService.MarkExchangeAsVerified(exchange.Name, status.CanTrade);
                        }
                        else
                        {
                            exchange.Status = SplashCheckStatus.Failed;
                            exchange.StatusMessage = status.ErrorMessage ?? "เชื่อมต่อล้มเหลว";
                            exchange.StatusIcon = "✗";
                            exchange.StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                            exchange.IconBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20EF4444"));

                            _logger?.LogWarning("ConnectionCheck", $"{exchange.Name}: Failed - {status.ErrorMessage}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    exchange.Status = SplashCheckStatus.Failed;
                    exchange.StatusMessage = ex.Message.Length > 30 ? ex.Message.Substring(0, 30) + "..." : ex.Message;
                    exchange.StatusIcon = "✗";
                    exchange.StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                    exchange.IconBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20EF4444"));

                    _logger?.LogError("ConnectionCheck", $"{exchange.Name}: Error - {ex.Message}");
                }
            }

            // Update summary
            if (ConnectionSummary != null)
            {
                if (totalConfigured == 0)
                {
                    ConnectionSummary.Text = "ไม่พบ API Key - กรุณาตั้งค่าใน Settings";
                }
                else if (connectedCount == totalConfigured)
                {
                    ConnectionSummary.Text = $"✓ เชื่อมต่อสำเร็จ {connectedCount} Exchange";
                    ConnectionSummary.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                }
                else if (connectedCount > 0)
                {
                    ConnectionSummary.Text = $"เชื่อมต่อได้ {connectedCount}/{totalConfigured} Exchange";
                }
                else
                {
                    ConnectionSummary.Text = $"ไม่สามารถเชื่อมต่อ Exchange ได้";
                    ConnectionSummary.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                }
            }

            // Update progress bar to 100%
            if (LoadingProgress != null)
            {
                LoadingProgress.Width = 300;
            }

            if (LoadingText != null)
            {
                LoadingText.Text = connectedCount > 0 ? "พร้อมใช้งาน!" : "กรุณาตั้งค่า API Key";
            }

            _logger?.LogInfo("ConnectionCheck", $"Check complete: {connectedCount}/{totalConfigured} connected");

            // Wait a moment before transitioning
            await Task.Delay(1500);
        }
        catch (Exception ex)
        {
            _logger?.LogError("Startup", $"Connection check error: {ex.Message}");
        }
    }

    /// <summary>
    /// Check if exchange has credentials configured (env vars OR saved file)
    /// ตรวจสอบว่า exchange มี credentials หรือไม่ (จาก env vars หรือ saved file)
    /// </summary>
    private bool HasExchangeCredentials(string exchangeName)
    {
        // Check environment variables first
        var (keyEnv, secretEnv) = GetExchangeEnvVarNamesForCheck(exchangeName);
        var apiKey = Environment.GetEnvironmentVariable(keyEnv);
        var apiSecret = Environment.GetEnvironmentVariable(secretEnv);

        if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(apiSecret))
        {
            return true;
        }

        // Also check saved credentials file (for credentials saved in Settings)
        // ไฟล์ credentials ถูกบันทึกที่ credentials.encrypted.json
        try
        {
            var credentialsPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AutoTradeX",
                "credentials.encrypted.json");

            if (File.Exists(credentialsPath))
            {
                var json = File.ReadAllText(credentialsPath);
                var creds = System.Text.Json.JsonSerializer.Deserialize<StartupSavedCredentials>(json);

                if (creds != null)
                {
                    // Get credentials based on exchange name
                    var (savedKey, savedSecret, savedPassphrase) = exchangeName.ToLower() switch
                    {
                        "binance" => (creds.BinanceApiKey, creds.BinanceApiSecret, (string?)null),
                        "kucoin" => (creds.KuCoinApiKey, creds.KuCoinApiSecret, creds.KuCoinPassphrase),
                        "okx" => (creds.OKXApiKey, creds.OKXApiSecret, creds.OKXPassphrase),
                        "bybit" => (creds.BybitApiKey, creds.BybitApiSecret, (string?)null),
                        "gate.io" => (creds.GateIOApiKey, creds.GateIOApiSecret, (string?)null),
                        "bitkub" => (creds.BitkubApiKey, creds.BitkubApiSecret, (string?)null),
                        _ => (null, null, null)
                    };

                    if (!string.IsNullOrEmpty(savedKey) && !string.IsNullOrEmpty(savedSecret))
                    {
                        // Set to environment variables for this session
                        Environment.SetEnvironmentVariable(keyEnv, savedKey);
                        Environment.SetEnvironmentVariable(secretEnv, savedSecret);

                        // Handle passphrase for OKX/KuCoin
                        if (!string.IsNullOrEmpty(savedPassphrase))
                        {
                            if (exchangeName.ToLower() == "okx")
                            {
                                Environment.SetEnvironmentVariable("AUTOTRADEX_OKX_PASSPHRASE", savedPassphrase);
                            }
                            else if (exchangeName.ToLower() == "kucoin")
                            {
                                Environment.SetEnvironmentVariable("AUTOTRADEX_KUCOIN_API_KEY_PASSPHRASE", savedPassphrase);
                            }
                        }

                        _logger?.LogInfo("Startup", $"{exchangeName}: Loaded credentials from saved file");
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Startup", $"Error reading saved credentials: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Get environment variable names for exchange credentials
    /// </summary>
    private static (string keyEnv, string secretEnv) GetExchangeEnvVarNamesForCheck(string exchangeName)
    {
        return exchangeName.ToLower() switch
        {
            "binance" => ("AUTOTRADEX_BINANCE_API_KEY", "AUTOTRADEX_BINANCE_API_SECRET"),
            "kucoin" => ("AUTOTRADEX_KUCOIN_API_KEY", "AUTOTRADEX_KUCOIN_API_SECRET"),
            "okx" => ("AUTOTRADEX_OKX_API_KEY", "AUTOTRADEX_OKX_API_SECRET"),
            "bybit" => ("AUTOTRADEX_BYBIT_API_KEY", "AUTOTRADEX_BYBIT_API_SECRET"),
            "gate.io" => ("AUTOTRADEX_GATEIO_API_KEY", "AUTOTRADEX_GATEIO_API_SECRET"),
            "bitkub" => ("AUTOTRADEX_BITKUB_API_KEY", "AUTOTRADEX_BITKUB_API_SECRET"),
            _ => ($"AUTOTRADEX_{exchangeName.ToUpper().Replace(".", "")}_API_KEY",
                  $"AUTOTRADEX_{exchangeName.ToUpper().Replace(".", "")}_API_SECRET")
        };
    }

    /// <summary>
    /// Set environment variables for exchange credentials
    /// ตั้งค่า environment variables สำหรับ credentials ของ exchange
    /// </summary>
    private static void SetExchangeEnvVars(string exchangeName, string apiKey, string apiSecret, string? passphrase)
    {
        var prefix = $"AUTOTRADEX_{exchangeName.ToUpper().Replace(".", "").Replace(" ", "")}";

        if (!string.IsNullOrEmpty(apiKey))
            Environment.SetEnvironmentVariable($"{prefix}_API_KEY", apiKey);

        if (!string.IsNullOrEmpty(apiSecret))
            Environment.SetEnvironmentVariable($"{prefix}_API_SECRET", apiSecret);

        if (!string.IsNullOrEmpty(passphrase))
        {
            var passEnv = exchangeName.ToLower() == "okx"
                ? "AUTOTRADEX_OKX_PASSPHRASE"
                : $"{prefix}_API_KEY_PASSPHRASE";
            Environment.SetEnvironmentVariable(passEnv, passphrase);
        }
    }

    /// <summary>
    /// Initialize splash exchange list with all supported exchanges
    /// </summary>
    private System.Collections.ObjectModel.ObservableCollection<SplashExchangeItem> InitializeSplashExchangeList()
    {
        var exchanges = new System.Collections.ObjectModel.ObservableCollection<SplashExchangeItem>();
        var supportedExchanges = new[] { "Binance", "KuCoin", "OKX", "Bybit", "Gate.io", "Bitkub" };

        foreach (var name in supportedExchanges)
        {
            var logoName = name.ToLower().Replace(".", "").Replace(" ", "");
            exchanges.Add(new SplashExchangeItem
            {
                Name = name,
                LogoPath = $"pack://application:,,,/Assets/Exchanges/{logoName}.png",
                Status = SplashCheckStatus.Pending,
                StatusMessage = "รอตรวจสอบ...",
                StatusIcon = "⏳",
                StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80FFFFFF")),
                IconBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30FFFFFF"))
            });
        }

        return exchanges;
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
        catch (Exception ex)
        {
            _logger?.LogError("MainWindow", $"UpdateStatusBar error: {ex.Message}");
        }
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
        // Update interval to 5 seconds after initial load for near real-time updates
        _priceUpdateTimer!.Interval = TimeSpan.FromSeconds(5);
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
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            // Restore border when normal
            if (MainBorder != null)
                MainBorder.BorderThickness = new Thickness(1);
        }
        else
        {
            WindowState = WindowState.Maximized;
            // Remove border when maximized (avoids edge artifacts)
            if (MainBorder != null)
                MainBorder.BorderThickness = new Thickness(0);
        }
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
            catch (Exception ex)
            {
                _logger?.LogError("MainWindow", $"Error stopping ArbEngine on close: {ex.Message}");
            }
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

    private void AITradingTab_Click(object sender, MouseButtonEventArgs e)
    {
        SetActiveTab(AITradingTab);
        ShowPage("AITrading");
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
        AITradingContent.Visibility = Visibility.Collapsed;
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
            case "AITrading":
                AITradingContent.Visibility = Visibility.Visible;
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
            "AITrading" => AITradingTab,
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

/// <summary>
/// Status for splash screen connection check
/// </summary>
public enum SplashCheckStatus
{
    Pending,
    Checking,
    Connected,
    Failed,
    NotConfigured
}

/// <summary>
/// Model for splash screen exchange connection check
/// </summary>
public class SplashExchangeItem : System.ComponentModel.INotifyPropertyChanged
{
    private string _name = "";
    private string _logoPath = "";
    private SplashCheckStatus _status;
    private string _statusMessage = "";
    private string _statusIcon = "";
    private SolidColorBrush _statusColor = Brushes.Gray;
    private SolidColorBrush _iconBackground = Brushes.Gray;

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(nameof(Name)); }
    }

    public string LogoPath
    {
        get => _logoPath;
        set { _logoPath = value; OnPropertyChanged(nameof(LogoPath)); }
    }

    public SplashCheckStatus Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(nameof(Status)); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); }
    }

    public string StatusIcon
    {
        get => _statusIcon;
        set { _statusIcon = value; OnPropertyChanged(nameof(StatusIcon)); }
    }

    public SolidColorBrush StatusColor
    {
        get => _statusColor;
        set { _statusColor = value; OnPropertyChanged(nameof(StatusColor)); }
    }

    public SolidColorBrush IconBackground
    {
        get => _iconBackground;
        set { _iconBackground = value; OnPropertyChanged(nameof(IconBackground)); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Model for reading saved credentials from file (matching SettingsPage SavedCredentials)
/// </summary>
public class StartupSavedCredentials
{
    public string? BinanceApiKey { get; set; }
    public string? BinanceApiSecret { get; set; }
    public string? KuCoinApiKey { get; set; }
    public string? KuCoinApiSecret { get; set; }
    public string? KuCoinPassphrase { get; set; }
    public string? BitkubApiKey { get; set; }
    public string? BitkubApiSecret { get; set; }
    public string? OKXApiKey { get; set; }
    public string? OKXApiSecret { get; set; }
    public string? OKXPassphrase { get; set; }
    public string? BybitApiKey { get; set; }
    public string? BybitApiSecret { get; set; }
    public string? GateIOApiKey { get; set; }
    public string? GateIOApiSecret { get; set; }
}
