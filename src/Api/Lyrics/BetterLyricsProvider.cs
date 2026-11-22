using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace DynamicIsland;

// Better Lyrics backend (https://api.betterlyrics.org/getLyrics). Unofficial and undocumented for third parties.
// Answers are TTML; per the backend README, misses are resolved upstream, so this source can carry lrc.red data.
internal sealed class BetterLyricsProvider : ILyricsProvider
{
    internal const string ProviderId = "BetterLyrics";
    const string Endpoint = "https://api.betterlyrics.org/getLyrics";
    const int MaxBodyBytes = 2 * 1024 * 1024;
    static readonly TimeSpan Budget = TimeSpan.FromSeconds(8);
    static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);
    static readonly HttpClient SharedHttp = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = Timeout.InfiniteTimeSpan };
    static readonly object Sync = new();
    static DateTimeOffset _cooldownUntil;
    readonly HttpClient _http;

    public BetterLyricsProvider(HttpClient? http = null) => _http = http ?? SharedHttp;

    public async Task<ProviderSearchResult> SearchAsync(TrackSnapshot track, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(track);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(track.Title)) return new([], LyricsStatus.NotFound, true);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        lock (Sync)
            if (_cooldownUntil > now) return new([], LyricsStatus.RateLimited, false, _cooldownUntil, "ProviderCooldown429");

        var query = new List<string>
        {
            "s=" + Uri.EscapeDataString(track.Title),
            "a=" + Uri.EscapeDataString(track.Artist ?? ""),
        };
        if (!string.IsNullOrWhiteSpace(track.Album)) query.Add("al=" + Uri.EscapeDataString(track.Album));
        if (track.Duration.CanMatch) query.Add("d=" + track.Duration.Value.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(Budget);
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint + "?" + string.Join("&", query));
        request.Headers.UserAgent.ParseAdd(AppVersion.Product + " (https://github.com/mihailkotovski/DynamicIsland)");
        request.Headers.Accept.ParseAdd("application/json");

        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token)
                .ConfigureAwait(false);
            int code = (int)response.StatusCode;
            if (code is 403 or 429)
            {
                DateTimeOffset until = DateTimeOffset.UtcNow + Cooldown;
                lock (Sync) _cooldownUntil = until;
                return new([], LyricsStatus.RateLimited, false, until, "Http" + code);
            }
            // Only the public cache is used. An uncached title can require authorization.
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent or HttpStatusCode.Unauthorized)
                return new([], LyricsStatus.NotFound, true);
            if (code is >= 300 and < 400) return new([], LyricsStatus.ProviderError, false, ErrorCode: "RedirectBlocked");
            if (!response.IsSuccessStatusCode) return new([], LyricsStatus.ProviderError, false, ErrorCode: "Http" + code);

            string? type = response.Content.Headers.ContentType?.MediaType;
            if (type is null || !(type.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                || type.EndsWith("+json", StringComparison.OrdinalIgnoreCase)))
                return new([], LyricsStatus.ProviderError, false, ErrorCode: "UnexpectedContentType");
            if (response.Content.Headers.ContentLength > MaxBodyBytes)
                return new([], LyricsStatus.ProviderError, false, ErrorCode: "PayloadTooLarge");

            string body = await response.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false);
            if (body.Length > MaxBodyBytes) return new([], LyricsStatus.ProviderError, false, ErrorCode: "PayloadTooLarge");

            using JsonDocument json = JsonDocument.Parse(body, new() { MaxDepth = 32 });
            if (json.RootElement.ValueKind != JsonValueKind.Object) return new([], LyricsStatus.ProviderError, false, ErrorCode: "UnexpectedJsonShape");
            string? ttml = json.RootElement.TryGetProperty("ttml", out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;

            LyricsService.Line[] lines = TtmlParser.Parse(ttml);
            if (lines.Length == 0) return new([], LyricsStatus.NotFound, true);
            // The endpoint returns no record metadata; the candidate carries the requested identity and no verified duration.
            var candidate = new LyricsCandidate(ProviderId, null, track.Title, track.Artist ?? "", track.Album ?? "",
                0, lines[^1].Time.TotalSeconds, lines, null, false) { HasValidSync = true };
            return new([candidate], LyricsStatus.AwaitingVerification, true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new([], LyricsStatus.ProviderError, false, ErrorCode: "RequestTimeout");
        }
        catch (HttpRequestException) { return new([], LyricsStatus.ProviderError, false, ErrorCode: "NetworkError"); }
        catch (JsonException) { return new([], LyricsStatus.ProviderError, false, ErrorCode: "MalformedJson"); }
    }
}
