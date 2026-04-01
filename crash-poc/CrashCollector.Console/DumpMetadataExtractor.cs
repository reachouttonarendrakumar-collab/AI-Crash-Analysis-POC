using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using CrashCollector.Console.Models;

namespace CrashCollector.Console;

/// <summary>
/// Extracts structured <see cref="CrashMetadata"/> from a crash dump file.
///
/// Strategy (ordered by preference):
///   1. WinDbg / cdb.exe – run <c>!analyze -v</c> and parse output.
///   2. Raw minidump header parsing – read the MDMP binary header, stream
///      directory, exception and thread-list streams directly.
///   3. Synthetic / mock fallback – if the file carries a MSCF (cabinet)
///      signature instead of MDMP, derive metadata from the accompanying
///      <c>metadata.json</c> and report the extraction method accordingly.
/// </summary>
public sealed partial class DumpMetadataExtractor
{
    // ── Minidump binary constants ───────────────────────────────────────────
    private const uint MdmpSignature = 0x504D444D;  // "MDMP"
    private const uint MscfSignature = 0x4643534D;  // "MSCF" (cabinet)

    // Stream types we care about
    private const uint ThreadListStream = 3;
    private const uint ModuleListStream = 4;
    private const uint ExceptionStream  = 6;

    // ── WinDbg discovery ────────────────────────────────────────────────────
    private static readonly Lazy<string?> CdbPath = new(FindCdb);

    /// <summary>
    /// Extracts metadata from the dump file at <paramref name="dumpPath"/>.
    /// </summary>
    public async Task<CrashMetadata> ExtractAsync(
        string crashId,
        string dumpPath,
        CancellationToken ct = default)
    {
        if (!File.Exists(dumpPath))
        {
            return new CrashMetadata
            {
                CrashId = crashId,
                DumpPath = dumpPath,
                Error = "Dump file does not exist"
            };
        }

        // Detect file type by reading first 4 bytes
        var sig = await ReadSignatureAsync(dumpPath, ct).ConfigureAwait(false);

        // MSCF = our mock cab. Derive what we can from the companion metadata.json.
        if (sig == MscfSignature)
            return ExtractFromMockDump(crashId, dumpPath);

        // Real MDMP file – check companion metadata first (DellDigitalDelivery.App dumps),
        // then try WinDbg, then fall back to header parsing
        if (sig == MdmpSignature)
        {
            // Check if there's a rich metadata.json from DellDigitalDelivery.App alongside the dump
            var richMeta = TryLoadRichMetadataForDump(crashId, dumpPath);
            if (richMeta is not null)
                return richMeta;

            if (CdbPath.Value is not null)
            {
                try
                {
                    var result = await ExtractViaWinDbgAsync(crashId, dumpPath, CdbPath.Value, ct)
                        .ConfigureAwait(false);
                    if (result.Error is null) return result;
                }
                catch { /* fall through */ }
            }

            return ExtractFromMinidumpHeader(crashId, dumpPath);
        }

        return new CrashMetadata
        {
            CrashId = crashId,
            DumpPath = dumpPath,
            DumpSignature = $"0x{sig:X8}",
            ExtractionMethod = "Unknown",
            Error = $"Unrecognised dump signature: 0x{sig:X8}"
        };
    }

    // =========================================================================
    //  WinDbg / cdb.exe extraction
    // =========================================================================

    private static async Task<CrashMetadata> ExtractViaWinDbgAsync(
        string crashId, string dumpPath, string cdbExe, CancellationToken ct)
    {
        // Run: cdb -z <dump> -c "!analyze -v; .ecxr; kn; q"
        var psi = new ProcessStartInfo
        {
            FileName = cdbExe,
            Arguments = $"-z \"{dumpPath}\" -c \"!analyze -v; .ecxr; kn; q\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start cdb.exe");

        var output = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);

