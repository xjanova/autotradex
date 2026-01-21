using System.Text.Json;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;

namespace AutoTradeX.Infrastructure.Services;

/// <summary>
/// Strategy validation result
/// </summary>
public class StrategyValidationResult
{
    public bool IsValid { get; set; } = true;
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Implementation of IStrategyService for managing trading strategies
/// </summary>
public class StrategyService : IStrategyService
{
    private readonly string _strategiesPath;
    private readonly ILoggingService? _logger;
    private List<TradingStrategy> _strategies = new();
    private bool _isLoaded = false;

    public StrategyService(ILoggingService? logger = null)
    {
        _logger = logger;
        _strategiesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoTradeX", "strategies.json");

        // Ensure directory exists
        var dir = Path.GetDirectoryName(_strategiesPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    private async Task EnsureLoadedAsync()
    {
        if (_isLoaded) return;

        try
        {
            if (File.Exists(_strategiesPath))
            {
                var json = await File.ReadAllTextAsync(_strategiesPath);
                _strategies = JsonSerializer.Deserialize<List<TradingStrategy>>(json) ?? new();
            }
            else
            {
                // Create default strategies
                _strategies = GetPresetStrategies().ToList();
                await SaveAllAsync();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("StrategyService", $"Error loading strategies: {ex.Message}");
            _strategies = GetPresetStrategies().ToList();
        }

        _isLoaded = true;
    }

    private async Task SaveAllAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_strategies, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_strategiesPath, json);
        }
        catch (Exception ex)
        {
            _logger?.LogError("StrategyService", $"Error saving strategies: {ex.Message}");
        }
    }

    public async Task<IEnumerable<TradingStrategy>> GetAllStrategiesAsync()
    {
        await EnsureLoadedAsync();
        return _strategies.ToList();
    }

    public async Task<TradingStrategy?> GetStrategyAsync(string id)
    {
        await EnsureLoadedAsync();
        return _strategies.FirstOrDefault(s => s.Id == id);
    }

    public async Task<bool> SaveStrategyAsync(TradingStrategy strategy)
    {
        await EnsureLoadedAsync();

        // Validate strategy before saving
        var validation = ValidateStrategy(strategy);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
                _logger?.LogError("StrategyService", $"Validation error: {error}");
            return false;
        }

        // Log warnings but allow save
        foreach (var warning in validation.Warnings)
            _logger?.LogWarning("StrategyService", $"Validation warning: {warning}");

        var existing = _strategies.FindIndex(s => s.Id == strategy.Id);
        if (existing >= 0)
        {
            strategy.UpdatedAt = DateTime.UtcNow;
            _strategies[existing] = strategy;
        }
        else
        {
            strategy.CreatedAt = DateTime.UtcNow;
            strategy.UpdatedAt = DateTime.UtcNow;
            _strategies.Add(strategy);
        }

        await SaveAllAsync();
        _logger?.LogInfo("StrategyService", $"Strategy saved: {strategy.Name}");
        return true;
    }

