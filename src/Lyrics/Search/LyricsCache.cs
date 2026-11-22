using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DynamicIsland;

internal sealed record LyricsCacheOptions
{
    public int MaxMemoryEntries { get; init; } = 128;
    public int MaxMemoryCandidates { get; init; } = 128;
    public long MaxMemoryBytes { get; init; } = 8 * 1024 * 1024;
    public int MaxDiskEntries { get; init; } = 500;
    public long MaxDiskBytes { get; init; } = 20 * 1024 * 1024;
    public int MaxFileBytes { get; init; } = 2 * 1024 * 1024;
    public int MaxCandidates { get; init; } = 120;
    public TimeSpan HitTtl { get; init; } = TimeSpan.FromDays(7);
    public TimeSpan MissTtl { get; init; } = TimeSpan.FromMinutes(10);
    public int NormalizationVersion { get; init; } = LyricsNormalizer.Version;
}

// This stores provider pools, never a selected winner. Each hit must pass the current matcher.
internal sealed class LyricsCache
{
    const string Format = "DynamicIslandLyrics";
    const int SchemaVersion = 1;
    static readonly ConcurrentDictionary<string, DirectoryState> Directories = new(StringComparer.OrdinalIgnoreCase);
    static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 16 };
    readonly string _directory;
    readonly TimeProvider _time;
    readonly LyricsCacheOptions _options;
    readonly Func<string> _providerPolicy;
    readonly DirectoryState _state;
    readonly Dictionary<string, MemoryEntry> _memory = new(StringComparer.Ordinal);
    readonly LinkedList<string> _lru = new();
    long _memoryGeneration;
    int _memoryCandidates;
    long _memoryBytes;

    public LyricsCache(string? directory = null, TimeProvider? time = null, LyricsCacheOptions? options = null,
        Func<string>? providerPolicy = null)
    {
        _directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "LyricsCache")));
        if (string.Equals(_directory, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(_directory)!), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The cache directory cannot be a filesystem root.", nameof(directory));
        _time = time ?? TimeProvider.System;
        _options = options ?? new();
        _providerPolicy = providerPolicy ?? (() => "");
        if (_options.MaxMemoryEntries is < 1 or > 128 || _options.MaxDiskEntries is < 1 or > 500
            || _options.MaxMemoryCandidates is < 1 or > 128 || _options.MaxMemoryBytes is < 1 or > 20 * 1024 * 1024
            || _options.MaxDiskBytes is < 1 or > 20 * 1024 * 1024 || _options.MaxFileBytes is < 1 or > 2 * 1024 * 1024
            || _options.MaxCandidates is < 1 or > 120 || _options.HitTtl <= TimeSpan.Zero || _options.HitTtl > TimeSpan.FromDays(7)
            || _options.MissTtl <= TimeSpan.Zero || _options.MissTtl > TimeSpan.FromMinutes(10) || _options.NormalizationVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        _state = Directories.GetOrAdd(_directory, static _ => new());
        _memoryGeneration = Interlocked.Read(ref _state.Generation);
    }

    public Task<ProviderSearchResult?> GetAsync(TrackSnapshot track, bool exact, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string policy = _providerPolicy();
        return Task.Run(() => GetCoreAsync(track, exact, policy, cancellationToken), cancellationToken);
    }

    public Task PutAsync(TrackSnapshot track, bool exact, ProviderSearchResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string policy = _providerPolicy();
        long generation = Interlocked.Read(ref _state.Generation);
        return Task.Run(() => PutCoreAsync(track, exact, result, generation, policy, cancellationToken), cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Invalidate writes already waiting for IO, including those in another instance.
        Interlocked.Increment(ref _state.Generation);
        return Task.Run(() => ClearCoreAsync(cancellationToken), cancellationToken);
    }

    async Task<ProviderSearchResult?> GetCoreAsync(TrackSnapshot track, bool exact, string policy, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string? key = Key(track, exact, policy);
        if (key is null) return null;
        await _state.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            SynchronizeMemory();
            if (!SafeDirectory()) return null;
            if (_memory.TryGetValue(key, out MemoryEntry? memory))
            {
                if (memory.Document.ExpiresUtc > _time.GetUtcNow())
                {
                    Touch(memory);
                    TouchDisk(FilePath(key));
                    return Clone(memory.Document.Result!);
                }
                RemoveMemory(key);
            }
            CacheDocument? document = await ReadAsync(FilePath(key), key, token).ConfigureAwait(false);
            if (document is null || document.ExpiresUtc <= _time.GetUtcNow()) return null;
            AddMemory(key, document);
            TouchDisk(FilePath(key));
            return Clone(document.Result!);
        }
        catch (Exception exception) when (IoFailure(exception)) { return null; }
        finally { _state.Gate.Release(); }
    }

    async Task PutCoreAsync(TrackSnapshot track, bool exact, ProviderSearchResult result, long generation, string policy, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string? key = Key(track, exact, policy);
        if (key is null || !ValidResult(result)) return;
        DateTimeOffset created = _time.GetUtcNow();
        var document = new CacheDocument
        {
            Format = Format, SchemaVersion = SchemaVersion, NormalizationVersion = _options.NormalizationVersion,
            Key = key, CreatedUtc = created, ExpiresUtc = created + (result.Candidates.Length == 0 ? _options.MissTtl
                : result.NeedsRefresh ? TimeSpan.FromTicks(Math.Min(_options.HitTtl.Ticks, TimeSpan.FromMinutes(1).Ticks)) : _options.HitTtl),
            Result = Clone(result),
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        if (bytes.Length > _options.MaxFileBytes || bytes.Length > _options.MaxDiskBytes) return;
        document.ByteSize = bytes.Length;
        await _state.Gate.WaitAsync(token).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            SynchronizeMemory();
            if (generation != Interlocked.Read(ref _state.Generation) || !SafeDirectory()) return;
            Directory.CreateDirectory(_directory);
            if (!SafeDirectory()) return;
            string destination = FilePath(key);
            if (File.Exists(destination) && !SafeFile(destination)) return;
            // Unsupported future records survive: a normalization/schema upgrade is a miss, not a destructive migration.
            if (File.Exists(destination) && !await CanModifyRecordAsync(destination, key, token).ConfigureAwait(false)) return;
            temporary = Path.Combine(_directory, key + "." + Guid.NewGuid().ToString("N") + ".lyrics.tmp");
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                8192, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            if (generation != Interlocked.Read(ref _state.Generation) || !SafeDirectory()
                || !SafeFile(temporary) || File.Exists(destination) && !SafeFile(destination)) return;
            File.Move(temporary, destination, true);
            temporary = null;
            TouchDisk(destination);
            AddMemory(key, document);
            await PruneAsync(token).ConfigureAwait(false);
        }
        catch (Exception exception) when (IoFailure(exception)) { }
        finally
        {
            if (temporary is not null && SafeDirectory() && SafeFile(temporary))
            {
                try { File.Delete(temporary); } catch (Exception exception) when (IoFailure(exception)) { }
            }
            _state.Gate.Release();
        }
    }

    async Task ClearCoreAsync(CancellationToken token)
    {
        await _state.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            SynchronizeMemory();
            _memory.Clear();
            _lru.Clear();
            _memoryCandidates = 0;
            _memoryBytes = 0;
            if (!SafeDirectory() || !Directory.Exists(_directory)) return;
            foreach (string path in Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                string? key = FileKey(path);
                if (key is null || !SafeFile(path)) continue;
                if (await CanModifyRecordAsync(path, key, token).ConfigureAwait(false))
                {
                    if (!SafeDirectory() || !SafeFile(path)) return;
                    File.Delete(path);
                }
            }
        }
        catch (Exception exception) when (IoFailure(exception)) { }
        finally { _state.Gate.Release(); }
    }

    async Task PruneAsync(CancellationToken token)
    {
        var records = new List<(string Path, string Key, long Bytes, DateTimeOffset Created, DateTimeOffset Used)>();
        foreach (string path in Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            token.ThrowIfCancellationRequested();
            string? key = FileKey(path);
            if (key is null || !SafeFile(path)) continue;
            CacheHeader? header = await ReadHeaderAsync(path, key, token).ConfigureAwait(false);
            if (header is null)
            {
                if (await CanModifyRecordAsync(path, key, token).ConfigureAwait(false))
                {
                    if (!SafeDirectory() || !SafeFile(path)) return;
                    File.Delete(path);
                    RemoveMemory(key);
                }
                continue;
            }
            if (header.Value.ExpiresUtc <= _time.GetUtcNow())
            {
                if (!SafeDirectory() || !SafeFile(path)) return;
                File.Delete(path);
                RemoveMemory(key);
                continue;
            }
            var info = new FileInfo(path);
            DateTimeOffset lastUse = info.LastWriteTimeUtc;
            DateTimeOffset now = _time.GetUtcNow();
            if (lastUse > now) lastUse = now;
            if (lastUse < header.Value.CreatedUtc) lastUse = header.Value.CreatedUtc;
            records.Add((path, key, info.Length, header.Value.CreatedUtc, lastUse));
        }
        long size = records.Sum(record => record.Bytes);
        int count = records.Count;
        // Memory recency wins; persisted file timestamps retain usage order after restart.
        var recency = _lru.Select((key, index) => (key, index)).ToDictionary(item => item.key, item => item.index);
        foreach (var record in records.OrderBy(record => recency.ContainsKey(record.Key) ? 1 : 0)
            .ThenBy(record => recency.TryGetValue(record.Key, out int rank) ? rank : -1)
            .ThenBy(record => record.Used).ThenBy(record => record.Created))
        {
            if (count <= _options.MaxDiskEntries && size <= _options.MaxDiskBytes) break;
            if (!SafeDirectory() || !SafeFile(record.Path)) return;
            File.Delete(record.Path);
            RemoveMemory(record.Key);
            size -= record.Bytes;
            count--;
        }
    }

    async Task<CacheDocument?> ReadAsync(string path, string key, CancellationToken token)
    {
        if (!SafeDirectory() || !SafeFile(path)) return null;
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 || info.Length > _options.MaxFileBytes) return null;
        byte[] bytes = new byte[info.Length];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (await stream.ReadAsync(new byte[1], token).ConfigureAwait(false) != 0) return null;
        token.ThrowIfCancellationRequested();
        CacheDocument? document;
        try { document = JsonSerializer.Deserialize<CacheDocument>(bytes, JsonOptions); }
        catch (JsonException) { return null; }
        if (!Owned(document, key) || document!.NormalizationVersion != _options.NormalizationVersion
            || document.CreatedUtc > _time.GetUtcNow() || document.ExpiresUtc <= document.CreatedUtc
            || document.Result is null || !ValidResult(document.Result)) return null;
        TimeSpan ttl = document.Result.Candidates.Length == 0 ? _options.MissTtl : _options.HitTtl;
        document.ByteSize = bytes.Length;
        return document.ExpiresUtc - document.CreatedUtc <= ttl ? document : null;
    }

    async Task<CacheHeader?> ReadHeaderAsync(string path, string key, CancellationToken token)
    {
        if (!SafeDirectory() || !SafeFile(path)) return null;
        long length = new FileInfo(path).Length;
        if (length is <= 0 || length > _options.MaxFileBytes) return null;
        byte[] prefix = await ReadPrefixAsync(path, length, token).ConfigureAwait(false);
        return ParseHeader(prefix, key, _options.NormalizationVersion, _time.GetUtcNow());
    }

    static async Task<byte[]> ReadPrefixAsync(string path, long length, CancellationToken token)
    {
        byte[] prefix = new byte[Math.Min(length, 4096)];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        await stream.ReadExactlyAsync(prefix, token).ConfigureAwait(false);
        return prefix;
    }

    // Pruning needs only storage metadata, not hundreds of repeated LRC/normalizer validations.
    // The owned writer places these properties before Result. GetAsync still validates the full document.
    static CacheHeader? ParseHeader(byte[] prefix, string key, int normalizationVersion, DateTimeOffset now)
    {
        try
        {
            var reader = new Utf8JsonReader(prefix, false, new JsonReaderState(new() { MaxDepth = 16 }));
            bool format = false, schema = false, identity = false, normalization = false;
            DateTimeOffset? created = null, expires = null;
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1) continue;
                string? property = reader.GetString();
                if (property == nameof(CacheDocument.Result))
                    return format && schema && identity && normalization && created is DateTimeOffset createdAt
                        && expires is DateTimeOffset expiresAt && createdAt <= now && expiresAt > createdAt
                        ? new(createdAt, expiresAt) : null;
                if (!reader.Read()) return null;
                switch (property)
                {
                    case nameof(CacheDocument.Format): format = reader.TokenType == JsonTokenType.String && reader.GetString() == Format; break;
                    case nameof(CacheDocument.SchemaVersion): schema = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int version) && version == SchemaVersion; break;
                    case nameof(CacheDocument.NormalizationVersion): normalization = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int algorithm) && algorithm == normalizationVersion; break;
                    case nameof(CacheDocument.Key): identity = reader.TokenType == JsonTokenType.String && reader.GetString() == key; break;
                    case nameof(CacheDocument.CreatedUtc): if (reader.TokenType == JsonTokenType.String && reader.TryGetDateTimeOffset(out DateTimeOffset value)) created = value; break;
                    case nameof(CacheDocument.ExpiresUtc): if (reader.TokenType == JsonTokenType.String && reader.TryGetDateTimeOffset(out DateTimeOffset end)) expires = end; break;
                }
            }
        }
        catch (JsonException failure) { App.Log(failure); }
        return null;
    }

    async Task<bool> CanModifyRecordAsync(string path, string key, CancellationToken token)
    {
        if (!SafeDirectory() || !SafeFile(path)) return false;
        long length = new FileInfo(path).Length;
        if (length <= 0) return true;
        if (length > _options.MaxFileBytes)
            return !PreserveOwnershipPrefix(await ReadPrefixAsync(path, length, token).ConfigureAwait(false), key);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        try
        {
            using JsonDocument json = await JsonDocument.ParseAsync(stream, new() { MaxDepth = 16 }, token).ConfigureAwait(false);
            JsonElement root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return true;
            if (root.TryGetProperty(nameof(CacheDocument.Format), out JsonElement format)
                && format.ValueKind == JsonValueKind.String && format.GetString() != Format) return false;
            if (root.TryGetProperty(nameof(CacheDocument.SchemaVersion), out JsonElement schema)
                && schema.ValueKind == JsonValueKind.Number && schema.TryGetInt32(out int version) && version != SchemaVersion) return false;
            return !root.TryGetProperty(nameof(CacheDocument.Key), out JsonElement identifier)
                || identifier.ValueKind != JsonValueKind.String || identifier.GetString() == key;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return true; }
    }

    // A future schema may have a larger payload limit. Inspect its bounded header without parsing that payload.
    static bool PreserveOwnershipPrefix(byte[] prefix, string key)
    {
        try
        {
            var reader = new Utf8JsonReader(prefix, false, new JsonReaderState(new() { MaxDepth = 16 }));
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1) continue;
                string? property = reader.GetString();
                if (property == nameof(CacheDocument.Result) || !reader.Read()) return false;
                if (property == nameof(CacheDocument.Format) && reader.TokenType == JsonTokenType.String && reader.GetString() != Format
                    || property == nameof(CacheDocument.SchemaVersion) && reader.TokenType == JsonTokenType.Number
                        && reader.TryGetInt32(out int schema) && schema != SchemaVersion
                    || property == nameof(CacheDocument.Key) && reader.TokenType == JsonTokenType.String && reader.GetString() != key) return true;
            }
        }
        catch (JsonException failure) { App.Log(failure); }
        return false;
    }

    bool ValidResult(ProviderSearchResult result)
    {
        if (result is null || !result.Complete || result.ErrorCode is not null || result.RetryAt is not null
            || result.NeedsRefresh && result.Candidates is { Length: 0 }
            || result.Candidates is null || result.Candidates.Length > _options.MaxCandidates
            || (result.Candidates.Length == 0 ? result.Status != LyricsStatus.NotFound : result.Status != LyricsStatus.AwaitingVerification)) return false;
        long bytes = 0;
        foreach (LyricsCandidate candidate in result.Candidates)
        {
            if (candidate is null || !ValidSource(candidate) || candidate.RecordId is <= 0
                || !Metadata(candidate.Title) || !Metadata(candidate.Artist) || !Metadata(candidate.Album)
                || !double.IsFinite(candidate.Duration) || candidate.Duration is < 0 or > 86400
                || !double.IsFinite(candidate.End) || candidate.End is < 0 or > 86400
                || candidate.Lines is null || candidate.Lines.Length > 5000
                || candidate.VersionTags is null || candidate.VersionTags.Length > 32
                || !candidate.VersionTags.SequenceEqual(LyricsNormalizer.ExtractVersionTags(candidate.Title), StringComparer.Ordinal)
                || candidate.TimingWarnings is null || candidate.TimingWarnings.Length > 101
                || candidate.TimingWarnings.Any(warning => warning is null || warning.Length > 128)
                || candidate.PlainLyrics?.Length > 1024 * 1024) return false;
            long previous = -1;
            bool nonempty = false;
            foreach (LyricsService.Line line in candidate.Lines)
            {
                if (line.Time.Ticks <= previous || line.Time < TimeSpan.Zero || line.Time > TimeSpan.FromDays(1)
                    || line.Text is null || line.Text.Length > 4096) return false;
                previous = line.Time.Ticks;
                nonempty |= !string.IsNullOrWhiteSpace(line.Text);
                bytes += Encoding.UTF8.GetByteCount(line.Text);
            }
            if (candidate.HasValidSync && !nonempty || candidate.End != (candidate.Lines.Length > 0 ? candidate.Lines[^1].Time.TotalSeconds : 0)) return false;
            bytes += Encoding.UTF8.GetByteCount(candidate.PlainLyrics ?? "") + Encoding.UTF8.GetByteCount(candidate.Title)
                + Encoding.UTF8.GetByteCount(candidate.Artist) + Encoding.UTF8.GetByteCount(candidate.Album);
            if (bytes > _options.MaxFileBytes) return false;
        }
        return true;
    }

    static bool ValidSource(LyricsCandidate candidate) => LyricsSources.ValidSource(candidate);

    string? Key(TrackSnapshot track, bool exact, string policy)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!Metadata(track.Title) || !Metadata(track.Artist) || !Metadata(track.Album)
            || string.IsNullOrWhiteSpace(track.Title) || LyricsNormalizer.BuildQueries(track).Length == 0) return null;
        if (exact && (!track.Duration.CanMatch || track.Duration.Value.TotalSeconds is < 1 or > 86400)) return null;
        NormalizedLyricsMetadata normalized = LyricsNormalizer.Normalize(track.Title, track.Artist);
        string[] fields = ["LRCLIB", _options.NormalizationVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Canonical(normalized.Title), Canonical(normalized.Artist), Canonical(track.Album),
            string.Join("\u001f", normalized.VersionTags.Order(StringComparer.Ordinal)), exact ? "exact-media-session" : "search",
            exact ? track.Duration.Value.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : ""];
        if (policy.Length > 128 || policy.Any(char.IsControl)) return null;
        if (policy.Length > 0) fields = [.. fields, policy];
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(fields))).ToLowerInvariant();
    }

    static string Canonical(string value) => string.Join(" ", value.Normalize(NormalizationForm.FormKC)
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    static bool Metadata(string? value) => value is not null && value.Length <= 512 && !value.Any(char.IsControl)
        && WellFormed(value);

    static bool WellFormed(string text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            char value = text[index];
            if (!char.IsSurrogate(value)) continue;
            if (!char.IsHighSurrogate(value) || index + 1 == text.Length || !char.IsLowSurrogate(text[++index])) return false;
        }
        return true;
    }

    bool SafeDirectory()
    {
        try
        {
            for (DirectoryInfo? current = new(_directory); current is not null; current = current.Parent)
            {
                try { if ((File.GetAttributes(current.FullName) & FileAttributes.ReparsePoint) != 0) return false; }
                catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException) { }
            }
            return true;
        }
        catch (Exception exception) when (IoFailure(exception)) { return false; }
    }

    bool SafeFile(string path)
    {
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), _directory, StringComparison.OrdinalIgnoreCase)) return false;
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0; }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException) { return true; }
        catch (Exception exception) when (IoFailure(exception)) { return false; }
    }

    void TouchDisk(string path)
    {
        if (!SafeDirectory() || !SafeFile(path) || !File.Exists(path)) return;
        try { File.SetLastWriteTimeUtc(path, _time.GetUtcNow().UtcDateTime); }
        catch (Exception exception) when (IoFailure(exception)) { }
    }

    string FilePath(string key) => Path.Combine(_directory, key + ".json");
    static string? FileKey(string path)
    {
        string name = Path.GetFileName(path);
        if (name.Length != 69 || !name.EndsWith(".json", StringComparison.Ordinal)) return null;
        string key = name[..64];
        return key.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f') ? key : null;
    }

    static bool Owned(CacheDocument? document, string key) => document?.Format == Format && document.SchemaVersion == SchemaVersion && document.Key == key;
    static bool IoFailure(Exception exception) => exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;
    static ProviderSearchResult Clone(ProviderSearchResult result) => result with
    {
        Candidates = result.Candidates.Select(candidate => candidate with
        {
            Lines = (LyricsService.Line[])candidate.Lines.Clone(),
            VersionTags = (string[])candidate.VersionTags.Clone(),
            TimingWarnings = (string[])candidate.TimingWarnings.Clone(),
        }).ToArray(),
    };

    void SynchronizeMemory()
    {
        long generation = Interlocked.Read(ref _state.Generation);
        if (_memoryGeneration == generation) return;
        _memory.Clear();
        _lru.Clear();
        _memoryCandidates = 0;
        _memoryBytes = 0;
        _memoryGeneration = generation;
    }

    void Touch(MemoryEntry entry) { _lru.Remove(entry.Node); _lru.AddLast(entry.Node); }
    void RemoveMemory(string key)
    {
        if (!_memory.Remove(key, out MemoryEntry? entry)) return;
        _lru.Remove(entry.Node);
        _memoryCandidates -= Math.Max(1, entry.Document.Result!.Candidates.Length);
        _memoryBytes -= entry.Document.ByteSize;
    }
    void AddMemory(string key, CacheDocument document)
    {
        RemoveMemory(key);
        _memory[key] = new(document, _lru.AddLast(key));
        _memoryCandidates += Math.Max(1, document.Result!.Candidates.Length);
        _memoryBytes += document.ByteSize;
        while (_memory.Count > _options.MaxMemoryEntries || _memoryCandidates > _options.MaxMemoryCandidates
            || _memoryBytes > _options.MaxMemoryBytes) RemoveMemory(_lru.First!.Value);
    }

    sealed class DirectoryState
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal long Generation;
    }
    sealed record MemoryEntry(CacheDocument Document, LinkedListNode<string> Node);
    readonly record struct CacheHeader(DateTimeOffset CreatedUtc, DateTimeOffset ExpiresUtc);
    sealed class CacheDocument
    {
        public string? Format { get; init; }
        public int SchemaVersion { get; init; }
        public int NormalizationVersion { get; init; }
        public string? Key { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }
        public DateTimeOffset ExpiresUtc { get; init; }
        public ProviderSearchResult? Result { get; init; }
        internal int ByteSize { get; set; }
    }
}
