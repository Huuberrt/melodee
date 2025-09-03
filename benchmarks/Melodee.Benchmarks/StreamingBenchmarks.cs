using System.Buffers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Melodee.Benchmarks;

/// <summary>
/// Benchmarks for streaming operations addressing API_REVIEW_FIX.md requirements
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class StreamingBenchmarks
{
    private readonly string _testFilePath;
    private readonly byte[] _testData;
    private const int SmallFileSize = 1024 * 1024; // 1MB
    private const int LargeFileSize = 50 * 1024 * 1024; // 50MB

    public StreamingBenchmarks()
    {
        _testFilePath = Path.GetTempFileName();
        _testData = new byte[LargeFileSize];
        new Random(42).NextBytes(_testData);
        File.WriteAllBytes(_testFilePath, _testData);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (File.Exists(_testFilePath))
        {
            File.Delete(_testFilePath);
        }
    }

    [Params(4096, 8192, 16384, 32768, 65536, 131072, 262144)] // 4KB to 256KB buffer sizes
    public int BufferSize { get; set; }

    [Benchmark(Baseline = true)]
    public async Task FileStreamToMemoryStream_NewBuffer()
    {
        using var fileStream = new FileStream(_testFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var memoryStream = new MemoryStream();
        
        var buffer = new byte[BufferSize];
        int bytesRead;
        while ((bytesRead = await fileStream.ReadAsync(buffer)) > 0)
        {
            await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead));
        }
    }

    [Benchmark]
    public async Task FileStreamToMemoryStream_ArrayPool()
    {
        using var fileStream = new FileStream(_testFilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var memoryStream = new MemoryStream();
        
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int bytesRead;
            while ((bytesRead = await fileStream.ReadAsync(buffer)) > 0)
            {
                await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    [Benchmark]
    public async Task FileStreamCopyToAsync()
    {
        using var fileStream = new FileStream(_testFilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var memoryStream = new MemoryStream();
        
        await fileStream.CopyToAsync(memoryStream, BufferSize);
    }

    [Benchmark]
    [Arguments(0, 1024)]      // First 1KB
    [Arguments(1024, 8192)]   // 1KB to 9KB range
    [Arguments(0, -1)]        // Full file (range disabled)
    public async Task RangeRequestProcessing(long start, long length)
    {
        using var fileStream = new FileStream(_testFilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var memoryStream = new MemoryStream();

        if (start > 0)
        {
            fileStream.Seek(start, SeekOrigin.Begin);
        }

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            long totalBytesRead = 0;
            int bytesRead;
            
            while ((bytesRead = await fileStream.ReadAsync(buffer)) > 0)
            {
                var bytesToWrite = length > 0 ? 
                    Math.Min(bytesRead, (int)(length - totalBytesRead)) : 
                    bytesRead;
                    
                if (bytesToWrite <= 0) break;
                
                await memoryStream.WriteAsync(buffer.AsMemory(0, bytesToWrite));
                totalBytesRead += bytesToWrite;
                
                if (length > 0 && totalBytesRead >= length) break;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    [Benchmark]
    public string RangeParsing_ValidRange()
    {
        return ParseRange("bytes=0-1023", 1048576);
    }

    [Benchmark]
    public string RangeParsing_StartOnly()
    {
        return ParseRange("bytes=1024-", 1048576);
    }

    [Benchmark]
    public string RangeParsing_SuffixOnly()
    {
        return ParseRange("bytes=-512", 1048576);
    }

    private string ParseRange(string rangeHeader, long fileSize)
    {
        if (string.IsNullOrEmpty(rangeHeader) || !rangeHeader.StartsWith("bytes="))
        {
            return "invalid";
        }

        var range = rangeHeader.Substring(6); // Remove "bytes="
        var parts = range.Split('-');
        
        if (parts.Length != 2)
        {
            return "invalid";
        }

        long start = 0, end = fileSize - 1;

        if (!string.IsNullOrEmpty(parts[0]))
        {
            if (!long.TryParse(parts[0], out start))
                return "invalid";
        }

        if (!string.IsNullOrEmpty(parts[1]))
        {
            if (!long.TryParse(parts[1], out end))
                return "invalid";
        }
        else if (string.IsNullOrEmpty(parts[0]))
        {
            // Suffix range like "bytes=-500"
            if (long.TryParse(parts[1], out var suffix))
            {
                start = Math.Max(0, fileSize - suffix);
                end = fileSize - 1;
            }
        }

        return $"bytes {start}-{end}/{fileSize}";
    }

    [Benchmark]
    public string HeaderConstruction()
    {
        var start = 1024L;
        var end = 8191L;
        var fileSize = 1048576L;
        var contentLength = end - start + 1;

        return $"bytes {start}-{end}/{fileSize}";
    }
}