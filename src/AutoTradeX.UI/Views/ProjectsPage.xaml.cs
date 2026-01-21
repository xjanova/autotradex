using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;

namespace AutoTradeX.UI.Views;

public partial class ProjectsPage : UserControl
{
    private readonly IProjectService? _projectService;
    private readonly IStrategyService? _strategyService;
    private readonly ILoggingService? _logger;
    private bool _isInitialized = false;
    private bool _isLoading = false;

    public ObservableCollection<ProjectDisplayItem> Projects { get; } = new();

    public ProjectsPage()
    {
        InitializeComponent();
        DataContext = this;

        _projectService = App.Services?.GetService<IProjectService>();
        _strategyService = App.Services?.GetService<IStrategyService>();
        _logger = App.Services?.GetService<ILoggingService>();

        Loaded += ProjectsPage_Loaded;
    }

    private async void ProjectsPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Prevent multiple loads
        if (_isInitialized || _isLoading) return;

        _isLoading = true;
        try
        {
            await LoadProjectsAsync();
            _isInitialized = true;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task LoadProjectsAsync()
    {
        Projects.Clear();

        if (_projectService == null)
        {
            _logger?.LogWarning("ProjectsPage", "Project service not available");
            // Don't add demo data - let user know service is unavailable
        }
        else
        {
            try
            {
                var projects = await _projectService.GetAllProjectsAsync();
                foreach (var project in projects)
                {
                    var strategyName = await GetStrategyNameAsync(project.DefaultStrategyId);
                    Projects.Add(new ProjectDisplayItem(project, strategyName));
                }
                _logger?.LogInfo("ProjectsPage", $"Loaded {projects.Count()} projects from database");
            }
            catch (Exception ex)
            {
                _logger?.LogError("ProjectsPage", $"Error loading projects: {ex.Message}");
            }
        }

        ProjectsList.ItemsSource = Projects;
        UpdateEmptyState();
    }

    private async Task<string> GetStrategyNameAsync(string strategyId)
    {
        if (_strategyService == null || string.IsNullOrEmpty(strategyId))
            return "Default";

        var strategy = await _strategyService.GetStrategyAsync(strategyId);
        return strategy?.Name ?? "Default";
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = Projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void NewProjectButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProjectEditDialog();
        if (dialog.ShowDialog() == true)
        {
            var newProject = new TradingProject
            {
                Name = dialog.ProjectName,
                Description = dialog.ProjectDescription
            };

            if (_projectService != null)
            {
                await _projectService.SaveProjectAsync(newProject);
            }

            Projects.Add(new ProjectDisplayItem(newProject, "Default"));
            UpdateEmptyState();

            _logger?.LogInfo("Projects", $"Created new project: {newProject.Name}");
        }
    }

    private async void EditProjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string projectId)
        {
            var displayItem = Projects.FirstOrDefault(p => p.Project.Id == projectId);
            if (displayItem == null) return;

            var dialog = new ProjectEditDialog(displayItem.Project.Name, displayItem.Project.Description);
            if (dialog.ShowDialog() == true)
            {
                displayItem.Project.Name = dialog.ProjectName;
                displayItem.Project.Description = dialog.ProjectDescription;
                displayItem.Name = dialog.ProjectName;
                displayItem.Description = dialog.ProjectDescription;

                if (_projectService != null)
                {
                    await _projectService.SaveProjectAsync(displayItem.Project);
                }

                ProjectsList.Items.Refresh();
                _logger?.LogInfo("Projects", $"Updated project: {displayItem.Project.Name}");
            }
        }
    }

    private async void DeleteProjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string projectId)
        {
            var displayItem = Projects.FirstOrDefault(p => p.Project.Id == projectId);
            if (displayItem == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete '{displayItem.Project.Name}'?\nThis will remove all trading pairs in this project.",
                "Delete Project",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            if (_projectService != null)
            {
                await _projectService.DeleteProjectAsync(projectId);
            }

            Projects.Remove(displayItem);
            UpdateEmptyState();

            _logger?.LogInfo("Projects", $"Deleted project: {displayItem.Project.Name}");
        }
    }

    private async void ChangeStrategyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string projectId)
        {
            var displayItem = Projects.FirstOrDefault(p => p.Project.Id == projectId);
            if (displayItem == null) return;

            var strategies = _strategyService != null
                ? (await _strategyService.GetAllStrategiesAsync()).ToList()
                : new Infrastructure.Services.StrategyService().GetPresetStrategies().ToList();

            var dialog = new StrategySelectDialog(strategies, displayItem.Project.DefaultStrategyId);
            if (dialog.ShowDialog() == true && dialog.SelectedStrategy != null)
            {
                displayItem.Project.DefaultStrategyId = dialog.SelectedStrategy.Id;
                displayItem.DefaultStrategyName = dialog.SelectedStrategy.Name;

                if (_projectService != null)
                {
                    await _projectService.SaveProjectAsync(displayItem.Project);
                }

                ProjectsList.Items.Refresh();
                _logger?.LogInfo("Projects", $"Changed default strategy for {displayItem.Project.Name}");
            }
        }
    }

    private async void AddPairButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string projectId)
        {
            var displayItem = Projects.FirstOrDefault(p => p.Project.Id == projectId);
            if (displayItem == null) return;

            if (displayItem.Project.TradingPairs.Count >= 10)
            {
                MessageBox.Show("Maximum 10 trading pairs per project reached.",
                    "Limit Reached", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var strategies = _strategyService != null
                ? (await _strategyService.GetAllStrategiesAsync()).ToList()
                : new Infrastructure.Services.StrategyService().GetPresetStrategies().ToList();

            var dialog = new AddPairDialog(strategies);
            if (dialog.ShowDialog() == true)
            {
                var newPair = new ProjectTradingPair
                {
                    Symbol = dialog.Symbol,
                    ExchangeA = dialog.ExchangeA,
                    ExchangeB = dialog.ExchangeB,
                    StrategyId = dialog.SelectedStrategyId,
                    TradeAmount = dialog.TradeAmount,
                    IsEnabled = true
                };

                if (_projectService != null)
                {
                    await _projectService.AddTradingPairAsync(projectId, newPair);
                }
                else
                {
                    displayItem.Project.TradingPairs.Add(newPair);
                }

                // Refresh the display
                var strategyName = await GetStrategyNameAsync(newPair.StrategyId);
                displayItem.TradingPairs.Add(new TradingPairDisplayItem(newPair, strategyName));
                displayItem.UpdateCanAddMore();

                ProjectsList.Items.Refresh();
                _logger?.LogInfo("Projects", $"Added pair {newPair.Symbol} to {displayItem.Project.Name}");
            }
        }
    }

    private async void EditPairButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ProjectTradingPair pair)
        {
            var parentProject = Projects.FirstOrDefault(p =>
                p.Project.TradingPairs.Any(tp => tp.Symbol == pair.Symbol && tp.ExchangeA == pair.ExchangeA));

            if (parentProject == null) return;

            var strategies = _strategyService != null
                ? (await _strategyService.GetAllStrategiesAsync()).ToList()
                : new Infrastructure.Services.StrategyService().GetPresetStrategies().ToList();

            var dialog = new AddPairDialog(strategies, pair);
            if (dialog.ShowDialog() == true)
            {
                pair.Symbol = dialog.Symbol;
                pair.ExchangeA = dialog.ExchangeA;
                pair.ExchangeB = dialog.ExchangeB;
                pair.StrategyId = dialog.SelectedStrategyId;
                pair.TradeAmount = dialog.TradeAmount;

                if (_projectService != null)
                {
                    await _projectService.SaveProjectAsync(parentProject.Project);
                }

                // Refresh display
                var pairDisplay = parentProject.TradingPairs.FirstOrDefault(tp =>
                    tp.Pair.Symbol == pair.Symbol && tp.Pair.ExchangeA == pair.ExchangeA);
                if (pairDisplay != null)
                {
                    pairDisplay.StrategyName = await GetStrategyNameAsync(pair.StrategyId);
                }

                ProjectsList.Items.Refresh();
                _logger?.LogInfo("Projects", $"Updated pair {pair.Symbol}");
            }
        }
    }

    private async void RemovePairButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ProjectTradingPair pair)
        {
            var parentProject = Projects.FirstOrDefault(p =>
                p.Project.TradingPairs.Contains(pair));

            if (parentProject == null) return;

            var result = MessageBox.Show(
                $"Remove {pair.Symbol} ({pair.ExchangeA} ↔ {pair.ExchangeB}) from this project?",
                "Remove Trading Pair",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            if (_projectService != null)
            {
                await _projectService.RemoveTradingPairAsync(parentProject.Project.Id, pair.Id);
            }
            else
            {
                parentProject.Project.TradingPairs.Remove(pair);
            }

            var pairDisplay = parentProject.TradingPairs.FirstOrDefault(tp => tp.Pair == pair);
            if (pairDisplay != null)
            {
                parentProject.TradingPairs.Remove(pairDisplay);
            }

            parentProject.UpdateCanAddMore();
            ProjectsList.Items.Refresh();

            _logger?.LogInfo("Projects", $"Removed pair {pair.Symbol} from {parentProject.Project.Name}");
        }
    }
}

