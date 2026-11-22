using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace DynamicIsland;

internal sealed record ProviderSearchResult(LyricsCandidate[] Candidates, LyricsStatus Status, bool Complete,
    DateTimeOffset? RetryAt = null, string? ErrorCode = null)
{
    public bool NeedsRefresh { get; init; }
}

internal interface ILyricsProvider
{
    Task<ProviderSearchResult> SearchAsync(TrackSnapshot track, CancellationToken cancellationToken);
}

internal interface IProgressiveLyricsProvider : ILyricsProvider
{
    Task<ProviderSearchResult> SearchAsync(TrackSnapshot track, Func<ProviderSearchResult, Task>? progress,
        CancellationToken cancellationToken);
}

internal sealed record LrclibOptions
{
    public TimeSpan TotalBudget { get; init; } = TimeSpan.FromSeconds(12);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(4);
    public TimeSpan RequestSpacing { get; init; } = TimeSpan.FromMilliseconds(300);
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan DefaultCooldown { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxRequests { get; init; } = 6;
    public int MaxBodyBytes { get; init; } = 2 * 1024 * 1024;
    public int MaxCandidates { get; init; } = 120;
    public bool RefinePlain { get; init; } = true;
}

// Production provider instances share this state, including across track cancellation.
internal sealed class LrclibRequestCoordinator
{
    internal readonly SemaphoreSlim Gate = new(1, 1);
    readonly object _sync = new();
    DateTimeOffset? _retryAt;
    int _cooldownStatus = 429;
    long? _lastStarted;

    internal (DateTimeOffset RetryAt, int Status)? Cooldown(TimeProvider time)
    {
        lock (_sync) return _retryAt > time.GetUtcNow() ? (_retryAt.Value, _cooldownStatus) : null;
    }

    internal void SetCooldown(DateTimeOffset retryAt, int status)
    {
        lock (_sync)
            if (_retryAt is null || retryAt > _retryAt) (_retryAt, _cooldownStatus) = (retryAt, status);
    }

    internal TimeSpan SpacingDelay(TimeProvider time, TimeSpan spacing)
    {
        lock (_sync)
            return _lastStarted is long started ? spacing - time.GetElapsedTime(started) : TimeSpan.Zero;
    }

    internal void Started(TimeProvider time)
    {
        lock (_sync) _lastStarted = time.GetTimestamp();
    }
}

internal sealed class LrclibProvider : IProgressiveLyricsProvider
{
    const string Api = "https://lrclib.net/api/";
    static readonly LrclibRequestCoordinator SharedCoordinator = new();
    static readonly HttpClient SharedHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    static readonly string[] TextFields = ["trackName", "name", "artistName", "albumName", "syncedLyrics", "plainLyrics"];
    readonly HttpClient _http;
    readonly TimeProvider _time;
    readonly LrclibOptions _options;
    readonly LrclibRequestCoordinator _coordinator;
    readonly string _api, _providerId;

    public LrclibProvider(HttpClient? http = null, TimeProvider? time = null, LrclibOptions? options = null,
        LrclibRequestCoordinator? coordinator = null, string? api = null, string providerId = "LRCLIB")
    {
        _http = http ?? SharedHttp;
        _time = time ?? TimeProvider.System;
        _options = options ?? new();
        _coordinator = coordinator ?? SharedCoordinator;
        _api = api ?? Api;
        _providerId = providerId;
        if (_options.TotalBudget <= TimeSpan.Zero || _options.RequestTimeout <= TimeSpan.Zero
            || _options.RequestSpacing < TimeSpan.Zero || _options.RetryDelay < TimeSpan.Zero
            || _options.DefaultCooldown <= TimeSpan.Zero || _options.MaxRequests is < 1 or > 6
            || _options.MaxBodyBytes is < 1 or > 2 * 1024 * 1024 || _options.MaxCandidates is < 1 or > 120)
            throw new ArgumentOutOfRangeException(nameof(options));
    }

    public Task<ProviderSearchResult> SearchAsync(TrackSnapshot track, CancellationToken cancellationToken)
        => SearchAsync(track, null, cancellationToken);

