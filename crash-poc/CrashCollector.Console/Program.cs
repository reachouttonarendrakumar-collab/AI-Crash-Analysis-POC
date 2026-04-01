using System.Diagnostics;
using System.Text.Json;
using CrashCollector.Console;
using CrashCollector.Console.Data;
using CrashCollector.Console.Models;

// ═════════════════════════════════════════════════════════════════════════════
//  Crash Collector POC – 7-Step Demo Flow
// ═════════════════════════════════════════════════════════════════════════════

const string TargetExe   = "DELL.DIGITAL.DELIVERY.SERVICE.SUBAGENT.EXE";
const string D3BuildRoot = @"C:\Users\Narendra_K_Meena\source\repos\D3\d3-client-nga\src";
const string ApiBaseUrl  = "http://localhost:5100";
const int    MaxCrashes  = 20;

var totalSw = Stopwatch.StartNew();
var jsonOpts = new JsonSerializerOptions { WriteIndented = true };

Log("=", "Crash Collector Console - POC Demo");
Log("i", $"Target: {TargetExe}");
Log("i", $"Symbol source: {D3BuildRoot}");
Log("i", $"API base URL: {ApiBaseUrl}");
Console.WriteLine();

// ── Bootstrap services ──────────────────────────────────────────────────
var tenantId     = Environment.GetEnvironmentVariable("WER_TENANT_ID") ?? "";
var clientId     = Environment.GetEnvironmentVariable("WER_CLIENT_ID") ?? "";
var clientSecret = Environment.GetEnvironmentVariable("WER_CLIENT_SECRET") ?? "";

using var http = new HttpClient();
IWerApiClient wer = new WerApiClient(http,
    tenantId: tenantId, clientId: clientId,
    clientSecret: clientSecret, allowMockFallback: true);

ILocalDumpStore store = new LocalDumpStore();

using var db = new CrashDb();
db.EnsureCreated();
var crashRepo  = new CrashRepository(db);
var bucketRepo = new BucketRepository(db);
Log("+", $"SQLite ready: {Path.GetFullPath(Path.Combine(".", "data", "crashes.db"))}");
Console.WriteLine();

// ═════════════════════════════════════════════════════════════════════════════
//  STEP 1 – Fetch crashes from WER
// ═════════════════════════════════════════════════════════════════════════════
StepHeader(1, "Fetch crashes from WER");
var sw = Stopwatch.StartNew();

var crashes = await wer.GetCrashesAsync(TargetExe, maxResults: MaxCrashes);

sw.Stop();
Log("+", $"Retrieved {crashes.Count} crash report(s) in {sw.ElapsedMilliseconds}ms");

foreach (var c in crashes.Take(5))
    Log(" ", $"  {c.CrashId}  {c.AppVersion,-14} {c.Timestamp:yyyy-MM-dd HH:mm}  {c.FailureBucket ?? "n/a"}");
if (crashes.Count > 5)
    Log(" ", $"  ... and {crashes.Count - 5} more");

StepFooter(1, sw);

// ═════════════════════════════════════════════════════════════════════════════
//  STEP 2 – Download dumps
// ═════════════════════════════════════════════════════════════════════════════
StepHeader(2, "Download dumps");
sw = Stopwatch.StartNew();

int dlNew = 0, dlSkip = 0, dlMeta = 0, dlErr = 0;
var storeResults = new Dictionary<string, DumpStoreResult>();

foreach (var crash in crashes)
{
    var r = await store.StoreDumpAsync(crash, http);
    storeResults[crash.CrashId] = r;

    if (r.Error is not null)      { Log("!", $"ERROR  {crash.CrashId}: {r.Error}"); dlErr++; }
    else if (r.AlreadyExisted)    { Log(" ", $"SKIP   {crash.CrashId}"); dlSkip++; }
    else if (r.Downloaded)        { Log("+", $"SAVED  {crash.CrashId}  [{r.FileSizeBytes:#,0} B]"); dlNew++; }
    else                          { Log(" ", $"META   {crash.CrashId}  (no dump URL)"); dlMeta++; }
}

sw.Stop();
Log("+", $"downloaded={dlNew}  skipped={dlSkip}  meta-only={dlMeta}  errors={dlErr}");
StepFooter(2, sw);

// ═════════════════════════════════════════════════════════════════════════════
//  STEP 3 – Extract metadata
// ═════════════════════════════════════════════════════════════════════════════
StepHeader(3, "Extract metadata");
sw = Stopwatch.StartNew();

var extractor   = new DumpMetadataExtractor();
var allMetadata = new List<CrashMetadata>();

