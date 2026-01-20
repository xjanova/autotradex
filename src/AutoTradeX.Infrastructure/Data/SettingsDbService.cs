/*
 * ============================================================================
 * AutoTrade-X - Settings Database Service (SQLite)
 * ============================================================================
 * Handles saving and loading settings to/from SQLite database
 * ============================================================================
 */

using System.Text.Json;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;

namespace AutoTradeX.Infrastructure.Data;

public interface ISettingsDbService
{
    // Generic settings
    Task<string?> GetSettingAsync(string key, string? category = null);
    Task SetSettingAsync(string key, string value, string? category = null);
    Task<T?> GetSettingAsync<T>(string key, string? category = null) where T : class;
    Task SetSettingAsync<T>(string key, T value, string? category = null) where T : class;
    Task<Dictionary<string, string>> GetAllSettingsAsync(string? category = null);
    Task DeleteSettingAsync(string key);

    // Trading Pairs
    Task<List<TradingPairConfig>> GetTradingPairsAsync();
    Task SaveTradingPairAsync(TradingPairConfig pair);
    Task DeleteTradingPairAsync(string symbol);
    Task SetTradingPairEnabledAsync(string symbol, bool enabled);

    // Exchange Configs
    Task<List<ExchangeDbConfig>> GetExchangeConfigsAsync();
    Task SaveExchangeConfigAsync(ExchangeDbConfig config);
    Task SetExchangeEnabledAsync(string name, bool enabled);
}

public class SettingsDbService : ISettingsDbService
{
    private readonly IDatabaseService _db;
    private readonly ILoggingService _logger;

    public SettingsDbService(IDatabaseService databaseService, ILoggingService logger)
    {
        _db = databaseService;
        _logger = logger;
    }

    #region Generic Settings

    public async Task<string?> GetSettingAsync(string key, string? category = null)
    {
        var sql = "SELECT Value FROM Settings WHERE Key = @Key";
        if (!string.IsNullOrEmpty(category))
        {
            sql += " AND Category = @Category";
        }

        return await _db.QueryFirstOrDefaultAsync<string>(sql, new { Key = key, Category = category });
    }

    public async Task SetSettingAsync(string key, string value, string? category = null)
    {
        await _db.ExecuteAsync(@"
            INSERT INTO Settings (Key, Value, Category, UpdatedAt)
            VALUES (@Key, @Value, @Category, @UpdatedAt)
            ON CONFLICT(Key) DO UPDATE SET
                Value = @Value,
                Category = COALESCE(@Category, Category),
                UpdatedAt = @UpdatedAt",
            new
            {
                Key = key,
                Value = value,
                Category = category,
                UpdatedAt = DateTime.UtcNow.ToString("o")
            });
    }

