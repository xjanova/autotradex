/*
 * ============================================================================
 * AutoTrade-X - TradingView-Style Chart Control
 * ============================================================================
 * Smooth candlestick chart with AI trade markers
 * - No flickering on updates
 * - Real-time price streaming
 * - Buy/Sell markers
 * - Pending order indicators
 * ============================================================================
 */

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoTradeX.Core.Models;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using SkiaSharp;

namespace AutoTradeX.UI.Controls;

/// <summary>
/// Trade marker for chart display
/// </summary>
public class TradeMarker
{
    public DateTime Time { get; set; }
    public decimal Price { get; set; }
    public string Type { get; set; } = "Buy"; // Buy, Sell, PendingBuy, PendingSell
    public string Label { get; set; } = "";
    public decimal? Amount { get; set; }
}

public partial class TradingViewChart : UserControl
{
    // Observable collection for smooth updates (no flickering)
    private readonly ObservableCollection<FinancialPoint> _candleData = new();
    private readonly ObservableCollection<ObservablePoint> _priceLineData = new(); // For line chart fallback
    private readonly ObservableCollection<ObservablePoint> _buyMarkers = new();
    private readonly ObservableCollection<ObservablePoint> _sellMarkers = new();
    private readonly ObservableCollection<ObservablePoint> _pendingMarkers = new();

    // Series references (keep references to avoid recreation)
    private CandlesticksSeries<FinancialPoint>? _candlestickSeries;
    private LineSeries<ObservablePoint>? _priceLineSeries; // Fallback line chart
    private ScatterSeries<ObservablePoint>? _buyScatterSeries;
    private ScatterSeries<ObservablePoint>? _sellScatterSeries;
    private ScatterSeries<ObservablePoint>? _pendingScatterSeries;
    private LineSeries<ObservablePoint>? _currentPriceLine;

    // Current price line data
    private readonly ObservableCollection<ObservablePoint> _currentPriceData = new();

    // Track last candle time for streaming updates
    private DateTime _lastCandleTime = DateTime.MinValue;
    private decimal _currentPrice;

    // Colors
    private static readonly SKColor BullishColor = new(16, 185, 129); // #10B981 Green
    private static readonly SKColor BearishColor = new(239, 68, 68);  // #EF4444 Red
    private static readonly SKColor PendingColor = new(245, 158, 11); // #F59E0B Amber
    private static readonly SKColor PriceLineColor = new(124, 58, 237); // #7C3AED Purple
    private static readonly SKColor GridColor = new(255, 255, 255, 20);
    private static readonly SKColor LabelColor = new(255, 255, 255, 150);

    public TradingViewChart()
    {
        InitializeComponent();
        SetupChart();
    }

    private void SetupChart()
    {
        // Create candlestick series with smooth animations
        _candlestickSeries = new CandlesticksSeries<FinancialPoint>
        {
            Values = _candleData,
            UpFill = new SolidColorPaint(BullishColor.WithAlpha(200)),
            UpStroke = new SolidColorPaint(BullishColor) { StrokeThickness = 2 },
            DownFill = new SolidColorPaint(BearishColor.WithAlpha(200)),
            DownStroke = new SolidColorPaint(BearishColor) { StrokeThickness = 2 },
            MaxBarWidth = 8
        };

        // Price line series (shows close prices as line - more reliable rendering)
        _priceLineSeries = new LineSeries<ObservablePoint>
        {
            Values = _priceLineData,
            Fill = new LinearGradientPaint(
                new[] { new SKColor(124, 58, 237, 80), SKColors.Transparent },
                new SKPoint(0.5f, 0),
                new SKPoint(0.5f, 1)),
            Stroke = new SolidColorPaint(new SKColor(124, 58, 237)) { StrokeThickness = 2 },
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 0.3
        };

        // Buy markers (green circles)
        _buyScatterSeries = new ScatterSeries<ObservablePoint>
        {
            Values = _buyMarkers,
            Fill = new SolidColorPaint(BullishColor),
            Stroke = new SolidColorPaint(SKColors.White) { StrokeThickness = 1 },
            GeometrySize = 14
        };

        // Sell markers (red circles)
        _sellScatterSeries = new ScatterSeries<ObservablePoint>
        {
            Values = _sellMarkers,
            Fill = new SolidColorPaint(BearishColor),
            Stroke = new SolidColorPaint(SKColors.White) { StrokeThickness = 1 },
            GeometrySize = 14
        };

        // Pending order markers (amber circles)
        _pendingScatterSeries = new ScatterSeries<ObservablePoint>
        {
            Values = _pendingMarkers,
            Fill = new SolidColorPaint(PendingColor),
            Stroke = new SolidColorPaint(SKColors.White) { StrokeThickness = 2 },
            GeometrySize = 12
        };

        // Current price line (horizontal dashed line)
        _currentPriceLine = new LineSeries<ObservablePoint>
        {
            Values = _currentPriceData,
            Fill = null,
            Stroke = new SolidColorPaint(PriceLineColor)
            {
                StrokeThickness = 1,
                PathEffect = new DashEffect(new float[] { 5, 5 })
            },
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 0
        };

        // Set series - use line chart as primary (more reliable), candlestick as secondary
        MainChart.Series = new ISeries[]
        {
            _priceLineSeries,      // Primary: Line chart (reliable)
            _candlestickSeries,    // Secondary: Candlestick overlay
            _buyScatterSeries,
            _sellScatterSeries,
            _pendingScatterSeries,
            _currentPriceLine
        };

        // Configure X axis (time)
        MainChart.XAxes = new Axis[]
        {
            new Axis
            {
                LabelsPaint = new SolidColorPaint(LabelColor),
                SeparatorsPaint = new SolidColorPaint(GridColor),
                Labeler = value =>
                {
                    var index = (int)value;
                    if (index >= 0 && index < _candleData.Count)
                    {
                        var candle = _candleData[index];
                        if (candle.Date != DateTime.MinValue)
                            return candle.Date.ToString("HH:mm");
                    }
                    return "";
                },
                MinStep = 5,
                ForceStepToMin = true,
                TextSize = 10
            }
        };

        // Configure Y axis (price)
        MainChart.YAxes = new Axis[]
        {
            new Axis
            {
                LabelsPaint = new SolidColorPaint(LabelColor),
                SeparatorsPaint = new SolidColorPaint(GridColor),
                Position = LiveChartsCore.Measure.AxisPosition.End,
                Labeler = value => value.ToString("N2"),
                TextSize = 10,
                MinLimit = null,
                MaxLimit = null
            }
        };

        // Disable default animations for smoother streaming
        MainChart.AnimationsSpeed = TimeSpan.FromMilliseconds(150);
        MainChart.EasingFunction = null;
    }

