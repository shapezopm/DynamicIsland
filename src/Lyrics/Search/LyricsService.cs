using System.Net.Http;

namespace DynamicIsland;

sealed class LyricsService : IDisposable
{
    public readonly record struct Line(TimeSpan Time, string Text);
    static readonly Line[] None = [];
    ILyricsProvider _provider;
    readonly Func<LyricSource, ILyricsProvider>? _providerFactory;
    LyricSource _sources;
    readonly SynchronizationContext? _context;
    readonly LyricsCache? _cache;
    readonly LyricsSearchPreferences? _preferences;
    readonly ITrackIdentityResolver? _identityResolver;
    TrackIdentity? _identity;
    LyricsSearchPreference? _preference;
    LyricsCandidate? _selected;
    LyricsCandidate? _verifiedAutomatic;
    LyricsCandidate? _heldDocument;
    // Last synced document shown before a retry. It stays on screen while the retry runs and when it finds no synced text.
    LyricsCandidate? _retainedDocument;
    readonly TimeProvider _time;
    readonly CancellationTokenSource _lifetime = new();
    TrackSnapshot? _track;
    PreparedLyricsTrack? _metadata;
    ProviderSearchResult _search = new([], LyricsStatus.NotFound, true);
    PreparedLyricsPool _pool = LyricsMatcher.Prepare(new([], LyricsStatus.NotFound, true));
    CancellationTokenSource? _loadCancellation;
    // True while the active load runs without an exact duration (SMTC timing not known yet).
    bool _loadedWithoutDuration;
    Task _cacheClearTask = Task.CompletedTask;
    bool _checkedExactCache;
    TimeSpan _searchElapsed;
    string _cacheSource = "Network";
    bool _enabled = true, _disposed;
    int _requestVersion;
    bool _refining;
    bool _providerSearching;
    Line[]? _displaySource, _displayLines;
    readonly Dictionary<string, DateTimeOffset> _plainRefreshes = new(StringComparer.Ordinal);

    public event Action? Changed;
    public bool IsPending { get; private set; }
    public LyricsResult Result { get; private set; } = LyricsResult.Empty;
    internal Task PendingLoad { get; private set; } = Task.CompletedTask;
    internal LyricsDiagnostics Diagnostics { get; }
    internal bool HasManualSelection => _selected is not null;
    // Текст для трека отключён: галочка «Автоматически» снята и ни один источник не выбран.
    internal bool IsOff => _preference?.Off == true;

    internal void TurnOff(TrackSnapshot expected)
    {
        if (!expected.SameIdentity(_track) || _selected is not null) return;
        Interlocked.Increment(ref _requestVersion);
        CancelLoad();
        _refining = false;
        _preference = (_preference ?? new LyricsSearchPreference(expected.Title, expected.Artist)) with { Selected = null, Off = true };
        _preferences?.Set(expected, _preference);
        PublishResult();
    }
    internal bool IsSearchingSources => _providerSearching || _refining || IsPending;

    internal void HoldDocument() => _heldDocument = Result.Candidate;
    internal void ReleaseDocument() { _heldDocument = null; PublishResult(); }

    internal Task UseAutomaticAsync()
    {
        _selected = null;
        if (_preference is not null)
        {
            _preference = _preference with { Selected = null, Off = false };
            if (_track is { } track)
            {
                // Old document pins stored the uncorrected media fields too. Releasing the pin
                // must not turn those unchanged fields into a permanent search correction.
                if (_preference.Title == track.Title && _preference.Artist == track.Artist) _preference = null;
                _preferences?.Set(track, _preference);
            }
        }
        return StartRetry(skipHeldSource: false);
    }

    internal Task UseAutomaticAsync(TrackSnapshot expected) => expected.SameIdentity(_track) ? UseAutomaticAsync() : Task.CompletedTask;
    internal Task RetryAsync(TrackSnapshot expected) => expected.SameIdentity(_track) ? RetryAsync() : Task.CompletedTask;

