# AutoTrade-X AI Trading System Documentation

> **Version:** 0.2.0
> **Last Updated:** 2026-01-27
> **Author:** Development Team

---

## Overview

ระบบ AI Trading ของ AutoTrade-X ประกอบด้วย 6 กลยุทธ์การเทรดที่แตกต่างกัน โดยแต่ละกลยุทธ์ใช้ตัวชี้วัดทางเทคนิค (Technical Indicators) ที่เหมาะสมกับสภาวะตลาดที่แตกต่างกัน

---

## AI Trading Strategies

### 1. Scalping (สกัลปิง)
```
Mode: AITradingMode.Scalping
Hold Time: 5-15 นาที
TP: 0.3-0.5%
SL: 0.2-0.3%
```

**Indicators Used:**
- RSI (น้ำหนักสูง)
  - RSI < 25: Bullish +40
  - RSI < 35: Bullish +25
  - RSI > 75: Bearish -40
  - RSI > 65: Bearish -25
- Spread Analysis
  - Tight spread (< 0.1%): +15

**Best For:** ตลาดที่มี Volume สูง, Spread แคบ

---

### 2. Momentum (โมเมนตัม)
```
Mode: AITradingMode.Momentum
Hold Time: 1-4 ชั่วโมง
TP: 1.5-3%
SL: 0.8-1.5%
```

**Indicators Used:**
- EMA 9/21 Crossover: +35 / -35
- MACD Histogram: +30 / -30
- SMA50 Trend: +20 / -20
- RSI (moderate weight): +15 / -15

**Best For:** ตลาดที่มีเทรนด์ชัดเจน, Volatility ปานกลาง-สูง

---

### 3. Mean Reversion (คืนค่าเฉลี่ย)
```
Mode: AITradingMode.MeanReversion
Hold Time: 30 นาที - 2 ชั่วโมง
TP: 0.8-1.5%
SL: 0.5-1%
```

**Indicators Used:**
- Bollinger Bands
  - Price <= Lower Band: Bullish +45
  - Price >= Upper Band: Bearish -45
- RSI Extremes
  - RSI < 25: Bullish +35
  - RSI > 75: Bearish -35
- Distance from BB Middle: +20 / -20

**Best For:** ตลาด Sideways, Range-bound

---

### 4. Grid Trading (กริด)
```
Mode: AITradingMode.GridTrading
Hold Time: Variable (ขึ้นกับ grid level)
TP: Dynamic (grid spacing)
SL: 2-3% (overall)
```

**Indicators Used:**
- ATR (Average True Range) for grid sizing
- Support/Resistance levels
  - Near support: Bullish +30
  - Near resistance: Bearish -30
- Grid position scoring
- Volatility check (ATR-based)

**Best For:** ตลาด Sideways ที่มีกรอบราคาชัดเจน

---

### 5. Breakout (เบรคเอาท์)
```
Mode: AITradingMode.Breakout
Hold Time: 30 นาที - 4 ชั่วโมง
TP: 2-5%
SL: 1-2%
```

**Indicators Used:**
- Bollinger Band Breakout
  - Price > Upper BB: Bullish breakout +40
  - Price < Lower BB: Bearish breakout -40
- Volume Confirmation: +25 / -25
- Momentum (MACD): +20 / -20
- RSI Confirmation: +15 / -15

**Best For:** หลังช่วง Consolidation, ข่าวสำคัญ

---

### 6. Smart DCA (Dollar Cost Averaging อัจฉริยะ)
```
Mode: AITradingMode.SmartDCA
Hold Time: Long-term
TP: 3-5%
SL: 5-10%
```

**Indicators Used:**
- RSI for timing
  - RSI < 40: Good DCA entry +30
  - RSI < 30: Strong DCA entry +40
- SMA200 Trend
  - Price > SMA200: Uptrend +25
  - Price < SMA200: Downtrend (wait) -15
- Volatility consideration
- Position sizing based on drawdown

**Best For:** การลงทุนระยะยาว, สะสมเหรียญ

---

## Signal Generation Flow

