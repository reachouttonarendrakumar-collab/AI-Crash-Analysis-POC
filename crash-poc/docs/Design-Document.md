# Crash Dump Collection & Analysis System — Design Document

## 1. Executive Summary

The Crash Dump Collection & Analysis System is an enterprise-scale platform for automated crash collection, symbolication, analysis, and remediation across all Dell CSG client applications. The system targets ~100M managed client devices reporting via Windows Error Reporting (WER), with capacity for 100K–1M crashes/day at 500 dumps/min sustained processing and <200ms P95 query latency.

**Objective**: Reduce crash rates, hangs, freezes, and BSODs across the CSG portfolio. Address immediate field issues, establish systematic crash analysis and remediation, and add resilience to limit future errors.

**Current Status**: Phase 1 POC completed targeting Dell Digital Delivery, validating core pipeline steps 5–12 of the enterprise workflow.

**Infrastructure**: Dell Digital Cloud for all production services, with crash telemetry stored in the Dell Data Lake House (IOMETE) queried via Spark.

---

## 2. Problem Statement

### 2.1 Technical & Process Gaps

| Gap | Impact | Metric |
|-----|--------|--------|
| **Manual / Fragmented Collection** | No centralized system; ~90% crash data never collected | 90% data loss |
| **No Automated Symbol Management** | Manual PDB distribution; ~80% analysis failures from GUID/AGE mismatches | 80% symbol failures |
| **Inefficient Debugging Workflow** | Manual dump downloads; no bucketing/grouping; slow triage | 40+ hours per crash |
| **No Analytics / Trending** | Patterns missed; no regression detection; repeated issues undetected | Zero trend visibility |
| **Compliance & Security Risks** | Unencrypted dumps; no audit trails; PII exposure risk; GDPR/CCPA gaps | PII exposure risk |
| **Scalability Crisis** | Current tools support <100 devices; chat/email workflows break at scale | <100 device ceiling |

### 2.2 Problem → Solution Mapping

| Current State | Proposed Solution |
|--------------|-------------------|
| Manual collection | Centralized WER intake + API |
| No fleet visibility | Indexed per-app dashboards |
| No symbol automation | CI/CD symbol server (SymSrv) |
| Slow debugging | Bucketing + WinDbg automation |
| No analytics | Trends & regression detection |
| Compliance risk | Encryption, RBAC, audits |
| Doesn't scale | Horizontal collectors + queues |

---

## 3. System Architecture

### 3.1 System Components

The system comprises seven major components spanning external (client + Microsoft) and internal (Dell infrastructure) boundaries:

#### External Components

**WER Client Configuration (~100M devices)**
- Policy-driven crash capture on endpoints with app-specific controls
- Local dumps: mini by default, full for critical scenarios
- Corporate consent for automatic WER submission
- Per-app settings: DumpType, DumpCount, throttling

**Microsoft WER Service**
- Secure intake and temporary catalog of crash dumps and metadata
- Receives uploads via HTTPS from client WER
- Assigns crash IDs, retains data short-term
- Exposes WER REST API for enterprise retrieval

#### Internal Components

**WER API Collection Service**
- Scheduled retrieval of new crashes with robust, scalable polling
- OAuth 2.0 auth; cadence 15 min–24 hr per app
- Rate-limit aware with retries, exponential backoff, idempotency keys
- Filters by application, version, severity

**Crash Dump Processing & Storage**
- Parallel processing pipeline backed by tiered object storage
- Queue-based ingestion; extract metadata & deduplicate
- Blob storage tiers: Hot (0–30d), Warm (30–180d), Cold (180d+)
- Compression and lifecycle policies for cost control

**Symbol Server Infrastructure**
- CI/CD-driven PDB publishing for reliable symbolication
- SymSrv-compatible GUID+AGE hierarchy
- Validation, multi-version retention, and access logs
- CDN acceleration for global engineering teams

**Indexing & Analysis Engine**
- Fast search and grouping across applications and versions
- PostgreSQL metadata (partitioned); Elasticsearch search
- Stack-signature bucketing, per-app indices
- Trends, regressions, and severity tagging

**Analysis Dashboard & Tools**
- Operational visibility and deep debug with symbol-aware tools
- WinDbg integration with corporate and Microsoft symbols
- Dashboards, alerts, and per-application views
- Engineer workflows from triage to root cause

