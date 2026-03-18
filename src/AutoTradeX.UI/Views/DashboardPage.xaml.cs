/*
 * ============================================================================
 * AutoTrade-X - Production-Quality Dashboard Page
 * ============================================================================
 * Premium dashboard with real-time data, live charts, system health monitoring
 * ============================================================================
 */

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.Defaults;
using SkiaSharp;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using AutoTradeX.Infrastructure.Services;

namespace AutoTradeX.UI.Views;

public partial class DashboardPage : UserControl
{
    // Services (not readonly because they may need to be re-fetched after App.Services is ready)
    // Services ไม่ใช่ readonly เพราะอาจต้อง re-fetch หลังจาก App.Services พร้อม
    private ICoinDataService? _coinDataService;
    private ILoggingService? _logger;
    private IArbEngine? _arbEngine;
    private IConfigService? _configService;
    private IBalancePoolService? _balancePool;
    private ITradeHistoryService? _tradeHistory;
    private INotificationService? _notificationService;
    private IExchangeClientFactory? _exchangeFactory;
    private IProjectService? _projectService;
    private IConnectionStatusService? _connectionStatusService;

    // Timers
    private DispatcherTimer? _refreshTimer;
    private DispatcherTimer? _chartUpdateTimer;
    private DispatcherTimer? _connectionRefreshTimer;

    // State
    private bool _isBotRunning = false;
    private bool _isBotPaused = false;
    private int _scanCount = 0;
    private decimal _lastSpread = 0;

    // Chart Data
    private readonly ObservableCollection<DateTimePoint> _pnlChartData = new();
    private readonly ObservableCollection<double> _spreadHistoryData = new();
    private readonly ObservableCollection<DateTimePoint> _bestPairChartData = new();
    private readonly ObservableCollection<DateTimePoint> _worstPairChartData = new();
    private const int MaxSpreadHistory = 20;

    // Best/Worst Pair tracking
    private string _bestPairSymbol = "";
    private string _worstPairSymbol = "";

    // View Models
    public ObservableCollection<ExchangeStatusItem> ExchangeStatuses { get; } = new();
    public ObservableCollection<MarketOverviewItem> MarketItems { get; } = new();
    public ObservableCollection<TradingPairItem> ActivePairs { get; } = new();
    public ObservableCollection<RecentTradeItem> RecentTrades { get; } = new();
    public ObservableCollection<ActivityLogItem> ActivityLogs { get; } = new();
    public ObservableCollection<ExchangeWalletItem> ExchangeWallets { get; } = new();

    // Arbitrage Mode State / สถานะโหมด Arbitrage
    private ArbitrageExecutionMode _currentMode = ArbitrageExecutionMode.DualBalance;
    private TransferExecutionType _currentTransferType = TransferExecutionType.Manual;
    private decimal _dashboardSessionPnL = 0;

    // Trading Mode State (new) / สถานะโหมดการเทรด (ใหม่)
    private TradingModeType _currentTradingMode = TradingModeType.Arbitrage;
    private string _selectedSingleExchange = "Binance";

    public DashboardPage()
    {
        InitializeComponent();

        // Get services from DI
        _coinDataService = App.Services?.GetService<ICoinDataService>();
        _logger = App.Services?.GetService<ILoggingService>();
        _arbEngine = App.Services?.GetService<IArbEngine>();
        _configService = App.Services?.GetService<IConfigService>();
        _balancePool = App.Services?.GetService<IBalancePoolService>();
        _tradeHistory = App.Services?.GetService<ITradeHistoryService>();
        _notificationService = App.Services?.GetService<INotificationService>();
        _exchangeFactory = App.Services?.GetService<IExchangeClientFactory>();
        _projectService = App.Services?.GetService<IProjectService>();
        _connectionStatusService = App.Services?.GetService<IConnectionStatusService>();

        // Initialize UI
        InitializeCharts();
        InitializeData();
        SubscribeToEvents();

        // Start timers
        StartTimers();

        // Start logo rotation animation (fixed position, slow spin)
        StartLogoAnimation();

        Loaded += DashboardPage_Loaded;
        Unloaded += DashboardPage_Unloaded;
    }

    #region Initialization

