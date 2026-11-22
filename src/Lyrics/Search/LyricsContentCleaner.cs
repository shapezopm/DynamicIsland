using System.Text.RegularExpressions;

namespace DynamicIsland;

// Presentation only: provider documents and their timestamps remain unchanged.
internal static class LyricsContentCleaner
{
    static readonly Regex Credit = new(@"^(作词|作曲|编曲|制作人|监制|词|曲|lyrics by|lyricist|composer|composed by|written by|produced by|producer|arranged by|автор текста|автор музыки|слова|музыка|композитор|продюсер|аранжировка)\s*[:：]\s*\S",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    internal static bool IsCredit(string text) => text.Length <= 4096 && Credit.IsMatch(text.Trim().TrimStart('\uFEFF').TrimStart());

    internal static string Text(string text)
    {
        string[] rows = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return rows.Any(IsCredit) ? string.Join(Environment.NewLine, rows.Where(row => !IsCredit(row))) : text;
    }

    // Строки с авторами из того же текста, что скрыт в Text. Не больше трёх строк, каждая короче 120 символов.
    internal static string Credits(string text)
    {
        string[] rows = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        string[] found = rows.Where(IsCredit).Take(3)
            .Select(row => row.Trim().TrimStart('﻿').Trim())
            .Select(row => row.Length > 120 ? row[..120] + "…" : row)
            .ToArray();
        return string.Join(" · ", found);
    }

    internal static LyricsService.Line[] Lines(LyricsService.Line[] lines)
    {
        LyricsService.Line[]? clean = null;
        for (int i = 0; i < lines.Length; i++)
        {
            string text = Text(lines[i].Text);
            if (text == lines[i].Text) continue;
            clean ??= (LyricsService.Line[])lines.Clone();
            // Empty events retain the end of the preceding vocal interval.
            clean[i] = lines[i] with { Text = text };
        }
        return clean ?? lines;
    }
}
