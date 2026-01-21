using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoTradeX.Core.Models;

namespace AutoTradeX.UI.Views;

public partial class AddPairDialog : Window
{
    public bool DialogResultOk { get; private set; } = false;
    public TradingPairConfig? Result { get; private set; }
    public List<TradingPairConfig> Results { get; private set; } = new();
    public PairingMode SelectedPairingMode { get; private set; } = PairingMode.Single;

    // New properties for project integration
    public string Symbol => $"{(BaseAssetCombo.SelectedItem as ComboBoxItem)?.Content}/{(QuoteAssetCombo.SelectedItem as ComboBoxItem)?.Content}";
    public string ExchangeA => (ExchangeACombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Binance";
    public string ExchangeB => (ExchangeBCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "KuCoin";
    public string SelectedStrategyId => _strategies.Count > 0 && StrategyCombo.SelectedIndex >= 0
        ? _strategies[StrategyCombo.SelectedIndex].Id : "";
    public decimal TradeAmount => decimal.TryParse(TradeAmountInput.Text, out var amt) ? amt : 100m;

    private readonly string[] _allExchanges = { "Binance", "KuCoin", "OKX", "Bybit", "Gate.io", "Bitkub" };
    private List<TradingStrategy> _strategies = new();
    private ProjectTradingPair? _editingPair;

    public AddPairDialog()
    {
        InitializeComponent();
        SetupEventHandlers();
        LoadDefaultStrategies();
        UpdatePreview(null, null);
    }

    public AddPairDialog(IEnumerable<TradingStrategy> strategies) : this()
    {
        LoadStrategies(strategies);
    }

    public AddPairDialog(IEnumerable<TradingStrategy> strategies, ProjectTradingPair editPair) : this(strategies)
    {
        _editingPair = editPair;
        LoadPairForEditing(editPair);
    }

    private void SetupEventHandlers()
    {
        // Setup event handlers for preview updates
        BaseAssetCombo.SelectionChanged += UpdatePreview;
        QuoteAssetCombo.SelectionChanged += UpdatePreview;
        ExchangeACombo.SelectionChanged += UpdatePreview;
        ExchangeBCombo.SelectionChanged += UpdatePreview;
        PrimaryExchangeCombo.SelectionChanged += OnPrimaryExchangeChanged;
        AllModePrimaryCombo.SelectionChanged += UpdateAllModePreview;
        StrategyCombo.SelectionChanged += OnStrategyChanged;
    }

    private void LoadDefaultStrategies()
    {
        var strategyService = new Infrastructure.Services.StrategyService();
        _strategies = strategyService.GetPresetStrategies().ToList();
        PopulateStrategyCombo();
    }

    private void LoadStrategies(IEnumerable<TradingStrategy> strategies)
    {
        _strategies = strategies.ToList();
        PopulateStrategyCombo();
    }

    private void PopulateStrategyCombo()
    {
        StrategyCombo.Items.Clear();
        foreach (var strategy in _strategies)
        {
            StrategyCombo.Items.Add(new ComboBoxItem { Content = strategy.Name, Tag = strategy.Id });
        }
        if (StrategyCombo.Items.Count > 0)
        {
            StrategyCombo.SelectedIndex = 0;
        }
    }

    private void OnStrategyChanged(object? sender, SelectionChangedEventArgs? e)
    {
        if (StrategyCombo.SelectedIndex >= 0 && StrategyCombo.SelectedIndex < _strategies.Count)
        {
            var strategy = _strategies[StrategyCombo.SelectedIndex];
            StrategyDescription.Text = strategy.Description;
        }
    }

    private void LoadPairForEditing(ProjectTradingPair pair)
    {
        // Parse symbol
        var parts = pair.Symbol.Split('/');
        if (parts.Length == 2)
        {
            SelectComboItemByContent(BaseAssetCombo, parts[0]);
            SelectComboItemByContent(QuoteAssetCombo, parts[1]);
        }

        SelectComboItemByContent(ExchangeACombo, pair.ExchangeA);
        SelectComboItemByContent(ExchangeBCombo, pair.ExchangeB);
        TradeAmountInput.Text = pair.TradeAmount.ToString();

        // Select strategy
        var strategyIndex = _strategies.FindIndex(s => s.Id == pair.StrategyId);
        if (strategyIndex >= 0)
        {
            StrategyCombo.SelectedIndex = strategyIndex;
        }
    }

    private void SelectComboItemByContent(ComboBox combo, string content)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Content?.ToString() == content)
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private void PairMode_Changed(object sender, RoutedEventArgs e)
    {
        // Null check - UI elements may not be ready during initialization
        if (SinglePairPanel == null || OneToManyPanel == null || OneToAllInfoPanel == null)
            return;

        if (SinglePairMode?.IsChecked == true)
        {
            SelectedPairingMode = PairingMode.Single;
            SinglePairPanel.Visibility = Visibility.Visible;
            OneToManyPanel.Visibility = Visibility.Collapsed;
            OneToAllInfoPanel.Visibility = Visibility.Collapsed;
        }
        else if (OneToManyMode?.IsChecked == true)
        {
            SelectedPairingMode = PairingMode.OneToMany;
            SinglePairPanel.Visibility = Visibility.Collapsed;
            OneToManyPanel.Visibility = Visibility.Visible;
            OneToAllInfoPanel.Visibility = Visibility.Collapsed;
            UpdateOneToManyCheckboxes();
        }
        else if (OneToAllMode?.IsChecked == true)
        {
            SelectedPairingMode = PairingMode.OneToAll;
            SinglePairPanel.Visibility = Visibility.Collapsed;
            OneToManyPanel.Visibility = Visibility.Collapsed;
            OneToAllInfoPanel.Visibility = Visibility.Visible;
            UpdateAllModePreview(null, null);
        }

        UpdatePreview(null, null);
    }

    private void OnPrimaryExchangeChanged(object? sender, SelectionChangedEventArgs? e)
    {
        UpdateOneToManyCheckboxes();
        UpdatePreview(sender, e);
    }

    private void UpdateOneToManyCheckboxes()
    {
        // Null check - UI elements may not be ready
        if (PrimaryExchangeCombo == null || CheckBinance == null) return;

        var primary = (PrimaryExchangeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Binance";

        // Disable checkbox that matches primary exchange
        CheckBinance.IsEnabled = primary != "Binance";
        CheckKuCoin.IsEnabled = primary != "KuCoin";
        CheckOKX.IsEnabled = primary != "OKX";
        CheckBybit.IsEnabled = primary != "Bybit";
        CheckGateIO.IsEnabled = primary != "Gate.io";
        CheckBitkub.IsEnabled = primary != "Bitkub";

        // Uncheck if disabled
        if (!CheckBinance.IsEnabled) CheckBinance.IsChecked = false;
        if (!CheckKuCoin.IsEnabled) CheckKuCoin.IsChecked = false;
        if (!CheckOKX.IsEnabled) CheckOKX.IsChecked = false;
        if (!CheckBybit.IsEnabled) CheckBybit.IsChecked = false;
        if (!CheckGateIO.IsEnabled) CheckGateIO.IsChecked = false;
        if (!CheckBitkub.IsEnabled) CheckBitkub.IsChecked = false;
    }

    private void UpdateAllModePreview(object? sender, SelectionChangedEventArgs? e)
    {
        if (AllModePairsPreview == null) return;

        var primary = (AllModePrimaryCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Binance";
        var others = _allExchanges.Where(ex => ex != primary).ToList();
        AllModePairsPreview.Text = $"Will create {others.Count} pairs: {primary} → {string.Join(", ", others)}";
    }

    private void UpdatePreview(object? sender, SelectionChangedEventArgs? e)
    {
        if (PreviewSymbol == null) return;

        var baseAsset = (BaseAssetCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "BTC";
        var quoteAsset = (QuoteAssetCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "USDT";

        PreviewSymbol.Text = $"{baseAsset}/{quoteAsset}";
        PreviewIcon.Text = baseAsset.Length > 0 ? baseAsset[0].ToString() : "?";

        // Update preview based on mode
        if (SelectedPairingMode == PairingMode.Single)
        {
            var exchangeA = (ExchangeACombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Binance";
            var exchangeB = (ExchangeBCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "KuCoin";
            PreviewExchanges.Text = $"{exchangeA} → {exchangeB}";

            if (exchangeA == exchangeB)
            {
                ShowValidation("Buy and Sell exchanges must be different!");
            }
            else
            {
                HideValidation();
            }
        }
        else if (SelectedPairingMode == PairingMode.OneToMany)
        {
            var primary = (PrimaryExchangeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Binance";
            var targets = GetSelectedTargetExchanges();
            PreviewExchanges.Text = targets.Count > 0
                ? $"{primary} → {targets.Count} exchanges"
                : $"{primary} → (select targets)";

            if (targets.Count == 0)
            {
                ShowValidation("Please select at least one target exchange!");
            }
            else
            {
                HideValidation();
            }
        }
        else // OneToAll
        {
            var primary = (AllModePrimaryCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Binance";
            PreviewExchanges.Text = $"{primary} → All ({_allExchanges.Length - 1} exchanges)";
            HideValidation();
        }
    }

    private List<string> GetSelectedTargetExchanges()
    {
        var targets = new List<string>();
        if (CheckBinance.IsChecked == true && CheckBinance.IsEnabled) targets.Add("Binance");
        if (CheckKuCoin.IsChecked == true && CheckKuCoin.IsEnabled) targets.Add("KuCoin");
        if (CheckOKX.IsChecked == true && CheckOKX.IsEnabled) targets.Add("OKX");
        if (CheckBybit.IsChecked == true && CheckBybit.IsEnabled) targets.Add("Bybit");
        if (CheckGateIO.IsChecked == true && CheckGateIO.IsEnabled) targets.Add("Gate.io");
        if (CheckBitkub.IsChecked == true && CheckBitkub.IsEnabled) targets.Add("Bitkub");
        return targets;
    }

    private void ShowValidation(string message)
    {
        ValidationBorder.Visibility = Visibility.Visible;
        ValidationText.Text = message;
    }

    private void HideValidation()
    {
        ValidationBorder.Visibility = Visibility.Collapsed;
    }

    private void MinSpreadSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MinSpreadText != null)
        {
            MinSpreadText.Text = $"{e.NewValue:F2}%";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResultOk = false;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResultOk = false;
        Close();
    }

    private void AddPairButton_Click(object sender, RoutedEventArgs e)
    {
        var baseAsset = (BaseAssetCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "BTC";
        var quoteAsset = (QuoteAssetCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "USDT";

        if (!decimal.TryParse(TradeAmountInput.Text, out var tradeAmount) || tradeAmount <= 0)
        {
            ShowValidation("Please enter a valid trade amount!");
            return;
        }

        Results.Clear();

        if (SelectedPairingMode == PairingMode.Single)
        {
            var exchangeA = (ExchangeACombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Binance";
            var exchangeB = (ExchangeBCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "KuCoin";

            if (exchangeA == exchangeB)
            {
                ShowValidation("Buy and Sell exchanges must be different!");
                return;
            }

            Result = CreatePairConfig(baseAsset, quoteAsset, exchangeA, exchangeB, tradeAmount);
            Results.Add(Result);
        }
        else if (SelectedPairingMode == PairingMode.OneToMany)
        {
            var primary = (PrimaryExchangeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Binance";
            var targets = GetSelectedTargetExchanges();

            if (targets.Count == 0)
            {
                ShowValidation("Please select at least one target exchange!");
                return;
            }

            foreach (var target in targets)
            {
                Results.Add(CreatePairConfig(baseAsset, quoteAsset, primary, target, tradeAmount));
            }
            Result = Results.FirstOrDefault();
        }
        else // OneToAll
        {
            var primary = (AllModePrimaryCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Binance";
            var targets = _allExchanges.Where(ex => ex != primary).ToList();

            foreach (var target in targets)
            {
                Results.Add(CreatePairConfig(baseAsset, quoteAsset, primary, target, tradeAmount));
            }
            Result = Results.FirstOrDefault();
        }

        DialogResultOk = true;
        DialogResult = true;
        Close();
    }

    private TradingPairConfig CreatePairConfig(string baseAsset, string quoteAsset, string exchangeA, string exchangeB, decimal tradeAmount)
    {
        return new TradingPairConfig
        {
            Symbol = $"{baseAsset}/{quoteAsset}",
            BaseAsset = baseAsset,
            QuoteAsset = quoteAsset,
            ExchangeA = exchangeA,
            ExchangeB = exchangeB,
            MinSpreadPercent = MinSpreadSlider.Value,
            TradeAmount = tradeAmount,
            AutoTradeEnabled = AutoTradeCheck.IsChecked ?? false
        };
    }
}

public enum PairingMode
{
    Single,      // 1:1 - One exchange to one exchange
    OneToMany,   // 1:N - One exchange to selected exchanges
    OneToAll     // 1:All - One exchange to all other exchanges
}

public class TradingPairConfig
{
    public string Symbol { get; set; } = "";
    public string BaseAsset { get; set; } = "";
    public string QuoteAsset { get; set; } = "USDT";
    public string ExchangeA { get; set; } = "Binance";
    public string ExchangeB { get; set; } = "KuCoin";
    public double MinSpreadPercent { get; set; } = 0.1;
    public decimal TradeAmount { get; set; } = 100;
    public bool AutoTradeEnabled { get; set; } = false;
}
