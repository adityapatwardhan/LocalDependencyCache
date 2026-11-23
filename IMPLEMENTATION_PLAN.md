# LocalDependencyCache Implementation Plan

## 1. Objective
Build a C# Azure Functions app that reads package metadata from PowerShell Gallery OData APIs and writes a compact SQLite cache file that PSResourceGet can use for:
- Fast package search by `Id`, `Version`, and `Tags`
- Command search via tags prefixed with `PSCommand_`
- Dependency lookup with NuGet-style version range matching
- Incremental refresh based on published timestamp checkpoint
- Artifact distribution through Azure Blob Storage exposed via Azure CDN or static website endpoint

## 2. Scope
In scope:
- Full and incremental metadata ingestion from `Search()` endpoint
- Parsing OData `properties` payload
- SQLite database creation, migration, and update
- Storage for package identity, tags, and dependencies with parsed version constraints
- Query patterns needed by PSResourceGet
- Azure Functions hosting, scheduling, configuration, and observability
- Publishing generated SQLite cache artifact to Azure Blob Storage
- Consumer download pattern where PSResourceGet fetches SQLite from CDN/static website and performs local queries

Out of scope (initial version):
- Downloading package contents (.nupkg)
- Signature verification of packages
- Hosting API service on top of the DB
- Querying package data from Azure Function endpoints

## 3. Key Requirements Mapped to Design
- Compact DB file:
  - Normalized schema
  - Integer surrogate keys internally
  - `WITHOUT ROWID` where useful
  - Limited text duplication in child tables
- Searchable metadata:
  - Package identity (`Id`, `Version`)
  - Tags (including `PSCommand_` command tags)
- Dependency resolution:
  - Preserve original dependency range text
  - Parse and store normalized bounds (`min_version`, `max_version`, inclusive flags)

## 4. High-Level Architecture
1. Azure Functions host (isolated worker)
- `TimerTrigger` function runs periodic sync jobs
- Dependency injection composes client, parser, repository, and storage services

2. Gallery client
- Calls `https://www.powershellgallery.com/api/v2/Search()` with:
  - `$filter=Published gt datetime'<checkpoint>'`
  - `$orderby=Published`
  - `$skip`, `$top`
  - `includePrerelease=true`
- Handles pagination until terminal batch

3. Metadata parser
- Extracts `properties`
- Maps package fields, splits `Tags`, parses `Dependencies`
- Produces normalized model objects

4. SQLite writer
- Creates/updates local SQLite file in function temp storage
- Upsert package rows
- Replace or upsert tags and dependencies
- Maintains checkpoint state transactionally

5. Artifact publisher
- Uploads SQLite cache file to Azure Blob Storage
- Uses atomic publish pattern (upload to staging blob, then swap/copy to active blob)
- Produces a small manifest file with `etag`, `last_modified_utc`, and content hash for client freshness checks

6. Distribution layer
- Blob container is exposed via Azure CDN or Azure Storage static website endpoint
- PSResourceGet downloads the latest SQLite artifact from CDN/static endpoint
- PSResourceGet performs all search and dependency queries locally against downloaded SQLite

7. Query layer (internal)
- Parameterized SQL used by tests and diagnostics
- Validates command-tag and dependency matching behavior

## 5. Data Flow
1. Timer trigger starts sync run
2. Download current cache from Blob Storage if it exists; otherwise initialize new DB
3. Read checkpoint from DB `sync_state` table (optional override via config)
4. Pull paged OData results from Gallery
5. For each package entry:
- Parse package metadata
- Parse tags into distinct rows
- Parse dependency list into rows with version range bounds
6. Batch write in SQLite transaction
7. Update checkpoint to max published timestamp successfully committed
8. Upload updated SQLite DB to Blob Storage and emit/update manifest blob
9. CDN/static endpoint serves SQLite and manifest to PSResourceGet clients
10. PSResourceGet downloads artifact and executes queries locally

## 6. Incremental Update Strategy
- Use monotonic checkpoint (`last_published_utc`)
- Sort by `Published` ascending
- For safety window, allow optional overlap (for example, re-read last N minutes)
- Use deterministic upsert keys (`Id` + `Version`) to avoid duplicates
- Keep checkpoint state inside the SQLite artifact so each published DB is self-describing