    public async Task<T?> GetSettingAsync<T>(string key, string? category = null) where T : class
    {
        var json = await GetSettingAsync(key, category);
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task SetSettingAsync<T>(string key, T value, string? category = null) where T : class
    {
        var json = JsonSerializer.Serialize(value);
        await SetSettingAsync(key, json, category);
    }

    public async Task<Dictionary<string, string>> GetAllSettingsAsync(string? category = null)
    {
        var sql = "SELECT Key, Value FROM Settings";
        if (!string.IsNullOrEmpty(category))
        {
            sql += " WHERE Category = @Category";
        }

        var records = await _db.QueryAsync<SettingRecord>(sql, new { Category = category });
        return records.ToDictionary(r => r.Key, r => r.Value);
    }

    public async Task DeleteSettingAsync(string key)
    {
        await _db.ExecuteAsync("DELETE FROM Settings WHERE Key = @Key", new { Key = key });
    }

    #endregion

    #region Trading Pairs

    public async Task<List<TradingPairConfig>> GetTradingPairsAsync()
    {
        var records = await _db.QueryAsync<TradingPairDbRecord>(
            "SELECT * FROM TradingPairs ORDER BY Symbol");

        return records.Select(r => new TradingPairConfig
        {
            Symbol = r.Symbol,
            BaseCurrency = r.BaseCurrency,
            QuoteCurrency = r.QuoteCurrency,
            IsEnabled = r.IsEnabled == 1,
            MinOrderSize = r.MinOrderSize.HasValue ? (decimal)r.MinOrderSize.Value : null,
            PricePrecision = r.PricePrecision,
            QuantityPrecision = r.QuantityPrecision
        }).ToList();
    }

    public async Task SaveTradingPairAsync(TradingPairConfig pair)
    {
        await _db.ExecuteAsync(@"
            INSERT INTO TradingPairs
            (Symbol, BaseCurrency, QuoteCurrency, IsEnabled, MinOrderSize, PricePrecision, QuantityPrecision)
            VALUES
            (@Symbol, @BaseCurrency, @QuoteCurrency, @IsEnabled, @MinOrderSize, @PricePrecision, @QuantityPrecision)
            ON CONFLICT(Symbol) DO UPDATE SET
                BaseCurrency = @BaseCurrency,
                QuoteCurrency = @QuoteCurrency,
                IsEnabled = @IsEnabled,
                MinOrderSize = @MinOrderSize,
                PricePrecision = @PricePrecision,
                QuantityPrecision = @QuantityPrecision",
            new
            {
                pair.Symbol,
                pair.BaseCurrency,
                pair.QuoteCurrency,
                IsEnabled = pair.IsEnabled ? 1 : 0,
                pair.MinOrderSize,
                pair.PricePrecision,
                pair.QuantityPrecision
            });

        _logger.LogInfo("Settings", $"Trading pair saved: {pair.Symbol}");
    }

    public async Task DeleteTradingPairAsync(string symbol)
    {
        await _db.ExecuteAsync(
            "DELETE FROM TradingPairs WHERE Symbol = @Symbol",
            new { Symbol = symbol });

        _logger.LogInfo("Settings", $"Trading pair deleted: {symbol}");
    }

    public async Task SetTradingPairEnabledAsync(string symbol, bool enabled)
    {
        await _db.ExecuteAsync(
            "UPDATE TradingPairs SET IsEnabled = @IsEnabled WHERE Symbol = @Symbol",
            new { Symbol = symbol, IsEnabled = enabled ? 1 : 0 });
    }

    #endregion

    #region Exchange Configs

    public async Task<List<ExchangeDbConfig>> GetExchangeConfigsAsync()
    {
        var records = await _db.QueryAsync<ExchangeConfigDbRecord>(
            "SELECT * FROM ExchangeConfigs ORDER BY Name");

        return records.Select(r => new ExchangeDbConfig
        {
            Name = r.Name,
            ApiBaseUrl = r.ApiBaseUrl,
            WebSocketUrl = r.WebSocketUrl,
            ApiKeyEnvVar = r.ApiKeyEnvVar,
            ApiSecretEnvVar = r.ApiSecretEnvVar,
            PassphraseEnvVar = r.PassphraseEnvVar,
            TradingFeePercent = (decimal)r.TradingFeePercent,
            RateLimitPerSecond = r.RateLimitPerSecond,
            TimeoutMs = r.TimeoutMs,
            MaxRetries = r.MaxRetries,
            IsEnabled = r.IsEnabled == 1
        }).ToList();
    }

    public async Task SaveExchangeConfigAsync(ExchangeDbConfig config)
    {
        await _db.ExecuteAsync(@"
            INSERT INTO ExchangeConfigs
            (Name, ApiBaseUrl, WebSocketUrl, ApiKeyEnvVar, ApiSecretEnvVar, PassphraseEnvVar,
             TradingFeePercent, RateLimitPerSecond, TimeoutMs, MaxRetries, IsEnabled, UpdatedAt)
            VALUES
            (@Name, @ApiBaseUrl, @WebSocketUrl, @ApiKeyEnvVar, @ApiSecretEnvVar, @PassphraseEnvVar,
             @TradingFeePercent, @RateLimitPerSecond, @TimeoutMs, @MaxRetries, @IsEnabled, @UpdatedAt)
            ON CONFLICT(Name) DO UPDATE SET
                ApiBaseUrl = @ApiBaseUrl,
                WebSocketUrl = @WebSocketUrl,
                ApiKeyEnvVar = @ApiKeyEnvVar,
                ApiSecretEnvVar = @ApiSecretEnvVar,
                PassphraseEnvVar = @PassphraseEnvVar,
                TradingFeePercent = @TradingFeePercent,
                RateLimitPerSecond = @RateLimitPerSecond,
                TimeoutMs = @TimeoutMs,
                MaxRetries = @MaxRetries,
                IsEnabled = @IsEnabled,
                UpdatedAt = @UpdatedAt",
            new
            {
                config.Name,
                config.ApiBaseUrl,
                config.WebSocketUrl,
                config.ApiKeyEnvVar,
                config.ApiSecretEnvVar,
                config.PassphraseEnvVar,
                config.TradingFeePercent,
                config.RateLimitPerSecond,
                config.TimeoutMs,
                config.MaxRetries,
                IsEnabled = config.IsEnabled ? 1 : 0,
                UpdatedAt = DateTime.UtcNow.ToString("o")
            });

        _logger.LogInfo("Settings", $"Exchange config saved: {config.Name}");
    }

    public async Task SetExchangeEnabledAsync(string name, bool enabled)
    {
        await _db.ExecuteAsync(
            "UPDATE ExchangeConfigs SET IsEnabled = @IsEnabled, UpdatedAt = @UpdatedAt WHERE Name = @Name",
            new { Name = name, IsEnabled = enabled ? 1 : 0, UpdatedAt = DateTime.UtcNow.ToString("o") });
    }

    #endregion
}

// Database record classes
internal class SettingRecord
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Category { get; set; }
}

