/*
 * ============================================================================
 * AutoTrade-X - License Models
 * ============================================================================
 * License data models for API communication and local storage
 * ============================================================================
 */

namespace AutoTradeX.Core.Models;

/// <summary>
/// License status enumeration
/// </summary>
public enum LicenseStatus
{
    Unknown,
    Valid,
    Invalid,
    Expired,
    Trial,
    Suspended,
    DeviceLimitReached
}

/// <summary>
/// License tier levels
/// </summary>
public enum LicenseTier
{
    Trial,      // Free trial - limited features
    Basic,      // Basic license - single exchange pair
    Pro,        // Pro license - multiple pairs, all features
    Enterprise  // Enterprise - unlimited, priority support
}

/// <summary>
/// License information stored locally
/// </summary>
public class LicenseInfo
{
    public string LicenseKey { get; set; } = "";
    public string Email { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public LicenseStatus Status { get; set; } = LicenseStatus.Unknown;
    public LicenseTier Tier { get; set; } = LicenseTier.Trial;
    public DateTime ActivatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime LastValidated { get; set; }
    public DateTime LastOnline { get; set; }
    public string[] Features { get; set; } = Array.Empty<string>();
    public int MaxTradingPairs { get; set; } = 1;
    public int MaxExchanges { get; set; } = 2;
    public bool IsOfflineMode { get; set; } = false;
    public int OfflineDaysRemaining { get; set; } = 7;
}

/// <summary>
/// License activation request
/// </summary>
public class LicenseActivationRequest
{
    public string LicenseKey { get; set; } = "";
    public string Email { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public string OsVersion { get; set; } = "";
}

/// <summary>
/// License activation response from server
/// </summary>
public class LicenseActivationResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? ErrorCode { get; set; }
    public LicenseData? License { get; set; }
}

/// <summary>
/// License data from server
/// </summary>
public class LicenseData
{
    public string LicenseKey { get; set; } = "";
    public string Email { get; set; } = "";
    public string Tier { get; set; } = "trial";
    public string Status { get; set; } = "valid";
    public DateTime ActivatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string[] Features { get; set; } = Array.Empty<string>();
    public int MaxDevices { get; set; } = 1;
    public int CurrentDevices { get; set; } = 0;
    public int MaxTradingPairs { get; set; } = 1;
    public int MaxExchanges { get; set; } = 2;
}

/// <summary>
/// License validation request
/// </summary>
public class LicenseValidationRequest
{
    public string LicenseKey { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string AppVersion { get; set; } = "";
}

/// <summary>
/// License validation response
/// </summary>
public class LicenseValidationResponse
{
    public bool Valid { get; set; }
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime? ExpiresAt { get; set; }
    public string[] Features { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Device registration request
/// </summary>
public class DeviceRegistrationRequest
{
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public string OsVersion { get; set; } = "";
}

/// <summary>
/// Device registration response
/// </summary>
public class DeviceRegistrationResponse
{
    public bool Success { get; set; }
    public string DeviceToken { get; set; } = "";
    public string Message { get; set; } = "";
    public int TrialDaysRemaining { get; set; } = 30;
}

/// <summary>
/// License deactivation request
/// </summary>
public class LicenseDeactivationRequest
{
    public string LicenseKey { get; set; } = "";
    public string DeviceId { get; set; } = "";
}

/// <summary>
/// Features available per license tier
/// </summary>
public static class LicenseFeatures
{
    // Trial features
    public static readonly string[] TrialFeatures = new[]
    {
        "basic_scan",
        "manual_trading",
        "single_pair"
    };

    // Basic features
    public static readonly string[] BasicFeatures = new[]
    {
        "basic_scan",
        "manual_trading",
        "auto_trading",
        "multi_pair",
        "basic_analytics",
        "email_alerts"
    };

    // Pro features
    public static readonly string[] ProFeatures = new[]
    {
        "basic_scan",
        "advanced_scan",
        "manual_trading",
        "auto_trading",
        "multi_pair",
        "basic_analytics",
        "advanced_analytics",
        "ai_strategies",
        "email_alerts",
        "push_notifications",
        "export_data",
        "api_access"
    };

    // Enterprise features
    public static readonly string[] EnterpriseFeatures = new[]
    {
        "basic_scan",
        "advanced_scan",
        "manual_trading",
        "auto_trading",
        "multi_pair",
        "unlimited_pairs",
        "basic_analytics",
        "advanced_analytics",
        "ai_strategies",
        "custom_strategies",
        "email_alerts",
        "push_notifications",
        "export_data",
        "api_access",
        "priority_support",
        "white_label"
    };

    /// <summary>
    /// Get features for a license tier
    /// </summary>
    public static string[] GetFeaturesForTier(LicenseTier tier) => tier switch
    {
        LicenseTier.Trial => TrialFeatures,
        LicenseTier.Basic => BasicFeatures,
        LicenseTier.Pro => ProFeatures,
        LicenseTier.Enterprise => EnterpriseFeatures,
        _ => TrialFeatures
    };

    /// <summary>
    /// Get max trading pairs for a tier
    /// </summary>
    public static int GetMaxPairsForTier(LicenseTier tier) => tier switch
    {
        LicenseTier.Trial => 1,
        LicenseTier.Basic => 3,
        LicenseTier.Pro => 10,
        LicenseTier.Enterprise => 100,
        _ => 1
    };

    /// <summary>
    /// Get max exchanges for a tier
    /// </summary>
    public static int GetMaxExchangesForTier(LicenseTier tier) => tier switch
    {
        LicenseTier.Trial => 2,
        LicenseTier.Basic => 3,
        LicenseTier.Pro => 6,
        LicenseTier.Enterprise => 10,
        _ => 2
    };
}
