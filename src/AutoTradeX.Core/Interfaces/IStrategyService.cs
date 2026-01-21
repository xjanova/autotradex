using AutoTradeX.Core.Models;

namespace AutoTradeX.Core.Interfaces;

/// <summary>
/// Service for managing AI trading strategies
/// </summary>
public interface IStrategyService
{
    /// <summary>
    /// Get all strategies
    /// </summary>
    Task<IEnumerable<TradingStrategy>> GetAllStrategiesAsync();

    /// <summary>
    /// Get strategy by ID
    /// </summary>
    Task<TradingStrategy?> GetStrategyAsync(string id);

    /// <summary>
    /// Save or update a strategy
    /// </summary>
    Task<bool> SaveStrategyAsync(TradingStrategy strategy);

    /// <summary>
    /// Delete a strategy
    /// </summary>
    Task<bool> DeleteStrategyAsync(string id);

    /// <summary>
    /// Get default strategy
    /// </summary>
    Task<TradingStrategy> GetDefaultStrategyAsync();

    /// <summary>
    /// Create a copy of existing strategy
    /// </summary>
    Task<TradingStrategy> CloneStrategyAsync(string id, string newName);

    /// <summary>
    /// Export strategy to JSON
    /// </summary>
    Task<string> ExportStrategyAsync(string id);

    /// <summary>
    /// Import strategy from JSON
    /// </summary>
    Task<TradingStrategy?> ImportStrategyAsync(string json);

    /// <summary>
    /// Get preset strategies
    /// </summary>
    IEnumerable<TradingStrategy> GetPresetStrategies();
}

/// <summary>
/// Service for managing trading projects
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// Get all projects
    /// </summary>
    Task<IEnumerable<TradingProject>> GetAllProjectsAsync();

    /// <summary>
    /// Get project by ID
    /// </summary>
    Task<TradingProject?> GetProjectAsync(string id);

    /// <summary>
    /// Get active project
    /// </summary>
    Task<TradingProject?> GetActiveProjectAsync();

    /// <summary>
    /// Save or update a project
    /// </summary>
    Task<bool> SaveProjectAsync(TradingProject project);

    /// <summary>
    /// Delete a project
    /// </summary>
    Task<bool> DeleteProjectAsync(string id);

    /// <summary>
    /// Set active project
    /// </summary>
    Task<bool> SetActiveProjectAsync(string id);

    /// <summary>
    /// Add trading pair to project
    /// </summary>
    Task<bool> AddTradingPairAsync(string projectId, ProjectTradingPair pair);

    /// <summary>
    /// Remove trading pair from project
    /// </summary>
    Task<bool> RemoveTradingPairAsync(string projectId, string pairId);

    /// <summary>
    /// Update trading pair in project
    /// </summary>
    Task<bool> UpdateTradingPairAsync(string projectId, ProjectTradingPair pair);

    /// <summary>
    /// Get trading pairs for project
    /// </summary>
    Task<IEnumerable<ProjectTradingPair>> GetTradingPairsAsync(string projectId);

    /// <summary>
    /// Check if can add more pairs (max 10)
    /// </summary>
    Task<bool> CanAddMorePairsAsync(string projectId);
}
