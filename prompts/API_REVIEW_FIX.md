# API Streaming Fix Checklist

> **Reference** This action list is from the [API REVIEW](API_REVIEW.md)

Use this checklist to resolve memory pressure and correctness issues in streaming endpoints.

## Core Streaming Changes
- [ ] Replace full-buffer reads with true streaming (no `byte[]` payloads for tracks).
- [ ] Prefer `FileStreamResult`/`PhysicalFileResult` with `EnableRangeProcessing = true` where possible.
- [ ] For custom handling, stream via `FileStream` → `Response.Body`/`BodyWriter` in chunks (64–256 KB) with `FileOptions.Asynchronous | FileOptions.SequentialScan`.
- [ ] Remove `StreamResponse.Bytes` usage from hot paths; pass stream/file-path/offset metadata instead.

## Range Handling & Headers
- [ ] Implement robust Range parsing (strip `bytes=`, support `start-`, `-suffix`, and `start-end`).
- [ ] Validate ranges against file length; respond with 206 + correct `Content-Range` and `Content-Length`.
- [ ] Set `Accept-Ranges: bytes`, `Content-Type`, and caching headers consistently; avoid malformed `Content-Range`.
- [ ] Use `long` for file size and range math throughout; avoid casts to `int`.

## Cancellation & Resource Usage
- [ ] Respect `RequestAborted`/`CancellationToken` before opening/reading files; exit chunk loop on cancellation.
- [ ] Avoid allocating full track buffers; if buffering is unavoidable, gate by a small size threshold and use `ArrayPool<byte>`.
- [ ] Ensure `await using` for `FileStream` and other I/O resources; no retained references beyond the request lifetime.

## Controller Adjustments
- [ ] In `SongsController.StreamSong`, return `FileStreamResult` (or chunked copy) instead of writing a `byte[]`.
- [ ] Do not call `Response.Headers.Clear()`; only set/append required headers.
- [ ] Ensure correct status codes: 200 for full, 206 for partial content.
- [ ] Apply identical fixes to OpenSubsonic `MediaRetrievalController.StreamAsync` and `DownloadAsync`.

## Service Refactor
- [ ] Refactor `SongService.GetStreamForSongAsync` to a descriptor model (file path, length, content type, optional range) rather than returning bytes.
- [ ] Consolidate duplicated streaming logic into a single code path that both Melodee and OpenSubsonic controllers use.
- [ ] Double-check header construction after refactor; controllers should own response headers.

## ETag Repository Improvements
- [ ] Fix guard logic to use `&&` (both key and value required) in `AddEtag`/`EtagMatch`.
- [ ] Add eviction policy (LRU or time-based expiry) to bound memory; expose configurable limits.
- [ ] Add unit tests for add/match/eviction behavior.

## Testing & Validation
- [ ] Pre-change test readiness: ensure unit tests exist for all user-visible behaviors of streaming/download APIs (status codes, headers, range semantics, auth/permissions, content-type, content-disposition).
- [ ] Pre-change integration coverage: add/verify integration tests for core API consumer flows (stream, download, range requests, auth failure paths).
- [ ] Run full suite BEFORE refactor and capture baseline artifacts (test results, approval/snapshot files, sample response headers/statuses, byte counts).
- [ ] Post-change verification: rerun entire test suite AFTER changes; assert zero regressions in API behaviors used by consumers; only update snapshots when changes are intentional and documented.
- [ ] Unit tests for Range parser covering valid/invalid forms and edge cases.
- [ ] Integration tests: full file stream, partial range, client abort (cancellation) behavior.
- [ ] Large file test (>2 GB) to verify `long` math and headers.
- [ ] Concurrency/load test to verify bounded memory under N simultaneous streams.
 - [ ] Establish a baseline regression suite before refactor (capture current status codes, headers, and body lengths for representative endpoints: `SongsController.StreamSong`, `OpenSubsonic /rest/stream.view`, `/rest/download.view`).
 - [ ] Snapshot/approval tests for response headers and status codes for stream and download paths to detect accidental changes.
 - [ ] Contract tests to verify auth, blacklist, and permission behavior unchanged by streaming refactor.
 - [ ] Golden-file tests using small sample media to validate byte-accurate content for full and ranged responses.
 - [ ] Negative tests for invalid/malformed ranges (expect 416) and ranges beyond EOF (clamped or rejected per spec).
 - [ ] Cancellation tests that assert streaming stops early and does not complete full file read when client disconnects.
 - [ ] Backward-compat tests for OpenSubsonic clients (ensure accepted headers and 206 handling remain compatible).
 - [ ] Add optional performance check in CI (e.g., dotnet-counters or benchmark harness) to assert no large managed allocations during streaming paths.

