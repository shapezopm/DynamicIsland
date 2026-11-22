using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace DynamicIsland;

internal sealed class NetEaseLyricsProvider : ILyricsProvider
{
    static readonly LyricsRemoteCoordinator SharedCoordinator = new();
    readonly LyricsRemoteHttp _remote;
    internal NetEaseLyricsProvider(HttpClient? http = null, LyricsRemoteCoordinator? coordinator = null)
        => _remote = new(http, coordinator ?? SharedCoordinator);

    public async Task<ProviderSearchResult> SearchAsync(TrackSnapshot track, CancellationToken cancellationToken)
    {
        LyricsQuery[] queries = LyricsSecondaryQueries.For(track);
        if (queries.Length == 0) return new([], LyricsStatus.NotFound, true);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(8));
        var candidates = new List<LyricsCandidate>();
        var visited = new HashSet<long>();
        PreparedLyricsTrack identity = LyricsMatcher.PrepareTrack(track);
        try
        {
            foreach (LyricsQuery query in queries)
            {
                string url = "https://music.163.com/api/cloudsearch/pc?type=1&limit=10&s="
                    + Uri.EscapeDataString(query.Artist + " " + query.Title);
                using JsonDocument search = JsonDocument.Parse(await _remote.GetAsync(url, budget.Token).ConfigureAwait(false), new() { MaxDepth = 32 });
                JsonElement root = search.RootElement;
                if (root.TryGetProperty("code", out var code) && code.GetInt32() != 200) throw new IOException("SourceDenied");
                if (!root.TryGetProperty("result", out var result) || !result.TryGetProperty("songs", out var songs)) continue;
                if (songs.ValueKind != JsonValueKind.Array) throw new JsonException();
                foreach (JsonElement song in songs.EnumerateArray().Take(10))
                {
                    LyricsCandidate? metadata = ParseMetadata(song);
                    if (metadata?.RecordId is not long id || visited.Contains(id)) continue;
                    if (!LyricsMatcher.CanFetchDocument(track, identity, metadata)) continue;
                    if (visited.Count == 2) return Found();
                    visited.Add(id);
                    using JsonDocument text = JsonDocument.Parse(await _remote.GetAsync(
                        "https://music.163.com/api/song/lyric?lv=-1&kv=-1&tv=-1&id=" + id,
                        budget.Token).ConfigureAwait(false), new() { MaxDepth = 32 });
                    LyricsCandidate? candidate = ParseDocument(metadata, text.RootElement);
                    if (candidate is not null) candidates.Add(candidate);
                }
                if (candidates.Any(candidate => candidate.HasValidSync)) break;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return Found();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(candidates.ToArray(), LyricsStatus.ProviderError, false, ErrorCode: "NetEaseTimeout"); }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or InvalidOperationException or FormatException)
        { return new(candidates.ToArray(), LyricsStatus.ProviderError, false, ErrorCode: "NetEaseUnavailable"); }

        ProviderSearchResult Found() => new(candidates.ToArray(), candidates.Count > 0
            ? LyricsStatus.AwaitingVerification : LyricsStatus.NotFound, true);
    }

    internal static LyricsCandidate? ParseMetadata(JsonElement song)
    {
        if (!song.TryGetProperty("id", out var identifier) || !identifier.TryGetInt64(out long id) || id <= 0) return null;
        string title = song.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "";
        if (!(song.TryGetProperty("ar", out var artists) || song.TryGetProperty("artists", out artists)) || artists.ValueKind != JsonValueKind.Array) return null;
        string artist = string.Join(", ", artists.EnumerateArray().Take(8)
            .Select(item => item.TryGetProperty("name", out var by) ? by.GetString() ?? "" : ""));
        string album = (song.TryGetProperty("al", out var release) || song.TryGetProperty("album", out release)) && release.TryGetProperty("name", out var albumName)
            ? albumName.GetString() ?? "" : "";
        double duration = (song.TryGetProperty("dt", out var seconds) || song.TryGetProperty("duration", out seconds)) && seconds.TryGetDouble(out double ms) ? ms / 1000 : 0;
        if (!LyricsSearchPreferences.Valid(title, artist) || album.Length > 512 || !double.IsFinite(duration)
            || duration is < 0 or > 86400) return null;
        return new("NetEase", id, title, artist, album, duration, 0, [], null, false)
            { VersionTags = LyricsNormalizer.ExtractVersionTags(title) };
    }

    internal static LyricsCandidate? ParseDocument(LyricsCandidate metadata, JsonElement root)
    {
        if (root.TryGetProperty("code", out var code) && code.GetInt32() != 200) throw new IOException("SourceDenied");
        if (root.TryGetProperty("nolyric", out var instrumental) && instrumental.ValueKind == JsonValueKind.True)
            return metadata with { IsInstrumental = true };
        string? text = root.TryGetProperty("lrc", out var lrc) && lrc.TryGetProperty("lyric", out var lyric) ? lyric.GetString() : null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1024 * 1024) return null;
        LrcParseResult parsed = LrcParser.Parse(text);
        if (!parsed.IsUsable) return System.Text.RegularExpressions.Regex.IsMatch(text, @"\[\d{1,3}:",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
            ? null : metadata with { PlainLyrics = text.Trim() };
        return metadata with { Lines = parsed.Lines, HasValidSync = true, TimingWarnings = parsed.Warnings,
            End = parsed.LastTimestamp.TotalSeconds, PlainLyrics = string.Join('\n', parsed.Lines.Select(line => line.Text)) };
    }
}
