using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CrashCollector.Console.Models;

namespace CrashCollector.Console;

/// <summary>
/// Groups crash dumps into stable buckets based on normalised stack traces.
///
/// Algorithm:
///   1. Take the raw stack frames (from WinDbg kn, .NET StackTrace, or mock).
///   2. Normalise each frame: strip addresses, line numbers, IL offsets,
///      generic arity, and whitespace to get "Module!Function" form.
///   3. Filter out system / framework frames (ntdll, kernelbase, coreclr, etc.).
///   4. Take the top N non-system frames (default 3).
///   5. Combine with the exception code and SHA-256 hash into a stable BucketId.
///
/// Persistence:
///   Writes a <c>buckets.json</c> manifest mapping BucketId → list of CrashIds.
/// </summary>
public sealed partial class Bucketizer
{
    private const int DefaultKeyFrameCount = 3;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Prefixes considered "system" frames that should be skipped when
    // selecting key frames. Case-insensitive comparison.
    private static readonly string[] SystemPrefixes =
    {
        "ntdll",
        "ntoskrnl",
        "kernelbase",
        "kernel32",
        "win32u",
        "user32",
        "combase",
        "rpcrt4",
        "msvcrt",
        "ucrtbase",
        "coreclr",
        "clrjit",
        "system.private.corelib",
        "hostpolicy",
        "hostfxr",
        "mscorlib",
        "system.runtime",
        "system.threading",
        "system.collections",
    };

    private readonly string _bucketManifestPath;
    private readonly int _keyFrameCount;

