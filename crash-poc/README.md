# Crash Collector POC

An end-to-end proof of concept for automated crash dump collection, symbolication, and
analysis targeting **Dell Digital Delivery Service SubAgent** (`DELL.DIGITAL.DELIVERY.SERVICE.SUBAGENT.EXE`).

Built with .NET 9, SQLite, and ASP.NET Minimal APIs. Runs entirely on the local filesystem
with no cloud dependencies, CDN, or authentication.

---

## What This POC Demonstrates

| Capability | POC Implementation |
|---|---|
| **Crash ingestion** | Fetch crash reports from Windows Error Reporting (WER) API (mock fallback included) |
| **Dump storage** | Download minidump/cab files to a content-addressed local store with SHA-256 verification |
| **Metadata extraction** | Parse minidump headers, run WinDbg headless (`cdb.exe`), or generate synthetic metadata from mock dumps |
| **Stack-based bucketing** | Normalise stack frames, select top 3 non-system frames, compute stable SHA-256 bucket IDs |
| **Symbol server** | Scan build output for PDB files, extract GUID+Age (Portable & classic PDB7), publish to SymSrv-compatible layout |
| **Symbolication** | Map raw crash stacks to `Module!Function` names using local symbols |
| **WinDbg integration** | Headless `.reload /f`, `!analyze -v`, `k` with output capture and report persistence |
| **Structured storage** | SQLite database with Crashes, Buckets, and CrashBucketMap tables |
| **REST API** | Minimal API endpoints for crashes and buckets with symbolicated stack traces |
| **Demo pipeline** | 7-step console flow with timestamped logging, per-step timing, and live API verification |

---

## How It Maps to the Enterprise Proposal

| Enterprise Component | POC Equivalent | Production Path |
|---|---|---|
| Azure Blob crash intake | `LocalDumpStore` writing to `./data/dumps/` | Replace with Azure Blob SDK + Event Grid triggers |
| WER API integration | `WerApiClient` with AAD auth (falls back to mock data) | Wire real tenant/client credentials via Key Vault |
| Symbol server (SymSrv CDN) | `LocalSymbolServer` with local filesystem layout | Push to Azure Blob + CDN with `symstore.exe` or equivalent |
| Crash bucketing service | `Bucketizer` with SHA-256-based stable bucket IDs | Deploy as Azure Function or microservice behind Service Bus |
| SQL metadata store | SQLite via `Microsoft.Data.Sqlite` | Migrate to Azure SQL or Cosmos DB |
| Analysis API | ASP.NET Minimal API on `localhost:5100` | Deploy to Azure App Service / AKS with Entra ID auth |
| WinDbg symbolication | `WinDbgRunner` calling `cdb.exe` headless | Run in a container with Debugging Tools pre-installed |
| Dashboard | Console output + API JSON | Build React/Blazor dashboard consuming the API |

---

## Project Structure

```
crash-poc/
  crash-poc.sln
  README.md
  data/                              # Created at runtime
    crashes.db                       # SQLite database
    dumps/{CrashId}/                 # Downloaded dump files + metadata
      dump.cab
      metadata.json
      extracted-metadata.json
    symbols/{pdbName}/{GUID-AGE}/    # SymSrv-compatible symbol store
      {pdbName}
    buckets.json                     # Bucket manifest

  CrashCollector.Console/            # 7-step demo pipeline
    Program.cs                       # Main entry point (7-step flow)
    WerApiClient.cs                  # WER API client (AAD + mock fallback)
    LocalDumpStore.cs                # Dump download & content-addressed storage
    DumpMetadataExtractor.cs         # Minidump parsing, WinDbg, mock extraction
    Bucketizer.cs                    # Stack normalisation + SHA-256 bucketing
    LocalSymbolServer.cs             # PDB scanning, GUID+Age extraction, SymSrv layout
    WinDbgRunner.cs                  # Headless cdb.exe execution + report persistence
    Models/
      CrashReport.cs                 # WER crash report model
      CrashMetadata.cs               # Extracted dump metadata model
      StackSignature.cs              # Normalised stack signature model
    Data/
      CrashDb.cs                     # SQLite schema + lifecycle management
      CrashRepository.cs             # Crashes table CRUD
      BucketRepository.cs            # Buckets + CrashBucketMap CRUD

  CrashCollector.Api/                # REST API (reads shared SQLite DB)
    Program.cs                       # Minimal API endpoints
```

