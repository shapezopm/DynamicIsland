using System.Windows;
using System.Windows.Media.Animation;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public partial class MainWindow
{
    const int NotificationQueueLimit = 8;
    const double NotificationFadeMs = 145;
    static readonly TimeSpan NotificationLifetime = TimeSpan.FromSeconds(20);
    static readonly TimeSpan MessageLifetime = TimeSpan.FromSeconds(45);

    sealed record PendingNotification(View View, double Seconds, Action Present,
        string Key, Func<bool>? IsRelevant, TimeSpan ExpiresAt);

    readonly List<PendingNotification> _notifications = [];
    PendingNotification? _activeNotification;
    bool _notificationLeaving;

    void EnqueueNotification(View view, double seconds, Action present, string key,
        Func<bool>? isRelevant = null, bool force = false)
    {
        var item = new PendingNotification(view, seconds, present, key, isRelevant,
            _clock.Elapsed + (view == View.Message ? MessageLifetime : NotificationLifetime));
        if (force)
        {
            // The alarm and the QR picker need immediate feedback. Hide any outgoing notification first.
            if (_activeNotification is { } outgoing) CollapseView(_views[outgoing.View]);
            ClearActiveNotification();
            PresentNotification(item, force: true);
            return;
        }

        if (_activeNotification?.Key == key && view != View.Toast) return;
        _notifications.RemoveAll(other => other.Key == key || other.ExpiresAt <= _clock.Elapsed);
        if (_notifications.Count == NotificationQueueLimit)
        {
            int replace = _notifications.FindIndex(other => other.View != View.Message);
            if (replace < 0 && view != View.Message) return;
            _notifications.RemoveAt(Math.Max(replace, 0));
        }
        _notifications.Add(item);
        TryShowNextNotification();
    }

    bool TryShowNextNotification()
    {
        if (_notifications.Count == 0 || _transientView is not null || _forcedView is not null
            || _hiddenByFullscreen || _sentAway || _ringing || _pickingArea) return false;
        if (_panel != Panel.None && _panel != Panel.Player) return false;
        if (NoticeView.Visibility == Visibility.Visible || ToastView.Visibility == Visibility.Visible
            || MessageView.Visibility == Visibility.Visible) return false;

        _notifications.RemoveAll(item => item.ExpiresAt <= _clock.Elapsed || item.IsRelevant?.Invoke() == false);
        // The expanded player can show chat messages. Keep other types in order until it closes.
        int index = _panel == Panel.Player ? _notifications.FindIndex(item => item.View == View.Message)
            : _notifications.Count > 0 ? 0 : -1;
        if (index < 0) return false;

        var next = _notifications[index];
        _notifications.RemoveAt(index);
        PresentNotification(next);
        return true;
    }

    void PresentNotification(PendingNotification item, bool force = false)
    {
        _activeNotification = item;
        bool sameView = _view == item.View;
        item.Present();
        ShowTransient(item.View, item.Seconds, force);
        if (sameView) FadeIn(_views[item.View]);
    }

    bool StartNotificationFade()
    {
        if (_activeNotification is not { } active || _notificationLeaving || _view != active.View) return false;
        FrameworkElement panel = _views[active.View];
        _notificationLeaving = true;
        _noticeOpen = _messageOpen = null;
        panel.IsHitTestVisible = false;
        panel.BeginAnimation(OpacityProperty, new DoubleAnimation(panel.Opacity, 0, Ms(NotificationFadeMs)));
        _transientTimeout.Start(TimeSpan.FromMilliseconds(NotificationFadeMs));
        return true;
    }

    void ClearActiveNotification()
    {
        bool updateCover = _activeNotification?.View == View.Toast;
        _activeNotification = null;
        _notificationLeaving = false;
        _noticeOpen = _messageOpen = null;
        if (updateCover) SyncMediaCover();
    }
}
