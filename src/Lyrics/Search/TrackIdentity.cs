using System.Text;
using System.Text.RegularExpressions;

namespace DynamicIsland;

internal sealed record TrackIdentity(string Title, string Artist, string Evidence)
{
    public bool RequiresDuration { get; init; }
    public string[] ArtistAliases { get; init; } = [];
    public string? PrimaryArtist { get; init; }
    public TrackSnapshot Apply(TrackSnapshot track) => track with
    {
        Title = Title, Artist = Artist, IdentityRequiresDuration = RequiresDuration, IdentityArtistAliases = ArtistAliases, IdentityPrimaryArtist = PrimaryArtist,
    };
}

internal interface ITrackIdentityResolver
{
    Task<TrackIdentity> ResolveAsync(TrackSnapshot track, CancellationToken token);
}

internal interface ITrackIdentityCatalogue
{
    TrackIdentity? FindTrack(TrackSnapshot track);
    string[] ArtistNames(string name);
}

internal static class TrackIdentityParser
{
    static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(100);
    internal static string Key(string value) => Regex.Replace(value.Normalize(NormalizationForm.FormKC), @"\s+", " ",
        RegexOptions.CultureInvariant, Budget).Trim().ToUpperInvariant();

    public static TrackIdentity Parse(TrackSnapshot track, ITrackIdentityCatalogue? catalogue = null)
    {
        var normalized = LyricsNormalizer.Normalize(track.Title, track.Artist);
        string title = normalized.Title, artist = normalized.Artist;
        var credit = LyricsNormalizer.TryTitleCredit(normalized);
        if (credit is not null)
        {
            // A nested upload credit can repeat the uploader before the real artist list.
            var nested = LyricsNormalizer.TryTitleCredit(LyricsNormalizer.Normalize(credit.Title, credit.FullArtist));
            if (nested is not null) credit = nested;
            return new(credit.Title, Key(credit.FullArtist) == Key(artist) && !artist.Contains(" + ", StringComparison.Ordinal)
                ? artist : ArtistList(credit.FullArtist, catalogue, splitAmpersand: true), "EmbeddedCredit");
        }
        int dash = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
        {
            string prefix = title[..dash].Trim(), song = title[(dash + 3)..].Trim();
            string[] members = Members(prefix);
            bool member = members.Any(value => Key(value) == Key(artist));
            bool known = catalogue?.ArtistNames(prefix).Length > 0;
            bool uploader = track.Artist.StartsWith('@') || track.Artist.Contains("t.me/", StringComparison.OrdinalIgnoreCase)
                || LyricsNormalizer.IsPlaceholderArtist(artist);
            bool upperCredit = prefix.Any(char.IsLetter) && prefix.Where(char.IsLetter).All(char.IsUpper);
            if (song.Length > 0 && prefix.Length <= 128 && (member || known || uploader || upperCredit)
                && !LyricsNormalizer.ExtractVersionTags("Song (" + song + ")").Any())
                return new(song, ArtistList(prefix, catalogue, splitAmpersand: true), member || known ? "EmbeddedCredit" : "UploadCredit")
                    { RequiresDuration = !member && !known };
        }
        return new(title, ArtistList(artist, catalogue), "NormalizedMetadata");
    }

    internal static string[] Members(string value) => Regex.Split(value, @"\s+(?:\+|&)\s+|\s*,\s*",
        RegexOptions.CultureInvariant, Budget).Select(part => part.Trim()).Where(part => part.Length > 0)
        .DistinctBy(Key).Take(8).ToArray();

    internal static string ArtistList(string value, ITrackIdentityCatalogue? catalogue = null, bool splitAmpersand = false)
    {
        value = LyricsArtistAliases.Canonicalize(value);
        // A full catalogue entry may be one band's name, including commas or ampersands.
        if (catalogue?.ArtistNames(value).Length > 0) return value;
        var members = splitAmpersand ? Members(value) : Regex.Split(value, @"\s+\+\s+|\s*,\s*", RegexOptions.CultureInvariant, Budget);
        return string.Join(", ", members.Select(part => part.Trim()).Where(part => part.Length > 0).DistinctBy(Key));
    }
}

internal sealed class TrackIdentityResolver(ITrackIdentityCatalogue? catalogue = null)
    : ITrackIdentityResolver
{
    readonly Dictionary<string, TrackIdentity> _cache = new(StringComparer.Ordinal);

    public static ITrackIdentityResolver Create() => new TrackIdentityResolver(new TrackIdentityDatabase());

    public TrackIdentity ResolveInitial(TrackSnapshot track)
    {
        string key = CacheKey(track);
        lock (_cache) if (_cache.TryGetValue(key, out var cached)) return cached;
        var identity = catalogue?.FindTrack(track) ?? TrackIdentityParser.Parse(track, catalogue);
        string[] aliases = catalogue?.ArtistNames(identity.Artist) ?? [];
        string[] members = TrackIdentityParser.Members(identity.Artist);
        bool explicitList = identity.Evidence is "EmbeddedCredit" or "SoundCloudPublisher" || track.Artist.Contains(" + ", StringComparison.Ordinal);
        return identity with { ArtistAliases = aliases, PrimaryArtist = explicitList && aliases.Length == 0 && members.Length > 1 ? members[0] : null };
    }

    static string CacheKey(TrackSnapshot track) => LyricsSearchPreferences.Key(track) + ":"
        + (track.Duration.CanMatch ? track.Duration.Value.TotalSeconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) : "?");

    public Task<TrackIdentity> ResolveAsync(TrackSnapshot track, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string key = CacheKey(track);
        lock (_cache) if (_cache.TryGetValue(key, out var cached)) return Task.FromResult(cached);
        TrackIdentity resolved = ResolveInitial(track);
        token.ThrowIfCancellationRequested();
        string[] aliases = catalogue?.ArtistNames(resolved.Artist) ?? [];
        if (aliases.Length > 0) resolved = resolved with { ArtistAliases = aliases };
        lock (_cache)
        {
            if (_cache.Count >= 128) _cache.Remove(_cache.Keys.First());
            _cache[key] = resolved;
        }
        return Task.FromResult(resolved);
    }
}
