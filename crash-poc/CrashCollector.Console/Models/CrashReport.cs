using System.Text.Json.Serialization;

namespace CrashCollector.Console.Models;

/// <summary>
/// Represents a single crash report retrieved from WER.
/// </summary>
public sealed class CrashReport
{
    /// <summary>WER-assigned crash / event identifier.</summary>
    [JsonPropertyName("crashId")]
    public string CrashId { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the crash event.</summary>
    [JsonPropertyName("eventTime")]
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Version of the crashing application.</summary>
    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = string.Empty;

    /// <summary>URL to download the minidump / cab, if available.</summary>
    [JsonPropertyName("cabDownloadUrl")]
    public string? DumpDownloadUrl { get; set; }

    /// <summary>Executable name that produced the crash.</summary>
    [JsonPropertyName("appName")]
    public string AppName { get; set; } = string.Empty;

    /// <summary>Windows error signature bucket (e.g. E0434352).</summary>
    [JsonPropertyName("failureBucket")]
    public string? FailureBucket { get; set; }

    public override string ToString() =>
        $"[{CrashId}] {AppName} v{AppVersion} @ {Timestamp:u} | bucket={FailureBucket ?? "n/a"} | dump={DumpDownloadUrl ?? "none"}";
}
