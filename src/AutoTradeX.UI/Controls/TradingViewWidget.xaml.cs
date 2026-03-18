using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace AutoTradeX.UI.Controls;

/// <summary>
/// Extended trade marker for TradingView widget with TP/SL support
/// </summary>
public class TradingViewMarker
{
    public string Type { get; set; } = "Buy"; // "Buy" or "Sell"
    public decimal Price { get; set; }
    public DateTime Time { get; set; }
    public string? Label { get; set; }
    public decimal? TakeProfit { get; set; }
    public decimal? StopLoss { get; set; }
}

/// <summary>
/// TradingView chart widget using WebView2
/// </summary>
public partial class TradingViewWidget : UserControl
{
    private bool _isInitialized;
    private string _currentSymbol = "BTCUSDT";
    private string _currentExchange = "BINANCE";
    private string _currentInterval = "1";
    private bool _showOverlay = true;

    // Trade markers to display
    private readonly List<TradingViewMarker> _markers = new();

    // Symbol mapping for TradingView
    private static readonly Dictionary<string, string> ExchangeMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Binance", "BINANCE" },
        { "Bitkub", "BITKUB" },
        { "KuCoin", "KUCOIN" },
        { "OKX", "OKX" },
        { "Bybit", "BYBIT" },
        { "Gate.io", "GATEIO" }
    };

    public TradingViewWidget()
    {
        InitializeComponent();
        Loaded += TradingViewWidget_Loaded;
        Unloaded += TradingViewWidget_Unloaded;
    }

    private void TradingViewWidget_Unloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_isInitialized && TradingViewBrowser.CoreWebView2 != null)
            {
                TradingViewBrowser.CoreWebView2.NavigateToString("");
            }
            _markers.Clear();
        }
        catch
        {
            // Ignore disposal errors
        }
    }

    private async void TradingViewWidget_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;

        try
        {
            // Initialize WebView2
            var env = await CoreWebView2Environment.CreateAsync();
            await TradingViewBrowser.EnsureCoreWebView2Async(env);

            // Configure WebView2
            TradingViewBrowser.CoreWebView2.Settings.IsScriptEnabled = true;
            TradingViewBrowser.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            TradingViewBrowser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            TradingViewBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;

            // Load initial chart
            LoadChart(_currentExchange, _currentSymbol, _currentInterval);

            _isInitialized = true;

            // Hide loading overlay when navigation completes
            TradingViewBrowser.NavigationCompleted += (s, args) =>
            {
                Dispatcher.Invoke(() =>
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                });
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TradingView init error: {ex.Message}");
        }
    }

    /// <summary>
    /// Set the chart symbol and exchange
    /// </summary>
    public void SetSymbol(string exchange, string symbol)
    {
        // Convert symbol format: "BTC/THB" -> "BTCTHB", "BTC/USDT" -> "BTCUSDT"
        var tvSymbol = symbol.Replace("/", "").ToUpperInvariant();

        // Get TradingView exchange name
        var tvExchange = ExchangeMapping.TryGetValue(exchange, out var mapped) ? mapped : "BINANCE";

        if (_currentSymbol == tvSymbol && _currentExchange == tvExchange)
            return;

        _currentSymbol = tvSymbol;
        _currentExchange = tvExchange;

        if (_isInitialized)
        {
            LoadChart(tvExchange, tvSymbol, _currentInterval);
        }
    }

    /// <summary>
    /// Set the chart interval
    /// </summary>
    public void SetInterval(string interval)
    {
        // Convert interval: "1m" -> "1", "5m" -> "5", "1h" -> "60", "1d" -> "D"
        var tvInterval = interval switch
        {
            "1m" => "1",
            "5m" => "5",
            "15m" => "15",
            "30m" => "30",
            "1h" => "60",
            "4h" => "240",
            "1d" => "D",
            "1w" => "W",
            _ => "1"
        };

        if (_currentInterval == tvInterval)
            return;

        _currentInterval = tvInterval;

        if (_isInitialized)
        {
            LoadChart(_currentExchange, _currentSymbol, tvInterval);
        }
    }

    private void LoadChart(string exchange, string symbol, string interval)
    {
        if (TradingViewBrowser.CoreWebView2 == null) return;

        // Show loading overlay
        LoadingOverlay.Visibility = Visibility.Visible;

        var fullSymbol = $"{exchange}:{symbol}";
        var html = GenerateTradingViewHtml(fullSymbol, interval);

        TradingViewBrowser.NavigateToString(html);
    }

    private string GenerateTradingViewHtml(string symbol, string interval)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        html, body {{
            width: 100%;
            height: 100%;
            overflow: hidden;
            background: #0A0A1A;
        }}
        .tradingview-widget-container {{
            width: 100%;
            height: 100%;
            position: relative;
        }}
        #tradingview_chart {{
            width: 100%;
            height: 100%;
        }}

        /* Trade Markers Overlay */
        #markers-overlay {{
            position: absolute;
            top: 60px;
            right: 10px;
            z-index: 1000;
            pointer-events: none;
            max-height: calc(100% - 120px);
            overflow-y: auto;
        }}
        .trade-marker {{
            display: flex;
            align-items: center;
            padding: 6px 10px;
            margin-bottom: 4px;
            border-radius: 6px;
            font-size: 11px;
            font-weight: 600;
            font-family: 'Segoe UI', sans-serif;
            backdrop-filter: blur(8px);
            animation: fadeIn 0.3s ease;
            pointer-events: auto;
        }}
        @keyframes fadeIn {{
            from {{ opacity: 0; transform: translateX(20px); }}
            to {{ opacity: 1; transform: translateX(0); }}
        }}
        .marker-buy {{
            background: linear-gradient(135deg, rgba(16, 185, 129, 0.9), rgba(5, 150, 105, 0.9));
            color: white;
            box-shadow: 0 2px 8px rgba(16, 185, 129, 0.4);
        }}
        .marker-sell {{
            background: linear-gradient(135deg, rgba(239, 68, 68, 0.9), rgba(220, 38, 38, 0.9));
            color: white;
            box-shadow: 0 2px 8px rgba(239, 68, 68, 0.4);
        }}
        .marker-icon {{
            font-size: 14px;
            margin-right: 6px;
        }}
        .marker-time {{
            font-size: 9px;
            opacity: 0.8;
            margin-left: 8px;
        }}

        /* Position Lines Info */
        #position-info {{
            position: absolute;
            bottom: 60px;
            right: 10px;
            z-index: 1000;
            display: none;
        }}
        .position-line {{
            display: flex;
            align-items: center;
            padding: 6px 12px;
            margin-bottom: 4px;
            border-radius: 6px;
            font-size: 11px;
            font-weight: 600;
            font-family: 'Segoe UI', sans-serif;
            backdrop-filter: blur(8px);
        }}
        .line-entry {{
            background: rgba(124, 58, 237, 0.9);
            color: white;
        }}
        .line-tp {{
            background: rgba(16, 185, 129, 0.9);
            color: white;
        }}
        .line-sl {{
            background: rgba(239, 68, 68, 0.9);
            color: white;
        }}
    </style>
