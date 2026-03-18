/*
 * ============================================================================
 * AutoTrade-X - Demo Wallet Service (SQLite)
 * ============================================================================
 * Virtual balance for paper trading - stored in SQLite database
 * ============================================================================
 */

using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using AutoTradeX.Infrastructure.Data;
using System.Globalization;

namespace AutoTradeX.Infrastructure.Services;

/// <summary>
/// Demo Wallet Service - Virtual balance for paper trading (SQLite backed)
/// </summary>
public class DemoWalletService
{
    private readonly IDatabaseService _db;
    private readonly ILoggingService _logger;
    private volatile DemoWallet? _cachedWallet;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public event EventHandler<DemoWallet>? WalletChanged;

    public DemoWalletService(IDatabaseService databaseService, ILoggingService logger)
    {
        _db = databaseService;
        _logger = logger;
    }

    /// <summary>
    /// Safely parse a trading pair into base and quote currencies
    /// แยกคู่เทรดเป็น base และ quote currency อย่างปลอดภัย
    /// </summary>
    private static (string baseCurrency, string quoteCurrency) ParsePair(string pair)
    {
        if (string.IsNullOrWhiteSpace(pair))
            throw new ArgumentException("Trading pair cannot be empty", nameof(pair));

        var parts = pair.Split('/');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            throw new ArgumentException($"Invalid trading pair format '{pair}'. Expected format: 'BTC/USDT'", nameof(pair));

        return (parts[0].Trim().ToUpperInvariant(), parts[1].Trim().ToUpperInvariant());
    }

    /// <summary>
    /// Safely fire WalletChanged event without letting subscriber exceptions crash the service
    /// เรียก event อย่างปลอดภัยไม่ให้ subscriber ทำให้ service ล่ม
    /// </summary>
    private void RaiseWalletChanged(DemoWallet wallet)
    {
        try
        {
            WalletChanged?.Invoke(this, wallet);
        }
        catch (Exception ex)
        {
            _logger.LogError("DemoWallet", $"Error in WalletChanged event handler: {ex.Message}");
        }
    }

