using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Manager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using Status = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace DynamicIsland;

sealed class MediaService
{
    const string Untitled = "Без названия";
    const string BarSeparator = " – ";
    const int MinPercentToMeasure = 10;
    const int PollPlayingMs = 250, PollPausedMs = 1000;
    const int UnsetYear = 2000;
    const int BarMissesToForget = 3;
    const int TimelineRecheckMs = 400;
    // Трек, для которого уже выполнена перепроверка таймлайна (одна на трек, без повторов).
    long _timelineRecheckTrack = -1;

    readonly Dispatcher _ui;
    readonly PlaybackClock _clock;
    readonly PlayerBar _bar = new();
    Manager? _manager;
    Session? _session;
    Session? _pinned;
    Session? _latestPlaying;
    readonly Dictionary<Session, bool> _observed = [];
    DateTime _metadataPollAt;
    bool _metadataPolling;
    bool _observedStarted;
    int _activeRefreshes;
    int _refreshVersion;
    int _seekVersion;
    long _sessionGeneration, _trackGeneration;

    string _title = "", _artist = "", _album = "";
    // A missing or undecodable thumbnail is retried by the metadata poll; after a few misses the web catalogue is asked once.
    const int CoverFallbackAfter = 3;
    string _coverKey = "", _coverFallbackKey = "", _coverFailure = "";
    byte[]? _thumbnailHash;
    int _coverAttempts;
    TimeSpan _duration;
    TrackDuration _sourceDuration;
    PositionQuality _positionQuality;
    Status _status;

    bool _noTimeline;
    bool _barSearched;
    bool _barFound;
    string _barText = "", _barTitle = "", _barArtist = "";
    TimeSpan _barTime = TimeSpan.MinValue;
    int _barPercent = -1;
    int _barMisses;
    TimeSpan _measuredLength;
    TimeSpan _assumedLength;
    bool _barSeekUnanchored;

    public MediaService(Dispatcher ui, TimeProvider? time = null)
    {
        _ui = ui;
        _clock = new(time);
    }

    public event Action? Changed;
    readonly CancellationTokenSource _watchCts = new();
    public void StopWatching() => _watchCts.Cancel();

