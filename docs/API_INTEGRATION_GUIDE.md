# AutoTrade-X API Integration Guide

> **Version:** 0.2.0
> **Last Updated:** 2026-01-27

คู่มือนี้อธิบายวิธีเรียกใช้ API และการเชื่อมโยงระหว่าง Layer ต่างๆ ในโปรเจค

---

## Project Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        AutoTradeX.UI                             │
│  (WPF Application - Views, Controls, ViewModels)                │
│                                                                  │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────┐  │
│  │ AITradingPage │  │ DashboardPage │  │ TradingPage/Scanner │  │
│  └──────┬───────┘  └──────┬───────┘  └──────────┬───────────┘  │
└─────────┼─────────────────┼─────────────────────┼───────────────┘
          │                 │                     │
          │    Dependency Injection (App.Services)
          │                 │                     │
┌─────────┼─────────────────┼─────────────────────┼───────────────┐
│         ▼                 ▼                     ▼               │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │              AutoTradeX.Infrastructure                   │   │
│  │                                                          │   │
│  │  ┌─────────────────┐  ┌──────────────────────────────┐  │   │
│  │  │ DemoWalletService│  │ ExchangeClients (Real API)   │  │   │
│  │  │ (Demo Trading)   │  │ - BinanceClient              │  │   │
│  │  └─────────────────┘  │ - KuCoinClient                │  │   │
│  │                       │ - OKXClient                   │  │   │
│  │  ┌─────────────────┐  │ - BybitClient                 │  │   │
│  │  │ DatabaseService │  │ - GateIOClient                │  │   │
│  │  │ (SQLite)        │  │ - BitkubClient                │  │   │
│  │  └─────────────────┘  └──────────────────────────────┘  │   │
│  │                                                          │   │
│  │  ┌─────────────────┐  ┌──────────────────────────────┐  │   │
│  │  │ LicenseService  │  │ ConnectionStatusService      │  │   │
│  │  └─────────────────┘  └──────────────────────────────┘  │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
          │                 │                     │
          │    Interface Contracts                │
          │                 │                     │
┌─────────┼─────────────────┼─────────────────────┼───────────────┐
│         ▼                 ▼                     ▼               │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │                  AutoTradeX.Core                         │   │
│  │                                                          │   │
│  │  ┌─────────────────┐  ┌──────────────────────────────┐  │   │
│  │  │ Interfaces      │  │ Models                       │  │   │
│  │  │ - IExchangeClient│  │ - AITradingModels            │  │   │
│  │  │ - IAITradingService│ │ - TradeResult               │  │   │
│  │  │ - IDatabaseService│ │ - AppConfig                  │  │   │
│  │  └─────────────────┘  └──────────────────────────────┘  │   │
│  │                                                          │   │
│  │  ┌─────────────────────────────────────────────────────┐│   │
│  │  │ Services                                            ││   │
│  │  │ - AITradingService (Signal Generation)              ││   │
│  │  │ - ArbEngine (Arbitrage Detection)                   ││   │
│  │  └─────────────────────────────────────────────────────┘│   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

---

## Dependency Injection Setup

### App.xaml.cs - Service Registration

```csharp
// File: src/AutoTradeX.UI/App.xaml.cs

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Build service collection
        var services = new ServiceCollection();

        // Register Core Services
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<IAITradingService, AITradingService>();

        // Register Infrastructure Services
        services.AddSingleton<IExchangeClientFactory, ExchangeClientFactory>();
        services.AddSingleton<DemoWalletService>();
        services.AddSingleton<IConnectionStatusService, ConnectionStatusService>();
        services.AddSingleton<IApiCredentialsService, ApiCredentialsService>();

        // Build provider
        Services = services.BuildServiceProvider();
    }
}
```

---

## How to Get Services in UI

### Method 1: In Constructor (may be null)

```csharp
public partial class AITradingPage : UserControl
{
    private IAITradingService? _aiTradingService;
    private DemoWalletService? _demoWallet;

    public AITradingPage()
    {
        InitializeComponent();

        // WARNING: App.Services may be null here if constructor runs
        // before App.OnStartup completes
        _aiTradingService = App.Services?.GetService<IAITradingService>();
        _demoWallet = App.Services?.GetService<DemoWalletService>();
    }
}
```

### Method 2: In Loaded Event (recommended)

```csharp
public AITradingPage()
{
    InitializeComponent();
    Loaded += AITradingPage_Loaded;
}

private void AITradingPage_Loaded(object sender, RoutedEventArgs e)
{
    // Services are guaranteed to be ready here
    _aiTradingService ??= App.Services?.GetService<IAITradingService>();
    _demoWallet ??= App.Services?.GetService<DemoWalletService>();
    _exchangeFactory ??= App.Services?.GetService<IExchangeClientFactory>();
    _logger ??= App.Services?.GetService<ILoggingService>();
}
```