```
┌─────────────────┐
│  Market Data    │
│  (Price, Vol,   │
│   Indicators)   │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ Strategy Router │
│ (switch by Mode)│
└────────┬────────┘
         │
    ┌────┴────┬────────┬──────────┬──────────┬──────────┐
    ▼         ▼        ▼          ▼          ▼          ▼
Scalping  Momentum  MeanRev   GridTrade  Breakout   SmartDCA
    │         │        │          │          │          │
    └────┬────┴────────┴──────────┴──────────┴──────────┘
         │
         ▼
┌─────────────────┐
│  Net Score      │
│  Calculation    │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ Signal Output   │
│ (Buy/Sell/Hold) │
│ + Confidence %  │
└─────────────────┘
```

---

## Auto Strategy Recommendation

```csharp
// File: AITradingModels.cs
public static AITradingMode RecommendStrategy(
    decimal volatilityPercent,
    bool isTrending,
    decimal volume24h)
```

| Condition | Recommended Strategy |
|-----------|---------------------|
| Volatility > 3% + Trending + High Volume | Breakout |
| Volatility > 3% + Trending | Momentum |
| Volatility < 2% + Sideways + High Volume | Grid Trading |
| Volatility < 2% + Sideways | Mean Reversion |
| Volatility 1-3% | Scalping |
| Uncertain conditions | Smart DCA |

---

## Demo Trading Mode

### Architecture

```
┌─────────────────────────────────────────────────────┐
│                  AITradingPage                       │
│  ┌─────────────┐    ┌─────────────────────────────┐ │
│  │ _isDemoMode │───▶│ Demo Mode: DemoWalletService │ │
│  │   = true    │    │ Real Mode: AITradingService  │ │
│  └─────────────┘    └─────────────────────────────┘ │
└─────────────────────────────────────────────────────┘
```

### DemoWalletService API

#### Buy (Open Position)
```csharp
Task<DemoTradeResult> ExecuteAIBuyAsync(
    string pair,        // e.g., "BTC/USDT"
    string exchange,    // e.g., "Binance"
    decimal quantity,   // Amount of base currency
    decimal price,      // Entry price
    decimal feePercent = 0.1m  // Trading fee
)
```

**Flow:**
1. Check USDT balance >= cost + fee
2. Deduct USDT from balance
3. Add coin to balance
4. Update total wallet value
5. Fire `WalletChanged` event

#### Sell (Close Position)
```csharp
Task<DemoTradeResult> ExecuteAISellAsync(
    string pair,
    string exchange,
    decimal quantity,
    decimal entryPrice,   // Original buy price
    decimal exitPrice,    // Current sell price
    decimal feePercent = 0.1m
)
```

**Flow:**
1. Check coin balance >= quantity
2. Calculate: revenue = quantity × exitPrice - fee
3. Calculate: profit = revenue - originalCost
4. Add USDT to balance
5. Deduct coin from balance
6. Record trade in database
7. Update win/loss count
8. Fire `WalletChanged` event

### DemoTradeResult
```csharp
public class DemoTradeResult
{
    bool Success          // Trade executed?
    DemoTrade? Trade      // Trade record (on sell)
    decimal Profit        // PnL in USDT
    decimal ProfitPercent // PnL %
    decimal NewBalance    // New USDT balance
    string Message        // Thai/English message
}
```

### DemoAIPosition (UI State)
```csharp
public class DemoAIPosition
{
    string Symbol
    string Exchange
    decimal EntryPrice
    decimal CurrentPrice      // Updated real-time
    decimal Quantity
    decimal TakeProfitPrice
    decimal StopLossPrice
    DateTime EntryTime
    AITradingMode Strategy

    // Calculated
    decimal UnrealizedPnL
    decimal UnrealizedPnLPercent
    decimal Value
}
```

---

## Real-Time Updates

### Price Update Flow (Every 1 second)
```
UpdateMarketDataAsync()
    │
    ├─► Get market data from exchange
    │
    ├─► Update UI (price, indicators)
    │
    ├─► If Demo Mode + Has Position:
    │       ├─► Update _demoPosition.CurrentPrice
    │       └─► CheckDemoTPSLAsync()
    │               ├─► If price >= TP → Auto Sell
    │               └─► If price <= SL → Auto Sell
    │
    └─► UpdatePositionDisplay()
```

### TP/SL Auto-Close
```csharp
private async Task CheckDemoTPSLAsync(decimal currentPrice)
{
    // Take Profit
    if (currentPrice >= _demoPosition.TakeProfitPrice)
    {
        await _demoWallet.ExecuteAISellAsync(...);
        // Show success notification
    }

    // Stop Loss
    if (currentPrice <= _demoPosition.StopLossPrice)
    {
        await _demoWallet.ExecuteAISellAsync(...);
        // Show warning notification
    }
}
```

