# Melodee Performance Review & Action Plan

## Overview
This document outlines identified performance and memory concerns in the Melodee music streaming system, organized by impact level with specific action items and testing requirements.

---

## HIGH IMPACT Issues (Priority 1)

### 1. Complex Database Query Patterns
**Impact**: Critical - Can cause significant database performance degradation and memory usage

**Files Affected**: 
- `src/Melodee.Common/Services/PlaylistService.cs:691-692`
- `src/Melodee.Common/Services/SongService.cs:314-315`
- `src/Melodee.Common/Services/UserService.cs:2000-2001`

**Issues**:
- [ ] **P1.1**: Refactor nested `Include().ThenInclude().ThenInclude()` chains in PlaylistService
- [ ] **P1.2**: Optimize SongService complex joins with Contributors and Albums
- [ ] **P1.3**: Review UserService song loading with multiple includes
- [ ] **P1.4**: Implement query result pagination for large datasets
- [ ] **P1.5**: Add query performance monitoring and logging

**Testing Requirements**:
- [ ] **T1.1**: ❌ **MISSING** - Add unit tests for query execution time under various data sizes
  ```csharp
  // Required: Create PlaylistServicePerformanceTests.cs
  [Fact] public async Task GetPlaylistWithComplexIncludes_WithLargeDataset_CompletesWithinTimeLimit()
  // Test nested Include().ThenInclude().ThenInclude() with 1K+ songs
  ```
- [ ] **T1.2**: ❌ **MISSING** - Create integration tests that verify query result limits  
  ```csharp
  // Required: Add to PlaylistServiceTests.cs
  [Fact] public async Task GetPlaylists_WithUnboundedQuery_ThrowsOrLimitsResults()
  ```
- [ ] **T1.3**: ❌ **MISSING** - Add memory usage tests for complex query scenarios
  ```csharp
  // Required: Create MemoryUsageTests.cs with BenchmarkDotNet
  [MemoryDiagnoser] public class DatabaseQueryMemoryTests
  ```
- [ ] **T1.4**: ❌ **MISSING** - Implement load tests for database query performance
  ```csharp
  // Required: Create LoadTests/DatabaseQueryLoadTests.cs with NBomber
  NBomberRunner.RegisterScenarios(playlistQueryScenario).Run()
  ```

### 2. N+1 Query Potential in Parallel Processing
**Impact**: Critical - Database connection pool exhaustion and performance degradation

**Files Affected**:
- `src/Melodee.Common/MessageBus/EventHandlers/ArtistRescanEventHandler.cs:75`
- `src/Melodee.Common/Services/Scanning/AlbumDiscoveryService.cs:148`
- `src/Melodee.Common/Services/Scanning/DirectoryProcessorToStagingService.cs:481`

**Issues**:
- [ ] **P2.1**: Review parallel database operations in ArtistRescanEventHandler
- [ ] **P2.2**: Optimize AlbumDiscoveryService parallel directory processing
- [ ] **P2.3**: Implement batch operations instead of individual database calls in parallel loops
- [ ] **P2.4**: Add connection pool monitoring and alerting
- [ ] **P2.5**: Configure appropriate MaxDegreeOfParallelism based on connection pool size

**Testing Requirements**:
- [ ] **T2.1**: ❌ **MISSING** - Create stress tests for parallel database operations
  ```csharp
  // Required: Create ParallelProcessingTests.cs
  [Fact] public async Task ArtistRescan_WithParallelAlbumProcessing_DoesNotExhaustConnectionPool()
  // Test with MaxDegreeOfParallelism and connection pool monitoring
  ```
- [ ] **T2.2**: ❌ **MISSING** - Add connection pool exhaustion detection tests
  ```csharp
  // Required: Add to AlbumDiscoveryServiceTests.cs  
  [Fact] public async Task ProcessDirectoriesInParallel_UnderHighLoad_MaintainsConnectionPoolHealth()
  ```
- [ ] **T2.3**: ❌ **MISSING** - Implement performance benchmarks for batch vs individual operations
  ```csharp
  // Required: Create BatchOperationBenchmarks.cs with BenchmarkDotNet
  [Benchmark] public async Task IndividualDatabaseCalls_vs_BatchOperations()
  ```
- [ ] **T2.4**: ❌ **MISSING** - Add integration tests that verify no N+1 queries under parallel load
  ```csharp
  // Required: Create N+1QueryDetectionTests.cs with EF Core logging
  [Fact] public async Task ParallelFileProcessing_DoesNotTriggerN1Queries()
  ```

