using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using AutoTradeX.Core.Models;

namespace AutoTradeX.UI.Views;

public partial class StrategySelectDialog : Window
{
    public TradingStrategy? SelectedStrategy { get; private set; }
    public ObservableCollection<StrategySelectionItem> Strategies { get; } = new();

    public StrategySelectDialog(IEnumerable<TradingStrategy> strategies, string? currentStrategyId = null)
    {
        InitializeComponent();
        DataContext = this;

        foreach (var strategy in strategies)
        {
            var item = new StrategySelectionItem(strategy)
            {
                IsSelected = strategy.Id == currentStrategyId
            };
            Strategies.Add(item);
        }

        // Select first if none selected
        if (!Strategies.Any(s => s.IsSelected) && Strategies.Count > 0)
        {
            Strategies[0].IsSelected = true;
        }

        StrategyList.ItemsSource = Strategies;
    }

    private void StrategyCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is StrategySelectionItem item)
        {
            foreach (var s in Strategies)
            {
                s.IsSelected = false;
            }
            item.IsSelected = true;
        }
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedItem = Strategies.FirstOrDefault(s => s.IsSelected);
        if (selectedItem != null)
        {
            SelectedStrategy = selectedItem.Strategy;
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show("Please select a strategy.", "No Selection",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public class StrategySelectionItem : System.ComponentModel.INotifyPropertyChanged
{
    public TradingStrategy Strategy { get; }

    public string Name => Strategy.Name;
    public string Description => Strategy.Description;
    public AISettings AI => Strategy.AI;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));
        }
    }

    public StrategySelectionItem(TradingStrategy strategy)
    {
        Strategy = strategy;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}