internal class TradingPairDbRecord
{
    public string Symbol { get; set; } = "";
    public string BaseCurrency { get; set; } = "";
    public string QuoteCurrency { get; set; } = "";
    public int IsEnabled { get; set; }
    public double? MinOrderSize { get; set; }
    public int? PricePrecision { get; set; }
    public int? QuantityPrecision { get; set; }
}

internal class ExchangeConfigDbRecord
{
    public string Name { get; set; } = "";
    public string? ApiBaseUrl { get; set; }
    public string? WebSocketUrl { get; set; }
    public string? ApiKeyEnvVar { get; set; }
    public string? ApiSecretEnvVar { get; set; }
    public string? PassphraseEnvVar { get; set; }
    public double TradingFeePercent { get; set; }
    public int RateLimitPerSecond { get; set; }
    public int TimeoutMs { get; set; }
    public int MaxRetries { get; set; }
    public int IsEnabled { get; set; }
}

// Public models
public class TradingPairConfig
{
    public string Symbol { get; set; } = "";
    public string BaseCurrency { get; set; } = "";
    public string QuoteCurrency { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public decimal? MinOrderSize { get; set; }
    public int? PricePrecision { get; set; }
    public int? QuantityPrecision { get; set; }

    public static TradingPairConfig FromSymbol(string symbol)
    {
        var parts = symbol.Split('/');
        return new TradingPairConfig
        {
            Symbol = symbol,
            BaseCurrency = parts.Length > 0 ? parts[0] : "",
            QuoteCurrency = parts.Length > 1 ? parts[1] : "USDT"
        };
    }
}

public class ExchangeDbConfig
{
    public string Name { get; set; } = "";
    public string? ApiBaseUrl { get; set; }
    public string? WebSocketUrl { get; set; }
    public string? ApiKeyEnvVar { get; set; }
    public string? ApiSecretEnvVar { get; set; }
    public string? PassphraseEnvVar { get; set; }
    public decimal TradingFeePercent { get; set; } = 0.1m;
    public int RateLimitPerSecond { get; set; } = 10;
    public int TimeoutMs { get; set; } = 10000;
    public int MaxRetries { get; set; } = 3;
    public bool IsEnabled { get; set; } = true;
}
