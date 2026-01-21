/*
 * ============================================================================
 * AutoTrade-X - License Activation Dialog
 * ============================================================================
 */

using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using AutoTradeX.Core.Interfaces;
using AutoTradeX.Core.Models;

namespace AutoTradeX.UI.Views;

public partial class LicenseDialog : Window
{
    private readonly ILicenseService? _licenseService;
    private bool _isActivating = false;

    public bool LicenseActivated { get; private set; } = false;
    public bool ContinueAsTrial { get; private set; } = false;

    public LicenseDialog()
    {
        InitializeComponent();

        _licenseService = App.Services?.GetService<ILicenseService>();

        Loaded += LicenseDialog_Loaded;
        MouseLeftButtonDown += (s, e) => DragMove();
    }

    private async void LicenseDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (_licenseService == null)
        {
            ShowError("License service not available");
            return;
        }

        // Show device ID
        var deviceId = _licenseService.GetMachineId();
        DeviceIdText.Text = deviceId.Length > 16 ? deviceId[..16] + "..." : deviceId;

        // Update status display
        await UpdateStatusDisplayAsync();
    }

    private async Task UpdateStatusDisplayAsync()
    {
        if (_licenseService == null) return;

        var license = _licenseService.CurrentLicense;

        if (license == null)
        {
            // No license - trial mode
            StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            StatusText.Text = "Trial Mode";
            StatusDetail.Text = "Start your 30-day trial";
            TrialButton.Content = "Start Trial";
            SetDeactivateLinkVisibility(false);
            return;
        }

        switch (license.Status)
        {
            case LicenseStatus.Valid:
                StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                StatusText.Text = $"{license.Tier} License";
                StatusDetail.Text = $"Valid until {license.ExpiresAt:MMM dd, yyyy}";
                TrialButton.Content = "Continue";
                LicenseActivated = true;
                SetDeactivateLinkVisibility(true);
                break;

            case LicenseStatus.Trial:
                var daysRemaining = _licenseService.GetTrialDaysRemaining();
                StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                StatusText.Text = "Trial Mode";
                StatusDetail.Text = daysRemaining > 0
                    ? $"{daysRemaining} days remaining"
                    : "Trial expired";
                TrialButton.Content = daysRemaining > 0 ? "Continue Trial" : "Trial Expired";
                TrialButton.IsEnabled = daysRemaining > 0;
                SetDeactivateLinkVisibility(false);
                break;

            case LicenseStatus.Expired:
                StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                StatusText.Text = "License Expired";
                StatusDetail.Text = $"Expired on {license.ExpiresAt:MMM dd, yyyy}";
                TrialButton.Content = "Continue (Limited)";
                SetDeactivateLinkVisibility(true);
                break;

            case LicenseStatus.Invalid:
            case LicenseStatus.Suspended:
                StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                StatusText.Text = license.Status == LicenseStatus.Suspended ? "License Suspended" : "Invalid License";
                StatusDetail.Text = "Please contact support";
                TrialButton.IsEnabled = false;
                SetDeactivateLinkVisibility(true);
                break;

            case LicenseStatus.DeviceLimitReached:
                StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                StatusText.Text = "Device Limit Reached";
                StatusDetail.Text = "Please deactivate another device";
                TrialButton.IsEnabled = false;
                SetDeactivateLinkVisibility(true);
                break;
        }

        // Pre-fill email if available
        if (!string.IsNullOrEmpty(license.Email))
        {
            EmailInput.Text = license.Email;
        }
    }

    private async void ActivateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isActivating || _licenseService == null) return;

        var licenseKey = LicenseKeyInput.Text.Trim();
        var email = EmailInput.Text.Trim();

        // Validation
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            ShowError("Please enter your license key");
            LicenseKeyInput.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            ShowError("Please enter a valid email address");
            EmailInput.Focus();
            return;
        }

        HideError();
        ShowLoading("Activating license...");
        _isActivating = true;

        try
        {
            var result = await _licenseService.ActivateLicenseAsync(licenseKey, email);

            if (result.Success)
            {
                HideLoading();
                LicenseActivated = true;
                await UpdateStatusDisplayAsync();

                MessageBox.Show(
                    "License activated successfully!",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            else
            {
                HideLoading();
                ShowError(result.Message ?? "Activation failed");
            }
        }
        catch (Exception ex)
        {
            HideLoading();
            ShowError($"Error: {ex.Message}");
        }
        finally
        {
            _isActivating = false;
        }
    }

    private void TrialButton_Click(object sender, RoutedEventArgs e)
    {
        ContinueAsTrial = true;
        DialogResult = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void PurchaseLink_Click(object sender, RoutedEventArgs e)
    {
        if (_licenseService != null)
        {
            var url = _licenseService.GetPurchaseUrl();
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private async void DeactivateLink_Click(object sender, RoutedEventArgs e)
    {
        if (_licenseService == null) return;

        var result = MessageBox.Show(
            "Are you sure you want to deactivate this license?\nYou can reactivate it later.",
            "Deactivate License",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            ShowLoading("Deactivating...");

            try
            {
                await _licenseService.DeactivateLicenseAsync();
                HideLoading();
                LicenseActivated = false;
                LicenseKeyInput.Clear();
                await UpdateStatusDisplayAsync();
            }
            catch (Exception ex)
            {
                HideLoading();
                ShowError($"Deactivation failed: {ex.Message}");
            }
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorBorder.Visibility = Visibility.Collapsed;
    }

    private void ShowLoading(string message)
    {
        LoadingText.Text = message;
        LoadingOverlay.Visibility = Visibility.Visible;

        // Start spinner animation
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromSeconds(1),
            RepeatBehavior = RepeatBehavior.Forever
        };
        SpinnerRotation.BeginAnimation(RotateTransform.AngleProperty, animation);
    }

    private void HideLoading()
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;
        SpinnerRotation.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    private void SetDeactivateLinkVisibility(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        DeactivateTextBlock.Visibility = visibility;
        DeactivateSeparator.Visibility = visibility;
    }
}
