using System.Security.Cryptography;
using System.Text;

namespace DynamicIsland;

internal sealed record LyricsIdentityHypothesis(string Title, string Artist, string[] VersionTags,
    int TitlePoints, int ArtistPoints)
{
    public bool RequiresDuration { get; init; }
    public bool RequiresConfirmation { get; init; }
}
internal sealed record PreparedLyricsTrack(NormalizedLyricsMetadata Metadata, LyricsIdentityHypothesis[] Hypotheses);
internal sealed record PreparedLyricsCandidate(LyricsCandidate Candidate, NormalizedLyricsMetadata Metadata,
    string DocumentKey, bool HasUsableSync, string[] StructuralReasons)
{
    public string? PlainDocumentKey { get; init; }
    public LyricsService.Line[] ComparableLines { get; init; } = [];
}
internal sealed record PreparedLyricsPool(ProviderSearchResult Search, PreparedLyricsCandidate[] Candidates);
internal sealed record LyricsMatchEvidence(double IdentityScore, double IdentityEvidence, double SyncedScore,
    int? TitlePoints, int? ArtistPoints, int VersionPoints, int? DurationPoints, int? TimingPoints,
    int? ProvenancePoints, int AvailableIdentityWeight, int AvailableSyncedWeight, double? DurationDelta,
    LyricsCandidate[] EquivalentCandidates);
internal sealed record LyricsMatch(LyricsStatus Status, LyricsCandidate? Candidate, LyricsService.Line[] Lines,
    LyricsMatchEvidence? Evidence, string[] ReasonCodes);

internal static class LyricsMatcher
{
    const double MinimumScore = 85, MinimumMargin = 10, EndSlack = 1;
    static readonly LyricsService.Line[] None = [];

    internal static LyricsMatch Manual(TrackSnapshot track, LyricsCandidate candidate)
    {
        PreparedLyricsCandidate prepared = PrepareCandidate(candidate);
        bool versionConfirmed = LyricsNormalizer.Normalize(track.Title, track.Artist).VersionTags.SequenceEqual(prepared.Metadata.VersionTags);
        bool sync = prepared.HasUsableSync && versionConfirmed && (!track.Duration.CanMatch
            || FitsSource(candidate, track.Duration.Value.TotalSeconds)
                && (candidate.Duration <= 0 || Math.Abs(candidate.Duration - track.Duration.Value.TotalSeconds) <= 5));
        if (sync) return new(track.PositionQuality == PositionQuality.Unknown
            ? LyricsStatus.AwaitingVerification : LyricsStatus.Synced, candidate,
            track.PositionQuality == PositionQuality.Unknown ? None : candidate.Lines, null, ["UserSelected"]);
        string? plain = candidate.PlainLyrics;
        if (string.IsNullOrWhiteSpace(plain) && prepared.HasUsableSync)
            plain = string.Join('\n', candidate.Lines.Select(line => line.Text));
        return !string.IsNullOrWhiteSpace(plain) && prepared.StructuralReasons.Length == 0
            ? new(LyricsStatus.Plain, candidate with { PlainLyrics = plain }, None, null, ["UserSelectedPlain"])
            : new(LyricsStatus.NotFound, null, None, null, ["InvalidManualDocument"]);
    }

    public static PreparedLyricsPool Prepare(ProviderSearchResult search)
    {
        ArgumentNullException.ThrowIfNull(search);
        return new(search, search.Candidates.Select(PrepareCandidate).ToArray());
    }

    internal static bool CanFetchDocument(TrackSnapshot track, PreparedLyricsTrack identity, LyricsCandidate metadata)
    {
        var pool = Prepare(new([metadata with { PlainLyrics = "metadata probe" }], LyricsStatus.AwaitingVerification, true));
        if (Match(track, identity, pool).Status == LyricsStatus.Plain) return true;
        PreparedLyricsCandidate candidate = pool.Candidates[0];
        if (candidate.StructuralReasons.Length > 0 || track.Duration.CanMatch && metadata.Duration > 0
            && Math.Abs(track.Duration.Value.TotalSeconds - metadata.Duration) > 10) return false;
        // Fetching a possible document is separate from granting it automatic playback timing.
        return identity.Hypotheses.Any(hypothesis => hypothesis.RequiresDuration
            && Equal(hypothesis.Title, candidate.Metadata.BaseTitle) && Equal(hypothesis.Artist, candidate.Metadata.Artist)
            && hypothesis.VersionTags.SequenceEqual(candidate.Metadata.VersionTags));
    }

