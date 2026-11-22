using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace DynamicIsland;

internal sealed class ShelfCaptureStore
{
    internal const int MaxTextChars = 100_000;
    internal const long MaxImagePixels = 32_000_000;
    const string CaptureTimestamp = "yyyy-MM-dd HH-mm-ss-fff";
    internal static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "ShelfCaptures");
    readonly string _directory;
    readonly SemaphoreSlim _gate = new(1, 1);

    internal ShelfCaptureStore(string? directory = null)
    {
        _directory = Path.GetFullPath(directory ?? DefaultDirectory);
        if (Path.TrimEndingDirectorySeparator(_directory) == Path.GetPathRoot(_directory))
            throw new ArgumentException("A capture folder cannot be a filesystem root.", nameof(directory));
    }

    internal static bool IsTextNote(string path) => Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase)
        && Path.GetFullPath(path).StartsWith(Path.GetFullPath(DefaultDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    internal static bool IsCapture(string path) => IsCapturePath(path, DefaultDirectory);

    internal static bool IsCapturePath(string path, string directory)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (!string.Equals(Path.GetDirectoryName(fullPath), Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)),
                StringComparison.OrdinalIgnoreCase)) return false;

            string name = Path.GetFileNameWithoutExtension(fullPath);
            string extension = Path.GetExtension(fullPath);
            if (name.StartsWith("Text ", StringComparison.Ordinal) && extension == ".txt") name = name[5..];
            else if (name.StartsWith("Screenshot ", StringComparison.Ordinal) && extension == ".png") name = name[11..];
            else return false;

            int separator = name.LastIndexOf(' ');
            return separator > 0 && name.Length - separator == 17
                && name[(separator + 1)..].All(Uri.IsHexDigit)
                && DateTime.TryParseExact(name.AsSpan(0, separator), CaptureTimestamp, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return false; }
    }

    static bool HasPlainDirectories(string path)
    {
        for (DirectoryInfo? directory = new(path); directory != null; directory = directory.Parent)
        {
            try
            {
                if ((File.GetAttributes(directory.FullName) & (FileAttributes.ReparsePoint | FileAttributes.Directory))
                    != FileAttributes.Directory) return false;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return true;
    }

    static bool IsPlainFile(string path) => (File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == 0;

    internal static void Delete(string path)
    {
        try
        {
            if (IsCapture(path) && HasPlainDirectories(DefaultDirectory) && File.Exists(path) && IsPlainFile(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Log(ex);
        }
    }

    internal Task<string?> SaveTextAsync(string text, CancellationToken token = default)
        => string.IsNullOrWhiteSpace(text) || text.Length > MaxTextChars || text.Contains('\0')
            ? Task.FromResult<string?>(null)
            : Task.Run(() => SaveAsync(Encoding.UTF8.GetBytes(text), "Text", ".txt", token), token);

    internal Task<string?> SaveImageAsync(BitmapSource image, CancellationToken token = default)
    {
        if (image.PixelWidth <= 0 || image.PixelHeight <= 0 || (long)image.PixelWidth * image.PixelHeight > MaxImagePixels)
            return Task.FromResult<string?>(null);
        if (!image.IsFrozen) image = (BitmapSource)image.GetAsFrozen();
        BitmapSource frozen = image;
        return Task.Run(async () =>
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frozen));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            if (stream.Length > 64 * 1024 * 1024) return null;
            return await SaveAsync(stream.ToArray(), "Screenshot", ".png", token).ConfigureAwait(false);
        }, token);
    }

    async Task<string?> SaveAsync(byte[] bytes, string name, string extension, CancellationToken token)
    {
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string tag = hash[..16];
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            // The content hash is in the file name, so a repeat copy is recognised even after a restart.
            // Edited notes are user data: an edited file has a different hash, so it is never reused or overwritten.
            if (!HasPlainDirectories(_directory)) return null;
            if (Directory.Exists(_directory))
                foreach (string candidate in Directory.EnumerateFiles(_directory, "* " + tag + extension))
                {
                    if (!IsCapturePath(candidate, _directory) || !IsPlainFile(candidate)) continue;
                    await using var existing = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                    if (existing.Length == bytes.Length
                        && Convert.ToHexString(await SHA256.HashDataAsync(existing, token).ConfigureAwait(false)) == hash)
                        return candidate;
                }
            Directory.CreateDirectory(_directory);
            if (!HasPlainDirectories(_directory)) return null;
            string stamp = DateTime.Now.ToString(CaptureTimestamp, CultureInfo.InvariantCulture);
            string file = Path.Combine(_directory, $"{name} {stamp} {tag}{extension}");
            bool created = false;
            try
            {
                await using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
                created = true;
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
            }
            catch
            {
                if (created && HasPlainDirectories(_directory) && File.Exists(file) && IsPlainFile(file)) File.Delete(file);
                throw;
            }
            return file;
        }
        finally { _gate.Release(); }
    }
}