foreach (var crash in crashes)
{
    var dumpDir  = Path.Combine(".", "data", "dumps", crash.CrashId);
    var dumpFile = Path.Combine(dumpDir, "dump.cab");

    if (!File.Exists(dumpFile))
    {
        Log(" ", $"SKIP   {crash.CrashId} (no dump)");
        continue;
    }

    var meta = await extractor.ExtractAsync(crash.CrashId, dumpFile);
    allMetadata.Add(meta);

    // Persist extracted metadata JSON (includes stackFrames) for the API
    var extractedPath = Path.Combine(dumpDir, "extracted-metadata.json");
    await File.WriteAllTextAsync(extractedPath, JsonSerializer.Serialize(meta, jsonOpts));

    Log(meta.Error is not null ? "!" : "+",
        $"{(meta.Error is not null ? "WARN" : "OK")}     {crash.CrashId}  " +
        $"exc={meta.ExceptionCode ?? "n/a"}  mod={meta.FaultingModule ?? "n/a"}  threads={meta.ThreadCount}  " +
        $"frames={meta.StackFrames.Count}  via={meta.ExtractionMethod}");
}

// Persist to SQLite
var metaLookup = allMetadata.ToDictionary(m => m.CrashId);
foreach (var crash in crashes)
{
    metaLookup.TryGetValue(crash.CrashId, out var meta);
    storeResults.TryGetValue(crash.CrashId, out var sr);
    crashRepo.Upsert(crash, meta, sr);
}

sw.Stop();
Log("+", $"{allMetadata.Count} dump(s) analysed, {crashes.Count} crash(es) persisted to SQLite");
StepFooter(3, sw);

// ═════════════════════════════════════════════════════════════════════════════
//  STEP 4 – Bucket crashes
// ═════════════════════════════════════════════════════════════════════════════
StepHeader(4, "Bucket crashes by stack signature");
sw = Stopwatch.StartNew();

var bucketizer  = new Bucketizer();
var bucketItems = allMetadata
    .Where(m => m.StackFrames.Count > 0)
    .Select(m => (m, m.StackFrames))
    .ToList();

var buckets = await bucketizer.BucketiseAndPersistAsync(bucketItems);

foreach (var b in buckets.Values.OrderByDescending(b => b.CrashIds.Count))
{
    Log("+", $"[{b.BucketId[..12]}...] {b.ExceptionCode ?? "?"} | {b.CrashIds.Count} crash(es)");
    Log(" ", $"    key: {string.Join(" -> ", b.KeyFrames)}");
}

// Persist to SQLite
foreach (var b in buckets.Values)
    bucketRepo.Upsert(b);

sw.Stop();
Log("+", $"{buckets.Count} bucket(s) from {bucketItems.Count} crash(es). Saved to SQLite + ./data/buckets.json");
StepFooter(4, sw);

// ═════════════════════════════════════════════════════════════════════════════
//  STEP 5 – Publish symbols
// ═════════════════════════════════════════════════════════════════════════════
StepHeader(5, "Publish symbols from build output");
sw = Stopwatch.StartNew();

var symServer = new LocalSymbolServer();
var symResult = await symServer.PublishSymbolsFromBuildOutputAsync(D3BuildRoot);

if (symResult.Error is not null)
{
    Log("!", $"ERROR: {symResult.Error}");
}
else
{
    Log("+", $"Found {symResult.TotalPdbsFound} PDB(s)");
    Log("+", $"Published: {symResult.Published}  Already present: {symResult.AlreadyPresent}  " +
             $"Skipped: {symResult.Skipped}  Errors: {symResult.Errors}");

    foreach (var e in symResult.Entries.Where(e => !e.AlreadyExisted).Take(10))
        Log(" ", $"  NEW  {e.PdbName}/{e.GuidAge}  ({e.FileSizeBytes:#,0} B)");
    if (symResult.Published > 10)
        Log(" ", $"  ... and {symResult.Published - 10} more");
    if (symResult.AlreadyPresent > 0)
        Log(" ", $"  ({symResult.AlreadyPresent} already in store)");

    foreach (var w in symResult.Warnings.Take(3))
        Log("!", $"  WARN: {w}");
}

sw.Stop();
Log("+", $"Symbol path: {symServer.GetSymbolPath()}");
StepFooter(5, sw);

// ═════════════════════════════════════════════════════════════════════════════
//  STEP 6 – Symbolicate one crash
// ═════════════════════════════════════════════════════════════════════════════
StepHeader(6, "Symbolicate one representative crash");
sw = Stopwatch.StartNew();

// Pick the bucket with the most crashes, take its first crash
var topBucket = buckets.Values.OrderByDescending(b => b.CrashIds.Count).FirstOrDefault();
CrashMetadata? demoCrashMeta = null;
StackSignature? demoSig = null;
string? demoCrashId = null;

