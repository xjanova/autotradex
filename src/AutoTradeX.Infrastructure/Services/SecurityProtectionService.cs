/*
 * ============================================================================
 * AutoTrade-X - Security Protection Service
 * ============================================================================
 * Comprehensive anti-crack, anti-debug, and integrity protection
 * Protects against:
 * - Memory manipulation
 * - Debugger attachment
 * - Code tampering
 * - License bypass attempts
 * - Runtime patches
 * ============================================================================
 */

using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AutoTradeX.Core.Interfaces;

namespace AutoTradeX.Infrastructure.Services;

/// <summary>
/// Security protection service for anti-crack and anti-tamper mechanisms
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class SecurityProtectionService : IDisposable
{
    private readonly ILoggingService _logger;
    private readonly ILicenseService _licenseService;

    private System.Timers.Timer? _integrityCheckTimer;
    private System.Timers.Timer? _memoryProtectionTimer;

    private readonly Dictionary<string, string> _criticalMethodHashes = new();
    private readonly Dictionary<string, object> _protectedValues = new();
    private readonly object _lockObject = new();

    private bool _tamperingDetected = false;
    private DateTime _lastIntegrityCheck = DateTime.MinValue;
    private int _integrityFailCount = 0;

    // Windows API imports for debugger detection
    [DllImport("kernel32.dll")]
    private static extern bool IsDebuggerPresent();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, ref bool isDebuggerPresent);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass,
        ref IntPtr processInformation, int processInformationLength, out int returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    // Events
    public event EventHandler<SecurityViolationEventArgs>? SecurityViolationDetected;

    // Constants
    private const int IntegrityCheckIntervalMs = 60000; // 1 minute
    private const int MemoryCheckIntervalMs = 30000; // 30 seconds
    private const int MaxIntegrityFailures = 3;

    public bool IsTamperingDetected => _tamperingDetected;

    public SecurityProtectionService(ILoggingService logger, ILicenseService licenseService)
    {
        _logger = logger;
        _licenseService = licenseService;
    }

    #region Initialization

    /// <summary>
    /// Initialize all security protection mechanisms
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger.LogInfo("Security", "Initializing security protection...");

        try
        {
            // Run initial checks
            var debuggerCheck = CheckForDebugger();
            var tamperCheck = await CheckCodeIntegrityAsync();
            var vmCheck = CheckForVirtualMachine();

            if (debuggerCheck)
            {
                _logger.LogCritical("Security", "Debugger detected during initialization!");
                OnSecurityViolation("Debugger", "Debugger attachment detected");
            }

            // Store critical method hashes for later verification
            ComputeCriticalMethodHashes();

            // Start protection timers
            StartIntegrityCheckTimer();
            StartMemoryProtectionTimer();

            _logger.LogInfo("Security", "Security protection initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError("Security", $"Failed to initialize security: {ex.Message}");
        }
    }

    #endregion

    #region Debugger Detection

    /// <summary>
    /// Check for debugger presence using multiple methods
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool CheckForDebugger()
    {
        try
        {
            // Method 1: Windows API IsDebuggerPresent
            if (IsDebuggerPresent())
            {
                _logger.LogWarning("Security", "Debugger detected (IsDebuggerPresent)");
                return true;
            }

            // Method 2: Check for remote debugger
            bool isRemoteDebugger = false;
            CheckRemoteDebuggerPresent(GetCurrentProcess(), ref isRemoteDebugger);
            if (isRemoteDebugger)
            {
                _logger.LogWarning("Security", "Remote debugger detected");
                return true;
            }

            // Method 3: Check .NET Debugger.IsAttached
            if (Debugger.IsAttached)
            {
                _logger.LogWarning("Security", "Debugger detected (Debugger.IsAttached)");
                return true;
            }

            // Method 4: NtQueryInformationProcess - DebugPort check
            if (CheckDebugPort())
            {
                _logger.LogWarning("Security", "Debug port detected");
                return true;
            }

            // Method 5: Timing-based detection
            if (CheckTimingAnomaly())
            {
                _logger.LogWarning("Security", "Timing anomaly detected (possible debugger)");
                return true;
            }

            // Method 6: Check for common debugging tools
            if (CheckDebuggerProcesses())
            {
                _logger.LogWarning("Security", "Known debugger process detected");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Security", $"Debugger check error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Check debug port using NtQueryInformationProcess
    /// </summary>
    private bool CheckDebugPort()
    {
        try
        {
            const int ProcessDebugPort = 7;
            IntPtr debugPort = IntPtr.Zero;

            int status = NtQueryInformationProcess(GetCurrentProcess(), ProcessDebugPort,
                ref debugPort, IntPtr.Size, out _);

            return status == 0 && debugPort != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Timing-based debugger detection
    /// A debugger will slow down execution significantly
    /// </summary>
    private bool CheckTimingAnomaly()
    {
        try
        {
            var sw = Stopwatch.StartNew();

            // Perform a simple operation that should be fast
            long sum = 0;
            for (int i = 0; i < 10000; i++)
            {
                sum += i;
            }

            sw.Stop();

            // If it takes more than 100ms, something is slowing us down
            if (sw.ElapsedMilliseconds > 100)
            {
                return true;
            }

            // Prevent optimization from removing the loop
            if (sum < 0) Console.WriteLine(sum);

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Check for common debugger processes
    /// </summary>
    private bool CheckDebuggerProcesses()
    {
        try
        {
            string[] debuggerNames = new[]
            {
                "dnspy", "dnspy32", "dnspy64",
                "ollydbg", "ollyice", "immunitydebugger",
                "x64dbg", "x32dbg",
                "ida", "ida64", "idag", "idag64",
                "cheatengine", "cheatengine-x86_64",
                "fiddler", "wireshark",
                "processhacker", "procmon", "procexp",
                "de4dot", "ilspy", "dotpeek",
                "mega dumper", "megadumper"
            };

            var processes = Process.GetProcesses();
            foreach (var process in processes)
            {
                try
                {
                    var name = process.ProcessName.ToLowerInvariant();
                    if (debuggerNames.Any(d => name.Contains(d)))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Access denied for some processes
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Code Integrity

    /// <summary>
    /// Check code integrity by verifying assembly hashes
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<bool> CheckCodeIntegrityAsync()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var location = assembly.Location;

            if (string.IsNullOrEmpty(location))
            {
                // Running in single-file deployment mode
                return await CheckMemoryIntegrityAsync();
            }

            // Check if assembly file has been modified
            if (File.Exists(location))
            {
                var fileInfo = new FileInfo(location);

                // Store and verify file hash
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(location);
                var hash = Convert.ToBase64String(sha256.ComputeHash(stream));

                var expectedHash = GetStoredAssemblyHash();
                if (!string.IsNullOrEmpty(expectedHash) && hash != expectedHash)
                {
                    _logger.LogCritical("Security", "Assembly file has been modified!");
                    return false;
                }

                // Store hash for future checks
                StoreAssemblyHash(hash);
            }

            // Verify critical method hashes
            return await VerifyCriticalMethodsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("Security", $"Code integrity check error: {ex.Message}");
            return true; // Don't fail on error
        }
    }

    /// <summary>
    /// Check memory integrity for critical license-related code
    /// </summary>
    private async Task<bool> CheckMemoryIntegrityAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                // Verify that critical methods haven't been patched in memory
                foreach (var kvp in _criticalMethodHashes)
                {
                    var currentHash = ComputeMethodHash(kvp.Key);
                    if (!string.IsNullOrEmpty(kvp.Value) && currentHash != kvp.Value)
                    {
                        _logger.LogCritical("Security", $"Method {kvp.Key} has been modified in memory!");
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return true;
            }
        });
    }

    /// <summary>
    /// Compute hashes for critical license-checking methods
    /// </summary>
    private void ComputeCriticalMethodHashes()
    {
        try
        {
            var criticalMethods = new[]
            {
                "AutoTradeX.Infrastructure.Services.LicenseService.IsLicensed",
                "AutoTradeX.Infrastructure.Services.LicenseService.IsTrial",
                "AutoTradeX.Infrastructure.Services.LicenseService.IsDemoMode",
                "AutoTradeX.Infrastructure.Services.LicenseService.CanTrade",
                "AutoTradeX.Infrastructure.Services.LicenseService.HasFeature",
                "AutoTradeX.Infrastructure.Services.LicenseService.ValidateLicenseAsync",
                "AutoTradeX.Infrastructure.Services.SecurityProtectionService.CheckForDebugger"
            };

            foreach (var methodName in criticalMethods)
            {
                var hash = ComputeMethodHash(methodName);
                if (!string.IsNullOrEmpty(hash))
                {
                    _criticalMethodHashes[methodName] = hash;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Security", $"Failed to compute method hashes: {ex.Message}");
        }
    }

    /// <summary>
    /// Compute a hash of a method's IL bytecode
    /// </summary>
    private string ComputeMethodHash(string fullMethodName)
    {
        try
        {
            var parts = fullMethodName.Split('.');
            var methodName = parts[^1];
            var typeName = string.Join(".", parts[..^1]);

            var assembly = Assembly.GetExecutingAssembly();
            var type = assembly.GetType(typeName);

            if (type == null)
            {
                // Try other assemblies
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = asm.GetType(typeName);
                    if (type != null) break;
                }
            }

            if (type == null) return "";

            var method = type.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            if (method == null) return "";

            var methodBody = method.GetMethodBody();
            if (methodBody == null) return "";

            var ilBytes = methodBody.GetILAsByteArray();
            if (ilBytes == null) return "";

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(ilBytes);
            return Convert.ToBase64String(hash);
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Verify critical methods haven't been tampered with
    /// </summary>
    private async Task<bool> VerifyCriticalMethodsAsync()
    {
        return await Task.Run(() =>
        {
            foreach (var kvp in _criticalMethodHashes)
            {
                var currentHash = ComputeMethodHash(kvp.Key);
                if (!string.IsNullOrEmpty(kvp.Value) && !string.IsNullOrEmpty(currentHash))
                {
                    if (currentHash != kvp.Value)
                    {
                        _logger.LogCritical("Security", $"Critical method {kvp.Key} has been tampered!");
                        return false;
                    }
                }
            }
            return true;
        });
    }

    #endregion

    #region Memory Protection

    /// <summary>
    /// Protect critical values in memory using obfuscation
    /// </summary>
    public void ProtectValue(string key, object value)
    {
        lock (_lockObject)
        {
            // Store value with obfuscation
            var obfuscatedValue = ObfuscateValue(value);
            _protectedValues[key] = obfuscatedValue;
        }
    }

    /// <summary>
    /// Retrieve a protected value from memory
    /// </summary>
    public T? GetProtectedValue<T>(string key)
    {
        lock (_lockObject)
        {
            if (_protectedValues.TryGetValue(key, out var obfuscatedValue))
            {
                return DeobfuscateValue<T>(obfuscatedValue);
            }
            return default;
        }
    }

    /// <summary>
    /// Check if a protected value exists
    /// </summary>
    public bool HasProtectedValue(string key)
    {
        lock (_lockObject)
        {
            return _protectedValues.ContainsKey(key);
        }
    }

    /// <summary>
    /// Obfuscate a value for storage
    /// </summary>
    private object ObfuscateValue(object value)
    {
        // Simple XOR-based obfuscation for runtime protection
        var valueStr = value?.ToString() ?? "";
        var bytes = Encoding.UTF8.GetBytes(valueStr);
        var key = GetObfuscationKey();

        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] ^= key[i % key.Length];
        }

        return new ProtectedData
        {
            Data = bytes,
            Type = value?.GetType().FullName ?? "System.Object",
            Timestamp = DateTime.UtcNow.Ticks
        };
    }

    /// <summary>
    /// Deobfuscate a stored value
    /// </summary>
    private T? DeobfuscateValue<T>(object obfuscated)
    {
        try
        {
            if (obfuscated is not ProtectedData protectedData)
                return default;

            var key = GetObfuscationKey();
            var bytes = new byte[protectedData.Data.Length];
            Array.Copy(protectedData.Data, bytes, bytes.Length);

            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] ^= key[i % key.Length];
            }

            var valueStr = Encoding.UTF8.GetString(bytes);

            // Convert back to original type
            if (typeof(T) == typeof(bool))
            {
                return (T)(object)bool.Parse(valueStr);
            }
            if (typeof(T) == typeof(int))
            {
                return (T)(object)int.Parse(valueStr);
            }
            if (typeof(T) == typeof(string))
            {
                return (T)(object)valueStr;
            }

            return default;
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// Get obfuscation key based on machine-specific data
    /// </summary>
    private byte[] GetObfuscationKey()
    {
        var machineId = _licenseService.GetMachineId();
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes($"ATX-PROTECT-{machineId}"));
    }

    private class ProtectedData
    {
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string Type { get; set; } = "";
        public long Timestamp { get; set; }
    }

    #endregion

    #region License Verification Guard

    /// <summary>
    /// Verify license status with multiple checks (anti-bypass protection)
    /// Call this before any sensitive operation
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool VerifyLicenseIntegrity()
    {
        try
        {
            // Check 1: Basic license status
            var isLicensed = _licenseService.IsLicensed;
            var isTrial = _licenseService.IsTrial;
            var isDemoMode = _licenseService.IsDemoMode;

            // Check 2: Verify these values match stored protected values
            if (HasProtectedValue("license_status"))
            {
                var storedIsLicensed = GetProtectedValue<bool>("license_status");
                if (storedIsLicensed != isLicensed)
                {
                    _logger.LogCritical("Security", "License status mismatch detected!");
                    OnSecurityViolation("LicenseBypass", "License status has been tampered");
                    return false;
                }
            }

            // Check 3: Check for debugger during sensitive operation
            if (CheckForDebugger())
            {
                _logger.LogCritical("Security", "Debugger detected during license check!");
                OnSecurityViolation("Debugger", "Debugger detected during sensitive operation");
                return false;
            }

            // Check 4: Timing check (anti-step-through)
            var sw = Stopwatch.StartNew();
            Thread.SpinWait(1000);
            sw.Stop();
            if (sw.ElapsedMilliseconds > 50)
            {
                _logger.LogWarning("Security", "Suspicious timing during license check");
            }

            // Store current status for future verification
            ProtectValue("license_status", isLicensed);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Security", $"License integrity check error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Guard a sensitive action with security checks
    /// </summary>
    public async Task<bool> GuardSensitiveActionAsync(string actionName, Func<Task<bool>> action)
    {
        _logger.LogInfo("Security", $"Security guard: {actionName}");

        // Pre-action checks
        if (_tamperingDetected)
        {
            _logger.LogCritical("Security", $"Action {actionName} blocked due to previous tampering");
            return false;
        }

        if (CheckForDebugger())
        {
            _logger.LogCritical("Security", $"Action {actionName} blocked due to debugger");
            OnSecurityViolation("Debugger", $"Debugger detected before {actionName}");
            return false;
        }

        if (!VerifyLicenseIntegrity())
        {
            _logger.LogCritical("Security", $"Action {actionName} blocked due to integrity failure");
            return false;
        }

        // Execute the action
        var result = await action();

        // Post-action checks
        if (CheckForDebugger())
        {
            _logger.LogWarning("Security", $"Debugger attached during {actionName}");
        }

        return result;
    }

    #endregion

    #region Virtual Machine Detection

    /// <summary>
    /// Check if running in a virtual machine
    /// (Informational - doesn't block execution)
    /// </summary>
    public bool CheckForVirtualMachine()
    {
        try
        {
            var vmIndicators = new List<string>();

            // Check system manufacturer
            var manufacturer = GetSystemProperty("Win32_ComputerSystem", "Manufacturer");
            var model = GetSystemProperty("Win32_ComputerSystem", "Model");

            string[] vmManufacturers = new[]
            {
                "vmware", "virtualbox", "virtual", "xen", "qemu",
                "microsoft corporation", "parallels", "innotek"
            };

            if (vmManufacturers.Any(vm =>
                manufacturer?.ToLowerInvariant().Contains(vm) == true ||
                model?.ToLowerInvariant().Contains(vm) == true))
            {
                vmIndicators.Add($"VM manufacturer: {manufacturer} {model}");
            }

            // Check BIOS version
            var bios = GetSystemProperty("Win32_BIOS", "SerialNumber");
            if (!string.IsNullOrEmpty(bios) && vmManufacturers.Any(vm =>
                bios.ToLowerInvariant().Contains(vm)))
            {
                vmIndicators.Add($"VM BIOS: {bios}");
            }

            // Check for VM-specific drivers/services
            var services = System.ServiceProcess.ServiceController.GetServices();
            string[] vmServices = new[]
            {
                "vmtools", "vmhgfs", "vmmemctl", "vmrawdsk", "vmusbmouse",
                "vmci", "vmwaretray", "vmvss", "vmxnet",
                "vboxservice", "vboxmouse", "vboxguest", "vboxsf", "vboxvideo"
            };

            foreach (var service in services)
            {
                if (vmServices.Any(vm => service.ServiceName.ToLowerInvariant().Contains(vm)))
                {
                    vmIndicators.Add($"VM service: {service.ServiceName}");
                }
            }

            if (vmIndicators.Count > 0)
            {
                _logger.LogInfo("Security", $"VM indicators found: {string.Join(", ", vmIndicators)}");
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private string? GetSystemProperty(string wmiClass, string propertyName)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT {propertyName} FROM {wmiClass}");

            foreach (var obj in searcher.Get())
            {
                return obj[propertyName]?.ToString();
            }
        }
        catch
        {
            // Ignore WMI errors
        }
        return null;
    }

    #endregion

    #region Integrity Check Timer

    /// <summary>
    /// Start periodic integrity checking
    /// </summary>
    private void StartIntegrityCheckTimer()
    {
        _integrityCheckTimer = new System.Timers.Timer(IntegrityCheckIntervalMs);
        _integrityCheckTimer.Elapsed += async (s, e) => await OnIntegrityCheckTimerElapsed();
        _integrityCheckTimer.AutoReset = true;
        _integrityCheckTimer.Start();

        _logger.LogInfo("Security", "Started integrity check timer");
    }

    private async Task OnIntegrityCheckTimerElapsed()
    {
        try
        {
            _lastIntegrityCheck = DateTime.UtcNow;

            // Check for debugger
            if (CheckForDebugger())
            {
                _integrityFailCount++;
                OnSecurityViolation("Debugger", "Debugger detected during periodic check");
            }

            // Check code integrity
            var integrityOk = await CheckCodeIntegrityAsync();
            if (!integrityOk)
            {
                _integrityFailCount++;
                OnSecurityViolation("CodeTamper", "Code integrity check failed");
            }

            // Check license integrity
            VerifyLicenseIntegrity();

            // If too many failures, mark as tampered
            if (_integrityFailCount >= MaxIntegrityFailures)
            {
                _tamperingDetected = true;
                OnSecurityViolation("MultipleFailures", $"Multiple integrity failures ({_integrityFailCount})");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Security", $"Integrity check error: {ex.Message}");
        }
    }

    /// <summary>
    /// Start memory protection monitoring
    /// </summary>
    private void StartMemoryProtectionTimer()
    {
        _memoryProtectionTimer = new System.Timers.Timer(MemoryCheckIntervalMs);
        _memoryProtectionTimer.Elapsed += (s, e) => CheckMemoryProtection();
        _memoryProtectionTimer.AutoReset = true;
        _memoryProtectionTimer.Start();
    }

    private void CheckMemoryProtection()
    {
        try
        {
            // Verify protected values haven't been tampered
            if (HasProtectedValue("license_status"))
            {
                var isLicensedStored = GetProtectedValue<bool>("license_status");
                var isLicensedActual = _licenseService.IsLicensed;

                if (isLicensedStored != isLicensedActual)
                {
                    _logger.LogCritical("Security", "Memory tampering detected - license status modified!");
                    _integrityFailCount++;
                    OnSecurityViolation("MemoryTamper", "License status memory tampering");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Security", $"Memory check error: {ex.Message}");
        }
    }

    #endregion

    #region Assembly Hash Storage

    private string GetStoredAssemblyHash()
    {
        try
        {
            var hashPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AutoTradeX", ".integrity");

            if (File.Exists(hashPath))
            {
                return File.ReadAllText(hashPath);
            }
        }
        catch { }
        return "";
    }

    private void StoreAssemblyHash(string hash)
    {
        try
        {
            var hashPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AutoTradeX", ".integrity");

            File.WriteAllText(hashPath, hash);
            File.SetAttributes(hashPath, FileAttributes.Hidden);
        }
        catch { }
    }

    #endregion

    #region Security Violation Handler

    private void OnSecurityViolation(string violationType, string message)
    {
        _logger.LogCritical("Security", $"SECURITY VIOLATION [{violationType}]: {message}");

        SecurityViolationDetected?.Invoke(this, new SecurityViolationEventArgs
        {
            ViolationType = violationType,
            Message = message,
            Timestamp = DateTime.UtcNow,
            IsCritical = violationType != "VM"
        });
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _integrityCheckTimer?.Stop();
        _integrityCheckTimer?.Dispose();
        _memoryProtectionTimer?.Stop();
        _memoryProtectionTimer?.Dispose();
    }

    #endregion
}

/// <summary>
/// Security violation event arguments
/// </summary>
public class SecurityViolationEventArgs : EventArgs
{
    public string ViolationType { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public bool IsCritical { get; set; }
}