    public static PreparedLyricsTrack PrepareTrack(TrackSnapshot track)
    {
        ArgumentNullException.ThrowIfNull(track);
        var identities = new List<LyricsIdentityHypothesis>();
        if (!SafeField(track.Title) || !SafeField(track.Artist))
            return new(new(track.Title, track.Artist, "", "", "", []), []);
        NormalizedLyricsMetadata original = LyricsNormalizer.Normalize(track.Title, track.Artist);
        if (original.Title.Length == 0) return new(original, []);
        identities.Add(new(original.BaseTitle, original.Artist, original.VersionTags, 25, 25));
        foreach (LyricsQuery query in LyricsNormalizer.BuildQueries(track))
        {
            if (query.Kind is LyricsQueryKind.FreeText or LyricsQueryKind.BaseTitleFallback) continue;
            if (query.Title.Length == 0 || query.Artist.Length == 0) continue;
            NormalizedLyricsMetadata identity = LyricsNormalizer.Normalize(query.Title, query.Artist);
            bool credited = query.Kind == LyricsQueryKind.CreditedArtist;
            if (!credited && !SameArtist(original.Artist, identity.Artist)) continue;
            var hypothesis = new LyricsIdentityHypothesis(identity.BaseTitle, identity.Artist, identity.VersionTags,
                credited || Equal(original.BaseTitle, identity.BaseTitle) ? 25 : 22,
                credited || Equal(original.Artist, identity.Artist) ? 25 : 22)
                { RequiresDuration = query.Kind == LyricsQueryKind.UploadHint };
            if (!identities.Any(existing => Equal(existing.Title, hypothesis.Title)
                && Equal(existing.Artist, hypothesis.Artist) && existing.VersionTags.SequenceEqual(hypothesis.VersionTags)))
                identities.Add(hypothesis);
        }
        // An upload may belong to a repost account. The catalogue must independently confirm
        // both halves of its explicit artist/title credit and its duration before timing is used.
        if (LyricsNormalizer.TryTitleCredit(original) is null && LyricsNormalizer.DiscoveryCredit(original) is { } discovered)
        {
            var normalized = LyricsNormalizer.Normalize(discovered.Title, discovered.FullArtist);
            if (!identities.Any(existing => Equal(existing.Title, normalized.BaseTitle) && Equal(existing.Artist, normalized.Artist)))
                identities.Add(new(normalized.BaseTitle, normalized.Artist, normalized.VersionTags, 22, 22)
                    { RequiresDuration = true, RequiresConfirmation = !LyricsNormalizer.IsPlaceholderArtist(original.Artist) });
        }
        foreach (string alias in track.IdentityArtistAliases.Take(24))
            identities.Add(new(original.BaseTitle, LyricsNormalizer.Normalize(track.Title, alias).Artist, original.VersionTags, 25, 25));
        if (!string.IsNullOrWhiteSpace(track.IdentityPrimaryArtist))
            identities.Add(new(original.BaseTitle, track.IdentityPrimaryArtist, original.VersionTags, 25, 22) { RequiresDuration = true });
        if (track.IdentityRequiresDuration)
            identities = identities.Select(item => item with { RequiresDuration = true }).ToList();
        return new(original, identities.ToArray());
    }

    public static LyricsMatch Match(TrackSnapshot track, PreparedLyricsTrack identity, PreparedLyricsPool pool)
        => MatchWithAutoSync(track, identity, pool, Settings.LyricsAutoSync);

    // Thread-local so parallel tests cannot switch the mode for each other.
    [ThreadStatic] static bool _autoSync;

    internal static LyricsMatch MatchWithAutoSync(TrackSnapshot track, PreparedLyricsTrack identity, PreparedLyricsPool pool, bool autoSync)
    {
        bool previous = _autoSync;
        _autoSync = autoSync;
        try
        {
            LyricsMatch match = MatchCore(track, identity, pool);
            return autoSync ? FitToTrack(track, match) : match;
        }
        finally { _autoSync = previous; }
    }

    static LyricsMatch MatchCore(TrackSnapshot track, PreparedLyricsTrack identity, PreparedLyricsPool pool)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(pool);
        if (!pool.Search.Complete)
            return new(pool.Search.Status == LyricsStatus.RateLimited ? LyricsStatus.RateLimited : LyricsStatus.ProviderError,
                null, None, null, ["IncompleteSearch"]);
        if (pool.Candidates.Length == 0)
            return new(LyricsStatus.NotFound, null, None, null, ["NoCandidates"]);