if (topBucket is not null)
{
    demoCrashId = topBucket.CrashIds.First();
    demoCrashMeta = metaLookup.GetValueOrDefault(demoCrashId);

    if (demoCrashMeta is not null)
    {
        // Compute the full stack signature (normalised + key frames)
        demoSig = bucketizer.ComputeSignature(demoCrashMeta, demoCrashMeta.StackFrames);

        Log("i", $"Crash:     {demoCrashId}");
        Log("i", $"Exception: {demoCrashMeta.ExceptionCode} ({demoCrashMeta.ExceptionName})");
        Log("i", $"Module:    {demoCrashMeta.FaultingModule}");
        Log("i", $"Threads:   {demoCrashMeta.ThreadCount}");
        Log("i", $"Bucket:    {demoSig.BucketId}");
        Log("i", $"Method:    {demoCrashMeta.ExtractionMethod}");
        Console.WriteLine();

        Log("i", "Symbolicated stack trace:");
        for (int i = 0; i < demoCrashMeta.StackFrames.Count; i++)
        {
            var raw   = demoCrashMeta.StackFrames[i];
            var norm  = Bucketizer.NormalizeFrame(raw);
            var isKey = demoSig.KeyFrames.Contains(norm);
            var tag   = isKey ? " <<< KEY" : "";
            Log(" ", $"  #{i:D2}  {raw}{tag}");
        }
        Console.WriteLine();

        Log("i", "Key frames (bucket identity):");
        foreach (var kf in demoSig.KeyFrames)
            Log("+", $"  {kf}");

        // Also try WinDbg if available
        if (WinDbgRunner.IsAvailable)
        {
            Log("i", $"WinDbg available at {WinDbgRunner.CdbExePath}");
            var dumpFile = Path.Combine(".", "data", "dumps", demoCrashId, "dump.cab");
            if (File.Exists(dumpFile))
            {
                var windbg = new WinDbgRunner(symbolPath: symServer.GetSymbolPath());
                var (dbgResult, reportPath) = await windbg.AnalyseAndSaveAsync(dumpFile);
                if (dbgResult.Success)
                    Log("+", $"WinDbg report saved: {reportPath} ({dbgResult.ElapsedMs}ms)");
                else
                    Log("!", $"WinDbg: {dbgResult.Error}");
            }
        }
        else
        {
            Log(" ", "WinDbg (cdb.exe) not installed -- skipping live analysis");
        }
    }
    else
    {
        Log("!", $"No extracted metadata for crash {demoCrashId}");
    }
}
else
{
    Log("!", "No buckets available -- nothing to symbolicate");
}

sw.Stop();
StepFooter(6, sw);

// ═════════════════════════════════════════════════════════════════════════════
//  STEP 7 – Print results to console and API
// ═════════════════════════════════════════════════════════════════════════════
StepHeader(7, "Results summary + API verification");
sw = Stopwatch.StartNew();

// ── Console summary ──────────────────────────────────────────────────────
Console.WriteLine();
Log("=", "DATABASE SUMMARY");
Log("i", $"  Crashes in DB: {crashRepo.Count()}");
Log("i", $"  Buckets in DB: {bucketRepo.Count()}");
Console.WriteLine();

Log("=", "TOP BUCKETS");
var dbBuckets = bucketRepo.GetAll(limit: 10);
foreach (var b in dbBuckets)
{
    var members = bucketRepo.GetCrashIdsForBucket(b.BucketId);
    Log("i", $"  [{b.BucketId[..12]}...] {b.ExceptionCode ?? "?",-14} | " +
             $"{b.CrashCount} crash(es) | {b.KeyFrame1}");
}
Console.WriteLine();

if (demoCrashMeta is not null && demoSig is not null)
{
    Log("=", "SYMBOLICATED CRASH DETAIL");
    Log("i", $"  Crash ID:      {demoCrashId}");
    Log("i", $"  Exception:     {demoCrashMeta.ExceptionCode} ({demoCrashMeta.ExceptionName})");
    Log("i", $"  Module:        {demoCrashMeta.FaultingModule}");
    Log("i", $"  Bucket:        {demoSig.BucketId}");
    Log("i", $"  Key frames:    {string.Join(" -> ", demoSig.KeyFrames)}");
    Log("i", $"  Total frames:  {demoCrashMeta.StackFrames.Count}");
    Log("i", $"  Threads:       {demoCrashMeta.ThreadCount}");
    Console.WriteLine();
}

// ── API verification ─────────────────────────────────────────────────────
Log("=", "API ENDPOINTS (start API with: dotnet run --project CrashCollector.Api --urls http://localhost:5100)");
Console.WriteLine();

var apiAvailable = false;
try
{
    var probe = await http.GetAsync($"{ApiBaseUrl}/");
    apiAvailable = probe.IsSuccessStatusCode;
}
catch { /* API not running */ }

