/*
 * ============================================================================
 * AutoTrade-X - Coin Icon Control
 * ============================================================================
 * Displays cryptocurrency icons with fallback support
 * Fetches icons from CoinGecko API and caches them in database
 * ============================================================================
 */

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using AutoTradeX.Infrastructure.Services;

namespace AutoTradeX.UI.Controls;

public partial class CoinIcon : UserControl
{
    // In-memory cache for loaded images (shared across all CoinIcon instances)
    private static readonly Dictionary<string, BitmapImage> ImageCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    // Fallback colors for coins (when icon not available)
    private static readonly Dictionary<string, string> CoinColors = new(StringComparer.OrdinalIgnoreCase)
    {
        { "BTC", "#F7931A" }, { "ETH", "#627EEA" }, { "USDT", "#26A17B" },
        { "BNB", "#F3BA2F" }, { "SOL", "#9945FF" }, { "XRP", "#23292F" },
        { "USDC", "#2775CA" }, { "ADA", "#0033AD" }, { "AVAX", "#E84142" },
        { "DOGE", "#C2A633" }, { "DOT", "#E6007A" }, { "LINK", "#2A5ADA" },
        { "MATIC", "#8247E5" }, { "SHIB", "#FFA409" }, { "LTC", "#345D9D" },
        { "TRX", "#FF0013" }, { "UNI", "#FF007A" }, { "ATOM", "#2E3148" },
        { "XLM", "#14B6E7" }, { "NEAR", "#00C08B" }, { "APT", "#000000" },
        { "ARB", "#12AAFF" }, { "OP", "#FF0420" }, { "AAVE", "#2EBAC6" },
        { "TON", "#0098EA" }, { "PEPE", "#3D9D4E" }, { "SUI", "#4DA2FF" },
        { "SEI", "#9B1B30" }, { "INJ", "#00F2EA" }, { "TIA", "#7B2BF9" },
        { "FET", "#1D2951" }, { "TAO", "#000000" }, { "RENDER", "#D92D64" },
        { "WIF", "#E8B849" }, { "JUP", "#70D87A" }, { "PYTH", "#E8006F" },
        { "STX", "#5546FF" }, { "RUNE", "#33FF99" }, { "FIL", "#0090FF" },
        { "IMX", "#00CFFF" }, { "BCH", "#8DC351" }, { "ETC", "#328332" },
        { "SAND", "#04ADEF" }, { "MANA", "#FF2D55" }, { "CRV", "#FF6B00" },
        { "GMT", "#E8D655" }, { "APE", "#0047FF" }, { "FTM", "#1969FF" },
        { "GRT", "#6747ED" }, { "ALGO", "#000000" }, { "XTZ", "#2C7DF7" },
        { "EOS", "#000000" }, { "THETA", "#2AB8E6" }, { "VET", "#15BDFF" },
        { "HBAR", "#000000" }, { "FLOW", "#00EF8B" }, { "AXS", "#0055D5" },
        { "KCS", "#23AF91" }, { "1INCH", "#94A6C3" }, { "SNX", "#00D1FF" },
        { "THB", "#2D6B42" }
    };

    // Track loading state to prevent duplicate loads
    private bool _isLoading;
    private string? _pendingSymbol;

