using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DynamicIsland;

// Protocols adapted from mihailkotovski/DynamicIsland v1.24.0 LyricsCatalogues.cs.
// Documents keep catalogue identity and go through the local parser and matcher.
internal sealed class CatalogueLyricsProvider : ILyricsProvider
{
    static readonly ConcurrentDictionary<LyricSource, LyricsRemoteCoordinator> Shared = new();
    static readonly Regex Markup = new("<[^>]+>", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    const string DeezerQuery = "query TrackLyrics($trackId: String!) { track(trackId: $trackId) { lyrics { synchronizedLines { lrcTimestamp line } } } }";
    readonly LyricsRemoteHttp _remote;
    internal LyricSource Source { get; }
    internal string ProviderId => Source switch { LyricSource.Deezer => "Deezer", LyricSource.QqMusic => "QQ Music",
        LyricSource.Kugou => "Kugou", LyricSource.Paxsenix => "Paxsenix", _ => throw new InvalidOperationException() };
    string Site => Source switch { LyricSource.Deezer => "https://www.deezer.com/", LyricSource.QqMusic => "https://y.qq.com/",
        LyricSource.Kugou => "https://www.kugou.com/", _ => "https://music.apple.com/" };

    internal CatalogueLyricsProvider(LyricSource source, HttpClient? http = null, LyricsRemoteCoordinator? coordinator = null)
    {
        if (source is not (LyricSource.Deezer or LyricSource.QqMusic or LyricSource.Kugou or LyricSource.Paxsenix))
            throw new ArgumentOutOfRangeException(nameof(source));
        Source = source;
        _remote = new(http, coordinator ?? Shared.GetOrAdd(source, _ => new()));
    }

    public async Task<ProviderSearchResult> SearchAsync(TrackSnapshot track, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        LyricsQuery[] queries = LyricsSecondaryQueries.For(track);
        if (queries.Length == 0) return new([], LyricsStatus.NotFound, true);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(8));
        var candidates = new List<LyricsCandidate>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        PreparedLyricsTrack identity = LyricsMatcher.PrepareTrack(track);
        int requests = 0, documents = 0;
        bool invalidTiming = false;
        string? anonymousPass = null;
        try
        {
            foreach (LyricsQuery query in queries)
            {
                string phrase = Uri.EscapeDataString((query.Artist + " " + query.Title).Trim());
                using JsonDocument json = await Request(SearchAddress(phrase));
                foreach (var hit in ParseHits(json.RootElement).Take(10))
                {
                    if (visited.Contains(hit.Id) || !Fits(track, identity, hit.Metadata)) continue;
                    if (documents >= 2) break;
                    visited.Add(hit.Id);
                    if (Source == LyricSource.Deezer)
                    {
                        if (anonymousPass is null)
                        {
                            using var pass = await Request("https://auth.deezer.com/login/anonymous?jo=p&rto=c&i=c");
                            CheckCode(pass.RootElement);
                            anonymousPass = RequiredText(pass.RootElement, "jwt");
                            if (anonymousPass.Length is 0 or > 8192) throw new IOException("AnonymousAuthenticationUnavailable");
                        }
                        documents++;
                        string body = JsonSerializer.Serialize(new { operationName = "TrackLyrics", variables = new { trackId = hit.Id }, query = DeezerQuery });
                        using var text = await Request("https://pipe.deezer.com/api", body, anonymousPass);
                        Add(hit.Metadata, DeezerLrc(text.RootElement));
                    }
                    else if (Source == LyricSource.QqMusic)
                    {
                        documents++;
                        using var text = await Request("https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?format=json&songmid=" + hit.Id);
                        CheckCode(text.RootElement);
                        Add(hit.Metadata, WebUtility.HtmlDecode(Decode(RequiredText(text.RootElement, "lyric"))));
                    }
                    else if (Source == LyricSource.Kugou)
                    {
                        using var versions = await Request("https://krcs.kugou.com/search?ver=1&man=yes&client=mobi&hash=" + hit.Id);
                        CheckKugou(versions.RootElement);
                        foreach (var version in RequiredArray(versions.RootElement, "candidates").EnumerateArray().Take(10))
                        {
                            if (documents >= 2) break;
                            string id = Text(version, "id"), access = Text(version, "accesskey");
                            if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out long lyricId) || lyricId <= 0
                                || access.Length is 0 or > 256 || access.Any(char.IsControl)) continue;
                            var versionMetadata = hit.Metadata with
                            {
                                Title = Text(version, "song") is { Length: > 0 } name ? name : hit.Metadata.Title,
                                Artist = Text(version, "singer") is { Length: > 0 } by ? by : hit.Metadata.Artist,
                                Duration = Number(version, "duration") is > 0 and var duration ? duration / 1000 : hit.Metadata.Duration,
                            };
                            if (!Fits(track, identity, versionMetadata)) continue;
                            documents++;
                            using var text = await Request("https://lyrics.kugou.com/download?ver=1&client=pc&fmt=lrc&charset=utf8&id="
                                + id + "&accesskey=" + Uri.EscapeDataString(access));
                            CheckKugou(text.RootElement);
                            Add(hit.Metadata with { RecordId = lyricId }, Decode(RequiredText(text.RootElement, "content")));
                        }
                    }
                    else
                    {
                        documents++;
                        using var text = await Request("https://lyrics.paxsenix.org/apple-music/lyrics?id=" + hit.Id);
                        CheckCode(text.RootElement);
                        Add(hit.Metadata, RequiredText(text.RootElement, "lrc"));
                    }
                }
                if (candidates.Count > 0)
                {
                    LyricsMatch match = LyricsMatcher.Match(track, identity, LyricsMatcher.Prepare(Found()));
                    if (match.Status == LyricsStatus.Synced || match.Candidate?.HasValidSync == true || documents >= 2) break;
                }
            }
            token.ThrowIfCancellationRequested();
            return Found();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return new(candidates.ToArray(), LyricsStatus.ProviderError, false, ErrorCode: ProviderId + "Timeout"); }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or InvalidOperationException
            or FormatException or DecoderFallbackException or RegexMatchTimeoutException)
        { return new(candidates.ToArray(), LyricsStatus.ProviderError, false, ErrorCode: ProviderId + "Unavailable"); }

        async Task<JsonDocument> Request(string url, string? body = null, string? bearer = null)
        {
            if (++requests > 8) throw new IOException("RequestLimitExceeded");
            string response = await _remote.SendAsync(url, budget.Token, Site, body, bearer).ConfigureAwait(false);
            return JsonDocument.Parse(response, new() { MaxDepth = 32 });
        }
        void Add(LyricsCandidate metadata, string lrc)
        {
            if (lrc.Length == 0) return;
            if (lrc.Length > 1024 * 1024) throw new IOException("PayloadTooLarge");
            LrcParseResult parsed = LrcParser.Parse(lrc);
            // An untimed/broken first record must not prevent trying a second LRC.
            // If no usable record follows, this is not a cacheable clean miss.
            if (!parsed.IsUsable) { invalidTiming = true; return; }
            LyricsService.Line[] lines = parsed.Lines;
            if (!lines.Any(line => !string.IsNullOrWhiteSpace(line.Text))) return;
            candidates.Add(metadata with { Lines = lines, HasValidSync = true, TimingWarnings = parsed.Warnings,
                End = lines[^1].Time.TotalSeconds, PlainLyrics = string.Join('\n', lines.Select(line => line.Text)) });
        }
        ProviderSearchResult Found() => invalidTiming && candidates.Count == 0
            ? new([], LyricsStatus.ProviderError, false, ErrorCode: ProviderId + "InvalidTimingPayload")
            : new(candidates.ToArray(), candidates.Count > 0 ? LyricsStatus.AwaitingVerification : LyricsStatus.NotFound, true);
    }

    static bool Fits(TrackSnapshot track, PreparedLyricsTrack identity, LyricsCandidate metadata)
        => LyricsMatcher.CanFetchDocument(track, identity, metadata);

    string SearchAddress(string phrase) => Source switch
    {
        LyricSource.Deezer => "https://api.deezer.com/search?limit=10&q=" + phrase,
        LyricSource.QqMusic => "https://c.y.qq.com/soso/fcgi-bin/client_search_cp?format=json&p=1&n=10&w=" + phrase,
        LyricSource.Kugou => "https://songsearch.kugou.com/song_search_v2?page=1&pagesize=10&platform=WebFilter&format=json&keyword=" + phrase,
        _ => "https://itunes.apple.com/search?entity=song&limit=10&country=ru&term=" + phrase,
    };

    IEnumerable<(string Id, LyricsCandidate Metadata)> ParseHits(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
        if (Source == LyricSource.Kugou) CheckKugou(root);
        else CheckCode(root);
        string[] path = Source switch { LyricSource.Deezer => ["data"], LyricSource.QqMusic => ["data", "song", "list"],
            LyricSource.Kugou => ["data", "lists"], _ => ["results"] };
        JsonElement container = root;
        foreach (string name in path)
            if (container.ValueKind != JsonValueKind.Object || !container.TryGetProperty(name, out container)) throw new JsonException("UnexpectedSearchShape");
        if (container.ValueKind != JsonValueKind.Array) throw new JsonException("UnexpectedSearchShape");
        foreach (JsonElement song in Items(root, path).Take(10))
        {
            string id, title, artist, album;
            double duration;
            long? numeric = null;
            string? url = null;
            switch (Source)
            {
                case LyricSource.Deezer:
                    id = Text(song, "id"); title = Text(song, "title"); artist = ChildText(song, "artist", "name");
                    album = ChildText(song, "album", "title"); duration = Number(song, "duration");
                    if (!PositiveId(id, out long deezerId)) continue;
                    numeric = deezerId;
                    break;
                case LyricSource.QqMusic:
                    id = Text(song, "songmid"); title = Text(song, "songname");
                    artist = string.Join(", ", Items(song, "singer").Take(8).Select(by => Text(by, "name")));
                    album = Text(song, "albumname"); duration = Number(song, "interval");
                    if (!Token(id)) continue;
                    url = "https://y.qq.com/n/ryqq/songDetail/" + id;
                    break;
                case LyricSource.Kugou:
                    id = Text(song, "FileHash"); title = Markup.Replace(Text(song, "SongName"), "");
                    artist = Markup.Replace(Text(song, "SingerName"), "").Replace("、", ", ");
                    album = Markup.Replace(Text(song, "AlbumName"), ""); duration = Number(song, "Duration");
                    if (id.Length != 32 || !id.All(Uri.IsHexDigit)) continue;
                    url = "https://www.kugou.com/song/#hash=" + id;
                    break;
                default:
                    id = Text(song, "trackId"); title = Text(song, "trackName"); artist = Text(song, "artistName");
                    album = Text(song, "collectionName"); duration = Number(song, "trackTimeMillis") / 1000;
                    if (!PositiveId(id, out long appleId)) continue;
                    numeric = appleId;
                    break;
            }
            if (!LyricsSearchPreferences.Valid(title, artist) || album.Length > 512 || album.Any(char.IsControl)
                || !double.IsFinite(duration) || duration is <= 0 or > 86400) continue;
            yield return (id, new(ProviderId, numeric, title, artist, album, duration, 0, [], null, false)
            { VersionTags = LyricsNormalizer.ExtractVersionTags(title), SourceUrl = url });
        }
    }

    internal static bool Token(string value) => value.Length is > 0 and <= 128 && value.All(char.IsAsciiLetterOrDigit);
    static bool PositiveId(string id, out long value) => long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
    static string Decode(string value)
    {
        if (value.Length > 1400 * 1024) throw new IOException("PayloadTooLarge");
        try { return new UTF8Encoding(false, true).GetString(Convert.FromBase64String(value)).TrimStart('\uFEFF'); }
        catch (FormatException) { throw new JsonException("InvalidBase64"); }
    }
    static void CheckCode(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
        if (root.TryGetProperty("code", out var code))
        {
            if (code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out int value)) throw new JsonException("InvalidStatusCode");
            if (value is not (0 or 200)) throw new IOException("SourceDenied");
        }
        if (root.TryGetProperty("error", out var error) && error.ValueKind is not (JsonValueKind.Null or JsonValueKind.False)) throw new IOException("SourceDenied");
        if (root.TryGetProperty("success", out var success) && success.ValueKind != JsonValueKind.True) throw new IOException("SourceDenied");
    }
    static void CheckKugou(JsonElement root)
    {
        CheckCode(root);
        foreach (string name in new[] { "status", "errcode", "error_code" })
        {
            if (!root.TryGetProperty(name, out var code)) continue;
            if (code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out int value)) throw new JsonException("InvalidStatusCode");
            if (name == "status" ? value is not (1 or 200) : value != 0) throw new IOException("SourceDenied");
        }
    }
    static JsonElement Required(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var field)) throw new JsonException("MissingResponseField");
        return field;
    }
    static string RequiredText(JsonElement root, string name)
    {
        JsonElement field = Required(root, name);
        return field.ValueKind switch { JsonValueKind.String => field.GetString()!, JsonValueKind.Null => "", _ => throw new JsonException("InvalidResponseField") };
    }
    static JsonElement RequiredArray(JsonElement root, string name)
    {
        JsonElement field = Required(root, name);
        if (field.ValueKind != JsonValueKind.Array) throw new JsonException("InvalidResponseField");
        return field;
    }
    static string DeezerLrc(JsonElement root)
    {
        CheckCode(root);
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind != JsonValueKind.Null
            && (errors.ValueKind != JsonValueKind.Array || errors.GetArrayLength() != 0)) throw new IOException("SourceDenied");
        JsonElement track = Required(Required(root, "data"), "track");
        if (track.ValueKind == JsonValueKind.Null) return "";
        JsonElement lyrics = Required(track, "lyrics");
        if (lyrics.ValueKind == JsonValueKind.Null) return "";
        return string.Join('\n', RequiredArray(lyrics, "synchronizedLines").EnumerateArray().Take(5000)
            .Select(line => RequiredText(line, "lrcTimestamp") + RequiredText(line, "line")));
    }
    static string ChildText(JsonElement from, string child, string field)
        => from.ValueKind == JsonValueKind.Object && from.TryGetProperty(child, out var value) ? Text(value, field) : "";
    static string Text(JsonElement from, string name) => from.ValueKind != JsonValueKind.Object || !from.TryGetProperty(name, out var value) ? ""
        : value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : "";
    static double Number(JsonElement from, string name) => from.ValueKind == JsonValueKind.Object && from.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) ? number : 0;
    static IEnumerable<JsonElement> Items(JsonElement from, params string[] path)
    {
        foreach (string name in path)
            if (from.ValueKind != JsonValueKind.Object || !from.TryGetProperty(name, out from)) return [];
        return from.ValueKind == JsonValueKind.Array ? from.EnumerateArray() : [];
    }
}
