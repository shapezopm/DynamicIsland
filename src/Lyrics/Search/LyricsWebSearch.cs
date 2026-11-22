using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace DynamicIsland;

// Web discovery is a fallback, never a source of synthetic timestamps.
internal sealed class LyricsWebSearch : ILyricsProvider
{
    static readonly HttpClient SharedHttp = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = Timeout.InfiniteTimeSpan };
    static readonly LyricsWebCoordinator SharedCoordinator = new();
    readonly LyricsWebCoordinator _coordinator;
    readonly HttpClient _http;
    static Regex Pattern(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline
        | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    static readonly Regex Links = Pattern(@"https://genius\.com/[a-z0-9%_\-]+-lyrics");
    static readonly Regex Tags = Pattern(@"<[^>]+>");
    static readonly Regex Divs = Pattern(@"</?div\b[^>]*>");
    static readonly Regex Containers = Pattern(@"<div\b[^>]*data-lyrics-container\s*=\s*['"" ]*true['"" ][^>]*>");
    static readonly Regex Title = Pattern(@"<title\b[^>]*>(.*?)</title>");
    static readonly Regex Breaks = Pattern(@"<br\s*/?>|</p\s*>");
    static readonly Regex UnsafeBlocks = Pattern(@"<(script|style)\b[^>]*>.*?</\1\s*>");

    internal LyricsWebSearch(HttpClient? http = null, LyricsWebCoordinator? coordinator = null)
    { _http = http ?? SharedHttp; _coordinator = coordinator ?? SharedCoordinator; }

    internal static string BrowserUrl(TrackSnapshot track, bool cleaned = false)
    {
        LyricsQuery[] queries = LyricsNormalizer.BuildQueries(track);
        string? main = LyricsNormalizer.TryTitleCredit(LyricsNormalizer.Normalize(track.Title, track.Artist))?.MainArtist;
        LyricsQuery? query = cleaned ? queries.FirstOrDefault(q => q.Kind == LyricsQueryKind.CreditedArtist && q.Artist == main)
            ?? queries.FirstOrDefault(q => q.Kind == LyricsQueryKind.CreditedArtist)
            ?? queries.FirstOrDefault(q => q.Kind == LyricsQueryKind.Normalized) : queries.FirstOrDefault();
        string text = query is null ? "" : query.Artist + " " + query.Title;
        return "https://www.google.com/search?q=" + Uri.EscapeDataString("site:genius.com " + text + " lyrics");
    }

    public async Task<ProviderSearchResult> SearchAsync(TrackSnapshot track, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(8));
        var candidates = new List<LyricsCandidate>();
        bool entered = false;
        try
        {
            await _coordinator.Gate.WaitAsync(budget.Token).ConfigureAwait(false);
            entered = true;
            if (_coordinator.BlockedUntil > DateTimeOffset.UtcNow)
                return new([], LyricsStatus.ProviderError, false, ErrorCode: "WebAccessBlocked");
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Search the provider directly; raw metadata precedes supported cleaned variants.
            foreach (LyricsQuery query in LyricsSecondaryQueries.For(track))
            {
                string url = "https://genius.com/api/search/song?per_page=5&q="
                    + Uri.EscapeDataString(query.Artist + " " + query.Title);
                using var search = System.Text.Json.JsonDocument.Parse(await ReadAsync(url, budget.Token, json: true).ConfigureAwait(false));
                foreach (Uri page in SearchPages(search.RootElement, track).Where(page => visited.Add(page.AbsoluteUri)))
                {
                    if (visited.Count > 2) break;
                    await Task.Delay(300, budget.Token).ConfigureAwait(false);
                    LyricsCandidate? candidate = ParsePage(page,
                        await ReadAsync(page.AbsoluteUri, budget.Token).ConfigureAwait(false));
                    if (candidate is not null) candidates.Add(candidate);
                    break;
                }
                if (candidates.Count > 0 && LyricsMatcher.Match(track, LyricsMatcher.PrepareTrack(track),
                    LyricsMatcher.Prepare(new(candidates.ToArray(), LyricsStatus.AwaitingVerification, true))).Status == LyricsStatus.Plain
                    || visited.Count >= 2) break;
                await Task.Delay(300, budget.Token).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (visited.Count > 0 && candidates.Count == 0)
                return new([], LyricsStatus.ProviderError, false, ErrorCode: "WebPageUnreadable");
            return new(candidates.ToArray(), candidates.Count > 0 ? LyricsStatus.AwaitingVerification : LyricsStatus.NotFound, true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(candidates.ToArray(), LyricsStatus.ProviderError, false, ErrorCode: "WebBudgetExceeded"); }
        catch (Exception exception) when (exception is HttpRequestException or IOException or RegexMatchTimeoutException or System.Text.Json.JsonException or InvalidOperationException)
        { return new(candidates.ToArray(), LyricsStatus.ProviderError, false, ErrorCode: "WebAccessBlocked"); }
        finally { if (entered) _coordinator.Gate.Release(); }
    }

    async Task<string> ReadAsync(string url, CancellationToken token, bool json = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(AppVersion.Product);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        // Do not follow redirects into login, challenges or arbitrary hosts.
        if ((int)response.StatusCode is 403 or 429)
            _coordinator.BlockedUntil = DateTimeOffset.UtcNow.AddMinutes(5);
        response.EnsureSuccessStatusCode();
        if ((int)response.StatusCode is >= 300 and < 400) throw new HttpRequestException("Redirect refused");
        if (json ? response.Content.Headers.ContentType?.MediaType != "application/json"
            : response.Content.Headers.ContentType?.MediaType is not ("text/html" or "application/xhtml+xml"))
            throw new IOException("Unexpected page content type");
        if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw new IOException("Page too large");
        await using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (bytes.Length + count > 2 * 1024 * 1024) throw new IOException("Page too large");
            bytes.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    internal static Uri[] Discover(string html) => Links.Matches(WebUtility.HtmlDecode(html))
        .Select(match => Uri.TryCreate(match.Value, UriKind.Absolute, out Uri? page) ? page : null)
        .Where(page => page is not null && IsLyricsPage(page)).Cast<Uri>().DistinctBy(page => page.AbsoluteUri)
        .Take(2).ToArray();

    internal static Uri[] SearchPages(System.Text.Json.JsonElement root, TrackSnapshot track)
    {
        if (!root.TryGetProperty("response", out var response)) throw new System.Text.Json.JsonException();
        var items = new List<System.Text.Json.JsonElement>();
        if (response.TryGetProperty("hits", out var hits) && hits.ValueKind == System.Text.Json.JsonValueKind.Array)
            items.AddRange(hits.EnumerateArray().Take(5));
        else if (response.TryGetProperty("sections", out var sections) && sections.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var section in sections.EnumerateArray().Take(5))
                if (section.TryGetProperty("hits", out hits) && hits.ValueKind == System.Text.Json.JsonValueKind.Array)
                    items.AddRange(hits.EnumerateArray().Take(5));
        }
        else throw new System.Text.Json.JsonException();
        var pages = new List<Uri>();
        foreach (var item in items.Take(25))
        {
            if (!item.TryGetProperty("result", out var song) || !song.TryGetProperty("url", out var url)
                || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out Uri? page) || !IsLyricsPage(page)
                || !song.TryGetProperty("title", out var title)
                || !song.TryGetProperty("primary_artist", out var artist) || !artist.TryGetProperty("name", out var name)) continue;
            var probe = new LyricsCandidate("Genius", null, title.GetString() ?? "", name.GetString() ?? "", "", 0, 0, [], "metadata probe", false);
            if (LyricsMatcher.CanFetchDocument(track, LyricsMatcher.PrepareTrack(track), probe))
                pages.Add(page);
        }
        return pages.DistinctBy(page => page.AbsoluteUri).Take(2).ToArray();
    }

    internal static bool IsLyricsPage(Uri page) => page.Scheme == "https" && page.IsDefaultPort
        && page.Host.Equals("genius.com", StringComparison.OrdinalIgnoreCase) && page.UserInfo.Length == 0
        && page.Query.Length == 0 && page.Fragment.Length == 0
        && Regex.IsMatch(page.AbsolutePath, @"^/[a-zA-Z0-9%_\-]+-lyrics$", RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

    internal static LyricsCandidate? ParsePage(Uri page, string html)
    {
        if (!IsLyricsPage(page) || html.Length > 2 * 1024 * 1024) return null;
        string heading = WebUtility.HtmlDecode(Title.Match(html).Groups[1].Value).Trim();
        heading = Regex.Replace(heading, @"\s+Lyrics\s*(?:\|\s*Genius(?:\s+Lyrics)?)?\s*$", "",
            RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        int dash = heading.IndexOf(" – ", StringComparison.Ordinal);
        if (dash < 1) dash = heading.IndexOf(" - ", StringComparison.Ordinal);
        if (dash < 1) return null;
        string artist = heading[..dash].Trim(), song = heading[(dash + 3)..].Trim();
        if (artist.Length is 0 or > 512 || song.Length is 0 or > 512) return null;
        var text = new StringBuilder();
        foreach (Match container in Containers.Matches(html))
        {
            int depth = 1, start = container.Index + container.Length, end = -1;
            foreach (Match div in Divs.Matches(html, start))
            {
                depth += div.Value.StartsWith("</", StringComparison.Ordinal) ? -1 : 1;
                if (depth == 0) { end = div.Index; break; }
            }
            if (end < 0) return null;
            string block = UnsafeBlocks.Replace(html[start..end], "");
            block = WebUtility.HtmlDecode(Tags.Replace(Breaks.Replace(block, "\n"), "")).Trim();
            if (block.Length > 0) text.AppendLine(block);
            if (text.Length > 512 * 1024) return null;
        }
        string plain = text.ToString().Trim();
        if (plain.Length == 0) return null;
        return new("Genius", null, song, artist, "", 0, 0, [], plain, false)
            { VersionTags = LyricsNormalizer.ExtractVersionTags(song), SourceUrl = page.AbsoluteUri };
    }
}

internal sealed class LyricsWebCoordinator
{
    internal readonly SemaphoreSlim Gate = new(1, 1);
    internal DateTimeOffset BlockedUntil;
}

internal sealed class LyricsSearchProvider(ILyricsProvider primary, ILyricsProvider fallback) : ILyricsProvider
{
    public async Task<ProviderSearchResult> SearchAsync(TrackSnapshot track, CancellationToken token)
    {
        ProviderSearchResult first = await primary.SearchAsync(track, token).ConfigureAwait(false);
        if (first.ErrorCode == "InvalidMetadata" || string.IsNullOrWhiteSpace(track.Title)) return first;
        LyricsMatch match = LyricsMatcher.Match(track, LyricsMatcher.PrepareTrack(track), LyricsMatcher.Prepare(first));
        if (match.Status is LyricsStatus.Synced or LyricsStatus.Plain or LyricsStatus.Instrumental
            || match.Candidate is { HasValidSync: true }) return first;
        ProviderSearchResult second = await fallback.SearchAsync(track, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        // Secondary source must not hide the primary error or competitors.
        if (!second.Complete)
            return first.Status == LyricsStatus.RateLimited ? first : new(
                first.Candidates.Concat(second.Candidates).Take(120).ToArray(), LyricsStatus.ProviderError,
                false, ErrorCode: second.ErrorCode);
        if (second.Candidates.Length == 0) return first;
        if (!first.Complete)
        {
            LyricsMatch web = LyricsMatcher.Match(track, LyricsMatcher.PrepareTrack(track), LyricsMatcher.Prepare(second));
            return web.Status == LyricsStatus.Plain ? second : first;
        }
        return new(first.Candidates.Concat(second.Candidates).Take(120).ToArray(), LyricsStatus.AwaitingVerification,
            first.Candidates.Length + second.Candidates.Length <= 120);
    }
}