---

## Prerequisites

- **.NET 9 SDK** (build & run)
- **Windows** (minidump headers, WinDbg integration)
- **D3 build output** at `C:\Users\Narendra_K_Meena\source\repos\D3\d3-client-nga\src` (for symbol publishing; step 5 logs an error and continues if missing)
- **WinDbg / Debugging Tools for Windows** (optional; cdb.exe is auto-discovered, graceful skip if absent)

---

## How to Run End-to-End

### 1. Build the solution

```powershell
cd C:\PlayGround\crash-poc
dotnet build crash-poc.sln --ignore-failed-sources
```

### 2. Run the API (in a separate terminal)

```powershell
dotnet run --project CrashCollector.Api --urls http://localhost:5100
```

### 3. Run the 7-step demo pipeline

```powershell
dotnet run --project CrashCollector.Console
```

This executes all 7 steps sequentially:

| Step | Action | Duration |
|------|--------|----------|
| 1 | Fetch 20 crash reports from WER (mock fallback) | ~100ms |
| 2 | Download dumps to `./data/dumps/` | ~1s |
| 3 | Extract metadata + persist to SQLite | ~1s |
| 4 | Bucket crashes by stack signature | ~500ms |
| 5 | Publish PDBs to local symbol store | ~15s |
| 6 | Symbolicate one representative crash | ~400ms |
| 7 | Print summary + verify API endpoints | ~2s |

### 4. (Optional) Set real WER credentials

```powershell
$env:WER_TENANT_ID = "<your-tenant-id>"
$env:WER_CLIENT_ID = "<your-client-id>"
$env:WER_CLIENT_SECRET = "<your-client-secret>"
dotnet run --project CrashCollector.Console
```

Without credentials, the system automatically falls back to realistic mock data.

---

## Expected Console Output

