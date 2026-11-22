using System.Windows;
using System.Windows.Media;

namespace DynamicIsland;

public partial class MainWindow
{
    PointerLyric? _pointerLyric;
    bool _pointerLyricBroken;

    void PointerLyric_Click(object sender, RoutedEventArgs e)
    {
        int picked = PointerLyricSegments.PickUnderPointer();
        Settings.PointerLyric = picked != PointerLyricOff;
        if (Settings.PointerLyric) Settings.PointerLyricShape = (PointerLyricShape)(picked - FirstPointerLyricShape);
        UpdateSwitches(true);
        _pointerLyric?.Reshape();
        UpdatePointerLyric();
    }

    void UpdatePointerLyric()
    {
        (string text, double sung, double seconds) = PointerLyricLine();
        if ((text.Length == 0 && _pointerLyric == null) || _pointerLyricBroken) return;

        if (_pointerLyric == null)
        {
            try
            {
                _pointerLyric = new PointerLyric((FontFamily)FindResource("Display"), () => _hovered);
            }
            catch (Exception ex)
            {
                App.Log(ex);
                _pointerLyricBroken = true;
                return;
            }
            StayOnTop();
        }
        try
        {
            _pointerLyric.ShowLine(text, sung, seconds);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            _pointerLyricBroken = true;
            _pointerLyric.ShowLine("", 0, 0);
        }
    }

    void StayOnTop()
    {
        Native.KeepOnTop(_hwnd);
        _glass.Place();

        if (_pointerLyric != null) Native.PutBehind(_pointerLyric.Handle, _glass.IsOn ? _glass.Handle : _hwnd);
    }

    (string Text, double Sung, double Seconds) PointerLyricLine()
    {
        if (!Settings.PointerLyric || !Settings.Lyrics || !_media.HasTrack || !_media.IsPlaying || _hiddenByFullscreen) return ("", 0, 0);
        LyricsResult result = CurrentLyricsResult();
        if (result.Status != LyricsStatus.Synced) return ("", 0, 0);
        double duration = result.Candidate?.Duration ?? 0;
        LyricsService.Line[] lines = TrimmedOutro(result.Lines, duration, out TimeSpan? end, out _);
        return PointerLyricCaption(lines, LyricTime, _media.SourceDuration, duration, end);
    }

    internal static (string Text, double Sung, double Seconds) PointerLyricCaption(
        LyricsResult result, TimeSpan at, TrackDuration source)
    {
        double duration = result.Candidate?.Duration ?? 0;
        LyricsService.Line[] lines = result.Status == LyricsStatus.Synced ? LyricTiming.TrimUntrustedOutro(result.Lines, source, duration, out _) : [];
        return PointerLyricCaption(lines, at, source, duration, LyricTiming.DocumentEnd(lines, source, duration));
    }

    static (string Text, double Sung, double Seconds) PointerLyricCaption(
        LyricsService.Line[] lines, TimeSpan at, TrackDuration source, double duration, TimeSpan? documentEnd)
    {
        int index = LyricTiming.DisplayLineAt(lines, at, documentEnd);
        string text = index < 0 ? "" : lines[index].Text.Trim();
        if (Lyric.IsWordless(text)) return ("", 0, 0);

        TimeSpan start = lines[index].Time;
        TimeSpan? end = index == lines.Length - 1 ? documentEnd : LyricTiming.EndFor(lines, index, source, duration);
        double seconds = end is { } until ? (until.Ticks - (double)start.Ticks) / TimeSpan.TicksPerSecond : 0;
        double elapsed = seconds > 0 ? Math.Clamp((at.Ticks - (double)start.Ticks) / TimeSpan.TicksPerSecond, 0, seconds) : 0;
        return (text, elapsed, seconds);
    }
}
