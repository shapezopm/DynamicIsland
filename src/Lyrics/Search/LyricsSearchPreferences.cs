using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace DynamicIsland;

internal sealed record LyricsSearchPreference(string Title, string Artist, LyricsCandidate? Selected = null, bool Off = false);

internal sealed class LyricsSearchPreferences
{
    const string RegistryPath = @"Software\DynamicIsland\LyricsSearch";
    readonly Func<string, string?> _read;
    readonly Action<string, string?> _write;
    internal LyricsSearchPreferences(Func<string, string?>? read = null, Action<string, string?>? write = null)
    { _read = read ?? ReadRegistry; _write = write ?? WriteRegistry; }

    internal static string Key(TrackSnapshot track) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new[] { "search-v1", track.SourceApp, track.Title, track.Artist, track.Album })));

    internal LyricsSearchPreference? Read(TrackSnapshot track)
    {
        try
        {
            string? json = _read(Key(track));
            if (json is null || json.Length > 256 * 1024) return null;
            var preference = JsonSerializer.Deserialize<LyricsSearchPreference>(json);
            if (preference is null || !Valid(preference.Title, preference.Artist)) return null;
            if (preference.Selected is { } selected && !ValidCandidate(selected))
                return preference with { Selected = null };
            return preference;
        }
        catch { return null; }
    }

    internal bool Set(TrackSnapshot track, LyricsSearchPreference? preference)
    {
        if (preference is not null && (!Valid(preference.Title, preference.Artist)
            || preference.Selected is { } selected && !ValidCandidate(selected))) return false;
        try
        {
            string? json = preference is null ? null : JsonSerializer.Serialize(preference);
            if (json?.Length > 256 * 1024) return false;
            _write(Key(track), json);
            return true;
        }
        catch { return false; }
    }

    internal static bool Valid(string title, string artist) => !string.IsNullOrWhiteSpace(title)
        && title.Length <= 512 && artist is not null && artist.Length <= 512
        && !title.Any(char.IsControl) && !artist.Any(char.IsControl);

    internal static bool ValidCandidate(LyricsCandidate candidate) => LyricsSources.Enabled(candidate.ProviderId, LyricSource.All)
        && Valid(candidate.Title, candidate.Artist) && candidate.Album is { Length: <= 512 }
        && candidate.Lines is { Length: <= 5000 } && candidate.Lines.All(line => line.Text is { Length: <= 4096 })
        && candidate.PlainLyrics?.Length is not > 65536 && candidate.VersionTags is { Length: <= 32 }
        && candidate.VersionTags.All(tag => tag is { Length: <= 512 } && !tag.Any(char.IsControl))
        && candidate.TimingWarnings is { Length: <= 101 } && candidate.TimingWarnings.All(warning => warning is { Length: <= 128 })
        && double.IsFinite(candidate.Duration)
        && candidate.Duration is >= 0 and <= 86400 && double.IsFinite(candidate.End)
        && candidate.End is >= 0 and <= 86400
        && (candidate.SourceUrl is null || LyricsSources.ValidSource(candidate));

    static string? ReadRegistry(string key)
    {
        using RegistryKey? registry = Registry.CurrentUser.OpenSubKey(RegistryPath);
        return registry?.GetValue(key) as string;
    }
    static void WriteRegistry(string key, string? value)
    {
        using RegistryKey registry = Registry.CurrentUser.CreateSubKey(RegistryPath);
        if (value is null) registry.DeleteValue(key, false);
        else registry.SetValue(key, value, RegistryValueKind.String);
    }
}
