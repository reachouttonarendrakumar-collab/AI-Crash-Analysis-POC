using System.Text.Json;
using CrashCollector.Console.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Register SQLite DB + repositories as singletons (POC – single connection is fine)
// Resolve the DB path relative to the solution root (one level up from the Api project)
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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// =========================================================================
//  Minimal API endpoints
// =========================================================================

app.MapGet("/", () => Results.Ok(new
{
    name = "Crash Collector API",
    version = "1.0.0-poc",
    endpoints = new[]
    {
        "GET /crashes",
        "GET /crashes/{id}",
        "GET /buckets",
        "GET /buckets/{bucketId}",
    }
}));

// ── GET /crashes ─────────────────────────────────────────────────────────
app.MapGet("/crashes", (CrashRepository repo, int? limit) =>
{
    var rows = repo.GetAll(limit ?? 100);
    return Results.Ok(new
    {
        count = rows.Count,
        crashes = rows
    });
})
.WithName("GetCrashes")
.WithDescription("Returns all crashes ordered by timestamp descending.");

// ── GET /crashes/{id} ────────────────────────────────────────────────────
app.MapGet("/crashes/{id}", (string id, CrashRepository crashRepo, BucketRepository bucketRepo) =>
{
    var crash = crashRepo.GetById(id);
    if (crash is null)
        return Results.NotFound(new { error = $"Crash '{id}' not found." });

    var bucketId = bucketRepo.GetBucketForCrash(id);

    // Try to load the symbolicated stack trace from the metadata JSON on disk
    List<string>? stackFrames = null;
    var metadataJsonPath = crash.DumpPath is not null
        ? Path.Combine(Path.GetDirectoryName(crash.DumpPath)!, "metadata.json")
        : null;

    if (metadataJsonPath is not null && File.Exists(metadataJsonPath))
    {
        try
        {
            var json = File.ReadAllText(metadataJsonPath);
            using var doc = JsonDocument.Parse(json);
            // The metadata.json written by LocalDumpStore may not have stackFrames,
            // so we also check the extracted metadata JSON if present.
        }
        catch { /* ignore parse errors */ }
    }

    // Also try reading from the dump-level extracted metadata
    var dumpDir = crash.DumpPath is not null ? Path.GetDirectoryName(crash.DumpPath) : null;
    if (dumpDir is null || !Directory.Exists(dumpDir))
    {
        // Fallback: look up by CrashId in standard layout (relative to solution root)
        dumpDir = Path.Combine(solutionRoot, "data", "dumps", crash.CrashId);
    }

    var extractedMetaPath = Path.Combine(dumpDir!, "extracted-metadata.json");
    if (File.Exists(extractedMetaPath))
    {
        try
        {
            var json = File.ReadAllText(extractedMetaPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("stackFrames", out var framesEl)
                && framesEl.ValueKind == JsonValueKind.Array)
            {
                stackFrames = framesEl.EnumerateArray()
                    .Select(e => e.GetString() ?? "")
                    .Where(s => s.Length > 0)
                    .ToList();
            }
        }
        catch { /* ignore */ }
    }

    // Also check buckets.json for key frames as fallback symbolicated trace
    BucketRow? bucketInfo = null;
    if (bucketId is not null)
    {
        var allBuckets = bucketRepo.GetAll(1000);
        bucketInfo = allBuckets.FirstOrDefault(b => b.BucketId == bucketId);
    }

    // Build symbolicated stack from key frames if we don't have full frames
    if (stackFrames is null && bucketInfo is not null)
    {
        stackFrames = new List<string>();
        if (bucketInfo.KeyFrame1 is not null) stackFrames.Add(bucketInfo.KeyFrame1);
        if (bucketInfo.KeyFrame2 is not null) stackFrames.Add(bucketInfo.KeyFrame2);
        if (bucketInfo.KeyFrame3 is not null) stackFrames.Add(bucketInfo.KeyFrame3);
    }

    return Results.Ok(new
    {
        crash,
        bucketId,
        bucket = bucketInfo,
        symbolicatedStack = stackFrames
    });
})
.WithName("GetCrashById")
.WithDescription("Returns a single crash with bucket info and symbolicated stack trace.");

// ── GET /buckets ─────────────────────────────────────────────────────────
app.MapGet("/buckets", (BucketRepository repo, int? limit) =>
{
    var rows = repo.GetAll(limit ?? 100);
    return Results.Ok(new
    {
        count = rows.Count,
        buckets = rows
    });
})
.WithName("GetBuckets")
.WithDescription("Returns all buckets ordered by crash count descending.");

// ── GET /buckets/{bucketId} ──────────────────────────────────────────────
app.MapGet("/buckets/{bucketId}", (string bucketId, BucketRepository bucketRepo, CrashRepository crashRepo) =>
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

    return Results.Ok(new
    {
        bucket,
        keyFrames,
        crashes
    });
})
.WithName("GetBucketById")
.WithDescription("Returns a single bucket with its key frames and member crashes.");

app.Run();
