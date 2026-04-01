using System.Text.Json;
using CrashCollector.Console.Data;
using CrashCollector.AI;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// CORS for React dashboard
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// Register SQLite DB + repositories as singletons (POC – single connection is fine)
var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var dbPath = Path.Combine(solutionRoot, "data", "crashes.db");
builder.Services.AddSingleton(_ =>
{
    var db = new CrashDb(dbPath);
    db.EnsureCreated();
    return db;
});
builder.Services.AddSingleton(sp => new CrashRepository(sp.GetRequiredService<CrashDb>()));
builder.Services.AddSingleton(sp => new BucketRepository(sp.GetRequiredService<CrashDb>()));
builder.Services.AddSingleton(sp => new AIRepository(sp.GetRequiredService<CrashDb>()));

// ── LLM configuration (Howler-style multi-provider) ──
// Merge environment variables into the LlmSettings model configs at startup
var llmSection = builder.Configuration.GetSection("LlmSettings");
builder.Services.Configure<LlmSettings>(llmSection);

// Allow env-var overrides for API keys (keeps secrets out of appsettings)
builder.Services.PostConfigure<LlmSettings>(settings =>
{
    var envOpenAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    var envGeminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");

    if (!string.IsNullOrWhiteSpace(envOpenAiKey) && settings.Models.TryGetValue("gpt-4o", out var oai))
        oai.ApiKey = envOpenAiKey;

    if (!string.IsNullOrWhiteSpace(envGeminiKey) && settings.Models.TryGetValue("gemini-2.0-flash", out var gem))
        gem.ApiKey = envGeminiKey;
});

builder.Services.AddHttpClient<LlmClient>();

var sourceCodeRoot = Path.GetFullPath(Path.Combine(solutionRoot, "DellDigitalDelivery.App"));
builder.Services.AddSingleton<CrashAnalyzer>(sp =>
    new CrashAnalyzer(sp.GetRequiredService<LlmClient>(), sourceCodeRoot));

var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? "";
var githubRepo = Environment.GetEnvironmentVariable("GITHUB_REPO")
    ?? "reachouttonarendrakumar-collab/AI-Crash-Analysis-POC";
builder.Services.AddSingleton<GitHubClient?>(_ =>
    !string.IsNullOrWhiteSpace(githubToken)
        ? new GitHubClient(githubToken, githubRepo)
        : null);
builder.Services.AddSingleton<AutoFixWorkflow>(sp =>
    new AutoFixWorkflow(
        sp.GetRequiredService<CrashAnalyzer>(),
        sp.GetService<GitHubClient>(),
        sp.GetRequiredService<AIRepository>(),
        sourceCodeRoot));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

// =========================================================================
//  Minimal API endpoints
// =========================================================================

app.MapGet("/", () => Results.Ok(new
{
    name = "Crash Collector API",
    version = "2.0.0-poc",
    endpoints = new[]
    {
        "GET  /crashes",
        "GET  /crashes/{id}",
        "GET  /buckets",
        "GET  /buckets/{bucketId}",
        "GET  /ai/analyses",
        "GET  /ai/analysis/{bucketId}",
        "POST /ai/analyze/{bucketId}",
        "GET  /ai/fixes",
        "GET  /ai/fix/{fixId}",
        "POST /ai/fix/{bucketId}",
        "POST /auth/login",
        "GET  /auth/me",
    }
}));

// ═════════════════════════════════════════════════════════════════════════
//  CRASHES
// ═════════════════════════════════════════════════════════════════════════

app.MapGet("/crashes", (CrashRepository repo, int? limit) =>
{
    var rows = repo.GetAll(limit ?? 100);
    return Results.Ok(new { count = rows.Count, crashes = rows });
})
.WithName("GetCrashes");