    public async Task<DemoWallet> GetWalletAsync()
    {
        // Double-check pattern: return cache if available without acquiring lock
        var cached = _cachedWallet;
        if (cached != null) return cached;

        await _lock.WaitAsync();
        try
        {
            // Re-check after acquiring lock (another thread may have loaded it)
            if (_cachedWallet != null) return _cachedWallet;
            _cachedWallet = await LoadWalletAsync();
            return _cachedWallet;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Get cached wallet without blocking (returns null if not loaded yet)
    /// ใช้สำหรับ UI เพื่อไม่ให้ block UI thread
    /// </summary>
    public DemoWallet? GetCachedWallet()
    {
        return _cachedWallet;
    }

    public async Task<decimal> GetBalanceAsync(string currency = "USDT")
    {
        var wallet = await GetWalletAsync();
        return wallet.Balances.GetValueOrDefault(currency, 0);
    }

    public async Task<decimal> GetTotalValueInUSDAsync()
    {
        var wallet = await GetWalletAsync();
        return wallet.TotalValueUSD;
    }

    public async Task ResetWalletAsync(decimal startingBalance = 10000m)
    {
        await _lock.WaitAsync();
        try
        {
            await ResetWalletInternalAsync(startingBalance);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Internal reset logic — must be called while _lock is already held.
    /// ใช้ภายในเมื่อ lock ถูก acquire แล้ว (เช่น จาก LoadWalletAsync)
    /// </summary>
    private async Task ResetWalletInternalAsync(decimal startingBalance = 10000m)
    {
        // ลบข้อมูลเก่า
        await _db.ExecuteAsync("DELETE FROM DemoWallet");
        await _db.ExecuteAsync("DELETE FROM DemoBalances");
        await _db.ExecuteAsync("DELETE FROM DemoTrades");

        // สร้าง Wallet ใหม่
        await _db.ExecuteAsync(@"
            INSERT INTO DemoWallet (Id, StartingBalance, TotalValueUSD, TotalProfit, WinCount, LossCount, UpdatedAt)
            VALUES (1, @StartingBalance, @TotalValueUSD, 0, 0, 0, @UpdatedAt)",
            new
            {
                StartingBalance = startingBalance,
                TotalValueUSD = startingBalance,
                UpdatedAt = DateTime.UtcNow.ToString("o")
            });

        // สร้าง Balance เริ่มต้น
        var defaultCurrencies = new[] { "USDT", "BTC", "ETH", "SOL" };
        foreach (var currency in defaultCurrencies)
        {
            await _db.ExecuteAsync(@"
                INSERT INTO DemoBalances (Currency, Amount)
                VALUES (@Currency, @Amount)",
                new
                {
                    Currency = currency,
                    Amount = currency == "USDT" ? startingBalance : 0m
                });
        }

        // อัปเดต cache
        _cachedWallet = new DemoWallet
        {
            StartingBalance = startingBalance,
            Balances = new Dictionary<string, decimal>
            {
                { "USDT", startingBalance },
                { "BTC", 0 },
                { "ETH", 0 },
                { "SOL", 0 }
            },
            TotalValueUSD = startingBalance,
            TotalProfit = 0,
            WinCount = 0,
            LossCount = 0,
            Trades = new List<DemoTrade>()
        };

        RaiseWalletChanged(_cachedWallet);
        _logger.LogInfo("DemoWallet", $"Wallet reset to ${startingBalance:N2}");
    }

    public async Task<DemoTradeResult> ExecuteTradeAsync(
        string pair,
        string buyExchange,
        string sellExchange,
        decimal quantity,
        decimal buyPrice,
        decimal sellPrice,
        decimal feePercent = 0.1m)
    {
        // Validate inputs
        if (quantity <= 0) return new DemoTradeResult { Success = false, Message = "Quantity must be positive" };
        if (buyPrice <= 0) return new DemoTradeResult { Success = false, Message = "Buy price must be positive" };
        if (sellPrice <= 0) return new DemoTradeResult { Success = false, Message = "Sell price must be positive" };

        await _lock.WaitAsync();
        try
        {
            var wallet = await GetWalletAsync();
            var (baseCurrency, quoteCurrency) = ParsePair(pair);

            // Calculate costs
            var buyCost = quantity * buyPrice;
            var buyFee = buyCost * (feePercent / 100);
            var totalBuyCost = buyCost + buyFee;

            // Check if enough balance
            var quoteBalance = wallet.Balances.GetValueOrDefault(quoteCurrency, 0);
            if (quoteBalance < totalBuyCost)
            {
                return new DemoTradeResult
                {
                    Success = false,
                    Message = $"Insufficient {quoteCurrency} balance. Need: {totalBuyCost:N2}, Have: {quoteBalance:N2}"
                };
            }

            // Calculate revenue
            var sellRevenue = quantity * sellPrice;
            var sellFee = sellRevenue * (feePercent / 100);
            var netSellRevenue = sellRevenue - sellFee;

            // Calculate profit
            var profit = netSellRevenue - totalBuyCost;
            var profitPercent = (profit / totalBuyCost) * 100;

            // Update balances
            var newBalance = quoteBalance - totalBuyCost + netSellRevenue;

            // Update database
            await _db.ExecuteAsync(@"
                INSERT OR REPLACE INTO DemoBalances (Currency, Amount)
                VALUES (@Currency, @Amount)",
                new { Currency = quoteCurrency, Amount = newBalance });

            // Record trade
            var trade = new DemoTrade
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow,
                Pair = pair,
                BuyExchange = buyExchange,
                SellExchange = sellExchange,
                Quantity = quantity,
                BuyPrice = buyPrice,
                SellPrice = sellPrice,
                Profit = profit,
                ProfitPercent = profitPercent,
                TotalFees = buyFee + sellFee
            };

            await _db.ExecuteAsync(@"
                INSERT INTO DemoTrades (Id, Timestamp, Pair, BuyExchange, SellExchange, Quantity, BuyPrice, SellPrice, Profit, ProfitPercent, TotalFees)
                VALUES (@Id, @Timestamp, @Pair, @BuyExchange, @SellExchange, @Quantity, @BuyPrice, @SellPrice, @Profit, @ProfitPercent, @TotalFees)",
                new
                {
                    trade.Id,
                    Timestamp = trade.Timestamp.ToString("o"),
                    trade.Pair,
                    trade.BuyExchange,
                    trade.SellExchange,
                    trade.Quantity,
                    trade.BuyPrice,
                    trade.SellPrice,
                    trade.Profit,
                    trade.ProfitPercent,
                    trade.TotalFees
                });

            // Update wallet stats
            var isWin = profit > 0;
            await _db.ExecuteAsync(@"
                UPDATE DemoWallet SET
                    TotalValueUSD = @TotalValueUSD,
                    TotalProfit = TotalProfit + @Profit,
                    WinCount = WinCount + @WinIncrement,
                    LossCount = LossCount + @LossIncrement,
                    UpdatedAt = @UpdatedAt
                WHERE Id = 1",
                new
                {
                    TotalValueUSD = newBalance,
                    Profit = profit,
                    WinIncrement = isWin ? 1 : 0,
                    LossIncrement = isWin ? 0 : 1,
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                });

            // Update cache
            wallet.Balances[quoteCurrency] = newBalance;
            wallet.TotalProfit += profit;
            wallet.TotalValueUSD = newBalance;
            if (isWin) wallet.WinCount++; else wallet.LossCount++;
            wallet.Trades.Insert(0, trade);
            if (wallet.Trades.Count > 100)
                wallet.Trades = wallet.Trades.Take(100).ToList();

            RaiseWalletChanged(wallet);

            _logger.LogInfo("DemoWallet",
                $"Trade executed: {pair} Buy@{buyExchange} ${buyPrice:N2} -> Sell@{sellExchange} ${sellPrice:N2} = " +
                $"Profit: ${profit:N2} ({profitPercent:N2}%)");

            return new DemoTradeResult
            {
                Success = true,
                Trade = trade,
                Profit = profit,
                ProfitPercent = profitPercent,
                NewBalance = newBalance,
                Message = profit > 0
                    ? $"Profit: +${profit:N2} ({profitPercent:N2}%)"
                    : $"Loss: ${profit:N2} ({profitPercent:N2}%)"
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<DemoTrade>> GetRecentTradesAsync(int count = 50)
    {
        var records = await _db.QueryAsync<DemoTradeDbRecord>(
            "SELECT * FROM DemoTrades ORDER BY Timestamp DESC LIMIT @Count",
            new { Count = count });

        return records.Select(r => new DemoTrade
        {
            Id = r.Id,
            Timestamp = DateTime.Parse(r.Timestamp, CultureInfo.InvariantCulture),
            Pair = r.Pair,
            BuyExchange = r.BuyExchange,
            SellExchange = r.SellExchange,
            Quantity = (decimal)r.Quantity,
            BuyPrice = (decimal)r.BuyPrice,
            SellPrice = (decimal)r.SellPrice,
            Profit = (decimal)r.Profit,
            ProfitPercent = (decimal)r.ProfitPercent,
            TotalFees = (decimal)r.TotalFees
        }).ToList();
    }

    /// <summary>
    /// Execute a single-exchange AI trade (Buy) - deducts USDT and adds coin
    /// ซื้อเหรียญด้วยเงินเสมือน - หักเงิน USDT และเพิ่มเหรียญ
    /// </summary>
    /// <param name="pair">Trading pair (e.g., BTC/USDT)</param>
    /// <param name="exchange">Exchange name (e.g., Binance, Bitkub)</param>
    /// <param name="quantity">Amount of base currency to buy</param>
    /// <param name="price">Price per unit in quote currency</param>
    /// <param name="feePercent">Optional: Override fee percent (default: use exchange-specific fee)</param>
    public async Task<DemoTradeResult> ExecuteAIBuyAsync(
        string pair,
        string exchange,
        decimal quantity,
        decimal price,
        decimal? feePercent = null)
    {
        // Validate inputs
        if (quantity <= 0) return new DemoTradeResult { Success = false, Message = "Quantity must be positive" };
        if (price <= 0) return new DemoTradeResult { Success = false, Message = "Price must be positive" };
        if (string.IsNullOrWhiteSpace(exchange)) return new DemoTradeResult { Success = false, Message = "Exchange cannot be empty" };

        await _lock.WaitAsync();
        try
        {
            var wallet = await GetWalletAsync();
            var (baseCurrency, quoteCurrency) = ParsePair(pair);

            // Use exchange-specific fee if not overridden
            var actualFeePercent = feePercent ?? ExchangeFees.GetDefaultTakerFee(exchange);
            _logger.LogInfo("DemoWallet", $"Using fee: {actualFeePercent}% for {exchange}");

            // Calculate costs
            var cost = quantity * price;
            var fee = cost * (actualFeePercent / 100);
            var totalCost = cost + fee;

            // Check if enough balance
            var quoteBalance = wallet.Balances.GetValueOrDefault(quoteCurrency, 0);
            if (quoteBalance < totalCost)
            {
                return new DemoTradeResult
                {
                    Success = false,
                    Message = $"เงินไม่พอ {quoteCurrency}. ต้องการ: {totalCost:N2}, มี: {quoteBalance:N2}"
                };
            }

            // Update balances: deduct quote (USDT), add base (BTC)
            var newQuoteBalance = quoteBalance - totalCost;
            var baseBalance = wallet.Balances.GetValueOrDefault(baseCurrency, 0);
            var newBaseBalance = baseBalance + quantity;

            // Update database
            await _db.ExecuteAsync(@"
                INSERT OR REPLACE INTO DemoBalances (Currency, Amount)
                VALUES (@Currency, @Amount)",
                new { Currency = quoteCurrency, Amount = newQuoteBalance });

            await _db.ExecuteAsync(@"
                INSERT OR REPLACE INTO DemoBalances (Currency, Amount)
                VALUES (@Currency, @Amount)",
                new { Currency = baseCurrency, Amount = newBaseBalance });

            // Update wallet total value (approximate)
            var totalValue = newQuoteBalance + (newBaseBalance * price);

            await _db.ExecuteAsync(@"
                UPDATE DemoWallet SET
                    TotalValueUSD = @TotalValueUSD,
                    UpdatedAt = @UpdatedAt
                WHERE Id = 1",
                new
                {
                    TotalValueUSD = totalValue,
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                });

            // Update cache
            wallet.Balances[quoteCurrency] = newQuoteBalance;
            wallet.Balances[baseCurrency] = newBaseBalance;
            wallet.TotalValueUSD = totalValue;

            RaiseWalletChanged(wallet);

            _logger.LogInfo("DemoWallet",
                $"AI Buy: {pair} @ {exchange} | {quantity:N6} @ ${price:N2} | Cost: ${totalCost:N2} (Fee: ${fee:N2})");

            return new DemoTradeResult
            {
                Success = true,
                Profit = 0, // Not closed yet
                ProfitPercent = 0,
                NewBalance = newQuoteBalance,
                Message = $"ซื้อ {quantity:N6} {baseCurrency} @ ${price:N2}"
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Execute a single-exchange AI trade (Sell/Close) - adds USDT and removes coin
    /// ขายเหรียญ - เพิ่มเงิน USDT และหักเหรียญ
    /// </summary>
    /// <param name="pair">Trading pair (e.g., BTC/USDT)</param>
    /// <param name="exchange">Exchange name (e.g., Binance, Bitkub)</param>
    /// <param name="quantity">Amount of base currency to sell</param>
    /// <param name="entryPrice">Original buy price</param>
    /// <param name="exitPrice">Current sell price</param>
    /// <param name="feePercent">Optional: Override fee percent (default: use exchange-specific fee)</param>
    public async Task<DemoTradeResult> ExecuteAISellAsync(
        string pair,
        string exchange,
        decimal quantity,
        decimal entryPrice,
        decimal exitPrice,
        decimal? feePercent = null)
    {
        // Validate inputs
        if (quantity <= 0) return new DemoTradeResult { Success = false, Message = "Quantity must be positive" };
        if (entryPrice <= 0) return new DemoTradeResult { Success = false, Message = "Entry price must be positive" };
        if (exitPrice <= 0) return new DemoTradeResult { Success = false, Message = "Exit price must be positive" };
        if (string.IsNullOrWhiteSpace(exchange)) return new DemoTradeResult { Success = false, Message = "Exchange cannot be empty" };

        await _lock.WaitAsync();
        try
        {
            var wallet = await GetWalletAsync();
            var (baseCurrency, quoteCurrency) = ParsePair(pair);

            // Use exchange-specific fee if not overridden
            // Use taker fee for sell orders (market orders for immediate execution)
            var actualFeePercent = feePercent ?? ExchangeFees.GetDefaultTakerFee(exchange);
            _logger.LogInfo("DemoWallet", $"Using fee: {actualFeePercent}% for {exchange} sell");

            // Check if enough coins to sell
            var baseBalance = wallet.Balances.GetValueOrDefault(baseCurrency, 0);
            if (baseBalance < quantity)
            {
                return new DemoTradeResult
                {
                    Success = false,
                    Message = $"เหรียญไม่พอ {baseCurrency}. ต้องการ: {quantity:N6}, มี: {baseBalance:N6}"
                };
            }

            // Calculate sell revenue with sell fee
            var revenue = quantity * exitPrice;
            var fee = revenue * (actualFeePercent / 100);
            var netRevenue = revenue - fee;

            // Calculate original cost with buy fee (use taker fee for buy)
            var buyFeePercent = ExchangeFees.GetDefaultTakerFee(exchange);
            var originalCost = quantity * entryPrice;
            var entryFee = originalCost * (buyFeePercent / 100);
            var totalEntryCost = originalCost + entryFee;

            // Calculate profit
            var profit = netRevenue - totalEntryCost;
            var profitPercent = totalEntryCost > 0 ? (profit / totalEntryCost) * 100 : 0;

            // Update balances
            var quoteBalance = wallet.Balances.GetValueOrDefault(quoteCurrency, 0);
            var newQuoteBalance = quoteBalance + netRevenue;
            var newBaseBalance = baseBalance - quantity;

            // Update database
            await _db.ExecuteAsync(@"
                INSERT OR REPLACE INTO DemoBalances (Currency, Amount)
                VALUES (@Currency, @Amount)",
                new { Currency = quoteCurrency, Amount = newQuoteBalance });

            await _db.ExecuteAsync(@"
                INSERT OR REPLACE INTO DemoBalances (Currency, Amount)
                VALUES (@Currency, @Amount)",
                new { Currency = baseCurrency, Amount = newBaseBalance });

            // Record trade
            var trade = new DemoTrade
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow,
                Pair = pair,
                BuyExchange = exchange,
                SellExchange = exchange,
                Quantity = quantity,
                BuyPrice = entryPrice,
                SellPrice = exitPrice,
                Profit = profit,
                ProfitPercent = profitPercent,
                TotalFees = entryFee + fee
            };

            await _db.ExecuteAsync(@"
                INSERT INTO DemoTrades (Id, Timestamp, Pair, BuyExchange, SellExchange, Quantity, BuyPrice, SellPrice, Profit, ProfitPercent, TotalFees)
                VALUES (@Id, @Timestamp, @Pair, @BuyExchange, @SellExchange, @Quantity, @BuyPrice, @SellPrice, @Profit, @ProfitPercent, @TotalFees)",
                new
                {
                    trade.Id,
                    Timestamp = trade.Timestamp.ToString("o"),
                    trade.Pair,
                    trade.BuyExchange,
                    trade.SellExchange,
                    trade.Quantity,
                    trade.BuyPrice,
                    trade.SellPrice,
                    trade.Profit,
                    trade.ProfitPercent,
                    trade.TotalFees
                });

            // Update wallet stats
            var isWin = profit > 0;
            var totalValue = newQuoteBalance + (newBaseBalance * exitPrice);

            await _db.ExecuteAsync(@"
                UPDATE DemoWallet SET
                    TotalValueUSD = @TotalValueUSD,
                    TotalProfit = TotalProfit + @Profit,
                    WinCount = WinCount + @WinIncrement,
                    LossCount = LossCount + @LossIncrement,
                    UpdatedAt = @UpdatedAt
                WHERE Id = 1",
                new
                {
                    TotalValueUSD = totalValue,
                    Profit = profit,
                    WinIncrement = isWin ? 1 : 0,
                    LossIncrement = isWin ? 0 : 1,
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                });

            // Update cache
            wallet.Balances[quoteCurrency] = newQuoteBalance;
            wallet.Balances[baseCurrency] = newBaseBalance;
            wallet.TotalProfit += profit;
            wallet.TotalValueUSD = totalValue;
            if (isWin) wallet.WinCount++; else wallet.LossCount++;
            wallet.Trades.Insert(0, trade);
            if (wallet.Trades.Count > 100)
                wallet.Trades = wallet.Trades.Take(100).ToList();

            RaiseWalletChanged(wallet);

            var profitColor = profit >= 0 ? "+" : "";
            _logger.LogInfo("DemoWallet",
                $"AI Sell: {pair} @ {exchange} | {quantity:N6} @ ${exitPrice:N2} | PnL: {profitColor}${profit:N2} ({profitColor}{profitPercent:N2}%)");

            return new DemoTradeResult
            {
                Success = true,
                Trade = trade,
                Profit = profit,
                ProfitPercent = profitPercent,
                NewBalance = newQuoteBalance,
                Message = profit >= 0
                    ? $"กำไร: +${profit:N2} ({profitPercent:N2}%)"
                    : $"ขาดทุน: ${profit:N2} ({profitPercent:N2}%)"
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<DemoWallet> LoadWalletAsync()
    {
        try
        {
            // Load wallet
            var walletRecord = await _db.QueryFirstOrDefaultAsync<DemoWalletDbRecord>(
                "SELECT * FROM DemoWallet WHERE Id = 1");

            if (walletRecord == null)
            {
                // สร้าง wallet ใหม่ (ใช้ Internal เพราะ lock ถูก acquire แล้วจาก GetWalletAsync)
                await ResetWalletInternalAsync(10000m);
                // ResetWalletInternalAsync sets _cachedWallet, but guard against null just in case
                return _cachedWallet ?? new DemoWallet
                {
                    StartingBalance = 10000m,
                    Balances = new Dictionary<string, decimal> { { "USDT", 10000m } },
                    TotalValueUSD = 10000m,
                    Trades = new List<DemoTrade>()
                };
            }

            // Load balances
            var balanceRecords = await _db.QueryAsync<DemoBalanceDbRecord>(
                "SELECT * FROM DemoBalances");

            var balances = balanceRecords.ToDictionary(
                b => b.Currency,
                b => (decimal)b.Amount);

            // Load recent trades
            var trades = await GetRecentTradesAsync(100);

            var wallet = new DemoWallet
            {
                StartingBalance = (decimal)walletRecord.StartingBalance,
                Balances = balances,
                TotalValueUSD = (decimal)walletRecord.TotalValueUSD,
                TotalProfit = (decimal)walletRecord.TotalProfit,
                WinCount = walletRecord.WinCount,
                LossCount = walletRecord.LossCount,
                Trades = trades
            };

            _logger.LogInfo("DemoWallet", $"Wallet loaded: ${wallet.TotalValueUSD:N2}");
            return wallet;
        }
        catch (Exception ex)
        {
            _logger.LogError("DemoWallet", $"Error loading wallet: {ex.Message}");

            // Return default wallet
            return new DemoWallet
            {
                StartingBalance = 10000m,
                Balances = new Dictionary<string, decimal>
                {
                    { "USDT", 10000m },
                    { "BTC", 0 },
                    { "ETH", 0 },
                    { "SOL", 0 }
                },
                TotalValueUSD = 10000m,
                TotalProfit = 0,
                WinCount = 0,
                LossCount = 0,
                Trades = new List<DemoTrade>()
            };
        }
    }
}

// Database record classes
internal class DemoWalletDbRecord
{
    public int Id { get; set; }
    public double StartingBalance { get; set; }
    public double TotalValueUSD { get; set; }
    public double TotalProfit { get; set; }
    public int WinCount { get; set; }
    public int LossCount { get; set; }
}

internal class DemoBalanceDbRecord
{
    public string Currency { get; set; } = "";
    public double Amount { get; set; }
}

internal class DemoTradeDbRecord
{
    public string Id { get; set; } = "";
    public string Timestamp { get; set; } = "";
    public string Pair { get; set; } = "";
    public string BuyExchange { get; set; } = "";
    public string SellExchange { get; set; } = "";
    public double Quantity { get; set; }
    public double BuyPrice { get; set; }
    public double SellPrice { get; set; }
    public double Profit { get; set; }
    public double ProfitPercent { get; set; }
    public double TotalFees { get; set; }
}

// Public models
public class DemoWallet
{
    public decimal StartingBalance { get; set; } = 10000m;
    public Dictionary<string, decimal> Balances { get; set; } = new();
    public decimal TotalValueUSD { get; set; }
    public decimal TotalProfit { get; set; }
    public int WinCount { get; set; }
    public int LossCount { get; set; }
    public List<DemoTrade> Trades { get; set; } = new();

    public decimal WinRate => WinCount + LossCount > 0
        ? (decimal)WinCount / (WinCount + LossCount) * 100
        : 0;
}

public class DemoTrade
{
    public string Id { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string Pair { get; set; } = "";
    public string BuyExchange { get; set; } = "";
    public string SellExchange { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal BuyPrice { get; set; }
    public decimal SellPrice { get; set; }
    public decimal Profit { get; set; }
    public decimal ProfitPercent { get; set; }
    public decimal TotalFees { get; set; }
}

public class DemoTradeResult
{
    public bool Success { get; set; }
    public DemoTrade? Trade { get; set; }
    public decimal Profit { get; set; }
    public decimal ProfitPercent { get; set; }
    public decimal NewBalance { get; set; }
    public string Message { get; set; } = "";
}