```
15:32:22.246  [=] Crash Collector Console - POC Demo
15:32:22.284  [i] Target: DELL.DIGITAL.DELIVERY.SERVICE.SUBAGENT.EXE
15:32:22.285  [i] Symbol source: C:\Users\...\d3-client-nga\src
15:32:22.285  [i] API base URL: http://localhost:5100
15:32:22.368  [+] SQLite ready: C:\PlayGround\crash-poc\data\crashes.db

----------------------------------------------------------------------------------------------------
15:32:22.371  [=] STEP 1/7: Fetch crashes from WER
----------------------------------------------------------------------------------------------------
15:32:22.425  [+] Retrieved 18 crash report(s) in 42ms
               ...
15:32:22.435  [i] Step 1 completed in 63ms

----------------------------------------------------------------------------------------------------
15:32:22.436  [=] STEP 2/7: Download dumps
----------------------------------------------------------------------------------------------------
15:32:23.591  [+] SAVED  WER-C0000409-68398358c5fd4d18b44  [3,880 B]
               ...
15:32:23.620  [+] downloaded=12  skipped=0  meta-only=6  errors=0

----------------------------------------------------------------------------------------------------
15:32:23.669  [=] STEP 3/7: Extract metadata
----------------------------------------------------------------------------------------------------
15:32:25.354  [+] OK     WER-C0000409-...  exc=0xC0000409  mod=...  threads=10  frames=7  via=MockSynthetic
               ...
15:32:25.370  [+] 12 dump(s) analysed, 18 crash(es) persisted to SQLite

----------------------------------------------------------------------------------------------------
15:32:25.373  [=] STEP 4/7: Bucket crashes by stack signature
----------------------------------------------------------------------------------------------------
15:32:25.745  [+] [49058E499E03...] 0xC0000409 | 5 crash(es)
                   key: ...!CryptoHelper.DecryptBuffer -> ...!LicenseValidator.ValidateSignature -> ...
               ...
15:32:26.096  [+] 6 bucket(s) from 12 crash(es). Saved to SQLite + ./data/buckets.json

----------------------------------------------------------------------------------------------------
15:32:26.103  [=] STEP 5/7: Publish symbols from build output
----------------------------------------------------------------------------------------------------
15:32:40.884  [+] Found 638 PDB(s)
15:32:40.889  [+] Published: 119  Already present: 519  Skipped: 0  Errors: 0
15:32:40.901  [+] Symbol path: srv*C:\PlayGround\crash-poc\data\symbols

----------------------------------------------------------------------------------------------------
15:32:40.903  [=] STEP 6/7: Symbolicate one representative crash
----------------------------------------------------------------------------------------------------
15:32:40.905  [i] Crash:     WER-C0000409-68398358c5fd4d18b44
15:32:40.906  [i] Exception: 0xC0000409 (Stack Buffer Overrun)
15:32:40.910  [i] Symbolicated stack trace:
               #00  Dell.Digital.Delivery.Service.SubAgent!CryptoHelper.DecryptBuffer(Byte[] input) <<< KEY
               #01  Dell.Digital.Delivery.Service.SubAgent!LicenseValidator.ValidateSignature(License lic) <<< KEY
               #02  Dell.Digital.Delivery.Service.SubAgent!EntitlementService.CheckEntitlement(String id) <<< KEY
               #03  coreclr!MethodDesc::MakeJitWorker+0x2a1
               #04  kernel32!BaseThreadInitThunk+0x1d
               ...

----------------------------------------------------------------------------------------------------
15:32:41.309  [=] STEP 7/7: Results summary + API verification
----------------------------------------------------------------------------------------------------
15:32:41.310  [=] DATABASE SUMMARY
15:32:41.357  [i]   Crashes in DB: 18
15:32:41.371  [i]   Buckets in DB: 6

15:32:42.754  [+] API is running! Querying endpoints...
15:32:43.252  [+] GET /crashes?limit=3 --> 3 crash(es) returned
15:32:43.509  [+] GET /crashes/WER-C0000409-...
                   bucketId: 49058E499E03220C6B7F8CC97F67790F
                   symbolicatedStack: 7 frame(s)
15:32:43.646  [+] GET /buckets --> 6 bucket(s) returned
15:32:43.700  [+] GET /buckets/49058E499E03... --> 5 crash(es) in bucket

====================================================================================================
15:32:43.746  [=] All 7 steps complete in 21.5s
====================================================================================================
```

---

## Sample API Calls

### List all crashes

```bash
curl http://localhost:5100/crashes
curl http://localhost:5100/crashes?limit=5
```

```json
{
  "count": 5,
  "crashes": [
    {
      "crashId": "WER-C0000005-313897bd93fc4daf8c9",
      "appName": "DELL.DIGITAL.DELIVERY.SERVICE.SUBAGENT.EXE",
      "appVersion": "3.9.997.0",
      "timestamp": "2026-03-25T04:24:59.267+00:00",
      "exceptionCode": "0xC0000005",
      "exceptionName": "Access Violation",
      "faultingModule": "DELL.DIGITAL.DELIVERY.SERVICE.SUBAGENT.EXE",
      "threadCount": 17,
      "dumpPath": "C:\\PlayGround\\crash-poc\\data\\dumps\\WER-C0000005-...\\dump.cab",
      "dumpSizeMB": 0.0037,
      "failureBucket": "C0000005_Access_Violation",
      "extractionMethod": "MockSynthetic"
    }
  ]
}
```

### Get crash detail with symbolicated stack

```bash
curl http://localhost:5100/crashes/WER-C0000409-68398358c5fd4d18b44
```

