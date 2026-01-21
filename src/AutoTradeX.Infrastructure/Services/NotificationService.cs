/*
 * ============================================================================
 * AutoTrade-X - Notification Service
 * ============================================================================
 * Handles in-app notifications, toast notifications, and sound alerts
 * ============================================================================
 */

using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using AutoTradeX.Core.Interfaces;

namespace AutoTradeX.Infrastructure.Services;

public interface INotificationService
{
    event EventHandler<NotificationEventArgs>? NotificationReceived;
    ObservableCollection<NotificationItem> Notifications { get; }

    void NotifyTrade(string symbol, decimal pnl, string message);
    void NotifyOpportunity(string symbol, decimal spread, string exchanges);
    void NotifyError(string source, string message);
    void NotifyWarning(string source, string message);
    void NotifyInfo(string source, string message);
    void NotifySuccess(string source, string message);

    void ClearNotifications();
    void MarkAsRead(string notificationId);
    void SetSettings(NotificationSettings settings);
    int UnreadCount { get; }

    /// <summary>
    /// Register a custom toast notification handler (for UI layer to provide implementation)
    /// </summary>
    void SetToastHandler(Action<NotificationItem>? handler);
}

public class NotificationService : INotificationService
{
    public event EventHandler<NotificationEventArgs>? NotificationReceived;
    public ObservableCollection<NotificationItem> Notifications { get; } = new();

    private readonly ILoggingService _logger;
    private NotificationSettings _settings = new();
    private readonly object _lock = new();
    private Action<NotificationItem>? _toastHandler;

    public int UnreadCount => Notifications.Count(n => !n.IsRead);

    public NotificationService(ILoggingService logger)
    {
        _logger = logger;
    }

    public void SetToastHandler(Action<NotificationItem>? handler)
    {
        _toastHandler = handler;
    }

    public void NotifyTrade(string symbol, decimal pnl, string message)
    {
        if (!_settings.NotifyOnTrade) return;

        var type = pnl >= 0 ? NotificationType.Success : NotificationType.Warning;
        var title = pnl >= 0 ? $"Trade Profitable: {symbol}" : $"Trade Loss: {symbol}";
        var fullMessage = $"{message}\nP&L: ${pnl:F2}";

        AddNotification(new NotificationItem
        {
            Title = title,
            Message = fullMessage,
            Type = type,
            Category = NotificationCategory.Trade,
            Symbol = symbol,
            PnL = pnl
        });

        if (_settings.PlaySound && pnl >= 0)
        {
            PlaySound(NotificationSoundType.Trade);
        }
    }

    public void NotifyOpportunity(string symbol, decimal spread, string exchanges)
    {
        if (!_settings.NotifyOnOpportunity) return;

        AddNotification(new NotificationItem
        {
            Title = $"High Spread: {symbol}",
            Message = $"Spread: {spread:F3}%\n{exchanges}",
            Type = NotificationType.Info,
            Category = NotificationCategory.Opportunity,
            Symbol = symbol,
            Spread = spread
        });

        if (_settings.PlaySound)
        {
            PlaySound(NotificationSoundType.Opportunity);
        }
    }

    public void NotifyError(string source, string message)
    {
        if (!_settings.NotifyOnError) return;

        AddNotification(new NotificationItem
        {
            Title = $"Error: {source}",
            Message = message,
            Type = NotificationType.Error,
            Category = NotificationCategory.System
        });

        if (_settings.PlaySound)
        {
            PlaySound(NotificationSoundType.Error);
        }
    }

    public void NotifyWarning(string source, string message)
    {
        AddNotification(new NotificationItem
        {
            Title = $"Warning: {source}",
            Message = message,
            Type = NotificationType.Warning,
            Category = NotificationCategory.System
        });
    }

    public void NotifyInfo(string source, string message)
    {
        AddNotification(new NotificationItem
        {
            Title = source,
            Message = message,
            Type = NotificationType.Info,
            Category = NotificationCategory.System
        });
    }

    public void NotifySuccess(string source, string message)
    {
        AddNotification(new NotificationItem
        {
            Title = source,
            Message = message,
            Type = NotificationType.Success,
            Category = NotificationCategory.System
        });
    }

    public void ClearNotifications()
    {
        lock (_lock)
        {
            Notifications.Clear();
        }
    }

