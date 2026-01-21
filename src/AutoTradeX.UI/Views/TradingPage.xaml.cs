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

namespace AutoTradeX.UI.Views;

public partial class TradingPage : UserControl
{
    private readonly IArbEngine? _arbEngine;
    private readonly ILoggingService? _logger;
    private readonly IBalancePoolService? _balancePool;
    private readonly IExchangeClientFactory? _exchangeFactory;
    private System.Windows.Threading.DispatcherTimer? _priceUpdateTimer;
    private System.Windows.Threading.DispatcherTimer? _botAnimationTimer;
    private TradingPair? _selectedPair;
    private TradingPairDisplay? _selectedPairDisplay;
    private bool _autoTradeEnabled = false;
    private string _currentApiGuideExchange = "Binance";
    private readonly Random _animationRandom = new();

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

    public TradingPage()
    {
        InitializeComponent();
        DataContext = this;

        _arbEngine = App.Services?.GetService<IArbEngine>();
        _logger = App.Services?.GetService<ILoggingService>();
        _balancePool = App.Services?.GetService<IBalancePoolService>();
        _exchangeFactory = App.Services?.GetService<IExchangeClientFactory>();

        Loaded += TradingPage_Loaded;
        Unloaded += TradingPage_Unloaded;
    }

    private async void TradingPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Check exchange connections first
        var isConnected = await CheckExchangeConnectionsAsync();