    // Karalyr serves the LRCLIB API shape (https://www.karalyr.com/api). Its own cooldown state is kept apart from lrclib.net.
    static readonly LrclibRequestCoordinator KaralyrCoordinator = new();

    internal static LrclibProvider Karalyr(HttpClient? http = null, TimeProvider? time = null)
        => new(http, time, null, KaralyrCoordinator, "https://www.karalyr.com/api/", "Karalyr");

    // AMLL TTML DB serves the LRCLIB API shape at /v1/lrclib (https://api.amll.dev). Own cooldown state, as for Karalyr.
    static readonly LrclibRequestCoordinator AmllCoordinator = new();

    internal static LrclibProvider Amll(HttpClient? http = null, TimeProvider? time = null)
        => new(http, time, null, AmllCoordinator, "https://api.amll.dev/v1/lrclib/", "AMLL TTML DB");

    public async Task<ProviderSearchResult> SearchAsync(TrackSnapshot track, Func<ProviderSearchResult, Task>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(track);
        cancellationToken.ThrowIfCancellationRequested();
        long started = _time.GetTimestamp();
        using var budget = new CancellationTokenSource(_options.TotalBudget, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        var candidates = new List<LyricsCandidate>();
        int requests = 0, records = 0;
        bool retried = false, clean = true;
        string? error = null;
        LyricsCandidate? verifiedPlain = null;
        var queries = LyricsNormalizer.SearchQueries(track);
        if (queries.Length == 0)
            return new([], string.IsNullOrWhiteSpace(track.Title) ? LyricsStatus.NotFound : LyricsStatus.ProviderError,
                string.IsNullOrWhiteSpace(track.Title), ErrorCode: string.IsNullOrWhiteSpace(track.Title) ? null : "InvalidMetadata");

        var plan = new List<(string Url, bool Exact)>();
        double duration = track.Duration.Value.TotalSeconds;
        // LRCLIB /api/get documents duration as 1..3600 seconds; out-of-range values must not trigger exact lookup.
        if (track.Duration.CanMatch && duration is >= 1 and <= 3600 && !string.IsNullOrWhiteSpace(track.Artist))
        {
            string parameters = queries[0].Parameters + "&duration=" + duration.ToString("0.###", CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(track.Album) && track.Album.Length <= LyricsNormalizer.MaxQueryFieldLength
                && !track.Album.Any(char.IsControl)) parameters += "&album_name=" + Uri.EscapeDataString(track.Album);
            plan.Add((_api + "get?" + parameters, true));
        }
        plan.AddRange(queries.Take(_options.MaxRequests - plan.Count).Select(query => (_api + "search?" + query.Parameters, false)));

        try
        {
            foreach ((string url, bool exact) in plan)
            {
                while (true)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    if (requests == _options.MaxRequests)
                        return Result(false, "RequestLimitExceeded");
                    RequestResult response = await RequestAsync(url, exact, started, linked.Token).ConfigureAwait(false);
                    using JsonDocument? json = response.Json;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (response.Sent) requests++;
                    if (response.RetryAt is not null)
                    {
                        if (verifiedPlain is not null) return Result(false, response.ErrorCode);
                        return new(candidates.ToArray(), LyricsStatus.RateLimited, false, response.RetryAt, response.ErrorCode);
                    }
                    if (response.ErrorCode is not null)
                    {
                        if (response.Transient && !retried && requests < _options.MaxRequests)
                        {
                            retried = true;
                            if (_options.RetryDelay > TimeSpan.Zero)
                                await Task.Delay(_options.RetryDelay, _time, linked.Token).ConfigureAwait(false);
                            continue;
                        }
                        return Result(false, response.ErrorCode);
                    }
                    if (json is not null)
                    {
                        bool malformed = false;
                        IEnumerable<JsonElement> elements = exact ? [json.RootElement] : json.RootElement.EnumerateArray();
                        foreach (JsonElement element in elements)
                        {
                            linked.Token.ThrowIfCancellationRequested();
                            if (++records > _options.MaxCandidates)
                                return Result(false, "CandidateLimitExceeded");
                            LyricsCandidate? candidate;
                            try { candidate = ParseCandidate(element, _providerId); }
                            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException or TimeoutException)
                            {
                                candidate = null;
                                malformed = true;
                            }
                            if (candidate is not null)
                            {
                                AddCandidate(candidates, candidate);
                                if (exact && StrongExact(track, candidate))
                                {
                                    linked.Token.ThrowIfCancellationRequested();
                                    if (_options.RefinePlain && !candidate.HasValidSync && !candidate.IsInstrumental)
                                    {
                                        verifiedPlain = candidate;
                                        if (progress is not null) await progress(Result(true, null)).ConfigureAwait(false);
                                    }
                                    else return Result(true, null);
                                }
                            }
                            else if (!ValidEmptyRecord(element)) malformed = true;
                        }
                        if (malformed) { clean = false; error = "MalformedRecord"; }
                        if (_options.RefinePlain && verifiedPlain is not null && !malformed)
                        {
                            var match = LyricsMatcher.Match(track, LyricsMatcher.PrepareTrack(track),
                                LyricsMatcher.Prepare(Result(true, null)));
                            if (match.Status == LyricsStatus.Synced || match.Candidate?.HasValidSync == true)
                                return Result(true, null);
                        }
                        // A complete, well-formed query can be shown while broader fallback queries continue.
                        // Never promote a partially parsed response or a pool with a malformed earlier response.
                        if (!exact && verifiedPlain is null && clean && !malformed && candidates.Count > 0 && progress is not null)
                            await progress(Result(true, null) with { NeedsRefresh = true }).ConfigureAwait(false);
                    }
                    break;
                }
            }
            linked.Token.ThrowIfCancellationRequested();
            return Result(clean, error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result(false, "BudgetExceeded");
        }

        ProviderSearchResult Result(bool complete, string? code)
            => verifiedPlain is not null && (!complete || code is not null)
                ? new([verifiedPlain], LyricsStatus.AwaitingVerification, true)
                : new(candidates.ToArray(), code is not null ? LyricsStatus.ProviderError
                : candidates.Count > 0 ? LyricsStatus.AwaitingVerification : LyricsStatus.NotFound, complete, ErrorCode: code);
    }

