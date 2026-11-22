using System.Globalization;

namespace DynamicIsland;

internal sealed record LrcParseResult(
    LyricsService.Line[] Lines, string[] Warnings, bool IsUsable, TimeSpan LastTimestamp);

internal static class LrcParser
{
    const int MaxEvents = 5000;
    const int MaxLineLength = 4096;
    const int MaxWarnings = 100;
    const long MaxTimestampMs = 24L * 60 * 60 * 1000;

    public static LrcParseResult Parse(string lrc)
    {
        ArgumentNullException.ThrowIfNull(lrc);
        var warnings = new List<string>();
        var events = new List<LyricsService.Line>();
        long offset = 0;
        bool hasOffset = false, damaged = false, warningsTruncated = false;

        void Warn(string code, int row = 0)
        {
            if (warnings.Count < MaxWarnings)
                warnings.Add(row > 0 ? $"{code}:{row}" : code);
            else if (!warningsTruncated)
            {
                warningsTruncated = true;
                warnings.Add("WarningsTruncated");
            }
        }

        // A global offset may follow the events. Read it before parsing any time.
        int cursor = 0, rowNumber = 0;
        while (cursor < lrc.Length)
        {
            ReadOnlySpan<char> row = ReadLine(lrc, ref cursor);
            rowNumber++;
            if (row.Length > MaxLineLength) continue;
            row = NormalizeRow(row);
            if (!IsOffset(row)) continue;
            ReadOnlySpan<char> value = row[8..];
            if (value.Length == 0 || value[^1] != ']'
                || !long.TryParse(value[..^1], NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out long parsed))
            {
                damaged = true;
                Warn("InvalidOffset", rowNumber);
                continue;
            }
            if (parsed < -MaxTimestampMs || parsed > MaxTimestampMs)
            {
                damaged = true;
                Warn("OffsetOutOfRange", rowNumber);
                continue;
            }
            if (hasOffset)
            {
                Warn(parsed == offset ? "DuplicateOffset" : "ConflictingOffset", rowNumber);
                if (parsed != offset) damaged = true;
                continue;
            }
            offset = parsed;
            hasOffset = true;
        }

        cursor = rowNumber = 0;
        int timedRows = 0, invalidRows = 0;
        bool reordered = false, eventLimitReported = false;
        TimeSpan previous = TimeSpan.Zero;
        while (cursor < lrc.Length)
        {
            ReadOnlySpan<char> row = ReadLine(lrc, ref cursor);
            rowNumber++;
            if (row.Length > MaxLineLength)
            {
                damaged = true;
                Warn("LineTooLong", rowNumber);
                continue;
            }
            row = NormalizeRow(row);
            if (row.IsEmpty || IsOffset(row) || IsMetadata(row)) continue;
            if (row[0] != '[' || !LooksTimed(row[1..]))
            {
                Warn("UntimedRow", rowNumber);
                continue;
            }

            timedRows++;
            bool invalid = false;
            var times = new List<long>();
            int consumed = 0;
            while (consumed < row.Length && row[consumed] == '['
                && LooksTimed(row[(consumed + 1)..]))
            {
                int closing = row[(consumed + 1)..].IndexOf(']');
                if (closing < 0)
                {
                    invalid = true;
                    Warn("UnclosedTimestamp", rowNumber);
                    consumed = row.Length;
                    break;
                }
                closing += consumed + 1;
                ReadOnlySpan<char> token = row[(consumed + 1)..closing];
                if (!TryTimestamp(token, out long rawMs, out bool outOfRange))
                {
                    invalid = true;
                    damaged |= outOfRange;
                    Warn(outOfRange ? "TimestampOutOfRange" : "InvalidTimestamp", rowNumber);
                }
                else
                {
                    long adjusted = rawMs - offset;
                    if (adjusted < 0)
                    {
                        adjusted = 0;
                        Warn("OffsetClamped", rowNumber);
                    }
                    if (adjusted > MaxTimestampMs)
                    {
                        damaged = invalid = true;
                        Warn("AdjustedTimestampOutOfRange", rowNumber);
                    }
                    else times.Add(adjusted);
                }
                consumed = closing + 1;
            }
            if (invalid) invalidRows++;
            string text = row[consumed..].Trim().ToString();
            foreach (long ms in times)
            {
                if (events.Count == MaxEvents)
                {
                    damaged = true;
                    if (!eventLimitReported)
                    {
                        Warn("EventLimitExceeded", rowNumber);
                        eventLimitReported = true;
                    }
                    break;
                }
                TimeSpan time = TimeSpan.FromMilliseconds(ms);
                if (events.Count > 0 && time < previous) reordered = true;
                previous = time;
                events.Add(new(time, text));
            }
        }

        if (reordered) Warn("TimestampsReordered");
        var lines = new List<LyricsService.Line>();
        // LINQ OrderBy is stable; collision blocks keep their original order.
        foreach (var group in events.OrderBy(line => line.Time).GroupBy(line => line.Time))
        {
            var unique = new HashSet<string>(StringComparer.Ordinal);
            var text = new List<string>();
            bool hasEmpty = false, duplicate = false;
            foreach (LyricsService.Line line in group)
            {
                if (!unique.Add(line.Text)) { duplicate = true; continue; }
                if (line.Text.Length == 0) hasEmpty = true;
                else text.Add(line.Text);
            }
            if (duplicate) Warn("DuplicateEvent");
            if (hasEmpty && text.Count > 0) Warn("EmptyTextConflict");
            if (text.Count > 1) Warn("SimultaneousTextMerged");
            lines.Add(new(group.Key, string.Join('\n', text)));
        }
        bool hasText = lines.Any(line => line.Text.Length > 0);
        if (!hasText) Warn("NoText");
        bool tooManyInvalid = (long)invalidRows * 20 > timedRows;
        if (tooManyInvalid) Warn("InvalidTimedRowsExceeded");
        return new(lines.ToArray(), warnings.ToArray(),
            hasText && !damaged && !tooManyInvalid, lines.Count > 0 ? lines[^1].Time : TimeSpan.Zero);
    }

