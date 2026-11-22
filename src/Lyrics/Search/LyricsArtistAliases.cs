namespace DynamicIsland;

// Explicit aliases confirmed for this project's listening catalog. Never infer them from fuzzy spelling.
internal static class LyricsArtistAliases
{
    static readonly Dictionary<string, string> Confirmed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vzkill"] = "VZKillTurbo2000",
        ["vzk1ll"] = "VZKillTurbo2000",
        ["империя зла + империя харма щенят"] = "империя зла",
    };

    public static bool IsKnownAlias(string artist) => Confirmed.ContainsKey(HandleName(artist.Trim()));

    public static string Canonicalize(string artist)
    {
        string value = artist.Trim();
        return Confirmed.TryGetValue(HandleName(value), out string? canonical) ? canonical : value;
    }

    static string HandleName(string value) => value.StartsWith('@') && !value.StartsWith("@@", StringComparison.Ordinal)
        ? value[1..] : value;
}
