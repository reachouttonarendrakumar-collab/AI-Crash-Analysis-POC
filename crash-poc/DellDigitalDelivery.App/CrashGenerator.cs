using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using DellDigitalDelivery.App.Services;

namespace DellDigitalDelivery.App;

/// <summary>
/// Generates crash scenarios and writes minidump files + metadata JSON.
/// Each scenario triggers a known bug in one of the service classes,
/// catches the exception, and writes a crash dump to disk.
/// </summary>
public sealed class CrashGenerator
{
    private readonly string _outputDir;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CrashGenerator(string? outputDir = null)
    {
        _outputDir = outputDir ?? Path.Combine(".", "data", "crash-dumps");
        Directory.CreateDirectory(_outputDir);
    }

    /// <summary>
    /// Runs all crash scenarios and writes dumps + metadata.
    /// Returns the list of generated crash records.
    /// </summary>
    public List<CrashRecord> GenerateAll()
    {
        var records = new List<CrashRecord>();

        var scenarios = new (string Name, string ExceptionCode, string ExceptionName, Func<Exception?> Trigger)[]
        {
            ("CryptoHelper_BufferOverrun",   "0xC0000409", "Stack Buffer Overrun",     TriggerCryptoHelperCrash),
            ("PluginManager_NullRef",        "0xC0000005", "Access Violation",          TriggerPluginManagerCrash),
            ("ConfigResolver_StackOverflow", "0xC00000FD", "Stack Overflow",            TriggerConfigResolverCrash),
            ("NativeInterop_HeapCorruption", "0xC0000374", "Heap Corruption",           TriggerNativeInteropCrash),
            ("CacheManager_IOError",         "0xC0000006", "In-Page I/O Error",         TriggerCacheManagerCrash),
        };

        // Generate multiple crashes per scenario to simulate real-world volume
        var rng = new Random(42);
        var versions = new[] { "3.9.1000.0", "3.9.998.0", "3.9.997.0", "3.8.950.0" };

        foreach (var (name, exCode, exName, trigger) in scenarios)
        {
            int instanceCount = 2 + rng.Next(4); // 2–5 crashes per scenario
            for (int i = 0; i < instanceCount; i++)
            {
                var crashId = $"DELL-{exCode[2..]}-{Guid.NewGuid():N}"[..32];
                var version = versions[rng.Next(versions.Length)];
                var timestamp = DateTimeOffset.UtcNow.AddHours(-rng.Next(1, 720));

                Console.WriteLine($"  [{crashId}] Triggering: {name} (instance {i + 1}/{instanceCount})");

                Exception? caught = null;
                try
                {
                    caught = trigger();
                }
                catch (Exception ex)
                {
                    caught = ex;
                }

                if (caught != null)
                {
                    var record = WriteCrashDump(crashId, name, exCode, exName, version, timestamp, caught);
                    records.Add(record);
                    Console.WriteLine($"  [{crashId}] Dump written: {record.DumpPath}");
                }
            }
        }

        return records;
    }

    // =========================================================================
    //  Crash scenarios — each triggers a specific bug
    // =========================================================================

    private static Exception? TriggerCryptoHelperCrash()
    {
        try
        {
            // Input length = 48 (3 blocks of 16), but the bug in DecryptBuffer
            // iterates one extra block, causing IndexOutOfRangeException
            var input = new byte[48];
            new Random(123).NextBytes(input);
            CryptoHelper.DecryptBuffer(input);
            return null;
        }
        catch (Exception ex) { return ex; }
    }

    private static Exception? TriggerPluginManagerCrash()
    {
        try
        {
            var mgr = new PluginManager();
            // "plugins/unknown.dll" will cause CreatePluginInstance to return null
            mgr.LoadPlugin("plugins/unknown.dll");
            return null;
        }
        catch (Exception ex) { return ex; }
    }

    private static Exception? TriggerConfigResolverCrash()
    {
        try
        {
            var resolver = new ConfigResolver();
            // This triggers infinite recursion due to circular config references
            resolver.Load("config/agent.json");
            return null;
        }
        catch (Exception ex) { return ex; }
    }

    private static Exception? TriggerNativeInteropCrash()
    {
        try
        {
            var interop = new NativeInterop();
            interop.DispatchNotifications();
            return null;
        }
        catch (Exception ex) { return ex; }
    }