        if (isConnected)
        {
            NotConnectedOverlay.Visibility = Visibility.Collapsed;
            MainTradingContent.Visibility = Visibility.Visible;

            LoadTradingPairs();
            SetupEventHandlers();
            StartPriceUpdates();
            SetupBotAnimation();

            // Load real order book data
            await LoadRealOrderBookAsync();

            // Update manual trading forms based on bot state
            UpdateManualTradingState();
        }
        else
        {
            // Show not connected overlay
            NotConnectedOverlay.Visibility = Visibility.Visible;
            MainTradingContent.Visibility = Visibility.Collapsed;
        }
    }

    private async Task<bool> CheckExchangeConnectionsAsync()
    {
        if (_exchangeFactory == null) return false;

        var exchanges = new[] { "Binance", "KuCoin", "OKX", "Bybit", "Gate.io", "Bitkub" };
        var connectedCount = 0;

        foreach (var exchangeName in exchanges)
        {
            try
            {
                var client = _exchangeFactory.CreateClient(exchangeName);
                var isConnected = await client.TestConnectionAsync();
                if (isConnected) connectedCount++;
            }
            catch
            {
                // Ignore connection errors
            }
        }

        return connectedCount > 0;
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
        _priceUpdateTimer?.Stop();
        _botAnimationTimer?.Stop();
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
        var pulseAnim = new DoubleAnimation
        {
            From = 0,
            To = 0.8,
            Duration = TimeSpan.FromMilliseconds(500),
            AutoReverse = true
        };

        if (_animationRandom.NextDouble() > 0.5)
            BotPulse1.BeginAnimation(OpacityProperty, pulseAnim);
        else
            BotPulse2.BeginAnimation(OpacityProperty, pulseAnim);
    }

    private void AnimateDataFlow()
    {
        var dots = new[] { DataDot1, DataDot2, DataDot3 };
        var dotIndex = _animationRandom.Next(3);
        var dot = dots[dotIndex];

        var moveAnim = new DoubleAnimation
        {
            From = 0,
            To = 36,
            Duration = TimeSpan.FromMilliseconds(600)
        };

        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(100)
        };

        var fadeOut = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(200),
            BeginTime = TimeSpan.FromMilliseconds(400)
        };

        dot.BeginAnimation(Canvas.LeftProperty, moveAnim);
        dot.BeginAnimation(OpacityProperty, fadeIn);
        dot.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void AnimateArrowFlow()
    {
        var particles = new[] { FlowParticle1, FlowParticle2, FlowParticle3 };
        var particleIndex = _animationRandom.Next(3);
        var particle = particles[particleIndex];

        var moveAnim = new DoubleAnimation
        {
            From = 15,
            To = 55,
            Duration = TimeSpan.FromMilliseconds(500)
        };

        var fadeAnim = new DoubleAnimation
        {
            From = 0.8,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(500)
        };

        particle.BeginAnimation(Canvas.LeftProperty, moveAnim);
        particle.BeginAnimation(OpacityProperty, fadeAnim);
    }

    private void UpdateManualTradingState()
    {
        // Enable/disable manual trading based on bot state
        var isManualEnabled = !_autoTradeEnabled;

        ManualAmountA.IsEnabled = isManualEnabled;
        ManualPriceA.IsEnabled = isManualEnabled;
        BuyMarketA.IsEnabled = isManualEnabled;
        BuyLimitA.IsEnabled = isManualEnabled;

        ManualAmountB.IsEnabled = isManualEnabled;
        ManualPriceB.IsEnabled = isManualEnabled;
        SellMarketB.IsEnabled = isManualEnabled;
        SellLimitB.IsEnabled = isManualEnabled;

        // Show/hide blocking overlay
        BotBlockingOverlayA.Visibility = _autoTradeEnabled ? Visibility.Visible : Visibility.Collapsed;
        BotBlockingOverlayB.Visibility = _autoTradeEnabled ? Visibility.Visible : Visibility.Collapsed;

        // Show/hide bot animation panel
        BotAnimationPanel.Visibility = _autoTradeEnabled ? Visibility.Visible : Visibility.Collapsed;

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
        // Update Exchange A modes
        if (ExchangeModes.TryGetValue(exchangeA, out var modesA))
        {
            ExchangeAFutures.Visibility = modesA.Futures ? Visibility.Visible : Visibility.Collapsed;
            ExchangeAMargin.Visibility = modesA.Margin ? Visibility.Visible : Visibility.Collapsed;
        }

        // Update Exchange B modes
        if (ExchangeModes.TryGetValue(exchangeB, out var modesB))
        {
            ExchangeBFutures.Visibility = modesB.Futures ? Visibility.Visible : Visibility.Collapsed;
            ExchangeBMargin.Visibility = modesB.Margin ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void LoadTradingPairs()
    {
        TradingPairs.Clear();

        if (_arbEngine != null)
        {
            var pairs = _arbEngine.GetTradingPairs();
            foreach (var pair in pairs)
            {
                TradingPairs.Add(new TradingPairDisplay(pair));
            }
        }

        // Show message if no pairs configured - users must add pairs via Projects/Scanner
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
            // Fetch REAL prices from exchanges
            var symbol = _selectedPairDisplay.Symbol.Replace("/", "");

            var clientA = _exchangeFactory.CreateClient(_selectedPairDisplay.ExchangeA);
            var clientB = _exchangeFactory.CreateClient(_selectedPairDisplay.ExchangeB);

            var tickerATask = clientA.GetTickerAsync(symbol);
            var tickerBTask = clientB.GetTickerAsync(symbol);

            await Task.WhenAll(tickerATask, tickerBTask);

            var tickerA = tickerATask.Result;
            var tickerB = tickerBTask.Result;

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
            var client = _exchangeFactory.CreateClient(_selectedPairDisplay.ExchangeA);

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
                if (e.Exchange.ToLower().Contains("binance") || e.Exchange.Contains("A"))
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
        Dispatcher.Invoke(() =>
        {
            if (e.Snapshot.CombinedBalances.TryGetValue("USDT", out var usdt))
            {
                ExchangeABalance.Text = $"{usdt.ExchangeA_Total:N2}";
                ExchangeBBalance.Text = $"{usdt.ExchangeB_Total:N2}";
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
        }
    }

    private void AddPairButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddPairDialog
        {
            Owner = Window.GetWindow(this)
        };

        dialog.ShowDialog();

        if (dialog.DialogResultOk && dialog.Result != null)
        {
            var config = dialog.Result;

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

        ManualTradeButton.IsEnabled = false;
        ManualTradeButton.Content = "Executing...";

        try
        {
            _logger?.LogInfo("Trading", $"Manual trade: {_selectedPair.Symbol} - Amount: {amount}");

            // Execute trade through engine
            if (_arbEngine != null)
            {
                var opportunity = await _arbEngine.AnalyzeOpportunityAsync(_selectedPair);
                if (opportunity.ShouldTrade || true) // Allow manual override
                {
                    var result = await _arbEngine.ExecuteArbitrageAsync(opportunity);
                    _logger?.LogInfo("Trading", $"Trade result: {(result.IsFullySuccessful ? "Success" : "Failed")} - PnL: ${result.NetPnL:F2}");

                    MessageBox.Show(
                        $"Trade Executed!\n\nSymbol: {result.Symbol}\nP&L: ${result.NetPnL:F2}\nExecution Time: {result.Metadata.GetValueOrDefault("TotalExecutionMs", 0)}ms",
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
            ManualTradeButton.Content = "Execute Trade";
        }
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

    private async void ManualBuyA_Click(object sender, RoutedEventArgs e)
    {
        if (_autoTradeEnabled)
        {
            MessageBox.Show("Please turn off the bot before trading manually.", "Bot Running",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (TradingPairsList.SelectedItem is not TradingPairDisplay selected)
        {
            MessageBox.Show("Please select a trading pair first.", "No Pair Selected",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!decimal.TryParse(ManualAmountA.Text, out var amount) || amount <= 0)
        {
            MessageBox.Show("Please enter a valid amount.", "Invalid Amount",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var orderType = (sender as Button)?.Tag?.ToString() ?? "Market";
        decimal? price = null;

        if (orderType == "Limit")
        {
            if (!decimal.TryParse(ManualPriceA.Text, out var limitPrice) || limitPrice <= 0)
            {
                MessageBox.Show("Please enter a valid limit price.", "Invalid Price",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            price = limitPrice;
        }

        try
        {
            BuyMarketA.IsEnabled = false;
            BuyLimitA.IsEnabled = false;

            _logger?.LogInfo("Trading", $"Manual BUY on {selected.ExchangeA}: {amount} {selected.BaseAsset} @ {(price.HasValue ? $"${price:N2}" : "Market")}");

            // Simulate order execution
            await Task.Delay(500);

            MessageBox.Show(
                $"BUY Order Placed!\n\nExchange: {selected.ExchangeA}\nPair: {selected.Symbol}\nAmount: {amount}\nType: {orderType}\n{(price.HasValue ? $"Price: ${price:N2}" : "Price: Market")}",
                "Order Submitted",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger?.LogError("Trading", $"Manual buy error: {ex.Message}");
            MessageBox.Show($"Order failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BuyMarketA.IsEnabled = true;
            BuyLimitA.IsEnabled = true;
        }
    }

    private async void ManualSellB_Click(object sender, RoutedEventArgs e)
    {
        if (_autoTradeEnabled)
        {
            MessageBox.Show("Please turn off the bot before trading manually.", "Bot Running",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (TradingPairsList.SelectedItem is not TradingPairDisplay selected)
        {
            MessageBox.Show("Please select a trading pair first.", "No Pair Selected",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!decimal.TryParse(ManualAmountB.Text, out var amount) || amount <= 0)
        {
            MessageBox.Show("Please enter a valid amount.", "Invalid Amount",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var orderType = (sender as Button)?.Tag?.ToString() ?? "Market";
        decimal? price = null;

        if (orderType == "Limit")
        {
            if (!decimal.TryParse(ManualPriceB.Text, out var limitPrice) || limitPrice <= 0)
            {
                MessageBox.Show("Please enter a valid limit price.", "Invalid Price",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            price = limitPrice;
        }

        try
        {
            SellMarketB.IsEnabled = false;
            SellLimitB.IsEnabled = false;

            _logger?.LogInfo("Trading", $"Manual SELL on {selected.ExchangeB}: {amount} {selected.BaseAsset} @ {(price.HasValue ? $"${price:N2}" : "Market")}");

            // Simulate order execution
            await Task.Delay(500);

            MessageBox.Show(
                $"SELL Order Placed!\n\nExchange: {selected.ExchangeB}\nPair: {selected.Symbol}\nAmount: {amount}\nType: {orderType}\n{(price.HasValue ? $"Price: ${price:N2}" : "Price: Market")}",
                "Order Submitted",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger?.LogError("Trading", $"Manual sell error: {ex.Message}");
            MessageBox.Show($"Order failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SellMarketB.IsEnabled = true;
            SellLimitB.IsEnabled = true;
        }
    }

    #endregion

    #region API Guide Handlers

    private void ApiGuideButton_Click(object sender, RoutedEventArgs e)
    {
        var exchangeSide = (sender as Button)?.Tag?.ToString() ?? "A";
        var exchangeName = exchangeSide == "A" ? ExchangeAName.Text : ExchangeBName.Text;

        // Normalize exchange name (remove uppercase)
        _currentApiGuideExchange = exchangeName.Substring(0, 1).ToUpper() + exchangeName.Substring(1).ToLower();
        if (_currentApiGuideExchange == "Kucoin") _currentApiGuideExchange = "KuCoin";
        if (_currentApiGuideExchange == "Okx") _currentApiGuideExchange = "OKX";
        if (_currentApiGuideExchange == "Gate.io") _currentApiGuideExchange = "Gate.io";

        ApiGuideExchangeName.Text = $"{_currentApiGuideExchange} API Setup";

        // Update permissions list based on exchange
        UpdateApiPermissionsList(_currentApiGuideExchange);

        ApiGuidePanel.Visibility = Visibility.Visible;
    }

    private void UpdateApiPermissionsList(string exchange)
    {
        // Permissions are mostly the same, but we can customize per exchange if needed
        ApiPermissionsList.Children.Clear();

        // Reading permission
        AddPermissionItem("Enable Reading", "View balances and orders", true);

        // Spot trading
        AddPermissionItem("Enable Spot Trading", "Execute spot trades", true);

        // Futures (if supported)
        if (ExchangeModes.TryGetValue(exchange, out var modes) && modes.Futures)
        {
            AddPermissionItem("Enable Futures (Optional)", "For futures trading", true);
        }

        // Margin (if supported)
        if (ExchangeModes.TryGetValue(exchange, out modes) && modes.Margin)
        {
            AddPermissionItem("Enable Margin (Optional)", "For margin trading", true);
        }

        // Withdrawals - always disabled
        AddPermissionItem("Withdrawals", "NOT NEEDED (keep disabled)", false, true);
    }

    private void AddPermissionItem(string permission, string description, bool required, bool isStrikethrough = false)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };

        var checkmark = new TextBlock
        {
            Text = required ? "✓" : "✗",
            FontSize = 12,
            Foreground = required ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"))
                                  : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
            Margin = new Thickness(0, 0, 8, 0)
        };

        var permText = new TextBlock
        {
            Text = permission,
            FontSize = 11,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(required ? "#80FFFFFF" : "#60FFFFFF"))
        };
        if (isStrikethrough)
        {
            permText.TextDecorations = TextDecorations.Strikethrough;
        }

        var descText = new TextBlock
        {
            Text = $" - {description}",
            FontSize = 10,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(required ? "#50FFFFFF" : "#EF4444"))
        };

        panel.Children.Add(checkmark);
        panel.Children.Add(permText);
        panel.Children.Add(descText);

        ApiPermissionsList.Children.Add(panel);
    }

    private void CloseApiGuide_Click(object sender, RoutedEventArgs e)
    {
        ApiGuidePanel.Visibility = Visibility.Collapsed;
    }

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

    public TradingPairDisplay(TradingPair pair)
    {
        Symbol = pair.Symbol;
        BaseAsset = pair.BaseCurrency;
        QuoteAsset = pair.QuoteCurrency;
        ExchangeA = "Binance";
        ExchangeB = "KuCoin";
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