    /// <summary>
    /// Validate a trading strategy for logical consistency
    /// </summary>
    public StrategyValidationResult ValidateStrategy(TradingStrategy strategy)
    {
        var result = new StrategyValidationResult();

        // Entry Conditions Validation
        if (strategy.Entry.MinSpreadPercent < 0)
            result.Errors.Add("Min spread cannot be negative");

        if (strategy.Entry.MaxSpreadPercent < strategy.Entry.MinSpreadPercent)
            result.Errors.Add($"Max spread ({strategy.Entry.MaxSpreadPercent}%) must be greater than min spread ({strategy.Entry.MinSpreadPercent}%)");

        if (strategy.Entry.MinSpreadPercent < 0.01m)
            result.Warnings.Add("Min spread below 0.01% may result in unprofitable trades after fees");

        if (strategy.Entry.MaxSpreadPercent > 20m)
            result.Warnings.Add("Max spread above 20% may indicate price manipulation - trades could be risky");

        if (strategy.Entry.MinVolume24h < 10000m)
            result.Warnings.Add("Low volume threshold may result in slippage issues");

        if (strategy.Entry.SpreadConfirmationSeconds < 1)
            result.Errors.Add("Spread confirmation time must be at least 1 second");

        // Exit Conditions Validation
        if (strategy.Exit.TakeProfitPercent <= 0)
            result.Errors.Add("Take profit must be greater than 0%");

        if (strategy.Exit.StopLossPercent <= 0)
            result.Errors.Add("Stop loss must be greater than 0%");

        if (strategy.Exit.StopLossPercent >= strategy.Exit.TakeProfitPercent)
            result.Warnings.Add("Stop loss is greater than or equal to take profit - consider adjusting for better risk/reward ratio");

        if (strategy.Exit.EnableTrailingStop)
        {
            if (strategy.Exit.TrailingStopActivation <= 0)
                result.Errors.Add("Trailing stop activation must be greater than 0%");

            if (strategy.Exit.TrailingStopDistance <= 0)
                result.Errors.Add("Trailing stop distance must be greater than 0%");

            if (strategy.Exit.TrailingStopDistance >= strategy.Exit.TrailingStopActivation)
                result.Warnings.Add("Trailing stop distance should be less than activation threshold");
        }

        if (strategy.Exit.MaxHoldTimeMinutes < 1)
            result.Warnings.Add("Max hold time less than 1 minute may cause premature exits");

        // Risk Management Validation
        if (strategy.Risk.MaxPositionSize <= 0)
            result.Errors.Add("Max position size must be greater than 0");

        if (strategy.Risk.MaxBalancePercentPerTrade <= 0 || strategy.Risk.MaxBalancePercentPerTrade > 100)
            result.Errors.Add("Max balance percent per trade must be between 0% and 100%");

        if (strategy.Risk.MaxBalancePercentPerTrade > 50)
            result.Warnings.Add("Risking more than 50% of balance per trade is extremely risky");

        if (strategy.Risk.MaxDailyLoss <= 0)
            result.Errors.Add("Max daily loss must be greater than 0");

        if (strategy.Risk.MaxConsecutiveLosses < 1)
            result.Errors.Add("Max consecutive losses must be at least 1");

        if (strategy.Risk.MaxOpenPositions < 1)
            result.Errors.Add("Max open positions must be at least 1");

        if (strategy.Risk.MaxOpenPositions > 10)
            result.Warnings.Add("More than 10 open positions may be difficult to manage");

        if (strategy.Risk.MaxTradesPerHour < 1)
            result.Errors.Add("Max trades per hour must be at least 1");

        if (strategy.Risk.MaxTradesPerHour > 100)
            result.Warnings.Add("More than 100 trades per hour may indicate over-trading");

        // AI Settings Validation
        if (strategy.AI.EnableAI)
        {
            if (strategy.AI.MinConfidenceScore < 0 || strategy.AI.MinConfidenceScore > 100)
                result.Errors.Add("AI confidence score must be between 0 and 100");

            if (strategy.AI.MinConfidenceScore < 50)
                result.Warnings.Add("AI confidence threshold below 50% may result in unreliable signals");
        }

        // Advanced Settings Validation
        if (strategy.Advanced.EnableSlippageProtection)
        {
            if (strategy.Advanced.MaxSlippagePercent <= 0)
                result.Errors.Add("Max slippage must be greater than 0%");

            if (strategy.Advanced.MaxSlippagePercent > 5)
                result.Warnings.Add("Max slippage above 5% may result in significant losses");
        }

        if (strategy.Advanced.OrderTimeoutSeconds < 1)
            result.Errors.Add("Order timeout must be at least 1 second");

        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    public async Task<bool> DeleteStrategyAsync(string id)
    {
        await EnsureLoadedAsync();

        var strategy = _strategies.FirstOrDefault(s => s.Id == id);
        if (strategy == null) return false;

        _strategies.Remove(strategy);
        await SaveAllAsync();
        _logger?.LogInfo("StrategyService", $"Strategy deleted: {strategy.Name}");
        return true;
    }

    public async Task<TradingStrategy> GetDefaultStrategyAsync()
    {
        await EnsureLoadedAsync();
        return _strategies.FirstOrDefault(s => s.Name == "Balanced") ?? GetPresetStrategies().First();
    }

    public async Task<TradingStrategy> CloneStrategyAsync(string id, string newName)
    {
        await EnsureLoadedAsync();

        var original = await GetStrategyAsync(id);
        if (original == null)
            return new TradingStrategy { Name = newName };

        // Deep clone using JSON
        var json = JsonSerializer.Serialize(original);
        var clone = JsonSerializer.Deserialize<TradingStrategy>(json) ?? new();

        clone.Id = Guid.NewGuid().ToString();
        clone.Name = newName;
        clone.CreatedAt = DateTime.UtcNow;
        clone.UpdatedAt = DateTime.UtcNow;

        await SaveStrategyAsync(clone);
        return clone;
    }

    public async Task<string> ExportStrategyAsync(string id)
    {
        var strategy = await GetStrategyAsync(id);
        if (strategy == null) return "{}";

        return JsonSerializer.Serialize(strategy, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<TradingStrategy?> ImportStrategyAsync(string json)
    {
        try
        {
            var strategy = JsonSerializer.Deserialize<TradingStrategy>(json);
            if (strategy == null) return null;

            strategy.Id = Guid.NewGuid().ToString();
            strategy.CreatedAt = DateTime.UtcNow;
            strategy.UpdatedAt = DateTime.UtcNow;

            await SaveStrategyAsync(strategy);
            return strategy;
        }
        catch (Exception ex)
        {
            _logger?.LogError("StrategyService", $"Error importing strategy: {ex.Message}");
            return null;
        }
    }

    public IEnumerable<TradingStrategy> GetPresetStrategies()
    {
        return new List<TradingStrategy>
        {
            new TradingStrategy
            {
                Id = "preset-conservative",
                Name = "Conservative",
                Description = "Low risk, stable returns. Best for beginners.",
                Entry = new EntryConditions
                {
                    MinSpreadPercent = 0.25m,
                    MaxSpreadPercent = 3.0m,
                    MinVolume24h = 200000m,
                    SpreadConfirmationSeconds = 5,
                    RequiredConfirmations = 3,
                    CheckMomentum = true,
                    AvoidHighVolatility = true,
                    MaxVolatilityPercent = 1.5m
                },
                Exit = new ExitConditions
                {
                    TakeProfitPercent = 0.3m,
                    StopLossPercent = 0.2m,
                    MaxHoldTimeMinutes = 15,
                    EnableTrailingStop = false
                },
                Risk = new RiskManagement
                {
                    MaxPositionSize = 500m,
                    MaxBalancePercentPerTrade = 5m,
                    MaxDailyLoss = 50m,
                    MaxConsecutiveLosses = 2,
                    MaxOpenPositions = 2,
                    MaxTradesPerHour = 5
                },
                AI = new AISettings
                {
                    EnableAI = true,
                    MinConfidenceScore = 80,
                    ModelType = AIModelType.Conservative
                }
            },
            new TradingStrategy
            {
                Id = "preset-balanced",
                Name = "Balanced",
                Description = "Balanced risk/reward. Recommended for most users.",
                Entry = new EntryConditions
                {
                    MinSpreadPercent = 0.15m,
                    MaxSpreadPercent = 5.0m,
                    MinVolume24h = 100000m,
                    SpreadConfirmationSeconds = 3,
                    RequiredConfirmations = 2,
                    CheckMomentum = true,
                    AvoidHighVolatility = true,
                    MaxVolatilityPercent = 2.0m
                },
                Exit = new ExitConditions
                {
                    TakeProfitPercent = 0.5m,
                    StopLossPercent = 0.3m,
                    MaxHoldTimeMinutes = 30,
                    EnableTrailingStop = false
                },
                Risk = new RiskManagement
                {
                    MaxPositionSize = 1000m,
                    MaxBalancePercentPerTrade = 10m,
                    MaxDailyLoss = 100m,
                    MaxConsecutiveLosses = 3,
                    MaxOpenPositions = 3,
                    MaxTradesPerHour = 10
                },
                AI = new AISettings
                {
                    EnableAI = true,
                    MinConfidenceScore = 70,
                    ModelType = AIModelType.Balanced
                }
            },
            new TradingStrategy
            {
                Id = "preset-aggressive",
                Name = "Aggressive",
                Description = "Higher risk, higher potential returns. For experienced traders.",
                Entry = new EntryConditions
                {
                    MinSpreadPercent = 0.10m,
                    MaxSpreadPercent = 10.0m,
                    MinVolume24h = 50000m,
                    SpreadConfirmationSeconds = 2,
                    RequiredConfirmations = 1,
                    CheckMomentum = true,
                    AvoidHighVolatility = false,
                    MaxVolatilityPercent = 5.0m
                },
                Exit = new ExitConditions
                {
                    TakeProfitPercent = 1.0m,
                    StopLossPercent = 0.5m,
                    MaxHoldTimeMinutes = 60,
                    EnableTrailingStop = true,
                    TrailingStopActivation = 0.5m,
                    TrailingStopDistance = 0.2m
                },
                Risk = new RiskManagement
                {
                    MaxPositionSize = 2000m,
                    MaxBalancePercentPerTrade = 20m,
                    MaxDailyLoss = 200m,
                    MaxConsecutiveLosses = 5,
                    MaxOpenPositions = 5,
                    MaxTradesPerHour = 20
                },
                AI = new AISettings
                {
                    EnableAI = true,
                    MinConfidenceScore = 60,
                    ModelType = AIModelType.Aggressive
                }
            },
            new TradingStrategy
            {
                Id = "preset-scalping",
                Name = "Scalping",
                Description = "Very fast trades, small profits. Requires low latency.",
                Entry = new EntryConditions
                {
                    MinSpreadPercent = 0.05m,
                    MaxSpreadPercent = 1.0m,
                    MinVolume24h = 500000m,
                    SpreadConfirmationSeconds = 1,
                    RequiredConfirmations = 1,
                    CheckMomentum = false,
                    AvoidHighVolatility = true,
                    MaxVolatilityPercent = 1.0m
                },
                Exit = new ExitConditions
                {
                    TakeProfitPercent = 0.15m,
                    StopLossPercent = 0.1m,
                    MaxHoldTimeMinutes = 5,
                    EnableTrailingStop = false
                },
                Risk = new RiskManagement
                {
                    MaxPositionSize = 500m,
                    MaxBalancePercentPerTrade = 5m,
                    MaxDailyLoss = 50m,
                    MaxConsecutiveLosses = 5,
                    MaxOpenPositions = 1,
                    MaxTradesPerHour = 50,
                    MinTimeBetweenTrades = 10
                },
                AI = new AISettings
                {
                    EnableAI = true,
                    MinConfidenceScore = 65,
                    ModelType = AIModelType.ScalpingMode
                },
                Advanced = new AdvancedSettings
                {
                    EnableSlippageProtection = true,
                    MaxSlippagePercent = 0.05m,
                    OrderTimeoutSeconds = 5
                }
            }
        };
    }
}

/// <summary>
/// Implementation of IProjectService for managing trading projects
/// </summary>
public class ProjectService : IProjectService
{
    private readonly string _projectsPath;
    private readonly ILoggingService? _logger;
    private List<TradingProject> _projects = new();
    private string _activeProjectId = "";
    private bool _isLoaded = false;

    private const int MaxPairsPerProject = 10;

    public ProjectService(ILoggingService? logger = null)
    {
        _logger = logger;
        _projectsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoTradeX", "projects.json");

        var dir = Path.GetDirectoryName(_projectsPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    private async Task EnsureLoadedAsync()
    {
        if (_isLoaded) return;

        try
        {
            if (File.Exists(_projectsPath))
            {
                var json = await File.ReadAllTextAsync(_projectsPath);
                var data = JsonSerializer.Deserialize<ProjectData>(json);
                if (data != null)
                {
                    _projects = data.Projects ?? new();
                    _activeProjectId = data.ActiveProjectId ?? "";
                }
            }

            if (_projects.Count == 0)
            {
                // Create default project
                var defaultProject = new TradingProject
                {
                    Name = "Main Project",
                    Description = "Default trading project",
                    IsActive = true
                };
                _projects.Add(defaultProject);
                _activeProjectId = defaultProject.Id;
                await SaveAllAsync();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("ProjectService", $"Error loading projects: {ex.Message}");
            _projects = new List<TradingProject>
            {
                new TradingProject { Name = "Main Project", IsActive = true }
            };
            _activeProjectId = _projects[0].Id;
        }

        _isLoaded = true;
    }

    private async Task SaveAllAsync()
    {
        try
        {
            var data = new ProjectData
            {
                Projects = _projects,
                ActiveProjectId = _activeProjectId
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_projectsPath, json);
        }
        catch (Exception ex)
        {
            _logger?.LogError("ProjectService", $"Error saving projects: {ex.Message}");
        }
    }

    public async Task<IEnumerable<TradingProject>> GetAllProjectsAsync()
    {
        await EnsureLoadedAsync();
        return _projects.ToList();
    }

    public async Task<TradingProject?> GetProjectAsync(string id)
    {
        await EnsureLoadedAsync();
        return _projects.FirstOrDefault(p => p.Id == id);
    }

    public async Task<TradingProject?> GetActiveProjectAsync()
    {
        await EnsureLoadedAsync();
        return _projects.FirstOrDefault(p => p.Id == _activeProjectId)
            ?? _projects.FirstOrDefault();
    }

    public async Task<bool> SaveProjectAsync(TradingProject project)
    {
        await EnsureLoadedAsync();

        var existing = _projects.FindIndex(p => p.Id == project.Id);
        if (existing >= 0)
        {
            project.UpdatedAt = DateTime.UtcNow;
            _projects[existing] = project;
        }
        else
        {
            project.CreatedAt = DateTime.UtcNow;
            project.UpdatedAt = DateTime.UtcNow;
            _projects.Add(project);
        }

        await SaveAllAsync();
        _logger?.LogInfo("ProjectService", $"Project saved: {project.Name}");
        return true;
    }

    public async Task<bool> DeleteProjectAsync(string id)
    {
        await EnsureLoadedAsync();

        if (_projects.Count <= 1)
        {
            _logger?.LogWarning("ProjectService", "Cannot delete the last project");
            return false;
        }

        var project = _projects.FirstOrDefault(p => p.Id == id);
        if (project == null) return false;

        _projects.Remove(project);

        if (_activeProjectId == id)
            _activeProjectId = _projects.First().Id;

        await SaveAllAsync();
        _logger?.LogInfo("ProjectService", $"Project deleted: {project.Name}");
        return true;
    }

    public async Task<bool> SetActiveProjectAsync(string id)
    {
        await EnsureLoadedAsync();

        if (!_projects.Any(p => p.Id == id)) return false;

        _activeProjectId = id;
        await SaveAllAsync();
        _logger?.LogInfo("ProjectService", $"Active project set to: {id}");
        return true;
    }

    public async Task<bool> AddTradingPairAsync(string projectId, ProjectTradingPair pair)
    {
        await EnsureLoadedAsync();

        var project = _projects.FirstOrDefault(p => p.Id == projectId);
        if (project == null) return false;

        if (project.TradingPairs.Count >= MaxPairsPerProject)
        {
            _logger?.LogWarning("ProjectService", $"Cannot add more pairs. Maximum is {MaxPairsPerProject}");
            return false;
        }

        project.TradingPairs.Add(pair);
        project.UpdatedAt = DateTime.UtcNow;
        await SaveAllAsync();
        return true;
    }

    public async Task<bool> RemoveTradingPairAsync(string projectId, string pairId)
    {
        await EnsureLoadedAsync();

        var project = _projects.FirstOrDefault(p => p.Id == projectId);
        if (project == null) return false;

        var pair = project.TradingPairs.FirstOrDefault(p => p.Id == pairId);
        if (pair == null) return false;

        project.TradingPairs.Remove(pair);
        project.UpdatedAt = DateTime.UtcNow;
        await SaveAllAsync();
        return true;
    }

    public async Task<bool> UpdateTradingPairAsync(string projectId, ProjectTradingPair pair)
    {
        await EnsureLoadedAsync();

        var project = _projects.FirstOrDefault(p => p.Id == projectId);
        if (project == null) return false;

        var index = project.TradingPairs.FindIndex(p => p.Id == pair.Id);
        if (index < 0) return false;

        project.TradingPairs[index] = pair;
        project.UpdatedAt = DateTime.UtcNow;
        await SaveAllAsync();
        return true;
    }

    public async Task<IEnumerable<ProjectTradingPair>> GetTradingPairsAsync(string projectId)
    {
        await EnsureLoadedAsync();

        var project = _projects.FirstOrDefault(p => p.Id == projectId);
        return project?.TradingPairs ?? Enumerable.Empty<ProjectTradingPair>();
    }

    public async Task<bool> CanAddMorePairsAsync(string projectId)
    {
        await EnsureLoadedAsync();

        var project = _projects.FirstOrDefault(p => p.Id == projectId);
        return project != null && project.TradingPairs.Count < MaxPairsPerProject;
    }

    private class ProjectData
    {
        public List<TradingProject> Projects { get; set; } = new();
        public string ActiveProjectId { get; set; } = "";
    }
}
