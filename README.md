# AutoTrade-X

<p align="center">
  <img src="src/AutoTradeX.UI/Assets/logo2.png" alt="AutoTrade-X Logo" width="200"/>
</p>

<p align="center">
  <strong>Cross-Exchange Cryptocurrency Arbitrage Trading Bot Dashboard</strong>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/version-0.1.0--beta-blue" alt="Version"/>
  <img src="https://img.shields.io/badge/.NET-8.0-purple" alt=".NET 8"/>
  <img src="https://img.shields.io/badge/platform-Windows-lightgrey" alt="Platform"/>
  <img src="https://img.shields.io/badge/license-Educational-orange" alt="License"/>
</p>

<p align="center">
  Developed by <strong>Xman Studio</strong>
</p>

---

> ⚠️ **IMPORTANT DISCLAIMER / คำเตือนสำคัญ**
>
> This software is for **Educational/Experimental Purposes Only**
>
> - ❌ **No profit guarantees**
> - ❌ **Cryptocurrency trading involves high risk - you may lose all your capital**
> - ❌ **Users are solely responsible for all decisions and risks**
>
> Always test in **Simulation Mode** before using with real funds.

---

## Overview

**AutoTrade-X** is a sophisticated WPF desktop application designed for monitoring and executing cryptocurrency arbitrage trades across multiple exchanges. Built with modern .NET 8 and featuring a stunning glass morphism UI design.

### Key Features

- ✅ **Multi-Exchange Support:** Binance, KuCoin, OKX, Bybit, Gate.io, Bitkub
- ✅ **Real-time Monitoring:** Live price feeds and arbitrage opportunity detection
- ✅ **Beautiful UI:** Dark theme with glass morphism effects and smooth animations
- ✅ **Trade History:** Comprehensive logging and P&L tracking with charts
- ✅ **Splash Screen:** Animated hyperdrive effect on startup
- ✅ **Risk Management:** Configurable limits and safety parameters
- ✅ **Simulation Mode:** Test strategies without real money

### How It Works

```
┌─────────────────────────────────────────────────────────────────┐
│                    Cross-Exchange Arbitrage                      │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Exchange A                        Exchange B                    │
│  ┌──────────────┐                 ┌──────────────┐              │
│  │ BTC: 0.5     │                 │ BTC: 0.5     │              │
│  │ USDT: 10,000 │                 │ USDT: 10,000 │              │
│  └──────────────┘                 └──────────────┘              │
│         │                                │                       │
│         │   Price A < Price B            │                       │
│         │                                │                       │
│         ▼                                ▼                       │
│    BUY BTC                          SELL BTC                     │
│    (use USDT)                       (get USDT)                   │
│                                                                  │
│  Result: Profit from price difference (after fees)               │
└─────────────────────────────────────────────────────────────────┘
```

---

## Tech Stack

| Component | Technology |
|-----------|------------|
| Framework | .NET 8.0 Windows |
| UI | WPF with custom styling |
| Charts | LiveChartsCore + SkiaSharp |
| Architecture | Clean Architecture |
| Database | SQLite |

---

## Getting Started

### Prerequisites

- Windows 10/11
- .NET 8.0 SDK
- Visual Studio 2022 or VS Code

### Installation

```bash
# Clone the repository
git clone https://github.com/xjanova/autotradex.git

# Navigate to project
cd autotradex

# Restore dependencies
dotnet restore

# Build
dotnet build

# Run
dotnet run --project src/AutoTradeX.UI/AutoTradeX.UI.csproj
```

---

## Project Structure

```
AutoTrade-X/
├── src/
│   ├── AutoTradeX.Core/           # Domain layer - Models & Interfaces
│   ├── AutoTradeX.Infrastructure/ # Infrastructure - Exchange clients, DB
│   └── AutoTradeX.UI/             # Presentation - WPF Application
├── tests/                         # Test projects
├── docs/                          # Documentation
├── CLAUDE.md                      # Claude Code development guide
└── README.md                      # This file
```

---

## Supported Exchanges

| Exchange | Status | API Client |
|----------|--------|------------|
| Binance | ✅ Ready | BinanceClient |
| KuCoin | ✅ Ready | KuCoinClient |
| OKX | ✅ Ready | OKXClient |
| Bybit | ✅ Ready | BybitClient |
| Gate.io | ✅ Ready | GateIOClient |
| Bitkub | ✅ Ready | BitkubClient |

---

## Configuration

### appsettings.json

```json
{
  "strategy": {
    "minSpreadPercentage": 0.3,
    "minExpectedProfitQuoteCurrency": 0.5,
    "pollingIntervalMs": 1000
  },
  "risk": {
    "maxPositionSizePerTrade": 100,
    "maxDailyLoss": 50,
    "maxTradesPerDay": 100
  },
  "general": {
    "liveTrading": false
  }
}
```

### API Keys (Environment Variables)

⚠️ **Never store API keys in config files!**

```powershell
# PowerShell
$env:BINANCE_API_KEY = "your-api-key"
$env:BINANCE_API_SECRET = "your-api-secret"
```

---

## Development

See [CLAUDE.md](CLAUDE.md) for detailed development guidelines when using Claude Code.

### Build Commands

```bash
# Debug build
dotnet build

# Release build
dotnet build --configuration Release

# Run tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

---

## Risk Management

| Parameter | Description | Recommended |
|-----------|-------------|-------------|
| MaxPositionSizePerTrade | Max trade size | 1-5% of capital |
| MaxDailyLoss | Max daily loss | 1-2% of capital |
| MaxTradesPerDay | Max trades per day | 50-100 |
| MinTimeBetweenTradesMs | Time between trades | 5000ms+ |

---

## Screenshots

*Coming soon*

---

## License

This project is licensed for **Educational Use Only**.

Copyright © 2024 Xman Studio

---

## Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

---

## Contact

**Xman Studio**
- GitHub: [@xjanova](https://github.com/xjanova)

---

<p align="center">
  Made with ❤️ by Xman Studio
</p>

<p align="center">
  <sub>v0.1.0-beta | PRO EDITION</sub>
</p>