app.MapGet("/crashes/{id}", (string id, CrashRepository crashRepo, BucketRepository bucketRepo, AIRepository aiRepo) =>
{
    var crash = crashRepo.GetById(id);
    if (crash is null)
        return Results.NotFound(new { error = $"Crash '{id}' not found." });

    var bucketId = bucketRepo.GetBucketForCrash(id);

    // Load stack frames from crash-dumps metadata
    List<string>? stackFrames = null;

    // Try the crash-dumps directory first (DellDigitalDelivery.App output)
    var crashDumpDirs = new[]
    {
        Path.Combine(solutionRoot, "..", "data", "crash-dumps", crash.CrashId),
        Path.Combine(solutionRoot, "data", "crash-dumps", crash.CrashId),
        crash.DumpPath is not null ? Path.GetDirectoryName(crash.DumpPath) : null,
        Path.Combine(solutionRoot, "data", "dumps", crash.CrashId),
    };

    foreach (var dir in crashDumpDirs)
    {
        if (dir is null || !Directory.Exists(dir)) continue;

        var metaPath = Path.Combine(dir, "metadata.json");
        if (!File.Exists(metaPath)) continue;

        try
        {
            var json = File.ReadAllText(metaPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("stackFrames", out var framesEl)
                && framesEl.ValueKind == JsonValueKind.Array)
            {
                stackFrames = framesEl.EnumerateArray()
                    .Select(e => e.GetString() ?? "")
                    .Where(s => s.Length > 0)
                    .ToList();
                if (stackFrames.Count > 0) break;
            }
        }
        catch { /* ignore */ }
    }

    // Also try extracted-metadata.json
    if (stackFrames is null || stackFrames.Count == 0)
    {
        foreach (var dir in crashDumpDirs)
        {
            if (dir is null) continue;
            var extractedPath = Path.Combine(dir, "extracted-metadata.json");
            if (!File.Exists(extractedPath)) continue;

            try
            {
                var json = File.ReadAllText(extractedPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("stackFrames", out var framesEl)
                    && framesEl.ValueKind == JsonValueKind.Array)
                {
                    stackFrames = framesEl.EnumerateArray()
                        .Select(e => e.GetString() ?? "")
                        .Where(s => s.Length > 0)
                        .ToList();
                    if (stackFrames.Count > 0) break;
                }
            }
            catch { /* ignore */ }
        }
    }

    BucketRow? bucketInfo = null;
    if (bucketId is not null)
    {
        var allBuckets = bucketRepo.GetAll(1000);
        bucketInfo = allBuckets.FirstOrDefault(b => b.BucketId == bucketId);
    }

    // Fall back to key frames
    if ((stackFrames is null || stackFrames.Count == 0) && bucketInfo is not null)
    {
        stackFrames = new List<string>();
        if (bucketInfo.KeyFrame1 is not null) stackFrames.Add(bucketInfo.KeyFrame1);
        if (bucketInfo.KeyFrame2 is not null) stackFrames.Add(bucketInfo.KeyFrame2);
        if (bucketInfo.KeyFrame3 is not null) stackFrames.Add(bucketInfo.KeyFrame3);
    }

    // Get AI analysis for this crash's bucket
    AIAnalysisRow? analysis = bucketId is not null ? aiRepo.GetAnalysisByBucket(bucketId) : null;
    AIFixRow? fix = bucketId is not null ? aiRepo.GetFixByBucket(bucketId) : null;

    return Results.Ok(new
    {
        crash,
        bucketId,
        bucket = bucketInfo,
        symbolicatedStack = stackFrames,
        aiAnalysis = analysis,
        aiFix = fix
    });
})
.WithName("GetCrashById");

// ═════════════════════════════════════════════════════════════════════════
//  BUCKETS
// ═════════════════════════════════════════════════════════════════════════

app.MapGet("/buckets", (BucketRepository repo, AIRepository aiRepo, int? limit) =>
{
    var rows = repo.GetAll(limit ?? 100);

    // Enrich with AI analysis status
    var enriched = rows.Select(b =>
    {
        var analysis = aiRepo.GetAnalysisByBucket(b.BucketId);
        var fix = aiRepo.GetFixByBucket(b.BucketId);
        return new
        {
            b.BucketId, b.ExceptionCode, b.KeyFrame1, b.KeyFrame2, b.KeyFrame3,
            b.CrashCount, b.FirstSeenUtc, b.LastSeenUtc,
            aiStatus = analysis?.Status,
            aiConfidence = analysis?.Confidence,
            fixStatus = fix?.PRStatus,
            prUrl = fix?.PRUrl
        };
    }).ToList();

    return Results.Ok(new { count = rows.Count, buckets = enriched });
})
.WithName("GetBuckets");

app.MapGet("/buckets/{bucketId}", (string bucketId, BucketRepository bucketRepo, CrashRepository crashRepo, AIRepository aiRepo) =>
{
    var allBuckets = bucketRepo.GetAll(1000);
    var bucket = allBuckets.FirstOrDefault(b => b.BucketId == bucketId);

    if (bucket is null)
        return Results.NotFound(new { error = $"Bucket '{bucketId}' not found." });

    var crashIds = bucketRepo.GetCrashIdsForBucket(bucketId);
    var crashes = crashIds
        .Select(id => crashRepo.GetById(id))
        .Where(c => c is not null)
        .ToList();

    var keyFrames = new List<string>();
    if (bucket.KeyFrame1 is not null) keyFrames.Add(bucket.KeyFrame1);
    if (bucket.KeyFrame2 is not null) keyFrames.Add(bucket.KeyFrame2);
    if (bucket.KeyFrame3 is not null) keyFrames.Add(bucket.KeyFrame3);

    var analysis = aiRepo.GetAnalysisByBucket(bucketId);
    var fix = aiRepo.GetFixByBucket(bucketId);

    return Results.Ok(new
    {
        bucket,
        keyFrames,
        crashes,
        aiAnalysis = analysis,
        aiFix = fix
    });
})
.WithName("GetBucketById");

// ═════════════════════════════════════════════════════════════════════════
//  AI ANALYSIS
// ═════════════════════════════════════════════════════════════════════════