### 3.2 Architecture Diagram

```
┌─────────────────────────────────────────────────────────────┐
│              Client Machines (~100M devices)                  │
│     WER Client → Mini/Full dump → Upload to Microsoft WER    │
└──────────────────────────┬──────────────────────────────────┘
                           │ HTTPS (automatic)
                           ▼
┌─────────────────────────────────────────────────────────────┐
│              Microsoft WER Service (External)                │
│     Validates, catalogs, stores crash dumps temporarily      │
│     Exposes WER REST API for enterprise collection           │
└──────────────────────────┬──────────────────────────────────┘
                           │ OAuth 2.0 / REST API
                           ▼
┌─────────────────────────────────────────────────────────────┐
│         WER API Collection Service (Internal)                │
│     Polls WER API on schedule (15 min – 24 hr per app)       │
│     Batch downloads dumps with retries & idempotency         │
└──────────────────────────┬──────────────────────────────────┘
                           │
              ┌────────────┼────────────────┐
              ▼                             ▼
┌──────────────────────┐     ┌──────────────────────────────┐
│ Crash Dump Processing │     │ Symbol Server Infrastructure  │
│ Queue-based parallel  │     │ CI/CD → symstore → SymSrv    │
│ Extract/Sign/Dedup    │     │ GUID+AGE hierarchy            │
│ Tiered blob storage   │◄───│ CDN accelerated               │
│ Hot/Warm/Cold         │     │ 98% symbol match target       │
└──────────┬───────────┘     └──────────────────────────────┘
           │
           ▼
┌─────────────────────────────────────────────────────────────┐
│              Indexing & Analysis Engine                       │
│     PostgreSQL (partitioned) + Elasticsearch                 │
│     Stack-signature bucketing, per-app indices               │
│     Correlate with DTM / IOMETE Data Lake (Spark)            │
└──────────────────────────┬──────────────────────────────────┘
                           │
              ┌────────────┼────────────────┐
              ▼                             ▼
┌──────────────────────┐     ┌──────────────────────────────┐
│ Analysis Dashboard    │     │ AI Analysis Engine (Phase 3)  │
│ Per-app views, alerts │     │ LLM root cause analysis       │
│ WinDbg integration    │     │ Source code RAG                │
│ Engineer triage flow  │     │ Fix generation + PR creation   │
└──────────────────────┘     └──────────────────────────────┘
```

---

## 4. Data Flow: 12-Step Workflow

| Step | Action | Location | Protocol |
|------|--------|----------|----------|
| 1 | Crash detected (WER) | Client | WER |
| 2 | Create mini/full dump (local) | Client | Local |
| 3 | Upload securely to Microsoft WER | Client → MS | HTTPS |
| 4 | WER validates & catalogs crash | Microsoft | Internal |
| 5 | Scheduler triggers company collection | Internal | Cron/Timer |
| 6 | List new crashes via WER API | Internal → MS | OAuth 2.0 + REST |
| 7 | Batch download dumps & metadata | Internal → MS | HTTPS (batch) |
| 8 | Enqueue for async processing | Internal | Message queue |
| 9 | Extract metadata / Sign / Deduplicate | Internal | Processing pipeline |
| 10 | Store blob + index in DB & search | Internal | PostgreSQL + ES |
| 11 | Dashboards, alerts, engineer triage | Internal | Web + API |
| 12 | Symbolicate & analyze with WinDbg | Internal | SymSrv + WinDbg |

### Timing Milestones

| Time | Milestone |
|------|-----------|
| T+0s | Crash occurs on client; WER detects event |
| T+30s | WER creates mini/full dump and uploads securely to Microsoft |
| T+1hr | Company collector polls WER API and downloads new crashes |
| T+2hr | Dumps processed, deduplicated, and indexed for search |
| T+4hr | Symbolicated analysis available in dashboards and WinDbg |

### Throughput Targets

| Metric | Target |
|--------|--------|
| Sustained processing | 500 dumps/min |
| Daily volume | 100K–1M crashes/day |
| Storage | 100–500 GB/day |
| P95 query latency | <200ms |

---

## 5. Symbol Server & CI/CD Integration

### 5.1 CI/CD Symbol Publishing (8-Step Workflow)

