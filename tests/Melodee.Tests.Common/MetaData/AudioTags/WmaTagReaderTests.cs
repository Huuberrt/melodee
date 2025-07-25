using Melodee.Common.Enums;
using Melodee.Common.Metadata.AudioTags;
using Melodee.Common.Metadata.AudioTags.Readers;
using Melodee.Common.Utility;

namespace Melodee.Tests.Common.MetaData.AudioTags;

public class WmaTagReaderTests
{
    [Fact]
    public async Task Returns_Empty_On_NonTagged_File()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var reader = new WmaTagReader();
            var tags = await reader.ReadTagsAsync(tempFile, CancellationToken.None);
            Assert.Empty(tags);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task WMA_Reader_Handles_Non_WMA_Files_Gracefully()
    {
        // WMA format is complex and proprietary, so instead of relying on external files,
        // we'll test that the WMA reader handles non-WMA files without crashing
        
        var tempFile = Path.GetTempFileName();
        try
        {
            // Create a file that's not a WMA file
            await File.WriteAllTextAsync(tempFile, "This is not a WMA file");
            
            var reader = new WmaTagReader();
            var tags = await reader.ReadTagsAsync(tempFile, CancellationToken.None);
            
            // WMA reader should return empty tags for non-WMA files
            Assert.NotNull(tags);
            Assert.Empty(tags);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