</head>
<body>
    <div class='tradingview-widget-container'>
        <div id='tradingview_chart'></div>

        <!-- Trade Markers Overlay -->
        <div id='markers-overlay'></div>

        <!-- Position Lines Info -->
        <div id='position-info'>
            <div id='entry-line' class='position-line line-entry'></div>
            <div id='tp-line' class='position-line line-tp'></div>
            <div id='sl-line' class='position-line line-sl'></div>
        </div>
    </div>

    <script type='text/javascript' src='https://s3.tradingview.com/tv.js'></script>
    <script type='text/javascript'>
        // Initialize TradingView widget - Clean minimal chart
        new TradingView.widget({{
            ""autosize"": true,
            ""symbol"": ""{symbol}"",
            ""interval"": ""{interval}"",
            ""timezone"": ""Asia/Bangkok"",
            ""theme"": ""dark"",
            ""style"": ""1"",
            ""locale"": ""th_TH"",
            ""toolbar_bg"": ""#0A0A1A"",
            ""enable_publishing"": false,
            ""hide_top_toolbar"": true,
            ""hide_legend"": true,
            ""hide_side_toolbar"": false,
            ""save_image"": false,
            ""container_id"": ""tradingview_chart"",
            ""hide_volume"": true,
            ""studies"": [],
            ""show_popup_button"": false,
            ""withdateranges"": false,
            ""allow_symbol_change"": false,
            ""details"": false,
            ""hotlist"": false,
            ""calendar"": false,
            ""news"": [],
            ""overrides"": {{
                ""paneProperties.background"": ""#0A0A1A"",
                ""paneProperties.backgroundType"": ""solid"",
                ""scalesProperties.backgroundColor"": ""#0A0A1A"",
                ""mainSeriesProperties.candleStyle.upColor"": ""#10B981"",
                ""mainSeriesProperties.candleStyle.downColor"": ""#EF4444"",
                ""mainSeriesProperties.candleStyle.borderUpColor"": ""#10B981"",
                ""mainSeriesProperties.candleStyle.borderDownColor"": ""#EF4444"",
                ""mainSeriesProperties.candleStyle.wickUpColor"": ""#10B981"",
                ""mainSeriesProperties.candleStyle.wickDownColor"": ""#EF4444""
            }}
        }});

        // Trade markers management
        function updateTradeMarkers(markers) {{
            var overlay = document.getElementById('markers-overlay');
            overlay.innerHTML = '';

            // Show only last 10 markers
            var recentMarkers = markers.slice(-10);

            for (var i = 0; i < recentMarkers.length; i++) {{
                var marker = recentMarkers[i];
                var div = document.createElement('div');
                div.className = 'trade-marker marker-' + marker.type.toLowerCase();

                var icon = marker.type === 'Buy' ? '▲' : '▼';
                var time = new Date(marker.time).toLocaleTimeString('th-TH', {{ hour: '2-digit', minute: '2-digit' }});

                div.innerHTML = '<span class=marker-icon>' + icon + '</span>' +
                    '<span>' + marker.label + '</span>' +
                    '<span class=marker-time>' + time + '</span>';
                overlay.appendChild(div);
            }}
        }}

        // Position lines management
        function updatePositionLines(tp, sl, entry) {{
            var posInfo = document.getElementById('position-info');
            var entryLine = document.getElementById('entry-line');
            var tpLine = document.getElementById('tp-line');
            var slLine = document.getElementById('sl-line');

            posInfo.style.display = 'block';

            entryLine.innerHTML = '<span style=margin-right:6px>📍</span> Entry: $' + entry.toFixed(2);

            if (tp) {{
                tpLine.style.display = 'flex';
                tpLine.innerHTML = '<span style=margin-right:6px>🎯</span> TP: $' + tp.toFixed(2);
            }} else {{
                tpLine.style.display = 'none';
            }}

            if (sl) {{
                slLine.style.display = 'flex';
                slLine.innerHTML = '<span style=margin-right:6px>🛑</span> SL: $' + sl.toFixed(2);
            }} else {{
                slLine.style.display = 'none';
            }}
        }}

        function clearPositionLines() {{
            document.getElementById('position-info').style.display = 'none';
        }}
    </script>
