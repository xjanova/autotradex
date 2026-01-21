using System.Net.Http;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Check internet connection first - required for license validation
        if (!await CheckInternetConnectionAsync())
        {
            ShowNoInternetDialog();
            Shutdown();
            return;
        }

        // Setup DI
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        // Initialize License Service
        var licenseService = Services.GetRequiredService<ILicenseService>();
        await licenseService.InitializeAsync();

        // Show license dialog if not licensed or trial
        if (!licenseService.IsLicensed)
        {
            var licenseDialog = new Views.LicenseDialog();
            var result = licenseDialog.ShowDialog();

            // User closed dialog without activating or continuing trial
            if (result != true && !licenseDialog.ContinueAsTrial)
            {
                Shutdown();
                return;
            }
        }

        // Start periodic license validation
        licenseService.StartPeriodicValidation();

        // Register toast notification handler with NotificationService
        var notificationService = Services.GetRequiredService<INotificationService>();
        notificationService.SetToastHandler(ShowToastNotification);
    }

    /// <summary>
    /// Checks if there is an active internet connection
    /// </summary>
    private async Task<bool> CheckInternetConnectionAsync()
    {
        try
        {
            // First check if network is available
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                return false;
            }

            // Try to reach license server or a reliable endpoint
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            // Try multiple endpoints for reliability
            var endpoints = new[]
            {
                "https://www.google.com",
                "https://www.cloudflare.com",
                "https://www.microsoft.com"
            };

            foreach (var endpoint in endpoints)
            {
                try
                {
                    var response = await httpClient.GetAsync(endpoint);
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Try next endpoint
                    continue;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Shows a dialog indicating no internet connection
    /// </summary>
    private void ShowNoInternetDialog()
    {
        // Create a custom styled dialog
        var dialog = new Window
        {
            Title = "Internet Connection Required",
            Width = 450,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize
        };

        var border = new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#1A0A2E"),
                (Color)ColorConverter.ConvertFromString("#0A0A1A"),
                45),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
            BorderThickness = new Thickness(2),
            Padding = new Thickness(30),
            Effect = new DropShadowEffect
            {
                BlurRadius = 30,
                ShadowDepth = 5,
                Opacity = 0.6,
                Color = (Color)ColorConverter.ConvertFromString("#EF4444")
            }
        };

        var stackPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // Warning icon using text
        var iconText = new TextBlock
        {
            Text = "⚠",
            FontSize = 48,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 15)
        };

        var titleText = new TextBlock
        {
            Text = "ไม่พบการเชื่อมต่ออินเทอร์เน็ต",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var messageText = new TextBlock
        {
            Text = "AutoTrade-X ต้องการการเชื่อมต่ออินเทอร์เน็ต\nเพื่อตรวจสอบใบอนุญาตและทำงาน\n\nกรุณาตรวจสอบการเชื่อมต่อและลองใหม่อีกครั้ง",
            FontSize = 14,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A0A0A0")),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20)
        };

        var closeButton = new Button
        {
            Content = "ปิดโปรแกรม",
            Width = 150,
            Height = 40,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Background = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#EF4444"),
                (Color)ColorConverter.ConvertFromString("#DC2626"),
                90),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // Style the button
        closeButton.Template = CreateRoundedButtonTemplate();
        closeButton.Click += (s, e) => dialog.Close();

        stackPanel.Children.Add(iconText);
        stackPanel.Children.Add(titleText);
        stackPanel.Children.Add(messageText);
        stackPanel.Children.Add(closeButton);

        border.Child = stackPanel;
        dialog.Content = border;

        dialog.ShowDialog();
    }

    /// <summary>
    /// Creates a rounded button control template
    /// </summary>
    private ControlTemplate CreateRoundedButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));

        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        borderFactory.SetValue(Border.PaddingProperty, new Thickness(20, 10, 20, 10));

        var contentPresenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contentPresenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

        borderFactory.AppendChild(contentPresenterFactory);
        template.VisualTree = borderFactory;

        return template;
    }

    /// <summary>
    /// Shows a WPF toast notification in the bottom-right corner
    /// </summary>
    private void ShowToastNotification(NotificationItem notification)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                // Create toast window
                var toast = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    Width = 350,
                    Height = 100,
                    WindowStartupLocation = WindowStartupLocation.Manual
                };

                // Position in bottom-right corner
                var workArea = SystemParameters.WorkArea;
                toast.Left = workArea.Right - toast.Width - 20;
                toast.Top = workArea.Bottom - toast.Height - 20;

                // Create content with dark theme styling
                var border = new Border
                {
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(16),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F1F2E")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(notification.TypeColor)),
                    BorderThickness = new Thickness(1),
                    Effect = new DropShadowEffect
                    {
                        BlurRadius = 20,
                        ShadowDepth = 5,
                        Opacity = 0.5
                    }
                };

                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var titleText = new TextBlock
                {
                    Text = notification.Title,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 14,
                    Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetRow(titleText, 0);

                var messageText = new TextBlock
                {
                    Text = notification.Message.Replace("\n", " "),
                    FontSize = 12,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A0FFFFFF")),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                Grid.SetRow(messageText, 1);

                grid.Children.Add(titleText);
                grid.Children.Add(messageText);
                border.Child = grid;
                toast.Content = border;

                // Click to dismiss
                toast.MouseLeftButtonDown += (s, args) => toast.Close();

                // Auto-close after 4 seconds with fade out
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                    fadeOut.Completed += (s2, args2) => toast.Close();
                    toast.BeginAnimation(Window.OpacityProperty, fadeOut);
                };

                // Fade in animation
                toast.Opacity = 0;
                toast.Show();
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                fadeIn.Completed += (s, args) => timer.Start();
                toast.BeginAnimation(Window.OpacityProperty, fadeIn);
            }
            catch
            {
                // Ignore toast display errors
            }
        }));
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Config Service - must be registered first
        services.AddSingleton<IConfigService, ConfigService>();

        // Logging Service (text file logs)
        services.AddSingleton<ILoggingService, FileLoggingService>();

        // License Service - must be early for feature checking
        services.AddSingleton<ILicenseService, LicenseService>();

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

        // Currency Converter Service - THB/USDT rates for Bitkub
        services.AddSingleton<ICurrencyConverterService>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggingService>();
            return new CurrencyConverterService(logger);
        });

        // Demo Wallet Service for paper trading (SQLite backed)
        services.AddSingleton<DemoWalletService>();

        // Exchange Client Factory for creating appropriate clients (with currency converter for Bitkub)
        services.AddSingleton<ExchangeClientFactory>(sp =>
        {
            var configService = sp.GetRequiredService<IConfigService>();
            var logger = sp.GetRequiredService<ILoggingService>();
            var currencyConverter = sp.GetRequiredService<ICurrencyConverterService>();
            return new ExchangeClientFactory(configService.GetConfig(), logger, currencyConverter);
        });

        // Register IExchangeClientFactory interface to use the same instance
        services.AddSingleton<IExchangeClientFactory>(sp => sp.GetRequiredService<ExchangeClientFactory>());

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

        // ArbEngine and BalancePoolService - uses config and license to determine live or simulation mode
        services.AddSingleton<IArbEngine>(sp =>
        {
            var configService = sp.GetRequiredService<IConfigService>();
            var licenseService = sp.GetRequiredService<ILicenseService>();
            var logger = sp.GetRequiredService<ILoggingService>();
            var config = configService.GetConfig();

            IExchangeClient exchangeA;
            IExchangeClient exchangeB;

            // Check if licensed - Demo version can only use simulation mode
            var isLicensed = licenseService.IsLicensed &&
                             licenseService.CurrentLicense?.Status == Core.Models.LicenseStatus.Valid;

            if (!isLicensed)
            {
                // DEMO VERSION - Force simulation mode, no real wallet access
                logger.LogWarning("App", "DEMO VERSION: Real wallet access is disabled. Using simulation mode only.");
                exchangeA = new SimulationExchangeClient("ExchangeA", logger, isExchangeA: true);
                exchangeB = new SimulationExchangeClient("ExchangeB", logger, isExchangeA: false);
            }
            else if (config.General.LiveTrading)
            {
                // Licensed + Live Trading Mode - Use real exchange clients
                logger.LogWarning("App", "Starting in LIVE TRADING mode (Licensed)!");

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
                // Licensed + Simulation Mode - Use simulation clients (safe, no real money)
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

        // Smart Scanner Service for intelligent coin scanning with REAL exchange data
        services.AddSingleton<ISmartScannerService>(sp =>
        {
            var coinDataService = sp.GetRequiredService<ICoinDataService>();
            var exchangeFactory = sp.GetRequiredService<IExchangeClientFactory>();
            var logger = sp.GetRequiredService<ILoggingService>();
            return new SmartScannerService(coinDataService, exchangeFactory, logger);
        });

        // Coin Logo Service for fetching coin and exchange logos
        services.AddSingleton<ICoinLogoService>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggingService>();
            return new CoinLogoService(logger);
        });

        // Strategy Service for AI trading strategies
        services.AddSingleton<IStrategyService, StrategyService>();

        // Project Service for trading projects (max 10 pairs)
        services.AddSingleton<IProjectService, ProjectService>();

        // Connection Status Service for monitoring API connections
        services.AddSingleton<IConnectionStatusService>(sp =>
        {
            var exchangeFactory = sp.GetRequiredService<IExchangeClientFactory>();
            var configService = sp.GetRequiredService<IConfigService>();
            var strategyService = sp.GetRequiredService<IStrategyService>();
            var logger = sp.GetRequiredService<ILoggingService>();
            return new ConnectionStatusService(exchangeFactory, configService, strategyService, logger);
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
    private static IExchangeClient CreateExchangeClient(ExchangeConfig config, ILoggingService logger, ICurrencyConverterService? currencyConverter = null)
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
            return new BitkubClient(config, logger, currencyConverter);
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