    static ReadOnlySpan<char> ReadLine(string input, ref int cursor)
    {
        ReadOnlySpan<char> remaining = input.AsSpan(cursor);
        int newline = remaining.IndexOf('\n');
        ReadOnlySpan<char> row = newline < 0 ? remaining : remaining[..newline];
        cursor = newline < 0 ? input.Length : cursor + newline + 1;
        if (!row.IsEmpty && row[^1] == '\r') row = row[..^1];
        return row;
    }

    static ReadOnlySpan<char> NormalizeRow(ReadOnlySpan<char> row)
    {
        row = row.Trim();
        if (!row.IsEmpty && row[0] == '\uFEFF') row = row[1..].Trim();
        return row;
    }

    static bool IsOffset(ReadOnlySpan<char> row)
        => row.StartsWith("[offset:", StringComparison.OrdinalIgnoreCase);

    static bool IsMetadata(ReadOnlySpan<char> row)
    {
        if (row.Length < 4 || row[0] != '[' || row[^1] != ']') return false;
        int colon = row.IndexOf(':');
        if (colon <= 1) return false;
        foreach (char c in row[1..colon])
            if (!char.IsAsciiLetter(c)) return false;
        return true;
    }

    static bool LooksTimed(ReadOnlySpan<char> token)
        => !token.IsEmpty && (char.IsAsciiDigit(token[0]) || token[0] is '+' or '-');

    static bool TryTimestamp(ReadOnlySpan<char> token, out long milliseconds, out bool outOfRange)
    {
        milliseconds = 0;
        outOfRange = false;
        int colon = token.IndexOf(':');
        if (colon <= 0) return false;
        ReadOnlySpan<char> minutesText = token[..colon];
        if (!AllDigits(minutesText)) return false;
        if (!long.TryParse(minutesText, NumberStyles.None, CultureInfo.InvariantCulture, out long minutes))
        {
            outOfRange = true;
            return false;
        }
        if (minutes > 24 * 60)
        {
            outOfRange = true;
            return false;
        }

        ReadOnlySpan<char> rest = token[(colon + 1)..];
        int dot = rest.IndexOf('.');
        ReadOnlySpan<char> secondsText = dot < 0 ? rest : rest[..dot];
        if (!AllDigits(secondsText)) return false;
        if (!long.TryParse(secondsText, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds))
        {
            outOfRange = true;
            return false;
        }
        if (seconds >= 60) return false;
        int fractionMs = 0;
        if (dot >= 0)
        {
            ReadOnlySpan<char> fraction = rest[(dot + 1)..];
            if (fraction.Length is < 1 or > 3 || !AllDigits(fraction)) return false;
            foreach (char c in fraction) fractionMs = fractionMs * 10 + c - '0';
            if (fraction.Length == 1) fractionMs *= 100;
            else if (fraction.Length == 2) fractionMs *= 10;
        }
        milliseconds = minutes * 60_000 + seconds * 1000 + fractionMs;
        if (milliseconds <= MaxTimestampMs) return true;
        outOfRange = true;
        return false;
    }

    static bool AllDigits(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) return false;
        foreach (char c in value)
            if (!char.IsAsciiDigit(c)) return false;
        return true;
    }
}
