using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.Extensions.DependencyInjection;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using AutoTradeX.Infrastructure.Services;
using AutoTradeX.UI.Controls;
using ScanStrategy = AutoTradeX.Infrastructure.Services.ScanStrategy;

namespace AutoTradeX.UI.Views;

public partial class TradingPage : UserControl
{
    // Services (not readonly - may need to be re-fetched after App.Services is ready)
    // Services ไม่ใช่ readonly เพราะอาจต้อง re-fetch หลังจาก App.Services พร้อม
    private IArbEngine? _arbEngine;
    private ILoggingService? _logger;
    private IBalancePoolService? _balancePool;
    private IExchangeClientFactory? _exchangeFactory;
    private IConnectionStatusService? _connectionStatusService;
    private IApiCredentialsService? _apiCredentialsService;
    private IConfigService? _configService;

    // Scanner services
    private ISmartScannerService? _scannerService;
    private ICurrencyConverterService? _currencyConverter;

    // Project service for persistent storage / บริการโปรเจคสำหรับจัดเก็บข้อมูลถาวร
    private IProjectService? _projectService;
    private bool _suppressProjectReload = false;

    private System.Windows.Threading.DispatcherTimer? _priceUpdateTimer;
    private System.Windows.Threading.DispatcherTimer? _botAnimationTimer;
    private System.Windows.Threading.DispatcherTimer? _scanTimer;
    private CancellationTokenSource? _scanCts;
    private bool _isScanning = false;
    private int _scanCount = 0;
    private TradingPair? _selectedPair;
    private TradingPairDisplay? _selectedPairDisplay;
    private bool _autoTradeEnabled = false;
    private string _currentApiGuideExchange = "Binance";
    private readonly Random _animationRandom = new();
    private bool _isInitialized = false;

    // Arbitrage Mode tracking / ติดตามโหมด Arbitrage
    private ArbitrageExecutionMode _currentMode = ArbitrageExecutionMode.DualBalance;
    private TransferExecutionType _currentTransferType = TransferExecutionType.Manual;
    private TransferStatus? _activeTransfer;
    private System.Windows.Threading.DispatcherTimer? _transferProgressTimer;

    // Session P&L tracking / ติดตามกำไร/ขาดทุนของ Session
    private decimal _sessionPnL = 0;
    private decimal _todayPnL = 0;
    private int _sessionTradeCount = 0;
    private decimal _sessionUsdtChange = 0;
    private decimal _sessionBtcChange = 0;
    private TradingPairBalanceSnapshot? _sessionStartSnapshot;