        Scored[] scored = pool.Candidates.Select(candidate => Score(track, identity, candidate)).ToArray();
        Scored[] relevant = scored.Where(score => !score.HardRejected).ToArray();
        if (relevant.Length == 0)
            return new(LyricsStatus.NotFound, null, None, null, new[] { "NoRelevantCandidate" }
                .Concat(scored.SelectMany(score => score.Reasons)).Distinct(StringComparer.Ordinal).OrderBy(reason => reason, StringComparer.Ordinal).Take(12).ToArray());

        // IDs identify provider records, not distinct lyric documents. Group text and exact timestamps first.
        Scored[][] groups = GroupDocuments(relevant)
            .Select(group => group.OrderByDescending(score => score.CanAuto).ThenByDescending(score => score.SyncedScore)
                .ThenByDescending(score => score.IdentityScore).ThenByDescending(score => AlbumMatches(track.Album, score.Prepared.Candidate.Album))
                .ThenBy(score => StableKey(score.Prepared.Candidate), StringComparer.Ordinal).ToArray())
            .ToArray();
        Scored[][] synced = groups.Where(group => group.Any(score => score.SyncUsable))
            .OrderByDescending(group => group[0].CanAuto).ThenByDescending(group => group[0].SyncedScore)
            .ThenBy(group => group[0].Prepared.DocumentKey, StringComparer.Ordinal).ToArray();
        Scored[][] plain = groups.Where(group => group.Any(score => !score.SyncUsable && (!string.IsNullOrWhiteSpace(score.Prepared.Candidate.PlainLyrics)
                || score.Prepared.HasUsableSync)))
            .OrderByDescending(group => group[0].CanAuto).ThenByDescending(group => group[0].PlainScore)
            .ThenBy(group => group[0].Prepared.DocumentKey, StringComparer.Ordinal).ToArray();
        Scored[][] instrumentals = groups.Where(group => group[0].Prepared.Candidate.IsInstrumental)
            .OrderByDescending(group => group[0].PlainScore).ThenBy(group => group[0].Prepared.DocumentKey, StringComparer.Ordinal).ToArray();

