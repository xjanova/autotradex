using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;

namespace AutoTradeX.UI.Views;

public partial class StrategyPage : UserControl
{
    private readonly IStrategyService? _strategyService;
    private readonly ILoggingService? _logger;
    private TradingStrategy? _currentStrategy;
#pragma warning disable CS0414 // Field is assigned but never used - reserved for preventing updates during loading
    private bool _isLoading = false;
#pragma warning restore CS0414

    public ObservableCollection<StrategyDisplayItem> Strategies { get; } = new();

    public StrategyPage()
    {
        InitializeComponent();
        DataContext = this;

        _strategyService = App.Services?.GetService<IStrategyService>();
        _logger = App.Services?.GetService<ILoggingService>();

        Loaded += StrategyPage_Loaded;

        // Setup toggle event handlers
        TrailingStopToggle.Checked += TrailingStopToggle_Changed;
        TrailingStopToggle.Unchecked += TrailingStopToggle_Changed;
    }

    private async void StrategyPage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadStrategiesAsync();
    }

    private async Task LoadStrategiesAsync()
    {
        Strategies.Clear();

        if (_strategyService == null)
        {
            // Load preset strategies if service not available
            var presets = new Infrastructure.Services.StrategyService().GetPresetStrategies();
            foreach (var strategy in presets)
            {
                Strategies.Add(new StrategyDisplayItem(strategy));
            }
        }
        else
        {
            var strategies = await _strategyService.GetAllStrategiesAsync();
            foreach (var strategy in strategies)
            {
                Strategies.Add(new StrategyDisplayItem(strategy));
            }
        }

        StrategyList.ItemsSource = Strategies;

        // Select first strategy
        if (Strategies.Count > 0)
        {
            StrategyList.SelectedIndex = 0;
        }
    }

    private void StrategyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StrategyList.SelectedItem is StrategyDisplayItem item)
        {
            LoadStrategyToEditor(item.Strategy);
        }
    }

    private void LoadStrategyToEditor(TradingStrategy strategy)
    {
        _isLoading = true;
        _currentStrategy = strategy;

        // Header
        StrategyNameInput.Text = strategy.Name;
        StrategyDescriptionInput.Text = strategy.Description;
        StrategyModelType.Text = strategy.AI.ModelType.ToString();

        // Entry Conditions
        MinSpreadInput.Text = strategy.Entry.MinSpreadPercent.ToString();
        MaxSpreadInput.Text = strategy.Entry.MaxSpreadPercent.ToString();
        MinVolumeInput.Text = strategy.Entry.MinVolume24h.ToString();
        ConfirmationTimeInput.Text = strategy.Entry.SpreadConfirmationSeconds.ToString();
        CheckMomentumToggle.IsChecked = strategy.Entry.CheckMomentum;
        AvoidVolatilityToggle.IsChecked = strategy.Entry.AvoidHighVolatility;
        CheckOrderBookToggle.IsChecked = strategy.Entry.CheckOrderBookDepth;

        // Exit Conditions
        TakeProfitInput.Text = strategy.Exit.TakeProfitPercent.ToString();
        StopLossInput.Text = strategy.Exit.StopLossPercent.ToString();
        TrailingStopToggle.IsChecked = strategy.Exit.EnableTrailingStop;
        TrailingActivationInput.Text = strategy.Exit.TrailingStopActivation.ToString();
        TrailingDistanceInput.Text = strategy.Exit.TrailingStopDistance.ToString();
        MaxHoldTimeInput.Text = strategy.Exit.MaxHoldTimeMinutes.ToString();

        // Risk Management
        MaxPositionInput.Text = strategy.Risk.MaxPositionSize.ToString();
        MaxBalancePercentInput.Text = strategy.Risk.MaxBalancePercentPerTrade.ToString();
        MaxOpenPositionsInput.Text = strategy.Risk.MaxOpenPositions.ToString();
        MaxTradesHourInput.Text = strategy.Risk.MaxTradesPerHour.ToString();
        MaxDailyLossInput.Text = strategy.Risk.MaxDailyLoss.ToString();
        MaxConsecutiveLossesInput.Text = strategy.Risk.MaxConsecutiveLosses.ToString();
        DrawdownProtectionToggle.IsChecked = strategy.Risk.EnableDrawdownProtection;

        // AI Settings
        EnableAIToggle.IsChecked = strategy.AI.EnableAI;
        AIModelTypeCombo.SelectedIndex = (int)strategy.AI.ModelType;
        MinConfidenceInput.Text = strategy.AI.MinConfidenceScore.ToString();
        UseSentimentToggle.IsChecked = strategy.AI.UseMarketSentiment;
        UsePatternToggle.IsChecked = strategy.AI.UsePatternRecognition;
        AdaptiveLearningToggle.IsChecked = strategy.AI.EnableAdaptiveLearning;
        HistoricalPredictionToggle.IsChecked = strategy.AI.UseHistoricalPrediction;

        // Advanced
        SlippageProtectionToggle.IsChecked = strategy.Advanced.EnableSlippageProtection;
        MaxSlippageInput.Text = strategy.Advanced.MaxSlippagePercent.ToString();
        OrderTimeoutInput.Text = strategy.Advanced.OrderTimeoutSeconds.ToString();
        UseLimitOrdersToggle.IsChecked = strategy.Advanced.UseLimitOrders;
        SplitOrdersToggle.IsChecked = strategy.Advanced.SplitLargeOrders;
        RetryOrdersToggle.IsChecked = strategy.Advanced.RetryFailedOrders;
        FeeOptimizationToggle.IsChecked = strategy.Advanced.EnableFeeOptimization;
        PreferLowerFeesToggle.IsChecked = strategy.Advanced.PreferLowerFeeExchanges;

        // Update trailing stop UI state
        UpdateTrailingStopState();

        _isLoading = false;
    }

    private TradingStrategy GetStrategyFromEditor()
    {
        var strategy = _currentStrategy ?? new TradingStrategy();

        // Header
        strategy.Name = StrategyNameInput.Text;
        strategy.Description = StrategyDescriptionInput.Text;

        // Entry Conditions
        if (decimal.TryParse(MinSpreadInput.Text, out var minSpread)) strategy.Entry.MinSpreadPercent = minSpread;
        if (decimal.TryParse(MaxSpreadInput.Text, out var maxSpread)) strategy.Entry.MaxSpreadPercent = maxSpread;
        if (decimal.TryParse(MinVolumeInput.Text, out var minVolume)) strategy.Entry.MinVolume24h = minVolume;
        if (int.TryParse(ConfirmationTimeInput.Text, out var confTime)) strategy.Entry.SpreadConfirmationSeconds = confTime;
        strategy.Entry.CheckMomentum = CheckMomentumToggle.IsChecked ?? true;
        strategy.Entry.AvoidHighVolatility = AvoidVolatilityToggle.IsChecked ?? true;
        strategy.Entry.CheckOrderBookDepth = CheckOrderBookToggle.IsChecked ?? true;

        // Exit Conditions
        if (decimal.TryParse(TakeProfitInput.Text, out var tp)) strategy.Exit.TakeProfitPercent = tp;
        if (decimal.TryParse(StopLossInput.Text, out var sl)) strategy.Exit.StopLossPercent = sl;
        strategy.Exit.EnableTrailingStop = TrailingStopToggle.IsChecked ?? false;
        if (decimal.TryParse(TrailingActivationInput.Text, out var tsAct)) strategy.Exit.TrailingStopActivation = tsAct;
        if (decimal.TryParse(TrailingDistanceInput.Text, out var tsDist)) strategy.Exit.TrailingStopDistance = tsDist;
        if (int.TryParse(MaxHoldTimeInput.Text, out var maxHold)) strategy.Exit.MaxHoldTimeMinutes = maxHold;

        // Risk Management
        if (decimal.TryParse(MaxPositionInput.Text, out var maxPos)) strategy.Risk.MaxPositionSize = maxPos;
        if (decimal.TryParse(MaxBalancePercentInput.Text, out var maxBal)) strategy.Risk.MaxBalancePercentPerTrade = maxBal;
        if (int.TryParse(MaxOpenPositionsInput.Text, out var maxOpen)) strategy.Risk.MaxOpenPositions = maxOpen;
        if (int.TryParse(MaxTradesHourInput.Text, out var maxTrades)) strategy.Risk.MaxTradesPerHour = maxTrades;
        if (decimal.TryParse(MaxDailyLossInput.Text, out var maxLoss)) strategy.Risk.MaxDailyLoss = maxLoss;
        if (int.TryParse(MaxConsecutiveLossesInput.Text, out var maxConsec)) strategy.Risk.MaxConsecutiveLosses = maxConsec;
        strategy.Risk.EnableDrawdownProtection = DrawdownProtectionToggle.IsChecked ?? true;

        // AI Settings
        strategy.AI.EnableAI = EnableAIToggle.IsChecked ?? true;
        strategy.AI.ModelType = (AIModelType)AIModelTypeCombo.SelectedIndex;
        if (int.TryParse(MinConfidenceInput.Text, out var minConf)) strategy.AI.MinConfidenceScore = minConf;
        strategy.AI.UseMarketSentiment = UseSentimentToggle.IsChecked ?? true;
        strategy.AI.UsePatternRecognition = UsePatternToggle.IsChecked ?? true;
        strategy.AI.EnableAdaptiveLearning = AdaptiveLearningToggle.IsChecked ?? true;
        strategy.AI.UseHistoricalPrediction = HistoricalPredictionToggle.IsChecked ?? true;

        // Advanced
        strategy.Advanced.EnableSlippageProtection = SlippageProtectionToggle.IsChecked ?? true;
        if (decimal.TryParse(MaxSlippageInput.Text, out var maxSlip)) strategy.Advanced.MaxSlippagePercent = maxSlip;
        if (int.TryParse(OrderTimeoutInput.Text, out var timeout)) strategy.Advanced.OrderTimeoutSeconds = timeout;
        strategy.Advanced.UseLimitOrders = UseLimitOrdersToggle.IsChecked ?? false;
        strategy.Advanced.SplitLargeOrders = SplitOrdersToggle.IsChecked ?? true;
        strategy.Advanced.RetryFailedOrders = RetryOrdersToggle.IsChecked ?? true;
        strategy.Advanced.EnableFeeOptimization = FeeOptimizationToggle.IsChecked ?? true;
        strategy.Advanced.PreferLowerFeeExchanges = PreferLowerFeesToggle.IsChecked ?? true;

        return strategy;
    }

    private void TrailingStopToggle_Changed(object sender, RoutedEventArgs e)
    {
        UpdateTrailingStopState();
    }

    private void UpdateTrailingStopState()
    {
        var isEnabled = TrailingStopToggle.IsChecked ?? false;
        TrailingStopSettings.Opacity = isEnabled ? 1.0 : 0.5;
        TrailingActivationInput.IsEnabled = isEnabled;
        TrailingDistanceInput.IsEnabled = isEnabled;
    }

    private async void NewStrategyButton_Click(object sender, RoutedEventArgs e)
    {
        var newStrategy = new TradingStrategy
        {
            Name = "New Strategy",
            Description = "Custom trading strategy"
        };

        if (_strategyService != null)
        {
            await _strategyService.SaveStrategyAsync(newStrategy);
        }

        Strategies.Add(new StrategyDisplayItem(newStrategy));
        StrategyList.SelectedItem = Strategies.Last();

        _logger?.LogInfo("Strategy", $"Created new strategy: {newStrategy.Name}");
    }

    private async void SaveStrategyButton_Click(object sender, RoutedEventArgs e)
    {
        var strategy = GetStrategyFromEditor();

        if (_strategyService != null)
        {
            await _strategyService.SaveStrategyAsync(strategy);
        }

        // Update list display
        var displayItem = Strategies.FirstOrDefault(s => s.Strategy.Id == strategy.Id);
        if (displayItem != null)
        {
            displayItem.Name = strategy.Name;
            displayItem.Strategy = strategy;
        }

        // Refresh list
        StrategyList.Items.Refresh();

        _logger?.LogInfo("Strategy", $"Saved strategy: {strategy.Name}");
        MessageBox.Show($"Strategy '{strategy.Name}' saved successfully!", "Strategy Saved",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void DeleteStrategyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStrategy == null) return;

        if (Strategies.Count <= 1)
        {
            MessageBox.Show("Cannot delete the last strategy.", "Cannot Delete",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show($"Are you sure you want to delete '{_currentStrategy.Name}'?",
            "Delete Strategy", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        if (_strategyService != null)
        {
            await _strategyService.DeleteStrategyAsync(_currentStrategy.Id);
        }

        var displayItem = Strategies.FirstOrDefault(s => s.Strategy.Id == _currentStrategy.Id);
        if (displayItem != null)
        {
            Strategies.Remove(displayItem);
        }

        if (Strategies.Count > 0)
        {
            StrategyList.SelectedIndex = 0;
        }

        _logger?.LogInfo("Strategy", $"Deleted strategy: {_currentStrategy.Name}");
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            Title = "Import Strategy"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var json = await System.IO.File.ReadAllTextAsync(dialog.FileName);
                var strategy = _strategyService != null
                    ? await _strategyService.ImportStrategyAsync(json)
                    : System.Text.Json.JsonSerializer.Deserialize<TradingStrategy>(json);

                if (strategy != null)
                {
                    Strategies.Add(new StrategyDisplayItem(strategy));
                    StrategyList.SelectedItem = Strategies.Last();

                    _logger?.LogInfo("Strategy", $"Imported strategy: {strategy.Name}");
                    MessageBox.Show($"Strategy '{strategy.Name}' imported successfully!", "Import Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to import strategy: {ex.Message}", "Import Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStrategy == null)
        {
            MessageBox.Show("Please select a strategy to export.", "No Strategy Selected",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            FileName = $"{_currentStrategy.Name.Replace(" ", "_")}_strategy.json",
            Title = "Export Strategy"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var json = _strategyService != null
                    ? await _strategyService.ExportStrategyAsync(_currentStrategy.Id)
                    : System.Text.Json.JsonSerializer.Serialize(_currentStrategy, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                await System.IO.File.WriteAllTextAsync(dialog.FileName, json);

                _logger?.LogInfo("Strategy", $"Exported strategy: {_currentStrategy.Name}");
                MessageBox.Show($"Strategy exported to {dialog.FileName}", "Export Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export strategy: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

public class StrategyDisplayItem : System.ComponentModel.INotifyPropertyChanged
{
    public TradingStrategy Strategy { get; set; }

    private string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(nameof(Name)); }
    }

    public AISettings AI => Strategy.AI;

    public string StatusText => Strategy.IsActive ? "Active" : "Disabled";
    public Brush StatusColor => new SolidColorBrush(Strategy.IsActive
        ? (Color)ColorConverter.ConvertFromString("#10B981")
        : (Color)ColorConverter.ConvertFromString("#60FFFFFF"));

    public StrategyDisplayItem(TradingStrategy strategy)
    {
        Strategy = strategy;
        _name = strategy.Name;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}
