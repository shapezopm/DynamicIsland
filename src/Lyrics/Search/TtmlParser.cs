using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace DynamicIsland;

// Reads absolute paragraph timing from TTML. Relative (seq) timing and DTD declarations fail closed: no timing is invented.
internal static class TtmlParser
{
    const int MaxBytes = 1024 * 1024;
    const int MaxLines = 2000;
    static readonly Regex ClockPattern = new(@"^(?:(\d+):)?(?:(\d+):)?(\d+(?:\.\d{1,3})?)$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static LyricsService.Line[] Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxBytes) return [];
        if (text.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || text.Contains("<!ENTITY", StringComparison.OrdinalIgnoreCase)) return [];
        XDocument document;
        try { document = XDocument.Parse(text, LoadOptions.None); }
        catch (XmlException) { return []; }
        XElement root = document.Root!;
        if (root.DescendantsAndSelf().Any(node => node.Attribute("timeContainer")?.Value == "seq")) return [];

        // A paragraph that starts before its enclosing div or body is relative timing we do not support.
        foreach (XElement container in root.DescendantsAndSelf().Where(node => LocalName(node) is "body" or "div"))
        {
            TimeSpan? containerStart = BeginOf(container);
            if (containerStart is null) continue;
            if (container.Descendants().Where(node => LocalName(node) == "p")
                .Any(paragraph => BeginOf(paragraph) is { } start && start < containerStart)) return [];
        }

        var lines = new List<LyricsService.Line>();
        foreach (XElement paragraph in root.Descendants().Where(node => LocalName(node) == "p"))
        {
            TimeSpan? start = StartOf(paragraph);
            if (start is null) continue;
            string content = Collapse(VisibleText(paragraph));
            if (content.Length == 0) continue;
            lines.Add(new LyricsService.Line(start.Value, content));
            if (lines.Count >= MaxLines) break;
        }
        return lines.OrderBy(line => line.Time).ToArray();
    }

    static string LocalName(XElement node) => node.Name.LocalName;

    static TimeSpan? BeginOf(XElement node)
    {
        string? value = node.Attributes().FirstOrDefault(attr => attr.Name.LocalName == "begin")?.Value;
        return value is null ? null : ParseClock(value);
    }

    // A paragraph without its own begin takes the earliest begin among its spans.
    static TimeSpan? StartOf(XElement paragraph)
    {
        TimeSpan? own = BeginOf(paragraph);
        if (own is not null) return own;
        var spanStarts = paragraph.Descendants().Select(BeginOf).Where(value => value is not null)
            .Select(value => value!.Value).ToList();
        return spanStarts.Count > 0 ? spanStarts.Min() : null;
    }

    static TimeSpan? ParseClock(string value)
    {
        Match match = ClockPattern.Match(value.Trim());
        if (!match.Success) return null;
        double seconds = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        if (match.Groups[2].Success) seconds += double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60;
        if (match.Groups[1].Success) seconds += double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 3600;
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > 24 * 3600) return null;
        return TimeSpan.FromSeconds(seconds);
    }

    // Translation, romanisation and background-vocal layers are not the lyric line itself.
    static string VisibleText(XElement node)
    {
        string role = node.Attributes().FirstOrDefault(attr => attr.Name.LocalName == "role")?.Value ?? "";
        if (role is "x-translation" or "x-roman" or "x-bg" or "translation" or "romanization") return "";
        var builder = new StringBuilder();
        foreach (XNode child in node.Nodes())
        {
            switch (child)
            {
                case XText text:
                    builder.Append(text.Value);
                    break;
                case XElement element when LocalName(element) == "br":
                    builder.Append(' ');
                    break;
                case XElement element:
                    builder.Append(VisibleText(element));
                    break;
            }
        }
        return builder.ToString();
    }

    static string Collapse(string text)
    {
        string cleaned = new(text.Where(ch => !char.IsControl(ch) || char.IsWhiteSpace(ch)).ToArray());
        return Spaces.Replace(cleaned, " ").Trim();
    }
}