        if (synced.Length > 0)
        {
            if (_autoSync) synced = PreferSameRecordingLeader(synced);
            Scored[] leading = synced[0];
            Scored best = leading[0];
            if (best.CanAuto && best.SyncedScore >= MinimumScore)
            {
                if (CompetingVersionWithoutDuration(identity, best, relevant)) return Decision(LyricsStatus.Ambiguous, leading, "UnverifiedCompetingVersion");
                // Sync files of the same recording are resolved above; only different versions keep the margin rule.
                Scored[] rivals = VersionRivals(synced, identity, best).Where(other => !(_autoSync && SameRecording(best, other))).ToArray();
                if (rivals.Length > 0 && best.SyncedScore - rivals.Max(score => score.SyncedScore) < MinimumMargin)
                    return Decision(LyricsStatus.Ambiguous, leading, "InsufficientMargin");
                if (instrumentals.Any(group => group[0].CanAuto)) return Decision(LyricsStatus.Ambiguous, leading, "InstrumentalConflict");
                return track.PositionQuality == PositionQuality.Unknown
                    ? Decision(LyricsStatus.AwaitingVerification, leading, "PositionUnavailable", keepCandidate: true)
                    : Decision(LyricsStatus.Synced, leading, "AcceptedSynced", keepCandidate: true);
            }
        }
        if (plain.Length > 0 && plain[0][0].CanAuto)
        {
            Scored[] leading = plain[0];
            if (CompetingVersionWithoutDuration(identity, leading[0], relevant)) return Decision(LyricsStatus.Ambiguous, leading, "UnverifiedCompetingVersion");
            Scored[] rivals = VersionRivals(plain, identity, leading[0]);
            if (rivals.Length > 0 && leading[0].PlainScore - rivals.Max(score => score.PlainScore) < MinimumMargin)
                return Decision(LyricsStatus.Ambiguous, leading, "InsufficientMargin");
            if (instrumentals.Any(group => group[0].CanAuto)) return Decision(LyricsStatus.Ambiguous, leading, "InstrumentalConflict");
            return Decision(LyricsStatus.Plain, leading, "AcceptedPlain", keepCandidate: true);
        }
        if (instrumentals.Length > 0 && instrumentals[0][0].CanAuto)
        {
            Scored[] leading = instrumentals[0];
            if (CompetingVersionWithoutDuration(identity, leading[0], relevant)) return Decision(LyricsStatus.Ambiguous, leading, "UnverifiedCompetingVersion");
            Scored[] rivals = VersionRivals(instrumentals, identity, leading[0]);
            if (rivals.Length > 0 && leading[0].PlainScore - rivals.Max(score => score.PlainScore) < MinimumMargin)
                return Decision(LyricsStatus.Ambiguous, leading, "InsufficientMargin");
            return Decision(LyricsStatus.Instrumental, leading, "AcceptedInstrumental", keepCandidate: true);
        }
        Scored[] diagnostic = groups.OrderByDescending(group => group[0].IdentityScore)
            .ThenBy(group => group[0].Prepared.DocumentKey, StringComparer.Ordinal).First();
        LyricsStatus status = diagnostic[0].IdentityScore is >= 70 and < MinimumScore
            || diagnostic[0].SyncUsable && diagnostic[0].SyncedScore is >= 70 and < MinimumScore
            || diagnostic[0].Reasons.Contains("VersionUnconfirmed") || diagnostic[0].Reasons.Contains("DurationDiagnosticOnly")
            || diagnostic[0].Reasons.Contains("FuzzyTitleDiagnosticOnly") || diagnostic[0].Reasons.Contains("FuzzyArtistDiagnosticOnly")
            ? LyricsStatus.Ambiguous : LyricsStatus.AwaitingVerification;
        return Decision(status, diagnostic, diagnostic[0].Reasons.FirstOrDefault() ?? "InsufficientEvidence");
    }

    static PreparedLyricsCandidate PrepareCandidate(LyricsCandidate candidate)
    {
        if (!SafeField(candidate.Title) || !SafeField(candidate.Artist) || !SafeField(candidate.Album)
            || candidate.VersionTags.Any(tag => !SafeField(tag)))
            return new(candidate, new(candidate.Title, candidate.Artist, "", "", "", []), "invalid:" + StableKey(candidate), false, ["InvalidMetadata"]);
        NormalizedLyricsMetadata metadata = LyricsNormalizer.Normalize(candidate.Title, candidate.Artist);
        metadata = metadata with { VersionTags = metadata.VersionTags.Concat(candidate.VersionTags)
            .Select(tag => tag.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).OrderBy(tag => tag, StringComparer.Ordinal).ToArray() };
        var reasons = new List<string>();
        if (!double.IsFinite(candidate.Duration) || candidate.Duration is < 0 or > 86400) reasons.Add("InvalidDuration");
        bool usable = candidate.HasValidSync && candidate.Lines.Length is > 0 and <= 5000
            && candidate.Lines.Any(line => !string.IsNullOrWhiteSpace(line.Text));
        long previous = -1;
        foreach (LyricsService.Line line in candidate.Lines)
        {
            if (line.Time.Ticks < 0 || line.Time.TotalSeconds > 86400 || line.Time.Ticks <= previous
                || !ValidText(line.Text, 4096)) usable = false;
            previous = line.Time.Ticks;
        }
        if (!double.IsFinite(candidate.End) || candidate.End is < 0 or > 86400
            || candidate.Lines.Length > 0 && Math.Abs(candidate.End - candidate.Lines[^1].Time.TotalSeconds) > 0.001
            || candidate.Duration > 0 && candidate.End > candidate.Duration + EndSlack) usable = false;
        if (!usable && (candidate.HasValidSync || candidate.Lines.Length > 0)) reasons.Add("InvalidTiming");
        if (candidate.IsInstrumental && (candidate.Lines.Any(line => !string.IsNullOrWhiteSpace(line.Text))
            || !string.IsNullOrWhiteSpace(candidate.PlainLyrics))) reasons.Add("InstrumentalTextConflict");
        string? plainDocument = !string.IsNullOrWhiteSpace(candidate.PlainLyrics) && ValidText(candidate.PlainLyrics, 2 * 1024 * 1024)
            ? "plain:" + HashText(candidate.PlainLyrics) : null;
        if (!string.IsNullOrWhiteSpace(candidate.PlainLyrics) && plainDocument is null) reasons.Add("InvalidPlainText");
        string document = usable ? "synced:" + HashLines(candidate.Lines)
            : plainDocument is not null ? plainDocument
                : candidate.IsInstrumental ? "instrumental" : "unusable:" + StableKey(candidate);
        LyricsService.Line[] comparable = usable ? LyricsContentCleaner.Lines(candidate.Lines)
            .Where((line, index) => !string.IsNullOrWhiteSpace(line.Text) || string.IsNullOrWhiteSpace(candidate.Lines[index].Text)).ToArray() : [];
        return new(candidate, metadata, document, usable, reasons.ToArray())
            { PlainDocumentKey = plainDocument, ComparableLines = comparable };
    }

    static IEnumerable<List<Scored>> GroupDocuments(Scored[] candidates)
    {
        var groups = new List<List<Scored>>();
        foreach (Scored candidate in candidates.OrderBy(score => StableKey(score.Prepared.Candidate), StringComparer.Ordinal))
        {
            string key = candidate.SyncUsable ? candidate.Prepared.DocumentKey
                : candidate.Prepared.PlainDocumentKey ?? candidate.Prepared.DocumentKey;
            List<Scored>? group = groups.FirstOrDefault(items => items.All(other =>
                key == (other.SyncUsable ? other.Prepared.DocumentKey : other.Prepared.PlainDocumentKey ?? other.Prepared.DocumentKey)
                || EquivalentTiming(candidate, other)));
            if (group is null) groups.Add([candidate]);
            else group.Add(candidate);
        }
        return groups;
    }

    static bool EquivalentTiming(Scored first, Scored second)
    {
        if (!first.SyncUsable || !second.SyncUsable || !first.CanAuto || !second.CanAuto) return false;
        PreparedLyricsCandidate a = first.Prepared, b = second.Prepared;
        if (!Equal(a.Metadata.BaseTitle, b.Metadata.BaseTitle) || !SameArtist(a.Metadata.Artist, b.Metadata.Artist)
            || !a.Metadata.VersionTags.SequenceEqual(b.Metadata.VersionTags)
            || a.Candidate.Duration > 0 && b.Candidate.Duration > 0 && Math.Abs(a.Candidate.Duration - b.Candidate.Duration) > 2
            || a.ComparableLines.Length == 0 || a.ComparableLines.Length != b.ComparableLines.Length) return false;
        for (int i = 0; i < a.ComparableLines.Length; i++)
            if (a.ComparableLines[i].Text != b.ComparableLines[i].Text
                || Math.Abs((a.ComparableLines[i].Time - b.ComparableLines[i].Time).TotalMilliseconds) > 350) return false;
        // All members must agree pairwise: no chain of small shifts can merge incompatible timings.
        return true;
    }

    static Scored Score(TrackSnapshot track, PreparedLyricsTrack identity, PreparedLyricsCandidate prepared)
    {
        LyricsCandidate candidate = prepared.Candidate;
        var reasons = new List<string>(prepared.StructuralReasons);
        bool hard = reasons.Any(reason => reason is "InvalidMetadata" or "InvalidDuration" or "InstrumentalTextConflict" or "InvalidPlainText");
        int? title = null, artist = null;
        int version = 15;
        bool versionConfirmed = false;
        bool requiresDuration = false;
        bool requiresConfirmation = false;
        bool uploaderUnverified = false;
        if (identity.Hypotheses.Length == 0 || identity.Metadata.BaseTitle.Length == 0 || prepared.Metadata.BaseTitle.Length == 0)
            reasons.Add("TitleUnavailable");
        else
        {
            LyricsIdentityHypothesis[] exact = identity.Hypotheses.Where(hypothesis => Equal(hypothesis.Title, prepared.Metadata.BaseTitle)).ToArray();
            if (exact.Length == 0)
            {
                bool similar = identity.Hypotheses.Any(hypothesis => Similar(hypothesis.Title, prepared.Metadata.BaseTitle));
                title = similar ? 20 : 0;
                reasons.Add(similar ? "FuzzyTitleDiagnosticOnly" : "TitleMismatch");
                hard |= !similar;
                exact = identity.Hypotheses;
            }
            else title = exact.Max(hypothesis => hypothesis.TitlePoints);
            if (identity.Metadata.Artist.Length == 0 || prepared.Metadata.Artist.Length == 0)
                reasons.Add("ArtistUnavailable");
            else
            {
                LyricsIdentityHypothesis? full = exact.Where(hypothesis => Equal(hypothesis.Artist, prepared.Metadata.Artist))
                    .OrderByDescending(hypothesis => hypothesis.ArtistPoints).FirstOrDefault();
                if (full is not null) { artist = full.ArtistPoints; requiresDuration = full.RequiresDuration; requiresConfirmation = full.RequiresConfirmation; }
                else if (exact.Any(hypothesis => !hypothesis.RequiresDuration && SameArtist(hypothesis.Artist, prepared.Metadata.Artist))) artist = 22;
                else
                {
                    bool similar = exact.Any(hypothesis => Similar(hypothesis.Artist, prepared.Metadata.Artist));
                    // SoundCloud uploads often carry the uploader in the artist field. An exact title with a length within
                    // 2 s is strong evidence, so the artist weight is reduced instead of rejecting the candidate.
                    bool uploaderLength = _autoSync && title == 25 && track.Duration.CanMatch && candidate.Duration > 0
                        && Math.Abs(track.Duration.Value.TotalSeconds - candidate.Duration) <= 2;
                    if (!similar && uploaderLength)
                    {
                        artist = 15;
                        uploaderUnverified = true;
                        reasons.Add("UploaderArtistUnverified");
                    }
                    else
                    {
                        artist = similar ? 20 : 0;
                        reasons.Add(similar ? "FuzzyArtistDiagnosticOnly" : "ArtistMismatch");
                        hard |= !similar;
                    }
                }
            }
            string[] wanted = identity.Metadata.VersionTags;
            string[] actual = prepared.Metadata.VersionTags;
            if (wanted.SequenceEqual(actual)) versionConfirmed = true;
            else if (wanted.Length == 0 || actual.Length == 0) { version = 8; reasons.Add("VersionUnconfirmed"); }
            else { version = 0; hard = true; reasons.Add("VersionMismatch"); }
        }
        int? duration = null;
        double? delta = null;
        bool plainUpload = title >= 22 && artist == 25 && LyricsNormalizer.HasTextCompatibleUploadEdit(identity.Metadata)
            && (prepared.Metadata.VersionTags.Length == 0 || identity.Metadata.VersionTags.SequenceEqual(prepared.Metadata.VersionTags))
            && !candidate.IsInstrumental;
        if (track.Duration.CanMatch && candidate.Duration > 0)
        {
            delta = Math.Abs(track.Duration.Value.TotalSeconds - candidate.Duration);
            duration = delta <= 2 ? 15 : delta <= 5 ? 9 : delta <= 10 ? 3 : 0;
            if (delta > 10 && !plainUpload) { hard = true; reasons.Add("DurationMismatch"); }
            else if (delta > 5) reasons.Add("DurationDiagnosticOnly");
        }
        else reasons.Add("DurationUnavailable");
        bool sync = prepared.HasUsableSync;
        bool plainOnly = plainUpload && (!versionConfirmed || delta > 5);
        if (plainOnly)
        {
            sync = false;
            duration = null;
            reasons.Add("UploadEditTextOnly");
        }
        if (sync && track.Duration.CanMatch && !FitsSource(candidate, track.Duration.Value.TotalSeconds))
        { sync = false; reasons.Add("TimingBeyondSourceDuration"); }
        int? timing = sync ? candidate.TimingWarnings.Length == 0 ? 15 : 10 : null;
        int? provenance = !string.IsNullOrWhiteSpace(candidate.ProviderId) && candidate.RecordId is > 0 ? 5 : null;
        int identityWeight = (title.HasValue ? 25 : 0) + (artist.HasValue ? 25 : 0) + 15 + (duration.HasValue ? 15 : 0);
        int identityPoints = (title ?? 0) + (artist ?? 0) + version + (duration ?? 0);
        double identityScore = ScorePercent(identityPoints, identityWeight);
        int syncedWeight = identityWeight + (timing.HasValue ? 15 : 0) + (provenance.HasValue ? 5 : 0);
        double syncedScore = ScorePercent(identityPoints + (timing ?? 0) + (provenance ?? 0), syncedWeight);
        double plainScore = ScorePercent(identityPoints + (provenance ?? 0), identityWeight + (provenance.HasValue ? 5 : 0));
        bool hasIndependentDuration = delta.HasValue;
        bool unverifiedProvider = candidate.ProviderId == LiriQoProvider.ProviderId && candidate.ProviderIdentityVerified != true;
        if (unverifiedProvider) reasons.Add("ProviderIdentityUnverified");
        double mediaSeconds = track.Duration.Value.TotalSeconds;
        bool shortDocumentForLongMedia = prepared.HasUsableSync && track.Duration.CanMatch && mediaSeconds > 600
            && mediaSeconds > candidate.Lines.LastOrDefault(line => !Lyric.IsWordless(line.Text)).Time.TotalSeconds * 3;
        if (shortDocumentForLongMedia) reasons.Add("LyricDocumentMuchShorterThanMedia");
        bool auto = !unverifiedProvider && !shortDocumentForLongMedia && !hard && !requiresConfirmation && title >= 22 && artist >= 22 && (versionConfirmed || plainOnly) && identityScore >= MinimumScore
            && (plainOnly || !hasIndependentDuration || delta <= 5)
            && (hasIndependentDuration || title == 25 && artist == 25 || requiresDuration && !sync)
            && (!requiresDuration || !sync || delta <= 2);
        if (!auto && reasons.Count == 0) reasons.Add("InsufficientIdentityEvidence");
        return new(prepared, hard, auto, sync, identityScore, syncedScore, plainScore,
            title, artist, version, duration, timing, provenance, identityWeight, syncedWeight, delta, reasons.ToArray());
    }

    // Same title, artist and version tags: one recording with several sync files (or uploads).
    static bool SameRecording(Scored a, Scored b) =>
        a.TitlePoints == 25 && a.ArtistPoints == 25 && b.TitlePoints == 25 && b.ArtistPoints == 25
        && a.Prepared.Metadata.VersionTags.SequenceEqual(b.Prepared.Metadata.VersionTags);

    // Experimental: among sync files of one recording the one closest in length to the track leads, without asking.
    static Scored[][] PreferSameRecordingLeader(Scored[][] synced)
    {
        Scored lead = synced[0][0];
        if (!lead.CanAuto) return synced;
        Scored[][] same = synced.Where(group => group[0].CanAuto && group[0].SyncedScore >= MinimumScore && SameRecording(lead, group[0]))
            .ToArray();
        if (same.Length < 2) return synced;
        Scored[] chosen = same.OrderBy(group => group[0].DurationDelta ?? double.MaxValue)
            .ThenBy(group => group[0].Prepared.DocumentKey, StringComparer.Ordinal).First();
        return new[] { chosen }.Concat(synced.Where(group => !ReferenceEquals(group, chosen))).ToArray();
    }

    static bool UnspecifiedVersionRival(PreparedLyricsTrack identity, Scored selected, Scored other)
        => identity.Metadata.VersionTags.Length > 0 && selected.VersionPoints == 15
            && selected.Prepared.Metadata.VersionTags.SequenceEqual(identity.Metadata.VersionTags)
            && other.Prepared.Metadata.VersionTags.Length == 0;

    static Scored[] VersionRivals(Scored[][] groups, PreparedLyricsTrack identity, Scored selected)
        => groups.Skip(1).Select(group => group.FirstOrDefault(other => !UnspecifiedVersionRival(identity, selected, other)))
            .OfType<Scored>().ToArray();

    static bool CompetingVersionWithoutDuration(PreparedLyricsTrack identity, Scored selected, Scored[] relevant)
        => selected.DurationDelta is null && relevant.Any(other => other.TitlePoints >= 22 && other.ArtistPoints >= 22
            && !other.Prepared.Metadata.VersionTags.SequenceEqual(selected.Prepared.Metadata.VersionTags)
            && !UnspecifiedVersionRival(identity, selected, other));

    static LyricsMatch Decision(LyricsStatus status, Scored[] group, string reason, bool keepCandidate = false)
    {
        Scored best = group[0];
        LyricsCandidate[] equivalent = group.Select(score => score.Prepared.Candidate)
            .OrderBy(StableKey, StringComparer.Ordinal).ToArray();
        var evidence = new LyricsMatchEvidence(best.IdentityScore, best.IdentityWeight / 80d, best.SyncedScore,
            best.TitlePoints, best.ArtistPoints, best.VersionPoints, best.DurationPoints, best.TimingPoints,
            best.ProvenancePoints, best.IdentityWeight, best.SyncedWeight, best.DurationDelta, equivalent);
        return new(status, keepCandidate ? best.Prepared.Candidate : null,
            status == LyricsStatus.Synced ? best.Prepared.Candidate.Lines : None, evidence,
            new[] { reason }.Concat(best.Reasons).Distinct(StringComparer.Ordinal).ToArray());
    }

    static bool SameArtist(string first, string second)
        => first.Length > 0 && second.Length > 0 && (Equal(first, second) || Equal(MainArtist(first), MainArtist(second)));

    static double LastSungTime(LyricsCandidate candidate) => candidate.Lines.LastOrDefault(line => !string.IsNullOrWhiteSpace(line.Text)).Time.TotalSeconds;

    // Experimental: a synced document of the same recording with a slightly different length is stretched linearly to
    // the track's own length. This is a guess (no audio is analysed), so it only applies to small length differences.
    static LyricsMatch FitToTrack(TrackSnapshot track, LyricsMatch match)
    {
        if (match.Status != LyricsStatus.Synced || match.Candidate is not { Duration: > 0 } candidate || !track.Duration.CanMatch
            || match.Lines.Length == 0)
            return match;
        double target = track.Duration.Value.TotalSeconds;
        double delta = target - candidate.Duration;
        if (Math.Abs(delta) < 0.5 || Math.Abs(delta) > 10) return match;
        double factor = target / candidate.Duration;
        LyricsService.Line[] fitted = match.Lines
            .Select(line => line with { Time = TimeSpan.FromTicks((long)(line.Time.Ticks * factor)) }).ToArray();
        return match with { Lines = fitted, ReasonCodes = match.ReasonCodes.Append("FittedToTrackLength").ToArray() };
    }

    static bool FitsSource(LyricsCandidate candidate, double seconds) => candidate.End <= seconds + EndSlack
        || candidate.Duration > 0 && Math.Abs(candidate.Duration - seconds) <= 5 && candidate.End <= seconds + 5
            && LastSungTime(candidate) <= seconds + EndSlack;

    static string MainArtist(string credit)
    {
        foreach (string marker in new[] { " feat ", " feat. ", " featuring ", " ft ", " ft. ", " (feat ", " (feat. ", " (featuring ", " (ft ", " (ft. " })
        {
            int start = credit.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start > 0) return credit[..start].Trim();
        }
        return credit;
    }

    static bool Similar(string first, string second)
    {
        if (first.Length is < 4 or > 128 || second.Length is < 4 or > 128
            || Math.Abs(first.Length - second.Length) > Math.Max(first.Length, second.Length) / 5) return false;
        string a = first.ToUpperInvariant(), b = second.ToUpperInvariant();
        int[] previous = Enumerable.Range(0, b.Length + 1).ToArray(), current = new int[b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return previous[b.Length] <= Math.Max(first.Length, second.Length) / 5;
    }

    static bool SafeField(string text)
    {
        return ValidText(text, 512);
    }
    static bool ValidText(string text, int limit)
    {
        if (text is null || text.Length > limit || text.Any(character => char.IsControl(character) && !char.IsWhiteSpace(character))) return false;
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (++i == text.Length || !char.IsLowSurrogate(text[i])) return false;
            }
            else if (char.IsLowSurrogate(text[i])) return false;
        }
        return true;
    }
    static bool AlbumMatches(string source, string candidate) => source.Length > 0 && SafeField(source)
        && SafeField(candidate) && Equal(source.Normalize(NormalizationForm.FormKC).Trim(), candidate.Normalize(NormalizationForm.FormKC).Trim());
    static bool Equal(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    static double ScorePercent(int points, int weight) => weight == 0 ? 0 : 100d * points / weight;
    static string StableKey(LyricsCandidate candidate) => candidate.ProviderId + ":" + candidate.RecordId?.ToString("D20")
        + ":" + candidate.Title + ":" + candidate.Artist + ":" + candidate.Album + ":" + candidate.Duration.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    static string HashLines(LyricsService.Line[] lines)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (LyricsService.Line line in lines)
        {
            hash.AppendData(BitConverter.GetBytes(line.Time.Ticks));
            byte[] text = Encoding.UTF8.GetBytes(line.Text);
            hash.AppendData(BitConverter.GetBytes(text.Length));
            hash.AppendData(text);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n"))));

    sealed record Scored(PreparedLyricsCandidate Prepared, bool HardRejected, bool CanAuto, bool SyncUsable,
        double IdentityScore, double SyncedScore, double PlainScore, int? TitlePoints, int? ArtistPoints,
        int VersionPoints, int? DurationPoints, int? TimingPoints, int? ProvenancePoints,
        int IdentityWeight, int SyncedWeight, double? DurationDelta, string[] Reasons);
}
