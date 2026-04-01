using System.Text;
using System.Text.Json;
using CrashCollector.Console.Data;

namespace CrashCollector.AI;

/// <summary>
/// Orchestrates the full AI crash analysis and auto-fix workflow:
/// 1. Analyze crash bucket with Gemini AI
/// 2. Generate code fix
/// 3. Create branch, commit fix, and open PR on GitHub
/// 4. Store results in database
/// </summary>
public sealed class AutoFixWorkflow
{
    private readonly CrashAnalyzer _analyzer;
    private readonly GitHubClient? _github;
    private readonly AIRepository _aiRepo;
    private readonly string _sourceCodeRoot;
    private readonly string _repoPathPrefix;

    public AutoFixWorkflow(
        CrashAnalyzer analyzer,
        GitHubClient? github,
        AIRepository aiRepo,
        string sourceCodeRoot,
        string repoPathPrefix = "crash-poc/DellDigitalDelivery.App")
    {
        _analyzer = analyzer;
        _github = github;
        _aiRepo = aiRepo;
        _sourceCodeRoot = sourceCodeRoot;
        _repoPathPrefix = repoPathPrefix.TrimEnd('/');
    }

    /// <summary>
    /// Runs the full workflow for a single crash bucket:
    /// analyze → generate fix → create branch → commit → PR.
    /// </summary>
    public async Task<WorkflowResult> ExecuteAsync(
        string bucketId,
        string? exceptionCode,
        string? exceptionName,
        IReadOnlyList<string> keyFrames,
        IReadOnlyList<string> allFrames,
        int crashCount,
        CancellationToken ct = default)
    {
        var workflowResult = new WorkflowResult { BucketId = bucketId };
        var analysisId = $"AI-{Guid.NewGuid():N}"[..24];

        System.Console.WriteLine($"[AutoFix] Starting analysis for bucket {bucketId[..12]}...");

        // ── Step 1: Save initial analysis record ────────────────────────────
        _aiRepo.UpsertAnalysis(new AIAnalysisRow
        {
            AnalysisId = analysisId,
            BucketId = bucketId,
            Status = "Analyzing"
        });

        // ── Step 2: Run AI analysis ─────────────────────────────────────────
        var analysis = await _analyzer.AnalyzeCrashAsync(
            bucketId, exceptionCode, exceptionName,
            keyFrames, allFrames, crashCount, ct).ConfigureAwait(false);

        workflowResult.Analysis = analysis;

        // Update analysis record
        _aiRepo.UpsertAnalysis(new AIAnalysisRow
        {
            AnalysisId = analysisId,
            BucketId = bucketId,
            RootCause = analysis.RootCause,
            Confidence = analysis.Confidence,
            SuggestedFix = analysis.FixedCode,
            AffectedFile = analysis.AffectedFile,
            AffectedFunction = analysis.AffectedFunction,
            Status = analysis.Status,
            ErrorMessage = analysis.ErrorMessage,
            PromptTokens = analysis.PromptTokens,
            ResponseTokens = analysis.ResponseTokens,
            CompletedAtUtc = DateTime.UtcNow.ToString("o")
        });

        if (analysis.Status == "Failed")
        {
            System.Console.WriteLine($"[AutoFix] Analysis failed: {analysis.ErrorMessage}");
            workflowResult.Status = "AnalysisFailed";
            return workflowResult;
        }

        System.Console.WriteLine($"[AutoFix] Analysis complete. Confidence: {analysis.Confidence:P0}");
        System.Console.WriteLine($"[AutoFix] Root cause: {analysis.RootCause?[..Math.Min(100, analysis.RootCause?.Length ?? 0)]}...");

        // ── Step 3: Check if we should auto-fix ─────────────────────────────
        if (analysis.Confidence < 0.5 || string.IsNullOrWhiteSpace(analysis.FixedCode))
        {
            System.Console.WriteLine("[AutoFix] Low confidence or no fix generated. Marking for manual review.");
            _aiRepo.UpsertAnalysis(new AIAnalysisRow
            {
                AnalysisId = analysisId,
                BucketId = bucketId,
                RootCause = analysis.RootCause,
                Confidence = analysis.Confidence,
                SuggestedFix = analysis.FixedCode,
                AffectedFile = analysis.AffectedFile,
                AffectedFunction = analysis.AffectedFunction,
                Status = "ManualReview",
                PromptTokens = analysis.PromptTokens,
                ResponseTokens = analysis.ResponseTokens,
                CompletedAtUtc = DateTime.UtcNow.ToString("o")
            });
            workflowResult.Status = "ManualReview";
            return workflowResult;
        }

        // ── Step 4: Create GitHub branch and PR ─────────────────────────────
        if (_github is null)
        {
            System.Console.WriteLine("[AutoFix] GitHub client not configured. Fix generated but PR not created.");
            workflowResult.Status = "FixGenerated";

            // Save fix locally
            if (analysis.AffectedFile is not null && analysis.FixedCode is not null)
            {
                SaveFixLocally(analysis.AffectedFile, analysis.FixedCode, bucketId);
            }

            // Store a fix record without PR info
            var fixId = $"FIX-{Guid.NewGuid():N}"[..24];
            _aiRepo.UpsertFix(new AIFixRow
            {
                FixId = fixId,
                AnalysisId = analysisId,
                BucketId = bucketId,
                PRStatus = "NoPR",
                FixDescription = analysis.FixDescription,
                FilesChanged = analysis.AffectedFile is not null
                    ? JsonSerializer.Serialize(new[] { analysis.AffectedFile })
                    : null
            });
            workflowResult.FixId = fixId;
            return workflowResult;
        }

        try
        {
            var fixId = $"FIX-{Guid.NewGuid():N}"[..24];
            var branchName = $"ai-fix/{bucketId[..12].ToLowerInvariant()}";
            var shortBucketId = bucketId[..12];

            System.Console.WriteLine($"[AutoFix] Creating branch: {branchName}");

            // Get main branch SHA
            var mainSha = await _github.GetBranchShaAsync("main", ct).ConfigureAwait(false);
            if (mainSha is null)
            {
                System.Console.WriteLine("[AutoFix] ERROR: Could not get main branch SHA");
                workflowResult.Status = "GitHubError";
                _aiRepo.UpsertFix(new AIFixRow
                {
                    FixId = fixId,
                    AnalysisId = analysisId,
                    BucketId = bucketId,
                    BranchName = branchName,
                    PRStatus = "Failed",
                    ErrorMessage = "Could not get main branch SHA"
                });
                return workflowResult;
            }

            // Create branch
            var branchCreated = await _github.CreateBranchAsync(branchName, mainSha, ct).ConfigureAwait(false);
            if (!branchCreated)
            {
                System.Console.WriteLine("[AutoFix] WARN: Branch may already exist, continuing...");
            }

            // Commit the fix — prepend the repo-relative path so GitHub API finds the existing file
            var affectedRelative = NormalizeGitHubPath(analysis.AffectedFile ?? "fix.cs");
            var filePath = $"{_repoPathPrefix}/{affectedRelative}";
            var commitMessage = $"[AI-Fix] {exceptionName ?? exceptionCode}: {analysis.FixDescription?[..Math.Min(60, analysis.FixDescription?.Length ?? 0)]}";

            System.Console.WriteLine($"[AutoFix] Committing fix to: {filePath}");
            var commitSha = await _github.CreateOrUpdateFileAsync(
                branchName, filePath, analysis.FixedCode!, commitMessage, ct).ConfigureAwait(false);

            // Create PR
            var prTitle = $"[AI-Fix] {exceptionName ?? exceptionCode} in {analysis.AffectedFunction ?? "Unknown"}";
            var prBody = BuildPRBody(analysis, bucketId, crashCount, keyFrames);

            System.Console.WriteLine("[AutoFix] Creating Pull Request...");
            var pr = await _github.CreatePullRequestAsync(
                prTitle, prBody, branchName, "main", ct).ConfigureAwait(false);

            // Store fix record
            _aiRepo.UpsertFix(new AIFixRow
            {
                FixId = fixId,
                AnalysisId = analysisId,
                BucketId = bucketId,
                BranchName = branchName,
                CommitSha = commitSha,
                PRNumber = pr?.Number ?? 0,
                PRUrl = pr?.Url,
                PRTitle = pr?.Title ?? prTitle,
                PRStatus = pr is not null ? "Open" : "Failed",
                FixDescription = analysis.FixDescription,
                FilesChanged = JsonSerializer.Serialize(new[] { filePath }),
                ErrorMessage = pr is null ? "Failed to create PR" : null
            });

            workflowResult.FixId = fixId;
            workflowResult.PRUrl = pr?.Url;
            workflowResult.PRNumber = pr?.Number ?? 0;
            workflowResult.Status = pr is not null ? "PRCreated" : "CommitOnly";

            System.Console.WriteLine(pr is not null
                ? $"[AutoFix] PR #{pr.Number} created: {pr.Url}"
                : "[AutoFix] Commit succeeded but PR creation failed.");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[AutoFix] GitHub error: {ex.Message}");
            workflowResult.Status = "GitHubError";
        }

        return workflowResult;
    }

