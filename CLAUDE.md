# CLAUDE.md - AutoTrade-X Development Guide

This file provides guidance for Claude Code when working on the AutoTrade-X project.

## Project Overview

**AutoTrade-X** is a cross-exchange cryptocurrency arbitrage trading bot dashboard built with WPF (.NET 8). It monitors price differences across multiple exchanges and can execute arbitrage trades automatically.

**Version:** 0.1.0-beta
**Developer:** Xman Studio
**License:** Educational Use Only

## Tech Stack

- **Framework:** .NET 8.0 Windows (WPF)
- **Language:** C# 12
- **UI:** WPF with custom glass/gradient styling
- **Charts:** LiveChartsCore.SkiaSharpView.WPF
- **Architecture:** Clean Architecture (Core, Infrastructure, UI)
- **Database:** SQLite (via custom DatabaseService)

## Project Structure

```
AutoTrade-X/
├── src/
│   ├── AutoTradeX.Core/           # Domain models, interfaces, business logic
│   │   ├── Models/                # Trade, Exchange, Opportunity models
│   │   ├── Interfaces/            # Service contracts (IExchangeClient, etc.)
│   │   └── Services/              # Core business services
│   │
│   ├── AutoTradeX.Infrastructure/ # External implementations
│   │   ├── ExchangeClients/       # Binance, KuCoin, OKX, Bybit, Gate.io, Bitkub
│   │   ├── Data/                  # DatabaseService, repositories
│   │   └── Services/              # Infrastructure services
│   │
│   └── AutoTradeX.UI/             # WPF Desktop Application
│       ├── Views/                 # MainWindow, DashboardPage, TradingPage, etc.
│       ├── Controls/              # Custom controls (ExchangeIcon, etc.)
│       ├── ViewModels/            # MVVM ViewModels
│       ├── Converters/            # Value converters
│       └── Assets/                # Images, logos, icons
│
├── tests/                         # Unit and integration tests
├── docs/                          # Documentation
└── scripts/                       # Build and deployment scripts
```

## Build Commands

```bash
# Restore dependencies
dotnet restore

# Build all projects
dotnet build

# Build release
dotnet build --configuration Release

# Run the application
dotnet run --project src/AutoTradeX.UI/AutoTradeX.UI.csproj

# Run tests
dotnet test
```

## Supported Exchanges

| Exchange | Client Class | Status |
|----------|-------------|--------|
| Binance | BinanceClient | ✅ Implemented |
| KuCoin | KuCoinClient | ✅ Implemented |
| OKX | OKXClient | ✅ Implemented |
| Bybit | BybitClient | ✅ Implemented |
| Gate.io | GateIOClient | ✅ Implemented |
| Bitkub | BitkubClient | ✅ Implemented |

## UI Design Guidelines

- **Theme:** Dark mode with purple/blue gradient accents
- **Colors:**
  - Primary: `#7C3AED` (Purple)
  - Secondary: `#2563EB` (Blue)
  - Success: `#10B981` (Green)
  - Danger: `#EF4444` (Red)
  - Background: `#0A0A1A` to `#1A0A2E`
- **Style:** Glass morphism with blur effects
- **Corner Radius:** 12-16px for cards
- **Animations:** Smooth transitions, hyperdrive effect on logo

## Key Files

### UI Entry Points
- `src/AutoTradeX.UI/App.xaml` - Application startup
- `src/AutoTradeX.UI/Views/MainWindow.xaml` - Main window with navigation
- `src/AutoTradeX.UI/Views/DashboardPage.xaml` - Dashboard view

### Core Services
- `src/AutoTradeX.Core/Services/ArbitrageService.cs` - Arbitrage detection
- `src/AutoTradeX.Core/Services/TradingService.cs` - Trade execution

### Infrastructure
- `src/AutoTradeX.Infrastructure/Data/DatabaseService.cs` - SQLite operations
- `src/AutoTradeX.Infrastructure/ExchangeClients/` - Exchange API implementations

## Development Notes

### Adding a New Exchange

