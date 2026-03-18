/*
 * ============================================================================
 * AutoTrade-X - API Credentials Service
 * ============================================================================
 * Manages encrypted storage of API credentials in SQLite database
 * Uses AES-256 encryption with machine-specific key derivation
 * ============================================================================
 */

using System.Security.Cryptography;
using System.Text;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Infrastructure.Data;

namespace AutoTradeX.Infrastructure.Services;

/// <summary>
/// Interface for managing API credentials
/// </summary>
public interface IApiCredentialsService
{
    /// <summary>
    /// Save API credentials for an exchange (encrypted)
    /// </summary>
    Task SaveCredentialsAsync(string exchangeName, string apiKey, string apiSecret, string? passphrase = null);

    /// <summary>
    /// Get API credentials for an exchange (decrypted)
    /// </summary>
    Task<ApiCredentials?> GetCredentialsAsync(string exchangeName);

    /// <summary>
    /// Check if credentials exist for an exchange
    /// </summary>
    Task<bool> HasCredentialsAsync(string exchangeName);

    /// <summary>
    /// Mark credentials as verified (after successful connection test)
    /// </summary>
    Task MarkAsVerifiedAsync(string exchangeName);

    /// <summary>
    /// Delete credentials for an exchange
    /// </summary>
    Task DeleteCredentialsAsync(string exchangeName);

    /// <summary>
    /// Get all exchanges that have credentials configured
    /// </summary>
    Task<IEnumerable<string>> GetConfiguredExchangesAsync();

    /// <summary>
    /// Load all credentials into environment variables (for backward compatibility)
    /// </summary>
    Task LoadCredentialsToEnvironmentAsync();
}

/// <summary>
/// API credentials model
/// </summary>
public class ApiCredentials
{
    public string ExchangeName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string? Passphrase { get; set; }
    public bool IsVerified { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
}

/// <summary>
/// Database model for ApiCredentials table
/// </summary>
internal class ApiCredentialsEntity
{
    public string ExchangeName { get; set; } = string.Empty;
    public string ApiKeyEncrypted { get; set; } = string.Empty;
    public string ApiSecretEncrypted { get; set; } = string.Empty;
    public string? PassphraseEncrypted { get; set; }
    public int IsVerified { get; set; }
    public string? LastVerifiedAt { get; set; }
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
}

/// <summary>
/// Service for managing encrypted API credentials in SQLite
/// </summary>
public class ApiCredentialsService : IApiCredentialsService
{
    private readonly IDatabaseService _database;
    private readonly ILoggingService _logger;
    private readonly byte[] _encryptionKey;

    // Static key prefix (combined with machine-specific data for full key)
    private static readonly byte[] KeyPrefix = Encoding.UTF8.GetBytes("AutoTradeX-API-Creds-v1");

    public ApiCredentialsService(IDatabaseService database, ILoggingService logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _encryptionKey = DeriveEncryptionKey();
    }

    /// <summary>
    /// Derive a machine-specific encryption key
    /// Uses: machine name + username + static prefix
    /// </summary>
    private byte[] DeriveEncryptionKey()
    {
        // Combine machine-specific data with static prefix
        var machineData = $"{Environment.MachineName}-{Environment.UserName}-{KeyPrefix}";

        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(machineData));
    }