### 3. Large Object Loading Without Pagination
**Impact**: Critical - Memory exhaustion with large music libraries

**Files Affected**:
- `src/Melodee.Common/Services/PlaylistService.cs:697`
- Various service methods using `ToArrayAsync()` without limits

**Issues**:
- [ ] **P3.1**: Add pagination to playlist loading in PlaylistService
- [ ] **P3.2**: Implement result size limits for all collection queries
- [ ] **P3.3**: Add memory-efficient streaming for large result sets
- [ ] **P3.4**: Review and limit all `ToArrayAsync()` and `ToListAsync()` calls
- [ ] **P3.5**: Implement lazy loading where appropriate

**Testing Requirements**:
- [ ] **T3.1**: ❌ **MISSING** - Create memory usage tests with large datasets (>10k songs, >1k playlists)
  ```csharp
  // Required: Create LargeDatasetMemoryTests.cs
  [Fact] public async Task LoadPlaylistWithThousandsOfSongs_DoesNotExceedMemoryThreshold()
  // Use GC.GetTotalMemory() before/after with 10K+ test data
  ```
- [ ] **T3.2**: ✅ **EXISTS** - Add pagination correctness tests  
  ```csharp
  // Current: PlaylistServiceTests.cs:30-50 has basic pagination tests
  // Enhancement needed: Test with larger datasets and edge cases
  ```
- [ ] **T3.3**: ❌ **MISSING** - Implement memory leak detection tests for large operations
  ```csharp
  // Required: Create MemoryLeakDetectionTests.cs
  [Fact] public async Task RepeatedLargeQueryExecution_DoesNotLeakMemory()
  // Run multiple iterations and verify memory returns to baseline
  ```
- [ ] **T3.4**: ❌ **MISSING** - Add performance benchmarks comparing paginated vs non-paginated queries
  ```csharp
  // Required: Create PaginationBenchmarks.cs with BenchmarkDotNet
  [Benchmark] public async Task PaginatedQuery_vs_FullDatasetQuery()
  ```

---

## MEDIUM IMPACT Issues (Priority 2)

### 4. In-Memory Caching Without Bounds
**Impact**: High - Memory leaks over time as caches grow unbounded

**Files Affected**:
- `src/Melodee.Common/Services/Scanning/AlbumDiscoveryService.cs:40`
- `src/Melodee.Blazor/Filters/EtagRepository.cs:7`
- `src/Melodee.Common/Plugins/Scrobbling/NowPlayingInMemoryRepository.cs:12`

**Issues**:
- [ ] **P4.1**: Add size limits to AlbumDiscoveryService directory cache
- [ ] **P4.2**: Implement expiration policy for ETagRepository cache
- [ ] **P4.3**: Add bounded cache for NowPlayingInMemoryRepository
- [ ] **P4.4**: Implement cache eviction strategies (LRU, time-based)
- [ ] **P4.5**: Add cache hit/miss ratio monitoring

**Testing Requirements**:
- [ ] **T4.1**: ❌ **MISSING** - Create cache bounds enforcement tests
  ```csharp
  // Required: Enhance MemoryCacheManagerTests.cs
  [Fact] public async Task Cache_WithSizeLimitExceeded_EvictsOldestEntries()
  // Test cache size limits and eviction policies
  ```
- [ ] **T4.2**: ❌ **MISSING** - Add cache eviction policy verification tests
  ```csharp
  // Required: Add to AlbumDiscoveryServiceTests.cs
  [Fact] public async Task DirectoryCache_WithTimeBasedEviction_RemovesExpiredEntries()
  ```
- [ ] **T4.3**: ❌ **MISSING** - Implement long-running cache growth tests
  ```csharp
  // Required: Create CacheGrowthTests.cs
  [Fact] public async Task UnboundedCache_OverExtendedPeriod_DoesNotGrowIndefinitely()
  // Run for hours/days to detect memory leaks
  ```
- [ ] **T4.4**: ❌ **MISSING** - Add cache performance metrics validation tests
  ```csharp
  // Required: Create CacheMetricsTests.cs
  [Fact] public async Task CacheHitRatio_UnderTypicalLoad_MeetsPerformanceThreshold()
  ```

### 5. Inefficient Collection Operations
**Impact**: Medium - Unnecessary memory allocations and CPU cycles

**Files Affected**:
- `src/Melodee.Common/Services/PlaylistService.cs:586-600`
- Various files with multiple `ToList()` calls