    /// <summary>
    /// Saves the fix locally when GitHub is not configured.
    /// </summary>
    private void SaveFixLocally(string affectedFile, string fixedCode, string bucketId)
    {
        var fixDir = Path.Combine(_sourceCodeRoot, "..", "data", "ai-fixes", bucketId[..12]);
        Directory.CreateDirectory(fixDir);

        var fixPath = Path.Combine(fixDir, Path.GetFileName(affectedFile));
        File.WriteAllText(fixPath, fixedCode);
        System.Console.WriteLine($"[AutoFix] Fix saved locally: {fixPath}");
    }

    private static string NormalizeGitHubPath(string path)
    {
        // Convert Windows paths to GitHub-style paths
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static string BuildPRBody(
        AnalysisResult analysis, string bucketId, int crashCount,
        IReadOnlyList<string> keyFrames)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## AI-Generated Crash Fix");
        sb.AppendLine();
        sb.AppendLine($"**Bucket ID:** `{bucketId}`");
        sb.AppendLine($"**Exception:** {analysis.ExceptionCode} ({analysis.ExceptionName})");
        sb.AppendLine($"**Affected Crashes:** {crashCount}");
        sb.AppendLine($"**AI Confidence:** {analysis.Confidence:P0}");
        sb.AppendLine();

        sb.AppendLine("### Root Cause Analysis");
        sb.AppendLine(analysis.RootCause);
        sb.AppendLine();

        sb.AppendLine("### Fix Description");
        sb.AppendLine(analysis.FixDescription);
        sb.AppendLine();

        sb.AppendLine("### Key Stack Frames");
        foreach (var frame in keyFrames)
            sb.AppendLine($"- `{frame}`");
        sb.AppendLine();

        sb.AppendLine("### Affected File");
        sb.AppendLine($"- `{analysis.AffectedFile}`");
        sb.AppendLine($"- Function: `{analysis.AffectedFunction}`");
        sb.AppendLine();

        sb.AppendLine("---");
        sb.AppendLine("*This PR was automatically generated by the AI Crash Analysis system.*");
        sb.AppendLine("*Please review the changes carefully before merging.*");

        return sb.ToString();
    }
}

public sealed class WorkflowResult
{
    public string BucketId { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public AnalysisResult? Analysis { get; set; }
    public string? FixId { get; set; }
    public string? PRUrl { get; set; }
    public int PRNumber { get; set; }
}