---

## API Usage Examples

### 1. AI Trading Service

```csharp
// Get service
var aiService = App.Services?.GetService<IAITradingService>();

// Get market data
var marketData = await aiService.GetMarketDataAsync("Binance", "BTC/USDT");

// Get AI signal
var config = new AIStrategyConfig
{
    Exchange = "Binance",
    Symbol = "BTC/USDT",
    Mode = AITradingMode.Scalping,
    TradeAmountUSDT = 100m,
    TakeProfitPercent = 0.5m,
    StopLossPercent = 0.3m
};
var signal = await aiService.GetCurrentSignalAsync("Binance", "BTC/USDT", config);

// Execute manual trade (REAL MODE)
await aiService.ExecuteManualTradeAsync(signal, amount);

// Close position (REAL MODE)
await aiService.ClosePositionAsync("Manual");

// Start AI auto-trading
await aiService.StartAsync("Binance", "BTC/USDT", config);

// Stop AI auto-trading
await aiService.StopAsync();

// Get current position
var position = aiService.GetCurrentPosition();

// Get session stats
var stats = aiService.GetSessionStats();
```

### 2. Demo Wallet Service

```csharp
// Get service
var demoWallet = App.Services?.GetService<DemoWalletService>();

// Get wallet info
var wallet = await demoWallet.GetWalletAsync();
var balance = await demoWallet.GetBalanceAsync("USDT");

// Execute demo buy
var buyResult = await demoWallet.ExecuteAIBuyAsync(
    pair: "BTC/USDT",
    exchange: "Binance",
    quantity: 0.001m,
    price: 95000m,
    feePercent: 0.1m
);

if (buyResult.Success)
{
    // Trade executed, wallet balance updated
    Console.WriteLine($"Bought! New balance: {buyResult.NewBalance}");
}

// Execute demo sell
var sellResult = await demoWallet.ExecuteAISellAsync(
    pair: "BTC/USDT",
    exchange: "Binance",
    quantity: 0.001m,
    entryPrice: 95000m,   // Original buy price
    exitPrice: 95500m,     // Current sell price
    feePercent: 0.1m
);

if (sellResult.Success)
{
    Console.WriteLine($"PnL: {sellResult.Profit} ({sellResult.ProfitPercent}%)");
}

// Reset wallet
await demoWallet.ResetWalletAsync(10000m);

// Get recent trades
var trades = await demoWallet.GetRecentTradesAsync(50);

// Subscribe to wallet changes
demoWallet.WalletChanged += (sender, wallet) =>
{
    Console.WriteLine($"Wallet updated: ${wallet.TotalValueUSD}");
};
```

### 3. Exchange Client Factory

```csharp
// Get factory
var factory = App.Services?.GetService<IExchangeClientFactory>();

// Create real client (uses API keys)
var binanceClient = factory.CreateRealClient("Binance");

// Test connection
var isConnected = await binanceClient.TestConnectionAsync();

// Get ticker
var ticker = await binanceClient.GetTickerAsync("BTC/USDT");
Console.WriteLine($"Price: {ticker.LastPrice}");

// Get all tickers
var tickers = await binanceClient.GetAllTickersAsync("USDT");

// Get order book
var orderBook = await binanceClient.GetOrderBookAsync("BTC/USDT");

// Place order (REAL TRADING)
var order = await binanceClient.PlaceOrderAsync(
    symbol: "BTC/USDT",
    side: OrderSide.Buy,
    type: OrderType.Market,
    quantity: 0.001m
);
```

### 4. Database Service

```csharp
// Get service
var db = App.Services?.GetService<IDatabaseService>();

// Execute query
await db.ExecuteAsync(
    "INSERT INTO Settings (Key, Value) VALUES (@Key, @Value)",
    new { Key = "theme", Value = "dark" }
);

// Query single
var setting = await db.QueryFirstOrDefaultAsync<SettingRecord>(
    "SELECT * FROM Settings WHERE Key = @Key",
    new { Key = "theme" }
);

// Query multiple
var allSettings = await db.QueryAsync<SettingRecord>(
    "SELECT * FROM Settings"
);
```

### 5. Logging Service

```csharp
// Get service
var logger = App.Services?.GetService<ILoggingService>();

// Log messages
logger.LogInfo("MyComponent", "Operation completed successfully");
logger.LogWarning("MyComponent", "Something might be wrong");
logger.LogError("MyComponent", $"Error occurred: {ex.Message}");
```

---

## Complete Flow Example: Demo Trading

