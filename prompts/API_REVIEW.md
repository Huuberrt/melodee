# API Review: Streaming & Memory Concerns

Scope: Melodee.Blazor.Controllers.Melodee and OpenSubsonic controllers with focus on song streaming paths and related helpers.

## Overview
Over a 24 hour period the API was used by several dozen users each streaming songs continuously (over 10k stream were served.) This exposed some performance issues around streaming as the API server suffered a catastrophic failure as it stop responding to requests due memory consumption.

This document is an overview of the likely identified issues and proposed fixes.

## Summary
- Current streaming APIs load entire media files into memory as `byte[]` and then write them to the response. Under concurrency this causes high memory pressure (LOH allocations), GC churn, and can appear as memory leaks.
- Range handling is incomplete/incorrect in some paths, often falling back to full reads, amplifying memory usage.
- Some supporting components can retain memory longer than needed (e.g., ETag cache with no eviction), or have logic that risks exceptions and allocations.

## High‑Risk Endpoints
- `src/Melodee.Blazor/Controllers/Melodee/SongsController.cs` → `StreamSong`
  - Reads full track into memory via `songService.GetStreamForSongAsync(user, apiKey)` (the 1st overload) which allocates `byte[]` of entire file.
  - Writes entire buffer to `Response.Body.WriteAsync(...)` in one go.
  - Clears headers (`Response.Headers.Clear()`), then sets custom headers; not a leak but unnecessary and brittle.

- `src/Melodee.Common/Services/SongService.cs` → `GetStreamForSongAsync(...)`
  - Overload 1 (no range params) reads full file: `var bytesToRead = (int)s.FileSize; var trackBytes = new byte[bytesToRead]; ... fs.ReadAsync(...)` and returns `trackBytes` in `StreamResponse`.
  - Overload 2 supports ranges, but still allocates `byte[]` equal to the requested range size. No true stream semantics.
  - Casts file sizes to `int` which can overflow on large files; also guarantees LOH allocations for typical audio sizes (> 85 KB).

- `src/Melodee.Blazor/Controllers/OpenSubsonic/MediaRetrievalController.cs` → `StreamAsync` and `DownloadAsync`
  - Uses `OpenSubsonicApiService.StreamAsync(...)`, returning `byte[]`; writes entire buffer to `Response.Body` or returns `FileContentResult`.
  - Same memory profile as above.

## Root Causes of Memory Pressure/Leaks
- Full‑buffering of files in memory:
  - Entire tracks are read into `byte[]` and kept alive through service → controller → response write. With concurrent streams, total in‑flight allocation scales with N×trackSize.
  - Allocations land on the Large Object Heap (LOH), which is not compacted by default, causing fragmentation and elevated memory footprint that “looks like” a leak.

- Missing true streaming:
  - No `FileStreamResult`, `PhysicalFileResult`, `SendFileAsync`, or copy from `FileStream` to `Response.BodyWriter` in small chunks. Chunking would bound memory usage and reduce LOH allocations.

- Partial content/range implementation gaps:
  - Range parsing in `OpenSubsonicApiService.StreamAsync` doesn’t strip `bytes=` and attempts `long.TryParse` on the entire header value; this silently falls back to `0` and can lead to full‑file reads.
  - `SongsService` overload 1 returns `Content-Range: bytes 0-{FileSize}/{numberOfBytesRead}` which is malformed; mismatch can cause clients to retry or ignore ranges.

- Cancellation/early‑exit handling:
  - Actions accept a `CancellationToken` (bound to `RequestAborted`), but services always allocate the entire buffer before returning; cancellation has little effect once allocation/read begins.

- Auxiliary retention and logic issues:
  - `src/Melodee.Blazor/Filters/EtagRepository.cs` uses `||` instead of `&&` when validating inputs. This can attempt adds/checks with null keys/values (forced via `!`), causing exceptions and extra allocations. Also, the ETag store has no eviction policy and can grow unbounded over time.

## Recommendations

1) Stream directly from file to response
- Use `FileStreamResult` or `PhysicalFileResult` with `EnableRangeProcessing = true` where possible.
- Alternatively, open `FileStream` with `FileOptions.Asynchronous | FileOptions.SequentialScan` and copy to `Response.BodyWriter`/`Response.Body` in fixed‑size chunks (e.g., 64–256 KB) using `await stream.CopyToAsync(Response.Body, bufferSize, RequestAborted)`.
- Avoid ever materializing entire files into managed `byte[]` for live streaming paths.

2) Correct and unify range handling
- Parse `Range` header robustly: strip `bytes=`, handle `start-`, `-suffix`, and `start-end` forms; validate against file length; respond with `206 Partial Content`.
- Set `Content-Range`, `Content-Length`, `Accept-Ranges: bytes`, and `Content-Type` consistently; rely on framework helpers if using `FileStreamResult`.

3) Respect cancellation early
- Before allocating or reading, check `cancellationToken.IsCancellationRequested`.
- When streaming in chunks, break the loop on `RequestAborted` to avoid completing large reads once the client disconnects.

4) Remove full‑buffer `StreamResponse.Bytes`
- Redesign `StreamResponse` to carry stream metadata (headers, file path, range offsets) instead of `byte[]`. Let the controller produce a `FileStreamResult` or perform the chunked copy.
- If a byte array is truly needed (e.g., tiny images), gate it behind a size threshold; otherwise stream.

5) Fix ETag repository logic and growth
- Change guards to `if (!string.IsNullOrWhiteSpace(apiKeyId) && !string.IsNullOrWhiteSpace(etag))`.
- Consider bounded cache with eviction (LRU) or time‑based expiration to avoid unbounded growth.

6) Avoid `Response.Headers.Clear()`
- Do not clear all headers; instead, set/append only what’s needed. Clearing can remove server defaults and security headers.

