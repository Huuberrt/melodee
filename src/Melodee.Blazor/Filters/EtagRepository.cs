using System.Collections.Concurrent;

namespace Melodee.Blazor.Filters;

public class EtagRepository
{
    private readonly ConcurrentDictionary<string, ETagEntry> _eTags = new();
    private readonly int _maxEntries;
    private readonly TimeSpan _entryMaxAge;
    private volatile int _currentCount;
    private DateTime _lastCleanup = DateTime.UtcNow;
    private readonly object _cleanupLock = new object();
    
    private readonly record struct ETagEntry(string ETag, DateTime CreatedAt);

    public EtagRepository(int maxEntries = 10000, TimeSpan? entryMaxAge = null)
    {
        _maxEntries = maxEntries;
        _entryMaxAge = entryMaxAge ?? TimeSpan.FromHours(24);
    }

    public bool AddEtag(string? apiKeyId, string? etag)
    {
        // Fix guard logic: both key AND value are required (was OR, now AND)
        if (!string.IsNullOrWhiteSpace(apiKeyId) && !string.IsNullOrWhiteSpace(etag))
        {
            var entry = new ETagEntry(etag, DateTime.UtcNow);
            var added = _eTags.TryAdd(apiKeyId!, entry);
            
            if (added)
            {
                Interlocked.Increment(ref _currentCount);
                
                // Trigger cleanup if needed (simple heuristic)
                if (_currentCount > _maxEntries || ShouldCleanup())
                {
                    _ = Task.Run(CleanupExpiredEntries);
                }
            }
            
            return added;
        }

        return false;
    }

    public bool EtagMatch(string? apiKeyId, string? etag)
    {
        // Fix guard logic: both key AND value are required (was OR, now AND)
        if (!string.IsNullOrWhiteSpace(apiKeyId) && !string.IsNullOrWhiteSpace(etag))
        {
            if (_eTags.TryGetValue(apiKeyId!, out var entry))
            {
                // Check if entry is still valid (not expired)
                if (DateTime.UtcNow - entry.CreatedAt <= _entryMaxAge)
                {
                    return entry.ETag == etag;
                }
                else
                {
                    // Remove expired entry
                    if (_eTags.TryRemove(apiKeyId!, out _))
                    {
                        Interlocked.Decrement(ref _currentCount);
                    }
                }
            }
        }

        return false;
    }
    
    private bool ShouldCleanup()
    {
        var now = DateTime.UtcNow;
        return now - _lastCleanup > TimeSpan.FromMinutes(10); // Cleanup every 10 minutes
    }
    
    private void CleanupExpiredEntries()
    {
        if (!Monitor.TryEnter(_cleanupLock, TimeSpan.FromSeconds(1)))
        {
            return; // Another cleanup is running
        }
        
        try
        {
            var now = DateTime.UtcNow;
            var expiredKeys = new List<string>();
            var entriesRemoved = 0;
            
            // Find expired entries
            foreach (var kvp in _eTags)
            {
                if (now - kvp.Value.CreatedAt > _entryMaxAge)
                {
                    expiredKeys.Add(kvp.Key);
                }
            }
            
            // Remove expired entries
            foreach (var key in expiredKeys)
            {
                if (_eTags.TryRemove(key, out _))
                {
                    entriesRemoved++;
                }
            }
            
            // If still over capacity, remove oldest entries (LRU-style)
            if (_currentCount > _maxEntries)
            {
                var entriesToRemove = _currentCount - (int)(_maxEntries * 0.8); // Remove to 80% capacity
                var oldestEntries = _eTags
                    .OrderBy(kvp => kvp.Value.CreatedAt)
                    .Take(entriesToRemove)
                    .Select(kvp => kvp.Key)
                    .ToArray();
                
                foreach (var key in oldestEntries)
                {
                    if (_eTags.TryRemove(key, out _))
                    {
                        entriesRemoved++;
                    }
                }
            }
            
            // Update counters
            Interlocked.Add(ref _currentCount, -entriesRemoved);
            _lastCleanup = now;
        }
        finally
        {
            Monitor.Exit(_cleanupLock);
        }
    }
    
    // For testing purposes
    public int CurrentCount => _currentCount;
    public void ForceCleanup() => CleanupExpiredEntries();
}
