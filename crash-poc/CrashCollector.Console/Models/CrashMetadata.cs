using System.Text.Json.Serialization;

namespace CrashCollector.Console.Models;

/// <summary>
/// Structured metadata extracted from a crash dump file.
/// </summary>
public sealed class CrashMetadata
{
    /// <summary>Crash identifier this metadata belongs to.</summary>
    [JsonPropertyName("crashId")]
    public string CrashId { get; set; } = string.Empty;

    /// <summary>Path to the dump file that was analysed.</summary>
    [JsonPropertyName("dumpPath")]
    public string? DumpPath { get; set; }

    // ── Exception info ──────────────────────────────────────────────────────

    /// <summary>NTSTATUS / Win32 exception code (e.g. 0xC0000005).</summary>
    [JsonPropertyName("exceptionCode")]
    public string? ExceptionCode { get; set; }

    /// <summary>Human-readable name of the exception (e.g. "Access Violation").</summary>
    [JsonPropertyName("exceptionName")]
    public string? ExceptionName { get; set; }

    /// <summary>Number of exception parameters recorded in the dump.</summary>
    [JsonPropertyName("exceptionParameterCount")]
    public int ExceptionParameterCount { get; set; }

    // ── Faulting module ─────────────────────────────────────────────────────

    /// <summary>Name of the module that caused the crash.</summary>
    [JsonPropertyName("faultingModule")]
    public string? FaultingModule { get; set; }

    /// <summary>Base address of the faulting module (hex string).</summary>
    [JsonPropertyName("faultingModuleBase")]
    public string? FaultingModuleBase { get; set; }

    /// <summary>Size of the faulting module image in bytes.</summary>
    [JsonPropertyName("faultingModuleSize")]
    public uint FaultingModuleSize { get; set; }

    /// <summary>TimeDateStamp from the faulting module PE header.</summary>
    [JsonPropertyName("faultingModuleTimestamp")]
    public DateTimeOffset? FaultingModuleTimestamp { get; set; }

    // ── Thread info ─────────────────────────────────────────────────────────

    /// <summary>Total number of threads captured in the dump.</summary>
    [JsonPropertyName("threadCount")]
    public int ThreadCount { get; set; }

    // ── Dump-level info ─────────────────────────────────────────────────────

    /// <summary>Minidump signature (should be "MDMP" / 0x504D444D).</summary>
    [JsonPropertyName("dumpSignature")]
    public string? DumpSignature { get; set; }

    /// <summary>Minidump version.</summary>
    [JsonPropertyName("dumpVersion")]
    public int DumpVersion { get; set; }

    /// <summary>Number of streams in the minidump directory.</summary>
    [JsonPropertyName("streamCount")]
    public int StreamCount { get; set; }

    /// <summary>Timestamp embedded in the minidump header.</summary>
    [JsonPropertyName("dumpTimestamp")]
    public DateTimeOffset? DumpTimestamp { get; set; }

    // ── Stack trace ─────────────────────────────────────────────────────────

    /// <summary>Raw stack frames extracted from the dump (one string per frame).</summary>
    [JsonPropertyName("stackFrames")]
    public IReadOnlyList<string> StackFrames { get; set; } = Array.Empty<string>();

    // ── Extraction metadata ─────────────────────────────────────────────────

    /// <summary>Which method produced this result: "WinDbg", "MinidumpHeader", "MockSynthetic".</summary>
    [JsonPropertyName("extractionMethod")]
    public string ExtractionMethod { get; set; } = "Unknown";

    /// <summary>Non-null when extraction failed or was partial.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>Raw WinDbg !analyze -v output (only populated when WinDbg was used).</summary>
    [JsonPropertyName("winDbgRawOutput")]
    public string? WinDbgRawOutput { get; set; }

    public override string ToString()
    {
        var exc = ExceptionCode is not null ? $"exception={ExceptionCode} ({ExceptionName})" : "exception=n/a";
        var mod = FaultingModule ?? "n/a";
        return $"[{CrashId}] {exc} | module={mod} | threads={ThreadCount} | via={ExtractionMethod}";
    }
}