    /// <param name="dataDir">Root data directory (default: <c>./data</c>).</param>
    /// <param name="keyFrameCount">Number of top non-system frames to use for the bucket hash.</param>
    public Bucketizer(string? dataDir = null, int keyFrameCount = DefaultKeyFrameCount)
    {
        var dir = dataDir ?? Path.Combine(".", "data");
        _bucketManifestPath = Path.Combine(dir, "buckets.json");
        _keyFrameCount = keyFrameCount;
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    /// <summary>
    /// Computes a <see cref="StackSignature"/> for a single crash.
    /// </summary>
    public StackSignature ComputeSignature(CrashMetadata metadata, IReadOnlyList<string> rawFrames)
    {
        var normalised = rawFrames
            .Select(NormalizeFrame)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToList();

        var keyFrames = normalised
            .Where(f => !IsSystemFrame(f))
            .Take(_keyFrameCount)
            .ToList();

        // If we don't have enough non-system frames, pad with whatever we have
        if (keyFrames.Count < _keyFrameCount)
        {
            var extras = normalised
                .Where(f => !keyFrames.Contains(f))
                .Take(_keyFrameCount - keyFrames.Count);
            keyFrames.AddRange(extras);
        }

        var bucketId = ComputeBucketId(metadata.ExceptionCode, keyFrames);

        return new StackSignature
        {
            BucketId = bucketId,
            ExceptionCode = metadata.ExceptionCode,
            KeyFrames = keyFrames.AsReadOnly(),
            NormalizedFrames = normalised.AsReadOnly(),
            RawFrameCount = rawFrames.Count
        };
    }

    /// <summary>
    /// Bucketises a batch of crashes and persists the BucketId → CrashIds manifest.
    /// Returns the mapping.
    /// </summary>
    public async Task<Dictionary<string, BucketInfo>> BucketiseAndPersistAsync(
        IReadOnlyList<(CrashMetadata Metadata, IReadOnlyList<string> RawFrames)> items,
        CancellationToken ct = default)
    {
        // Load existing manifest (additive — never lose prior data)
        var manifest = await LoadManifestAsync(ct).ConfigureAwait(false);

        foreach (var (metadata, rawFrames) in items)
        {
            var sig = ComputeSignature(metadata, rawFrames);

            if (!manifest.TryGetValue(sig.BucketId, out var bucket))
            {
                bucket = new BucketInfo
                {
                    BucketId = sig.BucketId,
                    ExceptionCode = sig.ExceptionCode,
                    KeyFrames = sig.KeyFrames.ToList()
                };
                manifest[sig.BucketId] = bucket;
            }

            // Deduplicate crash IDs within the bucket
            if (!bucket.CrashIds.Contains(metadata.CrashId))
                bucket.CrashIds.Add(metadata.CrashId);
        }

        await SaveManifestAsync(manifest, ct).ConfigureAwait(false);
        return manifest;
    }

    // =========================================================================
    //  Frame normalisation
    // =========================================================================

    /// <summary>
    /// Normalises a single stack frame string into "Module!Function" form.
    /// Strips addresses, offsets, line numbers, generic arity, parameters.
    /// </summary>
    internal static string NormalizeFrame(string raw)
    {
        var s = raw.Trim();

        // Strip leading frame number + address (WinDbg "kn" format):
        //   0a 0000002b`6c8fe4a0 coreclr!CallDescrWorkerInternal+0x83
        s = LeadingFrameNumberRegex().Replace(s, "");

        // Strip "at " prefix (.NET StackTrace format):
        //   at Dell.Digital.Delivery.Service.SubAgent.Program.Main(String[] args) in C:\...\Program.cs:line 42
        s = AtPrefixRegex().Replace(s, "");

        // Strip file path + line info:  " in C:\foo\bar.cs:line 42"
        s = FileLineRegex().Replace(s, "");

        // Strip IL offset: " [il 0x00a4]"
        s = IlOffsetRegex().Replace(s, "");

        // Strip hex offset after +: "Module!Func+0x1a3" → "Module!Func"
        s = HexOffsetRegex().Replace(s, "");

        // Strip generic arity: "Method`1" → "Method", "Type`2" → "Type"
        s = GenericArityRegex().Replace(s, "");

        // Strip parameter list: "Method(String, Int32)" → "Method"
        s = ParameterListRegex().Replace(s, "");

        // Collapse whitespace
        s = MultiSpaceRegex().Replace(s.Trim(), " ");

        // For .NET "Namespace.Type.Method" without a "!", prefix with module placeholder
        if (!s.Contains('!') && s.Contains('.'))
        {
            var lastDot = s.LastIndexOf('.');
            if (lastDot > 0)
            {
                var module = s[..lastDot];
                var func = s[(lastDot + 1)..];
                s = $"{module}!{func}";
            }
        }

        return s;
    }

    private static bool IsSystemFrame(string normalised)
    {
        var lower = normalised.ToLowerInvariant();
        return SystemPrefixes.Any(p => lower.StartsWith(p, StringComparison.Ordinal));
    }

    // ── Regex patterns (source-generated) ───────────────────────────────────

    [GeneratedRegex(@"^\s*[0-9a-f]+\s+[0-9a-f`]+\s+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingFrameNumberRegex();

    [GeneratedRegex(@"^\s*at\s+", RegexOptions.IgnoreCase)]
    private static partial Regex AtPrefixRegex();

    [GeneratedRegex(@"\s+in\s+\S+:\s*line\s+\d+", RegexOptions.IgnoreCase)]
    private static partial Regex FileLineRegex();

    [GeneratedRegex(@"\s*\[il\s+0x[0-9a-f]+\]", RegexOptions.IgnoreCase)]
    private static partial Regex IlOffsetRegex();

    [GeneratedRegex(@"\+0x[0-9a-f]+", RegexOptions.IgnoreCase)]
    private static partial Regex HexOffsetRegex();

    [GeneratedRegex(@"`\d+")]
    private static partial Regex GenericArityRegex();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex ParameterListRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpaceRegex();

    // =========================================================================
    //  Hashing
    // =========================================================================

    private static string ComputeBucketId(string? exceptionCode, IReadOnlyList<string> keyFrames)
    {
        // Build a deterministic string: "ExceptionCode|Frame1|Frame2|Frame3"
        var sb = new StringBuilder();
        sb.Append(exceptionCode?.ToUpperInvariant() ?? "UNKNOWN");
        foreach (var f in keyFrames)
        {
            sb.Append('|');
            sb.Append(f.ToUpperInvariant());
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        // Use first 16 bytes (32 hex chars) for a compact but collision-resistant ID
        return Convert.ToHexString(hash)[..32];
    }

    // =========================================================================
    //  Persistence
    // =========================================================================

    private async Task<Dictionary<string, BucketInfo>> LoadManifestAsync(CancellationToken ct)
    {
        if (!File.Exists(_bucketManifestPath))
            return new Dictionary<string, BucketInfo>(StringComparer.OrdinalIgnoreCase);

        try
        {
            await using var fs = new FileStream(_bucketManifestPath, FileMode.Open,
                FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
            var list = await JsonSerializer.DeserializeAsync<List<BucketInfo>>(fs, JsonOpts, ct)
                .ConfigureAwait(false);

            return list?.ToDictionary(b => b.BucketId, b => b, StringComparer.OrdinalIgnoreCase)
                   ?? new Dictionary<string, BucketInfo>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, BucketInfo>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task SaveManifestAsync(Dictionary<string, BucketInfo> manifest, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_bucketManifestPath);
        if (dir is not null) Directory.CreateDirectory(dir);

        var list = manifest.Values
            .OrderByDescending(b => b.CrashIds.Count)
            .ToList();

        await using var fs = new FileStream(_bucketManifestPath, FileMode.Create,
            FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(fs, list, JsonOpts, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// A single bucket in the manifest: maps a BucketId to its member crashes.
/// </summary>
public sealed class BucketInfo
{
    public string BucketId { get; set; } = string.Empty;
    public string? ExceptionCode { get; set; }
    public List<string> KeyFrames { get; set; } = new();
    public List<string> CrashIds { get; set; } = new();

    public override string ToString() =>
        $"[{BucketId[..12]}…] {ExceptionCode ?? "?"} | {CrashIds.Count} crash(es) | keys=[{string.Join(" → ", KeyFrames)}]";
}
