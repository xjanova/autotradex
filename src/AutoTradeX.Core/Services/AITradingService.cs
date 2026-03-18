/*
 * ============================================================================
 * AutoTrade-X - AI Trading Service Implementation
 * ============================================================================
 * AI-powered single-exchange trading with smart strategies
 * ============================================================================
 */

using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;

namespace AutoTradeX.Core.Services;

/// <summary>
/// AI Trading Service - Implements AI-powered trading strategies
/// </summary>
public class AITradingService : IAITradingService
{
    private readonly IExchangeClientFactory _exchangeFactory;
    private readonly ILoggingService _logger;
    private readonly object _lock = new();

    private bool _isRunning;
    private bool _isPaused;
    private CancellationTokenSource? _cts;
    private Task? _tradingLoop;

    private string? _activeExchange;
    private string? _activeSymbol;
    private AIStrategyConfig? _config;
    private AITradingPosition? _currentPosition;
    private AITradingSessionStats _sessionStats = new();
    private readonly List<AITradeResult> _tradeHistory = new();
    // Candle cache keyed by (exchange|symbol|interval)
    private readonly Dictionary<string, (List<PriceCandle> candles, DateTime fetchedAt)> _candleCache = new();
    private AIMarketData? _lastMarketData;

    // Risk management state
    private decimal _dailyPnL;
    private DateTime _dailyPnLResetDate = DateTime.UtcNow.Date;
    private int _consecutiveLosses;
    private DateTime? _pausedUntil;
    private int _tradesThisHour;
    private DateTime _hourlyTradesResetTime = DateTime.UtcNow;

    public bool IsRunning => _isRunning && !_isPaused;
    public string? ActiveExchange => _activeExchange;
    public string? ActiveSymbol => _activeSymbol;

    public event EventHandler<AISignalEventArgs>? SignalGenerated;
    public event EventHandler<AIPositionEventArgs>? PositionOpened;
    public event EventHandler<AIPositionEventArgs>? PositionClosed;
    public event EventHandler<AITradeEventArgs>? TradeCompleted;
    public event EventHandler<AIMarketDataEventArgs>? MarketDataUpdated;
    public event EventHandler<AIEmergencyEventArgs>? EmergencyTriggered;

    public AITradingService(IExchangeClientFactory exchangeFactory, ILoggingService logger)
    {
        _exchangeFactory = exchangeFactory;
        _logger = logger;
    }

    public async Task StartAsync(string exchange, string symbol, AIStrategyConfig config, CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            await StopAsync();
        }

        lock (_lock)
        {
            _activeExchange = exchange;
            _activeSymbol = symbol;
            _config = config;
            _isRunning = true;
            _isPaused = false;
            _sessionStats = new AITradingSessionStats();
            _tradeHistory.Clear();
            _cts = new CancellationTokenSource();
        }

        _logger.LogInfo("AITradingService", $"Starting AI trading: {exchange} - {symbol}");