    /// <summary>
    /// Encrypt a string using AES-256
    /// </summary>
    private string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;

        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Prepend IV to encrypted data
        var result = new byte[aes.IV.Length + encryptedBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// Decrypt a string using AES-256
    /// </summary>
    private string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return string.Empty;

        try
        {
            var fullCipher = Convert.FromBase64String(cipherText);

            using var aes = Aes.Create();
            aes.Key = _encryptionKey;

            // Extract IV from the beginning
            var iv = new byte[aes.BlockSize / 8];
            var encryptedBytes = new byte[fullCipher.Length - iv.Length];
            Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
            Buffer.BlockCopy(fullCipher, iv.Length, encryptedBytes, 0, encryptedBytes.Length);

            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);

            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError("ApiCredentials", $"Decryption failed: {ex.Message}");
            return string.Empty;
        }
    }

    public async Task SaveCredentialsAsync(string exchangeName, string apiKey, string apiSecret, string? passphrase = null)
    {
        if (string.IsNullOrEmpty(exchangeName))
            throw new ArgumentException("Exchange name is required", nameof(exchangeName));

        var normalizedName = exchangeName.ToLower();

        _logger.LogInfo("ApiCredentials", $"Saving credentials for {exchangeName}");

        var encryptedKey = Encrypt(apiKey);
        var encryptedSecret = Encrypt(apiSecret);
        var encryptedPassphrase = !string.IsNullOrEmpty(passphrase) ? Encrypt(passphrase) : null;

        var sql = @"
            INSERT OR REPLACE INTO ApiCredentials
            (ExchangeName, ApiKeyEncrypted, ApiSecretEncrypted, PassphraseEncrypted, IsVerified, UpdatedAt)
            VALUES (@ExchangeName, @ApiKeyEncrypted, @ApiSecretEncrypted, @PassphraseEncrypted, 0, datetime('now'))
        ";

        await _database.ExecuteAsync(sql, new
        {
            ExchangeName = normalizedName,
            ApiKeyEncrypted = encryptedKey,
            ApiSecretEncrypted = encryptedSecret,
            PassphraseEncrypted = encryptedPassphrase
        });

        // Also set environment variable for immediate use (backward compatibility)
        SetEnvironmentVariable(exchangeName, apiKey, apiSecret, passphrase);

        _logger.LogInfo("ApiCredentials", $"Credentials saved for {exchangeName}");
    }

    public async Task<ApiCredentials?> GetCredentialsAsync(string exchangeName)
    {
        var normalizedName = exchangeName.ToLower();

        var sql = "SELECT * FROM ApiCredentials WHERE ExchangeName = @ExchangeName";
        var entity = await _database.QueryFirstOrDefaultAsync<ApiCredentialsEntity>(sql, new { ExchangeName = normalizedName });

        if (entity == null) return null;

        return new ApiCredentials
        {
            ExchangeName = exchangeName,
            ApiKey = Decrypt(entity.ApiKeyEncrypted),
            ApiSecret = Decrypt(entity.ApiSecretEncrypted),
            Passphrase = !string.IsNullOrEmpty(entity.PassphraseEncrypted) ? Decrypt(entity.PassphraseEncrypted) : null,
            IsVerified = entity.IsVerified == 1,
            LastVerifiedAt = !string.IsNullOrEmpty(entity.LastVerifiedAt) ? DateTime.Parse(entity.LastVerifiedAt) : null
        };
    }

    public async Task<bool> HasCredentialsAsync(string exchangeName)
    {
        var normalizedName = exchangeName.ToLower();
        var sql = "SELECT COUNT(*) FROM ApiCredentials WHERE ExchangeName = @ExchangeName";
        var count = await _database.ExecuteScalarAsync<int>(sql, new { ExchangeName = normalizedName });
        return count > 0;
    }

    public async Task MarkAsVerifiedAsync(string exchangeName)
    {
        var normalizedName = exchangeName.ToLower();

        var sql = @"
            UPDATE ApiCredentials
            SET IsVerified = 1, LastVerifiedAt = datetime('now'), UpdatedAt = datetime('now')
            WHERE ExchangeName = @ExchangeName
        ";

        await _database.ExecuteAsync(sql, new { ExchangeName = normalizedName });
        _logger.LogInfo("ApiCredentials", $"{exchangeName} marked as verified");
    }

    public async Task DeleteCredentialsAsync(string exchangeName)
    {
        var normalizedName = exchangeName.ToLower();

        var sql = "DELETE FROM ApiCredentials WHERE ExchangeName = @ExchangeName";
        await _database.ExecuteAsync(sql, new { ExchangeName = normalizedName });

        // Clear environment variables
        ClearEnvironmentVariable(exchangeName);

        _logger.LogInfo("ApiCredentials", $"Credentials deleted for {exchangeName}");
    }

    public async Task<IEnumerable<string>> GetConfiguredExchangesAsync()
    {
        var sql = "SELECT ExchangeName FROM ApiCredentials";
        return await _database.QueryAsync<string>(sql);
    }

    public async Task LoadCredentialsToEnvironmentAsync()
    {
        _logger.LogInfo("ApiCredentials", "Loading all credentials to environment variables...");

        var sql = "SELECT * FROM ApiCredentials";
        var entities = await _database.QueryAsync<ApiCredentialsEntity>(sql);

        foreach (var entity in entities)
        {
            var apiKey = Decrypt(entity.ApiKeyEncrypted);
            var apiSecret = Decrypt(entity.ApiSecretEncrypted);
            var passphrase = !string.IsNullOrEmpty(entity.PassphraseEncrypted)
                ? Decrypt(entity.PassphraseEncrypted)
                : null;

            SetEnvironmentVariable(entity.ExchangeName, apiKey, apiSecret, passphrase);
        }

        _logger.LogInfo("ApiCredentials", $"Loaded credentials for {entities.Count()} exchanges");
    }

    /// <summary>
    /// Set environment variables for an exchange (for backward compatibility with existing code)
    /// </summary>
    private void SetEnvironmentVariable(string exchangeName, string apiKey, string apiSecret, string? passphrase)
    {
        var prefix = GetEnvVarPrefix(exchangeName);

        if (!string.IsNullOrEmpty(apiKey))
            Environment.SetEnvironmentVariable($"{prefix}_API_KEY", apiKey);

        if (!string.IsNullOrEmpty(apiSecret))
            Environment.SetEnvironmentVariable($"{prefix}_API_SECRET", apiSecret);

        if (!string.IsNullOrEmpty(passphrase))
        {
            var passEnv = exchangeName.ToLower() == "okx"
                ? "AUTOTRADEX_OKX_PASSPHRASE"
                : $"{prefix}_API_KEY_PASSPHRASE";
            Environment.SetEnvironmentVariable(passEnv, passphrase);
        }
    }

    /// <summary>
    /// Clear environment variables for an exchange
    /// </summary>
    private void ClearEnvironmentVariable(string exchangeName)
    {
        var prefix = GetEnvVarPrefix(exchangeName);

        Environment.SetEnvironmentVariable($"{prefix}_API_KEY", null);
        Environment.SetEnvironmentVariable($"{prefix}_API_SECRET", null);
        Environment.SetEnvironmentVariable($"{prefix}_API_KEY_PASSPHRASE", null);

        if (exchangeName.ToLower() == "okx")
            Environment.SetEnvironmentVariable("AUTOTRADEX_OKX_PASSPHRASE", null);
    }

    /// <summary>
    /// Get environment variable prefix for exchange
    /// </summary>
    private string GetEnvVarPrefix(string exchangeName)
    {
        return $"AUTOTRADEX_{exchangeName.ToUpper().Replace(".", "").Replace(" ", "")}";
    }
}