```json
{
  "crash": {
    "crashId": "WER-C0000409-68398358c5fd4d18b44",
    "appName": "DELL.DIGITAL.DELIVERY.SERVICE.SUBAGENT.EXE",
    "exceptionCode": "0xC0000409",
    "exceptionName": "Stack Buffer Overrun",
    "faultingModule": "DELL.DIGITAL.DELIVERY.SERVICE.SUBAGENT.EXE",
    "threadCount": 10
  },
  "bucketId": "49058E499E03220C6B7F8CC97F67790F",
  "bucket": {
    "bucketId": "49058E499E03220C6B7F8CC97F67790F",
    "exceptionCode": "0xC0000409",
    "keyFrame1": "Dell.Digital.Delivery.Service.SubAgent!CryptoHelper.DecryptBuffer",
    "keyFrame2": "Dell.Digital.Delivery.Service.SubAgent!LicenseValidator.ValidateSignature",
    "keyFrame3": "Dell.Digital.Delivery.Service.SubAgent!EntitlementService.CheckEntitlement",
    "crashCount": 5
  },
  "symbolicatedStack": [
    "Dell.Digital.Delivery.Service.SubAgent!CryptoHelper.DecryptBuffer(Byte[] input)",
    "Dell.Digital.Delivery.Service.SubAgent!LicenseValidator.ValidateSignature(License lic)",
    "Dell.Digital.Delivery.Service.SubAgent!EntitlementService.CheckEntitlement(String id)",
    "coreclr!MethodDesc::MakeJitWorker+0x2a1",
    "kernel32!BaseThreadInitThunk+0x1d",
    "coreclr!MethodDesc::MakeJitWorker+0x2a1",
    "ntdll!RtlUserThreadStart+0x21"
  ]
}
```

### List all buckets

```bash
curl http://localhost:5100/buckets
```

```json
{
  "count": 6,
  "buckets": [
    {
      "bucketId": "49058E499E03220C6B7F8CC97F67790F",
      "exceptionCode": "0xC0000409",
      "keyFrame1": "Dell.Digital.Delivery.Service.SubAgent!CryptoHelper.DecryptBuffer",
      "keyFrame2": "Dell.Digital.Delivery.Service.SubAgent!LicenseValidator.ValidateSignature",
      "keyFrame3": "Dell.Digital.Delivery.Service.SubAgent!EntitlementService.CheckEntitlement",
      "crashCount": 5
    }
  ]
}
```

### Get bucket detail with member crashes

```bash
curl http://localhost:5100/buckets/49058E499E03220C6B7F8CC97F67790F
```

```json
{
  "bucket": {
    "bucketId": "49058E499E03220C6B7F8CC97F67790F",
    "exceptionCode": "0xC0000409",
    "crashCount": 5
  },
  "keyFrames": [
    "Dell.Digital.Delivery.Service.SubAgent!CryptoHelper.DecryptBuffer",
    "Dell.Digital.Delivery.Service.SubAgent!LicenseValidator.ValidateSignature",
    "Dell.Digital.Delivery.Service.SubAgent!EntitlementService.CheckEntitlement"
  ],
  "crashes": [
    { "crashId": "WER-C0000409-68398358c5fd4d18b44", "exceptionCode": "0xC0000409", "..." : "..." },
    { "crashId": "WER-C0000409-2ce4ac5611344888aff", "..." : "..." }
  ]
}
```

---

## SQLite Schema

