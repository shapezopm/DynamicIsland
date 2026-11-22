using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace DynamicIsland;

// Uses YouTube Music's unofficial catalogue interface. No account, API key or Python runtime.
internal sealed class YouTubeMusicLyricsProvider : ILyricsProvider
{
    internal const string ProviderId = "YouTube Music";
    const string Api = "https://music.youtube.com/youtubei/v1/";
    static readonly LyricsRemoteCoordinator SharedCoordinator = new();
    readonly LyricsRemoteHttp _remote;

    internal YouTubeMusicLyricsProvider(HttpClient? http = null, LyricsRemoteCoordinator? coordinator = null)
        => _remote = new(http, coordinator ?? SharedCoordinator);

    public async Task<ProviderSearchResult> SearchAsync(TrackSnapshot track, CancellationToken token)
    {
        LyricsQuery[] queries = LyricsSecondaryQueries.For(track);
        if (queries.Length == 0) return new([], LyricsStatus.NotFound, true);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(8));
        var candidates = new List<LyricsCandidate>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        PreparedLyricsTrack identity = LyricsMatcher.PrepareTrack(track);
        try
        {
            foreach (LyricsQuery query in queries)
            {
                using JsonDocument search = await Request("search", new
                {
                    query = (query.Artist + " " + query.Title).Trim(),
                    @params = "EgWKAQIIAWoKEAkQBRAKEAMQBA%3D%3D",
                }, false, budget.Token).ConfigureAwait(false);
                foreach (LyricsCandidate metadata in ParseSearch(search.RootElement))
                {
                    string videoId = new Uri(metadata.SourceUrl!).Query[3..];
                    if (visited.Contains(videoId)) continue;
                    if (!LyricsMatcher.CanFetchDocument(track, identity, metadata)) continue;
                    if (visited.Count == 2) return Found();
                    visited.Add(videoId);
                    using JsonDocument next = await Request("next", new
                    {
                        videoId, playlistId = "RDAMVM" + videoId, isAudioOnly = true,
                    }, false, budget.Token).ConfigureAwait(false);
                    string? browseId = LyricsBrowseId(next.RootElement);
                    if (browseId is null) continue;
                    using JsonDocument lyrics = await Request("browse", new { browseId }, true, budget.Token).ConfigureAwait(false);
                    if (ParseDocument(metadata, lyrics.RootElement) is { } candidate) candidates.Add(candidate);
                }
                if (candidates.Any(candidate => candidate.HasValidSync)) break;
            }
            token.ThrowIfCancellationRequested();
            return Found();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return Failure("YouTubeMusicTimeout"); }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or InvalidOperationException or FormatException)
        { return Failure("YouTubeMusicUnavailable"); }

        ProviderSearchResult Found() => new(candidates.ToArray(), candidates.Count > 0
            ? LyricsStatus.AwaitingVerification : LyricsStatus.NotFound, true);
        ProviderSearchResult Failure(string code) => candidates.Count > 0
            ? Found() with { ErrorCode = code, NeedsRefresh = true }
            : new([], LyricsStatus.ProviderError, false, ErrorCode: code);
    }

    async Task<JsonDocument> Request(string endpoint, object body, bool mobile, CancellationToken token)
    {
        var fields = JsonSerializer.SerializeToElement(body).EnumerateObject()
            .ToDictionary(property => property.Name, property => (object)property.Value, StringComparer.Ordinal);
        fields["context"] = new
        {
            client = new
            {
                clientName = mobile ? "ANDROID_MUSIC" : "WEB_REMIX",
                clientVersion = mobile ? "7.21.50" : "1." + DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".01.00",
                hl = "en", gl = "US",
            },
            user = new { },
        };
        string raw = await _remote.SendAsync(Api + endpoint + "?alt=json", token, "https://music.youtube.com/",
            JsonSerializer.Serialize(fields), maxBodyBytes: 4 * 1024 * 1024).ConfigureAwait(false);
        JsonDocument document = JsonDocument.Parse(raw, new() { MaxDepth = 64 });
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || document.RootElement.TryGetProperty("error", out _)
            || !ValidResponse(endpoint, document.RootElement))
        { document.Dispose(); throw new IOException("YouTubeMusicResponseError"); }
        return document;
    }

    static bool ValidResponse(string endpoint, JsonElement root)
    {
        if (endpoint == "search")
        {
            if (At(root, "contents", "sectionListRenderer", "contents").ValueKind == JsonValueKind.Array) return true;
            JsonElement tabs = At(root, "contents", "tabbedSearchResultsRenderer", "tabs");
            return tabs.ValueKind == JsonValueKind.Array && tabs.EnumerateArray().Any(tab =>
                At(tab, "tabRenderer", "content", "sectionListRenderer", "contents").ValueKind == JsonValueKind.Array);
        }
        if (endpoint == "next")
        {
            JsonElement tabs = At(root, "contents", "singleColumnMusicWatchNextResultsRenderer", "tabbedRenderer", "watchNextTabbedResultsRenderer", "tabs");
            return tabs.ValueKind == JsonValueKind.Array && tabs.GetArrayLength() > 0;
        }
        return endpoint == "browse" && (At(root, "contents", "elementRenderer", "newElement", "type", "componentType", "model", "timedLyricsModel", "lyricsData", "timedLyricsData").ValueKind == JsonValueKind.Array
            || Named(At(root, "contents"), "musicDescriptionShelfRenderer").Any(shelf =>
                At(shelf, "description", "runs").ValueKind == JsonValueKind.Array));
    }

    internal static LyricsCandidate[] ParseSearch(JsonElement root)
    {
        var candidates = new List<LyricsCandidate>();
        foreach (JsonElement row in Named(root, "musicResponsiveListItemRenderer").Take(10))
        {
            JsonElement columns = At(row, "flexColumns");
            if (columns.ValueKind != JsonValueKind.Array || columns.GetArrayLength() < 2) continue;
            JsonElement titleRuns = At(columns[0], "musicResponsiveListItemFlexColumnRenderer", "text", "runs");
            if (titleRuns.ValueKind != JsonValueKind.Array) continue;
            string title = string.Concat(titleRuns.EnumerateArray().Select(run => Text(run, "text")));
            string? videoId = Text(At(row, "playlistItemData"), "videoId")
                ?? titleRuns.EnumerateArray().Select(run => Text(At(run, "navigationEndpoint", "watchEndpoint"), "videoId")).FirstOrDefault(id => id is not null);
            if (!ValidVideoId(videoId)) continue;
            var artists = new List<string>();
            string album = "";
            double duration = 0;
            foreach (JsonElement column in columns.EnumerateArray().Skip(1))
            {
                JsonElement runs = At(column, "musicResponsiveListItemFlexColumnRenderer", "text", "runs");
                if (runs.ValueKind != JsonValueKind.Array) continue;
                foreach (JsonElement run in runs.EnumerateArray())
                {
                    string value = Text(run, "text") ?? "";
                    JsonElement browse = At(run, "navigationEndpoint", "browseEndpoint");
                    string? page = Text(At(browse, "browseEndpointContextSupportedConfigs", "browseEndpointContextMusicConfig"), "pageType");
                    if (page == "MUSIC_PAGE_TYPE_ARTIST" && value.Length > 0 && artists.Count < 8) artists.Add(value);
                    else if (page == "MUSIC_PAGE_TYPE_ALBUM") album = value;
                    else if (Duration(value) is > 0 and var seconds) duration = seconds;
                }
            }
            string artist = string.Join(", ", artists);
            if (!LyricsSearchPreferences.Valid(title, artist) || artist.Length == 0 || album.Length > 512 || duration <= 0) continue;
            candidates.Add(new(ProviderId, null, title, artist, album, duration, 0, [], null, false)
            {
                SourceUrl = "https://music.youtube.com/watch?v=" + videoId,
                VersionTags = LyricsNormalizer.ExtractVersionTags(title),
            });
        }
        return candidates.DistinctBy(candidate => candidate.SourceUrl).ToArray();
    }

    internal static LyricsCandidate? ParseDocument(LyricsCandidate metadata, JsonElement root)
    {
        JsonElement data = At(root, "contents", "elementRenderer", "newElement", "type", "componentType", "model", "timedLyricsModel", "lyricsData", "timedLyricsData");
        if (data.ValueKind == JsonValueKind.Array)
        {
            if (data.GetArrayLength() is 0 or > 2500) return null;
            var cues = new List<(string Text, long Start, long End)>();
            bool timed = true;
            foreach (JsonElement item in data.EnumerateArray())
            {
                string? text = Text(item, "lyricLine");
                if (text is null || text.Length > 4096) return null;
                JsonElement range = At(item, "cueRange");
                long? start = Milliseconds(range, "startTimeMilliseconds"), end = Milliseconds(range, "endTimeMilliseconds");
                if (start is null || end is null || end < start || cues.Count > 0 && start < cues[^1].Start) timed = false;
                cues.Add((text, start ?? 0, end ?? 0));
            }
            string plain = string.Join('\n', cues.Select(cue => cue.Text)).Trim();
            if (plain.Length is 0 or > 65536) return null;
            if (!timed || !cues.Any(cue => cue.Start > 0)) return metadata with { PlainLyrics = plain };
            var lines = new List<LyricsService.Line>();
            for (int i = 0; i < cues.Count; i++)
            {
                var cue = cues[i];
                lines.Add(new(TimeSpan.FromMilliseconds(cue.Start), cue.Text));
                if (cue.End > cue.Start && (i == cues.Count - 1 || cue.End < cues[i + 1].Start))
                    lines.Add(new(TimeSpan.FromMilliseconds(cue.End), ""));
            }
            double endSeconds = lines[^1].Time.TotalSeconds;
            if (metadata.Duration > 0 && endSeconds > metadata.Duration + 1) return metadata with { PlainLyrics = plain };
            return metadata with { PlainLyrics = plain, Lines = lines.ToArray(), HasValidSync = true, End = endSeconds };
        }

        foreach (JsonElement shelf in Named(At(root, "contents"), "musicDescriptionShelfRenderer"))
        {
            JsonElement runs = At(shelf, "description", "runs");
            if (runs.ValueKind != JsonValueKind.Array) continue;
            string plain = string.Concat(runs.EnumerateArray().Select(run => Text(run, "text"))).Trim();
            if (plain.Length is > 0 and <= 65536) return metadata with { PlainLyrics = plain };
        }
        return null;
    }

    static string? LyricsBrowseId(JsonElement root)
    {
        foreach (JsonElement endpoint in Named(root, "browseEndpoint"))
        {
            string? page = Text(At(endpoint, "browseEndpointContextSupportedConfigs", "browseEndpointContextMusicConfig"), "pageType");
            string? id = Text(endpoint, "browseId");
            if (page == "MUSIC_PAGE_TYPE_TRACK_LYRICS" && id is { Length: > 6 and <= 256 }
                && id.StartsWith("MPLYt_", StringComparison.Ordinal) && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')) return id;
        }
        return null;
    }

    internal static bool ValidVideoId(string? id) => id is { Length: 11 }
        && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    static long? Milliseconds(JsonElement element, string key)
    {
        JsonElement value = At(element, key);
        string? text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long ms) && ms is >= 0 and <= 86400000 ? ms : null;
    }

    static double Duration(string text)
    {
        string[] parts = text.Split(':');
        if (parts.Length is < 2 or > 3 || parts.Any(part => part.Length is < 1 or > 3 || !part.All(char.IsAsciiDigit))) return 0;
        int[] numbers = parts.Select(part => int.Parse(part, CultureInfo.InvariantCulture)).ToArray();
        if (numbers.Skip(1).Any(number => number >= 60)) return 0;
        double seconds = numbers.Aggregate(0d, (total, number) => total * 60 + number);
        return seconds <= 86400 ? seconds : 0;
    }

    static string? Text(JsonElement element, string name) => At(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    static JsonElement At(JsonElement root, params string[] path)
    {
        foreach (string key in path)
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(key, out root)) return default;
        return root;
    }

    static IEnumerable<JsonElement> Named(JsonElement root, string name)
    {
        if (root.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (property.Name == name) yield return property.Value;
                else foreach (JsonElement nested in Named(property.Value, name)) yield return nested;
            }
        else if (root.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in root.EnumerateArray())
                foreach (JsonElement nested in Named(item, name)) yield return nested;
    }
}
