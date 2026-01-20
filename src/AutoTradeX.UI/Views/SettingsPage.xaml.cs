using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;

namespace AutoTradeX.UI.Views;

public partial class SettingsPage : UserControl
{
    private IConfigService? _configService;
    private ILoggingService? _logger;
    private IExchangeClientFactory? _exchangeFactory;
    private readonly string _credentialsPath;

    public SettingsPage()
    {
        InitializeComponent();
        _credentialsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AutoTradeX",
            "credentials.encrypted.json"
        );

        // Defer service initialization until Loaded event
        Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _configService = App.Services?.GetService<IConfigService>();
            _logger = App.Services?.GetService<ILoggingService>();
            _exchangeFactory = App.Services?.GetService<IExchangeClientFactory>();
            LoadSettings();
            LoadSavedCredentials();
            LoadTradingSettings();
        }
        catch { }
    }

    private void LoadSettings()
    {
        try
        {
            var config = _configService?.GetConfig();
            if (config == null) return;

            // Load demo mode setting (inverse of LiveTrading)
            DemoModeEnabled.IsChecked = !config.General.LiveTrading;

            _logger?.LogInfo("Settings", "Settings loaded successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError("Settings", $"Error loading settings: {ex.Message}");
        }
    }

    private void LoadSavedCredentials()
    {
        try
        {
            // Try to load from environment variables first
            LoadFromEnvironment();

            // Also check if there are saved credentials
            if (File.Exists(_credentialsPath))
            {
                var json = File.ReadAllText(_credentialsPath);
                var credentials = JsonSerializer.Deserialize<SavedCredentials>(json);

                if (credentials != null)
                {
                    // Only load if not already set from environment
                    if (string.IsNullOrEmpty(BinanceApiKey.Text))
                    {
                        BinanceApiKey.Text = credentials.BinanceApiKey ?? "";
                        BinanceApiSecret.Password = credentials.BinanceApiSecret ?? "";
                    }
                    if (string.IsNullOrEmpty(KuCoinApiKey.Text))
                    {
                        KuCoinApiKey.Text = credentials.KuCoinApiKey ?? "";
                        KuCoinApiSecret.Password = credentials.KuCoinApiSecret ?? "";
                        KuCoinPassphrase.Password = credentials.KuCoinPassphrase ?? "";
                    }
                    if (string.IsNullOrEmpty(BitkubApiKey.Text))
                    {
                        BitkubApiKey.Text = credentials.BitkubApiKey ?? "";
                        BitkubApiSecret.Password = credentials.BitkubApiSecret ?? "";
                    }
                    if (string.IsNullOrEmpty(OKXApiKey.Text))
                    {
                        OKXApiKey.Text = credentials.OKXApiKey ?? "";
                        OKXApiSecret.Password = credentials.OKXApiSecret ?? "";
                        OKXPassphrase.Password = credentials.OKXPassphrase ?? "";
                    }
                    if (string.IsNullOrEmpty(BybitApiKey.Text))
                    {
                        BybitApiKey.Text = credentials.BybitApiKey ?? "";
                        BybitApiSecret.Password = credentials.BybitApiSecret ?? "";
                    }
                    if (string.IsNullOrEmpty(GateIOApiKey.Text))
                    {
                        GateIOApiKey.Text = credentials.GateIOApiKey ?? "";
                        GateIOApiSecret.Password = credentials.GateIOApiSecret ?? "";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Settings", $"Could not load saved credentials: {ex.Message}");
        }
    }

    private void LoadFromEnvironment()
    {
        // Binance
        var binanceKey = Environment.GetEnvironmentVariable("AUTOTRADEX_BINANCE_API_KEY");
        var binanceSecret = Environment.GetEnvironmentVariable("AUTOTRADEX_BINANCE_API_SECRET");
        if (!string.IsNullOrEmpty(binanceKey)) BinanceApiKey.Text = binanceKey;
        if (!string.IsNullOrEmpty(binanceSecret)) BinanceApiSecret.Password = binanceSecret;

        // KuCoin
        var kucoinKey = Environment.GetEnvironmentVariable("AUTOTRADEX_KUCOIN_API_KEY");
        var kucoinSecret = Environment.GetEnvironmentVariable("AUTOTRADEX_KUCOIN_API_SECRET");
        var kucoinPass = Environment.GetEnvironmentVariable("AUTOTRADEX_KUCOIN_API_KEY_PASSPHRASE");
        if (!string.IsNullOrEmpty(kucoinKey)) KuCoinApiKey.Text = kucoinKey;
        if (!string.IsNullOrEmpty(kucoinSecret)) KuCoinApiSecret.Password = kucoinSecret;
        if (!string.IsNullOrEmpty(kucoinPass)) KuCoinPassphrase.Password = kucoinPass;

        // Bitkub
        var bitkubKey = Environment.GetEnvironmentVariable("AUTOTRADEX_BITKUB_API_KEY");
        var bitkubSecret = Environment.GetEnvironmentVariable("AUTOTRADEX_BITKUB_API_SECRET");
        if (!string.IsNullOrEmpty(bitkubKey)) BitkubApiKey.Text = bitkubKey;
        if (!string.IsNullOrEmpty(bitkubSecret)) BitkubApiSecret.Password = bitkubSecret;

        // OKX
        var okxKey = Environment.GetEnvironmentVariable("AUTOTRADEX_OKX_API_KEY");
        var okxSecret = Environment.GetEnvironmentVariable("AUTOTRADEX_OKX_API_SECRET");
        var okxPass = Environment.GetEnvironmentVariable("AUTOTRADEX_OKX_PASSPHRASE");
        if (!string.IsNullOrEmpty(okxKey)) OKXApiKey.Text = okxKey;
        if (!string.IsNullOrEmpty(okxSecret)) OKXApiSecret.Password = okxSecret;
        if (!string.IsNullOrEmpty(okxPass)) OKXPassphrase.Password = okxPass;

        // Bybit
        var bybitKey = Environment.GetEnvironmentVariable("AUTOTRADEX_BYBIT_API_KEY");
        var bybitSecret = Environment.GetEnvironmentVariable("AUTOTRADEX_BYBIT_API_SECRET");
        if (!string.IsNullOrEmpty(bybitKey)) BybitApiKey.Text = bybitKey;
        if (!string.IsNullOrEmpty(bybitSecret)) BybitApiSecret.Password = bybitSecret;

        // Gate.io
        var gateKey = Environment.GetEnvironmentVariable("AUTOTRADEX_GATEIO_API_KEY");
        var gateSecret = Environment.GetEnvironmentVariable("AUTOTRADEX_GATEIO_API_SECRET");
        if (!string.IsNullOrEmpty(gateKey)) GateIOApiKey.Text = gateKey;
        if (!string.IsNullOrEmpty(gateSecret)) GateIOApiSecret.Password = gateSecret;
    }

    private void SaveCredentialsToFile()
    {
        try
        {
            var credentials = new SavedCredentials
            {
                BinanceApiKey = BinanceApiKey.Text,
                BinanceApiSecret = BinanceApiSecret.Password,
                KuCoinApiKey = KuCoinApiKey.Text,
                KuCoinApiSecret = KuCoinApiSecret.Password,
                KuCoinPassphrase = KuCoinPassphrase.Password,
                BitkubApiKey = BitkubApiKey.Text,
                BitkubApiSecret = BitkubApiSecret.Password,
                OKXApiKey = OKXApiKey.Text,
                OKXApiSecret = OKXApiSecret.Password,
                OKXPassphrase = OKXPassphrase.Password,
                BybitApiKey = BybitApiKey.Text,
                BybitApiSecret = BybitApiSecret.Password,
                GateIOApiKey = GateIOApiKey.Text,
                GateIOApiSecret = GateIOApiSecret.Password
            };

            var directory = Path.GetDirectoryName(_credentialsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(credentials, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_credentialsPath, json);

            // Also set environment variables for current session
            SetEnvironmentVariables(credentials);

            _logger?.LogInfo("Settings", "API credentials saved successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError("Settings", $"Error saving credentials: {ex.Message}");
            throw;
        }
    }

    private void SetEnvironmentVariables(SavedCredentials credentials)
    {
        // Set for current process
        if (!string.IsNullOrEmpty(credentials.BinanceApiKey))
            Environment.SetEnvironmentVariable("AUTOTRADEX_BINANCE_API_KEY", credentials.BinanceApiKey);
        if (!string.IsNullOrEmpty(credentials.BinanceApiSecret))
            Environment.SetEnvironmentVariable("AUTOTRADEX_BINANCE_API_SECRET", credentials.BinanceApiSecret);

        if (!string.IsNullOrEmpty(credentials.KuCoinApiKey))
            Environment.SetEnvironmentVariable("AUTOTRADEX_KUCOIN_API_KEY", credentials.KuCoinApiKey);
        if (!string.IsNullOrEmpty(credentials.KuCoinApiSecret))
            Environment.SetEnvironmentVariable("AUTOTRADEX_KUCOIN_API_SECRET", credentials.KuCoinApiSecret);
        if (!string.IsNullOrEmpty(credentials.KuCoinPassphrase))
            Environment.SetEnvironmentVariable("AUTOTRADEX_KUCOIN_API_KEY_PASSPHRASE", credentials.KuCoinPassphrase);

        if (!string.IsNullOrEmpty(credentials.BitkubApiKey))
            Environment.SetEnvironmentVariable("AUTOTRADEX_BITKUB_API_KEY", credentials.BitkubApiKey);
        if (!string.IsNullOrEmpty(credentials.BitkubApiSecret))
            Environment.SetEnvironmentVariable("AUTOTRADEX_BITKUB_API_SECRET", credentials.BitkubApiSecret);

        if (!string.IsNullOrEmpty(credentials.OKXApiKey))
            Environment.SetEnvironmentVariable("AUTOTRADEX_OKX_API_KEY", credentials.OKXApiKey);
        if (!string.IsNullOrEmpty(credentials.OKXApiSecret))
            Environment.SetEnvironmentVariable("AUTOTRADEX_OKX_API_SECRET", credentials.OKXApiSecret);
        if (!string.IsNullOrEmpty(credentials.OKXPassphrase))
            Environment.SetEnvironmentVariable("AUTOTRADEX_OKX_PASSPHRASE", credentials.OKXPassphrase);

        if (!string.IsNullOrEmpty(credentials.BybitApiKey))
            Environment.SetEnvironmentVariable("AUTOTRADEX_BYBIT_API_KEY", credentials.BybitApiKey);
        if (!string.IsNullOrEmpty(credentials.BybitApiSecret))
            Environment.SetEnvironmentVariable("AUTOTRADEX_BYBIT_API_SECRET", credentials.BybitApiSecret);

        if (!string.IsNullOrEmpty(credentials.GateIOApiKey))
            Environment.SetEnvironmentVariable("AUTOTRADEX_GATEIO_API_KEY", credentials.GateIOApiKey);
        if (!string.IsNullOrEmpty(credentials.GateIOApiSecret))
            Environment.SetEnvironmentVariable("AUTOTRADEX_GATEIO_API_SECRET", credentials.GateIOApiSecret);
    }

    private async void TestBinance_Click(object sender, RoutedEventArgs e)
    {
        await TestExchangeConnection("Binance", BinanceStatus, BinanceApiKey.Text, BinanceApiSecret.Password);
    }

    private async void TestKuCoin_Click(object sender, RoutedEventArgs e)
    {
        await TestExchangeConnection("KuCoin", KuCoinStatus, KuCoinApiKey.Text, KuCoinApiSecret.Password, KuCoinPassphrase.Password);
    }

    private async void TestBitkub_Click(object sender, RoutedEventArgs e)
    {
        await TestExchangeConnection("Bitkub", BitkubStatus, BitkubApiKey.Text, BitkubApiSecret.Password);
    }

    private async void TestOKX_Click(object sender, RoutedEventArgs e)
    {
        await TestExchangeConnection("OKX", OKXStatus, OKXApiKey.Text, OKXApiSecret.Password, OKXPassphrase.Password);
    }

    private async void TestBybit_Click(object sender, RoutedEventArgs e)
    {
        await TestExchangeConnection("Bybit", BybitStatus, BybitApiKey.Text, BybitApiSecret.Password);
    }

    private async void TestGateIO_Click(object sender, RoutedEventArgs e)
    {
        await TestExchangeConnection("Gate.io", GateIOStatus, GateIOApiKey.Text, GateIOApiSecret.Password);
    }

    private async Task TestExchangeConnection(string exchangeName, TextBlock statusBlock, string apiKey, string apiSecret, string? passphrase = null)
    {
        UpdateStatus(statusBlock, "Testing...", "#F59E0B");

        try
        {
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
            {
                UpdateStatus(statusBlock, "Missing credentials", "#EF4444");
                return;
            }

            if ((exchangeName == "KuCoin" || exchangeName == "OKX") && string.IsNullOrEmpty(passphrase))
            {
                UpdateStatus(statusBlock, "Passphrase required", "#EF4444");
                return;
            }

            // Set temporary environment variables for testing
            var keyEnv = $"AUTOTRADEX_{exchangeName.ToUpper().Replace(".", "")}_API_KEY";
            var secretEnv = $"AUTOTRADEX_{exchangeName.ToUpper().Replace(".", "")}_API_SECRET";
            Environment.SetEnvironmentVariable(keyEnv, apiKey);
            Environment.SetEnvironmentVariable(secretEnv, apiSecret);

            if (!string.IsNullOrEmpty(passphrase))
            {
                var passEnv = $"AUTOTRADEX_{exchangeName.ToUpper().Replace(".", "")}_API_KEY_PASSPHRASE";
                if (exchangeName == "OKX") passEnv = "AUTOTRADEX_OKX_PASSPHRASE";
                Environment.SetEnvironmentVariable(passEnv, passphrase);
            }

            // Try to create client and test connection
            if (_exchangeFactory != null)
            {
                try
                {
                    var client = _exchangeFactory.CreateClient(exchangeName);
                    var connected = await client.TestConnectionAsync();

                    if (connected)
                    {
                        UpdateStatus(statusBlock, "Connected", "#10B981");
                        _logger?.LogInfo("Settings", $"{exchangeName} connection test successful");
                    }
                    else
                    {
                        UpdateStatus(statusBlock, "Connection failed", "#EF4444");
                        _logger?.LogWarning("Settings", $"{exchangeName} connection test failed");
                    }
                }
                catch (Exception ex)
                {
                    UpdateStatus(statusBlock, "Error: " + ex.Message.Substring(0, Math.Min(30, ex.Message.Length)), "#EF4444");
                    _logger?.LogError("Settings", $"{exchangeName} test error: {ex.Message}");
                }
            }
            else
            {
                // Fallback: Test by making a simple public API call
                using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var testUrl = exchangeName switch
                {
                    "Binance" => "https://api.binance.com/api/v3/ping",
                    "KuCoin" => "https://api.kucoin.com/api/v1/timestamp",
                    "Bitkub" => "https://api.bitkub.com/api/status",
                    "OKX" => "https://www.okx.com/api/v5/public/time",
                    "Bybit" => "https://api.bybit.com/v5/market/time",
                    "Gate.io" => "https://api.gateio.ws/api/v4/spot/time",
                    _ => throw new Exception("Unknown exchange")
                };

                var response = await httpClient.GetAsync(testUrl);
                if (response.IsSuccessStatusCode)
                {
                    UpdateStatus(statusBlock, "API reachable", "#10B981");
                    _logger?.LogInfo("Settings", $"{exchangeName} API is reachable");
                }
                else
                {
                    UpdateStatus(statusBlock, "API error", "#EF4444");
                }
            }
        }
        catch (Exception ex)
        {
            UpdateStatus(statusBlock, "Test failed", "#EF4444");
            _logger?.LogError("Settings", $"{exchangeName} connection test error: {ex.Message}");
        }
    }

    private void ResetDemo_Click(object sender, RoutedEventArgs e)
    {
        if (decimal.TryParse(DemoBalance.Text, out var balance))
        {
            // Reset demo wallet
            _logger?.LogInfo("Settings", $"Demo wallet reset to ${balance:N2}");
            MessageBox.Show($"Demo wallet has been reset to ${balance:N2}", "Demo Reset",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Please enter a valid balance amount", "Invalid Input",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveStatus.Text = "Saving...";
            SaveStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));

            // Save API credentials
            SaveCredentialsToFile();

            // Save trading settings
            SaveTradingSettings();

            // Save to config
            if (_configService != null)
            {
                await _configService.UpdateConfigAsync(config =>
                {
                    // Update live trading mode (inverse of demo mode)
                    config.General.LiveTrading = !(DemoModeEnabled.IsChecked ?? true);

                    // Update exchange configurations
                    config.ExchangeA.ApiKeyEnvVar = "AUTOTRADEX_BINANCE_API_KEY";
                    config.ExchangeA.ApiSecretEnvVar = "AUTOTRADEX_BINANCE_API_SECRET";
                    config.ExchangeA.Name = "Binance";

                    config.ExchangeB.ApiKeyEnvVar = "AUTOTRADEX_KUCOIN_API_KEY";
                    config.ExchangeB.ApiSecretEnvVar = "AUTOTRADEX_KUCOIN_API_SECRET";
                    config.ExchangeB.Name = "KuCoin";
                });
            }

            SaveStatus.Text = "Settings saved successfully!";
            SaveStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));

            _logger?.LogInfo("Settings", "All settings saved successfully");

            // Clear status after 3 seconds
            await Task.Delay(3000);
            SaveStatus.Text = "";
        }
        catch (Exception ex)
        {
            SaveStatus.Text = $"Error: {ex.Message}";
            SaveStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            _logger?.LogError("Settings", $"Error saving settings: {ex.Message}");
        }
    }

    private void UpdateStatus(TextBlock statusBlock, string text, string color)
    {
        statusBlock.Text = text;
        statusBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    // Slider value changed handlers
    private void MinSpreadSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MinSpreadText != null)
        {
            MinSpreadText.Text = $"{e.NewValue:F2}%";
        }
    }

    private void MaxTradesSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MaxTradesText != null)
        {
            MaxTradesText.Text = ((int)e.NewValue).ToString();
        }
    }

    private void SlippageSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SlippageText != null)
        {
            SlippageText.Text = $"{e.NewValue:F2}%";
        }
    }

    private void DrawdownSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DrawdownText != null)
        {
            DrawdownText.Text = $"{(int)e.NewValue}%";
        }
    }

    private void CooldownSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CooldownText != null)
        {
            CooldownText.Text = $"{(int)e.NewValue}s";
        }
    }

    private void LoadTradingSettings()
    {
        try
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AutoTradeX",
                "trading_settings.json"
            );

            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var settings = JsonSerializer.Deserialize<TradingSettings>(json);

                if (settings != null)
                {
                    MinSpreadSlider.Value = settings.MinSpread;
                    DefaultTradeAmount.Text = settings.DefaultTradeAmount.ToString();
                    MaxTradesSlider.Value = settings.MaxOpenTrades;
                    AutoExecuteEnabled.IsChecked = settings.AutoExecute;
                    SlippageSlider.Value = settings.MaxSlippage;
                    OrderTypeCombo.SelectedIndex = settings.UseMarketOrder ? 0 : 1;

                    DailyLossLimit.Text = settings.DailyLossLimit.ToString();
                    DrawdownSlider.Value = settings.MaxDrawdown;
                    StopLossEnabled.IsChecked = settings.StopLossEnabled;
                    EmergencyStopEnabled.IsChecked = settings.EmergencyStopEnabled;
                    MinBalanceThreshold.Text = settings.MinBalanceThreshold.ToString();
                    CooldownSlider.Value = settings.TradeCooldown;

                    NotifyOnTrade.IsChecked = settings.NotifyOnTrade;
                    NotifyOnOpportunity.IsChecked = settings.NotifyOnOpportunity;
                    NotifyOnError.IsChecked = settings.NotifyOnError;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Settings", $"Could not load trading settings: {ex.Message}");
        }
    }

    private void SaveTradingSettings()
    {
        try
        {
            var settings = new TradingSettings
            {
                MinSpread = MinSpreadSlider.Value,
                DefaultTradeAmount = decimal.TryParse(DefaultTradeAmount.Text, out var amt) ? amt : 100,
                MaxOpenTrades = (int)MaxTradesSlider.Value,
                AutoExecute = AutoExecuteEnabled.IsChecked ?? false,
                MaxSlippage = SlippageSlider.Value,
                UseMarketOrder = OrderTypeCombo.SelectedIndex == 0,

                DailyLossLimit = decimal.TryParse(DailyLossLimit.Text, out var loss) ? loss : 500,
                MaxDrawdown = DrawdownSlider.Value,
                StopLossEnabled = StopLossEnabled.IsChecked ?? true,
                EmergencyStopEnabled = EmergencyStopEnabled.IsChecked ?? true,
                MinBalanceThreshold = decimal.TryParse(MinBalanceThreshold.Text, out var thresh) ? thresh : 100,
                TradeCooldown = (int)CooldownSlider.Value,

                NotifyOnTrade = NotifyOnTrade.IsChecked ?? true,
                NotifyOnOpportunity = NotifyOnOpportunity.IsChecked ?? true,
                NotifyOnError = NotifyOnError.IsChecked ?? true
            };

            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AutoTradeX",
                "trading_settings.json"
            );

            var directory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(settingsPath, json);

            _logger?.LogInfo("Settings", "Trading settings saved successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError("Settings", $"Error saving trading settings: {ex.Message}");
        }
    }
}