## 7. Component Plan
## 7.1 Project layout
- `src/LocalDependencyCache.FunctionApp/Program.cs`
- `src/LocalDependencyCache.FunctionApp/Functions/SyncCacheTimerFunction.cs`
- `src/LocalDependencyCache.FunctionApp/Gallery/GalleryClient.cs`
- `src/LocalDependencyCache.FunctionApp/Parsing/MetadataParser.cs`
- `src/LocalDependencyCache.FunctionApp/Storage/SchemaManager.cs`
- `src/LocalDependencyCache.FunctionApp/Storage/PackageRepository.cs`
- `src/LocalDependencyCache.FunctionApp/Storage/BlobArtifactPublisher.cs`
- `src/LocalDependencyCache.FunctionApp/Storage/ArtifactManifestWriter.cs`
- `src/LocalDependencyCache.FunctionApp/Versioning/NuGetRangeParser.cs`
- `src/LocalDependencyCache.FunctionApp/Models/*.cs`

## 7.2 Package choices
- `Microsoft.Data.Sqlite` for SQLite access
- `System.Text.Json` for parsing and serialization
- `Microsoft.Azure.Functions.Worker` and `Microsoft.Azure.Functions.Worker.Sdk`
- `Azure.Storage.Blobs` for publishing the cache artifact
- `Azure.Identity` if using managed identity for Blob access
- Optional: `Polly` for retry policies (or custom retry)

## 7.3 Configuration (Azure)
- `PowerShellGallery:Endpoint` (default `https://www.powershellgallery.com/api/v2/Search()`)
- `Sync:PageSize` (default `100`)
- `Sync:OverlapMinutes` (default `5`)
- `Sync:Schedule` (CRON for timer trigger)
- `Storage:BlobConnection` or managed identity settings
- `Storage:ContainerName`
- `Storage:BlobName` (for example, `psgallery-cache.sqlite`)
- `Storage:ManifestBlobName` (for example, `psgallery-cache.manifest.json`)
- `Storage:LeaseBlobName` (for single-run lock)
- `Distribution:CdnBaseUrl` or `Distribution:StaticWebsiteBaseUrl`

## 8. Reliability and Performance
- HTTP retry with exponential backoff on transient errors
- Per-page processing with bounded memory
- Batched inserts in transactions (for example, 500 to 2000 rows per commit)
- SQLite pragmas tuned for build/update job:
  - `journal_mode=WAL`
  - `synchronous=NORMAL`
  - `temp_store=MEMORY`
- Final compaction step after full sync:
  - `ANALYZE`
  - `VACUUM` (optional for scheduled maintenance)
- Acquire Blob lease or distributed lock to prevent overlapping timer runs
- Emit structured logs and custom metrics to Application Insights
- Use cancellation tokens and function timeout-aware checkpoints
- Publish immutable versioned blobs plus stable latest alias to reduce CDN stale-content risk

## 9. Validation and Tests
Unit tests:
- Tag parsing and normalization
- Dependency string parser (NuGet range variants)
- Version bound comparison logic

Integration tests:
- Seed mini dataset and validate SQL lookup behavior
- Verify incremental checkpoint update correctness
- Verify command search finds packages via `PSCommand_*` tags
- Verify Blob download/update/upload flow and atomic publish behavior
- Verify timer trigger idempotency with concurrent invocation attempts
- Verify manifest correctness and client download URL compatibility through CDN/static endpoint

Data-quality checks:
- Missing `Id`/`Version` rejection path
- Duplicate rows prevented by constraints

## 10. Azure Function Contract
Timer trigger:
- Function name: `SyncCacheTimerFunction`
- Trigger schedule: configurable CRON (`Sync:Schedule`)
- Behavior: download current DB, run incremental sync, upload new DB and manifest

No consumer query endpoint:
- Azure Function does not provide package search APIs
- Consumption path is SQLite file download via CDN/static endpoint

## 11. Milestones
1. Schema and repository layer complete
2. Function host and timer trigger complete
3. OData ingestion and parser complete
4. Incremental sync, checkpointing, and blob publish complete
5. CDN/static distribution validation, query verification, observability, and performance pass

## 12. Risks and Mitigations
- OData payload variation:
  - Keep parser defensive and nullable-friendly
- Dependency format edge cases:
  - Preserve original range text and fail-soft parsing
- Large gallery volume:
  - Stream pages and avoid loading all rows in memory
- Concurrent function executions:
  - Use Blob lease/distributed lock and idempotent upserts
- Partial artifact update risk:
  - Publish via staging blob then swap/copy on success
- CDN propagation delay or stale edge cache:
  - Version blob names and publish manifest containing current version and hash

## 13. Deliverables
- C# Azure Functions app for scheduled SQLite cache generation and blob publish
- SQLite schema and migration scripts
- Test suite covering parser and search/dependency behavior
- Operational notes for Azure deployment, scheduling, blob/CDN/static distribution, and monitoring
