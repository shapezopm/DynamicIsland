using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DynamicIsland;

// Shared JSON helpers for the lyrics providers that read timed lines (LRC Mux, LiriQo).
// Every read is defensive: a missing or wrong-typed field yields a neutral value, never an exception.
internal static class ProviderJson
{
    internal const int MaxLines = 2000;
    internal const int MaxLineLength = 500;
    internal const int MaxBodyBytes = 2 * 1024 * 1024;
    internal static readonly TimeSpan DurationSlack = TimeSpan.FromSeconds(18);

    internal static string Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value)
           && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    // Seconds, positive finite only; anything else is 0 (unknown).
    internal static double Seconds(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value)
           && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double seconds)
           && double.IsFinite(seconds) && seconds > 0 ? seconds : 0;

    // A line start in milliseconds. False when the field is absent, not a number, negative or not finite.
    internal static bool Millis(JsonElement element, string name, out double milliseconds)
    {
        milliseconds = -1;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out milliseconds)
            && double.IsFinite(milliseconds) && milliseconds >= 0;
    }

    // Drops control characters, trims, and caps length. Lyric text is never rewritten beyond that.
    internal static string Clean(string raw)
    {
        var builder = new StringBuilder(raw.Length);
        foreach (char c in raw)
            if (!char.IsControl(c)) builder.Append(c);
        string text = builder.ToString().Trim();
        return text.Length > MaxLineLength ? text[..MaxLineLength] : text;
    }

    internal static string Format(double seconds)
        => seconds.ToString("0.###", CultureInfo.InvariantCulture);
}