    /// <summary>
    /// Update chart with new candle data (smooth, no flickering)
    /// </summary>
    public void UpdateCandles(IEnumerable<PriceCandle> candles)
    {
        var candleList = candles.ToList();
        if (candleList.Count == 0) return;

        // Check if this is streaming update (only last candle changed) or full refresh
        var lastCandle = candleList[^1];
        var isStreamingUpdate = _candleData.Count > 0 &&
                                candleList.Count == _candleData.Count &&
                                _lastCandleTime == candleList[0].Time;

        if (isStreamingUpdate)
        {
            // Streaming update - only update last candle (no flickering)
            var lastPoint = _candleData[^1];
            lastPoint.High = (double)lastCandle.High;
            lastPoint.Low = (double)lastCandle.Low;
            lastPoint.Close = (double)lastCandle.Close;
            lastPoint.Date = lastCandle.Time;
        }
        else if (_candleData.Count > 0 && candleList.Count > _candleData.Count &&
                 _lastCandleTime == candleList[^2].Time)
        {
            // New candle added - append without clearing
            _candleData.Add(new FinancialPoint
            {
                Date = lastCandle.Time,
                High = (double)lastCandle.High,
                Open = (double)lastCandle.Open,
                Close = (double)lastCandle.Close,
                Low = (double)lastCandle.Low
            });

            // Keep max 200 candles to prevent memory issues
            while (_candleData.Count > 200)
                _candleData.RemoveAt(0);
        }
        else
        {
            // Full refresh needed (interval changed or initial load)
            _candleData.Clear();
            foreach (var candle in candleList.TakeLast(200))
            {
                // Use property setters to ensure correct values
                _candleData.Add(new FinancialPoint
                {
                    Date = candle.Time,
                    High = (double)candle.High,
                    Open = (double)candle.Open,
                    Close = (double)candle.Close,
                    Low = (double)candle.Low
                });
            }
        }

        _lastCandleTime = candleList[0].Time;
        _currentPrice = lastCandle.Close;

        // Update price line data (for line chart)
        _priceLineData.Clear();
        for (int i = 0; i < _candleData.Count; i++)
        {
            _priceLineData.Add(new ObservablePoint(i, _candleData[i].Close ?? 0));
        }

        System.Diagnostics.Debug.WriteLine($"[TradingViewChart] Updated {_candleData.Count} candles, {_priceLineData.Count} price points");
        if (_priceLineData.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine($"[TradingViewChart] Price range: {_priceLineData.Min(p => p.Y)} - {_priceLineData.Max(p => p.Y)}");
        }

        // Force chart to update series
        if (_priceLineSeries != null)
        {
            _priceLineSeries.Values = _priceLineData;
        }
        if (_candlestickSeries != null)
        {
            _candlestickSeries.Values = _candleData;
        }

        // Update current price line
        UpdateCurrentPriceLine();

        // Update price label
        UpdatePriceLabel();
    }