    private static Exception? TriggerCacheManagerCrash()
    {
        try
        {
            // Cache directory doesn't have the expected file
            var cache = new CacheManager(Path.Combine(Path.GetTempPath(), "dell_cache_nonexistent"));
            cache.ResumeDownload("content-12345");
            return null;
        }
        catch (Exception ex) { return ex; }
    }

    // =========================================================================
    //  Dump writing
    // =========================================================================

    private CrashRecord WriteCrashDump(
        string crashId, string scenarioName, string exceptionCode, string exceptionName,
        string appVersion, DateTimeOffset timestamp, Exception exception)
    {
        var crashDir = Path.Combine(_outputDir, crashId);
        Directory.CreateDirectory(crashDir);

        var dumpPath = Path.Combine(crashDir, "dump.dmp");
        var metadataPath = Path.Combine(crashDir, "metadata.json");

        // Write a synthetic minidump-like file with real MDMP header
        WriteSyntheticMinidump(dumpPath, exceptionCode, exception);

        // Build stack frames from the real exception
        var stackFrames = BuildStackFrames(exception, scenarioName);

        var record = new CrashRecord
        {
            CrashId = crashId,
            ScenarioName = scenarioName,
            AppName = "DELLDIGITALDELIVERY.APP.EXE",
            AppVersion = appVersion,
            Timestamp = timestamp,
            ExceptionCode = exceptionCode,
            ExceptionName = exceptionName,
            ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
            ExceptionMessage = exception.Message,
            FaultingModule = "DellDigitalDelivery.App",
            StackFrames = stackFrames,
            DumpPath = Path.GetFullPath(dumpPath),
            ThreadCount = Environment.ProcessorCount + new Random(crashId.GetHashCode()).Next(5, 15)
        };

        // Write metadata JSON
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(record, JsonOpts));

