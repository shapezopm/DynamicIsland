using System.Text;
using System.Text.RegularExpressions;

namespace DynamicIsland;

internal enum LyricsQueryKind { Exact, Normalized, ArtistSplit, CreditedArtist, FreeText, BaseTitleFallback, UploadHint }
internal sealed record LyricsQuery(string Parameters, LyricsQueryKind Kind)
{
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
}
internal sealed record NormalizedLyricsMetadata(string OriginalTitle, string OriginalArtist, string Title,
    string Artist, string BaseTitle, string[] VersionTags);
internal sealed record LyricsTitleCredit(string Title, string FullArtist, string MainArtist);

internal static class LyricsNormalizer
{
    public const int Version = 7;
    public const int MaxQueries = 6;
    public const int MaxQueryFieldLength = 512;

    const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;
    static readonly TimeSpan RegexBudget = TimeSpan.FromMilliseconds(100);
    static readonly Regex Spaces = Pattern(@"\s+");
    static readonly Regex Groups = Pattern(@"\([^()]*\)|\[[^\[\]]*\]");
    static readonly Regex NoiseOnly = Pattern(@"^(?:(?:official\s+)?(?:music\s+|lyric\s+)?video|(?:official\s+)?audio|official|lyrics?|visuali[sz]er|hd|hq|4k|(?:720|1080|1440|2160|4320)p|mv|клип|официальн(?:ый|ое)\s+(?:клип|видео|аудио)|на\s+всех\s+площадках|out\s+now)(?:\s*[,|+-]\s*(?:hd|hq|4k|(?:720|1080|1440|2160|4320)p))*$");
    static readonly Regex NoisePhrase = Pattern(@"\b(?:official\s+(?:music\s+|lyric\s+)?(?:video|audio)|music\s+video|lyric\s+video|visuali[sz]er)\b");
    static readonly Regex VersionMarker = Pattern(@"\b(?:remix|rmx|bootleg|mashup|remaster(?:ed)?|radio\s+edit|extended(?:\s+(?:mix|edit|version))?|slowed(?:\s+down)?|sped[ -]+up|speed[ -]+up|reverb|nightcore|acoustic|instrumental|cover|hardstyle|phonk|ремикс|замедлен(?:ная|ный|о)|ускорен(?:ная|ный|о)|акустическ(?:ая|ий)|инструментальн(?:ая|ый)|кавер)\b|\blive(?:$|\s+(?:at|in|from|on)\b|\s+\d)|\bmix$");
    static readonly Regex StrongSpeedTail = Pattern(@"\s+(?<version>slowed\s*(?:\+|and)\s*reverb|sped[ -]+up|speed[ -]+up)$");
    static readonly Regex Featuring = Pattern(@"(?:^|\s)(?:feat(?:uring)?\.?|ft\.?)\s+\S");
    static readonly Regex FeaturingGroup = Pattern(@"^(?:feat(?:uring)?\.?|ft\.?)\s+\S");
    static readonly Regex ChannelTopic = Pattern(@"\s*-\s*Topic$");
    static readonly Regex ChannelVevo = Pattern(@"\s*VEVO$");
    static readonly Regex RemixAlias = Pattern(@"\b(?:rmx|ремикс)\b");
    static readonly Regex CoverMarker = Pattern(@"\b(?:cover|кавер)\b");
    static readonly Regex RemasterAlias = Pattern(@"\bremastered\b");
    static readonly Regex SpeedAlias = Pattern(@"\b(?:speed[ -]+up|sped[ -]+up)\b");
    static readonly Regex SlowedAlias = Pattern(@"\bslowed\s+down\b");
    static readonly Regex PlusSpacing = Pattern(@"\s*\+\s*");
    static readonly Regex ProducerCredit = Pattern(@"^(?:prod(?:\.\s*|\s+)|produced\s+by\s+)\S(?:.*\S)?$");
    static readonly Regex VolumePercent = Pattern(@"^volume\s*:\s*(?:100|[1-9]?\d)\s*%$");
    static readonly Regex TrailingHashtags = Pattern(@"\s+#[\p{L}\p{N}_]+(?:\s*#[\p{L}\p{N}_]+)*$");
    static readonly Regex SocialLink = Pattern(@"^(?:https?://)?(?:t\.me|telegram\.me|vk\.com|instagram\.com)/\S+$");
    static readonly Regex TrailingDecoration = Pattern(@"(?:\s+[\u2580-\u259f\u2713\u2714\u221a]+)+$");
    static readonly Regex LeadingHandle = Pattern(@"^[@#](?=[\p{L}\p{N}_][\p{L}\p{N}_.-]*(?:$|\s))");
    static readonly Regex WholeHandle = Pattern(@"^@[\p{L}\p{N}_][\p{L}\p{N}_.-]*$");
    static readonly Regex ProducerHandle = Pattern(@"^(?=.{3,48}$)(?=[\p{L}\p{N}_.@-]*\p{L})(?=[\p{L}\p{N}_.@-]*\d)[\p{L}\p{N}_.@-]+$");
    static readonly Regex NumberedTitlePart = Pattern(@"^(?:part|pt|vol|volume|chapter|эпизод|часть)[._-]?\d+$");
    static readonly Regex PitchVersion = Pattern(@"^no[ -]+pitch$");
    static readonly Regex TextCompatibleVersion = Pattern(@"^(?:no[ -]+pitch|slowed(?:\s+down)?(?:\s*(?:\+|and)\s*reverb)?|sped[ -]+up|speed[ -]+up|reverb|nightcore)$");

