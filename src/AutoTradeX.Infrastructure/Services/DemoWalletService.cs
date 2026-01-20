/*
 * ============================================================================
 * AutoTrade-X - Demo Wallet Service (SQLite)
 * ============================================================================
 * Virtual balance for paper trading - stored in SQLite database
 * ============================================================================
 */

using AutoTradeX.Core.Interfaces;
using AutoTradeX.Infrastructure.Data;

namespace AutoTradeX.Infrastructure.Services;

/// <summary>
/// Demo Wallet Service - Virtual balance for paper trading (SQLite backed)
/// </summary>
public class DemoWalletService
{
    private readonly IDatabaseService _db;
    private readonly ILoggingService _logger;
    private DemoWallet? _cachedWallet;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public event EventHandler<DemoWallet>? WalletChanged;

    public DemoWalletService(IDatabaseService databaseService, ILoggingService logger)
    {
        _db = databaseService;
        _logger = logger;
    }

    public async Task<DemoWallet> GetWalletAsync()
    {
        if (_cachedWallet != null) return _cachedWallet;

        await _lock.WaitAsync();
        try
        {
            _cachedWallet = await LoadWalletAsync();
            return _cachedWallet;
        }
        finally
        {
            _lock.Release();
        }
    }

    public DemoWallet GetWallet()
    {
        return GetWalletAsync().GetAwaiter().GetResult();
    }

    public async Task<decimal> GetBalanceAsync(string currency = "USDT")
    {
        var wallet = await GetWalletAsync();
        return wallet.Balances.GetValueOrDefault(currency, 0);
    }

    public decimal GetBalance(string currency = "USDT")
    {
        return GetBalanceAsync(currency).GetAwaiter().GetResult();
    }

    public async Task<decimal> GetTotalValueInUSDAsync()
    {
        var wallet = await GetWalletAsync();
        return wallet.TotalValueUSD;
    }

    public decimal GetTotalValueInUSD() => GetTotalValueInUSDAsync().GetAwaiter().GetResult();

    public async Task ResetWalletAsync(decimal startingBalance = 10000m)
    {
        await _lock.WaitAsync();
        try
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

            WalletChanged?.Invoke(this, _cachedWallet);
            _logger.LogInfo("DemoWallet", $"Wallet reset to ${startingBalance:N2}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public void ResetWallet(decimal startingBalance = 10000m)
    {
        ResetWalletAsync(startingBalance).GetAwaiter().GetResult();
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
        await _lock.WaitAsync();
        try
        {
            var wallet = await GetWalletAsync();
            var baseCurrency = pair.Split('/')[0];
            var quoteCurrency = pair.Split('/')[1];

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

            WalletChanged?.Invoke(this, wallet);

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

    public DemoTradeResult ExecuteTrade(
        string pair,
        string buyExchange,
        string sellExchange,
        decimal quantity,
        decimal buyPrice,
        decimal sellPrice,
        decimal feePercent = 0.1m)
    {
        return ExecuteTradeAsync(pair, buyExchange, sellExchange, quantity, buyPrice, sellPrice, feePercent)
            .GetAwaiter().GetResult();
    }

    public async Task<List<DemoTrade>> GetRecentTradesAsync(int count = 50)
    {
        var records = await _db.QueryAsync<DemoTradeDbRecord>(
            "SELECT * FROM DemoTrades ORDER BY Timestamp DESC LIMIT @Count",
            new { Count = count });

        return records.Select(r => new DemoTrade
        {
            Id = r.Id,
            Timestamp = DateTime.Parse(r.Timestamp),
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

    private async Task<DemoWallet> LoadWalletAsync()
    {
        try
        {
            // Load wallet
            var walletRecord = await _db.QueryFirstOrDefaultAsync<DemoWalletDbRecord>(
                "SELECT * FROM DemoWallet WHERE Id = 1");

            if (walletRecord == null)
            {
                // สร้าง wallet ใหม่
                await ResetWalletAsync(10000m);
                return _cachedWallet!;
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