    public LyricsService(HttpClient? http = null, TimeProvider? time = null, SynchronizationContext? context = null)
        : this(LyricsSources.Create(Settings.LyricSources, http, time), context, null, time, useCache: false,
            preferences: new(), providerFactory: sources => LyricsSources.Create(sources, http, time), sources: Settings.LyricSources,
            identityResolver: TrackIdentityResolver.Create())
    {
        _cache = new LyricsCache(time: time, providerPolicy: () => "sources-v2-alternatives:" + (int)_sources);
    }

    internal LyricsService(ILyricsProvider provider, SynchronizationContext? context = null,
        LyricsCache? cache = null, TimeProvider? time = null, bool useCache = false,
        LyricsSearchPreferences? preferences = null, Func<LyricSource, ILyricsProvider>? providerFactory = null,
        LyricSource sources = LyricSource.Default, ITrackIdentityResolver? identityResolver = null)
    {
        _provider = provider;
        _providerFactory = providerFactory;
        _sources = Settings.Known(sources);
        _context = context ?? SynchronizationContext.Current;
        _time = time ?? TimeProvider.System;
        _cache = useCache ? cache ?? new LyricsCache(time: _time) : null;
        _preferences = preferences;
        _identityResolver = identityResolver;
        Diagnostics = new(_time);
    }

    public void Track(string title, string artist) =>
        Track(new(title, artist, "", "", 0, 0, TrackDuration.Unknown, PositionQuality.Unknown));

    public void Track(TrackSnapshot track, bool enabled = true, LyricSource? sources = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LyricSource requested = Settings.Known(sources ?? (_providerFactory is null ? _sources : Settings.LyricSources));
        bool sourceChanged = _providerFactory is not null && requested != _sources;
        bool sameTrack = track.SameIdentity(_track) && enabled == _enabled && !sourceChanged;
        bool timingChanged = _track?.Duration != track.Duration || _track?.PositionQuality != track.PositionQuality;
        if (sameTrack)
        {
            _track = track;
            if (timingChanged)
            {
                PublishResult();
                RepeatWhenTimed();
            }
            return;
        }
        int version = Interlocked.Increment(ref _requestVersion);
        CancelLoad();
        if (sourceChanged)
        {
            _sources = requested;
            _provider = _providerFactory!(requested);
            lock (_plainRefreshes) _plainRefreshes.Clear();
        }
        _track = track;
        _identity = null;
        _enabled = enabled;
        _refining = false;
        _retainedDocument = null;
        ResetPool();
        _preference = _preferences?.Read(track);
        _selected = _preference?.Selected;
        if (_providerFactory is not null && _selected is not null && !LyricsSources.Enabled(_selected.ProviderId, _sources)) _selected = null;
        _checkedExactCache = ExactLookup(track);
        _metadata = LyricsMatcher.PrepareTrack(LookupTrack(track));
        IsPending = enabled && !string.IsNullOrWhiteSpace(track.Title) && _selected is null && !IsOff;
        PendingLoad = Task.CompletedTask;
        PublishResult();
        _loadedWithoutDuration = _preference is null && IsPending && !ExactLookup(track);
        if (IsPending && version == Volatile.Read(ref _requestVersion))
        {
            _loadCancellation = new();
            PendingLoad = LoadAsync(track, version, _loadCancellation.Token);
        }
    }

    public Line[] LinesFor() => Result.Lines;
    internal TrackSnapshot? SearchTrack => _track is null ? null : LookupTrack(_track);
    internal LyricsCandidate[] Candidates => _selected is null ? _search.Candidates.ToArray()
        : _search.Candidates.Append(_selected).Distinct().ToArray();

