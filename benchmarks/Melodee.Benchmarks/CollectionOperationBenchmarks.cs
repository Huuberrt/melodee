using System.Buffers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Melodee.Benchmarks;

/// <summary>
/// Benchmarks for collection operations addressing PERFORMANCE_REVIEW.md requirements
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class CollectionOperationBenchmarks
{
    private List<PlaylistSongData> _playlistSongs = null!;
    private List<int> _numbers = null!;

    [Params(100, 1000, 5000)]
    public int CollectionSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _playlistSongs = GeneratePlaylistSongs(CollectionSize);
        _numbers = Enumerable.Range(1, CollectionSize).ToList();
    }

    private List<PlaylistSongData> GeneratePlaylistSongs(int count)
    {
        var songs = new List<PlaylistSongData>();
        var random = new Random(42);
        
        for (int i = 0; i < count; i++)
        {
            songs.Add(new PlaylistSongData
            {
                Id = i + 1,
                SongId = random.Next(1, 10000),
                PlaylistOrder = i + 1,
                Title = $"Song {i + 1}",
                Duration = random.Next(120000, 300000)
            });
        }
        
        return songs;
    }

    [Benchmark(Baseline = true)]
    public void PlaylistReordering_MultipleToListCalls()
    {
        // Simulate the inefficient pattern from PlaylistService.cs:586-600
        var songsToRemove = _playlistSongs
            .Where(ps => ps.SongId % 10 == 0) // Remove every 10th song
            .ToList(); // First ToList() call

        var remainingSongs = _playlistSongs
            .Where(ps => ps.SongId % 10 != 0)
            .OrderBy(x => x.PlaylistOrder)
            .ToList(); // Second ToList() call

        for (int i = 0; i < remainingSongs.Count; i++)
        {
            remainingSongs[i].PlaylistOrder = i + 1;
        }

        var totalDuration = remainingSongs.Sum(x => x.Duration);
        var songCount = remainingSongs.Count;
    }

    [Benchmark]
    public void PlaylistReordering_OptimizedSinglePass()
    {
        var remainingSongs = new List<PlaylistSongData>();
        var totalDuration = 0L;
        var playlistOrder = 1;

        foreach (var song in _playlistSongs)
        {
            if (song.SongId % 10 != 0) // Keep songs that don't match removal criteria
            {
                song.PlaylistOrder = playlistOrder++;
                remainingSongs.Add(song);
                totalDuration += song.Duration;
            }
        }

        var songCount = remainingSongs.Count;
    }

    [Benchmark]
    public void PlaylistReordering_SpanBased()
    {
        var songsArray = _playlistSongs.ToArray();
        var remainingSongsSpan = songsArray.AsSpan();
        
        var writeIndex = 0;
        var totalDuration = 0L;

        for (int readIndex = 0; readIndex < remainingSongsSpan.Length; readIndex++)
        {
            if (remainingSongsSpan[readIndex].SongId % 10 != 0)
            {
                if (readIndex != writeIndex)
                {
                    remainingSongsSpan[writeIndex] = remainingSongsSpan[readIndex];
                }
                remainingSongsSpan[writeIndex].PlaylistOrder = writeIndex + 1;
                totalDuration += remainingSongsSpan[writeIndex].Duration;
                writeIndex++;
            }
        }

        var songCount = writeIndex;
    }

    [Benchmark]
    public List<int> MultipleLinqOperations_Inefficient()
    {
        return _numbers
            .Where(x => x % 2 == 0)
            .ToList()
            .Where(x => x > 10)
            .ToList()
            .OrderByDescending(x => x)
            .ToList()
            .Take(50)
            .ToList();
    }

    [Benchmark]
    public List<int> SingleLinqChain_Optimized()
    {
        return _numbers
            .Where(x => x % 2 == 0)
            .Where(x => x > 10)
            .OrderByDescending(x => x)
            .Take(50)
            .ToList();
    }

    [Benchmark]
    public List<int> ForLoop_Manual()
    {
        var result = new List<int>();
        
        for (int i = 0; i < _numbers.Count; i++)
        {
            var number = _numbers[i];
            if (number % 2 == 0 && number > 10)
            {
                result.Add(number);
            }
        }
        
        result.Sort((a, b) => b.CompareTo(a)); // Descending order
        
        if (result.Count > 50)
        {
            result = result.Take(50).ToList();
        }
        
        return result;
    }

    [Benchmark]
    public void BulkOperations_vs_Individual()
    {
        // Simulate individual updates (inefficient)
        foreach (var song in _playlistSongs.Take(100))
        {
            song.PlaylistOrder += 1000; // Simulate database update
        }
    }

    [Benchmark]
    public void BulkOperations_BatchUpdate()
    {
        // Simulate bulk update (efficient)
        var songsToUpdate = _playlistSongs.Take(100).ToList();
        
        // Batch processing
        const int batchSize = 50;
        for (int i = 0; i < songsToUpdate.Count; i += batchSize)
        {
            var batch = songsToUpdate.Skip(i).Take(batchSize);
            foreach (var song in batch)
            {
                song.PlaylistOrder += 1000;
            }
            // In real scenario, this would be a single database call for the batch
        }
    }

    [Benchmark]
    [Arguments(1024)]
    [Arguments(4096)]
    [Arguments(16384)]
    public void ArrayPoolUsage_vs_NewArray(int arraySize)
    {
        var pool = ArrayPool<byte>.Shared;
        var rentedArray = pool.Rent(arraySize);
        
        try
        {
            // Simulate work with the array
            for (int i = 0; i < Math.Min(arraySize, rentedArray.Length); i++)
            {
                rentedArray[i] = (byte)(i % 256);
            }
        }
        finally
        {
            pool.Return(rentedArray);
        }
    }

    [Benchmark]
    [Arguments(1024)]
    [Arguments(4096)]
    [Arguments(16384)]
    public void NewArray_Allocation(int arraySize)
    {
        var newArray = new byte[arraySize];
        
        // Simulate work with the array
        for (int i = 0; i < arraySize; i++)
        {
            newArray[i] = (byte)(i % 256);
        }
        
        // Array goes out of scope and will be GC'd
    }

    [Benchmark]
    public void CollectionOperations_MemoryAllocation()
    {
        var lists = new List<List<int>>();
        
        // Create many temporary collections (high allocation)
        for (int i = 0; i < 1000; i++)
        {
            var tempList = _numbers.Where(n => n % (i + 1) == 0).ToList();
            if (tempList.Count > 0)
            {
                lists.Add(tempList);
            }
        }
        
        var totalItems = lists.Sum(list => list.Count);
    }

    [Benchmark]
    public void CollectionOperations_OptimizedAllocation()
    {
        var totalItems = 0;
        var reusableList = new List<int>();
        
        // Reuse collections to reduce allocation
        for (int i = 0; i < 1000; i++)
        {
            reusableList.Clear();
            
            foreach (var number in _numbers)
            {
                if (number % (i + 1) == 0)
                {
                    reusableList.Add(number);
                }
            }
            
            totalItems += reusableList.Count;
        }
    }

    [Benchmark]
    public bool Original_vs_Optimized_Comparison()
    {
        // Original inefficient version
        var result1 = _playlistSongs
            .Where(ps => ps.Duration > 180000)
            .ToList()
            .OrderBy(ps => ps.Title)
            .ToList()
            .Take(10)
            .ToList();

        // Optimized version
        var result2 = _playlistSongs
            .Where(ps => ps.Duration > 180000)
            .OrderBy(ps => ps.Title)
            .Take(10)
            .ToList();

        return result1.Count == result2.Count;
    }

    private class PlaylistSongData
    {
        public int Id { get; set; }
        public int SongId { get; set; }
        public int PlaylistOrder { get; set; }
        public string Title { get; set; } = string.Empty;
        public long Duration { get; set; }
    }
}