    async Task<RequestResult> RequestAsync(string url, bool exact, long started, CancellationToken cancellationToken)
    {
        if (_coordinator.Cooldown(_time) is { } cooling)
            return new(RetryAt: cooling.RetryAt, ErrorCode: CooldownCode(cooling.Status));
        await _coordinator.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        bool sent = false;
        try
        {
            if (_coordinator.Cooldown(_time) is { } retryAt)
                return new(RetryAt: retryAt.RetryAt, ErrorCode: CooldownCode(retryAt.Status));
            TimeSpan delay = _coordinator.SpacingDelay(_time, _options.RequestSpacing);
            if (delay > TimeSpan.Zero) await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan remaining = _options.TotalBudget - _time.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero) cancellationToken.ThrowIfCancellationRequested();
            if (remaining <= TimeSpan.Zero) return new(ErrorCode: "BudgetExceeded");
            using var timeout = new CancellationTokenSource(remaining < _options.RequestTimeout ? remaining : _options.RequestTimeout, _time);
            using var requestToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(AppVersion.Product + " (https://github.com/mihailkotovski/DynamicIsland)");
            request.Headers.Accept.ParseAdd("application/json");
            _coordinator.Started(_time);
            sent = true;
            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests
                || response.StatusCode == HttpStatusCode.ServiceUnavailable && response.Headers.Contains("Retry-After"))
            {
                DateTimeOffset cooldown = RetryAt(response);
                _coordinator.SetCooldown(cooldown, (int)response.StatusCode);
                return new(RetryAt: cooldown, ErrorCode: "Http" + (int)response.StatusCode, Sent: true);
            }
            requestToken.Token.ThrowIfCancellationRequested();
            if (response.StatusCode == HttpStatusCode.NoContent || exact && response.StatusCode == HttpStatusCode.NotFound)
                return new(Sent: true);
            if (!response.IsSuccessStatusCode)
                return new(ErrorCode: "Http" + (int)response.StatusCode,
                    Transient: response.StatusCode == HttpStatusCode.RequestTimeout || (int)response.StatusCode >= 500, Sent: true);
            string? type = response.Content.Headers.ContentType?.MediaType;
            if (type is null || !(type.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                || type.EndsWith("+json", StringComparison.OrdinalIgnoreCase)))
                return new(ErrorCode: "UnexpectedContentType", Sent: true);
            if (response.Content.Headers.ContentLength > _options.MaxBodyBytes)
                return new(ErrorCode: "PayloadTooLarge", Sent: true);

