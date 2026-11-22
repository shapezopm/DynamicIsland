using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DynamicIsland;

public partial class MainWindow
{
    static readonly Brush DiscordMusicSolid = Brushes.Black;
    static readonly Brush DiscordMusicGlass = new SolidColorBrush(Color.FromArgb(148, 15, 15, 18));
    const double DiscordMiniWidth = 34, DiscordCompactWidth = 146, DiscordMusicDragThreshold = 16;
    static readonly Thickness CompactMusicInfoMargin = new(12, 0, 12, 0);
    static readonly Thickness NormalMusicInfoMargin = new(34, 0, 34, 0);
    static readonly Thickness LyricMusicInfoMargin = new(34, 2, 34, 0);
    readonly Spring _discordMusicWidth = new(DiscordCompactWidth, 280, 28);
    readonly Spring _discordMusicX = new(0, 280, 28), _discordMusicY = new(0, 280, 28);
    readonly Spring _discordMusicOpacity = new(0, 280, 28);
    Point? _discordMusicDragStart;
    CompactLyric? _discordLyric;
    int _discordLyricIndex = -1;
    LyricsService.Line[]? _discordLyricLines;
    double _discordLyricWidth;
    TimeSpan? _discordLyricEnd;

    bool WantsDiscordMusic => ShowsVoice && Settings.DiscordMusicBubble && IsMediaActive && _view == View.Voice;
    bool DiscordNormalMusic => Settings.DiscordMusicState == DiscordMusicState.Normal;

    void SyncDiscordMusic()
    {
        _discordMusicOpacity.Target = WantsDiscordMusic ? 1 : 0;
        DiscordMusicTitle.Text = _media.Name;
        DiscordMusicArtist.Text = _media.Artist;
        UpdateDiscordMusicLyrics();
        StartShapeLoop();
    }

    void UpdateDiscordMusicLyrics(bool snap = false)
    {
        if (!WantsDiscordMusic || !DiscordNormalMusic)
        {
            _discordLyricIndex = -1;
            _discordLyricLines = null;
            return;
        }

        LyricsResult result = CurrentLyricsResult();
        LyricsService.Line[] lines = Settings.Lyrics && result.Status == LyricsStatus.Synced ? result.Lines : [];
        double duration = result.Candidate?.Duration ?? 0;
        lines = TrimmedOutro(lines, duration, out _, out var final);
        int index = _media.IsPlaying ? final.LineAt(lines, LyricTime) : -1;
        if (!snap && index == _discordLyricIndex && ReferenceEquals(lines, _discordLyricLines)) return;
        _discordLyricIndex = index;
        _discordLyricLines = lines;
        if (index >= 0)
        {
            CompactLyric next = _discordLyric == DiscordLyricA ? DiscordLyricB : DiscordLyricA;
            next.Sweeps = true;
            ApplyCompactLyricFx(next);
            next.Show(lines[index].Text, Brushes.White, double.PositiveInfinity, Settings.LyricChange);
            next.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _discordLyricWidth = next.DesiredSize.Width;
            _discordLyricEnd = TimedLineEnd(lines, index, final, duration);
            Canvas.SetTop(next, Math.Round((DiscordMusicLyrics.Height - next.DesiredSize.Height) / 2));
            next.Enter(snap);
            _discordLyric?.Leave(snap);
            _discordLyric = next;
            StirLyrics();
        }
        else _discordLyric?.Leave(snap);
        StartShapeLoop();
    }

    bool AdvanceDiscordMusicLyrics(double dt)
    {
        if (!WantsDiscordMusic || !DiscordNormalMusic || _discordLyric is not { } line
            || _discordLyricLines is not { } lines || _discordLyricIndex < 0) return false;
        TimeSpan now = LyricTime;
        TimeSpan start = lines[_discordLyricIndex].Time;
        if (_discordLyricEnd is { } end && end > start)
            line.Progress = Math.Clamp((now - start).TotalSeconds / (end - start).TotalSeconds, 0, 1);
        Point? pointer = Settings.LyricParting && DiscordMusicPill.IsMouseOver ? Mouse.GetPosition(line) : null;
        return line.Part(pointer, dt, _media.IsPlaying) || _media.IsPlaying;
    }

