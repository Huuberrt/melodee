# Melodee Benchmarks

This project contains performance benchmarks for the Melodee music streaming system, addressing performance concerns identified in the API and Performance reviews.

## Quick Start

```bash
# Run all benchmarks
dotnet run -c Release --project benchmarks/Melodee.Benchmarks all

# Run specific benchmark categories
dotnet run -c Release --project benchmarks/Melodee.Benchmarks streaming
dotnet run -c Release --project benchmarks/Melodee.Benchmarks database
dotnet run -c Release --project benchmarks/Melodee.Benchmarks cache
dotnet run -c Release --project benchmarks/Melodee.Benchmarks collection

# Run with custom exporters and artifacts path
dotnet run -c Release --project benchmarks/Melodee.Benchmarks all -- --exporters json,github,csv --artifacts benchmarks/artifacts
dotnet run -c Release --project benchmarks/Melodee.Benchmarks streaming -- --exporters json,csv --artifacts results/streaming
```

## Configuration Options

### Exporters
The benchmark project supports multiple output formats:
- `json`: JSON format for programmatic analysis
- `csv`: CSV format for spreadsheet analysis  
- `github`: GitHub-flavored markdown tables
- `html`: HTML reports for web viewing

**Usage**: `--exporters json,csv,github,html`

### Artifacts Path
Specify where benchmark results are saved:
- Default: BenchmarkDotNet creates temporary directories
- Custom: `--artifacts your/custom/path`

**Usage**: `--artifacts benchmarks/artifacts`

### Combined Example
```bash
dotnet run -c Release --project benchmarks/Melodee.Benchmarks all -- --exporters json,github,csv --artifacts benchmarks/artifacts
```

## Benchmark Categories

### 1. Streaming Benchmarks (`streaming`)
**Addresses**: API_REVIEW_FIX.md streaming performance requirements

- **File Streaming Performance**: Tests different buffer sizes and streaming approaches
- **Range Request Processing**: Benchmarks HTTP range header parsing and processing
- **Buffer Management**: Compares new byte[] vs ArrayPool<byte> performance
- **Memory Allocation**: Measures memory usage during streaming operations

**Key Metrics**:
- Throughput (MB/s)
- Memory allocations per operation
- Buffer size optimization (4KB to 256KB tested)

### 2. Database Query Benchmarks (`database`)
**Addresses**: PERFORMANCE_REVIEW.md database performance concerns

- **Complex Query Patterns**: Tests nested Include().ThenInclude() chains
- **Pagination Performance**: Compares paginated vs unbounded queries
- **N+1 Query Detection**: Benchmarks batch vs individual database operations
- **Query Splitting**: Tests AsSplitQuery() effectiveness

**Key Metrics**:
- Query execution time
- Memory usage for large datasets
- Database connection pool efficiency

### 3. Cache Benchmarks (`cache`)
**Addresses**: PERFORMANCE_REVIEW.md caching concerns

- **Cache Hit/Miss Ratios**: Measures cache effectiveness under load
- **Eviction Policies**: Tests LRU vs time-based eviction strategies
- **Concurrent Access**: Benchmarks cache performance under concurrent load
- **Unbounded Growth**: Tests memory leak patterns in caches

**Key Metrics**:
- Cache hit ratio (target >90%)
- Memory growth patterns
- Concurrent access performance

### 4. Collection Operation Benchmarks (`collection`)
**Addresses**: PERFORMANCE_REVIEW.md collection efficiency concerns

- **LINQ Optimization**: Tests multiple ToList() calls vs optimized chains
- **Playlist Reordering**: Benchmarks the inefficient PlaylistService patterns
- **Memory Allocation**: Compares collection operation memory usage
- **Bulk Operations**: Tests batch vs individual operations

**Key Metrics**:
- Memory allocations per operation
- Execution time for collection manipulations
- GC pressure measurements

## Understanding Results

### Memory Diagnoser Output
- **Allocated**: Memory allocated during the benchmark
- **Gen 0/1/2**: Garbage collection counts for each generation
- **Gen 0-2**: Total GC collections

### Performance Targets
Based on performance review requirements:

- **Database Queries**: <100ms for complex queries
- **Cache Hit Ratio**: >90% for frequently accessed data
- **Memory Usage**: <512MB for large dataset operations
- **Streaming**: >80% memory reduction vs full-load approaches

