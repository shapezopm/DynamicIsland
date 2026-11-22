namespace DynamicIsland;

internal sealed record LyricsProviderDiagnostic(string Provider, long? RecordId, bool HasValidSync, int LineCount,
    string[] TimingWarnings, string[] ReasonCodes);

internal sealed record LyricsDiagnosticEntry(DateTimeOffset At, long SessionGeneration, long TrackGeneration,
    LyricsStatus Status, string CacheSource, double SearchMilliseconds, int CandidateCount,
    double? IdentityScore, double? SyncedScore, string[] ReasonCodes, string? ErrorCode)
{
    public LyricsProviderDiagnostic[] Providers { get; init; } = [];
}

// Diagnostics intentionally omit titles, artists, queries, URLs and lyric content.
internal sealed class LyricsDiagnostics(TimeProvider? time = null, int capacity = 64)
{
    readonly TimeProvider _time = time ?? TimeProvider.System;
    readonly int _capacity = Math.Clamp(capacity, 1, 128);
    readonly Queue<LyricsDiagnosticEntry> _entries = new();
    readonly object _sync = new();

    internal void Record(LyricsResult result, string cacheSource, TimeSpan elapsed, int candidateCount,
        IReadOnlyList<LyricsCandidate>? candidates = null, TrackSnapshot? lookupTrack = null)
    {
        var entry = new LyricsDiagnosticEntry(_time.GetUtcNow(), result.Track?.SessionGeneration ?? 0,
            result.Track?.TrackGeneration ?? 0, result.Status, SafeCode(cacheSource) ?? "Unknown",
            Math.Max(0, elapsed.TotalMilliseconds), Math.Clamp(candidateCount, 0, 120),
            result.MatchEvidence?.IdentityScore, result.MatchEvidence?.SyncedScore,
            result.ReasonCodes.Select(SafeCode).OfType<string>().Distinct().Take(12).ToArray(), SafeCode(result.ErrorCode))
        {
            Providers = candidates?.Take(120).Select(candidate => new LyricsProviderDiagnostic(
                LyricsSources.Enabled(candidate.ProviderId, LyricSource.All) ? candidate.ProviderId : "Unknown",
                candidate.RecordId, candidate.HasValidSync, candidate.Lines.Length,
                candidate.TimingWarnings.Take(12).Select(warning => warning.Split(':')[0]).Select(SafeCode).OfType<string>().ToArray(),
                (lookupTrack ?? result.Track) is { } track ? LyricsMatcher.Match(track, LyricsMatcher.PrepareTrack(track),
                    LyricsMatcher.Prepare(new([candidate], LyricsStatus.AwaitingVerification, true)))
                    .ReasonCodes.Select(SafeCode).OfType<string>().Take(12).ToArray() : [])).ToArray() ?? [],
        };
        lock (_sync)
        {
            while (_entries.Count >= _capacity) _entries.Dequeue();
            _entries.Enqueue(entry);
        }
    }

    internal LyricsDiagnosticEntry[] Snapshot()
    {
        lock (_sync) return _entries.Select(entry => entry with
        {
            ReasonCodes = entry.ReasonCodes.ToArray(),
            Providers = entry.Providers.Select(provider => provider with
                { TimingWarnings = provider.TimingWarnings.ToArray(), ReasonCodes = provider.ReasonCodes.ToArray() }).ToArray(),
        }).ToArray();
    }

    static string? SafeCode(string? code) => code is { Length: > 0 and <= 64 }
        && code.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or ' ') ? code.Replace(' ', '_') : null;
}