    // Exchange supported modes
    private static readonly Dictionary<string, (bool Spot, bool Futures, bool Margin)> ExchangeModes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Binance", (true, true, true) },
        { "KuCoin", (true, true, true) },
        { "OKX", (true, true, true) },
        { "Bybit", (true, true, false) },
        { "Gate.io", (true, true, false) },
        { "Bitkub", (true, false, false) },
        { "Coinbase", (true, false, false) },
        { "Kraken", (true, true, true) },
        { "Huobi", (true, true, true) },
        { "Bitfinex", (true, false, true) }
    };

    // Exchange API URLs
    private static readonly Dictionary<string, string> ExchangeApiUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Binance", "https://www.binance.com/en/my/settings/api-management" },
        { "KuCoin", "https://www.kucoin.com/account/api" },
        { "OKX", "https://www.okx.com/account/my-api" },
        { "Bybit", "https://www.bybit.com/app/user/api-management" },
        { "Gate.io", "https://www.gate.io/myaccount/apiv4keys" },
        { "Bitkub", "https://www.bitkub.com/settings/api" },
        { "Coinbase", "https://www.coinbase.com/settings/api" },
        { "Kraken", "https://www.kraken.com/u/settings/api" },
        { "Huobi", "https://www.huobi.com/en-us/apikey/" },
        { "Bitfinex", "https://setting.bitfinex.com/api" }
    };

    // Observable collections for UI binding
    public ObservableCollection<TradingPairDisplay> TradingPairs { get; } = new();
    public ObservableCollection<OrderBookEntry> Asks { get; } = new();
    public ObservableCollection<OrderBookEntry> Bids { get; } = new();
    public ObservableCollection<ExecutionDisplay> RecentExecutions { get; } = new();
    public ObservableCollection<ScanResultDisplay> ScanResults { get; } = new();

    public TradingPage()
    {
        InitializeComponent();
        DataContext = this;

        // Try to get services (may be null if App.Services not ready yet)
        // พยายาม get services (อาจเป็น null ถ้า App.Services ยังไม่พร้อม)
        TryInitializeServices();

        Loaded += TradingPage_Loaded;
        Unloaded += TradingPage_Unloaded;
    }

    /// <summary>
    /// Try to initialize services from DI container
    /// พยายาม initialize services จาก DI container
    /// </summary>
    private void TryInitializeServices()
    {
        if (App.Services == null) return;

        _arbEngine ??= App.Services.GetService<IArbEngine>();
        _logger ??= App.Services.GetService<ILoggingService>();
        _balancePool ??= App.Services.GetService<IBalancePoolService>();
        _exchangeFactory ??= App.Services.GetService<IExchangeClientFactory>();
        _connectionStatusService ??= App.Services.GetService<IConnectionStatusService>();
        _apiCredentialsService ??= App.Services.GetService<IApiCredentialsService>();
        _configService ??= App.Services.GetService<IConfigService>();
        _scannerService ??= App.Services.GetService<ISmartScannerService>();
        _currencyConverter ??= App.Services.GetService<ICurrencyConverterService>();
        _projectService ??= App.Services.GetService<IProjectService>();
    }

    /// <summary>
    /// Ensure all services are initialized. Called on page load.
    /// ตรวจสอบว่า services ถูก initialize แล้ว เรียกเมื่อ load หน้า
    /// </summary>
    private void EnsureServicesInitialized()
    {
        TryInitializeServices();

        if (_exchangeFactory == null || _arbEngine == null)
        {
            _logger?.LogWarning("TradingPage", "Some services are still null after initialization attempt");
        }
    }

    private async void TradingPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Ensure services are initialized before checking connections
        // ตรวจสอบว่า services พร้อมก่อนเช็คการเชื่อมต่อ
        EnsureServicesInitialized();

        // Prevent re-initialization if already done
        if (_isInitialized) return;

        // Check exchange connections first
        var isConnected = await CheckExchangeConnectionsAsync();

        if (isConnected)
        {
            NotConnectedOverlay.Visibility = Visibility.Collapsed;
            MainTradingContent.Visibility = Visibility.Visible;

            // Bind scan results list
            // Bind รายการผลลัพธ์การสแกน
            ScanResultsList.ItemsSource = ScanResults;

            await LoadTradingPairsFromProjectAsync();
            SetupEventHandlers();
            StartPriceUpdates();
            SetupBotAnimation();

            // Load real order book data
            try
            {
                await LoadRealOrderBookAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError("TradingPage", $"Error loading order book: {ex.Message}");
            }

            // Update manual trading forms based on bot state
            UpdateManualTradingState();

            // Initialize mode selector and P&L display / เริ่มต้นตัวเลือกโหมดและการแสดง P&L
            InitializeModeSelectorAndPnL();

            _isInitialized = true;
        }
        else
        {
            // Show not connected overlay
            NotConnectedOverlay.Visibility = Visibility.Visible;
            MainTradingContent.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Initialize mode selector and P&L display
    /// เริ่มต้นตัวเลือกโหมดและการแสดงกำไร/ขาดทุน
    /// </summary>
    private void InitializeModeSelectorAndPnL()
    {
        // Initialize Real P&L display
        UpdateRealPnLDisplay();

        // Hide transfer progress panel initially
        TransferProgressPanel.Visibility = Visibility.Collapsed;

        // Set initial exchange names if a pair is selected
        if (_selectedPairDisplay != null)
        {
            ModeSelector.ExchangeAName = _selectedPairDisplay.ExchangeA;
            ModeSelector.ExchangeBName = _selectedPairDisplay.ExchangeB;

            // Capture initial session balances
            _ = CaptureSessionStartBalances();

            // Update balance readiness
            _ = UpdateBalanceReadinessAsync();
        }
    }

    private async Task<bool> CheckExchangeConnectionsAsync()
    {
        // Use ConnectionStatusService if available (preferred - uses cache from Splash)
        // ใช้ ConnectionStatusService ถ้ามี (แนะนำ - ใช้ cache จาก Splash)
        if (_connectionStatusService != null)
        {
            try
            {
                var status = await _connectionStatusService.CheckAllConnectionsAsync();
                var connectedCount = status.Exchanges.Count(e => e.Value.IsConnected && e.Value.HasValidCredentials);
                _logger?.LogInfo("TradingPage", $"Connection check via service: {connectedCount} exchanges connected");
                return connectedCount > 0;
            }
            catch (Exception ex)
            {
                _logger?.LogError("TradingPage", $"Error checking connections via service: {ex.Message}");
            }
        }

        // Fallback: Check manually if service not available
        if (_exchangeFactory == null) return false;

        var exchanges = new[] { "Binance", "KuCoin", "OKX", "Bybit", "Gate.io", "Bitkub" };
        var connectedCount2 = 0;

        foreach (var exchangeName in exchanges)
        {
            try
            {
                // Check credentials from database first (via ApiCredentialsService)
                // ตรวจสอบ credentials จาก database ก่อน
                bool hasCredentials = false;

                if (_apiCredentialsService != null)
                {
                    hasCredentials = await _apiCredentialsService.HasCredentialsAsync(exchangeName);
                    if (hasCredentials)
                    {
                        // Load credentials to env vars
                        var creds = await _apiCredentialsService.GetCredentialsAsync(exchangeName);
                        if (creds != null)
                        {
                            var (keyEnv, secretEnv) = GetExchangeEnvVarNames(exchangeName);
                            Environment.SetEnvironmentVariable(keyEnv, creds.ApiKey);
                            Environment.SetEnvironmentVariable(secretEnv, creds.ApiSecret);
                        }
                    }
                }
                else
                {
                    // Fallback: Check env vars directly
                    var (keyEnv, secretEnv) = GetExchangeEnvVarNames(exchangeName);
                    var apiKey = Environment.GetEnvironmentVariable(keyEnv);
                    var apiSecret = Environment.GetEnvironmentVariable(secretEnv);
                    hasCredentials = !string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(apiSecret);
                }

                if (!hasCredentials) continue;

                // Use real client for connection testing (not simulation)
                var client = _exchangeFactory.CreateRealClient(exchangeName);
                var isConnected = await client.TestConnectionAsync();
                if (isConnected) connectedCount2++;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("TradingPage", $"Connection check failed for {exchangeName}: {ex.Message}");
            }
        }

        _logger?.LogInfo("TradingPage", $"Manual connection check: {connectedCount2} exchanges connected");
        return connectedCount2 > 0;
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

    private void GoToSettings_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to Settings page
        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.NavigateToPage("Settings");
        }
    }

    private void TradingPage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Stop all timers
        _priceUpdateTimer?.Stop();
        _botAnimationTimer?.Stop();
        _transferProgressTimer?.Stop();

        // Stop scanner
        StopScanning();
        DisposeScannerResources();

        // Unsubscribe from events to prevent memory leaks
        // ยกเลิกการลงทะเบียน event เพื่อป้องกัน memory leak
        CleanupEventHandlers();
    }

    /// <summary>
    /// Dispose scanner resources properly
    /// Dispose resources ของ scanner อย่างถูกต้อง
    /// </summary>
    private void DisposeScannerResources()
    {
        try
        {
            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = null;

            if (_scanTimer != null)
            {
                _scanTimer.Stop();
                _scanTimer.Tick -= ScanTimer_Tick;
                _scanTimer = null;
            }
        }
        catch (ObjectDisposedException)
        {
            // Already disposed, ignore
        }
    }

    /// <summary>
    /// Cleanup event handlers to prevent memory leaks
    /// ยกเลิกการลงทะเบียน event handlers เพื่อป้องกัน memory leak
    /// </summary>
    private void CleanupEventHandlers()
    {
        if (_arbEngine != null)
        {
            _arbEngine.PriceUpdated -= ArbEngine_PriceUpdated;
            _arbEngine.TradeCompleted -= ArbEngine_TradeCompleted;
            _arbEngine.OpportunityFound -= ArbEngine_OpportunityFound;
        }

        if (_balancePool != null)
        {
            _balancePool.BalanceUpdated -= BalancePool_BalanceUpdated;
        }

        if (_projectService != null)
        {
            _projectService.ActiveProjectChanged -= ProjectService_ActiveProjectChanged;
        }
    }

    private void SetupBotAnimation()
    {
        _botAnimationTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _botAnimationTimer.Tick += BotAnimationTimer_Tick;
    }

    private void BotAnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (!_autoTradeEnabled) return;

        // Animate bot pulse
        AnimateBotPulse();

        // Animate data flow dots
        AnimateDataFlow();

        // Animate arrow flow particles
        AnimateArrowFlow();
    }

    private void AnimateBotPulse()
    {
        // Animation elements removed in simplified layout
        // หน่วยงานแอนิเมชั่นถูกลบออกในเลย์เอาต์ที่เรียบง่าย
    }

    private void AnimateDataFlow()
    {
        // Animation elements removed in simplified layout
        // หน่วยงานแอนิเมชั่นถูกลบออกในเลย์เอาต์ที่เรียบง่าย
    }

    private void AnimateArrowFlow()
    {
        // Animation elements removed in simplified layout
        // หน่วยงานแอนิเมชั่นถูกลบออกในเลย์เอาต์ที่เรียบง่าย
    }

    private void UpdateManualTradingState()
    {
        // Enable/disable manual trading based on bot state
        // เปิด/ปิดการเทรดด้วยตนเองตามสถานะของ bot
        var isManualEnabled = !_autoTradeEnabled;

        // Manual trading controls removed in simplified layout
        // ปุ่มควบคุมการเทรดด้วยตนเองถูกลบออกในเลย์เอาต์ที่เรียบง่าย

        if (_autoTradeEnabled)
        {
            _botAnimationTimer?.Start();
        }
        else
        {
            _botAnimationTimer?.Stop();
        }
    }

    private void UpdateExchangeModes(string exchangeA, string exchangeB)
    {
        // Mode indicators removed in simplified layout
        // แถบแสดงโหมดถูกลบออกในเลย์เอาต์ที่เรียบง่าย
        // Keep exchange modes data for reference if needed
    }

    /// <summary>
    /// Load trading pairs from Active Project (persisted)
    /// โหลดคู่เทรดจาก Active Project (ข้อมูลถาวร)
    /// </summary>
    private async Task LoadTradingPairsFromProjectAsync()
    {
        TradingPairs.Clear();

        if (_projectService != null)
        {
            try
            {
                var activeProject = await _projectService.GetActiveProjectAsync();
                if (activeProject != null && activeProject.TradingPairs.Count > 0)
                {
                    foreach (var projectPair in activeProject.TradingPairs.Where(p => p.IsEnabled))
                    {
                        // Create display model from project pair
                        // สร้าง display model จากคู่เทรดในโปรเจค
                        var display = new TradingPairDisplay
                        {
                            Symbol = projectPair.Symbol,
                            BaseAsset = projectPair.BaseAsset,
                            QuoteAsset = projectPair.QuoteAsset,
                            ExchangeA = projectPair.ExchangeA,
                            ExchangeB = projectPair.ExchangeB,
                            Status = "Watching"
                        };
                        TradingPairs.Add(display);

                        // Also add to ArbEngine for real-time monitoring
                        // เพิ่มลง ArbEngine สำหรับการเฝ้าดูแบบเรียลไทม์
                        if (_arbEngine != null)
                        {
                            var tradingPair = new TradingPair
                            {
                                Symbol = projectPair.Symbol,
                                BaseCurrency = projectPair.BaseAsset,
                                QuoteCurrency = projectPair.QuoteAsset,
                                ExchangeA_Symbol = projectPair.Symbol.Replace("/", ""),
                                ExchangeB_Symbol = projectPair.Symbol.Replace("/", ""),
                                IsEnabled = true
                            };
                            _arbEngine.AddTradingPair(tradingPair);
                        }
                    }

                    // Restore arbitrage mode from project settings
                    // คืนค่าโหมด arbitrage จากการตั้งค่าโปรเจค
                    if (activeProject.Settings != null &&
                        !string.IsNullOrEmpty(activeProject.Settings.PreferredArbitrageMode))
                    {
                        if (Enum.TryParse<ArbitrageExecutionMode>(
                            activeProject.Settings.PreferredArbitrageMode, out var mode))
                        {
                            _currentMode = mode;
                        }
                    }

                    _logger?.LogInfo("TradingPage", $"Loaded {TradingPairs.Count} pairs from project: {activeProject.Name}");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError("TradingPage", $"Error loading pairs from project: {ex.Message}");
            }
        }

        // Fallback: load from engine if project service unavailable
        // สำรอง: โหลดจาก engine ถ้าไม่มี project service
        if (TradingPairs.Count == 0 && _arbEngine != null)
        {
            var config = _configService?.GetConfig();
            var exchangeA = config?.ExchangeA?.Name ?? "Binance";
            var exchangeB = config?.ExchangeB?.Name ?? "KuCoin";

            var pairs = _arbEngine.GetTradingPairs();
            foreach (var pair in pairs)
            {
                TradingPairs.Add(new TradingPairDisplay(pair, exchangeA, exchangeB));
            }
        }

        if (TradingPairs.Count == 0)
        {
            _logger?.LogInfo("TradingPage", "No trading pairs configured. Add pairs from Scanner or Projects page.");
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

        // Subscribe to project changes for cross-page sync
        // ลงทะเบียนรับการเปลี่ยนแปลงโปรเจคสำหรับ sync ข้ามหน้า
        if (_projectService != null)
        {
            _projectService.ActiveProjectChanged += ProjectService_ActiveProjectChanged;
        }
    }

    private void ProjectService_ActiveProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        if (_suppressProjectReload) return;

        Dispatcher.Invoke(async () =>
        {
            try
            {
                await LoadTradingPairsFromProjectAsync();
                _logger?.LogInfo("TradingPage", $"Reloaded pairs due to project change: {e.ChangeType}");
            }
            catch (Exception ex)
            {
                _logger?.LogError("TradingPage", $"Error reloading pairs on project change: {ex.Message}");
            }
        });
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
        if (_selectedPairDisplay == null || _exchangeFactory == null) return;

        try
        {
            // Fetch REAL prices from exchanges (use CreateRealClient to bypass LiveTrading flag)
            var symbol = _selectedPairDisplay.Symbol.Replace("/", "");

            var clientA = _exchangeFactory.CreateRealClient(_selectedPairDisplay.ExchangeA);
            var clientB = _exchangeFactory.CreateRealClient(_selectedPairDisplay.ExchangeB);

            var tickerATask = clientA.GetTickerAsync(symbol);
            var tickerBTask = clientB.GetTickerAsync(symbol);

            // Add timeout to prevent hanging if exchange API is slow
            // เพิ่ม timeout เพื่อป้องกันการค้างถ้า exchange API ช้า
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
            var dataTask = Task.WhenAll(tickerATask, tickerBTask);
            var completedTask = await Task.WhenAny(dataTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _logger?.LogWarning("TradingPage", "Price update timed out");
                return;
            }

            var tickerA = await tickerATask;
            var tickerB = await tickerBTask;

            await Dispatcher.InvokeAsync(() =>
            {
                // Update Exchange A prices
                if (tickerA != null && tickerA.LastPrice > 0)
                {
                    ExchangeAPrice.Text = $"${tickerA.LastPrice:N2}";
                    ExchangeABid.Text = $"${tickerA.BidPrice:N2}";
                    ExchangeAAsk.Text = $"${tickerA.AskPrice:N2}";
                }

                // Update Exchange B prices
                if (tickerB != null && tickerB.LastPrice > 0)
                {
                    ExchangeBPrice.Text = $"${tickerB.LastPrice:N2}";
                    ExchangeBBid.Text = $"${tickerB.BidPrice:N2}";
                    ExchangeBAsk.Text = $"${tickerB.AskPrice:N2}";
                }

                // Calculate spread from real prices
                if (tickerA != null && tickerB != null && tickerA.AskPrice > 0 && tickerB.BidPrice > 0)
                {
                    var spread = (tickerB.BidPrice - tickerA.AskPrice) / tickerA.AskPrice * 100;
                    SelectedSpread.Text = $"{spread:F3}%";
                    SelectedSpread.Foreground = new SolidColorBrush(
                        spread > 0.1m ? (Color)ColorConverter.ConvertFromString("#10B981") :
                        spread > 0 ? (Color)ColorConverter.ConvertFromString("#F59E0B") :
                        (Color)ColorConverter.ConvertFromString("#EF4444"));

                    // Update estimated profit
                    if (decimal.TryParse(TradeAmountInput.Text, out var amount))
                    {
                        var feePercent = 0.2m; // 0.1% per side
                        var profit = amount * (spread - feePercent) / 100;
                        EstimatedProfit.Text = $"Estimated Profit: ${profit:F2}";
                        SelectedProfitEstimate.Text = $"Est. Profit: ${profit:F2} per trade";
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError("TradingPage", $"Error updating prices: {ex.Message}");
        }
    }

    /// <summary>
    /// Load REAL order book data from exchanges
    /// </summary>
    private async Task LoadRealOrderBookAsync()
    {
        if (_selectedPairDisplay == null || _exchangeFactory == null) return;

        try
        {
            Asks.Clear();
            Bids.Clear();

            var symbol = _selectedPairDisplay.Symbol.Replace("/", "");
            var client = _exchangeFactory.CreateRealClient(_selectedPairDisplay.ExchangeA);

            var orderBook = await client.GetOrderBookAsync(symbol, 10);

            if (orderBook != null)
            {
                // Load asks (sell orders)
                decimal maxAskTotal = orderBook.Asks.Any() ? orderBook.Asks.Max(a => a.Price * a.Quantity) : 1;
                foreach (var ask in orderBook.Asks.Take(5))
                {
                    Asks.Add(new OrderBookEntry
                    {
                        Price = ask.Price,
                        Amount = ask.Quantity,
                        Total = ask.Price * ask.Quantity,
                        DepthPercent = (int)((ask.Price * ask.Quantity) / maxAskTotal * 100)
                    });
                }

                // Load bids (buy orders)
                decimal maxBidTotal = orderBook.Bids.Any() ? orderBook.Bids.Max(b => b.Price * b.Quantity) : 1;
                foreach (var bid in orderBook.Bids.Take(5))
                {
                    Bids.Add(new OrderBookEntry
                    {
                        Price = bid.Price,
                        Amount = bid.Quantity,
                        Total = bid.Price * bid.Quantity,
                        DepthPercent = (int)((bid.Price * bid.Quantity) / maxBidTotal * 100)
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
        }
        catch (Exception ex)
        {
            _logger?.LogError("TradingPage", $"Error loading order book: {ex.Message}");
        }
    }

    private void ArbEngine_PriceUpdated(object? sender, PriceUpdateEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_selectedPair?.Symbol == e.Symbol)
            {
                if (_selectedPairDisplay != null &&
                    e.Exchange.Equals(_selectedPairDisplay.ExchangeA, StringComparison.OrdinalIgnoreCase))
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

            // Update session P&L tracking / อัปเดตการติดตามกำไร/ขาดทุนของ Session
            UpdateSessionPnL(e.Result);
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
        // Null check for event args and snapshot
        // ตรวจสอบ null สำหรับ event args และ snapshot
        if (e?.Snapshot?.CombinedBalances == null) return;

        Dispatcher.Invoke(() =>
        {
            try
            {
                if (e.Snapshot.CombinedBalances.TryGetValue("USDT", out var usdt))
                {
                    ExchangeABalance.Text = $"{usdt.ExchangeA_Total:N2}";
                    ExchangeBBalance.Text = $"{usdt.ExchangeB_Total:N2}";
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError("TradingPage", $"Error updating balance display: {ex.Message}");
            }
        });
    }

    private async void TradingPairsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TradingPairsList.SelectedItem is TradingPairDisplay selected)
        {
            _selectedPairDisplay = selected;

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

            // Update exchange modes display
            UpdateExchangeModes(selected.ExchangeA, selected.ExchangeB);

            // Update center spread display
            CenterSpreadDisplay.Text = $"{selected.CurrentSpread:F3}%";

            // Find actual TradingPair
            if (_arbEngine != null)
            {
                _selectedPair = _arbEngine.GetTradingPairs()
                    .FirstOrDefault(p => p.Symbol == selected.Symbol);
            }

            // Load real order book from exchange
            await LoadRealOrderBookAsync();

            // Update mode selector exchange names / อัปเดตชื่อกระดานในตัวเลือกโหมด
            ModeSelector.ExchangeAName = selected.ExchangeA;
            ModeSelector.ExchangeBName = selected.ExchangeB;

            // Update balance readiness for Dual-Balance mode / อัปเดตความพร้อมยอดสำหรับโหมดสองกระเป๋า
            if (_currentMode == ArbitrageExecutionMode.DualBalance)
            {
                _ = UpdateBalanceReadinessAsync();
            }
        }
    }

    private async void AddPairButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddPairDialog
        {
            Owner = Window.GetWindow(this)
        };

        dialog.ShowDialog();

        if (dialog.DialogResultOk && dialog.Result != null)
        {
            var config = dialog.Result;

            // Auto-save to Active Project (persistent)
            // บันทึกอัตโนมัติไปยัง Active Project (ข้อมูลถาวร)
            if (_projectService != null)
            {
                _suppressProjectReload = true;
                try
                {
                    var projectPair = new ProjectTradingPair
                    {
                        Symbol = config.Symbol,
                        BaseAsset = config.BaseAsset,
                        QuoteAsset = config.QuoteAsset,
                        ExchangeA = config.ExchangeA,
                        ExchangeB = config.ExchangeB,
                        TradeAmount = 100m,
                        IsEnabled = true,
                        Priority = 5
                    };

                    var saved = await _projectService.AddToActiveProjectAsync(projectPair);
                    if (!saved)
                    {
                        MessageBox.Show(
                            "Cannot add more pairs. Maximum is 10 per project.\nไม่สามารถเพิ่มคู่เทรดได้อีก สูงสุด 10 คู่ต่อโปรเจค",
                            "Limit Reached / ถึงขีดจำกัด",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                finally
                {
                    _suppressProjectReload = false;
                }
            }

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

        // Prevent double-click during transfer
        if (_activeTransfer != null)
        {
            MessageBox.Show("There is already an active transfer in progress.\nมีการโอนกำลังดำเนินการอยู่แล้ว",
                "Transfer Active", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ManualTradeButton.IsEnabled = false;
        ManualTradeButton.Content = "Executing...";

        try
        {
            _logger?.LogInfo("Trading", $"Manual trade: {_selectedPair.Symbol} - Amount: {amount} - Mode: {_currentMode}");

            if (_arbEngine != null)
            {
                var opportunity = await _arbEngine.AnalyzeOpportunityAsync(_selectedPair);

                // Execute using current mode (Dual-Balance or Transfer)
                var result = await _arbEngine.ExecuteArbitrageWithModeAsync(
                    opportunity, _currentMode, _currentTransferType);

                _logger?.LogInfo("Trading", $"Trade result: Status={result.Status} - PnL: ${result.NetPnL:F2} - Mode: {_currentMode}");

                if (_currentMode == ArbitrageExecutionMode.Transfer && result.TransferDetails != null)
                {
                    // Transfer Mode: Show transfer progress panel instead of result dialog
                    StartTransferProgressTracking(result.TransferDetails);

                    // Show manual transfer instructions if Manual type
                    if (_currentTransferType == TransferExecutionType.Manual && result.TransferDetails.ManualInstructions != null)
                    {
                        ShowManualTransferInstructions(result);
                    }
                    else
                    {
                        MessageBox.Show(
                            $"Buy order filled!\n\n" +
                            $"Symbol: {result.Symbol}\n" +
                            $"Amount: {result.TransferDetails.Amount:F8} {result.TransferDetails.Asset}\n" +
                            $"Buy Price: ${result.TransferDetails.PriceAtBuy:N2}\n\n" +
                            $"Auto transfer initiated. Monitor progress in the transfer panel.\n" +
                            $"คำสั่งซื้อเสร็จแล้ว! การโอนอัตโนมัติเริ่มต้นแล้ว",
                            "Transfer Initiated",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    // Dual-Balance Mode: Show immediate result
                    MessageBox.Show(
                        $"Trade Executed!\n\nSymbol: {result.Symbol}\n" +
                        $"P&L: ${result.NetPnL:F2}\n" +
                        $"Real P&L: ${result.RealPnL:F2}\n" +
                        $"Execution Time: {result.Metadata.GetValueOrDefault("TotalExecutionMs", 0)}ms",
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
            ManualTradeButton.Content = _currentMode == ArbitrageExecutionMode.Transfer
                ? "Execute Transfer Trade" : "Execute Trade";
        }
    }

    /// <summary>
    /// Show manual transfer instructions dialog
    /// แสดงหน้าต่างคำแนะนำการโอนเอง
    /// </summary>
    private void ShowManualTransferInstructions(TradeResult result)
    {
        var transfer = result.TransferDetails!;
        var instructions = transfer.ManualInstructions!;

        var message =
            $"Buy Order Filled! / คำสั่งซื้อเสร็จแล้ว!\n\n" +
            $"Amount: {transfer.Amount:F8} {transfer.Asset}\n" +
            $"Buy Price: ${transfer.PriceAtBuy:N2}\n\n" +
            $"--- Manual Transfer Required / ต้องโอนเอง ---\n\n" +
            $"From: {transfer.FromExchange}\n" +
            $"To: {transfer.ToExchange}\n" +
            $"Network: {instructions.Network}\n" +
            $"Deposit Address: {instructions.DepositAddress}\n" +
            (instructions.Memo != null ? $"Memo/Tag: {instructions.Memo}\n" : "") +
            $"\n⚠️ {instructions.WarningMessage}\n\n" +
            $"After you complete the transfer, click 'I've Transferred'\n" +
            $"in the transfer progress panel.\n" +
            $"หลังจากโอนเสร็จ ให้กดปุ่ม 'ฉันโอนแล้ว' ในแผงความคืบหน้า";

        MessageBox.Show(message, "Manual Transfer Instructions / คำแนะนำการโอนเอง",
            MessageBoxButton.OK, MessageBoxImage.Information);
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

        // Update manual trading state
        UpdateManualTradingState();
    }

    #region Manual Trading Handlers

    // Manual trading form handlers removed - simplified layout uses unified trade controls
    // ฟอร์มเทรดด้วยตนเองถูกลบออก - ใช้ปุ่มควบคุมเทรดแบบรวมในเลย์เอาต์ที่เรียบง่าย

    #endregion

    #region API Guide Handlers

    // API Guide panel removed in simplified layout - users can access API settings from Settings page
    // หน้าต่าง API Guide ถูกลบออก - ผู้ใช้สามารถเข้าถึงการตั้งค่า API ได้จากหน้า Settings

    private void OpenExchangeApiPage_Click(object sender, RoutedEventArgs e)
    {
        if (ExchangeApiUrls.TryGetValue(_currentApiGuideExchange, out var url))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError("Trading", $"Failed to open URL: {ex.Message}");
                MessageBox.Show($"Could not open browser. Please visit:\n{url}", "Open Browser",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    #endregion

    #region Arbitrage Mode Handlers / ตัวจัดการโหมด Arbitrage

    private async void ModeSelector_ModeChanged(object sender, ModeChangedEventArgs e)
    {
        _currentMode = e.NewMode;

        var modeInfo = ArbitrageModeInfo.GetModeInfo(e.NewMode);
        _logger?.LogInfo("TradingPage", $"Arbitrage mode changed to: {modeInfo.EnglishName} / {modeInfo.ThaiName}");

        // Update UI based on mode
        UpdateModeUI();

        // Update balance readiness when in Dual-Balance mode
        if (e.NewMode == ArbitrageExecutionMode.DualBalance)
        {
            _ = UpdateBalanceReadinessAsync();
        }

        // Save mode to Active Project settings / บันทึกโหมดลง Active Project
        if (_projectService != null)
        {
            _suppressProjectReload = true;
            try
            {
                var activeProject = await _projectService.GetActiveProjectAsync();
                if (activeProject != null)
                {
                    activeProject.Settings.PreferredArbitrageMode = _currentMode.ToString();
                    await _projectService.SaveProjectAsync(activeProject);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError("TradingPage", $"Error saving mode to project: {ex.Message}");
            }
            finally
            {
                _suppressProjectReload = false;
            }
        }
    }

    private void ModeSelector_TransferTypeChanged(object sender, TransferTypeChangedEventArgs e)
    {
        _currentTransferType = e.NewType;

        var typeInfo = TransferExecutionTypeInfo.GetTypeInfo(e.NewType);
        _logger?.LogInfo("TradingPage", $"Transfer type changed to: {typeInfo.EnglishName} / {typeInfo.ThaiName}");
    }

    private void UpdateModeUI()
    {
        // Update trade button text based on mode
        if (_currentMode == ArbitrageExecutionMode.Transfer)
        {
            ManualTradeButton.Content = "Execute Transfer Trade";
        }
        else
        {
            ManualTradeButton.Content = "Execute Trade";
        }
    }

    private async Task UpdateBalanceReadinessAsync()
    {
        if (_selectedPairDisplay == null || _balancePool == null) return;

        try
        {
            // Parse trade amount
            var tradeAmount = 100m; // Default
            if (decimal.TryParse(TradeAmountInput.Text, out var amount) && amount > 0)
            {
                tradeAmount = amount;
            }

            // Get balance readiness from balance pool
            var readiness = _balancePool.CheckDualBalanceReadiness(
                _selectedPairDisplay.ExchangeA,
                _selectedPairDisplay.ExchangeB,
                _selectedPairDisplay.BaseAsset,
                _selectedPairDisplay.QuoteAsset,
                tradeAmount);

            // Update mode selector control
            await Dispatcher.InvokeAsync(() =>
            {
                ModeSelector.UpdateBalanceReadiness(readiness);
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError("TradingPage", $"Error updating balance readiness: {ex.Message}");
        }
    }

    #endregion

    #region Transfer Progress Handlers / ตัวจัดการความคืบหน้าการโอน

    private void CancelTransferButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTransfer == null) return;

        var result = MessageBox.Show(
            "คุณต้องการยกเลิกการโอนนี้หรือไม่?\nDo you want to cancel this transfer?\n\n" +
            "⚠️ หากคุณได้โอนเหรียญไปแล้ว คุณต้องดำเนินการด้วยตนเอง\n" +
            "⚠️ If you've already transferred coins, you'll need to handle it manually.",
            "Cancel Transfer / ยกเลิกการโอน",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            CancelActiveTransfer();
        }
    }

    private async void CancelActiveTransfer()
    {
        if (_activeTransfer == null || _arbEngine == null) return;

        try
        {
            _logger?.LogInfo("TradingPage", $"Cancelling transfer: {_activeTransfer.TransferId}");

            // Cancel through engine if it supports it
            await _arbEngine.CancelTransferAsync(_activeTransfer.TransferId);

            _activeTransfer = null;
            _transferProgressTimer?.Stop();

            await Dispatcher.InvokeAsync(() =>
            {
                TransferProgressPanel.Visibility = Visibility.Collapsed;
            });

            _logger?.LogInfo("TradingPage", "Transfer cancelled successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError("TradingPage", $"Error cancelling transfer: {ex.Message}");
            MessageBox.Show($"Failed to cancel transfer: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StartTransferProgressTracking(TransferStatus transfer)
    {
        _activeTransfer = transfer;

        // Show transfer progress panel
        Dispatcher.Invoke(() =>
        {
            TransferProgressPanel.Visibility = Visibility.Visible;
            UpdateTransferProgressUI(transfer);

            // Show "I've Transferred" button for Manual mode (only when in Pending state)
            if (transfer.ExecutionType == TransferExecutionType.Manual &&
                transfer.State == TransferState.Pending)
            {
                ConfirmTransferButton.Visibility = Visibility.Visible;
            }
            else
            {
                ConfirmTransferButton.Visibility = Visibility.Collapsed;
            }

            // Display transfer fee
            if (transfer.TransferFee.HasValue && transfer.TransferFee > 0)
            {
                TransferFeeDisplay.Text = $"{transfer.TransferFee:G} {transfer.Asset}";
            }
            else
            {
                TransferFeeDisplay.Text = "N/A";
            }
        });

        // Start progress update timer
        _transferProgressTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _transferProgressTimer.Tick += async (s, e) => await UpdateTransferProgressAsync();
        _transferProgressTimer.Start();
    }

    private async Task UpdateTransferProgressAsync()
    {
        if (_activeTransfer == null || _arbEngine == null) return;

        try
        {
            // Get updated transfer status
            var updatedStatus = await _arbEngine.UpdateTransferStatusAsync(_activeTransfer.TransferId);

            if (updatedStatus != null)
            {
                _activeTransfer = updatedStatus;

                await Dispatcher.InvokeAsync(() =>
                {
                    UpdateTransferProgressUI(updatedStatus);
                });

                // Check if transfer is complete
                if (updatedStatus.State == TransferState.Completed ||
                    updatedStatus.State == TransferState.Failed ||
                    updatedStatus.State == TransferState.Cancelled)
                {
                    _transferProgressTimer?.Stop();

                    if (updatedStatus.State == TransferState.Completed)
                    {
                        // Complete the arbitrage trade (sell side)
                        await CompleteTransferArbitrageAsync(updatedStatus);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("TradingPage", $"Error updating transfer progress: {ex.Message}");
        }
    }

    private void UpdateTransferProgressUI(TransferStatus transfer)
    {
        // Update step indicators using XAML element names
        Step1Icon.Text = transfer.State >= TransferState.Pending ? "✓" : "○";
        Step1Icon.Foreground = new SolidColorBrush(
            transfer.State >= TransferState.Pending
                ? (Color)ColorConverter.ConvertFromString("#10B981")
                : (Color)ColorConverter.ConvertFromString("#60FFFFFF"));
        Step1Details.Text = transfer.Amount > 0
            ? $"{transfer.Amount:N8} {transfer.Asset}"
            : "Pending...";

        Step2Icon.Text = transfer.State >= TransferState.Withdrawing ? "⏳" :
                         transfer.State > TransferState.Withdrawing ? "✓" : "○";
        Step2Icon.Foreground = new SolidColorBrush(
            transfer.State > TransferState.Withdrawing
                ? (Color)ColorConverter.ConvertFromString("#10B981")
                : transfer.State == TransferState.Withdrawing
                    ? (Color)ColorConverter.ConvertFromString("#F59E0B")
                    : (Color)ColorConverter.ConvertFromString("#60FFFFFF"));
        Step2Details.Text = transfer.WithdrawalId ?? "Pending...";

        Step3Icon.Text = transfer.State == TransferState.InTransit ? "⏳" :
                         transfer.State > TransferState.InTransit ? "✓" : "○";
        Step3Icon.Foreground = new SolidColorBrush(
            transfer.State > TransferState.InTransit
                ? (Color)ColorConverter.ConvertFromString("#10B981")
                : transfer.State == TransferState.InTransit
                    ? (Color)ColorConverter.ConvertFromString("#F59E0B")
                    : (Color)ColorConverter.ConvertFromString("#60FFFFFF"));
        Step3Details.Text = transfer.Confirmations > 0
            ? $"{transfer.Confirmations}/{transfer.RequiredConfirmations} confirms"
            : "Waiting...";

        Step4Icon.Text = transfer.State >= TransferState.Depositing ? "⏳" :
                         transfer.State == TransferState.Completed ? "✓" : "○";
        Step4Icon.Foreground = new SolidColorBrush(
            transfer.State == TransferState.Completed
                ? (Color)ColorConverter.ConvertFromString("#10B981")
                : transfer.State == TransferState.Depositing
                    ? (Color)ColorConverter.ConvertFromString("#F59E0B")
                    : (Color)ColorConverter.ConvertFromString("#60FFFFFF"));
        Step4Details.Text = transfer.State == TransferState.Completed ? "Complete" : "Waiting...";

        // Update price monitor
        TransferBuyPrice.Text = $"${transfer.PriceAtBuy:N2}";
        if (transfer.CurrentPrice.HasValue && transfer.CurrentPrice > 0)
        {
            TransferCurrentPrice.Text = $"${transfer.CurrentPrice:N2}";
            TransferCurrentPrice.Foreground = new SolidColorBrush(
                transfer.CurrentPrice >= transfer.PriceAtBuy
                    ? (Color)ColorConverter.ConvertFromString("#10B981")
                    : (Color)ColorConverter.ConvertFromString("#EF4444"));
        }
        TransferUnrealizedPnL.Text = transfer.UnrealizedPnL >= 0
            ? $"+${transfer.UnrealizedPnL:F2}"
            : $"-${Math.Abs(transfer.UnrealizedPnL):F2}";
        TransferUnrealizedPnL.Foreground = new SolidColorBrush(
            transfer.UnrealizedPnL >= 0
                ? (Color)ColorConverter.ConvertFromString("#10B981")
                : (Color)ColorConverter.ConvertFromString("#EF4444"));

        // Update fee display
        if (transfer.TotalFees > 0)
        {
            TransferFeeDisplay.Text = $"{transfer.TotalFees:G} {transfer.Asset}";
        }

        // Show/hide "I've Transferred" button based on state
        if (transfer.ExecutionType == TransferExecutionType.Manual &&
            transfer.State == TransferState.Pending)
        {
            ConfirmTransferButton.Visibility = Visibility.Visible;
        }
        else
        {
            ConfirmTransferButton.Visibility = Visibility.Collapsed;
        }
    }

    private void ConfirmTransferButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTransfer == null || _arbEngine == null) return;

        var result = MessageBox.Show(
            "คุณได้โอนเหรียญไปยังกระดานปลายทางแล้วใช่หรือไม่?\n" +
            "Have you completed the transfer to the destination exchange?\n\n" +
            $"Asset: {_activeTransfer.Amount:F8} {_activeTransfer.Asset}\n" +
            $"From: {_activeTransfer.FromExchange} → To: {_activeTransfer.ToExchange}\n\n" +
            "ระบบจะเริ่มตรวจสอบการฝากและดำเนินการขายเมื่อเหรียญถึง\n" +
            "The system will monitor for the deposit and execute the sell order.",
            "Confirm Transfer / ยืนยันการโอน",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            // Mark manual transfer as confirmed
            if (_activeTransfer.ManualInstructions != null)
            {
                _activeTransfer.ManualInstructions.UserConfirmedTransfer = true;
                _activeTransfer.ManualInstructions.UserConfirmedTime = DateTime.UtcNow;
            }

            // Advance state to InTransit (waiting for blockchain confirmation)
            _activeTransfer.State = TransferState.InTransit;
            _logger?.LogInfo("TradingPage", $"User confirmed manual transfer: {_activeTransfer.TransferId}");

            // Update UI
            ConfirmTransferButton.Visibility = Visibility.Collapsed;
            UpdateTransferProgressUI(_activeTransfer);

            // Notify user that monitoring has started
            MessageBox.Show(
                "Transfer confirmed! Monitoring for deposit...\n" +
                "ยืนยันการโอนแล้ว! กำลังตรวจสอบการฝาก...\n\n" +
                "The sell order will execute automatically when the deposit is detected.\n" +
                "คำสั่งขายจะทำงานอัตโนมัติเมื่อตรวจพบการฝาก",
                "Monitoring / กำลังตรวจสอบ",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async Task CompleteTransferArbitrageAsync(TransferStatus transfer)
    {
        if (_arbEngine == null || _selectedPair == null) return;

        try
        {
            _logger?.LogInfo("TradingPage", $"Completing transfer arbitrage: {transfer.TransferId}");

            // Complete the sell side of the arbitrage
            var result = await _arbEngine.CompleteTransferArbitrageAsync(transfer.TransferId);

            await Dispatcher.InvokeAsync(() =>
            {
                // Hide transfer progress panel
                TransferProgressPanel.Visibility = Visibility.Collapsed;

                // Update session P&L
                if (result != null)
                {
                    UpdateSessionPnL(result);
                }
            });

            _activeTransfer = null;
            _logger?.LogInfo("TradingPage", $"Transfer arbitrage completed: PnL=${result?.NetPnL:F2}");
        }
        catch (Exception ex)
        {
            _logger?.LogError("TradingPage", $"Error completing transfer arbitrage: {ex.Message}");
            MessageBox.Show($"Failed to complete transfer: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Real P&L Tracking / ติดตามกำไร/ขาดทุนจริง

    private void UpdateSessionPnL(TradeResult result)
    {
        _sessionTradeCount++;
        _sessionPnL += result.RealPnL;
        _todayPnL += result.RealPnL;

        // Track balance changes if available
        if (result.BalanceChange != null)
        {
            _sessionUsdtChange += result.BalanceChange.NetProfitQuote;
            _sessionBtcChange += result.BalanceChange.TotalBaseChange;
        }

        // Update UI
        UpdateRealPnLDisplay();
    }

    private void UpdateRealPnLDisplay()
    {
        // Using correct XAML element names
        SessionPnL.Text = _sessionPnL >= 0 ? $"+${_sessionPnL:F2}" : $"-${Math.Abs(_sessionPnL):F2}";
        SessionPnL.Foreground = new SolidColorBrush(
            _sessionPnL >= 0
                ? (Color)ColorConverter.ConvertFromString("#10B981")
                : (Color)ColorConverter.ConvertFromString("#EF4444"));

        TodayPnL.Text = _todayPnL >= 0 ? $"+${_todayPnL:F2}" : $"-${Math.Abs(_todayPnL):F2}";
        TodayPnL.Foreground = new SolidColorBrush(
            _todayPnL >= 0
                ? (Color)ColorConverter.ConvertFromString("#10B981")
                : (Color)ColorConverter.ConvertFromString("#EF4444"));

        // Balance changes display
        USDTChangeText.Text = _sessionUsdtChange >= 0 ? $"+{_sessionUsdtChange:F2}" : $"{_sessionUsdtChange:F2}";
        USDTChangeText.Foreground = new SolidColorBrush(
            _sessionUsdtChange >= 0
                ? (Color)ColorConverter.ConvertFromString("#10B981")
                : (Color)ColorConverter.ConvertFromString("#EF4444"));

        BaseAssetChangeText.Text = _sessionBtcChange >= 0 ? $"+{_sessionBtcChange:F8}" : $"{_sessionBtcChange:F8}";
        BaseAssetChangeText.Foreground = new SolidColorBrush(
            _sessionBtcChange >= 0
                ? (Color)ColorConverter.ConvertFromString("#10B981")
                : (Color)ColorConverter.ConvertFromString("#EF4444"));
    }

    private async Task CaptureSessionStartBalances()
    {
        if (_selectedPairDisplay == null || _balancePool == null) return;

        try
        {
            _sessionStartSnapshot = await Task.Run(() =>
                _balancePool.GetCurrentBalanceSnapshot(
                    _selectedPairDisplay.ExchangeA,
                    _selectedPairDisplay.ExchangeB,
                    _selectedPairDisplay.BaseAsset,
                    _selectedPairDisplay.QuoteAsset));

            _logger?.LogInfo("TradingPage", "Session start balances captured");
        }
        catch (Exception ex)
        {
            _logger?.LogError("TradingPage", $"Error capturing session start balances: {ex.Message}");
        }
    }

    #endregion

    #region Scanner Methods / เมธอดสำหรับ Scanner

    private async void StartScanButton_Click(object sender, RoutedEventArgs e)
    {
        await StartScanningAsync();
    }

    private void StopScanButton_Click(object sender, RoutedEventArgs e)
    {
        StopScanning();
    }

    private void StrategyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Strategy changed - if scanning, restart with new strategy
        if (_isScanning && _scannerService != null)
        {
            _logger?.LogInfo("Scanner", $"Strategy changed to: {GetSelectedStrategyName()}");
        }
    }

    private void ScanResult_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is ScanResultDisplay result)
        {
            // Select this result and add to active pairs
            SelectScanResult(result);
        }
    }

    private async Task StartScanningAsync()
    {
        if (_isScanning || _scannerService == null)
        {
            _logger?.LogWarning("Scanner", "Cannot start scanning - already scanning or no scanner service");
            return;
        }

        _isScanning = true;
        _scanCts = new CancellationTokenSource();

        // Update UI
        StartScanButton.Visibility = Visibility.Collapsed;
        StopScanButton.Visibility = Visibility.Visible;
        ScannerStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
        ScannerStatusText.Text = "Scanning";
        ScannerStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
        ScanEmptyState.Visibility = Visibility.Collapsed;
        ScanLoadingState.Visibility = Visibility.Visible;

        _logger?.LogInfo("Scanner", "Started scanning");

        // Setup scan timer (every 5 seconds)
        _scanTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _scanTimer.Tick += ScanTimer_Tick;
        _scanTimer.Start();

        // Run initial scan
        await RunScanAsync();
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
        ScannerStatusText.Text = "Idle";
        ScannerStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#808080"));
        ScanLoadingState.Visibility = Visibility.Collapsed;

        _logger?.LogInfo("Scanner", "Stopped scanning");
    }

    private async void ScanTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isScanning) return;
        await RunScanAsync();
    }

    private async Task RunScanAsync()
    {
        if (_scannerService == null || (_scanCts?.IsCancellationRequested ?? true)) return;

        try
        {
            _scanCount++;

            await Dispatcher.InvokeAsync(() =>
            {
                ScanCountDisplay.Text = _scanCount.ToString();
                ScanLoadingState.Visibility = Visibility.Visible;
                ScanLoadingText.Text = "Scanning...";
            });

            var strategy = GetSelectedScanStrategy();
            var options = new ScanOptions
            {
                MinSpreadPercent = 0.1m,
                MaxResults = 10
            };

            // Add timeout to prevent hanging
            var scanTask = _scannerService.ScanAsync(strategy, options);
            var completedTask = await Task.WhenAny(
                scanTask,
                Task.Delay(TimeSpan.FromSeconds(30), _scanCts?.Token ?? CancellationToken.None)
            );

            if (completedTask != scanTask)
            {
                _logger?.LogWarning("Scanner", "Scan timed out after 30 seconds");
                await Dispatcher.InvokeAsync(() =>
                {
                    ScanLoadingState.Visibility = Visibility.Collapsed;
                    ScanEmptyState.Visibility = ScanResults.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                });
                return;
            }

            var results = await scanTask;

            await Dispatcher.InvokeAsync(() =>
            {
                ScanResults.Clear();
                var thbRate = _currencyConverter?.GetCachedThbUsdtRate() ?? 35.0m;
                var filtered = results?.Where(r => r.Score >= 30).Take(10).ToList() ?? new List<ScanResult>();

                foreach (var result in filtered)
                {
                    ScanResults.Add(new ScanResultDisplay(result, thbRate));
                }

                OpportunityCountDisplay.Text = ScanResults.Count.ToString();
                BestScoreDisplay.Text = ScanResults.Any() ? ScanResults.Max(r => r.Score).ToString("F0") : "0";

                ScanLoadingState.Visibility = Visibility.Collapsed;
                ScanEmptyState.Visibility = ScanResults.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                // Show recommended badge if any hot opportunities
                RecommendedBadge.Visibility = ScanResults.Any(r => r.IsRecommended) ? Visibility.Visible : Visibility.Collapsed;
            });
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInfo("Scanner", "Scan cancelled");
        }
        catch (Exception ex)
        {
            _logger?.LogError("Scanner", $"Scan error: {ex.Message}");
            await Dispatcher.InvokeAsync(() =>
            {
                ScanLoadingState.Visibility = Visibility.Collapsed;
                ScanEmptyState.Visibility = ScanResults.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            });
        }
    }

    private ScanStrategy GetSelectedScanStrategy()
    {
        var index = 0;
        Dispatcher.Invoke(() => { index = StrategyCombo.SelectedIndex; });

        return index switch
        {
            0 => ScanStrategy.ArbitrageBest,
            1 => ScanStrategy.PriceDrop,
            2 => ScanStrategy.HighVolatility,
            3 => ScanStrategy.VolumeSurge,
            4 => ScanStrategy.MomentumUp,
            5 => ScanStrategy.TopGainers,
            _ => ScanStrategy.ArbitrageBest
        };
    }

    private string GetSelectedStrategyName()
    {
        return GetSelectedScanStrategy().ToString();
    }

    private async void SelectScanResult(ScanResultDisplay result)
    {
        try
        {
            // Create a TradingPair from the scan result
            var pair = new TradingPair
            {
                Symbol = result.Symbol,
                BaseCurrency = result.BaseAsset,
                QuoteCurrency = "USDT",
                IsEnabled = true
            };

            var pairDisplay = new TradingPairDisplay(pair, result.BestBuyExchange, result.BestSellExchange)
            {
                CurrentSpread = result.SpreadPercent,
                Status = "Watching"
            };

            // Check if already exists
            var existing = TradingPairs.FirstOrDefault(p => p.Symbol == pairDisplay.Symbol);
            if (existing != null)
            {
                TradingPairsList.SelectedItem = existing;
                _selectedPairDisplay = existing;
            }
            else
            {
                // Auto-save to Active Project / บันทึกอัตโนมัติไปยัง Active Project
                if (_projectService != null)
                {
                    _suppressProjectReload = true;
                    try
                    {
                        var projectPair = new ProjectTradingPair
                        {
                            Symbol = result.Symbol,
                            BaseAsset = result.BaseAsset,
                            QuoteAsset = "USDT",
                            ExchangeA = result.BestBuyExchange,
                            ExchangeB = result.BestSellExchange,
                            TradeAmount = 100m,
                            IsEnabled = true,
                            Priority = 5
                        };
                        await _projectService.AddToActiveProjectAsync(projectPair);
                    }
                    finally
                    {
                        _suppressProjectReload = false;
                    }
                }

                TradingPairs.Insert(0, pairDisplay);
                TradingPairsList.SelectedItem = pairDisplay;
                _selectedPairDisplay = pairDisplay;
            }

            // Update the main view - trigger TradingPairsList_SelectionChanged
            // อัปเดต main view โดย trigger TradingPairsList_SelectionChanged
            _selectedPair = pair;

            _logger?.LogInfo("Scanner", $"Selected scan result: {result.Symbol}");
        }
        catch (Exception ex)
        {
            _logger?.LogError("Scanner", $"Error selecting scan result: {ex.Message}");
        }
    }

    #endregion
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

    public TradingPairDisplay(TradingPair pair, string exchangeA = "", string exchangeB = "")
    {
        Symbol = pair.Symbol;
        BaseAsset = pair.BaseCurrency;
        QuoteAsset = pair.QuoteCurrency;
        // Use provided exchange names, or fall back to defaults if not provided
        // ใช้ชื่อ exchange ที่ให้มา หรือใช้ค่าเริ่มต้นถ้าไม่มี
        ExchangeA = !string.IsNullOrEmpty(exchangeA) ? exchangeA : "Binance";
        ExchangeB = !string.IsNullOrEmpty(exchangeB) ? exchangeB : "KuCoin";
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

// ScanResultDisplay is defined in ScannerPage.xaml.cs and shared with TradingPage
// ScanResultDisplay ถูกกำหนดใน ScannerPage.xaml.cs และใช้ร่วมกับ TradingPage
