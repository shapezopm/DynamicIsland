using System.Security.Cryptography;
using System.Text.Json;

namespace DynamicIsland;

internal static class LyricsDocumentIdentity
{
    internal static string Key(LyricsCandidate? candidate) => candidate is null ? "" : Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new { candidate.ProviderId, candidate.RecordId, candidate.SourceUrl,
            candidate.Title, candidate.Artist, candidate.Duration, candidate.Lines, candidate.PlainLyrics })));
}