## Interpreting Benchmark Results

### Streaming Benchmarks
- **Lower allocation = Better**: Look for benchmarks with minimal memory allocation
- **Buffer Size Sweet Spot**: Usually 64KB-256KB for optimal throughput vs memory usage
- **ArrayPool Benefits**: Should show reduced allocations vs new byte[] arrays

### Database Benchmarks
- **Pagination Benefits**: Paginated queries should show linear memory growth
- **Split Query Performance**: Should reduce memory usage for complex joins
- **Batch Operations**: Should show better performance than N+1 patterns

### Cache Benchmarks
- **Bounded vs Unbounded**: Bounded caches should show stable memory usage
- **Hit Ratio Impact**: Higher hit ratios should correlate with better performance
- **Concurrent Performance**: Should scale with thread count without significant degradation

### Collection Benchmarks
- **LINQ Optimization**: Single-pass operations should outperform multiple ToList() calls
- **Memory Efficiency**: Optimized versions should show reduced allocations
- **Reusable Collections**: Should show better GC characteristics

## Running Specific Tests

The new configuration supports BenchmarkDotNet's native filtering and parameter options:

```bash
# Run specific benchmark category with custom output
dotnet run -c Release --project benchmarks/Melodee.Benchmarks streaming -- --exporters json --artifacts results/streaming-only

# Run database benchmarks with artifacts path
dotnet run -c Release --project benchmarks/Melodee.Benchmarks database -- --artifacts database-results

# Multiple categories with full export options
dotnet run -c Release --project benchmarks/Melodee.Benchmarks cache -- --exporters json,csv,github --artifacts benchmarks/cache-artifacts
```

**Note**: The `--filter` and `--params` options mentioned in the original documentation are standard BenchmarkDotNet features that require direct BenchmarkDotNet CLI usage rather than this wrapper.

## Baseline Data

Benchmark results should be compared against baseline measurements. The project includes:

- Initial baseline JSON/CSV exports in `/benchmarks/results/`
- Historical performance tracking
- Regression detection capabilities

## Adding New Benchmarks

1. Create a new benchmark class implementing the appropriate patterns
2. Add the `[SimpleJob(RuntimeMoniker.Net90)]`, `[MemoryDiagnoser]`, and `[ThreadingDiagnoser]` attributes
3. Include setup/cleanup methods for proper resource management
4. Add meaningful parameter variations with `[Params()]`
5. Update the Program.cs switch statement to include the new benchmark category

## Integration with CI/CD

The benchmarks can be integrated into continuous integration:

```bash
# Run benchmarks and export results to a consistent location
dotnet run -c Release --project benchmarks/Melodee.Benchmarks all -- --exporters json,csv --artifacts benchmarks/artifacts

# Run specific categories for faster CI feedback
dotnet run -c Release --project benchmarks/Melodee.Benchmarks streaming -- --exporters json --artifacts ci-results

# Compare against baseline (future enhancement)
# dotnet run -c Release -- --baseline baseline.json --threshold 10%
```

### CI Output Structure
When using `--artifacts benchmarks/artifacts`, results are organized as:
```
benchmarks/artifacts/
├── results/
│   ├── Melodee.Benchmarks.StreamingBenchmarks-report.json
│   ├── Melodee.Benchmarks.StreamingBenchmarks-report.csv
│   └── Melodee.Benchmarks.StreamingBenchmarks-report-github.md
└── Melodee.Benchmarks.StreamingBenchmarks-20231203-142035.log
```

## Troubleshooting

### Common Issues

1. **OutOfMemoryException**: Reduce dataset sizes in benchmark parameters
2. **Slow Execution**: Ensure running in Release mode with optimizations enabled
3. **Inconsistent Results**: Run multiple iterations and check for background processes

### Performance Tips

- Always run benchmarks in **Release** mode
- Close other applications during benchmarking
- Run on a dedicated machine for consistent results
- Allow JIT warmup by running multiple iterations

## References

- [BenchmarkDotNet Documentation](https://benchmarkdotnet.org/)
- [API_REVIEW_FIX.md](../../prompts/API_REVIEW_FIX.md) - Streaming performance requirements
- [PERFORMANCE_REVIEW.md](../../prompts/PERFORMANCE_REVIEW.md) - General performance concerns