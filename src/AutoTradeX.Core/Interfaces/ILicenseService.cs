/*
 * ============================================================================
 * AutoTrade-X - License Service Interface
 * ============================================================================
 * Contract for license management operations
 * ============================================================================
 */

using AutoTradeX.Core.Models;

namespace AutoTradeX.Core.Interfaces;

/// <summary>
/// Service for managing software licensing
/// </summary>
public interface ILicenseService
{
    /// <summary>
    /// Current license information
    /// </summary>
    LicenseInfo? CurrentLicense { get; }

    /// <summary>
    /// Whether the software is properly licensed
    /// </summary>
    bool IsLicensed { get; }

    /// <summary>
    /// Whether running in trial mode
    /// </summary>
    bool IsTrial { get; }

    /// <summary>
    /// Whether license is offline downgraded
    /// </summary>
    bool IsOfflineDowngraded { get; }

    /// <summary>
    /// Event raised when license status changes
    /// </summary>
    event EventHandler<LicenseStatusChangedEventArgs>? LicenseStatusChanged;

    /// <summary>
    /// Initialize the license service and load stored license
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Activate a license key
    /// </summary>
    Task<LicenseActivationResponse> ActivateLicenseAsync(string licenseKey, string email);

    /// <summary>
    /// Validate current license with server
    /// </summary>
    Task<LicenseValidationResponse> ValidateLicenseAsync();

    /// <summary>
    /// Deactivate current license
    /// </summary>
    Task<bool> DeactivateLicenseAsync();

    /// <summary>
    /// Register device for trial
    /// </summary>
    Task<DeviceRegistrationResponse> RegisterDeviceAsync();

    /// <summary>
    /// Check if a feature is available
    /// </summary>
    bool HasFeature(string featureName);

    /// <summary>
    /// Get remaining trial days
    /// </summary>
    int GetTrialDaysRemaining();

    /// <summary>
    /// Get machine ID for this device
    /// </summary>
    string GetMachineId();

    /// <summary>
    /// Start automatic license validation timer
    /// </summary>
    void StartPeriodicValidation();

    /// <summary>
    /// Stop automatic license validation
    /// </summary>
    void StopPeriodicValidation();

    /// <summary>
    /// Get purchase URL with device ID
    /// </summary>
    string GetPurchaseUrl();
}

/// <summary>
/// Event args for license status changes
/// </summary>
public class LicenseStatusChangedEventArgs : EventArgs
{
    public LicenseStatus OldStatus { get; }
    public LicenseStatus NewStatus { get; }
    public string Message { get; }

    public LicenseStatusChangedEventArgs(LicenseStatus oldStatus, LicenseStatus newStatus, string message)
    {
        OldStatus = oldStatus;
        NewStatus = newStatus;
        Message = message;
    }
}
