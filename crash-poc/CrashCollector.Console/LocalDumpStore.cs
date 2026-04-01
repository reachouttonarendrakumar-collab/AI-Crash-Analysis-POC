using System.Security.Cryptography;
using System.Text.Json;
using CrashCollector.Console.Models;

namespace CrashCollector.Console;

/// <summary>
/// Persists crash dump .cab files and metadata JSON to the local filesystem.
///
/// Layout:
///   ./data/dumps/{CrashId}/
///       dump.cab          – the raw cab/minidump
///       metadata.json     – serialised <see cref="CrashReport"/> + hash info
///
/// Idempotent: if a dump directory already contains a valid file whose SHA-256
/// matches the recorded hash, the download is skipped entirely.
/// </summary>
public sealed class LocalDumpStore : ILocalDumpStore
{
    private const string DumpFileName = "dump.cab";
    private const string MetadataFileName = "metadata.json";
    private const long MaxDumpSizeBytes = 500 * 1024 * 1024; // 500 MB safety cap
    private const string MockDumpHost = "wer.microsoft.com"; // mock URLs from WerApiClient fallback

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _rootDir;

    /// <param name="rootDir">
    /// Base directory for dump storage.  Defaults to <c>./data/dumps</c>.
    /// </param>
    public LocalDumpStore(string? rootDir = null)
    {
        _rootDir = rootDir ?? Path.Combine(".", "data", "dumps");
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    /// <inheritdoc />
    public bool Exists(string crashId)
    {
        var dir = CrashDir(crashId);
        var dumpPath = Path.Combine(dir, DumpFileName);
        var metaPath = Path.Combine(dir, MetadataFileName);

        if (!File.Exists(dumpPath) || !File.Exists(metaPath))
            return false;

        // Validate hash recorded in metadata still matches the file on disk
        try
        {
            var meta = ReadStoredMetadata(metaPath);
            if (meta?.Sha256 is null) return false;

            var actualHash = ComputeSha256(dumpPath);
            return string.Equals(meta.Sha256, actualHash, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<DumpStoreResult> StoreDumpAsync(
        CrashReport report,
        HttpClient http,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(http);

        var crashId = report.CrashId;
        var dir = CrashDir(crashId);
        var dumpPath = Path.Combine(dir, DumpFileName);
        var metaPath = Path.Combine(dir, MetadataFileName);

        // ── Idempotent check ────────────────────────────────────────────────
        if (Exists(crashId))
        {
            var existing = ReadStoredMetadata(metaPath)!;
            return new DumpStoreResult
            {
                CrashId = crashId,
                AlreadyExisted = true,
                Downloaded = false,
                HashValid = true,
                FileSizeBytes = new FileInfo(dumpPath).Length,
                Sha256 = existing.Sha256,
                DumpPath = Path.GetFullPath(dumpPath),
                MetadataPath = Path.GetFullPath(metaPath)
            };
        }

        // ── No dump URL → metadata-only record ─────────────────────────────
        if (string.IsNullOrWhiteSpace(report.DumpDownloadUrl))
        {
            Directory.CreateDirectory(dir);
            var metaOnly = BuildMetadata(report, sha256: null, fileSizeBytes: 0);
            await WriteMetadataAsync(metaPath, metaOnly, ct).ConfigureAwait(false);

            return new DumpStoreResult
            {
                CrashId = crashId,
                AlreadyExisted = false,
                Downloaded = false,
                HashValid = true,
                FileSizeBytes = 0,
                Sha256 = null,
                DumpPath = null,
                MetadataPath = Path.GetFullPath(metaPath)
            };
        }

        // ── Download ────────────────────────────────────────────────────────
        try
        {
            Directory.CreateDirectory(dir);

            var tempPath = dumpPath + ".tmp";
            long size;

            var isMockUrl = IsMockUrl(report.DumpDownloadUrl);

            if (isMockUrl)
            {
                // Generate a synthetic minidump-like file for POC/mock mode
                size = await WriteMockCabAsync(tempPath, report.CrashId, ct).ConfigureAwait(false);
            }
            else
            {
                // Stream to a temp file first so a partial download never poisons
                // the store (atomic rename at the end).
                using (var response = await http.GetAsync(report.DumpDownloadUrl,
                           HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    await using var dest = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
                        FileShare.None, bufferSize: 81920, useAsync: true);

                    var buffer = new byte[81920];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        totalRead += bytesRead;
                        if (totalRead > MaxDumpSizeBytes)
                        {
                            dest.Close();
                            TryDelete(tempPath);
                            return new DumpStoreResult
                            {
                                CrashId = crashId,
                                Error = $"Download exceeded max allowed size ({MaxDumpSizeBytes / (1024 * 1024)} MB)"
                            };
                        }

                        await dest.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                    }

                    size = totalRead;
                }
            }

            // ── Validate size ───────────────────────────────────────────────
            if (size == 0)
            {
                TryDelete(tempPath);
                return new DumpStoreResult
                {
                    CrashId = crashId,
                    Error = "Downloaded file is empty (0 bytes)"
                };
            }

            // ── Compute hash ────────────────────────────────────────────────
            var sha256 = ComputeSha256(tempPath);

            // ── Atomic move temp → final ────────────────────────────────────
            File.Move(tempPath, dumpPath, overwrite: true);

            // ── Write metadata ──────────────────────────────────────────────
            var meta = BuildMetadata(report, sha256, size);
            await WriteMetadataAsync(metaPath, meta, ct).ConfigureAwait(false);

            return new DumpStoreResult
            {
                CrashId = crashId,
                AlreadyExisted = false,
                Downloaded = true,
                HashValid = true,
                FileSizeBytes = size,
                Sha256 = sha256,
                DumpPath = Path.GetFullPath(dumpPath),
                MetadataPath = Path.GetFullPath(metaPath)
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DumpStoreResult
            {
                CrashId = crashId,
                Error = $"{ex.GetType().Name}: {ex.Message}"
            };
        }
    }

    // =========================================================================
    //  Helpers
    // =========================================================================

    private string CrashDir(string crashId) => Path.Combine(_rootDir, SanitiseFileName(crashId));

    private static string SanitiseFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c));
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private static DumpMetadata BuildMetadata(CrashReport report, string? sha256, long fileSizeBytes) => new()
    {
        CrashId = report.CrashId,
        AppName = report.AppName,
        AppVersion = report.AppVersion,
        Timestamp = report.Timestamp,
        FailureBucket = report.FailureBucket,
        DumpDownloadUrl = report.DumpDownloadUrl,
        Sha256 = sha256,
        FileSizeBytes = fileSizeBytes,
        StoredAtUtc = DateTimeOffset.UtcNow
    };

    private static async Task WriteMetadataAsync(string path, DumpMetadata meta, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(fs, meta, JsonOpts, ct).ConfigureAwait(false);
    }

    private static DumpMetadata? ReadStoredMetadata(string path)
    {
        using var fs = File.OpenRead(path);
        return JsonSerializer.Deserialize<DumpMetadata>(fs, JsonOpts);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best-effort cleanup */ }
    }

    private static bool IsMockUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && uri.Host.Equals(MockDumpHost, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Writes a synthetic .cab-shaped file for POC/mock mode so the full
    /// hash + metadata pipeline can be exercised without network access.
    /// The file starts with the real MSCF cabinet signature (0x4D534346)
    /// followed by random payload bytes to give each crash a unique hash.
    /// </summary>
    private static async Task<long> WriteMockCabAsync(string path, string crashId, CancellationToken ct)
    {
        // MSCF magic header (real cab signature) + 4-byte reserved
        var header = new byte[] { 0x4D, 0x53, 0x43, 0x46, 0x00, 0x00, 0x00, 0x00 };

        // Deterministic-ish payload seeded from crashId so re-runs produce
        // the same hash for the same crash (aids idempotency testing).
        var seed = crashId.GetHashCode();
        var rng = new Random(seed);
        var payloadSize = 1024 + rng.Next(4096); // 1–5 KB
        var payload = new byte[payloadSize];
        rng.NextBytes(payload);

        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: 4096, useAsync: true);
        await fs.WriteAsync(header, ct).ConfigureAwait(false);
        await fs.WriteAsync(payload, ct).ConfigureAwait(false);

        return header.Length + payload.Length;
    }

    // =========================================================================
    //  Metadata model (what gets written to metadata.json)
    // =========================================================================

    private sealed class DumpMetadata
    {
        public string CrashId { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;
        public string AppVersion { get; set; } = string.Empty;
        public DateTimeOffset Timestamp { get; set; }
        public string? FailureBucket { get; set; }
        public string? DumpDownloadUrl { get; set; }
        public string? Sha256 { get; set; }
        public long FileSizeBytes { get; set; }
        public DateTimeOffset StoredAtUtc { get; set; }
    }
}
