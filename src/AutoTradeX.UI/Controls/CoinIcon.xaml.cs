using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AutoTradeX.UI.Controls;

public partial class CoinIcon : UserControl
{
    // Local coin image files (stored in Assets/Coins/)
    private static readonly HashSet<string> AvailableCoins = new(StringComparer.OrdinalIgnoreCase)
    {
        "BTC", "ETH", "USDT", "BNB", "SOL", "XRP", "USDC", "ADA", "AVAX", "DOGE",
        "DOT", "LINK", "MATIC", "SHIB", "LTC", "TRX", "UNI", "ATOM", "XLM", "NEAR",
        "APT", "ARB", "OP", "AAVE", "TON"
    };

    // Fallback colors for coins
    private static readonly Dictionary<string, string> CoinColors = new(StringComparer.OrdinalIgnoreCase)
    {
        { "BTC", "#F7931A" },
        { "ETH", "#627EEA" },
        { "USDT", "#26A17B" },
        { "BNB", "#F3BA2F" },
        { "SOL", "#9945FF" },
        { "XRP", "#23292F" },
        { "USDC", "#2775CA" },
        { "ADA", "#0033AD" },
        { "AVAX", "#E84142" },
        { "DOGE", "#C2A633" },
        { "DOT", "#E6007A" },
        { "LINK", "#2A5ADA" },
        { "MATIC", "#8247E5" },
        { "SHIB", "#FFA409" },
        { "LTC", "#345D9D" },
        { "TRX", "#FF0013" },
        { "UNI", "#FF007A" },
        { "ATOM", "#2E3148" },
        { "XLM", "#000000" },
        { "NEAR", "#000000" },
        { "APT", "#000000" },
        { "ARB", "#12AAFF" },
        { "OP", "#FF0420" },
        { "AAVE", "#2EBAC6" },
        { "TON", "#0098EA" }
    };

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
            icon.LoadCoinImage();
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

    private void LoadCoinImage()
    {
        UpdateSize();

        var symbol = Symbol?.ToUpperInvariant() ?? "";

        // Extract symbol from pair (e.g., "BTC/USDT" -> "BTC")
        if (symbol.Contains('/'))
        {
            symbol = symbol.Split('/')[0];
        }

        if (AvailableCoins.Contains(symbol))
        {
            try
            {
                // Get path relative to executable
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var iconPath = Path.Combine(appDir, "Assets", "Coins", $"{symbol.ToLowerInvariant()}.png");

                if (File.Exists(iconPath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(iconPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();

                    IconImage.ImageSource = bitmap;
                    FallbackText.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ShowFallback(symbol);
                }
            }
            catch
            {
                ShowFallback(symbol);
            }
        }
        else
        {
            ShowFallback(symbol);
        }
    }

    private void ShowFallback(string symbol)
    {
        var color = CoinColors.TryGetValue(symbol, out var c) ? c : "#6366F1";
        IconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        FallbackText.Text = symbol.Length > 0 ? symbol[0].ToString() : "?";
        FallbackText.Visibility = Visibility.Visible;
        IconImage.ImageSource = null;
    }
}