        // Start the trading loop
        _tradingLoop = Task.Run(async () => await TradingLoopAsync(_cts.Token), cancellationToken);
    }

    public async Task StopAsync()
    {
        if (!_isRunning) return;

        _logger.LogInfo("AITradingService", "Stopping AI trading...");

        _cts?.Cancel();

        if (_tradingLoop != null)
        {
            try
            {
                await _tradingLoop;
            }
            catch (OperationCanceledException) { }
        }

        lock (_lock)
        {
            _isRunning = false;
            _isPaused = false;
            _sessionStats.SessionEnd = DateTime.UtcNow;
        }

        _logger.LogInfo("AITradingService", "AI trading stopped");
    }

    public void Pause()
    {
        _isPaused = true;
        _logger.LogInfo("AITradingService", "AI trading paused");
    }

    public void Resume()
    {
        _isPaused = false;
        _pausedUntil = null;
        _logger.LogInfo("AITradingService", "AI trading resumed");
    }

    public async Task EmergencyStopAsync()
    {
        _logger.LogCritical("AITradingService", "EMERGENCY STOP triggered!");

        EmergencyTriggered?.Invoke(this, new AIEmergencyEventArgs
        {
            Reason = "Emergency stop activated by user",
            LossAmount = _currentPosition?.UnrealizedPnL,
            LossPercent = _currentPosition?.UnrealizedPnLPercent
        });

        // Close any open position immediately
        if (_currentPosition != null && _currentPosition.Status == AIPositionStatus.InPosition)
        {
            await ClosePositionAsync("EmergencyStop");
        }

        await StopAsync();
    }

    public async Task<AIMarketData?> GetMarketDataAsync(string exchange, string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _exchangeFactory.CreateRealClient(exchange);

            // Bitkub uses "BTC/THB" format internally; other exchanges use "BTCUSDT"
            var isBitkub = exchange.Equals("bitkub", StringComparison.OrdinalIgnoreCase);
            var tickerSymbol = isBitkub ? symbol : symbol.Replace("/", "");

            var ticker = await client.GetTickerAsync(tickerSymbol, cancellationToken);
            if (ticker == null) return null;

            // Get candles for indicator calculation (1m candles for technical indicators)
            var candles = await GetCandlesAsync(exchange, symbol, "1m", 200, cancellationToken);

            // Get 1h candles for 24h price data (24 candles = 24 hours)
            var candles1h = await GetCandlesAsync(exchange, symbol, "1h", 25, cancellationToken);

            // Calculate 24h price change, high, low from hourly candles
            decimal priceChange24h = 0;
            decimal priceChangePercent24h = 0;
            decimal high24h = ticker.LastPrice;
            decimal low24h = ticker.LastPrice;
            decimal volumeChange24h = 0;

            if (candles1h.Count >= 2)
            {
                // Price 24h ago = open of first candle in 24h window
                var price24hAgo = candles1h[0].Open;
                priceChange24h = ticker.LastPrice - price24hAgo;
                priceChangePercent24h = price24hAgo > 0 ? (priceChange24h / price24hAgo) * 100 : 0;

                // High/Low 24h from hourly candles
                high24h = candles1h.Max(c => c.High);
                low24h = candles1h.Min(c => c.Low);

                // Volume change: compare recent 12h volume vs previous 12h volume
                if (candles1h.Count >= 24)
                {
                    var recentVolume = candles1h.TakeLast(12).Sum(c => c.Volume);
                    var previousVolume = candles1h.Take(12).Sum(c => c.Volume);
                    volumeChange24h = previousVolume > 0
                        ? ((recentVolume - previousVolume) / previousVolume) * 100
                        : 0;
                }
            }
            else if (candles.Count >= 2)
            {
                // Fallback: use 1m candles if hourly not available
                var oldestCandle = candles[0];
                priceChange24h = ticker.LastPrice - oldestCandle.Open;
                priceChangePercent24h = oldestCandle.Open > 0 ? (priceChange24h / oldestCandle.Open) * 100 : 0;
                high24h = candles.Max(c => c.High);
                low24h = candles.Min(c => c.Low);
            }

            var marketData = new AIMarketData
            {
                Symbol = symbol,
                Exchange = exchange,
                Timestamp = DateTime.UtcNow,
                CurrentPrice = ticker.LastPrice,
                BidPrice = ticker.BidPrice,
                AskPrice = ticker.AskPrice,
                Spread = ticker.AskPrice - ticker.BidPrice,
                SpreadPercent = ticker.BidPrice > 0 ? (ticker.AskPrice - ticker.BidPrice) / ticker.BidPrice * 100 : 0,
                Volume24h = ticker.Volume24h,
                PriceChange24h = priceChange24h,
                PriceChangePercent24h = priceChangePercent24h,
                High24h = high24h,
                Low24h = low24h,
                VolumeChange24h = volumeChange24h,
                RecentCandles = candles.TakeLast(50).ToList()
            };

            // Calculate indicators
            if (candles.Count >= 14)
            {
                marketData.RSI = CalculateRSI(candles, 14);
            }

            if (candles.Count >= 26)
            {
                var (macd, signal, histogram) = CalculateMACD(candles);
                marketData.MACD = macd;
                marketData.MACDSignal = signal;
                marketData.MACDHistogram = histogram;
            }

            if (candles.Count >= 9)
            {
                marketData.EMA9 = CalculateEMA(candles, 9);
            }

            if (candles.Count >= 21)
            {
                marketData.EMA21 = CalculateEMA(candles, 21);
            }

            if (candles.Count >= 50)
            {
                marketData.SMA50 = CalculateSMA(candles, 50);
            }

            if (candles.Count >= 200)
            {
                marketData.SMA200 = CalculateSMA(candles, 200);
            }

            if (candles.Count >= 20)
            {
                var (upper, middle, lower) = CalculateBollingerBands(candles, 20);
                marketData.BollingerUpper = upper;
                marketData.BollingerMiddle = middle;
                marketData.BollingerLower = lower;
            }

            if (candles.Count >= 14)
            {
                marketData.ATR = CalculateATR(candles, 14);
                marketData.Volatility = ticker.LastPrice > 0 ? marketData.ATR / ticker.LastPrice * 100 : 0;
            }

            _lastMarketData = marketData;
            MarketDataUpdated?.Invoke(this, new AIMarketDataEventArgs { Data = marketData });

            return marketData;
        }
        catch (Exception ex)
        {
            _logger.LogError("AITradingService", $"Error getting market data: {ex.Message}");
            return null;
        }
    }

    public async Task<AITradingSignal?> GetCurrentSignalAsync(string exchange, string symbol, AIStrategyConfig config, CancellationToken cancellationToken = default)
    {
        var marketData = await GetMarketDataAsync(exchange, symbol, cancellationToken);
        if (marketData == null) return null;

        return GenerateSignal(marketData, config);
    }

    public AITradingPosition? GetCurrentPosition() => _currentPosition;

    public AITradingSessionStats GetSessionStats() => _sessionStats;

    public List<AITradeResult> GetTradeHistory() => _tradeHistory.ToList();

    public async Task<AITradeResult?> ExecuteManualTradeAsync(AITradingSignal signal, decimal amount, CancellationToken cancellationToken = default)
    {
        if (_activeExchange == null || _activeSymbol == null)
        {
            _logger.LogError("AITradingService", "Cannot execute trade: no active exchange/symbol");
            return null;
        }

        try
        {
            var client = _exchangeFactory.CreateClient(_activeExchange);
            var symbolClean = _activeSymbol.Replace("/", "");

            // Calculate quantity
            var ticker = await client.GetTickerAsync(symbolClean, cancellationToken);
            if (ticker == null) return null;

            // Apply percentage of balance if configured
            var tradeAmount = amount;
            if (_config != null && _config.UsePercentageOfBalance && _config.BalancePercentage > 0)
            {
                try
                {
                    // Try USDT first, then THB for Bitkub
                    var isBitkub = _activeExchange.Equals("bitkub", StringComparison.OrdinalIgnoreCase);
                    var quoteAsset = isBitkub ? "THB" : "USDT";
                    var assetBalance = await client.GetAssetBalanceAsync(quoteAsset, cancellationToken);
                    if (assetBalance != null && assetBalance.Available > 0)
                    {
                        tradeAmount = assetBalance.Available * (_config.BalancePercentage / 100m);
                        _logger.LogInfo("AITradingService", $"Using {_config.BalancePercentage}% of balance: {tradeAmount:F2} {quoteAsset}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("AITradingService", $"Could not get balance for percentage calculation, using fixed amount: {ex.Message}");
                }
            }

            var quantity = tradeAmount / ticker.LastPrice;

            // Place buy order
            var order = await client.PlaceOrderAsync(new OrderRequest
            {
                Symbol = symbolClean,
                Side = OrderSide.Buy,
                Type = _config?.UseMarketOrders == true ? OrderType.Market : OrderType.Limit,
                Quantity = quantity,
                Price = _config?.UseMarketOrders == true ? null : ticker.AskPrice
            }, cancellationToken);

            if (order.Status == OrderStatus.Filled || order.Status == OrderStatus.PartiallyFilled)
            {
                // Use actual filled quantity (may be less than requested for partial fills)
                var filledQty = order.FilledQuantity > 0 ? order.FilledQuantity : quantity;
                var entryPrice = order.AverageFilledPrice > 0 ? order.AverageFilledPrice.Value : ticker.LastPrice;

                // Create position
                _currentPosition = new AITradingPosition
                {
                    Symbol = _activeSymbol,
                    Exchange = _activeExchange,
                    Status = AIPositionStatus.InPosition,
                    EntryTime = DateTime.UtcNow,
                    EntryPrice = entryPrice,
                    CurrentPrice = ticker.LastPrice,
                    Size = filledQty,
                    Value = filledQty * entryPrice,
                    TakeProfitPrice = signal.TargetPrice,
                    StopLossPrice = signal.StopLossPrice,
                    Strategy = _config?.Mode ?? AITradingMode.Scalping,
                    OrderIds = new List<string> { order.OrderId }
                };

                PositionOpened?.Invoke(this, new AIPositionEventArgs { Position = _currentPosition });
                _logger.LogInfo("AITradingService", $"Position opened: {_currentPosition.Size} @ ${_currentPosition.EntryPrice:F2}");
            }

            return null; // Trade result will be returned when position is closed
        }
        catch (Exception ex)
        {
            _logger.LogError("AITradingService", $"Error executing manual trade: {ex.Message}");
            return null;
        }
    }

    public async Task<AITradeResult?> ClosePositionAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        if (_currentPosition == null || _activeExchange == null)
        {
            return null;
        }

        try
        {
            var client = _exchangeFactory.CreateClient(_activeExchange);
            var symbolClean = _currentPosition.Symbol.Replace("/", "");

            // Place sell order
            var order = await client.PlaceOrderAsync(new OrderRequest
            {
                Symbol = symbolClean,
                Side = OrderSide.Sell,
                Type = _config?.UseMarketOrders == true ? OrderType.Market : OrderType.Limit,
                Quantity = _currentPosition.Size,
                Price = _config?.UseMarketOrders == true ? null : _currentPosition.CurrentPrice
            }, cancellationToken);

            if (order.Status == OrderStatus.Filled || order.Status == OrderStatus.PartiallyFilled)
            {
                var exitPrice = order.AverageFilledPrice > 0 ? order.AverageFilledPrice.Value : _currentPosition.CurrentPrice;
                var grossPnL = (_currentPosition.Size * exitPrice) - (_currentPosition.Size * _currentPosition.EntryPrice);
                // Use actual order fees if available, otherwise estimate from config or exchange-specific defaults
                var actualFees = order.Fee;
                decimal fees;
                if (actualFees > 0)
                {
                    // Use actual reported fee + estimated entry fee
                    var entryFeeRate = _config?.FeePercent > 0 ? _config.FeePercent / 100m : 0.001m;
                    fees = actualFees + (_currentPosition.Size * _currentPosition.EntryPrice * entryFeeRate);
                }
                else
                {
                    // Fallback: estimate both sides. Use config FeePercent (per-exchange) or default 0.1%
                    var feeRate = _config?.FeePercent > 0 ? _config.FeePercent / 100m : 0.001m;
                    fees = (_currentPosition.Size * _currentPosition.EntryPrice * feeRate) + (_currentPosition.Size * exitPrice * feeRate);
                }
                var netPnL = grossPnL - fees;

                var result = new AITradeResult
                {
                    PositionId = _currentPosition.Id,
                    Symbol = _currentPosition.Symbol,
                    Exchange = _currentPosition.Exchange,
                    Strategy = _currentPosition.Strategy,
                    EntryTime = _currentPosition.EntryTime ?? DateTime.UtcNow,
                    ExitTime = DateTime.UtcNow,
                    EntryPrice = _currentPosition.EntryPrice,
                    ExitPrice = exitPrice,
                    Size = _currentPosition.Size,
                    GrossPnL = grossPnL,
                    Fees = fees,
                    NetPnL = netPnL,
                    PnLPercent = _currentPosition.EntryPrice > 0 ? netPnL / (_currentPosition.Size * _currentPosition.EntryPrice) * 100 : 0,
                    ExitReason = reason ?? "Manual"
                };

                // Update stats
                UpdateSessionStats(result);

                // Add to history
                _tradeHistory.Add(result);

                // Fire events
                PositionClosed?.Invoke(this, new AIPositionEventArgs { Position = _currentPosition });
                TradeCompleted?.Invoke(this, new AITradeEventArgs { Trade = result });

                _logger.LogInfo("AITradingService", $"Position closed: PnL ${result.NetPnL:F2} ({result.PnLPercent:F2}%) - {result.ExitReason}");

                _currentPosition = null;
                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("AITradingService", $"Error closing position: {ex.Message}");
        }

        return null;
    }

    public void UpdateConfig(AIStrategyConfig config)
    {
        _config = config;
        _logger.LogInfo("AITradingService", "Configuration updated");
    }

    public async Task<List<PriceCandle>> GetCandlesAsync(string exchange, string symbol, string interval = "1m", int limit = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check cache using composite key (exchange|symbol|interval)
            var cacheKey = $"{exchange}|{symbol}|{interval}".ToLowerInvariant();
            if (_candleCache.TryGetValue(cacheKey, out var cached) &&
                cached.candles.Count >= limit &&
                (DateTime.UtcNow - cached.fetchedAt).TotalSeconds < 30) // Cache valid for 30 seconds
            {
                return cached.candles.TakeLast(limit).ToList();
            }

            var client = _exchangeFactory.CreateRealClient(exchange);

            // Bitkub uses "BTC/THB" format internally; other exchanges use "BTCUSDT"
            var isBitkub = exchange.Equals("bitkub", StringComparison.OrdinalIgnoreCase);
            var tickerSymbol = isBitkub ? symbol : symbol.Replace("/", "");

            // Try to fetch real klines from exchange API
            var candles = await client.GetKlinesAsync(tickerSymbol, interval, limit, cancellationToken);

            // Fallback to simulated candles if exchange doesn't support klines
            if (candles == null || candles.Count == 0)
            {
                _logger.LogWarning("AITradingService", $"No klines data from {exchange}, generating simulated candles");
                candles = await GenerateSimulatedCandlesAsync(client, tickerSymbol, interval, limit, cancellationToken);
            }
            else
            {
                _logger.LogInfo("AITradingService", $"Got {candles.Count} real candles from {exchange} for {symbol} ({interval})");
            }

            // Cache candles with composite key
            _candleCache[cacheKey] = (candles, DateTime.UtcNow);

            // Cleanup old cache entries (keep max 10)
            if (_candleCache.Count > 10)
            {
                var oldestKey = _candleCache.OrderBy(kvp => kvp.Value.fetchedAt).First().Key;
                _candleCache.Remove(oldestKey);
            }

            return candles;
        }
        catch (Exception ex)
        {
            _logger.LogError("AITradingService", $"Error getting candles: {ex.Message}");
            return new List<PriceCandle>();
        }
    }

    /// <summary>
    /// Generate simulated candles as fallback when exchange doesn't support klines API
    /// สร้างแท่งเทียนจำลองเมื่อ exchange ไม่รองรับ klines API
    /// </summary>
    private async Task<List<PriceCandle>> GenerateSimulatedCandlesAsync(
        IExchangeClient client, string tickerSymbol, string interval, int limit,
        CancellationToken cancellationToken)
    {
        var candles = new List<PriceCandle>();
        var ticker = await client.GetTickerAsync(tickerSymbol, cancellationToken);
        if (ticker == null) return candles;

        var basePrice = ticker.LastPrice;
        var random = new Random((int)(DateTime.UtcNow.Ticks % int.MaxValue));

        var timeStep = interval switch
        {
            "1m" => TimeSpan.FromMinutes(1),
            "5m" => TimeSpan.FromMinutes(5),
            "15m" => TimeSpan.FromMinutes(15),
            "1h" => TimeSpan.FromHours(1),
            "4h" => TimeSpan.FromHours(4),
            "1d" => TimeSpan.FromDays(1),
            _ => TimeSpan.FromMinutes(1)
        };

        var volatilityMultiplier = interval switch
        {
            "1m" => 0.005m,
            "5m" => 0.01m,
            "15m" => 0.015m,
            "1h" => 0.025m,
            "4h" => 0.04m,
            "1d" => 0.08m,
            _ => 0.01m
        };

        for (int i = limit; i > 0; i--)
        {
            var time = DateTime.UtcNow - (timeStep * i);
            var volatility = basePrice * volatilityMultiplier;
            var change = (decimal)(random.NextDouble() - 0.5) * 2 * volatility;

            var open = basePrice + change;
            var close = open + (decimal)(random.NextDouble() - 0.5) * volatility;
            var high = Math.Max(open, close) + (decimal)random.NextDouble() * volatility * 0.3m;
            var low = Math.Min(open, close) - (decimal)random.NextDouble() * volatility * 0.3m;

            candles.Add(new PriceCandle
            {
                Time = time, Open = open, High = high, Low = low, Close = close,
                Volume = (decimal)(random.NextDouble() * 1000000)
            });

            basePrice = close;
        }

        return candles;
    }

    private async Task TradingLoopAsync(CancellationToken cancellationToken)
    {
        int consecutiveErrors = 0;
        const int maxBackoffSeconds = 60;

        while (!cancellationToken.IsCancellationRequested && _isRunning)
        {
            try
            {
                // Check if paused
                if (_isPaused || (_pausedUntil.HasValue && DateTime.UtcNow < _pausedUntil.Value))
                {
                    await Task.Delay(1000, cancellationToken);
                    continue;
                }

                // Check trading hours
                if (_config != null && _config.TradingHoursEnabled)
                {
                    var currentTime = DateTime.UtcNow.TimeOfDay;
                    bool isInTradingHours;
                    if (_config.TradingStartTime <= _config.TradingEndTime)
                        isInTradingHours = currentTime >= _config.TradingStartTime && currentTime < _config.TradingEndTime;
                    else // Wraps around midnight
                        isInTradingHours = currentTime >= _config.TradingStartTime || currentTime < _config.TradingEndTime;

                    if (!isInTradingHours)
                    {
                        await Task.Delay(30000, cancellationToken); // Check every 30s outside trading hours
                        continue;
                    }
                }

                // Reset daily PnL if new day
                if (DateTime.UtcNow.Date > _dailyPnLResetDate)
                {
                    _dailyPnL = 0;
                    _dailyPnLResetDate = DateTime.UtcNow.Date;
                }

                // Reset hourly trades
                if (DateTime.UtcNow > _hourlyTradesResetTime.AddHours(1))
                {
                    _tradesThisHour = 0;
                    _hourlyTradesResetTime = DateTime.UtcNow;
                }

                // Check risk management
                if (!CheckRiskManagement())
                {
                    await Task.Delay(5000, cancellationToken);
                    continue;
                }

                // Get market data and signal
                if (_activeExchange != null && _activeSymbol != null && _config != null)
                {
                    _logger.LogInfo("AITradingService", $"Trading loop: Getting signal for {_activeExchange}:{_activeSymbol}");
                    var signal = await GetCurrentSignalAsync(_activeExchange, _activeSymbol, _config, cancellationToken);

                    if (signal != null)
                    {
                        _logger.LogInfo("AITradingService", $"Signal generated: {signal.SignalType} confidence={signal.Confidence:P0}");
                        SignalGenerated?.Invoke(this, new AISignalEventArgs { Signal = signal });

                        // Update current position if exists
                        if (_currentPosition != null && _lastMarketData != null)
                        {
                            await UpdatePositionAsync(_lastMarketData, cancellationToken);
                        }

                        // Check for entry
                        if (_currentPosition == null && signal.SignalType == "Buy" && signal.Confidence >= _config.MinConfidenceToEnter)
                        {
                            await ExecuteManualTradeAsync(signal, _config.TradeAmountUSDT, cancellationToken);
                        }
                    }
                }

                // Reset error counter on successful iteration
                consecutiveErrors = 0;

                // Wait before next iteration
                await Task.Delay(2000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveErrors++;
                var backoffSeconds = Math.Min(maxBackoffSeconds, 5 * (int)Math.Pow(2, Math.Min(consecutiveErrors - 1, 4)));
                _logger.LogError("AITradingService", $"Error in trading loop (attempt {consecutiveErrors}): {ex.Message}. Retrying in {backoffSeconds}s");
                await Task.Delay(backoffSeconds * 1000, cancellationToken);
            }
        }
    }

    private async Task UpdatePositionAsync(AIMarketData marketData, CancellationToken cancellationToken)
    {
        var position = _currentPosition;
        var config = _config;
        if (position == null || config == null) return;

        // Update all position fields atomically using local reference
        var currentPrice = marketData.CurrentPrice;
        var positionValue = position.Size * currentPrice;
        var unrealizedPnL = (position.Size * currentPrice) - (position.Size * position.EntryPrice);
        var unrealizedPnLPercent = position.EntryPrice > 0 && position.Size > 0
            ? (unrealizedPnL / (position.Size * position.EntryPrice)) * 100
            : 0;

        position.CurrentPrice = currentPrice;
        position.Value = positionValue;
        position.UnrealizedPnL = unrealizedPnL;
        position.UnrealizedPnLPercent = unrealizedPnLPercent;

        // Check take profit
        if (position.TakeProfitPrice.HasValue && currentPrice >= position.TakeProfitPrice.Value)
        {
            await ClosePositionAsync("TakeProfit", cancellationToken);
            return;
        }

        // Check stop loss
        if (position.StopLossPrice.HasValue && currentPrice <= position.StopLossPrice.Value)
        {
            await ClosePositionAsync("StopLoss", cancellationToken);
            return;
        }

        // Check trailing stop
        if (config.EnableTrailingStop && unrealizedPnLPercent >= config.TrailingStopActivationPercent)
        {
            var trailingStopPrice = currentPrice * (1 - config.TrailingStopDistancePercent / 100);
            if (position.StopLossPrice == null || trailingStopPrice > position.StopLossPrice)
            {
                position.StopLossPrice = trailingStopPrice;
                position.TrailingStopPercent = config.TrailingStopDistancePercent;
            }
        }

        // Check max hold time
        if (position.EntryTime.HasValue && config.MaxHoldTimeMinutes > 0)
        {
            var holdTime = DateTime.UtcNow - position.EntryTime.Value;
            if (holdTime.TotalMinutes >= config.MaxHoldTimeMinutes)
            {
                await ClosePositionAsync("Timeout", cancellationToken);
                return;
            }
        }

        // Check emergency stop
        if (config.EnableEmergencyStop && unrealizedPnLPercent <= -config.EmergencyStopLossPercent)
        {
            EmergencyTriggered?.Invoke(this, new AIEmergencyEventArgs
            {
                Reason = "Emergency stop loss triggered",
                LossAmount = unrealizedPnL,
                LossPercent = unrealizedPnLPercent
            });

            if (config.AutoCloseOnEmergency)
            {
                await ClosePositionAsync("Emergency", cancellationToken);
            }
        }

        _sessionStats.CurrentUnrealizedPnL = unrealizedPnL;
    }

    private bool CheckRiskManagement()
    {
        if (_config == null) return true;

        // Check daily loss limit
        if (_dailyPnL <= -_config.MaxDailyLossUSDT)
        {
            _logger.LogWarning("AITradingService", $"Daily loss limit reached: ${_dailyPnL:F2}");
            return false;
        }

        // Check consecutive losses
        if (_consecutiveLosses >= _config.MaxConsecutiveLosses)
        {
            if (!_pausedUntil.HasValue || DateTime.UtcNow >= _pausedUntil.Value)
            {
                _pausedUntil = DateTime.UtcNow.AddMinutes(_config.PauseAfterLossesMinutes);
                _logger.LogWarning("AITradingService", $"Max consecutive losses reached. Pausing until {_pausedUntil.Value:HH:mm:ss}");
            }
            return false;
        }

        // Check max trades per hour
        if (_tradesThisHour >= _config.MaxTradesPerHour)
        {
            return false;
        }

        // Check max drawdown
        if (_sessionStats.MaxDrawdownPercent >= _config.MaxDrawdownPercent)
        {
            _logger.LogWarning("AITradingService", $"Max drawdown reached: {_sessionStats.MaxDrawdownPercent:F2}%");
            return false;
        }

        return true;
    }

    private void UpdateSessionStats(AITradeResult trade)
    {
        _sessionStats.TotalTrades++;
        _sessionStats.TotalRealizedPnL += trade.NetPnL;
        _sessionStats.TotalFees += trade.Fees;
        _dailyPnL += trade.NetPnL;
        _tradesThisHour++;

        if (trade.IsWin)
        {
            _sessionStats.WinningTrades++;
            _sessionStats.GrossProfit += trade.NetPnL;
            _sessionStats.CurrentConsecutiveWins++;
            _sessionStats.CurrentConsecutiveLosses = 0;
            _consecutiveLosses = 0;

            if (trade.NetPnL > _sessionStats.LargestWin)
                _sessionStats.LargestWin = trade.NetPnL;

            if (_sessionStats.CurrentConsecutiveWins > _sessionStats.MaxConsecutiveWins)
                _sessionStats.MaxConsecutiveWins = _sessionStats.CurrentConsecutiveWins;
        }
        else
        {
            _sessionStats.LosingTrades++;
            _sessionStats.GrossLoss += Math.Abs(trade.NetPnL);
            _sessionStats.CurrentConsecutiveLosses++;
            _sessionStats.CurrentConsecutiveWins = 0;
            _consecutiveLosses++;

            if (trade.NetPnL < _sessionStats.LargestLoss)
                _sessionStats.LargestLoss = trade.NetPnL;

            if (_sessionStats.CurrentConsecutiveLosses > _sessionStats.MaxConsecutiveLosses)
                _sessionStats.MaxConsecutiveLosses = _sessionStats.CurrentConsecutiveLosses;
        }

        // Track max drawdown
        if (_sessionStats.TotalRealizedPnL > _sessionStats.PeakPnL)
            _sessionStats.PeakPnL = _sessionStats.TotalRealizedPnL;

        var currentDrawdown = _sessionStats.PeakPnL - _sessionStats.TotalRealizedPnL;
        // Use initial capital as base for drawdown % when PeakPnL is zero (e.g., first trade is a loss)
        var drawdownBase = Math.Abs(_sessionStats.PeakPnL);
        var drawdownPercent = drawdownBase > 0
            ? currentDrawdown / drawdownBase * 100
            : (currentDrawdown > 0 ? 100 : 0); // If peak is 0 and we have drawdown, it's 100%
        if (drawdownPercent > _sessionStats.MaxDrawdownPercent)
            _sessionStats.MaxDrawdownPercent = drawdownPercent;

        // Update average trade duration (guard against division by zero)
        if (_sessionStats.TotalTrades > 0)
        {
            var totalDuration = _tradeHistory.Sum(t => t.Duration.TotalMinutes);
            _sessionStats.AverageTradeDurationMinutes = totalDuration / _sessionStats.TotalTrades;
        }
    }

    private AITradingSignal GenerateSignal(AIMarketData marketData, AIStrategyConfig config)
    {
        // Route to strategy-specific signal generation
        return config.Mode switch
        {
            AITradingMode.Scalping => GenerateScalpingSignal(marketData, config),
            AITradingMode.Momentum => GenerateMomentumSignal(marketData, config),
            AITradingMode.MeanReversion => GenerateMeanReversionSignal(marketData, config),
            AITradingMode.GridTrading => GenerateGridTradingSignal(marketData, config),
            AITradingMode.Breakout => GenerateBreakoutSignal(marketData, config),
            AITradingMode.SmartDCA => GenerateSmartDCASignal(marketData, config),
            _ => GenerateScalpingSignal(marketData, config)
        };
    }

    /// <summary>
    /// Scalping Strategy: Quick trades with small profits
    /// Uses RSI oversold/overbought + tight spreads + high volume
    /// เทรดเร็ว กำไรน้อยแต่บ่อย ใช้ RSI สุดขั้ว + Spread แคบ + Volume สูง
    /// </summary>
    private AITradingSignal GenerateScalpingSignal(AIMarketData marketData, AIStrategyConfig config)
    {
        var signal = CreateBaseSignal(marketData);
        var indicators = new List<IndicatorValue>();
        int bullishScore = 0;
        int bearishScore = 0;

        // Scalping focuses on RSI extremes (primary indicator - weighted heavily)
        if (marketData.RSI.HasValue)
        {
            var rsiStatus = marketData.RSI < 25 ? "Very Oversold" : marketData.RSI < 35 ? "Oversold" :
                           marketData.RSI > 75 ? "Very Overbought" : marketData.RSI > 65 ? "Overbought" : "Neutral";
            indicators.Add(new IndicatorValue
            {
                Name = "RSI (Scalping)",
                ShortName = "RSI",
                Value = marketData.RSI.Value,
                Status = rsiStatus.Contains("Oversold") ? "Bullish" : rsiStatus.Contains("Overbought") ? "Bearish" : "Neutral",
                Description = $"RSI {marketData.RSI.Value:F1} - {rsiStatus} | สกัลปิงใช้ RSI สุดขั้วเพื่อหาจุดกลับตัว"
            });

            if (marketData.RSI < 25) bullishScore += 40; // Very strong buy signal
            else if (marketData.RSI < 35) bullishScore += 25;
            else if (marketData.RSI > 75) bearishScore += 40;
            else if (marketData.RSI > 65) bearishScore += 25;
        }

        // Spread check - scalping needs tight spread
        if (marketData.SpreadPercent < 0.1m)
        {
            indicators.Add(new IndicatorValue
            {
                Name = "Spread",
                ShortName = "SPR",
                Value = marketData.SpreadPercent,
                Status = "Bullish",
                Description = $"Spread {marketData.SpreadPercent:F3}% - เหมาะสำหรับ Scalping (แคบดี)"
            });
            bullishScore += 15;
        }
        else if (marketData.SpreadPercent > 0.2m)
        {
            indicators.Add(new IndicatorValue
            {
                Name = "Spread",
                ShortName = "SPR",
                Value = marketData.SpreadPercent,
                Status = "Bearish",
                Description = $"Spread {marketData.SpreadPercent:F3}% - กว้างเกินไปสำหรับ Scalping"
            });
            bearishScore += 20;
        }

        // Volume confirmation
        if (marketData.Volume24h > 0)
        {
            indicators.Add(new IndicatorValue
            {
                Name = "Volume",
                ShortName = "VOL",
                Value = marketData.Volume24h,
                Status = "Neutral",
                Description = $"Volume 24h: {marketData.Volume24h:N0} | Scalping ต้องการ Volume สูงเพื่อ entry/exit ได้เร็ว"
            });
        }

        signal.Indicators = indicators;

        // Scalping needs strong signals due to tight margins
        var netScore = bullishScore - bearishScore;
        var confidence = Math.Min(100, Math.Abs(netScore) * 1.5);

        if (netScore >= 30)
        {
            signal.SignalType = "Buy";
            signal.Confidence = (int)confidence;
            signal.Strength = GetSignalStrength(confidence);
            signal.RecommendedEntryPrice = marketData.AskPrice;
            var tpPercent = config.TakeProfitPercent > 0 ? config.TakeProfitPercent : 0.3m;
            var slPercent = config.StopLossPercent > 0 ? config.StopLossPercent : 0.2m;
            signal.TargetPrice = marketData.CurrentPrice * (1 + tpPercent / 100);
            signal.StopLossPrice = marketData.CurrentPrice * (1 - slPercent / 100);
            signal.ExpectedProfitPercent = tpPercent;
            signal.RiskRewardRatio = slPercent > 0 ? tpPercent / slPercent : 1.5m;
            signal.EstimatedHoldTimeMinutes = 5; // Very short hold
            signal.Reasoning = $"[Scalping] RSI สุดขั้ว ({marketData.RSI:F0}) บ่งชี้จุดกลับตัว | Spread แคบเหมาะสม | เป้าหมาย +0.3% ใน 5 นาที";
        }
        else if (netScore <= -30)
        {
            signal.SignalType = "Sell";
            signal.Confidence = (int)confidence;
            signal.Strength = GetSignalStrength(confidence);
            signal.Reasoning = $"[Scalping] RSI Overbought ({marketData.RSI:F0}) | รอจังหวะ RSI ลงก่อนเข้าซื้อ";
        }
        else
        {
            signal.SignalType = "Hold";
            signal.Confidence = 0;
            signal.Strength = AISignalStrength.None;
            signal.Reasoning = "[Scalping] RSI ยังไม่ถึงจุดสุดขั้ว | รอสัญญาณชัดเจนกว่านี้";
        }

        return signal;
    }

    /// <summary>
    /// Momentum Strategy: Follow the trend
    /// Uses EMA crossover + MACD + price above/below SMA50
    /// ตามเทรนด์ ใช้ EMA crossover + MACD + ราคาเทียบ SMA50
    /// </summary>
    private AITradingSignal GenerateMomentumSignal(AIMarketData marketData, AIStrategyConfig config)
    {
        var signal = CreateBaseSignal(marketData);
        var indicators = new List<IndicatorValue>();
        int bullishScore = 0;
        int bearishScore = 0;

        // EMA Crossover - Primary momentum indicator
        if (marketData.EMA9.HasValue && marketData.EMA21.HasValue)
        {
            var emaBullish = marketData.EMA9 > marketData.EMA21;
            var emaDistance = Math.Abs(marketData.EMA9.Value - marketData.EMA21.Value) / marketData.CurrentPrice * 100;
            indicators.Add(new IndicatorValue
            {
                Name = "EMA Crossover (9/21)",
                ShortName = "EMA",
                Value = marketData.EMA9.Value - marketData.EMA21.Value,
                Status = emaBullish ? "Bullish" : "Bearish",
                Description = emaBullish ? $"EMA9 > EMA21 (Uptrend) ห่าง {emaDistance:F2}% | โมเมนตัมขาขึ้น"
                            : $"EMA9 < EMA21 (Downtrend) ห่าง {emaDistance:F2}% | โมเมนตัมขาลง"
            });

            if (emaBullish) bullishScore += 35;
            else bearishScore += 35;
        }

        // MACD - Momentum confirmation
        if (marketData.MACD.HasValue && marketData.MACDSignal.HasValue)
        {
            var macdBullish = marketData.MACD > marketData.MACDSignal;
            var histogramStrength = Math.Abs(marketData.MACDHistogram ?? 0);
            indicators.Add(new IndicatorValue
            {
                Name = "MACD Momentum",
                ShortName = "MACD",
                Value = marketData.MACDHistogram ?? 0,
                Status = macdBullish ? "Bullish" : "Bearish",
                Description = macdBullish ? $"MACD Bullish | Histogram: +{histogramStrength:F4} | แรงส่งขาขึ้น"
                            : $"MACD Bearish | Histogram: -{histogramStrength:F4} | แรงส่งขาลง"
            });

            if (macdBullish) bullishScore += 30;
            else bearishScore += 30;
        }

        // SMA50 - Trend filter
        if (marketData.SMA50.HasValue)
        {
            var aboveSMA = marketData.CurrentPrice > marketData.SMA50;
            var distanceFromSMA = (marketData.CurrentPrice - marketData.SMA50.Value) / marketData.SMA50.Value * 100;
            indicators.Add(new IndicatorValue
            {
                Name = "Price vs SMA50",
                ShortName = "SMA50",
                Value = marketData.SMA50.Value,
                Status = aboveSMA ? "Bullish" : "Bearish",
                Description = aboveSMA ? $"ราคาอยู่เหนือ SMA50 ({distanceFromSMA:F2}%) | เทรนด์ขาขึ้น"
                            : $"ราคาอยู่ใต้ SMA50 ({distanceFromSMA:F2}%) | เทรนด์ขาลง"
            });

            if (aboveSMA) bullishScore += 20;
            else bearishScore += 20;
        }

        // ATR for volatility-based targets
        if (marketData.ATR.HasValue)
        {
            indicators.Add(new IndicatorValue
            {
                Name = "ATR (Volatility)",
                ShortName = "ATR",
                Value = marketData.ATR.Value,
                Status = "Neutral",
                Description = $"ATR: {marketData.ATR.Value:F2} | ใช้คำนวณเป้าหมายและ Stop Loss"
            });
        }

        signal.Indicators = indicators;

        var netScore = bullishScore - bearishScore;
        var confidence = Math.Min(100, Math.Abs(netScore) * 1.2);

        if (netScore >= 40)
        {
            signal.SignalType = "Buy";
            signal.Confidence = (int)confidence;
            signal.Strength = GetSignalStrength(confidence);
            signal.RecommendedEntryPrice = marketData.AskPrice;
            var atrMultiple = (marketData.ATR ?? 0) > 0 ? marketData.ATR!.Value : marketData.CurrentPrice * 0.02m;
            signal.TargetPrice = marketData.CurrentPrice + (atrMultiple * 2); // 2x ATR target
            signal.StopLossPrice = marketData.CurrentPrice - atrMultiple; // 1x ATR stop
            signal.ExpectedProfitPercent = marketData.CurrentPrice > 0
                ? (atrMultiple * 2) / marketData.CurrentPrice * 100 : 0;
            signal.RiskRewardRatio = 2.0m;
            signal.EstimatedHoldTimeMinutes = 30; // Medium hold
            signal.Reasoning = $"[Momentum] เทรนด์ขาขึ้นชัดเจน | EMA9 > EMA21 | MACD Bullish | เป้าหมาย 2x ATR";
        }
        else if (netScore <= -40)
        {
            signal.SignalType = "Sell";
            signal.Confidence = (int)confidence;
            signal.Strength = GetSignalStrength(confidence);
            signal.Reasoning = $"[Momentum] เทรนด์ขาลง | EMA9 < EMA21 | MACD Bearish | ไม่ควรเข้าซื้อ";
        }
        else
        {
            signal.SignalType = "Hold";
            signal.Confidence = 0;
            signal.Strength = AISignalStrength.None;
            signal.Reasoning = "[Momentum] เทรนด์ไม่ชัดเจน | EMA และ MACD ไม่สอดคล้องกัน | รอสัญญาณยืนยัน";
        }

        return signal;
    }

    /// <summary>
    /// Mean Reversion Strategy: Buy low, sell high
    /// Uses Bollinger Bands + RSI extremes to find reversal points
    /// ซื้อถูกขายแพง ใช้ Bollinger Bands + RSI สุดขั้วหาจุดกลับตัว
    /// </summary>
    private AITradingSignal GenerateMeanReversionSignal(AIMarketData marketData, AIStrategyConfig config)
    {
        var signal = CreateBaseSignal(marketData);
        var indicators = new List<IndicatorValue>();
        int bullishScore = 0;
        int bearishScore = 0;

        // Bollinger Bands - Primary mean reversion indicator
        if (marketData.BollingerLower.HasValue && marketData.BollingerUpper.HasValue && marketData.BollingerMiddle.HasValue)
        {
            var bbRange = marketData.BollingerUpper.Value - marketData.BollingerLower.Value;
            var bbWidth = marketData.BollingerMiddle.Value > 0
                ? bbRange / marketData.BollingerMiddle.Value * 100 : 0;
            var pricePosition = bbRange > 0
                ? (marketData.CurrentPrice - marketData.BollingerLower.Value) / bbRange * 100
                : 50m; // Default to middle if bands are collapsed

            if (marketData.CurrentPrice <= marketData.BollingerLower.Value * 1.005m)
            {
                bullishScore += 45; // Strong reversal signal
                indicators.Add(new IndicatorValue
                {
                    Name = "Bollinger Bands",
                    ShortName = "BB",
                    Value = pricePosition,
                    Status = "Bullish",
                    Description = $"ราคาแตะ BB Lower | เบี่ยงเบนมาก ({pricePosition:F0}%) | โอกาสกลับตัวขึ้นสูง"
                });
            }
            else if (marketData.CurrentPrice >= marketData.BollingerUpper.Value * 0.995m)
            {
                bearishScore += 45;
                indicators.Add(new IndicatorValue
                {
                    Name = "Bollinger Bands",
                    ShortName = "BB",
                    Value = pricePosition,
                    Status = "Bearish",
                    Description = $"ราคาแตะ BB Upper | เบี่ยงเบนมาก ({pricePosition:F0}%) | โอกาสกลับตัวลงสูง"
                });
            }
            else
            {
                indicators.Add(new IndicatorValue
                {
                    Name = "Bollinger Bands",
                    ShortName = "BB",
                    Value = pricePosition,
                    Status = "Neutral",
                    Description = $"ราคาอยู่กลาง BB ({pricePosition:F0}%) | รอราคาเบี่ยงเบนก่อน"
                });
            }
        }

        // RSI - Confirmation for mean reversion
        if (marketData.RSI.HasValue)
        {
            if (marketData.RSI < 30)
            {
                indicators.Add(new IndicatorValue
                {
                    Name = "RSI (Mean Reversion)",
                    ShortName = "RSI",
                    Value = marketData.RSI.Value,
                    Status = "Bullish",
                    Description = $"RSI {marketData.RSI.Value:F1} (Oversold) | ยืนยันจุดกลับตัวขาขึ้น"
                });
                bullishScore += 30;
            }
            else if (marketData.RSI > 70)
            {
                indicators.Add(new IndicatorValue
                {
                    Name = "RSI (Mean Reversion)",
                    ShortName = "RSI",
                    Value = marketData.RSI.Value,
                    Status = "Bearish",
                    Description = $"RSI {marketData.RSI.Value:F1} (Overbought) | ยืนยันจุดกลับตัวขาลง"
                });
                bearishScore += 30;
            }
            else
            {
                indicators.Add(new IndicatorValue
                {
                    Name = "RSI (Mean Reversion)",
                    ShortName = "RSI",
                    Value = marketData.RSI.Value,
                    Status = "Neutral",
                    Description = $"RSI {marketData.RSI.Value:F1} | ยังไม่ถึงจุด Oversold/Overbought"
                });
            }
        }

        // Distance from mean (SMA20 = BB Middle)
        if (marketData.BollingerMiddle.HasValue)
        {
            var deviation = (marketData.CurrentPrice - marketData.BollingerMiddle.Value) / marketData.BollingerMiddle.Value * 100;
            indicators.Add(new IndicatorValue
            {
                Name = "Distance from Mean",
                ShortName = "DEV",
                Value = deviation,
                Status = Math.Abs(deviation) > 2 ? (deviation < 0 ? "Bullish" : "Bearish") : "Neutral",
                Description = $"ห่างจากค่าเฉลี่ย {deviation:F2}% | Mean Reversion คาดว่าจะกลับสู่ค่าเฉลี่ย"
            });
        }

        signal.Indicators = indicators;

        var netScore = bullishScore - bearishScore;
        var confidence = Math.Min(100, Math.Abs(netScore) * 1.3);

        if (netScore >= 50) // Need strong signal for mean reversion
        {
            signal.SignalType = "Buy";
            signal.Confidence = (int)confidence;
            signal.Strength = GetSignalStrength(confidence);
            signal.RecommendedEntryPrice = marketData.AskPrice;
            signal.TargetPrice = marketData.BollingerMiddle ?? marketData.CurrentPrice * 1.01m; // Target = mean
            // Stop loss below BB Lower with ATR buffer for breathing room
            var meanRevSlBuffer = marketData.ATR.HasValue ? marketData.ATR.Value * 0.5m : marketData.CurrentPrice * 0.005m;
            signal.StopLossPrice = (marketData.BollingerLower ?? marketData.CurrentPrice * 0.995m) - meanRevSlBuffer;
            signal.ExpectedProfitPercent = ((signal.TargetPrice ?? marketData.CurrentPrice) - marketData.CurrentPrice) / marketData.CurrentPrice * 100;
            signal.RiskRewardRatio = config.StopLossPercent > 0 ? signal.ExpectedProfitPercent / config.StopLossPercent : 0;
            signal.EstimatedHoldTimeMinutes = 15;
            signal.Reasoning = $"[Mean Reversion] ราคาต่ำกว่าค่าเฉลี่ย + RSI Oversold | เป้าหมาย = BB Middle | คาดการณ์กลับตัว";
        }
        else if (netScore <= -50)
        {
            signal.SignalType = "Sell";
            signal.Confidence = (int)confidence;
            signal.Strength = GetSignalStrength(confidence);
            signal.Reasoning = $"[Mean Reversion] ราคาสูงกว่าค่าเฉลี่ย + RSI Overbought | ไม่ควรเข้าซื้อตอนนี้";
        }
        else
        {
            signal.SignalType = "Hold";
            signal.Confidence = 0;
            signal.Strength = AISignalStrength.None;
            signal.Reasoning = "[Mean Reversion] ราคายังไม่เบี่ยงเบนจากค่าเฉลี่ยมากพอ | รอสัญญาณที่ดีกว่า";
        }

        return signal;
    }

    /// <summary>
    /// Grid Trading Strategy: Place orders at intervals
    /// Creates buy zones at support levels with fixed grid spacing
    /// วางออเดอร์เป็นช่วงๆ สร้างโซนซื้อที่แนวรับด้วยระยะห่างคงที่
    /// </summary>
    private AITradingSignal GenerateGridTradingSignal(AIMarketData marketData, AIStrategyConfig config)
    {
        var signal = CreateBaseSignal(marketData);
        var indicators = new List<IndicatorValue>();
        int bullishScore = 0;
        int bearishScore = 0;

        // ATR for dynamic grid sizing
        decimal gridSize;
        if (marketData.ATR.HasValue)
        {
            gridSize = marketData.ATR.Value * 0.5m; // Half ATR for grid spacing
            indicators.Add(new IndicatorValue
            {
                Name = "Grid Size (ATR-based)",
                ShortName = "GRID",
                Value = gridSize,
                Status = "Neutral",
                Description = $"Grid spacing: ${gridSize:F2} (0.5x ATR) | ช่องกริดตาม Volatility"
            });
        }
        else
        {
            gridSize = marketData.CurrentPrice * 0.005m; // 0.5% default
            indicators.Add(new IndicatorValue
            {
                Name = "Grid Size (Fixed)",
                ShortName = "GRID",
                Value = gridSize,
                Status = "Neutral",
                Description = $"Grid spacing: ${gridSize:F2} (0.5%) | ช่องกริดคงที่"
            });
        }

        // Calculate grid levels using Bollinger Lower as anchor, or Low24h
        var gridAnchor = marketData.BollingerLower ?? (marketData.Low24h > 0 ? marketData.Low24h : marketData.CurrentPrice * 0.95m);
        // Ensure grid anchor is valid (positive and below current price)
        if (gridAnchor <= 0) gridAnchor = marketData.CurrentPrice * 0.95m;
        var currentGridLevel = gridSize > 0 && marketData.CurrentPrice > gridAnchor
            ? (int)((marketData.CurrentPrice - gridAnchor) / gridSize)
            : 0;

        // Calculate support/resistance from grid anchor
        var nearestGridBelow = gridAnchor + (currentGridLevel * gridSize);
        var support1 = nearestGridBelow;
        var support2 = nearestGridBelow - gridSize;
        var resistance1 = nearestGridBelow + gridSize;

        indicators.Add(new IndicatorValue
        {
            Name = "Grid Levels",
            ShortName = "LVL",
            Value = currentGridLevel,
            Status = "Neutral",
            Description = $"S1: ${support1:F2} | S2: ${support2:F2} | R1: ${resistance1:F2}"
        });

        // Check if price is near grid support (actual distance from nearest grid level below)
        var distanceToSupport = marketData.CurrentPrice > 0
            ? (marketData.CurrentPrice - support1) / marketData.CurrentPrice * 100
            : 100m;
        if (distanceToSupport >= 0 && distanceToSupport < 0.3m)
        {
            indicators.Add(new IndicatorValue
            {
                Name = "Near Grid Support",
                ShortName = "SUP",
                Value = distanceToSupport,
                Status = "Bullish",
                Description = $"ใกล้แนวรับ Grid S1 ({distanceToSupport:F2}%) | โซนซื้อ"
            });
            bullishScore += 40;
        }

        // Volume check for grid trading
        if (marketData.Volume24h > 0)
        {
            indicators.Add(new IndicatorValue
            {
                Name = "Liquidity",
                ShortName = "LIQ",
                Value = marketData.Volume24h,
                Status = marketData.Volume24h > 1000000 ? "Bullish" : "Neutral",
                Description = $"Volume 24h: ${marketData.Volume24h:N0} | Grid ต้องการ Liquidity สูง"
            });
            if (marketData.Volume24h > 1000000) bullishScore += 15;
        }

        // Sideways market is ideal for grid
        if (marketData.Volatility.HasValue && marketData.Volatility < 2)
        {
            indicators.Add(new IndicatorValue
            {
                Name = "Volatility (Grid ideal)",
                ShortName = "VOL%",
                Value = marketData.Volatility.Value,
                Status = "Bullish",
                Description = $"Volatility {marketData.Volatility.Value:F2}% | ต่ำ = เหมาะสำหรับ Grid"
            });
            bullishScore += 20;
        }
        else if (marketData.Volatility.HasValue)
        {
            indicators.Add(new IndicatorValue
            {
                Name = "Volatility (Grid risky)",
                ShortName = "VOL%",
                Value = marketData.Volatility.Value,
                Status = "Bearish",
                Description = $"Volatility {marketData.Volatility.Value:F2}% | สูง = Grid มีความเสี่ยง"
            });
            bearishScore += 15;
        }

        signal.Indicators = indicators;

        var netScore = bullishScore - bearishScore;
        var confidence = Math.Min(100, Math.Abs(netScore) * 1.5);

        if (netScore >= 30)
        {
            signal.SignalType = "Buy";
            signal.Confidence = (int)confidence;
            signal.Strength = GetSignalStrength(confidence);
            signal.RecommendedEntryPrice = support1; // Buy at grid support
            signal.TargetPrice = resistance1; // Sell at next grid level
            signal.StopLossPrice = support2; // Stop at lower grid
            signal.ExpectedProfitPercent = marketData.CurrentPrice > 0 ? (gridSize / marketData.CurrentPrice * 100) : 0.5m;
            signal.RiskRewardRatio = 1.0m; // Grid typically 1:1
            signal.EstimatedHoldTimeMinutes = 60; // Variable hold time
            signal.Reasoning = $"[Grid] ราคาใกล้แนวรับ Grid S1 (${support1:F2}) | ซื้อที่นี่ขายที่ R1 (${resistance1:F2})";
        }
        else if (netScore <= -30)
        {
            signal.SignalType = "Sell";
            signal.Confidence = (int)confidence;
            signal.Strength = GetSignalStrength(confidence);
            signal.Reasoning = $"[Grid] Volatility สูงเกินไปสำหรับ Grid | หรือราคาไม่อยู่ที่โซนซื้อ";
        }
        else
        {
            signal.SignalType = "Hold";
            signal.Confidence = 0;
            signal.Strength = AISignalStrength.None;
            signal.Reasoning = "[Grid] รอราคาลงมาที่แนวรับ Grid | ไม่ไล่ซื้อ";
        }

        return signal;
    }

    /// <summary>
    /// Breakout Strategy: Trade on price breakouts
    /// Uses support/resistance levels and volume confirmation
    /// เทรดเมื่อราคาทะลุแนวต้าน/แนวรับ พร้อม Volume ยืนยัน
    /// </summary>
    private AITradingSignal GenerateBreakoutSignal(AIMarketData marketData, AIStrategyConfig config)
    {
        var signal = CreateBaseSignal(marketData);
        var indicators = new List<IndicatorValue>();
        int bullishScore = 0;
        int bearishScore = 0;

        // Use Bollinger Upper as resistance
        if (marketData.BollingerUpper.HasValue && marketData.BollingerLower.HasValue)
        {
            if (marketData.CurrentPrice > marketData.BollingerUpper.Value)
            {
                indicators.Add(new IndicatorValue
                {
                    Name = "Breakout (BB Upper)",
                    ShortName = "BRK",
                    Value = marketData.CurrentPrice - marketData.BollingerUpper.Value,
                    Status = "Bullish",
                    Description = $"ราคาทะลุ BB Upper (${marketData.BollingerUpper.Value:F2}) | Breakout ขาขึ้น!"
                });
                bullishScore += 35;
            }
            else if (marketData.CurrentPrice < marketData.BollingerLower.Value)
            {
                indicators.Add(new IndicatorValue
                {
                    Name = "Breakdown (BB Lower)",
                    ShortName = "BRK",
                    Value = marketData.BollingerLower.Value - marketData.CurrentPrice,
                    Status = "Bearish",
                    Description = $"ราคาหลุด BB Lower (${marketData.BollingerLower.Value:F2}) | Breakdown ขาลง!"
                });
                bearishScore += 35;
            }
            else
            {
                indicators.Add(new IndicatorValue
                {
                    Name = "No Breakout",
                    ShortName = "BRK",
                    Value = 0,
                    Status = "Neutral",
                    Description = $"ราคายังอยู่ใน BB Range | รอ Breakout"
                });
            }
        }

        // SMA200 as major support/resistance
        if (marketData.SMA200.HasValue)
        {
            var aboveSMA200 = marketData.CurrentPrice > marketData.SMA200.Value;
            indicators.Add(new IndicatorValue
            {
                Name = "SMA200 (Major Level)",
                ShortName = "S200",
                Value = marketData.SMA200.Value,
                Status = aboveSMA200 ? "Bullish" : "Bearish",
                Description = aboveSMA200 ? $"ราคาเหนือ SMA200 = Long-term Bullish" : $"ราคาใต้ SMA200 = Long-term Bearish"
            });
            if (aboveSMA200) bullishScore += 20;
            else bearishScore += 20;
        }

        // Volume confirmation is crucial for breakouts
        if (marketData.VolumeChange24h > 30)
        {
            indicators.Add(new IndicatorValue
            {
                Name = "Volume Surge",
                ShortName = "VOL+",
                Value = marketData.VolumeChange24h,
                Status = "Bullish",
                Description = $"Volume เพิ่ม {marketData.VolumeChange24h:F1}% | ยืนยัน Breakout!"
            });
            bullishScore += 25;
        }
        else
        {
            indicators.Add(new IndicatorValue
            {
                Name = "Volume Normal",
                ShortName = "VOL",
                Value = marketData.VolumeChange24h,
                Status = "Neutral",
                Description = $"Volume ปกติ | Breakout อาจเป็น False Signal"
            });
        }

        // MACD momentum confirmation
        if (marketData.MACD.HasValue && marketData.MACDSignal.HasValue && marketData.MACD > marketData.MACDSignal)
        {
            indicators.Add(new IndicatorValue
            {
                Name = "MACD Momentum",
                ShortName = "MACD",
                Value = marketData.MACDHistogram ?? 0,
                Status = "Bullish",
                Description = $"MACD Bullish | แรงส่งรองรับ Breakout"
            });
            bullishScore += 15;
        }

        signal.Indicators = indicators;

        var netScore = bullishScore - bearishScore;
        var confidence = Math.Min(100, Math.Abs(netScore) * 1.4);

        if (netScore >= 50) // Breakout needs strong confirmation
        {
            signal.SignalType = "Buy";
            signal.Confidence = (int)confidence;
            signal.Strength = GetSignalStrength(confidence);
            signal.RecommendedEntryPrice = marketData.AskPrice;
            var atr = marketData.ATR ?? marketData.CurrentPrice * 0.02m;
            signal.TargetPrice = marketData.CurrentPrice + (atr * 3); // Breakout = bigger targets
            // Stop loss below breakout level with ATR buffer for pullback
            var breakoutSlBuffer = (marketData.ATR ?? marketData.CurrentPrice * 0.01m) * 0.5m;
            signal.StopLossPrice = (marketData.BollingerUpper ?? marketData.CurrentPrice * 0.99m) - breakoutSlBuffer;
            signal.ExpectedProfitPercent = (atr * 3) / marketData.CurrentPrice * 100;
            signal.RiskRewardRatio = 3.0m;
            signal.EstimatedHoldTimeMinutes = 60;
            signal.Reasoning = $"[Breakout] ราคาทะลุแนวต้านพร้อม Volume ยืนยัน | เป้าหมาย 3x ATR";
        }
        else if (netScore <= -50)
        {
            signal.SignalType = "Sell";
            signal.Confidence = (int)confidence;
            signal.Strength = GetSignalStrength(confidence);
            signal.Reasoning = $"[Breakout] ราคาหลุดแนวรับ | ไม่ควรเข้าซื้อ";
        }
        else
        {
            signal.SignalType = "Hold";
            signal.Confidence = 0;
            signal.Strength = AISignalStrength.None;
            signal.Reasoning = "[Breakout] ยังไม่มี Breakout ชัดเจน | รอราคาทะลุแนวต้าน/รับพร้อม Volume";
        }

        return signal;
    }

    /// <summary>
    /// Smart DCA Strategy: Dollar cost averaging with AI timing
    /// Uses multiple indicators to find optimal DCA entry points
    /// เฉลี่ยต้นทุนด้วยจังหวะ AI หาจุดซื้อเฉลี่ยที่เหมาะสม
    /// </summary>
    private AITradingSignal GenerateSmartDCASignal(AIMarketData marketData, AIStrategyConfig config)
    {
        var signal = CreateBaseSignal(marketData);
        var indicators = new List<IndicatorValue>();
        int bullishScore = 0;
        int bearishScore = 0;

        // RSI for DCA timing
        if (marketData.RSI.HasValue)
        {
            if (marketData.RSI < 40)
            {
                var dcaQuality = marketData.RSI < 30 ? "Excellent DCA Zone" : "Good DCA Zone";
                indicators.Add(new IndicatorValue
                {
                    Name = "RSI (DCA Timing)",
                    ShortName = "RSI",
                    Value = marketData.RSI.Value,
                    Status = "Bullish",
                    Description = $"RSI {marketData.RSI.Value:F1} | {dcaQuality} | จังหวะซื้อเฉลี่ยดี"
                });
                bullishScore += marketData.RSI < 30 ? 35 : 25;
            }
            else if (marketData.RSI > 60)
            {
                indicators.Add(new IndicatorValue
                {
                    Name = "RSI (DCA Timing)",
                    ShortName = "RSI",
                    Value = marketData.RSI.Value,
                    Status = "Bearish",
                    Description = $"RSI {marketData.RSI.Value:F1} | Not ideal for DCA | รอจังหวะที่ดีกว่า"
                });
                bearishScore += 20;
            }
            else
            {
                indicators.Add(new IndicatorValue
                {
                    Name = "RSI (DCA Timing)",
                    ShortName = "RSI",
                    Value = marketData.RSI.Value,
                    Status = "Neutral",
                    Description = $"RSI {marketData.RSI.Value:F1} | OK for DCA | จังหวะปานกลาง"
                });
                bullishScore += 10;
            }
        }

        // Price vs SMA200 for long-term value
        if (marketData.SMA200.HasValue)
        {
            var priceVsSMA = (marketData.CurrentPrice - marketData.SMA200.Value) / marketData.SMA200.Value * 100;
            if (priceVsSMA < -10)
            {
                indicators.Add(new IndicatorValue
                {
                    Name = "Price vs SMA200",
                    ShortName = "S200",
                    Value = priceVsSMA,
                    Status = "Bullish",
                    Description = $"ราคาต่ำกว่า SMA200 ({priceVsSMA:F1}%) | DCA ที่ราคาต่ำกว่าค่าเฉลี่ยระยะยาว"
                });
                bullishScore += 30;
            }
            else if (priceVsSMA > 20)
            {
                indicators.Add(new IndicatorValue
                {
                    Name = "Price vs SMA200",
                    ShortName = "S200",
                    Value = priceVsSMA,
                    Status = "Bearish",
                    Description = $"ราคาสูงกว่า SMA200 ({priceVsSMA:F1}%) | อาจรอราคาลงก่อน DCA"
                });
                bearishScore += 25;
            }
            else
            {
                indicators.Add(new IndicatorValue
                {
                    Name = "Price vs SMA200",
                    ShortName = "S200",
                    Value = priceVsSMA,
                    Status = "Neutral",
                    Description = $"ราคาใกล้ SMA200 ({priceVsSMA:F1}%) | Fair value สำหรับ DCA"
                });
                bullishScore += 15;
            }
        }

        // Bollinger position for DCA
        if (marketData.BollingerLower.HasValue && marketData.BollingerMiddle.HasValue)
        {
            var bbRange = marketData.BollingerMiddle.Value - marketData.BollingerLower.Value;
            var bbPosition = bbRange > 0
                ? (marketData.CurrentPrice - marketData.BollingerLower.Value) / bbRange * 100
                : 50m; // Default to middle if range is zero
            if (bbPosition < 30)
            {
                indicators.Add(new IndicatorValue
                {
                    Name = "BB Position (DCA)",
                    ShortName = "BB%",
                    Value = bbPosition,
                    Status = "Bullish",
                    Description = $"ราคาอยู่ส่วนล่างของ BB ({bbPosition:F0}%) | DCA Zone ดี"
                });
                bullishScore += 20;
            }
            else
            {
                indicators.Add(new IndicatorValue
                {
                    Name = "BB Position (DCA)",
                    ShortName = "BB%",
                    Value = bbPosition,
                    Status = "Neutral",
                    Description = $"ราคาอยู่ส่วนบนของ BB ({bbPosition:F0}%) | รอลงก่อน DCA"
                });
            }
        }

        signal.Indicators = indicators;

        var netScore = bullishScore - bearishScore;
        var confidence = Math.Min(100, Math.Abs(netScore) * 1.2);

        if (netScore >= 40)
        {
            signal.SignalType = "Buy";
            signal.Confidence = (int)confidence;
            signal.Strength = GetSignalStrength(confidence);
            signal.RecommendedEntryPrice = marketData.AskPrice;
            signal.TargetPrice = marketData.SMA200 ?? marketData.CurrentPrice * 1.10m; // Long-term target
            signal.StopLossPrice = null; // DCA doesn't use stop loss traditionally
            signal.ExpectedProfitPercent = 10; // Long-term expectation
            signal.RiskRewardRatio = 0; // Not applicable for DCA
            signal.EstimatedHoldTimeMinutes = 0; // Long-term hold
            signal.Reasoning = $"[Smart DCA] จังหวะซื้อเฉลี่ยดี | RSI ต่ำ + ราคาต่ำกว่าค่าเฉลี่ย | ซื้อสะสมระยะยาว";
        }
        else if (netScore <= -20)
        {
            signal.SignalType = "Sell";
            signal.Confidence = (int)confidence;
            signal.Strength = GetSignalStrength(confidence);
            signal.Reasoning = $"[Smart DCA] ไม่ใช่จังหวะ DCA ที่ดี | ราคาสูงกว่าค่าเฉลี่ย | รอราคาลงก่อน";
        }
        else
        {
            signal.SignalType = "Hold";
            signal.Confidence = 0;
            signal.Strength = AISignalStrength.None;
            signal.Reasoning = "[Smart DCA] จังหวะปานกลาง | สามารถ DCA ได้แต่ไม่ใช่จังหวะที่ดีที่สุด";
        }

        return signal;
    }

    private AITradingSignal CreateBaseSignal(AIMarketData marketData)
    {
        return new AITradingSignal
        {
            Symbol = marketData.Symbol,
            Exchange = marketData.Exchange,
            Timestamp = DateTime.UtcNow
        };
    }

    private AISignalStrength GetSignalStrength(double confidence)
    {
        return confidence >= 75 ? AISignalStrength.VeryStrong :
               confidence >= 50 ? AISignalStrength.Strong :
               confidence >= 25 ? AISignalStrength.Moderate : AISignalStrength.Weak;
    }

    #region Technical Indicators

    private decimal CalculateRSI(List<PriceCandle> candles, int period)
    {
        if (candles.Count < period + 1) return 50;

        var changes = new List<(decimal gain, decimal loss)>();
        for (int i = 1; i < candles.Count; i++)
        {
            var change = candles[i].Close - candles[i - 1].Close;
            changes.Add(change > 0 ? (change, 0) : (0, Math.Abs(change)));
        }

        if (changes.Count < period) return 50;

        // Initial average using SMA for first 'period' values (Wilder's method)
        var avgGain = changes.Take(period).Average(c => c.gain);
        var avgLoss = changes.Take(period).Average(c => c.loss);

        // Apply Wilder's smoothing for remaining values
        for (int i = period; i < changes.Count; i++)
        {
            avgGain = (avgGain * (period - 1) + changes[i].gain) / period;
            avgLoss = (avgLoss * (period - 1) + changes[i].loss) / period;
        }

        if (avgLoss == 0 && avgGain == 0) return 50; // Neutral - no price movement
        if (avgLoss == 0) return 100;
        var rs = avgGain / avgLoss;
        return 100 - (100 / (1 + rs));
    }

    private (decimal macd, decimal signal, decimal histogram) CalculateMACD(List<PriceCandle> candles)
    {
        if (candles.Count < 26) return (0, 0, 0);

        // Calculate EMA12 and EMA26 series to get MACD line series
        var ema12Series = CalculateEMASeries(candles, 12);
        var ema26Series = CalculateEMASeries(candles, 26);

        // MACD line = EMA12 - EMA26 (use the overlapping period)
        var startIndex = 26 - 1; // EMA26 starts producing values at index 25
        var macdSeries = new List<decimal>();
        for (int i = startIndex; i < candles.Count; i++)
        {
            macdSeries.Add(ema12Series[i] - ema26Series[i]);
        }

        if (macdSeries.Count == 0) return (0, 0, 0);

        // Current MACD value (latest)
        var macd = macdSeries[^1];

        // Signal line = 9-period EMA of MACD series
        decimal signal;
        if (macdSeries.Count >= 9)
        {
            var signalMultiplier = 2m / (9 + 1);
            var signalEma = macdSeries.Take(9).Average();
            foreach (var m in macdSeries.Skip(9))
            {
                signalEma = (m - signalEma) * signalMultiplier + signalEma;
            }
            signal = signalEma;
        }
        else
        {
            signal = macdSeries.Average();
        }

        var histogram = macd - signal;
        return (macd, signal, histogram);
    }

    /// <summary>
    /// Calculate EMA series for all candles (returns array of same length as candles)
    /// </summary>
    private decimal[] CalculateEMASeries(List<PriceCandle> candles, int period)
    {
        var result = new decimal[candles.Count];
        if (candles.Count < period) return result;

        var multiplier = 2m / (period + 1);

        // First EMA value = SMA of first 'period' candles
        decimal ema = 0;
        for (int i = 0; i < period; i++)
        {
            ema += candles[i].Close;
            result[i] = candles[i].Close; // Fill with close price before period
        }
        ema /= period;
        result[period - 1] = ema;

        // Calculate EMA for remaining candles
        for (int i = period; i < candles.Count; i++)
        {
            ema = (candles[i].Close - ema) * multiplier + ema;
            result[i] = ema;
        }

        return result;
    }

    private decimal CalculateEMA(List<PriceCandle> candles, int period)
    {
        if (candles.Count < period) return candles.LastOrDefault()?.Close ?? 0;

        var multiplier = 2m / (period + 1);
        var ema = candles.Take(period).Average(c => c.Close);

        foreach (var candle in candles.Skip(period))
        {
            ema = (candle.Close - ema) * multiplier + ema;
        }

        return ema;
    }

    private decimal CalculateSMA(List<PriceCandle> candles, int period)
    {
        return candles.TakeLast(period).Average(c => c.Close);
    }

    private (decimal upper, decimal middle, decimal lower) CalculateBollingerBands(List<PriceCandle> candles, int period)
    {
        var closes = candles.TakeLast(period).Select(c => c.Close).ToList();
        var middle = closes.Average();

        // Sample standard deviation (N-1) for statistical correctness
        var sumSquaredDiff = closes.Sum(c => (double)((c - middle) * (c - middle)));
        var stdDev = closes.Count > 1
            ? (decimal)Math.Sqrt(sumSquaredDiff / (closes.Count - 1))
            : 0m;

        var upper = middle + (2 * stdDev);
        var lower = middle - (2 * stdDev);

        return (upper, middle, lower);
    }

    private decimal CalculateATR(List<PriceCandle> candles, int period)
    {
        if (candles.Count < period + 1) return 0;

        var trueRanges = new List<decimal>();

        for (int i = 1; i < candles.Count; i++)
        {
            var high = candles[i].High;
            var low = candles[i].Low;
            var prevClose = candles[i - 1].Close;

            var tr = Math.Max(high - low, Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));
            trueRanges.Add(tr);
        }

        if (trueRanges.Count < period) return trueRanges.Count > 0 ? trueRanges.Average() : 0;

        // Initial ATR = SMA of first 'period' true ranges
        var atr = trueRanges.Take(period).Average();

        // Wilder's smoothing for remaining values
        for (int i = period; i < trueRanges.Count; i++)
        {
            atr = (atr * (period - 1) + trueRanges[i]) / period;
        }

        return atr;
    }

    #endregion
}
