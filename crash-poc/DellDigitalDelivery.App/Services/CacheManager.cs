namespace DellDigitalDelivery.App.Services;

/// <summary>
/// Manages cached content metadata for download resumption.
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
    /// </summary>
    public byte[] ReadPage(long offset, int pageSize = 4096)
    {
        string cachePath = Path.Combine(_cacheDir, "content_cache.dat");

        // Check if the file exists before attempting to open it
        if (!File.Exists(cachePath))
        {
            Console.WriteLine($"[CacheManager] Cache file not found: {cachePath}");
            return Array.Empty<byte>();
        }

        using var fs = new FileStream(cachePath, FileMode.Open, FileAccess.Read);

        // Check if the offset is within the file length
        if (offset >= fs.Length)
        {
            Console.WriteLine($"[CacheManager] Offset {offset} exceeds file length {fs.Length}");
            return Array.Empty<byte>();
        }

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