app.MapGet("/ai/analyses", (AIRepository aiRepo, int? limit) =>
{
    var rows = aiRepo.GetAllAnalyses(limit ?? 100);
    return Results.Ok(new { count = rows.Count, analyses = rows });
})
.WithName("GetAIAnalyses");

app.MapGet("/ai/analysis/{bucketId}", (string bucketId, AIRepository aiRepo) =>
{
    var analysis = aiRepo.GetAnalysisByBucket(bucketId);
    if (analysis is null)
        return Results.NotFound(new { error = $"No AI analysis for bucket '{bucketId}'." });
    return Results.Ok(analysis);
})
.WithName("GetAIAnalysisByBucket");

app.MapPost("/ai/analyze/{bucketId}", async (
    string bucketId,
    BucketRepository bucketRepo,
    AutoFixWorkflow workflow,
    CancellationToken ct) =>
{
    var allBuckets = bucketRepo.GetAll(1000);
    var bucket = allBuckets.FirstOrDefault(b => b.BucketId == bucketId);
    if (bucket is null)
        return Results.NotFound(new { error = $"Bucket '{bucketId}' not found." });

    var keyFrames = new List<string>();
    if (bucket.KeyFrame1 is not null) keyFrames.Add(bucket.KeyFrame1);
    if (bucket.KeyFrame2 is not null) keyFrames.Add(bucket.KeyFrame2);
    if (bucket.KeyFrame3 is not null) keyFrames.Add(bucket.KeyFrame3);

    var result = await workflow.ExecuteAsync(
        bucketId, bucket.ExceptionCode, null,
        keyFrames, keyFrames, bucket.CrashCount, ct);

    return Results.Ok(new
    {
        bucketId,
        status = result.Status,
        rootCause = result.Analysis?.RootCause,
        confidence = result.Analysis?.Confidence,
        fixId = result.FixId,
        prUrl = result.PRUrl,
        prNumber = result.PRNumber
    });
})
.WithName("TriggerAIAnalysis");

// ═════════════════════════════════════════════════════════════════════════
//  AI FIXES
// ═════════════════════════════════════════════════════════════════════════

app.MapGet("/ai/fixes", (AIRepository aiRepo, int? limit) =>
{
    var rows = aiRepo.GetAllFixes(limit ?? 100);
    return Results.Ok(new { count = rows.Count, fixes = rows });
})
.WithName("GetAIFixes");

app.MapGet("/ai/fix/{fixId}", (string fixId, AIRepository aiRepo) =>
{
    var fix = aiRepo.GetFixById(fixId);
    if (fix is null)
        return Results.NotFound(new { error = $"Fix '{fixId}' not found." });
    return Results.Ok(fix);
})
.WithName("GetAIFixById");

app.MapPost("/ai/fix/{bucketId}", async (
    string bucketId,
    BucketRepository bucketRepo,
    AutoFixWorkflow workflow,
    CancellationToken ct) =>
{
    var allBuckets = bucketRepo.GetAll(1000);
    var bucket = allBuckets.FirstOrDefault(b => b.BucketId == bucketId);
    if (bucket is null)
        return Results.NotFound(new { error = $"Bucket '{bucketId}' not found." });

    var keyFrames = new List<string>();
    if (bucket.KeyFrame1 is not null) keyFrames.Add(bucket.KeyFrame1);
    if (bucket.KeyFrame2 is not null) keyFrames.Add(bucket.KeyFrame2);
    if (bucket.KeyFrame3 is not null) keyFrames.Add(bucket.KeyFrame3);

    var result = await workflow.ExecuteAsync(
        bucketId, bucket.ExceptionCode, null,
        keyFrames, keyFrames, bucket.CrashCount, ct);

    return Results.Ok(new
    {
        fixId = result.FixId,
        status = result.Status,
        prUrl = result.PRUrl,
        prNumber = result.PRNumber
    });
})
.WithName("TriggerAIFix");

// ═════════════════════════════════════════════════════════════════════════
//  AUTH (mock for POC)
// ═════════════════════════════════════════════════════════════════════════

app.MapPost("/auth/login", (HttpContext ctx) =>
{
    // Mock login — accepts any credentials and returns a token
    var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    return Results.Ok(new
    {
        token,
        user = new
        {
            id = "dev-001",
            name = "Developer",
            email = "developer@dell.com",
            role = "developer",
            application = "DellDigitalDelivery.App"
        }
    });
})
.WithName("Login");

app.MapGet("/auth/me", (HttpContext ctx) =>
{
    // Mock auth check — always returns the dev user
    var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
    if (string.IsNullOrWhiteSpace(authHeader))
        return Results.Unauthorized();

    return Results.Ok(new
    {
        id = "dev-001",
        name = "Developer",
        email = "developer@dell.com",
        role = "developer",
        application = "DellDigitalDelivery.App"
    });
})
.WithName("GetCurrentUser");

app.Run();