    /// <summary>
    /// Add a single candle (for real-time streaming)
    /// </summary>
    public void AddCandle(PriceCandle candle)
    {
        if (_candleData.Count > 0 && _candleData[^1].Date == candle.Time)
        {
            // Update existing candle
            var lastPoint = _candleData[^1];
            lastPoint.High = Math.Max(lastPoint.High ?? 0, (double)candle.High);
            lastPoint.Low = Math.Min(lastPoint.Low ?? double.MaxValue, (double)candle.Low);
            lastPoint.Close = (double)candle.Close;
        }
        else
        {
            // New candle
            _candleData.Add(new FinancialPoint(
                candle.Time,
                (double)candle.High,
                (double)candle.Open,
                (double)candle.Close,
                (double)candle.Low
            ));

            // Keep max 200 candles
            while (_candleData.Count > 200)
                _candleData.RemoveAt(0);
        }

        _currentPrice = candle.Close;
        UpdateCurrentPriceLine();
        UpdatePriceLabel();
    }

    /// <summary>
    /// Update current price (for real-time streaming without new candle)
    /// </summary>
    public void UpdateCurrentPrice(decimal price)
    {
        _currentPrice = price;

        // Update last candle's close if exists
        if (_candleData.Count > 0)
        {
            var lastPoint = _candleData[^1];
            lastPoint.Close = (double)price;
            lastPoint.High = Math.Max(lastPoint.High ?? 0, (double)price);
            lastPoint.Low = Math.Min(lastPoint.Low ?? double.MaxValue, (double)price);
        }

        UpdateCurrentPriceLine();
        UpdatePriceLabel();
    }

    private void UpdateCurrentPriceLine()
    {
        if (_candleData.Count < 2) return;

        var priceY = (double)_currentPrice;

        // Create horizontal line across entire chart
        _currentPriceData.Clear();
        _currentPriceData.Add(new ObservablePoint(0, priceY));
        _currentPriceData.Add(new ObservablePoint(_candleData.Count - 1, priceY));
    }

    private void UpdatePriceLabel()
    {
        Dispatcher.Invoke(() =>
        {
            CurrentPriceText.Text = $"${_currentPrice:N2}";
            CurrentPriceLabel.Visibility = Visibility.Visible;

            // Position label based on price relative to visible range
            var yAxis = MainChart.YAxes.FirstOrDefault();
            if (yAxis != null && _candleData.Count > 0)
            {
                // Color based on trend
                var firstClose = _candleData.Count > 1 ? _candleData[^2].Close ?? 0 : 0;
                var isUp = (double)_currentPrice >= firstClose;
                CurrentPriceLabel.Background = new SolidColorBrush(
                    isUp ? Color.FromRgb(16, 185, 129) : Color.FromRgb(239, 68, 68)
                );
            }
        });
    }

    /// <summary>
    /// Add trade markers (buy/sell points)
    /// </summary>
    public void AddTradeMarker(TradeMarker marker)
    {
        // Find X position (candle index) for this time
        var xIndex = FindCandleIndex(marker.Time);
        if (xIndex < 0) return;

        var point = new ObservablePoint(xIndex, (double)marker.Price);

        switch (marker.Type)
        {
            case "Buy":
                _buyMarkers.Add(point);
                break;
            case "Sell":
                _sellMarkers.Add(point);
                break;
            case "PendingBuy":
            case "PendingSell":
            case "Pending":
                _pendingMarkers.Add(point);
                break;
        }
    }

    /// <summary>
    /// Set all trade markers at once
    /// </summary>
    public void SetTradeMarkers(IEnumerable<TradeMarker> markers)
    {
        _buyMarkers.Clear();
        _sellMarkers.Clear();
        _pendingMarkers.Clear();

        foreach (var marker in markers)
        {
            AddTradeMarker(marker);
        }
    }

    /// <summary>
    /// Clear all trade markers
    /// </summary>
    public void ClearMarkers()
    {
        _buyMarkers.Clear();
        _sellMarkers.Clear();
        _pendingMarkers.Clear();
    }

    /// <summary>
    /// Clear pending markers only
    /// </summary>
    public void ClearPendingMarkers()
    {
        _pendingMarkers.Clear();
    }

    /// <summary>
    /// Add pending order marker
    /// </summary>
    public void AddPendingOrder(decimal price, string type)
    {
        // Add at the last candle position
        if (_candleData.Count == 0) return;

        var point = new ObservablePoint(_candleData.Count - 1, (double)price);
        _pendingMarkers.Add(point);
    }

    private int FindCandleIndex(DateTime time)
    {
        for (int i = 0; i < _candleData.Count; i++)
        {
            if (_candleData[i].Date >= time)
                return i;
        }
        // Return last index if time is after all candles
        return _candleData.Count - 1;
    }

    /// <summary>
    /// Clear all chart data
    /// </summary>
    public void Clear()
    {
        _candleData.Clear();
        _buyMarkers.Clear();
        _sellMarkers.Clear();
        _pendingMarkers.Clear();
        _currentPriceData.Clear();
        _lastCandleTime = DateTime.MinValue;
        CurrentPriceLabel.Visibility = Visibility.Collapsed;
    }
}
