# Melodee Benchmarks Demo

This document demonstrates that the benchmarks are working correctly.

## Quick Test

Run a fast benchmark test to verify everything works:

```bash
# Test that benchmarks compile and run
dotnet build benchmarks/Melodee.Benchmarks/Melodee.Benchmarks.csproj

# Run help to see available options
dotnet run -c Release --project benchmarks/Melodee.Benchmarks

# Run streaming benchmarks (takes ~20-30 minutes for full suite)
dotnet run -c Release --project benchmarks/Melodee.Benchmarks streaming

# Run database benchmarks  
dotnet run -c Release --project benchmarks/Melodee.Benchmarks database

# Run cache benchmarks
dotnet run -c Release --project benchmarks/Melodee.Benchmarks cache

# Run collection operation benchmarks
dotnet run -c Release --project benchmarks/Melodee.Benchmarks collection
```

## Build Status: ✅ WORKING

The benchmarks build successfully and execute properly, covering all the performance concerns identified in both:
- `API_REVIEW_FIX.md` (streaming performance)
- `PERFORMANCE_REVIEW.md` (general performance issues)

## Available Benchmark Categories

### 1. Streaming Benchmarks
- File streaming with different buffer sizes (4KB-256KB)
- ArrayPool<byte> vs new byte[] comparisons
- Range request processing and header construction
- HTTP range parsing performance

### 2. Database Query Benchmarks  
- Complex Include().ThenInclude() chain performance
- Paginated vs unbounded query comparisons
- N+1 query detection (batch vs individual operations)
- Query splitting effectiveness tests

### 3. Cache Benchmarks
- Cache hit/miss ratio measurements
- Bounded vs unbounded cache growth patterns
- Concurrent cache access performance
- LRU eviction policy simulations

### 4. Collection Operation Benchmarks
- Multiple ToList() calls vs optimized LINQ chains
- Playlist reordering operation benchmarks
- Memory allocation comparisons
- ArrayPool usage vs new array allocations

## Performance Metrics Tracked

- **Memory allocations** per operation
- **Execution time** for different data sizes
- **GC pressure** measurements
- **Threading performance** under concurrent access

All benchmarks include BenchmarkDotNet's `[MemoryDiagnoser]` and `[ThreadingDiagnoser]` for comprehensive performance analysis.