```csharp
public partial class AITradingPage : UserControl
{
    private IAITradingService? _aiTradingService;
    private DemoWalletService? _demoWallet;
    private ILoggingService? _logger;

    private bool _isDemoMode = true;
    private DemoAIPosition? _demoPosition;
    private AITradingSignal? _currentSignal;

    // Step 1: Get services on load
    private void AITradingPage_Loaded(object sender, RoutedEventArgs e)
    {
        _aiTradingService = App.Services?.GetService<IAITradingService>();
        _demoWallet = App.Services?.GetService<DemoWalletService>();
        _logger = App.Services?.GetService<ILoggingService>();

        // Start market data updates
        StartUpdateTimer();
    }

    // Step 2: Update market data every 1 second
    private async Task UpdateMarketDataAsync()
    {
        // Get market data from AI service
        var marketData = await _aiTradingService.GetMarketDataAsync(
            _selectedExchange, _selectedSymbol);

        // Update UI
        CurrentPrice.Text = $"${marketData.CurrentPrice:N2}";

        // Get AI signal
        var signal = await _aiTradingService.GetCurrentSignalAsync(
            _selectedExchange, _selectedSymbol, _config);
        _currentSignal = signal;

        // Update demo position price
        if (_isDemoMode && _demoPosition != null)
        {
            _demoPosition.CurrentPrice = marketData.CurrentPrice;
            await CheckDemoTPSLAsync(marketData.CurrentPrice);
        }
    }

    // Step 3: Handle buy click
    private async void ManualBuy_Click(object sender, RoutedEventArgs e)
    {
        decimal amount = 100m; // USDT amount

        if (_isDemoMode)
        {
            // Demo mode - use DemoWalletService
            await ExecuteDemoBuyAsync(amount);
        }
        else
        {
            // Real mode - use AITradingService
            await _aiTradingService.ExecuteManualTradeAsync(_currentSignal, amount);
        }
    }

    // Step 4: Execute demo buy
    private async Task ExecuteDemoBuyAsync(decimal amountUSDT)
    {
        var price = _currentSignal?.RecommendedEntryPrice ??
                    (await _aiTradingService.GetMarketDataAsync(
                        _selectedExchange, _selectedSymbol))?.CurrentPrice ?? 0;

        var quantity = amountUSDT / price;

        // Call DemoWalletService API
        var result = await _demoWallet.ExecuteAIBuyAsync(
            _selectedSymbol,    // "BTC/USDT"
            _selectedExchange,  // "Binance"
            quantity,           // 0.00105
            price               // 95000
        );

        if (result.Success)
        {
            // Create position tracking object
            _demoPosition = new DemoAIPosition
            {
                Symbol = _selectedSymbol,
                Exchange = _selectedExchange,
                EntryPrice = price,
                CurrentPrice = price,
                Quantity = quantity,
                TakeProfitPrice = price * 1.01m,
                StopLossPrice = price * 0.995m
            };

            _logger.LogInfo("AITradingPage",
                $"[DEMO] Buy: {quantity:N6} @ ${price:N2}");
        }
    }

    // Step 5: Auto check TP/SL
    private async Task CheckDemoTPSLAsync(decimal currentPrice)
    {
        if (_demoPosition == null) return;

        // Take Profit hit
        if (currentPrice >= _demoPosition.TakeProfitPrice)
        {
            var result = await _demoWallet.ExecuteAISellAsync(
                _demoPosition.Symbol,
                _demoPosition.Exchange,
                _demoPosition.Quantity,
                _demoPosition.EntryPrice,
                currentPrice
            );

            if (result.Success)
            {
                _logger.LogInfo("AITradingPage",
                    $"[DEMO] TP Hit! PnL: +${result.Profit:N2}");
                _demoPosition = null;
            }
        }

        // Stop Loss hit
        else if (currentPrice <= _demoPosition.StopLossPrice)
        {
            var result = await _demoWallet.ExecuteAISellAsync(
                _demoPosition.Symbol,
                _demoPosition.Exchange,
                _demoPosition.Quantity,
                _demoPosition.EntryPrice,
                currentPrice
            );

            if (result.Success)
            {
                _logger.LogInfo("AITradingPage",
                    $"[DEMO] SL Hit! PnL: ${result.Profit:N2}");
                _demoPosition = null;
            }
        }
    }
}
```

---

## Service Interfaces Reference

### IAITradingService

