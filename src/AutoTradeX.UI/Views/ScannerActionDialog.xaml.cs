using System.Windows;
using System.Windows.Input;

namespace AutoTradeX.UI.Views;

public enum ScannerAction
{
    None,
    AddToProject,
    ExecuteTrade,
    ViewDetails
}

public partial class ScannerActionDialog : Window
{
    public ScannerAction SelectedAction { get; private set; } = ScannerAction.None;
    private readonly ScanResultDisplay _result;

    public ScannerActionDialog(ScanResultDisplay result)
    {
        InitializeComponent();
        _result = result;
        LoadCoinInfo();
    }

    private void LoadCoinInfo()
    {
        CoinIconDisplay.Symbol = _result.BaseAsset;
        SymbolText.Text = _result.Symbol;
        BuyExchangeText.Text = _result.BestBuyExchange;
        SellExchangeText.Text = _result.BestSellExchange;
        ScoreText.Text = $"{_result.Score:F0}";
        SpreadText.Text = $"{_result.SpreadPercent:F3}%";
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void AddToProjectButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedAction = ScannerAction.AddToProject;
        DialogResult = true;
        Close();
    }

    private void ExecuteTradeButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedAction = ScannerAction.ExecuteTrade;
        DialogResult = true;
        Close();
    }

    private void ViewDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        // Show detailed information
        var message = $"📊 รายละเอียด {_result.Symbol}\n\n" +
                      $"═══════════════════════════\n\n" +
                      $"💰 ราคาปัจจุบัน: {_result.CurrentPriceDisplay}\n" +
                      $"📈 เปลี่ยนแปลง 24h: {_result.PriceChangeDisplay}\n\n" +
                      $"═══════════════════════════\n\n" +
                      $"🏪 ซื้อจาก: {_result.BestBuyExchange}\n" +
                      $"   ราคา: ${_result.BestBuyPrice:N4}\n\n" +
                      $"🏪 ขายที่: {_result.BestSellExchange}\n" +
                      $"   ราคา: ${_result.BestSellPrice:N4}\n\n" +
                      $"═══════════════════════════\n\n" +
                      $"📊 Spread: {_result.SpreadPercent:F3}%\n" +
                      $"💵 Est. Profit: ${_result.EstimatedProfit:F2} (per $1000)\n" +
                      $"⭐ Score: {_result.Score:F0}\n" +
                      $"📝 Reason: {_result.ScoreReason}";

        MessageBox.Show(message, $"รายละเอียด {_result.Symbol}", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedAction = ScannerAction.None;
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedAction = ScannerAction.None;
        DialogResult = false;
        Close();
    }
}