    public void MarkAsRead(string notificationId)
    {
        lock (_lock)
        {
            var notification = Notifications.FirstOrDefault(n => n.Id == notificationId);
            if (notification != null)
            {
                notification.IsRead = true;
            }
        }
    }

    public void SetSettings(NotificationSettings settings)
    {
        _settings = settings;
    }

    private void AddNotification(NotificationItem notification)
    {
        lock (_lock)
        {
            // Keep only last 100 notifications
            while (Notifications.Count >= 100)
            {
                Notifications.RemoveAt(Notifications.Count - 1);
            }

            Notifications.Insert(0, notification);
        }

        _logger.LogInfo("Notification", $"[{notification.Type}] {notification.Title}: {notification.Message}");

        NotificationReceived?.Invoke(this, new NotificationEventArgs(notification));

        // Show Windows toast notification if enabled
        if (_settings.ShowToastNotifications)
        {
            ShowToastNotification(notification);
        }
    }

    // P/Invoke for Windows system sounds
    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);

    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONHAND = 0x00000010;        // Error
    private const uint MB_ICONQUESTION = 0x00000020;    // Question
    private const uint MB_ICONEXCLAMATION = 0x00000030; // Warning/Exclamation
    private const uint MB_ICONASTERISK = 0x00000040;    // Info/Asterisk

    private void PlaySound(NotificationSoundType soundType)
    {
        try
        {
            // Use Windows MessageBeep for system sounds
            var beepType = soundType switch
            {
                NotificationSoundType.Trade => MB_ICONEXCLAMATION,
                NotificationSoundType.Opportunity => MB_ICONASTERISK,
                NotificationSoundType.Error => MB_ICONHAND,
                _ => MB_OK
            };

            MessageBeep(beepType);
        }
        catch
        {
            // Ignore sound errors on non-Windows platforms
        }
    }

    private void ShowToastNotification(NotificationItem notification)
    {
        // Try to use registered toast handler from UI layer
        try
        {
            if (_toastHandler != null)
            {
                _toastHandler.Invoke(notification);
            }
            else
            {
                // No toast handler registered, just play sound as fallback
                PlaySound(notification.Type == NotificationType.Error
                    ? NotificationSoundType.Error
                    : NotificationSoundType.Default);
            }
        }
        catch
        {
            // Ignore toast errors
        }
    }
}

// Models
public class NotificationItem : System.ComponentModel.INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public NotificationType Type { get; set; } = NotificationType.Info;
    public NotificationCategory Category { get; set; } = NotificationCategory.System;

    // Optional metadata
    public string? Symbol { get; set; }
    public decimal? PnL { get; set; }
    public decimal? Spread { get; set; }

    private bool _isRead = false;
    public bool IsRead
    {
        get => _isRead;
        set
        {
            _isRead = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsRead)));
        }
    }

    public string TimeAgo
    {
        get
        {
            var diff = DateTime.UtcNow - Timestamp;
            if (diff.TotalSeconds < 60) return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            return Timestamp.ToString("MMM dd");
        }
    }

    public string TypeColor => Type switch
    {
        NotificationType.Success => "#10B981",
        NotificationType.Error => "#EF4444",
        NotificationType.Warning => "#F59E0B",
        NotificationType.Info => "#3B82F6",
        _ => "#6B7280"
    };

    public string TypeIcon => Type switch
    {
        NotificationType.Success => "check_circle",
        NotificationType.Error => "error",
        NotificationType.Warning => "warning",
        NotificationType.Info => "info",
        _ => "notifications"
    };

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error
}

public enum NotificationCategory
{
    System,
    Trade,
    Opportunity,
    Balance
}

public enum NotificationSoundType
{
    Default,
    Trade,
    Opportunity,
    Error
}

public class NotificationSettings
{
    public bool NotifyOnTrade { get; set; } = true;
    public bool NotifyOnOpportunity { get; set; } = true;
    public bool NotifyOnError { get; set; } = true;
    public bool PlaySound { get; set; } = true;
    public bool ShowToastNotifications { get; set; } = false;
    public decimal MinSpreadForNotification { get; set; } = 0.2m;
}

public class NotificationEventArgs : EventArgs
{
    public NotificationItem Notification { get; }

    public NotificationEventArgs(NotificationItem notification)
    {
        Notification = notification;
    }
}
