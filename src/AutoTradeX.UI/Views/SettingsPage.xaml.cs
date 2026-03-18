using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;
using AutoTradeX.Infrastructure.Services;

namespace AutoTradeX.UI.Views;

public partial class SettingsPage : UserControl
{
    private IConfigService? _configService;
    private ILoggingService? _logger;
    private IExchangeClientFactory? _exchangeFactory;
    private IConnectionStatusService? _connectionStatusService;
    private IApiCredentialsService? _apiCredentialsService;

    // Legacy path for migration only (will be removed in future versions)
    private readonly string _legacyCredentialsPath;

    public SettingsPage()
    {
        InitializeComponent();
        _legacyCredentialsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AutoTradeX",
            "credentials.encrypted.json"
        );

        Loaded += SettingsPage_Loaded;
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _configService = App.Services?.GetService<IConfigService>();
            _logger = App.Services?.GetService<ILoggingService>();
            _exchangeFactory = App.Services?.GetService<IExchangeClientFactory>();
            _connectionStatusService = App.Services?.GetService<IConnectionStatusService>();
            _apiCredentialsService = App.Services?.GetService<IApiCredentialsService>();

            LoadSettings();
            await LoadSavedCredentialsAsync();
            LoadTradingSettings();
        }
        catch (Exception ex)
        {
            _logger?.LogError("Settings", $"Error loading settings: {ex.Message}");
        }
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

    private async Task LoadSavedCredentialsAsync()
    {
        try
        {
            // Primary: Load from database (encrypted storage)
            if (_apiCredentialsService != null)
            {
                await LoadCredentialsFromDatabaseAsync();
            }

            // Fallback: Load from legacy JSON file (for migration to database)
            await LoadFromLegacyFileAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Settings", $"Could not load saved credentials: {ex.Message}");
        }
    }

    private async Task LoadCredentialsFromDatabaseAsync()
    {
        if (_apiCredentialsService == null) return;

        // Binance
        var binance = await _apiCredentialsService.GetCredentialsAsync("Binance");
        if (binance != null && string.IsNullOrEmpty(BinanceApiKey.Text))
        {
            BinanceApiKey.Text = binance.ApiKey;
            BinanceApiSecret.Password = binance.ApiSecret;
        }

        // KuCoin
        var kucoin = await _apiCredentialsService.GetCredentialsAsync("KuCoin");
        if (kucoin != null && string.IsNullOrEmpty(KuCoinApiKey.Text))
        {
            KuCoinApiKey.Text = kucoin.ApiKey;
            KuCoinApiSecret.Password = kucoin.ApiSecret;
            KuCoinPassphrase.Password = kucoin.Passphrase ?? "";
        }

        // Bitkub
        var bitkub = await _apiCredentialsService.GetCredentialsAsync("Bitkub");
        if (bitkub != null && string.IsNullOrEmpty(BitkubApiKey.Text))
        {
            BitkubApiKey.Text = bitkub.ApiKey;
            BitkubApiSecret.Password = bitkub.ApiSecret;
        }

        // OKX
        var okx = await _apiCredentialsService.GetCredentialsAsync("OKX");
        if (okx != null && string.IsNullOrEmpty(OKXApiKey.Text))
        {
            OKXApiKey.Text = okx.ApiKey;
            OKXApiSecret.Password = okx.ApiSecret;
            OKXPassphrase.Password = okx.Passphrase ?? "";
        }

        // Bybit
        var bybit = await _apiCredentialsService.GetCredentialsAsync("Bybit");
        if (bybit != null && string.IsNullOrEmpty(BybitApiKey.Text))
        {
            BybitApiKey.Text = bybit.ApiKey;
            BybitApiSecret.Password = bybit.ApiSecret;
        }

        // Gate.io
        var gateio = await _apiCredentialsService.GetCredentialsAsync("Gate.io");
        if (gateio != null && string.IsNullOrEmpty(GateIOApiKey.Text))
        {
            GateIOApiKey.Text = gateio.ApiKey;
            GateIOApiSecret.Password = gateio.ApiSecret;
        }
    }

    private async Task LoadFromLegacyFileAsync()
    {
        try
        {
            if (!File.Exists(_legacyCredentialsPath)) return;

            var json = await File.ReadAllTextAsync(_legacyCredentialsPath);
            var credentials = JsonSerializer.Deserialize<SavedCredentials>(json);

            if (credentials == null) return;

            // Only load if not already set
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

            // Migrate legacy credentials to database
            if (_apiCredentialsService != null)
            {
                _logger?.LogInfo("Settings", "Migrating legacy credentials to database...");
                await MigrateCredentialsToDatabaseAsync(credentials);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Settings", $"Could not load legacy credentials: {ex.Message}");
        }
    }

    private async Task MigrateCredentialsToDatabaseAsync(SavedCredentials credentials)
    {
        if (_apiCredentialsService == null) return;

        if (!string.IsNullOrEmpty(credentials.BinanceApiKey))
            await _apiCredentialsService.SaveCredentialsAsync("Binance", credentials.BinanceApiKey, credentials.BinanceApiSecret ?? "");

        if (!string.IsNullOrEmpty(credentials.KuCoinApiKey))
            await _apiCredentialsService.SaveCredentialsAsync("KuCoin", credentials.KuCoinApiKey, credentials.KuCoinApiSecret ?? "", credentials.KuCoinPassphrase);

        if (!string.IsNullOrEmpty(credentials.BitkubApiKey))
            await _apiCredentialsService.SaveCredentialsAsync("Bitkub", credentials.BitkubApiKey, credentials.BitkubApiSecret ?? "");

        if (!string.IsNullOrEmpty(credentials.OKXApiKey))
            await _apiCredentialsService.SaveCredentialsAsync("OKX", credentials.OKXApiKey, credentials.OKXApiSecret ?? "", credentials.OKXPassphrase);

        if (!string.IsNullOrEmpty(credentials.BybitApiKey))
            await _apiCredentialsService.SaveCredentialsAsync("Bybit", credentials.BybitApiKey, credentials.BybitApiSecret ?? "");

        if (!string.IsNullOrEmpty(credentials.GateIOApiKey))
            await _apiCredentialsService.SaveCredentialsAsync("Gate.io", credentials.GateIOApiKey, credentials.GateIOApiSecret ?? "");

        _logger?.LogInfo("Settings", "Legacy credentials migrated to database");
    }

    private async void SaveCredentialsToDatabase()
    {
        try
        {
            if (_apiCredentialsService == null)
            {
                MessageBox.Show("ระบบยังไม่พร้อม กรุณาลองใหม่", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _logger?.LogInfo("Settings", "Saving credentials to database...");

            // Save credentials and invalidate connection cache for each exchange
            var exchangesToSave = new (string name, string key, string secret, string? passphrase)[]
            {
                ("Binance", BinanceApiKey.Text, BinanceApiSecret.Password, null),
                ("KuCoin", KuCoinApiKey.Text, KuCoinApiSecret.Password, KuCoinPassphrase.Password),
                ("Bitkub", BitkubApiKey.Text, BitkubApiSecret.Password, null),
                ("OKX", OKXApiKey.Text, OKXApiSecret.Password, OKXPassphrase.Password),
                ("Bybit", BybitApiKey.Text, BybitApiSecret.Password, null),
                ("Gate.io", GateIOApiKey.Text, GateIOApiSecret.Password, null)
            };

            foreach (var (name, key, secret, passphrase) in exchangesToSave)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    await _apiCredentialsService.SaveCredentialsAsync(name, key, secret, passphrase);
                    // Invalidate cached connection status so it will be re-tested
                    _connectionStatusService?.ClearVerifiedStatus(name);
                }
            }

            _logger?.LogInfo("Settings", "API credentials saved to database successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError("Settings", $"Error saving credentials: {ex.Message}");
            MessageBox.Show($"ไม่สามารถบันทึก credentials:\n{ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
        _logger?.LogInfo("Settings", $"Testing connection for: {exchangeName}");

        UpdateStatus(statusBlock, "Testing...", "#F59E0B");

        // Hide permissions panel initially
        GetPermissionsPanel(exchangeName)?.SetValue(VisibilityProperty, Visibility.Collapsed);

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

            // Set environment variables for the credentials
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

            if (_exchangeFactory != null)
            {
                try
                {
                    var client = _exchangeFactory.CreateRealClient(exchangeName);
                    var connected = await client.TestConnectionAsync();

                    if (connected)
                    {
                        UpdateStatus(statusBlock, "Connected ✓", "#10B981");
                        _logger?.LogInfo("Settings", $"{exchangeName} connection test successful");

                        // Mark exchange as verified so other pages will remember this
                        // บันทึกสถานะ verified ให้หน้าอื่นใช้ได้
                        if (_connectionStatusService != null)
                        {
                            _logger?.LogInfo("Settings", $"Calling MarkExchangeAsVerified for {exchangeName}...");
                            _connectionStatusService.MarkExchangeAsVerified(exchangeName);
                            _logger?.LogInfo("Settings", $"MarkExchangeAsVerified completed for {exchangeName}");
                        }
                        else
                        {
                            _logger?.LogError("Settings", $"_connectionStatusService is NULL! Cannot mark {exchangeName} as verified");
                        }

                        // Fetch and display API permissions
                        await FetchAndDisplayPermissions(exchangeName, client);
                    }
                    else
                    {
                        UpdateStatus(statusBlock, "Connection failed ✗", "#EF4444");
                        _logger?.LogWarning("Settings", $"{exchangeName} connection test failed");
                    }
                }
                catch (Exception ex)
                {
                    // Show short error in status, full error in log and popup
                    var shortError = ex.Message.Length > 50
                        ? ex.Message.Substring(0, 50) + "..."
                        : ex.Message;
                    UpdateStatus(statusBlock, $"Error: {shortError}", "#EF4444");
                    _logger?.LogError("Settings", $"{exchangeName} test error: {ex.Message}");

                    // Show full error in message box for user to see
                    MessageBox.Show(
                        $"การเชื่อมต่อ {exchangeName} ล้มเหลว:\n\n{ex.Message}\n\nกรุณาตรวจสอบ:\n• API Key และ Secret ถูกต้อง\n• IP ของคุณอยู่ใน whitelist (ถ้า exchange ต้องการ)\n• API มีสิทธิ์เข้าถึง wallet/balance",
                        $"{exchangeName} Connection Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            else
            {
                // Fallback: Test by making a simple public API call
                _logger?.LogWarning("Settings", $"ExchangeFactory is NULL! Using fallback HTTP test for {exchangeName}");
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

    /// <summary>
    /// Fetch API permissions from exchange and display them
    /// </summary>
    private async Task FetchAndDisplayPermissions(string exchangeName, Core.Interfaces.IExchangeClient client)
    {
        try
        {
            var permissionInfo = await client.GetApiPermissionsAsync();

            var permissions = new ApiPermissions
            {
                CanRead = permissionInfo.CanRead,
                CanTrade = permissionInfo.CanTrade,
                CanWithdraw = permissionInfo.CanWithdraw,
                CanDeposit = permissionInfo.CanDeposit,
                IpRestriction = permissionInfo.IpRestriction
            };

            var permissionsPanel = GetPermissionsPanel(exchangeName);
            var tagsPanel = GetPermissionTagsPanel(exchangeName);

            if (permissionsPanel != null && tagsPanel != null)
            {
                DisplayApiPermissions(exchangeName, permissionsPanel, tagsPanel, permissions);
            }

            _logger?.LogInfo("Settings", $"{exchangeName} permissions: Read={permissions.CanRead}, Trade={permissions.CanTrade}, Withdraw={permissions.CanWithdraw}");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Settings", $"Failed to fetch {exchangeName} permissions: {ex.Message}");
        }
    }

    /// <summary>
    /// Get the permissions panel Border for an exchange
    /// </summary>
    private Border? GetPermissionsPanel(string exchangeName)
    {
        return exchangeName switch
        {
            "Binance" => BinancePermissions,
            "KuCoin" => KuCoinPermissions,
            "Bitkub" => BitkubPermissions,
            "OKX" => OKXPermissions,
            "Bybit" => BybitPermissions,
            "Gate.io" => GateIOPermissions,
            _ => null
        };
    }

    /// <summary>
    /// Get the permission tags WrapPanel for an exchange
    /// </summary>
    private WrapPanel? GetPermissionTagsPanel(string exchangeName)
    {
        return exchangeName switch
        {
            "Binance" => BinancePermissionTags,
            "KuCoin" => KuCoinPermissionTags,
            "Bitkub" => BitkubPermissionTags,
            "OKX" => OKXPermissionTags,
            "Bybit" => BybitPermissionTags,
            "Gate.io" => GateIOPermissionTags,
            _ => null
        };
    }

    private async void ResetDemo_Click(object sender, RoutedEventArgs e)
    {
        if (decimal.TryParse(DemoBalance.Text, out var balance))
        {
            var result = MessageBox.Show(
                $"Reset demo wallet to ${balance:N2}?\n\nThis will clear all demo trading history and positions.",
                "Confirm Reset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var demoWallet = App.Services?.GetService<AutoTradeX.Infrastructure.Services.DemoWalletService>();
                    if (demoWallet != null)
                    {
                        await demoWallet.ResetWalletAsync(balance);
                        _logger?.LogInfo("Settings", $"Demo wallet reset to ${balance:N2}");
                        MessageBox.Show($"Demo wallet has been reset to ${balance:N2}\n\nAll positions and history cleared.", "Demo Reset",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        _logger?.LogInfo("Settings", $"Demo wallet reset to ${balance:N2} (service not available)");
                        MessageBox.Show($"Demo wallet reset to ${balance:N2}", "Demo Reset",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError("Settings", $"Failed to reset demo wallet: {ex.Message}");
                    MessageBox.Show($"Failed to reset demo wallet: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
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

            // Save API credentials to database
            SaveCredentialsToDatabase();

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

    #region API Documentation Links

    // API Documentation URLs for each exchange
    private static readonly Dictionary<string, string> ApiDocUrls = new()
    {
        ["Binance"] = "https://www.binance.com/th/support/faq/วิธีสร้าง-api-key-360002502072",
        ["KuCoin"] = "https://www.kucoin.com/support/900004829406-วิธีสร้าง-api",
        ["Bitkub"] = "https://api.bitkub.com/",
        ["OKX"] = "https://www.okx.com/help/how-to-create-api-keys",
        ["Bybit"] = "https://www.bybit.com/th-TH/help-center/article/How-to-create-API-key",
        ["Gate.io"] = "https://www.gate.io/help/guide/guide/17521/how-to-create-an-api-key"
    };

    private void OpenBinanceApiDocs_Click(object sender, MouseButtonEventArgs e)
    {
        OpenUrl(ApiDocUrls["Binance"]);
    }

    private void OpenKuCoinApiDocs_Click(object sender, MouseButtonEventArgs e)
    {
        OpenUrl(ApiDocUrls["KuCoin"]);
    }

    private void OpenBitkubApiDocs_Click(object sender, MouseButtonEventArgs e)
    {
        OpenUrl(ApiDocUrls["Bitkub"]);
    }

    private void OpenOKXApiDocs_Click(object sender, MouseButtonEventArgs e)
    {
        OpenUrl(ApiDocUrls["OKX"]);
    }

    private void OpenBybitApiDocs_Click(object sender, MouseButtonEventArgs e)
    {
        OpenUrl(ApiDocUrls["Bybit"]);
    }

    private void OpenGateIOApiDocs_Click(object sender, MouseButtonEventArgs e)
    {
        OpenUrl(ApiDocUrls["Gate.io"]);
    }

    private void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            _logger?.LogInfo("Settings", $"Opened URL: {url}");
        }
        catch (Exception ex)
        {
            _logger?.LogError("Settings", $"Failed to open URL: {ex.Message}");
            MessageBox.Show($"ไม่สามารถเปิดลิงก์ได้: {url}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    #endregion

    #region API Permissions Display

    /// <summary>
    /// Display API permissions after successful connection test
    /// </summary>
    private void DisplayApiPermissions(string exchangeName, Border permissionsPanel, WrapPanel tagsPanel, ApiPermissions permissions)
    {
        Dispatcher.Invoke(() =>
        {
            tagsPanel.Children.Clear();

            // Add permission tags
            AddPermissionTag(tagsPanel, "📖 Read", permissions.CanRead, "#3B82F6");
            AddPermissionTag(tagsPanel, "💰 Trade", permissions.CanTrade, "#10B981");
            AddPermissionTag(tagsPanel, "💸 Withdraw", permissions.CanWithdraw, "#EF4444");
            AddPermissionTag(tagsPanel, "📥 Deposit", permissions.CanDeposit, "#F59E0B");

            // Add IP restriction info if available
            if (!string.IsNullOrEmpty(permissions.IpRestriction))
            {
                AddInfoTag(tagsPanel, $"🌐 IP: {permissions.IpRestriction}", "#6B7280");
            }

            permissionsPanel.Visibility = Visibility.Visible;
        });
    }

    private void AddPermissionTag(WrapPanel panel, string text, bool enabled, string color)
    {
        var tag = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(enabled ? $"#20{color.Substring(1)}" : "#15FFFFFF")),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 6)
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            Text = enabled ? "✓" : "✗",
            FontSize = 10,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(enabled ? color : "#6B7280")),
            Margin = new Thickness(0, 0, 4, 0)
        });
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(enabled ? color : "#6B7280"))
        });

        tag.Child = content;
        panel.Children.Add(tag);
    }

    private void AddInfoTag(WrapPanel panel, string text, string color)
    {
        var tag = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10FFFFFF")),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 6)
        };

        tag.Child = new TextBlock
        {
            Text = text,
            FontSize = 10,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
        };

        panel.Children.Add(tag);
    }

    /// <summary>
    /// API permissions data class
    /// </summary>
    public class ApiPermissions
    {
        public bool CanRead { get; set; }
        public bool CanTrade { get; set; }
        public bool CanWithdraw { get; set; }
        public bool CanDeposit { get; set; }
        public string? IpRestriction { get; set; }
    }

    #endregion

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
