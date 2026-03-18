using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.Extensions.DependencyInjection;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using AutoTradeX.Infrastructure.Services;
using AutoTradeX.UI.Controls;

namespace AutoTradeX.UI.Views;

/// <summary>
/// Represents an active trading pair with its own state and strategy
/// </summary>
public class ActiveTradingPair
{
    public string Symbol { get; set; } = "";
    public string Exchange { get; set; } = "";
    public AITradingMode Strategy { get; set; } = AITradingMode.Scalping;
    public AIStrategyConfig Config { get; set; } = new();
    public bool IsAIRunning { get; set; } = false;
    public AITradingPosition? Position { get; set; }
    public AITradingSignal? CurrentSignal { get; set; }
    public Border? TabElement { get; set; }
    public DateTime? SessionStartTime { get; set; }
    public TimeSpan SessionDuration { get; set; } = TimeSpan.Zero;
}

public partial class AITradingPage : UserControl
{
    private IAITradingService? _aiTradingService;
    private IExchangeClientFactory? _exchangeFactory;
    private ILoggingService? _logger;
    private DemoWalletService? _demoWallet;
    private IConnectionStatusService? _connectionStatusService;
    private IApiCredentialsService? _apiCredentialsService;

    // List of configured exchanges (has API keys)
    private List<string> _configuredExchanges = new();

    private System.Windows.Threading.DispatcherTimer? _updateTimer;
    private System.Windows.Threading.DispatcherTimer? _sessionTimer;
    private DateTime _sessionStartTime;
    private TimeSpan _sessionDuration;

    // Session balance tracking for P&L display
    private decimal _sessionStartingBalance = 10000m;
    private bool _sessionBalanceInitialized = false;

    private string _selectedExchange = "Binance";
    private string _selectedSymbol = "BTC/USDT";
    private AITradingMode _selectedStrategy = AITradingMode.Scalping;
    private string _chartInterval = "1m";
    private bool _isAIRunning = false;
    private bool _isAutoStrategy = false; // Auto strategy mode OFF by default

    // CRITICAL: Use static field to persist mode across page lifecycle
    // This ensures mode is preserved even if page is reloaded
    private static bool _staticIsDemoMode = true; // Demo mode ON by default
    private bool _isDemoMode
    {
        get => _staticIsDemoMode;
        set => _staticIsDemoMode = value;
    }

    private AIStrategyConfig _config = new();
    private AITradingSignal? _currentSignal;
    private AITradingPosition? _currentPosition;
    private AIMarketData? _lastMarketData; // Keep last market data for analysis

    // Demo trading position (used when _isDemoMode = true)
    private DemoAIPosition? _demoPosition;
    private bool _isClosingDemoPosition = false;
    private bool _isUpdatingMarketData = false;

    // Auto-strategy cooldown (prevents rapid thrashing)
    private AITradingMode? _pendingAutoStrategy = null;
    private DateTime _pendingAutoStrategyTime = DateTime.MinValue;

    // Exchange tab references
    private readonly Dictionary<string, Border> _exchangeTabs = new();

    // Active trading pairs (max 10 per exchange)
    private readonly Dictionary<string, List<ActiveTradingPair>> _activePairs = new();
    private ActiveTradingPair? _currentPair;
    private const int MaxPairsPerExchange = 10;

    public AITradingPage()
    {
        InitializeComponent();
        DataContext = this;

        _aiTradingService = App.Services?.GetService<IAITradingService>();
        _exchangeFactory = App.Services?.GetService<IExchangeClientFactory>();
        _logger = App.Services?.GetService<ILoggingService>();
        _demoWallet = App.Services?.GetService<DemoWalletService>();
        _connectionStatusService = App.Services?.GetService<IConnectionStatusService>();

        InitializeExchangeTabs();
        InitializeActivePairs();
        UpdateDemoWalletDisplay();

        Loaded += AITradingPage_Loaded;
        Unloaded += AITradingPage_Unloaded;
    }

    private void InitializeActivePairs()
    {
        // Initialize active pairs dictionary for each exchange
        var exchanges = new[] { "Binance", "KuCoin", "OKX", "Bybit", "Gate.io", "Bitkub" };
        foreach (var exchange in exchanges)
        {
            _activePairs[exchange] = new List<ActiveTradingPair>();
        }

        // Add default BTC/USDT pair for Binance
        AddTradingPair("Binance", "BTC/USDT", setAsCurrent: true);
    }

    private void UpdateDemoWalletDisplay()
    {
        if (_isDemoMode)
        {
            // Demo Mode = ข้อมูลจริงจาก Exchange + เงินเสมือนจาก DemoWallet
            ModeIcon.Text = "🎮";
            DemoModeText.Text = "DEMO MODE";
            DemoModeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0A0A1A")!);
            ModeDescription.Text = "ใช้เงินเสมือน - ปลอดภัย";
            BalanceLabelStart.Text = "💰 เริ่มต้น:";
            BalanceLabel.Text = "📊 ปัจจุบัน:";

            // Update badge colors (orange for demo)
            DemoModeBadge.Background = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#F59E0B")!,
                (Color)ColorConverter.ConvertFromString("#D97706")!,
                0);

            // Update toggle switch to left (DEMO)
            DemoRealToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30FFFFFF")!);
            DemoRealToggleKnob.HorizontalAlignment = HorizontalAlignment.Left;
            DemoRealToggleKnob.Margin = new Thickness(3, 0, 0, 0);

            // Use cached balance to avoid blocking UI thread
            var cachedWallet = _demoWallet?.GetCachedWallet();
            var currentBalance = cachedWallet?.Balances.GetValueOrDefault("USDT", 10000m) ?? 10000m;

            // Initialize starting balance on first call or when switching to demo mode
            if (!_sessionBalanceInitialized)
            {
                _sessionStartingBalance = currentBalance;
                _sessionBalanceInitialized = true;
            }

            // Update Starting Balance display
            StartingBalance.Text = $"${_sessionStartingBalance:N2}";
            StartingBalance.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A78BFA")!);
            StartingBalance.ToolTip = "ยอดเงินเมื่อเริ่ม Session";

            // Update Current Balance display
            DemoWalletBalance.Text = $"${currentBalance:N2}";
            DemoWalletBalance.Foreground = new SolidColorBrush(Colors.White);
            DemoWalletBalance.ToolTip = "Demo Wallet - ใช้เงินเสมือน แต่ข้อมูลตลาดจริง";

            // Calculate and display P&L
            UpdatePnLDisplay(currentBalance);

            // Hide real mode warning
            RealModeWarning.Visibility = Visibility.Collapsed;

            // Update panel background to orange tint
            ModePanelColor1.Color = (Color)ColorConverter.ConvertFromString("#20F59E0B")!;
            ModePanelColor2.Color = (Color)ColorConverter.ConvertFromString("#10F59E0B")!;
        }
        else
        {
            // Real Mode = ข้อมูลจริง + เงินจริงจาก Exchange
            ModeIcon.Text = "💰";
            DemoModeText.Text = "REAL MODE";
            DemoModeText.Foreground = new SolidColorBrush(Colors.White);
            ModeDescription.Text = "⚠️ ใช้เงินจริง - ระวัง!";
            BalanceLabelStart.Text = "💰 เริ่มต้น:";
            BalanceLabel.Text = "📊 ปัจจุบัน:";

            // Update badge colors (red for real - warning)
            DemoModeBadge.Background = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#EF4444")!,
                (Color)ColorConverter.ConvertFromString("#DC2626")!,
                0);

            // Update toggle switch to right (REAL)
            DemoRealToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")!);
            DemoRealToggleKnob.HorizontalAlignment = HorizontalAlignment.Right;
            DemoRealToggleKnob.Margin = new Thickness(0, 0, 3, 0);

            // Show Starting Balance (keep previous or show placeholder)
            StartingBalance.Text = _sessionBalanceInitialized ? $"${_sessionStartingBalance:N2}" : "Loading...";
            StartingBalance.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A78BFA")!);

            DemoWalletBalance.Text = "Loading...";
            DemoWalletBalance.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F87171")!);
            DemoWalletBalance.ToolTip = "Real Mode - ใช้เงินจริงจาก Exchange ระวัง!";

            // Reset P&L display while loading
            UpdatePnLDisplay(_sessionStartingBalance);

            // Show real mode warning banner
            RealModeWarning.Visibility = Visibility.Visible;

            // Update panel background to red tint
            ModePanelColor1.Color = (Color)ColorConverter.ConvertFromString("#20EF4444")!;
            ModePanelColor2.Color = (Color)ColorConverter.ConvertFromString("#10EF4444")!;