**Issues**:
- [ ] **P5.1**: Optimize playlist song reordering operations
- [ ] **P5.2**: Reduce multiple `ToList()` calls in single methods
- [ ] **P5.3**: Use more efficient collection operations (spans, memory)
- [ ] **P5.4**: Implement bulk update operations for collections
- [ ] **P5.5**: Review LINQ chains for optimization opportunities

**Testing Requirements**:
- [ ] **T5.1**: ❌ **MISSING** - Add performance benchmarks for collection operations
  ```csharp
  // Required: Create CollectionOperationBenchmarks.cs
  [Benchmark] public void PlaylistReordering_MultipleToListCalls_vs_OptimizedVersion()
  // Test PlaylistService.cs:586-600 reordering operations
  ```
- [ ] **T5.2**: ❌ **MISSING** - Create memory allocation tests for collection manipulations
  ```csharp
  // Required: Add [MemoryDiagnoser] to collection operation tests
  [Fact] public void CollectionOperations_DoNotExcessivelyAllocate()
  ```
- [ ] **T5.3**: ❌ **MISSING** - Implement correctness tests for optimized collection operations
  ```csharp
  // Required: Add to PlaylistServiceTests.cs
  [Fact] public async Task OptimizedPlaylistReordering_ProducesSameResultAsOriginal()
  ```
- [ ] **T5.4**: ❌ **MISSING** - Add comparative performance tests (before/after optimization)
  ```csharp
  // Required: Create BeforeAfterOptimizationTests.cs
  [Benchmark(Baseline = true)] public void Original_vs_Optimized_CollectionOperations()
  ```

### 6. Docker Configuration Concerns
**Impact**: Medium - Higher memory usage and potential connection bottlenecks

**Files Affected**:
- `Dockerfile:29` (using SDK image in production)
- `compose.yml:33` (fixed connection pool configuration)

**Issues**:
- [ ] **P6.1**: Create optimized production Dockerfile using runtime image
- [ ] **P6.2**: Make database connection pool size configurable
- [ ] **P6.3**: Add container resource limits and monitoring
- [ ] **P6.4**: Optimize container startup time and resource usage
- [ ] **P6.5**: Implement health checks and readiness probes

**Testing Requirements**:
- [ ] **T6.1**: ❌ **MISSING** - Add container startup time performance tests
  ```bash
  # Required: Create integration/DockerPerformanceTests.sh
  # Measure container startup times and resource usage
  ```
- [ ] **T6.2**: ❌ **MISSING** - Create memory usage comparison tests (SDK vs runtime)
  ```bash
  # Required: Add Docker memory usage benchmarks
  # Compare SDK vs runtime image memory footprint
  ```
- [ ] **T6.3**: ❌ **MISSING** - Implement connection pool configuration validation tests
  ```csharp
  // Required: Create DatabaseConnectionTests.cs
  [Fact] public async Task ConnectionPool_UnderHighLoad_HandlesConfiguredMaxConnections()
  ```
- [ ] **T6.4**: ❌ **MISSING** - Add container resource consumption monitoring tests
  ```bash
  # Required: Create monitoring/ContainerResourceTests.sh
  # Monitor CPU, memory, I/O during typical operations
  ```

### 7. File Processing Without Streaming
**Impact**: Medium - Memory spikes during large file processing

**Files Affected**:
- Various file processing operations throughout the codebase

**Issues**:
- [ ] **P7.1**: Implement streaming for large audio file processing
- [ ] **P7.2**: Add memory-efficient image processing for album art
- [ ] **P7.3**: Optimize metadata extraction for large files
- [ ] **P7.4**: Implement progressive file upload handling
- [ ] **P7.5**: Add file size limits and validation

**Testing Requirements**:
- [ ] **T7.1**: ❌ **MISSING** - Create large file processing memory tests
  ```csharp
  // Required: Create LargeFileProcessingTests.cs
  [Fact] public async Task ProcessLargeAudioFile_DoesNotExceedMemoryLimit()
  // Test with files >100MB
  ```
- [ ] **T7.2**: ❌ **MISSING** - Add streaming operation correctness tests
  ```csharp
  // Required: Create StreamingOperationTests.cs
  [Fact] public async Task StreamedFileProcessing_ProducesSameResultAsLoadingFully()
  ```
- [ ] **T7.3**: ❌ **MISSING** - Implement file processing performance benchmarks
  ```csharp
  // Required: Create FileProcessingBenchmarks.cs
  [Benchmark] public async Task StreamedProcessing_vs_FullLoad_Performance()
  ```
- [ ] **T7.4**: ❌ **MISSING** - Add memory usage monitoring during file operations
  ```csharp
  // Required: Add to existing file processing tests
  // Monitor GC.GetTotalMemory() during file operations
  ```

