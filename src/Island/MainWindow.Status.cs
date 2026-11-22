using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public partial class MainWindow
{
    const double VolumeTrackWidth = 162;
    const double VolumeOvershoot = 7;
    const double VolumeOvershootSquash = 0.22;
    const double VolumeChangeThreshold = 0.004;
    const float VolumeUnknown = -1, VolumeMax = 0.999f, VolumeMin = 0.001f;
    const double QuietVolumeBelow = 0.34, MidVolumeBelow = 0.67;
    const double VolumeHudSeconds = 1.6, ChargeSeconds = 3, FocusSeconds = 2.2;
    const double ChargeFillWidth = 20;
    const double FullscreenEntryDepth = 18, FullscreenEntryPadding = 22;
    const int HeadsetLowPercent = 20, HeadsetCriticalPercent = 10;
    const string MutedLabel = "выкл";
    static readonly TimeSpan OvershootHold = TimeSpan.FromMilliseconds(140);
    static readonly CultureInfo Russian = new("ru-RU");

    readonly Spring _volumeOvershoot = new(0, 420, 18);
    readonly DelayedAction _overshootTimeout;
    float _lastVolume = VolumeUnknown;
    bool _lastMuted, _lastPlugged, _powerKnown;
    int _headsetCharge = Headset.Unknown;
    Guid _headsetId;
    bool? _doNotDisturb;
    IntPtr _fullscreenWindow;
    Rect _fullscreenScreen;
    bool _fullscreenRevealAllowed, _fullscreenExclusive, _revealedInFullscreen;
    bool HiddenByFullscreen => _hiddenByFullscreen && !_revealedInFullscreen;

    void UpdateClock()
    {
        DateTime now = DateTime.Now;
        BigClock.Text = now.ToString("HH:mm");
        BigDate.Text = now.ToString("dddd, d MMMM", Russian);
        UpdateMiniIsland(now, MiniWeatherReport());
    }

    void CheckFullscreen()
    {
        IntPtr window = IntPtr.Zero;
        Rect screen = Rect.Empty;
        bool materialgram = !_pickingArea && MaterialgramWindowMode.MatchesWindow(Native.ForegroundWindow);
        if (materialgram && !_sentAway) StayOnTop();
        bool hidden = !materialgram && Settings.HideFullscreen && !_pickingArea
            && !(Settings.DiscordInGames && ShowsVoice)
            && Native.TryGetForegroundFullscreen(_hwnd, out window, out screen);
        bool changed = hidden != _hiddenByFullscreen;
        if (!hidden) window = IntPtr.Zero;
        if (window != _fullscreenWindow)
        {
            _fullscreenWindow = window;
            _fullscreenRevealAllowed = hidden && Native.AllowsFullscreenReveal(window);
            changed |= _revealedInFullscreen;
            _revealedInFullscreen = false;
        }
        _fullscreenScreen = screen;
        _hiddenByFullscreen = hidden;
        _fullscreenExclusive = hidden && Native.IsExclusiveDirect3DFullscreen();
        if (changed && hidden) App.Log("hidden by fullscreen: " + Native.DescribeForeground());
        if (changed || hidden) UpdateFullscreenReveal(updateTargets: changed);
    }

    void UpdateFullscreenReveal(bool updateTargets = false)
    {
        bool show = false;
        if (_hiddenByFullscreen && _fullscreenRevealAllowed && !_fullscreenExclusive && !_sentAway
            && Native.ForegroundWindow == _fullscreenWindow)
        {
            PillShape shape = ShapeOf(_view);
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;
            show = InFullscreenRevealArea(_fullscreenScreen, Native.CursorPosition,
                new Size(shape.Width, shape.Height), dpi, Settings.Scale / 100d, Settings.Gap, _revealedInFullscreen);
        }
        if (_revealedInFullscreen != show)
        {
            if (show)
            {
                Native.KeepOnTop(_hwnd);
                _glass.Place();
            }
            updateTargets = true;
        }
        _revealedInFullscreen = show;
        if (updateTargets) UpdateTargets();
    }

    internal static bool InFullscreenRevealArea(Rect screen, Point pointer, Size island, double dpi,
        double islandScale, double gap, bool revealed)
    {
        if (screen.IsEmpty || !screen.Contains(pointer)) return false;
        double middle = screen.Left + screen.Width / 2;
        double halfWidth = (island.Width * islandScale / 2 + (revealed ? 52 : FullscreenEntryPadding)) * dpi;
        double depth = (revealed ? island.Height * islandScale + gap + 16 : FullscreenEntryDepth) * dpi;
        return Math.Abs(pointer.X - middle) <= halfWidth && pointer.Y - screen.Top <= depth;
    }

    void PollVolume()
    {
        if (!_audio.TryGetVolume(out float level, out bool muted)) return;
        if (_audio.TryTakeSwitch(out AudioService.Output device))
        {
            _lastVolume = VolumeUnknown;
            ReadHeadset(device, true);
        }
        bool first = _lastVolume < 0;
        if (!first && Math.Abs(level - _lastVolume) < VolumeChangeThreshold && muted == _lastMuted) return;

        _lastVolume = level;
        _lastMuted = muted;

        int percent = (int)Math.Round(level * 100);
        VolIcon.Kind = InfoVolIcon.Kind = muted || percent == 0 ? Glyph.Mute : level < QuietVolumeBelow ? Glyph.Quiet : level < MidVolumeBelow ? Glyph.Mid : Glyph.Loud;
        VolText.Text = percent.ToString();
        InfoVol.Text = VolumeLabel(level, muted);
        VolFill.BeginAnimation(WidthProperty, new DoubleAnimation(muted ? 0 : VolumeTrackWidth * level, Ms(first ? 0 : 140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });

        if (first) return;
        ShowTransient(View.Volume, VolumeHudSeconds);
        ShowPlayerVolume(level, muted, false);
    }

    static string VolumeLabel(float level, bool muted) => muted ? MutedLabel : (int)Math.Round(level * 100) + "%";

    bool IsVolumeAtLimit(bool up) => _lastVolume >= 0 && (up ? !_lastMuted && _lastVolume >= VolumeMax : _lastVolume <= VolumeMin);

    void OvershootVolume(bool up)
    {
        VolTrack.RenderTransformOrigin = new Point(up ? 0 : 1, 0.5);
        _volumeOvershoot.Target = VolumeOvershoot;
        _overshootTimeout.Start(OvershootHold);
        ShowTransient(View.Volume, VolumeHudSeconds);
        ShowPlayerVolume(_lastVolume, _lastMuted, false);
        StartShapeLoop();
    }

    void ReleaseVolumeOvershoot()
    {
        _volumeOvershoot.Target = 0;
        StartShapeLoop();
    }

    void StretchVolumeBar()
    {
        double push = Math.Max(_volumeOvershoot.Value, -VolumeOvershoot);
        VolStretch.ScaleX = 1 + push / VolumeTrackWidth;
        VolStretch.ScaleY = 1 - push / VolumeOvershoot * VolumeOvershootSquash;
    }

    void ShowPlayerVolume(float level, bool muted, bool app)
    {
        if (_view != View.MediaBig) return;
        PlayerVolumeIcon.Kind = app ? Glyph.Note : VolIcon.Kind;
        PlayerVolumeIcon.Fill = PlayerVolumeText.Foreground = app ? _accentBrush : _dim;
        PlayerVolumeText.Text = VolumeLabel(level, muted);

        var show = new DoubleAnimationUsingKeyFrames { Duration = Ms(1900) };
        show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1500))));
        show.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1900))));
        PlayerVolume.BeginAnimation(OpacityProperty, show);
    }

    void PollPower()
    {
        bool hasBattery = Native.TryGetBattery(out int percent, out bool plugged);
        InfoBatRow.SetVisible(hasBattery);
        if (!hasBattery) return;

        InfoBat.Text = percent + "%";
        InfoBat.Foreground = plugged ? _green : Brushes.White;

        if (_powerKnown && plugged && !_lastPlugged) ShowCharging(percent);
        _powerKnown = true;
        _lastPlugged = plugged;
    }

    void ShowCharging(int percent)
    {
        ChargeText.Text = percent + "%";
        ChargeFill.BeginAnimation(WidthProperty, new DoubleAnimation(0, ChargeFillWidth * percent / 100.0, Ms(700))
        {
            BeginTime = TimeSpan.FromMilliseconds(250),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        ShowTransient(View.Charge, ChargeSeconds);

        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 };
        BoltTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-35, 0, Ms(520)) { BeginTime = FadeInDelay, EasingFunction = ease });
        BoltSize.AnimateScale(new DoubleAnimation(0.3, 1, Ms(520)) { BeginTime = FadeInDelay, EasingFunction = ease });
    }

    async void ReadHeadset(AudioService.Output? output, bool announce = false)
    {
        int level = output is { } bound ? await Headset.ChargeAsync(bound.Container) : Headset.Unknown;
        if (output != _audio.Device) return;

        AudioService.Output device = output ?? default;
        bool known = level >= 0, low = known && level <= HeadsetLowPercent;
        bool sameHeadset = device.Container == _headsetId;
        int was = sameHeadset ? _headsetCharge : Headset.Unknown;
        if (known || !sameHeadset) _headsetCharge = level;
        _headsetId = device.Container;

        Glyph icon = device.Headphones ? Glyph.Headphones : Glyph.Speaker;
        InfoHeadsetRow.SetVisible(known);
        PlayerHeadset.SetVisible(known);
        InfoHeadsetIcon.Kind = PlayerHeadsetIcon.Kind = icon;
        InfoHeadset.Text = PlayerHeadsetText.Text = level + "%";
        InfoHeadset.Foreground = low ? _red : Brushes.White;
        PlayerHeadsetIcon.Fill = PlayerHeadsetText.Foreground = low ? _red : _dim;
        if (output == null) return;

        string name = device.Name.Length > 0 ? device.Name : "Вывод звука";
        bool Passed(int mark) => was > mark && level <= mark;
        if (announce && known)
            ShowHeadset(name, HeadsetModel(device), level, low);
        else if (announce)
            Notify(icon, Brushes.White, device.Kind.Length > 0 ? device.Kind : "Аудиоустройство", name);
        else if (known && (Passed(HeadsetLowPercent) || Passed(HeadsetCriticalPercent)))
            ShowHeadset(name, HeadsetModel(device), level, true);
    }

    // Системный флаг «Не беспокоить» иногда на миг меняется сам (например, при скриншоте),
    // поэтому уведомление показываем только когда новое значение держится дольше SettleTime.
    static readonly TimeSpan DoNotDisturbSettleTime = TimeSpan.FromSeconds(2);
    bool? _doNotDisturbCandidate;
    DateTime _doNotDisturbCandidateSince;

    void PollDoNotDisturb()
    {
        if (IsFocusUnsettled || Native.IsDoNotDisturbOn() is not bool quiet) return;
        if (quiet == _doNotDisturb) { _doNotDisturbCandidate = null; return; }
        if (_doNotDisturb is not null)
        {
            if (_doNotDisturbCandidate != quiet)
            {
                _doNotDisturbCandidate = quiet;
                _doNotDisturbCandidateSince = DateTime.UtcNow;
                return;
            }
            if (DateTime.UtcNow - _doNotDisturbCandidateSince < DoNotDisturbSettleTime) return;
        }
        _doNotDisturbCandidate = null;
        if (_doNotDisturb is not null && Native.IsForegroundCoveringScreen(_hwnd))
        {
            // Windows сама включает «Не беспокоить» при полноэкранных окнах (например, Telegram): запоминаем тихо.
            _doNotDisturb = quiet;
            InfoFocus.SetVisible(quiet);
            return;
        }
        bool first = _doNotDisturb == null;
        _doNotDisturb = quiet;
        InfoFocus.SetVisible(quiet);
        if (first) return;

        FocusIcon.Fill = FocusText.Foreground = quiet ? _indigo : _dim;
        FocusText.Text = quiet ? "Вкл." : "Выкл.";
        ShowTransient(View.Focus, FocusSeconds);

        IEasingFunction ease = quiet ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } : new CubicEase { EasingMode = EasingMode.EaseOut };
        var swing = new DoubleAnimation(quiet ? -80 : 0, quiet ? 0 : 24, Ms(quiet ? 620 : 420)) { BeginTime = FadeInDelay, EasingFunction = ease };
        var grow = new DoubleAnimation(quiet ? 0.4 : 1, quiet ? 1 : 0.84, Ms(quiet ? 520 : 420)) { BeginTime = FadeInDelay, EasingFunction = ease };
        FocusTurn.BeginAnimation(RotateTransform.AngleProperty, swing);
        FocusSize.AnimateScale(grow);
    }
}
