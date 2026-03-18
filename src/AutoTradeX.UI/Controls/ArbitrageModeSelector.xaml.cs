using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoTradeX.Core.Models;

namespace AutoTradeX.UI.Controls;

/// <summary>
/// Control for selecting arbitrage execution mode
/// คอนโทรลสำหรับเลือกโหมดการทำ Arbitrage
/// </summary>
public partial class ArbitrageModeSelector : UserControl
{
    #region Dependency Properties

    public static readonly DependencyProperty SelectedModeProperty =
        DependencyProperty.Register(nameof(SelectedMode), typeof(ArbitrageExecutionMode), typeof(ArbitrageModeSelector),
            new FrameworkPropertyMetadata(ArbitrageExecutionMode.DualBalance,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedModeChanged));

    public static readonly DependencyProperty SelectedTransferTypeProperty =
        DependencyProperty.Register(nameof(SelectedTransferType), typeof(TransferExecutionType), typeof(ArbitrageModeSelector),
            new FrameworkPropertyMetadata(TransferExecutionType.Manual,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedTransferTypeChanged));

    public static readonly DependencyProperty ExchangeANameProperty =
        DependencyProperty.Register(nameof(ExchangeAName), typeof(string), typeof(ArbitrageModeSelector),
            new PropertyMetadata("Exchange A", OnExchangeNameChanged));

    public static readonly DependencyProperty ExchangeBNameProperty =
        DependencyProperty.Register(nameof(ExchangeBName), typeof(string), typeof(ArbitrageModeSelector),
            new PropertyMetadata("Exchange B", OnExchangeNameChanged));

    public static readonly DependencyProperty IsReadyProperty =
        DependencyProperty.Register(nameof(IsReady), typeof(bool), typeof(ArbitrageModeSelector),
            new PropertyMetadata(true, OnReadinessChanged));

    public static readonly DependencyProperty ExchangeABalanceTextProperty =
        DependencyProperty.Register(nameof(ExchangeABalanceText), typeof(string), typeof(ArbitrageModeSelector),
            new PropertyMetadata("0.00 USDT"));

    public static readonly DependencyProperty ExchangeBBalanceTextProperty =
        DependencyProperty.Register(nameof(ExchangeBBalanceText), typeof(string), typeof(ArbitrageModeSelector),
            new PropertyMetadata("0.00000000 BTC"));

    public static readonly DependencyProperty ExchangeAIsReadyProperty =
        DependencyProperty.Register(nameof(ExchangeAIsReady), typeof(bool), typeof(ArbitrageModeSelector),
            new PropertyMetadata(true, OnExchangeReadinessChanged));

    public static readonly DependencyProperty ExchangeBIsReadyProperty =
        DependencyProperty.Register(nameof(ExchangeBIsReady), typeof(bool), typeof(ArbitrageModeSelector),
            new PropertyMetadata(true, OnExchangeReadinessChanged));

    #endregion

    #region Properties

    public ArbitrageExecutionMode SelectedMode
    {
        get => (ArbitrageExecutionMode)GetValue(SelectedModeProperty);
        set => SetValue(SelectedModeProperty, value);
    }

    public TransferExecutionType SelectedTransferType
    {
        get => (TransferExecutionType)GetValue(SelectedTransferTypeProperty);
        set => SetValue(SelectedTransferTypeProperty, value);
    }

    public string ExchangeAName
    {
        get => (string)GetValue(ExchangeANameProperty);
        set => SetValue(ExchangeANameProperty, value);
    }

    public string ExchangeBName
    {
        get => (string)GetValue(ExchangeBNameProperty);
        set => SetValue(ExchangeBNameProperty, value);
    }

    public bool IsReady
    {
        get => (bool)GetValue(IsReadyProperty);
        set => SetValue(IsReadyProperty, value);
    }

    public string ExchangeABalanceText
    {
        get => (string)GetValue(ExchangeABalanceTextProperty);
        set => SetValue(ExchangeABalanceTextProperty, value);
    }

    public string ExchangeBBalanceText
    {
        get => (string)GetValue(ExchangeBBalanceTextProperty);
        set => SetValue(ExchangeBBalanceTextProperty, value);
    }

    public bool ExchangeAIsReady
    {
        get => (bool)GetValue(ExchangeAIsReadyProperty);
        set => SetValue(ExchangeAIsReadyProperty, value);
    }