```csharp
public interface IAITradingService
{
    // Events
    event EventHandler<AITradingSignal>? SignalGenerated;
    event EventHandler<AITradingPosition>? PositionOpened;
    event EventHandler<AITradingPosition>? PositionClosed;
    event EventHandler<AITradeResult>? TradeCompleted;
    event EventHandler<AIMarketData>? MarketDataUpdated;
    event EventHandler<string>? EmergencyTriggered;

    // Methods
    Task<AIMarketData?> GetMarketDataAsync(string exchange, string symbol,
        CancellationToken ct = default);
    Task<AITradingSignal?> GetCurrentSignalAsync(string exchange, string symbol,
        AIStrategyConfig config);
    Task StartAsync(string exchange, string symbol, AIStrategyConfig config);
    Task StopAsync();
    Task ExecuteManualTradeAsync(AITradingSignal signal, decimal amount);
    Task ClosePositionAsync(string reason);
    AITradingPosition? GetCurrentPosition();
    AITradingSessionStats GetSessionStats();
}
```

### IExchangeClient

```csharp
public interface IExchangeClient
{
    string ExchangeName { get; }

    Task<bool> TestConnectionAsync(CancellationToken ct = default);
    Task<Ticker> GetTickerAsync(string symbol, CancellationToken ct = default);
    Task<Dictionary<string, Ticker>> GetAllTickersAsync(string? quoteAsset = null,
        CancellationToken ct = default);
    Task<OrderBook> GetOrderBookAsync(string symbol, int limit = 20,
        CancellationToken ct = default);
    Task<Balance> GetBalanceAsync(string currency, CancellationToken ct = default);
    Task<Order> PlaceOrderAsync(string symbol, OrderSide side, OrderType type,
        decimal quantity, decimal? price = null, CancellationToken ct = default);
    Task<bool> CancelOrderAsync(string symbol, string orderId,
        CancellationToken ct = default);
}
```

### IDatabaseService

```csharp
public interface IDatabaseService
{
    Task<int> ExecuteAsync(string sql, object? param = null);
    Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null);
    Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null);
}
```

---

## Data Flow Diagrams

### Buy Flow (Demo Mode)

```
User clicks BUY
       │
       ▼
┌──────────────────┐
│ AITradingPage    │
│ ManualBuy_Click()│
└────────┬─────────┘
         │ if (_isDemoMode)
         ▼
┌──────────────────┐
│ ExecuteDemoBuy   │
│ Async()          │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐     ┌─────────────────┐
│ DemoWalletService│────▶│ DatabaseService │
│ ExecuteAIBuyAsync│     │ (SQLite)        │
└────────┬─────────┘     └─────────────────┘
         │
         │ Returns DemoTradeResult
         ▼
┌──────────────────┐
│ Create           │
│ DemoAIPosition   │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ Update UI        │
│ - Wallet balance │
│ - Position info  │
│ - Chart markers  │
└──────────────────┘
```

### Market Data Flow

```
Every 1 second (Timer)
       │
       ▼
┌──────────────────┐
│ UpdateMarket     │
│ DataAsync()      │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐     ┌─────────────────┐
│ IAITradingService│────▶│ IExchangeClient │
│ GetMarketData    │     │ GetTicker()     │
│ Async()          │     └─────────────────┘
└────────┬─────────┘
         │
         │ AIMarketData (price, indicators)
         ▼
┌──────────────────┐
│ Update UI        │
│ - Price display  │
│ - Indicators     │
│ - Demo position  │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ CheckDemoTPSL    │──── If TP/SL hit ────▶ Auto Sell
│ Async()          │
└──────────────────┘
```

---

## Error Handling Patterns

```csharp
// Always use try-catch with timeout
private async Task UpdateMarketDataAsync()
{
    if (_aiTradingService == null) return;

    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var marketData = await _aiTradingService.GetMarketDataAsync(
            _selectedExchange, _selectedSymbol, cts.Token);

        if (marketData != null)
        {
            // Update UI on dispatcher thread
            await Dispatcher.InvokeAsync(() =>
            {
                CurrentPrice.Text = $"${marketData.CurrentPrice:N2}";
            });
        }
    }
    catch (OperationCanceledException)
    {
        _logger?.LogWarning("Page", "Request timed out");
    }
    catch (Exception ex)
    {
        _logger?.LogError("Page", $"Error: {ex.Message}");
    }
}
```

---

## Quick Reference

| Task | Service | Method |
|------|---------|--------|
| Get price | `IAITradingService` | `GetMarketDataAsync()` |
| Get AI signal | `IAITradingService` | `GetCurrentSignalAsync()` |
| Demo buy | `DemoWalletService` | `ExecuteAIBuyAsync()` |
| Demo sell | `DemoWalletService` | `ExecuteAISellAsync()` |
| Real buy/sell | `IAITradingService` | `ExecuteManualTradeAsync()` |
| Test connection | `IExchangeClient` | `TestConnectionAsync()` |
| Get balance | `DemoWalletService` | `GetBalanceAsync()` |
| Log message | `ILoggingService` | `LogInfo/Warning/Error()` |
| Save to DB | `IDatabaseService` | `ExecuteAsync()` |
