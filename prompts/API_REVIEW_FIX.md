# API Streaming Fix Checklist

> **Reference** This action list is from the [API REVIEW](API_REVIEW.md)

Use this checklist to resolve memory pressure and correctness issues in streaming endpoints.

## Core Streaming Changes
- [x] Replace full-buffer reads with true streaming (no `byte[]` payloads for tracks).
- [x] Prefer `FileStreamResult`/`PhysicalFileResult` with `EnableRangeProcessing = true` where possible.
- [x] For custom handling, stream via `FileStream` → `Response.Body`/`BodyWriter` in chunks (64–256 KB) with `FileOptions.Asynchronous | FileOptions.SequentialScan`.
- [x] Remove `StreamResponse.Bytes` usage from hot paths; pass stream/file-path/offset metadata instead.

## Range Handling & Headers
- [x] Implement robust Range parsing (strip `bytes=`, support `start-`, `-suffix`, and `start-end`).
- [x] Validate ranges against file length; respond with 206 + correct `Content-Range` and `Content-Length`.
- [x] Set `Accept-Ranges: bytes`, `Content-Type`, and caching headers consistently; avoid malformed `Content-Range`.
- [x] Use `long` for file size and range math throughout; avoid casts to `int`.

## Cancellation & Resource Usage
- [x] Respect `RequestAborted`/`CancellationToken` before opening/reading files; exit chunk loop on cancellation.
- [x] Avoid allocating full track buffers; if buffering is unavoidable, gate by a small size threshold and use `ArrayPool<byte>`.
- [x] Ensure `await using` for `FileStream` and other I/O resources; no retained references beyond the request lifetime.

## Controller Adjustments
- [x] In `SongsController.StreamSong`, return `FileStreamResult` (or chunked copy) instead of writing a `byte[]`.
- [x] Do not call `Response.Headers.Clear()`; only set/append required headers.
- [x] Ensure correct status codes: 200 for full, 206 for partial content.
- [x] Apply identical fixes to OpenSubsonic `MediaRetrievalController.StreamAsync` and `DownloadAsync`.

## Service Refactor
- [x] Refactor `SongService.GetStreamForSongAsync` to a descriptor model (file path, length, content type, optional range) rather than returning bytes.
- [x] Consolidate duplicated streaming logic into a single code path that both Melodee and OpenSubsonic controllers use.
- [x] Double-check header construction after refactor; controllers should own response headers.

## ETag Repository Improvements
- [x] Fix guard logic to use `&&` (both key and value required) in `AddEtag`/`EtagMatch`.
- [x] Add eviction policy (LRU or time-based expiry) to bound memory; expose configurable limits.
- [x] Add unit tests for add/match/eviction behavior.

## Testing & Validation
- [x] Pre-change test readiness: ensure unit tests exist for all user-visible behaviors of streaming/download APIs (status codes, headers, range semantics, auth/permissions, content-type, content-disposition).
- [x] Pre-change integration coverage: add/verify integration tests for core API consumer flows (stream, download, range requests, auth failure paths).
- [x] Run full suite BEFORE refactor and capture baseline artifacts (test results, approval/snapshot files, sample response headers/statuses, byte counts).
- [x] Post-change verification: rerun entire test suite AFTER changes; assert zero regressions in API behaviors used by consumers; only update snapshots when changes are intentional and documented.
- [x] Unit tests for Range parser covering valid/invalid forms and edge cases.
- [x] Integration tests: full file stream, partial range, client abort (cancellation) behavior.
- [x] Large file test (>2 GB) to verify `long` math and headers.
- [ ] Concurrency/load test to verify bounded memory under N simultaneous streams.
 - [x] Establish a baseline regression suite before refactor (capture current status codes, headers, and body lengths for representative endpoints: `SongsController.StreamSong`, `OpenSubsonic /rest/stream.view`, `/rest/download.view`).
 - [x] Snapshot/approval tests for response headers and status codes for stream and download paths to detect accidental changes.
 - [x] Contract tests to verify auth, blacklist, and permission behavior unchanged by streaming refactor.
 - [x] Golden-file tests using small sample media to validate byte-accurate content for full and ranged responses.
 - [x] Negative tests for invalid/malformed ranges (expect 416) and ranges beyond EOF (clamped or rejected per spec).
 - [x] Cancellation tests that assert streaming stops early and does not complete full file read when client disconnects.
 - [x] Backward-compat tests for OpenSubsonic clients (ensure accepted headers and 206 handling remain compatible).
 - [ ] Add optional performance check in CI (e.g., dotnet-counters or benchmark harness) to assert no large managed allocations during streaming paths.

## Benchmarking 
- [ ] Create `benchmarks/Melodee.Benchmarks` project and add BenchmarkDotNet packages.
- [ ] Configure jobs for `net9.0`, Server GC, and add `[MemoryDiagnoser]` (and `[ThreadingDiagnoser]` if helpful).
- [ ] Implement microbenchmarks for hot paths:
  - [ ] Streaming loop: `FileStream` → sink (ArrayPool vs. new buffer; buffer sizes).
  - [ ] Range parsing and header construction.
  - [ ] Controller path comparison: `FileStreamResult` with `EnableRangeProcessing` vs. manual chunking (logic only, not full HTTP).
- [ ] Add runnable entrypoint and docs to execute: `dotnet run -c Release --project benchmarks/Melodee.Benchmarks`.
- [ ] Capture and commit a baseline JSON/CSV export for benchmarks (or store as artifact) to enable regression comparison.

## Observability & Logging
- [ ] Metrics: in-flight streams, bytes streamed, average chunk size, duration, cancellations, and allocation estimates.
- [ ] Structured logs around range decisions, fallbacks, and early terminations.
- [ ] Dashboard/alerts for abnormal memory growth or streaming errors.

## Performance & Tuning
- [ ] Use `Response.StartAsync()` before streaming when applicable to reduce latency.
- [ ] Consider `SendFileAsync` fast-path when serving from disk without transformation.
- [ ] If manual chunking, experiment with buffer sizes and `ArrayPool<byte>` to minimize GC pressure.

## Security & Limits
- [x] Validate request auth before any heavy I/O; keep blacklist checks early in the flow.
- [x] Enforce per-user and global concurrent stream limits to avoid resource exhaustion.
- [x] For downloads, set `Content-Disposition` properly and honor system download enable/disable setting.

## Compatibility & Rollout
- [ ] Verify existing clients with new 206/Range behavior and header changes.
- [x] Provide a feature flag for streaming mode (buffered vs streaming) for safe rollout.
- [ ] Update API docs to reflect new behavior and headers.

## Documentation
- [ ] Document streaming architecture, range semantics, and cancellation behavior.
- [ ] Add troubleshooting guidance for memory usage and client compatibility.

## File/Area Mapping
- [x] `src/Melodee.Blazor/Controllers/Melodee/SongsController.cs` (switch to streaming, remove header clearing).
- [x] `src/Melodee.Common/Services/SongService.cs` (return descriptor, range/long math, no byte arrays).
- [x] `src/Melodee.Blazor/Controllers/OpenSubsonic/MediaRetrievalController.cs` (streaming for stream/download).
- [x] `src/Melodee.Common/Services/OpenSubsonicApiService.cs` (correct range parsing; delegate to unified streaming path).
- [x] `src/Melodee.Blazor/Filters/EtagRepository.cs` (guard fix, eviction).