</body>
</html>";
    }

    /// <summary>
    /// Refresh the chart
    /// </summary>
    public void Refresh()
    {
        if (_isInitialized)
        {
            LoadChart(_currentExchange, _currentSymbol, _currentInterval);
        }
    }

    /// <summary>
    /// Show or hide the AI markers overlay
    /// </summary>
    public void SetOverlayVisible(bool visible)
    {
        _showOverlay = visible;
        if (_isInitialized && TradingViewBrowser.CoreWebView2 != null)
        {
            var display = visible ? "block" : "none";
            var script = $"document.getElementById('markers-overlay').style.display = '{display}'; document.getElementById('position-info').style.display = '{(visible ? "block" : "none")}';";
            _ = TradingViewBrowser.CoreWebView2.ExecuteScriptAsync(script);
        }
    }

    /// <summary>
    /// Check if overlay is visible
    /// </summary>
    public bool IsOverlayVisible => _showOverlay;

    /// <summary>
    /// Add a buy marker to the chart
    /// </summary>
    public void AddBuyMarker(decimal price, DateTime time, string? label = null, decimal? takeProfit = null, decimal? stopLoss = null)
    {
        var marker = new TradingViewMarker
        {
            Type = "Buy",
            Price = price,
            Time = time,
            Label = label ?? $"BUY @ {price:N2}",
            TakeProfit = takeProfit,
            StopLoss = stopLoss
        };
        _markers.Add(marker);
        UpdateMarkersOnChart();
    }

    /// <summary>
    /// Add a sell marker to the chart
    /// </summary>
    public void AddSellMarker(decimal price, DateTime time, string? label = null)
    {
        var marker = new TradingViewMarker
        {
            Type = "Sell",
            Price = price,
            Time = time,
            Label = label ?? $"SELL @ {price:N2}"
        };
        _markers.Add(marker);
        UpdateMarkersOnChart();
    }

    /// <summary>
    /// Clear all markers from the chart
    /// </summary>
    public void ClearMarkers()
    {
        _markers.Clear();
        UpdateMarkersOnChart();
    }

    /// <summary>
    /// Update TP/SL lines for current position
    /// </summary>
    public void UpdatePositionLines(decimal? takeProfit, decimal? stopLoss, decimal entryPrice)
    {
        if (_isInitialized && TradingViewBrowser.CoreWebView2 != null)
        {
            var script = $@"
                if (typeof updatePositionLines === 'function') {{
                    updatePositionLines({(takeProfit?.ToString() ?? "null")}, {(stopLoss?.ToString() ?? "null")}, {entryPrice});
                }}
            ";
            _ = TradingViewBrowser.CoreWebView2.ExecuteScriptAsync(script);
        }
    }

    /// <summary>
    /// Clear position lines
    /// </summary>
    public void ClearPositionLines()
    {
        if (_isInitialized && TradingViewBrowser.CoreWebView2 != null)
        {
            var script = "if (typeof clearPositionLines === 'function') { clearPositionLines(); }";
            _ = TradingViewBrowser.CoreWebView2.ExecuteScriptAsync(script);
        }
    }

    private void UpdateMarkersOnChart()
    {
        if (!_isInitialized || TradingViewBrowser.CoreWebView2 == null) return;

        var markersJson = JsonSerializer.Serialize(_markers.Select(m => new
        {
            type = m.Type,
            price = m.Price,
            time = new DateTimeOffset(m.Time).ToUnixTimeMilliseconds(),
            label = m.Label,
            tp = m.TakeProfit,
            sl = m.StopLoss
        }));

        var script = $@"
            if (typeof updateTradeMarkers === 'function') {{
                updateTradeMarkers({markersJson});
            }}
        ";
        _ = TradingViewBrowser.CoreWebView2.ExecuteScriptAsync(script);
    }
}
