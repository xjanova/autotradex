// AutoTrade-X v1.0.0

using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace AutoTradeX.UI.ViewModels;

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IArbEngine _arbEngine;
    private readonly ILoggingService _logger;
    private readonly IConfigService _configService;
    private readonly IBalancePoolService _balancePool;

    // ========== State Properties ==========

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value))
            {
                ((AsyncRelayCommand)StartBotCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)StopBotCommand).RaiseCanExecuteChanged();
            }
        }
    }

    private string _botStatus = "Idle";
    public string BotStatus
    {
        get => _botStatus;
        set
        {
            if (SetProperty(ref _botStatus, value))
            {
                OnPropertyChanged(nameof(BotStatusColor));
                OnPropertyChanged(nameof(BotStatusGlowColor));
            }
        }
    }

    public string BotStatusColor => BotStatus switch
    {
        "Running" => "#00FF88",
        "Trading" => "#00FFFF",
        "Stopped" => "#8888CC",
        "Error" => "#FF0055",
        "StoppedByRiskLimit" => "#FFAA00",
        _ => "#8888CC"
    };

    public string BotStatusGlowColor => BotStatus switch
    {
        "Running" => "#00FF88",
        "Trading" => "#00FFFF",
        "Stopped" => "#8B5CF6",
        "Error" => "#FF0055",
        "StoppedByRiskLimit" => "#FFAA00",
        _ => "#8B5CF6"
    };

    private string _tradingMode = "Simulation";
    public string TradingMode
    {
        get => _tradingMode;
        set
        {
            if (SetProperty(ref _tradingMode, value))
                OnPropertyChanged(nameof(TradingModeColor));
        }
    }

    public string TradingModeColor => TradingMode == "LIVE" ? "#FF0055" : "#00FF88";

    // ========== Statistics ==========

    private decimal _todayPnL;
    public decimal TodayPnL
    {
        get => _todayPnL;
        set
        {
            if (SetProperty(ref _todayPnL, value))
            {
                OnPropertyChanged(nameof(TodayPnLDisplay));
                OnPropertyChanged(nameof(TodayPnLColor));
                OnPropertyChanged(nameof(TodayPnLGlowColor));
            }
        }
    }

    public string TodayPnLDisplay => TodayPnL >= 0 ? $"+{TodayPnL:F4}" : $"{TodayPnL:F4}";
    public string TodayPnLColor => TodayPnL >= 0 ? "#00FF88" : "#FF0055";
    public string TodayPnLGlowColor => TodayPnL >= 0 ? "#00FF88" : "#FF0055";

    private int _todayTradeCount;
    public int TodayTradeCount
    {
        get => _todayTradeCount;
        set
        {
            if (SetProperty(ref _todayTradeCount, value))
                OnPropertyChanged(nameof(WinRateDisplay));
        }
    }

    private int _successfulTrades;
    public int SuccessfulTrades
    {
        get => _successfulTrades;
        set
        {
            if (SetProperty(ref _successfulTrades, value))
                OnPropertyChanged(nameof(WinRateDisplay));
        }
    }

    public string WinRateDisplay => TodayTradeCount > 0
        ? $"{(decimal)SuccessfulTrades / TodayTradeCount * 100:F1}%"
        : "-";

    // ========== Balance Pool (Real P&L) ==========

    private decimal _realPnL;
    public decimal RealPnL
    {
        get => _realPnL;
        set
        {
            if (SetProperty(ref _realPnL, value))
            {
                OnPropertyChanged(nameof(RealPnLDisplay));
                OnPropertyChanged(nameof(RealPnLColor));
            }
        }
    }

    public string RealPnLDisplay => RealPnL >= 0 ? $"+{RealPnL:F2}" : $"{RealPnL:F2}";
    public string RealPnLColor => RealPnL >= 0 ? "#00FF88" : "#FF0055";

    private decimal _totalBalanceUSDT;
    public decimal TotalBalanceUSDT
    {
        get => _totalBalanceUSDT;
        set => SetProperty(ref _totalBalanceUSDT, value);
    }

    private decimal _currentDrawdown;
    public decimal CurrentDrawdown
    {
        get => _currentDrawdown;
        set
        {
            if (SetProperty(ref _currentDrawdown, value))
            {
                OnPropertyChanged(nameof(DrawdownDisplay));
                OnPropertyChanged(nameof(DrawdownColor));
            }
        }
    }

    public string DrawdownDisplay => $"-{CurrentDrawdown:F2}%";
    public string DrawdownColor => CurrentDrawdown > 3 ? "#FF0055" : CurrentDrawdown > 1 ? "#FFAA00" : "#00FF88";

    private string _emergencyStatus = "Normal";
    public string EmergencyStatus
    {
        get => _emergencyStatus;
        set
        {
            if (SetProperty(ref _emergencyStatus, value))
                OnPropertyChanged(nameof(EmergencyStatusColor));
        }
    }

    public string EmergencyStatusColor => EmergencyStatus switch
    {
        "Normal" => "#00FF88",
        "Warning" => "#FFAA00",
        "Critical" => "#FF0055",
        _ => "#8888CC"
    };

    private long _lastExecutionMs;
    public long LastExecutionMs
    {
        get => _lastExecutionMs;
        set
        {
            if (SetProperty(ref _lastExecutionMs, value))
                OnPropertyChanged(nameof(ExecutionSpeedDisplay));
        }
    }

    public string ExecutionSpeedDisplay => LastExecutionMs > 0 ? $"{LastExecutionMs}ms" : "-";

    // ========== Config Properties ==========

    private decimal _minSpreadPercent;
    public decimal MinSpreadPercent
    {
        get => _minSpreadPercent;
        set => SetProperty(ref _minSpreadPercent, value);
    }

    private decimal _minExpectedProfit;
    public decimal MinExpectedProfit
    {
        get => _minExpectedProfit;
        set => SetProperty(ref _minExpectedProfit, value);
    }

    private decimal _maxPositionSize;
    public decimal MaxPositionSize
    {
        get => _maxPositionSize;
        set => SetProperty(ref _maxPositionSize, value);
    }

    private int _pollingIntervalMs;
    public int PollingIntervalMs
    {
        get => _pollingIntervalMs;
        set => SetProperty(ref _pollingIntervalMs, value);
    }

    // ========== Collections ==========

    public ObservableCollection<TradingPairViewModel> TradingPairs { get; } = new();
    public ObservableCollection<LogEntry> RecentLogs { get; } = new();
    public ObservableCollection<TradeResult> TradeHistory { get; } = new();

    // ========== Chart Data ==========

    public ObservableCollection<ISeries> SpreadSeries { get; } = new();
    private readonly ObservableCollection<ObservableValue> _spreadValues = new();

    public Axis[] XAxes { get; } = new[]
    {
        new Axis
        {
            LabelsPaint = new SolidColorPaint(SKColors.Gray),
            TextSize = 10,
            IsVisible = false
        }
    };

    public Axis[] YAxes { get; } = new[]
    {
        new Axis
        {
            LabelsPaint = new SolidColorPaint(SKColors.Gray),
            TextSize = 10,
            Labeler = value => $"{value:F2}%"
        }
    };

    // ========== Commands ==========

    public ICommand StartBotCommand { get; }
    public ICommand StopBotCommand { get; }
    public ICommand SaveConfigCommand { get; }
    public ICommand ResetDailyStatsCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand ClearLogsCommand { get; }

    // ========== Constructor ==========

    public MainWindowViewModel()
    {
        _arbEngine = App.Services.GetRequiredService<IArbEngine>();
        _logger = App.Services.GetRequiredService<ILoggingService>();
        _configService = App.Services.GetRequiredService<IConfigService>();
        _balancePool = App.Services.GetRequiredService<IBalancePoolService>();

        // Initialize commands
        StartBotCommand = new AsyncRelayCommand(StartBot, CanStartBot);
        StopBotCommand = new AsyncRelayCommand(StopBot, CanStopBot);
        SaveConfigCommand = new AsyncRelayCommand(SaveConfig);
        ResetDailyStatsCommand = new RelayCommand(ResetDailyStats);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        ClearLogsCommand = new RelayCommand(ClearLogs);

        LoadConfig();
        SetupEventHandlers();
        SetupBalancePoolHandlers();
        LoadTradingPairs();
        SetupChart();
        LoadRecentLogs();

        _logger.LogInfo("MainWindow", "MainWindow initialized");
    }

    // ========== Setup Methods ==========

    private void LoadConfig()
    {
        var config = _configService.GetConfig();

        MinSpreadPercent = config.Strategy.MinSpreadPercentage;
        MinExpectedProfit = config.Strategy.MinExpectedProfitQuoteCurrency;
        MaxPositionSize = config.Risk.MaxPositionSizePerTrade;
        PollingIntervalMs = config.Strategy.PollingIntervalMs;

        TradingMode = config.General.LiveTrading ? "LIVE" : "Simulation";
    }

    private void SetupEventHandlers()
    {
        _arbEngine.StatusChanged += (sender, e) =>
        {
            RunOnUI(() =>
            {
                BotStatus = e.Status.ToString();
                IsRunning = e.Status is EngineStatus.Running;
            });
        };

        _arbEngine.PriceUpdated += (sender, e) =>
        {
            RunOnUI(() =>
            {
                var vm = TradingPairs.FirstOrDefault(p => p.Symbol == e.Symbol);
                // Update price display based on exchange
                if (vm != null)
                {
                    // Note: TradingPairViewModel would need to be updated to handle Ticker updates
                }
            });
        };

        _arbEngine.OpportunityFound += (sender, e) =>
        {
            RunOnUI(() =>
            {
                UpdateSpreadChart(e.Opportunity.NetSpreadPercentage);
            });
        };

        _arbEngine.TradeCompleted += (sender, e) =>
        {
            RunOnUI(() =>
            {
                var stats = _arbEngine.GetTodayStats();
                TodayPnL = stats.TotalNetPnL;
                TodayTradeCount = stats.TotalTrades;
                SuccessfulTrades = stats.SuccessfulTrades;

                // Track execution speed
                if (e.Result.Metadata.TryGetValue("TotalExecutionMs", out var execMs) && execMs is long ms)
                {
                    LastExecutionMs = ms;
                }

                // Record trade in balance pool
                _balancePool.RecordTrade(e.Result);

                TradeHistory.Insert(0, e.Result);
                if (TradeHistory.Count > 100)
                {
                    TradeHistory.RemoveAt(TradeHistory.Count - 1);
                }
            });
        };

        _arbEngine.ErrorOccurred += (sender, e) =>
        {
            RunOnUI(() => SetError(e.Message));
        };

        _logger.LogAdded += (sender, entry) =>
        {
            RunOnUI(() =>
            {
                RecentLogs.Insert(0, entry);
                if (RecentLogs.Count > 200)
                {
                    RecentLogs.RemoveAt(RecentLogs.Count - 1);
                }
            });
        };
    }

    private void SetupBalancePoolHandlers()
    {
        // Balance Pool updates
        _balancePool.BalanceUpdated += (sender, e) =>
        {
            RunOnUI(() =>
            {
                TotalBalanceUSDT = e.Snapshot.TotalValueUSDT;
                RealPnL = e.PnL.TotalPnLUSDT;
                CurrentDrawdown = _balancePool.CurrentDrawdown;
            });
        };

        // Emergency protection triggers
        _balancePool.EmergencyTriggered += (sender, e) =>
        {
            RunOnUI(() =>
            {
                var check = e.Check;
                EmergencyStatus = check.Reason switch
                {
                    EmergencyTriggerReason.MaxDrawdownExceeded => "Critical",
                    EmergencyTriggerReason.MaxLossExceeded => "Critical",
                    EmergencyTriggerReason.ConsecutiveLosses => "Warning",
                    EmergencyTriggerReason.RapidLossRate => "Warning",
                    EmergencyTriggerReason.CriticalImbalance => "Warning",
                    _ => "Normal"
                };

                // Show warning message
                _logger.LogCritical("Emergency", $"ALERT: {check.Message}");

                // Auto-pause on critical conditions
                if (check.RecommendedAction == EmergencyAction.StopTrading ||
                    check.RecommendedAction == EmergencyAction.PauseTrading)
                {
                    _arbEngine.Pause();
                    ShowWarning($"Trading paused due to: {check.Message}");
                }
            });
        };

        // Rebalance recommendations
        _balancePool.RebalanceRecommended += (sender, e) =>
        {
            RunOnUI(() =>
            {
                if (e.Recommendation.Urgency >= RebalanceUrgency.High)
                {
                    _logger.LogWarning("Rebalance", e.Recommendation.Summary);
                }
            });
        };
    }

    private void LoadTradingPairs()
    {
        var pairs = _arbEngine.GetTradingPairs();
        foreach (var pair in pairs)
        {
            TradingPairs.Add(new TradingPairViewModel(pair));
        }
    }

    private void SetupChart()
    {
        for (int i = 0; i < 60; i++)
        {
            _spreadValues.Add(new ObservableValue(0));
        }

        SpreadSeries.Add(new LineSeries<ObservableValue>
        {
            Values = _spreadValues,
            Fill = null,
            Stroke = new SolidColorPaint(new SKColor(0x00, 0xFF, 0xFF), 2),
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 0.3
        });
    }

    private void UpdateSpreadChart(decimal spreadPercent)
    {
        _spreadValues.Add(new ObservableValue((double)spreadPercent));
        if (_spreadValues.Count > 60)
        {
            _spreadValues.RemoveAt(0);
        }
    }

    private void LoadRecentLogs()
    {
        var logs = _logger.GetRecentLogs(50);
        foreach (var log in logs)
        {
            RecentLogs.Add(log);
        }
    }

    // ========== Command Implementations ==========

    private async Task StartBot()
    {
        await ExecuteAsync(async () =>
        {
            _logger.LogInfo("MainWindow", "Starting bot...");

            // Initialize balance pool first
            await _balancePool.InitializeAsync();
            _logger.LogInfo("MainWindow", "Balance pool initialized");

            await _arbEngine.StartAsync();
            IsRunning = true;

            // Start balance monitoring task
            _ = Task.Run(async () =>
            {
                while (IsRunning)
                {
                    await _balancePool.UpdateBalancesAsync();
                    await Task.Delay(10000); // Update every 10 seconds
                }
            });
        }, "Starting...");
    }

    private bool CanStartBot() => !IsRunning;

    private async Task StopBot()
    {
        await ExecuteAsync(async () =>
        {
            _logger.LogInfo("MainWindow", "Stopping bot...");
            await _arbEngine.StopAsync();
            IsRunning = false;
        }, "Stopping...");
    }

    private bool CanStopBot() => IsRunning;

    private async Task SaveConfig()
    {
        await ExecuteAsync(async () =>
        {
            await _configService.UpdateConfigAsync(config =>
            {
                config.Strategy.MinSpreadPercentage = MinSpreadPercent;
                config.Strategy.MinExpectedProfitQuoteCurrency = MinExpectedProfit;
                config.Risk.MaxPositionSizePerTrade = MaxPositionSize;
                config.Strategy.PollingIntervalMs = PollingIntervalMs;
            });

            _arbEngine.UpdateConfig(_configService.GetConfig());
            _logger.LogInfo("MainWindow", "Configuration saved");
            ShowInfo("Configuration saved successfully");
        }, "Saving...");
    }

    private void ResetDailyStats()
    {
        if (Confirm("Reset today's statistics?"))
        {
            _arbEngine.ResetDailyStats();
            TodayPnL = 0;
            TodayTradeCount = 0;
            SuccessfulTrades = 0;

            _logger.LogInfo("MainWindow", "Daily stats reset by user");
        }
    }

    private void OpenSettings()
    {
        ShowInfo("Settings window - Coming soon");
    }

    private void ClearLogs()
    {
        RecentLogs.Clear();
    }

    // ========== Dispose ==========

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