| Step | Action | Detail |
|------|--------|--------|
| 1 | Build completes | Release + Debug builds |
| 2 | Generate PDB symbols | Compiler output |
| 3 | Extract GUID+Age metadata | From PDB header |
| 4 | Existence check | Idempotent (skip if already published) |
| 5 | Publish via symstore | `symstore.exe add /f AppA.pdb /s \\symbols\\server /t "AppA Build 1234" /compress` |
| 6 | Validate integrity | Hash/size verification |
| 7 | Update symbol registry/index | Database update |
| 8 | Notify & make available globally | CDN propagation |

### 5.2 Symbol Server (SymSrv)

| Property | Value |
|----------|-------|
| Protocol | HTTP/HTTPS SymSrv |
| Hierarchy | `/symbols/{pdb_name}/{GUID-AGE}/{pdb_file}` |
| Storage | Hierarchical object store; CDN accelerated |
| Access | RBAC, access logs, integrity checks |
| Availability target | ≥98% symbol match rate |

### 5.3 Engineer Debugging Workflow

```
Query crashes (App/Version/Bucket)
    → Select crash bucket
    → Download dump (.dmp)
    → Open in WinDbg
    → Symbolicate & analyze
    → Root cause identified
```

**WinDbg Symbol Path**:
```
.sympath srv*c:\symbols*https://symbols.dell.com/symbols;srv*c:\symbols*https://msdl.microsoft.com/download/symbols
.reload /f
```

**Key Commands**: `!analyze -v`, `kv`, `~* k`

**Targets**:
- Average analysis time: <2 hours
- Symbol match rate: ≥98%
- Time to first triage: ≤30 min

---

## 6. Data Sources

### 6.1 DTM / IOMETE Data Lake

| Property | Value |
|----------|-------|
| Data | Event Viewer logs, crash telemetry events |
| Storage | IOMETE Data Lakehouse |
| Query | Spark interface (SQL / PySpark) |
| Strength | Fleet-wide volume, KPIs, trends |
| Limitation | No actual stack traces; event-level only |

### 6.2 Microsoft WER / Partner Center

| Property | Value |
|----------|-------|
| Data | Crash signatures, stack traces, CAB files |
| Storage | Microsoft cloud (WER backend) |
| API | WER REST API + Microsoft Store Analytics API |
| Endpoint | `manage.devcenter.microsoft.com/v1.0/my/analytics/desktop/stacktrace` |
| Auth | Azure AD / OAuth 2.0 (client credentials) |
| Retention | Last 30 days (requires scheduled ingestion) |
| Constraint | PDB upload blocked by PIA/Compliance |

### 6.3 Unified Approach

- DTM provides fleet telemetry + crash event volume
- WER provides real crash dumps + stack traces
- In-house symbol server maps PDBs internally (zero external upload)
- Combined view: event trends (DTM) + deep crash analysis (WER)
- Correlate DTM event IDs with WER crash buckets

### 6.4 Infrastructure: Dell Digital Cloud

All production services deployed on **Dell Digital Cloud** (not Azure):
- IOMETE lakehouse for long-term crash data retention
- PostgreSQL + Elasticsearch for crash indexing
- Object storage (tiered: Hot/Warm/Cold) for dump files
- CDN for symbol server global distribution

---

## 7. Security & Compliance

### 7.1 Security Controls

| Control | Implementation |
|---------|---------------|
| **Encryption at rest** | AES-256 across all object stores and databases |
| **Encryption in transit** | TLS 1.3 for all APIs, symbol/dump transfers |
| **Authentication** | OAuth 2.0 (integrates with Dell AD) |
| **Authorization** | Role-Based Access Control, least-privilege roles |
| **Automation tokens** | Scoped tokens for service accounts |
| **Key management** | Managed keys with rotation and restricted access |

### 7.2 Privacy & PII

- PII scrubbing pipeline for dumps and metadata
- Data minimization by default; sensitive fields redacted
- Audit logs on all crash data access
- PDB files never leave Dell infrastructure (PIA compliant)

### 7.3 Compliance

| Requirement | Approach |
|-------------|----------|
| GDPR | Aligned; retention & deletion via lifecycle policies |
| CCPA | Data subject request support (access/erasure) |
| PIA | PDB files internal only; zero external exposure |
| Audit | Immutable audit trails for access, downloads, changes |
| Monitoring | Security alerting for anomalous activity and RBAC violations |
| Testing | Regular penetration testing cadence |

---

## 8. Success Metrics & KPIs

### 8.1 Primary KPIs

