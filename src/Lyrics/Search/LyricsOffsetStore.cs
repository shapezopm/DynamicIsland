using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace DynamicIsland;

internal sealed class LyricsOffsetStore
{
    const string RegistryPath = @"Software\DynamicIsland\LyricsOffsets";
    public const int MaximumAdvanceMs = 5000;
    public const int StepMs = 100;
    readonly Func<string, int?> _read;
    readonly Action<string, int?> _write;

    internal LyricsOffsetStore(Func<string, int?>? read = null, Action<string, int?>? write = null)
    {
        _read = read ?? ReadRegistry;
        _write = write ?? WriteRegistry;
    }

    internal int Read(string key)
    {
        try { return Normalize(_read(key) ?? 0); }
        catch { return 0; }
    }

    internal int Set(string key, int milliseconds)
    {
        int value = Normalize(milliseconds);
        try { _write(key, value == 0 ? null : value); }
        catch (Exception failure) { App.Log(failure); }
        return value;
    }

    internal static int Normalize(int milliseconds) => Math.Clamp((int)Math.Round(
        Math.Clamp(milliseconds, -MaximumAdvanceMs, MaximumAdvanceMs) / (double)StepMs,
        MidpointRounding.AwayFromZero) * StepMs, -MaximumAdvanceMs, MaximumAdvanceMs);

    internal static string Key(TrackSnapshot track, LyricsCandidate? candidate)
    {
        string[] identity;
        if (candidate is { RecordId: > 0 })
            identity = ["record-v1", track.SourceApp, candidate.ProviderId, candidate.RecordId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        else if (candidate is not null)
            identity = ["document-v2", track.SourceApp, LyricsDocumentIdentity.Key(candidate)];
        else
        {
            NormalizedLyricsMetadata metadata = LyricsNormalizer.Normalize(track.Title, track.Artist);
            identity = ["metadata-v1", track.SourceApp, metadata.Title.ToUpperInvariant(), metadata.Artist.ToUpperInvariant(),
                track.Album.ToUpperInvariant(), string.Join('|', metadata.VersionTags)];
        }
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(identity)));
    }

    static int? ReadRegistry(string key)
    {
        using RegistryKey? registry = Registry.CurrentUser.OpenSubKey(RegistryPath);
        return registry?.GetValue(key) as int?;
    }

    static void WriteRegistry(string key, int? value)
    {
        using RegistryKey registry = Registry.CurrentUser.CreateSubKey(RegistryPath);
        if (value.HasValue) registry.SetValue(key, value.Value, RegistryValueKind.DWord);
        else registry.DeleteValue(key, throwOnMissingValue: false);
    }
}