---

## LOW IMPACT Issues (Priority 3)

### 8. Missing Query Splitting Configuration
**Impact**: Low - Occasional query performance degradation

**Issues**:
- [ ] **P8.1**: Review all complex queries for appropriate `AsSplitQuery()` usage
- [ ] **P8.2**: Add consistent query splitting strategy
- [ ] **P8.3**: Document when to use split vs single queries

**Testing Requirements**:
- [ ] **T8.1**: Add query splitting effectiveness tests
- [ ] **T8.2**: Create performance comparison tests (split vs single queries)

### 9. Synchronous Operations in Async Context
**Impact**: Low - Reduced throughput under high concurrency

**Issues**:
- [ ] **P9.1**: Identify and convert blocking calls to async
- [ ] **P9.2**: Review thread pool usage patterns
- [ ] **P9.3**: Add async best practices documentation

**Testing Requirements**:
- [ ] **T9.1**: Add async/sync operation detection tests
- [ ] **T9.2**: Create thread pool starvation detection tests

### 10. Inconsistent AsNoTracking() Usage
**Impact**: Low - Minor memory savings opportunity

**Issues**:
- [ ] **P10.1**: Standardize `AsNoTracking()` usage across all read-only queries
- [ ] **P10.2**: Add code analysis rules for tracking usage

**Testing Requirements**:
- [ ] **T10.1**: Add automated detection for missing `AsNoTracking()` in read queries

---

## Testing Coverage Analysis

### Current Test Coverage Status
Based on analysis of existing tests in `/tests/`, the project has:
- ✅ **Good Coverage**: Basic service functionality, cache invalidation, pagination
- ✅ **Existing Performance Tests**: Some timing tests using `Stopwatch` in OpenSubsonicApiService and DirectoryProcessorToStagingService
- ✅ **Cache Testing**: MemoryCacheManager has comprehensive cache functionality tests
- ❌ **Missing**: Dedicated performance benchmarking framework
- ❌ **Missing**: Memory usage and leak detection tests
- ❌ **Missing**: Load/stress tests for concurrent operations
- ❌ **Missing**: Database query performance validation

### Detailed Test Gap Analysis

#### HIGH IMPACT Test Gaps (Critical Missing Coverage)

**Complex Database Queries (P1.x)**
- ❌ **Missing**: Query execution time benchmarks for nested Include chains
- ❌ **Missing**: Memory usage tests for complex join operations  
- ❌ **Missing**: N+1 query detection tests
- ❌ **Missing**: Database connection pool exhaustion tests
- 📋 **Current**: Only basic pagination tests in `PlaylistServiceTests.cs:30-50`

**Large Dataset Handling (P3.x)**
- ❌ **Missing**: Memory stress tests with >10K song datasets
- ❌ **Missing**: Pagination effectiveness with large collections
- ❌ **Missing**: OutOfMemoryException prevention tests
- 📋 **Current**: Basic pagination correctness tests exist

**Parallel Processing (P2.x)**
- ❌ **Missing**: Parallel database operation performance tests
- ❌ **Missing**: Connection pool contention under parallel load
- ❌ **Missing**: Thread safety validation for concurrent operations
- 📋 **Current**: No dedicated parallel processing tests found

#### MEDIUM IMPACT Test Gaps

**Cache Management (P4.x)**
- ✅ **Existing**: Cache functionality tests in `MemoryCacheManagerTests.cs`
- ❌ **Missing**: Cache bounds enforcement tests
- ❌ **Missing**: Memory leak detection for unbounded caches
- ❌ **Missing**: Cache eviction policy validation
- ❌ **Missing**: Long-running cache growth tests

**Collection Operations (P5.x)**
- ❌ **Missing**: Performance benchmarks for collection manipulations
- ❌ **Missing**: Memory allocation measurement for LINQ operations
- ❌ **Missing**: Bulk operation vs individual operation comparisons
- 📋 **Current**: Basic functional tests only

---

## Testing Infrastructure Requirements

### Critical Missing Test Infrastructure
- [ ] **TI.1**: **URGENT** - Set up BenchmarkDotNet for performance benchmarking
- [ ] **TI.2**: **URGENT** - Implement memory usage monitoring with dotMemory or PerfView integration
- [ ] **TI.3**: Add database query performance monitoring with Entity Framework logging
- [ ] **TI.4**: Create automated performance regression detection in CI pipeline
- [ ] **TI.5**: Set up load testing environment with NBomber or similar

