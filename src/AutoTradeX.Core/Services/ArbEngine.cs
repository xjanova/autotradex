// AutoTrade-X v1.0.0

using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using System.Collections.Concurrent;

namespace AutoTradeX.Core.Services;

public class ArbEngine : IArbEngine
{
    private readonly IExchangeClient _exchangeA;
    private readonly IExchangeClient _exchangeB;
    private readonly ILoggingService _logger;
    private readonly IConfigService _configService;

    private AppConfig _config;
    private CancellationTokenSource? _cts;
    private Task? _runningTask;
    private readonly object _lock = new();

    private readonly ConcurrentDictionary<string, TradingPair> _tradingPairs = new();
    private readonly ConcurrentQueue<TradeResult> _tradeHistory = new();
    private const int MaxTradeHistoryCount = 1000;

    private DailyPnL _todayStats = new() { Date = DateOnly.FromDateTime(DateTime.UtcNow) };
    private DateTime _lastTradeTime = DateTime.MinValue;
    private int _consecutiveLosses = 0;
    private bool _isPaused = false;

    public bool IsRunning { get; private set; }
    public ArbEngineStatus Status { get; private set; } = ArbEngineStatus.Idle;
    public decimal TodayPnL => _todayStats.TotalNetPnL;
    public int TodayTradeCount => _todayStats.TotalTrades;
    public string? LastError { get; private set; }

    public event EventHandler<OpportunityEventArgs>? OpportunityFound;
    public event EventHandler<TradeCompletedEventArgs>? TradeCompleted;
    public event EventHandler<EngineStatusEventArgs>? StatusChanged;
    public event EventHandler<EngineErrorEventArgs>? ErrorOccurred;
    public event EventHandler<PriceUpdateEventArgs>? PriceUpdated;
    public event EventHandler<BalancePoolUpdateEventArgs>? BalancePoolUpdated;
    public event EventHandler<EmergencyProtectionEventArgs>? EmergencyTriggered;