| KPI | Target | Description |
|-----|--------|-------------|
| **Collection Coverage** | ≥95% | Crashes successfully collected from devices |
| **Collection Latency** | <4 hr | Crash to available for triage |
| **Analysis Time** | <2 hr | Average time to triage and diagnose |
| **Resolution Rate** | ≥80% (30 days) | Critical crash buckets resolved within 30 days |
| **Symbol Availability** | ≥98% | Dumps with matching symbols (PDBs) |
| **System Uptime** | 99.9% | Overall service availability SLA |

### 8.2 Quality Metrics

- Duplicate detection accuracy (bucketing)
- Low false-positive bucketing rate
- Symbolication success aligned with availability target
- Regression detection on new versions
- Query P95 <200ms (search usability)

### 8.3 Scale Metrics

| Metric | Target |
|--------|--------|
| Device scale | ~100M managed clients |
| Daily crash volume | 100K–1M crashes/day |
| Processing throughput | 500 dumps/min sustained |
| Deduplication ratio | 15:1 (bucketed) |
| Storage throughput | 100–500 GB/day |

---

## 9. Scalability & Performance

### 9.1 Scaling Strategies

- **Horizontal scaling** of collectors and processors
- **Regional collection endpoints** (geo distribution)
- **Shard/partition indices** (time & app dimensions)
- **CDN for symbol distribution** (global teams)
- **Load balancing & HA queues** (async pipeline)

### 9.2 Monitoring & Alerting

**SLOs**:
- Collection coverage ≥95%
- Collection latency <4 hr
- System uptime 99.9%

**Automated Alerting**:
- API rate limit >80% for 5 min → throttle collectors, notify on-call
- Queue depth >10K for 10 min → scale processors horizontally
- Coverage drops <95% daily → open incident, run source checks
- Latency P95 >4 hr → escalate to SRE, enable backlog priority mode

**Capacity Planning**:
- Hot/Warm/Cold storage utilization tracking
- Processor utilization monitoring
- Queue depth threshold alerting

---

## 10. POC Implementation

### 10.1 POC Scope

The POC validates steps 5–12 of the enterprise workflow using Dell Digital Delivery as the target application.

| Enterprise Step | POC Implementation | Status |
|----------------|-------------------|--------|
| 5–6. Collect via WER API | Fetch crash reports from WER | Done |
| 7. Batch download dumps | Download & hash dump files | Done |
| 9. Extract / Dedup | Metadata extraction (3 strategies) | Done |
| 9. Stack signature | Frame normalization + SHA-256 bucketing | Done |
| 10. Store + index | SQLite + REST API (4 endpoints) | Done |
| 11. Dashboards | React dashboard (5 pages) | Done |
| 12. Symbolicate | Symbolicated stack trace viewer | Done |

### 10.2 POC Tech Stack

| Component | Technology | Version |
|-----------|-----------|---------|
| Console Pipeline | .NET 9 | 9.0 |
| REST API | ASP.NET Minimal API | 9.0 |
| Database | SQLite (Microsoft.Data.Sqlite) | Latest |
| Frontend | React + TypeScript | 19.x / 5.x |
| Build Tool | Vite | 8.x |
| Routing | React Router | 6.x |
| Charts | Recharts | 2.x |

### 10.3 POC Pipeline (7 Steps)

| Step | Action | Output |
|------|--------|--------|
| 1 | Fetch crash reports from WER | `CrashReport` objects |
| 2 | Download dump files | `.dmp` files + SHA-256 hashes |
| 3 | Extract metadata | Exception code, faulting module, thread count, stack frames |
| 4 | Normalize stack frames | Clean `Module!Function` strings |
| 5 | Bucket by signature | `BucketId = SHA256(ExceptionCode|KeyFrame1|KeyFrame2|KeyFrame3)` |
| 6 | Persist to SQLite | Crashes and Buckets tables populated |
| 7 | Symbolicate & verify | Stack trace resolution, API endpoint test |

**Extraction Strategies** (priority order):
1. **WinDbg** (`cdb.exe`): Runs `!analyze -v`, parses exception code, faulting module, thread count
2. **Raw Minidump**: Parses PE header directly for basic metadata
3. **Mock/Synthetic**: Reads companion `metadata.json`, generates deterministic frames