    internal static bool HasTextCompatibleUploadEdit(NormalizedLyricsMetadata metadata)
        => metadata.VersionTags.Length > 0 && metadata.VersionTags.All(tag => TextCompatibleVersion.IsMatch(tag))
            || metadata.VersionTags.Length == 0 && HasProducerHandleSuffix(Canonical(metadata.OriginalTitle));

    internal static bool IsPlaceholderArtist(string artist) => Regex.IsMatch(artist, @"^@?user\d{4,}$", Options, RegexBudget);

    static bool HasProducerHandleSuffix(string title) => Groups.Matches(title).Cast<Match>().Any(match =>
        match.Index > 0 && match.Index + match.Length == title.Length && IsProducerHandle(match.Value[1..^1].Trim()));

    static bool IsProducerHandle(string text) => ProducerHandle.IsMatch(text) && !NumberedTitlePart.IsMatch(text) && !IsVersion(text);

    static string WithoutProducerHandle(string title) => Groups.Replace(title, match =>
        match.Index > 0 && match.Index + match.Length == title.Length && IsProducerHandle(match.Value[1..^1].Trim())
            ? "" : match.Value).Trim();

    internal static LyricsTitleCredit? DiscoveryCredit(NormalizedLyricsMetadata metadata)
        => SplitTitle(metadata.Title) is { } split ? new(split.Title, NormalizeArtist(split.Artist), NormalizeArtist(split.Artist)) : null;

    internal static string DiscoveryTitle(NormalizedLyricsMetadata metadata)
    {
        string title = WithoutProducerHandle(TryTitleCredit(metadata)?.Title ?? DiscoveryCredit(metadata)?.Title ?? metadata.Title);
        return metadata.VersionTags.Length > 0 && metadata.VersionTags.All(tag => TextCompatibleVersion.IsMatch(tag))
            ? VersionParts(title).BaseTitle : title;
    }

