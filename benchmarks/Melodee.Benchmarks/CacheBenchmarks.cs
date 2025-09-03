using System.Collections.Concurrent;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.Caching.Memory;
using Melodee.Common.Services.Caching;
using Melodee.Common.Serialization;
using Serilog;

namespace Melodee.Benchmarks;

/// <summary>
/// Benchmarks for caching operations addressing PERFORMANCE_REVIEW.md requirements
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class CacheBenchmarks
{
    private MemoryCacheManager _memoryCacheManager = null!;
    private IMemoryCache _memoryCache = null!;
    private ConcurrentDictionary<string, object> _concurrentDictionary = null!;
    private readonly Serilog.ILogger _logger = new LoggerConfiguration().CreateLogger();

    [Params(100, 1000, 10000)]
    public int CacheSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var serializer = new Serializer(_logger);
        _memoryCacheManager = new MemoryCacheManager(_logger, TimeSpan.FromMinutes(5), serializer);
        _memoryCache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = CacheSize * 2 // Allow some overhead
        });
        _concurrentDictionary = new ConcurrentDictionary<string, object>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _memoryCacheManager?.Clear();
        _memoryCache?.Dispose();
        _concurrentDictionary?.Clear();
    }

    [Benchmark(Baseline = true)]
    public async Task MemoryCacheManager_GetOrAdd()
    {
        var tasks = new List<Task>();
        
        for (int i = 0; i < CacheSize; i++)
        {
            var key = $"key_{i}";
            var task = _memoryCacheManager.GetAsync(key, () => Task.FromResult($"value_{i}"), CancellationToken.None);
            tasks.Add(task);
        }
        
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    public void ConcurrentDictionary_GetOrAdd()
    {
        var tasks = new List<Task>();
        
        for (int i = 0; i < CacheSize; i++)
        {
            var key = $"key_{i}";
            var task = Task.Run(() => _concurrentDictionary.GetOrAdd(key, k => $"value_{k}"));
            tasks.Add(task);
        }
        
        Task.WaitAll(tasks.ToArray());
    }

    [Benchmark]
    public void MemoryCache_GetOrAdd()
    {
        for (int i = 0; i < CacheSize; i++)
        {
            var key = $"key_{i}";
            _memoryCache.GetOrCreate(key, entry =>
            {
                entry.SlidingExpiration = TimeSpan.FromMinutes(5);
                entry.Size = 1;
                return $"value_{i}";
            });
        }
    }

    [Benchmark]
    public async Task CacheEviction_LRU_Simulation()
    {
        const int cacheLimit = 1000;
        var cache = new Dictionary<string, (string value, DateTime lastAccess)>();
        
        // Fill cache beyond limit to test eviction
        for (int i = 0; i < CacheSize; i++)
        {
            var key = $"key_{i}";
            var value = $"value_{i}";
            
            if (cache.Count >= cacheLimit)
            {
                // Find and remove oldest entry (LRU simulation)
                var oldestKey = cache.OrderBy(kvp => kvp.Value.lastAccess).First().Key;
                cache.Remove(oldestKey);
            }
            
            cache[key] = (value, DateTime.UtcNow);
            
            // Simulate some access pattern
            if (i % 100 == 0)
            {
                await Task.Delay(1); // Simulate async work
            }
        }
    }

    [Benchmark]
    public void UnboundedCache_GrowthSimulation()
    {
        var unboundedCache = new ConcurrentDictionary<string, object>();
        
        // Simulate unbounded cache growth (memory leak pattern)
        for (int i = 0; i < CacheSize; i++)
        {
            var key = $"unique_key_{i}_{Guid.NewGuid()}"; // Always unique keys
            var value = new byte[1024]; // 1KB per entry
            unboundedCache.TryAdd(key, value);
        }
        
        // Memory should grow linearly with CacheSize
    }

    [Benchmark]
    public void BoundedCache_WithEviction()
    {
        const int maxCacheSize = 1000;
        var cache = new Dictionary<string, object>();
        var accessOrder = new Queue<string>();
        
        for (int i = 0; i < CacheSize; i++)
        {
            var key = $"key_{i % (maxCacheSize * 2)}"; // Reuse some keys
            var value = new byte[1024];
            
            if (cache.ContainsKey(key))
            {
                // Update existing entry
                cache[key] = value;
            }
            else
            {
                // Add new entry with eviction
                if (cache.Count >= maxCacheSize)
                {
                    // Remove oldest entry
                    var oldestKey = accessOrder.Dequeue();
                    cache.Remove(oldestKey);
                }
                
                cache[key] = value;
                accessOrder.Enqueue(key);
            }
        }
    }

    [Benchmark]
    [Arguments(1)]
    [Arguments(10)]
    [Arguments(100)]
    public async Task CacheHitRatio_Measurement(int accessPatternRepeat)
    {
        var cache = new ConcurrentDictionary<string, string>();
        var hitCount = 0;
        var missCount = 0;
        
        // Pre-populate cache
        for (int i = 0; i < 100; i++)
        {
            cache.TryAdd($"key_{i}", $"value_{i}");
        }
        
        // Simulate access pattern
        for (int repeat = 0; repeat < accessPatternRepeat; repeat++)
        {
            for (int i = 0; i < CacheSize; i++)
            {
                var key = $"key_{i % 150}"; // 100 keys exist, 50 don't (66% hit rate expected)
                
                if (cache.TryGetValue(key, out _))
                {
                    Interlocked.Increment(ref hitCount);
                }
                else
                {
                    Interlocked.Increment(ref missCount);
                    // Cache miss - add to cache
                    cache.TryAdd(key, $"value_{key}");
                }
                
                if (i % 1000 == 0)
                {
                    await Task.Yield(); // Allow other work
                }
            }
        }
        
        // Hit ratio calculation would be: hitCount / (hitCount + missCount)
    }

    [Benchmark]
    public async Task ConcurrentCacheAccess()
    {
        var cache = new ConcurrentDictionary<string, Task<string>>();
        var concurrentTasks = new List<Task>();
        
        for (int i = 0; i < 100; i++)
        {
            var taskIndex = i;
            var task = Task.Run(async () =>
            {
                for (int j = 0; j < CacheSize / 100; j++)
                {
                    var key = $"key_{taskIndex}_{j}";
                    var valueTask = cache.GetOrAdd(key, async k =>
                    {
                        await Task.Delay(1); // Simulate async work
                        return $"value_{k}";
                    });
                    
                    await valueTask;
                }
            });
            
            concurrentTasks.Add(task);
        }
        
        await Task.WhenAll(concurrentTasks);
    }

    [Benchmark]
    public void ETagRepository_Simulation()
    {
        var eTags = new ConcurrentDictionary<string, (string etag, DateTime created)>();
        const int maxETags = 10000;
        
        for (int i = 0; i < CacheSize; i++)
        {
            var key = $"resource_{i}";
            var etag = $"etag_{Guid.NewGuid()}";
            
            // Simulate ETag repository behavior
            if (eTags.Count >= maxETags)
            {
                // Remove oldest entries (time-based eviction simulation)
                var cutoffTime = DateTime.UtcNow.AddMinutes(-30);
                var keysToRemove = eTags.Where(kvp => kvp.Value.created < cutoffTime).Select(kvp => kvp.Key).ToList();
                
                foreach (var keyToRemove in keysToRemove.Take(100)) // Remove in batches
                {
                    eTags.TryRemove(keyToRemove, out _);
                }
            }
            
            eTags.TryAdd(key, (etag, DateTime.UtcNow));
        }
    }
}