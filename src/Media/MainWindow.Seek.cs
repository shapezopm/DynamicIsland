using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public partial class MainWindow
{
    enum SeekDisplay { TrackShape, Plain, Lines, Wave }

    const double SeekTrackWidth = 260;
    const double SeekHeight = 6, SeekDragHeight = 12, MinSeekHeight = 2;
    const double SeekSettledFraction = 0.02;
    // За сколько секунд до конца волна плавно становится ровной. Затрагивает только волну.
    const double SeekEndFadeSeconds = 10;
    const double SeekHoldSeconds = 1;
    const int NoSeconds = -1;
    const string NoTime = "–:––";

    readonly Spring _seekFill = new(0, 170, 26), _seekHeight = new(SeekHeight, 420, 26);
    // Волна: при play растёт из линии (~0.43 с до полной амплитуды), при паузе выравнивается (~0.35 с).
    // Оба режима — критическое затухание: ω = √k, c = 2ω.
    const double EnergyRiseStiffness = 182, EnergyRiseDamping = 27;
    const double EnergyFallStiffness = 275, EnergyFallDamping = 33;
    readonly Spring _seekEnergy = new(0, EnergyRiseStiffness, EnergyRiseDamping);
    readonly FrameLoop _seekLoop;
    bool _scrubbing;
    double _scrubFraction, _scrubHeldUntil;
    (int At, int Total) _seekLabel = (NoSeconds, NoSeconds);
    LyricsService.Line[] _seekLinesFrom = [];
    TimeSpan _seekLinesSpan;
    int _seekLinesAdvanceMs;
    SeekDisplay _seekDisplay;
    TrackSnapshot? _trackShapeTrack;
    CancellationTokenSource? _trackShapeLookup;
    bool _trackShapeLookupStarted;
    TimeSpan? _soundCloudWaveformDuration;
    static bool UseTrackShape => !Settings.LineBar && !Settings.SeekWave;

    void StartSeekLoop()
    {
        _seekFill.Value = _seekFill.Velocity = 0;
        SyncSeekStyle(false);
        _seekLoop.Start();
    }

    void SyncTrackShape()
    {
        TrackSnapshot track = _media.Snapshot;
        bool sameTrack = track.SameIdentity(_trackShapeTrack);
        _trackShapeTrack = track;
        if (!sameTrack || _soundCloudWaveformDuration is { } duration
            && !SoundCloudWaveform.MatchesDuration(track.Duration, duration))
        {
            _trackShapeLookup?.Cancel();
            _trackShapeLookupStarted = false;
            _soundCloudWaveformDuration = null;
            SeekTrackShape.Reset();
        }
        StartTrackShapeLookup();
    }

    void StartTrackShapeLookup()
    {
        if (!UseTrackShape || _trackShapeLookupStarted || _trackShapeTrack is not { Title.Length: > 0 } track) return;
        _trackShapeLookupStarted = true;
        SeekTrackShape.SetProfile(TrackShapeBar.ApproximateLevels(track));
        if (track.SourceApp.Length == 0) return;
        var lookup = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        _trackShapeLookup = lookup;
        _ = LoadTrackShapeAsync(track, lookup);
    }

    async Task LoadTrackShapeAsync(TrackSnapshot track, CancellationTokenSource lookup)
    {
        try
        {
            SoundCloudWaveform.Profile? profile = await SoundCloudWaveform.LoadAsync(track, lookup.Token);
            if (profile is null || lookup.IsCancellationRequested || _trackShapeTrack is not { } current
                || !track.SameIdentity(current) || !SoundCloudWaveform.MatchesDuration(current.Duration, profile.Duration)) return;
            _soundCloudWaveformDuration = profile.Duration;
            SeekTrackShape.SetProfile(TrackShapeBar.ShapeLevels(profile.Levels));
            App.Log("Loaded complete SoundCloud waveform.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Text.Json.JsonException) { }
        finally
        {
            if (ReferenceEquals(_trackShapeLookup, lookup)) _trackShapeLookup = null;
            lookup.Dispose();
        }
    }

    bool AdvanceSeek(double dt)
    {
        if (_view != View.MediaBig && !_scrubbing) return false;

        double now = _clock.Elapsed.TotalSeconds;
        TimeSpan duration = _media.Duration;
        bool known = duration.TotalSeconds >= 1;
        bool counted = CanCountPosition(known, _media.PositionQuality);
        double played = _media.Progress;
        if (!_scrubbing && now < _scrubHeldUntil && Math.Abs(played - _scrubFraction) < SeekSettledFraction) _scrubHeldUntil = 0;
        bool preview = _scrubbing || now < _scrubHeldUntil;
        double shown = preview ? _scrubFraction : played;

        double track = SeekArea.ActualWidth > 0 ? SeekArea.ActualWidth : SeekTrackWidth;
        _seekFill.Target = track * shown;
        _seekHeight.Target = _scrubbing ? SeekDragHeight : SeekHeight;
        _seekEnergy.Target = _media.IsPlaying ? 1 : 0;
        if (_seekEnergy.Target > _seekEnergy.Value) _seekEnergy.Tune(EnergyRiseStiffness, EnergyRiseDamping);
        else _seekEnergy.Tune(EnergyFallStiffness, EnergyFallDamping);
        _seekFill.Advance(dt);
        _seekHeight.Advance(dt);
        _seekEnergy.Advance(dt);

        if (_seekDisplay == SeekDisplay.TrackShape)
        {
            SeekTrackShape.SetProgress(_seekFill.Value / track);
        }
        else
        {
            double thickness = Math.Max(_seekHeight.Value, MinSeekHeight);
            double fill = Math.Clamp(_seekFill.Value, 0, track);
            bool wave = _seekDisplay == SeekDisplay.Wave;
            bool hoverOn = !wave || Settings.SeekHoverOn;
            double? pointer = hoverOn && (SeekArea.IsMouseOver || _scrubbing) ? Mouse.GetPosition(SeekArea).X : null;
            if (wave)
            {
                SeekWave.Thickness = thickness;
                SeekWave.Fill = fill;
                SeekWave.Energy = _seekEnergy.Value;
                double remaining = duration.TotalSeconds - _media.Position.TotalSeconds;
                double fade = Math.Clamp(remaining / SeekEndFadeSeconds, 0, 1);
                SeekWave.Taper = known ? fade * fade * (3 - 2 * fade) : 1;
                SeekWave.Advance(dt, pointer, SeekHover.Magnifier);
            }
            else
            {
                LineBar bar = _seekDisplay == SeekDisplay.Lines ? SeekLines : SeekPlain;
                bar.Thickness = thickness;
                bar.Fill = fill;
                bar.Advance(dt, pointer, Settings.SeekHover);
            }
        }
        TimeSpan lyricAt = LyricTime;
        if (_playerFinal.IsComplete(lyricAt) != _playerLyricsFrozen || _playerFinal.LineAt(_playerLines, lyricAt) != _playerIndex)
            UpdatePlayerLyrics(snap: false);
        MarkLineStarts(duration);
        FillSungLine();
        AdvancePlayerLyrics(now, dt);

        TimeSpan at = SeekLabelPosition(_media.Position, duration, shown, preview);
        var label = counted ? ((int)at.TotalSeconds, known ? (int)duration.TotalSeconds : NoSeconds) : (NoSeconds, NoSeconds);
        if (label == _seekLabel) return true;

        _seekLabel = label;
        PosText.Text = counted ? FormatTime(at) : NoTime;
        RemText.Text = known ? FormatTime(duration) : NoTime;
        return true;
    }

    void MarkLineStarts(TimeSpan duration)
    {
        if (ReferenceEquals(_playerLines, _seekLinesFrom) && duration == _seekLinesSpan && _seekLinesAdvanceMs == _lyricAdvanceMs) return;

        _seekLinesFrom = _playerLines;
        _seekLinesSpan = duration;
        _seekLinesAdvanceMs = _lyricAdvanceMs;
        double seconds = duration.TotalSeconds;
        SeekLines.SetLines(seconds < 1 ? [] : _playerLines.Select(line => LyricTiming.MarkerFraction(line.Time, duration, _lyricAdvanceMs)).ToArray(),
            _playerLines.Select(line => line.Text).ToArray());
        SyncSeekStyle(true);
    }

    void SyncSeekStyle(bool animate)
    {
        SeekDisplay display = UseTrackShape ? SeekDisplay.TrackShape : Settings.SeekWave ? SeekDisplay.Wave
            : SeekLines.HasMarks ? SeekDisplay.Lines : SeekDisplay.Plain;
        if (display == _seekDisplay) return;

        _seekDisplay = display;
        if (display == SeekDisplay.TrackShape) StartTrackShapeLookup();
        Duration time = Ms(animate ? 260 : 0);
        SeekTrackShape.BeginAnimation(OpacityProperty, new DoubleAnimation(display == SeekDisplay.TrackShape ? 1 : 0, time));
        SeekLines.BeginAnimation(OpacityProperty, new DoubleAnimation(display == SeekDisplay.Lines ? 1 : 0, time));
        SeekWave.BeginAnimation(OpacityProperty, new DoubleAnimation(display == SeekDisplay.Wave ? 1 : 0, time));
        SeekPlain.BeginAnimation(OpacityProperty, new DoubleAnimation(display == SeekDisplay.Plain ? 1 : 0, time));
    }

    static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : t.ToString(@"m\:ss");

    internal static bool CanCountPosition(bool knownDuration, PositionQuality quality) =>
        knownDuration || quality != PositionQuality.Unknown;

    internal static TimeSpan SeekLabelPosition(TimeSpan position, TimeSpan duration, double fraction, bool preview) =>
        preview && duration.TotalSeconds >= 1 ? duration * Math.Clamp(fraction, 0, 1) : position;

    double SeekFraction(MouseEventArgs e)
    {
        double x = e.GetPosition(SeekArea).X;
        if (_seekDisplay == SeekDisplay.Lines) x = SeekLines.Snap(x);
        return Math.Clamp(x / SeekArea.ActualWidth, 0, 1);
    }

    void Seek_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (SeekArea.ActualWidth <= 0 || !_media.Seekable) return;

        _scrubbing = true;
        _scrubFraction = SeekFraction(e);
        _seekFill.Tune(900, 60);
        PosText.Foreground = RemText.Foreground = Brushes.White;
        SeekArea.CaptureMouse();
    }

    void Seek_MouseMove(object sender, MouseEventArgs e)
    {
        if (_scrubbing) _scrubFraction = SeekFraction(e);
    }

    void Seek_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_scrubbing) return;

        EndScrub();
        _scrubHeldUntil = _clock.Elapsed.TotalSeconds + SeekHoldSeconds;
        _ = _media.Seek(_scrubFraction);
        SeekArea.ReleaseMouseCapture();
    }

    void Seek_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_scrubbing) EndScrub();
    }

    void EndScrub()
    {
        _scrubbing = false;
        _seekFill.Tune(170, 26);
        PosText.Foreground = RemText.Foreground = _dim;
    }
}
