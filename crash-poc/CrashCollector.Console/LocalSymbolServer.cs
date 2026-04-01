using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace CrashCollector.Console;

/// <summary>
/// Local SymSrv-compatible symbol server for POC use.
///
/// Layout (matches Microsoft symbol server convention):
///   ./data/symbols/{pdbName}/{GUID-AGE}/{pdbName}
///
/// Example:
///   ./data/symbols/Dell.D3.Core.pdb/AABBCCDD11223344EEFF00112233AABB1/Dell.D3.Core.pdb
///
/// Sources PDB files from the D3 build output tree and publishes them into
/// the local symbol store. Idempotent – skips files already present.
///
/// Supports:
///   - Portable PDB (BSJB signature) – reads GUID from #Pdb metadata stream
///   - Windows PDB (Microsoft C/C++ MSF 7.00) – reads GUID from PDB7 header
/// </summary>
public sealed class LocalSymbolServer
{
    private const string ManifestFileName = "symbols-manifest.json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _symbolRoot;

    /// <param name="symbolRoot">Root directory for the symbol store. Default: <c>./data/symbols</c></param>
    public LocalSymbolServer(string? symbolRoot = null)
    {
        _symbolRoot = symbolRoot ?? Path.Combine(".", "data", "symbols");
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    /// <summary>
    /// Scans <paramref name="buildOutputRoot"/> for <c>*.pdb</c> files under
    /// <c>*\bin\*\</c> directories and publishes them into the local symbol store.
    /// </summary>
    /// <param name="buildOutputRoot">
    /// Root of the source tree, e.g.
    /// <c>C:\Users\...\source\repos\D3\d3-client-nga\src</c>
    /// </param>
    /// <returns>Summary of what was published.</returns>
    public async Task<SymbolPublishResult> PublishSymbolsFromBuildOutputAsync(
        string buildOutputRoot,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(buildOutputRoot))
        {
            return new SymbolPublishResult
            {
                Error = $"Build output root does not exist: {buildOutputRoot}"
            };
        }

        // Discover all .pdb files under *\bin\*
        var pdbFiles = Directory.EnumerateFiles(buildOutputRoot, "*.pdb", SearchOption.AllDirectories)
            .Where(p =>
            {
                var rel = Path.GetRelativePath(buildOutputRoot, p);
                // Only pick up bin/ output (not obj/)
                return rel.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        var result = new SymbolPublishResult { TotalPdbsFound = pdbFiles.Count };
        var entries = new List<SymbolEntry>();

        foreach (var pdbPath in pdbFiles)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var info = ReadPdbIdentity(pdbPath);
                if (info is null)
                {
                    result.Skipped++;
                    result.Warnings.Add($"Could not read identity: {Path.GetFileName(pdbPath)}");
                    continue;
                }

                var pdbName = Path.GetFileName(pdbPath);
                var guidAge = $"{info.Value.Guid:N}{info.Value.Age}".ToUpperInvariant();
                var destDir = Path.Combine(_symbolRoot, pdbName, guidAge);
                var destPath = Path.Combine(destDir, pdbName);

                if (File.Exists(destPath))
                {
                    result.AlreadyPresent++;
                    entries.Add(new SymbolEntry
                    {
                        PdbName = pdbName,
                        GuidAge = guidAge,
                        SourcePath = pdbPath,
                        StorePath = Path.GetFullPath(destPath),
                        AlreadyExisted = true
                    });
                    continue;
                }

                Directory.CreateDirectory(destDir);
                File.Copy(pdbPath, destPath, overwrite: false);

                result.Published++;
                entries.Add(new SymbolEntry
                {
                    PdbName = pdbName,
                    GuidAge = guidAge,
                    SourcePath = pdbPath,
                    StorePath = Path.GetFullPath(destPath),
                    FileSizeBytes = new FileInfo(destPath).Length
                });
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.Warnings.Add($"{Path.GetFileName(pdbPath)}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        result.Entries = entries;

        // Persist manifest
        await SaveManifestAsync(entries, ct).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Resolves a PDB from the local symbol store by name and GUID+Age.
    /// Returns the full path if found, null otherwise.
    /// </summary>
    public string? Resolve(string pdbName, string guidAge)
    {
        var path = Path.Combine(_symbolRoot, pdbName, guidAge.ToUpperInvariant(), pdbName);
        return File.Exists(path) ? Path.GetFullPath(path) : null;
    }

    /// <summary>
    /// Returns the SymSrv-style symbol path string for use with WinDbg / cdb:
    ///   srv*{localSymbolRoot}
    /// </summary>
    public string GetSymbolPath() => $"srv*{Path.GetFullPath(_symbolRoot)}";

    // =========================================================================
    //  PDB identity reading
    // =========================================================================

    /// <summary>
    /// Reads the GUID + Age from a PDB file. Supports both Portable PDB (BSJB)
    /// and classic Windows PDB7 (Microsoft C/C++ MSF 7.00) formats.
    /// </summary>
    private static (Guid Guid, int Age)? ReadPdbIdentity(string pdbPath)
    {
        using var fs = File.OpenRead(pdbPath);

        // Read first 4 bytes to detect format
        var sigBuf = new byte[4];
        if (fs.Read(sigBuf, 0, 4) < 4) return null;
        fs.Seek(0, SeekOrigin.Begin);

        var sig = System.Text.Encoding.ASCII.GetString(sigBuf);

        if (sig == "BSJB")
            return ReadPortablePdbIdentity(fs);

        if (sig == "Micr")
            return ReadClassicPdbIdentity(fs);

        return null;
    }

    /// <summary>
    /// Reads GUID + Age from a Portable PDB using System.Reflection.Metadata.
    /// The PDB ID is stored in the #Pdb metadata stream header.
    /// </summary>
    private static (Guid Guid, int Age)? ReadPortablePdbIdentity(Stream stream)
    {
        try
        {
            using var provider = MetadataReaderProvider.FromPortablePdbStream(stream,
                MetadataStreamOptions.LeaveOpen);
            var reader = provider.GetMetadataReader();
            var id = reader.DebugMetadataHeader?.Id;

            if (id is null || id.Value.IsEmpty) return null;

            // The PDB ID blob is 20 bytes: 16-byte GUID + 4-byte stamp.
            // For SymSrv, Age = 1 for portable PDBs (convention).
            var idBytes = id.Value.ToArray();
            if (idBytes.Length < 20) return null;

            var guid = new Guid(idBytes.AsSpan(0, 16));
            // Portable PDBs use age=1 by convention in SymSrv
            return (guid, 1);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads GUID + Age from a classic Windows PDB7 file (MSF format).
    /// The PDB7 signature line: "Microsoft C/C++ MSF 7.00\r\n\x1ADS\0\0\0"
    /// GUID is at a known offset in the PDB header stream.
    /// </summary>
    private static (Guid Guid, int Age)? ReadClassicPdbIdentity(Stream stream)
    {
        try
        {
            using var br = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

            // MSF 7.0 superblock: 32-byte signature + page size (4) + ...
            var sigBytes = br.ReadBytes(32);
            var sigStr = System.Text.Encoding.ASCII.GetString(sigBytes).TrimEnd('\0', '\r', '\n', '\x1a');
            if (!sigStr.StartsWith("Microsoft C/C++ MSF 7.00"))
                return null;

            var pageSize = br.ReadInt32();
            if (pageSize <= 0 || pageSize > 0x10000) return null;

            // Skip FreePageMap(4) + PagesInUse(4) + DirectoryByteSize(4) + Unknown(4)
            _ = br.ReadInt32(); // FreePageMap
            _ = br.ReadInt32(); // PagesInUse
            var directoryByteSize = br.ReadInt32();
            _ = br.ReadInt32(); // unknown

            // Directory page map pointer is at offset 52 (page number)
            var directoryMapPage = br.ReadInt32();

            // Read the directory map page to get the first page of the stream directory
            stream.Seek((long)directoryMapPage * pageSize, SeekOrigin.Begin);
            var directoryPage = br.ReadInt32();

            // Read the stream directory
            stream.Seek((long)directoryPage * pageSize, SeekOrigin.Begin);
            var numStreams = br.ReadInt32();

            if (numStreams < 2) return null;

            // Read stream sizes
            var streamSizes = new int[numStreams];
            for (int i = 0; i < numStreams; i++)
                streamSizes[i] = br.ReadInt32();

            // Stream 1 = PDB info stream. Read its page numbers.
            // First skip stream 0's pages
            var stream0Pages = (streamSizes[0] + pageSize - 1) / pageSize;
            for (int i = 0; i < stream0Pages; i++)
                _ = br.ReadInt32();

            // Stream 1 pages
            var stream1Pages = (streamSizes[1] + pageSize - 1) / pageSize;
            if (stream1Pages < 1) return null;

            var stream1FirstPage = br.ReadInt32();

            // Read PDB info stream header:
            //   Version(4) + Signature(4) + Age(4) + GUID(16)
            stream.Seek((long)stream1FirstPage * pageSize, SeekOrigin.Begin);
            _ = br.ReadInt32(); // version
            _ = br.ReadInt32(); // signature (timestamp)
            var age = br.ReadInt32();
            var guidBytes = br.ReadBytes(16);
            var guid = new Guid(guidBytes);

            return (guid, age);
        }
        catch
        {
            return null;
        }
    }

    // =========================================================================
    //  Manifest persistence
    // =========================================================================

    private async Task SaveManifestAsync(List<SymbolEntry> entries, CancellationToken ct)
    {
        Directory.CreateDirectory(_symbolRoot);
        var manifestPath = Path.Combine(_symbolRoot, ManifestFileName);

        // Merge with existing manifest
        var existing = new List<SymbolEntry>();
        if (File.Exists(manifestPath))
        {
            try
            {
                await using var rfs = new FileStream(manifestPath, FileMode.Open,
                    FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                existing = await JsonSerializer.DeserializeAsync<List<SymbolEntry>>(rfs, JsonOpts, ct)
                    .ConfigureAwait(false) ?? new();
            }
            catch { /* start fresh */ }
        }

        // Merge: key by PdbName + GuidAge
        var merged = existing
            .Concat(entries)
            .GroupBy(e => $"{e.PdbName}|{e.GuidAge}")
            .Select(g => g.Last())
            .OrderBy(e => e.PdbName)
            .ThenBy(e => e.GuidAge)
            .ToList();

        await using var wfs = new FileStream(manifestPath, FileMode.Create,
            FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(wfs, merged, JsonOpts, ct).ConfigureAwait(false);
    }
}

// =========================================================================
//  Result / entry models
// =========================================================================

/// <summary>Summary of a <see cref="LocalSymbolServer.PublishSymbolsFromBuildOutputAsync"/> run.</summary>
public sealed class SymbolPublishResult
{
    public int TotalPdbsFound { get; set; }
    public int Published { get; set; }
    public int AlreadyPresent { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public string? Error { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<SymbolEntry> Entries { get; set; } = new();
}

/// <summary>A single PDB entry in the symbol store.</summary>
public sealed class SymbolEntry
{
    public string PdbName { get; set; } = string.Empty;
    public string GuidAge { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string StorePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public bool AlreadyExisted { get; set; }
}