    public static readonly DependencyProperty SymbolProperty =
        DependencyProperty.Register(nameof(Symbol), typeof(string), typeof(CoinIcon),
            new PropertyMetadata(string.Empty, OnSymbolChanged));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double), typeof(CoinIcon),
            new PropertyMetadata(32.0, OnSizeChanged));

    public string Symbol
    {
        get => (string)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public CoinIcon()
    {
        InitializeComponent();
    }

    private static void OnSymbolChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CoinIcon icon)
        {
            icon.LoadCoinImageAsync();
        }
    }

    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CoinIcon icon)
        {
            icon.UpdateSize();
        }
    }

    private void UpdateSize()
    {
        IconBorder.Width = Size;
        IconBorder.Height = Size;
        IconBorder.CornerRadius = new CornerRadius(Size / 2);
        FallbackText.FontSize = Size * 0.4;
    }

    private async void LoadCoinImageAsync()
    {
        UpdateSize();

        var symbol = NormalizeSymbol(Symbol);
        if (string.IsNullOrEmpty(symbol))
        {
            ShowFallback("?");
            return;
        }

        // If already loading, queue this request
        if (_isLoading)
        {
            _pendingSymbol = symbol;
            return;
        }

        _isLoading = true;
        _pendingSymbol = null;

        try
        {
            // 1. Check in-memory cache first (fastest)
            lock (CacheLock)
            {
                if (ImageCache.TryGetValue(symbol, out var cachedImage))
                {
                    IconImage.ImageSource = cachedImage;
                    FallbackText.Visibility = Visibility.Collapsed;
                    return;
                }
            }

            // 2. Try to load from local Assets folder
            var localImage = TryLoadLocalImage(symbol);
            if (localImage != null)
            {
                lock (CacheLock)
                {
                    ImageCache[symbol] = localImage;
                }
                IconImage.ImageSource = localImage;
                FallbackText.Visibility = Visibility.Collapsed;
                return;
            }

            // 3. Try to fetch from CoinMetadataService (database cache or API)
            var service = App.Services?.GetService<ICoinMetadataService>();
            if (service != null)
            {
                var iconData = await service.GetCoinIconAsync(symbol);
                if (iconData != null && iconData.Length > 0)
                {
                    var bitmap = ByteArrayToBitmapImage(iconData);
                    if (bitmap != null)
                    {
                        lock (CacheLock)
                        {
                            ImageCache[symbol] = bitmap;
                        }

                        // Update UI on dispatcher thread
                        await Dispatcher.InvokeAsync(() =>
                        {
                            IconImage.ImageSource = bitmap;
                            FallbackText.Visibility = Visibility.Collapsed;
                        });
                        return;
                    }
                }
            }

            // 4. Show fallback if all else fails
            ShowFallback(symbol);
        }
        catch
        {
            ShowFallback(symbol);
        }
        finally
        {
            _isLoading = false;

            // Process pending request if any
            if (_pendingSymbol != null)
            {
                LoadCoinImageAsync();
            }
        }
    }

    private static string NormalizeSymbol(string? symbol)
    {
        if (string.IsNullOrEmpty(symbol))
            return "";

        symbol = symbol.ToUpperInvariant();

        // Extract base asset from pair (e.g., "BTC/USDT" -> "BTC")
        if (symbol.Contains('/'))
            symbol = symbol.Split('/')[0];
        else if (symbol.Contains('-'))
            symbol = symbol.Split('-')[0];
        else if (symbol.Contains('_'))
        {
            // Handle Bitkub format: THB_BTC -> BTC
            var parts = symbol.Split('_');
            symbol = parts.Length > 1 ? parts[^1] : parts[0];
        }

        return symbol;
    }

    private static BitmapImage? TryLoadLocalImage(string symbol)
    {
        try
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var iconPath = Path.Combine(appDir, "Assets", "Coins", $"{symbol.ToLowerInvariant()}.png");

            if (File.Exists(iconPath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(iconPath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze(); // Make it thread-safe
                return bitmap;
            }
        }
        catch
        {
            // Ignore errors
        }

        return null;
    }

    private static BitmapImage? ByteArrayToBitmapImage(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = ms;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze(); // Make it thread-safe
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void ShowFallback(string symbol)
    {
        var color = CoinColors.TryGetValue(symbol, out var c) ? c : GenerateColorFromSymbol(symbol);
        IconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        FallbackText.Text = symbol.Length > 0 ? symbol[0].ToString() : "?";
        FallbackText.Visibility = Visibility.Visible;
        IconImage.ImageSource = null;
    }

    /// <summary>
    /// Generate a consistent color based on symbol hash
    /// </summary>
    private static string GenerateColorFromSymbol(string symbol)
    {
        if (string.IsNullOrEmpty(symbol))
            return "#6366F1";

        // Simple hash-based color generation
        var hash = symbol.GetHashCode();
        var hue = Math.Abs(hash % 360);

        // Convert HSL to RGB (simplified, saturation=70%, lightness=50%)
        var h = hue / 60.0;
        var c = 0.6;
        var x = c * (1 - Math.Abs(h % 2 - 1));
        var m = 0.3;

        double r, g, b;
        if (h < 1) { r = c; g = x; b = 0; }
        else if (h < 2) { r = x; g = c; b = 0; }
        else if (h < 3) { r = 0; g = c; b = x; }
        else if (h < 4) { r = 0; g = x; b = c; }
        else if (h < 5) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        var rInt = (int)((r + m) * 255);
        var gInt = (int)((g + m) * 255);
        var bInt = (int)((b + m) * 255);

        return $"#{rInt:X2}{gInt:X2}{bInt:X2}";
    }

    /// <summary>
    /// Pre-cache icons for multiple symbols (call during startup)
    /// </summary>
    public static async Task PreCacheIconsAsync(IEnumerable<string> symbols)
    {
        var service = App.Services?.GetService<ICoinMetadataService>();
        if (service != null)
        {
            await service.PreCacheIconsAsync(symbols);
        }
    }
}