    void PlaceDiscordMusic(Rect pill, double scale)
    {
        bool visible = _discordMusicOpacity.Value > 0.01 && !HiddenByFullscreen && !_sentAway;
        DiscordMusicPill.SetVisible(visible);
        DiscordMusicPill.IsHitTestVisible = WantsDiscordMusic && _discordMusicOpacity.Value > 0.75;
        if (!visible) return;
        DiscordMusicPill.Background = Settings.Glass && _glass.IsOn ? DiscordMusicGlass : DiscordMusicSolid;
        bool mini = Settings.DiscordMusicState == DiscordMusicState.Mini;
        bool normal = DiscordNormalMusic;
        bool compact = !mini && !normal;
        bool lyrics = normal && _discordLyricIndex >= 0;
        _discordMusicWidth.Target = normal
            ? Math.Clamp(lyrics ? _discordLyricWidth + 84 : CompactMediaWidth, CompactMediaWidth, MaxCompactMediaWidth)
            : mini ? DiscordMiniWidth : DiscordCompactWidth;
        double width = Math.Max(DiscordMiniWidth, _discordMusicWidth.Value);
        double neighbor = Bubble.IsVisible
            ? BubbleMove.X + (BubbleScale.ScaleX - 0.5) * Bubble.Width
            : pill.Width * scale / 2;

        double right = neighbor + BubbleGap + _discordMusicWidth.Target;
        bool below = normal || right > HostWidth / 2;
        if (compact && !below && IsLoaded)
        {
            Point center = Root.PointToScreen(new Point(HostWidth / 2, 0));
            Point edge = Root.PointToScreen(new Point(HostWidth / 2 + right, 0));
            Rect workArea = Native.WorkAreaAt(center);
            below = !workArea.IsEmpty && edge.X > workArea.Right;
        }
        _discordMusicX.Target = below ? 0 : neighbor + BubbleGap + width / 2;
        _discordMusicY.Target = below ? pill.Height * scale + BubbleGap : 0;
        DiscordMusicMove.X = _discordMusicX.Value;
        DiscordMusicMove.Y = _discordMusicY.Value;
        DiscordMusicPill.Width = width;
        DiscordMusicPill.Opacity = Math.Clamp(_discordMusicOpacity.Value, 0, 1);
        DiscordMusicPill.Clip = Squircle.Of(new Rect(0, 0, width, 34), 17);
        DiscordMusicGlow.Width = width;
        DiscordMusicCover.SetVisible(!compact);
        DiscordMusicGlow.SetVisible(!compact);
        DiscordMusicEq.SetVisible(normal);
        DiscordMusicLyrics.SetVisible(lyrics);
        DiscordMusicInfo.SetVisible(!mini);
        DiscordMusicInfo.Margin = compact ? CompactMusicInfoMargin : lyrics ? LyricMusicInfoMargin : NormalMusicInfoMargin;
        DiscordMusicInfo.VerticalAlignment = lyrics ? VerticalAlignment.Top : VerticalAlignment.Center;
        DiscordMusicArtist.SetVisible(normal && !lyrics);
        if (lyrics && _discordLyric is { } line)
            line.Offset.X = 0;
    }

    void DiscordMusicDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (DiscordMusicPill.CaptureMouse()) _discordMusicDragStart = e.GetPosition(Root);
    }

    void DiscordMusicCaptureLost(object sender, MouseEventArgs e) => _discordMusicDragStart = null;

    void DiscordMusicUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_discordMusicDragStart is not Point start) return;
        Vector delta = e.GetPosition(Root) - start;
        DiscordMusicPill.ReleaseMouseCapture();
        if (!WantsDiscordMusic) return;

        if (Math.Abs(delta.X) >= DiscordMusicDragThreshold && Math.Abs(delta.X) > Math.Abs(delta.Y))
        {
            int current = (int)Settings.DiscordMusicState;
            int next = Math.Clamp(current + Math.Sign(delta.X), (int)DiscordMusicState.Mini, (int)DiscordMusicState.Normal);
            if (next != current) Settings.DiscordMusicState = (DiscordMusicState)next;
        }
        else if (delta.Length < DiscordMusicDragThreshold)
            SelectPanel(Panel.Player);
        UpdateView();
        UpdateTargets();
    }
}