7) Type safety and overflow
- Avoid casting file sizes to `int`; keep as `long` throughout range math and header construction.

## Quick Wins
- Switch controller responses to `PhysicalFileResult`/`FileStreamResult` with `EnableRangeProcessing = true` for both `StreamSong` and OpenSubsonic `StreamAsync`/`DownloadAsync`.
- Fix range header parsing in `OpenSubsonicApiService.StreamAsync` and the malformed `Content-Range` in `SongService`.
- Replace `Response.Headers.Clear()` with targeted header setting.

## Longer‑Term Improvements
- Introduce a streaming abstraction (e.g., a `StreamDescriptor` or `FileSegment`) that carries file path, offset, count, content type, and disposition, leaving the controller to stream.
- Use `ArrayPool<byte>` or pipelines when manual chunking is required for transcoding scenarios.
- Add integration tests for range requests and cancellation behavior; load test concurrent streaming.

## Observability
- Add metrics: in‑flight streams, bytes streamed, average chunk size, stream duration, cancellation count, and per‑request peak managed allocation.
- Add logging around range decisions, fallback to full reads, and early cancellations.

## Performance Benchmarking (BenchmarkDotNet)
To prevent performance regressions while implementing the changes above, add a microbenchmark suite using BenchmarkDotNet and gate key metrics (throughput and allocations).

1) Project setup
- Create a new project: `benchmarks/Melodee.Benchmarks`
- `dotnet new console -n Melodee.Benchmarks -o benchmarks/Melodee.Benchmarks`
- Add packages: `BenchmarkDotNet`, `BenchmarkDotNet.Diagnostics.Windows` (Windows only), and `BenchmarkDotNet.Extensions` if needed.
- Reference the code under test (e.g., `src/Melodee.Common` and any helpers you benchmark directly).

2) Benchmark configuration
- Use `[MemoryDiagnoser]` to capture allocations and GC counts.
- Consider `[ThreadingDiagnoser]` when measuring async copy behavior.
- Fix the runtime and JIT: configure jobs for `net8.0` with `Server GC` to mirror production.
- Prefer small, focused microbenchmarks of the core hot paths (file streaming copy, header construction), not full HTTP end‑to‑end.

Example benchmark
```csharp
using System.Buffers;
using System.IO;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

[SimpleJob(runtimeMoniker: RuntimeMoniker.Net80, launchCount: 1, warmupCount: 3, iterationCount: 8)]
[MemoryDiagnoser]
public class StreamingBenchmarks
{
    private const int BufferSize = 128 * 1024; // 128 KB
    private string _tempFile = default!;

    [Params(5_000_000, 50_000_000)] // 5 MB, 50 MB
    public int FileBytes;

    [GlobalSetup]
    public void Setup()
    {
        var data = new byte[FileBytes];
        new Random(42).NextBytes(data);
        _tempFile = Path.GetTempFileName();
        File.WriteAllBytes(_tempFile, data);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    [Benchmark(Baseline = true)]
    public async Task CopyWithArrayPool()
    {
        await using var input = new FileStream(_tempFile, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var sink = Stream.Null; // stand‑in for Response.Body
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, BufferSize))) > 0)
            {
                await sink.WriteAsync(buffer.AsMemory(0, read));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    [Benchmark]
    public async Task CopyWithNewBufferEachTime()
    {
        await using var input = new FileStream(_tempFile, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var sink = Stream.Null;
        var buffer = new byte[BufferSize];
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, BufferSize))) > 0)
        {
            await sink.WriteAsync(buffer.AsMemory(0, read));
        }
    }
}
```

3) How to run
- `dotnet run -c Release --project benchmarks/Melodee.Benchmarks -- --filter *StreamingBenchmarks*`
- Artifacts (summaries, CSV/Markdown) are emitted under `benchmarks/Melodee.Benchmarks/BenchmarkDotNet.Artifacts`.

4) Metrics to watch
- Mean/Median time (lower is better) and Throughput (ops/s).
- Allocated bytes/op and Gen0/Gen1/Gen2 counts (should trend down with streaming improvements).
- P95/P99 outliers if present; watch for high variance.

5) Regression gates in CI
- Add a CI job that runs the benchmark suite in `Release` on a dedicated runner.
- Export results to JSON (`--exporters json`) and compare against a checked‑in baseline JSON.
- Fail the build if:
  - Mean time regresses by more than +10% on any benchmark.
  - Allocated bytes/op increases by more than +5%.
  - Any benchmark introduces new Gen1/Gen2 collections.
- Simple approach: a small verification step (script) that parses BenchmarkDotNet JSON and enforces thresholds. Keep thresholds configurable.

6) Scope of benchmarks
- Focus on microbenchmarks for the streaming loop, range math, and header formatting.
- For end‑to‑end, consider a lightweight Kestrel self‑hosted benchmark only after microbaselines are stable; keep it optional due to noise.

7) Reporting
- Include a “Performance” section in PRs with a table of before/after for the changed benchmarks (time, throughput, allocated bytes/op).
- Track trends over time by committing baseline artifacts per release or by publishing to a dashboard.

## Compatibility Notes
- Moving to framework streaming will change how headers are set; verify clients that depend on existing custom headers.
- If enabling range processing, ensure clients handle `206 Partial Content` and `Content-Range` correctly.

## Files Reviewed
- `src/Melodee.Blazor/Controllers/Melodee/SongsController.cs`
- `src/Melodee.Common/Services/SongService.cs`
- `src/Melodee.Blazor/Controllers/OpenSubsonic/MediaRetrievalController.cs`
- `src/Melodee.Common/Services/OpenSubsonicApiService.cs`
- `src/Melodee.Blazor/Filters/EtagRepository.cs`
