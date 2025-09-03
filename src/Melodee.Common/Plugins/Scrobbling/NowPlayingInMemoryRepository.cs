using System.Collections.Concurrent;
using System.Diagnostics;
using Melodee.Common.Models;
using Melodee.Common.Models.Scrobbling;

namespace Melodee.Common.Plugins.Scrobbling;

public sealed class NowPlayingInMemoryRepository : INowPlayingRepository
{
    private const int DefaultMaximumMinutesAgo = 60;
    private const int DefaultMaxEntries = 1000;

    private static readonly ConcurrentDictionary<long, NowPlayingInfo> Storage = new();

    private readonly int _maxEntries;
    private readonly TimeSpan _entryMaxAge;
    private long _addCount;
    private long _removeCount;
    private long _cleanupCount;

    public NowPlayingInMemoryRepository(int maxEntries = DefaultMaxEntries, TimeSpan? entryMaxAge = null)
    {
        _maxEntries = maxEntries <= 0 ? DefaultMaxEntries : maxEntries;
        _entryMaxAge = entryMaxAge ?? TimeSpan.FromMinutes(DefaultMaximumMinutesAgo);
    }

    public Task RemoveNowPlayingAsync(long uniqueId, CancellationToken token = default)
    {
        if (Storage.ContainsKey(uniqueId))
        {
            Storage.TryRemove(uniqueId, out var existing);
            Interlocked.Increment(ref _removeCount);
        }

        return Task.CompletedTask;
    }

    public Task AddOrUpdateNowPlayingAsync(NowPlayingInfo nowPlaying, CancellationToken token = default)
    {
        var result = false;

        // Remove expired entries before adding
        RemoveExpiredNonScrobbledEntries();

        if (Storage.TryGetValue(nowPlaying.UniqueId, out var existing))
        {
            // Update only the time to reduce allocations
            existing.Scrobble.LastScrobbledAt = nowPlaying.Scrobble.LastScrobbledAt;
            Storage[nowPlaying.UniqueId] = existing;
            result = true;
        }
        else
        {
            result = Storage.TryAdd(nowPlaying.UniqueId, nowPlaying);
        }

        // Enforce capacity with LRU-style eviction based on LastScrobbledAt
        if (Storage.Count > _maxEntries)
        {
            EvictOldestEntries(Storage.Count - (int)(_maxEntries * 0.8));
        }

        if (result)
        {
            Trace.WriteLine($"[NowPlayingInMemoryRepository] Added or updated now playing: {nowPlaying}");
            Interlocked.Increment(ref _addCount);
        }

        return Task.CompletedTask;
    }

    public Task<OperationResult<NowPlayingInfo[]>> GetNowPlayingAsync(CancellationToken token = default)
    {
        RemoveExpiredNonScrobbledEntries();

        return Task.FromResult(new OperationResult<NowPlayingInfo[]>
        {
            Data = Storage.Values.ToArray()
        });
    }

    public Task ClearNowPlayingAsync(CancellationToken token = default)
    {
        Storage.Clear();
        return Task.CompletedTask;
    }

    private void RemoveExpiredNonScrobbledEntries()
    {
        var now = NodaTime.SystemClock.Instance.GetCurrentInstant();
        var removed = 0;
        foreach (var nowPlaying in Storage.Values)
        {
            // Treat item as expired if marked or older than configured max age
            if (nowPlaying.Scrobble.IsExpired ||
                (now - nowPlaying.Scrobble.LastScrobbledAt) > NodaTime.Duration.FromTimeSpan(_entryMaxAge))
            {
                if (Storage.TryRemove(nowPlaying.UniqueId, out var _))
                {
                    removed++;
                }
            }
        }
        if (removed > 0)
        {
            Interlocked.Add(ref _cleanupCount, removed);
        }
    }

    private void EvictOldestEntries(int entriesToRemove)
    {
        if (entriesToRemove <= 0) return;

        var oldest = Storage
            .OrderBy(kvp => kvp.Value.Scrobble.LastScrobbledAt)
            .Take(entriesToRemove)
            .Select(kvp => kvp.Key)
            .ToArray();

        var removed = 0;
        foreach (var key in oldest)
        {
            if (Storage.TryRemove(key, out _))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            Interlocked.Add(ref _cleanupCount, removed);
        }
    }

    // Metrics
    public int CurrentCount => Storage.Count;
    public long AddedCount => Interlocked.Read(ref _addCount);
    public long RemovedCount => Interlocked.Read(ref _removeCount);
    public long CleanupRemovedCount => Interlocked.Read(ref _cleanupCount);
}
