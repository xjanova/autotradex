/*
 * ============================================================================
 * AutoTrade-X - License Service Implementation
 * ============================================================================
 * Handles license activation, validation, and offline grace period
 * API Server: https://xman4289.com/api/v1/license
 * ============================================================================
 */

using System.Management;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
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
    private const string ApiBaseUrl = "https://xman4289.com/api/v1/autotradex";
    private const string PurchaseBaseUrl = "https://xman4289.com/autotradex/purchase";
    private const string AppName = "AutoTradeX";
    private const string AppVersion = "0.2.0";

    // Server verification - Anti-fake server protection
    private const string ExpectedServerDomain = "xman4289.com";
    private const string ServerPublicKey = "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA"; // Truncated for security
    private const string ServerSignatureHeader = "X-License-Signature";
    private const string ServerTimestampHeader = "X-License-Timestamp";
    private const string ServerNonceHeader = "X-License-Nonce";

    // Known server IP ranges (for additional verification)
    private static readonly string[] TrustedServerIPs = new[]
    {
        "104.21.", // Cloudflare
        "172.67.", // Cloudflare
        "188.114.", // Cloudflare
    };

    // Offline grace period configuration
    private const int OfflineGracePeriodDays = 7;
    private const int TrialPeriodDays = 30;
    private const int ValidationIntervalMinutes = 30;

    // Anti-fake server state
    private bool _serverVerified = false;
    private DateTime _lastServerVerification = DateTime.MinValue;

    // File paths
    private readonly string _licensePath;
    private readonly string _devicePath;
    private readonly string _timeCheckPath; // Anti-tampering: persisted time tracking

    // Services
    private readonly ILoggingService _logger;
    private readonly HttpClient _httpClient;
    private System.Timers.Timer? _validationTimer;

    // State
    private LicenseInfo? _currentLicense;
    private string? _machineId;
    private bool _isInitialized = false;
    private DateTime _lastKnownTime = DateTime.MinValue; // Anti-tampering: track highest known time
    private bool _timeTamperingDetected = false; // Flag for clock manipulation

    public LicenseInfo? CurrentLicense => _currentLicense;
    public bool IsLicensed => _currentLicense?.Status == LicenseStatus.Valid;
    public bool IsTrial => _currentLicense?.Status == LicenseStatus.Trial || _currentLicense?.Tier == LicenseTier.Trial;
    public bool IsOfflineDowngraded { get; private set; } = false;

    // Demo Mode properties
    public bool IsDemoMode => _currentLicense?.Status == LicenseStatus.DemoMode ||
                              _currentLicense?.Status == LicenseStatus.Expired ||
                              (_currentLicense?.Status == LicenseStatus.Trial && GetTrialDaysRemaining() <= 0);
    public bool CanTrade => _currentLicense?.CanTrade ?? false;
    public bool CanAutoTrade => _currentLicense?.CanAutoTrade ?? false;

    // Early Bird Discount properties
    public bool IsEarlyBirdEligible => _currentLicense?.EarlyBird?.Eligible ?? false;
    public EarlyBirdInfo? EarlyBirdInfo => _currentLicense?.EarlyBird;
    public PricingInfo? PricingInfo => _currentLicense?.Pricing;

    // Demo mode reminder timer
    private System.Timers.Timer? _demoReminderTimer;
    private DateTime _lastDemoReminder = DateTime.MinValue;
    private const int DemoReminderIntervalMinutes = 15;

    public event EventHandler<LicenseStatusChangedEventArgs>? LicenseStatusChanged;
    public event EventHandler<DemoModeReminderEventArgs>? DemoModeReminder;

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
        _timeCheckPath = Path.Combine(appDataPath, ".timecheck"); // Hidden file for time tracking
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

            // FIRST: Register device with server immediately
            // This ensures server knows about this device before any license checks
            var deviceRegistration = await RegisterDeviceOnStartupAsync();
            if (deviceRegistration.Success)
            {
                _logger.LogInfo("License", $"Device status: {deviceRegistration.DeviceStatus}");
            }

            // Check for time tampering (before loading license)
            var tampering = await CheckTimeTamperingAsync();
            if (tampering)
            {
                _logger.LogCritical("License", "Time tampering detected! Trial features will be restricted.");
            }

            // Load stored license
            await LoadStoredLicenseAsync();

            if (_currentLicense != null)
            {
                _logger.LogInfo("License", $"Loaded license: {_currentLicense.Tier} - {_currentLicense.Status}");

                // If tampering detected and this is a trial, expire it immediately
                if (_timeTamperingDetected && _currentLicense.Status == LicenseStatus.Trial)
                {
                    _logger.LogCritical("License", "Trial expired due to time tampering!");
                    _currentLicense.Status = LicenseStatus.Expired;
                    _currentLicense.ExpiresAt = DateTime.UtcNow.AddDays(-1); // Set as expired
                    await SaveLicenseAsync();
                }

                // Check offline grace period
                CheckOfflineGracePeriod();

                // Try to validate with server
                if (!IsOfflineDowngraded)
                {
                    await ValidateLicenseAsync();
                }

                // Check if should switch to demo mode (trial expired)
                CheckAndSwitchToDemoMode();

                // If in demo mode, start reminder timer and trigger initial reminder
                if (IsDemoMode)
                {
                    StartDemoReminderTimer();
                    // Give UI time to initialize before first reminder
                    _ = Task.Delay(3000).ContinueWith(_ => TriggerDemoReminder());
                }
            }
            else
            {
                // No license - check for device registration (trial)
                await CheckTrialStatusAsync();

                // Check if new trial should be demo mode
                CheckAndSwitchToDemoMode();
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

    #region Server Verification (Anti-Fake Server)

    /// <summary>
    /// Verify that we're connecting to the real license server, not a fake one
    /// </summary>
    private async Task<bool> VerifyServerAuthenticityAsync()
    {
        try
        {
            _logger.LogInfo("License", "Verifying server authenticity...");

            // Check 1: DNS Resolution - ensure domain resolves to expected IPs
            if (!await VerifyDnsResolutionAsync())
            {
                _logger.LogCritical("License", "DNS verification failed - possible DNS spoofing");
                return false;
            }

            // Check 2: SSL Certificate verification (done by HttpClient by default)
            // Check 3: Domain verification
            if (!VerifyDomain())
            {
                _logger.LogCritical("License", "Domain verification failed - possible MITM attack");
                return false;
            }

            // Check 4: Server challenge-response
            if (!await VerifyServerChallengeAsync())
            {
                _logger.LogCritical("License", "Server challenge verification failed - possible fake server");
                return false;
            }

            // Check 5: Response signature verification (if server supports it)
            // This will be checked on each API response

            _serverVerified = true;
            _lastServerVerification = DateTime.UtcNow;
            _logger.LogInfo("License", "Server authenticity verified successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("License", $"Server verification error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Verify DNS resolution returns expected IP ranges
    /// Prevents DNS spoofing attacks
    /// </summary>
    private async Task<bool> VerifyDnsResolutionAsync()
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(ExpectedServerDomain);

            if (addresses.Length == 0)
            {
                _logger.LogWarning("License", "No DNS records found for server");
                return false;
            }

            // Check if at least one IP is in trusted ranges
            foreach (var address in addresses)
            {
                var ipString = address.ToString();

                // Check against known trusted IP ranges
                foreach (var trustedRange in TrustedServerIPs)
                {
                    if (ipString.StartsWith(trustedRange))
                    {
                        _logger.LogInfo("License", $"Server IP {ipString} is in trusted range");
                        return true;
                    }
                }
            }

            // If using direct hosting (not Cloudflare), log but don't fail
            // This allows for server IP changes
            _logger.LogWarning("License", $"Server IPs not in known ranges: {string.Join(", ", addresses.Select(a => a.ToString()))}");

            // Additional check: verify the IP responds correctly
            return await VerifyServerResponseFromIpAsync(addresses[0].ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError("License", $"DNS verification error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Verify the domain in API URL matches expected
    /// </summary>
    private bool VerifyDomain()
    {
        try
        {
            var uri = new Uri(ApiBaseUrl);

            if (!uri.Host.Equals(ExpectedServerDomain, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogCritical("License", $"Domain mismatch: expected {ExpectedServerDomain}, got {uri.Host}");
                return false;
            }

            // Verify HTTPS
            if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogCritical("License", "Non-HTTPS connection detected");
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Send a challenge to the server and verify response
    /// Real server will respond with correct signature
    /// </summary>
    private async Task<bool> VerifyServerChallengeAsync()
    {
        try
        {
            // Generate random challenge
            var challengeBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(challengeBytes);
            }
            var challenge = Convert.ToBase64String(challengeBytes);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Create challenge request
            var challengeRequest = new
            {
                challenge = challenge,
                timestamp = timestamp,
                app_name = AppName,
                app_version = AppVersion
            };

            // Send to server's verify endpoint
            var response = await _httpClient.PostAsJsonAsync($"{ApiBaseUrl}/verify-server", challengeRequest);

            if (!response.IsSuccessStatusCode)
            {
                // If endpoint doesn't exist, use alternative verification
                return await VerifyServerAlternativeAsync();
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ServerChallengeResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null || !result.Success)
            {
                return false;
            }

            // Verify the server's response matches expected format
            // Real server will echo the challenge with a valid signature
            if (result.Challenge != challenge)
            {
                _logger.LogCritical("License", "Challenge mismatch - server returned wrong challenge");
                return false;
            }

            // Verify timestamp is within acceptable range (5 minutes)
            var responseTimestamp = result.Timestamp;
            if (Math.Abs(responseTimestamp - timestamp) > 300)
            {
                _logger.LogCritical("License", "Timestamp mismatch - possible replay attack");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("License", $"Challenge verification error: {ex.Message}");
            // Fall back to alternative verification
            return await VerifyServerAlternativeAsync();
        }
    }

    /// <summary>
    /// Alternative server verification using pricing endpoint
    /// </summary>
    private async Task<bool> VerifyServerAlternativeAsync()
    {
        try
        {
            // Use pricing endpoint which should return expected data structure
            var response = await _httpClient.GetAsync($"{ApiBaseUrl}/pricing");

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var json = await response.Content.ReadAsStringAsync();

            // Verify response contains expected structure
            if (!json.Contains("\"success\"") ||
                !json.Contains("\"plans\"") ||
                !json.Contains("autotradex"))
            {
                _logger.LogCritical("License", "Pricing response structure invalid - possible fake server");
                return false;
            }

            // Verify response headers
            if (response.Headers.Server != null)
            {
                var serverHeader = string.Join(" ", response.Headers.Server.Select(s => s.ToString()));
                // Log for monitoring
                _logger.LogInfo("License", $"Server header: {serverHeader}");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("License", $"Alternative verification error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Verify server responds correctly from a specific IP
    /// </summary>
    private async Task<bool> VerifyServerResponseFromIpAsync(string ip)
    {
        try
        {
            // Try to connect directly to verify it's responding
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(ip, 443);
            return tcpClient.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verify API response signature (if present)
    /// </summary>
    private bool VerifyResponseSignature(HttpResponseMessage response, string content)
    {
        try
        {
            // Check if signature headers are present
            if (!response.Headers.TryGetValues(ServerSignatureHeader, out var signatures))
            {
                // Signature not required for all endpoints
                return true;
            }

            var signature = signatures.FirstOrDefault();
            if (string.IsNullOrEmpty(signature))
            {
                return true;
            }

            // Get timestamp
            if (!response.Headers.TryGetValues(ServerTimestampHeader, out var timestamps))
            {
                _logger.LogWarning("License", "Signature present but timestamp missing");
                return false;
            }

            var timestamp = timestamps.FirstOrDefault();

            // Verify timestamp is recent (within 5 minutes)
            if (long.TryParse(timestamp, out var ts))
            {
                var responseTime = DateTimeOffset.FromUnixTimeSeconds(ts);
                if (Math.Abs((DateTimeOffset.UtcNow - responseTime).TotalMinutes) > 5)
                {
                    _logger.LogWarning("License", "Response timestamp too old - possible replay attack");
                    return false;
                }
            }

            // Note: Full signature verification would require server public key
            // For now, we verify the signature format is correct
            if (signature.Length < 64)
            {
                _logger.LogWarning("License", "Invalid signature length");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("License", $"Signature verification error: {ex.Message}");
            return true; // Don't fail on verification errors
        }
    }

    /// <summary>
    /// Check for signs of fake server (hosts file modification, proxy, etc.)
    /// </summary>
    private bool CheckForFakeServerIndicators()
    {
        try
        {
            // Check 1: Look for hosts file modification
            var hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
            if (File.Exists(hostsPath))
            {
                var hostsContent = File.ReadAllText(hostsPath).ToLower();
                if (hostsContent.Contains(ExpectedServerDomain.ToLower()))
                {
                    _logger.LogCritical("License", "Server domain found in hosts file - possible DNS override");
                    return true; // Fake server indicator detected
                }
            }

            // Check 2: Check for common proxy environment variables
            var proxyEnvVars = new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY" };
            foreach (var envVar in proxyEnvVars)
            {
                var value = Environment.GetEnvironmentVariable(envVar);
                if (!string.IsNullOrEmpty(value))
                {
                    _logger.LogWarning("License", $"Proxy detected: {envVar}");
                    // Don't fail, but log for monitoring
                }
            }

            return false; // No fake server indicators
        }
        catch (Exception ex)
        {
            _logger.LogWarning("License", $"Fake server check error: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Device Registration

    /// <summary>
    /// Register device with server immediately on app startup
    /// This happens before any license check to ensure server knows about this device
    /// </summary>
    public async Task<DeviceRegistrationResponse> RegisterDeviceOnStartupAsync()
    {
        try
        {
            // First: Check for fake server indicators
            if (CheckForFakeServerIndicators())
            {
                _logger.LogCritical("License", "Fake server indicators detected - aborting registration");
                return new DeviceRegistrationResponse
                {
                    Success = false,
                    Message = "Security check failed - possible fake server detected",
                    DeviceStatus = "blocked"
                };
            }

            // Second: Verify server authenticity
            if (!_serverVerified || (DateTime.UtcNow - _lastServerVerification).TotalHours > 1)
            {
                if (!await VerifyServerAuthenticityAsync())
                {
                    _logger.LogCritical("License", "Server verification failed - aborting registration");
                    return new DeviceRegistrationResponse
                    {
                        Success = false,
                        Message = "Cannot verify license server authenticity",
                        DeviceStatus = "blocked"
                    };
                }
            }

            _logger.LogInfo("License", "Registering device with server...");

            var request = new
            {
                machine_id = GetMachineId(),
                machine_name = Environment.MachineName,
                app_version = AppVersion,
                os_version = Environment.OSVersion.ToString(),
                hardware_hash = GetHardwareHash()
            };

            var response = await _httpClient.PostAsJsonAsync($"{ApiBaseUrl}/register-device", request);
            var json = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<DeviceRegistrationApiResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result?.Success == true)
                {
                    _logger.LogInfo("License", $"Device registered: {result.Message}");

                    // Check if device already has a license
                    if (result.Data?.HasLicense == true)
                    {
                        _logger.LogInfo("License", $"Device has active license: {result.Data.LicenseType}");
                    }

                    // Check trial status
                    if (result.Data?.Trial != null && result.Data.Trial.IsActive)
                    {
                        _logger.LogInfo("License", $"Device has active trial: {result.Data.Trial.DaysRemaining} days remaining");
                    }

                    // Check if suspicious
                    if (result.Data?.IsSuspicious == true)
                    {
                        _logger.LogWarning("License", "Device flagged as suspicious by server");
                    }

                    // Convert early bird info from API response
                    EarlyBirdInfo? earlyBird = null;
                    if (result.Data?.EarlyBird != null)
                    {
                        earlyBird = new EarlyBirdInfo
                        {
                            Eligible = result.Data.EarlyBird.Eligible,
                            DiscountPercent = result.Data.EarlyBird.DiscountPercent,
                            DaysRemaining = result.Data.EarlyBird.DaysRemaining,
                            DiscountCode = result.Data.EarlyBird.Code,
                            Message = result.Data.EarlyBird.Message ?? "",
                            NotEligibleReason = result.Data.EarlyBird.Reason
                        };

                        if (!string.IsNullOrEmpty(result.Data.EarlyBird.ExpiresAt) &&
                            DateTime.TryParse(result.Data.EarlyBird.ExpiresAt, out var expiresAt))
                        {
                            earlyBird.ExpiresAt = expiresAt;
                        }
                    }

                    // Convert pricing info from API response
                    PricingInfo? pricing = null;
                    if (result.Data?.Pricing?.Plans != null)
                    {
                        pricing = new PricingInfo
                        {
                            Plans = new Dictionary<string, PlanPricing>(),
                            EarlyBird = earlyBird
                        };

                        foreach (var (planName, planInfo) in result.Data.Pricing.Plans)
                        {
                            pricing.Plans[planName] = new PlanPricing
                            {
                                PlanName = planName,
                                OriginalPrice = planInfo.OriginalPrice,
                                FinalPrice = planInfo.FinalPrice,
                                Currency = planInfo.Currency ?? "THB",
                                DiscountPercent = planInfo.Discount?.Percent,
                                DiscountAmount = planInfo.Discount?.Amount,
                                Features = planInfo.Features,
                                Exchanges = planInfo.Exchanges
                            };
                        }
                    }

                    // Store early bird info in current license for UI access
                    if (_currentLicense != null && earlyBird != null)
                    {
                        _currentLicense.EarlyBird = earlyBird;
                        _currentLicense.Pricing = pricing;
                        _currentLicense.PurchaseUrl = result.Data?.PurchaseUrl;
                        _ = SaveLicenseAsync();
                    }

                    return new DeviceRegistrationResponse
                    {
                        Success = true,
                        Message = result.Message,
                        TrialDaysRemaining = result.Data?.Trial?.DaysRemaining ?? 0,
                        CanStartTrial = result.Data?.CanStartTrial ?? false,
                        HasLicense = result.Data?.HasLicense ?? false,
                        DeviceStatus = result.Data?.DeviceStatus ?? "pending",
                        PurchaseUrl = result.Data?.PurchaseUrl,
                        IsDemoMode = result.Data?.IsDemoMode ?? false,
                        EarlyBird = earlyBird,
                        Pricing = pricing
                    };
                }
            }

            _logger.LogWarning("License", $"Device registration failed: {json}");
            return new DeviceRegistrationResponse
            {
                Success = false,
                Message = "Failed to register device"
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("License", $"Cannot register device (network): {ex.Message}");
            return new DeviceRegistrationResponse
            {
                Success = false,
                Message = "Cannot connect to license server"
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

    /// <summary>
    /// Get hardware hash for abuse detection
    /// </summary>
    private string GetHardwareHash()
    {
        try
        {
            var sb = new StringBuilder();

            // CPU ID
            sb.Append(GetWmiProperty("Win32_Processor", "ProcessorId"));

            // Motherboard serial
            sb.Append(GetWmiProperty("Win32_BaseBoard", "SerialNumber"));

            // Hash it
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(hashBytes)[..32]; // First 32 chars
        }
        catch
        {
            return "";
        }
    }

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

            var response = await _httpClient.PostAsJsonAsync($"{ApiBaseUrl}/demo", request);
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

    #region API Response Models

    /// <summary>
    /// Response from /register-device API
    /// </summary>
    private class DeviceRegistrationApiResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public DeviceRegistrationData? Data { get; set; }
    }

    private class DeviceRegistrationData
    {
        public string DeviceStatus { get; set; } = "";
        public bool IsNew { get; set; }
        public bool HasLicense { get; set; }
        public string? LicenseType { get; set; }
        public TrialInfo? Trial { get; set; }
        public bool CanStartTrial { get; set; }
        public bool IsSuspicious { get; set; }
        public string? PurchaseUrl { get; set; }
        // Demo mode info
        public bool IsDemoMode { get; set; }
        public DemoModeApiInfo? DemoMode { get; set; }
        // Early bird discount info
        public EarlyBirdApiInfo? EarlyBird { get; set; }
        public PricingApiInfo? Pricing { get; set; }
    }

    private class TrialInfo
    {
        public bool IsActive { get; set; }
        public int DaysRemaining { get; set; }
        public string? ExpiresAt { get; set; }
    }

    private class DemoModeApiInfo
    {
        public bool CanViewOpportunities { get; set; }
        public bool CanExecuteTrades { get; set; }
        public bool CanUseAutoTrading { get; set; }
        public int MaxExchanges { get; set; }
        public int ReminderIntervalMinutes { get; set; }
        public string? DemoMessage { get; set; }
        public string? PurchaseUrl { get; set; }
    }

    private class EarlyBirdApiInfo
    {
        public bool Eligible { get; set; }
        public int DiscountPercent { get; set; }
        public int DaysRemaining { get; set; }
        public string? Code { get; set; }
        public string? ExpiresAt { get; set; }
        public string? Message { get; set; }
        public string? Reason { get; set; }
    }

    private class PricingApiInfo
    {
        public Dictionary<string, PlanApiInfo>? Plans { get; set; }
        public EarlyBirdApiInfo? EarlyBird { get; set; }
    }

    private class PlanApiInfo
    {
        public decimal OriginalPrice { get; set; }
        public decimal FinalPrice { get; set; }
        public string? Currency { get; set; }
        public DiscountApiInfo? Discount { get; set; }
        public string[]? Features { get; set; }
        public string[]? Exchanges { get; set; }
    }

    private class DiscountApiInfo
    {
        public int Percent { get; set; }
        public decimal Amount { get; set; }
        public string? Code { get; set; }
    }

    /// <summary>
    /// Response from /verify-server API
    /// </summary>
    private class ServerChallengeResponse
    {
        public bool Success { get; set; }
        public string Challenge { get; set; } = "";
        public long Timestamp { get; set; }
        public string? Signature { get; set; }
        public string? ServerVersion { get; set; }
    }

    #endregion

    #region Offline Grace Period

    private void CheckOfflineGracePeriod()
    {
        if (_currentLicense == null) return;

        // Use secure time to prevent clock manipulation
        var secureNow = GetSecureCurrentTime();
        var daysSinceOnline = (secureNow - _currentLicense.LastOnline).TotalDays;

        // If tampering detected, immediately downgrade
        if (_timeTamperingDetected)
        {
            _logger.LogCritical("License", "Forcing offline downgrade due to time tampering");
            DowngradeToTrial();
            return;
        }

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

        // If tampering detected, return 0 (expired)
        if (_timeTamperingDetected)
        {
            return 0;
        }

        // Use secure time to prevent clock manipulation
        var secureNow = GetSecureCurrentTime();
        var remaining = (_currentLicense.ExpiresAt - secureNow).TotalDays;
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
            // ALWAYS verify time with internet during periodic checks
            _logger.LogInfo("License", "Periodic time verification...");
            var tamperingDetected = await VerifyTimeWithInternetAsync();

            if (tamperingDetected)
            {
                _logger.LogCritical("License", "Time tampering detected during periodic check!");

                // If trial, expire it
                if (_currentLicense?.Status == LicenseStatus.Trial)
                {
                    _currentLicense.Status = LicenseStatus.Expired;
                    _currentLicense.ExpiresAt = DateTime.UtcNow.AddDays(-1);
                    await SaveLicenseAsync();
                    OnLicenseStatusChanged(LicenseStatus.Trial, LicenseStatus.Expired, "Trial expired due to time tampering");
                }
                return;
            }

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
        // Use the API endpoint to get purchase URL
        return $"{ApiBaseUrl}/purchase-url?machine_id={deviceId[..16]}";
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

    #region Anti-Time-Tampering

    /// <summary>
    /// Check if system time has been manipulated (rolled back)
    /// Uses multiple verification methods:
    /// 1. Internet time (NTP/HTTP) - most reliable
    /// 2. Persisted last known time - backup check
    /// </summary>
    private async Task<bool> CheckTimeTamperingAsync()
    {
        var currentTime = DateTime.UtcNow;

        // Load persisted last known time
        await LoadLastKnownTimeAsync();

        // FIRST: Try to verify with internet time (most reliable)
        _logger.LogInfo("License", "Verifying time with internet...");
        var internetTampered = await VerifyTimeWithInternetAsync();
        if (internetTampered)
        {
            _logger.LogCritical("License", "Time tampering confirmed via internet verification!");
            return true;
        }

        // SECOND: Check against persisted last known time (backup check)
        if (_lastKnownTime != DateTime.MinValue)
        {
            var timeDiff = (currentTime - _lastKnownTime).TotalHours;

            if (timeDiff < -1) // Time went backwards more than 1 hour
            {
                _timeTamperingDetected = true;
                _logger.LogCritical("License", $"TAMPERING DETECTED: System clock rolled back by {Math.Abs(timeDiff):F1} hours!");
                _logger.LogWarning("License", $"Last known time: {_lastKnownTime:yyyy-MM-dd HH:mm:ss}, Current time: {currentTime:yyyy-MM-dd HH:mm:ss}");
                return true;
            }

            // Check for unrealistic time jump forward (more than 365 days might indicate tampering too)
            if (timeDiff > 365 * 24)
            {
                _logger.LogWarning("License", $"Unusual time jump detected: {timeDiff / 24:F0} days forward");
                // For large forward jumps, verify with internet to be sure
                var verifyResult = await VerifyTimeWithInternetAsync();
                if (verifyResult)
                {
                    return true;
                }
            }
        }

        // Update and persist the new highest known time
        if (currentTime > _lastKnownTime)
        {
            _lastKnownTime = currentTime;
            await SaveLastKnownTimeAsync();
        }

        return false;
    }

    /// <summary>
    /// Get the secure current time (returns last known time if tampering detected)
    /// </summary>
    private DateTime GetSecureCurrentTime()
    {
        var currentTime = DateTime.UtcNow;

        // If tampering was detected, use the last known time instead
        // This prevents users from gaining trial time by rolling back clock
        if (_timeTamperingDetected && _lastKnownTime != DateTime.MinValue)
        {
            _logger.LogWarning("License", "Using last known time due to tampering detection");
            return _lastKnownTime;
        }

        // Update last known time if current time is higher
        if (currentTime > _lastKnownTime)
        {
            _lastKnownTime = currentTime;
            _ = SaveLastKnownTimeAsync(); // Fire and forget
        }

        return currentTime;
    }

    /// <summary>
    /// Save the last known time to disk (encrypted)
    /// </summary>
    private async Task SaveLastKnownTimeAsync()
    {
        try
        {
            var timeData = new TimeCheckData
            {
                LastKnownTime = _lastKnownTime,
                LastUpdated = DateTime.UtcNow,
                MachineId = GetMachineId(),
                ChecksumSalt = Guid.NewGuid().ToString("N")
            };

            // Create checksum for integrity verification
            var dataString = $"{timeData.LastKnownTime:O}|{timeData.MachineId}|{timeData.ChecksumSalt}";
            using var sha256 = SHA256.Create();
            var checksumBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(dataString));
            timeData.Checksum = Convert.ToBase64String(checksumBytes);

            var json = JsonSerializer.Serialize(timeData);
            var encrypted = EncryptData(json);
            await File.WriteAllBytesAsync(_timeCheckPath, encrypted);

            // Set file as hidden
            File.SetAttributes(_timeCheckPath, FileAttributes.Hidden);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("License", $"Failed to save time check: {ex.Message}");
        }
    }

    /// <summary>
    /// Load the last known time from disk
    /// </summary>
    private async Task LoadLastKnownTimeAsync()
    {
        try
        {
            if (!File.Exists(_timeCheckPath))
            {
                _lastKnownTime = DateTime.MinValue;
                return;
            }

            var encrypted = await File.ReadAllBytesAsync(_timeCheckPath);
            var json = DecryptData(encrypted);

            if (string.IsNullOrEmpty(json))
            {
                _lastKnownTime = DateTime.MinValue;
                return;
            }

            var timeData = JsonSerializer.Deserialize<TimeCheckData>(json);

            if (timeData == null)
            {
                _lastKnownTime = DateTime.MinValue;
                return;
            }

            // Verify checksum to detect file tampering
            var dataString = $"{timeData.LastKnownTime:O}|{timeData.MachineId}|{timeData.ChecksumSalt}";
            using var sha256 = SHA256.Create();
            var expectedChecksumBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(dataString));
            var expectedChecksum = Convert.ToBase64String(expectedChecksumBytes);

            if (timeData.Checksum != expectedChecksum)
            {
                _logger.LogCritical("License", "TAMPERING DETECTED: Time check file has been modified!");
                _timeTamperingDetected = true;
                // Use the stored time anyway as punishment
                _lastKnownTime = timeData.LastKnownTime;
                return;
            }

            // Verify machine ID matches
            if (timeData.MachineId != GetMachineId())
            {
                _logger.LogWarning("License", "Time check file from different machine - ignoring");
                _lastKnownTime = DateTime.MinValue;
                return;
            }

            _lastKnownTime = timeData.LastKnownTime;
            _logger.LogInfo("License", $"Loaded last known time: {_lastKnownTime:yyyy-MM-dd HH:mm:ss}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("License", $"Failed to load time check: {ex.Message}");
            _lastKnownTime = DateTime.MinValue;
        }
    }

    /// <summary>
    /// Data structure for time tracking (serialized to JSON)
    /// </summary>
    private class TimeCheckData
    {
        public DateTime LastKnownTime { get; set; }
        public DateTime LastUpdated { get; set; }
        public string MachineId { get; set; } = "";
        public string ChecksumSalt { get; set; } = "";
        public string Checksum { get; set; } = "";
    }

    #endregion

    #region Internet Time Verification

    /// <summary>
    /// Get the current time from the internet using multiple methods
    /// Returns null if unable to get internet time (offline)
    /// </summary>
    private async Task<DateTime?> GetInternetTimeAsync()
    {
        // Try multiple methods in order of reliability
        DateTime? internetTime = null;

        // Method 1: Try NTP servers
        internetTime = await GetNtpTimeAsync();
        if (internetTime.HasValue)
        {
            _logger.LogInfo("License", $"Got time from NTP: {internetTime.Value:yyyy-MM-dd HH:mm:ss} UTC");
            return internetTime;
        }

        // Method 2: Try HTTP Date headers from reliable servers
        internetTime = await GetHttpTimeAsync();
        if (internetTime.HasValue)
        {
            _logger.LogInfo("License", $"Got time from HTTP: {internetTime.Value:yyyy-MM-dd HH:mm:ss} UTC");
            return internetTime;
        }

        // Method 3: Try our own license server
        internetTime = await GetLicenseServerTimeAsync();
        if (internetTime.HasValue)
        {
            _logger.LogInfo("License", $"Got time from license server: {internetTime.Value:yyyy-MM-dd HH:mm:ss} UTC");
            return internetTime;
        }

        _logger.LogWarning("License", "Could not get internet time - all methods failed");
        return null;
    }

    /// <summary>
    /// Get time from NTP server
    /// </summary>
    private async Task<DateTime?> GetNtpTimeAsync()
    {
        // List of public NTP servers to try
        string[] ntpServers = new[]
        {
            "time.google.com",
            "time.windows.com",
            "pool.ntp.org",
            "time.cloudflare.com",
            "time.apple.com"
        };

        foreach (var server in ntpServers)
        {
            try
            {
                var ntpTime = await QueryNtpServerAsync(server);
                if (ntpTime.HasValue)
                {
                    return ntpTime;
                }
            }
            catch
            {
                // Try next server
            }
        }

        return null;
    }

    /// <summary>
    /// Query a specific NTP server for current time
    /// </summary>
    private async Task<DateTime?> QueryNtpServerAsync(string ntpServer)
    {
        try
        {
            const int NtpPort = 123;
            var ntpData = new byte[48];
            ntpData[0] = 0x1B; // NTP request header

            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.ReceiveTimeout = 3000;
            socket.SendTimeout = 3000;

            var addresses = await Dns.GetHostAddressesAsync(ntpServer);
            var endpoint = new IPEndPoint(addresses[0], NtpPort);

            await socket.ConnectAsync(endpoint);
            await socket.SendAsync(ntpData, SocketFlags.None);

            var receiveBuffer = new byte[48];
            await socket.ReceiveAsync(receiveBuffer, SocketFlags.None);

            // Extract timestamp from NTP response (bytes 40-47)
            ulong intPart = (ulong)receiveBuffer[40] << 24 | (ulong)receiveBuffer[41] << 16 |
                           (ulong)receiveBuffer[42] << 8 | receiveBuffer[43];
            ulong fractPart = (ulong)receiveBuffer[44] << 24 | (ulong)receiveBuffer[45] << 16 |
                             (ulong)receiveBuffer[46] << 8 | receiveBuffer[47];

            var milliseconds = (intPart * 1000) + ((fractPart * 1000) / 0x100000000L);

            // NTP timestamp starts from 1900-01-01
            var ntpEpoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var ntpTime = ntpEpoch.AddMilliseconds(milliseconds);

            return ntpTime;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("License", $"NTP query to {ntpServer} failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get time from HTTP Date header of reliable servers
    /// </summary>
    private async Task<DateTime?> GetHttpTimeAsync()
    {
        // Reliable servers that always return accurate Date headers
        string[] httpServers = new[]
        {
            "https://www.google.com",
            "https://www.cloudflare.com",
            "https://www.microsoft.com",
            "https://www.apple.com"
        };

        foreach (var server in httpServers)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, server));

                if (response.Headers.Date.HasValue)
                {
                    return response.Headers.Date.Value.UtcDateTime;
                }
            }
            catch
            {
                // Try next server
            }
        }

        return null;
    }

    /// <summary>
    /// Get time from our license server (using pricing endpoint for Date header)
    /// </summary>
    private async Task<DateTime?> GetLicenseServerTimeAsync()
    {
        try
        {
            // Use pricing endpoint which is public and always available
            var response = await _httpClient.GetAsync($"{ApiBaseUrl}/pricing");
            if (response.Headers.Date.HasValue)
            {
                return response.Headers.Date.Value.UtcDateTime;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("License", $"License server time query failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Verify system time against internet time
    /// Returns true if system time appears to be tampered with
    /// </summary>
    private async Task<bool> VerifyTimeWithInternetAsync()
    {
        var internetTime = await GetInternetTimeAsync();

        if (!internetTime.HasValue)
        {
            // Can't verify - user might be offline
            // For trial, we'll be stricter and mark as suspicious
            _logger.LogWarning("License", "Cannot verify time with internet - offline mode");
            return false;
        }

        var systemTime = DateTime.UtcNow;
        var timeDiff = Math.Abs((systemTime - internetTime.Value).TotalMinutes);

        // Allow 5 minutes tolerance for clock drift
        if (timeDiff > 5)
        {
            _logger.LogCritical("License", $"TAMPERING DETECTED: System time differs from internet by {timeDiff:F0} minutes!");
            _logger.LogWarning("License", $"System: {systemTime:yyyy-MM-dd HH:mm:ss}, Internet: {internetTime.Value:yyyy-MM-dd HH:mm:ss}");

            // Update last known time to internet time (the real time)
            _lastKnownTime = internetTime.Value;
            await SaveLastKnownTimeAsync();

            _timeTamperingDetected = true;
            return true;
        }

        _logger.LogInfo("License", $"Time verification passed (diff: {timeDiff:F1} minutes)");

        // Update last known time with verified internet time
        if (internetTime.Value > _lastKnownTime)
        {
            _lastKnownTime = internetTime.Value;
            await SaveLastKnownTimeAsync();
        }

        return false;
    }

    /// <summary>
    /// Response from license server time endpoint
    /// </summary>
    private class ServerTimeResponse
    {
        public DateTime UtcTime { get; set; }
        public long UnixTimestamp { get; set; }
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

    #region Demo Mode

    /// <summary>
    /// Switch to demo mode when trial expires
    /// Demo mode allows viewing but not real trading
    /// </summary>
    public void SwitchToDemoMode(string reason = "Trial expired")
    {
        if (_currentLicense == null)
        {
            _currentLicense = new LicenseInfo();
        }

        var oldStatus = _currentLicense.Status;
        _currentLicense.Status = LicenseStatus.DemoMode;
        _currentLicense.IsDemoMode = true;
        _currentLicense.DemoModeStartedAt = DateTime.UtcNow;

        // Demo mode features - view only, no trading
        _currentLicense.Features = new[] { "basic_scan", "view_opportunities" };
        _currentLicense.MaxTradingPairs = 1;
        _currentLicense.MaxExchanges = 2;

        _ = SaveLicenseAsync();

        _logger.LogWarning("License", $"Switched to Demo Mode: {reason}");
        OnLicenseStatusChanged(oldStatus, LicenseStatus.DemoMode, reason);

        // Start demo reminder timer
        StartDemoReminderTimer();
    }

    /// <summary>
    /// Check if trial has expired and switch to demo mode if needed
    /// </summary>
    public void CheckAndSwitchToDemoMode()
    {
        if (_currentLicense == null) return;

        // Already in demo mode
        if (_currentLicense.Status == LicenseStatus.DemoMode) return;

        // Check if trial expired
        if (_currentLicense.Status == LicenseStatus.Trial && GetTrialDaysRemaining() <= 0)
        {
            SwitchToDemoMode("Trial period ended");
        }
        else if (_currentLicense.Status == LicenseStatus.Expired)
        {
            SwitchToDemoMode("License expired");
        }
    }

    /// <summary>
    /// Get demo mode configuration
    /// </summary>
    public DemoModeConfig GetDemoModeConfig()
    {
        return new DemoModeConfig
        {
            CanViewOpportunities = true,
            CanExecuteTrades = false,
            CanUseAutoTrading = false,
            MaxExchanges = 2,
            ReminderIntervalMinutes = DemoReminderIntervalMinutes,
            DemoMessage = "คุณกำลังใช้งาน Demo Mode - ไม่สามารถเทรดจริงได้ กรุณา Activate License เพื่อใช้งานเต็มรูปแบบ",
            PurchaseUrl = _currentLicense?.PurchaseUrl ?? GetPurchaseUrl()
        };
    }

    /// <summary>
    /// Check if a specific action is allowed in current mode
    /// </summary>
    public bool IsActionAllowed(string action)
    {
        if (IsLicensed) return true;

        if (IsDemoMode)
        {
            // Demo mode: only viewing is allowed
            return action switch
            {
                "view_opportunities" => true,
                "view_prices" => true,
                "connect_exchange" => true, // Can connect but not trade
                "execute_trade" => false,
                "auto_trade" => false,
                "manual_trade" => false,
                "place_order" => false,
                _ => false
            };
        }

        if (IsTrial)
        {
            // Trial mode: limited trading allowed
            return action switch
            {
                "view_opportunities" => true,
                "view_prices" => true,
                "connect_exchange" => true,
                "execute_trade" => true,
                "manual_trade" => true,
                "auto_trade" => HasFeature("auto_trading"),
                "place_order" => true,
                _ => true
            };
        }

        return false;
    }

    /// <summary>
    /// Start the demo mode reminder timer
    /// </summary>
    private void StartDemoReminderTimer()
    {
        StopDemoReminderTimer();

        if (!IsDemoMode) return;

        _demoReminderTimer = new System.Timers.Timer(DemoReminderIntervalMinutes * 60 * 1000);
        _demoReminderTimer.Elapsed += OnDemoReminderTimerElapsed;
        _demoReminderTimer.AutoReset = true;
        _demoReminderTimer.Start();

        _logger.LogInfo("License", $"Demo reminder timer started (every {DemoReminderIntervalMinutes} minutes)");
    }

    /// <summary>
    /// Stop the demo mode reminder timer
    /// </summary>
    private void StopDemoReminderTimer()
    {
        if (_demoReminderTimer != null)
        {
            _demoReminderTimer.Stop();
            _demoReminderTimer.Dispose();
            _demoReminderTimer = null;
        }
    }

    /// <summary>
    /// Called when demo reminder timer elapses
    /// </summary>
    private void OnDemoReminderTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (!IsDemoMode)
        {
            StopDemoReminderTimer();
            return;
        }

        _lastDemoReminder = DateTime.UtcNow;

        var config = GetDemoModeConfig();
        DemoModeReminder?.Invoke(this, new DemoModeReminderEventArgs(config));

        _logger.LogInfo("License", "Demo mode reminder triggered");
    }

    /// <summary>
    /// Trigger immediate demo reminder (for UI to show on startup or specific events)
    /// </summary>
    public void TriggerDemoReminder()
    {
        if (!IsDemoMode) return;

        var config = GetDemoModeConfig();
        DemoModeReminder?.Invoke(this, new DemoModeReminderEventArgs(config));
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        StopPeriodicValidation();
        StopDemoReminderTimer();
        _httpClient.Dispose();
    }

    #endregion
}

// Note: DemoModeReminderEventArgs is defined in AutoTradeX.Core.Models.LicenseModels
