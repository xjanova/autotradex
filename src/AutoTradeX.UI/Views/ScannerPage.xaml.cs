using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using AutoTradeX.Infrastructure.Services;

namespace AutoTradeX.UI.Views;

public partial class ScannerPage : UserControl
{
    // Services (not readonly - may need to be re-fetched after App.Services is ready)
    // Services ไม่ใช่ readonly เพราะอาจต้อง re-fetch หลังจาก App.Services พร้อม
    private ISmartScannerService? _scanner;
    private ICoinDataService? _coinDataService;
    private ILoggingService? _logger;
    private IArbEngine? _arbEngine;
    private IProjectService? _projectService;
    private IStrategyService? _strategyService;
    private IExchangeClientFactory? _exchangeFactory;
    private ICurrencyConverterService? _currencyConverter;
    private IConnectionStatusService? _connectionStatusService;
    private IApiCredentialsService? _apiCredentialsService;

    private System.Windows.Threading.DispatcherTimer? _scanTimer;
    private bool _isScanning = false;
    private int _scanCount = 0;
    private CancellationTokenSource? _scanCts;
    private bool _autoMode = false;
    private bool _isInitialized = false;

    public ObservableCollection<ScanResultDisplay> ScanResults { get; } = new();

    public ScannerPage()
    {
        InitializeComponent();
        DataContext = this;

        // Try to get services (may be null if App.Services not ready yet)
        // พยายาม get services (อาจเป็น null ถ้า App.Services ยังไม่พร้อม)
        TryInitializeServices();

        ResultsList.ItemsSource = ScanResults;

        Loaded += ScannerPage_Loaded;
        Unloaded += ScannerPage_Unloaded;
    }

    /// <summary>
    /// Try to initialize services from DI container
    /// พยายาม initialize services จาก DI container
    /// </summary>
    private void TryInitializeServices()
    {
        if (App.Services == null) return;

        _scanner ??= App.Services.GetService<ISmartScannerService>();
        _coinDataService ??= App.Services.GetService<ICoinDataService>();
        _logger ??= App.Services.GetService<ILoggingService>();
        _arbEngine ??= App.Services.GetService<IArbEngine>();
        _projectService ??= App.Services.GetService<IProjectService>();
        _strategyService ??= App.Services.GetService<IStrategyService>();
        _exchangeFactory ??= App.Services.GetService<IExchangeClientFactory>();
        _currencyConverter ??= App.Services.GetService<ICurrencyConverterService>();
        _connectionStatusService ??= App.Services.GetService<IConnectionStatusService>();
        _apiCredentialsService ??= App.Services.GetService<IApiCredentialsService>();
    }

    /// <summary>
    /// Ensure all services are initialized. Called on page load.
    /// ตรวจสอบว่า services ถูก initialize แล้ว เรียกเมื่อ load หน้า
    /// </summary>
    private void EnsureServicesInitialized()
    {
        TryInitializeServices();

        if (_exchangeFactory == null || _scanner == null)
        {
            _logger?.LogWarning("ScannerPage", "Some services are still null after initialization attempt");
        }
    }

    private async void ScannerPage_Loaded(object sender, RoutedEventArgs e)
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
            MainScannerContent.Visibility = Visibility.Visible;

            // Subscribe to events
            SetupEventHandlers();

