/*
 * ============================================================================
 * AutoTrade-X - Unit Tests for ConfigService
 * ============================================================================
 */

using AutoTradeX.Core.Models;
using AutoTradeX.Infrastructure.Services;

namespace AutoTradeX.Tests;

/// <summary>
/// Unit Tests สำหรับ ConfigService
/// </summary>
public class ConfigServiceTests
{
    [Fact]
    public void ValidateConfig_ReturnsNoErrors_ForValidConfig()
    {
        // Arrange
        var configService = new ConfigService("test_config.json");
        var validConfig = CreateValidConfig();

        // Act
        var errors = configService.ValidateConfig(validConfig);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateConfig_ReturnsErrors_ForInvalidConfig()
    {
        // Arrange
        var configService = new ConfigService("test_config.json");
        var invalidConfig = new AppConfig
        {
            ExchangeA = new ExchangeConfig { ApiBaseUrl = "" },  // Empty URL
            ExchangeB = new ExchangeConfig { ApiBaseUrl = "" },
            Strategy = new StrategyConfig
            {
                MinSpreadPercentage = -1,  // Negative
                PollingIntervalMs = 50     // Too low
            },
            Risk = new RiskConfig
            {
                MaxPositionSizePerTrade = 0,  // Zero
                MaxDailyLoss = -10  // Negative
            },
            TradingPairs = new List<string> { "BTCUSDT" }  // Invalid format (no /)
        };

        // Act
        var errors = configService.ValidateConfig(invalidConfig);

        // Assert
        Assert.NotEmpty(errors);
        Assert.True(errors.Count >= 5);
    }

    [Fact]
    public void ValidateConfig_ReturnsError_ForInvalidTradingPairFormat()
    {
        // Arrange
        var configService = new ConfigService("test_config.json");
        var config = CreateValidConfig();
        config.TradingPairs = new List<string> { "BTCUSDT" };  // ไม่มี /

        // Act
        var errors = configService.ValidateConfig(config);

        // Assert
        Assert.Contains(errors, e => e.Contains("Invalid trading pair format"));
    }

    [Fact]
    public void ValidateConfig_ReturnsError_WhenNoTradingPairs()
    {
        // Arrange
        var configService = new ConfigService("test_config.json");
        var config = CreateValidConfig();
        config.TradingPairs = new List<string>();

        // Act
        var errors = configService.ValidateConfig(config);

        // Assert
        Assert.Contains(errors, e => e.Contains("trading pair is required"));
    }

    [Fact]
    public void GetApiKey_ReturnsEnvironmentVariable()
    {
        // Arrange
        var configService = new ConfigService("test_config.json");
        var testKey = "TEST_API_KEY_" + Guid.NewGuid().ToString("N");
        var testValue = "test_value_123";
        Environment.SetEnvironmentVariable(testKey, testValue);

        try
        {
            // Act
            var result = configService.GetApiKey(testKey);

            // Assert
            Assert.Equal(testValue, result);
        }
        finally
        {
            // Cleanup
            Environment.SetEnvironmentVariable(testKey, null);
        }
    }

    [Fact]
    public void GetApiKey_ReturnsNull_WhenNotSet()
    {
        // Arrange
        var configService = new ConfigService("test_config.json");
        var nonExistentKey = "NON_EXISTENT_KEY_" + Guid.NewGuid().ToString("N");

        // Act
        var result = configService.GetApiKey(nonExistentKey);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void HasValidCredentials_ReturnsTrue_WhenBothSet()
    {
        // Arrange
        var configService = new ConfigService("test_config.json");
        var keyEnv = "TEST_KEY_" + Guid.NewGuid().ToString("N");
        var secretEnv = "TEST_SECRET_" + Guid.NewGuid().ToString("N");

        Environment.SetEnvironmentVariable(keyEnv, "key123");
        Environment.SetEnvironmentVariable(secretEnv, "secret456");

        var exchangeConfig = new ExchangeConfig
        {
            ApiKeyEnvVar = keyEnv,
            ApiSecretEnvVar = secretEnv
        };

        try
        {
            // Act
            var result = configService.HasValidCredentials(exchangeConfig);

            // Assert
            Assert.True(result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(keyEnv, null);
            Environment.SetEnvironmentVariable(secretEnv, null);
        }
    }

    [Fact]
    public void HasValidCredentials_ReturnsFalse_WhenKeyMissing()
    {
        // Arrange
        var configService = new ConfigService("test_config.json");
        var exchangeConfig = new ExchangeConfig
        {
            ApiKeyEnvVar = "NONEXISTENT_KEY_123",
            ApiSecretEnvVar = "NONEXISTENT_SECRET_123"
        };

        // Act
        var result = configService.HasValidCredentials(exchangeConfig);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ExportConfigJson_ReturnsValidJson()
    {
        // Arrange
        var configService = new ConfigService("test_config.json");

        // Act
        var json = configService.ExportConfigJson();

        // Assert
        Assert.NotNull(json);
        Assert.NotEmpty(json);
        Assert.Contains("exchangeA", json);
        Assert.Contains("strategy", json);
        Assert.Contains("risk", json);
    }

    [Fact]
    public void ImportConfigJson_ParsesValidJson()
    {
        // Arrange
        var configService = new ConfigService("test_config.json");
        var json = @"{
            ""exchangeA"": { ""name"": ""TestA"", ""apiBaseUrl"": ""https://test.com"", ""tradingFeePercent"": 0.1 },
            ""exchangeB"": { ""name"": ""TestB"", ""apiBaseUrl"": ""https://test.com"", ""tradingFeePercent"": 0.1 },
            ""strategy"": { ""minSpreadPercentage"": 0.5, ""pollingIntervalMs"": 1000 },
            ""risk"": { ""maxPositionSizePerTrade"": 100, ""maxDailyLoss"": 50, ""maxTradesPerDay"": 100 },
            ""tradingPairs"": [""BTC/USDT""]
        }";

        // Act
        var config = configService.ImportConfigJson(json);

        // Assert
        Assert.NotNull(config);
        Assert.Equal("TestA", config.ExchangeA.Name);
        Assert.Equal(0.5m, config.Strategy.MinSpreadPercentage);
        Assert.Single(config.TradingPairs);
    }

    [Fact]
    public void ImportConfigJson_ThrowsOnInvalidJson()
    {
        // Arrange
        var configService = new ConfigService("test_config.json");
        var invalidJson = "{ invalid json }";

        // Act & Assert
        // JsonSerializer.Deserialize throws JsonException which inherits from Exception
        var exception = Assert.ThrowsAny<Exception>(() => configService.ImportConfigJson(invalidJson));
        Assert.NotNull(exception);
    }

    private AppConfig CreateValidConfig()
    {
        return new AppConfig
        {
            ExchangeA = new ExchangeConfig
            {
                Name = "ExchangeA",
                ApiBaseUrl = "https://api.exchange-a.com",
                TradingFeePercent = 0.1m
            },
            ExchangeB = new ExchangeConfig
            {
                Name = "ExchangeB",
                ApiBaseUrl = "https://api.exchange-b.com",
                TradingFeePercent = 0.1m
            },
            Strategy = new StrategyConfig
            {
                MinSpreadPercentage = 0.3m,
                MinExpectedProfitQuoteCurrency = 0.5m,
                PollingIntervalMs = 1000
            },
            Risk = new RiskConfig
            {
                MaxPositionSizePerTrade = 100m,
                MaxDailyLoss = 50m,
                MaxTradesPerDay = 100
            },
            General = new GeneralConfig
            {
                LiveTrading = false
            },
            TradingPairs = new List<string> { "BTC/USDT", "ETH/USDT" }
        };
    }
}
