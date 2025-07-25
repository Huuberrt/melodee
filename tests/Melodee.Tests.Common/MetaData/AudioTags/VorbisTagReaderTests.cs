using Melodee.Common.Enums;
using Melodee.Common.Metadata.AudioTags;
using Melodee.Common.Metadata.AudioTags.Readers;
using Melodee.Common.Utility;

namespace Melodee.Tests.Common.MetaData.AudioTags;

public class VorbisTagReaderTests
{
    [Fact]
    public async Task Returns_Empty_On_NonTagged_File()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var reader = new VorbisTagReader();
            var tags = await reader.ReadTagsAsync(tempFile, CancellationToken.None);
            Assert.Empty(tags);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Vorbis_Reader_Handles_Non_OGG_Files_Gracefully()
    {
        // OGG/Vorbis format is complex, so instead of relying on external files,
        // we'll test that the Vorbis reader handles non-OGG files without crashing
        
        var tempFile = Path.GetTempFileName();
        try
        {
            // Create a file that's not an OGG file
            await File.WriteAllTextAsync(tempFile, "This is not an OGG file");
            
            var reader = new VorbisTagReader();
            var tags = await reader.ReadTagsAsync(tempFile, CancellationToken.None);
            
            // Vorbis reader should return empty tags for non-OGG files
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
