using Melodee.Common.Enums;
using Melodee.Common.Metadata.AudioTags;
using Melodee.Common.Utility;
using Melodee.Tests.Common.Utility;

namespace Melodee.Tests.Common.MetaData.AudioTags;

public class Id3v1TagReaderTests
{
    private readonly string _testOutputPath;

    public Id3v1TagReaderTests()
    {
        // Create a test directory in the current directory
        _testOutputPath = Path.Combine(Directory.GetCurrentDirectory(), "Id3v1TestFiles");
        Directory.CreateDirectory(_testOutputPath);
    }

    [Fact]
    public async Task Read_Id3v1_Tags_From_Generated_Mp3_File()
    {
        // Arrange - Create a test file with Id3v1 tags instead of relying on external folder
        var metadata = new BlankMusicFileGenerator.MusicMetadata
        {
            Title = "ID3v1 Test Song",
            Artist = "ID3v1 Test Artist",
            Album = "ID3v1 Test Album",
            RecordingYear = 2023,
            TrackNumber = 3,
            Genre = "Rock",
            Comment = "ID3v1 Test Comment"
        };

        var filePath = await BlankMusicFileGenerator.CreateMinimalMp3FileWithVersionAsync(
            _testOutputPath, 
            BlankMusicFileGenerator.Id3Version.Id3v1_1, 
            metadata);

        try
        {
            // Act
            var tags = await AudioTagManager.ReadAllTagsAsync(filePath, CancellationToken.None);

            // Assert - Verify the tags were properly read
            Assert.NotEqual(AudioFormat.Unknown, tags.Format);
            Assert.NotEqual(0, tags.FileMetadata.FileSize);
            Assert.NotEqual(string.Empty, tags.FileMetadata.FilePath);
            Assert.NotEqual(DateTimeOffset.MinValue, tags.FileMetadata.Created);
            Assert.NotEqual(DateTimeOffset.MinValue, tags.FileMetadata.LastModified);
            Assert.NotNull(tags.Tags);
            Assert.NotEmpty(tags.Tags);
            Assert.NotEmpty(tags.Tags.Keys);
            Assert.NotEmpty(tags.Tags.Values);
            Assert.NotEmpty(SafeParser.ToString(tags.Tags[MetaTagIdentifier.Artist]));
            Assert.True(SafeParser.ToNumber<int>(tags.Tags[MetaTagIdentifier.TrackNumber]) > 0);
            Assert.NotEmpty(SafeParser.ToString(tags.Tags[MetaTagIdentifier.Title]));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}
