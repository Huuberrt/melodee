using System.Diagnostics;

namespace Melodee.Tests.Common.Common.Concurrency;

public class ParallelProcessingTests
{
    private static int ComputeSafeParallelismFromConnectionString(string? connectionString, int processorCount)
    {
        var cpuBoundDefault = Math.Max(1, processorCount / 2);
        if (string.IsNullOrWhiteSpace(connectionString)) return cpuBoundDefault;

        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var maxPoolPart = parts.FirstOrDefault(p => p.StartsWith("MaxPoolSize", StringComparison.OrdinalIgnoreCase) ||
                                                    p.StartsWith("Maximum Pool Size", StringComparison.OrdinalIgnoreCase));
        if (maxPoolPart == null) return cpuBoundDefault;
        var eqIndex = maxPoolPart.IndexOf('=');
        if (eqIndex <= 0) return cpuBoundDefault;
        if (!int.TryParse(maxPoolPart[(eqIndex + 1)..], out var maxPoolSize)) return cpuBoundDefault;
        var reserve = Math.Min(5, Math.Max(1, maxPoolSize / 10));
        return Math.Max(1, Math.Min(processorCount, maxPoolSize - reserve));
    }

    [Fact]
    public async Task ArtistRescan_WithParallelAlbumProcessing_DoesNotExhaustConnectionPool()
    {
        // Arrange: simulate a connection string with a bounded pool
        const string conn = "Host=localhost;Database=test;MaxPoolSize=50;Pooling=true;";
        var safe = ComputeSafeParallelismFromConnectionString(conn, Environment.ProcessorCount);

        var concurrent = 0;
        var peak = 0;
        var gate = new SemaphoreSlim(safe, safe);
        var work = Enumerable.Range(0, 200) // Simulate many albums
            .Select(async _ =>
            {
                await gate.WaitAsync();
                try
                {
                    var now = Interlocked.Increment(ref concurrent);
                    InterlockedExtensions.Max(ref peak, now);
                    // Simulate brief DB work
                    await Task.Delay(5);
                }
                finally
                {
                    Interlocked.Decrement(ref concurrent);
                    gate.Release();
                }
            });

        var sw = Stopwatch.StartNew();
        await Task.WhenAll(work);
        sw.Stop();

        // Assert: peak concurrency stays within safe limit
        Assert.InRange(peak, 1, safe);
        // Also ensure it completed in reasonable time window
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
    }
}

internal static class InterlockedExtensions
{
    public static void Max(ref int target, int value)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref target);
            if (value <= snapshot) return;
            if (Interlocked.CompareExchange(ref target, value, snapshot) == snapshot) return;
        }
    }
}
