// AutoTrade-X v1.0.0
// Balance Pool Service Implementation - Real P&L Tracking from Actual Wallet Balances

using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using System.Collections.Concurrent;

namespace AutoTradeX.Core.Services;

/// <summary>
/// BalancePoolService - Tracks real P&L from actual wallet balances across both exchanges
///
/// Key responsibilities:
/// 1. Track actual balances in both exchange wallets
/// 2. Calculate real P&L from balance changes (not just trade results)
/// 3. Monitor for emergency conditions (drawdown, imbalance, rapid loss)
/// 4. Recommend rebalancing when distribution becomes skewed
/// </summary>
public class BalancePoolService : IBalancePoolService
{
    private readonly IExchangeClient _exchangeA;
    private readonly IExchangeClient _exchangeB;
    private readonly ILoggingService _logger;
    private readonly IConfigService _configService;
    private readonly ICoinDataService _coinDataService;

    private BalancePoolSnapshot _initialSnapshot = new();
    private BalancePoolSnapshot _currentSnapshot = new();
    private decimal _peakValueUSDT;
    private decimal _maxDrawdown;
    private readonly ConcurrentQueue<BalancePoolSnapshot> _history = new();
    private readonly ConcurrentQueue<TradeResult> _recentTrades = new();
    private readonly object _lock = new();

    private const int MaxHistoryCount = 1000;
    private const int RecentTradesWindow = 10;

    public BalancePoolSnapshot CurrentSnapshot => _currentSnapshot;
    public BalancePoolSnapshot InitialSnapshot => _initialSnapshot;
    public decimal RealizedPnL => _currentSnapshot.TotalValueUSDT - _initialSnapshot.TotalValueUSDT;
    public decimal CurrentDrawdown => _peakValueUSDT > 0
        ? (_peakValueUSDT - _currentSnapshot.TotalValueUSDT) / _peakValueUSDT * 100
        : 0;
    public decimal MaxDrawdown => _maxDrawdown;

    public event EventHandler<BalanceUpdateEventArgs>? BalanceUpdated;
    public event EventHandler<EmergencyEventArgs>? EmergencyTriggered;
    public event EventHandler<RebalanceEventArgs>? RebalanceRecommended;

    public BalancePoolService(
        IExchangeClient exchangeA,
        IExchangeClient exchangeB,
        ILoggingService logger,
        IConfigService configService,
        ICoinDataService coinDataService)
    {
        _exchangeA = exchangeA ?? throw new ArgumentNullException(nameof(exchangeA));
        _exchangeB = exchangeB ?? throw new ArgumentNullException(nameof(exchangeB));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _coinDataService = coinDataService ?? throw new ArgumentNullException(nameof(coinDataService));
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _logger.LogInfo("BalancePool", "Initializing balance pool...");

        var snapshot = await CreateSnapshotAsync(ct);

        lock (_lock)
        {
            _initialSnapshot = snapshot;
            _currentSnapshot = snapshot;
            _peakValueUSDT = snapshot.TotalValueUSDT;
            _maxDrawdown = 0;
        }

        _history.Enqueue(snapshot);

        _logger.LogInfo("BalancePool",
            $"Balance pool initialized. Total: {snapshot.TotalValueUSDT:F2} USDT");

        LogAssetDetails(snapshot);
    }

