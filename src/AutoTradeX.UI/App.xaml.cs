using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using AutoTradeX.Core.Services;
using AutoTradeX.Infrastructure.Services;
using AutoTradeX.Infrastructure.ExchangeClients;
using AutoTradeX.Infrastructure.Data;

namespace AutoTradeX.UI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            MessageBox.Show($"Error: {e.Exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Setup DI
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Config Service - must be registered first
        services.AddSingleton<IConfigService, ConfigService>();

        // Logging Service (text file logs)
        services.AddSingleton<ILoggingService, FileLoggingService>();

        // SQLite Database Service - central data storage
        services.AddSingleton<IDatabaseService>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggingService>();
            return new DatabaseService(logger);
        });

        // Balance History Service (SQLite)
        services.AddSingleton<IBalanceHistoryService, BalanceHistoryService>();

        // Settings Database Service (SQLite)
        services.AddSingleton<ISettingsDbService, SettingsDbService>();

        // CoinGecko Service for real-time prices and coin data
        services.AddSingleton<ICoinDataService, CoinGeckoService>();

        // Demo Wallet Service for paper trading (SQLite backed)
        services.AddSingleton<DemoWalletService>();

        // Exchange Client Factory for creating appropriate clients
        services.AddSingleton<ExchangeClientFactory>();

        // Exchange clients - register both based on config
        services.AddSingleton<IExchangeClient>(sp =>
        {
            var configService = sp.GetRequiredService<IConfigService>();
            var logger = sp.GetRequiredService<ILoggingService>();
            var config = configService.GetConfig();

            if (config.General.LiveTrading && configService.HasValidCredentials(config.ExchangeA))
            {
                return CreateExchangeClient(config.ExchangeA, logger);
            }
            return new SimulationExchangeClient("ExchangeA", logger, isExchangeA: true);
        });

        // ArbEngine and BalancePoolService - uses config to determine live or simulation mode
        services.AddSingleton<IArbEngine>(sp =>
        {
            var configService = sp.GetRequiredService<IConfigService>();
            var logger = sp.GetRequiredService<ILoggingService>();
            var config = configService.GetConfig();

            IExchangeClient exchangeA;
            IExchangeClient exchangeB;

            if (config.General.LiveTrading)
            {
                // Live Trading Mode - Use real exchange clients
                logger.LogWarning("App", "Starting in LIVE TRADING mode!");

                // Validate credentials before creating clients
                if (!configService.HasValidCredentials(config.ExchangeA))
                {
                    logger.LogCritical("App", "Exchange A credentials not configured! Falling back to simulation.");
                    exchangeA = new SimulationExchangeClient("ExchangeA", logger, isExchangeA: true);
                }
                else
                {
                    exchangeA = CreateExchangeClient(config.ExchangeA, logger);
                }

                if (!configService.HasValidCredentials(config.ExchangeB))
                {
                    logger.LogCritical("App", "Exchange B credentials not configured! Falling back to simulation.");
                    exchangeB = new SimulationExchangeClient("ExchangeB", logger, isExchangeA: false);
                }
                else
                {
                    exchangeB = CreateExchangeClient(config.ExchangeB, logger);
                }
            }
            else
            {
                // Simulation Mode - Use simulation clients (safe, no real money)
                logger.LogInfo("App", "Starting in SIMULATION mode (paper trading)");
                exchangeA = new SimulationExchangeClient("ExchangeA", logger, isExchangeA: true);
                exchangeB = new SimulationExchangeClient("ExchangeB", logger, isExchangeA: false);
            }

            // Store exchange clients for BalancePoolService
            sp.GetRequiredService<ExchangeClientHolder>().ExchangeA = exchangeA;
            sp.GetRequiredService<ExchangeClientHolder>().ExchangeB = exchangeB;

            return new ArbEngine(exchangeA, exchangeB, logger, configService);
        });

        // Exchange client holder for sharing between services
        services.AddSingleton<ExchangeClientHolder>();

        // Balance Pool Service for real P&L tracking
        services.AddSingleton<IBalancePoolService>(sp =>
        {
            var holder = sp.GetRequiredService<ExchangeClientHolder>();
            var logger = sp.GetRequiredService<ILoggingService>();
            var configService = sp.GetRequiredService<IConfigService>();
            var coinDataService = sp.GetRequiredService<ICoinDataService>();

            // Ensure ArbEngine is created first to populate holder
            var _ = sp.GetRequiredService<IArbEngine>();

            return new BalancePoolService(
                holder.ExchangeA!,
                holder.ExchangeB!,
                logger,
                configService,
                coinDataService);
        });

        // Trade History Service for persistent trade storage (SQLite backed)
        services.AddSingleton<ITradeHistoryService>(sp =>
        {
            var db = sp.GetRequiredService<IDatabaseService>();
            var logger = sp.GetRequiredService<ILoggingService>();
            return new TradeHistoryService(db, logger);
        });

        // Notification Service for alerts and in-app notifications
        services.AddSingleton<INotificationService, NotificationService>();

        // Smart Scanner Service for intelligent coin scanning
        services.AddSingleton<ISmartScannerService>(sp =>
        {
            var coinDataService = sp.GetRequiredService<ICoinDataService>();
            var logger = sp.GetRequiredService<ILoggingService>();
            return new SmartScannerService(coinDataService, logger);
        });

        // Coin Logo Service for fetching coin and exchange logos
        services.AddSingleton<ICoinLogoService>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggingService>();
            return new CoinLogoService(logger);
        });

        // Initialize database on startup
        Task.Run(async () =>
        {
            var db = Services.GetRequiredService<IDatabaseService>();
            await db.InitializeAsync();
        });
    }

    /// <summary>
    /// Helper class to share exchange clients between services
    /// </summary>
    public class ExchangeClientHolder
    {
        public IExchangeClient? ExchangeA { get; set; }
        public IExchangeClient? ExchangeB { get; set; }
    }

    /// <summary>
    /// Creates the appropriate exchange client based on configuration
    /// </summary>
    private static IExchangeClient CreateExchangeClient(ExchangeConfig config, ILoggingService logger)
    {
        // Determine exchange type from API URL or name
        var name = config.Name.ToLowerInvariant();
        var url = config.ApiBaseUrl.ToLowerInvariant();

        if (name.Contains("binance") || url.Contains("binance"))
        {
            return new BinanceClient(config, logger);
        }
        else if (name.Contains("bybit") || url.Contains("bybit"))
        {
            return new BybitClient(config, logger);
        }
        else if (name.Contains("bitkub") || url.Contains("bitkub"))
        {
            return new BitkubClient(config, logger);
        }
        else if (name.Contains("kucoin") || url.Contains("kucoin"))
        {
            return new KuCoinClient(config, logger);
        }
        else if (name.Contains("okx") || url.Contains("okx"))
        {
            return new OKXClient(config, logger);
        }
        else if (name.Contains("gate") || url.Contains("gate"))
        {
            return new GateIOClient(config, logger);
        }
        else
        {
            // Default to simulation for unknown exchanges
            logger.LogWarning("App", $"Unknown exchange type: {config.Name}. Using simulation client.");
            return new SimulationExchangeClient(config.Name, logger, isExchangeA: true);
        }
    }
}