        if (proc.ExitCode != 0 && string.IsNullOrWhiteSpace(output))
        {
            return new CrashMetadata
            {
                CrashId = crashId,
                DumpPath = dumpPath,
                ExtractionMethod = "WinDbg",
                Error = $"cdb.exe exited with code {proc.ExitCode}"
            };
        }

        return ParseWinDbgOutput(crashId, dumpPath, output);
    }

    private static CrashMetadata ParseWinDbgOutput(string crashId, string dumpPath, string output)
    {
        var meta = new CrashMetadata
        {
            CrashId = crashId,
            DumpPath = dumpPath,
            ExtractionMethod = "WinDbg",
            WinDbgRawOutput = output
        };

        // EXCEPTION_CODE: (NTSTATUS) 0xc0000005
        var excMatch = ExceptionCodeRegex().Match(output);
        if (excMatch.Success)
        {
            meta.ExceptionCode = excMatch.Groups["code"].Value;
            meta.ExceptionName = LookupExceptionName(meta.ExceptionCode);
        }

        // MODULE_NAME: Dell.Digital.Delivery.Service.SubAgent
        var modMatch = ModuleNameRegex().Match(output);
        if (modMatch.Success)
            meta.FaultingModule = modMatch.Groups["name"].Value;

        // IMAGE_NAME as fallback
        if (meta.FaultingModule is null)
        {
            var imgMatch = ImageNameRegex().Match(output);
            if (imgMatch.Success)
                meta.FaultingModule = imgMatch.Groups["name"].Value;
        }

        // Thread count from "kn" output – count lines matching frame pattern
        var threadCountMatch = ThreadCountRegex().Matches(output);
        // Rough: count unique "Thread " headers in the output
        var threadHeaders = Regex.Matches(output, @"^\s*\d+\s+Id:", RegexOptions.Multiline);
        meta.ThreadCount = threadHeaders.Count > 0 ? threadHeaders.Count : 0;

        return meta;
    }

    // ── WinDbg regex patterns ───────────────────────────────────────────────

    [GeneratedRegex(@"EXCEPTION_CODE:\s*\(NTSTATUS\)\s*(?<code>0x[0-9a-fA-F]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ExceptionCodeRegex();

    [GeneratedRegex(@"MODULE_NAME:\s*(?<name>\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex ModuleNameRegex();

    [GeneratedRegex(@"IMAGE_NAME:\s*(?<name>\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex ImageNameRegex();

    [GeneratedRegex(@"^\s*[0-9a-f]+\s+[0-9a-f]+\s+", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex ThreadCountRegex();

    // =========================================================================
    //  Raw minidump header parsing (no WinDbg required)
    //  Reference: https://learn.microsoft.com/en-us/windows/win32/api/minidumpapiset/ns-minidumpapiset-minidump_header
    // =========================================================================

    private static CrashMetadata ExtractFromMinidumpHeader(string crashId, string dumpPath)
    {
        var meta = new CrashMetadata
        {
            CrashId = crashId,
            DumpPath = dumpPath,
            ExtractionMethod = "MinidumpHeader"
        };

        try
        {
            using var fs = File.OpenRead(dumpPath);
            using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);

            // ── MINIDUMP_HEADER (32 bytes) ──────────────────────────────────
            var signature = br.ReadUInt32();     // 0x504D444D "MDMP"
            meta.DumpSignature = $"0x{signature:X8}";

            var version = br.ReadUInt32();       // low 16 = version, high 16 = impl
            meta.DumpVersion = (int)(version & 0xFFFF);

            var numberOfStreams = br.ReadUInt32();
            meta.StreamCount = (int)numberOfStreams;

            var streamDirectoryRva = br.ReadUInt32();
            var checksum = br.ReadUInt32();
            var timeDateStamp = br.ReadUInt32();
            meta.DumpTimestamp = DateTimeOffset.FromUnixTimeSeconds(timeDateStamp);

            // flags (uint64)
            _ = br.ReadUInt64();

            // ── Walk the stream directory ───────────────────────────────────
            // Each MINIDUMP_DIRECTORY entry: StreamType(4) + DataSize(4) + Rva(4) = 12 bytes
            fs.Seek(streamDirectoryRva, SeekOrigin.Begin);

            uint exceptionRva = 0, exceptionSize = 0;
            uint threadListRva = 0, threadListSize = 0;
            uint moduleListRva = 0, moduleListSize = 0;

            for (int i = 0; i < numberOfStreams; i++)
            {
                var streamType = br.ReadUInt32();
                var dataSize = br.ReadUInt32();
                var rva = br.ReadUInt32();

                switch (streamType)
                {
                    case ExceptionStream:
                        exceptionRva = rva; exceptionSize = dataSize;
                        break;
                    case ThreadListStream:
                        threadListRva = rva; threadListSize = dataSize;
                        break;
                    case ModuleListStream:
                        moduleListRva = rva; moduleListSize = dataSize;
                        break;
                }
            }

            // ── Parse exception stream ──────────────────────────────────────
            // MINIDUMP_EXCEPTION_STREAM:
            //   ThreadId (4) + __alignment (4) + MINIDUMP_EXCEPTION
            // MINIDUMP_EXCEPTION:
            //   ExceptionCode (4) + ExceptionFlags (4) + ExceptionRecord (8) +
            //   ExceptionAddress (8) + NumberParameters (4) + ...
            if (exceptionRva > 0 && exceptionSize >= 28)
            {
                fs.Seek(exceptionRva, SeekOrigin.Begin);
                _ = br.ReadUInt32(); // ThreadId
                _ = br.ReadUInt32(); // alignment

                var exCode = br.ReadUInt32();
                meta.ExceptionCode = $"0x{exCode:X8}";
                meta.ExceptionName = LookupExceptionName(meta.ExceptionCode);

                _ = br.ReadUInt32(); // flags
                _ = br.ReadInt64();  // ExceptionRecord
                _ = br.ReadInt64();  // ExceptionAddress
                var numParams = br.ReadUInt32();
                meta.ExceptionParameterCount = (int)numParams;
            }

            // ── Parse thread list stream ────────────────────────────────────
            // MINIDUMP_THREAD_LIST: NumberOfThreads (4) + Threads[...]
            if (threadListRva > 0 && threadListSize >= 4)
            {
                fs.Seek(threadListRva, SeekOrigin.Begin);
                meta.ThreadCount = (int)br.ReadUInt32();
            }

            // ── Parse module list stream (first module = likely faulting) ───
            // MINIDUMP_MODULE_LIST: NumberOfModules(4) + Modules[...]
            // MINIDUMP_MODULE: BaseOfImage(8) + SizeOfImage(4) + CheckSum(4) +
            //   TimeDateStamp(4) + ModuleNameRva(4) + ...
            if (moduleListRva > 0 && moduleListSize >= 4)
            {
                fs.Seek(moduleListRva, SeekOrigin.Begin);
                var numModules = br.ReadUInt32();

                if (numModules > 0)
                {
                    var baseOfImage = br.ReadUInt64();
                    var sizeOfImage = br.ReadUInt32();
                    _ = br.ReadUInt32(); // checksum
                    var modTimestamp = br.ReadUInt32();
                    var moduleNameRva = br.ReadUInt32();

                    meta.FaultingModuleBase = $"0x{baseOfImage:X16}";
                    meta.FaultingModuleSize = sizeOfImage;
                    meta.FaultingModuleTimestamp = DateTimeOffset.FromUnixTimeSeconds(modTimestamp);

                    // Read the module name (MINIDUMP_STRING: Length(4) + UTF16 chars)
                    if (moduleNameRva > 0 && moduleNameRva < fs.Length - 4)
                    {
                        fs.Seek(moduleNameRva, SeekOrigin.Begin);
                        var nameLen = br.ReadUInt32(); // byte count
                        if (nameLen > 0 && nameLen < 1024)
                        {
                            var nameBytes = br.ReadBytes((int)nameLen);
                            var fullPath = Encoding.Unicode.GetString(nameBytes).TrimEnd('\0');
                            meta.FaultingModule = Path.GetFileName(fullPath);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            meta.Error = $"Header parse error: {ex.Message}";
        }

        return meta;
    }

    // =========================================================================
    //  Mock / synthetic dump extraction
    // =========================================================================

    private static CrashMetadata ExtractFromMockDump(string crashId, string dumpPath)
    {
        var meta = new CrashMetadata
        {
            CrashId = crashId,
            DumpPath = dumpPath,
            DumpSignature = "0x4643534D",
            ExtractionMethod = "MockSynthetic"
        };

        // Try to read companion metadata.json for context
        var dir = Path.GetDirectoryName(dumpPath);
        var metadataPath = dir is not null ? Path.Combine(dir, "metadata.json") : null;

        if (metadataPath is not null && File.Exists(metadataPath))
        {
            try
            {
                var json = File.ReadAllText(metadataPath);

                // Try to load stack frames from the crash-dumps metadata (from DellDigitalDelivery.App)
                var stackFramesLoaded = TryLoadStackFramesFromMetadata(json, meta);

                if (!stackFramesLoaded)
                {
                    // Parse the failureBucket to derive exception code (old mock path)
                    var bucketMatch = Regex.Match(json, @"""failureBucket""\s*:\s*""([^""]+)""");
                    if (bucketMatch.Success)
                    {
                        var bucket = bucketMatch.Groups[1].Value;
                        var underscoreIdx = bucket.IndexOf('_');
                        if (underscoreIdx > 0)
                        {
                            var code = "0x" + bucket[..underscoreIdx];
                            meta.ExceptionCode = code;
                            meta.ExceptionName = LookupExceptionName(code);
                        }
                    }

                    // Faulting module from appName
                    var appMatch = Regex.Match(json, @"""appName""\s*:\s*""([^""]+)""");
                    if (appMatch.Success)
                        meta.FaultingModule = appMatch.Groups[1].Value;

                    meta.ThreadCount = 4 + Math.Abs(crashId.GetHashCode() % 20);
                    meta.StackFrames = GenerateMockStackFrames(meta.ExceptionCode, crashId);
                }
            }
            catch
            {
                meta.Error = "Failed to parse companion metadata.json";
            }
        }

        return meta;
    }

    /// <summary>
    /// For MDMP dumps copied from crash-dumps dir, find the original metadata.json
    /// by looking in the crash-dumps directory tree for a matching crashId folder.
    /// </summary>
    private static CrashMetadata? TryLoadRichMetadataForDump(string crashId, string dumpPath)
    {
        // The dump may have been copied by LocalDumpStore into data/dumps/{crashId}/dump.cab
        // but the original metadata.json is in data/crash-dumps/{crashId}/metadata.json
        var candidates = new List<string>();

        // Check alongside the dump itself
        var dumpDir = Path.GetDirectoryName(dumpPath);
        if (dumpDir is not null)
            candidates.Add(Path.Combine(dumpDir, "metadata.json"));

        // Check crash-dumps directory patterns
        var searchRoots = new[]
        {
            Path.Combine(".", "data", "crash-dumps"),
            Path.Combine("..", "data", "crash-dumps"),
        };
        foreach (var root in searchRoots)
        {
            var full = Path.GetFullPath(root);
            if (Directory.Exists(full))
            {
                candidates.Add(Path.Combine(full, crashId, "metadata.json"));
                // Also check all subdirs in case crash ID mapping differs
                foreach (var dir in Directory.GetDirectories(full))
                {
                    if (Path.GetFileName(dir).Contains(crashId[..Math.Min(12, crashId.Length)], StringComparison.OrdinalIgnoreCase))
                        candidates.Add(Path.Combine(dir, "metadata.json"));
                }
            }
        }

        foreach (var metaPath in candidates)
        {
            if (!File.Exists(metaPath)) continue;
            try
            {
                var json = File.ReadAllText(metaPath);
                var meta = new CrashMetadata
                {
                    CrashId = crashId,
                    DumpPath = dumpPath,
                    DumpSignature = "0x4D504D44",
                };
                if (TryLoadStackFramesFromMetadata(json, meta) && meta.StackFrames?.Count > 0)
                    return meta;
            }
            catch { /* continue searching */ }
        }

        return null;
    }

    /// <summary>
    /// Tries to load rich metadata from a DellDigitalDelivery.App crash record JSON.
    /// Returns true if successful, populating the CrashMetadata fields.
    /// </summary>
    private static bool TryLoadStackFramesFromMetadata(string json, CrashMetadata meta)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Check for stackFrames array — this indicates a DellDigitalDelivery.App dump
            if (!root.TryGetProperty("stackFrames", out var framesEl) ||
                framesEl.ValueKind != System.Text.Json.JsonValueKind.Array ||
                framesEl.GetArrayLength() == 0)
                return false;

            // Load stack frames
            var frames = new List<string>();
            foreach (var frame in framesEl.EnumerateArray())
            {
                var f = frame.GetString();
                if (!string.IsNullOrWhiteSpace(f))
                    frames.Add(f);
            }
            meta.StackFrames = frames.AsReadOnly();

            // Load exception info
            if (root.TryGetProperty("exceptionCode", out var ec))
            {
                meta.ExceptionCode = ec.GetString();
                meta.ExceptionName = LookupExceptionName(meta.ExceptionCode);
            }
            if (root.TryGetProperty("exceptionName", out var en))
                meta.ExceptionName = en.GetString();
            if (root.TryGetProperty("faultingModule", out var fm))
                meta.FaultingModule = fm.GetString();
            if (root.TryGetProperty("threadCount", out var tc))
                meta.ThreadCount = tc.GetInt32();
            if (root.TryGetProperty("scenarioName", out var sn))
                meta.ExtractionMethod = $"DellApp:{sn.GetString()}";
            else
                meta.ExtractionMethod = "DellAppDump";

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Builds a realistic synthetic stack trace. The top application frames are
    /// deterministic per exception code so that duplicate crashes bucket together.
    /// A small crashId-seeded tail adds per-crash variance in system frames.
    /// </summary>
    private static IReadOnlyList<string> GenerateMockStackFrames(string? exceptionCode, string crashId)
    {
        // Per-exception-code application frames (these drive bucketing)
        var appFrames = (exceptionCode?.ToUpperInvariant()) switch
        {
            "0XC0000005" => new[]
            {
                "Dell.Digital.Delivery.Service.SubAgent!PluginManager.LoadPlugin(String pluginPath)",
                "Dell.Digital.Delivery.Service.SubAgent!PluginManager.InitializePlugins()",
                "Dell.Digital.Delivery.Service.SubAgent!ServiceHost.OnStart(String[] args)",
            },
            "0XC00000FD" => new[]
            {
                "Dell.Digital.Delivery.Service.SubAgent!ConfigResolver.ResolveRecursive(ConfigNode node)",
                "Dell.Digital.Delivery.Service.SubAgent!ConfigResolver.ResolveRecursive(ConfigNode node)",
                "Dell.Digital.Delivery.Service.SubAgent!UnifiedAgentConfig.Load(String path)",
            },
            "0XC0000374" => new[]
            {
                "Dell.Digital.Delivery.Service.SubAgent!NativeInterop.MarshalCallbackData(IntPtr ptr)",
                "Dell.Digital.Delivery.Service.SubAgent!NativeInterop.ProcessNotification(Int32 code)",
                "Dell.Digital.Delivery.Service.SubAgent!NotificationPump.Dispatch()",
            },
            "0XC0000409" => new[]
            {
                "Dell.Digital.Delivery.Service.SubAgent!CryptoHelper.DecryptBuffer(Byte[] input)",
                "Dell.Digital.Delivery.Service.SubAgent!LicenseValidator.ValidateSignature(License lic)",
                "Dell.Digital.Delivery.Service.SubAgent!EntitlementService.CheckEntitlement(String id)",
            },
            "0XC0000006" => new[]
            {
                "Dell.Digital.Delivery.Service.SubAgent!CacheManager.ReadPage(Int64 offset)",
                "Dell.Digital.Delivery.Service.SubAgent!CacheManager.GetCachedMetadata(String key)",
                "Dell.Digital.Delivery.Service.SubAgent!ContentDownloader.ResumeDownload()",
            },
            "0XE0434352" => new[]
            {
                "Dell.Digital.Delivery.Service.SubAgent!Program.Main(String[] args)",
                "Dell.Digital.Delivery.Service.SubAgent!ServiceHost.Run()",
                "Dell.Digital.Delivery.Service.SubAgent!ServiceHost.OnStart(String[] args)",
            },
            _ => new[]
            {
                "Dell.Digital.Delivery.Service.SubAgent!Unknown.CrashSite()",
                "Dell.Digital.Delivery.Service.SubAgent!ServiceHost.Run()",
                "Dell.Digital.Delivery.Service.SubAgent!Program.Main(String[] args)",
            }
        };

        // Per-crash-id variance in the system tail (doesn't affect bucketing)
        var rng = new Random(crashId.GetHashCode());
        var systemTails = new[]
        {
            "coreclr!CallDescrWorkerInternal+0x83",
            "coreclr!MethodDesc::MakeJitWorker+0x2a1",
            "System.Private.CoreLib!System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()",
            "ntdll!RtlUserThreadStart+0x21",
            "kernel32!BaseThreadInitThunk+0x1d",
            "ntdll!NtWaitForSingleObject+0x14",
            "kernelbase!UnhandledExceptionFilter+0x1f2",
        };

        var frames = new List<string>(appFrames);
        // Pick 2-4 system frames
        var tailCount = 2 + rng.Next(3);
        for (int i = 0; i < tailCount; i++)
            frames.Add(systemTails[rng.Next(systemTails.Length)]);

        return frames.AsReadOnly();
    }

    // =========================================================================
    //  Helpers
    // =========================================================================

    private static async Task<uint> ReadSignatureAsync(string path, CancellationToken ct)
    {
        var buf = new byte[4];
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: 4096, useAsync: true);
        _ = await fs.ReadAsync(buf, ct).ConfigureAwait(false);
        return BitConverter.ToUInt32(buf, 0);
    }

    private static string? FindCdb()
    {
        // Search common WinDbg / Debugging Tools install locations
        var candidates = new[]
        {
            @"C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe",
            @"C:\Program Files\Windows Kits\10\Debuggers\x64\cdb.exe",
            @"C:\Program Files (x86)\Windows Kits\10\Debuggers\x86\cdb.exe",
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        // Try PATH
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "cdb.exe",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                var result = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                if (proc.ExitCode == 0 && File.Exists(result.Split('\n')[0].Trim()))
                    return result.Split('\n')[0].Trim();
            }
        }
        catch { /* not found */ }

        return null;
    }

    /// <summary>
    /// Maps well-known NTSTATUS exception codes to human-readable names.
    /// </summary>
    private static string? LookupExceptionName(string? hexCode)
    {
        if (hexCode is null) return null;

        return hexCode.ToUpperInvariant().Replace("0X", "0x") switch
        {
            "0xC0000005" => "Access Violation",
            "0xC00000FD" => "Stack Overflow",
            "0xC0000374" => "Heap Corruption",
            "0xC0000409" => "Stack Buffer Overrun",
            "0xC0000006" => "In-Page I/O Error",
            "0xE0434352" => "CLR Unhandled Exception",
            "0xC000001D" => "Illegal Instruction",
            "0xC0000194" => "Possible Deadlock",
            "0xC00000AA" => "Resource Not Owned",
            "0x80000003" => "Breakpoint",
            "0xC0000008" => "Invalid Handle",
            "0xC000000D" => "Invalid Parameter",
            "0xC0000017" => "No Memory",
            "0xC0000096" => "Privileged Instruction",
            _ => null
        };
    }
}
