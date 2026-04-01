using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrashCollector.AI;

/// <summary>
/// Unified response type for all LLM providers
/// </summary>
public sealed class LLMResponse
{
    public bool Success { get; init; }
    public string? Text { get; init; }
    public string? Error { get; init; }
    public int PromptTokens { get; init; }
    public int ResponseTokens { get; init; }
}

/// <summary>
/// Builds analysis prompts from crash context (stack trace, source code, PDB info)
/// and parses LLM responses into structured analysis results.
/// </summary>
public sealed class CrashAnalyzer
{
    private readonly LlmClient _llmClient;
    private readonly string _sourceCodeRoot;

    public CrashAnalyzer(LlmClient llmClient, string sourceCodeRoot)
    {
        _llmClient = llmClient;
        _sourceCodeRoot = sourceCodeRoot;
    }

    /// <summary>
    /// Analyzes a crash bucket using the configured LLM provider.
    /// Sends stack traces + relevant source code and gets root cause + fix.
    /// </summary>
    public async Task<AnalysisResult> AnalyzeCrashAsync(
        string bucketId,
        string? exceptionCode,
        string? exceptionName,
        IReadOnlyList<string> keyFrames,
        IReadOnlyList<string> allFrames,
        int crashCount,
        CancellationToken ct = default)
    {
        var result = new AnalysisResult
        {
            BucketId = bucketId,
            ExceptionCode = exceptionCode,
            ExceptionName = exceptionName
        };

        try
        {
            // Find source files that match the key frames
            var sourceContext = GatherSourceContext(keyFrames);

            // Build the analysis prompt
            var prompt = BuildAnalysisPrompt(
                exceptionCode, exceptionName, keyFrames, allFrames,
                crashCount, sourceContext);

            // Call the unified LLM client
            var response = await _llmClient.GenerateContentAsync(prompt, ct).ConfigureAwait(false);

            if (!response.Success)
            {
                result.Status = "Failed";
                result.ErrorMessage = response.Error;
                return result;
            }

            result.PromptTokens = response.PromptTokens;
            result.ResponseTokens = response.ResponseTokens;

            // Parse the structured response
            ParseAnalysisResponse(response.Text, result, sourceContext);
            result.Status = "Completed";
        }
        catch (Exception ex)
        {
            result.Status = "Failed";
            result.ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Searches the source code directory for files matching the key frames.
    /// </summary>
    private Dictionary<string, string> GatherSourceContext(IReadOnlyList<string> keyFrames)
    {
        var sourceFiles = new Dictionary<string, string>();

        if (!Directory.Exists(_sourceCodeRoot))
            return sourceFiles;

        // Extract class/file names from key frames
        // Frame format: "DellDigitalDelivery.App!CryptoHelper.DecryptBuffer(Byte[] input)"
        var targetFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var frame in keyFrames)
        {
            var match = Regex.Match(frame, @"!(\w+)\.\w+");
            if (match.Success)
            {
                targetFiles.Add(match.Groups[1].Value + ".cs");
            }
        }

        // Search for matching .cs files
        foreach (var file in Directory.EnumerateFiles(_sourceCodeRoot, "*.cs", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);
            if (targetFiles.Contains(fileName))
            {
                try
                {
                    var content = File.ReadAllText(file);
                    var relativePath = Path.GetRelativePath(_sourceCodeRoot, file);
                    sourceFiles[relativePath] = content;
                }
                catch { /* skip unreadable files */ }
            }
        }

        return sourceFiles;
    }

    private static string BuildAnalysisPrompt(
        string? exceptionCode, string? exceptionName,
        IReadOnlyList<string> keyFrames, IReadOnlyList<string> allFrames,
        int crashCount, Dictionary<string, string> sourceContext)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are an expert crash analyst for Windows applications. Analyze the following crash and provide a root cause analysis and code fix.");
        sb.AppendLine();
        sb.AppendLine("## Crash Information");
        sb.AppendLine($"- Exception Code: {exceptionCode ?? "Unknown"}");
        sb.AppendLine($"- Exception Name: {exceptionName ?? "Unknown"}");
        sb.AppendLine($"- Number of crashes with this signature: {crashCount}");
        sb.AppendLine();

        sb.AppendLine("## Key Stack Frames (bucket signature)");
        for (int i = 0; i < keyFrames.Count; i++)
            sb.AppendLine($"  {i + 1}. {keyFrames[i]}");
        sb.AppendLine();

        if (allFrames.Count > 0)
        {
            sb.AppendLine("## Full Stack Trace");
            for (int i = 0; i < allFrames.Count; i++)
                sb.AppendLine($"  #{i:D2} {allFrames[i]}");
            sb.AppendLine();
        }

        if (sourceContext.Count > 0)
        {
            sb.AppendLine("## Source Code (from PDB-matched files)");
            foreach (var (path, content) in sourceContext)
            {
                sb.AppendLine($"### File: {path}");
                sb.AppendLine("```csharp");
                sb.AppendLine(content);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Required Response Format");
        sb.AppendLine("Respond with EXACTLY this format (keep the markers):");
        sb.AppendLine();
        sb.AppendLine("ROOT_CAUSE:");
        sb.AppendLine("[Detailed explanation of the root cause]");
        sb.AppendLine();
        sb.AppendLine("CONFIDENCE: [0.0-1.0]");
        sb.AppendLine();
        sb.AppendLine("AFFECTED_FILE: [Use EXACTLY one of the file paths shown in the '## Source Code' section headers above, e.g., 'Services\\PluginManager.cs']");
        sb.AppendLine();
        sb.AppendLine("AFFECTED_FUNCTION: [name of the function with the bug]");
        sb.AppendLine();
        sb.AppendLine("FIX_DESCRIPTION:");
        sb.AppendLine("[Brief description of what the fix does]");
        sb.AppendLine();
        sb.AppendLine("FIXED_CODE:");
        sb.AppendLine("```csharp");
        sb.AppendLine("[Complete corrected source code for the affected file]");
        sb.AppendLine("```");

        return sb.ToString();
    }

    private static void ParseAnalysisResponse(string text, AnalysisResult result, Dictionary<string, string> sourceContext)
    {
        // Normalize line endings (LLMs may return \r\n)
        text = text.Replace("\r\n", "\n");

        // Parse ROOT_CAUSE
        var rootCauseMatch = Regex.Match(text, @"ROOT_CAUSE:\s*\n(.*?)(?=\nCONFIDENCE:)", RegexOptions.Singleline);
        if (rootCauseMatch.Success)
            result.RootCause = rootCauseMatch.Groups[1].Value.Trim();

        // Parse CONFIDENCE
        var confidenceMatch = Regex.Match(text, @"CONFIDENCE:\s*([\d.]+)");
        if (confidenceMatch.Success && double.TryParse(confidenceMatch.Groups[1].Value, out var conf))
            result.Confidence = Math.Clamp(conf, 0.0, 1.0);

        // Parse AFFECTED_FILE
        var fileMatch = Regex.Match(text, @"AFFECTED_FILE:\s*(.+)");
        if (fileMatch.Success)
            result.AffectedFile = fileMatch.Groups[1].Value.Trim();

        // Parse AFFECTED_FUNCTION
        var funcMatch = Regex.Match(text, @"AFFECTED_FUNCTION:\s*(.+)");
        if (funcMatch.Success)
            result.AffectedFunction = funcMatch.Groups[1].Value.Trim();

        // Parse FIX_DESCRIPTION
        var fixDescMatch = Regex.Match(text, @"FIX_DESCRIPTION:\s*\n(.*?)(?=\nFIXED_CODE:)", RegexOptions.Singleline);
        if (fixDescMatch.Success)
            result.FixDescription = fixDescMatch.Groups[1].Value.Trim();

        // Parse FIXED_CODE — handle various LLM formatting (newline or same-line code fence)
        var codeMatch = Regex.Match(text, @"FIXED_CODE:\s*\n?```(?:csharp)?\s*\n(.*?)```", RegexOptions.Singleline);
        if (codeMatch.Success)
            result.FixedCode = codeMatch.Groups[1].Value.Trim();

        // Fallback: if no closing ``` found (truncated response), grab everything after the opening fence
        if (result.FixedCode is null || result.FixedCode.Length == 0)
        {
            var fallback = Regex.Match(text, @"FIXED_CODE:\s*\n?```(?:csharp)?\s*\n(.+)", RegexOptions.Singleline);
            if (fallback.Success)
                result.FixedCode = fallback.Groups[1].Value.Trim();
        }

        System.Console.WriteLine($"[CrashAnalyzer] Parse results — RootCause:{result.RootCause?.Length ?? 0}chars, Confidence:{result.Confidence}, File:{result.AffectedFile}, Func:{result.AffectedFunction}, FixDesc:{result.FixDescription?.Length ?? 0}chars, FixedCode:{result.FixedCode?.Length ?? 0}chars");
        if (result.FixedCode is null || result.FixedCode.Length == 0)
        {
            // Log the tail of the LLM response to diagnose why FIXED_CODE wasn't parsed
            var tail = text.Length > 300 ? text[^300..] : text;
            System.Console.WriteLine($"[CrashAnalyzer] FIXED_CODE not parsed. Response tail: {tail}");
        }

        // Validate/correct AffectedFile against known source context paths
        if (result.AffectedFile is not null && sourceContext.Count > 0)
        {
            var fileName = Path.GetFileName(result.AffectedFile.Replace('/', '\\'));
            var matchingKey = sourceContext.Keys.FirstOrDefault(k =>
                k.Replace('\\', '/').Equals(result.AffectedFile.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)) 
                ?? sourceContext.Keys.FirstOrDefault(k =>
                    Path.GetFileName(k).Equals(fileName, StringComparison.OrdinalIgnoreCase));
            if (matchingKey is not null)
                result.AffectedFile = matchingKey.Replace('\\', '/');
        }

        // If no source context was available, mark for manual review
        if (sourceContext.Count == 0 && result.Confidence < 0.5)
        {
            result.Status = "ManualReview";
        }
    }
}

/// <summary>
/// Structured result from AI crash analysis.
/// </summary>
public sealed class AnalysisResult
{
    public string BucketId { get; set; } = string.Empty;
    public string? ExceptionCode { get; set; }
    public string? ExceptionName { get; set; }
    public string? RootCause { get; set; }
    public double Confidence { get; set; }
    public string? AffectedFile { get; set; }
    public string? AffectedFunction { get; set; }
    public string? FixDescription { get; set; }
    public string? FixedCode { get; set; }
    public string Status { get; set; } = "Pending";
    public string? ErrorMessage { get; set; }
    public int PromptTokens { get; set; }
    public int ResponseTokens { get; set; }
}
