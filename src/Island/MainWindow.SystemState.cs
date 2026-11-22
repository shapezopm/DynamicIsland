using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static DynamicIsland.Motion;

namespace DynamicIsland;

// Системные переключатели и яркость: ночной свет, режим «в самолёте», яркость основного экрана.
public partial class MainWindow
{
    // Переключатель показываем, только если новое состояние держится дольше этого времени
    // (ночной свет по расписанию и быстрые переключения не должны спамить уведомлениями).
    static readonly TimeSpan SystemSettleTime = TimeSpan.FromSeconds(0.5);
    static readonly TimeSpan BrightnessRetryDelay = TimeSpan.FromSeconds(5);

    struct SystemSlot
    {
        public bool? Shown;
        public bool? Candidate;
        public DateTime Since;
    }

    SystemSlot _nightLightSlot, _airplaneSlot;
    int? _brightness;
    bool _readingBrightness;
    TimeSpan _nextBrightnessRead;
    string _systemDiag = "";

    // Возвращает true только для подтверждённого изменения; самое первое значение запоминается молча.
    bool ConfirmedChange(ref SystemSlot slot, bool value)
    {
        if (!slot.Shown.HasValue)
        {
            slot.Shown = value;
            return false;
        }
        bool shown = slot.Shown.Value;
        if (value == shown)
        {
            slot.Candidate = null;
            return false;
        }
        if (slot.Candidate != value)
        {
            slot.Candidate = value;
            slot.Since = DateTime.UtcNow;
            return false;
        }
        if (DateTime.UtcNow - slot.Since < SystemSettleTime) return false;
        slot.Shown = value;
        slot.Candidate = null;
        return true;
    }

    // Переключатели опрашиваются раз в ~0.5 с: состояние меняется редко.
    void PollSystemState()
    {
        bool? nightLight = Native.IsNightLightOn();
        bool? airplane = Native.IsAirplaneModeOn();

        string diag = $"nightlight={nightLight} airplane={airplane}";
        if (diag != _systemDiag)
        {
            _systemDiag = diag;
            App.Log("system: " + diag);
        }

        if (nightLight is bool night && ConfirmedChange(ref _nightLightSlot, night))
            ShowSystemToggle(Glyph.Moon, "Ночной свет", night);

        if (airplane is bool plane && ConfirmedChange(ref _airplaneSlot, plane))
            ShowSystemToggle(Glyph.Offline, "Режим «в самолёте»", plane);
    }

    // A slow WMI reply must not block animation or accumulate pending reads.
    async Task PollBrightness()
    {
        if (_readingBrightness || _clock.Elapsed < _nextBrightnessRead) return;
        _readingBrightness = true;
        int? reading;
        try { reading = await Task.Run(Native.GetBrightness); }
        finally { _readingBrightness = false; }
        if (reading is not int level)
        {
            _nextBrightnessRead = _clock.Elapsed + BrightnessRetryDelay;
            return;
        }
        bool first = _brightness is null;
        if (!first && level == _brightness) return;
        _brightness = level;

        BrightText.Text = level + "%";
        BrightFill.BeginAnimation(WidthProperty, new DoubleAnimation(VolumeTrackWidth * Math.Clamp(level, 0, 100) / 100.0, Ms(first ? 0 : 140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        if (first) return;
        ShowTransient(View.Brightness, VolumeHudSeconds);
    }

    void ShowSystemToggle(Glyph icon, string title, bool on)
    {
        SysIcon.Kind = icon;
        SysIcon.Fill = SysState.Foreground = on ? _indigo : _dim;
        SysTitle.Text = title;
        SysState.Text = on ? "Вкл." : "Выкл.";
        ShowTransient(View.SystemToggle, FocusSeconds);

        IEasingFunction ease = on ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } : new CubicEase { EasingMode = EasingMode.EaseOut };
        var swing = new DoubleAnimation(on ? -80 : 0, on ? 0 : 24, Ms(on ? 620 : 420)) { BeginTime = FadeInDelay, EasingFunction = ease };
        var grow = new DoubleAnimation(on ? 0.4 : 1, on ? 1 : 0.84, Ms(on ? 520 : 420)) { BeginTime = FadeInDelay, EasingFunction = ease };
        SysTurn.BeginAnimation(RotateTransform.AngleProperty, swing);
        SysSize.AnimateScale(grow);
    }
}
