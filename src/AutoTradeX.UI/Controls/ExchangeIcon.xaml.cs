using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AutoTradeX.UI.Controls;

public partial class ExchangeIcon : UserControl
{
    // Local file names for exchanges (stored in Assets/Exchanges/)
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

    private void LoadExchangeIcon()
    {
        UpdateSize();

        var name = ExchangeName ?? "";

        // Set background color
        var (bgColor, fgColor) = ExchangeColors.TryGetValue(name, out var colors)
            ? colors
            : ("#6366F1", "White");

        IconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgColor));
        FallbackText.Foreground = fgColor == "Black" ? Brushes.Black : Brushes.White;

        // Try to load logo image
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

                        IconImage.Source = bitmap;
                        IconImage.Visibility = Visibility.Visible;
                        FallbackText.Visibility = Visibility.Collapsed;
                        return;
                    }
                }
                catch { }
            }
        }

        // Show fallback if all methods fail
        ShowFallback(name);
    }

    private void ShowFallback(string name)
    {
        var abbr = ExchangeAbbreviations.TryGetValue(name, out var a) ? a : name.Length > 0 ? name[0].ToString() : "?";
        FallbackText.Text = abbr;
        FallbackText.Visibility = Visibility.Visible;
        IconImage.Visibility = Visibility.Collapsed;
    }
}