1. Create client in `Infrastructure/ExchangeClients/NewExchangeClient.cs`
2. Implement `IExchangeClient` interface
3. Add logo to `UI/Assets/Exchanges/newexchange.png`
4. Register in `ExchangeIcon.xaml.cs` dictionaries
5. Add as embedded resource in `UI/AutoTradeX.UI.csproj`

### Modifying UI Styles

- Global styles are in `MainWindow.xaml` Resources section
- Use `GlassCard` style for card containers
- Use `PremiumButton` style for primary buttons
- Keep consistent with existing gradient and glow effects

### Premium ScrollBar (IMPORTANT - Always Use)

**ALWAYS use premium scrollbar styles for any new ScrollViewer or ListView!**

Available styles defined in each page's Resources:
- `PremiumScrollBarThumb` - Thumb style with gradient and glow effects
- `PremiumScrollBar` - ScrollBar with purple gradient thumb
- `PremiumScrollViewer` / `ModernScrollViewerStyle` - ScrollViewer with premium scrollbar
- `PremiumListView` - ListView with premium scrollbar built-in

**Usage Examples:**
```xml
<!-- For ScrollViewer -->
<ScrollViewer Style="{StaticResource PremiumScrollViewer}">
    <!-- content -->
</ScrollViewer>

<!-- In MainWindow.xaml use -->
<ScrollViewer Style="{StaticResource ModernScrollViewerStyle}">
    <!-- content -->
</ScrollViewer>

<!-- For ListView -->
<ListView Style="{StaticResource PremiumListView}">
    <!-- items -->
</ListView>
```