    public static NormalizedLyricsMetadata Normalize(string title, string artist)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(artist);
        string song = RemoveUploadNoise(Canonical(title));
        string by = NormalizeArtist(artist);
        (string baseTitle, string[] tags) = VersionParts(song);
        return new(title, artist, song, by, baseTitle, tags);
    }

    public static string[] ExtractVersionTags(string title) => VersionParts(RemoveUploadNoise(Canonical(title))).Tags;

    public static LyricsQuery[] BuildQueries(TrackSnapshot track)
    {
        ArgumentNullException.ThrowIfNull(track);
        // Never truncate a title into the identity of a different song.
        if (!SafeField(track.Title) || !SafeField(track.Artist) || string.IsNullOrWhiteSpace(track.Title)) return [];
        NormalizedLyricsMetadata metadata = Normalize(track.Title, track.Artist);
        if (metadata.Title.Length == 0) return [];
        var queries = new List<LyricsQuery>(MaxQueries);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string title, string artist, LyricsQueryKind kind, bool freeText = false)
        {
            if (queries.Count == MaxQueries || title.Length == 0 || !SafeField(title) || !SafeField(artist)) return;
            string parameters = freeText || artist.Length == 0
                ? "q=" + Uri.EscapeDataString(artist.Length == 0 ? title : artist + " " + title)
                : "track_name=" + Uri.EscapeDataString(title) + "&artist_name=" + Uri.EscapeDataString(artist);
            if (seen.Add(parameters)) queries.Add(new(parameters, kind) { Title = title, Artist = artist });
        }

        if (track.IdentityPrimaryArtist is not null || track.IdentityArtistAliases.Length > 0)
            Add(metadata.Title, metadata.Artist, LyricsQueryKind.CreditedArtist);
        Add(track.Title, track.Artist, LyricsQueryKind.Exact);
        Add(metadata.Title, metadata.Artist, LyricsQueryKind.Normalized);
        if (!string.IsNullOrWhiteSpace(track.IdentityPrimaryArtist))
            Add(metadata.Title, track.IdentityPrimaryArtist, LyricsQueryKind.ArtistSplit);
        foreach (string alias in track.IdentityArtistAliases.Where(alias => !string.Equals(alias, metadata.Artist, StringComparison.OrdinalIgnoreCase)).Take(2))
            Add(metadata.Title, alias, LyricsQueryKind.ArtistSplit);
        string withoutProducer = WithoutProducerHandle(metadata.Title);
        if (withoutProducer != metadata.Title) Add(withoutProducer, metadata.Artist, LyricsQueryKind.UploadHint);

        string creditTitle = WithoutFeaturing(metadata.Title);
        string mainArtist = NormalizeArtist(WithoutFeaturing(metadata.Artist));
        if (creditTitle != metadata.Title || mainArtist != metadata.Artist)
            Add(creditTitle, mainArtist, LyricsQueryKind.ArtistSplit);

        string? splitSong = null;
        string? splitArtist = null;
        string collaborationTitle = metadata.Title;
        if (TryTitleCredit(metadata) is { } credit)
        {
            splitSong = WithoutFeaturing(credit.Title);
            collaborationTitle = splitSong;
            splitArtist = credit.FullArtist;
            Add(splitSong, credit.FullArtist, LyricsQueryKind.CreditedArtist);
            Add(splitSong, credit.MainArtist, LyricsQueryKind.CreditedArtist);
        }
        else if (SplitTitle(metadata.Title) is { } auxiliary)
        {
            splitSong = auxiliary.Title;
            splitArtist = auxiliary.Artist;
            Add(splitSong, splitArtist, LyricsQueryKind.ArtistSplit);
        }

        string[] collaborators = metadata.Artist.Split(" + ", StringSplitOptions.TrimEntries);
        if (collaborators.Length is >= 2 and <= 8 && collaborators.All(member => member.Length is > 0 and <= 128
            && !member.Contains(" - ", StringComparison.Ordinal)))
        {
            Add(collaborationTitle, string.Join(", ", collaborators), LyricsQueryKind.CreditedArtist);
            Add(collaborationTitle, collaborators[0], LyricsQueryKind.CreditedArtist);
        }

        if (splitSong is not null || IsPlaceholderArtist(metadata.Artist))
            Add(DiscoveryTitle(metadata), "", metadata.VersionTags.Length > 0 ? LyricsQueryKind.BaseTitleFallback : LyricsQueryKind.FreeText, freeText: true);
        Add(splitSong ?? metadata.Title, splitArtist ?? metadata.Artist, LyricsQueryKind.FreeText, freeText: true);
        // A cover qualifier can be part of the published title; never search its original as a fallback.
        if (metadata.BaseTitle != metadata.Title && !metadata.VersionTags.Any(tag => CoverMarker.IsMatch(tag)))
            Add(metadata.BaseTitle, metadata.Artist, LyricsQueryKind.BaseTitleFallback);
        return queries.ToArray();
    }

    // Keep raw metadata in the diagnostic plan, but spend the network budget on cleaned identities first.
    internal static LyricsQuery[] SearchQueries(TrackSnapshot track) => BuildQueries(track)
        .OrderBy(query => query.Kind switch
        {
            LyricsQueryKind.CreditedArtist => 0, LyricsQueryKind.ArtistSplit or LyricsQueryKind.UploadHint => 1,
            LyricsQueryKind.Normalized => 2, LyricsQueryKind.Exact => 3,
            LyricsQueryKind.FreeText => 4, _ => 5,
        }).ToArray();

    public static LyricsTitleCredit? TryTitleCredit(NormalizedLyricsMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (SplitTitle(metadata.Title) is not { } split) return null;
        string whole = NormalizeArtist(split.Artist);
        string current = NormalizeArtist(metadata.Artist);
        if (whole.Length > 0 && string.Equals(whole, current, StringComparison.OrdinalIgnoreCase)
            && !split.Artist.Contains(" + ", StringComparison.Ordinal)) return new(split.Title, whole, whole);
        // Reverse order is authoritative only when the right-hand field equals the known artist.
        string rightArtist = NormalizeArtist(split.Title);
        if (current.Length > 0 && string.Equals(rightArtist, current, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(whole, current, StringComparison.OrdinalIgnoreCase))
            return new(split.Artist, rightArtist, rightArtist);
        if (LyricsArtistAliases.IsKnownAlias(split.Artist)) return new(split.Title, whole, whole);
        bool explicitCollaboration = split.Artist.Contains(" + ", StringComparison.Ordinal) || split.Artist.Contains(" & ", StringComparison.Ordinal);
        string[] rawMembers = (explicitCollaboration ? Regex.Split(split.Artist, @"\s+(?:\+|&)\s+", RegexOptions.CultureInvariant, RegexBudget) : split.Artist.Split(','))
            .Select(member => member.Trim()).ToArray();
        if (rawMembers.Any(member => member.Length == 0)) return null;
        bool markedList = rawMembers.Length > 1 && (explicitCollaboration || rawMembers.Any(member => WholeHandle.IsMatch(member)));
        string[] members = rawMembers.Select(NormalizeArtist).ToArray();
        if (members.Any(member => member.Length == 0)) return null;
        bool wholeMatches = whole.Length > 0 && string.Equals(whole, current, StringComparison.OrdinalIgnoreCase);
        bool memberMatches = current.Length > 0 && members.Any(member => string.Equals(member, current, StringComparison.OrdinalIgnoreCase));
        if (!markedList && !wholeMatches && !memberMatches) return null;
        // Commas may be part of one band name. A matching full artist is not proof of separate members.
        if (wholeMatches && !markedList) return new(split.Title, whole, whole);
        return new(split.Title, string.Join(", ", members), members[0]);
    }

    static (string Title, string Artist)? SplitTitle(string title)
    {
        int dash = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dash <= 0) return null;
        string left = title[..dash].Trim(), right = title[(dash + 3)..].Trim();
        string rightWithoutGroups = Groups.Replace(right, " ").Trim();
        if (left.Length > 128 || rightWithoutGroups.Length == 0) return null;
        // A standalone version suffix is not a credit; a song followed by its remix still is.
        if (IsVersion(rightWithoutGroups) && IsVersion(VersionParts(right).BaseTitle)) return null;
        return (right, left);
    }

    static string NormalizeArtist(string artist)
    {
        string original = Canonical(artist);
        string by = Groups.Replace(original, match => SocialLink.IsMatch(match.Value[1..^1].Trim()) ? " " : match.Value);
        by = RemoveSuffixNoise(Spaces.Replace(by, " ").Trim());
        by = ChannelTopic.Replace(by, "").Trim();
        // A channel suffix is evidence, but an artist literally called VEVO is not.
        if (by.Length > 4) by = ChannelVevo.Replace(by, "").Trim();
        by = RemoveSuffixNoise(by);
        by = LeadingHandle.Replace(by, "");
        if (by.Length == 0) by = original;
        return LyricsArtistAliases.Canonicalize(by);
    }

    static string Canonical(string text)
    {
        try { text = text.Normalize(NormalizationForm.FormKC); }
        catch (ArgumentException) { return ""; }
        text = text.Replace('\u2010', '-').Replace('\u2011', '-').Replace('\u2012', '-')
            .Replace('\u2013', '-').Replace('\u2014', '-').Replace('\u2212', '-')
            .Replace('\u2018', '\'').Replace('\u2019', '\'').Replace('\u02bc', '\'')
            .Replace('\u201c', '"').Replace('\u201d', '"');
        return Spaces.Replace(text, " ").Trim();
    }

    static string RemoveUploadNoise(string title)
    {
        string cleaned = Groups.Replace(title, match =>
        {
            string inner = match.Value[1..^1].Trim();
            if (NoiseOnly.IsMatch(inner) || ProducerCredit.IsMatch(inner) || VolumePercent.IsMatch(inner)) return " ";
            if (!IsVersion(inner)) return match.Value;
            string descriptor = NoisePhrase.Replace(inner, " ").Trim(' ', ',', '|', '-', '+');
            return match.Value[0] + Spaces.Replace(descriptor, " ") + match.Value[^1];
        });
        cleaned = RemoveSuffixNoise(Spaces.Replace(cleaned, " ").Trim());
        int dash = cleaned.LastIndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0 && NoiseOnly.IsMatch(cleaned[(dash + 3)..].Trim())) cleaned = cleaned[..dash].Trim();
        int pipe = cleaned.LastIndexOf(" | ", StringComparison.Ordinal);
        if (pipe > 0 && NoiseOnly.IsMatch(cleaned[(pipe + 3)..].Trim())) cleaned = cleaned[..pipe].Trim();
        cleaned = RemoveSuffixNoise(cleaned);
        // A song literally named Lyrics or Official Video must remain searchable.
        return cleaned.Length == 0 ? title : cleaned;
    }

    static string RemoveSuffixNoise(string text)
    {
        string cleaned = text;
        while (true)
        {
            string next = TrailingDecoration.Replace(TrailingHashtags.Replace(cleaned, ""), "").TrimEnd();
            if (next.Length == 0 || next == cleaned) return cleaned;
            cleaned = next;
        }
    }

    static (string BaseTitle, string[] Tags) VersionParts(string title)
    {
        var tags = new List<string>();
        string baseTitle = Groups.Replace(title, match =>
        {
            string inner = match.Value[1..^1].Trim();
            if (!IsVersion(inner)) return match.Value;
            tags.Add(VersionIdentity(inner));
            return " ";
        });
        baseTitle = Spaces.Replace(baseTitle, " ").Trim();
        int dash;
        while ((dash = baseTitle.LastIndexOf(" - ", StringComparison.Ordinal)) > 0
            && IsVersion(baseTitle[(dash + 3)..].Trim()))
        {
            tags.Add(VersionIdentity(baseTitle[(dash + 3)..].Trim()));
            baseTitle = baseTitle[..dash].Trim();
        }
        Match speed = StrongSpeedTail.Match(baseTitle);
        if (speed.Success && speed.Index > 0)
        {
            tags.Add(VersionIdentity(speed.Groups["version"].Value));
            baseTitle = baseTitle[..speed.Index].Trim();
        }
        if (baseTitle.Length == 0) return (title, []);
        return (baseTitle, tags.Distinct(StringComparer.Ordinal).OrderBy(tag => tag, StringComparer.Ordinal).ToArray());
    }

    static string WithoutFeaturing(string text)
    {
        string cleaned = Groups.Replace(text, match => FeaturingGroup.IsMatch(match.Value[1..^1].Trim()) ? " " : match.Value);
        cleaned = Spaces.Replace(cleaned, " ").Trim();
        Match credit = Featuring.Match(cleaned);
        if (credit.Success && credit.Index > 0)
        {
            // Retain following explicit version groups when removing a trailing credit.
            string versions = string.Join(" ", Groups.Matches(cleaned[credit.Index..]).Cast<Match>()
                .Where(group => IsVersion(group.Value[1..^1])).Select(group => group.Value));
            int dash = cleaned.LastIndexOf(" - ", StringComparison.Ordinal);
            if (dash > credit.Index && IsVersion(cleaned[(dash + 3)..].Trim()))
                versions = (versions + " - " + cleaned[(dash + 3)..].Trim()).Trim();
            cleaned = (cleaned[..credit.Index].Trim(' ', '(') + " " + versions).Trim();
        }
        return cleaned.Length == 0 ? text : cleaned;
    }

    static string VersionIdentity(string descriptor)
    {
        string identity = Canonical(NoisePhrase.Replace(descriptor, " ")).ToLowerInvariant().Trim(' ', ',', '|', '-', '+');
        identity = RemixAlias.Replace(identity, "remix");
        identity = RemasterAlias.Replace(identity, "remaster");
        identity = SpeedAlias.Replace(identity, "sped up");
        identity = SlowedAlias.Replace(identity, "slowed");
        identity = PlusSpacing.Replace(identity, " + ");
        return Spaces.Replace(identity, " ").Trim();
    }

    static bool IsVersion(string descriptor) => VersionMarker.IsMatch(descriptor) || PitchVersion.IsMatch(descriptor);
    static bool SafeField(string value) => value.Length <= MaxQueryFieldLength
        && !value.Any(character => char.IsControl(character) && !char.IsWhiteSpace(character))
        && WellFormed(value);

    static bool WellFormed(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                if (++i == value.Length || !char.IsLowSurrogate(value[i])) return false;
            }
            else if (char.IsLowSurrogate(value[i])) return false;
        }
        return true;
    }

    static Regex Pattern(string expression) => new(expression, Options, RegexBudget);
}