    private void InitializeCharts()
    {
        // Performance Chart
        PerformanceChart.Series = new ISeries[]
        {
            new LineSeries<DateTimePoint>
            {
                Values = _pnlChartData,
                Fill = new LinearGradientPaint(
                    new[] { new SKColor(124, 58, 237, 50), SKColors.Transparent },
                    new SKPoint(0.5f, 0), new SKPoint(0.5f, 1)),
                Stroke = new SolidColorPaint(new SKColor(124, 58, 237), 2),
                GeometrySize = 0,
                LineSmoothness = 0.5
            }
        };

        PerformanceChart.XAxes = new Axis[]
        {
            new Axis
            {
                Labeler = value => new DateTime((long)value).ToString("HH:mm"),
                LabelsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 100)),
                SeparatorsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 20)),
                TextSize = 10
            }
        };

        PerformanceChart.YAxes = new Axis[]
        {
            new Axis
            {
                Labeler = value => $"${value:F0}",
                LabelsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 100)),
                SeparatorsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 20)),
                TextSize = 10
            }
        };

        // Spread History Mini Chart
        SpreadHistoryChart.Series = new ISeries[]
        {
            new LineSeries<double>
            {
                Values = _spreadHistoryData,
                Fill = null,
                Stroke = new SolidColorPaint(new SKColor(124, 58, 237), 2),
                GeometrySize = 0,
                LineSmoothness = 0.3
            }
        };

        SpreadHistoryChart.XAxes = new Axis[] { new Axis { ShowSeparatorLines = false, IsVisible = false } };
        SpreadHistoryChart.YAxes = new Axis[] { new Axis { ShowSeparatorLines = false, IsVisible = false } };

        // Best Pair Chart (Green gradient)
        BestPairChart.Series = new ISeries[]
        {
            new LineSeries<DateTimePoint>
            {
                Values = _bestPairChartData,
                Fill = new LinearGradientPaint(
                    new[] { new SKColor(16, 185, 129, 60), SKColors.Transparent },
                    new SKPoint(0.5f, 0), new SKPoint(0.5f, 1)),
                Stroke = new SolidColorPaint(new SKColor(16, 185, 129), 2),
                GeometrySize = 0,
                LineSmoothness = 0.5
            }
        };

        BestPairChart.XAxes = new Axis[]
        {
            new Axis
            {
                Labeler = value => new DateTime((long)value).ToString("dd/MM"),
                LabelsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 80)),
                SeparatorsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 15)),
                TextSize = 9
            }
        };

        BestPairChart.YAxes = new Axis[]
        {
            new Axis
            {
                Labeler = value => $"${value:F0}",
                LabelsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 80)),
                SeparatorsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 15)),
                TextSize = 9
            }
        };

        // Worst Pair Chart (Red gradient)
        WorstPairChart.Series = new ISeries[]
        {
            new LineSeries<DateTimePoint>
            {
                Values = _worstPairChartData,
                Fill = new LinearGradientPaint(
                    new[] { new SKColor(239, 68, 68, 60), SKColors.Transparent },
                    new SKPoint(0.5f, 0), new SKPoint(0.5f, 1)),
                Stroke = new SolidColorPaint(new SKColor(239, 68, 68), 2),
                GeometrySize = 0,
                LineSmoothness = 0.5
            }
        };

        WorstPairChart.XAxes = new Axis[]
        {
            new Axis
            {
                Labeler = value => new DateTime((long)value).ToString("dd/MM"),
                LabelsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 80)),
                SeparatorsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 15)),
                TextSize = 9
            }
        };

        WorstPairChart.YAxes = new Axis[]
        {
            new Axis
            {
                Labeler = value => $"${value:F0}",
                LabelsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 80)),
                SeparatorsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 15)),
                TextSize = 9
            }
        };
    }

    private async void InitializeData()
    {
        // NOTE: Exchange status is NOT loaded here because services may not be ready yet
        // Exchange status will be loaded by RefreshExchangeStatuses() called from MainWindow after Splash
        // สถานะ Exchange จะถูกโหลดโดย RefreshExchangeStatuses() ที่ MainWindow เรียกหลัง Splash เสร็จ

        // Set ItemsSource first (data will be populated later)
        ExchangeStatusList.ItemsSource = ExchangeStatuses;
        ActivePairsList.ItemsSource = ActivePairs;

        // Initialize activity log
        AddActivityLog("System", "Dashboard initialized", "#00D4FF");

        // Load data that doesn't require exchange connections
        // โหลดข้อมูลที่ไม่ต้องการการเชื่อมต่อ exchange
        await LoadActiveTradingPairsAsync();
        await LoadMarketOverviewAsync();
        await LoadTradeStatsAsync();
        await LoadRecentTradesAsync();
        await LoadPnLChartDataAsync();
        await LoadBestWorstPairsAsync();

        // Update mode display
        UpdateCurrentModeDisplay();

        // Note: LoadExchangeWalletsAsync requires connection, will be called in RefreshExchangeStatuses
    }

    /// <summary>
    /// Load REAL exchange connection statuses using ConnectionStatusService
    /// This uses cached verified status from Splash screen when available
    /// to avoid redundant connection checks.
    /// </summary>
    private async Task LoadExchangeStatusesAsync()
    {
        ExchangeStatuses.Clear();

        // Use ConnectionStatusService for proper credential checking
        if (_connectionStatusService != null)
        {
            var exchanges = new[] { "Binance", "KuCoin", "OKX", "Bybit", "Gate.io", "Bitkub" };
            var statusDict = new Dictionary<string, ExchangeConnectionStatus>();
            var needsCheck = new List<string>();

            // First, try to use cached status from Splash screen verification
            foreach (var exchangeName in exchanges)
            {
                var cachedStatus = _connectionStatusService.GetVerifiedStatus(exchangeName);
                if (cachedStatus != null)
                {
                    _logger?.LogInfo("Dashboard", $"LoadExchangeStatuses: Using cached status for {exchangeName}");
                    statusDict[exchangeName] = cachedStatus;
                }
                else
                {
                    needsCheck.Add(exchangeName);
                }
            }

            // Check any exchanges that weren't in cache
            if (needsCheck.Count > 0)
            {
                _logger?.LogInfo("Dashboard", $"LoadExchangeStatuses: Checking {needsCheck.Count} exchanges not in cache: {string.Join(", ", needsCheck)}");
                var status = await _connectionStatusService.CheckAllConnectionsAsync();
                foreach (var exchangeName in needsCheck)
                {
                    if (status.Exchanges.TryGetValue(exchangeName, out var exchangeStatus))
                    {
                        statusDict[exchangeName] = exchangeStatus;
                    }
                }
            }

            foreach (var exchangeName in exchanges)
            {
                string displayStatus;
                string statusColor;
                string latency;

                // Get status from dictionary (cached or freshly checked)
                if (!statusDict.TryGetValue(exchangeName, out var exchangeStatus))
                {
                    // No status available - mark as not configured
                    displayStatus = "Not Configured";
                    statusColor = "#60FFFFFF";
                    latency = "-";
                }
                else if (exchangeStatus.IsConnected && exchangeStatus.HasValidCredentials)
                {
                    // Fully connected with valid API key
                    displayStatus = "Connected";
                    statusColor = "#10B981"; // Green
                    latency = exchangeStatus.Latency > 0 ? $"{exchangeStatus.Latency}ms" : "-";
                }
                else if (exchangeStatus.HasValidCredentials && !exchangeStatus.IsConnected)
                {
                    // Has API key but connection failed
                    displayStatus = "Connection Error";
                    statusColor = "#EF4444"; // Red
                    latency = "-";
                }
                else if (!string.IsNullOrEmpty(exchangeStatus.ErrorMessage) && exchangeStatus.ErrorMessage.Contains("not configured"))
                {
                    // Exchange not configured in settings
                    displayStatus = "Not Configured";
                    statusColor = "#60FFFFFF"; // Gray
                    latency = "-";
                }
                else
                {
                    // No API key configured
                    displayStatus = "No API Key";
                    statusColor = "#F59E0B"; // Yellow/Warning
                    latency = "-";
                }

                ExchangeStatuses.Add(new ExchangeStatusItem(
                    exchangeName,
                    displayStatus,
                    statusColor,
                    latency
                ));
            }
        }
        else
        {
            // Fallback if ConnectionStatusService is not available
            var exchanges = new[] { "Binance", "KuCoin", "OKX", "Bybit", "Gate.io", "Bitkub" };
            foreach (var exchangeName in exchanges)
            {
                ExchangeStatuses.Add(new ExchangeStatusItem(
                    exchangeName,
                    "Service Unavailable",
                    "#60FFFFFF",
                    "-"
                ));
            }
        }

        // Log connected count and update UI
        var connectedCount = ExchangeStatuses.Count(e => e.Status == "Connected");
        var noApiKeyCount = ExchangeStatuses.Count(e => e.Status == "No API Key");

        Dispatcher.Invoke(() =>
        {
            if (connectedCount > 0)
            {
                // Hide no connection panel, show exchange list
                NoExchangeConnectedPanel.Visibility = Visibility.Collapsed;
                ExchangeStatusList.Visibility = Visibility.Visible;

                // Update system health badge
                SystemHealthBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2010B981"));
                SystemHealthText.Text = $"{connectedCount} CONNECTED";
                SystemHealthText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));

                AddActivityLog("Info", $"Connected to {connectedCount} exchange(s) with valid API keys", "#10B981");
            }
            else if (noApiKeyCount > 0)
            {
                // Show exchange list but with warning
                NoExchangeConnectedPanel.Visibility = Visibility.Collapsed;
                ExchangeStatusList.Visibility = Visibility.Visible;

                // Update system health badge to warning
                SystemHealthBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20F59E0B"));
                SystemHealthText.Text = "NO API KEYS";
                SystemHealthText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));

                AddActivityLog("Warning", "No API keys configured. Please add your API keys in Settings to start trading.", "#F59E0B");
            }
            else
            {
                // Show no connection panel
                NoExchangeConnectedPanel.Visibility = Visibility.Visible;
                ExchangeStatusList.Visibility = Visibility.Collapsed;

                // Update system health badge to error
                SystemHealthBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20EF4444"));
                SystemHealthText.Text = "NO CONNECTION";
                SystemHealthText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));

                AddActivityLog("Warning", "No exchanges connected. Please configure API keys in Settings.", "#EF4444");
            }
        });
    }

    /// <summary>
    /// Load active trading pairs from projects
    /// </summary>
    private async Task LoadActiveTradingPairsAsync()
    {
        ActivePairs.Clear();

        try
        {
            if (_projectService != null)
            {
                var projects = await _projectService.GetAllProjectsAsync();
                foreach (var project in projects.Where(p => p.IsActive))
                {
                    foreach (var pair in project.TradingPairs.Where(p => p.IsEnabled).Take(5))
                    {
                        var status = _isBotRunning ? "Active" : "Standby";
                        var statusColor = _isBotRunning ? "#10B981" : "#F59E0B";
                        var bgColor = _isBotRunning ? "#2010B981" : "#20F59E0B";

                        ActivePairs.Add(new TradingPairItem(
                            pair.BaseAsset,
                            pair.Symbol,
                            $"{pair.ExchangeA} → {pair.ExchangeB}",
                            status,
                            statusColor,
                            bgColor
                        ));
                    }
                }
            }

            // Show message if no pairs configured
            if (ActivePairs.Count == 0)
            {
                _logger?.LogInfo("Dashboard", "No active trading pairs. Add pairs from Scanner or Projects page.");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("Dashboard", $"Error loading trading pairs: {ex.Message}");
        }
    }

    private void SubscribeToEvents()
    {
        if (_arbEngine != null)
        {
            _arbEngine.StatusChanged += ArbEngine_StatusChanged;
            _arbEngine.TradeCompleted += ArbEngine_TradeCompleted;
            _arbEngine.OpportunityFound += ArbEngine_OpportunityFound;
            _arbEngine.PriceUpdated += ArbEngine_PriceUpdated;
            _arbEngine.ErrorOccurred += ArbEngine_ErrorOccurred;
        }

        if (_balancePool != null)
        {
            _balancePool.BalanceUpdated += BalancePool_BalanceUpdated;
            _balancePool.EmergencyTriggered += BalancePool_EmergencyTriggered;
        }

        // Subscribe to project changes for cross-page sync
        // ลงทะเบียนรับการเปลี่ยนแปลงโปรเจคสำหรับ sync ข้ามหน้า
        if (_projectService != null)
        {
            _projectService.ActiveProjectChanged += ProjectService_ActiveProjectChanged;
        }
    }

    private void ProjectService_ActiveProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        Dispatcher.Invoke(async () =>
        {
            try
            {
                await LoadActiveTradingPairsAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError("Dashboard", $"Error refreshing pairs on project change: {ex.Message}");
            }
        });
    }

    private void StartTimers()
    {
        // Main refresh timer (every 2 seconds for near real-time updates)
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += async (s, e) => await RefreshDataAsync();
        _refreshTimer.Start();

        // Chart update timer (every 5 seconds for responsive charts)
        _chartUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _chartUpdateTimer.Tick += async (s, e) => await UpdateChartsAsync();
        _chartUpdateTimer.Start();

        // Connection status refresh timer (every 30 seconds - less frequent to avoid API spam)
        // Timer สำหรับ refresh สถานะการเชื่อมต่อ (ทุก 30 วินาที - ไม่บ่อยเกินไปเพื่อไม่ให้เรียก API มากเกินไป)
        _connectionRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _connectionRefreshTimer.Tick += async (s, e) => await RefreshConnectionStatusAsync();
        // Note: This timer is NOT started here - it will be started after Splash completes
        // เริ่มหลังจาก Splash เสร็จแล้ว ไม่ใช่ตอน constructor
    }

    #endregion

    #region Data Loading

    private async Task LoadMarketOverviewAsync()
    {
        var coins = new[] { ("BTC", "Bitcoin"), ("ETH", "Ethereum"), ("SOL", "Solana"), ("XRP", "Ripple"), ("DOGE", "Dogecoin") };

        // Fetch real market data from CoinGecko when available
        Dictionary<string, CoinPriceData>? priceData = null;
        if (_coinDataService != null)
        {
            try
            {
                var coinIds = coins.Select(c => _coinDataService.GetCoinIdFromSymbol(c.Item1));
                priceData = await _coinDataService.GetPricesAsync(coinIds);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("Dashboard", $"Failed to fetch market prices: {ex.Message}");
            }
        }

        for (int i = 0; i < coins.Length; i++)
        {
            var (symbol, name) = coins[i];
            try
            {
                decimal price = 0;
                decimal change = 0;
                decimal spread = 0;
                int score = 0;

                var coinId = _coinDataService?.GetCoinIdFromSymbol(symbol) ?? symbol.ToLower();
                if (priceData != null && priceData.TryGetValue(coinId, out var coinPrice))
                {
                    price = coinPrice.Price;
                    change = coinPrice.Change24h;
                }
                else if (_coinDataService != null)
                {
                    price = await _coinDataService.GetPriceAsync(coinId);
                }

                // Calculate spread from ArbEngine if available
                if (_arbEngine != null)
                {
                    var pairs = _arbEngine.GetTradingPairs();
                    var matchingPair = pairs.FirstOrDefault(p =>
                        p.BaseCurrency.Equals(symbol, StringComparison.OrdinalIgnoreCase));
                    if (matchingPair?.CurrentOpportunity != null)
                    {
                        spread = matchingPair.CurrentOpportunity.BestSpreadPercentage;
                    }
                }

                score = (int)Math.Clamp(spread * 100 + 30, 0, 100);

                var changeColorHex = change >= 0 ? "#10B981" : "#EF4444";
                var spreadColorHex = spread >= 0.15m ? "#10B981" : spread >= 0.1m ? "#F59E0B" : "#60FFFFFF";
                var scoreColorHex = score >= 50 ? "#10B981" : score >= 30 ? "#F59E0B" : "#60FFFFFF";
                var scoreBgColorHex = score >= 50 ? "#2010B981" : score >= 30 ? "#20F59E0B" : "#15FFFFFF";

                // Check if item already exists - update instead of recreate to prevent flickering
                if (i < MarketItems.Count && MarketItems[i].Symbol == symbol)
                {
                    // Update existing item in-place (no flickering)
                    var item = MarketItems[i];
                    item.Price = $"${price:N2}";
                    item.Change24h = $"{(change >= 0 ? "+" : "")}{change:F2}%";
                    item.ChangeColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(changeColorHex));
                    item.BestSpread = $"{spread:F2}%";
                    item.SpreadColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(spreadColorHex));
                    item.Score = score.ToString();
                    item.ScoreColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(scoreColorHex));
                    item.ScoreBgColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(scoreBgColorHex));
                }
                else
                {
                    // First load - add new item
                    MarketItems.Add(new MarketOverviewItem
                    {
                        Symbol = symbol,
                        Name = name,
                        Price = $"${price:N2}",
                        Change24h = $"{(change >= 0 ? "+" : "")}{change:F2}%",
                        ChangeColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(changeColorHex)),
                        BestSpread = $"{spread:F2}%",
                        SpreadColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(spreadColorHex)),
                        Score = score.ToString(),
                        ScoreColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(scoreColorHex)),
                        ScoreBgColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(scoreBgColorHex))
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError("Dashboard", $"Error loading market data for {symbol}: {ex.Message}");
            }
        }

        // Only set ItemsSource once on first load
        if (MarketOverviewList.ItemsSource == null)
        {
            MarketOverviewList.ItemsSource = MarketItems;
        }
    }

    private async Task LoadTradeStatsAsync()
    {
        if (_tradeHistory == null) return;

        try
        {
            var stats = await _tradeHistory.GetStatsAsync(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);
            var todayStats = await _tradeHistory.GetTodayPnLAsync();

            Dispatcher.Invoke(() =>
            {
                // Win Rate
                WinRateValue.Text = $"{stats.WinRate:F1}%";
                WinLossCount.Text = $"{stats.WinningTrades}W / {stats.LosingTrades}L";

                // Profit Factor
                ProfitFactorValue.Text = stats.ProfitFactor > 0 ? $"{stats.ProfitFactor:F2}" : "-";

                // Average Win/Loss
                AvgWinValue.Text = $"${stats.AverageWin:F2}";
                LargestWin.Text = $"Best: ${stats.LargestWin:F2}";
                AvgLossValue.Text = $"${stats.AverageLoss:F2}";
                LargestLoss.Text = $"Worst: ${stats.LargestLoss:F2}";

                // Max Drawdown
                MaxDrawdownValue.Text = $"-{stats.MaxDrawdown:F2}%";
                UpdateDrawdownColor(stats.MaxDrawdown);

                // Average Speed
                AvgSpeedValue.Text = stats.AverageExecutionTime > 0 ? $"{stats.AverageExecutionTime:F0}" : "-";

                // Today's Stats
                TodayPnL.Text = todayStats.TotalNetPnL >= 0 ? $"+${todayStats.TotalNetPnL:F2}" : $"-${Math.Abs(todayStats.TotalNetPnL):F2}";
                TodayPnL.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(todayStats.TotalNetPnL >= 0 ? "#10B981" : "#EF4444"));
                TodayTrades.Text = todayStats.TotalTrades.ToString();

                // Total Portfolio (simulated for demo)
                var portfolioValue = 10000 + (decimal)todayStats.TotalNetPnL;
                TotalPortfolioValue.Text = $"${portfolioValue:N2}";

                var pnlPercent = portfolioValue > 0 ? ((portfolioValue - 10000) / 10000) * 100 : 0;
                TotalPnLPercent.Text = $"{(pnlPercent >= 0 ? "+" : "")}{pnlPercent:F2}%";
                TotalPnLValue.Text = $" (${(pnlPercent >= 0 ? "+" : "")}{todayStats.TotalNetPnL:F2})";

                var pnlColor = pnlPercent >= 0 ? "#10B981" : "#EF4444";
                TotalPnLPercent.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pnlColor));
                TotalPnLValue.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pnlColor));
                PnLBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pnlPercent >= 0 ? "#2010B981" : "#20EF4444"));
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError("Dashboard", $"Error loading trade stats: {ex.Message}");
        }
    }

    private async Task LoadRecentTradesAsync()
    {
        if (_tradeHistory == null) return;

        try
        {
            var trades = await _tradeHistory.GetRecentTradesAsync(10);

            Dispatcher.Invoke(() =>
            {
                RecentTrades.Clear();

                foreach (var trade in trades)
                {
                    var isProfit = trade.PnL >= 0;
                    RecentTrades.Add(new RecentTradeItem
                    {
                        Symbol = trade.Symbol.Replace("/USDT", ""),
                        Route = $"{trade.BuyExchange} → {trade.SellExchange}",
                        Time = trade.Timestamp.ToString("HH:mm:ss"),
                        Spread = $"{trade.SpreadPercent:F2}%",
                        PnL = $"{(isProfit ? "+" : "")}${trade.PnL:F2}",
                        PnLColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isProfit ? "#10B981" : "#EF4444")),
                        ExecutionTime = $"{trade.ExecutionTimeMs}ms",
                        BackgroundColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isProfit ? "#0810B981" : "#08EF4444"))
                    });
                }

                RecentTradesList.ItemsSource = RecentTrades;
                RecentTradesCount.Text = $"({trades.Count})";
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError("Dashboard", $"Error loading recent trades: {ex.Message}");
        }
    }

    private async Task LoadPnLChartDataAsync()
    {
        if (_tradeHistory == null) return;

        try
        {
            var dailyPnL = await _tradeHistory.GetDailyPnLHistoryAsync(30);

            Dispatcher.Invoke(() =>
            {
                _pnlChartData.Clear();
                decimal runningTotal = 10000; // Starting balance

                foreach (var day in dailyPnL.OrderBy(d => d.Date))
                {
                    runningTotal += (decimal)day.TotalNetPnL;
                    _pnlChartData.Add(new DateTimePoint(DateTime.Parse(day.Date), (double)runningTotal));
                }

                // If no data, add a starting point
                if (_pnlChartData.Count == 0)
                {
                    _pnlChartData.Add(new DateTimePoint(DateTime.Now.AddDays(-1), 10000));
                    _pnlChartData.Add(new DateTimePoint(DateTime.Now, 10000));
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError("Dashboard", $"Error loading P&L chart data: {ex.Message}");
        }
    }

    /// <summary>
    /// Load Best and Worst performing trading pairs from trade history
    /// </summary>
    private async Task LoadBestWorstPairsAsync()
    {
        if (_tradeHistory == null) return;

        try
        {
            // Get trades from last 30 days
            var trades = await _tradeHistory.GetRecentTradesAsync(1000); // Get more trades for analysis

            if (trades == null || trades.Count == 0)
            {
                // No trades, show placeholder data
                Dispatcher.Invoke(() =>
                {
                    BestPairSymbol.Text = "No Data";
                    BestPairRoute.Text = "Start trading to see performance";
                    BestPairPnL.Text = "$0.00";
                    BestPairChange.Text = "0.00%";
                    BestPairIcon.Symbol = "BTC";

                    WorstPairSymbol.Text = "No Data";
                    WorstPairRoute.Text = "Start trading to see performance";
                    WorstPairPnL.Text = "$0.00";
                    WorstPairChange.Text = "0.00%";
                    WorstPairIcon.Symbol = "ETH";

                    // Add placeholder chart data
                    _bestPairChartData.Clear();
                    _worstPairChartData.Clear();

                    for (int i = 7; i >= 0; i--)
                    {
                        _bestPairChartData.Add(new DateTimePoint(DateTime.Now.AddDays(-i), 0));
                        _worstPairChartData.Add(new DateTimePoint(DateTime.Now.AddDays(-i), 0));
                    }
                });
                return;
            }

            // Group trades by symbol and calculate total P&L
            var pairPerformance = trades
                .GroupBy(t => t.Symbol)
                .Select(g => new
                {
                    Symbol = g.Key,
                    TotalPnL = g.Sum(t => t.PnL),
                    TradeCount = g.Count(),
                    WinRate = g.Count(t => t.PnL > 0) * 100.0 / g.Count(),
                    Trades = g.OrderBy(t => t.Timestamp).ToList(),
                    BuyExchange = g.First().BuyExchange,
                    SellExchange = g.First().SellExchange
                })
                .OrderByDescending(p => p.TotalPnL)
                .ToList();

            if (pairPerformance.Count == 0) return;

            // Best performing pair (highest P&L)
            var bestPair = pairPerformance.First();
            _bestPairSymbol = bestPair.Symbol;

            // Worst performing pair (lowest P&L or highest loss)
            var worstPair = pairPerformance.Last();
            _worstPairSymbol = worstPair.Symbol;

            Dispatcher.Invoke(() =>
            {
                // Update Best Pair UI
                var bestSymbol = bestPair.Symbol.Replace("/USDT", "").Replace("-USDT", "");
                BestPairSymbol.Text = bestPair.Symbol;
                BestPairRoute.Text = $"{bestPair.BuyExchange} → {bestPair.SellExchange}";
                BestPairPnL.Text = $"+${bestPair.TotalPnL:F2}";
                BestPairChange.Text = $"+{bestPair.WinRate:F1}% WR";
                BestPairRank.Text = $"#{pairPerformance.IndexOf(bestPair) + 1}";
                BestPairIcon.Symbol = bestSymbol;

                // Update chart data for best pair
                _bestPairChartData.Clear();
                decimal runningPnL = 0;
                var last7DaysTrades = bestPair.Trades.Where(t => t.Timestamp > DateTime.UtcNow.AddDays(-7)).ToList();

                if (last7DaysTrades.Any())
                {
                    foreach (var trade in last7DaysTrades)
                    {
                        runningPnL += trade.PnL;
                        _bestPairChartData.Add(new DateTimePoint(trade.Timestamp, (double)runningPnL));
                    }
                }
                else
                {
                    // No trades in last 7 days, show cumulative
                    foreach (var trade in bestPair.Trades.TakeLast(10))
                    {
                        runningPnL += trade.PnL;
                        _bestPairChartData.Add(new DateTimePoint(trade.Timestamp, (double)runningPnL));
                    }
                }

                // Update Worst Pair UI
                var worstSymbol = worstPair.Symbol.Replace("/USDT", "").Replace("-USDT", "");
                WorstPairSymbol.Text = worstPair.Symbol;
                WorstPairRoute.Text = $"{worstPair.BuyExchange} → {worstPair.SellExchange}";

                if (worstPair.TotalPnL >= 0)
                {
                    WorstPairPnL.Text = $"+${worstPair.TotalPnL:F2}";
                    WorstPairRank.Text = "OK";
                    WorstPairPnL.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                }
                else
                {
                    WorstPairPnL.Text = $"-${Math.Abs(worstPair.TotalPnL):F2}";
                    WorstPairRank.Text = "LOW";
                    WorstPairPnL.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                }

                WorstPairChange.Text = $"{worstPair.WinRate:F1}% WR";
                WorstPairIcon.Symbol = worstSymbol;

                // Update chart data for worst pair
                _worstPairChartData.Clear();
                runningPnL = 0;
                var worstLast7DaysTrades = worstPair.Trades.Where(t => t.Timestamp > DateTime.UtcNow.AddDays(-7)).ToList();

                if (worstLast7DaysTrades.Any())
                {
                    foreach (var trade in worstLast7DaysTrades)
                    {
                        runningPnL += trade.PnL;
                        _worstPairChartData.Add(new DateTimePoint(trade.Timestamp, (double)runningPnL));
                    }
                }
                else
                {
                    // No trades in last 7 days, show cumulative
                    foreach (var trade in worstPair.Trades.TakeLast(10))
                    {
                        runningPnL += trade.PnL;
                        _worstPairChartData.Add(new DateTimePoint(trade.Timestamp, (double)runningPnL));
                    }
                }

                // Log activity
                AddActivityLog("Analytics", $"Best pair: {bestPair.Symbol} (+${bestPair.TotalPnL:F2})", "#10B981");
                if (worstPair.TotalPnL < 0)
                {
                    AddActivityLog("Analytics", $"Watch: {worstPair.Symbol} (-${Math.Abs(worstPair.TotalPnL):F2})", "#EF4444");
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError("Dashboard", $"Error loading best/worst pairs: {ex.Message}");
        }
    }

    /// <summary>
    /// Load wallet balances from all connected exchanges
    /// โหลดยอดกระเป๋าจากทุกกระดานที่เชื่อมต่อ
    /// </summary>
    private async Task LoadExchangeWalletsAsync()
    {
        ExchangeWallets.Clear();
        decimal totalPortfolioValue = 0;

        var exchanges = new[] { "Binance", "KuCoin", "OKX", "Bybit", "Gate.io", "Bitkub" };

        foreach (var exchangeName in exchanges)
        {
            try
            {
                // Check if API key is configured first
                var (keyEnv, secretEnv) = GetExchangeEnvVarNames(exchangeName);
                var apiKey = Environment.GetEnvironmentVariable(keyEnv);
                var apiSecret = Environment.GetEnvironmentVariable(secretEnv);

                if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
                {
                    // Not configured
                    ExchangeWallets.Add(new ExchangeWalletItem
                    {
                        ExchangeName = exchangeName,
                        USDTBalance = "Not Connected",
                        OtherAssets = "กรุณาตั้งค่า API Key",
                        StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60FFFFFF")),
                        TotalValueUSDT = 0
                    });
                    continue;
                }

                // Use real client for wallet loading (not simulation)
                var client = _exchangeFactory?.CreateRealClient(exchangeName);
                if (client == null)
                {
                    // Factory not available
                    ExchangeWallets.Add(new ExchangeWalletItem
                    {
                        ExchangeName = exchangeName,
                        USDTBalance = "Error",
                        OtherAssets = "Factory not available",
                        StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
                        TotalValueUSDT = 0
                    });
                    continue;
                }

                // Get balance from exchange
                var balance = await client.GetBalanceAsync();
                if (balance != null)
                {
                    // Find USDT balance using Assets dictionary
                    var usdtAmount = balance.GetTotal("USDT");

                    // Get other major assets
                    var otherAssets = balance.Assets
                        .Where(kvp => !kvp.Key.Equals("USDT", StringComparison.OrdinalIgnoreCase) && kvp.Value.Total > 0)
                        .Take(3)
                        .Select(kvp => $"{kvp.Key}: {kvp.Value.Total:F4}")
                        .ToList();

                    var otherAssetsText = otherAssets.Count > 0
                        ? string.Join(" | ", otherAssets)
                        : "No other assets";

                    // Calculate total value (simplified - USDT only for now)
                    totalPortfolioValue += usdtAmount;

                    ExchangeWallets.Add(new ExchangeWalletItem
                    {
                        ExchangeName = exchangeName,
                        USDTBalance = $"${usdtAmount:N2}",
                        OtherAssets = otherAssetsText,
                        StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")),
                        TotalValueUSDT = usdtAmount
                    });
                }
                else
                {
                    ExchangeWallets.Add(new ExchangeWalletItem
                    {
                        ExchangeName = exchangeName,
                        USDTBalance = "Error",
                        OtherAssets = "ไม่สามารถโหลดยอดได้",
                        StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
                        TotalValueUSDT = 0
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError("Dashboard", $"Error loading wallet for {exchangeName}: {ex.Message}");

                // Extract meaningful error message
                var errorMsg = ex.Message;
                if (errorMsg.Contains("Error 5"))
                    errorMsg = "IP ไม่อยู่ใน whitelist";
                else if (errorMsg.Contains("Error 6"))
                    errorMsg = "Signature ไม่ถูกต้อง";
                else if (errorMsg.Contains("Error 3"))
                    errorMsg = "API Key ไม่ถูกต้อง";
                else if (errorMsg.Length > 40)
                    errorMsg = errorMsg.Substring(0, 40) + "...";

                ExchangeWallets.Add(new ExchangeWalletItem
                {
                    ExchangeName = exchangeName,
                    USDTBalance = "Error",
                    OtherAssets = errorMsg,
                    StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
                    TotalValueUSDT = 0
                });
            }
        }

        // Update UI
        Dispatcher.Invoke(() =>
        {
            ExchangeWalletsList.ItemsSource = ExchangeWallets;
            TotalWalletValue.Text = $"${totalPortfolioValue:N2}";
            WalletLastUpdate.Text = $"Updated: {DateTime.Now:HH:mm:ss}";

            // Update session P&L display
            DashboardSessionPnL.Text = _dashboardSessionPnL >= 0
                ? $"+${_dashboardSessionPnL:F2}"
                : $"-${Math.Abs(_dashboardSessionPnL):F2}";
            DashboardSessionPnL.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(_dashboardSessionPnL >= 0 ? "#10B981" : "#EF4444"));
        });
    }

    /// <summary>
    /// Update current arbitrage mode display
    /// อัปเดตการแสดงโหมด arbitrage ปัจจุบัน
    /// </summary>
    private void UpdateCurrentModeDisplay()
    {
        // Load mode from config if available
        var config = _configService?.GetConfig();
        if (config != null)
        {
            _currentMode = config.Strategy.DefaultExecutionMode;
            _currentTransferType = config.Strategy.DefaultTransferType;
        }

        var modeInfo = ArbitrageModeInfo.GetModeInfo(_currentMode);

        Dispatcher.Invoke(() =>
        {
            // Update mode indicator
            CurrentModeName.Text = modeInfo.EnglishName;
            CurrentModeNameThai.Text = modeInfo.ThaiName;
            CurrentModeDescription.Text = modeInfo.ShortDescription;

            // Update icon and colors based on mode
            if (_currentMode == ArbitrageExecutionMode.DualBalance)
            {
                CurrentModeIcon.Text = "⚡";
                CurrentModeBorder.Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop((Color)ColorConverter.ConvertFromString("#3010B981"), 0),
                        new GradientStop((Color)ColorConverter.ConvertFromString("#2010B981"), 1)
                    },
                    new Point(0, 0), new Point(1, 1));
                CurrentModeBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                TransferTypeIndicator.Visibility = Visibility.Collapsed;
            }
            else
            {
                CurrentModeIcon.Text = "🔄";
                CurrentModeBorder.Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop((Color)ColorConverter.ConvertFromString("#30F59E0B"), 0),
                        new GradientStop((Color)ColorConverter.ConvertFromString("#20F59E0B"), 1)
                    },
                    new Point(0, 0), new Point(1, 1));
                CurrentModeBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));

                // Show transfer type indicator
                TransferTypeIndicator.Visibility = Visibility.Visible;
                var transferInfo = TransferExecutionTypeInfo.GetTypeInfo(_currentTransferType);
                TransferTypeIcon.Text = _currentTransferType == TransferExecutionType.Auto ? "🤖" : "👤";
                TransferTypeName.Text = $"{transferInfo.EnglishName} / {transferInfo.ThaiName}";
            }
        });
    }

    #endregion

    #region Event Handlers

    private void ArbEngine_StatusChanged(object? sender, EngineStatusEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _isBotRunning = e.Status == EngineStatus.Running || e.Status == EngineStatus.Paused;
            _isBotPaused = e.Status == EngineStatus.Paused;
            UpdateBotStatusUI(e.Status, e.Message);
            AddActivityLog("Bot", $"Status: {e.Status}", GetStatusColor(e.Status));
        });
    }

    private void ArbEngine_TradeCompleted(object? sender, TradeCompletedEventArgs e)
    {
        Dispatcher.Invoke(async () =>
        {
            var result = e.Result;
            var isProfit = result.NetPnL >= 0;

            AddActivityLog(
                "Trade",
                $"{result.Symbol}: {(isProfit ? "+" : "")}${result.NetPnL:F2}",
                isProfit ? "#10B981" : "#EF4444",
                $"{result.BuyOrder?.Exchange} → {result.SellOrder?.Exchange} | {result.DurationMs}ms"
            );

            // Refresh stats
            await LoadTradeStatsAsync();
            await LoadRecentTradesAsync();

            // Show trade animation
            FlashTradeComplete(isProfit);
        });
    }

    private void ArbEngine_OpportunityFound(object? sender, OpportunityEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var opp = e.Opportunity;

            // Update spread display
            SpreadValue.Text = $"{opp.NetSpreadPercentage:F2}%";
            SpreadAmount.Text = $"${opp.ExpectedNetProfitQuote:F2}";
            _lastSpread = opp.NetSpreadPercentage;

            // Update spread history
            UpdateSpreadHistory(opp.NetSpreadPercentage);

            // Update trading status
            if (opp.ShouldTrade)
            {
                UpdateTradingStatus("EXECUTING", "#10B981");
            }
            else if (opp.HasPositiveSpread)
            {
                UpdateTradingStatus("OPPORTUNITY", "#F59E0B");
            }
            else
            {
                UpdateTradingStatus("SCANNING", "#00D4FF");
            }
        });
    }

    private void ArbEngine_PriceUpdated(object? sender, PriceUpdateEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _scanCount++;
            ScanCount.Text = $"Scans: {_scanCount}";
            LastUpdateTime.Text = DateTime.Now.ToString("HH:mm:ss");

            // Update prices based on exchange
            if (e.Exchange.Contains("A") || e.Exchange.ToLower().Contains("binance"))
            {
                BuyPrice.Text = $"${e.Ticker.LastPrice:N2}";
            }
            else
            {
                SellPrice.Text = $"${e.Ticker.LastPrice:N2}";
            }
        });
    }

    private void ArbEngine_ErrorOccurred(object? sender, EngineErrorEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            AddActivityLog("Error", e.Message, "#EF4444");
            UpdateTradingStatus("ERROR", "#EF4444");
        });
    }

    private void BalancePool_BalanceUpdated(object? sender, BalanceUpdateEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var snapshot = e.Snapshot;
            var pnl = e.PnL;

            // Update portfolio value
            TotalPortfolioValue.Text = $"${snapshot.TotalValueUSDT:N2}";

            // Update P&L
            var pnlPercent = pnl.TotalPnLPercent;
            TotalPnLPercent.Text = $"{(pnlPercent >= 0 ? "+" : "")}{pnlPercent:F2}%";
            TotalPnLValue.Text = $" (${(pnl.TotalPnLUSDT >= 0 ? "+" : "")}{pnl.TotalPnLUSDT:F2})";

            var pnlColor = pnlPercent >= 0 ? "#10B981" : "#EF4444";
            TotalPnLPercent.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pnlColor));
            TotalPnLValue.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pnlColor));

            // Update distribution
            if (snapshot.CombinedBalances.TryGetValue("USDT", out var usdtBalance))
            {
                var total = usdtBalance.TotalBalance;
                if (total > 0)
                {
                    var aRatio = (int)(usdtBalance.DistributionRatio * 100);
                    ExchangeARatio.Width = new GridLength(aRatio, GridUnitType.Star);
                    ExchangeBRatio.Width = new GridLength(100 - aRatio, GridUnitType.Star);
                    ExchangeAPercent.Text = $"{aRatio}%";
                    ExchangeBPercent.Text = $"{100 - aRatio}%";
                }
            }

            // Update drawdown
            var drawdown = _balancePool?.CurrentDrawdown ?? 0;
            CurrentDrawdown.Text = $"Current: -{drawdown:F2}%";
            UpdateDrawdownColor(drawdown);
        });
    }

    private void BalancePool_EmergencyTriggered(object? sender, EmergencyEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            AddActivityLog("Emergency", e.Check.Message, "#EF4444");

            if (e.Check.RecommendedAction == EmergencyAction.StopTrading ||
                e.Check.RecommendedAction == EmergencyAction.PauseTrading)
            {
                MessageBox.Show(
                    $"Emergency Protection Triggered!\n\n{e.Check.Message}",
                    "AutoTrade-X Emergency",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        });
    }

    #endregion

    #region UI Updates

    private void UpdateBotStatusUI(EngineStatus status, string? message)
    {
        var (statusText, subText, color) = status switch
        {
            EngineStatus.Idle => ("Bot Idle", "Ready to start", "#808080"),
            EngineStatus.Starting => ("Starting...", "Initializing connections", "#F59E0B"),
            EngineStatus.Running => ("Bot Running", "Scanning for opportunities", "#10B981"),
            EngineStatus.Paused => ("Bot Paused", "Click resume to continue", "#F59E0B"),
            EngineStatus.Stopping => ("Stopping...", "Closing positions", "#F59E0B"),
            EngineStatus.Stopped => ("Bot Stopped", "All operations halted", "#808080"),
            EngineStatus.Error => ("Error", message ?? "Unknown error", "#EF4444"),
            _ => ("Unknown", "-", "#808080")
        };

        BotStatusText.Text = statusText;
        BotStatusSubtext.Text = subText;

        var statusColor = (Color)ColorConverter.ConvertFromString(color);
        BotStatusInnerBrush.Color = statusColor;
        BotStatusOuterBrush.Color = statusColor;

        // Update button visibility
        if (_isBotRunning)
        {
            StartBotButton.Visibility = Visibility.Collapsed;
            PauseBotButton.Visibility = Visibility.Visible;
            StopBotButton.Visibility = Visibility.Visible;
            PauseBotButton.Content = _isBotPaused ? "▶ Resume" : "⏸ Pause";
        }
        else
        {
            StartBotButton.Visibility = Visibility.Visible;
            PauseBotButton.Visibility = Visibility.Collapsed;
            StopBotButton.Visibility = Visibility.Collapsed;
        }

        // Update live indicator
        LiveIndicatorText.Text = status == EngineStatus.Running ? "LIVE" : status.ToString().ToUpper();
        var liveColor = status == EngineStatus.Running ? "#10B981" : color;
        LiveIndicatorDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(liveColor));
        LiveIndicatorText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(liveColor));
        LiveIndicatorBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(liveColor.Replace("#", "#20")));

        // Animate status dot when running
        if (status == EngineStatus.Running)
        {
            StartStatusAnimation();
        }
        else
        {
            StopStatusAnimation();
        }
    }

    private void UpdateTradingStatus(string status, string color)
    {
        TradingStatusText.Text = status switch
        {
            "EXECUTING" => "Executing trade...",
            "OPPORTUNITY" => "Opportunity found!",
            "SCANNING" => "Scanning markets...",
            "ERROR" => "Error occurred",
            _ => "Waiting for bot to start..."
        };

        TradingStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    private void UpdateSpreadHistory(decimal spread)
    {
        if (_spreadHistoryData.Count >= MaxSpreadHistory)
        {
            _spreadHistoryData.RemoveAt(0);
        }
        _spreadHistoryData.Add((double)spread);

        if (_spreadHistoryData.Count > 0)
        {
            SpreadMin.Text = $"Min: {_spreadHistoryData.Min():F2}%";
            SpreadMax.Text = $"Max: {_spreadHistoryData.Max():F2}%";
        }
    }

    private void UpdateDrawdownColor(decimal drawdown)
    {
        var color = drawdown switch
        {
            > 3 => "#EF4444",
            > 1 => "#F59E0B",
            _ => "#10B981"
        };

        MaxDrawdownValue.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        DrawdownIndicator.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    private void AddActivityLog(string type, string message, string color, string? details = null)
    {
        // Skip if UI not loaded yet / ข้ามถ้า UI ยังโหลดไม่เสร็จ
        if (ActivityLogList == null) return;

        try
        {
            var logItem = new ActivityLogItem
            {
                Message = $"[{type}] {message}",
                Details = details,
                DetailsVisibility = string.IsNullOrEmpty(details) ? Visibility.Collapsed : Visibility.Visible,
                Time = DateTime.Now.ToString("HH:mm:ss"),
                IconColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
            };

            ActivityLogs.Insert(0, logItem);

            // Keep only last 100 entries
            while (ActivityLogs.Count > 100)
            {
                ActivityLogs.RemoveAt(ActivityLogs.Count - 1);
            }

            ActivityLogList.ItemsSource = ActivityLogs;
        }
        catch (Exception)
        {
            // Ignore errors during UI initialization
        }
    }

    private string GetStatusColor(EngineStatus status) => status switch
    {
        EngineStatus.Running => "#10B981",
        EngineStatus.Paused => "#F59E0B",
        EngineStatus.Error => "#EF4444",
        _ => "#00D4FF"
    };

    #endregion

    #region Animations

    private void StartStatusAnimation()
    {
        var animation = new DoubleAnimation
        {
            From = 0.3,
            To = 1,
            Duration = TimeSpan.FromSeconds(0.8),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };

        BotStatusOuter.BeginAnimation(OpacityProperty, animation);
    }

    private void StopStatusAnimation()
    {
        BotStatusOuter.BeginAnimation(OpacityProperty, null);
        BotStatusOuter.Opacity = 0.3;
    }

    private void FlashTradeComplete(bool isProfit)
    {
        var flashColor = (Color)ColorConverter.ConvertFromString(isProfit ? "#10B981" : "#EF4444");
        var originalColor = (Color)ColorConverter.ConvertFromString("#7C3AED");

        var animation = new ColorAnimation
        {
            From = flashColor,
            To = originalColor,
            Duration = TimeSpan.FromSeconds(0.5),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(2)
        };

        if (SpreadBorder.Background is LinearGradientBrush brush)
        {
            brush.GradientStops[0].BeginAnimation(GradientStop.ColorProperty, animation);
        }
    }

    #endregion

    #region Button Handlers

    private async void StartBotButton_Click(object sender, RoutedEventArgs e)
    {
        if (_arbEngine == null) return;

        // Check connection status and prerequisites before starting
        if (_connectionStatusService != null)
        {
            var missingPrereqs = _connectionStatusService.GetMissingPrerequisites();
            if (missingPrereqs.Count > 0)
            {
                // Show warning message with missing prerequisites
                var message = "ไม่สามารถเริ่มเทรดได้ กรุณาตั้งค่าก่อน:\n\n" +
                              string.Join("\n", missingPrereqs.Select(p => "• " + p)) +
                              "\n\nไปที่หน้า Settings เพื่อตั้งค่า API Key";

                MessageBox.Show(message, "กรุณาตั้งค่า Exchange ก่อน",
                    MessageBoxButton.OK, MessageBoxImage.Warning);

                AddActivityLog("Warning", "Cannot start - Exchange not configured", "#F59E0B");
                _logger?.LogWarning("Dashboard", $"Start blocked - missing: {string.Join(", ", missingPrereqs)}");
                return;
            }
        }

        // Check if live trading mode
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
                AddActivityLog("Info", "Live trading cancelled by user", "#7C3AED");
                return;
            }
        }

        try
        {
            AddActivityLog("Bot", "Starting arbitrage bot...", "#F59E0B");
            var cts = new CancellationTokenSource();
            _ = Task.Run(async () => await _arbEngine.StartAsync(cts.Token));
        }
        catch (Exception ex)
        {
            _logger?.LogError("Dashboard", $"Error starting bot: {ex.Message}");
            AddActivityLog("Error", $"Failed to start: {ex.Message}", "#EF4444");
        }
    }

    private async void StopBotButton_Click(object sender, RoutedEventArgs e)
    {
        if (_arbEngine == null) return;

        try
        {
            AddActivityLog("Bot", "Stopping arbitrage bot...", "#F59E0B");
            await _arbEngine.StopAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError("Dashboard", $"Error stopping bot: {ex.Message}");
        }
    }

    private void PauseBotButton_Click(object sender, RoutedEventArgs e)
    {
        if (_arbEngine == null) return;

        if (_isBotPaused)
        {
            _arbEngine.Resume();
            AddActivityLog("Bot", "Bot resumed", "#10B981");
        }
        else
        {
            _arbEngine.Pause();
            AddActivityLog("Bot", "Bot paused", "#F59E0B");
        }
    }

    private void EmergencyStopButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to emergency stop?\n\nThis will immediately halt all trading operations.",
            "Emergency Stop",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            _arbEngine?.StopAsync();
            AddActivityLog("Emergency", "Emergency stop triggered by user", "#EF4444");
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        AddActivityLog("System", "Manual refresh triggered", "#00D4FF");
        await RefreshDataAsync();
    }

    private void AddPairButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddPairDialog();
        dialog.Owner = Window.GetWindow(this);
        dialog.ShowDialog();

        if (dialog.DialogResultOk)
        {
            if (dialog.Results.Count > 1)
            {
                // Multiple pairs created (One to Many or One to All)
                AddActivityLog("Pairs", $"Added {dialog.Results.Count} pairs for {dialog.Result?.Symbol}", "#10B981");
                foreach (var pair in dialog.Results)
                {
                    AddActivityLog("Pairs", $"  → {pair.ExchangeA} → {pair.ExchangeB}", "#7C3AED");
                }
            }
            else if (dialog.Result != null)
            {
                // Single pair created
                AddActivityLog("Pairs", $"Added {dialog.Result.Symbol} ({dialog.Result.ExchangeA} → {dialog.Result.ExchangeB})", "#10B981");
            }
        }
    }

    private void ViewAllTradesButton_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to History page
        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            // Trigger navigation to history
        }
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        ActivityLogs.Clear();
        AddActivityLog("System", "Activity log cleared", "#00D4FF");
    }

    private void GoToSettings_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to Settings page
        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.NavigateToPage("Settings");
        }
    }

    private void GoToTradingPage_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to Trading page to change mode
        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.NavigateToPage("Trading");
        }
    }

    private async void RefreshWallets_Click(object sender, RoutedEventArgs e)
    {
        AddActivityLog("System", "Refreshing wallet balances...", "#00D4FF");
        await LoadExchangeWalletsAsync();
        AddActivityLog("System", "Wallet balances updated", "#10B981");
    }

    #endregion

    #region Refresh Methods

    private async Task RefreshDataAsync()
    {
        try
        {
            await LoadMarketOverviewAsync();
            await LoadTradeStatsAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError("Dashboard", $"Error refreshing data: {ex.Message}");
        }
    }

    private async Task UpdateChartsAsync()
    {
        try
        {
            await LoadPnLChartDataAsync();
            await LoadBestWorstPairsAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError("Dashboard", $"Error updating charts: {ex.Message}");
        }
    }

    #endregion

    #region Logo Animation (Fixed Background with Rotation)

    private void StartLogoAnimation()
    {
        // Start slow Z-axis rotation animation (like original)
        var rotateAnimation = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromSeconds(60),
            RepeatBehavior = RepeatBehavior.Forever
        };
        LogoRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, rotateAnimation);

        // Start X-axis tilt animation (using SkewTransform.AngleY to simulate 3D X-axis rotation)
        var xAxisTiltAnimation = new DoubleAnimation
        {
            From = -15,
            To = 15,
            Duration = TimeSpan.FromSeconds(8),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        LogoSkew.BeginAnimation(System.Windows.Media.SkewTransform.AngleYProperty, xAxisTiltAnimation);
    }

    private void StopLogoAnimation()
    {
        LogoRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        LogoSkew.BeginAnimation(System.Windows.Media.SkewTransform.AngleYProperty, null);
    }

    #endregion

    #region Lifecycle

    private async void DashboardPage_Loaded(object sender, RoutedEventArgs e)
    {
        _refreshTimer?.Start();
        _chartUpdateTimer?.Start();

        // Update Start button state based on connection status
        await UpdateStartButtonStateAsync();
    }

    /// <summary>
    /// Public method to refresh exchange statuses from MainWindow after Splash completes
    /// เมธอดสาธารณะให้ MainWindow เรียกหลัง Splash เสร็จเพื่อ refresh status
    /// </summary>
    public async void RefreshExchangeStatuses()
    {
        _logger?.LogInfo("Dashboard", "RefreshExchangeStatuses: Called from MainWindow after Splash completed");

        // Re-get services if they were null during constructor
        // รับ services ใหม่ถ้าตอน constructor ยังไม่พร้อม
        EnsureServicesInitialized();

        // Load exchange connection status (uses cache from Splash)
        await LoadExchangeStatusesAsync();

        // Load exchange wallets (requires connection)
        await LoadExchangeWalletsAsync();

        // Update Start button state
        await UpdateStartButtonStateAsync();

        // Start the connection refresh timer now that services are ready
        // เริ่ม timer refresh connection หลังจาก services พร้อมแล้ว
        _connectionRefreshTimer?.Start();

        _logger?.LogInfo("Dashboard", "RefreshExchangeStatuses: Completed, connection timer started");
    }

    /// <summary>
    /// Periodic refresh of connection status (called by timer)
    /// Refresh สถานะ connection แบบ periodic (เรียกโดย timer)
    /// </summary>
    private async Task RefreshConnectionStatusAsync()
    {
        try
        {
            // Clear the verified cache to force fresh check
            // ล้าง cache เพื่อบังคับให้ check ใหม่
            // Note: Don't clear cache, just reload status
            await LoadExchangeStatusesAsync();
            await LoadExchangeWalletsAsync();
            await UpdateStartButtonStateAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError("Dashboard", $"Error refreshing connection status: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensures all services are initialized. Called when refreshing after Splash.
    /// ตรวจสอบว่า services ถูก initialize แล้ว เรียกเมื่อ refresh หลัง Splash
    /// </summary>
    private void EnsureServicesInitialized()
    {
        if (App.Services == null)
        {
            _logger?.LogWarning("Dashboard", "EnsureServicesInitialized: App.Services is still null");
            return;
        }

        // Re-fetch any null services
        _coinDataService ??= App.Services.GetService<ICoinDataService>();
        _logger ??= App.Services.GetService<ILoggingService>();
        _arbEngine ??= App.Services.GetService<IArbEngine>();
        _configService ??= App.Services.GetService<IConfigService>();
        _balancePool ??= App.Services.GetService<IBalancePoolService>();
        _tradeHistory ??= App.Services.GetService<ITradeHistoryService>();
        _notificationService ??= App.Services.GetService<INotificationService>();
        _exchangeFactory ??= App.Services.GetService<IExchangeClientFactory>();
        _projectService ??= App.Services.GetService<IProjectService>();
        _connectionStatusService ??= App.Services.GetService<IConnectionStatusService>();

        _logger?.LogInfo("Dashboard", "EnsureServicesInitialized: Services checked/re-fetched");
    }

    /// <summary>
    /// Updates the Start button enabled state based on exchange connection status.
    /// If no exchanges are connected, the button shows a warning and is still clickable
    /// but will show a message to configure settings first.
    /// </summary>
    private async Task UpdateStartButtonStateAsync()
    {
        if (_connectionStatusService == null || StartBotButton == null) return;

        try
        {
            var status = await _connectionStatusService.CheckAllConnectionsAsync();
            var canStart = _connectionStatusService.CanStartTrading;

            if (canStart)
            {
                // Connected - enable button with normal style
                StartBotButton.ToolTip = "Start arbitrage bot";
                StartBotButton.Opacity = 1.0;
            }
            else
            {
                // Not connected - show warning state
                var missingPrereqs = _connectionStatusService.GetMissingPrerequisites();
                var tooltipText = "กรุณาตั้งค่า Exchange ก่อนเริ่มเทรด:\n" +
                                  string.Join("\n", missingPrereqs.Select(p => "• " + p));
                StartBotButton.ToolTip = tooltipText;
                StartBotButton.Opacity = 0.7; // Slightly dimmed to indicate issue
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("Dashboard", $"Error checking connection status: {ex.Message}");
        }
    }

    private void DashboardPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _refreshTimer?.Stop();
        _chartUpdateTimer?.Stop();
        _connectionRefreshTimer?.Stop();
        StopLogoAnimation();

        // Unsubscribe from all events to prevent memory leaks
        if (_arbEngine != null)
        {
            _arbEngine.StatusChanged -= ArbEngine_StatusChanged;
            _arbEngine.TradeCompleted -= ArbEngine_TradeCompleted;
            _arbEngine.OpportunityFound -= ArbEngine_OpportunityFound;
            _arbEngine.PriceUpdated -= ArbEngine_PriceUpdated;
            _arbEngine.ErrorOccurred -= ArbEngine_ErrorOccurred;
        }

        if (_balancePool != null)
        {
            _balancePool.BalanceUpdated -= BalancePool_BalanceUpdated;
            _balancePool.EmergencyTriggered -= BalancePool_EmergencyTriggered;
        }

        if (_projectService != null)
        {
            _projectService.ActiveProjectChanged -= ProjectService_ActiveProjectChanged;
        }
    }

    /// <summary>
    /// Get environment variable names for exchange credentials
    /// </summary>
    private static (string keyEnv, string secretEnv) GetExchangeEnvVarNames(string exchangeName)
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

    #endregion

    #region Trading Mode Switcher / ตัวเลือกโหมดการเทรด

    /// <summary>
    /// Handle trading mode change from UI tabs
    /// จัดการการเปลี่ยนโหมดการเทรดจาก UI tabs
    /// </summary>
    private void TradingMode_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radio)
        {
            if (radio.Name == "ModeArbitrage")
            {
                _currentTradingMode = TradingModeType.Arbitrage;
                ShowArbitrageMode();
                AddActivityLog("Mode", "Switched to Arbitrage Mode", "#7C3AED");
            }
            else if (radio.Name == "ModeSingleExchange")
            {
                _currentTradingMode = TradingModeType.SingleExchange;
                ShowSingleExchangeMode();
                AddActivityLog("Mode", "Switched to Single Exchange Mode", "#00D4FF");
            }
            else if (radio.Name == "ModeAITrading")
            {
                _currentTradingMode = TradingModeType.AITrading;
                ShowAITradingMode();
                AddActivityLog("Mode", "Switched to AI Trading Mode", "#F59E0B");
            }
        }
    }

    /// <summary>
    /// Show Arbitrage mode panel and hide others
    /// แสดง panel โหมด Arbitrage และซ่อนอื่นๆ
    /// </summary>
    private void ShowArbitrageMode()
    {
        // Null check for elements that might not be loaded yet
        if (ArbitrageModePanel != null) ArbitrageModePanel.Visibility = Visibility.Visible;
        if (SingleExchangeModePanel != null) SingleExchangeModePanel.Visibility = Visibility.Collapsed;
        if (AITradingModePanel != null) AITradingModePanel.Visibility = Visibility.Collapsed;
        if (BalanceDistributionPanel != null) BalanceDistributionPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Show Single Exchange mode panel and hide others
    /// แสดง panel โหมด Single Exchange และซ่อนอื่นๆ
    /// </summary>
    private void ShowSingleExchangeMode()
    {
        if (ArbitrageModePanel != null) ArbitrageModePanel.Visibility = Visibility.Collapsed;
        if (SingleExchangeModePanel != null) SingleExchangeModePanel.Visibility = Visibility.Visible;
        if (AITradingModePanel != null) AITradingModePanel.Visibility = Visibility.Collapsed;
        if (BalanceDistributionPanel != null) BalanceDistributionPanel.Visibility = Visibility.Collapsed;

        // Load single exchange data
        _ = LoadSingleExchangeDataAsync();
    }

    /// <summary>
    /// Show AI Trading mode panel and hide others
    /// แสดง panel โหมด AI Trading และซ่อนอื่นๆ
    /// </summary>
    private void ShowAITradingMode()
    {
        if (ArbitrageModePanel != null) ArbitrageModePanel.Visibility = Visibility.Collapsed;
        if (SingleExchangeModePanel != null) SingleExchangeModePanel.Visibility = Visibility.Collapsed;
        if (AITradingModePanel != null) AITradingModePanel.Visibility = Visibility.Visible;
        if (BalanceDistributionPanel != null) BalanceDistributionPanel.Visibility = Visibility.Collapsed;

        // Load AI trading data
        _ = LoadAITradingDataAsync();
    }

    /// <summary>
    /// Handle single exchange selector change
    /// จัดการการเปลี่ยน exchange ที่เลือก
    /// </summary>
    private async void SingleExchangeSelector_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SingleExchangeSelector.SelectedItem is ComboBoxItem selectedItem)
        {
            _selectedSingleExchange = selectedItem.Content?.ToString() ?? "Binance";
            await LoadSingleExchangeDataAsync();
        }
    }

    /// <summary>
    /// Load data for single exchange mode
    /// โหลดข้อมูลสำหรับโหมด Single Exchange
    /// </summary>
    private async Task LoadSingleExchangeDataAsync()
    {
        // Skip if UI elements not loaded yet
        if (SingleExchangeBalance == null) return;

        try
        {
            // Get balance for selected exchange
            var wallet = ExchangeWallets.FirstOrDefault(w => w.ExchangeName == _selectedSingleExchange);
            if (wallet != null)
            {
                SingleExchangeBalance.Text = $"Balance: {wallet.USDTBalance}";
            }
            else
            {
                SingleExchangeBalance.Text = "Balance: Not Connected";
            }

            // Get BTC price as default pair
            if (_coinDataService != null && SinglePairPrice != null)
            {
                var price = (decimal)await _coinDataService.GetPriceAsync("btc");
                SinglePairPrice.Text = $"${price:N2}";

                // Simulate 24h data (in real app, fetch from API)
                var random = new Random();
                var change = (decimal)(random.NextDouble() * 10 - 5);
                if (SinglePairChange != null)
                {
                    SinglePairChange.Text = $"{(change >= 0 ? "+" : "")}{change:F2}%";
                    SinglePairChange.Foreground = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(change >= 0 ? "#10B981" : "#EF4444"));
                }

                if (SingleHigh24h != null) SingleHigh24h.Text = $"${price * 1.02m:N2}";
                if (SingleLow24h != null) SingleLow24h.Text = $"${price * 0.98m:N2}";
                if (SingleVolume24h != null) SingleVolume24h.Text = $"${random.Next(1000, 5000)}M";
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("Dashboard", $"Error loading single exchange data: {ex.Message}");
        }
    }

    /// <summary>
    /// Load data for AI trading mode
    /// โหลดข้อมูลสำหรับโหมด AI Trading
    /// </summary>
    private async Task LoadAITradingDataAsync()
    {
        // Skip if UI elements not loaded yet
        if (AISignalText == null) return;

        try
        {
            // Simulate AI data (in real app, fetch from AI service)
            await Task.Delay(100); // Simulate async call

            var random = new Random();
            var signals = new[] { "BUY", "SELL", "HOLD" };
            var signal = signals[random.Next(signals.Length)];

            AISignalText.Text = signal;
            var signalColor = signal switch
            {
                "BUY" => "#10B981",
                "SELL" => "#EF4444",
                _ => "#F59E0B"
            };
            AISignalText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(signalColor));

            var confidence = 60 + random.Next(35);
            if (AIConfidence != null) AIConfidence.Text = $"Confidence: {confidence}%";

            var accuracy = 65 + random.Next(25);
            if (AIAccuracy != null) AIAccuracy.Text = $"{accuracy}%";

            // Check if there's an active AI position
            if (AIPositionStatus != null) AIPositionStatus.Text = "No Active Position";
            if (AIPositionPnL != null) AIPositionPnL.Text = "$0.00";
        }
        catch (Exception ex)
        {
            _logger?.LogError("Dashboard", $"Error loading AI trading data: {ex.Message}");
        }
    }

    #endregion
}

