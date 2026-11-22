using System.Net.Http;

namespace DynamicIsland;

internal static class LyricsSources
{
    internal static ILyricsProvider Create(LyricSource selected, HttpClient? http = null, TimeProvider? time = null,
        LyricsRemoteCoordinator? youtubeCoordinator = null)
    {
        selected = Settings.Known(selected);
        var chain = new List<ILyricsProvider>();
        if (selected.HasFlag(LyricSource.Lrclib)) chain.Add(new LrclibProvider(http, time));
        if (selected.HasFlag(LyricSource.Karalyr)) chain.Add(LrclibProvider.Karalyr(http, time));
        if (selected.HasFlag(LyricSource.BetterLyrics)) chain.Add(new BetterLyricsProvider(http));
        if (selected.HasFlag(LyricSource.AmllTtmlDb)) chain.Add(LrclibProvider.Amll(http, time));
        if (selected.HasFlag(LyricSource.LrcMux)) chain.Add(new LrcMuxProvider(http));
        var catalogues = new List<ILyricsProvider>();
        if (selected.HasFlag(LyricSource.YouTubeMusic)) catalogues.Add(new YouTubeMusicLyricsProvider(http, youtubeCoordinator));
        if (selected.HasFlag(LyricSource.Deezer)) catalogues.Add(new CatalogueLyricsProvider(LyricSource.Deezer, http));
        if (selected.HasFlag(LyricSource.NetEase)) catalogues.Add(new NetEaseLyricsProvider(http));
        foreach (LyricSource source in new[] { LyricSource.QqMusic, LyricSource.Kugou, LyricSource.Paxsenix })
            if (selected.HasFlag(source)) catalogues.Add(new CatalogueLyricsProvider(source, http));
        // Last in the group: LiriQo can wait out its 8 s on a miss, and it should not take a slot from the fast hosts.
        if (selected.HasFlag(LyricSource.LiriQo)) catalogues.Add(new LiriQoProvider(http));
        if (catalogues.Count > 0) chain.Add(new LyricsCatalogueGroup(catalogues.ToArray()) { CollectAlternatives = true });
        if (selected.HasFlag(LyricSource.Genius)) chain.Add(new LyricsWebSearch(http));
        return new LyricsProviderChain(chain.ToArray()) { CollectAlternatives = true };
    }

    internal static bool Enabled(string provider, LyricSource sources) => provider switch
    {
        "LRCLIB" => sources.HasFlag(LyricSource.Lrclib), "Karalyr" => sources.HasFlag(LyricSource.Karalyr), "Better Lyrics" => sources.HasFlag(LyricSource.BetterLyrics),
        "AMLL TTML DB" => sources.HasFlag(LyricSource.AmllTtmlDb), "LRC Mux" => sources.HasFlag(LyricSource.LrcMux),
        "LiriQo" => sources.HasFlag(LyricSource.LiriQo), "Deezer" => sources.HasFlag(LyricSource.Deezer),
        "NetEase" => sources.HasFlag(LyricSource.NetEase), "QQ Music" => sources.HasFlag(LyricSource.QqMusic),
        "Kugou" => sources.HasFlag(LyricSource.Kugou), "Paxsenix" => sources.HasFlag(LyricSource.Paxsenix),
        "Genius" => sources.HasFlag(LyricSource.Genius), "YouTube Music" => sources.HasFlag(LyricSource.YouTubeMusic),
        "Локальный текст" => true, _ => false,
    };

    // The flag of a provider by its candidate ProviderId; the inverse of Enabled. Zero when the provider has no flag.
    internal static LyricSource Flag(string provider) => provider switch
    {
        "LRCLIB" => LyricSource.Lrclib, "Karalyr" => LyricSource.Karalyr, "Better Lyrics" => LyricSource.BetterLyrics,
        "AMLL TTML DB" => LyricSource.AmllTtmlDb, "LRC Mux" => LyricSource.LrcMux, "LiriQo" => LyricSource.LiriQo,
        "Deezer" => LyricSource.Deezer, "NetEase" => LyricSource.NetEase, "QQ Music" => LyricSource.QqMusic,
        "Kugou" => LyricSource.Kugou, "Paxsenix" => LyricSource.Paxsenix, "Genius" => LyricSource.Genius,
        "YouTube Music" => LyricSource.YouTubeMusic, _ => 0,
    };

    internal static bool ValidSource(LyricsCandidate candidate)
    {
        if (candidate.ProviderId == YouTubeMusicLyricsProvider.ProviderId)
            return candidate.RecordId is null && candidate.SourceUrl is { Length: <= 128 } youtube
                && Uri.TryCreate(youtube, UriKind.Absolute, out var video) && video.Scheme == "https"
                && video.IsDefaultPort && video.Host == "music.youtube.com" && video.UserInfo.Length == 0
                && video.AbsolutePath == "/watch" && video.Fragment.Length == 0
                && video.Query.StartsWith("?v=", StringComparison.Ordinal)
                && YouTubeMusicLyricsProvider.ValidVideoId(video.Query[3..]);
        if (candidate.ProviderId is "LRCLIB" or "Karalyr" or "AMLL TTML DB") return candidate.SourceUrl is null;
        if (candidate.ProviderId is BetterLyricsProvider.ProviderId or LrcMuxProvider.ProviderId or LiriQoProvider.ProviderId)
            return candidate.RecordId is null && candidate.SourceUrl is null;
        if (candidate.ProviderId is "NetEase" or "Deezer" or "Paxsenix") return candidate.RecordId is > 0 && candidate.SourceUrl is null;
        if (candidate.ProviderId == "Genius") return candidate.RecordId is null && candidate.Duration == 0
            && candidate.End == 0 && candidate.Lines is { Length: 0 } && !candidate.HasValidSync && !candidate.IsInstrumental
            && candidate.SourceUrl is { } genius && Uri.TryCreate(genius, UriKind.Absolute, out var page) && LyricsWebSearch.IsLyricsPage(page);
        if (candidate.SourceUrl is not { Length: <= 512 } source || !Uri.TryCreate(source, UriKind.Absolute, out var url)
            || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length > 0 || url.Query.Length > 0) return false;
        if (candidate.ProviderId == "QQ Music") return candidate.RecordId is null && url.Host == "y.qq.com"
            && url.Fragment.Length == 0 && url.AbsolutePath.StartsWith("/n/ryqq/songDetail/", StringComparison.Ordinal)
            && CatalogueLyricsProvider.Token(url.AbsolutePath["/n/ryqq/songDetail/".Length..]);
        return candidate.ProviderId == "Kugou" && candidate.RecordId is > 0 && url.Host == "www.kugou.com"
            && url.AbsolutePath == "/song/" && url.Fragment.StartsWith("#hash=", StringComparison.Ordinal)
            && url.Fragment.Length == 38 && url.Fragment[6..].All(Uri.IsHexDigit);
    }
}
