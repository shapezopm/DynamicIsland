using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public partial class MainWindow
{
    const double CompactMediaWidth = 210;
    const double MaxCompactMediaWidth = 440;
    const double LyricInset = 77;
    const double LyricFadeEdge = 8;
    const double LyricScrollSpeed = 36;
    const double PlayerHeight = 176;
    const double PlayerLyricsHeight = 118;
    const double PlayerLineGap = 4;
    const double UnsungLineOpacity = 0.4;
    const double UnsungLineScale = 0.94;
    const double UnsungLineBlur = 1.5;
    const double UnsungPartOpacity = 0.6;
    const double LineScrollStagger = 0.04;
    const int VisibleLineRange = 4, BlurredLineRange = 2;
    static readonly TimeSpan LyricBuildBudget = TimeSpan.FromMilliseconds(3);

    sealed class PlayerLine(Lyric row, double middle)
    {
        public Lyric Row { get; } = row;
        public double Middle { get; } = middle;
        public Spring Y { get; } = new(0, 170, 22);
        public double DueAt { get; set; }
    }

    sealed class PlayerLyricsBuild(LyricsService.Line[] lines, int count)
    {
        public LyricsService.Line[] Lines { get; } = lines;
        public PlayerLine[] Rows { get; } = new PlayerLine[count];
        public int Next;
        public double Top;
    }

    readonly BitmapCache _lyricCache = new();
    readonly FrameLoop _partLoop;
    LyricsService.Line[] _lyricLines = [];
    LyricTiming.FinalLine _compactFinal;
    bool _compactLyricsFrozen;
    int _lyricIndex = -1;
    string _lyricTitle = "";
    bool _lyricShowsTitle;
    double _compactMediaWidth = CompactMediaWidth;
    CompactLyric _lyricBlock;
    LyricsService.Line[] _playerLines = [];
    LyricTiming.FinalLine _playerFinal;
    bool _playerLyricsFrozen;
    PlayerLine[] _playerRows = [];
    PlayerLyricsBuild? _playerLyricsBuild;
    double _playerScroll;
    bool _playerScrolling;
    readonly int[] _playerAnimatingRows = new int[(2 * VisibleLineRange + 1) * 2];
    int _playerAnimatingCount;
    double _compactLyricOverflow;
    readonly LyricsOffsetStore _lyricOffsets = new();
    TrackSnapshot? _lyricOffsetTrack;
    LyricsCandidate? _lyricOffsetCandidate;
    string? _lyricOffsetKey;
    int _lyricAdvanceMs;
    int _playerIndex = -1;
    bool _playerHasLyricRoom;
    bool _playerLyricsPending;
    LyricsResult? _playerPresentationResult;
    LyricsPresentation? _playerPresentation;
    TrackSnapshot? _playerPresentationTrack;
    LyricsCandidate? _playerDocument;
    LyricsCandidate? _fullTextCandidate;
    LyricsService.Line[]? _fullTextLines;
    string? _fullTextPlain;
    string _fullText = "";
    bool _playerFullText;

    TimeSpan LyricTime => LyricTiming.LookupTime(_media.Position, _lyricAdvanceMs);
    Color CompactTrackColor => Settings.CompactTrackAccent is { } color
        ? color == Colors.White ? color.WithAlpha(0.93) : color
        : _media.Accent;

    void SyncCompactTrackAccent()
    {
        if (_lyricShowsTitle) _lyricBlock.SetCaptionColor(CompactTrackColor);
    }

    void TrackLyrics() => _lyrics.Track(_media.Snapshot, Settings.Lyrics);

    void OnLyricsChanged()
    {
        if (_lyricsEditorOpen) KeepLyricsEditorOnTrack();
        SyncLyricAdvance();
        TimeSpan estimate = _lyrics.UsualLength;
        if (!_lyrics.IsPending && estimate > TimeSpan.Zero && _lyrics.Result.Track is { } track)
            _media.AssumeDuration(estimate, track);
        RefreshLyricsPresentation();
        UpdateLyric();
    }

    void LyricBox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double width = Math.Max(e.NewSize.Width, 2 * LyricFadeEdge);
        LyricMask.EndPoint = new Point(width, 0);
        LyricMaskIn.Offset = LyricFadeEdge / width;
        LyricMaskOut.Offset = 1 - LyricFadeEdge / width;
    }

    void UpdateLyric(bool snap = false)
    {
        if (HoldingCallTrack) return;
        if (_view == View.MediaBig) UpdatePlayerLyrics(snap);
        if (_view != View.Media) return;

        LyricsResult current = CurrentLyricsResult();
        double candidateDuration = current.Candidate?.Duration ?? 0;
        LyricsService.Line[] lines = TrimmedOutro(!CallMediaTitleOnly && !CallMediaMini && current.Status == LyricsStatus.Synced ? current.Lines : [],
            candidateDuration, out _, out _compactFinal);
        TimeSpan at = LyricTime;
        bool frozen = _compactFinal.IsComplete(at);
        int index = frozen ? -1 : _compactFinal.LineAt(lines, at);
        bool wasFrozen = _compactLyricsFrozen;
        bool titleOnly = CallMediaTitleOnly;
        bool sameLine = _lyricBlock.IsEntered && ReferenceEquals(lines, _lyricLines) && index == _lyricIndex
            && titleOnly == _compactCaptionTitleOnly;
        if (frozen && wasFrozen && sameLine) return;
        _compactLyricsFrozen = frozen;

        string title = _media.HasTrack ? _media.Name : "";
        string artist = _media.Artist?.Trim() ?? "";
        if (!CallMediaTitleOnly && title.Length > 0 && artist.Length > 0) title = $"{artist} - {title}";
        string text = index < 0 ? "" : lines[index].Text;
        TimeSpan? end = TimedLineEnd(lines, index, _compactFinal, candidateDuration);
        double seconds = end is { } until ? (until.Ticks - (double)at.Ticks) / TimeSpan.TicksPerSecond : 0;
        (string shown, bool named) = CompactCaption(text, title);
        if (!sameLine || named != _lyricShowsTitle || named && title != _lyricTitle)
        {
            if (!snap && !frozen && !wasFrozen && _lyricIndex >= 0 && ReferenceEquals(lines, _lyricLines)
                && at >= TimedLineEnd(_lyricLines, _lyricIndex, _compactFinal, candidateDuration))
                _lyricBlock.CompleteShimmer();
            ShowLyric(shown, named ? double.PositiveInfinity : seconds, snap || frozen || wasFrozen, named,
                immediate: frozen || wasFrozen);
        }

        _lyricLines = lines;
        _lyricIndex = index;
        _lyricTitle = title;
        _compactCaptionTitleOnly = titleOnly;
        if (frozen)
            (_lyricBlock == LyricA ? LyricB : LyricA).StopTransitions(visible: false);
        else
        {
            UpdateCompactLyricOffset(at, snap: wasFrozen);
            if (!named && _media.IsPlaying) StirLyrics();
        }
    }

    internal static (string Text, bool Named) CompactCaption(string lyric, string title) =>
        string.IsNullOrWhiteSpace(lyric) ? (title, true) : (lyric, false);

    bool _compactCaptionTitleOnly;

    void ShowLyric(string text, double seconds, bool snap, bool named, bool immediate)
    {
        CompactLyric old = _lyricBlock;
        if (old.IsEntered && text == old.Text && _compactCaptionTitleOnly == CallMediaTitleOnly
            && (text.Length == 0 || (named && _lyricShowsTitle))) return;
        CompactLyric next = old == LyricA ? LyricB : LyricA;

        // Prepare the replacement before hiding the caption that is still visible.
        next.Sweeps = !named;
        ApplyCompactLyricFx(next);
        next.Show(text, named ? new SolidColorBrush(CompactTrackColor) : Brushes.White, double.PositiveInfinity, Settings.LyricChange);
        next.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = next.DesiredSize.Width;
        Canvas.SetTop(next, Math.Round((LyricBox.Height - next.DesiredSize.Height) / 2));

        double compactWidth = text.Length == 0
            ? CompactMediaWidth
            : Math.Clamp(width + 2 * LyricFadeEdge + LyricInset, CompactMediaWidth, MaxCompactMediaWidth);
        next.IslandWidth = CallMediaTitleOnly ? CallMediaTitleWidth : compactWidth;
        CentreLyrics();
        double box = next.IslandWidth - LyricInset;
        double overflow = width - (box - 2 * LyricFadeEdge);
        DoubleAnimationUsingKeyFrames? scroll = named && overflow > 0 ? CreateLyricScroll(overflow, seconds) : null;

        next.Offset.X = overflow > 0 ? LyricFadeEdge : (box - width) / 2;
        next.Enter(immediate);
        next.Offset.BeginAnimation(TranslateTransform.XProperty, scroll);
        old.Leave(snap);
        _lyricBlock = next;
        _lyricShowsTitle = named;
        _compactMediaWidth = compactWidth;
        _compactLyricOverflow = Math.Max(0, overflow);
        if (!snap) _width.Tune(280, 30);
        UpdateTargets();
        if (!_compactLyricsFrozen) StirLyrics();
    }

    // Эффекты текста в мини-острове: тот же стиль, что и в развёрнутом (параметры эффектов фиксированы).
    void ApplyCompactLyricFx(CompactLyric line)
    {
        line.TextEffect = line.Sweeps ? Settings.TextEffect : LyricFx.Off;
    }

    void CentreLyrics()
    {
        MediaView.UpdateLayout();
        double boxLeft = VisualTreeHelper.GetOffset(MediaView).X + VisualTreeHelper.GetOffset(LyricBox).X;
        LyricA.Centring.X = (HostWidth - LyricA.IslandWidth) / 2 + LyricBox.Margin.Left - boxLeft;
        LyricB.Centring.X = (HostWidth - LyricB.IslandWidth) / 2 + LyricBox.Margin.Left - boxLeft;
    }

    void AdvanceCompactLyric()
    {
        if (_view != View.Media || _compactLyricsFrozen || _lyricShowsTitle || !_lyricBlock.IsEntered || _lyricLines.Length == 0) return;
        TimeSpan at = LyricTime;
        if (_compactFinal.IsComplete(at) || _compactFinal.LineAt(_lyricLines, at) != _lyricIndex) UpdateLyric();
        else UpdateCompactLyricOffset(at);
    }

    void UpdateCompactLyricOffset(TimeSpan at, bool snap = false)
    {
        if (_lyricShowsTitle || _lyricIndex < 0 || _lyricIndex >= _lyricLines.Length) return;
        TimeSpan start = _lyricLines[_lyricIndex].Time;
        TimeSpan? end = TimedLineEnd(_lyricLines, _lyricIndex, _compactFinal, _lyrics.Result.Candidate?.Duration ?? 0);
        double progress = LyricTiming.FillProgress(at, start, end);
        if (snap) _lyricBlock.SnapProgress(progress);
        else _lyricBlock.Progress = progress;
        if (_compactLyricOverflow <= 0) return;
        double x = LyricTiming.ScrollOffset(_compactLyricOverflow, LyricFadeEdge, at, start, end);
        if (Math.Abs(_lyricBlock.Offset.X - x) > 0.001) _lyricBlock.Offset.X = x;
    }

    TimeSpan? TimedLineEnd(LyricsService.Line[] lines, int index, LyricTiming.FinalLine final, double candidateDuration) =>
        index == final.Index || index == lines.Length - 1 ? final.End
            : LyricTiming.EndFor(lines, index, _media.SourceDuration, candidateDuration);

    void SyncLyricAdvance()
    {
        LyricsResult result = CurrentLyricsResult();
        RefreshLyricsSourceButton(result);
        if (ReferenceEquals(result.Candidate, _lyricOffsetCandidate)
            && (result.Track is null ? _lyricOffsetTrack is null : result.Track.SameIdentity(_lyricOffsetTrack))) return;
        _lyricOffsetTrack = result.Track;
        _lyricOffsetCandidate = result.Candidate;
        string? key = result.Track is { } track && result.Candidate?.HasValidSync == true
            ? LyricsOffsetStore.Key(track, result.Candidate) : null;
        if (key == _lyricOffsetKey) return;
        _lyricOffsetKey = key;
        _lyricAdvanceMs = key is null ? 0 : _lyricOffsets.Read(key);
    }

    void ChangeLyricAdvance(int difference, bool reset = false)
    {
        SyncLyricAdvance();
        if (_lyricOffsetKey is null) return;
        _lyricAdvanceMs = _lyricOffsets.Set(_lyricOffsetKey, reset ? 0 : _lyricAdvanceMs + difference);
        UpdateLyric(snap: true);
        MarkLineStarts(_media.Duration);
        FillSungLine();
    }

    internal static DoubleAnimationUsingKeyFrames CreateLyricScroll(double overflow, double seconds)
    {
        overflow = double.IsFinite(overflow) ? Math.Max(overflow, 0) : 0;
        double naturalRun = Math.Min(overflow / LyricScrollSpeed, 60);
        // Missing duration and an expired final timestamp are normal playback states.
        // Give them a finite reading window instead of creating negative key times.
        double budget = double.IsFinite(seconds) && seconds > 0 ? Math.Min(seconds, 60.6) : naturalRun + 0.6;
        double hold = Math.Min(0.6, budget * 0.2);
        double run = Math.Min(naturalRun, Math.Max(budget - hold - 0.5, 0.6));
        return Delayed(LyricFadeEdge, LyricFadeEdge - overflow, TimeSpan.FromSeconds(hold), TimeSpan.FromSeconds(run),
            new SineEase { EasingMode = EasingMode.EaseInOut });
    }

    void UpdatePlayerLyrics(bool snap)
    {
        LyricsPresentation presentation = RefreshLyricsPresentation();
        SyncLyricAdjust(presentation);
        double candidateDuration = _lyrics.Result.Candidate?.Duration ?? 0;
        LyricsService.Line[] lines = TrimmedOutro(VisibleLyricsLines(presentation, _playerFullText),
            candidateDuration, out TimeSpan? documentEnd, out _playerFinal);
        bool documentChanged = !ReferenceEquals(lines, _playerLines);
        _playerLines = lines;

        bool room = presentation.ShowArea;
        if (room != _playerHasLyricRoom)
        {
            _playerHasLyricRoom = room;
            _height.Tune(280, 30);
            UpdateTargets();
        }
        TimeSpan at = LyricTime;
        if (lines.Length == 0 || _playerFinal.Index < 0 && at >= documentEnd)
        {
            ClearPlayerLyricsRows();
            return;
        }

        int count = _playerFinal.Index >= 0 ? Math.Min(lines.Length, _playerFinal.Index + 1) : lines.Length;
        if (documentChanged || _playerRows.Length != count)
        {
            if (_playerLyricsBuild is not { } pending || !ReferenceEquals(pending.Lines, lines) || pending.Rows.Length != count)
                QueuePlayerLyricsBuild(lines, count);
            return;
        }
        ShowLyricsPlaceholder(false);

        bool frozen = _playerFinal.IsComplete(at);
        bool changedMode = frozen != _playerLyricsFrozen;
        _playerLyricsFrozen = frozen;
        int index = _playerFinal.LineAt(lines, at);
        if (frozen && !changedMode && index == _playerIndex && !snap) return;
        snap |= changedMode;
        bool lineLive = !frozen && index >= 0 && (index == _playerFinal.Index
            || at < TimedLineEnd(lines, index, _playerFinal, candidateDuration));
        if (index >= 0)
            _playerRows[index].Row.SetPlaybackState(current: true, active: lineLive && _media.IsPlaying);
        if (!frozen && (changedMode || lineLive && _media.IsPlaying && Settings.TextEffect is (LyricFx.Lift or LyricFx.Glow)))
            StirLyrics();
        if (index == _playerIndex && !snap) return;

        Duration fade = Ms(snap ? 0 : 300);
        int was = _playerIndex;
        _playerIndex = index;
        int oldFirst = Math.Max(0, was - BlurredLineRange), oldLast = Math.Min(_playerRows.Length - 1, was + BlurredLineRange);
        int newFirst = Math.Max(0, index - BlurredLineRange), newLast = Math.Min(_playerRows.Length - 1, index + BlurredLineRange);
        if (frozen)
        {
            PlayerLyricLines.BeginAnimation(OpacityProperty, null);
            PlayerLyricLines.Opacity = 1;
            for (int i = 0; i < _playerRows.Length; i++) UpdateRow(i);
            _playerRows[index].Row.Progress = 1;
        }
        else
        {
            for (int i = oldFirst; i <= oldLast; i++) UpdateRow(i);
            for (int i = newFirst; i <= newLast; i++) if (i < oldFirst || i > oldLast) UpdateRow(i);
            FillSungLine();
        }
        void UpdateRow(int i)
        {
            Lyric row = _playerRows[i].Row;
            if (frozen) row.StopParting();
            if (i == was && i != index) row.SetPlaybackState(current: false, active: false);
            if (frozen || i == index || i == was) HighlightLine(row, i == index, fade);
            if (i != index && Math.Abs(i - index) <= BlurredLineRange && Settings.LyricEffects) BlurLine(row, UnsungLineBlur, fade);
            else if (i == index || frozen) BlurLine(row, 0, fade);
            else row.Effect = null;
        }
        double middle = _playerRows[Math.Max(index, 0)].Middle;
        ScrollPlayerLyrics(PlayerTimedViewport.Height / 2 - middle, Math.Max(was, 0), Math.Max(index, 0), snap);
    }

    LyricsResult CurrentLyricsResult() => LyricsPresentation.IsCurrent(_lyrics.Result, _media.Snapshot)
        ? _lyrics.Result : LyricsResult.Empty;

    LyricsPresentation RefreshLyricsPresentation()
    {
        LyricsResult result = CurrentLyricsResult();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool deadlineChanged = result.Status == LyricsStatus.RateLimited && _playerPresentation is { } previous
            && previous.RetryEnabled != (result.RetryAt is null || result.RetryAt <= now);
        if (!ReferenceEquals(result, _playerPresentationResult) || deadlineChanged)
        {
            bool trackChanged = result.Track is null ? _playerPresentationTrack is not null
                : !result.Track.SameIdentity(_playerPresentationTrack);
            bool documentChanged = trackChanged || !ReferenceEquals(result.Candidate, _playerDocument);
            if (documentChanged)
            {
                _playerFullText = false;
                PlayerPlainScroll.ScrollToTop();
            }
            // Position/duration updates create a new result but retain the document.
            // Joining thousands of lyric lines belongs to document changes, not playback ticks.
            LyricsService.Line[]? documentLines = result.Status == LyricsStatus.Plain ? null
                : result.Candidate?.HasValidSync == true ? result.Candidate.Lines : result.Lines;
            if (trackChanged || !ReferenceEquals(result.Candidate, _fullTextCandidate)
                || !ReferenceEquals(documentLines, _fullTextLines) || !ReferenceEquals(result.Candidate?.PlainLyrics, _fullTextPlain))
            {
                _fullText = LyricsPresentation.TextForResult(result);
                if (_fullText.Length > 0 && _lyricsEdit is { } edit
                    && edit.Matches(result.Track, LyricsDocumentIdentity.Key(result.Candidate))) _fullText = edit.Text;
                _fullTextCandidate = result.Candidate;
                _fullTextLines = documentLines;
                _fullTextPlain = result.Candidate?.PlainLyrics;
            }
            _playerPresentation = LyricsPresentation.FromResult(result, now, _fullText, Settings.UnsyncedLyrics);
            _playerPresentationResult = result;
            _playerPresentationTrack = result.Track;
            _playerDocument = result.Candidate;
            ApplyLyricsPresentation(_playerPresentation, _playerFullText, PresentationControls());
            ShowEditorChip(_playerPresentation);
            if (!_playerPresentation.ShowArea)
            {
                _playerLines = [];
                _playerFinal = default;
                ClearPlayerLyricsRows();
            }
        }
        LyricsPresentation presentation = _playerPresentation ?? LyricsPresentation.FromResult(LyricsResult.Empty, now);
        return presentation;
    }

    internal sealed record LyricsPresentationControls(FrameworkElement Host, FrameworkElement Timed,
        ScrollViewer Scroll, TextBlock Text, TextBlock Status, TextBlock Message, TextBlock Source,
        Button Retry, Button Full, Button? CompactRetry = null);

    LyricsPresentationControls PresentationControls() => new(PlayerLyricBox, PlayerTimedViewport, PlayerPlainScroll,
        PlayerPlainText, PlayerLyricStatus, PlayerLyricMessage, PlayerLyricSource, PlayerLyricRetryAction, PlayerLyricFullAction,
        PlayerLyricRetryCompact);

    internal static LyricsService.Line[] VisibleLyricsLines(LyricsPresentation presentation, bool fullTextMode)
        => presentation.ShowTimed && !fullTextMode ? presentation.StableLines : [];

    internal static void ApplyLyricsPresentation(LyricsPresentation presentation, bool fullTextMode, LyricsPresentationControls controls)
    {
        bool full = presentation.CanShowFullText && fullTextMode;
        bool ordinary = presentation.ShowText || full;
        controls.Host.Visibility = presentation.ShowArea ? Visibility.Visible : Visibility.Collapsed;
        controls.Host.IsHitTestVisible = presentation.ShowArea;
        controls.Timed.Visibility = presentation.ShowTimed && !full ? Visibility.Visible : Visibility.Collapsed;
        controls.Scroll.Visibility = ordinary ? Visibility.Visible : Visibility.Collapsed;
        controls.Scroll.IsHitTestVisible = ordinary;
        // Keep manual scroll on timing-only updates. Text assignments reset WPF's layout/scroll extent.
        string text = ordinary ? presentation.FullText : "";
        if (controls.Text.Text != text) controls.Text.Text = text;
        controls.Status.Text = full ? "Полный текст" : presentation.StatusText;
        controls.Status.ToolTip = presentation.StatusText;
        controls.Message.Text = presentation.Message;
        controls.Message.Visibility = !presentation.IsSearching && !ordinary && !presentation.ShowTimed
            && presentation.ShowArea ? Visibility.Visible : Visibility.Collapsed;
        // Attribution stays separate from the playback application and timing quality.
        string sourceLine = presentation.Credits.Length == 0 ? presentation.Attribution
            : presentation.Attribution.Length == 0 ? presentation.Credits
            : presentation.Credits + " · " + presentation.Attribution;
        controls.Source.Text = sourceLine;
        controls.Source.ToolTip = null;
        controls.Source.Visibility = sourceLine.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        controls.Retry.Visibility = Visibility.Collapsed;
        controls.Retry.IsEnabled = presentation.RetryEnabled;
        controls.Retry.ToolTip = presentation.RetryTooltip;
        if (controls.CompactRetry is { } compact)
        {
            compact.Visibility = Visibility.Collapsed;
            compact.IsEnabled = presentation.RetryEnabled;
            compact.ToolTip = presentation.RetryTooltip;
        }
        controls.Full.Visibility = presentation.CanShowFullText ? Visibility.Visible : Visibility.Collapsed;
        controls.Full.Content = full ? "К строкам" : "Полный текст";
    }

    void PlayerPlainScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Native.IsCtrlDown) { Root_MouseWheel(sender, e); return; }
        HandleLyricsMouseWheel((ScrollViewer)sender, e);
    }

    void PlayerPlainScroll_MouseButton(object sender, MouseButtonEventArgs e) => HandleLyricsMouseButton(e);

    internal static void HandleLyricsMouseButton(MouseButtonEventArgs e) => e.Handled = true;

    internal static void HandleLyricsMouseWheel(ScrollViewer scroll, MouseWheelEventArgs e)
    {
        if (scroll.Visibility != Visibility.Visible) return;
        scroll.ScrollToVerticalOffset(Math.Clamp(scroll.VerticalOffset - e.Delta / 120d * 36, 0, scroll.ScrollableHeight));
        // Consume wheel at both bounds too; it must not fall through to the island volume control.
        e.Handled = true;
    }

    void ScrollPlayerLyrics(double to, int was, int index, bool snap)
    {
        bool up = to <= _playerScroll;
        int lead = index + (up ? -1 : 1);
        double now = _clock.Elapsed.TotalSeconds;
        _playerScroll = to;
        _playerAnimatingCount = 0;
        _playerScrolling = false;
        if (!snap) StirLyrics();
        for (int i = 0; i < _playerRows.Length; i++)
        {
            PlayerLine line = _playerRows[i];
            if (snap || (Math.Abs(i - index) > VisibleLineRange && Math.Abs(i - was) > VisibleLineRange))
            {
                line.Y.Snap(to);
                line.DueAt = 0;
                LineOffsetOf(line.Row).Y = to;
                continue;
            }
            int behind = Math.Clamp(up ? i - lead : lead - i, 0, VisibleLineRange + 1);
            line.DueAt = now + behind * LineScrollStagger;
            _playerAnimatingRows[_playerAnimatingCount++] = i;
            _playerScrolling = true;
        }
    }

    void AdvancePlayerLyrics(double now, double dt)
    {
        if (!_playerScrolling) return;
        bool rolling = false;
        for (int active = 0; active < _playerAnimatingCount; active++)
        {
            PlayerLine line = _playerRows[_playerAnimatingRows[active]];
            Spring y = line.Y;
            if (line.DueAt > 0)
            {
                if (now < line.DueAt) rolling = true;
                else
                {
                    line.DueAt = 0;
                    y.Target = _playerScroll;
                }
            }
            if (y.Value == y.Target && y.Velocity == 0) continue;
            rolling |= y.Advance(dt);
            LineOffsetOf(line.Row).Y = y.Value;
        }
        _playerScrolling = rolling;
        if (!rolling) _playerAnimatingCount = 0;
    }

    void StirLyrics() => _partLoop.Start();

    bool AdvanceLyricParting(double dt)
    {
        // Прогресс мини-строки обновляем каждый кадр: раньше он шёл из EQ-цикла (около 30 к/с) и давал рывки.
        AdvanceCompactLyric();
        bool lyricLive = !_compactLyricsFrozen && _media.IsPlaying && _view == View.Media && !_lyricShowsTitle
            && _lyricBlock.IsEntered && _lyricLines.Length > 0;
        bool parting = _hovered && Settings.LyricParting;
        bool overPlayer = parting && _view == View.MediaBig, overCompact = parting && _view == View.Media;
        bool moving = overPlayer && _playerScrolling;
        int sung = Math.Max(_playerIndex, 0);
        for (int i = Math.Max(0, sung - VisibleLineRange);
             !_playerLyricsFrozen && i < _playerRows.Length && i <= sung + VisibleLineRange; i++)
        {
            Lyric row = _playerRows[i].Row;
            moving |= row.Part(overPlayer ? Mouse.GetPosition(row) : null, dt, _view == View.MediaBig && _media.IsPlaying);
        }
        if (!_compactLyricsFrozen)
        {
            moving |= LyricA.Part(overCompact && LyricA == _lyricBlock ? Mouse.GetPosition(LyricA) : null, dt,
                _view == View.Media && _media.IsPlaying);
            moving |= LyricB.Part(overCompact && LyricB == _lyricBlock ? Mouse.GetPosition(LyricB) : null, dt,
                _view == View.Media && _media.IsPlaying);
        }
        return moving || lyricLive;
    }

    static ScaleTransform LineScaleOf(Lyric row) => (ScaleTransform)((TransformGroup)row.RenderTransform).Children[0];

    static TranslateTransform LineOffsetOf(Lyric row) => (TranslateTransform)((TransformGroup)row.RenderTransform).Children[1];

    void ShowLyricsPlaceholder(bool on)
    {
        PlayerLyricWait.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on == _playerLyricsPending) return;
        _playerLyricsPending = on;
        PlayerLyricWait.SetRunning(on);
        if (!on)
        {
            PlayerLyricWait.BeginAnimation(OpacityProperty, null);
            PlayerLyricWait.Opacity = 0;
            return;
        }
        var fade = new DoubleAnimation(on ? 1 : 0, Ms(on ? 300 : 200));
        PlayerLyricWait.BeginAnimation(OpacityProperty, fade);
    }

    void RebuildPlayerLyrics()
    {
        ApplyCompactLyricFx(LyricA);
        ApplyCompactLyricFx(LyricB);
        ClearPlayerLyricsRows();
        if (_view == View.MediaBig) UpdatePlayerLyrics(true);
    }

    void ClearPlayerLyricsRows()
    {
        _playerLyricsBuild = null;
        ShowLyricsPlaceholder(false);
        _playerIndex = -1;
        _playerLyricsFrozen = false;
        _playerScrolling = false;
        _playerAnimatingCount = 0;
        _playerRows = [];
        if (PlayerLyricLines.Children.Count == 0) return;
        PlayerLyricLines.BeginAnimation(OpacityProperty, null);
        PlayerLyricLines.Children.Clear();
    }

    void QueuePlayerLyricsBuild(LyricsService.Line[] lines, int count)
    {
        ClearPlayerLyricsRows();
        var build = _playerLyricsBuild = new PlayerLyricsBuild(lines, count);
        PlayerLyricLines.Opacity = 0;
        ShowLyricsPlaceholder(true);
        SchedulePlayerLyricsBuild(build);
    }

    void SchedulePlayerLyricsBuild(PlayerLyricsBuild build) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => BuildPlayerLyricsSlice(build));

    void BuildPlayerLyricsSlice(PlayerLyricsBuild build)
    {
        if (!ReferenceEquals(_playerLyricsBuild, build)) return;
        long started = Stopwatch.GetTimestamp();
        double width = PlayerTimedViewport.Width, size = Settings.LyricEffects ? UnsungLineScale : 1;
        do
        {
            int i = build.Next++;
            var row = new Lyric(build.Lines[i].Text)
            {
                Width = width,
                Opacity = UnsungLineOpacity,
                TextEffect = Settings.TextEffect,
                RenderTransformOrigin = new Point(0, 0.5),
                RenderTransform = new TransformGroup { Children = { new ScaleTransform(size, size), new TranslateTransform() } },
            };
            PlayerLyricLines.Children.Add(row);
            row.Measure(new Size(width, double.PositiveInfinity));
            Canvas.SetTop(row, build.Top);
            build.Rows[i] = new PlayerLine(row, build.Top + row.DesiredSize.Height / 2);
            build.Top += row.DesiredSize.Height + PlayerLineGap;
        } while (build.Next < build.Rows.Length && Stopwatch.GetElapsedTime(started) < LyricBuildBudget);

        if (build.Next < build.Rows.Length)
        {
            SchedulePlayerLyricsBuild(build);
            return;
        }
        _playerRows = build.Rows;
        _playerLyricsBuild = null;
        UpdatePlayerLyrics(true);
        if (_playerLyricsBuild is null) PlayerLyricLines.Opacity = 1;
    }

    void HighlightLine(Lyric row, bool sung, Duration time)
    {
        bool effects = Settings.LyricEffects;
        double opacity = sung ? 1 : UnsungLineOpacity;
        double unsung = sung && effects ? UnsungPartOpacity : 1;
        double size = sung || !effects ? 1 : UnsungLineScale;
        ScaleTransform scale = LineScaleOf(row);
        if (time.TimeSpan == TimeSpan.Zero)
        {
            row.Release();
            row.BeginAnimation(OpacityProperty, null);
            row.Opacity = opacity;
            row.BeginAnimation(Lyric.UnsungProperty, null);
            row.Unsung = unsung;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = scale.ScaleY = size;
            return;
        }
        // Подсветка уходящей строки гаснет вместе с уходом в серый, без белой вспышки.
        if (sung) row.Release(); else row.Hold(time);
        row.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, time));
        row.BeginAnimation(Lyric.UnsungProperty, new DoubleAnimation(unsung, time));
        scale.AnimateScale(new DoubleAnimation(size, time)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    void BlurLine(Lyric row, double radius, Duration time)
    {
        if (time.TimeSpan == TimeSpan.Zero)
        {
            if (row.Effect is BlurEffect old) old.BeginAnimation(BlurEffect.RadiusProperty, null);
            row.Effect = radius == 0 ? null : new BlurEffect { Radius = radius };
            return;
        }
        if (row.Effect is not BlurEffect blur)
        {
            if (radius == 0) return;
            row.Effect = blur = new BlurEffect { Radius = 0 };
        }
        var change = new DoubleAnimation(radius, time);
        change.Completed += (_, _) =>
        {
            if (ReferenceEquals(row.Effect, blur) && blur.Radius < 0.01) row.Effect = null;
        };
        blur.BeginAnimation(BlurEffect.RadiusProperty, change);
    }

    LyricsService.Line[] _outroFrom = [];
    LyricsService.Line[] _outroTo = [];
    TrackDuration _outroDuration;
    double _outroCandidate = double.NaN;
    TimeSpan? _outroEnd;
    LyricTiming.FinalLine _outroFinal;

    // Пустая метка в конце трека переносится при необходимости. Результат кешируется, чтобы не пересчитывать каждый кадр.
    // Строка для лога пишется один раз на загрузку текста, в лог попадают только времена, без текста песни.
    LyricsService.Line[] TrimmedOutro(LyricsService.Line[] lines, double candidate, out TimeSpan? lastEnd, out LyricTiming.FinalLine final)
    {
        TrackDuration duration = _media.SourceDuration;
        if (ReferenceEquals(lines, _outroFrom) && duration == _outroDuration && candidate.Equals(_outroCandidate))
        {
            lastEnd = _outroEnd;
            final = _outroFinal;
            return _outroTo;
        }
        LyricsService.Line[] trimmed = LyricTiming.TrimUntrustedOutro(lines, duration, candidate, out string report);
        if (report.Length > 0) App.Log(report);
        _outroFrom = lines;
        _outroTo = trimmed;
        _outroDuration = duration;
        _outroCandidate = candidate;
        lastEnd = _outroEnd = LyricTiming.DocumentEnd(trimmed, duration, candidate);
        final = _outroFinal = LyricTiming.FinalTextLine(trimmed, duration, candidate, lastEnd);
        return trimmed;
    }

    void FillSungLine()
    {
        int index = _playerIndex;
        if (_playerLyricsFrozen || index < 0 || index >= _playerRows.Length) return;

        TimeSpan start = _playerLines[index].Time;
        TimeSpan? end = TimedLineEnd(_playerLines, index, _playerFinal, _lyrics.Result.Candidate?.Duration ?? 0);
        Lyric row = _playerRows[index].Row;
        double progress = LyricTiming.Progress(LyricTime, start, end);
        if (row.Progress != progress) row.Progress = progress;
    }
}