**ScrollBar Design:**
- Gradient colors: `#8B5CF6` → `#7C3AED` → `#6D28D9`
- Hover: Brighter purple with glow effect
- Dragging: Brightest purple (#C4B5FD) with maximum glow
- Track: Semi-transparent (#15FFFFFF)
- Width: 10px with rounded corners

**DO NOT use default WPF scrollbars** - they look out of place with the dark theme.

### Database Operations

- Use `DatabaseService` for all DB operations
- Tables: `Trades`, `Settings`, `ExchangeConfigs`
- Connection string in `appsettings.json`

### Internet Connection Requirement

The application **requires an active internet connection** to start. This is enforced in `App.xaml.cs`:

- **On startup**, the app checks internet connectivity before initializing any services
- If no internet is detected, a styled error dialog is shown and the app shuts down
- This is required because the app needs to validate licenses with the server

**Key methods in `App.xaml.cs`:**
- `CheckInternetConnectionAsync()` - Tests connectivity to multiple endpoints (Google, Cloudflare, Microsoft)
- `ShowNoInternetDialog()` - Displays Thai-language error dialog with app theming

**Flow:**
```
App Start → Check Internet → No Internet? → Show Dialog → Shutdown
                          → Has Internet? → Continue to License Check → Main App
```

### License System (CRITICAL - Must Read)

> **⚠️ IMPORTANT:** When working on ANY license-related code, you MUST read the complete documentation at:
> **`D:\Code\APP Thaiprompt\xmanstudio\docs\LICENSE_SYSTEM.md`**

This application uses the Xman Studio License System with the following security features:

**Key Components:**
- `src/AutoTradeX.Infrastructure/Services/LicenseService.cs` - Main license service
- `src/AutoTradeX.Core/Models/LicenseModels.cs` - License data models

**Security Features (DO NOT modify without reading docs):**
1. **Device Registration** - Auto-registers device on app startup
2. **Trial Abuse Detection** - Hardware hash, IP tracking, attempt counting
3. **Fake Server Protection** - DNS verification, hosts file check, challenge-response
4. **License Lock** - License is locked to specific device

**API Endpoints (Server: xman4289.com):**
- `POST /api/v1/autotradex/register-device` - Register device
- `POST /api/v1/autotradex/activate` - Activate license
- `POST /api/v1/autotradex/validate` - Validate license
- `POST /api/v1/autotradex/demo` - Start trial
- `POST /api/v1/autotradex/verify-server` - Anti-fake server verification

**Before making ANY changes to license code:**
1. Read `xmanstudio/docs/LICENSE_SYSTEM.md` completely
2. Understand the abuse detection flow
3. Understand the fake server protection
4. Test changes against the actual server

## Warning

This is an **educational project** for learning about:
- Cryptocurrency exchange APIs
- Arbitrage trading concepts
- WPF application development
- Clean architecture patterns

**DO NOT use this for real trading without proper testing and understanding of the risks involved.**

## AI Trading System (IMPORTANT)

> **Full Documentation:** `docs/AI_TRADING_SYSTEM.md`
> **API Integration Guide:** `docs/API_INTEGRATION_GUIDE.md`

### Overview

ระบบ AI Trading มี 6 กลยุทธ์ที่แตกต่างกัน โดยแต่ละกลยุทธ์ใช้ตัวชี้วัดทางเทคนิคที่เหมาะสมกับสภาวะตลาดที่แตกต่างกัน

### Key Files

| File | Purpose |
|------|---------|
| `Core/Services/AITradingService.cs` | Signal generation, strategy logic |
| `Core/Models/AITradingModels.cs` | Data models, strategy info |
| `Infrastructure/Services/DemoWalletService.cs` | Demo trading wallet |
| `UI/Views/AITradingPage.xaml.cs` | UI logic, demo/real mode |

### AI Trading Strategies

```
┌─────────────────┬───────────────┬──────────────────────────────────┐
│ Strategy        │ Hold Time     │ Best For                         │
├─────────────────┼───────────────┼──────────────────────────────────┤
│ Scalping        │ 5-15 min      │ High volume, tight spread        │
│ Momentum        │ 1-4 hours     │ Clear trend, medium volatility   │
│ Mean Reversion  │ 30min-2hours  │ Sideways, range-bound market     │
│ Grid Trading    │ Variable      │ Sideways with clear range        │
│ Breakout        │ 30min-4hours  │ After consolidation, news        │
│ Smart DCA       │ Long-term     │ Long-term investment             │
└─────────────────┴───────────────┴──────────────────────────────────┘
```

### Signal Generation Flow

Each strategy has its OWN signal generation method:
- `GenerateScalpingSignal()` - RSI extremes + tight spreads
- `GenerateMomentumSignal()` - EMA crossover + MACD + SMA50
- `GenerateMeanReversionSignal()` - Bollinger Bands + RSI
- `GenerateGridTradingSignal()` - ATR grid sizing + support/resistance
- `GenerateBreakoutSignal()` - BB breakout + volume confirmation
- `GenerateSmartDCASignal()` - RSI + SMA200 for DCA timing

### Demo vs Real Mode

```csharp
// In AITradingPage.xaml.cs
private bool _isDemoMode = true;  // Default: Demo Mode ON

// When user clicks BUY:
if (_isDemoMode)
    await ExecuteDemoBuyAsync(amount);  // Uses DemoWalletService
else
    await _aiTradingService.ExecuteManualTradeAsync(...);  // Real API
```

### Demo Wallet API

```csharp
// Buy (open position)
DemoWalletService.ExecuteAIBuyAsync(pair, exchange, quantity, price)

// Sell (close position)
DemoWalletService.ExecuteAISellAsync(pair, exchange, quantity, entryPrice, exitPrice)
```

### Auto TP/SL

- Demo positions have automatic TP/SL checking every 1 second
- When price hits TP or SL, position closes automatically
- Uses `CheckDemoTPSLAsync()` in market data update loop

### Important Variables in AITradingPage

```csharp
private bool _isDemoMode = true;           // Demo mode flag
private DemoAIPosition? _demoPosition;     // Current demo position
private AITradingPosition? _currentPosition; // Real position
private DemoWalletService? _demoWallet;    // Demo wallet service
```

### Strategy Auto-Recommendation

```csharp
// In AITradingModels.cs
AITradingMode.RecommendStrategy(volatilityPercent, isTrending, volume24h)
```

| Condition | Recommended |
|-----------|-------------|
| High volatility + Trending | Breakout/Momentum |
| Low volatility + Sideways | Grid/Mean Reversion |
| Medium volatility | Scalping |
| Uncertain | Smart DCA |

## Common Tasks for Claude

1. **UI Improvements:** Focus on `Views/` folder, maintain glass morphism style
2. **Exchange Integration:** Work in `Infrastructure/ExchangeClients/`
3. **Business Logic:** Modify `Core/Services/`
4. **Bug Fixes:** Check both XAML and code-behind files
5. **Performance:** Look for async/await patterns, consider caching
6. **AI Trading:** Always check `_isDemoMode` for new trading features

## Testing Guidelines

- Unit test business logic in Core
- Integration test exchange clients with mock responses
- UI tests are optional but appreciated
- **AI Trading:** Test both Demo and Real mode paths

---

## ⚠️ Production Audit Notes (CRITICAL — Must Read Before Modifying)

> **Last audited:** February 2025 (3 rounds, ~42 fixes across ~20 files)
> **Build status:** 0 errors, 47 tests passed

### Patterns Already Established — DO NOT Break

The following patterns were intentionally implemented to fix critical bugs.
**If you modify these files, you MUST preserve these patterns:**

#### 1. DemoWalletService — Event Firing Pattern
```csharp
// ✅ CORRECT — Always use this pattern
private void RaiseWalletChanged(DemoWallet wallet)
{
    try { WalletChanged?.Invoke(this, wallet); }
    catch (Exception ex) { _logger.LogError(...); }
}

// ❌ NEVER do this — causes infinite recursion → StackOverflow
private void RaiseWalletChanged(DemoWallet wallet)
{
    RaiseWalletChanged(wallet);  // WRONG: calls itself!
}
```
- All wallet event firing goes through `RaiseWalletChanged()` wrapper
- Input validation exists on all trade methods (quantity > 0, price > 0)
- `ParsePair()` helper handles safe pair splitting with validation

#### 2. AITradingService — Division by Zero Guards
```csharp
// ✅ Every division MUST have a > 0 guard:
var result = denominator > 0 ? numerator / denominator : fallbackValue;
```
**Protected locations (DO NOT remove guards):**
- Drawdown calculation (`PeakPnL` can be 0)
- `AverageTradeDurationMinutes` (TotalTrades can be 0)
- Bollinger Bands `bbWidth` and `pricePosition` (`bbRange` and `bbMiddle` can be 0)
- ATR calculation (period can produce 0)
- RSI calculation (losses sum can be 0)

#### 3. AITradingService — Position Race Condition Fix
```csharp
// ✅ CORRECT — Always snapshot to local variables first
var position = _currentPosition;
var config = _config;
if (position == null || config == null) return;
// Use 'position' and 'config' locally, never re-read _currentPosition

// ❌ NEVER read _currentPosition multiple times in the same method
```

#### 4. AITradingService — Fee Calculation
```csharp
// ✅ Uses actual order fee when available, falls back to config FeePercent
// FeePercent is in AIStrategyConfig (e.g., 0.1 = 0.1%)
// DO NOT hardcode 0.001m — different exchanges have different fees
```

#### 5. AITradingService — Error Loop Backoff
```csharp
// ✅ Trading loop uses exponential backoff on errors
// consecutiveErrors → 5s, 10s, 20s, 40s, 60s (max)
// Resets to 0 on success
// DO NOT remove — prevents CPU spin on persistent errors
```

#### 6. BaseExchangeClient — Rate Limiter Semaphore
```csharp
// ✅ CORRECT — Release MUST always happen via try/finally with CancellationToken.None
_ = Task.Run(async () =>
{
    try { await Task.Delay(1000, CancellationToken.None); }
    finally { _rateLimiter.Release(); }
}, CancellationToken.None);

// ❌ NEVER pass caller's CancellationToken to the delay
// Cancellation during delay = semaphore never released = API permanently blocked
```

#### 7. CurrencyConverterService — HttpClient Reuse
```csharp
// ✅ Two HttpClient fields — both created ONCE in constructor:
private readonly HttpClient _httpClient;          // For Bitkub primary API
private readonly HttpClient _fallbackHttpClient;  // For all 4 fallback APIs

// ❌ NEVER create new HttpClient() inside methods
// Each HttpClient holds a socket; creating per-call = socket exhaustion
```
- `IsRateValid` — true only after at least one successful fetch
- `IsRateStale` — true if rate is older than 1 hour
- All API responses validated with `> 0` before accepting

#### 8. ConnectionStatusService — Case-Insensitive Keys
```csharp
// ✅ Dictionary uses OrdinalIgnoreCase — "Binance" == "binance" == "BINANCE"
private readonly Dictionary<string, ExchangeConnectionStatus> _verifiedExchanges
    = new(StringComparer.OrdinalIgnoreCase);

// ❌ NEVER use .ToLower() on exchange names before dictionary lookup
// ❌ NEVER create new Dictionary<string,...>() without StringComparer
```

#### 9. MainWindow — Event Cleanup on Close
```csharp
// ✅ MainWindow_Closing handler unsubscribes ALL 9+ events:
// _arbEngine: StatusChanged, TradeCompleted, OpportunityFound, PriceUpdated, ErrorOccurred
// _balancePool: BalanceUpdated, EmergencyTriggered
// _notificationService: NotificationReceived
// _licenseService: DemoModeReminder
// _connectionStatusService: ConnectionStatusChanged
// Also stops: _priceUpdateTimer, _aiScannerTimer, _statusBarTimer
// Also cancels: _botCancellationTokenSource

// ❌ If you add a NEW event subscription in constructor or Loaded,
//    you MUST add the corresponding -= in MainWindow_Closing
```

#### 10. App.xaml.cs — Database Init Order
```csharp
// ✅ CORRECT order in OnStartup:
Services = services.BuildServiceProvider();
await db.InitializeAsync();              // ← DB first!
await LoadCredentialsFromDatabaseAsync(); // ← Then credentials

// ❌ NEVER use fire-and-forget Task.Run for DB init
// ❌ NEVER access DB before InitializeAsync() completes
```

#### 11. DatabaseService — Transaction-Wrapped Schema
```csharp
// ✅ CreateTablesAsync wraps all DDL in BeginTransaction/Commit/Rollback
// This ensures atomic schema creation — all tables or none
// ❌ If adding new tables, add them INSIDE the existing transaction block
```

#### 12. BalancePoolService — Zero-Price Asset Guard
```csharp
// ✅ Exchange breakdown P&L skips assets where GetAssetPrice() returns 0
// Logs warning for skipped assets
// ❌ NEVER multiply balance * price without checking price > 0 first
// A zero price silently zeroes out P&L for that asset
```

#### 13. AppConfig — Validation
```csharp
// ✅ AppConfig.Validate() checks: exchange fees/timeouts, strategy params,
//    risk limits, trading pair formats
// AppConfig.IsValid — quick check
// Call Validate() before using config in production paths
```

### Known Safe — No Fix Needed

These were audited and confirmed correct:
- **AITradingPage** dynamic buttons — has `Unloaded` cleanup
- **TradingPage** events — has `CleanupEventHandlers()`
- **ScannerPage** buttons — has `Unloaded` cleanup
- **CoinIcon cache** — bounded by finite crypto count (~200 coins, <5MB)
- **TradingViewWidget** CoreWebView2 — has `_isInitialized` guard at every call site
- **NullToVisibilityConverter** `null!` in ConvertBack — ConvertBack is never called (OneWay binding only)

### Build Warnings (Acceptable)

These 4 warnings are known and acceptable:
- `CS8625` in `TradingViewChart.xaml.cs:200` — null to non-nullable (safe in context)
- `CS0414` in `DashboardPage.xaml.cs:78` — `_currentTradingMode` assigned but unused (reserved for future use)

### Quick Checklist for New Code

Before submitting any change, verify:

- [ ] No `new HttpClient()` inside methods — reuse class-level instances
- [ ] Every division has a `> 0` guard on denominator
- [ ] Every event `+=` has a matching `-=` in cleanup/dispose
- [ ] Exchange name comparisons are case-insensitive
- [ ] `SemaphoreSlim.Release()` is in `finally` block
- [ ] No fire-and-forget `Task.Run` for critical initialization
- [ ] New DB tables go inside existing transaction in `CreateTablesAsync`
- [ ] Demo mode (`_isDemoMode`) checked for all trading features
- [ ] ScrollViewer/ListView uses premium scrollbar styles
- [ ] `dotnet build` = 0 errors, `dotnet test` = 47 passed