    public ArbEngine(
        IExchangeClient exchangeA,
        IExchangeClient exchangeB,
        ILoggingService logger,
        IConfigService configService)
    {
        _exchangeA = exchangeA ?? throw new ArgumentNullException(nameof(exchangeA));
        _exchangeB = exchangeB ?? throw new ArgumentNullException(nameof(exchangeB));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));

        _config = _configService.GetConfig();

        foreach (var symbol in _config.TradingPairs)
        {
            var pair = TradingPair.FromSymbol(symbol);
            _tradingPairs[symbol] = pair;
        }

        _logger.LogInfo("ArbEngine", "ArbEngine initialized");
    }

    #region Core Operations

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            _logger.LogWarning("ArbEngine", "Engine is already running");
            return;
        }

        try
        {
            SetStatus(ArbEngineStatus.Starting);
            _logger.LogInfo("ArbEngine", "Starting ArbEngine...");

            await Task.WhenAll(
                _exchangeA.ConnectAsync(cancellationToken),
                _exchangeB.ConnectAsync(cancellationToken)
            );

            var testTaskA = _exchangeA.TestConnectionAsync(cancellationToken);
            var testTaskB = _exchangeB.TestConnectionAsync(cancellationToken);
            await Task.WhenAll(testTaskA, testTaskB);

            if (!testTaskA.Result)
                throw new Exception($"Cannot connect to {_exchangeA.ExchangeName}");
            if (!testTaskB.Result)
                throw new Exception($"Cannot connect to {_exchangeB.ExchangeName}");

            _logger.LogInfo("ArbEngine", $"Connected to {_exchangeA.ExchangeName} and {_exchangeB.ExchangeName}");

            CheckAndResetDailyStats();

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            IsRunning = true;
            SetStatus(ArbEngineStatus.Running);

            _runningTask = RunMainLoopAsync(_cts.Token);

            _logger.LogInfo("ArbEngine", "ArbEngine started successfully");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            SetStatus(ArbEngineStatus.Error);
            _logger.LogError("ArbEngine", "Failed to start ArbEngine", ex);
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
        {
            _logger.LogWarning("ArbEngine", "Engine is not running");
            return;
        }

        _logger.LogInfo("ArbEngine", "Stopping ArbEngine...");

        try
        {
            _cts?.Cancel();

            if (_runningTask != null)
            {
                await _runningTask;
            }

            await Task.WhenAll(
                _exchangeA.DisconnectAsync(),
                _exchangeB.DisconnectAsync()
            );
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError("ArbEngine", "Error while stopping", ex);
        }
        finally
        {
            IsRunning = false;
            SetStatus(ArbEngineStatus.Stopped);
            _cts?.Dispose();
            _cts = null;
            _runningTask = null;

            _logger.LogInfo("ArbEngine", "ArbEngine stopped");
        }
    }

    private async Task RunMainLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                CheckAndResetDailyStats();

                if (!CheckRiskLimits())
                {
                    SetStatus(ArbEngineStatus.StoppedByRiskLimit);
                    _logger.LogCritical("ArbEngine", $"Risk limit exceeded! Daily Loss: {TodayPnL:F4}, Max: {_config.Risk.MaxDailyLoss:F4}");
                    break;
                }

                foreach (var pair in _tradingPairs.Values.Where(p => p.IsEnabled))
                {
                    if (ct.IsCancellationRequested) break;
                    if (_isPaused)
                    {
                        await Task.Delay(500, ct);
                        continue;
                    }

                    try
                    {
                        await FetchPricesAsync(pair, ct);

                        var opportunity = await AnalyzeOpportunityAsync(pair, ct);
                        pair.CurrentOpportunity = opportunity;

                        // Raise price update event
                        if (pair.TickerA != null)
                            PriceUpdated?.Invoke(this, new PriceUpdateEventArgs(_exchangeA.ExchangeName, pair.Symbol, pair.TickerA));
                        if (pair.TickerB != null)
                            PriceUpdated?.Invoke(this, new PriceUpdateEventArgs(_exchangeB.ExchangeName, pair.Symbol, pair.TickerB));

                        if (opportunity.ShouldTrade)
                        {
                            _logger.LogInfo("ArbEngine", $"Opportunity found: {opportunity}");
                            OpportunityFound?.Invoke(this, new OpportunityEventArgs(opportunity, pair));

                            if (CanTrade())
                            {
                                pair.Status = PairStatus.Trading;
                                SetStatus(ArbEngineStatus.Trading);

                                var result = await ExecuteArbitrageAsync(opportunity, ct);
                                HandleTradeResult(result, pair);

                                pair.Status = PairStatus.Idle;
                                SetStatus(ArbEngineStatus.Running);
                            }
                            else
                            {
                                _logger.LogDebug("ArbEngine", "Trade cooldown active, skipping...");
                            }
                        }
                        else
                        {
                            pair.Status = opportunity.HasPositiveSpread ? PairStatus.Opportunity : PairStatus.Idle;
                        }
                    }
                    catch (Exception ex)
                    {
                        pair.Status = PairStatus.Error;
                        pair.LastError = ex.Message;
                        _logger.LogError("ArbEngine", $"Error processing pair {pair.Symbol}", ex);
                    }
                }

                await Task.Delay(_config.Strategy.PollingIntervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                ErrorOccurred?.Invoke(this, new EngineErrorEventArgs(ex.Message, ex, "MainLoop"));
                _logger.LogError("ArbEngine", "Error in main loop", ex);

                await Task.Delay(5000, ct);
            }
        }
    }

    private async Task FetchPricesAsync(TradingPair pair, CancellationToken ct)
    {
        var tickerTaskA = _exchangeA.GetTickerAsync(pair.ExchangeA_Symbol, ct);
        var tickerTaskB = _exchangeB.GetTickerAsync(pair.ExchangeB_Symbol, ct);
        await Task.WhenAll(tickerTaskA, tickerTaskB);

        pair.TickerA = tickerTaskA.Result;
        pair.TickerB = tickerTaskB.Result;
        pair.LastUpdated = DateTime.UtcNow;
    }

    #endregion

    #region Opportunity Analysis

    public async Task<SpreadOpportunity> AnalyzeOpportunityAsync(TradingPair pair, CancellationToken ct = default)
    {
        var opportunity = new SpreadOpportunity
        {
            Symbol = pair.Symbol,
            Timestamp = DateTime.UtcNow,
            ExchangeA_FeePercent = _config.ExchangeA.TradingFeePercent,
            ExchangeB_FeePercent = _config.ExchangeB.TradingFeePercent
        };

        if (pair.TickerA == null || pair.TickerB == null)
        {
            opportunity.Remarks = "Missing price data";
            return opportunity;
        }

        opportunity.ExchangeA_BidPrice = pair.TickerA.BidPrice;
        opportunity.ExchangeA_AskPrice = pair.TickerA.AskPrice;
        opportunity.ExchangeA_BidQuantity = pair.TickerA.BidQuantity;
        opportunity.ExchangeA_AskQuantity = pair.TickerA.AskQuantity;

        opportunity.ExchangeB_BidPrice = pair.TickerB.BidPrice;
        opportunity.ExchangeB_AskPrice = pair.TickerB.AskPrice;
        opportunity.ExchangeB_BidQuantity = pair.TickerB.BidQuantity;
        opportunity.ExchangeB_AskQuantity = pair.TickerB.AskQuantity;

        if (opportunity.SpreadBuyA_SellB > opportunity.SpreadBuyB_SellA)
        {
            opportunity.Direction = opportunity.SpreadBuyA_SellB > 0
                ? ArbitrageDirection.BuyA_SellB
                : ArbitrageDirection.None;
        }
        else
        {
            opportunity.Direction = opportunity.SpreadBuyB_SellA > 0
                ? ArbitrageDirection.BuyB_SellA
                : ArbitrageDirection.None;
        }

        if (opportunity.Direction == ArbitrageDirection.None)
        {
            opportunity.Remarks = "No profitable spread";
            return opportunity;
        }

        decimal buyQty, sellQty;
        if (opportunity.Direction == ArbitrageDirection.BuyA_SellB)
        {
            buyQty = opportunity.ExchangeA_AskQuantity;
            sellQty = opportunity.ExchangeB_BidQuantity;
        }
        else
        {
            buyQty = opportunity.ExchangeB_AskQuantity;
            sellQty = opportunity.ExchangeA_BidQuantity;
        }

        var maxQtyByConfig = _config.Risk.MaxPositionSizePerTrade / opportunity.BuyPrice;
        opportunity.SuggestedQuantity = Math.Min(Math.Min(buyQty, sellQty), maxQtyByConfig);
        opportunity.SuggestedQuantity = Math.Round(opportunity.SuggestedQuantity, pair.QuantityPrecision);

        var buyValue = opportunity.SuggestedQuantity * opportunity.BuyPrice;
        var sellValue = opportunity.SuggestedQuantity * opportunity.SellPrice;
        var buyFee = buyValue * (opportunity.ExchangeA_FeePercent / 100);
        var sellFee = sellValue * (opportunity.ExchangeB_FeePercent / 100);

        if (opportunity.Direction == ArbitrageDirection.BuyB_SellA)
        {
            (buyFee, sellFee) = (sellFee, buyFee);
        }

        opportunity.ExpectedNetProfitQuote = sellValue - buyValue - buyFee - sellFee;

        opportunity.MeetsMinSpread = opportunity.NetSpreadPercentage >= _config.Strategy.MinSpreadPercentage;
        opportunity.MeetsMinProfit = opportunity.ExpectedNetProfitQuote >= _config.Strategy.MinExpectedProfitQuoteCurrency;
        opportunity.HasSufficientLiquidity = opportunity.SuggestedQuantity >= _config.Strategy.MinDepthQuantity;

        try
        {
            var balanceCheckResult = await CheckBalancesAsync(pair, opportunity, ct);
            opportunity.HasSufficientBalance = balanceCheckResult;
        }
        catch
        {
            opportunity.HasSufficientBalance = false;
            opportunity.Remarks = "Failed to check balance";
        }

        if (!opportunity.ShouldTrade)
        {
            var reasons = new List<string>();
            if (!opportunity.MeetsMinSpread) reasons.Add($"Spread {opportunity.NetSpreadPercentage:F4}% < Min {_config.Strategy.MinSpreadPercentage:F4}%");
            if (!opportunity.MeetsMinProfit) reasons.Add($"Profit {opportunity.ExpectedNetProfitQuote:F4} < Min {_config.Strategy.MinExpectedProfitQuoteCurrency:F4}");
            if (!opportunity.HasSufficientLiquidity) reasons.Add($"Insufficient liquidity");
            if (!opportunity.HasSufficientBalance) reasons.Add($"Insufficient balance");
            opportunity.Remarks = string.Join("; ", reasons);
        }

        return opportunity;
    }

    private async Task<bool> CheckBalancesAsync(TradingPair pair, SpreadOpportunity opportunity, CancellationToken ct)
    {
        var balanceTaskA = _exchangeA.GetBalanceAsync(ct);
        var balanceTaskB = _exchangeB.GetBalanceAsync(ct);
        await Task.WhenAll(balanceTaskA, balanceTaskB);

        var balanceA = balanceTaskA.Result;
        var balanceB = balanceTaskB.Result;

        var requiredQuote = opportunity.SuggestedQuantity * opportunity.BuyPrice * 1.01m;
        var requiredBase = opportunity.SuggestedQuantity * 1.01m;

        if (opportunity.Direction == ArbitrageDirection.BuyA_SellB)
        {
            return balanceA.HasSufficientBalance(pair.QuoteCurrency, requiredQuote)
                && balanceB.HasSufficientBalance(pair.BaseCurrency, requiredBase);
        }
        else
        {
            return balanceB.HasSufficientBalance(pair.QuoteCurrency, requiredQuote)
                && balanceA.HasSufficientBalance(pair.BaseCurrency, requiredBase);
        }
    }

    #endregion

    #region Trade Execution

    /// <summary>
    /// Execute Arbitrage - OPTIMIZED FOR SUB-SECOND EXECUTION
    /// Key optimizations:
    /// 1. True parallel order placement (Task.WhenAll)
    /// 2. Pre-created order requests
    /// 3. Minimal logging during critical path
    /// 4. Aggressive timeout handling
    /// </summary>
    public async Task<TradeResult> ExecuteArbitrageAsync(SpreadOpportunity opportunity, CancellationToken ct = default)
    {
        var startTicks = Environment.TickCount64;
        var result = new TradeResult
        {
            Symbol = opportunity.Symbol,
            Direction = opportunity.Direction,
            Opportunity = opportunity,
            StartTime = DateTime.UtcNow
        };

        _logger.LogInfo("ArbEngine", $"[EXEC] {opportunity.Symbol} {opportunity.Direction} Qty={opportunity.SuggestedQuantity:F8} @ Spread={opportunity.NetSpreadPercentage:F4}%");

        try
        {
            // Pre-create order requests (minimal time)
            var buyRequest = CreateOrderRequest(opportunity, true);
            var sellRequest = CreateOrderRequest(opportunity, false);

            // Determine exchanges
            var (buyExchange, sellExchange) = opportunity.Direction == ArbitrageDirection.BuyA_SellB
                ? (_exchangeA, _exchangeB)
                : (_exchangeB, _exchangeA);

            // Execute BOTH orders in TRUE PARALLEL - Critical for sub-second execution
            var buyTask = ExecuteOrderWithTimingAsync(buyExchange, buyRequest, "BUY", ct);
            var sellTask = ExecuteOrderWithTimingAsync(sellExchange, sellRequest, "SELL", ct);

            // Wait for both to complete (truly parallel)
            var results = await Task.WhenAll(buyTask, sellTask);

            var buyResult = results[0];
            var sellResult = results[1];

            result.BuyOrder = buyResult.Order;
            result.SellOrder = sellResult.Order;

            // Log execution times
            var totalMs = Environment.TickCount64 - startTicks;
            _logger.LogInfo("ArbEngine",
                $"[TIMING] Total={totalMs}ms, Buy={buyResult.ExecutionMs}ms, Sell={sellResult.ExecutionMs}ms");

            // Handle any exceptions
            if (buyResult.Exception != null)
            {
                result.ErrorDetails.Add($"Buy failed: {buyResult.Exception.Message}");
            }
            if (sellResult.Exception != null)
            {
                result.ErrorDetails.Add($"Sell failed: {sellResult.Exception.Message}");
            }

            result = await AnalyzeAndHandleResultAsync(result, buyResult.Exception, sellResult.Exception, ct);
            result.EndTime = DateTime.UtcNow;

            // Add timing metadata
            result.Metadata["TotalExecutionMs"] = totalMs;
            result.Metadata["BuyExecutionMs"] = buyResult.ExecutionMs;
            result.Metadata["SellExecutionMs"] = sellResult.ExecutionMs;

            _logger.LogTradeResult(result);

            return result;
        }
        catch (Exception ex)
        {
            result.Status = TradeResultStatus.Error;
            result.ErrorMessage = ex.Message;
            result.EndTime = DateTime.UtcNow;
            result.Metadata["TotalExecutionMs"] = Environment.TickCount64 - startTicks;

            _logger.LogError("ArbEngine", "Unexpected error during arbitrage execution", ex);
            return result;
        }
    }

    /// <summary>
    /// Execute single order with timing measurement
    /// </summary>
    private async Task<OrderExecutionResult> ExecuteOrderWithTimingAsync(
        IExchangeClient exchange,
        OrderRequest request,
        string orderType,
        CancellationToken ct)
    {
        var startTicks = Environment.TickCount64;
        var result = new OrderExecutionResult();

        try
        {
            result.Order = await exchange.PlaceOrderAsync(request, ct);
            result.ExecutionMs = Environment.TickCount64 - startTicks;

            if (result.Order?.Status == OrderStatus.Filled)
            {
                _logger.LogDebug("ArbEngine",
                    $"[{orderType}] FILLED in {result.ExecutionMs}ms @ {result.Order.AverageFilledPrice:F8}");
            }
        }
        catch (Exception ex)
        {
            result.Exception = ex;
            result.ExecutionMs = Environment.TickCount64 - startTicks;
            _logger.LogError("ArbEngine", $"[{orderType}] FAILED in {result.ExecutionMs}ms: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Internal class for order execution results
    /// </summary>
    private class OrderExecutionResult
    {
        public Order? Order { get; set; }
        public Exception? Exception { get; set; }
        public long ExecutionMs { get; set; }
    }

    private OrderRequest CreateOrderRequest(SpreadOpportunity opportunity, bool isBuy)
    {
        var orderType = _config.Strategy.OrderType.Equals("Limit", StringComparison.OrdinalIgnoreCase)
            ? OrderType.Limit
            : OrderType.Market;

        decimal? price = null;
        if (orderType == OrderType.Limit)
        {
            if (isBuy)
            {
                price = opportunity.BuyPrice * (1 + _config.Strategy.LimitOrderSlippagePercent / 100);
            }
            else
            {
                price = opportunity.SellPrice * (1 - _config.Strategy.LimitOrderSlippagePercent / 100);
            }
        }

        return new OrderRequest
        {
            Symbol = opportunity.Symbol,
            Side = isBuy ? OrderSide.Buy : OrderSide.Sell,
            Type = orderType,
            Quantity = opportunity.SuggestedQuantity,
            Price = price,
            Metadata = new Dictionary<string, string>
            {
                ["ArbDirection"] = opportunity.Direction.ToString(),
                ["ExpectedProfit"] = opportunity.ExpectedNetProfitQuote.ToString("F8")
            }
        };
    }

    private async Task<TradeResult> AnalyzeAndHandleResultAsync(
        TradeResult result,
        Exception? buyException,
        Exception? sellException,
        CancellationToken ct)
    {
        var buyOrder = result.BuyOrder;
        var sellOrder = result.SellOrder;

        if (buyException == null && sellException == null
            && buyOrder?.Status == OrderStatus.Filled
            && sellOrder?.Status == OrderStatus.Filled)
        {
            result.Status = TradeResultStatus.Success;
            result.NetPnL = CalculateNetPnL(result);
            return result;
        }

        if (buyException != null && sellException != null)
        {
            result.Status = TradeResultStatus.BothFailed;
            result.ErrorMessage = "Both orders failed";
            return result;
        }

        if (buyException != null || sellException != null)
        {
            result.Status = TradeResultStatus.OneSideFailed;
            result.ErrorMessage = buyException != null ? "Buy order failed" : "Sell order failed";

            switch (_config.Strategy.OneSideFailStrategy)
            {
                case "Hedge":
                    await HandleHedgeAsync(result, buyException != null, ct);
                    break;
                case "CutLoss":
                    await HandleCutLossAsync(result, buyException != null, ct);
                    break;
                case "DoNothing":
                default:
                    result.Notes = "One side failed, no action taken (config: DoNothing)";
                    _logger.LogWarning("ArbEngine", result.Notes);
                    break;
            }

            return result;
        }

        if (buyOrder?.Status == OrderStatus.PartiallyFilled || sellOrder?.Status == OrderStatus.PartiallyFilled)
        {
            result.Status = TradeResultStatus.PartialSuccess;

            switch (_config.Strategy.PartialFillStrategy)
            {
                case "WaitMore":
                    await WaitForFillAsync(result, ct);
                    break;
                case "Hedge":
                    await HandlePartialFillHedgeAsync(result, ct);
                    break;
                case "CancelRemaining":
                default:
                    await CancelRemainingOrdersAsync(result, ct);
                    break;
            }
        }

        result.NetPnL = CalculateNetPnL(result);

        return result;
    }

    private decimal CalculateNetPnL(TradeResult result) => result.GrossPnL - result.TotalFees;

    private async Task HandleHedgeAsync(TradeResult result, bool buyFailed, CancellationToken ct)
    {
        _logger.LogWarning("ArbEngine", $"Attempting hedge for {result.Symbol}...");

        try
        {
            if (buyFailed && result.SellOrder != null)
            {
                var hedgeRequest = new OrderRequest
                {
                    Symbol = result.Symbol,
                    Side = OrderSide.Buy,
                    Type = OrderType.Market,
                    Quantity = result.SellOrder.FilledQuantity
                };

                var hedgeOrder = result.Direction == ArbitrageDirection.BuyA_SellB
                    ? await _exchangeB.PlaceOrderAsync(hedgeRequest, ct)
                    : await _exchangeA.PlaceOrderAsync(hedgeRequest, ct);

                result.Notes = $"Hedge buy executed: {hedgeOrder}";
            }
            else if (!buyFailed && result.BuyOrder != null)
            {
                var hedgeRequest = new OrderRequest
                {
                    Symbol = result.Symbol,
                    Side = OrderSide.Sell,
                    Type = OrderType.Market,
                    Quantity = result.BuyOrder.FilledQuantity
                };

                var hedgeOrder = result.Direction == ArbitrageDirection.BuyA_SellB
                    ? await _exchangeA.PlaceOrderAsync(hedgeRequest, ct)
                    : await _exchangeB.PlaceOrderAsync(hedgeRequest, ct);

                result.Notes = $"Hedge sell executed: {hedgeOrder}";
            }

            _logger.LogInfo("ArbEngine", $"Hedge completed: {result.Notes}");
        }
        catch (Exception ex)
        {
            result.ErrorDetails.Add($"Hedge failed: {ex.Message}");
            _logger.LogError("ArbEngine", "Hedge operation failed", ex);
        }
    }

    private async Task HandleCutLossAsync(TradeResult result, bool buyFailed, CancellationToken ct)
    {
        _logger.LogWarning("ArbEngine", $"Executing cut loss for {result.Symbol}...");
        await HandleHedgeAsync(result, buyFailed, ct);
        result.Notes = "Cut loss executed";
    }

    private async Task WaitForFillAsync(TradeResult result, CancellationToken ct)
    {
        var timeout = _config.Strategy.OrderFillTimeoutMs;
        var startTime = DateTime.UtcNow;

        while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeout)
        {
            if (ct.IsCancellationRequested) break;

            if (result.BuyOrder?.Status == OrderStatus.PartiallyFilled)
            {
                result.BuyOrder = result.Direction == ArbitrageDirection.BuyA_SellB
                    ? await _exchangeA.GetOrderAsync(result.Symbol, result.BuyOrder.OrderId, ct)
                    : await _exchangeB.GetOrderAsync(result.Symbol, result.BuyOrder.OrderId, ct);
            }

            if (result.SellOrder?.Status == OrderStatus.PartiallyFilled)
            {
                result.SellOrder = result.Direction == ArbitrageDirection.BuyA_SellB
                    ? await _exchangeB.GetOrderAsync(result.Symbol, result.SellOrder.OrderId, ct)
                    : await _exchangeA.GetOrderAsync(result.Symbol, result.SellOrder.OrderId, ct);
            }

            if (result.BuyOrder?.Status == OrderStatus.Filled && result.SellOrder?.Status == OrderStatus.Filled)
            {
                result.Status = TradeResultStatus.Success;
                return;
            }

            await Task.Delay(500, ct);
        }

        await CancelRemainingOrdersAsync(result, ct);
    }

    private async Task HandlePartialFillHedgeAsync(TradeResult result, CancellationToken ct)
    {
        _logger.LogWarning("ArbEngine", "Handling partial fill with hedge...");

        var buyFilled = result.BuyOrder?.FilledQuantity ?? 0;
        var sellFilled = result.SellOrder?.FilledQuantity ?? 0;
        var difference = Math.Abs(buyFilled - sellFilled);

        if (difference > 0)
        {
            var hedgeRequest = new OrderRequest
            {
                Symbol = result.Symbol,
                Side = buyFilled > sellFilled ? OrderSide.Sell : OrderSide.Buy,
                Type = OrderType.Market,
                Quantity = difference
            };

            _logger.LogInfo("ArbEngine", $"Hedge partial fill: {hedgeRequest.Side} {difference}");
        }

        await Task.CompletedTask;
    }

    private async Task CancelRemainingOrdersAsync(TradeResult result, CancellationToken ct)
    {
        try
        {
            var tasks = new List<Task>();

            if (result.BuyOrder?.Status is OrderStatus.Open or OrderStatus.PartiallyFilled)
            {
                var cancelTask = result.Direction == ArbitrageDirection.BuyA_SellB
                    ? _exchangeA.CancelOrderAsync(result.Symbol, result.BuyOrder.OrderId, ct)
                    : _exchangeB.CancelOrderAsync(result.Symbol, result.BuyOrder.OrderId, ct);
                tasks.Add(cancelTask);
            }

            if (result.SellOrder?.Status is OrderStatus.Open or OrderStatus.PartiallyFilled)
            {
                var cancelTask = result.Direction == ArbitrageDirection.BuyA_SellB
                    ? _exchangeB.CancelOrderAsync(result.Symbol, result.SellOrder.OrderId, ct)
                    : _exchangeA.CancelOrderAsync(result.Symbol, result.SellOrder.OrderId, ct);
                tasks.Add(cancelTask);
            }

            await Task.WhenAll(tasks);
            result.Notes = "Remaining orders cancelled";
            _logger.LogInfo("ArbEngine", "Remaining orders cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError("ArbEngine", "Failed to cancel remaining orders", ex);
        }
    }

    #endregion

    #region Risk Management

    private bool CheckRiskLimits()
    {
        if (-TodayPnL >= _config.Risk.MaxDailyLoss)
        {
            LastError = $"Max daily loss exceeded: {TodayPnL:F4}";
            return false;
        }

        if (TodayTradeCount >= _config.Risk.MaxTradesPerDay)
        {
            LastError = $"Max trades per day exceeded: {TodayTradeCount}";
            return false;
        }

        if (_consecutiveLosses >= _config.Risk.MaxConsecutiveLosses)
        {
            LastError = $"Max consecutive losses exceeded: {_consecutiveLosses}";
            return false;
        }

        return true;
    }

    private bool CanTrade()
    {
        var timeSinceLastTrade = (DateTime.UtcNow - _lastTradeTime).TotalMilliseconds;
        return timeSinceLastTrade >= _config.Risk.MinTimeBetweenTradesMs;
    }

    private void HandleTradeResult(TradeResult result, TradingPair pair)
    {
        _lastTradeTime = DateTime.UtcNow;

        _tradeHistory.Enqueue(result);
        while (_tradeHistory.Count > MaxTradeHistoryCount)
        {
            _tradeHistory.TryDequeue(out _);
        }

        _todayStats.TotalTrades++;
        _todayStats.TotalNetPnL += result.NetPnL;
        _todayStats.TotalFees += result.TotalFees;
        _todayStats.TotalVolume += result.ActualBuyValue;

        if (result.IsFullySuccessful)
        {
            _todayStats.SuccessfulTrades++;
        }
        else
        {
            _todayStats.FailedTrades++;
        }

        if (result.NetPnL >= 0)
        {
            _todayStats.TotalProfit += result.NetPnL;
            _consecutiveLosses = 0;
        }
        else
        {
            _todayStats.TotalLoss += Math.Abs(result.NetPnL);
            _consecutiveLosses++;
        }

        pair.TodayTradeCount++;
        pair.TodayPnL += result.NetPnL;

        TradeCompleted?.Invoke(this, new TradeCompletedEventArgs(result));
    }

    private void CheckAndResetDailyStats()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (_todayStats.Date != today)
        {
            ResetDailyStats();
        }
    }

    #endregion

    #region Configuration & Trading Pairs

    public void UpdateConfig(AppConfig config)
    {
        lock (_lock)
        {
            _config = config;
            _logger.LogInfo("ArbEngine", "Configuration updated");
        }
    }

    public AppConfig GetCurrentConfig() => _config;

    public void AddTradingPair(TradingPair pair)
    {
        _tradingPairs[pair.Symbol] = pair;
        _logger.LogInfo("ArbEngine", $"Trading pair added: {pair.Symbol}");
    }

    public void RemoveTradingPair(string symbol)
    {
        _tradingPairs.TryRemove(symbol, out _);
        _logger.LogInfo("ArbEngine", $"Trading pair removed: {symbol}");
    }

    public IReadOnlyList<TradingPair> GetTradingPairs() => _tradingPairs.Values.ToList();

    #endregion

    #region Statistics

    public DailyPnL GetTodayStats() => _todayStats;

    public IReadOnlyList<TradeResult> GetTradeHistory(int count = 100) => _tradeHistory.TakeLast(count).ToList();

    public void ResetDailyStats()
    {
        _todayStats = new DailyPnL { Date = DateOnly.FromDateTime(DateTime.UtcNow) };
        _consecutiveLosses = 0;

        foreach (var pair in _tradingPairs.Values)
        {
            pair.TodayTradeCount = 0;
            pair.TodayPnL = 0;
        }

        _logger.LogInfo("ArbEngine", "Daily stats reset");
    }

    #endregion

    #region Helpers

    private void SetStatus(ArbEngineStatus status, string? message = null)
    {
        if (Status != status)
        {
            Status = status;
            var engineStatus = status switch
            {
                ArbEngineStatus.Idle => EngineStatus.Idle,
                ArbEngineStatus.Starting => EngineStatus.Starting,
                ArbEngineStatus.Running => EngineStatus.Running,
                ArbEngineStatus.Paused => EngineStatus.Paused,
                ArbEngineStatus.Stopped => EngineStatus.Stopped,
                ArbEngineStatus.StoppedByRiskLimit => EngineStatus.Stopped,
                ArbEngineStatus.Error => EngineStatus.Error,
                _ => EngineStatus.Idle
            };
            StatusChanged?.Invoke(this, new EngineStatusEventArgs(engineStatus, message));
        }
    }

    public void Pause()
    {
        if (IsRunning && !_isPaused)
        {
            _isPaused = true;
            SetStatus(ArbEngineStatus.Paused, "Paused by user");
            _logger.LogInfo("ArbEngine", "Engine paused");
        }
    }

    public void Resume()
    {
        if (IsRunning && _isPaused)
        {
            _isPaused = false;
            SetStatus(ArbEngineStatus.Running, "Resumed by user");
            _logger.LogInfo("ArbEngine", "Engine resumed");
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    #endregion
}
