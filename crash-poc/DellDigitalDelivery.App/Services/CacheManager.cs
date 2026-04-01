namespace DellDigitalDelivery.App.Services;

/// <summary>
/// Manages cached content metadata for download resumption.
/// BUG: ReadPage does not validate the file path before opening,
/// causing an I/O error (0xC0000006) when the cache file doesn't exist.
/// </summary>
public class CacheManager
{
    private readonly string _cacheDir;

    public CacheManager(string cacheDir = "cache")
    {
        _cacheDir = cacheDir;
    }

    /// <summary>
    /// Reads a page of cached data from disk at the given offset.
    /// BUG: Does not check if file exists before opening, and does not
    /// handle the case where offset exceeds file length.
    /// </summary>
    public byte[] ReadPage(long offset, int pageSize = 4096)
    {
        // BUG: Hardcoded path that doesn't exist — causes FileNotFoundException
        string cachePath = Path.Combine(_cacheDir, "content_cache.dat");

        // BUG: No existence check — will throw IOException
        using var fs = new FileStream(cachePath, FileMode.Open, FileAccess.Read);

        // BUG: No bounds check — offset may exceed file length
        fs.Seek(offset, SeekOrigin.Begin);

        byte[] buffer = new byte[pageSize];
        int bytesRead = fs.Read(buffer, 0, pageSize);

        if (bytesRead < pageSize)
        {
            Array.Resize(ref buffer, bytesRead);
        }

        return buffer;
    }

    /// <summary>
    /// Gets cached metadata for a content key.
    /// </summary>
    public string? GetCachedMetadata(string key)
    {
        Console.WriteLine($"[CacheManager] Looking up cached metadata for: {key}");
        var data = ReadPage(0);
        return data.Length > 0 ? System.Text.Encoding.UTF8.GetString(data) : null;
    }

    /// <summary>
    /// Resumes a download using cached data.
    /// </summary>
    public void ResumeDownload(string contentId)
    {
        Console.WriteLine($"[CacheManager] Resuming download for: {contentId}");
        var metadata = GetCachedMetadata(contentId);
        Console.WriteLine($"[CacheManager] Cache status: {(metadata != null ? "HIT" : "MISS")}");
    }
}
