namespace DynamicIsland;

internal static class MediaSessionSelection
{
    internal static T? Pick<T>(IReadOnlyList<T> sessions, T? current, T? pinned, T? previous,
        T? latestPlaying, Func<T, bool> playing, Func<T, DateTimeOffset?>? updated = null) where T : class
    {
        T? Present(T? session) => session is null ? null : sessions.FirstOrDefault(item => Equals(item, session));
        if (Present(pinned) is { } pin) return pin;
        if (Present(latestPlaying) is { } latest && playing(latest)) return latest;
        if (Present(previous) is { } previousPlaying && playing(previousPlaying)) return previousPlaying;
        if (Present(current) is { } selected && playing(selected)) return selected;
        if (sessions.FirstOrDefault(playing) is { } active) return active;
        if (Present(latestPlaying) is { } pausedLatest) return pausedLatest;
        if (Present(previous) is { } pausedPrevious) return pausedPrevious;
        // Windows may have no current session on startup, or retain an older paused browser tab.
        if (updated is not null)
        {
            T? recent = sessions.OrderByDescending(item => Equals(item, current)).MaxBy(updated);
            if (recent is not null && updated(recent).HasValue) return recent;
        }
        return Present(current) ?? sessions.FirstOrDefault();
    }

    internal static T? Cycle<T>(IReadOnlyList<T> sessions, T? current, int direction) where T : class
    {
        if (sessions.Count < 2 || direction == 0) return null;
        int at = -1;
        for (int i = 0; i < sessions.Count; i++) if (Equals(sessions[i], current)) { at = i; break; }
        return sessions[at < 0 ? direction > 0 ? 0 : sessions.Count - 1
            : (at + Math.Sign(direction) + sessions.Count) % sessions.Count];
    }
}
