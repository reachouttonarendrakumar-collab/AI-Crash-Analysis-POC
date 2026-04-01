using System.Text.Json.Serialization;

namespace CrashCollector.Console.Models;

/// <summary>
/// A normalised, hashable stack trace signature used for crash bucketing.
/// Two crashes that share the same top non-system frames produce the same
/// <see cref="BucketId"/>, regardless of address layout or timestamps.
/// </summary>
public sealed class StackSignature
{
    /// <summary>
    /// Stable SHA-256-based bucket identifier derived from the normalised
    /// key frames and exception code.
    /// </summary>
    [JsonPropertyName("bucketId")]
    public string BucketId { get; set; } = string.Empty;

    /// <summary>The exception code included in the hash input.</summary>
    [JsonPropertyName("exceptionCode")]
    public string? ExceptionCode { get; set; }

    /// <summary>
    /// The top N non-system frames after normalisation (module!function).
    /// These are the frames that form the bucket identity.
    /// </summary>
    [JsonPropertyName("keyFrames")]
    public IReadOnlyList<string> KeyFrames { get; set; } = Array.Empty<string>();

    /// <summary>Full normalised stack trace (all frames).</summary>
    [JsonPropertyName("normalizedFrames")]
    public IReadOnlyList<string> NormalizedFrames { get; set; } = Array.Empty<string>();

    /// <summary>Number of raw frames before normalisation.</summary>
    [JsonPropertyName("rawFrameCount")]
    public int RawFrameCount { get; set; }

    public override string ToString() =>
        $"bucket={BucketId[..12]}… keys=[{string.Join(" → ", KeyFrames)}]";
}
