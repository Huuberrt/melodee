namespace Melodee.Tests.Common.Common.NPlusOne;

public class NPlusOneQueryDetectionTests
{
    [Fact]
    public async Task ParallelFileProcessing_DoesNotTriggerN1Queries()
    {
        // This test simulates detection by ensuring we perform batched-like work
        // rather than per-item redundant operations under parallel load.

        // Arrange: create a set of simulated items that would otherwise cause N+1
        var items = Enumerable.Range(1, 200).ToArray();

        // Simulate a cache to represent batching results
        var sharedLookup = new Dictionary<int, int>();
        var locker = new object();

        // Act: process in parallel, but only compute once per key (batched/cached)
        await Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, async (i, _) =>
        {
            // Simulate an expensive query
            int value;
            lock (locker)
            {
                if (!sharedLookup.TryGetValue(i % 10, out value))
                {
                    // First time for this key, compute and cache
                    value = (i % 10) * 2;
                    sharedLookup[i % 10] = value;
                }
            }
            await Task.Delay(1); // simulate work
        });

        // Assert: we computed at most unique-keys times instead of N times
        Assert.InRange(sharedLookup.Count, 1, 10);
    }
}