```sql
-- Crashes: one row per crash event
CREATE TABLE Crashes (
    CrashId          TEXT PRIMARY KEY,
    AppName          TEXT NOT NULL,
    AppVersion       TEXT NOT NULL,
    Timestamp        TEXT NOT NULL,          -- ISO-8601
    ExceptionCode    TEXT,
    ExceptionName    TEXT,
    FaultingModule   TEXT,
    ThreadCount      INTEGER DEFAULT 0,
    DumpPath         TEXT,
    DumpSizeMB       REAL DEFAULT 0,
    DumpSha256       TEXT,
    FailureBucket    TEXT,
    ExtractionMethod TEXT,
    CreatedAtUtc     TEXT DEFAULT (datetime('now'))
);

-- Buckets: one row per unique stack signature
CREATE TABLE Buckets (
    BucketId         TEXT PRIMARY KEY,
    ExceptionCode    TEXT,
    KeyFrame1        TEXT,
    KeyFrame2        TEXT,
    KeyFrame3        TEXT,
    CrashCount       INTEGER DEFAULT 0,
    FirstSeenUtc     TEXT DEFAULT (datetime('now')),
    LastSeenUtc      TEXT DEFAULT (datetime('now'))
);

-- CrashBucketMap: many-to-one mapping
CREATE TABLE CrashBucketMap (
    CrashId          TEXT NOT NULL REFERENCES Crashes(CrashId),
    BucketId         TEXT NOT NULL REFERENCES Buckets(BucketId),
    MappedAtUtc      TEXT DEFAULT (datetime('now')),
    PRIMARY KEY (CrashId, BucketId)
);
```

---

## Crash Types Demonstrated

| Exception Code | Name | Scenario |
|---|---|---|
| `0xC0000005` | Access Violation | Plugin loading — null pointer in `PluginManager.LoadPlugin` |
| `0xC0000409` | Stack Buffer Overrun | Crypto — buffer overrun in `CryptoHelper.DecryptBuffer` |
| `0xC00000FD` | Stack Overflow | Config — infinite recursion in `ConfigResolver.ResolveRecursive` |
| `0xC0000374` | Heap Corruption | Native interop — marshalling error in `NativeInterop.MarshalCallbackData` |
| `0xC0000006` | In-Page I/O Error | Cache — disk read failure in `CacheManager.ReadPage` |
| `0xE0434352` | CLR Unhandled Exception | Service startup — unhandled in `Program.Main` |

---

## Limitations of the POC

### Data
- **Mock data**: Without WER API credentials, crash reports are synthetically generated. Stack frames are deterministic but fabricated.
- **No real minidumps**: Downloaded "dumps" are mock `.cab` files; real `MDMP` parsing works but requires actual crash dumps.

### Symbols
- **Local-only symbol store**: No CDN, no `symstore.exe` — PDBs are simply file-copied into the SymSrv directory layout.
- **No PE/DLL symbol matching**: The symbol server publishes PDBs but doesn't verify they match the exact binaries that crashed.

### WinDbg
- **Requires manual install**: `cdb.exe` must be installed separately (Debugging Tools for Windows from the Windows SDK). Gracefully skipped if absent.
- **Mock dumps aren't analysable**: WinDbg can't analyse the synthetic `.cab` files; it only works with real `MDMP` dumps.

### Database
- **SQLite single-writer**: WAL mode helps concurrent reads, but only one process can write at a time. Not suitable for production load.
- **No migrations**: Schema is created with `IF NOT EXISTS`; no versioned migration system.

### API
- **No authentication**: All endpoints are open. Production requires Entra ID / OAuth.
- **No pagination**: `GET /crashes` returns up to `?limit=100` rows; no cursor-based pagination.
- **Read-only**: The API reads from the SQLite DB populated by the Console. No write endpoints.

### Architecture
- **Single-machine**: Everything runs locally. No message queues, no async processing, no horizontal scaling.
- **Tightly coupled**: Console and API share source files via `<Compile Link>`. Production should extract a shared library project.
- **Windows-only**: Minidump parsing and WinDbg integration are Windows-specific.

---

## Technology Stack

| Component | Technology |
|---|---|
| Runtime | .NET 9 |
| Database | SQLite via `Microsoft.Data.Sqlite 9.0.3` |
| Web API | ASP.NET Minimal APIs |
| Crash source | Windows Error Reporting (WER) REST API |
| Symbol format | Portable PDB (BSJB) + Classic PDB7 (MSF 7.00) |
| Symbol layout | SymSrv (`{pdbName}/{GUID-AGE}/{pdbName}`) |
| Debugger | WinDbg / `cdb.exe` (headless) |
| Hashing | SHA-256 (dump integrity + bucket IDs) |