            Stream stream = await response.Content.ReadAsStreamAsync(requestToken.Token).ConfigureAwait(false);
            await using var streamLifetime = stream.ConfigureAwait(false);
            using var body = new MemoryStream();
            byte[] buffer = new byte[8192];
            while (true)
            {
                int read = await stream.ReadAsync(buffer, requestToken.Token).ConfigureAwait(false);
                if (read == 0) break;
                if (body.Length + read > _options.MaxBodyBytes)
                    return new(ErrorCode: "PayloadTooLarge", Sent: true);
                body.Write(buffer, 0, read);
            }
            requestToken.Token.ThrowIfCancellationRequested();
            JsonDocument json = JsonDocument.Parse(body.GetBuffer().AsMemory(0, (int)body.Length), new() { MaxDepth = 32 });
            if (requestToken.IsCancellationRequested)
            {
                json.Dispose();
                requestToken.Token.ThrowIfCancellationRequested();
            }
            if (json.RootElement.ValueKind != (exact ? JsonValueKind.Object : JsonValueKind.Array))
            {
                json.Dispose();
                return new(ErrorCode: "UnexpectedJsonShape", Sent: true);
            }
            return new(Json: json, Sent: true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(ErrorCode: "RequestTimeout", Transient: true, Sent: sent);
        }
        catch (HttpRequestException) { return new(ErrorCode: "NetworkError", Transient: true, Sent: sent); }
        catch (IOException) { return new(ErrorCode: "NetworkError", Transient: true, Sent: sent); }
        catch (JsonException) { return new(ErrorCode: "MalformedJson", Sent: sent); }
        finally { _coordinator.Gate.Release(); }
    }

    static string CooldownCode(int status) => status == 503 ? "ProviderCooldown503" : "ProviderCooldown429";