            _isInitialized = true;
        }
        else
        {
            // Show not connected overlay
            NotConnectedOverlay.Visibility = Visibility.Visible;
            MainScannerContent.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Setup event handlers for scanner
    /// ตั้งค่า event handlers สำหรับ scanner
    /// </summary>
    private void SetupEventHandlers()
    {
        if (_scanner != null)
        {
            _scanner.OpportunityFound += Scanner_OpportunityFound;
        }

        if (_arbEngine != null)
        {
            _arbEngine.OpportunityFound += ArbEngine_OpportunityFound;
        }
    }

    /// <summary>
    /// Cleanup event handlers to prevent memory leaks
    /// ยกเลิก event handlers เพื่อป้องกัน memory leak
    /// </summary>
    private void CleanupEventHandlers()
    {
        if (_scanner != null)
        {
            _scanner.OpportunityFound -= Scanner_OpportunityFound;
        }

        if (_arbEngine != null)
        {
            _arbEngine.OpportunityFound -= ArbEngine_OpportunityFound;
        }
    }

    private void ArbEngine_OpportunityFound(object? sender, OpportunityEventArgs e)
    {
        // Handle arbitrage engine opportunity if needed
        Dispatcher.Invoke(() =>
        {
            _logger?.LogInfo("Scanner", $"ArbEngine opportunity: {e.Pair.Symbol} spread {e.Opportunity.NetSpreadPercentage:F3}%");
        });
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
                _logger?.LogInfo("ScannerPage", $"Connection check via service: {connectedCount} exchanges connected");
                return connectedCount > 0;
            }
            catch (Exception ex)
            {
                _logger?.LogError("ScannerPage", $"Error checking connections via service: {ex.Message}");
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
                _logger?.LogWarning("ScannerPage", $"Connection check failed for {exchangeName}: {ex.Message}");
            }
        }

        _logger?.LogInfo("ScannerPage", $"Manual connection check: {connectedCount2} exchanges connected");
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

    private void ScannerPage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Stop scanning and cleanup resources
        // หยุดการสแกนและ cleanup ทรัพยากร
        StopScanning();

        // Cleanup event handlers to prevent memory leaks
        // ยกเลิก event handlers เพื่อป้องกัน memory leak
        CleanupEventHandlers();

        // Dispose CancellationTokenSource
        // Dispose CancellationTokenSource
        DisposeCancellationToken();

        // Stop and dispose timer
        // หยุดและ dispose timer
        DisposeTimer();
    }

    /// <summary>
    /// Dispose CancellationTokenSource properly
    /// Dispose CancellationTokenSource อย่างถูกต้อง
    /// </summary>
    private void DisposeCancellationToken()
    {
        try
        {
            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = null;
        }
        catch (ObjectDisposedException)
        {
            // Already disposed, ignore
        }
    }

    /// <summary>
    /// Dispose timer properly
    /// Dispose timer อย่างถูกต้อง
    /// </summary>
    private void DisposeTimer()
    {
        try
        {
            if (_scanTimer != null)
            {
                _scanTimer.Stop();
                _scanTimer.Tick -= ScanTimer_Tick;
                _scanTimer = null;
            }
        }
        catch
        {
            // Ignore timer disposal errors
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

        // Cancel and dispose token
        // ยกเลิกและ dispose token
        DisposeCancellationToken();

        // Stop timer but don't dispose yet (might restart)
        // หยุด timer แต่ยังไม่ dispose (อาจเริ่มใหม่)
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

            await Dispatcher.InvokeAsync(() =>
            {
                ScanCountDisplay.Text = _scanCount.ToString();

                // Show loading
                LoadingState.Visibility = Visibility.Visible;
                LoadingText.Text = $"Scanning with {GetSelectedStrategy()} strategy...";
            });

            var strategy = GetSelectedStrategy();

            // Get slider values on UI thread
            decimal minSpread = 0.1m;
            decimal minScore = 50m;
            await Dispatcher.InvokeAsync(() =>
            {
                minSpread = (decimal)MinSpreadSlider.Value;
                minScore = (decimal)MinScoreSlider.Value;
            });

            var options = new ScanOptions
            {
                MinSpreadPercent = minSpread,
                MaxResults = 30
            };

            // Add timeout to prevent hanging if scanner is slow
            // เพิ่ม timeout เพื่อป้องกันการค้างถ้า scanner ช้า
            var scanTask = _scanner.ScanAsync(strategy, options);
            var completedTask = await Task.WhenAny(
                scanTask,
                Task.Delay(TimeSpan.FromSeconds(30), _scanCts?.Token ?? CancellationToken.None)
            );

            if (completedTask != scanTask)
            {
                _logger?.LogWarning("Scanner", "Scan timed out after 30 seconds");
                await Dispatcher.InvokeAsync(() =>
                {
                    LoadingState.Visibility = Visibility.Collapsed;
                });
                return;
            }

            var results = await scanTask;

            await Dispatcher.InvokeAsync(() =>
            {
                // Filter by min score
                var filtered = results?.Where(r => r.Score >= minScore).ToList() ?? new List<ScanResult>();

                ScanResults.Clear();
                var thbRate = _currencyConverter?.GetCachedThbUsdtRate() ?? 35.0m;
                foreach (var result in filtered)
                {
                    ScanResults.Add(new ScanResultDisplay(result, thbRate));
                }

                UpdateStats();
                EmptyState.Visibility = ScanResults.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
                LoadingState.Visibility = Visibility.Collapsed;

                if (ScanResults.Any(r => r.IsRecommended))
                {
                    RecommendedBadge.Visibility = Visibility.Visible;
                }
                else
                {
                    RecommendedBadge.Visibility = Visibility.Collapsed;
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Scan was cancelled, ignore
            _logger?.LogInfo("Scanner", "Scan cancelled");
        }
        catch (Exception ex)
        {
            _logger?.LogError("Scanner", $"Scan error: {ex.Message}");
            await Dispatcher.InvokeAsync(() =>
            {
                LoadingState.Visibility = Visibility.Collapsed;
            });
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

                // Show action selection dialog
                var dialog = new ScannerActionDialog(result);
                dialog.Owner = Window.GetWindow(this);
                dialog.ShowDialog();

                if (dialog.SelectedAction == ScannerAction.AddToProject)
                {
                    await AddToProjectAsync(result);
                }
                else if (dialog.SelectedAction == ScannerAction.ExecuteTrade)
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

    private async Task AddToProjectAsync(ScanResultDisplay result)
    {
        if (_projectService == null)
        {
            MessageBox.Show("Project service not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            // Auto-add to Active Project (no dialog needed)
            // เพิ่มอัตโนมัติไปยัง Active Project (ไม่ต้องเปิด dialog)
            var activeProject = await _projectService.GetActiveProjectAsync();
            if (activeProject != null)
            {
                // Check if can add more pairs
                var canAdd = await _projectService.CanAddMorePairsAsync(activeProject.Id);
                if (!canAdd)
                {
                    MessageBox.Show(
                        $"โปรเจค \"{activeProject.Name}\" มีคู่เทรดเต็ม (สูงสุด 10 คู่)\n" +
                        $"Project \"{activeProject.Name}\" has maximum trading pairs (10 max).\n\n" +
                        "กรุณาลบคู่เทรดที่ไม่ใช้ก่อนเพิ่มคู่ใหม่\n" +
                        "Please remove unused pairs before adding new ones.",
                        "Limit Reached / ถึงขีดจำกัด",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var newPair = new ProjectTradingPair
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

                var saved = await _projectService.AddToActiveProjectAsync(newPair);
                if (saved)
                {
                    _logger?.LogInfo("Scanner", $"Added {result.Symbol} to active project: {activeProject.Name}");

                    MessageBox.Show(
                        $"เพิ่ม {result.Symbol} เข้าโปรเจค \"{activeProject.Name}\" เรียบร้อยแล้ว!\n\n" +
                        $"Exchange: {result.BestBuyExchange} → {result.BestSellExchange}\n" +
                        $"คู่เทรดจะปรากฏในหน้า Trading อัตโนมัติ",
                        "เพิ่มคู่เทรดสำเร็จ / Pair Added",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    _logger?.LogInfo("Scanner", $"Pair {result.Symbol} already exists in active project");
                    MessageBox.Show(
                        $"{result.Symbol} มีอยู่ในโปรเจคแล้ว\n{result.Symbol} already exists in the project.",
                        "Already Exists / มีอยู่แล้ว",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            // Fallback: Show project selection dialog if no active project
            // สำรอง: แสดง dialog เลือกโปรเจคถ้าไม่มี active project
            var projects = await _projectService.GetAllProjectsAsync();
            if (!projects.Any())
            {
                var createResult = MessageBox.Show(
                    "ยังไม่มีโปรเจคใดๆ\n\nต้องการสร้างโปรเจคใหม่หรือไม่?",
                    "ไม่พบโปรเจค",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (createResult == MessageBoxResult.Yes)
                {
                    if (Window.GetWindow(this) is MainWindow mainWindow)
                    {
                        mainWindow.NavigateToPage("Projects");
                    }
                }
                return;
            }

            var selectDialog = new SelectProjectDialog(projects, result);
            selectDialog.Owner = Window.GetWindow(this);
            selectDialog.ShowDialog();

            if (selectDialog.DialogResultOk && selectDialog.SelectedProject != null)
            {
                var strategies = _strategyService?.GetPresetStrategies() ?? Enumerable.Empty<TradingStrategy>();

                var dialogPair = new ProjectTradingPair
                {
                    Symbol = result.Symbol,
                    BaseAsset = result.BaseAsset,
                    QuoteAsset = "USDT",
                    ExchangeA = result.BestBuyExchange,
                    ExchangeB = result.BestSellExchange,
                    StrategyId = selectDialog.SelectedStrategyId ?? strategies.FirstOrDefault()?.Id ?? "default",
                    TradeAmount = selectDialog.TradeAmount,
                    IsEnabled = true
                };

                await _projectService.AddTradingPairAsync(selectDialog.SelectedProject.Id, dialogPair);

                _logger?.LogInfo("Scanner", $"Added {result.Symbol} to project: {selectDialog.SelectedProject.Name}");

                MessageBox.Show(
                    $"เพิ่ม {result.Symbol} เข้าโปรเจค \"{selectDialog.SelectedProject.Name}\" เรียบร้อยแล้ว!\n\n" +
                    $"Exchange: {result.BestBuyExchange} → {result.BestSellExchange}\n" +
                    $"Trade Amount: ${selectDialog.TradeAmount:N0}",
                    "เพิ่มคู่เทรดสำเร็จ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"เกิดข้อผิดพลาด: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _logger?.LogError("Scanner", $"Failed to add pair to project: {ex.Message}");
        }
    }

    private async Task ExecuteTradeAsync(ScanResult result)
    {
        try
        {
            _logger?.LogInfo("Scanner", $"Executing trade for {result.Symbol}");

            if (_arbEngine == null)
            {
                MessageBox.Show("Trading engine not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Confirm with user before executing
            var confirmResult = MessageBox.Show(
                $"Execute Arbitrage Trade?\n\n" +
                $"Symbol: {result.Symbol}\n" +
                $"Buy: {result.BestBuyExchange} @ ${result.BestBuyPrice:N2}\n" +
                $"Sell: {result.BestSellExchange} @ ${result.BestSellPrice:N2}\n" +
                $"Spread: {result.SpreadPercent:F4}%\n" +
                $"Expected Profit: ${result.EstimatedProfit:F2}",
                "Confirm Trade",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes) return;

            // Create a TradingPair and analyze the opportunity via ArbEngine
            var pair = TradingPair.FromSymbol(result.Symbol);
            var opportunity = await _arbEngine.AnalyzeOpportunityAsync(pair);

            if (!opportunity.ShouldTrade)
            {
                MessageBox.Show(
                    $"Trade conditions no longer met:\n{opportunity.Remarks}",
                    "Trade Cancelled",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var tradeResult = await _arbEngine.ExecuteArbitrageWithModeAsync(
                opportunity, ArbitrageExecutionMode.DualBalance);

            var statusText = tradeResult.IsFullySuccessful ? "สำเร็จ" : $"สถานะ: {tradeResult.Status}";
            MessageBox.Show(
                $"Trade Result: {statusText}\n\n" +
                $"Symbol: {result.Symbol}\n" +
                $"Net P&L: ${tradeResult.NetPnL:F4}\n" +
                $"Duration: {tradeResult.DurationMs}ms",
                tradeResult.IsFullySuccessful ? "Trade Success" : "Trade Complete",
                MessageBoxButton.OK,
                tradeResult.IsFullySuccessful ? MessageBoxImage.Information : MessageBoxImage.Warning);
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
/// Supports THB conversion for Bitkub exchange
/// </summary>
public class ScanResultDisplay : INotifyPropertyChanged
{
    private readonly ScanResult _result;
    private readonly decimal _thbRate;

    public ScanResultDisplay(ScanResult result, decimal thbRate = 35.0m)
    {
        _result = result;
        _thbRate = thbRate;
    }

    public ScanResult ToScanResult() => _result;

    /// <summary>
    /// Check if Bitkub is involved (uses THB)
    /// </summary>
    public bool InvolvesBitkub =>
        BestBuyExchange.Contains("Bitkub", StringComparison.OrdinalIgnoreCase) ||
        BestSellExchange.Contains("Bitkub", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// THB/USDT rate for display
    /// </summary>
    public decimal ThbRate => _thbRate;

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

    // THB Display Properties (for Bitkub)
    public decimal BestBuyPriceThb => BestBuyPrice * _thbRate;
    public decimal BestSellPriceThb => BestSellPrice * _thbRate;
    public decimal EstimatedProfitThb => EstimatedProfit * _thbRate;

    /// <summary>
    /// Display buy price with THB if Bitkub is involved
    /// </summary>
    public string BuyPriceDisplay
    {
        get
        {
            var isBitkubBuy = BestBuyExchange.Contains("Bitkub", StringComparison.OrdinalIgnoreCase);
            if (isBitkubBuy)
            {
                return $"฿{BestBuyPriceThb:N2}";
            }
            return BestBuyPrice >= 1000 ? $"${BestBuyPrice:N0}" : $"${BestBuyPrice:N2}";
        }
    }

    /// <summary>
    /// Display sell price with THB if Bitkub is involved
    /// </summary>
    public string SellPriceDisplay
    {
        get
        {
            var isBitkubSell = BestSellExchange.Contains("Bitkub", StringComparison.OrdinalIgnoreCase);
            if (isBitkubSell)
            {
                return $"฿{BestSellPriceThb:N2}";
            }
            return BestSellPrice >= 1000 ? $"${BestSellPrice:N0}" : $"${BestSellPrice:N2}";
        }
    }

    /// <summary>
    /// THB rate display for reference
    /// </summary>
    public string ThbRateDisplay => InvolvesBitkub ? $"(1 USDT = ฿{_thbRate:N2})" : "";

    /// <summary>
    /// Show THB info badge visibility
    /// </summary>
    public Visibility ThbInfoVisibility => InvolvesBitkub ? Visibility.Visible : Visibility.Collapsed;

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