/// <summary>
/// Trading mode types / ประเภทโหมดการเทรด
/// </summary>
public enum TradingModeType
{
    Arbitrage,
    SingleExchange,
    AITrading
}

#region View Models

public class ExchangeStatusItem
{
    public string Name { get; set; }
    public string Status { get; set; }
    public SolidColorBrush StatusColor { get; set; }
    public string Latency { get; set; }

    public ExchangeStatusItem(string name, string status, string color, string latency)
    {
        Name = name;
        Status = status;
        StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        Latency = latency;
    }
}

public class MarketOverviewItem : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";

    private string _price = "";
    public string Price
    {
        get => _price;
        set { if (_price != value) { _price = value; OnPropertyChanged(nameof(Price)); } }
    }

    private string _change24h = "";
    public string Change24h
    {
        get => _change24h;
        set { if (_change24h != value) { _change24h = value; OnPropertyChanged(nameof(Change24h)); } }
    }

    private SolidColorBrush _changeColor = Brushes.White;
    public SolidColorBrush ChangeColor
    {
        get => _changeColor;
        set { _changeColor = value; OnPropertyChanged(nameof(ChangeColor)); }
    }

    private string _bestSpread = "";
    public string BestSpread
    {
        get => _bestSpread;
        set { if (_bestSpread != value) { _bestSpread = value; OnPropertyChanged(nameof(BestSpread)); } }
    }

    private SolidColorBrush _spreadColor = Brushes.White;
    public SolidColorBrush SpreadColor
    {
        get => _spreadColor;
        set { _spreadColor = value; OnPropertyChanged(nameof(SpreadColor)); }
    }

    private string _score = "";
    public string Score
    {
        get => _score;
        set { if (_score != value) { _score = value; OnPropertyChanged(nameof(Score)); } }
    }

    private SolidColorBrush _scoreColor = Brushes.White;
    public SolidColorBrush ScoreColor
    {
        get => _scoreColor;
        set { _scoreColor = value; OnPropertyChanged(nameof(ScoreColor)); }
    }

    private SolidColorBrush _scoreBgColor = Brushes.Transparent;
    public SolidColorBrush ScoreBgColor
    {
        get => _scoreBgColor;
        set { _scoreBgColor = value; OnPropertyChanged(nameof(ScoreBgColor)); }
    }
}