---

## Database Schema (SQLite)

### DemoWallet Table
```sql
CREATE TABLE DemoWallet (
    Id INTEGER PRIMARY KEY,
    StartingBalance REAL,
    TotalValueUSD REAL,
    TotalProfit REAL,
    WinCount INTEGER,
    LossCount INTEGER,
    UpdatedAt TEXT
);
```

### DemoBalances Table
```sql
CREATE TABLE DemoBalances (
    Currency TEXT PRIMARY KEY,
    Amount REAL
);
```

### DemoTrades Table
```sql
CREATE TABLE DemoTrades (
    Id TEXT PRIMARY KEY,
    Timestamp TEXT,
    Pair TEXT,
    BuyExchange TEXT,
    SellExchange TEXT,
    Quantity REAL,
    BuyPrice REAL,
    SellPrice REAL,
    Profit REAL,
    ProfitPercent REAL,
    TotalFees REAL
);
```

---

## UI Components

### Demo Mode Toggle
- Location: Top bar of AITradingPage
- Default: ON (Demo Mode)
- Badge shows: "ON" (orange) or "OFF" (gray)
- Balance shows: "$10,000.00" or "REAL MODE"

### Position Display
| Mode | Status Badge | Color |
|------|-------------|-------|
| Demo + Position | "DEMO POSITION" | Green |
| Real + Position | "IN POSITION" | Green |
| No Position | "NO POSITION" | Orange |

### Trade Markers on Chart
- Buy: Green arrow + TP/SL lines
- Sell: Red arrow
- TP Line: Green horizontal
- SL Line: Red horizontal
- Entry Line: Blue horizontal

---

## Event Flow

```
User clicks "BUY"
    │
    ├─► [Demo Mode]
    │       │
    │       ├─► ExecuteDemoBuyAsync()
    │       │       ├─► DemoWalletService.ExecuteAIBuyAsync()
    │       │       ├─► Create _demoPosition
    │       │       ├─► AddTradeMarkerToChart()
    │       │       └─► UpdateDemoWalletDisplay()
    │       │
    │       └─► Show success MessageBox
    │
    └─► [Real Mode]
            │
            └─► AITradingService.ExecuteManualTradeAsync()
```

---

## Error Handling

| Error | Thai Message | Action |
|-------|-------------|--------|
| Insufficient balance | "เงินไม่พอ" | Block trade |
| Insufficient coins | "เหรียญไม่พอ" | Block trade |
| Already has position | "มี position อยู่แล้ว" | Block trade |
| No position to sell | "ไม่มี position ที่จะขาย" | Block trade |
| Cannot get price | "ไม่สามารถดึงราคาได้" | Block trade |

---

## Files Reference

| File | Purpose |
|------|---------|
| `AITradingService.cs` | Core AI logic, signal generation |
| `AITradingModels.cs` | Data models, strategy info |
| `DemoWalletService.cs` | Demo trading wallet |
| `AITradingPage.xaml` | UI layout |
| `AITradingPage.xaml.cs` | UI logic, demo trading |

---

## For Claude AI Assistant

When working on AI Trading features:

1. **Strategy Changes**: Edit `GenerateXXXSignal()` methods in `AITradingService.cs`
2. **New Indicators**: Add to `AIMarketData` model and update signal methods
3. **Demo Trading**: Use `DemoWalletService` methods, NOT direct database access
4. **UI Updates**: Maintain demo/real mode separation in click handlers
5. **TP/SL Logic**: Check `CheckDemoTPSLAsync()` for auto-close behavior

### Important Variables in AITradingPage
```csharp
private bool _isDemoMode = true;           // Demo mode flag
private DemoAIPosition? _demoPosition;     // Current demo position
private AITradingPosition? _currentPosition; // Real position (from service)
private DemoWalletService? _demoWallet;    // Demo wallet service
```

### Testing Demo Mode
1. Ensure `_isDemoMode = true`
2. Click "BUY" button
3. Check wallet balance decreased
4. Check position display shows "DEMO POSITION"
5. Wait for TP/SL or click "SELL"
6. Verify PnL calculation and balance update