    public async Task UpdateBalancesAsync(CancellationToken ct = default)
    {
        try
        {
            var snapshot = await CreateSnapshotAsync(ct);

            lock (_lock)
            {
                _currentSnapshot = snapshot;

                // Update peak and drawdown
                if (snapshot.TotalValueUSDT > _peakValueUSDT)
                {
                    _peakValueUSDT = snapshot.TotalValueUSDT;
                    snapshot.PeakValueUSDT = _peakValueUSDT;
                }

                var currentDrawdown = CurrentDrawdown;
                if (currentDrawdown > _maxDrawdown)
                {
                    _maxDrawdown = currentDrawdown;
                }
            }

            // Store in history
            _history.Enqueue(snapshot);
            while (_history.Count > MaxHistoryCount)
            {
                _history.TryDequeue(out _);
            }

            // Calculate and emit P&L
            var pnl = CalculateRealPnL();
            BalanceUpdated?.Invoke(this, new BalanceUpdateEventArgs(snapshot, pnl));

            // Check emergency conditions
            var emergencyCheck = CheckEmergencyProtection();
            if (emergencyCheck.ShouldTrigger)
            {
                _logger.LogCritical("BalancePool",
                    $"EMERGENCY: {emergencyCheck.Reason} - {emergencyCheck.Message}");
                EmergencyTriggered?.Invoke(this, new EmergencyEventArgs(emergencyCheck));
            }

            // Check rebalance needs
            var rebalance = CalculateRebalance();
            if (rebalance.IsRebalanceNeeded && rebalance.Urgency >= RebalanceUrgency.Medium)
            {
                _logger.LogWarning("BalancePool", $"Rebalance recommended: {rebalance.Summary}");
                RebalanceRecommended?.Invoke(this, new RebalanceEventArgs(rebalance));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("BalancePool", "Failed to update balances", ex);
        }
    }

    public void RecordTrade(TradeResult result)
    {
        _recentTrades.Enqueue(result);
        while (_recentTrades.Count > RecentTradesWindow)
        {
            _recentTrades.TryDequeue(out _);
        }

        _logger.LogInfo("BalancePool",
            $"Trade recorded: {result.Symbol} PnL={result.NetPnL:F4} USDT, " +
            $"Duration={result.DurationMs}ms");
    }

    public BalancePoolPnL CalculateRealPnL()
    {
        var pnl = new BalancePoolPnL();

        lock (_lock)
        {
            var initial = _initialSnapshot;
            var current = _currentSnapshot;

            // Calculate per-asset P&L
            var allAssets = initial.CombinedBalances.Keys
                .Union(current.CombinedBalances.Keys)
                .Distinct();

            foreach (var asset in allAssets)
            {
                var initialBal = initial.CombinedBalances.GetValueOrDefault(asset);
                var currentBal = current.CombinedBalances.GetValueOrDefault(asset);

                var assetPnL = new AssetPnL
                {
                    Asset = asset,
                    InitialBalance = initialBal?.TotalBalance ?? 0,
                    CurrentBalance = currentBal?.TotalBalance ?? 0,
                    ValueChangeUSDT = (currentBal?.ValueUSDT ?? 0) - (initialBal?.ValueUSDT ?? 0)
                };

                pnl.AssetPnLs[asset] = assetPnL;
            }

            // Calculate totals
            pnl.TotalPnLUSDT = current.TotalValueUSDT - initial.TotalValueUSDT;
            pnl.TotalPnLPercent = initial.TotalValueUSDT > 0
                ? (pnl.TotalPnLUSDT / initial.TotalValueUSDT) * 100
                : 0;

            // Exchange breakdown — skip assets with no price data (price = 0)
            // to avoid silently zeroing out P&L for those assets
            var initialA_Value = 0m;
            var initialB_Value = 0m;
            var currentA_Value = 0m;
            var currentB_Value = 0m;

            foreach (var b in initial.CombinedBalances.Values)
            {
                var price = GetAssetPrice(b.Asset);
                if (price <= 0)
                {
                    _logger.LogWarning("BalancePool",
                        $"Skipping {b.Asset} in P&L exchange breakdown (no price data)");
                    continue;
                }
                initialA_Value += b.ExchangeA_Total * price;
                initialB_Value += b.ExchangeB_Total * price;
            }

            foreach (var b in current.CombinedBalances.Values)
            {
                var price = GetAssetPrice(b.Asset);
                if (price <= 0) continue; // Already warned above
                currentA_Value += b.ExchangeA_Total * price;
                currentB_Value += b.ExchangeB_Total * price;
            }

            pnl.ExchangeA_PnLUSDT = currentA_Value - initialA_Value;
            pnl.ExchangeB_PnLUSDT = currentB_Value - initialB_Value;
        }

        return pnl;
    }

    public EmergencyProtectionCheck CheckEmergencyProtection()
    {
        var config = _configService.GetConfig();
        var check = new EmergencyProtectionCheck();

        // Check max drawdown
        if (CurrentDrawdown >= config.Risk.MaxDrawdownPercent)
        {
            check.ShouldTrigger = true;
            check.Reason = EmergencyTriggerReason.MaxDrawdownExceeded;
            check.Message = $"Drawdown {CurrentDrawdown:F2}% exceeds max {config.Risk.MaxDrawdownPercent:F2}%";
            check.CurrentLoss = CurrentDrawdown;
            check.Threshold = config.Risk.MaxDrawdownPercent;
            check.RecommendedAction = EmergencyAction.StopTrading;
            return check;
        }

        // Check max loss
        if (-RealizedPnL >= config.Risk.MaxDailyLoss)
        {
            check.ShouldTrigger = true;
            check.Reason = EmergencyTriggerReason.MaxLossExceeded;
            check.Message = $"Loss {-RealizedPnL:F2} USDT exceeds max {config.Risk.MaxDailyLoss:F2} USDT";
            check.CurrentLoss = -RealizedPnL;
            check.Threshold = config.Risk.MaxDailyLoss;
            check.RecommendedAction = EmergencyAction.StopTrading;
            return check;
        }

        // Check consecutive losses
        var recentTrades = _recentTrades.ToArray();
        var consecutiveLosses = CountConsecutiveLosses(recentTrades);
        if (consecutiveLosses >= config.Risk.MaxConsecutiveLosses)
        {
            check.ShouldTrigger = true;
            check.Reason = EmergencyTriggerReason.ConsecutiveLosses;
            check.Message = $"{consecutiveLosses} consecutive losses (max: {config.Risk.MaxConsecutiveLosses})";
            check.CurrentLoss = consecutiveLosses;
            check.Threshold = config.Risk.MaxConsecutiveLosses;
            check.RecommendedAction = EmergencyAction.PauseTrading;
            return check;
        }

        // Check rapid loss rate (more than 1% in last 5 trades)
        var recentPnL = recentTrades.Sum(t => t.NetPnL);
        var rapidLossThreshold = _initialSnapshot.TotalValueUSDT * 0.01m;
        if (-recentPnL > rapidLossThreshold && recentTrades.Length >= 5)
        {
            check.ShouldTrigger = true;
            check.Reason = EmergencyTriggerReason.RapidLossRate;
            check.Message = $"Rapid loss: {-recentPnL:F2} USDT in last {recentTrades.Length} trades";
            check.CurrentLoss = -recentPnL;
            check.Threshold = rapidLossThreshold;
            check.RecommendedAction = EmergencyAction.PauseTrading;
            return check;
        }

        // Check critical imbalance
        var rebalance = CalculateRebalance();
        if (rebalance.Urgency == RebalanceUrgency.Critical)
        {
            check.ShouldTrigger = true;
            check.Reason = EmergencyTriggerReason.CriticalImbalance;
            check.Message = rebalance.Summary;
            check.RecommendedAction = EmergencyAction.RebalanceImmediate;
            return check;
        }

        return check;
    }

    public AssetPoolStatus GetAssetStatus(string asset)
    {
        lock (_lock)
        {
            var current = _currentSnapshot.CombinedBalances.GetValueOrDefault(asset)
                ?? new CombinedAssetBalance { Asset = asset };
            var initial = _initialSnapshot.CombinedBalances.GetValueOrDefault(asset)
                ?? new CombinedAssetBalance { Asset = asset };

            var pnl = new AssetPnL
            {
                Asset = asset,
                InitialBalance = initial.TotalBalance,
                CurrentBalance = current.TotalBalance,
                ValueChangeUSDT = current.ValueUSDT - initial.ValueUSDT
            };

            // Check for critical imbalance (> 80% on one side)
            var isCritical = current.TotalBalance > 0 &&
                (current.DistributionRatio < 0.2m || current.DistributionRatio > 0.8m);

            return new AssetPoolStatus
            {
                Asset = asset,
                Current = current,
                Initial = initial,
                PnL = pnl,
                IsCriticalImbalance = isCritical,
                RecommendedAction = isCritical
                    ? $"Transfer {asset} from {(current.DistributionRatio > 0.5m ? "A to B" : "B to A")}"
                    : null
            };
        }
    }

    public IReadOnlyDictionary<string, AssetPoolStatus> GetAllAssetStatuses()
    {
        var result = new Dictionary<string, AssetPoolStatus>();

        lock (_lock)
        {
            var allAssets = _currentSnapshot.CombinedBalances.Keys
                .Union(_initialSnapshot.CombinedBalances.Keys)
                .Distinct();

            foreach (var asset in allAssets)
            {
                result[asset] = GetAssetStatus(asset);
            }
        }

        return result;
    }

    public RebalanceRecommendation CalculateRebalance()
    {
        var recommendation = new RebalanceRecommendation();
        var config = _configService.GetConfig();

        lock (_lock)
        {
            foreach (var (asset, balance) in _currentSnapshot.CombinedBalances)
            {
                if (balance.TotalBalance <= 0) continue;

                var ratio = balance.DistributionRatio;
                var targetRatio = 0.5m; // Target 50/50 distribution

                // Check if rebalance needed (> 30% deviation from target)
                if (Math.Abs(ratio - targetRatio) > 0.30m)
                {
                    var transferAmount = balance.TotalBalance * Math.Abs(ratio - targetRatio);
                    var fromExchange = ratio > targetRatio ? "ExchangeA" : "ExchangeB";
                    var toExchange = ratio > targetRatio ? "ExchangeB" : "ExchangeA";

                    recommendation.Actions.Add(new RebalanceAction
                    {
                        Asset = asset,
                        FromExchange = fromExchange,
                        ToExchange = toExchange,
                        Amount = Math.Round(transferAmount, 8),
                        Reason = $"Distribution {ratio:P0} vs target {targetRatio:P0}"
                    });

                    // Determine urgency
                    if (Math.Abs(ratio - targetRatio) > 0.40m)
                    {
                        recommendation.Urgency = RebalanceUrgency.Critical;
                    }
                    else if (Math.Abs(ratio - targetRatio) > 0.35m &&
                        recommendation.Urgency < RebalanceUrgency.High)
                    {
                        recommendation.Urgency = RebalanceUrgency.High;
                    }
                    else if (recommendation.Urgency < RebalanceUrgency.Medium)
                    {
                        recommendation.Urgency = RebalanceUrgency.Medium;
                    }
                }
            }
        }

        recommendation.IsRebalanceNeeded = recommendation.Actions.Count > 0;
        recommendation.Summary = recommendation.IsRebalanceNeeded
            ? $"{recommendation.Actions.Count} asset(s) need rebalancing ({recommendation.Urgency})"
            : "Balances are well distributed";

        return recommendation;
    }

    public IReadOnlyList<BalancePoolSnapshot> GetHistory(int count = 100)
    {
        return _history.TakeLast(count).ToList();
    }

    // Private helpers

    private async Task<BalancePoolSnapshot> CreateSnapshotAsync(CancellationToken ct)
    {
        // Fetch balances in parallel for speed
        var balanceTaskA = _exchangeA.GetBalanceAsync(ct);
        var balanceTaskB = _exchangeB.GetBalanceAsync(ct);

        await Task.WhenAll(balanceTaskA, balanceTaskB);

        var balanceA = balanceTaskA.Result;
        var balanceB = balanceTaskB.Result;

        var snapshot = new BalancePoolSnapshot
        {
            Timestamp = DateTime.UtcNow,
            ExchangeA = balanceA,
            ExchangeB = balanceB
        };

        // Combine balances by asset
        var allAssets = balanceA.Assets.Keys.Union(balanceB.Assets.Keys).Distinct();

        foreach (var asset in allAssets)
        {
            var assetA = balanceA.Assets.GetValueOrDefault(asset);
            var assetB = balanceB.Assets.GetValueOrDefault(asset);
            var price = GetAssetPrice(asset);

            var combined = new CombinedAssetBalance
            {
                Asset = asset,
                ExchangeA_Total = assetA?.Total ?? 0,
                ExchangeA_Available = assetA?.Available ?? 0,
                ExchangeB_Total = assetB?.Total ?? 0,
                ExchangeB_Available = assetB?.Available ?? 0,
            };
            combined.ValueUSDT = combined.TotalBalance * price;

            snapshot.CombinedBalances[asset] = combined;
        }

        snapshot.TotalValueUSDT = snapshot.CombinedBalances.Values.Sum(b => b.ValueUSDT);
        snapshot.PeakValueUSDT = _peakValueUSDT;

        return snapshot;
    }

    private decimal GetAssetPrice(string asset)
    {
        // Stablecoins
        if (asset.Equals("USDT", StringComparison.OrdinalIgnoreCase) ||
            asset.Equals("USDC", StringComparison.OrdinalIgnoreCase) ||
            asset.Equals("BUSD", StringComparison.OrdinalIgnoreCase))
        {
            return 1m;
        }

        // Try to get price from coin data service
        try
        {
            var price = _coinDataService.GetPrice(asset);
            if (price > 0) return price;
        }
        catch
        {
            // Ignore errors
        }

        // No cached price available - log and return 0 to avoid incorrect valuations
        _logger.LogWarning("BalancePool", $"No price data available for {asset}, valuation will be incomplete");
        return 0m;
    }

    private int CountConsecutiveLosses(TradeResult[] trades)
    {
        int count = 0;
        for (int i = trades.Length - 1; i >= 0; i--)
        {
            if (trades[i].NetPnL < 0)
                count++;
            else
                break;
        }
        return count;
    }

    private void LogAssetDetails(BalancePoolSnapshot snapshot)
    {
        foreach (var (asset, balance) in snapshot.CombinedBalances
            .Where(b => b.Value.TotalBalance > 0)
            .OrderByDescending(b => b.Value.ValueUSDT))
        {
            _logger.LogInfo("BalancePool",
                $"  {asset}: A={balance.ExchangeA_Total:F8}, B={balance.ExchangeB_Total:F8}, " +
                $"Total={balance.TotalBalance:F8}, Value={balance.ValueUSDT:F2} USDT");
        }
    }

    #region Dual-Balance Mode Support / รองรับโหมดสองกระเป๋า

    /// <summary>
    /// Check if mode change is recommended based on current balances
    /// ตรวจสอบว่าควรแนะนำให้เปลี่ยนโหมดตามยอดปัจจุบันหรือไม่
    /// </summary>
    public (bool ShouldChange, ArbitrageExecutionMode RecommendedMode, string Reason, string ReasonThai) CheckModeRecommendation(
        string baseAsset,
        string quoteAsset,
        decimal tradeSize)
    {
        var readiness = CheckDualBalanceReadiness(
            _exchangeA.ExchangeName, _exchangeB.ExchangeName,
            baseAsset, quoteAsset, tradeSize);

        // If not ready for Dual-Balance, recommend Transfer Mode
        if (!readiness.IsReady)
        {
            return (true, ArbitrageExecutionMode.Transfer,
                $"Insufficient balance for Dual-Balance: {readiness.NotReadyReason}",
                $"ยอดไม่เพียงพอสำหรับโหมดสองกระเป๋า: {readiness.NotReadyReasonThai}");
        }

        // If balance is highly imbalanced (>70% on one side), suggest rebalancing first
        var baseBalance = _currentSnapshot.CombinedBalances.GetValueOrDefault(baseAsset);
        if (baseBalance != null && baseBalance.TotalBalance > 0)
        {
            var ratio = baseBalance.DistributionRatio;
            if (ratio < 0.3m || ratio > 0.7m)
            {
                return (true, ArbitrageExecutionMode.Transfer,
                    $"Balance imbalanced ({ratio:P0}), consider Transfer Mode for natural rebalancing",
                    $"ยอดไม่สมดุล ({ratio:P0}), แนะนำโหมดโอนจริงเพื่อปรับสมดุลอัตโนมัติ");
            }
        }

        return (false, ArbitrageExecutionMode.DualBalance, "Dual-Balance mode is optimal", "โหมดสองกระเป๋าเหมาะสมที่สุด");
    }

    /// <summary>
    /// Get summary of balance distribution across exchanges for UI display
    /// รับข้อมูลสรุปการกระจายยอดระหว่างกระดานสำหรับแสดง UI
    /// </summary>
    public BalanceDistributionSummary GetBalanceDistributionSummary()
    {
        lock (_lock)
        {
            var summary = new BalanceDistributionSummary
            {
                Timestamp = _currentSnapshot.Timestamp,
                ExchangeAName = _exchangeA.ExchangeName,
                ExchangeBName = _exchangeB.ExchangeName,
                TotalValueUSDT = _currentSnapshot.TotalValueUSDT
            };

            foreach (var (asset, balance) in _currentSnapshot.CombinedBalances
                .Where(b => b.Value.ValueUSDT >= 1m) // Only show assets worth >= $1
                .OrderByDescending(b => b.Value.ValueUSDT))
            {
                summary.Assets.Add(new AssetDistribution
                {
                    Asset = asset,
                    ExchangeAAmount = balance.ExchangeA_Total,
                    ExchangeBAmount = balance.ExchangeB_Total,
                    TotalAmount = balance.TotalBalance,
                    ValueUSDT = balance.ValueUSDT,
                    DistributionRatio = balance.DistributionRatio,
                    IsBalanced = balance.DistributionRatio >= 0.3m && balance.DistributionRatio <= 0.7m
                });

                summary.ExchangeA_ValueUSDT += balance.ExchangeA_Total * GetAssetPrice(asset);
                summary.ExchangeB_ValueUSDT += balance.ExchangeB_Total * GetAssetPrice(asset);
            }

            summary.OverallDistributionRatio = summary.TotalValueUSDT > 0
                ? summary.ExchangeA_ValueUSDT / summary.TotalValueUSDT
                : 0.5m;

            return summary;
        }
    }

    /// <summary>
    /// Check if balances support Dual-Balance mode for a trading pair (interface implementation)
    /// ตรวจสอบว่ายอดรองรับโหมดสองกระเป๋าสำหรับคู่เทรดหรือไม่
    /// </summary>
    public DualBalanceReadiness CheckDualBalanceReadiness(
        string exchangeA,
        string exchangeB,
        string baseAsset,
        string quoteAsset,
        decimal requiredQuoteAmount)
    {
        lock (_lock)
        {
            // Get balances for quote asset (for buy side) and base asset (for sell side)
            var quoteBal = _currentSnapshot.CombinedBalances.GetValueOrDefault(quoteAsset);
            var baseBal = _currentSnapshot.CombinedBalances.GetValueOrDefault(baseAsset);

            // Estimate how much base we need (using price estimation)
            var basePrice = GetAssetPrice(baseAsset);
            var requiredBaseAmount = basePrice > 0 ? requiredQuoteAmount / basePrice : 0;

            // Direction 1: Buy on A, Sell on B
            var dir1_BuyQuote = quoteBal?.ExchangeA_Available ?? 0;
            var dir1_SellBase = baseBal?.ExchangeB_Available ?? 0;
            var dir1_Ready = dir1_BuyQuote >= requiredQuoteAmount && dir1_SellBase >= requiredBaseAmount;

            // Direction 2: Buy on B, Sell on A
            var dir2_BuyQuote = quoteBal?.ExchangeB_Available ?? 0;
            var dir2_SellBase = baseBal?.ExchangeA_Available ?? 0;
            var dir2_Ready = dir2_BuyQuote >= requiredQuoteAmount && dir2_SellBase >= requiredBaseAmount;

            // Use whichever direction has better liquidity (or is ready)
            decimal buySideQuoteAvailable, sellSideBaseAvailable;
            bool isReady;

            if (dir1_Ready && dir2_Ready)
            {
                // Both directions work, pick the one with more headroom
                var dir1_Max = Math.Min(dir1_BuyQuote, dir1_SellBase * basePrice);
                var dir2_Max = Math.Min(dir2_BuyQuote, dir2_SellBase * basePrice);
                if (dir1_Max >= dir2_Max)
                {
                    buySideQuoteAvailable = dir1_BuyQuote;
                    sellSideBaseAvailable = dir1_SellBase;
                }
                else
                {
                    buySideQuoteAvailable = dir2_BuyQuote;
                    sellSideBaseAvailable = dir2_SellBase;
                }
                isReady = true;
            }
            else if (dir1_Ready)
            {
                buySideQuoteAvailable = dir1_BuyQuote;
                sellSideBaseAvailable = dir1_SellBase;
                isReady = true;
            }
            else if (dir2_Ready)
            {
                buySideQuoteAvailable = dir2_BuyQuote;
                sellSideBaseAvailable = dir2_SellBase;
                isReady = true;
            }
            else
            {
                // Neither direction is ready; report the better of the two
                var dir1_Max = Math.Min(dir1_BuyQuote, dir1_SellBase * basePrice);
                var dir2_Max = Math.Min(dir2_BuyQuote, dir2_SellBase * basePrice);
                if (dir1_Max >= dir2_Max)
                {
                    buySideQuoteAvailable = dir1_BuyQuote;
                    sellSideBaseAvailable = dir1_SellBase;
                }
                else
                {
                    buySideQuoteAvailable = dir2_BuyQuote;
                    sellSideBaseAvailable = dir2_SellBase;
                }
                isReady = false;
            }

            return new DualBalanceReadiness
            {
                ExchangeAName = exchangeA,
                ExchangeBName = exchangeB,
                BaseAsset = baseAsset,
                QuoteAsset = quoteAsset,
                BuySideQuoteRequired = requiredQuoteAmount,
                SellSideBaseRequired = requiredBaseAmount,
                BuySideQuoteAvailable = buySideQuoteAvailable,
                SellSideBaseAvailable = sellSideBaseAvailable,
                IsReady = isReady,
                MaxTradeableQuantity = Math.Min(buySideQuoteAvailable, sellSideBaseAvailable * basePrice)
            };
        }
    }

    /// <summary>
    /// Get current balance snapshot for a trading pair
    /// รับสแน็ปช็อตยอดปัจจุบันสำหรับคู่เทรด
    /// </summary>
    public TradingPairBalanceSnapshot GetCurrentBalanceSnapshot(
        string exchangeA,
        string exchangeB,
        string baseAsset,
        string quoteAsset)
    {
        lock (_lock)
        {
            var quoteBal = _currentSnapshot.CombinedBalances.GetValueOrDefault(quoteAsset);
            var baseBal = _currentSnapshot.CombinedBalances.GetValueOrDefault(baseAsset);

            return new TradingPairBalanceSnapshot
            {
                Timestamp = DateTime.UtcNow,
                ExchangeA = exchangeA,
                ExchangeB = exchangeB,
                BaseAsset = baseAsset,
                QuoteAsset = quoteAsset,
                Symbol = $"{baseAsset}/{quoteAsset}",
                ExchangeA_QuoteAvailable = quoteBal?.ExchangeA_Available ?? 0,
                ExchangeA_BaseAvailable = baseBal?.ExchangeA_Available ?? 0,
                ExchangeB_QuoteAvailable = quoteBal?.ExchangeB_Available ?? 0,
                ExchangeB_BaseAvailable = baseBal?.ExchangeB_Available ?? 0,
                CurrentPrice = GetAssetPrice(baseAsset)
            };
        }
    }

    /// <summary>
    /// Calculate real P&L from balance snapshots
    /// คำนวณกำไร/ขาดทุนจริงจากสแน็ปช็อตยอด
    /// </summary>
    public decimal CalculateRealPnLFromSnapshots(
        TradingPairBalanceSnapshot before,
        TradingPairBalanceSnapshot after,
        decimal quotePrice)
    {
        // Calculate total value before (in quote currency)
        var valueBefore = before.ExchangeA_QuoteAvailable + before.ExchangeB_QuoteAvailable
                         + (before.ExchangeA_BaseAvailable + before.ExchangeB_BaseAvailable) * quotePrice;

        // Calculate total value after
        var valueAfter = after.ExchangeA_QuoteAvailable + after.ExchangeB_QuoteAvailable
                        + (after.ExchangeA_BaseAvailable + after.ExchangeB_BaseAvailable) * quotePrice;

        return valueAfter - valueBefore;
    }

    #endregion
}

/// <summary>
/// Summary of balance distribution for UI
/// ข้อมูลสรุปการกระจายยอดสำหรับ UI
/// </summary>
public class BalanceDistributionSummary
{
    public DateTime Timestamp { get; set; }
    public string ExchangeAName { get; set; } = string.Empty;
    public string ExchangeBName { get; set; } = string.Empty;
    public decimal TotalValueUSDT { get; set; }
    public decimal ExchangeA_ValueUSDT { get; set; }
    public decimal ExchangeB_ValueUSDT { get; set; }
    public decimal OverallDistributionRatio { get; set; }
    public List<AssetDistribution> Assets { get; set; } = new();

    public string DistributionDisplay => $"{OverallDistributionRatio * 100:F0}% / {(1 - OverallDistributionRatio) * 100:F0}%";
    public string DistributionDisplayThai => $"{ExchangeAName}: {OverallDistributionRatio * 100:F0}% | {ExchangeBName}: {(1 - OverallDistributionRatio) * 100:F0}%";
}

/// <summary>
/// Individual asset distribution info
/// ข้อมูลการกระจายของแต่ละเหรียญ
/// </summary>
public class AssetDistribution
{
    public string Asset { get; set; } = string.Empty;
    public decimal ExchangeAAmount { get; set; }
    public decimal ExchangeBAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal ValueUSDT { get; set; }
    public decimal DistributionRatio { get; set; }
    public bool IsBalanced { get; set; }
}
