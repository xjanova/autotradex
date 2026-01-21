/*
 * ============================================================================
 * AutoTrade-X - License Service Implementation
 * ============================================================================
 * Handles license activation, validation, and offline grace period
 * API Server: https://xman4289.com/api/v1/license
 * ============================================================================
 */

using System.Management;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Timers;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;

namespace AutoTradeX.Infrastructure.Services;

/// <summary>
/// License management service with online validation and offline grace period
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class LicenseService : ILicenseService, IDisposable
{
    // API Configuration
    private const string ApiBaseUrl = "https://xman4289.com/api/v1/license";
    private const string PurchaseBaseUrl = "https://xman4289.com/purchase";
    private const string AppName = "AutoTradeX";
    private const string AppVersion = "0.1.0";

    // Offline grace period configuration
    private const int OfflineGracePeriodDays = 7;
    private const int TrialPeriodDays = 30;
    private const int ValidationIntervalMinutes = 30;

    // File paths
    private readonly string _licensePath;
    private readonly string _devicePath;

    // Services
    private readonly ILoggingService _logger;
    private readonly HttpClient _httpClient;
    private System.Timers.Timer? _validationTimer;

    // State
    private LicenseInfo? _currentLicense;
    private string? _machineId;
    private bool _isInitialized = false;

    public LicenseInfo? CurrentLicense => _currentLicense;
    public bool IsLicensed => _currentLicense?.Status == LicenseStatus.Valid;
    public bool IsTrial => _currentLicense?.Status == LicenseStatus.Trial || _currentLicense?.Tier == LicenseTier.Trial;
    public bool IsOfflineDowngraded { get; private set; } = false;

    public event EventHandler<LicenseStatusChangedEventArgs>? LicenseStatusChanged;

    public LicenseService(ILoggingService logger)
    {
        _logger = logger;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", $"{AppName}/{AppVersion}");

        // Set up paths in AppData
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoTradeX");
        Directory.CreateDirectory(appDataPath);

        _licensePath = Path.Combine(appDataPath, "license.dat");
        _devicePath = Path.Combine(appDataPath, "device.dat");
    }

    #region Initialization

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        try
        {
            _logger.LogInfo("License", "Initializing license service...");

            // Generate machine ID
            _machineId = GetMachineId();
            _logger.LogInfo("License", $"Device ID: {_machineId[..8]}...");

            // Load stored license
            await LoadStoredLicenseAsync();

            if (_currentLicense != null)
            {
                _logger.LogInfo("License", $"Loaded license: {_currentLicense.Tier} - {_currentLicense.Status}");

                // Check offline grace period
                CheckOfflineGracePeriod();

                // Try to validate with server
                if (!IsOfflineDowngraded)
                {
                    await ValidateLicenseAsync();
                }
            }
            else
            {
                // No license - check for device registration (trial)
                await CheckTrialStatusAsync();
            }

            _isInitialized = true;
            _logger.LogInfo("License", "License service initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError("License", $"Failed to initialize: {ex.Message}");
            // Default to trial mode on error
            SetTrialMode();
        }
    }

    #endregion

    #region License Activation

    public async Task<LicenseActivationResponse> ActivateLicenseAsync(string licenseKey, string email)
    {
        try
        {
            _logger.LogInfo("License", $"Activating license for {email}...");

            var request = new LicenseActivationRequest
            {
                LicenseKey = licenseKey.Trim().ToUpperInvariant(),
                Email = email.Trim().ToLowerInvariant(),
                DeviceId = GetMachineId(),
                DeviceName = Environment.MachineName,
                AppVersion = AppVersion,
                OsVersion = Environment.OSVersion.ToString()
            };

            var response = await _httpClient.PostAsJsonAsync($"{ApiBaseUrl}/activate", request);
            var result = await response.Content.ReadFromJsonAsync<LicenseActivationResponse>();

            if (result == null)
            {
                return new LicenseActivationResponse
                {
                    Success = false,
                    Message = "Invalid server response"
                };
            }

            if (result.Success && result.License != null)
            {
                // Store activated license
                var oldStatus = _currentLicense?.Status ?? LicenseStatus.Unknown;
                _currentLicense = ConvertToLicenseInfo(result.License);
                _currentLicense.LastOnline = DateTime.UtcNow;
                _currentLicense.LastValidated = DateTime.UtcNow;

                await SaveLicenseAsync();
                IsOfflineDowngraded = false;

                _logger.LogInfo("License", $"License activated successfully: {_currentLicense.Tier}");
                OnLicenseStatusChanged(oldStatus, _currentLicense.Status, "License activated");
            }
            else
            {
                _logger.LogWarning("License", $"Activation failed: {result.Message}");
            }

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError("License", $"Network error during activation: {ex.Message}");
            return new LicenseActivationResponse
            {
                Success = false,
                Message = "Cannot connect to license server. Please check your internet connection.",
                ErrorCode = "NETWORK_ERROR"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError("License", $"Activation error: {ex.Message}");
            return new LicenseActivationResponse
            {
                Success = false,
                Message = $"Activation failed: {ex.Message}",
                ErrorCode = "UNKNOWN_ERROR"
            };
        }
    }

    #endregion

    #region License Validation

    public async Task<LicenseValidationResponse> ValidateLicenseAsync()
    {
        if (_currentLicense == null || string.IsNullOrEmpty(_currentLicense.LicenseKey))
        {
            return new LicenseValidationResponse
            {
                Valid = false,
                Status = "no_license",
                Message = "No license to validate"
            };
        }

        try
        {
            var request = new LicenseValidationRequest
            {
                LicenseKey = _currentLicense.LicenseKey,
                DeviceId = GetMachineId(),
                AppVersion = AppVersion
            };

            var response = await _httpClient.PostAsJsonAsync($"{ApiBaseUrl}/validate", request);
            var result = await response.Content.ReadFromJsonAsync<LicenseValidationResponse>();

            if (result == null)
            {
                return new LicenseValidationResponse
                {
                    Valid = false,
                    Message = "Invalid server response"
                };
            }

            // Update license info
            _currentLicense.LastOnline = DateTime.UtcNow;
            _currentLicense.LastValidated = DateTime.UtcNow;

            if (result.Valid)
            {
                var oldStatus = _currentLicense.Status;
                _currentLicense.Status = LicenseStatus.Valid;
                _currentLicense.Features = result.Features;
                if (result.ExpiresAt.HasValue)
                {
                    _currentLicense.ExpiresAt = result.ExpiresAt.Value;
                }

                IsOfflineDowngraded = false;
                await SaveLicenseAsync();

                if (oldStatus != LicenseStatus.Valid)
                {
                    OnLicenseStatusChanged(oldStatus, LicenseStatus.Valid, "License validated");
                }

                _logger.LogInfo("License", "License validated successfully");
            }
            else
            {
                HandleValidationFailure(result.Status, result.Message);
            }

            return result;
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning("License", "Cannot reach license server - working offline");
            CheckOfflineGracePeriod();

            return new LicenseValidationResponse
            {
                Valid = !IsOfflineDowngraded,
                Status = "offline",
                Message = IsOfflineDowngraded
                    ? "Offline grace period expired"
                    : $"Working offline. {_currentLicense.OfflineDaysRemaining} days remaining."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError("License", $"Validation error: {ex.Message}");
            return new LicenseValidationResponse
            {
                Valid = false,
                Message = ex.Message
            };
        }
    }

    private void HandleValidationFailure(string status, string message)
    {
        var oldStatus = _currentLicense!.Status;

        switch (status.ToLowerInvariant())
        {
            case "expired":
                _currentLicense.Status = LicenseStatus.Expired;
                break;
            case "suspended":
                _currentLicense.Status = LicenseStatus.Suspended;
                break;
            case "device_limit":
                _currentLicense.Status = LicenseStatus.DeviceLimitReached;
                break;
            default:
                _currentLicense.Status = LicenseStatus.Invalid;
                break;
        }

        _logger.LogWarning("License", $"License validation failed: {status} - {message}");
        OnLicenseStatusChanged(oldStatus, _currentLicense.Status, message);
    }

    #endregion

    #region License Deactivation

    public async Task<bool> DeactivateLicenseAsync()
    {
        if (_currentLicense == null || string.IsNullOrEmpty(_currentLicense.LicenseKey))
        {
            return true;
        }

        try
        {
            var request = new LicenseDeactivationRequest
            {
                LicenseKey = _currentLicense.LicenseKey,
                DeviceId = GetMachineId()
            };

            var response = await _httpClient.PostAsJsonAsync($"{ApiBaseUrl}/deactivate", request);
            var success = response.IsSuccessStatusCode;

            if (success)
            {
                _logger.LogInfo("License", "License deactivated on server");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("License", $"Could not deactivate on server: {ex.Message}");
        }

        // Clear local license regardless of server response
        var oldStatus = _currentLicense.Status;
        _currentLicense = null;
        IsOfflineDowngraded = false;

        if (File.Exists(_licensePath))
        {
            File.Delete(_licensePath);
        }

        SetTrialMode();
        OnLicenseStatusChanged(oldStatus, LicenseStatus.Trial, "License deactivated");

        _logger.LogInfo("License", "Local license cleared");
        return true;
    }

    #endregion

    #region Device Registration (Trial)

    public async Task<DeviceRegistrationResponse> RegisterDeviceAsync()
    {
        try
        {
            var request = new DeviceRegistrationRequest
            {
                DeviceId = GetMachineId(),
                DeviceName = Environment.MachineName,
                AppVersion = AppVersion,
                OsVersion = Environment.OSVersion.ToString()
            };

            var response = await _httpClient.PostAsJsonAsync($"{ApiBaseUrl}/register-device", request);
            var result = await response.Content.ReadFromJsonAsync<DeviceRegistrationResponse>();

            if (result?.Success == true)
            {
                _logger.LogInfo("License", $"Device registered. Trial days: {result.TrialDaysRemaining}");
                await SaveDeviceTokenAsync(result.DeviceToken);
            }

            return result ?? new DeviceRegistrationResponse
            {
                Success = false,
                Message = "Invalid server response"
            };
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning("License", "Cannot register device - working offline");
            return new DeviceRegistrationResponse
            {
                Success = true,
                TrialDaysRemaining = TrialPeriodDays,
                Message = "Offline trial mode"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError("License", $"Device registration error: {ex.Message}");
            return new DeviceRegistrationResponse
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    private async Task CheckTrialStatusAsync()
    {
        var deviceToken = await LoadDeviceTokenAsync();

        if (string.IsNullOrEmpty(deviceToken))
        {
            // First time - register device
            var result = await RegisterDeviceAsync();
            SetTrialMode(result.TrialDaysRemaining);
        }
        else
        {
            // Existing trial - validate
            SetTrialMode();
        }
    }

    #endregion

    #region Offline Grace Period

    private void CheckOfflineGracePeriod()
    {
        if (_currentLicense == null) return;

        var daysSinceOnline = (DateTime.UtcNow - _currentLicense.LastOnline).TotalDays;

        if (daysSinceOnline > OfflineGracePeriodDays)
        {
            _logger.LogWarning("License", $"Offline grace period expired ({daysSinceOnline:F0} days)");
            DowngradeToTrial();
        }
        else
        {
            _currentLicense.OfflineDaysRemaining = OfflineGracePeriodDays - (int)daysSinceOnline;
            _currentLicense.IsOfflineMode = true;
            _logger.LogInfo("License", $"Offline mode: {_currentLicense.OfflineDaysRemaining} days remaining");
        }
    }

    private void DowngradeToTrial()
    {
        if (_currentLicense == null) return;

        var oldStatus = _currentLicense.Status;
        IsOfflineDowngraded = true;

        // Keep license info but restrict to trial features
        _currentLicense.Features = LicenseFeatures.TrialFeatures;
        _currentLicense.MaxTradingPairs = 1;
        _currentLicense.MaxExchanges = 2;
        _currentLicense.IsOfflineMode = true;

        _logger.LogWarning("License", "License downgraded to trial due to offline expiry");
        OnLicenseStatusChanged(oldStatus, LicenseStatus.Trial, "License downgraded - please connect to internet");
    }

    #endregion

    #region Feature Checking

    public bool HasFeature(string featureName)
    {
        if (_currentLicense == null) return false;

        // Check if feature is in current license
        return _currentLicense.Features.Contains(featureName, StringComparer.OrdinalIgnoreCase);
    }

    public int GetTrialDaysRemaining()
    {
        if (_currentLicense == null || _currentLicense.Status != LicenseStatus.Trial)
        {
            return 0;
        }

        var remaining = (_currentLicense.ExpiresAt - DateTime.UtcNow).TotalDays;
        return Math.Max(0, (int)remaining);
    }

    #endregion

    #region Machine ID Generation

    public string GetMachineId()
    {
        if (!string.IsNullOrEmpty(_machineId))
        {
            return _machineId;
        }

        try
        {
            var sb = new StringBuilder();

            // CPU ID
            sb.Append(GetWmiProperty("Win32_Processor", "ProcessorId"));

            // Motherboard serial
            sb.Append(GetWmiProperty("Win32_BaseBoard", "SerialNumber"));

            // BIOS serial
            sb.Append(GetWmiProperty("Win32_BIOS", "SerialNumber"));

            // Hash the combined string
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            _machineId = Convert.ToHexString(hashBytes);

            return _machineId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("License", $"Could not generate machine ID: {ex.Message}");

            // Fallback: use machine name + username hash
            var fallback = $"{Environment.MachineName}-{Environment.UserName}-AutoTradeX";
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(fallback));
            _machineId = Convert.ToHexString(hashBytes);

            return _machineId;
        }
    }

    private static string GetWmiProperty(string className, string propertyName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {propertyName} FROM {className}");
            foreach (var obj in searcher.Get())
            {
                var value = obj[propertyName]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch
        {
            // Ignore WMI errors
        }
        return "";
    }

    #endregion

    #region Periodic Validation

    public void StartPeriodicValidation()
    {
        StopPeriodicValidation();

        _validationTimer = new System.Timers.Timer(ValidationIntervalMinutes * 60 * 1000);
        _validationTimer.Elapsed += async (s, e) => await OnValidationTimerElapsed();
        _validationTimer.AutoReset = true;
        _validationTimer.Start();

        _logger.LogInfo("License", $"Started periodic validation (every {ValidationIntervalMinutes} minutes)");
    }

    public void StopPeriodicValidation()
    {
        if (_validationTimer != null)
        {
            _validationTimer.Stop();
            _validationTimer.Dispose();
            _validationTimer = null;
        }
    }

    private async Task OnValidationTimerElapsed()
    {
        try
        {
            var result = await ValidateLicenseAsync();

            if (result.Valid && IsOfflineDowngraded)
            {
                // Restore full license features
                IsOfflineDowngraded = false;
                if (_currentLicense != null)
                {
                    _currentLicense.Features = LicenseFeatures.GetFeaturesForTier(_currentLicense.Tier);
                    _currentLicense.MaxTradingPairs = LicenseFeatures.GetMaxPairsForTier(_currentLicense.Tier);
                    _currentLicense.MaxExchanges = LicenseFeatures.GetMaxExchangesForTier(_currentLicense.Tier);
                    _currentLicense.IsOfflineMode = false;

                    _logger.LogInfo("License", "License restored after reconnection");
                    OnLicenseStatusChanged(LicenseStatus.Trial, LicenseStatus.Valid, "License restored");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("License", $"Periodic validation failed: {ex.Message}");
        }
    }

    #endregion

    #region Purchase URL

    public string GetPurchaseUrl()
    {
        var deviceId = GetMachineId();
        return $"{PurchaseBaseUrl}?app={AppName}&device={deviceId[..16]}";
    }

    #endregion

    #region Storage (Encrypted)

    private async Task SaveLicenseAsync()
    {
        if (_currentLicense == null) return;

        try
        {
            var json = JsonSerializer.Serialize(_currentLicense);
            var encrypted = EncryptData(json);
            await File.WriteAllBytesAsync(_licensePath, encrypted);
        }
        catch (Exception ex)
        {
            _logger.LogError("License", $"Failed to save license: {ex.Message}");
        }
    }

    private async Task LoadStoredLicenseAsync()
    {
        try
        {
            if (!File.Exists(_licensePath)) return;

            var encrypted = await File.ReadAllBytesAsync(_licensePath);
            var json = DecryptData(encrypted);

            if (!string.IsNullOrEmpty(json))
            {
                _currentLicense = JsonSerializer.Deserialize<LicenseInfo>(json);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("License", $"Failed to load license: {ex.Message}");
            // Delete corrupted file
            if (File.Exists(_licensePath))
            {
                File.Delete(_licensePath);
            }
        }
    }

    private async Task SaveDeviceTokenAsync(string token)
    {
        try
        {
            var encrypted = EncryptData(token);
            await File.WriteAllBytesAsync(_devicePath, encrypted);
        }
        catch (Exception ex)
        {
            _logger.LogError("License", $"Failed to save device token: {ex.Message}");
        }
    }

    private async Task<string?> LoadDeviceTokenAsync()
    {
        try
        {
            if (!File.Exists(_devicePath)) return null;

            var encrypted = await File.ReadAllBytesAsync(_devicePath);
            return DecryptData(encrypted);
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Encryption

    private byte[] EncryptData(string data)
    {
        var key = DeriveKey(GetMachineId());
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(data);
        var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Prepend IV to encrypted data
        var result = new byte[aes.IV.Length + encryptedBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

        return result;
    }

    private string? DecryptData(byte[] encryptedData)
    {
        try
        {
            var key = DeriveKey(GetMachineId());
            using var aes = Aes.Create();
            aes.Key = key;

            // Extract IV from beginning
            var iv = new byte[aes.BlockSize / 8];
            var cipherText = new byte[encryptedData.Length - iv.Length];

            Buffer.BlockCopy(encryptedData, 0, iv, 0, iv.Length);
            Buffer.BlockCopy(encryptedData, iv.Length, cipherText, 0, cipherText.Length);

            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var decryptedBytes = decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);

            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] DeriveKey(string machineId)
    {
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes($"AutoTradeX-{machineId}-License"));
    }

    #endregion

    #region Helper Methods

    private void SetTrialMode(int trialDays = TrialPeriodDays)
    {
        _currentLicense = new LicenseInfo
        {
            Status = LicenseStatus.Trial,
            Tier = LicenseTier.Trial,
            DeviceId = GetMachineId(),
            DeviceName = Environment.MachineName,
            ActivatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(trialDays),
            LastOnline = DateTime.UtcNow,
            LastValidated = DateTime.UtcNow,
            Features = LicenseFeatures.TrialFeatures,
            MaxTradingPairs = 1,
            MaxExchanges = 2
        };

        _ = SaveLicenseAsync();
    }

    private LicenseInfo ConvertToLicenseInfo(LicenseData data)
    {
        var tier = data.Tier.ToLowerInvariant() switch
        {
            "basic" => LicenseTier.Basic,
            "pro" => LicenseTier.Pro,
            "enterprise" => LicenseTier.Enterprise,
            _ => LicenseTier.Trial
        };

        var status = data.Status.ToLowerInvariant() switch
        {
            "valid" or "active" => LicenseStatus.Valid,
            "expired" => LicenseStatus.Expired,
            "suspended" => LicenseStatus.Suspended,
            "trial" => LicenseStatus.Trial,
            _ => LicenseStatus.Invalid
        };

        return new LicenseInfo
        {
            LicenseKey = data.LicenseKey,
            Email = data.Email,
            DeviceId = GetMachineId(),
            DeviceName = Environment.MachineName,
            Status = status,
            Tier = tier,
            ActivatedAt = data.ActivatedAt,
            ExpiresAt = data.ExpiresAt,
            Features = data.Features.Length > 0 ? data.Features : LicenseFeatures.GetFeaturesForTier(tier),
            MaxTradingPairs = data.MaxTradingPairs > 0 ? data.MaxTradingPairs : LicenseFeatures.GetMaxPairsForTier(tier),
            MaxExchanges = data.MaxExchanges > 0 ? data.MaxExchanges : LicenseFeatures.GetMaxExchangesForTier(tier)
        };
    }

    private void OnLicenseStatusChanged(LicenseStatus oldStatus, LicenseStatus newStatus, string message)
    {
        LicenseStatusChanged?.Invoke(this, new LicenseStatusChangedEventArgs(oldStatus, newStatus, message));
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        StopPeriodicValidation();
        _httpClient.Dispose();
    }

    #endregion
}
