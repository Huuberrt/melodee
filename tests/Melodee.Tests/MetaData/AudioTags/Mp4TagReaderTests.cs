using Melodee.Common.Enums;
using Melodee.Common.Metadata.AudioTags;
using Melodee.Common.Metadata.AudioTags.Readers;
using Melodee.Common.Utility;

namespace Melodee.Tests.MetaData.AudioTags;

public class Mp4TagReaderTests
{
    [Fact]
    public async Task Returns_Empty_On_NonTagged_File()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var reader = new Mp4TagReader();
            var tags = await reader.ReadTagsAsync(tempFile, CancellationToken.None);
            Assert.Empty(tags);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task MP4_Reader_Handles_Non_MP4_Files_Gracefully()
    {
        // MP4 format is complex, so instead of relying on external files,
        // we'll test that the MP4 reader handles non-MP4 files without crashing
        
        var tempFile = Path.GetTempFileName();
        try
        {
            // Create a file that's not an MP4 file
            await File.WriteAllTextAsync(tempFile, "This is not an MP4 file");
            
            var reader = new Mp4TagReader();
            var tags = await reader.ReadTagsAsync(tempFile, CancellationToken.None);
            
            // MP4 reader should return empty tags for non-MP4 files
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

    [Fact]
    public async Task MP4_Reader_Handles_Invalid_File_Format_Gracefully()
    {
        // Test the MP4 reader with a file that has MP4 extension but invalid content
        
        var tempFile = Path.GetTempFileName() + ".mp4";
        try
        {
            // Create a file with .mp4 extension but invalid content
            await File.WriteAllTextAsync(tempFile, "Fake MP4 content");
            
            var reader = new Mp4TagReader();
            var tags = await reader.ReadTagsAsync(tempFile, CancellationToken.None);
            
            // Reader should handle invalid content gracefully
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
