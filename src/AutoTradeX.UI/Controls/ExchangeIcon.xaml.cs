using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AutoTradeX.UI.Controls;

public partial class ExchangeIcon : UserControl
{
    // HTTP client for downloading logos (static to reuse connection)
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly Dictionary<string, BitmapImage?> _logoCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _cacheLock = new();

    // Online logo URLs from reliable CDN sources (CoinGecko, CryptoLogos, etc.)
    private static readonly Dictionary<string, string> ExchangeLogoUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        // Using CoinGecko exchange images (reliable and up-to-date)
        { "Binance", "https://assets.coingecko.com/markets/images/52/large/binance.jpg" },
        { "KuCoin", "https://assets.coingecko.com/markets/images/61/large/kucoin.jpg" },
        { "OKX", "https://assets.coingecko.com/markets/images/96/large/WeChat_Image_20220117220452.png" },
        { "Bybit", "https://assets.coingecko.com/markets/images/698/large/bybit_spot.png" },
        { "Gate.io", "https://assets.coingecko.com/markets/images/60/large/gate_io_logo1.jpg" },
        { "Bitkub", "https://assets.coingecko.com/markets/images/391/large/bitkub.jpg" },
        { "Coinbase", "https://assets.coingecko.com/markets/images/23/large/Coinbase_Coin_Primary.png" },
        { "Kraken", "https://assets.coingecko.com/markets/images/29/large/kraken.jpg" },
        { "Huobi", "https://assets.coingecko.com/markets/images/25/large/Huobi_logo.png" },
        { "Bitfinex", "https://assets.coingecko.com/markets/images/4/large/BItfinex.png" }
    };

    // Local file names for exchanges (fallback if online fails)
    private static readonly Dictionary<string, string> ExchangeLocalFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Binance", "binance.png" },
        { "KuCoin", "kucoin.png" },
        { "OKX", "okx.png" },
        { "Bybit", "bybit.png" },
        { "Gate.io", "gateio.png" },
        { "Bitkub", "bitkub.png" }
    };

    // Exchange brand colors
    private static readonly Dictionary<string, (string bg, string fg)> ExchangeColors = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Binance", ("#F3BA2F", "Black") },
        { "KuCoin", ("#23AF91", "White") },
        { "OKX", ("#FFFFFF", "Black") },
        { "Bybit", ("#F7A600", "Black") },
        { "Gate.io", ("#17E6A1", "Black") },
        { "Bitkub", ("#00B14F", "White") },
        { "Coinbase", ("#0052FF", "White") },
        { "Kraken", ("#5741D9", "White") },
        { "Huobi", ("#1C4BA2", "White") },
        { "Bitfinex", ("#16B157", "White") }
    };

    // Exchange abbreviations
    private static readonly Dictionary<string, string> ExchangeAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Binance", "B" },
        { "KuCoin", "K" },
        { "OKX", "OK" },
        { "Bybit", "BY" },
        { "Gate.io", "G" },
        { "Bitkub", "฿" },
        { "Coinbase", "CB" },
        { "Kraken", "Kr" },
        { "Huobi", "H" },
        { "Bitfinex", "Bf" }
    };

    public static readonly DependencyProperty ExchangeNameProperty =
        DependencyProperty.Register(nameof(ExchangeName), typeof(string), typeof(ExchangeIcon),
            new PropertyMetadata(string.Empty, OnExchangeNameChanged));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double), typeof(ExchangeIcon),
            new PropertyMetadata(48.0, OnSizeChanged));

    public string ExchangeName
    {
        get => (string)GetValue(ExchangeNameProperty);
        set => SetValue(ExchangeNameProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public ExchangeIcon()
    {
        InitializeComponent();
    }

    private static void OnExchangeNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ExchangeIcon icon)
        {
            icon.LoadExchangeIcon();
        }
    }

    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ExchangeIcon icon)
        {
            icon.UpdateSize();
        }
    }

    private void UpdateSize()
    {
        IconBorder.Width = Size;
        IconBorder.Height = Size;
        IconBorder.CornerRadius = new CornerRadius(Size * 0.25);
        FallbackText.FontSize = Size * 0.4;
    }

    private async void LoadExchangeIcon()
    {
        UpdateSize();

        var name = ExchangeName ?? "";

        // Set background color
        var (bgColor, fgColor) = ExchangeColors.TryGetValue(name, out var colors)
            ? colors
            : ("#6366F1", "White");

        IconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgColor));
        FallbackText.Foreground = fgColor == "Black" ? Brushes.Black : Brushes.White;

        // Show fallback initially while loading
        ShowFallback(name);

        // Try 1: Check memory cache first
        lock (_cacheLock)
        {
            if (_logoCache.TryGetValue(name, out var cachedBitmap) && cachedBitmap != null)
            {
                IconImage.Source = cachedBitmap;
                IconImage.Visibility = Visibility.Visible;
                FallbackText.Visibility = Visibility.Collapsed;
                return;
            }
        }

        // Try 2: Load from internet (real-time download)
        if (ExchangeLogoUrls.TryGetValue(name, out var url))
        {
            try
            {
                var bitmap = await DownloadLogoAsync(url);
                if (bitmap != null)
                {
                    // Cache the downloaded bitmap
                    lock (_cacheLock)
                    {
                        _logoCache[name] = bitmap;
                    }

                    IconImage.Source = bitmap;
                    IconImage.Visibility = Visibility.Visible;
                    FallbackText.Visibility = Visibility.Collapsed;
                    return;
                }
            }
            catch
            {
                // Internet download failed, try local fallback
            }
        }

        // Try 3: Load from local file (fallback)
        if (ExchangeLocalFiles.TryGetValue(name, out var fileName))
        {
            try
            {
                // Use pack URI for embedded resource (most reliable)
                var packUri = new Uri($"pack://application:,,,/Assets/Exchanges/{fileName}", UriKind.Absolute);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = packUri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                // Cache the local bitmap too
                lock (_cacheLock)
                {
                    _logoCache[name] = bitmap;
                }

                IconImage.Source = bitmap;
                IconImage.Visibility = Visibility.Visible;
                FallbackText.Visibility = Visibility.Collapsed;
                return;
            }
            catch
            {
                // Fallback: try file path
                try
                {
                    var appDir = AppDomain.CurrentDomain.BaseDirectory;
                    var iconPath = Path.Combine(appDir, "Assets", "Exchanges", fileName);

                    if (File.Exists(iconPath))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();

                        // Cache the local bitmap
                        lock (_cacheLock)
                        {
                            _logoCache[name] = bitmap;
                        }

                        IconImage.Source = bitmap;
                        IconImage.Visibility = Visibility.Visible;
                        FallbackText.Visibility = Visibility.Collapsed;
                        return;
                    }
                }
                catch { }
            }
        }

        // All methods failed, keep showing fallback
    }

    private async Task<BitmapImage?> DownloadLogoAsync(string url)
    {
        try
        {
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadAsByteArrayAsync();

            // Create BitmapImage on UI thread
            BitmapImage? bitmap = null;
            await Dispatcher.InvokeAsync(() =>
            {
                bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = new MemoryStream(data);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze(); // Make it thread-safe
            });

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Clear the logo cache to force re-download from internet
    /// </summary>
    public static void ClearLogoCache()
    {
        lock (_cacheLock)
        {
            _logoCache.Clear();
        }
    }

    private void ShowFallback(string name)
    {
        var abbr = ExchangeAbbreviations.TryGetValue(name, out var a) ? a : name.Length > 0 ? name[0].ToString() : "?";
        FallbackText.Text = abbr;
        FallbackText.Visibility = Visibility.Visible;
        IconImage.Visibility = Visibility.Collapsed;
    }
}