    internal Task RefineSyncAsync(ILyricsProvider? provider = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_selected is not null || _heldDocument is not null || _refining || IsPending || !_enabled || _track is not { } track || Result.Status != LyricsStatus.Plain)
            return Task.CompletedTask;
        _refining = true;
        int version = Interlocked.Increment(ref _requestVersion);
        CancelLoad();
        _loadCancellation = new();
        return PendingLoad = RefineLoadAsync(track, LookupTrack(track), version, _loadCancellation.Token,
            provider ?? new LrclibProvider(options: new() { RefinePlain = true }));
    }

    async Task RefineLoadAsync(TrackSnapshot track, TrackSnapshot lookup, int version, CancellationToken token,
        ILyricsProvider provider)
    {
        try
        {
            ProviderSearchResult search = await provider.SearchAsync(lookup, token).ConfigureAwait(false);
            if (token.IsCancellationRequested || !search.Complete) return;
            ProviderSearchResult? acceptedSearch = null;
            await PublishOnContextAsync(() =>
            {
                if (_disposed || token.IsCancellationRequested || version != Volatile.Read(ref _requestVersion)
                    || !track.SameIdentity(_track) || !_enabled) return;
                PreparedLyricsPool pool = MergeCachePools(search, _search);
                LyricsMatch match = LyricsMatcher.Match(LookupTrack(_track!), _metadata!, pool);
                if (match.Status != LyricsStatus.Synced && match.Candidate?.HasValidSync != true) return;
                _selected = null;
                if (_preference is not null)
                {
                    _preference = _preference with { Selected = null };
                    _preferences?.Set(track, _preference);
                }
                _search = pool.Search; _pool = pool;
                acceptedSearch = pool.Search;
                PublishResult();
            }).ConfigureAwait(false);
            if (acceptedSearch is not null && _cache is not null && version == Volatile.Read(ref _requestVersion))
                await _cache.PutAsync(lookup, ExactLookup(lookup), acceptedSearch, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) { /* Refinement must not remove the already displayed document. */ }
        finally
        {
            await PublishOnContextAsync(() =>
            {
                if (_disposed || version != Volatile.Read(ref _requestVersion)) return;
                _refining = false;
                PublishResult();
            }).ConfigureAwait(false);
        }
    }

    // A manual query is searched as typed: its duration is not used to accept or reject sources.
    TrackSnapshot LookupTrack(TrackSnapshot track) => _preference is null ? _identity?.Apply(track) ?? track
        : track with { Title = _preference.Title, Artist = _preference.Artist, Duration = TrackDuration.Unknown };

    internal Task CorrectSearchAsync(TrackSnapshot expected, string title, string artist, bool reset = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!expected.SameIdentity(_track) || !reset && !LyricsSearchPreferences.Valid(title, artist)) return Task.CompletedTask;
        _preference = reset ? null : new(title.Trim(), artist.Trim());
        _identity = null;
        _selected = null;
        _heldDocument = null;
        _preferences?.Set(expected, _preference);
        _metadata = LyricsMatcher.PrepareTrack(LookupTrack(expected));
        return StartRetry(skipHeldSource: false);
    }

    internal bool SelectCandidate(TrackSnapshot expected, LyricsCandidate candidate, bool imported = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!expected.SameIdentity(_track) || !_enabled || IsPending
            || !LyricsSearchPreferences.ValidCandidate(candidate)
            || !imported && !_search.Candidates.Contains(candidate) && !ReferenceEquals(candidate, _selected)) return false;
        var preference = new LyricsSearchPreference(SearchTrack!.Title, SearchTrack.Artist, candidate);
        // Persistence validation also bounds pasted text and provider data.
        if (_preferences is not null && !_preferences.Set(expected, preference)) return false;
        Interlocked.Increment(ref _requestVersion);
        CancelLoad();
        _refining = false;
        _preference = preference;
        _selected = candidate;
        _retainedDocument = null;
        PublishResult();
        return true;
    }
    public Line[] LinesFor(TrackDuration duration) => _track is null ? None
        : Match(_track with { Duration = duration }).Lines;

    public TimeSpan UsualLength => !IsPending && Result.Candidate is { } candidate
        && double.IsFinite(candidate.Duration) && candidate.Duration is > 0 and <= 86400
        ? TimeSpan.FromSeconds(candidate.Duration) : TimeSpan.Zero;

    async Task LoadAsync(TrackSnapshot track, int version, CancellationToken token, bool bypassCache = false,
        ILyricsProvider? provider = null, LyricsCandidate[]? kept = null)
    {
        ILyricsProvider source = provider ?? _provider;
        _providerSearching = source is LyricsProviderChain;
        long started = _time.GetTimestamp();
        string cacheSource = "Network";
        ProviderSearchResult search;
        TrackSnapshot lookup = LookupTrack(track);
        bool refreshPlain = false;
        try
        {
            // A track arriving during Clear cannot read a pre-clear disk entry.
            await _cacheClearTask.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (_identityResolver is not null && _preference is null && _identity is null)
            {
                TrackIdentity resolved;
                try
                {
                    resolved = await _identityResolver.ResolveAsync(track, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception) { resolved = new(track.Title, track.Artist, "ResolutionUnavailable"); }
                await PublishOnContextAsync(() =>
                {
                    if (_disposed || token.IsCancellationRequested || version != Volatile.Read(ref _requestVersion)
                        || !track.SameIdentity(_track) || !_enabled || _preference is not null) return;
                    _identity = resolved;
                    _metadata = LyricsMatcher.PrepareTrack(LookupTrack(_track!));
                    PublishResult();
                }).ConfigureAwait(false);
                if (token.IsCancellationRequested || version != Volatile.Read(ref _requestVersion)) return;
                lookup = LookupTrack(track);
            }
            (ProviderSearchResult? cached, string cachedFrom) = await ReadCacheAsync(lookup, bypassCache, token).ConfigureAwait(false);
            refreshPlain = cached is not null && source is LyricsProviderChain
                && (cached.NeedsRefresh || LyricsMatcher.Match(lookup, LyricsMatcher.PrepareTrack(lookup), LyricsMatcher.Prepare(cached)).Status == LyricsStatus.Plain)
                && ShouldRefreshPlain(lookup);
            if (refreshPlain)
                await PublishOnContextAsync(() =>
                {
                    if (_disposed || token.IsCancellationRequested || version != Volatile.Read(ref _requestVersion)
                        || !track.SameIdentity(_track) || !_enabled) return;
                    _search = cached!; _pool = LyricsMatcher.Prepare(cached!); IsPending = false;
                    PublishResult();
                }).ConfigureAwait(false);
            search = cached is not null && !refreshPlain ? cached : (source is LyricsProviderChain chain
                ? await chain.SearchAsync(lookup, partial => PublishOnContextAsync(() =>
                {
                    if (_disposed || token.IsCancellationRequested || version != Volatile.Read(ref _requestVersion)
                        || !track.SameIdentity(_track) || !_enabled) return;
                    _pool = refreshPlain ? MergeCachePools(partial, cached!) : LyricsMatcher.Prepare(WithKept(partial, kept));
                    _search = _pool.Search;
                    IsPending = false;
                    _cacheSource = "Network";
                    _searchElapsed = _time.GetElapsedTime(started);
                    PublishResult();
                    Diagnostics.Record(Result, _cacheSource, _searchElapsed, _search.Candidates.Length, _search.Candidates, LookupTrack(track));
                }), token).ConfigureAwait(false)
                : await source.SearchAsync(lookup, token).ConfigureAwait(false));
            search = WithKept(search, kept);
            if (refreshPlain)
            {
                var merged = search.Complete ? MergeCachePools(search, cached!) : LyricsMatcher.Prepare(cached!);
                var match = LyricsMatcher.Match(lookup, LyricsMatcher.PrepareTrack(lookup), merged);
                search = match.Status is LyricsStatus.Synced or LyricsStatus.Plain || match.Candidate?.HasValidSync == true
                    ? merged.Search : cached!;
            }
            cacheSource = cached is null || refreshPlain ? "Network" : cachedFrom;
            if (token.IsCancellationRequested || version != Volatile.Read(ref _requestVersion)) return;
            // An empty result from a search without exact duration is not a miss worth remembering for 10 minutes.
            if (_cache is not null && cacheSource == "Network" && (search.Candidates.Length > 0 || ExactLookup(lookup)))
            {
                ProviderSearchResult stored = search.Candidates.Length > 0 && search.Complete
                    ? search with { Status = LyricsStatus.AwaitingVerification, ErrorCode = null, RetryAt = null,
                        NeedsRefresh = search.NeedsRefresh || search.ErrorCode is not null || search.RetryAt is not null } : search;
                try { await _cache.PutAsync(lookup, ExactLookup(lookup), stored, token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception) { cacheSource = "NetworkCacheWriteFailed"; }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception)
        {
            search = new([], LyricsStatus.ProviderError, false, ErrorCode: "UnexpectedProviderError");
        }
        if (token.IsCancellationRequested || version != Volatile.Read(ref _requestVersion)) return;
        PreparedLyricsPool pool = LyricsMatcher.Prepare(search);
        await PublishOnContextAsync(() =>
        {
            // Cancellation saves work; generation also rejects providers that ignore it.
            if (_disposed || token.IsCancellationRequested || version != Volatile.Read(ref _requestVersion)
                || !track.SameIdentity(_track) || !_enabled) return;
            if (refreshPlain) RememberPlainRefresh(lookup);
            _search = search;
            _pool = pool;
            _cacheSource = cacheSource;
            _searchElapsed = _time.GetElapsedTime(started);
            _providerSearching = false;
            IsPending = false;
            PublishResult();
            Diagnostics.Record(Result, _cacheSource, _searchElapsed, _search.Candidates.Length, _search.Candidates, _track is null ? null : LookupTrack(_track));
            RepeatWhenTimed();
        }).ConfigureAwait(false);
    }

    bool ShouldRefreshPlain(TrackSnapshot track)
    {
        string key = LyricsSearchPreferences.Key(track);
        DateTimeOffset now = _time.GetUtcNow();
        lock (_plainRefreshes)
            return !_plainRefreshes.TryGetValue(key, out DateTimeOffset checkedAt) || now - checkedAt >= TimeSpan.FromMinutes(1);
    }

    void RememberPlainRefresh(TrackSnapshot track)
    {
        lock (_plainRefreshes)
        {
            if (_plainRefreshes.Count >= 128) _plainRefreshes.Remove(_plainRefreshes.MinBy(item => item.Value).Key);
            _plainRefreshes[LyricsSearchPreferences.Key(track)] = _time.GetUtcNow();
        }
    }

    async Task<(ProviderSearchResult? Search, string Source)> ReadCacheAsync(TrackSnapshot track, bool bypass, CancellationToken token)
    {
        if (_cache is null || bypass) return (null, "Network");
        try
        {
            bool exact = ExactLookup(track);
            ProviderSearchResult? cached = await _cache.GetAsync(track, exact, token).ConfigureAwait(false);
            if (cached is not null) return (cached, exact ? "ExactCache" : "GenericCache");
            if (exact)
            {
                cached = await _cache.GetAsync(track, false, token).ConfigureAwait(false);
                if (cached is { Candidates.Length: > 0 })
                {
                    LyricsMatch match = LyricsMatcher.Match(track, LyricsMatcher.PrepareTrack(track), LyricsMatcher.Prepare(cached));
                    if (match.Status is LyricsStatus.Synced or LyricsStatus.Plain or LyricsStatus.Instrumental
                        || match.Status == LyricsStatus.AwaitingVerification && match.Candidate is not null)
                        return (cached, "GenericCache");
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { /* Cache failure is a miss, not a provider failure. */ }
        return (null, "Network");
    }

    static bool ExactLookup(TrackSnapshot track) => track.Duration.CanMatch
        && track.Duration.Value.TotalSeconds is >= 1 and <= 86400 && !string.IsNullOrWhiteSpace(track.Artist);

    // Timing arrived after a search that ran without it: prefer the exact cache, otherwise repeat the search once.
    // While a load is in flight, nothing happens here; the completed load calls this again.
    void RepeatWhenTimed()
    {
        if (!_loadedWithoutDuration || _selected is not null || !_enabled || IsPending || _providerSearching
            || _track is not { } track || !ExactLookup(track))
        {
            CheckLateExactCache();
            return;
        }
        if (_cache is null) RestartWithDuration(track);
        else CheckLateExactCache();
    }

    // One repeat search after timing arrives: the exact LRCLIB /get lookup needs a duration the first search lacked.
    void RestartWithDuration(TrackSnapshot track)
    {
        int version = Interlocked.Increment(ref _requestVersion);
        CancelLoad();
        _loadedWithoutDuration = false;
        _checkedExactCache = true;
        _loadCancellation = new();
        PendingLoad = LoadAsync(track, version, _loadCancellation.Token);
    }

    void CheckLateExactCache()
    {
        if (_cache is null || _checkedExactCache || IsPending || _providerSearching || !_enabled || _disposed
            || _track is not { } track || !ExactLookup(track) || _loadCancellation is null) return;
        _checkedExactCache = true;
        PendingLoad = ReadLateExactAsync(track, _search, Volatile.Read(ref _requestVersion), _loadCancellation.Token);
    }

    async Task ReadLateExactAsync(TrackSnapshot track, ProviderSearchResult previous, int version, CancellationToken token)
    {
        long started = _time.GetTimestamp();
        ProviderSearchResult? cached;
        try
        {
            await _cacheClearTask.ConfigureAwait(false);
            cached = await _cache!.GetAsync(LookupTrack(track), true, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception) { return; }
        if (token.IsCancellationRequested) return;
        if (cached is null)
        {
            // No exact entry: repeat the search once with the duration, on the UI context so version checks apply.
            await PublishOnContextAsync(() =>
            {
                if (_disposed || token.IsCancellationRequested || version != Volatile.Read(ref _requestVersion)
                    || !track.SameIdentity(_track) || !_enabled) return;
                if (_loadedWithoutDuration && _selected is null && _track is { } current && ExactLookup(current))
                    RestartWithDuration(current);
            }).ConfigureAwait(false);
            return;
        }
        PreparedLyricsPool pool = MergeCachePools(cached, previous);
        await PublishOnContextAsync(() =>
        {
            if (_disposed || token.IsCancellationRequested || version != Volatile.Read(ref _requestVersion)
                || !track.SameIdentity(_track) || !_enabled) return;
            // Keep known competitors when refining with an exact-key pool.
            // Matching remains relative to the latest timing snapshot.
            _search = pool.Search;
            _pool = pool;
            _cacheSource = "ExactCache";
            _searchElapsed = _time.GetElapsedTime(started);
            PublishResult();
            Diagnostics.Record(Result, _cacheSource, _searchElapsed, _search.Candidates.Length, _search.Candidates, _track is null ? null : LookupTrack(_track));
        }).ConfigureAwait(false);
    }

    static PreparedLyricsPool MergeCachePools(ProviderSearchResult cached, ProviderSearchResult previous)
    {
        LyricsCandidate[] combined = cached.Candidates.Concat(previous.Complete ? previous.Candidates : []).ToArray();
        PreparedLyricsPool prepared = LyricsMatcher.Prepare(new(combined, LyricsStatus.AwaitingVerification, true));
        PreparedLyricsCandidate[] unique = prepared.Candidates.DistinctBy(item => CacheRecordKey(item)).ToArray();
        // A cap must never silently remove a rival and manufacture a unique winner.
        bool complete = unique.Length <= 120;
        var search = new ProviderSearchResult(unique.Take(120).Select(item => item.Candidate).ToArray(), complete
            ? unique.Length > 0 ? LyricsStatus.AwaitingVerification : LyricsStatus.NotFound
            : LyricsStatus.ProviderError, complete, ErrorCode: complete ? cached.ErrorCode : "MergedCandidateLimitExceeded")
            { NeedsRefresh = cached.NeedsRefresh };
        return new(search, unique.Take(120).ToArray());
    }

    static string CacheRecordKey(PreparedLyricsCandidate item)
    {
        LyricsCandidate candidate = item.Candidate;
        if (candidate.RecordId is { } id) return candidate.ProviderId + ":" + id;
        if (candidate.SourceUrl is { } url) return candidate.ProviderId + ":" + url;
        return string.Join('\u001f', candidate.ProviderId, item.Metadata.Title, item.Metadata.Artist,
            candidate.Album, string.Join('|', item.Metadata.VersionTags), item.DocumentKey,
            candidate.Duration.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    }

    public Task RetryAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return StartRetry(skipHeldSource: true);
    }

    // The one retry path: the user's retry, the automatic return and a corrected search. The document on screen stays
    // until a synced one replaces it. A plain retry skips the source of that document, because its results are already
    // known. The other candidates of that source are kept in the pool, so alternatives from it still take part.
    Task StartRetry(bool skipHeldSource)
    {
        if (_selected is not null || !_enabled || _track is not { } track || string.IsNullOrWhiteSpace(track.Title)) return Task.CompletedTask;
        LyricsCandidate? shown = Result.Candidate is { HasValidSync: true } candidate ? candidate : null;
        if (shown is not null) _retainedDocument = shown;
        LyricSource heldFlag = shown is null ? 0 : LyricsSources.Flag(shown.ProviderId);
        LyricSource remaining = _sources & ~heldFlag & LyricSource.All;
        LyricsCandidate[]? kept = null;
        ILyricsProvider? provider = null;
        if (skipHeldSource && heldFlag != 0 && remaining != 0 && _providerFactory is not null)
        {
            string heldProvider = shown!.ProviderId;
            kept = _search.Candidates.Where(item => item.ProviderId == heldProvider).ToArray();
            provider = _providerFactory(remaining);
        }
        int version = Interlocked.Increment(ref _requestVersion);
        CancelLoad();
        ResetPool();
        _selected = null;
        _refining = false;
        if (_preference is not null)
        {
            _preference = _preference with { Selected = null };
            _preferences?.Set(track, _preference);
        }
        _checkedExactCache = ExactLookup(track);
        IsPending = true;
        PublishResult();
        if (version != Volatile.Read(ref _requestVersion)) return PendingLoad;
        _loadCancellation = new();
        return PendingLoad = LoadAsync(track, version, _loadCancellation.Token, bypassCache: true, provider, kept);
    }

    // Kept candidates of the held source join the fresh pool. A duplicate keeps the fresh record; the search status is unchanged.
    static ProviderSearchResult WithKept(ProviderSearchResult search, LyricsCandidate[]? kept)
    {
        if (kept is not { Length: > 0 }) return search;
        PreparedLyricsPool prepared = LyricsMatcher.Prepare(new(search.Candidates.Concat(kept).ToArray(), LyricsStatus.AwaitingVerification, true));
        return search with { Candidates = prepared.Candidates.DistinctBy(item => CacheRecordKey(item)).Select(item => item.Candidate).ToArray() };
    }

    public Task ClearCacheAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int version = Interlocked.Increment(ref _requestVersion);
        CancelLoad();
        _retainedDocument = null;
        ResetPool();
        _checkedExactCache = false;
        _refining = false;
        IsPending = _enabled && _track is { } track && !string.IsNullOrWhiteSpace(track.Title);
        PublishResult();
        Task clear = _cache?.ClearAsync(_lifetime.Token) ?? Task.CompletedTask;
        _cacheClearTask = ObserveClearAsync(clear);
        return PendingLoad = ClearAndReloadAsync(clear, version);
    }

    static async Task ObserveClearAsync(Task clear)
    {
        try { await clear.ConfigureAwait(false); }
        catch (Exception) { /* Loads still proceed if the cache is unavailable. */ }
    }

    async Task ClearAndReloadAsync(Task clear, int version)
    {
        try { await clear.ConfigureAwait(false); }
        catch (OperationCanceledException) when (_disposed) { return; }
        catch (Exception failure) { App.Log(failure); }
        Task? load = null;
        await PublishOnContextAsync(() =>
        {
            if (_disposed || version != Volatile.Read(ref _requestVersion)) return;
            if (!_enabled || _track is not { } track || string.IsNullOrWhiteSpace(track.Title))
            {
                IsPending = false;
                PublishResult();
                return;
            }
            _checkedExactCache = ExactLookup(track);
            _loadCancellation = new();
            load = PendingLoad = LoadAsync(track, version, _loadCancellation.Token, bypassCache: true);
        }).ConfigureAwait(false);
        if (load is not null) await load.ConfigureAwait(false);
    }

    Task PublishOnContextAsync(Action action)
    {
        if (_context is null || ReferenceEquals(_context, SynchronizationContext.Current))
        {
            action();
            return Task.CompletedTask;
        }
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _context.Post(_ =>
        {
            try { action(); done.TrySetResult(); }
            catch (Exception exception) { done.TrySetException(exception); }
        }, null);
        return done.Task;
    }

    LyricsMatch Match(TrackSnapshot track)
    {
        TrackSnapshot lookup = LookupTrack(track);
        if (_selected is { } selected) return LyricsMatcher.Manual(lookup, selected);
        if (_heldDocument is { } held) return LyricsMatcher.Manual(lookup, held);
        PreparedLyricsTrack identity = _metadata ?? LyricsMatcher.PrepareTrack(lookup);
        LyricsMatch match = LyricsMatcher.Match(lookup, identity, _pool);
        if (_provider is LyricsProviderChain { CollectAlternatives: true }
            && match.Status == LyricsStatus.Ambiguous && match.ReasonCodes.Contains("InsufficientMargin")
            && _verifiedAutomatic is { } previous && _search.Candidates.Contains(previous))
        {
            LyricsMatch verified = LyricsMatcher.Match(lookup, identity,
                LyricsMatcher.Prepare(new([previous], LyricsStatus.AwaitingVerification, true)));
            if (verified.Status == LyricsStatus.Synced || verified.Candidate?.HasValidSync == true)
                return verified with { ReasonCodes = verified.ReasonCodes.Append("KeptVerifiedSelection").ToArray() };
        }
        if (match.Candidate?.HasValidSync == true) _verifiedAutomatic = match.Candidate;
        return match;
    }

    void PublishResult()
    {
        bool active = _enabled && _track is not null && !string.IsNullOrWhiteSpace(_track.Title);
        bool off = IsOff;
        LyricsMatch? match = active && !IsPending && !off ? Match(_track!) : null;
        // A fresh synced document replaces the retained one. Without one, the retained document stays on screen
        // while a retry runs and after it finds only unsynced or no text.
        if (match?.Candidate?.HasValidSync == true) _retainedDocument = null;
        if (active && match?.Candidate?.HasValidSync != true && _retainedDocument is { } retained
            && _selected is null && _heldDocument is null && !off)
            match = LyricsMatcher.Manual(LookupTrack(_track!), retained);
        LyricsStatus status = !_enabled || off ? LyricsStatus.Disabled
            : _track is null || string.IsNullOrWhiteSpace(_track.Title) ? LyricsStatus.Idle
            : match is null ? LyricsStatus.Searching : match.Status;
        Line[] sourceLines = match?.Lines ?? None;
        if (!ReferenceEquals(sourceLines, _displaySource))
        {
            _displaySource = sourceLines;
            _displayLines = LyricsContentCleaner.Lines(sourceLines);
        }
        Result = new(_track, status, _displayLines ?? None, match?.Candidate)
        {
            SearchComplete = !IsPending && !_providerSearching && !_refining && _search.Complete,
            RetryAt = _search.RetryAt,
            ErrorCode = _search.ErrorCode,
            MatchEvidence = match?.Evidence,
            ReasonCodes = match?.ReasonCodes ?? [],
            CacheSource = _cacheSource,
            SearchElapsed = _searchElapsed,
        };
        Changed?.Invoke();
    }

    void ResetPool()
    {
        _verifiedAutomatic = null;
        _heldDocument = null;
        _providerSearching = false;
        _search = new([], LyricsStatus.NotFound, true);
        _pool = LyricsMatcher.Prepare(_search);
        _searchElapsed = TimeSpan.Zero;
        _cacheSource = "Network";
    }

    void CancelLoad()
    {
        _providerSearching = false;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        Interlocked.Increment(ref _requestVersion);
        CancelLoad();
        Changed = null;
        IsPending = false;
        ResetPool();
        Result = LyricsResult.Empty;
        _lifetime.Dispose();
    }
}