### 10.4 POC API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/` | Health check |
| GET | `/crashes?limit=N` | List crashes |
| GET | `/crashes/{id}` | Crash detail + symbolicated stack |
| GET | `/buckets?limit=N` | List buckets |
| GET | `/buckets/{id}` | Bucket detail + member crashes |

### 10.5 POC Dashboard

**Pages**:

| Route | Page | Description |
|-------|------|-------------|
| `/` | OverviewPage | Summary metrics, trend chart, top buckets |
| `/buckets` | BucketsPage | All buckets, sorted by crash count desc |
| `/buckets/:bucketId` | BucketDetailPage | Bucket summary, key frames, crashes |
| `/crashes` | CrashesPage | All crashes, sorted by timestamp desc |
| `/crashes/:id` | CrashDetailPage | Metadata, bucket info, symbolicated stack |

### 10.6 POC Data Model (SQLite)

**Crashes Table**:

| Column | Type | Description |
|--------|------|-------------|
| CrashId | TEXT PK | Unique crash identifier |
| AppName | TEXT | Application name |
| AppVersion | TEXT | Application version |
| Timestamp | TEXT | ISO 8601 UTC |
| ExceptionCode | TEXT | e.g., 0xC0000005 |
| FaultingModule | TEXT | Module that caused crash |
| DumpPath | TEXT | Local path to dump file |
| DumpSha256 | TEXT | SHA-256 hash |
| FailureBucket | TEXT FK | Reference to Buckets.BucketId |

**Buckets Table**:

| Column | Type | Description |
|--------|------|-------------|
| BucketId | TEXT PK | SHA-256 of signature |
| ExceptionCode | TEXT | Exception code |
| KeyFrame1-3 | TEXT | Top 3 application frames |
| CrashCount | INTEGER | Crashes in bucket |
| FirstSeenUtc | TEXT | First occurrence |
| LastSeenUtc | TEXT | Most recent occurrence |

---

## 11. AI Auto-Remediation (Future Phase)

### 11.1 Pipeline

```
Symbolicated Stack
       │
       ▼
┌─────────────────────┐
│ 1. Context Assembly  │  Retrieve source files around crash site (RAG)
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 2. Root Cause        │  LLM analyzes stack + source code
│    Analysis          │  Identifies bug pattern with confidence score
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 3. Fix Generation    │  Generate code patch; static analysis; CI tests
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 4. PR Creation       │  Branch + commit + PR via Azure DevOps
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 5. Human Review      │  Developer approves / requests changes
└─────────────────────┘
```

### 11.2 Safety Guardrails

- **Confidence threshold**: Only auto-create PRs for fixes above 80% confidence
- **Human-in-the-loop**: All AI-generated fixes require developer approval
- **Static analysis gate**: Fix must pass existing linters and analyzers
- **CI test gate**: Fix must pass full CI test suite before PR is opened
- **Feedback loop**: Developer accept/reject decisions improve future analysis

---

## 12. Production Technology Stack

### POC (Current)

| Component | Technology |
|-----------|-----------|
| Pipeline | .NET 9 Console |
| API | ASP.NET Minimal API |
| Database | SQLite |
| Frontend | React 19 + TypeScript + Vite |
| Charts | Recharts |

### Production (Target — Dell Digital Cloud)

| Component | Technology |
|-----------|-----------|
| Cloud | Dell Digital Cloud |
| Data Lake | IOMETE (Spark) |
| Database | PostgreSQL (partitioned) |
| Search | Elasticsearch |
| Object Storage | Tiered blob (Hot/Warm/Cold) |
| Symbol Server | SymSrv + CDN |
| Auth | OAuth 2.0 / Dell AD |
| Monitoring | Application metrics + alerting |
| AI (Phase 3) | LLM + RAG pipeline |
| PR Automation | Azure DevOps REST API |

---

## 13. Benefits & Value Proposition

| Benefit | Description |
|---------|-------------|
| **Reduced time-to-diagnosis** | Indexed, symbolicated dumps cut analysis to <2 hours |
| **Improved product quality** | Bucketing and trends drive targeted fixes and regression control |
| **Proactive detection** | Alerts on new/rising buckets prevent widespread impact |
| **Scalable platform** | Horizontal collectors, queues, storage handle 100M devices |
| **Comprehensive analytics** | Per-app indices, search, dashboards across versions |
| **Enhanced customer experience** | Fewer crashes and faster fixes increase reliability |
