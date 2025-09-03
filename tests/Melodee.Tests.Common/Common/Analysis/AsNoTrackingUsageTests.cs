using System.Text.RegularExpressions;
using Xunit;

namespace Melodee.Tests.Common.Common.Analysis;

public class AsNoTrackingUsageTests
{
    [Fact]
    public void ReadQueries_In_UserService_Should_Use_AsNoTracking()
    {
        var repoRoot = GetRepoRoot();
        var file = Path.Combine(repoRoot, "src/Melodee.Common/Services/UserService.cs");
        var content = File.ReadAllText(file);

        string[] methodsToCheck =
        [
            "UserArtistAsync(",
            "UserAlbumAsync(",
            "UserSongAsync(",
            "UserLastPlayedSongsAsync(",
            "UserSongsForPlaylistAsync("
        ];

        var allOk = true;
        var failures = new List<string>();

        foreach (var method in methodsToCheck)
        {
            var start = content.IndexOf(method, StringComparison.Ordinal);
            if (start < 0) continue;

            var end = content.IndexOf("\n    public", start + method.Length, StringComparison.Ordinal);
            if (end < 0) end = content.Length;
            var body = content.Substring(start, end - start);

            var hasMaterializer = Regex.IsMatch(body, @"\b(ToArrayAsync|ToListAsync|FirstOrDefaultAsync)\s*\(");
            if (hasMaterializer && !body.Contains("AsNoTracking()"))
            {
                allOk = false;
                failures.Add(method);
            }
        }

        Assert.True(allOk, "AsNoTracking() missing in read methods: " + string.Join(", ", failures));
    }

    private static string GetRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(dir, "Melodee.sln")))
        {
            dir = Directory.GetParent(dir)!.FullName;
        }
        return dir;
    }
}