public class TradingPairItem
{
    public string Symbol { get; set; }
    public string DisplayName { get; set; }
    public string Route { get; set; }
    public string Status { get; set; }
    public SolidColorBrush StatusColor { get; set; }
    public SolidColorBrush StatusBgColor { get; set; }

    public TradingPairItem(string symbol, string displayName, string route, string status, string statusColor, string statusBgColor)
    {
        Symbol = symbol;
        DisplayName = displayName;
        Route = route;
        Status = status;
        StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(statusColor));
        StatusBgColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(statusBgColor));
    }
}

public class RecentTradeItem
{
    public string Symbol { get; set; } = "";
    public string Route { get; set; } = "";
    public string Time { get; set; } = "";
    public string Spread { get; set; } = "";
    public string PnL { get; set; } = "";
    public SolidColorBrush PnLColor { get; set; } = Brushes.White;
    public string ExecutionTime { get; set; } = "";
    public SolidColorBrush BackgroundColor { get; set; } = Brushes.Transparent;
}

public class ActivityLogItem
{
    public string Message { get; set; } = "";
    public string? Details { get; set; }
    public Visibility DetailsVisibility { get; set; } = Visibility.Collapsed;
    public string Time { get; set; } = "";
    public SolidColorBrush IconColor { get; set; } = Brushes.White;
}

/// <summary>
/// View model for exchange wallet display in dashboard
/// แสดงข้อมูลกระเป๋าเงินของ exchange บน dashboard
/// </summary>
public class ExchangeWalletItem
{
    public string ExchangeName { get; set; } = "";
    public string USDTBalance { get; set; } = "$0.00";
    public string OtherAssets { get; set; } = "";
    public SolidColorBrush StatusColor { get; set; } = Brushes.Gray;
    public decimal TotalValueUSDT { get; set; }
}

#endregion
