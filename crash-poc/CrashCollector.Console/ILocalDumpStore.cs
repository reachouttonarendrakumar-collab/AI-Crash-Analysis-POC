using CrashCollector.Console.Models;

namespace CrashCollector.Console;

/// <summary>
/// Persists crash dump files and metadata to the local filesystem.
/// </summary>
public interface ILocalDumpStore
{
    /// <summary>
    /// Returns true if a validated dump already exists for the given crash.
    /// </summary>
    bool Exists(string crashId);

    /// <summary>
    /// Downloads the dump from <paramref name="downloadUrl"/>, validates its
    /// size and SHA-256 hash, writes it alongside a metadata JSON file, and
    /// returns the result.  No-ops if the dump is already present and valid.
    /// </summary>
    Task<DumpStoreResult> StoreDumpAsync(
        CrashReport report,
        HttpClient http,
        CancellationToken ct = default);
}

/// <summary>
/// Outcome of a single <see cref="ILocalDumpStore.StoreDumpAsync"/> call.
/// </summary>
public sealed class DumpStoreResult
{
    public string CrashId { get; init; } = string.Empty;
    public bool AlreadyExisted { get; init; }
    public bool Downloaded { get; init; }
    public bool HashValid { get; init; }
    public long FileSizeBytes { get; init; }
    public string? Sha256 { get; init; }
    public string? DumpPath { get; init; }
    public string? MetadataPath { get; init; }
    public string? Error { get; init; }
}
