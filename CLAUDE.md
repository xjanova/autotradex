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

## Common Tasks for Claude

1. **UI Improvements:** Focus on `Views/` folder, maintain glass morphism style
2. **Exchange Integration:** Work in `Infrastructure/ExchangeClients/`
3. **Business Logic:** Modify `Core/Services/`
4. **Bug Fixes:** Check both XAML and code-behind files
5. **Performance:** Look for async/await patterns, consider caching

## Testing Guidelines

- Unit test business logic in Core
- Integration test exchange clients with mock responses
- UI tests are optional but appreciated