            // Fetch real balance from exchange
            _ = UpdateRealBalanceAsync();
        }
    }

    /// <summary>
    /// Update P&L display based on current balance vs starting balance
    /// อัพเดทการแสดง P&L จากยอดเงินปัจจุบันเทียบกับยอดเริ่มต้น
    /// </summary>
    private void UpdatePnLDisplay(decimal currentBalance)
    {
        var pnl = currentBalance - _sessionStartingBalance;
        var pnlPercent = _sessionStartingBalance > 0 ? (pnl / _sessionStartingBalance) * 100 : 0;
        var isProfit = pnl >= 0;

        // Use correct currency symbol based on exchange
        var currencyPrefix = _selectedExchange == "Bitkub" ? "฿" : "$";

        // Update P&L icon
        PnLIcon.Text = isProfit ? "📈" : "📉";

        // Update P&L text with correct currency
        var pnlSign = isProfit ? "+" : "";
        PnLText.Text = $"{pnlSign}{currencyPrefix}{Math.Abs(pnl):N2}";
        PnLPercent.Text = $" ({pnlSign}{pnlPercent:N2}%)";

        // Update colors based on profit/loss
        var pnlColor = isProfit ? "#10B981" : "#EF4444";
        var pnlBgColor = isProfit ? "#2010B981" : "#20EF4444";

        PnLText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pnlColor)!);
        PnLPercent.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pnlColor)!);
        PnLBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pnlBgColor)!);
    }

    /// <summary>
    /// Fetch and display real balance from the selected exchange
    /// ดึงยอดเงินจริงจากกระดานเทรด
    /// </summary>
    private async Task UpdateRealBalanceAsync()
    {
        try
        {
            if (_exchangeFactory == null) return;

            _logger?.LogInfo("AITradingPage", $"Fetching real balance from {_selectedExchange}...");

            // Check if credentials exist in database first
            if (_apiCredentialsService != null)
            {
                var hasCredentials = await _apiCredentialsService.HasCredentialsAsync(_selectedExchange);
                _logger?.LogInfo("AITradingPage", $"HasCredentials for {_selectedExchange}: {hasCredentials}");

                if (!hasCredentials)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        StartingBalance.Text = "N/A";
                        DemoWalletBalance.Text = "N/A";
                        DemoWalletBalance.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")!);
                        RealBalanceText.Text = $"| ⚠️ ยังไม่ได้ตั้งค่า API Key สำหรับ {_selectedExchange}";
                    });
                    return;
                }

                // Load credentials to environment if not already loaded
                await _apiCredentialsService.LoadCredentialsToEnvironmentAsync();
            }

            var client = _exchangeFactory.CreateRealClient(_selectedExchange);

            // Bitkub uses THB, others use USDT
            var quoteCurrency = _selectedExchange == "Bitkub" ? "THB" : "USDT";

            // First test connection to make sure API credentials are valid
            var isConnected = await client.TestConnectionAsync();
            if (!isConnected)
            {
                _logger?.LogWarning("AITradingPage", $"Connection test failed for {_selectedExchange}");
                await Dispatcher.InvokeAsync(() =>
                {
                    StartingBalance.Text = "N/A";
                    DemoWalletBalance.Text = "N/A";
                    DemoWalletBalance.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")!);
                    RealBalanceText.Text = $"| ⚠️ เชื่อมต่อ {_selectedExchange} ไม่สำเร็จ - ตรวจสอบ API Key";
                });
                return;
            }

            var assetBalance = await client.GetAssetBalanceAsync(quoteCurrency);
            var balance = assetBalance.Available; // Available balance

            _logger?.LogInfo("AITradingPage", $"Balance fetched: {balance} {quoteCurrency}");

            await Dispatcher.InvokeAsync(() =>
            {
                var currencyPrefix = _selectedExchange == "Bitkub" ? "฿" : "$";

                // Initialize starting balance on first successful fetch (Real mode)
                if (!_sessionBalanceInitialized)
                {
                    _sessionStartingBalance = balance;
                    _sessionBalanceInitialized = true;
                }

                // Update Starting Balance display
                StartingBalance.Text = $"{currencyPrefix}{_sessionStartingBalance:N2}";
                StartingBalance.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A78BFA")!);

                // Update Current Balance display
                DemoWalletBalance.Text = $"{currencyPrefix}{balance:N2}";
                RealBalanceText.Text = $"| {_selectedExchange}: {currencyPrefix}{balance:N2} {quoteCurrency}";

                // Check if balance is too low (10 USDT or 350 THB)
                var minBalance = _selectedExchange == "Bitkub" ? 350m : 10m;
                if (balance < minBalance)
                {
                    DemoWalletBalance.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")!);
                    RealBalanceText.Text += " ⚠️ ยอดเงินต่ำ!";
                }
                else
                {
                    DemoWalletBalance.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")!);
                }

                // Update P&L display
                UpdatePnLDisplay(balance);
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError("AITradingPage", $"Error fetching real balance from {_selectedExchange}: {ex.Message}");

            // Parse error for user-friendly message
            var errorMessage = ParseExchangeError(ex.Message);

            await Dispatcher.InvokeAsync(() =>
            {
                StartingBalance.Text = "Error";
                DemoWalletBalance.Text = "Error";
                DemoWalletBalance.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")!);
                RealBalanceText.Text = $"| ❌ {errorMessage}";
            });
        }
    }

    /// <summary>
    /// Parse exchange error message for user-friendly display
    /// แปลง error message เป็นภาษาที่เข้าใจง่าย
    /// </summary>
    private string ParseExchangeError(string errorMessage)
    {
        // Bitkub specific errors
        if (errorMessage.Contains("IP not allowed") || errorMessage.Contains("error 5"))
            return "IP ไม่ได้อยู่ใน whitelist - กรุณาเพิ่ม IP ที่ bitkub.com/api";
        if (errorMessage.Contains("Invalid signature") || errorMessage.Contains("error 6"))
            return "ลายเซ็น API ไม่ถูกต้อง - ตรวจสอบ API Secret";
        if (errorMessage.Contains("Invalid API key") || errorMessage.Contains("error 3"))
            return "API Key ไม่ถูกต้อง - ตรวจสอบ API Key";
        if (errorMessage.Contains("API credentials not configured"))
            return "ยังไม่ได้ตั้งค่า API Key - กรุณาตั้งค่าใน Settings";
        if (errorMessage.Contains("Invalid timestamp") || errorMessage.Contains("error 8"))
            return "Timestamp ไม่ถูกต้อง - ตรวจสอบเวลาเครื่อง";
        if (errorMessage.Contains("Invalid permission") || errorMessage.Contains("error 52"))
            return "API Key ไม่มีสิทธิ์เข้าถึง - ตรวจสอบสิทธิ์ที่ bitkub.com";

        // General errors
        if (errorMessage.Contains("timeout") || errorMessage.Contains("Timeout"))
            return "หมดเวลาเชื่อมต่อ - ลองใหม่อีกครั้ง";
        if (errorMessage.Contains("network") || errorMessage.Contains("Network"))
            return "เชื่อมต่ออินเตอร์เน็ตไม่ได้";

        // Default: show shortened error
        if (errorMessage.Length > 50)
            return errorMessage.Substring(0, 50) + "...";
        return errorMessage;
    }

    private void InitializeExchangeTabs()
    {
        _exchangeTabs["Binance"] = BinanceTab;
        _exchangeTabs["KuCoin"] = KuCoinTab;
        _exchangeTabs["OKX"] = OKXTab;
        _exchangeTabs["Bybit"] = BybitTab;
        _exchangeTabs["Gate.io"] = GateIOTab;
        _exchangeTabs["Bitkub"] = BitkubTab;
    }

    /// <summary>
    /// Check if user has enough balance to start AI trading in Real mode
    /// ตรวจสอบยอดเงินขั้นต่ำก่อนเริ่ม AI Trading
    /// </summary>
    private async Task<(bool HasEnough, decimal CurrentBalance)> CheckMinimumBalanceAsync()
    {
        try
        {
            if (_exchangeFactory == null)
                return (false, 0);

            var client = _exchangeFactory.CreateRealClient(_selectedExchange);
            var quoteCurrency = _selectedExchange == "Bitkub" ? "THB" : "USDT";

            var assetBalance = await client.GetAssetBalanceAsync(quoteCurrency);
            var balance = assetBalance.Available;

            var minRequired = ExchangeFees.GetRecommendedMinBalance(_selectedExchange);

            _logger?.LogInfo("AITradingPage", $"Balance check: {balance} {quoteCurrency}, Min required: {minRequired}");

            return (balance >= minRequired, balance);
        }
        catch (Exception ex)
        {
            _logger?.LogError("AITradingPage", $"Error checking balance: {ex.Message}");
            return (false, 0);
        }
    }

    /// <summary>
    /// Load configured exchanges (those with API credentials) and update UI
    /// Demo Mode = ใช้เงินเสมือน แต่ต้องเชื่อมต่อ API จริงเพื่อดึงข้อมูลราคา/ตลาด
    /// Real Mode = ใช้เงินจริง + เชื่อมต่อ API จริง
    /// </summary>
    private async Task LoadConfiguredExchangesAsync()
    {
        try
        {
            // IMPORTANT: Both Demo and Real mode require API credentials
            // Demo mode only uses virtual money, but real market data from exchanges
            if (_apiCredentialsService == null)
            {
                _logger?.LogWarning("AITradingPage", "ApiCredentialsService not available");
                return;
            }

            // Get list of exchanges with configured API keys
            var configuredExchanges = (await _apiCredentialsService.GetConfiguredExchangesAsync()).ToList();
            _configuredExchanges = configuredExchanges;

            var modeText = _isDemoMode ? "Demo" : "Real";
            _logger?.LogInfo("AITradingPage", $"{modeText} mode: Found {configuredExchanges.Count} configured exchanges: {string.Join(", ", configuredExchanges)}");

            await Dispatcher.InvokeAsync(() =>
            {
                // Update exchange tab visibility based on API credentials
                foreach (var (exchangeName, tab) in _exchangeTabs)
                {
                    var normalizedName = exchangeName.ToLower().Replace(".", "");
                    var hasCredentials = configuredExchanges.Any(e =>
                        e.Equals(exchangeName, StringComparison.OrdinalIgnoreCase) ||
                        e.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));

                    // Show/hide tabs based on credentials
                    // Dimmed appearance for unconfigured exchanges
                    if (hasCredentials)
                    {
                        tab.Opacity = 1.0;
                        tab.IsEnabled = true;
                        tab.ToolTip = _isDemoMode
                            ? "Demo Mode - ข้อมูลจริง + เงินเสมือน"
                            : "Real Mode - ข้อมูลจริง + เงินจริง";
                    }
                    else
                    {
                        tab.Opacity = 0.4;
                        tab.IsEnabled = false;
                        tab.ToolTip = "ยังไม่ได้ตั้งค่า API Key - กรุณาไปที่หน้า Settings";
                    }
                }

                // Auto-select first configured exchange
                if (configuredExchanges.Count > 0)
                {
                    // Find the matching exchange name in _exchangeTabs
                    var firstConfigured = _exchangeTabs.Keys.FirstOrDefault(k =>
                        configuredExchanges.Any(c =>
                            c.Equals(k, StringComparison.OrdinalIgnoreCase) ||
                            c.Equals(k.ToLower().Replace(".", ""), StringComparison.OrdinalIgnoreCase)));

                    if (firstConfigured != null && firstConfigured != _selectedExchange)
                    {
                        _logger?.LogInfo("AITradingPage", $"Auto-selecting configured exchange: {firstConfigured}");
                        SelectExchange(firstConfigured);
                    }
                    else if (firstConfigured == _selectedExchange)
                    {
                        // Already selected correct exchange, just refresh data
                        _logger?.LogInfo("AITradingPage", $"Already on configured exchange: {_selectedExchange}");
                    }
                }
                else
                {
                    _logger?.LogWarning("AITradingPage", "No configured exchanges found - please configure API keys in Settings");
                    // Show warning message
                    ConnectionStatus.Text = "กรุณาตั้งค่า API Key ในหน้า Settings";
                    ConnectionStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")!);
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError("AITradingPage", $"Error loading configured exchanges: {ex.Message}");
        }
    }

    // TradingView chart - no local chart data needed
    private void UpdateTradingViewChart()
    {
        // Update TradingView with current exchange and symbol
        TradingViewChart?.SetSymbol(_selectedExchange, _selectedSymbol);
        TradingViewChart?.SetInterval(_chartInterval);
    }

    private void AddTradeMarkerToChart(string type, decimal price, DateTime time)
    {
        // Add marker to TradingView chart
        if (type.Equals("Buy", StringComparison.OrdinalIgnoreCase))
        {
            var tp = _currentPosition?.TakeProfitPrice;
            var sl = _currentPosition?.StopLossPrice;
            TradingViewChart?.AddBuyMarker(price, time, null, tp, sl);
        }
        else
        {
            TradingViewChart?.AddSellMarker(price, time);
        }
        _logger?.LogInfo("AITradingPage", $"Trade marker added: {type} at {price} ({time})");
    }

    private void UpdatePendingOrdersOnChart()
    {
        // Update TP/SL lines on TradingView chart
        if (_currentPosition != null && _currentPosition.Status == AIPositionStatus.InPosition)
        {
            TradingViewChart?.UpdatePositionLines(
                _currentPosition.TakeProfitPrice,
                _currentPosition.StopLossPrice,
                _currentPosition.EntryPrice);
        }
        else
        {
            TradingViewChart?.ClearPositionLines();
        }
    }

    private async void AITradingPage_Loaded(object sender, RoutedEventArgs e)
    {
        // CRITICAL: Re-acquire services now that App.Services is ready
        // (Constructor runs before App.OnStartup, so App.Services may be null at that time)
        if (_aiTradingService == null)
            _aiTradingService = App.Services?.GetService<IAITradingService>();
        if (_exchangeFactory == null)
            _exchangeFactory = App.Services?.GetService<IExchangeClientFactory>();
        if (_logger == null)
            _logger = App.Services?.GetService<ILoggingService>();
        if (_demoWallet == null)
            _demoWallet = App.Services?.GetService<DemoWalletService>();
        if (_connectionStatusService == null)
            _connectionStatusService = App.Services?.GetService<IConnectionStatusService>();
        if (_apiCredentialsService == null)
            _apiCredentialsService = App.Services?.GetService<IApiCredentialsService>();

        _logger?.LogInfo("AITradingPage", $"Services acquired: AITradingService={_aiTradingService != null}, " +
            $"ExchangeFactory={_exchangeFactory != null}, ApiCredentials={_apiCredentialsService != null}");

        // Preload demo wallet asynchronously (so it's cached for UI)
        if (_demoWallet != null)
        {
            try
            {
                await _demoWallet.GetWalletAsync();
                _logger?.LogInfo("AITradingPage", "Demo wallet preloaded");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("AITradingPage", $"Failed to preload demo wallet: {ex.Message}");
            }
        }

        // Update demo wallet display with cached data
        UpdateDemoWalletDisplay();

        // Load configured exchanges and update UI
        await LoadConfiguredExchangesAsync();

        // Setup event handlers
        if (_aiTradingService != null)
        {
            _aiTradingService.SignalGenerated += AITradingService_SignalGenerated;
            _aiTradingService.PositionOpened += AITradingService_PositionOpened;
            _aiTradingService.PositionClosed += AITradingService_PositionClosed;
            _aiTradingService.TradeCompleted += AITradingService_TradeCompleted;
            _aiTradingService.MarketDataUpdated += AITradingService_MarketDataUpdated;
            _aiTradingService.EmergencyTriggered += AITradingService_EmergencyTriggered;
        }

        // Start update timer (real-time market data - 1 second interval)
        _updateTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _updateTimer.Tick += async (s, args) => await UpdateMarketDataAsync();
        _updateTimer.Start();

        // TradingView chart updates automatically, no timer needed
        // Just set initial symbol
        UpdateTradingViewChart();

        // Initial data load
        await UpdateMarketDataAsync();

        // Check connection
        await CheckExchangeConnectionAsync();

        _logger?.LogInfo("AITradingPage", "AI Trading page loaded");
    }

    private void AITradingPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _updateTimer?.Stop();
        _sessionTimer?.Stop();

        if (_aiTradingService != null)
        {
            _aiTradingService.SignalGenerated -= AITradingService_SignalGenerated;
            _aiTradingService.PositionOpened -= AITradingService_PositionOpened;
            _aiTradingService.PositionClosed -= AITradingService_PositionClosed;
            _aiTradingService.TradeCompleted -= AITradingService_TradeCompleted;
            _aiTradingService.MarketDataUpdated -= AITradingService_MarketDataUpdated;
            _aiTradingService.EmergencyTriggered -= AITradingService_EmergencyTriggered;
        }
    }

    private async Task CheckExchangeConnectionAsync()
    {
        try
        {
            if (_exchangeFactory == null) return;

            // Show connecting status
            await Dispatcher.InvokeAsync(() =>
            {
                ConnectionDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")!);
                ConnectionStatus.Text = "Connecting... / กำลังเชื่อมต่อ...";
                ConnectionStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")!);
            });

            var client = _exchangeFactory.CreateRealClient(_selectedExchange);

            // Use timeout to prevent hanging
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            bool isConnected;
            try
            {
                isConnected = await client.TestConnectionAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger?.LogWarning("AITradingPage", $"Connection timeout for {_selectedExchange}");
                isConnected = false;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (isConnected)
                {
                    ConnectionDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")!);
                    ConnectionStatus.Text = "Connected / เชื่อมต่อแล้ว";
                    ConnectionStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")!);
                }
                else
                {
                    ConnectionDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")!);
                    ConnectionStatus.Text = "Not Connected / ไม่ได้เชื่อมต่อ";
                    ConnectionStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")!);
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError("AITradingPage", $"Connection check error: {ex.Message}");
            await Dispatcher.InvokeAsync(() =>
            {
                ConnectionDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")!);
                ConnectionStatus.Text = $"Error / ผิดพลาด";
                ConnectionStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")!);
            });
        }
    }

    private async Task UpdateMarketDataAsync()
    {
        if (_aiTradingService == null || _isUpdatingMarketData) return;

        _isUpdatingMarketData = true;
        try
        {
            // Use timeout to prevent hanging
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            // Show loading state for Bitkub since it may take longer
            if (_selectedExchange == "Bitkub")
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    CurrentPrice.Text = "Loading...";
                });
            }

            var marketData = await _aiTradingService.GetMarketDataAsync(_selectedExchange, _selectedSymbol, cts.Token);

            if (marketData != null)
            {
                // Bitkub prices are in THB - determine display prefix
                var currencyPrefix = _selectedExchange == "Bitkub" ? "฿" : "$";

                await Dispatcher.InvokeAsync(() =>
                {
                    // Update price display
                    CurrentPrice.Text = $"{currencyPrefix}{marketData.CurrentPrice:N2}";

                    // TradingView chart updates automatically via its own data feed

                    var changeColor = marketData.PriceChangePercent24h >= 0 ? "#10B981" : "#EF4444";
                    var changeSign = marketData.PriceChangePercent24h >= 0 ? "+" : "";
                    PriceChange.Text = $"{changeSign}{marketData.PriceChangePercent24h:F2}%";
                    PriceChange.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(changeColor)!);

                    Volume24h.Text = $"{currencyPrefix}{marketData.Volume24h:N0}";
                    High24h.Text = $"{currencyPrefix}{marketData.High24h:N2}";
                    Low24h.Text = $"{currencyPrefix}{marketData.Low24h:N2}";

                    // Update indicators
                    var indicators = new List<IndicatorValue>();

                    if (marketData.RSI.HasValue)
                    {
                        indicators.Add(new IndicatorValue
                        {
                            ShortName = "RSI",
                            Value = marketData.RSI.Value,
                            Status = marketData.RSI < 30 ? "Bullish" : marketData.RSI > 70 ? "Bearish" : "Neutral",
                            Description = $"RSI at {marketData.RSI.Value:F0}"
                        });
                    }

                    if (marketData.MACD.HasValue)
                    {
                        var macdBullish = marketData.MACD > marketData.MACDSignal;
                        indicators.Add(new IndicatorValue
                        {
                            ShortName = "MACD",
                            Value = marketData.MACDHistogram ?? 0,
                            Status = macdBullish ? "Bullish" : "Bearish",
                            Description = macdBullish ? "Above signal" : "Below signal"
                        });
                    }

                    if (marketData.EMA9.HasValue && marketData.EMA21.HasValue)
                    {
                        var emaBullish = marketData.EMA9 > marketData.EMA21;
                        indicators.Add(new IndicatorValue
                        {
                            ShortName = "EMA 9/21",
                            Value = marketData.EMA9.Value - marketData.EMA21.Value,
                            Status = emaBullish ? "Bullish" : "Bearish",
                            Description = emaBullish ? "Uptrend" : "Downtrend"
                        });
                    }

                    if (marketData.BollingerMiddle.HasValue)
                    {
                        var bbStatus = marketData.CurrentPrice <= marketData.BollingerLower ? "Bullish" :
                                       marketData.CurrentPrice >= marketData.BollingerUpper ? "Bearish" : "Neutral";
                        indicators.Add(new IndicatorValue
                        {
                            ShortName = "BB",
                            Value = marketData.BollingerMiddle.Value,
                            Status = bbStatus,
                            Description = bbStatus == "Bullish" ? "At lower band" : bbStatus == "Bearish" ? "At upper band" : "Within bands"
                        });
                    }

                    if (marketData.Volatility.HasValue)
                    {
                        indicators.Add(new IndicatorValue
                        {
                            ShortName = "VOL",
                            Value = marketData.Volatility.Value,
                            Status = marketData.Volatility > 2 ? "High" : "Neutral",
                            Description = $"Volatility: {marketData.Volatility.Value:F2}%"
                        });
                    }

                    IndicatorsList.ItemsSource = indicators;

                    // Store for market analysis
                    _lastMarketData = marketData;

                    // Update Market Analysis Panel
                    UpdateMarketAnalysisDisplay(marketData);
                });
            }

            // Get current signal (use auto-recommended strategy if enabled)
            if (_isAutoStrategy && _lastMarketData != null)
            {
                var recommendedStrategy = AnalyzeAndRecommendStrategy(_lastMarketData);
                if (recommendedStrategy != _selectedStrategy)
                {
                    // Cooldown: only switch strategy if recommendation is stable for 30+ seconds
                    if (_pendingAutoStrategy == recommendedStrategy)
                    {
                        if ((DateTime.UtcNow - _pendingAutoStrategyTime).TotalSeconds >= 30)
                        {
                            _selectedStrategy = recommendedStrategy;
                            _pendingAutoStrategy = null;
                            await Dispatcher.InvokeAsync(() =>
                            {
                                UpdateStrategySelection(recommendedStrategy);
                                ApplyStrategyDefaults(recommendedStrategy);
                                _logger?.LogInfo("AITradingPage", $"Auto Strategy: Changed to {recommendedStrategy} (stable for 30s)");
                            });
                        }
                    }
                    else
                    {
                        // Start cooldown timer for new recommendation
                        _pendingAutoStrategy = recommendedStrategy;
                        _pendingAutoStrategyTime = DateTime.UtcNow;
                    }
                }
                else
                {
                    _pendingAutoStrategy = null; // Current strategy matches, reset pending
                }
            }

            _config = BuildStrategyConfig();
            var signal = await _aiTradingService.GetCurrentSignalAsync(_selectedExchange, _selectedSymbol, _config);

            if (signal != null)
            {
                _currentSignal = signal;
                await Dispatcher.InvokeAsync(() => UpdateSignalDisplay(signal));
            }

            // Update demo position price in real-time
            if (_isDemoMode && _demoPosition != null && marketData != null)
            {
                _demoPosition.CurrentPrice = marketData.CurrentPrice;

                // Check TP/SL auto-close
                await CheckDemoTPSLAsync(marketData.CurrentPrice);
            }

            // Update position display
            _currentPosition = _aiTradingService.GetCurrentPosition();
            await Dispatcher.InvokeAsync(() => UpdatePositionDisplay());

            // Update session stats
            var stats = _aiTradingService.GetSessionStats();
            await Dispatcher.InvokeAsync(() => UpdateSessionStats(stats));
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("AITradingPage", $"Market data update timed out for {_selectedExchange}:{_selectedSymbol}");
            await Dispatcher.InvokeAsync(() =>
            {
                CurrentPrice.Text = "Timeout";
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError("AITradingPage", $"Error updating market data: {ex.Message}");
            await Dispatcher.InvokeAsync(() =>
            {
                CurrentPrice.Text = "Error";
            });
        }
        finally
        {
            _isUpdatingMarketData = false;
        }
    }

    private Task UpdateChartDataAsync()
    {
        // TradingView chart handles its own data fetching
        // Just update the symbol and interval
        Dispatcher.Invoke(() =>
        {
            UpdateTradingViewChart();
            _logger?.LogInfo("AITradingPage", $"TradingView chart updated: {_selectedExchange}:{_selectedSymbol} interval={_chartInterval}");
        });
        return Task.CompletedTask;
    }

    private AIStrategyConfig BuildStrategyConfig()
    {
        var config = new AIStrategyConfig
        {
            Exchange = _selectedExchange,
            Symbol = _selectedSymbol,
            Mode = _selectedStrategy,
            IsEnabled = _isAIRunning
        };

        // Parse trade settings from UI
        if (decimal.TryParse(TradeAmount.Text, out var amount))
            config.TradeAmountUSDT = amount;

        if (decimal.TryParse(TakeProfit.Text, out var tp))
            config.TakeProfitPercent = tp;

        if (decimal.TryParse(StopLoss.Text, out var sl))
            config.StopLossPercent = sl;

        if (int.TryParse(MaxTradesPerHour.Text, out var maxTrades))
            config.MaxTradesPerHour = maxTrades;

        return config;
    }

    private void UpdateSignalDisplay(AITradingSignal signal)
    {
        // Update signal type display
        if (signal.SignalType == "Buy")
        {
            SignalTypeBox.Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#10B981"), 0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#059669"), 1)
                }
            };
            SignalIcon.Text = "";
            SignalTypeText.Text = "BUY";
        }
        else if (signal.SignalType == "Sell")
        {
            SignalTypeBox.Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#EF4444"), 0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#DC2626"), 1)
                }
            };
            SignalIcon.Text = "";
            SignalTypeText.Text = "SELL";
        }
        else
        {
            SignalTypeBox.Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#F59E0B"), 0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#D97706"), 1)
                }
            };
            SignalIcon.Text = "";
            SignalTypeText.Text = "HOLD";
        }

        // Update confidence
        SignalConfidence.Text = $"{signal.Confidence}%";
        var confColor = signal.Confidence >= 75 ? "#10B981" : signal.Confidence >= 50 ? "#F59E0B" : "#EF4444";
        SignalConfidence.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(confColor));

        // Update confidence bar
        ConfidenceBar.Width = Math.Max(10, signal.Confidence * 2);
        ConfidenceBar.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(confColor));

        // Update entry/target/stop (use ฿ for Bitkub, $ for others)
        var cp = _selectedExchange == "Bitkub" ? "฿" : "$";
        SignalEntry.Text = signal.RecommendedEntryPrice.HasValue ? $"{cp}{signal.RecommendedEntryPrice.Value:N2}" : "-";
        SignalTarget.Text = signal.TargetPrice.HasValue ? $"{cp}{signal.TargetPrice.Value:N2}" : "-";
        SignalStopLoss.Text = signal.StopLossPrice.HasValue ? $"{cp}{signal.StopLossPrice.Value:N2}" : "-";

        // Update reasoning
        AIReasoning.Text = signal.Reasoning;

        // Update status badge
        if (_isAIRunning)
        {
            AIStatus.Text = "RUNNING";
            AIStatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2010B981"));
            AIStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
        }
        else
        {
            AIStatus.Text = "ANALYZING";
            AIStatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20F59E0B"));
            AIStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
        }
    }

    private void UpdatePositionDisplay()
    {
        var currencyPrefix = _selectedExchange == "Bitkub" ? "฿" : "$";

        // Check for demo position first (when in demo mode)
        if (_isDemoMode && _demoPosition != null)
        {
            UpdateDemoPositionDisplay();
            return;
        }

        // Real position display (also shown in demo mode if orphaned real position exists)
        if (_currentPosition != null && _currentPosition.Status == AIPositionStatus.InPosition)
        {
            PositionInfo.Visibility = Visibility.Visible;
            NoPositionText.Visibility = Visibility.Collapsed;

            PositionStatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2010B981")!);
            PositionStatusText.Text = "IN POSITION";
            PositionStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")!);

            PositionEntryPrice.Text = $"{currencyPrefix}{_currentPosition.EntryPrice:N2}";
            PositionCurrentPrice.Text = $"{currencyPrefix}{_currentPosition.CurrentPrice:N2}";
            PositionSize.Text = $"{_currentPosition.Size:N6}";
            PositionValue.Text = $"{currencyPrefix}{_currentPosition.Value:N2}";

            var pnlSign = _currentPosition.UnrealizedPnL >= 0 ? "+" : "";
            PositionPnL.Text = $"{pnlSign}{currencyPrefix}{_currentPosition.UnrealizedPnL:F2}";
            PositionPnLPercent.Text = $" ({pnlSign}{_currentPosition.UnrealizedPnLPercent:F2}%)";

            var pnlColor = _currentPosition.UnrealizedPnL >= 0 ? "#10B981" : "#EF4444";
            PositionPnL.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pnlColor)!);
            PositionPnLPercent.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pnlColor)!);

            PositionTP.Text = _currentPosition.TakeProfitPrice.HasValue ? $"{currencyPrefix}{_currentPosition.TakeProfitPrice.Value:N2}" : "-";
            PositionSL.Text = _currentPosition.StopLossPrice.HasValue ? $"{currencyPrefix}{_currentPosition.StopLossPrice.Value:N2}" : "-";
        }
        else
        {
            PositionInfo.Visibility = Visibility.Collapsed;
            NoPositionText.Visibility = Visibility.Visible;

            PositionStatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20F59E0B")!);
            PositionStatusText.Text = "NO POSITION";
            PositionStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")!);
        }
    }

    private void UpdateSessionStats(AITradingSessionStats stats)
    {
        var pnlSign = stats.TotalRealizedPnL >= 0 ? "+" : "";
        var sessionCp = _selectedExchange == "Bitkub" ? "฿" : "$";
        SessionPnL.Text = $"{pnlSign}{sessionCp}{stats.TotalRealizedPnL:F2}";
        SessionPnL.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            stats.TotalRealizedPnL >= 0 ? "#10B981" : "#EF4444"));

        SessionWinRate.Text = $"{stats.WinRate:F0}%";
        SessionTrades.Text = stats.TotalTrades.ToString();
        SessionWins.Text = stats.WinningTrades.ToString();
        SessionLosses.Text = stats.LosingTrades.ToString();
        SessionProfitFactor.Text = stats.ProfitFactor.ToString("F2");
        SessionDrawdown.Text = $"{stats.MaxDrawdownPercent:F1}%";
    }

    #region Event Handlers

    private void ExchangeTab_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string exchangeName)
        {
            SelectExchange(exchangeName);
        }
    }

    private void SelectExchange(string exchangeName)
    {
        _selectedExchange = exchangeName;

        // Update tab styles
        foreach (var (name, tab) in _exchangeTabs)
        {
            if (name == exchangeName)
            {
                tab.Style = (Style)FindResource("SelectedExchangeTab");
                var textBlock = FindVisualChild<TextBlock>(tab);
                if (textBlock != null) textBlock.Foreground = Brushes.White;
            }
            else
            {
                tab.Style = (Style)FindResource("ExchangeTabStyle");
                var textBlock = FindVisualChild<TextBlock>(tab);
                if (textBlock != null) textBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80FFFFFF")!);
            }
        }

        // Update header
        CurrentExchange.Text = exchangeName.ToUpper();

        // Update exchange badge color
        var badgeColor = exchangeName switch
        {
            "Binance" => "#F3BA2F",
            "KuCoin" => "#23AF91",
            "OKX" => "#FFFFFF",
            "Bybit" => "#F7A600",
            "Gate.io" => "#17E6A1",
            "Bitkub" => "#00B14F",
            _ => "#7C3AED"
        };
        ExchangeBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString($"#20{badgeColor.TrimStart('#')}")!);
        CurrentExchange.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(badgeColor)!);

        // Set default symbol based on exchange - Bitkub uses THB pairs
        var defaultSymbol = exchangeName switch
        {
            "Bitkub" => "BTC/THB",
            _ => "BTC/USDT"
        };

        // Reset session tracking when exchange changes
        _sessionBalanceInitialized = false;
        _sessionStartingBalance = 10000m;

        // Check if current pair list has any pairs for this exchange
        if (!_activePairs.TryGetValue(exchangeName, out var exchangePairs))
        {
            _activePairs[exchangeName] = new List<ActiveTradingPair>();
            exchangePairs = _activePairs[exchangeName];
        }

        if (!exchangePairs.Any())
        {
            AddTradingPair(exchangeName, defaultSymbol, setAsCurrent: true);
        }
        else
        {
            // Select the first pair if any exists
            _currentPair = exchangePairs.FirstOrDefault();
            if (_currentPair != null)
            {
                _selectedSymbol = _currentPair.Symbol;
            }
        }

        // Update UI with the symbol
        SymbolSearch.Text = _selectedSymbol;
        SelectSymbol(_selectedSymbol);

        // Update pair tabs display
        UpdatePairTabsDisplay();

        // Update trading fee display
        UpdateTradingFeeDisplay(exchangeName);

        // Check connection
        _ = CheckExchangeConnectionAsync();

        // Note: SelectSymbol already calls UpdateMarketDataAsync and UpdateChartDataAsync

        _logger?.LogInfo("AITradingPage", $"Selected exchange: {exchangeName}, symbol: {_selectedSymbol}");
    }

    /// <summary>
    /// Update trading fee display based on selected exchange
    /// อัปเดตแสดงผลค่าธรรมเนียมตามกระดานเทรดที่เลือก
    /// </summary>
    private void UpdateTradingFeeDisplay(string exchangeName)
    {
        try
        {
            var feeInfo = ExchangeFees.GetFeeInfo(exchangeName);
            var minTrade = ExchangeFees.GetRecommendedMinTradeAmount(exchangeName);

            Dispatcher.Invoke(() =>
            {
                // Update fee text
                TradingFeeText.Text = $"{feeInfo.TakerFeePercent}%";
                TradingFeeExchange.Text = $" ({feeInfo.ExchangeName})";

                // Update minimum trade recommendation
                if (exchangeName == "Bitkub")
                {
                    MinTradeText.Text = $"ขั้นต่ำแนะนำ: ฿{minTrade:N0} THB";
                }
                else
                {
                    MinTradeText.Text = $"ขั้นต่ำแนะนำ: ${minTrade:N0} USDT";
                }

                // Add discount info if available
                if (!string.IsNullOrEmpty(feeInfo.DiscountToken) && feeInfo.DiscountPercent > 0)
                {
                    TradingFeeExchange.Text += $" | ถือ {feeInfo.DiscountToken} ลด {feeInfo.DiscountPercent}%";
                }
            });

            _logger?.LogInfo("AITradingPage", $"Fee for {exchangeName}: {feeInfo.TakerFeePercent}%, Min: ${minTrade}");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("AITradingPage", $"Failed to update fee display: {ex.Message}");
        }
    }

    private void SymbolSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || SymbolSearch == null) return;
        // Can implement autocomplete here
    }

    private void SearchSymbol_Click(object sender, RoutedEventArgs e)
    {
        var symbol = SymbolSearch.Text.Trim().ToUpper();
        if (!string.IsNullOrEmpty(symbol))
        {
            SelectSymbol(symbol);
        }
    }

    private void QuickPair_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string symbol)
        {
            SymbolSearch.Text = symbol;
            SelectSymbol(symbol);
        }
    }

    private void SelectSymbol(string symbol)
    {
        _selectedSymbol = symbol;
        CurrentSymbol.Text = symbol;

        // Update coin icon
        var baseAsset = symbol.Split('/')[0];
        CurrentCoinIcon.Symbol = baseAsset;

        // Update TradingView chart with new symbol
        UpdateTradingViewChart();

        // Update market data
        _ = UpdateMarketDataAsync();

        _logger?.LogInfo("AITradingPage", $"Selected symbol: {symbol}");
    }

    private void Strategy_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string strategyName)
        {
            SelectStrategy(strategyName);
        }
    }

    private void SelectStrategy(string strategyName)
    {
        // Block manual strategy change while AI is running (auto-strategy handles its own changes)
        if (_isAIRunning && !_isAutoStrategy)
        {
            MessageBox.Show(
                "⚠️ กรุณาหยุด AI Bot ก่อนเปลี่ยนกลยุทธ์\n\nPlease stop AI Bot before changing strategy.\n\nหรือเปิด Auto Strategy เพื่อให้ระบบเปลี่ยนอัตโนมัติ",
                "AI Bot Running / AI กำลังทำงาน",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var newStrategy = strategyName switch
        {
            "Scalping" => AITradingMode.Scalping,
            "Momentum" => AITradingMode.Momentum,
            "MeanReversion" => AITradingMode.MeanReversion,
            "GridTrading" => AITradingMode.GridTrading,
            _ => AITradingMode.Scalping
        };

        _selectedStrategy = newStrategy;

        // Update strategy card styles
        UpdateStrategySelection(strategyName);

        // Update TP/SL/TradeAmount defaults for the selected strategy
        ApplyStrategyDefaults(newStrategy);

        _logger?.LogInfo("AITradingPage", $"Selected strategy: {strategyName}");
    }

    /// <summary>
    /// Apply recommended default TP/SL/trade settings when strategy changes
    /// อัปเดตค่า TP/SL/จำนวนเทรด ตามกลยุทธ์ที่เลือก
    /// </summary>
    private void ApplyStrategyDefaults(AITradingMode strategy)
    {
        // Only update if user hasn't already set custom values (fields are at default or empty)
        var (tp, sl, holdDesc) = strategy switch
        {
            AITradingMode.Scalping => (0.3m, 0.2m, "5-15 min"),
            AITradingMode.Momentum => (2.0m, 1.0m, "1-4 hours"),
            AITradingMode.MeanReversion => (1.5m, 1.0m, "30min-2hours"),
            AITradingMode.GridTrading => (0.5m, 0.5m, "Variable"),
            AITradingMode.Breakout => (3.0m, 1.0m, "30min-4hours"),
            AITradingMode.SmartDCA => (10.0m, 0m, "Long-term"),
            _ => (0.5m, 0.3m, "Variable")
        };

        TakeProfit.Text = tp.ToString("F1");
        StopLoss.Text = sl.ToString("F1");

        _logger?.LogInfo("AITradingPage", $"Applied strategy defaults: TP={tp}%, SL={sl}%, Hold={holdDesc}");
    }

    private void UpdateStrategySelection(string selectedStrategy)
    {
        var strategies = new Dictionary<string, (Border card, Ellipse check)>
        {
            { "Scalping", (ScalpingStrategy, ScalpingCheck) },
            { "Momentum", (MomentumStrategy, MomentumCheck) },
            { "MeanReversion", (MeanReversionStrategy, MeanReversionCheck) },
            { "GridTrading", (GridStrategy, GridCheck) }
        };

        foreach (var (name, (card, check)) in strategies)
        {
            if (name == selectedStrategy)
            {
                card.Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop((Color)ColorConverter.ConvertFromString("#207C3AED"), 0),
                        new GradientStop((Color)ColorConverter.ConvertFromString("#107C3AED"), 1)
                    }
                };
                check.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED"));
                check.Stroke = null;
            }
            else
            {
                card.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#15FFFFFF"));
                check.Fill = null;
                check.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#40FFFFFF"));
                check.StrokeThickness = 2;
            }
        }
    }

    private void ChartInterval_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string interval)
        {
            _chartInterval = interval;

            // Update button styles
            var buttons = new[] { Chart1m, Chart5m, Chart15m, Chart1h, Chart4h, Chart1d };
            foreach (var button in buttons)
            {
                button.Background = button.Tag?.ToString() == interval
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20FFFFFF"));
            }

            // Update TradingView chart interval
            UpdateTradingViewChart();
            _logger?.LogInfo("AITradingPage", $"Chart interval changed to: {interval}");
        }
    }

    private void AIOverlayToggle_Click(object sender, MouseButtonEventArgs e)
    {
        // Toggle overlay visibility
        var isVisible = TradingViewChart?.IsOverlayVisible ?? true;
        var newState = !isVisible;

        TradingViewChart?.SetOverlayVisible(newState);

        // Update toggle UI
        if (newState)
        {
            AIOverlayToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#207C3AED")!);
            AIOverlayDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED")!);
            AIOverlayText.Text = "ON";
            AIOverlayText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED")!);
        }
        else
        {
            AIOverlayToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20FFFFFF")!);
            AIOverlayDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60FFFFFF")!);
            AIOverlayText.Text = "OFF";
            AIOverlayText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60FFFFFF")!);
        }

        _logger?.LogInfo("AITradingPage", $"AI overlay toggled: {(newState ? "ON" : "OFF")}");
    }

    private void MockTrades_Click(object sender, RoutedEventArgs e)
    {
        // Add mock trade markers for demonstration
        TradingViewChart?.ClearMarkers();

        var now = DateTime.UtcNow;
        var random = new Random();

        // Generate realistic mock trades
        var basePrice = 95000m; // BTC approximate price

        // Mock trade 1: Buy signal 30 minutes ago
        var buy1Time = now.AddMinutes(-30);
        var buy1Price = basePrice - random.Next(100, 500);
        TradingViewChart?.AddBuyMarker(buy1Price, buy1Time, $"AI BUY @ ${buy1Price:N0}", buy1Price * 1.02m, buy1Price * 0.98m);

        // Mock trade 2: Sell (closed position) 15 minutes ago
        var sell1Time = now.AddMinutes(-15);
        var sell1Price = buy1Price + random.Next(200, 800);
        TradingViewChart?.AddSellMarker(sell1Price, sell1Time, $"AI SELL @ ${sell1Price:N0} (+{((sell1Price - buy1Price) / buy1Price * 100):F2}%)");

        // Mock trade 3: Current buy signal 5 minutes ago
        var buy2Time = now.AddMinutes(-5);
        var buy2Price = basePrice + random.Next(-200, 200);
        TradingViewChart?.AddBuyMarker(buy2Price, buy2Time, $"AI BUY @ ${buy2Price:N0}", buy2Price * 1.015m, buy2Price * 0.99m);

        // Update position lines for current position
        TradingViewChart?.UpdatePositionLines(buy2Price * 1.015m, buy2Price * 0.99m, buy2Price);

        _logger?.LogInfo("AITradingPage", "Mock trades added to chart for demonstration");

        // Show info message
        MessageBox.Show(
            "ตัวอย่างจุดซื้อขาย AI ถูกเพิ่มลงบนกราฟแล้ว\n\n" +
            "▲ สีเขียว = จุดที่ AI ซื้อ\n" +
            "▼ สีแดง = จุดที่ AI ขาย\n\n" +
            "📍 Entry = ราคาที่เข้าซื้อ\n" +
            "🎯 TP = Take Profit\n" +
            "🛑 SL = Stop Loss\n\n" +
            "คุณสามารถเปิด/ปิด overlay ได้ที่ปุ่ม AI Markers",
            "Demo Mode",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void ManualBuy_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSignal == null || _aiTradingService == null) return;

        if (!decimal.TryParse(TradeAmount.Text, out var amount) || amount <= 0)
        {
            MessageBox.Show("กรุณาใส่จำนวนเงินที่ถูกต้อง / Please enter a valid trade amount.", "Invalid Amount",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ManualBuyBtn.IsEnabled = false;
        ManualBuyBtn.Content = "Buying...";

        try
        {
            if (_isDemoMode)
            {
                // Demo Mode: Use DemoWallet
                await ExecuteDemoBuyAsync(amount);
            }
            else
            {
                // Real Mode: Use real exchange API
                _currentSignal.SignalType = "Buy";
                await _aiTradingService.ExecuteManualTradeAsync(_currentSignal, amount);
                _logger?.LogInfo("AITradingPage", $"[REAL] Manual buy executed: {_selectedSymbol} - ${amount}");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("AITradingPage", $"Manual buy error: {ex.Message}");
            MessageBox.Show($"Buy order failed: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ManualBuyBtn.IsEnabled = true;
            ManualBuyBtn.Content = "▲ BUY";
        }
    }

    private async void ManualSell_Click(object sender, RoutedEventArgs e)
    {
        // Check for demo position OR real position (also check real position in demo mode)
        var hasPosition = (_isDemoMode && _demoPosition != null)
                       || (!_isDemoMode && _currentPosition != null)
                       || (_isDemoMode && _currentPosition != null && _currentPosition.Status == AIPositionStatus.InPosition);

        if (_aiTradingService == null || !hasPosition)
        {
            MessageBox.Show("ไม่มี position ที่จะขาย / No position to sell.", "No Position",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ManualSellBtn.IsEnabled = false;
        ManualSellBtn.Content = "Selling...";

        try
        {
            if (_isDemoMode && _demoPosition != null)
            {
                // Demo Mode: Use DemoWallet
                await ExecuteDemoSellAsync();
            }
            else if (_currentPosition != null)
            {
                // Real Mode (or orphaned real position in demo mode): Use real exchange API
                await _aiTradingService.ClosePositionAsync("Manual");
                _logger?.LogInfo("AITradingPage", "[REAL] Manual sell executed");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("AITradingPage", $"Manual sell error: {ex.Message}");
            MessageBox.Show($"Sell order failed: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ManualSellBtn.IsEnabled = true;
            ManualSellBtn.Content = "▼ SELL";
        }
    }

    /// <summary>
    /// Execute demo buy - creates position using DemoWallet
    /// ซื้อเสมือน - สร้าง position โดยใช้ DemoWallet
    /// </summary>
    private async Task ExecuteDemoBuyAsync(decimal amountUSDT)
    {
        if (_demoWallet == null || _currentSignal == null || _aiTradingService == null)
        {
            MessageBox.Show("Demo wallet ไม่พร้อม / Demo wallet not available", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Check if already has position
        if (_demoPosition != null)
        {
            MessageBox.Show("มี position อยู่แล้ว กรุณาปิดก่อน / Already have position. Please close first.", "Position Exists",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var price = _currentSignal.RecommendedEntryPrice ?? 0;
        if (price <= 0)
        {
            // Get current market price
            var marketData = await _aiTradingService.GetMarketDataAsync(_selectedExchange, _selectedSymbol);
            price = marketData?.CurrentPrice ?? 0;
        }

        if (price <= 0)
        {
            MessageBox.Show("ไม่สามารถดึงราคาได้ / Cannot get price", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var quantity = amountUSDT / price;

        // Execute buy in demo wallet
        var result = await _demoWallet.ExecuteAIBuyAsync(_selectedSymbol, _selectedExchange, quantity, price);

        if (result.Success)
        {
            // Create demo position
            _demoPosition = new DemoAIPosition
            {
                Symbol = _selectedSymbol,
                Exchange = _selectedExchange,
                EntryPrice = price,
                CurrentPrice = price,
                Quantity = quantity,
                TakeProfitPrice = _currentSignal.TargetPrice ?? price * 1.01m,
                StopLossPrice = _currentSignal.StopLossPrice ?? price * 0.995m,
                EntryTime = DateTime.Now,
                Strategy = _selectedStrategy
            };

            // Add trade marker to chart
            AddTradeMarkerToChart("Buy", price, DateTime.Now);

            // Update displays
            UpdateDemoWalletDisplay();
            UpdateDemoPositionDisplay();

            var cp = _selectedExchange == "Bitkub" ? "฿" : "$";
            _logger?.LogInfo("AITradingPage", $"[DEMO] Buy executed: {_selectedSymbol} | {quantity:N6} @ {cp}{price:N2}");

            MessageBox.Show(
                $"✅ ซื้อสำเร็จ (Demo Mode)\n\n" +
                $"Symbol: {_selectedSymbol}\n" +
                $"Quantity: {quantity:N6}\n" +
                $"Price: {cp}{price:N2}\n" +
                $"Total: {cp}{amountUSDT:N2}\n\n" +
                $"🎯 TP: {cp}{_demoPosition.TakeProfitPrice:N2}\n" +
                $"🛑 SL: {cp}{_demoPosition.StopLossPrice:N2}",
                "Demo Buy Success",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show($"ซื้อไม่สำเร็จ: {result.Message}", "Buy Failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Execute demo sell - closes position using DemoWallet
    /// ขายเสมือน - ปิด position โดยใช้ DemoWallet
    /// </summary>
    private async Task ExecuteDemoSellAsync()
    {
        if (_demoWallet == null || _demoPosition == null || _aiTradingService == null)
        {
            MessageBox.Show("ไม่มี position ที่จะขาย / No position to sell", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Get current price
        var marketData = await _aiTradingService.GetMarketDataAsync(_selectedExchange, _selectedSymbol);
        var exitPrice = marketData?.CurrentPrice ?? _demoPosition.CurrentPrice;

        // Execute sell in demo wallet
        var result = await _demoWallet.ExecuteAISellAsync(
            _demoPosition.Symbol,
            _demoPosition.Exchange,
            _demoPosition.Quantity,
            _demoPosition.EntryPrice,
            exitPrice);

        if (result.Success)
        {
            // Add trade marker to chart
            AddTradeMarkerToChart("Sell", exitPrice, DateTime.Now);

            var profitSign = result.Profit >= 0 ? "+" : "";
            _logger?.LogInfo("AITradingPage", $"[DEMO] Sell executed: {_demoPosition.Symbol} | PnL: {profitSign}${result.Profit:N2} ({profitSign}{result.ProfitPercent:N2}%)");

            // Clear position lines from chart
            TradingViewChart?.ClearPositionLines();

            // Clear demo position
            _demoPosition = null;

            // Update displays
            UpdateDemoWalletDisplay();
            UpdateDemoPositionDisplay();

            var profitColor = result.Profit >= 0 ? "🟢" : "🔴";
            var cp = _selectedExchange == "Bitkub" ? "฿" : "$";
            MessageBox.Show(
                $"{profitColor} ขายสำเร็จ (Demo Mode)\n\n" +
                $"Exit Price: {cp}{exitPrice:N2}\n" +
                $"PnL: {profitSign}{cp}{result.Profit:N2} ({profitSign}{result.ProfitPercent:N2}%)\n" +
                $"New Balance: {cp}{result.NewBalance:N2}",
                result.Profit >= 0 ? "Demo Sell - Profit!" : "Demo Sell - Loss",
                MessageBoxButton.OK,
                result.Profit >= 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show($"ขายไม่สำเร็จ: {result.Message}", "Sell Failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Check TP/SL for demo position - auto close if hit
    /// ตรวจสอบ Take Profit / Stop Loss สำหรับ demo position
    /// </summary>
    private async Task CheckDemoTPSLAsync(decimal currentPrice)
    {
        if (_demoPosition == null || _demoWallet == null || _isClosingDemoPosition) return;

        _isClosingDemoPosition = true;
        try
        {
        // Check Take Profit
        if (currentPrice >= _demoPosition.TakeProfitPrice)
        {
            _logger?.LogInfo("AITradingPage", $"[DEMO] TP Hit! Price {currentPrice:N2} >= TP {_demoPosition.TakeProfitPrice:N2}");

            var result = await _demoWallet.ExecuteAISellAsync(
                _demoPosition.Symbol,
                _demoPosition.Exchange,
                _demoPosition.Quantity,
                _demoPosition.EntryPrice,
                currentPrice);

            if (result.Success)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    AddTradeMarkerToChart("Sell", currentPrice, DateTime.Now);
                    TradingViewChart?.ClearPositionLines();

                    _logger?.LogInfo("AITradingPage", $"[DEMO] TP Closed: PnL +${result.Profit:N2} (+{result.ProfitPercent:N2}%)");

                    _demoPosition = null;
                    UpdateDemoWalletDisplay();
                    UpdateDemoPositionDisplay();

                    // Show notification
                    var tpCp = _selectedExchange == "Bitkub" ? "฿" : "$";
                    MessageBox.Show(
                        $"🎯 Take Profit Hit!\n\n" +
                        $"Exit Price: {tpCp}{currentPrice:N2}\n" +
                        $"Profit: +{tpCp}{result.Profit:N2} (+{result.ProfitPercent:N2}%)\n" +
                        $"New Balance: {tpCp}{result.NewBalance:N2}",
                        "Demo TP Triggered",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                });
            }
            return;
        }

        // Check Stop Loss
        if (currentPrice <= _demoPosition.StopLossPrice)
        {
            _logger?.LogInfo("AITradingPage", $"[DEMO] SL Hit! Price {currentPrice:N2} <= SL {_demoPosition.StopLossPrice:N2}");

            var result = await _demoWallet.ExecuteAISellAsync(
                _demoPosition.Symbol,
                _demoPosition.Exchange,
                _demoPosition.Quantity,
                _demoPosition.EntryPrice,
                currentPrice);

            if (result.Success)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    AddTradeMarkerToChart("Sell", currentPrice, DateTime.Now);
                    TradingViewChart?.ClearPositionLines();

                    _logger?.LogInfo("AITradingPage", $"[DEMO] SL Closed: PnL ${result.Profit:N2} ({result.ProfitPercent:N2}%)");

                    _demoPosition = null;
                    UpdateDemoWalletDisplay();
                    UpdateDemoPositionDisplay();

                    // Show notification
                    var slCp = _selectedExchange == "Bitkub" ? "฿" : "$";
                    MessageBox.Show(
                        $"🛑 Stop Loss Hit!\n\n" +
                        $"Exit Price: {slCp}{currentPrice:N2}\n" +
                        $"Loss: {slCp}{result.Profit:N2} ({result.ProfitPercent:N2}%)\n" +
                        $"New Balance: {slCp}{result.NewBalance:N2}",
                        "Demo SL Triggered",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
            }
        }
        }
        finally
        {
            _isClosingDemoPosition = false;
        }
    }

    /// <summary>
    /// Update demo position display on UI
    /// อัปเดตการแสดงผล position เสมือน
    /// </summary>
    private void UpdateDemoPositionDisplay()
    {
        if (_demoPosition != null)
        {
            // Show position info
            PositionInfo.Visibility = Visibility.Visible;
            NoPositionText.Visibility = Visibility.Collapsed;

            PositionStatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2010B981")!);
            PositionStatusText.Text = "DEMO POSITION";
            PositionStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")!);

            var currencyPrefix = _selectedExchange == "Bitkub" ? "฿" : "$";

            PositionEntryPrice.Text = $"{currencyPrefix}{_demoPosition.EntryPrice:N2}";
            PositionCurrentPrice.Text = $"{currencyPrefix}{_demoPosition.CurrentPrice:N2}";
            PositionSize.Text = $"{_demoPosition.Quantity:N6}";
            PositionValue.Text = $"{currencyPrefix}{_demoPosition.Value:N2}";

            var pnl = _demoPosition.UnrealizedPnL;
            var pnlPercent = _demoPosition.UnrealizedPnLPercent;
            var pnlSign = pnl >= 0 ? "+" : "";
            var pnlColor = pnl >= 0 ? "#10B981" : "#EF4444";

            PositionPnL.Text = $"{pnlSign}{currencyPrefix}{pnl:N2}";
            PositionPnL.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pnlColor)!);
            PositionPnLPercent.Text = $" ({pnlSign}{pnlPercent:N2}%)";
            PositionPnLPercent.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pnlColor)!);

            PositionTP.Text = $"{currencyPrefix}{_demoPosition.TakeProfitPrice:N2}";
            PositionSL.Text = $"{currencyPrefix}{_demoPosition.StopLossPrice:N2}";

            // Update chart position lines
            TradingViewChart?.UpdatePositionLines(
                _demoPosition.TakeProfitPrice,
                _demoPosition.StopLossPrice,
                _demoPosition.EntryPrice);
        }
        else
        {
            // No position
            PositionInfo.Visibility = Visibility.Collapsed;
            NoPositionText.Visibility = Visibility.Visible;

            PositionStatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20F59E0B")!);
            PositionStatusText.Text = "NO POSITION";
            PositionStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")!);
        }
    }

    private async void AITradeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_aiTradingService == null) return;

        if (_isAIRunning)
        {
            // Stop AI trading
            await _aiTradingService.StopAsync();
            _isAIRunning = false;

            // Update current pair state
            if (_currentPair != null)
            {
                _currentPair.IsAIRunning = false;
                _currentPair.SessionStartTime = null;
                _currentPair.SessionDuration = TimeSpan.Zero;
            }

            // Stop session timer
            StopSessionTimer();

            // Update toggle button UI
            UpdateAIToggleButtonState(false);

            // Hide running indicator
            AIRunningIndicator.Visibility = Visibility.Collapsed;
            SessionTimerPanel.Visibility = Visibility.Collapsed;

            // Update pair tabs to reflect stopped state
            UpdatePairTabsDisplay();

            _logger?.LogInfo("AITradingPage", "AI trading stopped");
        }
        else
        {
            // REAL MODE: Check minimum balance before starting AI
            if (!_isDemoMode)
            {
                var minBalanceCheck = await CheckMinimumBalanceAsync();
                if (!minBalanceCheck.HasEnough)
                {
                    var currencySymbol = _selectedExchange == "Bitkub" ? "฿" : "$";
                    var minAmount = ExchangeFees.GetRecommendedMinBalance(_selectedExchange);

                    MessageBox.Show(
                        $"❌ ยอดเงินไม่เพียงพอ!\n\n" +
                        $"ยอดเงินปัจจุบัน: {currencySymbol}{minBalanceCheck.CurrentBalance:N2}\n" +
                        $"ขั้นต่ำที่ต้องการ: {currencySymbol}{minAmount:N2}\n\n" +
                        $"กรุณาเติมเงินในบัญชี {_selectedExchange} ก่อนเริ่ม AI Trading\n\n" +
                        $"💡 TIP: สลับไป Demo Mode เพื่อทดสอบโดยไม่ใช้เงินจริง",
                        "⚠️ ยอดเงินไม่เพียงพอ / Insufficient Balance",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            // Start AI trading
            _config = BuildStrategyConfig();
            await _aiTradingService.StartAsync(_selectedExchange, _selectedSymbol, _config);
            _isAIRunning = true;

            // Parse session duration
            _sessionDuration = GetSelectedDuration();

            // Update current pair state
            if (_currentPair != null)
            {
                _currentPair.IsAIRunning = true;
                _currentPair.SessionStartTime = DateTime.Now;
                _currentPair.SessionDuration = _sessionDuration;
            }

            // Start session timer
            StartSessionTimer();

            // Update toggle button UI
            UpdateAIToggleButtonState(true);

            // Show running indicator
            AIRunningIndicator.Visibility = Visibility.Visible;
            SessionTimerPanel.Visibility = Visibility.Visible;

            // Update pair tabs to reflect running state
            UpdatePairTabsDisplay();

            _logger?.LogInfo("AITradingPage", $"AI trading started: {_selectedExchange} - {_selectedSymbol}");
        }
    }

    private void StartSessionTimer()
    {
        _sessionStartTime = DateTime.Now;

        // Parse session duration from ComboBox
        _sessionDuration = GetSelectedDuration();

        // Initialize timer display
        SessionTimer.Text = "00:00:00";

        // Create and start session timer
        _sessionTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _sessionTimer.Tick += SessionTimer_Tick;
        _sessionTimer.Start();

        _logger?.LogInfo("AITradingPage", $"Session timer started. Duration limit: {(_sessionDuration == TimeSpan.MaxValue ? "Unlimited" : _sessionDuration.ToString())}");
    }

    private void StopSessionTimer()
    {
        _sessionTimer?.Stop();
        _sessionTimer = null;
        SessionTimer.Text = "00:00:00";
    }

    private void SessionTimer_Tick(object? sender, EventArgs e)
    {
        var elapsed = DateTime.Now - _sessionStartTime;
        SessionTimer.Text = elapsed.ToString(@"hh\:mm\:ss");

        // Check if session duration limit reached (if not unlimited)
        if (_sessionDuration != TimeSpan.MaxValue && elapsed >= _sessionDuration)
        {
            _logger?.LogInfo("AITradingPage", $"Session duration limit reached ({_sessionDuration}). Auto-stopping AI.");

            // Auto-stop AI trading
            Dispatcher.InvokeAsync(async () =>
            {
                if (_isAIRunning && _aiTradingService != null)
                {
                    await _aiTradingService.StopAsync();
                    _isAIRunning = false;

                    StopSessionTimer();

                    // Update UI
                    AIToggleIcon.Text = "▶";
                    AIToggleText.Text = "START AI";
                    AITradeToggle.Background = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(1, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop((Color)ColorConverter.ConvertFromString("#7C3AED")!, 0),
                            new GradientStop((Color)ColorConverter.ConvertFromString("#5B21B6")!, 1)
                        }
                    };

                    AIRunningIndicator.Visibility = Visibility.Collapsed;
                    SessionTimerPanel.Visibility = Visibility.Collapsed;

                    // Show notification
                    AIRunningText.Text = "หมดเวลาเทรด";
                    AIRunningIndicator.Visibility = Visibility.Visible;
                    await Task.Delay(3000);
                    AIRunningIndicator.Visibility = Visibility.Collapsed;
                }
            });
        }
    }

    private TimeSpan GetSelectedDuration()
    {
        // Map ComboBox selection to TimeSpan
        return TradeDurationCombo.SelectedIndex switch
        {
            0 => TimeSpan.FromMinutes(30),
            1 => TimeSpan.FromHours(1),
            2 => TimeSpan.FromHours(2),
            3 => TimeSpan.FromHours(4),
            4 => TimeSpan.FromHours(8),
            5 => TimeSpan.MaxValue, // Unlimited
            _ => TimeSpan.FromHours(2) // Default
        };
    }

    private async void ClosePosition_Click(object sender, RoutedEventArgs e)
    {
        // Handle demo mode positions
        if (_isDemoMode && _demoPosition != null && _demoWallet != null)
        {
            var currencyPrefix = _selectedExchange == "Bitkub" ? "฿" : "$";
            var pnl = _demoPosition.UnrealizedPnL;
            var confirmResult = MessageBox.Show(
                $"Close demo position for {_demoPosition.Symbol}?\n\nCurrent P&L: {(pnl >= 0 ? "+" : "")}{currencyPrefix}{pnl:F2}",
                "Close Demo Position",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmResult == MessageBoxResult.Yes)
            {
                try
                {
                    var exitPrice = _demoPosition.CurrentPrice;
                    var sellResult = await _demoWallet.ExecuteAISellAsync(
                        _demoPosition.Symbol,
                        _demoPosition.Exchange,
                        _demoPosition.Quantity,
                        _demoPosition.EntryPrice,
                        exitPrice);

                    if (sellResult.Success)
                    {
                        AddTradeMarkerToChart("Sell", exitPrice, DateTime.Now);
                        TradingViewChart?.ClearPositionLines();
                        _demoPosition = null;
                        UpdateDemoWalletDisplay();
                        UpdateDemoPositionDisplay();
                        _logger?.LogInfo("AITradingPage", "[DEMO] Position closed manually");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError("AITradingPage", $"Error closing demo position: {ex.Message}");
                }
            }
            return;
        }

        // Handle real mode positions
        if (_aiTradingService == null || _currentPosition == null) return;

        var result = MessageBox.Show(
            $"Close position for {_currentPosition.Symbol}?\n\nCurrent P&L: {(_currentPosition.UnrealizedPnL >= 0 ? "+" : "")}${_currentPosition.UnrealizedPnL:F2}",
            "Close Position",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                await _aiTradingService.ClosePositionAsync("Manual");
                _logger?.LogInfo("AITradingPage", "Position closed manually");
            }
            catch (Exception ex)
            {
                _logger?.LogError("AITradingPage", $"Error closing position: {ex.Message}");
            }
        }
    }

    private async void EmergencyStop_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "EMERGENCY STOP\n\nThis will immediately:\n- Close all open positions\n- Stop AI trading\n\nAre you sure?",
            "Emergency Stop",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes && _aiTradingService != null)
        {
            // Close demo position if exists
            if (_isDemoMode && _demoPosition != null && _demoWallet != null)
            {
                try
                {
                    var exitPrice = _demoPosition.CurrentPrice;
                    await _demoWallet.ExecuteAISellAsync(
                        _demoPosition.Symbol,
                        _demoPosition.Exchange,
                        _demoPosition.Quantity,
                        _demoPosition.EntryPrice,
                        exitPrice);
                    _demoPosition = null;
                    UpdateDemoWalletDisplay();
                    UpdateDemoPositionDisplay();
                    TradingViewChart?.ClearPositionLines();
                }
                catch (Exception ex)
                {
                    _logger?.LogError("AITradingPage", $"Error closing demo position during emergency stop: {ex.Message}");
                    _demoPosition = null; // Force clear even if sell fails
                }
            }

            // Only call real EmergencyStop if NOT in demo mode (or if real position exists)
            if (!_isDemoMode || (_currentPosition != null && _currentPosition.Status == AIPositionStatus.InPosition))
            {
                await _aiTradingService.EmergencyStopAsync();
            }
            else
            {
                // In demo mode with no real position, just stop the AI loop
                await _aiTradingService.StopAsync();
            }
            _isAIRunning = false;

            // Stop session timer
            StopSessionTimer();

            // Update toggle button UI
            AIToggleIcon.Text = "▶";
            AIToggleText.Text = "START AI";
            AITradeToggle.Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#7C3AED")!, 0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#5B21B6")!, 1)
                }
            };

            // Hide running indicator
            AIRunningIndicator.Visibility = Visibility.Collapsed;
            SessionTimerPanel.Visibility = Visibility.Collapsed;

            _logger?.LogCritical("AITradingPage", "EMERGENCY STOP activated!");
        }
    }

    #endregion

    #region Service Event Handlers

    private void AITradingService_SignalGenerated(object? sender, AISignalEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _currentSignal = e.Signal;
            UpdateSignalDisplay(e.Signal);
        });
    }

    private void AITradingService_PositionOpened(object? sender, AIPositionEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _currentPosition = e.Position;
            UpdatePositionDisplay();

            // Add BUY marker to chart
            if (e.Position.EntryTime.HasValue)
            {
                AddTradeMarkerToChart("Buy", e.Position.EntryPrice, e.Position.EntryTime.Value);
            }

            // Update pending orders (TP/SL)
            UpdatePendingOrdersOnChart();
        });
    }

    private void AITradingService_PositionClosed(object? sender, AIPositionEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // Add SELL marker to chart at current price
            if (e.Position != null)
            {
                AddTradeMarkerToChart("Sell", e.Position.CurrentPrice, DateTime.UtcNow);
            }

            _currentPosition = null;
            UpdatePositionDisplay();

            // Clear pending orders from chart (TODO: implement when markers are added)
            // _pendingMarkers.Clear();
        });
    }

    private void AITradingService_TradeCompleted(object? sender, AITradeEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var trade = e.Trade;
            var msg = trade.IsWin ? "WIN" : "LOSS";
            _logger?.LogInfo("AITradingPage", $"Trade completed: {msg} - P&L: ${trade.NetPnL:F2} ({trade.PnLPercent:F2}%)");

            // Add exit marker to chart
            AddTradeMarkerToChart("Sell", trade.ExitPrice, trade.ExitTime);

            // Update stats
            if (_aiTradingService != null)
            {
                var stats = _aiTradingService.GetSessionStats();
                UpdateSessionStats(stats);
            }
        });
    }

    private void AITradingService_MarketDataUpdated(object? sender, AIMarketDataEventArgs e)
    {
        // Already handled in UpdateMarketDataAsync
    }

    private void AITradingService_EmergencyTriggered(object? sender, AIEmergencyEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            MessageBox.Show(
                $"EMERGENCY TRIGGERED!\n\nReason: {e.Reason}\nLoss: ${e.LossAmount:F2} ({e.LossPercent:F2}%)",
                "Emergency",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _isAIRunning = false;
            UpdateAIToggleButtonState(false);
        });
    }

    #endregion

    #region Demo Mode & Multi-Pair Handlers

    /// <summary>
    /// Toggle between Demo and Real trading mode
    /// Uses MouseButtonEventArgs for MouseLeftButtonDown event
    /// </summary>
    private async void ToggleDemoMode_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        _logger?.LogInfo("AITradingPage", $"Toggle clicked! Current _isDemoMode={_isDemoMode}");

        // CRITICAL: Must stop AI bot before switching modes
        if (_isAIRunning)
        {
            MessageBox.Show(
                "⚠️ กรุณาหยุด AI Bot ก่อนสลับโหมด\n\nPlease stop AI Bot before switching modes.\n\nกดปุ่ม 'STOP AI' ก่อน แล้วลองอีกครั้ง",
                "AI Bot Running / AI กำลังทำงาน",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        bool currentMode = _isDemoMode;

        if (currentMode)
        {
            // Currently Demo -> Switch to Real (need confirmation)
            var result = MessageBox.Show(
                "🔴 SWITCH TO REAL MODE?\n\n" +
                "⚠️ WARNING: This will use REAL FUNDS from your exchange!\n\n" +
                "• ใช้เงินจริงจาก Exchange ที่เชื่อมต่อ\n" +
                "• ต้องตั้งค่า API Key ก่อน\n" +
                "• การเทรดจะมีผลจริงในบัญชี\n\n" +
                "Are you sure you want to continue?\nคุณแน่ใจหรือไม่?",
                "⚠️ REAL MODE / โหมดเทรดจริง",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _isDemoMode = false;
                _sessionBalanceInitialized = false;
                _demoPosition = null;

                _logger?.LogInfo("AITradingPage", "Switched to REAL mode");

                UpdateDemoWalletDisplay();
                UpdatePositionDisplay();
                await LoadConfiguredExchangesAsync();
            }
        }
        else
        {
            // Currently Real -> Switch to Demo
            // CRITICAL: Warn if real position is still open
            if (_currentPosition != null && _currentPosition.Status == AIPositionStatus.InPosition)
            {
                var warnResult = MessageBox.Show(
                    "⚠️ คุณมี Position จริงที่ยังเปิดอยู่!\n\n" +
                    "You have an open REAL position!\n\n" +
                    $"• {_currentPosition.Symbol} @ ${_currentPosition.EntryPrice:N2}\n" +
                    $"• Size: {_currentPosition.Size:N6}\n" +
                    $"• PnL: {(_currentPosition.UnrealizedPnL >= 0 ? "+" : "")}{_currentPosition.UnrealizedPnL:F2}\n\n" +
                    "Position จะยังคงเปิดอยู่บน Exchange จริง\n" +
                    "The position will remain open on the real exchange.\n\n" +
                    "ต้องการสลับไป Demo หรือไม่?",
                    "⚠️ Open Real Position / มี Position จริง",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (warnResult != MessageBoxResult.Yes)
                    return;
            }

            _isDemoMode = true;
            _sessionBalanceInitialized = false;

            _logger?.LogInfo("AITradingPage", $"Switched to DEMO mode - _isDemoMode is now: {_isDemoMode}, static: {_staticIsDemoMode}");

            // Update UI immediately
            UpdateDemoWalletDisplay();
            UpdatePositionDisplay();

            // Load demo wallet in background
            if (_demoWallet != null)
            {
                try
                {
                    await _demoWallet.GetWalletAsync();
                    UpdateDemoWalletDisplay(); // Update again with fresh wallet data
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning("AITradingPage", $"Error loading wallet: {ex.Message}");
                }
            }

            await LoadConfiguredExchangesAsync();

            // Show brief confirmation
            _logger?.LogInfo("AITradingPage", $"DEMO mode switch complete - Final _isDemoMode: {_isDemoMode}");
        }
    }

    private void PairTab_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string symbol)
        {
            SelectActivePair(symbol);
        }
    }

    private void SelectActivePair(string symbol)
    {
        if (!_activePairs.TryGetValue(_selectedExchange, out var pairs)) return;
        var pair = pairs.FirstOrDefault(p => p.Symbol == symbol);
        if (pair == null) return;

        _currentPair = pair;
        _selectedSymbol = symbol;
        _selectedStrategy = pair.Strategy;
        _isAIRunning = pair.IsAIRunning;
        _currentPosition = pair.Position;
        _currentSignal = pair.CurrentSignal;

        // Update UI
        SymbolSearch.Text = symbol;
        SelectSymbol(symbol);
        UpdateStrategySelection(pair.Strategy.ToString());
        UpdatePairTabsDisplay();

        // Update position display with current pair's data
        UpdatePositionDisplay();

        // Update signal display with current pair's signal
        if (_currentSignal != null)
        {
            UpdateSignalDisplay(_currentSignal);
        }
        else
        {
            // Reset signal display to default/neutral state
            ResetSignalDisplay();
        }

        // Update session timer visibility based on pair's AI running state
        UpdateSessionTimerForPair(pair);

        // Update AI toggle button state
        UpdateAIToggleButtonState(_isAIRunning);

        _logger?.LogInfo("AITradingPage", $"Switched to pair: {symbol}, AI Running: {_isAIRunning}");
    }

    /// <summary>
    /// Reset signal display to neutral/default state
    /// </summary>
    private void ResetSignalDisplay()
    {
        SignalTypeBox.Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop((Color)ColorConverter.ConvertFromString("#6B7280"), 0),
                new GradientStop((Color)ColorConverter.ConvertFromString("#4B5563"), 1)
            }
        };
        SignalIcon.Text = "⏳";
        SignalTypeText.Text = "WAIT";
        SignalConfidence.Text = "--%";
        SignalConfidence.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));
        ConfidenceBar.Width = 10;
        ConfidenceBar.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4B5563"));
        SignalEntry.Text = "-";
        SignalTarget.Text = "-";
        SignalStopLoss.Text = "-";
        AIReasoning.Text = "Waiting for AI analysis...";

        AIStatus.Text = "READY";
        AIStatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#206B7280"));
        AIStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));
        AIStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));
    }

    /// <summary>
    /// Update session timer visibility based on pair's AI state
    /// </summary>
    private void UpdateSessionTimerForPair(ActiveTradingPair pair)
    {
        if (pair.IsAIRunning && pair.SessionStartTime.HasValue)
        {
            // Show timer and update it
            SessionTimerPanel.Visibility = Visibility.Visible;
            AIRunningIndicator.Visibility = Visibility.Visible;

            // Update timer display
            _sessionStartTime = pair.SessionStartTime.Value;
            _sessionDuration = pair.SessionDuration;

            // Start timer if not already running
            if (_sessionTimer == null || !_sessionTimer.IsEnabled)
            {
                StartSessionTimer();
            }
        }
        else
        {
            // Hide timer for this pair
            SessionTimerPanel.Visibility = Visibility.Collapsed;
            AIRunningIndicator.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Update AI toggle button visual state
    /// </summary>
    private void UpdateAIToggleButtonState(bool isRunning)
    {
        if (isRunning)
        {
            if (AIToggleIcon != null) AIToggleIcon.Text = "⏹";
            if (AIToggleText != null) AIToggleText.Text = "STOP AI";
            AITradeToggle.Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#10B981")!, 0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#059669")!, 1)
                }
            };
        }
        else
        {
            if (AIToggleIcon != null) AIToggleIcon.Text = "▶";
            if (AIToggleText != null) AIToggleText.Text = "START AI";
            AITradeToggle.Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#7C3AED")!, 0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#5B21B6")!, 1)
                }
            };
        }
    }

    private void AddPair_Click(object sender, MouseButtonEventArgs e)
    {
        var currentPairs = _activePairs[_selectedExchange];
        if (currentPairs.Count >= MaxPairsPerExchange)
        {
            MessageBox.Show(
                $"Maximum {MaxPairsPerExchange} pairs per exchange reached!\n\nPlease remove a pair before adding a new one.",
                "Limit Reached",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Show pair selector dialog
        ShowPairSelectorDialog();
    }

    private void ClosePairTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string symbol)
        {
            var pair = _activePairs[_selectedExchange].FirstOrDefault(p => p.Symbol == symbol);
            if (pair == null) return;

            // Don't allow closing if AI is running
            if (pair.IsAIRunning)
            {
                MessageBox.Show(
                    "Cannot close pair while AI is running.\nStop AI first, then close.",
                    "AI Running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Don't allow closing last pair
            if (_activePairs[_selectedExchange].Count <= 1)
            {
                MessageBox.Show(
                    "At least one trading pair must be active.",
                    "Cannot Close",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Remove pair
            RemoveTradingPair(_selectedExchange, symbol);
        }
    }

    private void ScanPairs_Click(object sender, RoutedEventArgs e)
    {
        ShowPairScannerDialog();
    }

    private void AddTradingPair(string exchange, string symbol, bool setAsCurrent = false)
    {
        if (!_activePairs.ContainsKey(exchange))
            _activePairs[exchange] = new List<ActiveTradingPair>();

        var existingPair = _activePairs[exchange].FirstOrDefault(p => p.Symbol == symbol);
        if (existingPair != null)
        {
            if (setAsCurrent) SelectActivePair(symbol);
            return;
        }

        if (_activePairs[exchange].Count >= MaxPairsPerExchange)
            return;

        var newPair = new ActiveTradingPair
        {
            Symbol = symbol,
            Exchange = exchange,
            Strategy = AITradingMode.Scalping,
            Config = new AIStrategyConfig { Exchange = exchange, Symbol = symbol },
            IsAIRunning = false
        };

        _activePairs[exchange].Add(newPair);

        if (setAsCurrent)
        {
            _currentPair = newPair;
            _selectedSymbol = symbol;
        }

        UpdatePairTabsDisplay();
        _logger?.LogInfo("AITradingPage", $"Added trading pair: {exchange}/{symbol}");
    }

    private void RemoveTradingPair(string exchange, string symbol)
    {
        var pair = _activePairs[exchange].FirstOrDefault(p => p.Symbol == symbol);
        if (pair == null) return;

        _activePairs[exchange].Remove(pair);

        // If this was the current pair, switch to first available
        if (_currentPair?.Symbol == symbol)
        {
            var firstPair = _activePairs[exchange].FirstOrDefault();
            if (firstPair != null)
            {
                SelectActivePair(firstPair.Symbol);
            }
        }

        UpdatePairTabsDisplay();
        _logger?.LogInfo("AITradingPage", $"Removed trading pair: {exchange}/{symbol}");
    }

    private void UpdatePairTabsDisplay()
    {
        // Clear existing tabs (except add button)
        var toRemove = ActivePairsTabs.Children.OfType<Border>()
            .Where(b => b != AddPairButton && b.Tag is string)
            .ToList();
        foreach (var tab in toRemove)
        {
            ActivePairsTabs.Children.Remove(tab);
        }

        // Add tabs for active pairs
        int index = 0;
        foreach (var pair in _activePairs[_selectedExchange])
        {
            var isSelected = pair.Symbol == _selectedSymbol;
            var baseAsset = pair.Symbol.Split('/')[0];

            var tabBorder = new Border
            {
                Style = (Style)FindResource(isSelected ? "SelectedPairTab" : "ActivePairTabStyle"),
                Tag = pair.Symbol,
                Cursor = Cursors.Hand
            };
            tabBorder.MouseLeftButtonDown += PairTab_Click;

            var tabContent = new StackPanel { Orientation = Orientation.Horizontal };

            // Coin icon
            var coinIcon = new Controls.CoinIcon { Symbol = baseAsset, Size = 18 };
            coinIcon.Margin = new Thickness(0, 0, 6, 0);
            tabContent.Children.Add(coinIcon);

            // Symbol text
            var symbolText = new TextBlock
            {
                Text = pair.Symbol,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = isSelected ? Brushes.White : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80FFFFFF")!),
                VerticalAlignment = VerticalAlignment.Center
            };
            tabContent.Children.Add(symbolText);

            // AI Status badge
            if (pair.IsAIRunning)
            {
                var statusBadge = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2010B981")!),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 2, 4, 2),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                statusBadge.Child = new TextBlock
                {
                    Text = "AI",
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")!)
                };
                tabContent.Children.Add(statusBadge);
            }

            // Close button (only show when more than 1 pair and not running AI)
            if (_activePairs[_selectedExchange].Count > 1 && !pair.IsAIRunning)
            {
                var closeBtn = new Button
                {
                    Content = "✕",
                    FontSize = 10,
                    Padding = new Thickness(4, 2, 4, 2),
                    Margin = new Thickness(6, 0, 0, 0),
                    Tag = pair.Symbol,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80FFFFFF")!)
                };
                closeBtn.Template = (ControlTemplate)FindResource("CloseButtonTemplate");
                closeBtn.Click += ClosePairTab_Click;
                tabContent.Children.Add(closeBtn);
            }

            tabBorder.Child = tabContent;
            pair.TabElement = tabBorder;

            ActivePairsTabs.Children.Insert(index++, tabBorder);
        }

        // Update pair count
        PairCount.Text = $"{_activePairs[_selectedExchange].Count}/{MaxPairsPerExchange}";

        // Show/hide add button based on limit
        AddPairButton.Visibility = _activePairs[_selectedExchange].Count < MaxPairsPerExchange
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ShowPairSelectorDialog()
    {
        var dialog = new Window
        {
            Title = $"Add Trading Pair - {_selectedExchange}",
            Width = 400,
            Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0A0A1A")!),
            Foreground = Brushes.White,
            ResizeMode = ResizeMode.NoResize
        };

        var mainPanel = new StackPanel { Margin = new Thickness(20) };

        // Title
        mainPanel.Children.Add(new TextBlock
        {
            Text = "Select Trading Pair",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 15)
        });

        // Search box
        var searchBox = new TextBox
        {
            Style = (Style)FindResource("DarkTextBox"),
            Text = "",
            Margin = new Thickness(0, 0, 0, 15)
        };
        searchBox.SetValue(TextBlock.FontSizeProperty, 14.0);
        mainPanel.Children.Add(searchBox);

        // Popular pairs - different for Bitkub (THB pairs)
        var quoteCurrency = _selectedExchange == "Bitkub" ? "THB" : "USDT";
        mainPanel.Children.Add(new TextBlock
        {
            Text = $"Popular {quoteCurrency} Pairs:",
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80FFFFFF")!),
            Margin = new Thickness(0, 0, 0, 10)
        });

        var popularPairs = _selectedExchange == "Bitkub"
            ? new[] { "BTC/THB", "ETH/THB", "XRP/THB", "DOGE/THB", "ADA/THB",
                      "SOL/THB", "DOT/THB", "LINK/THB", "LTC/THB", "BCH/THB" }
            : new[] { "BTC/USDT", "ETH/USDT", "SOL/USDT", "XRP/USDT", "BNB/USDT",
                      "ADA/USDT", "DOGE/USDT", "DOT/USDT", "AVAX/USDT", "MATIC/USDT" };

        var pairsWrap = new WrapPanel();
        foreach (var pair in popularPairs)
        {
            var alreadyAdded = _activePairs[_selectedExchange].Any(p => p.Symbol == pair);
            var btn = new Button
            {
                Content = pair,
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 8, 8),
                FontSize = 12,
                IsEnabled = !alreadyAdded,
                Tag = pair
            };
            btn.Background = alreadyAdded
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10FFFFFF")!)
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED")!);
            btn.Foreground = Brushes.White;
            btn.Click += (s, e) =>
            {
                if (s is Button b && b.Tag is string symbol)
                {
                    AddTradingPair(_selectedExchange, symbol, setAsCurrent: true);
                    dialog.Close();
                }
            };
            pairsWrap.Children.Add(btn);
        }
        mainPanel.Children.Add(pairsWrap);

        // Add custom pair
        var examplePair = _selectedExchange == "Bitkub" ? "BTC/THB" : "BTC/USDT";
        mainPanel.Children.Add(new TextBlock
        {
            Text = "Or enter custom pair:",
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80FFFFFF")!),
            Margin = new Thickness(0, 20, 0, 10)
        });

        var customPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var customInput = new TextBox
        {
            Style = (Style)FindResource("DarkTextBox"),
            Width = 200,
            Text = "",
            Margin = new Thickness(0, 0, 10, 0)
        };
        var addBtn = new Button
        {
            Content = "Add",
            Padding = new Thickness(20, 10, 20, 10),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED")!),
            Foreground = Brushes.White
        };
        addBtn.Click += (s, e) =>
        {
            var symbol = customInput.Text.Trim().ToUpper();
            if (!string.IsNullOrEmpty(symbol) && symbol.Contains('/'))
            {
                AddTradingPair(_selectedExchange, symbol, setAsCurrent: true);
                dialog.Close();
            }
            else
            {
                MessageBox.Show($"Please enter a valid pair (e.g., {examplePair})", "Invalid Pair",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        customPanel.Children.Add(customInput);
        customPanel.Children.Add(addBtn);
        mainPanel.Children.Add(customPanel);

        dialog.Content = mainPanel;
        dialog.ShowDialog();
    }

    private async void ShowPairScannerDialog()
    {
        // First check if exchange client can be created and has API connectivity
        if (_exchangeFactory == null)
        {
            ShowNoApiWarningDialog("ไม่สามารถเชื่อมต่อได้ / Unable to connect",
                "ระบบยังไม่พร้อมใช้งาน กรุณารอสักครู่\n\nSystem is not ready. Please wait a moment.");
            return;
        }

        // Check API configuration before showing scanner
        bool hasApiConfigured = await CheckExchangeApiConfiguredAsync();
        if (!hasApiConfigured)
        {
            ShowNoApiWarningDialog($"ไม่พบ API Key สำหรับ {_selectedExchange}",
                $"กรุณาตั้งค่า API Key ของ {_selectedExchange} ก่อนสแกนคู่เทรด\n\n" +
                $"Please configure {_selectedExchange} API Key before scanning pairs.\n\n" +
                "ไปที่ Settings → Exchange API → เพิ่ม API Key");
            return;
        }

        var dialog = new Window
        {
            Title = $"Scan Trading Pairs - {_selectedExchange}",
            Width = 700,
            Height = 800,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0A0A1A")!),
            Foreground = Brushes.White,
            ResizeMode = ResizeMode.NoResize
        };

        var mainPanel = new StackPanel { Margin = new Thickness(20) };

        // Title with exchange badge
        var titlePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 15) };
        titlePanel.Children.Add(new TextBlock
        {
            Text = $"🔍 Scan Trading Pairs",
            FontSize = 18,
            FontWeight = FontWeights.Bold
        });
        var exchangeBadge = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#207C3AED")!),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        exchangeBadge.Child = new TextBlock
        {
            Text = _selectedExchange.ToUpper(),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED")!)
        };
        titlePanel.Children.Add(exchangeBadge);
        mainPanel.Children.Add(titlePanel);

        // Filter Options Panel
        var filterPanel = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#15FFFFFF")!),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(15),
            Margin = new Thickness(0, 0, 0, 15)
        };
        var filterContent = new StackPanel();

        // Filter Label
        filterContent.Children.Add(new TextBlock
        {
            Text = "FILTER BY / กรองตาม:",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60FFFFFF")!),
            Margin = new Thickness(0, 0, 0, 10)
        });

        // Filter Buttons
        var filterBtnPanel = new WrapPanel();
        string selectedFilter = "volume"; // Default filter

        var filterButtons = new Dictionary<string, Button>();
        var filters = new[]
        {
            ("volume", "📊 Top Volume", "เหรียญที่มีปริมาณซื้อขายสูง"),
            ("gainers", "🚀 Top Gainers", "เหรียญที่ราคาขึ้นมากที่สุด"),
            ("losers", "📉 Top Losers", "เหรียญที่ราคาลงมากที่สุด (Buy the dip)"),
            ("all", "📋 All Pairs", "แสดงทุกคู่เหรียญ")
        };

        foreach (var (key, label, tooltip) in filters)
        {
            var btn = new Button
            {
                Content = label,
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 8, 8),
                FontSize = 11,
                ToolTip = tooltip,
                Tag = key,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btn.Background = key == "volume"
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED")!)
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20FFFFFF")!);
            btn.Foreground = Brushes.White;
            filterButtons[key] = btn;
            filterBtnPanel.Children.Add(btn);
        }
        filterContent.Children.Add(filterBtnPanel);
        filterPanel.Child = filterContent;
        mainPanel.Children.Add(filterPanel);

        // Stats Panel
        var statsPanel = new Grid { Margin = new Thickness(0, 0, 0, 15) };
        statsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var scannedCount = new TextBlock { Text = "0", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED")!), HorizontalAlignment = HorizontalAlignment.Center };
        var foundCount = new TextBlock { Text = "0", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")!), HorizontalAlignment = HorizontalAlignment.Center };
        var addedCount = new TextBlock { Text = $"{_activePairs[_selectedExchange].Count}", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")!), HorizontalAlignment = HorizontalAlignment.Center };

        var stat1 = new StackPanel(); stat1.Children.Add(new TextBlock { Text = "Scanned", FontSize = 10, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60FFFFFF")!), HorizontalAlignment = HorizontalAlignment.Center }); stat1.Children.Add(scannedCount);
        var stat2 = new StackPanel(); stat2.Children.Add(new TextBlock { Text = "Found", FontSize = 10, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60FFFFFF")!), HorizontalAlignment = HorizontalAlignment.Center }); stat2.Children.Add(foundCount);
        var stat3 = new StackPanel(); stat3.Children.Add(new TextBlock { Text = "Added", FontSize = 10, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60FFFFFF")!), HorizontalAlignment = HorizontalAlignment.Center }); stat3.Children.Add(addedCount);

        Grid.SetColumn(stat1, 0); Grid.SetColumn(stat2, 1); Grid.SetColumn(stat3, 2);
        statsPanel.Children.Add(stat1); statsPanel.Children.Add(stat2); statsPanel.Children.Add(stat3);
        mainPanel.Children.Add(statsPanel);

        // Progress Bar
        var progressPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 15), Visibility = Visibility.Collapsed };
        var progressText = new TextBlock
        {
            Text = "🔄 Scanning pairs...",
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED")!),
            Margin = new Thickness(0, 0, 0, 5)
        };
        progressPanel.Children.Add(progressText);

        var progressBar = new ProgressBar
        {
            Height = 6,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20FFFFFF")!),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED")!),
            Value = 0,
            Maximum = 100
        };
        progressPanel.Children.Add(progressBar);
        mainPanel.Children.Add(progressPanel);

        // Pairs list
        var pairsListView = new ListView
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10FFFFFF")!),
            BorderThickness = new Thickness(0),
            Height = 400,
            Visibility = Visibility.Collapsed
        };
        mainPanel.Children.Add(pairsListView);

        // Empty State
        var emptyState = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 50, 0, 0) };
        emptyState.Children.Add(new TextBlock { Text = "👆", FontSize = 40, HorizontalAlignment = HorizontalAlignment.Center });
        emptyState.Children.Add(new TextBlock { Text = "Select a filter and click 'Start Scan'", FontSize = 14, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60FFFFFF")!), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
        mainPanel.Children.Add(emptyState);

        // Start Scan Button
        var startScanBtn = new Button
        {
            Content = "🔍 START SCAN",
            Padding = new Thickness(30, 12, 30, 12),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 15, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#7C3AED")!,
                (Color)ColorConverter.ConvertFromString("#5B21B6")!,
                new Point(0, 0), new Point(1, 1)),
            Foreground = Brushes.White
        };
        mainPanel.Children.Add(startScanBtn);

        dialog.Content = new ScrollViewer { Content = mainPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        // Filter button click handlers
        foreach (var (key, btn) in filterButtons)
        {
            var filterKey = key; // Capture for closure
            btn.Click += (s, e) =>
            {
                selectedFilter = filterKey;
                foreach (var (k, b) in filterButtons)
                {
                    b.Background = k == filterKey
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED")!)
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20FFFFFF")!);
                }
            };
        }

        // Start Scan button click handler
        startScanBtn.Click += async (s, e) =>
        {
            startScanBtn.IsEnabled = false;
            startScanBtn.Content = "⏳ Scanning...";
            emptyState.Visibility = Visibility.Collapsed;
            progressPanel.Visibility = Visibility.Visible;
            pairsListView.Items.Clear();
            pairsListView.Visibility = Visibility.Visible;

            await ScanPairsWithStreamingAsync(
                selectedFilter,
                pairsListView,
                progressBar,
                progressText,
                scannedCount,
                foundCount,
                addedCount,
                dialog);

            startScanBtn.IsEnabled = true;
            startScanBtn.Content = "🔍 SCAN AGAIN";
            progressPanel.Visibility = Visibility.Collapsed;
        };

        dialog.Show();
    }

    /// <summary>
    /// Scan pairs with streaming display - shows each pair as it's found
    /// </summary>
    private async Task ScanPairsWithStreamingAsync(
        string filterType,
        ListView pairsListView,
        ProgressBar progressBar,
        TextBlock progressText,
        TextBlock scannedCount,
        TextBlock foundCount,
        TextBlock addedCount,
        Window dialog)
    {
        if (_exchangeFactory == null)
        {
            progressText.Text = "❌ ระบบไม่พร้อม / System not ready";
            progressText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")!);
            return;
        }

        try
        {
            progressText.Text = $"🔄 กำลังเชื่อมต่อ {_selectedExchange}...";
            var client = _exchangeFactory.CreateRealClient(_selectedExchange);

            // Test connection first
            using var testCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var isConnected = await client.TestConnectionAsync(testCts.Token);

            if (!isConnected)
            {
                progressText.Text = $"❌ ไม่สามารถเชื่อมต่อ {_selectedExchange} / Cannot connect to {_selectedExchange}";
                progressText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")!);
                _logger?.LogWarning("AITradingPage", $"Scan failed: Cannot connect to {_selectedExchange}");
                return;
            }

            progressText.Text = $"✅ เชื่อมต่อสำเร็จ กำลังสแกน...";

            // Determine currency display prefix based on exchange
            string currencyPrefix = _selectedExchange == "Bitkub" ? "฿" : "$";

            // Determine quote asset filter based on exchange
            string? quoteAssetFilter = _selectedExchange switch
            {
                "Bitkub" => "THB",
                "KuCoin" => "USDT",
                "OKX" => "USDT",
                "Gate.io" => "USDT",
                _ => "USDT"
            };

            progressText.Text = $"🔄 กำลังดึงข้อมูลทุกคู่เทรด / Fetching all trading pairs...";

            // Use timeout for API call
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

            // Get ALL tickers from the exchange using new method
            Dictionary<string, Ticker> tickers;
            try
            {
                tickers = await client.GetAllTickersAsync(quoteAssetFilter, cts.Token);
                _logger?.LogInfo("AITradingPage", $"GetAllTickersAsync returned {tickers?.Count ?? 0} tickers for {_selectedExchange}");
            }
            catch (Exception ex)
            {
                _logger?.LogError("AITradingPage", $"GetAllTickersAsync failed: {ex.Message}");
                progressText.Text = $"❌ ไม่สามารถดึงข้อมูลได้ / Failed to fetch data: {ex.Message}";
                progressText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")!);
                return;
            }

            if (tickers == null || tickers.Count == 0)
            {
                progressText.Text = "❌ ไม่พบข้อมูลคู่เทรด / No trading pairs found\nกรุณาตรวจสอบ API Key หรือลองใหม่";
                progressText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")!);
                _logger?.LogWarning("AITradingPage", "GetAllTickersAsync returned empty results");
                return;
            }

            var allTickers = tickers.Values.ToList();
            int total = allTickers.Count;

            progressText.Text = $"✅ ได้รับข้อมูล {total} คู่ กำลังประมวลผล... / Got {total} pairs, processing...";
            scannedCount.Text = total.ToString();
            foundCount.Text = total.ToString();

            if (allTickers.Count == 0)
            {
                progressText.Text = "⚠️ ไม่พบคู่เทรดที่ตรงกัน / No matching pairs found";
                progressText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")!);
                return;
            }

            // Sort based on filter type
            IEnumerable<Ticker> sortedTickers = filterType switch
            {
                "volume" => allTickers.OrderByDescending(t => t.Volume24h),
                // Sort by position relative to mid-price (buy pressure = closer to ask, sell pressure = closer to bid)
                "gainers" => allTickers.Where(t => t.SpreadPercentage < 2).OrderByDescending(t => t.LastPrice > 0 && t.MidPrice > 0 ? ((t.LastPrice - t.MidPrice) / t.MidPrice * 100) : 0),
                "losers" => allTickers.Where(t => t.SpreadPercentage < 2).OrderBy(t => t.LastPrice > 0 && t.MidPrice > 0 ? ((t.LastPrice - t.MidPrice) / t.MidPrice * 100) : 0),
                _ => allTickers.OrderByDescending(t => t.Volume24h)
            };

            var sortedList = sortedTickers.ToList();

            // Add items one by one with animation effect
            progressText.Text = $"✅ พบ {sortedList.Count} คู่ กำลังแสดงผล... / Found {sortedList.Count} pairs, displaying...";

            // Show up to 200 pairs (increased from 50)
            const int maxDisplayPairs = 200;
            foreach (var ticker in sortedList.Take(maxDisplayPairs))
            {
                // Convert symbol to display format based on exchange format
                string displaySymbol = ConvertToDisplaySymbol(ticker.Symbol, _selectedExchange);

                var alreadyAdded = _activePairs[_selectedExchange].Any(p => p.Symbol == displaySymbol);

                // Create item UI
                var itemPanel = new Grid();
                itemPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                itemPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                itemPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                itemPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                itemPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

                // Coin icon
                var baseAsset = displaySymbol.Split('/')[0];
                var coinIcon = new Controls.CoinIcon { Symbol = baseAsset, Size = 28 };
                Grid.SetColumn(coinIcon, 0);
                itemPanel.Children.Add(coinIcon);

                // Symbol
                var symbolText = new TextBlock
                {
                    Text = displaySymbol,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(symbolText, 1);
                itemPanel.Children.Add(symbolText);

                // Price
                var priceText = new TextBlock
                {
                    Text = $"{currencyPrefix}{ticker.LastPrice:N4}",
                    FontSize = 12,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(priceText, 2);
                itemPanel.Children.Add(priceText);

                // Volume
                var volumeText = new TextBlock
                {
                    Text = $"Vol: {currencyPrefix}{ticker.Volume24h:N0}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60FFFFFF")!),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(volumeText, 3);
                itemPanel.Children.Add(volumeText);

                // Add button
                var addBtn = new Button
                {
                    Content = alreadyAdded ? "✓" : "Add",
                    Padding = new Thickness(10, 5, 10, 5),
                    IsEnabled = !alreadyAdded,
                    Background = alreadyAdded
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10FFFFFF")!)
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED")!),
                    Foreground = Brushes.White,
                    Tag = displaySymbol,
                    FontSize = 11,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                addBtn.Click += (s, e) =>
                {
                    if (s is Button btn && btn.Tag is string sym)
                    {
                        AddTradingPair(_selectedExchange, sym, setAsCurrent: false);
                        btn.Content = "✓";
                        btn.IsEnabled = false;
                        btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10FFFFFF")!);
                        addedCount.Text = $"{_activePairs[_selectedExchange].Count}";
                    }
                };
                Grid.SetColumn(addBtn, 4);
                itemPanel.Children.Add(addBtn);

                var item = new ListViewItem
                {
                    Content = itemPanel,
                    Background = Brushes.Transparent,
                    Foreground = Brushes.White,
                    Padding = new Thickness(10, 8, 10, 8)
                };

                pairsListView.Items.Add(item);

                // Small delay for streaming effect
                await Task.Delay(30);
            }

            var displayedCount = Math.Min(sortedList.Count, maxDisplayPairs);
            progressText.Text = $"✅ สแกนเสร็จสิ้น! พบ {sortedList.Count} คู่ (แสดง {displayedCount}) / Scan complete! Found {sortedList.Count} pairs (showing {displayedCount})";
            progressText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")!);
        }
        catch (OperationCanceledException)
        {
            progressText.Text = "⏱️ หมดเวลา กรุณาลองใหม่ / Scan timed out, please try again";
            progressText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")!);
            _logger?.LogWarning("AITradingPage", "Pair scan timed out");
        }
        catch (HttpRequestException ex)
        {
            progressText.Text = $"❌ เครือข่ายผิดพลาด / Network error: {ex.Message}";
            progressText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")!);
            _logger?.LogError("AITradingPage", $"Network error scanning pairs: {ex.Message}");
        }
        catch (Exception ex)
        {
            var errorMsg = ex.Message.Length > 50 ? ex.Message.Substring(0, 50) + "..." : ex.Message;
            progressText.Text = $"❌ ผิดพลาด / Error: {errorMsg}";
            progressText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")!);
            _logger?.LogError("AITradingPage", $"Error scanning pairs: {ex.Message}");
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Check if API is configured for the selected exchange
    /// Uses ConnectionStatusService to check verified status first, then falls back to environment variable check
    /// </summary>
    private async Task<bool> CheckExchangeApiConfiguredAsync()
    {
        _logger?.LogInfo("AITradingPage", $"CheckExchangeApiConfiguredAsync: Checking {_selectedExchange}");

        // Method 1: Check if exchange was verified in Settings page (cached)
        if (_connectionStatusService != null)
        {
            var isVerified = _connectionStatusService.IsExchangeVerified(_selectedExchange);
            if (isVerified)
            {
                _logger?.LogInfo("AITradingPage", $"{_selectedExchange} is verified in cache");
                return true;
            }
        }

        // Method 2: Check environment variables directly
        var (apiKeyEnv, apiSecretEnv) = GetExchangeEnvVarNames(_selectedExchange);
        var apiKey = Environment.GetEnvironmentVariable(apiKeyEnv);
        var apiSecret = Environment.GetEnvironmentVariable(apiSecretEnv);

        var hasCredentials = !string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(apiSecret);

        _logger?.LogInfo("AITradingPage", $"{_selectedExchange} credentials check: {apiKeyEnv}={!string.IsNullOrEmpty(apiKey)}, {apiSecretEnv}={!string.IsNullOrEmpty(apiSecret)}");

        if (!hasCredentials)
        {
            _logger?.LogWarning("AITradingPage", $"{_selectedExchange}: No credentials configured");
            return false;
        }

        // Method 3: If credentials exist but not verified, try to test connection
        if (_exchangeFactory != null)
        {
            try
            {
                var client = _exchangeFactory.CreateRealClient(_selectedExchange);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var isConnected = await client.TestConnectionAsync(cts.Token);

                if (isConnected)
                {
                    // Mark as verified so we don't have to test again
                    _connectionStatusService?.MarkExchangeAsVerified(_selectedExchange);
                    _logger?.LogInfo("AITradingPage", $"{_selectedExchange}: Connection test passed, marked as verified");
                }

                return isConnected;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("AITradingPage", $"{_selectedExchange}: Connection test failed - {ex.Message}");
                // Even if connection test fails, we have credentials so allow scanning (public API doesn't need auth)
                return true;
            }
        }

        // Fallback: credentials exist
        return hasCredentials;
    }

    /// <summary>
    /// Get environment variable names for exchange API credentials
    /// </summary>
    private (string apiKeyEnv, string apiSecretEnv) GetExchangeEnvVarNames(string exchangeName)
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
    /// Convert exchange-specific symbol format to display format (BASE/QUOTE)
    /// </summary>
    private static string ConvertToDisplaySymbol(string symbol, string exchangeName)
    {
        if (string.IsNullOrEmpty(symbol)) return symbol;

        return exchangeName.ToLower() switch
        {
            // Bitkub: THB_BTC -> BTC/THB
            "bitkub" => symbol.Contains('_')
                ? string.Join("/", symbol.Split('_').Reverse())
                : symbol,

            // KuCoin: BTC-USDT -> BTC/USDT
            // OKX: BTC-USDT -> BTC/USDT
            "kucoin" or "okx" => symbol.Replace("-", "/"),

            // Gate.io: BTC_USDT -> BTC/USDT
            "gate.io" => symbol.Replace("_", "/"),

            // Binance/Bybit: BTCUSDT -> BTC/USDT
            "binance" or "bybit" => symbol.EndsWith("USDT")
                ? $"{symbol[..^4]}/USDT"
                : symbol.EndsWith("USDC")
                    ? $"{symbol[..^4]}/USDC"
                    : symbol.EndsWith("BTC")
                        ? $"{symbol[..^3]}/BTC"
                        : symbol.EndsWith("ETH")
                            ? $"{symbol[..^3]}/ETH"
                            : symbol.EndsWith("BNB")
                                ? $"{symbol[..^3]}/BNB"
                                : symbol,

            // Default: try to detect format
            _ => symbol.Contains('/') ? symbol
                : symbol.Contains('-') ? symbol.Replace("-", "/")
                : symbol.Contains('_') ? symbol.Replace("_", "/")
                : symbol.EndsWith("USDT") ? $"{symbol[..^4]}/USDT"
                : symbol.EndsWith("USDC") ? $"{symbol[..^4]}/USDC"
                : symbol
        };
    }

    /// <summary>
    /// Show styled warning dialog when API is not configured
    /// </summary>
    private void ShowNoApiWarningDialog(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 450,
            Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0A0A1A")!),
            Foreground = Brushes.White,
            ResizeMode = ResizeMode.NoResize
        };

        var mainPanel = new StackPanel { Margin = new Thickness(25) };

        // Warning icon
        mainPanel.Children.Add(new TextBlock
        {
            Text = "⚠️",
            FontSize = 48,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 15)
        });

        // Title
        mainPanel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        // Message
        mainPanel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A0FFFFFF")!),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20)
        });

        // Buttons panel
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

        // Go to Settings button
        var settingsBtn = new Button
        {
            Content = "⚙️ ไปที่ Settings",
            Padding = new Thickness(20, 10, 20, 10),
            FontSize = 12,
            Margin = new Thickness(0, 0, 10, 0),
            Background = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#7C3AED")!,
                (Color)ColorConverter.ConvertFromString("#5B21B6")!,
                new Point(0, 0), new Point(1, 1)),
            Foreground = Brushes.White,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        settingsBtn.Click += (s, e) =>
        {
            dialog.Close();
            // Navigate to settings page - find MainWindow and navigate
            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.NavigateToPage("Settings");
        };
        buttonPanel.Children.Add(settingsBtn);

        // Close button
        var closeBtn = new Button
        {
            Content = "ปิด / Close",
            Padding = new Thickness(20, 10, 20, 10),
            FontSize = 12,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30FFFFFF")!),
            Foreground = Brushes.White,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        closeBtn.Click += (s, e) => dialog.Close();
        buttonPanel.Children.Add(closeBtn);

        mainPanel.Children.Add(buttonPanel);
        dialog.Content = mainPanel;
        dialog.ShowDialog();
    }

    private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;

            var result = FindVisualChild<T>(child);
            if (result != null)
                return result;
        }
        return null;
    }

    #endregion

    #region Auto Strategy & Market Analysis

    /// <summary>
    /// Toggle Auto Strategy mode
    /// เมื่อเปิด Auto จะเปลี่ยนกลยุทธ์ตามสภาพตลาดอัตโนมัติ
    /// </summary>
    private void AutoStrategyToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _isAutoStrategy = !_isAutoStrategy;

        // Update toggle visual
        if (_isAutoStrategy)
        {
            AutoStrategyToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")!);
            AutoStrategyToggleKnob.HorizontalAlignment = HorizontalAlignment.Right;
            AutoStrategyToggleKnob.Margin = new Thickness(0, 0, 3, 0);
            AutoStrategyDescription.Text = "AI จะเปลี่ยนกลยุทธ์อัตโนมัติ";
            RecommendedStrategyPanel.Visibility = Visibility.Visible;

            // Disable manual strategy selection
            SetStrategyCardsEnabled(false);

            // Apply recommended strategy immediately if we have market data
            if (_lastMarketData != null)
            {
                var recommendedStrategy = AnalyzeAndRecommendStrategy(_lastMarketData);
                _selectedStrategy = recommendedStrategy;
                UpdateStrategySelection(recommendedStrategy);
            }

            _logger?.LogInfo("AITradingPage", "Auto Strategy mode enabled");
        }
        else
        {
            AutoStrategyToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30FFFFFF")!);
            AutoStrategyToggleKnob.HorizontalAlignment = HorizontalAlignment.Left;
            AutoStrategyToggleKnob.Margin = new Thickness(3, 0, 0, 0);
            AutoStrategyDescription.Text = "AI จะเลือกกลยุทธ์ตามสภาพตลาด";
            RecommendedStrategyPanel.Visibility = Visibility.Collapsed;

            // Enable manual strategy selection
            SetStrategyCardsEnabled(true);

            _logger?.LogInfo("AITradingPage", "Auto Strategy mode disabled");
        }
    }

    /// <summary>
    /// Enable/Disable strategy cards for manual selection
    /// </summary>
    private void SetStrategyCardsEnabled(bool enabled)
    {
        var opacity = enabled ? 1.0 : 0.5;
        ScalpingStrategy.Opacity = opacity;
        MomentumStrategy.Opacity = opacity;
        MeanReversionStrategy.Opacity = opacity;
        GridStrategy.Opacity = opacity;

        ScalpingStrategy.IsHitTestVisible = enabled;
        MomentumStrategy.IsHitTestVisible = enabled;
        MeanReversionStrategy.IsHitTestVisible = enabled;
        GridStrategy.IsHitTestVisible = enabled;
    }

    /// <summary>
    /// Analyze market data and recommend best strategy
    /// วิเคราะห์สภาพตลาดและแนะนำกลยุทธ์ที่เหมาะสม
    /// </summary>
    private AITradingMode AnalyzeAndRecommendStrategy(AIMarketData marketData)
    {
        var volatility = marketData.Volatility ?? 0;
        var rsi = marketData.RSI ?? 50;
        var volume = marketData.Volume24h;

        // Determine if market is trending
        bool isTrending = false;
        if (marketData.EMA9.HasValue && marketData.EMA21.HasValue)
        {
            var emaDiff = Math.Abs((marketData.EMA9.Value - marketData.EMA21.Value) / marketData.CurrentPrice * 100);
            isTrending = emaDiff > 0.5m; // More than 0.5% difference = trending
        }

        // Check if price is at Bollinger Band extremes
        bool atBollingerExtreme = false;
        if (marketData.BollingerLower.HasValue && marketData.BollingerUpper.HasValue)
        {
            atBollingerExtreme = marketData.CurrentPrice <= marketData.BollingerLower ||
                                 marketData.CurrentPrice >= marketData.BollingerUpper;
        }

        // Strategy recommendation logic
        AITradingMode recommended;
        string reason;

        if (volatility > 3 && isTrending)
        {
            // High volatility + trending = Momentum or Breakout
            if (atBollingerExtreme)
            {
                recommended = AITradingMode.Breakout;
                reason = "Volatility สูง + ราคาชนขอบ BB → Breakout";
            }
            else
            {
                recommended = AITradingMode.Momentum;
                reason = "Volatility สูง + เทรนด์ชัด → Momentum";
            }
        }
        else if (volatility < 2 && !isTrending)
        {
            // Low volatility + sideways = Mean Reversion or Grid
            if (rsi < 30 || rsi > 70)
            {
                recommended = AITradingMode.MeanReversion;
                reason = "ตลาด Sideway + RSI extreme → Mean Reversion";
            }
            else
            {
                recommended = AITradingMode.GridTrading;
                reason = "ตลาด Sideway + Volatility ต่ำ → Grid Trading";
            }
        }
        else if (volatility >= 1 && volatility <= 3)
        {
            // Medium volatility = Scalping
            recommended = AITradingMode.Scalping;
            reason = "Volatility ปานกลาง → Scalping";
        }
        else
        {
            // Default to Smart DCA for uncertain conditions
            recommended = AITradingMode.SmartDCA;
            reason = "สภาพตลาดไม่ชัด → Smart DCA";
        }

        // Update recommended strategy panel
        Dispatcher.InvokeAsync(() =>
        {
            RecommendedStrategyText.Text = $"{GetStrategyDisplayName(recommended)} - {reason}";
        });

        return recommended;
    }

    /// <summary>
    /// Get display name for strategy
    /// </summary>
    private string GetStrategyDisplayName(AITradingMode mode)
    {
        return mode switch
        {
            AITradingMode.Scalping => "⚡ Scalping",
            AITradingMode.Momentum => "📈 Momentum",
            AITradingMode.MeanReversion => "🔄 Mean Reversion",
            AITradingMode.GridTrading => "📊 Grid Trading",
            AITradingMode.Breakout => "🚀 Breakout",
            AITradingMode.SmartDCA => "💰 Smart DCA",
            _ => "⚡ Scalping"
        };
    }

    /// <summary>
    /// Update Market Analysis Display Panel
    /// อัพเดทข้อมูลการวิเคราะห์ตลาด
    /// </summary>
    private void UpdateMarketAnalysisDisplay(AIMarketData marketData)
    {
        try
        {
            // Volatility
            var volatility = marketData.Volatility ?? 0;
            VolatilityText.Text = $"{volatility:F2}%";
            if (volatility > 3)
            {
                VolatilityLevel.Text = " (สูง)";
                VolatilityText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")!);
            }
            else if (volatility < 1.5m)
            {
                VolatilityLevel.Text = " (ต่ำ)";
                VolatilityText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")!);
            }
            else
            {
                VolatilityLevel.Text = " (ปานกลาง)";
                VolatilityText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")!);
            }

            // Volume 24h
            var currencyPrefix = _selectedExchange == "Bitkub" ? "฿" : "$";
            if (marketData.Volume24h >= 1_000_000_000)
                Volume24hText.Text = $"{currencyPrefix}{marketData.Volume24h / 1_000_000_000:F2}B";
            else if (marketData.Volume24h >= 1_000_000)
                Volume24hText.Text = $"{currencyPrefix}{marketData.Volume24h / 1_000_000:F2}M";
            else
                Volume24hText.Text = $"{currencyPrefix}{marketData.Volume24h:N0}";

            // RSI
            var rsi = marketData.RSI ?? 50;
            RSIText.Text = $"{rsi:F1}";
            if (rsi < 30)
                RSIText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")!);
            else if (rsi > 70)
                RSIText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")!);
            else
                RSIText.Foreground = new SolidColorBrush(Colors.White);

            // Trend detection
            bool isBullish = false;
            bool isTrending = false;
            string marketCondition;

            if (marketData.EMA9.HasValue && marketData.EMA21.HasValue)
            {
                isBullish = marketData.EMA9 > marketData.EMA21;
                var emaDiff = Math.Abs((marketData.EMA9.Value - marketData.EMA21.Value) / marketData.CurrentPrice * 100);
                isTrending = emaDiff > 0.5m;
            }

            if (isTrending)
            {
                if (isBullish)
                {
                    TrendIcon.Text = "📈";
                    TrendText.Text = "BULLISH";
                    TrendText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")!);
                    marketCondition = "TRENDING UP";
                    MarketConditionBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2010B981")!);
                    MarketConditionText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")!);
                }
                else
                {
                    TrendIcon.Text = "📉";
                    TrendText.Text = "BEARISH";
                    TrendText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")!);
                    marketCondition = "TRENDING DOWN";
                    MarketConditionBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20EF4444")!);
                    MarketConditionText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")!);
                }
            }
            else
            {
                TrendIcon.Text = "➡️";
                TrendText.Text = "SIDEWAYS";
                TrendText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")!);
                marketCondition = "SIDEWAYS";
                MarketConditionBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20F59E0B")!);
                MarketConditionText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")!);
            }
            MarketConditionText.Text = marketCondition;

            // Recommended strategy display
            var recommendedMode = AnalyzeAndRecommendStrategy(marketData);
            RecommendedStrategyName.Text = GetStrategyDisplayName(recommendedMode);

            string reasonText = recommendedMode switch
            {
                AITradingMode.Scalping => "เหมาะสำหรับทำกำไรเร็วในตลาดที่มี Volatility ปานกลาง",
                AITradingMode.Momentum => "เหมาะสำหรับตลาดที่มีเทรนด์ชัดเจน",
                AITradingMode.MeanReversion => "เหมาะสำหรับตลาด Sideway ที่ RSI อยู่ที่ขั้วสุด",
                AITradingMode.GridTrading => "เหมาะสำหรับตลาด Sideway ที่ Volatility ต่ำ",
                AITradingMode.Breakout => "เหมาะสำหรับการเทรดเมื่อราคาทะลุแนวรับ/แนวต้าน",
                AITradingMode.SmartDCA => "เหมาะสำหรับการลงทุนระยะยาวแบบเฉลี่ยต้นทุน",
                _ => ""
            };
            RecommendedStrategyReason.Text = reasonText;
        }
        catch (Exception ex)
        {
            _logger?.LogError("AITradingPage", $"Error updating market analysis: {ex.Message}");
        }
    }

    /// <summary>
    /// Update strategy selection UI to match selected strategy
    /// </summary>
    private void UpdateStrategySelection(AITradingMode strategy)
    {
        // Reset all checks
        ScalpingCheck.Fill = new SolidColorBrush(Colors.Transparent);
        ScalpingCheck.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#40FFFFFF")!);
        ScalpingCheck.StrokeThickness = 2;

        MomentumCheck.Fill = new SolidColorBrush(Colors.Transparent);
        MomentumCheck.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#40FFFFFF")!);
        MomentumCheck.StrokeThickness = 2;

        MeanReversionCheck.Fill = new SolidColorBrush(Colors.Transparent);
        MeanReversionCheck.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#40FFFFFF")!);
        MeanReversionCheck.StrokeThickness = 2;

        GridCheck.Fill = new SolidColorBrush(Colors.Transparent);
        GridCheck.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#40FFFFFF")!);
        GridCheck.StrokeThickness = 2;

        // Set selected check
        // Breakout maps to Momentum card, SmartDCA maps to GridTrading card (closest match)
        Ellipse selectedCheck = strategy switch
        {
            AITradingMode.Scalping => ScalpingCheck,
            AITradingMode.Momentum or AITradingMode.Breakout => MomentumCheck,
            AITradingMode.MeanReversion => MeanReversionCheck,
            AITradingMode.GridTrading or AITradingMode.SmartDCA => GridCheck,
            _ => ScalpingCheck
        };

        selectedCheck.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED")!);
        selectedCheck.Stroke = null;
        selectedCheck.StrokeThickness = 0;

        // Update strategy card backgrounds (Breakout→Momentum, SmartDCA→Grid)
        UpdateStrategyCardBackground(ScalpingStrategy, strategy == AITradingMode.Scalping);
        UpdateStrategyCardBackground(MomentumStrategy, strategy == AITradingMode.Momentum || strategy == AITradingMode.Breakout);
        UpdateStrategyCardBackground(MeanReversionStrategy, strategy == AITradingMode.MeanReversion);
        UpdateStrategyCardBackground(GridStrategy, strategy == AITradingMode.GridTrading || strategy == AITradingMode.SmartDCA);
    }

    /// <summary>
    /// Update strategy card background based on selection
    /// </summary>
    private void UpdateStrategyCardBackground(Border card, bool isSelected)
    {
        if (isSelected)
        {
            card.Background = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#207C3AED")!,
                (Color)ColorConverter.ConvertFromString("#107C3AED")!,
                0);
        }
        else
        {
            card.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#15FFFFFF")!);
        }
    }

    #endregion
}

/// <summary>
/// Demo AI Position - tracks position state for demo trading mode
/// ใช้สำหรับเก็บข้อมูล position ในโหมดเทรดเสมือน
/// </summary>
public class DemoAIPosition
{
    public string Symbol { get; set; } = "";
    public string Exchange { get; set; } = "";
    public decimal EntryPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal TakeProfitPrice { get; set; }
    public decimal StopLossPrice { get; set; }
    public DateTime EntryTime { get; set; } = DateTime.Now;
    public AITradingMode Strategy { get; set; }

    public decimal UnrealizedPnL => (CurrentPrice - EntryPrice) * Quantity;
    public decimal UnrealizedPnLPercent => EntryPrice > 0 ? (CurrentPrice - EntryPrice) / EntryPrice * 100 : 0;
    public decimal Value => Quantity * CurrentPrice;
}
