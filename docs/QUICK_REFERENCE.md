# AutoTrade-X Quick API Reference

## Get Services

```csharp
// In Page Loaded event (recommended)
_aiTradingService = App.Services?.GetService<IAITradingService>();
_demoWallet = App.Services?.GetService<DemoWalletService>();
_exchangeFactory = App.Services?.GetService<IExchangeClientFactory>();
_logger = App.Services?.GetService<ILoggingService>();
_db = App.Services?.GetService<IDatabaseService>();
```

---

## AI Trading Service

```csharp
// Get market data
var data = await _aiTradingService.GetMarketDataAsync("Binance", "BTC/USDT");

// Get signal
var signal = await _aiTradingService.GetCurrentSignalAsync(exchange, symbol, config);

// Execute real trade
await _aiTradingService.ExecuteManualTradeAsync(signal, amount);

// Close position
await _aiTradingService.ClosePositionAsync("Manual");
```

---

## Demo Wallet Service

```csharp
// Check balance
var balance = await _demoWallet.GetBalanceAsync("USDT");

// Demo Buy
var result = await _demoWallet.ExecuteAIBuyAsync(
    pair: "BTC/USDT",
    exchange: "Binance",
    quantity: 0.001m,
    price: 95000m
);

// Demo Sell
var result = await _demoWallet.ExecuteAISellAsync(
    pair: "BTC/USDT",
    exchange: "Binance",
    quantity: 0.001m,
    entryPrice: 95000m,
    exitPrice: 95500m
);

// Check result
if (result.Success) {
    Console.WriteLine($"PnL: {result.Profit} ({result.ProfitPercent}%)");
    Console.WriteLine($"New Balance: {result.NewBalance}");
}

// Reset wallet
await _demoWallet.ResetWalletAsync(10000m);
```

---

## Exchange Client

```csharp
// Get client
var client = _exchangeFactory.CreateRealClient("Binance");

// Test connection
var ok = await client.TestConnectionAsync();

// Get price
var ticker = await client.GetTickerAsync("BTC/USDT");

// Get all prices
var tickers = await client.GetAllTickersAsync("USDT");
```

---

## Demo/Real Mode Pattern

```csharp
private bool _isDemoMode = true;
private DemoAIPosition? _demoPosition;

private async void Buy_Click(...)
{
    if (_isDemoMode)
    {
        // Demo: use DemoWalletService
        var result = await _demoWallet.ExecuteAIBuyAsync(...);
        if (result.Success) {
            _demoPosition = new DemoAIPosition { ... };
        }
    }
    else
    {
        // Real: use AITradingService
        await _aiTradingService.ExecuteManualTradeAsync(...);
    }
}
```

---

## Data Models

### DemoTradeResult
```csharp
bool Success           // Did trade execute?
DemoTrade? Trade       // Trade record (on sell)
decimal Profit         // PnL amount
decimal ProfitPercent  // PnL %
decimal NewBalance     // New USDT balance
string Message         // Thai/English message
```

### AITradingSignal
```csharp
string SignalType      // "Buy", "Sell", "Hold"
decimal? RecommendedEntryPrice
decimal? TargetPrice   // Take Profit
decimal? StopLossPrice
decimal Confidence     // 0-100
string Reasoning       // AI explanation
```

### AIMarketData
```csharp
decimal CurrentPrice
decimal Volume24h
decimal High24h, Low24h
decimal? RSI, MACD, EMA9, EMA21
decimal? BollingerUpper/Middle/Lower
decimal? Volatility
```

---

## Database

```csharp
// Insert/Update
await _db.ExecuteAsync(
    "INSERT INTO Settings (Key, Value) VALUES (@Key, @Value)",
    new { Key = "theme", Value = "dark" }
);

// Query one
var item = await _db.QueryFirstOrDefaultAsync<T>(sql, param);

// Query many
var items = await _db.QueryAsync<T>(sql, param);
```

---

## Logging

```csharp
_logger.LogInfo("Component", "Message");
_logger.LogWarning("Component", "Warning message");
_logger.LogError("Component", $"Error: {ex.Message}");
```

---

## Strategy Modes

| Mode | Method | Best For |
|------|--------|----------|
| `Scalping` | `GenerateScalpingSignal()` | High volume, tight spread |
| `Momentum` | `GenerateMomentumSignal()` | Clear trend |
| `MeanReversion` | `GenerateMeanReversionSignal()` | Sideways market |
| `GridTrading` | `GenerateGridTradingSignal()` | Range-bound |
| `Breakout` | `GenerateBreakoutSignal()` | After consolidation |
| `SmartDCA` | `GenerateSmartDCASignal()` | Long-term |

---

## Files Location

| Purpose | File |
|---------|------|
| AI Logic | `Core/Services/AITradingService.cs` |
| Models | `Core/Models/AITradingModels.cs` |
| Demo Wallet | `Infrastructure/Services/DemoWalletService.cs` |
| UI Page | `UI/Views/AITradingPage.xaml.cs` |
| Service Setup | `UI/App.xaml.cs` |
