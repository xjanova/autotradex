using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AutoTradeX.UI.Views;

public partial class SelectProjectDialog : Window
{
    public bool DialogResultOk { get; private set; } = false;
    public TradingProject? SelectedProject { get; private set; }
    public string? SelectedStrategyId { get; private set; }
    public decimal TradeAmount { get; private set; } = 100m;

    private readonly ScanResultDisplay _result;
    private readonly List<TradingStrategy> _strategies = new();
    public ObservableCollection<ProjectSelectItem> Projects { get; } = new();

    public SelectProjectDialog(IEnumerable<TradingProject> projects, ScanResultDisplay result)
    {
        InitializeComponent();
        _result = result;
        DataContext = this;

        LoadCoinInfo();
        LoadProjects(projects);
        LoadStrategies();
    }

    private void LoadCoinInfo()
    {
        CoinIconDisplay.Symbol = _result.BaseAsset;
        SymbolText.Text = _result.Symbol;
        ExchangeText.Text = $"{_result.BestBuyExchange} → {_result.BestSellExchange}";
        SpreadText.Text = $"{_result.SpreadPercent:F3}%";
    }

    private void LoadProjects(IEnumerable<TradingProject> projects)
    {
        Projects.Clear();
        foreach (var project in projects)
        {
            var pairCount = project.TradingPairs?.Count ?? 0;
            var isFull = pairCount >= 10;

            Projects.Add(new ProjectSelectItem
            {
                Project = project,
                Name = project.Name,
                Description = project.Description ?? "No description",
                PairCount = pairCount,
                PairCountDisplay = $"{pairCount}/10 pairs",
                IsFull = isFull,
                StatusText = isFull ? "FULL" : (pairCount > 0 ? "Active" : "Empty"),
                StatusColor = new SolidColorBrush(isFull
                    ? (Color)ColorConverter.ConvertFromString("#EF4444")
                    : pairCount > 0
                        ? (Color)ColorConverter.ConvertFromString("#10B981")
                        : (Color)ColorConverter.ConvertFromString("#60FFFFFF"))
            });
        }

        ProjectList.ItemsSource = Projects;
    }

    private void LoadStrategies()
    {
        var strategyService = App.Services?.GetService<IStrategyService>();
        if (strategyService != null)
        {
            _strategies.AddRange(strategyService.GetPresetStrategies());
        }
        else
        {
            // Fallback to default strategies
            _strategies.Add(new TradingStrategy { Id = "default", Name = "Default Strategy" });
        }

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

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void ProjectRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radio && radio.Tag is ProjectSelectItem item)
        {
            if (item.IsFull)
            {
                radio.IsChecked = false;
                MessageBox.Show(
                    $"โปรเจค \"{item.Name}\" มีคู่เทรดครบ 10 คู่แล้ว\n\nกรุณาเลือกโปรเจคอื่นหรือลบคู่เทรดที่ไม่ใช้ออกก่อน",
                    "โปรเจคเต็ม",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            SelectedProject = item.Project;
            ConfirmButton.IsEnabled = true;
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProject == null)
        {
            MessageBox.Show("กรุณาเลือกโปรเจค", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Get trade amount
        if (!decimal.TryParse(TradeAmountInput.Text, out var amount) || amount <= 0)
        {
            MessageBox.Show("กรุณากรอก Trade Amount ที่ถูกต้อง", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TradeAmount = amount;

        // Get strategy
        if (StrategyCombo.SelectedItem is ComboBoxItem selectedStrategy)
        {
            SelectedStrategyId = selectedStrategy.Tag?.ToString();
        }

        DialogResultOk = true;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResultOk = false;
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResultOk = false;
        DialogResult = false;
        Close();
    }
}

public class ProjectSelectItem
{
    public TradingProject Project { get; set; } = null!;
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int PairCount { get; set; }
    public string PairCountDisplay { get; set; } = "0/10 pairs";
    public bool IsFull { get; set; }
    public string StatusText { get; set; } = "";
    public Brush StatusColor { get; set; } = Brushes.Gray;
}