### Immediate Test Infrastructure Setup Required

#### Performance Testing Framework Setup
```csharp
// Required NuGet packages to add:
// - BenchmarkDotNet (for performance benchmarking)
// - NBomber (for load testing)  
// - Microsoft.EntityFrameworkCore.InMemory (for database testing)
// - FluentAssertions (for better assertions)
```

#### Memory Testing Infrastructure
- [ ] **TI.6**: Add memory profiling integration for unit tests
- [ ] **TI.7**: Implement automated memory leak detection
- [ ] **TI.8**: Set up memory usage baselines and regression detection

### Monitoring and Alerting
- [ ] **MA.1**: Implement performance metrics collection
- [ ] **MA.2**: Add memory usage alerts and thresholds
- [ ] **MA.3**: Create database performance monitoring dashboards
- [ ] **MA.4**: Implement automated performance reporting

---

## Implementation Guidelines

### Development Process
1. **Assessment**: Before implementing fixes, create baseline performance measurements
2. **Implementation**: Implement changes with comprehensive testing
3. **Validation**: Verify improvements through performance testing
4. **Monitoring**: Add appropriate monitoring for ongoing performance tracking

### Success Criteria
- [ ] All HIGH impact issues resolved or mitigated
- [ ] Performance test coverage >90% for identified issues
- [ ] Memory usage reduced by >30% under typical loads
- [ ] Database query performance improved by >50%
- [ ] No performance regressions detected in CI/CD pipeline

---

## Priority Implementation Order

### Phase 1 (Immediate - HIGH Impact)
1. Complex Database Query Patterns (P1.1-P1.5)
2. Large Object Loading Pagination (P3.1-P3.5)
3. Parallel Processing N+1 Queries (P2.1-P2.5)

### Phase 2 (Short-term - MEDIUM Impact)
1. In-Memory Caching Bounds (P4.1-P4.5)
2. Collection Operation Optimization (P5.1-P5.5)
3. Docker Configuration (P6.1-P6.5)

### Phase 3 (Medium-term - LOW Impact + Infrastructure)
1. File Processing Streaming (P7.1-P7.5)
2. Testing Infrastructure (TI.1-TI.5)
3. Monitoring and Alerting (MA.1-MA.4)
4. Remaining LOW priority items

---

## Test Coverage Summary

### Critical Test Gaps Summary (Immediate Action Required)
- **📈 HIGH IMPACT**: 16 critical performance tests missing
- **🔍 MEDIUM IMPACT**: 16 important validation tests missing  
- **📋 LOW IMPACT**: 3 consistency tests missing
- **🛠️ Infrastructure**: 8 testing framework components needed

### Test Files to Create (Priority Order)
1. **`tests/Melodee.Tests.Common/Performance/`** (NEW directory)
   - `PlaylistServicePerformanceTests.cs` - Database query performance
   - `ParallelProcessingTests.cs` - Concurrent operation testing
   - `LargeDatasetMemoryTests.cs` - Memory usage with big datasets
   - `MemoryLeakDetectionTests.cs` - Long-running memory leak detection
   
2. **`tests/Melodee.Tests.Common/Benchmarks/`** (NEW directory)  
   - `DatabaseQueryBenchmarks.cs` - BenchmarkDotNet performance tests
   - `CollectionOperationBenchmarks.cs` - Collection performance
   - `CacheBenchmarks.cs` - Cache performance measurement
   
3. **`tests/Melodee.Tests.Common/Load/`** (NEW directory)
   - `DatabaseQueryLoadTests.cs` - NBomber load testing
   - `ConcurrentOperationLoadTests.cs` - Parallel processing under load

### Required NuGet Package Additions
```xml
<!-- Add to test projects -->
<PackageReference Include="BenchmarkDotNet" Version="0.13.12" />  
<PackageReference Include="NBomber" Version="5.9.2" />
<PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="9.0.8" />
<PackageReference Include="FluentAssertions" Version="8.6.0" />
```

### Key Testing Metrics to Track
- **Database Query Time**: Target <100ms for complex queries
- **Memory Usage**: Max 512MB for large dataset operations  
- **Cache Hit Ratio**: Target >90% for frequently accessed data
- **Parallel Processing**: No connection pool exhaustion under 10x load
- **File Processing**: Streaming vs full-load memory difference >80% reduction

---

*Last Updated: 2025-09-03*  
*Review Status: Ready for Implementation*  
*Test Coverage Analysis: Complete*  
*Next Action: Set up performance testing infrastructure (TI.1-TI.5)*