/*
 * ============================================================================
 * AutoTrade-X - Cross-Exchange Arbitrage Trading Bot
 * ============================================================================
 * Exchange Client Factory - Creates appropriate exchange clients
 * ============================================================================
 */

using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;

namespace AutoTradeX.Infrastructure.ExchangeClients;

/// <summary>
/// ExchangeClientFactory - Factory สำหรับสร้าง Exchange Client
/// รองรับ: Binance, Bybit, และ Simulation Mode
/// </summary>
public class ExchangeClientFactory : IExchangeClientFactory
{
    private readonly ILoggingService _logger;
    private readonly AppConfig _config;

    public ExchangeClientFactory(AppConfig config, ILoggingService logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// สร้าง Exchange Client ตามชื่อ
    /// </summary>
    public IExchangeClient CreateClient(string exchangeName)
    {
        return exchangeName.ToLowerInvariant() switch
        {
            // Binance
            "binance" => CreateBinanceClient(),

            // KuCoin
            "kucoin" => CreateKuCoinClient(),

            // Bybit
            "bybit" => CreateBybitClient(),

            // OKX
            "okx" => CreateOKXClient(),

            // Gate.io
            "gate.io" or "gateio" or "gate" => CreateGateIOClient(),

            // Bitkub (Thailand)
            "bitkub" => CreateBitkubClient(),

            // Legacy names (backward compatible)
            "exchangea" or "a" => CreateExchangeAClient(),
            "exchangeb" or "b" => CreateExchangeBClient(),

            // Simulation
            "simulation_a" or "sim_a" => CreateSimulationClient(true),
            "simulation_b" or "sim_b" => CreateSimulationClient(false),
            "simulation" or "sim" => CreateSimulationClient(true),

            _ => throw new ArgumentException($"Unknown exchange: {exchangeName}. " +
                $"Supported: Binance, KuCoin, Bybit, OKX, Gate.io, Bitkub, Simulation_A, Simulation_B")
        };
    }

    /// <summary>
    /// สร้าง Binance Client
    /// </summary>
    public IExchangeClient CreateBinanceClient()
    {
        if (!_config.General.LiveTrading)
        {
            _logger.LogInfo("Factory", "Creating Simulation Client for Binance (LiveTrading=false)");
            return CreateSimulationClient(true);
        }

        // ใช้ ExchangeA config สำหรับ Binance
        var config = new ExchangeConfig
        {
            Name = "Binance",
            ApiBaseUrl = "https://api.binance.com",
            ApiKeyEnvVar = _config.ExchangeA.ApiKeyEnvVar,
            ApiSecretEnvVar = _config.ExchangeA.ApiSecretEnvVar,
            TradingFeePercent = _config.ExchangeA.TradingFeePercent,
            TimeoutMs = _config.ExchangeA.TimeoutMs,
            RateLimitPerSecond = _config.ExchangeA.RateLimitPerSecond,
            MaxRetries = _config.ExchangeA.MaxRetries
        };

        _logger.LogInfo("Factory", "Creating Binance Client");
        return new BinanceClient(config, _logger);
    }

    /// <summary>
    /// สร้าง KuCoin Client
    /// </summary>
    public IExchangeClient CreateKuCoinClient()
    {
        if (!_config.General.LiveTrading)
        {
            _logger.LogInfo("Factory", "Creating Simulation Client for KuCoin (LiveTrading=false)");
            return CreateSimulationClient(false);
        }

        // ใช้ ExchangeB config สำหรับ KuCoin
        var config = new ExchangeConfig
        {
            Name = "KuCoin",
            ApiBaseUrl = "https://api.kucoin.com",
            ApiKeyEnvVar = _config.ExchangeB.ApiKeyEnvVar,
            ApiSecretEnvVar = _config.ExchangeB.ApiSecretEnvVar,
            TradingFeePercent = _config.ExchangeB.TradingFeePercent,
            TimeoutMs = _config.ExchangeB.TimeoutMs,
            RateLimitPerSecond = _config.ExchangeB.RateLimitPerSecond,
            MaxRetries = _config.ExchangeB.MaxRetries
        };

        _logger.LogInfo("Factory", "Creating KuCoin Client");
        return new KuCoinClient(config, _logger);
    }

    /// <summary>
    /// สร้าง Bybit Client
    /// </summary>
    public IExchangeClient CreateBybitClient()
    {
        if (!_config.General.LiveTrading)
        {
            _logger.LogInfo("Factory", "Creating Simulation Client for Bybit (LiveTrading=false)");
            return CreateSimulationClient(false);
        }

        // ใช้ ExchangeB config สำหรับ Bybit
        var config = new ExchangeConfig
        {
            Name = "Bybit",
            ApiBaseUrl = "https://api.bybit.com",
            ApiKeyEnvVar = _config.ExchangeB.ApiKeyEnvVar,
            ApiSecretEnvVar = _config.ExchangeB.ApiSecretEnvVar,
            TradingFeePercent = _config.ExchangeB.TradingFeePercent,
            TimeoutMs = _config.ExchangeB.TimeoutMs,
            RateLimitPerSecond = _config.ExchangeB.RateLimitPerSecond,
            MaxRetries = _config.ExchangeB.MaxRetries
        };

        _logger.LogInfo("Factory", "Creating Bybit Client");
        return new BybitClient(config, _logger);
    }

    /// <summary>
    /// สร้าง OKX Client
    /// </summary>
    public IExchangeClient CreateOKXClient()
    {
        if (!_config.General.LiveTrading)
        {
            _logger.LogInfo("Factory", "Creating Simulation Client for OKX (LiveTrading=false)");
            return CreateSimulationClient(false);
        }

        var config = new ExchangeConfig
        {
            Name = "OKX",
            ApiBaseUrl = "https://www.okx.com",
            ApiKeyEnvVar = "AUTOTRADEX_OKX_API_KEY",
            ApiSecretEnvVar = "AUTOTRADEX_OKX_API_SECRET",
            TradingFeePercent = 0.1m,
            TimeoutMs = _config.ExchangeB.TimeoutMs,
            RateLimitPerSecond = 10,
            MaxRetries = _config.ExchangeB.MaxRetries
        };

        _logger.LogInfo("Factory", "Creating OKX Client");
        return new OKXClient(config, _logger);
    }

    /// <summary>
    /// สร้าง Gate.io Client
    /// </summary>
    public IExchangeClient CreateGateIOClient()
    {
        if (!_config.General.LiveTrading)
        {
            _logger.LogInfo("Factory", "Creating Simulation Client for Gate.io (LiveTrading=false)");
            return CreateSimulationClient(false);
        }

        var config = new ExchangeConfig
        {
            Name = "Gate.io",
            ApiBaseUrl = "https://api.gateio.ws",
            ApiKeyEnvVar = "AUTOTRADEX_GATEIO_API_KEY",
            ApiSecretEnvVar = "AUTOTRADEX_GATEIO_API_SECRET",
            TradingFeePercent = 0.2m,
            TimeoutMs = _config.ExchangeB.TimeoutMs,
            RateLimitPerSecond = 10,
            MaxRetries = _config.ExchangeB.MaxRetries
        };

        _logger.LogInfo("Factory", "Creating Gate.io Client");
        return new GateIOClient(config, _logger);
    }

    /// <summary>
    /// สร้าง Bitkub Client (Thailand Exchange)
    /// </summary>
    public IExchangeClient CreateBitkubClient()
    {
        if (!_config.General.LiveTrading)
        {
            _logger.LogInfo("Factory", "Creating Simulation Client for Bitkub (LiveTrading=false)");
            return CreateSimulationClient(false);
        }

        var config = new ExchangeConfig
        {
            Name = "Bitkub",
            ApiBaseUrl = "https://api.bitkub.com",
            ApiKeyEnvVar = "AUTOTRADEX_BITKUB_API_KEY",
            ApiSecretEnvVar = "AUTOTRADEX_BITKUB_API_SECRET",
            TradingFeePercent = 0.25m,
            TimeoutMs = _config.ExchangeB.TimeoutMs,
            RateLimitPerSecond = 5,
            MaxRetries = _config.ExchangeB.MaxRetries
        };

        _logger.LogInfo("Factory", "Creating Bitkub Client");
        return new BitkubClient(config, _logger);
    }

    /// <summary>
    /// สร้าง Client สำหรับ Exchange A (Smart detection)
    /// </summary>
    public IExchangeClient CreateExchangeAClient()
    {
        if (!_config.General.LiveTrading)
        {
            _logger.LogInfo("Factory", "Creating Simulation Client for Exchange A (LiveTrading=false)");
            return CreateSimulationClient(true);
        }

        // Auto-detect exchange type from config name
        var exchangeType = DetectExchangeType(_config.ExchangeA.Name);

        return exchangeType switch
        {
            "binance" => CreateRealBinanceClient(_config.ExchangeA),
            "kucoin" => CreateRealKuCoinClient(_config.ExchangeA),
            "bybit" => CreateRealBybitClient(_config.ExchangeA),
            _ => new ExchangeAClient(_config.ExchangeA, _logger) // Fallback to placeholder
        };
    }

    /// <summary>
    /// สร้าง Client สำหรับ Exchange B (Smart detection)
    /// </summary>
    public IExchangeClient CreateExchangeBClient()
    {
        if (!_config.General.LiveTrading)
        {
            _logger.LogInfo("Factory", "Creating Simulation Client for Exchange B (LiveTrading=false)");
            return CreateSimulationClient(false);
        }

        // Auto-detect exchange type from config name
        var exchangeType = DetectExchangeType(_config.ExchangeB.Name);

        return exchangeType switch
        {
            "binance" => CreateRealBinanceClient(_config.ExchangeB),
            "kucoin" => CreateRealKuCoinClient(_config.ExchangeB),
            "bybit" => CreateRealBybitClient(_config.ExchangeB),
            _ => new ExchangeBClient(_config.ExchangeB, _logger) // Fallback to placeholder
        };
    }

    /// <summary>
    /// สร้าง Binance Client จาก ExchangeConfig
    /// </summary>
    private IExchangeClient CreateRealBinanceClient(ExchangeConfig config)
    {
        var binanceConfig = new ExchangeConfig
        {
            Name = "Binance",
            ApiBaseUrl = string.IsNullOrEmpty(config.ApiBaseUrl) || config.ApiBaseUrl.Contains("placeholder")
                ? "https://api.binance.com"
                : config.ApiBaseUrl,
            ApiKeyEnvVar = config.ApiKeyEnvVar,
            ApiSecretEnvVar = config.ApiSecretEnvVar,
            TradingFeePercent = config.TradingFeePercent,
            TimeoutMs = config.TimeoutMs,
            RateLimitPerSecond = config.RateLimitPerSecond,
            MaxRetries = config.MaxRetries
        };

        _logger.LogInfo("Factory", $"Creating Binance Client (from {config.Name} config)");
        return new BinanceClient(binanceConfig, _logger);
    }

    /// <summary>
    /// สร้าง KuCoin Client จาก ExchangeConfig
    /// </summary>
    private IExchangeClient CreateRealKuCoinClient(ExchangeConfig config)
    {
        var kucoinConfig = new ExchangeConfig
        {
            Name = "KuCoin",
            ApiBaseUrl = string.IsNullOrEmpty(config.ApiBaseUrl) || config.ApiBaseUrl.Contains("placeholder")
                ? "https://api.kucoin.com"
                : config.ApiBaseUrl,
            ApiKeyEnvVar = config.ApiKeyEnvVar,
            ApiSecretEnvVar = config.ApiSecretEnvVar,
            TradingFeePercent = config.TradingFeePercent,
            TimeoutMs = config.TimeoutMs,
            RateLimitPerSecond = config.RateLimitPerSecond,
            MaxRetries = config.MaxRetries
        };

        _logger.LogInfo("Factory", $"Creating KuCoin Client (from {config.Name} config)");
        return new KuCoinClient(kucoinConfig, _logger);
    }

    /// <summary>
    /// สร้าง Bybit Client จาก ExchangeConfig
    /// </summary>
    private IExchangeClient CreateRealBybitClient(ExchangeConfig config)
    {
        var bybitConfig = new ExchangeConfig
        {
            Name = "Bybit",
            ApiBaseUrl = string.IsNullOrEmpty(config.ApiBaseUrl) || config.ApiBaseUrl.Contains("placeholder")
                ? "https://api.bybit.com"
                : config.ApiBaseUrl,
            ApiKeyEnvVar = config.ApiKeyEnvVar,
            ApiSecretEnvVar = config.ApiSecretEnvVar,
            TradingFeePercent = config.TradingFeePercent,
            TimeoutMs = config.TimeoutMs,
            RateLimitPerSecond = config.RateLimitPerSecond,
            MaxRetries = config.MaxRetries
        };

        _logger.LogInfo("Factory", $"Creating Bybit Client (from {config.Name} config)");
        return new BybitClient(bybitConfig, _logger);
    }

    /// <summary>
    /// ตรวจจับประเภท Exchange จากชื่อ
    /// </summary>
    private string DetectExchangeType(string name)
    {
        var lowerName = name.ToLowerInvariant();

        if (lowerName.Contains("binance"))
            return "binance";

        if (lowerName.Contains("bybit"))
            return "bybit";

        if (lowerName.Contains("okx"))
            return "okx";

        if (lowerName.Contains("kucoin"))
            return "kucoin";

        return "unknown";
    }

    /// <summary>
    /// สร้าง Simulation Client
    /// </summary>
    private IExchangeClient CreateSimulationClient(bool isExchangeA)
    {
        var name = isExchangeA ? "SimExchangeA" : "SimExchangeB";
        var client = new SimulationExchangeClient(name, _logger, isExchangeA);

        // ตั้งค่า fee ตาม config
        var feePercent = isExchangeA
            ? _config.ExchangeA.TradingFeePercent
            : _config.ExchangeB.TradingFeePercent;
        client.SetFeePercent(feePercent);

        return client;
    }

    /// <summary>
    /// ดึงรายชื่อ Exchange ที่รองรับ
    /// </summary>
    public IEnumerable<string> GetSupportedExchanges()
    {
        return new[]
        {
            "Binance",
            "KuCoin",
            "Bybit",
            "OKX",
            "Bitkub",
            "Gate.io",
            "Simulation_A",
            "Simulation_B"
        };
    }
}
