using Microsoft.Extensions.Primitives;
using Melodee.Common.Models.Streaming;

namespace Melodee.Tests.Common.Common.Services;

public class RangeHeaderLargeFileTests
{
    [Fact]
    public void CreateResponseHeaders_FullContent_LargeFile_UsesLongContentLength()
    {
        // Arrange
        var size = (long)int.MaxValue + 123456789L; // > 2GB
        var descriptor = new StreamingDescriptor
        {
            FilePath = "/tmp/fake", // not used
            FileSize = size,
            ContentType = "audio/mpeg",
            ResponseHeaders = new Dictionary<string, StringValues>(),
            Range = null,
            IsDownload = false
        };

        // Act
        var headers = RangeParser.CreateResponseHeaders(descriptor, 200);

        // Assert
        Assert.True(headers.TryGetValue("Content-Length", out var contentLen));
        Assert.Equal(size.ToString(), contentLen.ToString());
        Assert.Equal("bytes", headers["Accept-Ranges"].ToString());
    }

    [Fact]
    public void CreateResponseHeaders_PartialContent_LargeFile_UsesLongMath()
    {
        // Arrange
        var size = (long)int.MaxValue + 987654321L; // > 2GB
        var range = new RangeInfo { Start = 0, End = 0 }; // first byte only
        var descriptor = new StreamingDescriptor
        {
            FilePath = "/tmp/fake",
            FileSize = size,
            ContentType = "audio/mpeg",
            ResponseHeaders = new Dictionary<string, StringValues>(),
            Range = range,
            IsDownload = false
        };

        // Act
        var headers = RangeParser.CreateResponseHeaders(descriptor, 206);

        // Assert
        Assert.Equal("1", headers["Content-Length"].ToString());
        Assert.Equal($"bytes 0-0/{size}", headers["Content-Range"].ToString());
    }
}