    public string Title => _title.Length > 0 ? _title : _barTitle;
    public string Artist => _title.Length > 0 ? _artist : _barArtist;
    public string Album => _title.Length > 0 ? _album : "";
    public TrackSnapshot Snapshot => new(Title, Artist, Album, Source, _sessionGeneration, _trackGeneration,
        _sourceDuration, _positionQuality);
    public TrackDuration SourceDuration => _sourceDuration;
    public TrackDuration DisplayDuration => _sourceDuration.IsKnown ? _sourceDuration
        : _duration > TimeSpan.Zero ? new(_duration, DurationOrigin.LyricsEstimate) : TrackDuration.Unknown;
    public PositionQuality PositionQuality => _positionQuality;
    public string Name => Title.Length > 0 ? Title : SourceApp.Name(Source) is { Length: > 0 } app ? app : Untitled;
    public ImageSource? Art { get; private set; }
    public Color[] Palette { get; private set; } = CoverPalette.Plain;
    public Color Accent => Palette[0];
    public bool IsPlaying { get; private set; }
    public double PlaybackRate => _clock.Rate;
    // Firefox keeps a paused, zero-length "YouTube" page session after its video closes.
    // Do not infer inactivity from loudness, or hide real paused/untimed media.
    internal bool IsEmptyYouTubePage => IsEmptyYouTubePageFor(SourceApp.Name(Source));
    bool IsEmptyYouTubePageFor(string sourceName) => IsBrowserName(sourceName) && !IsPlaying && !_barFound
        && string.Equals(_title.Trim(), "YouTube", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrWhiteSpace(_artist) && string.IsNullOrWhiteSpace(_album)
        && !_sourceDuration.IsKnown && RunningPosition <= TimeSpan.Zero;
    internal bool HasTrackContent(string sourceName) => !IsEmptyYouTubePageFor(sourceName)
        && (Title.Length > 0 || (_noTimeline && _barSearched && IsLive));
    public bool HasTrack => _session != null && HasTrackContent(SourceApp.Name(Source));

    static bool IsBrowserName(string name) => name.Trim().ToLowerInvariant() is
        "zen" or "zen browser" or "firefox" or "mozilla firefox" or "chrome" or "google chrome"
        or "edge" or "microsoft edge" or "brave" or "opera" or "opera gx" or "vivaldi";
    public TimeSpan Duration => _duration;
    public bool HasBarPosition => _noTimeline && _barFound;
    public bool Seekable => _noTimeline ? _barFound : _sourceDuration.CanMatch && _duration.TotalSeconds >= 1;

    public double Progress => _duration.TotalSeconds >= 1 ? Math.Clamp(Position / _duration, 0, 1)
        : HasBarPosition && _barPercent > 0 ? _barPercent / 100.0 : 0;

    public string Source
    {
        get
        {
            try { return _session?.SourceAppUserModelId ?? ""; }
            catch { return ""; }
        }
    }

    public TimeSpan Position
    {
        get
        {
            TimeSpan position = RunningPosition;
            if (position < TimeSpan.Zero || (_noTimeline && !_barFound)) return TimeSpan.Zero;
            return _sourceDuration.IsKnown && position > _sourceDuration.Value ? _sourceDuration.Value : position;
        }
    }

    TimeSpan RunningPosition => _clock.Position;

    bool IsLive => _status is Status.Playing or Status.Paused;

    public void AssumeDuration(TimeSpan length, TrackSnapshot track)
    {
        if (!Snapshot.SameIdentity(track)) return;
        _assumedLength = length > TimeSpan.Zero ? length : TimeSpan.Zero;
        if (_noTimeline) UpdateBarDuration();
        // Таймлайн без конца (YouTube и др.): длительность из лирики — оценка, чтобы показать остаток и прогресс.
        else if (!_sourceDuration.IsKnown) _duration = _assumedLength;
    }

    public async Task StartAsync()
    {
        _manager = await Manager.RequestAsync();
        _manager.CurrentSessionChanged += (_, _) => _ui.InvokeAsync(() =>
        {
            ObserveSessions();
            AttachSession();
        });
        _manager.SessionsChanged += (_, _) => _ui.InvokeAsync(() => { ObserveSessions(); AttachSession(); });
        ObserveSessions();
        AttachSession();
        _ = WatchBarAsync(_watchCts.Token);
        _ = WatchPlaybackAsync(_watchCts.Token);
    }

    public bool SwitchSession(int direction)
    {
        try
        {
            var sessions = _manager?.GetSessions();
            if (sessions == null || MediaSessionSelection.Cycle(sessions, _session, direction) is not { } next) return false;
            _pinned = next;
            AttachSession();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void TogglePlay() => _ = SendCommandAsync(session => session.TryTogglePlayPauseAsync());

    public void Next() => _ = SendCommandAsync(session => session.TrySkipNextAsync());

    public void Previous() => _ = SendCommandAsync(session => session.TrySkipPreviousAsync());

    public async Task Seek(double fraction)
    {
        Session? session = _session;
        if (session == null || !double.IsFinite(fraction)) return;
        var request = BeginSeekRequest();
        if (_noTimeline)
        {
            TimeSpan? target = _duration > TimeSpan.Zero ? _duration * Math.Clamp(fraction, 0, 1) : null;
            if (!_barFound || !await Task.Run(() => _bar.Seek(fraction)) || !SameSession(session, _session)) return;
            if (!TryConfirmSeek(target, request)) return;
            Changed?.Invoke();
            return;
        }
        // Перемотка только по известной длительности источника, оценка из лирики для неё не годится.
        if (!_sourceDuration.CanMatch || _duration <= TimeSpan.Zero) return;
        try
        {
            var target = TimeSpan.FromTicks((long)(_duration.Ticks * Math.Clamp(fraction, 0, 1)));
            TimeSpan start = session.GetTimelineProperties().StartTime;
            if (await session.TryChangePlaybackPositionAsync((start + target).Ticks) && SameSession(session, _session)
                && TryConfirmSeek(target, request))
            {
                Changed?.Invoke();
            }
        }
        catch (Exception failure) { App.Log(failure); }
    }

    internal (long SessionGeneration, long TrackGeneration, int Version) BeginSeekRequest()
        => (_sessionGeneration, _trackGeneration, ++_seekVersion);

    internal bool TryConfirmSeek(TimeSpan? position, (long SessionGeneration, long TrackGeneration, int Version) request,
        PositionQuality quality = PositionQuality.Estimated)
    {
        if (request.SessionGeneration != _sessionGeneration || request.TrackGeneration != _trackGeneration
            || request.Version != _seekVersion) return false;
        if (position is TimeSpan known) _clock.ConfirmSeek(known);
        else _clock.Reset();
        _barSeekUnanchored = _noTimeline && position is null;
        if (_noTimeline)
        {
            _barTime = TimeSpan.MinValue;
            _barPercent = -1;
        }
        _positionQuality = position is null ? PositionQuality.Unknown : quality;
        return true;
    }

    async Task SendCommandAsync(Func<Session, IAsyncOperation<bool>> command)
    {
        try { if (_session != null) await command(_session); }
        catch (Exception failure) { App.Log(failure); }
    }

    // WinRT wrappers implement equality by native identity, not managed wrapper reference.
    static bool SameSession(Session? a, Session? b) => Equals(a, b);

    static bool IsPlayingSession(Session session)
    {
        try { return session.GetPlaybackInfo().PlaybackStatus == Status.Playing; }
        catch { return false; }
    }

    static DateTimeOffset? SessionActivity(Session session)
    {
        try
        {
            if (session.GetPlaybackInfo().PlaybackStatus is not (Status.Playing or Status.Paused)) return null;
            DateTimeOffset updated = session.GetTimelineProperties().LastUpdatedTime;
            return updated > DateTimeOffset.MinValue && updated <= DateTimeOffset.UtcNow.AddMinutes(1) ? updated : null;
        }
        catch { return null; }
    }

    void AttachSession()
    {
        Session? next = PickSession();
        if (SameSession(next, _session)) return;
        bool keepTrack = SameApp(next, _session) && HasTrack;
        if (_session != null)
        {
            try
            {
                _session.MediaPropertiesChanged -= OnProperties;
                _session.PlaybackInfoChanged -= OnPlayback;
                _session.TimelinePropertiesChanged -= OnTimeline;
            }
            catch (Exception failure) { App.Log(failure); }
        }

        _session = next;
        if (keepTrack)
        {
            // A renewed SMTC session invalidates its pending reads without replacing the visible track.
            _sessionGeneration++;
            _refreshVersion++;
            _seekVersion++;
        }
        else ResetSessionState();

        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnProperties;
            _session.PlaybackInfoChanged += OnPlayback;
            _session.TimelinePropertiesChanged += OnTimeline;
        }
        if (!keepTrack) Changed?.Invoke();
        _ = RefreshAsync();
    }

    internal void ResetSessionState()
    {
        _sessionGeneration++;
        _trackGeneration++;
        _refreshVersion++;
        _title = _artist = _album = "";
        _thumbnailHash = null;
        _noTimeline = _barSearched = false;
        ResetTrackTiming();
        _clock.SetPlayback(false, 1);
        _status = default;
        IsPlaying = false;
        Art = null;
        Palette = CoverPalette.Plain;
    }

    internal bool UpdateTrackMetadata(string title, string artist, string album)
    {
        if (title == _title && artist == _artist && album == _album) return false;
        (_title, _artist, _album) = (title, artist, album);
        _trackGeneration++;
        ResetTrackTiming();
        return true;
    }

    void ResetTrackTiming()
    {
        ForgetBar();
        _seekVersion++;
        _clock.Reset();
        _duration = TimeSpan.Zero;
        _sourceDuration = TrackDuration.Unknown;
        _positionQuality = PositionQuality.Unknown;
    }

    // Как в старой версии: одна и та же программа-источник (AppUserModelId), даже если объект сессии пересоздан.
    static bool SameApp(Session? a, Session? b)
    {
        if (a == null || b == null) return false;
        if (ReferenceEquals(a, b)) return true;
        try { return a.SourceAppUserModelId == b.SourceAppUserModelId; }
        catch { return false; }
    }

    Session? PickSession()
    {
        try
        {
            var sessions = _manager!.GetSessions();
            if (_pinned != null)
            {
                if (sessions.FirstOrDefault(session => SameApp(session, _pinned)) is { } pinned)
                {
                    LogPick(sessions, pinned);
                    return pinned;
                }
                _pinned = null;
            }

            Session? current = _manager.GetCurrentSession();
            Session? previous = sessions.FirstOrDefault(session => SameSession(session, _session))
                ?? sessions.FirstOrDefault(session => SameApp(session, _session));
            Session? picked = MediaSessionSelection.Pick(sessions, current, null, previous,
                _latestPlaying, IsPlayingSession, SessionActivity);
            LogPick(sessions, picked);
            return picked;
        }
        catch
        {
            return null;
        }
    }

    // Диагностика: какие SMTC-сессии видны и какая выбрана; пишется только при изменении.
    string _pickDiag = "";

    void LogPick(IReadOnlyList<Session> sessions, Session? picked)
    {
        try
        {
            string list = sessions.Count == 0 ? "none" : string.Join(" | ", sessions.Select(s =>
                $"{s.SourceAppUserModelId}:{s.GetPlaybackInfo().PlaybackStatus}:{SessionActivity(s)?.ToString("HH:mm:ss") ?? "-"}"));
            string text = $"sessions[{list}] => {picked?.SourceAppUserModelId ?? "none"}";
            if (text == _pickDiag) return;
            _pickDiag = text;
            App.Log("media: " + text);
        }
        catch (Exception failure) { App.Log(failure); }
    }

    void OnProperties(Session session, MediaPropertiesChangedEventArgs e) => _ui.InvokeAsync(() =>
    {
        if (SameSession(session, _session)) _ = RefreshAsync();
    });

    void ObserveSessions()
    {
        if (_manager is null) return;
        try
        {
            var sessions = _manager.GetSessions();
            bool initial = !_observedStarted;
            _observedStarted = true;
            foreach (Session old in _observed.Keys.Where(old => !sessions.Any(session => SameSession(session, old))).ToArray())
            {
                old.PlaybackInfoChanged -= OnObservedPlayback;
                _observed.Remove(old);
            }
            foreach (Session session in sessions)
            {
                bool playing = IsPlayingSession(session);
                if (!_observed.TryGetValue(session, out bool wasPlaying))
                {
                    session.PlaybackInfoChanged += OnObservedPlayback;
                    if (!initial && playing)
                    {
                        _latestPlaying = session;
                        if (!SameSession(session, _pinned)) _pinned = null;
                    }
                }
                else if (!wasPlaying && playing)
                {
                    _latestPlaying = session;
                    if (!SameSession(session, _pinned)) _pinned = null;
                }
                _observed[session] = playing;
            }
        }
        catch { /* A browser can close a session during enumeration. Retry on the next poll. */ }
    }

    void OnObservedPlayback(Session session, PlaybackInfoChangedEventArgs e) => _ui.InvokeAsync(() =>
    {
        ObserveSessions();
        AttachSession();
    });

    void OnPlayback(Session session, PlaybackInfoChangedEventArgs e) => _ui.InvokeAsync(() =>
    {
        if (!SameSession(session, _session)) return;
        ReadPlayback();
        ReadTimeline();
        Changed?.Invoke();
    });

    void OnTimeline(Session session, TimelinePropertiesChangedEventArgs e) => _ui.InvokeAsync(() =>
    {
        if (!SameSession(session, _session)) return;
        ReadPlayback();
        ReadTimeline();
        Changed?.Invoke();
    });

    async Task RefreshAsync(bool refreshCover = true)
    {
        _activeRefreshes++;
        try { await RefreshCoreAsync(refreshCover); }
        finally { _activeRefreshes--; }
    }

    // Диагностика: метаданные сессии; пишется только при изменении.
    string _metaDiag = "";

    void LogMeta(string title, string artist, string album)
    {
        string text = $"title='{title}' artist='{artist}' album='{album}' status={_status} timeline={_noTimeline}";
        if (text == _metaDiag) return;
        _metaDiag = text;
        App.Log("media meta: " + text);
    }

    async Task RefreshCoreAsync(bool refreshCover)
    {
        Session? session = _session;
        int version = ++_refreshVersion;

        if (session == null)
        {
            _title = _artist = _album = "";
            Art = null;
            Palette = CoverPalette.Plain;
            IsPlaying = false;
            Changed?.Invoke();
            return;
        }

        try
        {
            // Порядок и логика как в старом RefreshAsync (DynamicIsland-main\MediaService.cs).
            var properties = await session.TryGetMediaPropertiesAsync();
            if (version != _refreshVersion) return;

            string title = properties.Title ?? "";
            string artist = properties.Artist ?? "", album = properties.AlbumTitle ?? "";
            LogMeta(title, artist, album);
            bool sameTrack = (title, artist, album) == (_title, _artist, _album);
            string coverKey = title + "\n" + artist + "\n" + album;
            if (coverKey != _coverKey)
                (_coverKey, _coverAttempts, _coverFailure, _thumbnailHash) = (coverKey, 0, "", null);
            // Обложку перезагружаем только при смене трека, если её нет или если вызов сам её запросил.
            // Фоновый опрос метаданных (refreshCover: false) не должен пересоздавать картинку: это даёт анимацию «уезжает и возвращается».
            (ImageSource? art, Color[] palette, byte[]? hash) = (null, CoverPalette.Plain, null);
            bool coverWanted = refreshCover || !sameTrack || Art is null;
            if (properties.Thumbnail != null && coverWanted)
            {
                (art, palette, hash) = await LoadCoverAsync(properties.Thumbnail, coverKey);
                if (version != _refreshVersion) return;
            }
            if (art is null && coverWanted) _coverAttempts++;

            _title = title;
            _artist = artist;
            _album = album;
            if (art != null || !sameTrack)
            {
                Art = art;
                Palette = palette;
                _thumbnailHash = hash;
            }
            if (Art is null && _coverAttempts >= CoverFallbackAfter && Settings.Lyrics && title.Length > 0)
                _ = LoadFallbackCoverAsync(title, artist, coverKey);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }

        ReadPlayback();
        ReadTimeline();
        Changed?.Invoke();
    }

    async Task<(ImageSource? Art, Color[] Palette, byte[]? Hash)> LoadCoverAsync(IRandomAccessStreamReference thumbnail, string key)
    {
        try
        {
            using var source = await thumbnail.OpenReadAsync();
            using var stream = source.AsStreamForRead();
            var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            if (buffer.Length == 0)
            {
                LogCoverFailure(key, new InvalidDataException("The media session returned an empty thumbnail stream."));
                return (null, CoverPalette.Plain, null);
            }
            buffer.Position = 0;
            byte[] hash = SHA256.HashData(buffer);
            if (key == _coverKey && Art is { } current && _thumbnailHash is { } previous && hash.AsSpan().SequenceEqual(previous))
                return (current, Palette, previous);
            buffer.Position = 0;
            var (art, palette) = await Task.Run(() => CoverPalette.Decode(buffer));
            return (art, palette, hash);
        }
        catch (Exception ex)
        {
            LogCoverFailure(key, ex);
            return (null, CoverPalette.Plain, null);
        }
    }

    // The metadata poll repeats every two seconds: log each distinct reason once per track, not every retry.
    void LogCoverFailure(string key, Exception ex)
    {
        string reason = ex.GetType().Name + ": " + ex.Message;
        if (key != _coverKey || reason == _coverFailure) return;
        _coverFailure = reason;
        App.Log(new InvalidOperationException("Cover thumbnail failed for \"" + key.Replace('\n', '|') + "\".", ex));
    }

    async Task LoadFallbackCoverAsync(string title, string artist, string key)
    {
        if (key == _coverFallbackKey) return;
        _coverFallbackKey = key;
        try
        {
            using MemoryStream? image = await CoverFallback.FindAsync(title, artist);
            if (image is null || key != _coverKey || Art != null || IsEmptyYouTubePage) return;
            (ImageSource art, Color[] palette) = await Task.Run(() => CoverPalette.Decode(image));
            // Back on the UI thread; the track may have changed while the picture was downloading.
            if (key != _coverKey || Art != null || IsEmptyYouTubePage) return;
            Art = art;
            Palette = palette;
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or System.Text.Json.JsonException
            or NotSupportedException or FileFormatException)
        {
            App.Log(ex);
        }
    }

    bool ReadPlayback()
    {
        if (_session == null) return false;
        try
        {
            var info = _session.GetPlaybackInfo();
            Status status = info.PlaybackStatus;
            bool playing = status == Status.Playing;
            double rate = PlaybackClock.ValidRate(info.PlaybackRate ?? 1);
            bool changed = ApplyPlayback(playing, rate) || status != _status;
            if (status != _status)
            {
                _status = status;
                if (IsLive) _bar.ResetRetry();
                else ForgetBar();
            }
            return changed;
        }
        catch
        {
            return false;
        }
    }

    internal bool ApplyPlayback(bool playing, double rate)
    {
        rate = PlaybackClock.ValidRate(rate);
        bool changed = playing != IsPlaying || rate != _clock.Rate;
        _clock.SetPlayback(playing, rate);
        IsPlaying = playing;
        return changed;
    }

    // Диагностика: сырой SMTC-таймлайн; пишется только при изменении.
    string _timelineDiag = "";

    void LogTimeline(TimeSpan start, TimeSpan end, TimeSpan position, DateTimeOffset updatedAt)
    {
        string text = $"start={start:g} end={end:g} pos={position:g} updated={updatedAt:HH:mm:ss.fff} playing={IsPlaying} noTimeline={_noTimeline}";
        if (text == _timelineDiag) return;
        _timelineDiag = text;
        App.Log($"media timeline: {text} clockPos={RunningPosition:g}");
    }

    bool ReadTimeline()
    {
        if (_session == null) return false;
        try
        {
            var timeline = _session.GetTimelineProperties();
            LogTimeline(timeline.StartTime, timeline.EndTime, timeline.Position, timeline.LastUpdatedTime);
            // Конец нулевой или таймлайна нет: через паузу перечитываем таймлайн (один раз на трек).
            if (timeline.EndTime <= timeline.StartTime) ScheduleTimelineRecheck();
            return ApplyTimeline(timeline.StartTime, timeline.EndTime, timeline.Position, timeline.LastUpdatedTime);
        }
        catch
        {
            return false;
        }
    }

    // Однократная перепроверка таймлайна для текущего трека. Защита от повторов: не больше одной проверки на трек,
    // а смена трека или сессии (новое поколение) снова разрешает одну.
    void ScheduleTimelineRecheck()
    {
        if (_timelineRecheckTrack == _trackGeneration) return;
        _timelineRecheckTrack = _trackGeneration;
        _ = RecheckTimelineAsync(_sessionGeneration, _trackGeneration);
    }

    async Task RecheckTimelineAsync(long session, long track)
    {
        await Task.Delay(TimelineRecheckMs);
        _ui.InvokeAsync(() =>
        {
            if (session != _sessionGeneration || track != _trackGeneration || _session == null) return;
            ReadPlayback();
            ReadTimeline();
            Changed?.Invoke();
        });
    }

    internal bool ApplyTimeline(TimeSpan start, TimeSpan end, TimeSpan position, DateTimeOffset updatedAt)
    {
        // LastUpdatedTime is not a track ID: the current timeline may arrive before metadata.
        TrackDuration previousDuration = DisplayDuration;
        PositionQuality previousQuality = _positionQuality;
        // Отчёт с нулевой длиной (start = end = 0, pos = 0) приходит между нормальными, но с настоящей меткой времени.
        // Раньше он стирал известную длительность и сбрасывал часы на 0. Пока длительность известна, такой отчёт игнорируем.
        if (end <= start && _sourceDuration.IsKnown && _session != null) return false;
        bool noTimeline = updatedAt.Year < UnsetYear && end <= start;
        // Browsers send transient reports without an end time (ads, tab switches). Keep the known end of the same track
        // instead of dropping it; a new track resets timing in ResetTrackTiming, so stale ends are not carried over.
        if (noTimeline && _sourceDuration.IsKnown && _session != null) return false;
        if (noTimeline)
        {
            if (!_noTimeline && previousQuality != PositionQuality.Unknown)
            {
                TimeSpan heldPosition = RunningPosition;
                if (!_clock.ApplyTimeline(position - start, updatedAt)) return false;
                // Missing timing cannot disprove the last monotonic position. The fallback decides its quality.
                _clock.AnchorPosition(heldPosition);
            }
            _noTimeline = true;
            UpdateBarDuration();
            return previousDuration != DisplayDuration || previousQuality != _positionQuality;
        }

        TimeSpan duration = end > start ? end - start : TimeSpan.Zero;
        TrackDuration sourceDuration = duration > TimeSpan.Zero ? new(duration, DurationOrigin.MediaSession) : TrackDuration.Unknown;
        // Position, duration and mode belong to one report. Reject them together before changing shared state.
        bool anchored = _clock.ApplyTimeline(position - start, updatedAt, sourceDuration.IsKnown ? sourceDuration.Value : null);
        if (!anchored) return false;
        _noTimeline = false;
        _sourceDuration = sourceDuration;
        // Без конца в таймлайне берём оценку из лирики (если уже известна), а не ноль.
        _duration = duration > TimeSpan.Zero ? duration : _assumedLength;
        _positionQuality = _clock.LastTimelineReliable ? PositionQuality.MediaSession : PositionQuality.Estimated;
        return anchored || previousDuration != DisplayDuration || previousQuality != _positionQuality;
    }

    async Task WatchPlaybackAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(IsPlaying ? PollPlayingMs : PollPausedMs, token);
                ObserveSessions();
                AttachSession();
                if (ReadPlayback() | ReadTimeline()) Changed?.Invoke();
                // Aggregate browser sessions sometimes omit the event when their media tab changes.
                if (_session != null && !_metadataPolling && _activeRefreshes == 0 && DateTime.UtcNow >= _metadataPollAt)
                    _ = PollMetadataAsync();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception failure) { App.Log(failure); }
        }
    }

    async Task PollMetadataAsync()
    {
        _metadataPolling = true;
        _metadataPollAt = DateTime.UtcNow.AddSeconds(2);
        try { await RefreshAsync(refreshCover: false); }
        finally { _metadataPolling = false; }
    }

    async Task WatchBarAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(IsPlaying ? PollPlayingMs : PollPausedMs, token);
                Session? session = _session;
                long generation = _sessionGeneration;
                long trackGeneration = _trackGeneration;
                int seekVersion = _seekVersion;
                if (session == null || !_noTimeline || !IsLive) continue;
                string app = Source;
                if (!PlayerBar.Supports(app)) continue;
                PlayerBar.Reading? reading = await Task.Run(() => _bar.Read(app));
                if (SameSession(session, _session) && _noTimeline && IsLive)
                    TryApplyBarReading(reading, generation, trackGeneration, seekVersion);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception failure) { App.Log(failure); }
        }
    }

    internal bool TryApplyBarReading(PlayerBar.Reading? reading, long sessionGeneration, long trackGeneration, int? seekVersion = null)
    {
        if (sessionGeneration != _sessionGeneration || trackGeneration != _trackGeneration
            || seekVersion is int seek && seek != _seekVersion) return false;
        ApplyBarReading(reading);
        return true;
    }

    internal void ApplyBarReading(PlayerBar.Reading? reading)
    {
        bool hadTrack = HasTrack;
        (string title, string artist, TimeSpan duration) = (Title, Artist, _duration);
        PositionQuality previousQuality = _positionQuality;
        _barSearched = true;

        if (reading is { } bar)
        {
            _barFound = true;
            if (bar.Text != _barText)
            {
                if (_title.Length == 0) _clock.Reset();
                ForgetBar();
                _barFound = true;
                _barText = bar.Text;
                int dash = bar.Text.IndexOf(BarSeparator, StringComparison.Ordinal);
                (_barArtist, _barTitle) = dash > 0 ? (bar.Text[..dash], bar.Text[(dash + BarSeparator.Length)..]) : ("", bar.Text);
            }
            _barMisses = 0;
            if (bar.Time is { } time && time != _barTime)
            {
                _barTime = time;
                _barSeekUnanchored = false;
                _clock.AnchorPosition(time);
            }
            MeasureLength(bar.Percent);
        }
        else if (_barFound && ++_barMisses >= BarMissesToForget)
        {
            ForgetBar();
        }

        if (Title != title || Artist != artist) _trackGeneration++;
        UpdateBarDuration();
        if (HasTrack != hadTrack || Title != title || Artist != artist || _duration != duration
            || previousQuality != _positionQuality) Changed?.Invoke();
    }

    void MeasureLength(int percent)
    {
        if (percent == _barPercent) return;

        bool stepped = IsPlaying && percent == _barPercent + 1;
        _barPercent = percent;
        if (!stepped || percent < MinPercentToMeasure) return;

        double length = RunningPosition.TotalSeconds * 100 / (percent - 0.5);
        if (_measuredLength == TimeSpan.Zero || Math.Abs(length - _measuredLength.TotalSeconds) > 50.0 / percent)
            _measuredLength = TimeSpan.FromSeconds(Math.Round(length));
    }

    void UpdateBarDuration()
    {
        if (_assumedLength > TimeSpan.Zero && _barPercent >= 0
            && Math.Abs(RunningPosition / _assumedLength * 100 - _barPercent) > 1.5 + 150 / _assumedLength.TotalSeconds)
            _assumedLength = TimeSpan.Zero;
        _duration = !_barFound ? TimeSpan.Zero : _measuredLength > TimeSpan.Zero ? _measuredLength : _assumedLength;
        _sourceDuration = _barFound && _measuredLength > TimeSpan.Zero
            ? new(_measuredLength, DurationOrigin.ProgressEstimate) : TrackDuration.Unknown;
        _positionQuality = !_barFound || _barSeekUnanchored ? PositionQuality.Unknown
            : _barTime == TimeSpan.MinValue ? PositionQuality.Estimated : PositionQuality.PlayerBar;
    }

    void ForgetBar()
    {
        _barFound = false;
        _barText = _barTitle = _barArtist = "";
        _barTime = TimeSpan.MinValue;
        _barPercent = -1;
        _barSeekUnanchored = false;
        _measuredLength = _assumedLength = TimeSpan.Zero;
    }
}
