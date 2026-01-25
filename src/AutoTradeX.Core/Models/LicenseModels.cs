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
    DeviceLimitReached,
    DemoMode  // Trial expired - limited functionality, no real trading
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

    // Demo mode properties
    public bool IsDemoMode { get; set; } = false;
    public DateTime? DemoModeStartedAt { get; set; }
    public string? PurchaseUrl { get; set; }

    // Early bird discount info (20% off during trial)
    public EarlyBirdInfo? EarlyBird { get; set; }
    public PricingInfo? Pricing { get; set; }

    /// <summary>
    /// Check if real trading is allowed
    /// </summary>
    public bool CanTrade => Status == LicenseStatus.Valid || Status == LicenseStatus.Trial;

    /// <summary>
    /// Check if auto-trading is allowed
    /// </summary>
    public bool CanAutoTrade => Status == LicenseStatus.Valid && Features.Contains("auto_trading");

    /// <summary>
    /// Get demo mode configuration
    /// </summary>
    public DemoModeConfig GetDemoConfig()
    {
        return new DemoModeConfig
        {
            CanViewOpportunities = true,
            CanExecuteTrades = false,
            CanUseAutoTrading = false,
            MaxExchanges = 2,
            ReminderIntervalMinutes = 15,
            PurchaseUrl = PurchaseUrl
        };
    }
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
    public int TrialDaysRemaining { get; set; } = 0;

    // New fields for enhanced device registration
    public bool CanStartTrial { get; set; } = true;
    public bool HasLicense { get; set; } = false;
    public string DeviceStatus { get; set; } = "pending"; // pending, trial, licensed, blocked, expired, demo
    public string? PurchaseUrl { get; set; }

    // Demo mode info (when trial expired)
    public bool IsDemoMode { get; set; } = false;
    public string? DemoModeReason { get; set; }

    // Early bird discount info (20% off during trial)
    public EarlyBirdInfo? EarlyBird { get; set; }
    public PricingInfo? Pricing { get; set; }
}

/// <summary>
/// Early bird discount information (20% off during trial)
/// </summary>
public class EarlyBirdInfo
{
    /// <summary>
    /// Is the device eligible for early bird discount
    /// </summary>
    public bool Eligible { get; set; } = false;

    /// <summary>
    /// Discount percentage (typically 20%)
    /// </summary>
    public int DiscountPercent { get; set; } = 20;

    /// <summary>
    /// Days remaining in trial (discount expires when trial ends)
    /// </summary>
    public int DaysRemaining { get; set; } = 0;

    /// <summary>
    /// Unique discount code for this device
    /// </summary>
    public string? DiscountCode { get; set; }

    /// <summary>
    /// When the discount expires (same as trial expiry)
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Message to show to user about the discount
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// Reason why discount is not eligible (if applicable)
    /// </summary>
    public string? NotEligibleReason { get; set; }
}

/// <summary>
/// Pricing information for a plan
/// </summary>
public class PlanPricing
{
    public string PlanName { get; set; } = "";
    public decimal OriginalPrice { get; set; }
    public decimal FinalPrice { get; set; }
    public string Currency { get; set; } = "THB";
    public int? DiscountPercent { get; set; }
    public decimal? DiscountAmount { get; set; }
    public string[]? Features { get; set; }
    public string[]? Exchanges { get; set; }
}

/// <summary>
/// Complete pricing response with all plans
/// </summary>
public class PricingInfo
{
    public Dictionary<string, PlanPricing> Plans { get; set; } = new();
    public EarlyBirdInfo? EarlyBird { get; set; }
}

/// <summary>
/// Demo mode configuration - what features are available/restricted
/// </summary>
public class DemoModeConfig
{
    /// <summary>
    /// Can view arbitrage opportunities but cannot execute trades
    /// </summary>
    public bool CanViewOpportunities { get; set; } = true;

    /// <summary>
    /// Can execute real trades
    /// </summary>
    public bool CanExecuteTrades { get; set; } = false;

    /// <summary>
    /// Can use auto-trading feature
    /// </summary>
    public bool CanUseAutoTrading { get; set; } = false;

    /// <summary>
    /// Maximum number of exchanges to connect
    /// </summary>
    public int MaxExchanges { get; set; } = 2;

    /// <summary>
    /// Show activation reminder every N minutes
    /// </summary>
    public int ReminderIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Message to show in demo mode
    /// </summary>
    public string DemoMessage { get; set; } = "คุณกำลังใช้งาน Demo Mode - ไม่สามารถเทรดจริงได้ กรุณา Activate License เพื่อใช้งานเต็มรูปแบบ";

    /// <summary>
    /// URL to purchase license
    /// </summary>
    public string? PurchaseUrl { get; set; }
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

/// <summary>
/// Event args for demo mode reminder
/// </summary>
public class DemoModeReminderEventArgs : EventArgs
{
    public DemoModeConfig Config { get; }
    public DateTime ReminderTime { get; }

    public DemoModeReminderEventArgs(DemoModeConfig config)
    {
        Config = config;
        ReminderTime = DateTime.UtcNow;
    }
}
