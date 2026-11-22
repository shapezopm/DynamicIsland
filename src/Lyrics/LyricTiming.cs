namespace DynamicIsland;

internal static class LyricTiming
{
    public const int SourceLeadMs = 200;
    static readonly TimeSpan MaxFinalLineLength = TimeSpan.FromMinutes(1);

    public static TimeSpan LookupTime(TimeSpan playbackPosition, int userAdvanceMs)
    {
        long shift = (long)(SourceLeadMs + LyricsOffsetStore.Normalize(userAdvanceMs)) * TimeSpan.TicksPerMillisecond;
        long ticks = playbackPosition.Ticks;
        return TimeSpan.FromTicks(shift > 0 && ticks > long.MaxValue - shift ? long.MaxValue
            : shift < 0 && ticks < long.MinValue - shift ? long.MinValue : ticks + shift);
    }

    public static int LineAt(LyricsService.Line[] lines, TimeSpan at)
    {
        int low = 0, high = lines.Length - 1, found = -1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            if (lines[middle].Time <= at) { found = middle; low = middle + 1; }
            else high = middle - 1;
        }
        return found;
    }

    // The source video may continue long after the lyric document. Do not keep its last
    // line alive until the video's end. Earlier gaps still use the existing line timings.
    public static int DisplayLineAt(LyricsService.Line[] lines, TimeSpan at, TrackDuration source, double candidateDuration)
        => DisplayLineAt(lines, at, DocumentEnd(lines, source, candidateDuration));

    public static int DisplayLineAt(LyricsService.Line[] lines, TimeSpan at, TimeSpan? documentEnd)
        => at >= documentEnd ? -1 : LineAt(lines, at);

    public static TimeSpan? DocumentEnd(LyricsService.Line[] lines, TrackDuration source, double candidateDuration)
        => lines.Length == 0 ? null : EndFor(lines, lines.Length - 1, source, candidateDuration) ?? TrackEndFor(source, candidateDuration);

    internal readonly record struct FinalLine(int Index, TimeSpan? End)
    {
        public bool IsComplete(TimeSpan at) => Index >= 0 && at >= End;

        public int LineAt(LyricsService.Line[] lines, TimeSpan at) =>
            at >= End ? Index : LyricTiming.LineAt(lines, at);
    }

    public static FinalLine FinalTextLine(LyricsService.Line[] lines, TrackDuration source, double candidateDuration, TimeSpan? documentEnd)
    {
        int index = LastTextIndex(lines);
        if (index < 0 || index == lines.Length - 1) return new(index, documentEnd);
        TimeSpan? end = EndFor(lines, index, source, candidateDuration);
        if (documentEnd < end) end = documentEnd;
        if (CandidateEnd(candidateDuration) is { } candidate && source.Value - candidate > TimeSpan.FromSeconds(10)
            && candidate > lines[index].Time && candidate < end) end = candidate;
        return new(index, end ?? documentEnd);
    }

    static int LastTextIndex(LyricsService.Line[] lines) => Array.FindLastIndex(lines, line => !Lyric.IsWordless(line.Text));

    public static TimeSpan? EndFor(LyricsService.Line[] lines, int index, TrackDuration source, double candidateDuration)
    {
        if (index < 0 || index >= lines.Length) return null;
        if (index + 1 < lines.Length) return CapPause(lines[index], lines[index + 1].Time);
        TimeSpan start = lines[index].Time;
        TimeSpan length = AverageLineLength(lines) ?? MaxFinalLineLength;
        TimeSpan end = start + (length < MaxFinalLineLength ? length : MaxFinalLineLength);
        // A selected lyric may belong to a much shorter recording than the current video.
        if (CandidateEnd(candidateDuration) is { } candidateEnd && candidateEnd >= start
            && source.Value - candidateEnd > TimeSpan.FromSeconds(10) && candidateEnd < end)
            end = candidateEnd;
        if (TrackEndFor(source, candidateDuration) is { } trackEnd && trackEnd < end) end = trackEnd;
        return end > start ? end : null;
    }

    // Длинная пауза после строки: если промежуток до следующей строки заметно больше, чем строка поётся по слогам,
    // конец строки ограничиваем оценкой. Начало следующей строки не меняем, поэтому синхронизация строк не сдвигается.
    public const int PauseMinSyllables = 3;
    public const double PauseMinGapSeconds = 4, PauseGapFactor = 1.5, PauseEndSlack = 1.3, SecondsPerSyllable = 0.3;
    static LyricsService.Line? _lastCappedLine;

    static TimeSpan CapPause(LyricsService.Line line, TimeSpan next)
    {
        double gap = (next - line.Time).TotalSeconds;
        int syllables = SyllableCount(line.Text);
        if (syllables < PauseMinSyllables || gap < PauseMinGapSeconds) return next;

        double estimate = syllables * SecondsPerSyllable;
        if (gap <= estimate * PauseGapFactor) return next;

        TimeSpan capped = line.Time + TimeSpan.FromSeconds(estimate * PauseEndSlack);
        if (capped >= next) return next;

        // Лог только при смене строки: EndFor вызывается часто, файл не должен расти на каждом кадре.
        if (line != _lastCappedLine)
        {
            _lastCappedLine = line;
            LogPauseCap(line.Text, syllables, gap, capped);
        }
        return capped;
    }

    // Заливка по времени, как FillTiming.progress в плагине (ayu_lyrics_sources): кубическое сглаживание
    // на краях окна ±40 мс и линейно между ними. Значение зависит от текущего времени, а не от частоты тиков.
    public const double FillWindowMs = 40;

    public static double FillProgress(TimeSpan at, TimeSpan start, TimeSpan? end, double windowMs = FillWindowMs)
    {
        if (end is not { } finish || finish <= start) return 0;
        double dMax = Math.Max(1, windowMs);
        double span = (finish - start).TotalMilliseconds;
        double d = (at - start).TotalMilliseconds;
        if (d <= -dMax) return 0;
        if (d >= span + dMax) return 1;
        double c;
        if (d < dMax || d > span - dMax)
        {
            double d4 = d + dMax, d5 = d - dMax;
            c = (((Cube(d4) - Cube(d) * 2) + Cube(d5)) - Cube(d4 - span) + Cube(d - span) * 2 - Cube(d5 - span))
                / (span * 6 * dMax * dMax);
        }
        else
        {
            c = d / span;
        }
        return Math.Clamp(c, 0, 1);
    }

    static double Cube(double x) => x <= 0 ? 0 : x * x * x;

    // Гласные примерно соответствуют слогам: русские и латинские гласные.
    public static int SyllableCount(string text)
    {
        int count = 0;
        foreach (char c in text)
            if ("аеёиоуыэюяaeiouy".Contains(char.ToLowerInvariant(c))) count++;
        return count;
    }

    static void LogPauseCap(string text, int syllables, double gap, TimeSpan capped)
    {
        App.Log($"[pause-cap] \"{text}\" syllables={syllables} gap={gap:0.0}s end={capped.TotalSeconds:0.00}s");
    }

    // Средняя длина строк по таймингам начала: интервалы между соседними строками. Последняя строка не входит,
    // у неё нет собственного интервала.
    public static TimeSpan? AverageLineLength(LyricsService.Line[] lines)
    {
        long total = 0;
        int count = 0;
        for (int i = 0; i + 1 < lines.Length; i++)
        {
            long gap = lines[i + 1].Time.Ticks - lines[i].Time.Ticks;
            if (gap <= 0) continue;
            total += gap;
            count++;
        }
        return count == 0 ? null : TimeSpan.FromTicks(total / count);
    }

    public const double OutroNearEndSeconds = 5;
    public const double OutroGapFactor = 3;

    public static TimeSpan? TrackEndFor(TrackDuration source, double candidateDuration)
        => source.IsKnown ? source.Value : CandidateEnd(candidateDuration);

    static TimeSpan? CandidateEnd(double seconds)
        => double.IsFinite(seconds) && seconds is > 0 and <= 86400 ? TimeSpan.FromSeconds(seconds) : null;

    // Пустая метка в самом конце (конец последней строки из источника) может стоять у конца трека и растянуть
    // последнюю строку до хвоста песни. Тогда метку переносим на среднюю длину строки от начала последней строки.
    public static LyricsService.Line[] TrimUntrustedOutro(LyricsService.Line[] lines, TrackDuration source, double candidateDuration, out string report)
    {
        report = "";
        int last = LastTextIndex(lines);
        if (last < 0 || last == lines.Length - 1) return lines;
        LyricsService.Line text = lines[last], marker = lines[last + 1];
        TimeSpan average = AverageLineLength(lines[..(last + 1)]) is { } interval && interval < MaxFinalLineLength
            ? interval : MaxFinalLineLength;
        TimeSpan? trackEnd = TrackEndFor(source, candidateDuration);
        report = $"outro: track={trackEnd?.TotalSeconds:0.00}s lastText={text.Time.TotalSeconds:0.00}s marker={marker.Time.TotalSeconds:0.00}s average={average.TotalSeconds:0.00}s";
        if (trackEnd is not { } end) return lines;
        bool nearEnd = end - marker.Time <= TimeSpan.FromSeconds(OutroNearEndSeconds);
        bool longGap = marker.Time - text.Time > average * OutroGapFactor;
        if (!nearEnd && !longGap) return lines;
        TimeSpan moved = text.Time + average;
        if (moved > end) moved = end;
        if (moved <= text.Time || moved >= marker.Time) return lines;
        report += $" -> marker moved to {moved.TotalSeconds:0.00}s" + (nearEnd ? " (near track end)" : "") + (longGap ? " (long gap)" : "");
        LyricsService.Line[] result = (LyricsService.Line[])lines.Clone();
        result[last + 1] = marker with { Time = moved };
        return result;
    }

    public static double Progress(TimeSpan at, TimeSpan start, TimeSpan? end)
    {
        if (end is not { } finish || finish <= start) return 0;
        return Math.Clamp((at.Ticks - (double)start.Ticks) / (finish.Ticks - (double)start.Ticks), 0, 1);
    }

    public static double ScrollOffset(double overflow, double inset, TimeSpan at, TimeSpan start, TimeSpan? end)
    {
        if (!double.IsFinite(overflow) || overflow <= 0 || !double.IsFinite(inset)) return 0;
        if (end is not { } finish || finish <= start) return inset;
        double seconds = (finish.Ticks - (double)start.Ticks) / TimeSpan.TicksPerSecond;
        double elapsed = Math.Clamp((at.Ticks - (double)start.Ticks) / TimeSpan.TicksPerSecond, 0, seconds);
        double hold = Math.Min(0.6, seconds * 0.2);
        double tail = Math.Min(0.5, seconds * 0.2);
        double run = Math.Max(seconds - hold - tail, 0.001);
        double fraction = Math.Clamp((elapsed - hold) / run, 0, 1);
        // Smooth interpolation follows playback position, so pause/seek/rate
        // need no animation restart or wall-clock timer.
        double eased = 0.5 - Math.Cos(Math.PI * fraction) * 0.5;
        return inset - overflow * eased;
    }

    public static double MarkerFraction(TimeSpan lineStart, TimeSpan duration, int userAdvanceMs)
    {
        if (duration <= TimeSpan.Zero) return 0;
        long shift = (long)(SourceLeadMs + LyricsOffsetStore.Normalize(userAdvanceMs)) * TimeSpan.TicksPerMillisecond;
        return Math.Clamp((lineStart.Ticks - (double)shift) / duration.Ticks, 0, 1);
    }
}
