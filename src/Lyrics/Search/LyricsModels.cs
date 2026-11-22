namespace DynamicIsland;

enum DurationOrigin { Unknown, MediaSession, ProgressEstimate, LyricsEstimate }
enum PositionQuality { Unknown, Estimated, MediaSession, PlayerBar }
enum LyricsStatus { Idle, Disabled, Searching, Synced, AwaitingVerification, Plain, Instrumental, Ambiguous, NotFound, ProviderError, RateLimited }

readonly record struct TrackDuration(TimeSpan Value, DurationOrigin Origin)
{
    public static TrackDuration Unknown => default;
    public bool IsKnown => Value > TimeSpan.Zero && Origin != DurationOrigin.Unknown;
    public bool CanMatch => IsKnown && Origin == DurationOrigin.MediaSession;
}

sealed record TrackSnapshot(string Title, string Artist, string Album, string SourceApp,
    long SessionGeneration, long TrackGeneration, TrackDuration Duration, PositionQuality PositionQuality)
{
    public bool IdentityRequiresDuration { get; init; }
    public string[] IdentityArtistAliases { get; init; } = [];
    public string? IdentityPrimaryArtist { get; init; }
    public bool SameIdentity(TrackSnapshot? other) => other is not null
        && SessionGeneration == other.SessionGeneration && TrackGeneration == other.TrackGeneration
        && SourceApp == other.SourceApp && Title == other.Title && Artist == other.Artist && Album == other.Album;
}

sealed record LyricsCandidate(string ProviderId, long? RecordId, string Title, string Artist, string Album,
    double Duration, double End, LyricsService.Line[] Lines, string? PlainLyrics, bool IsInstrumental)
{
    public string[] VersionTags { get; init; } = [];
    public string[] TimingWarnings { get; init; } = [];
    public bool HasValidSync { get; init; }
    public string? SourceUrl { get; init; }
    // The database behind an aggregating provider (LiriQo's upstream, LRC Mux's source). Shown as "Источник: X · Y".
    public string? Upstream { get; init; }
    // Null on older cached documents. Query echoes are not independent identity evidence.
    public bool? ProviderIdentityVerified { get; init; }
}

sealed record LyricsResult(TrackSnapshot? Track, LyricsStatus Status, LyricsService.Line[] Lines,
    LyricsCandidate? Candidate = null)
{
    public static LyricsResult Empty { get; } = new(null, LyricsStatus.Idle, []);
    public bool SearchComplete { get; init; }
    public DateTimeOffset? RetryAt { get; init; }
    public string? ErrorCode { get; init; }
    public LyricsMatchEvidence? MatchEvidence { get; init; }
    public string[] ReasonCodes { get; init; } = [];
    public string CacheSource { get; init; } = "None";
    public TimeSpan SearchElapsed { get; init; }
}
