namespace DynamicIsland;

internal sealed class LyricsProviderChain(params ILyricsProvider[] providers) : ILyricsProvider
{
    internal bool CollectAlternatives { get; init; }
    public Task<ProviderSearchResult> SearchAsync(TrackSnapshot track, CancellationToken token)
        => SearchAsync(track, null, token);

    internal async Task<ProviderSearchResult> SearchAsync(TrackSnapshot track,
        Func<ProviderSearchResult, Task>? progress, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(24));
        ProviderSearchResult? accepted = null, complete = null, failure = null;
        PreparedLyricsTrack identity = LyricsMatcher.PrepareTrack(track);
        try
        {
            foreach (ILyricsProvider provider in providers)
            {
                // Genius provides plain only; it cannot improve an already verified plain document.
                if (!CollectAlternatives && accepted is not null && provider is LyricsWebSearch) continue;
                ProviderSearchResult found;
                try
                {
                    found = provider is IProgressiveLyricsProvider progressive
                        ? await progressive.SearchAsync(track, async partial =>
                        {
                            if (!partial.Complete) return;
                            ProviderSearchResult pool = CollectAlternatives ? Merge(partial) : partial;
                            if (!pool.Complete) return;
                            LyricsMatch partialMatch = LyricsMatcher.Match(track, identity, LyricsMatcher.Prepare(pool));
                            if (partialMatch.Status == LyricsStatus.Plain || CollectAlternatives && IsDocument(partialMatch)) accepted = pool;
                            if (progress is not null && (CollectAlternatives || partialMatch.Status == LyricsStatus.Plain))
                                await progress(pool).ConfigureAwait(false);
                        }, budget.Token).ConfigureAwait(false)
                        : await provider.SearchAsync(track, budget.Token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                { found = new([], LyricsStatus.ProviderError, false, ErrorCode: "SourceUnavailable"); }
                cancellationToken.ThrowIfCancellationRequested();
                if (found.ErrorCode == "InvalidMetadata") return found;
                if (!found.Complete) { failure ??= found; continue; }
                if (found.ErrorCode is not null) failure ??= found;
                complete = Merge(found);
                if (!complete.Complete) return complete;
                LyricsMatch match = LyricsMatcher.Match(track, identity, LyricsMatcher.Prepare(complete));
                if (!CollectAlternatives && (match.Status == LyricsStatus.Synced || match.Candidate?.HasValidSync == true || match.Status == LyricsStatus.Instrumental))
                    return complete;
                if (match.Status == LyricsStatus.Plain || CollectAlternatives && IsDocument(match))
                {
                    accepted = complete;
                }
                if (progress is not null && (CollectAlternatives && complete.Candidates.Length > 0 || match.Status == LyricsStatus.Plain))
                    await progress(complete).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { failure ??= new([], LyricsStatus.ProviderError, false, ErrorCode: "SearchBudgetExceeded"); }
        cancellationToken.ThrowIfCancellationRequested();
        if (CollectAlternatives && complete?.Candidates.Length > 0)
            return complete with { ErrorCode = failure is null ? null : failure.ErrorCode ?? "SourceUnavailable", RetryAt = failure?.RetryAt };
        return accepted ?? (complete?.Candidates.Length > 0 ? complete : failure ?? complete)
            ?? new([], LyricsStatus.NotFound, true);

        ProviderSearchResult Merge(ProviderSearchResult found)
        {
            LyricsCandidate[] candidates = (complete?.Candidates ?? []).Concat(found.Candidates)
                .OrderByDescending(candidate => candidate.HasValidSync)
                .DistinctBy(candidate => candidate.ProviderId + ":" + (candidate.RecordId?.ToString()
                    ?? candidate.SourceUrl ?? candidate.Title + "\u001f" + candidate.Artist)).ToArray();
            complete = candidates.Length > 120 ? new([], LyricsStatus.ProviderError, false, ErrorCode: "CandidateLimitExceeded")
                : new(candidates, candidates.Length > 0 ? LyricsStatus.AwaitingVerification : LyricsStatus.NotFound, true);
            return complete;
        }
    }

    static bool IsDocument(LyricsMatch match) => match.Status is LyricsStatus.Synced or LyricsStatus.Plain or LyricsStatus.Instrumental
        || match.Candidate?.HasValidSync == true;
}