## Benchmarking & CI Gates (BenchmarkDotNet)
- [ ] Create `benchmarks/Melodee.Benchmarks` project and add BenchmarkDotNet packages.
- [ ] Configure jobs for `net8.0`, Server GC, and add `[MemoryDiagnoser]` (and `[ThreadingDiagnoser]` if helpful).
- [ ] Implement microbenchmarks for hot paths:
  - [ ] Streaming loop: `FileStream` → sink (ArrayPool vs. new buffer; buffer sizes).
  - [ ] Range parsing and header construction.
  - [ ] Controller path comparison: `FileStreamResult` with `EnableRangeProcessing` vs. manual chunking (logic only, not full HTTP).
- [ ] Add runnable entrypoint and docs to execute: `dotnet run -c Release --project benchmarks/Melodee.Benchmarks`.
- [ ] Capture and commit a baseline JSON/CSV export for benchmarks (or store as artifact) to enable regression comparison.
- [ ] Add CI job to run benchmarks in Release, export JSON, and compare to baseline.
- [ ] Enforce thresholds in CI (fail build if):
  - [ ] Mean time regresses > 10% for any benchmark.
  - [ ] Allocated bytes/op increase > 5%.
  - [ ] New Gen1/Gen2 GCs appear where there were none.
- [ ] Make thresholds configurable to reduce flakiness and document override procedure.
- [ ] Optional: add an E2E Kestrel benchmark guarded by a flag; keep microbenchmarks the required gate.
- [ ] Add a “Performance” section to PR template requiring before/after benchmark snippets (time, ops/s, allocated bytes/op).

## Observability & Logging
- [ ] Metrics: in-flight streams, bytes streamed, average chunk size, duration, cancellations, and allocation estimates.
- [ ] Structured logs around range decisions, fallbacks, and early terminations.
- [ ] Dashboard/alerts for abnormal memory growth or streaming errors.

## Performance & Tuning
- [ ] Use `Response.StartAsync()` before streaming when applicable to reduce latency.
- [ ] Consider `SendFileAsync` fast-path when serving from disk without transformation.
- [ ] If manual chunking, experiment with buffer sizes and `ArrayPool<byte>` to minimize GC pressure.

## Security & Limits
- [ ] Validate request auth before any heavy I/O; keep blacklist checks early in the flow.
- [ ] Enforce per-user and global concurrent stream limits to avoid resource exhaustion.
- [ ] For downloads, set `Content-Disposition` properly and honor system download enable/disable setting.

## Compatibility & Rollout
- [ ] Verify existing clients with new 206/Range behavior and header changes.
- [ ] Provide a feature flag for streaming mode (buffered vs streaming) for safe rollout.
- [ ] Update API docs to reflect new behavior and headers.

## Documentation
- [ ] Document streaming architecture, range semantics, and cancellation behavior.
- [ ] Add troubleshooting guidance for memory usage and client compatibility.

## File/Area Mapping
- [ ] `src/Melodee.Blazor/Controllers/Melodee/SongsController.cs` (switch to streaming, remove header clearing).
- [ ] `src/Melodee.Common/Services/SongService.cs` (return descriptor, range/long math, no byte arrays).
- [ ] `src/Melodee.Blazor/Controllers/OpenSubsonic/MediaRetrievalController.cs` (streaming for stream/download).
- [ ] `src/Melodee.Common/Services/OpenSubsonicApiService.cs` (correct range parsing; delegate to unified streaming path).
- [ ] `src/Melodee.Blazor/Filters/EtagRepository.cs` (guard fix, eviction).