// Model for trading settings
public class TradingSettings
{
    public double MinSpread { get; set; } = 0.5;
    public decimal DefaultTradeAmount { get; set; } = 100;
    public int MaxOpenTrades { get; set; } = 3;
    public bool AutoExecute { get; set; } = false;
    public double MaxSlippage { get; set; } = 0.1;
    public bool UseMarketOrder { get; set; } = true;

    public decimal DailyLossLimit { get; set; } = 500;
    public double MaxDrawdown { get; set; } = 10;
    public bool StopLossEnabled { get; set; } = true;
    public bool EmergencyStopEnabled { get; set; } = true;
    public decimal MinBalanceThreshold { get; set; } = 100;
    public int TradeCooldown { get; set; } = 5;

    public bool NotifyOnTrade { get; set; } = true;
    public bool NotifyOnOpportunity { get; set; } = true;
    public bool NotifyOnError { get; set; } = true;
}

// Model for saved credentials
public class SavedCredentials
{
    public string? BinanceApiKey { get; set; }
    public string? BinanceApiSecret { get; set; }
    public string? KuCoinApiKey { get; set; }
    public string? KuCoinApiSecret { get; set; }
    public string? KuCoinPassphrase { get; set; }
    public string? BitkubApiKey { get; set; }
    public string? BitkubApiSecret { get; set; }
    public string? OKXApiKey { get; set; }
    public string? OKXApiSecret { get; set; }
    public string? OKXPassphrase { get; set; }
    public string? BybitApiKey { get; set; }
    public string? BybitApiSecret { get; set; }
    public string? GateIOApiKey { get; set; }
    public string? GateIOApiSecret { get; set; }
}
