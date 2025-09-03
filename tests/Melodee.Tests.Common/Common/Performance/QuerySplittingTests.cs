using System.Threading.Tasks;
using Melodee.Common.Data;
using Microsoft.EntityFrameworkCore;
using Melodee.Tests.Common.Common.Services;
using Xunit;

namespace Melodee.Tests.Common.Common.Performance;

public class QuerySplittingTests : ServiceTestBase
{
    [Fact]
    public async Task SplitQuery_And_SingleQuery_Return_Same_Results()
    {
        await using var context = await MockFactory().CreateDbContextAsync();

        // Seed minimal related data: use pre-seeded Library with Id 3 (Storage) from model seeding
        var artist = new Melodee.Common.Data.Models.Artist
        {
            Name = "A",
            NameNormalized = "A",
            LibraryId = 3,
            Directory = "a",
            CreatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)
        };
        context.Artists.Add(artist);
        await context.SaveChangesAsync();

        var q1 = await context.Albums
            .Include(a => a.Artist)
            .AsNoTracking()
            .ToListAsync();

        var q2 = await context.Albums
            .Include(a => a.Artist)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(q1.Count, q2.Count);
    }
}
