using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoTradeX.UI.Views;

public partial class AddPairDialog : Window
{
    public bool DialogResultOk { get; private set; } = false;
    public TradingPairConfig? Result { get; private set; }

    public AddPairDialog()
    {
        InitializeComponent();

        // Setup event handlers for preview updates
        BaseAssetCombo.SelectionChanged += UpdatePreview;
        QuoteAssetCombo.SelectionChanged += UpdatePreview;
        ExchangeACombo.SelectionChanged += UpdatePreview;
        ExchangeBCombo.SelectionChanged += UpdatePreview;

        UpdatePreview(null, null);
    }

    private void UpdatePreview(object? sender, SelectionChangedEventArgs? e)
    {
        if (PreviewSymbol == null) return;

        var baseAsset = (BaseAssetCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "BTC";
        var quoteAsset = (QuoteAssetCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "USDT";
        var exchangeA = (ExchangeACombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Binance";
        var exchangeB = (ExchangeBCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "KuCoin";

        PreviewSymbol.Text = $"{baseAsset}/{quoteAsset}";
        PreviewExchanges.Text = $"{exchangeA} -> {exchangeB}";
        PreviewIcon.Text = baseAsset.Length > 0 ? baseAsset[0].ToString() : "?";

        // Validate exchanges are different
        if (exchangeA == exchangeB)
        {
            ShowValidation("Buy and Sell exchanges must be different!");
        }
        else
        {
            HideValidation();
        }
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
        var exchangeA = (ExchangeACombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Binance";
        var exchangeB = (ExchangeBCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "KuCoin";

        // Validate
        if (exchangeA == exchangeB)
        {
            ShowValidation("Buy and Sell exchanges must be different!");
            return;
        }

        if (!decimal.TryParse(TradeAmountInput.Text, out var tradeAmount) || tradeAmount <= 0)
        {
            ShowValidation("Please enter a valid trade amount!");
            return;
        }

        Result = new TradingPairConfig
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

        DialogResultOk = true;
        Close();
    }
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