    public bool ExchangeBIsReady
    {
        get => (bool)GetValue(ExchangeBIsReadyProperty);
        set => SetValue(ExchangeBIsReadyProperty, value);
    }

    #endregion

    #region Events

    public event EventHandler<ModeChangedEventArgs>? ModeChanged;
    public event EventHandler<TransferTypeChangedEventArgs>? TransferTypeChanged;

    #endregion

    public ArbitrageModeSelector()
    {
        InitializeComponent();
        UpdateUI();
    }

    #region Event Handlers

    private void DualBalanceCard_Click(object sender, MouseButtonEventArgs e)
    {
        SelectedMode = ArbitrageExecutionMode.DualBalance;
    }

    private void TransferModeCard_Click(object sender, MouseButtonEventArgs e)
    {
        SelectedMode = ArbitrageExecutionMode.Transfer;
    }

    private void AutoTransferCard_Click(object sender, MouseButtonEventArgs e)
    {
        SelectedTransferType = TransferExecutionType.Auto;
    }

    private void ManualTransferCard_Click(object sender, MouseButtonEventArgs e)
    {
        SelectedTransferType = TransferExecutionType.Manual;
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnSelectedModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ArbitrageModeSelector selector)
        {
            selector.UpdateUI();
            selector.ModeChanged?.Invoke(selector, new ModeChangedEventArgs(
                (ArbitrageExecutionMode)e.NewValue,
                (ArbitrageExecutionMode)e.OldValue));
        }
    }

    private static void OnSelectedTransferTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ArbitrageModeSelector selector)
        {
            selector.UpdateTransferTypeUI();
            selector.TransferTypeChanged?.Invoke(selector, new TransferTypeChangedEventArgs(
                (TransferExecutionType)e.NewValue,
                (TransferExecutionType)e.OldValue));
        }
    }

    private static void OnExchangeNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ArbitrageModeSelector selector)
        {
            selector.UpdateExchangeLabels();
        }
    }

    private static void OnReadinessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ArbitrageModeSelector selector)
        {
            selector.UpdateReadinessUI();
        }
    }

    private static void OnExchangeReadinessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ArbitrageModeSelector selector)
        {
            selector.UpdateExchangeReadinessUI();
        }
    }

    #endregion

    #region UI Update Methods

    private void UpdateUI()
    {
        var isDualBalance = SelectedMode == ArbitrageExecutionMode.DualBalance;

        // Update mode card styles
        if (isDualBalance)
        {
            DualBalanceCard.Style = (Style)Resources["SelectedModeCardStyle"];
            TransferModeCard.Style = (Style)Resources["ModeCardStyle"];
            DualBalanceCheck.Visibility = Visibility.Visible;
            TransferModeCheck.Visibility = Visibility.Collapsed;
            TransferModeUncheck.Visibility = Visibility.Visible;
        }
        else
        {
            DualBalanceCard.Style = (Style)Resources["ModeCardStyle"];
            TransferModeCard.Style = (Style)Resources["SelectedModeCardStyle"];
            DualBalanceCheck.Visibility = Visibility.Collapsed;
            TransferModeCheck.Visibility = Visibility.Visible;
            TransferModeUncheck.Visibility = Visibility.Collapsed;
        }

        // Update description
        var modeInfo = ArbitrageModeInfo.GetModeInfo(SelectedMode);
        ModeDescriptionTitle.Text = modeInfo.EnglishName;
        ModeDescriptionTitle.Foreground = new SolidColorBrush(isDualBalance
            ? (Color)ColorConverter.ConvertFromString("#10B981")
            : (Color)ColorConverter.ConvertFromString("#F59E0B"));
        ModeDescription.Text = modeInfo.DetailedDescriptionThai;

        // Show/hide transfer options
        TransferOptionsPanel.Visibility = isDualBalance ? Visibility.Collapsed : Visibility.Visible;

        // Show/hide balance readiness (only for Dual-Balance mode)
        BalanceReadinessPanel.Visibility = isDualBalance ? Visibility.Visible : Visibility.Collapsed;

        UpdateTransferTypeUI();
        UpdateExchangeLabels();
    }

    private void UpdateTransferTypeUI()
    {
        var isAuto = SelectedTransferType == TransferExecutionType.Auto;

        if (isAuto)
        {
            AutoTransferCard.Style = (Style)Resources["SelectedTransferOptionStyle"];
            ManualTransferCard.Style = (Style)Resources["TransferOptionStyle"];
            AutoTransferCheck.Visibility = Visibility.Visible;
            AutoTransferUncheck.Visibility = Visibility.Collapsed;
            ManualTransferCheck.Visibility = Visibility.Collapsed;
            ManualTransferUncheck.Visibility = Visibility.Visible;
        }
        else
        {
            AutoTransferCard.Style = (Style)Resources["TransferOptionStyle"];
            ManualTransferCard.Style = (Style)Resources["SelectedTransferOptionStyle"];
            AutoTransferCheck.Visibility = Visibility.Collapsed;
            AutoTransferUncheck.Visibility = Visibility.Visible;
            ManualTransferCheck.Visibility = Visibility.Visible;
            ManualTransferUncheck.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateExchangeLabels()
    {
        ExchangeALabel.Text = $"{ExchangeAName} (Buy)";
        ExchangeBLabel.Text = $"{ExchangeBName} (Sell)";
    }

    private void UpdateReadinessUI()
    {
        if (IsReady)
        {
            ReadyStatusBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2010B981"));
            ReadyStatusText.Text = "✓ พร้อมเทรด / Ready to Trade";
            ReadyStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
        }
        else
        {
            ReadyStatusBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20EF4444"));
            ReadyStatusText.Text = "✗ ยอดไม่เพียงพอ / Insufficient Balance";
            ReadyStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
        }
    }

    private void UpdateExchangeReadinessUI()
    {
        var readyColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
        var notReadyColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));

        ExchangeABalance.Text = (ExchangeAIsReady ? "✓ " : "✗ ") + ExchangeABalanceText;
        ExchangeABalance.Foreground = ExchangeAIsReady ? readyColor : notReadyColor;

        ExchangeBBalance.Text = (ExchangeBIsReady ? "✓ " : "✗ ") + ExchangeBBalanceText;
        ExchangeBBalance.Foreground = ExchangeBIsReady ? readyColor : notReadyColor;

        IsReady = ExchangeAIsReady && ExchangeBIsReady;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Update balance readiness display
    /// อัปเดตการแสดงความพร้อมของยอดเงิน
    /// </summary>
    public void UpdateBalanceReadiness(DualBalanceReadiness readiness)
    {
        ExchangeAName = readiness.ExchangeAName;
        ExchangeBName = readiness.ExchangeBName;

        ExchangeABalanceText = $"{readiness.BuySideQuoteAvailable:N2} {readiness.QuoteAsset}";
        ExchangeBBalanceText = $"{readiness.SellSideBaseAvailable:N8} {readiness.BaseAsset}";

        ExchangeAIsReady = readiness.BuySideReady;
        ExchangeBIsReady = readiness.SellSideReady;
    }

    /// <summary>
    /// Get current mode info
    /// รับข้อมูลโหมดปัจจุบัน
    /// </summary>
    public ArbitrageModeInfo GetCurrentModeInfo()
    {
        return ArbitrageModeInfo.GetModeInfo(SelectedMode);
    }

    /// <summary>
    /// Get current transfer type info
    /// รับข้อมูลรูปแบบการโอนปัจจุบัน
    /// </summary>
    public TransferExecutionTypeInfo GetCurrentTransferTypeInfo()
    {
        return TransferExecutionTypeInfo.GetTypeInfo(SelectedTransferType);
    }

    #endregion
}

#region Event Args

public class ModeChangedEventArgs : EventArgs
{
    public ArbitrageExecutionMode NewMode { get; }
    public ArbitrageExecutionMode OldMode { get; }

    public ModeChangedEventArgs(ArbitrageExecutionMode newMode, ArbitrageExecutionMode oldMode)
    {
        NewMode = newMode;
        OldMode = oldMode;
    }
}

public class TransferTypeChangedEventArgs : EventArgs
{
    public TransferExecutionType NewType { get; }
    public TransferExecutionType OldType { get; }

    public TransferTypeChangedEventArgs(TransferExecutionType newType, TransferExecutionType oldType)
    {
        NewType = newType;
        OldType = oldType;
    }
}

#endregion
