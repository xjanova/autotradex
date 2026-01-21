/*
 * ============================================================================
 * AutoTrade-X - Connection Status Service Interface
 * ============================================================================
 * Monitors exchange connections and validates configuration before trading
 * ============================================================================
 */

namespace AutoTradeX.Core.Interfaces;

/// <summary>
/// Service for monitoring connection status and validating trading prerequisites
/// </summary>
public interface IConnectionStatusService
{
    /// <summary>
    /// Event raised when connection status changes
    /// </summary>
    event EventHandler<ConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

    /// <summary>
    /// Current status of all connections
    /// </summary>
    ConnectionStatusSnapshot CurrentStatus { get; }

    /// <summary>
    /// Whether all prerequisites are met to start trading
    /// </summary>
    bool CanStartTrading { get; }

    /// <summary>
    /// Get list of missing prerequisites
    /// </summary>
    List<string> GetMissingPrerequisites();

    /// <summary>
    /// Check all exchange connections
    /// </summary>
    Task<ConnectionStatusSnapshot> CheckAllConnectionsAsync();

    /// <summary>
    /// Check a specific exchange connection
    /// </summary>
    Task<ExchangeConnectionStatus> CheckExchangeConnectionAsync(string exchangeName);

    /// <summary>
    /// Validate API credentials for an exchange (authenticated endpoint test)
    /// </summary>
    Task<ApiValidationResult> ValidateApiCredentialsAsync(string exchangeName);

    /// <summary>
    /// Start periodic connection monitoring
    /// </summary>
    void StartMonitoring(TimeSpan interval);

    /// <summary>
    /// Stop periodic connection monitoring
    /// </summary>
    void StopMonitoring();
}

/// <summary>
/// Snapshot of all connection statuses
/// </summary>
public class ConnectionStatusSnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, ExchangeConnectionStatus> Exchanges { get; set; } = new();
    public bool HasActiveStrategy { get; set; }
    public string? ActiveStrategyName { get; set; }
    public bool HasTradingPairs { get; set; }
    public int TradingPairCount { get; set; }
    public bool IsLiveMode { get; set; }

    /// <summary>
    /// Overall connection health
    /// </summary>
    public ConnectionHealth OverallHealth
    {
        get
        {
            var connected = Exchanges.Values.Count(e => e.IsConnected && e.HasValidCredentials);
            var total = Exchanges.Count;

            if (connected == 0) return ConnectionHealth.Disconnected;
            if (connected < total) return ConnectionHealth.Partial;
            return ConnectionHealth.Connected;
        }
    }

    /// <summary>
    /// Get count of connected exchanges
    /// </summary>
    public int ConnectedExchangeCount => Exchanges.Values.Count(e => e.IsConnected && e.HasValidCredentials);
}

/// <summary>
/// Status of a single exchange connection
/// </summary>
public class ExchangeConnectionStatus
{
    public string ExchangeName { get; set; } = "";
    public bool IsConnected { get; set; }
    public bool HasValidCredentials { get; set; }
    public bool CanReadBalance { get; set; }
    public bool CanTrade { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime LastChecked { get; set; } = DateTime.UtcNow;
    public int Latency { get; set; } // milliseconds

    public string StatusText
    {
        get
        {
            if (!IsConnected) return "Disconnected";
            if (!HasValidCredentials) return "No API Key";
            if (!CanReadBalance) return "API Error";
            if (!CanTrade) return "Read Only";
            return "Connected";
        }
    }

    public string StatusColor
    {
        get
        {
            if (!IsConnected) return "#EF4444"; // Red
            if (!HasValidCredentials) return "#F59E0B"; // Yellow
            if (!CanReadBalance) return "#F59E0B";
            if (!CanTrade) return "#F59E0B";
            return "#10B981"; // Green
        }
    }
}

/// <summary>
/// Result of API credential validation
/// </summary>
public class ApiValidationResult
{
    public bool IsValid { get; set; }
    public bool CanReadBalance { get; set; }
    public bool CanTrade { get; set; }
    public bool CanWithdraw { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, bool> Permissions { get; set; } = new();
}

/// <summary>
/// Overall connection health status
/// </summary>
public enum ConnectionHealth
{
    Disconnected,
    Partial,
    Connected
}

/// <summary>
/// Event args for connection status changes
/// </summary>
public class ConnectionStatusChangedEventArgs : EventArgs
{
    public ConnectionStatusSnapshot Status { get; }
    public string? ChangedExchange { get; }
    public string? Message { get; }

    public ConnectionStatusChangedEventArgs(ConnectionStatusSnapshot status, string? changedExchange = null, string? message = null)
    {
        Status = status;
        ChangedExchange = changedExchange;
        Message = message;
    }
}