if (apiAvailable)
{
    Log("+", "API is running! Querying endpoints...");
    Console.WriteLine();

    // GET /crashes
    try
    {
        var json = await http.GetStringAsync($"{ApiBaseUrl}/crashes?limit=3");
        var doc  = JsonDocument.Parse(json);
        var cnt  = doc.RootElement.GetProperty("count").GetInt32();
        Log("+", $"GET /crashes?limit=3 --> {cnt} crash(es) returned");
    }
    catch (Exception ex) { Log("!", $"GET /crashes failed: {ex.Message}"); }

    // GET /crashes/{id}
    if (demoCrashId is not null)
    {
        try
        {
            var json = await http.GetStringAsync($"{ApiBaseUrl}/crashes/{demoCrashId}");
            var doc  = JsonDocument.Parse(json);
            var hasBucket = doc.RootElement.TryGetProperty("bucketId", out var bid) &&
                            bid.ValueKind != JsonValueKind.Null;
            var hasStack  = doc.RootElement.TryGetProperty("symbolicatedStack", out var st) &&
                            st.ValueKind == JsonValueKind.Array && st.GetArrayLength() > 0;
            Log("+", $"GET /crashes/{demoCrashId}");
            Log(" ", $"    bucketId: {(hasBucket ? bid.GetString() : "n/a")}");
            Log(" ", $"    symbolicatedStack: {(hasStack ? $"{st.GetArrayLength()} frame(s)" : "n/a")}");
        }
        catch (Exception ex) { Log("!", $"GET /crashes/{{id}} failed: {ex.Message}"); }
    }

    // GET /buckets
    try
    {
        var json = await http.GetStringAsync($"{ApiBaseUrl}/buckets");
        var doc  = JsonDocument.Parse(json);
        var cnt  = doc.RootElement.GetProperty("count").GetInt32();
        Log("+", $"GET /buckets --> {cnt} bucket(s) returned");
    }
    catch (Exception ex) { Log("!", $"GET /buckets failed: {ex.Message}"); }

    // GET /buckets/{id}
    if (topBucket is not null)
    {
        try
        {
            var json = await http.GetStringAsync($"{ApiBaseUrl}/buckets/{topBucket.BucketId}");
            var doc  = JsonDocument.Parse(json);
            var numC = doc.RootElement.GetProperty("crashes").GetArrayLength();
            Log("+", $"GET /buckets/{topBucket.BucketId}");
            Log(" ", $"    {numC} crash(es) in bucket");
        }
        catch (Exception ex) { Log("!", $"GET /buckets/{{id}} failed: {ex.Message}"); }
    }
}
else
{
    Log(" ", "API is not running. To test the endpoints, start the API in another terminal:");
    Log(" ", $"  dotnet run --project CrashCollector.Api --urls {ApiBaseUrl}");
    Console.WriteLine();
    Log(" ", "Then query:");
    Log(" ", $"  curl {ApiBaseUrl}/crashes");
    Log(" ", $"  curl {ApiBaseUrl}/crashes/{demoCrashId ?? "<crashId>"}");
    Log(" ", $"  curl {ApiBaseUrl}/buckets");
    Log(" ", $"  curl {ApiBaseUrl}/buckets/{topBucket?.BucketId ?? "<bucketId>"}");
}

sw.Stop();
StepFooter(7, sw);

// ── Done ─────────────────────────────────────────────────────────────────
totalSw.Stop();
Console.WriteLine();
Console.WriteLine(new string('=', 100));
Log("=", $"All 7 steps complete in {totalSw.Elapsed.TotalSeconds:F1}s");
Log("i", $"  DB:      {Path.GetFullPath(Path.Combine(".", "data", "crashes.db"))}");
Log("i", $"  Dumps:   {Path.GetFullPath(Path.Combine(".", "data", "dumps"))}");
Log("i", $"  Symbols: {Path.GetFullPath(Path.Combine(".", "data", "symbols"))}");
Log("i", $"  Buckets: {Path.GetFullPath(Path.Combine(".", "data", "buckets.json"))}");
Console.WriteLine(new string('=', 100));

// ═════════════════════════════════════════════════════════════════════════════
//  Logging helpers
// ═════════════════════════════════════════════════════════════════════════════

static void Log(string level, string msg)
{
    var prefix = level switch
    {
        "+" => "[+]",
        "!" => "[!]",
        "i" => "[i]",
        "=" => "[=]",
        _   => "   "
    };
    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {prefix} {msg}");
}

static void StepHeader(int n, string title)
{
    Console.WriteLine(new string('-', 100));
    Log("=", $"STEP {n}/7: {title}");
    Console.WriteLine(new string('-', 100));
}

static void StepFooter(int n, Stopwatch sw)
{
    Log("i", $"Step {n} completed in {sw.ElapsedMilliseconds}ms");
    Console.WriteLine();
}
