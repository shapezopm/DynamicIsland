namespace DynamicIsland;

// Independent hosts share a bounded concurrency window, not each other's cooldown.
internal sealed class LyricsCatalogueGroup(params ILyricsProvider[] providers) : IProgressiveLyricsProvider
{
    internal bool CollectAlternatives { get; init; }
    public Task<ProviderSearchResult> SearchAsync(TrackSnapshot track, CancellationToken token) => SearchAsync(track, null, token);
    public async Task<ProviderSearchResult> SearchAsync(TrackSnapshot track, Func<ProviderSearchResult, Task>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(10));
        using var gate = new SemaphoreSlim(4, 4);
        PreparedLyricsTrack identity = LyricsMatcher.PrepareTrack(track);
        var candidates = new List<LyricsCandidate>();
        ProviderSearchResult? failure = null;
        var pending = providers.Select(async provider =>
        {
            await gate.WaitAsync(budget.Token).ConfigureAwait(false);
            try { return await provider.SearchAsync(track, budget.Token).WaitAsync(budget.Token).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            { return new ProviderSearchResult([], LyricsStatus.ProviderError, false, ErrorCode: "SourceUnavailable"); }
            finally { gate.Release(); }
        }).ToList();
        try
        {
            while (pending.Count > 0)
            {
                Task<ProviderSearchResult> completed = await Task.WhenAny(pending).WaitAsync(budget.Token).ConfigureAwait(false);
                pending.Remove(completed);
                ProviderSearchResult result;
                try { result = await completed.ConfigureAwait(false); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                { failure ??= new([], LyricsStatus.ProviderError, false, ErrorCode: "CatalogueTimeout"); continue; }
                if (!result.Complete) { failure ??= result; continue; }
                if (result.ErrorCode is not null || result.NeedsRefresh) failure ??= result;
                candidates.AddRange(result.Candidates);
                if (candidates.Count > 120) return new([], LyricsStatus.ProviderError, false, ErrorCode: "CandidateLimitExceeded");
                ProviderSearchResult combined = Found();
                LyricsMatch match = LyricsMatcher.Match(track, identity, LyricsMatcher.Prepare(combined));
                if (!CollectAlternatives && (match.Status == LyricsStatus.Synced || match.Candidate?.HasValidSync == true || match.Status == LyricsStatus.Instrumental))
                    return combined;
                if ((CollectAlternatives || match.Status == LyricsStatus.Plain) && progress is not null)
                    await progress(combined).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { failure ??= new([], LyricsStatus.ProviderError, false, ErrorCode: "CatalogueTimeout"); }
        finally
        {
            budget.Cancel();
            // Observe cancellation before disposing the shared semaphore. Late network
            // responses are never published and cannot escape the request generation.
            try { await Task.WhenAll(pending).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        token.ThrowIfCancellationRequested();
        return candidates.Count > 0 ? Found() with { ErrorCode = failure is null ? null : failure.ErrorCode ?? "SourceUnavailable", RetryAt = failure?.RetryAt } : failure ?? Found();

        ProviderSearchResult Found() => new(candidates.OrderByDescending(candidate => candidate.HasValidSync)
            .DistinctBy(candidate => candidate.ProviderId + ":" + (candidate.RecordId?.ToString() ?? candidate.SourceUrl
                ?? candidate.Title + "\u001f" + candidate.Artist)).ToArray(),
            candidates.Count > 0 ? LyricsStatus.AwaitingVerification : LyricsStatus.NotFound, true)
            { NeedsRefresh = failure is not null };
    }
}