public class ProjectDisplayItem : System.ComponentModel.INotifyPropertyChanged
{
    public TradingProject Project { get; set; }

    // Expose Project.Id for binding
    public string Id => Project.Id;

    private string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(nameof(Name)); }
    }

    private string _description = "";
    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(nameof(Description)); }
    }

    private string _defaultStrategyName = "Default";
    public string DefaultStrategyName
    {
        get => _defaultStrategyName;
        set { _defaultStrategyName = value; OnPropertyChanged(nameof(DefaultStrategyName)); }
    }

    public bool IsActive => Project.IsActive;

    private bool _canAddMorePairs = true;
    public bool CanAddMorePairs
    {
        get => _canAddMorePairs;
        set { _canAddMorePairs = value; OnPropertyChanged(nameof(CanAddMorePairs)); }
    }

    public ObservableCollection<TradingPairDisplayItem> TradingPairs { get; } = new();

    public ProjectDisplayItem(TradingProject project, string defaultStrategyName)
    {
        Project = project;
        _name = project.Name;
        _description = project.Description;
        _defaultStrategyName = defaultStrategyName;

        foreach (var pair in project.TradingPairs)
        {
            TradingPairs.Add(new TradingPairDisplayItem(pair, ""));
        }

        UpdateCanAddMore();
    }

    public void UpdateCanAddMore()
    {
        CanAddMorePairs = Project.TradingPairs.Count < 10;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public class TradingPairDisplayItem : System.ComponentModel.INotifyPropertyChanged
{
    public ProjectTradingPair Pair { get; set; }

    public string Symbol => Pair.Symbol;
    public string ExchangeA => Pair.ExchangeA;
    public string ExchangeB => Pair.ExchangeB;
    public decimal TradeAmount => Pair.TradeAmount;

    private string _strategyName = "";
    public string StrategyName
    {
        get => _strategyName;
        set { _strategyName = value; OnPropertyChanged(nameof(StrategyName)); }
    }

    public string StatusText => Pair.IsEnabled ? "Active" : "Paused";
    public Brush StatusBackground => new SolidColorBrush(Pair.IsEnabled
        ? Color.FromArgb(0x30, 0x10, 0xB9, 0x81)
        : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
    public Brush StatusForeground => new SolidColorBrush(Pair.IsEnabled
        ? (Color)ColorConverter.ConvertFromString("#10B981")
        : (Color)ColorConverter.ConvertFromString("#80FFFFFF"));

    public TradingPairDisplayItem(ProjectTradingPair pair, string strategyName)
    {
        Pair = pair;
        _strategyName = string.IsNullOrEmpty(strategyName) ? "Default" : strategyName;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}
