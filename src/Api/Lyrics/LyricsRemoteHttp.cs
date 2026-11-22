using System.IO;
using System.Net.Http;
using System.Text;

namespace DynamicIsland;

internal sealed class LyricsRemoteCoordinator
{
    internal readonly SemaphoreSlim Gate = new(1, 1);
    internal DateTimeOffset RetryAt;
    internal DateTimeOffset LastRequest;
}

internal sealed class LyricsRemoteHttp(HttpClient? http, LyricsRemoteCoordinator coordinator)
{
    static readonly HttpClient Shared = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = Timeout.InfiniteTimeSpan };

    internal Task<string> GetAsync(string url, CancellationToken token)
        => SendAsync(url, token);

    internal async Task<string> SendAsync(string url, CancellationToken token, string? site = null,
        string? jsonBody = null, string? bearer = null, int maxBodyBytes = 2 * 1024 * 1024)
    {
        await coordinator.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (coordinator.RetryAt > DateTimeOffset.UtcNow) throw new HttpRequestException("SourceCooldown");
            TimeSpan spacing = TimeSpan.FromMilliseconds(300) - (DateTimeOffset.UtcNow - coordinator.LastRequest);
            if (spacing > TimeSpan.Zero) await Task.Delay(spacing, token).ConfigureAwait(false);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            using var request = new HttpRequestMessage(jsonBody is null ? HttpMethod.Get : HttpMethod.Post, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) " + AppVersion.Product);
            request.Headers.Referrer = new Uri(site ?? "https://music.163.com/");
            request.Headers.Accept.ParseAdd("application/json");
            if (jsonBody is not null) request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            if (bearer is not null) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
            coordinator.LastRequest = DateTimeOffset.UtcNow;
            using var response = await (http ?? Shared).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is 403 or 429 or 503)
                coordinator.RetryAt = DateTimeOffset.UtcNow.AddMinutes(1);
            response.EnsureSuccessStatusCode();
            if ((int)response.StatusCode is >= 300 and < 400) throw new HttpRequestException("Redirect refused");
            if (response.Content.Headers.ContentLength > maxBodyBytes) throw new IOException("PayloadTooLarge");
            await using Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var body = new MemoryStream();
            byte[] buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) > 0)
            {
                if (body.Length + read > maxBodyBytes) throw new IOException("PayloadTooLarge");
                body.Write(buffer, 0, read);
            }
            return Encoding.UTF8.GetString(body.ToArray());
        }
        finally { coordinator.Gate.Release(); }
    }
}

internal static class LyricsSecondaryQueries
{
    internal static LyricsQuery[] For(TrackSnapshot track)
    {
        LyricsQuery[] plan = LyricsNormalizer.SearchQueries(track);
        if (plan.Length == 0) return [];
        var metadata = LyricsNormalizer.Normalize(track.Title, track.Artist);
        return plan.Where(query => query.Kind is not (LyricsQueryKind.FreeText or LyricsQueryKind.BaseTitleFallback))
            .DistinctBy(query => query.Artist + "\u001f" + query.Title).Take(2)
            .Append(new("", LyricsQueryKind.FreeText) { Title = LyricsNormalizer.DiscoveryTitle(metadata), Artist = "" })
            .DistinctBy(query => query.Artist + "\u001f" + query.Title).ToArray();
    }
}