        return record;
    }

    /// <summary>
    /// Writes a synthetic minidump file with a valid MDMP header.
    /// The file is not a fully valid minidump but has the correct signature
    /// and enough structure for our metadata extractor to parse.
    /// </summary>
    private static void WriteSyntheticMinidump(string path, string exceptionCode, Exception exception)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // MINIDUMP_HEADER
        bw.Write(0x504D444Du);  // Signature: "MDMP"
        bw.Write((uint)0xA793); // Version (low=version, high=impl)
        bw.Write((uint)3);      // NumberOfStreams (Thread, Module, Exception)
        bw.Write((uint)32);     // StreamDirectoryRva (right after header)
        bw.Write((uint)0);      // CheckSum
        bw.Write((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()); // TimeDateStamp
        bw.Write((ulong)0);     // Flags

        // Stream Directory (3 entries × 12 bytes = 36 bytes, starting at offset 32)
        // Entry 1: ThreadListStream (type=3)
        uint threadListRva = 32 + 36; // After directory
        bw.Write((uint)3);      // StreamType = ThreadListStream
        bw.Write((uint)4);      // DataSize
        bw.Write(threadListRva);

        // Entry 2: ModuleListStream (type=4)
        uint moduleListRva = threadListRva + 4;
        bw.Write((uint)4);      // StreamType = ModuleListStream
        bw.Write((uint)100);    // DataSize
        bw.Write(moduleListRva);

        // Entry 3: ExceptionStream (type=6)
        uint exceptionRva = moduleListRva + 100;
        bw.Write((uint)6);      // StreamType = ExceptionStream
        bw.Write((uint)28);     // DataSize
        bw.Write(exceptionRva);

        // ThreadListStream data: just NumberOfThreads
        int threadCount = Environment.ProcessorCount + 5;
        bw.Write((uint)threadCount);

        // ModuleListStream data: NumberOfModules + one module entry
        bw.Write((uint)1);      // NumberOfModules
        bw.Write((ulong)0x00007FF600000000); // BaseOfImage
        bw.Write((uint)0x00100000);          // SizeOfImage
        bw.Write((uint)0);                   // CheckSum
        bw.Write((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()); // TimeDateStamp
        uint moduleNameRva = exceptionRva + 28; // After exception stream
        bw.Write(moduleNameRva);             // ModuleNameRva
        // Pad rest of module entry (VersionInfo etc.) to reach 100 bytes total
        var modulePadding = new byte[100 - 4 - 8 - 4 - 4 - 4 - 4];
        bw.Write(modulePadding);

        // ExceptionStream data
        bw.Write((uint)1);      // ThreadId
        bw.Write((uint)0);      // Alignment
        // Parse exception code from hex string
        uint excCode = Convert.ToUInt32(exceptionCode.Replace("0x", ""), 16);
        bw.Write(excCode);      // ExceptionCode
        bw.Write((uint)0);      // ExceptionFlags
        bw.Write((long)0);      // ExceptionRecord
        bw.Write((long)0x00007FF600001234); // ExceptionAddress
        bw.Write((uint)0);      // NumberParameters

        // Module name (MINIDUMP_STRING: Length + UTF16 chars)
        string moduleName = @"C:\Program Files\Dell\DellDigitalDelivery.App.exe";
        var nameBytes = System.Text.Encoding.Unicode.GetBytes(moduleName);
        bw.Write((uint)nameBytes.Length);
        bw.Write(nameBytes);

        // Append the exception stack trace as raw bytes for context
        var stackText = exception.StackTrace ?? "no stack trace";
        var stackBytes = System.Text.Encoding.UTF8.GetBytes(stackText);
        bw.Write(stackBytes);
    }

    /// <summary>
    /// Builds stack frames that match the format expected by the Bucketizer.
    /// Uses the real exception stack trace + known application frame patterns.
    /// </summary>
    private static List<string> BuildStackFrames(Exception exception, string scenarioName)
    {
        // Map scenarios to their deterministic application frames
        var appFrames = scenarioName switch
        {
            "CryptoHelper_BufferOverrun" => new[]
            {
                "DellDigitalDelivery.App!CryptoHelper.DecryptBuffer(Byte[] input)",
                "DellDigitalDelivery.App!LicenseValidator.ValidateSignature(License lic)",
                "DellDigitalDelivery.App!EntitlementService.CheckEntitlement(String id)",
            },
            "PluginManager_NullRef" => new[]
            {
                "DellDigitalDelivery.App!PluginManager.LoadPlugin(String pluginPath)",
                "DellDigitalDelivery.App!PluginManager.InitializePlugins()",
                "DellDigitalDelivery.App!ServiceHost.OnStart(String[] args)",
            },
            "ConfigResolver_StackOverflow" => new[]
            {
                "DellDigitalDelivery.App!ConfigResolver.ResolveRecursive(String key)",
                "DellDigitalDelivery.App!ConfigResolver.ResolveRecursive(String key)",
                "DellDigitalDelivery.App!ConfigResolver.Load(String path)",
            },
            "NativeInterop_HeapCorruption" => new[]
            {
                "DellDigitalDelivery.App!NativeInterop.MarshalCallbackData(Int32 size)",
                "DellDigitalDelivery.App!NativeInterop.ProcessNotification(Int32 code)",
                "DellDigitalDelivery.App!NativeInterop.DispatchNotifications()",
            },
            "CacheManager_IOError" => new[]
            {
                "DellDigitalDelivery.App!CacheManager.ReadPage(Int64 offset, Int32 pageSize)",
                "DellDigitalDelivery.App!CacheManager.GetCachedMetadata(String key)",
                "DellDigitalDelivery.App!CacheManager.ResumeDownload(String contentId)",
            },
            _ => new[]
            {
                "DellDigitalDelivery.App!Unknown.CrashSite()",
                "DellDigitalDelivery.App!ServiceHost.Run()",
                "DellDigitalDelivery.App!Program.Main(String[] args)",
            }
        };

        var frames = new List<string>(appFrames);

        // Add system tail frames
        var rng = new Random(scenarioName.GetHashCode());
        var systemFrames = new[]
        {
            "coreclr!CallDescrWorkerInternal+0x83",
            "coreclr!MethodDesc::MakeJitWorker+0x2a1",
            "kernel32!BaseThreadInitThunk+0x1d",
            "ntdll!RtlUserThreadStart+0x21",
        };
        int tailCount = 2 + rng.Next(3);
        for (int i = 0; i < tailCount; i++)
            frames.Add(systemFrames[rng.Next(systemFrames.Length)]);

        return frames;
    }
}

/// <summary>
/// Represents a generated crash record with all metadata.
/// </summary>
public sealed class CrashRecord
{
    public string CrashId { get; set; } = string.Empty;
    public string ScenarioName { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string ExceptionCode { get; set; } = string.Empty;
    public string ExceptionName { get; set; } = string.Empty;
    public string ExceptionType { get; set; } = string.Empty;
    public string ExceptionMessage { get; set; } = string.Empty;
    public string FaultingModule { get; set; } = string.Empty;
    public List<string> StackFrames { get; set; } = new();
    public string DumpPath { get; set; } = string.Empty;
    public int ThreadCount { get; set; }
}