    static bool StrongExact(TrackSnapshot track, LyricsCandidate candidate)
    {
        if (!track.Duration.CanMatch || candidate.TimingWarnings.Length != 0
            || !double.IsFinite(candidate.Duration) || candidate.Duration <= 0
            || Math.Abs(track.Duration.Value.TotalSeconds - candidate.Duration) > 2) return false;
        NormalizedLyricsMetadata wanted = LyricsNormalizer.Normalize(track.Title, track.Artist);
        NormalizedLyricsMetadata actual = LyricsNormalizer.Normalize(candidate.Title, candidate.Artist);
        if (!string.Equals(wanted.BaseTitle, actual.BaseTitle, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(wanted.Artist, actual.Artist, StringComparison.OrdinalIgnoreCase)
            || !wanted.VersionTags.SequenceEqual(actual.VersionTags)) return false;
        var pool = LyricsMatcher.Prepare(new([candidate], LyricsStatus.AwaitingVerification, true));
        LyricsMatch match = LyricsMatcher.Match(track, LyricsMatcher.PrepareTrack(track), pool);
        return ReferenceEquals(match.Candidate, candidate) && (match.Status is LyricsStatus.Synced or LyricsStatus.Plain or LyricsStatus.Instrumental
            || match.Status == LyricsStatus.AwaitingVerification && candidate.HasValidSync
                && match.ReasonCodes.Contains("PositionUnavailable"));
    }

    DateTimeOffset RetryAt(HttpResponseMessage response)
    {
        DateTimeOffset now = _time.GetUtcNow();
        try
        {
            var header = response.Headers.RetryAfter;
            if (header?.Delta is TimeSpan delta && delta >= TimeSpan.Zero)
                return now + delta;
            if (header?.Date is DateTimeOffset date && date > now) return date;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException) { }
        return now + _options.DefaultCooldown;
    }

    public static LyricsCandidate? ParseCandidate(JsonElement item, string providerId = "LRCLIB")
    {
        if (!ValidStructure(item)) return null;
        double duration = 0;
        if (item.TryGetProperty("duration", out JsonElement length) && length.ValueKind != JsonValueKind.Null
            && (length.ValueKind != JsonValueKind.Number || !length.TryGetDouble(out duration)
                || !double.IsFinite(duration) || duration is <= 0 or > 86400)) return null;
        string title = Text(item, "trackName") ?? Text(item, "name") ?? "";
        string artist = Text(item, "artistName") ?? "";
        string album = Text(item, "albumName") ?? "";
        if (title.Length > 512 || artist.Length > 512 || album.Length > 512) return null;
        string? synced = Text(item, "syncedLyrics");
        string? plain = Text(item, "plainLyrics");
        if (string.IsNullOrWhiteSpace(plain)) plain = null;
        bool instrumental = item.TryGetProperty("instrumental", out JsonElement flag) && flag.ValueKind == JsonValueKind.True;
        LrcParseResult? parsed = string.IsNullOrWhiteSpace(synced) ? null : LrcParser.Parse(synced);
        if (parsed is null && plain is null && !instrumental) return null;
        long? id = item.TryGetProperty("id", out JsonElement recordId) && recordId.ValueKind == JsonValueKind.Number
            && recordId.TryGetInt64(out long value) && value > 0 ? value : null;
        return new(providerId, id, title, artist, album, duration, parsed?.LastTimestamp.TotalSeconds ?? 0,
            parsed?.Lines ?? [], plain, instrumental)
        {
            HasValidSync = parsed?.IsUsable == true,
            TimingWarnings = parsed?.Warnings ?? [],
            VersionTags = LyricsNormalizer.ExtractVersionTags(title),
        };
    }

    static bool ValidEmptyRecord(JsonElement item)
        => ValidStructure(item)
            && (Text(item, "trackName") is not null || Text(item, "name") is not null)
            && string.IsNullOrWhiteSpace(Text(item, "syncedLyrics")) && string.IsNullOrWhiteSpace(Text(item, "plainLyrics"))
            && (!item.TryGetProperty("instrumental", out JsonElement value) || value.ValueKind is JsonValueKind.False or JsonValueKind.Null);

    static bool ValidStructure(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return false;
        foreach (string name in TextFields)
        {
            if (!item.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null) continue;
            if (value.ValueKind != JsonValueKind.String) return false;
            if (name is not ("syncedLyrics" or "plainLyrics") && value.GetString()!.Length > 512) return false;
        }
        if (item.TryGetProperty("duration", out JsonElement duration) && duration.ValueKind != JsonValueKind.Null
            && (duration.ValueKind != JsonValueKind.Number || !duration.TryGetDouble(out double seconds)
                || !double.IsFinite(seconds) || seconds is <= 0 or > 86400)) return false;
        if (item.TryGetProperty("id", out JsonElement id) && id.ValueKind != JsonValueKind.Null
            && (id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out long recordId) || recordId <= 0)) return false;
        return !item.TryGetProperty("instrumental", out JsonElement flag)
            || flag.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null;
    }

    static string? Text(JsonElement item, string name)
        => item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static void AddCandidate(List<LyricsCandidate> candidates, LyricsCandidate candidate)
    {
        int existing = candidates.FindIndex(other => other.ProviderId == candidate.ProviderId
            && (candidate.RecordId is not null ? other.RecordId == candidate.RecordId
                : other.RecordId is null && other.Title == candidate.Title && other.Artist == candidate.Artist
                    && other.Album == candidate.Album && other.Duration == candidate.Duration
                    && other.IsInstrumental == candidate.IsInstrumental && other.PlainLyrics == candidate.PlainLyrics
                    && other.Lines.SequenceEqual(candidate.Lines)));
        if (existing < 0) candidates.Add(candidate);
        else if (!candidates[existing].HasValidSync && candidate.HasValidSync) candidates[existing] = candidate;
    }

    sealed record RequestResult(JsonDocument? Json = null, string? ErrorCode = null, bool Transient = false,
        DateTimeOffset? RetryAt = null, bool Sent = false);
}
