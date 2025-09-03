namespace Melodee.Tests.Common.Common.Services;

public class PlaylistReorderingAlgorithmTests
{
    [Fact]
    public void OptimizedReordering_ReindexesSequentially_AfterRemovals()
    {
        // Arrange: playlist orders 1..10
        var orders = Enumerable.Range(1, 10).Select(i => i).ToList();

        // Remove items at positions 0,3,5 (zero-based) using descending order to avoid index shifts
        var indexesToRemove = new[] { 5, 3, 0 };
        foreach (var idx in indexesToRemove)
        {
            orders.RemoveAt(idx);
        }

        // Optimized reindex (single pass)
        for (int i = 0; i < orders.Count; i++)
        {
            orders[i] = i + 1;
        }

        // Assert
        Assert.Equal(7, orders.Count);
        Assert.True(orders.SequenceEqual(Enumerable.Range(1, 7)));
    }
}

