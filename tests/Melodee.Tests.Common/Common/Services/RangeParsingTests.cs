using Xunit;

namespace Melodee.Tests.Common.Common.Services;

public class RangeParsingTests
{
    #region Baseline Tests - Current Range Handling Behavior
    
    // These tests document the current range parsing behavior in SongService
    // so we can verify the new implementation maintains compatibility
    
    [Fact]
    public void CurrentRangeBehavior_WithZeroRangeEnd_SetsToFileSize()
    {
        // Arrange - Simulate current SongService behavior
        const long fileSize = 1000000;
        long rangeBegin = 0;
        long rangeEnd = 0; // Current behavior: 0 means full file
        
        // Act - Current logic: rangeEnd = rangeEnd == 0 ? fileSize : rangeEnd;
        rangeEnd = rangeEnd == 0 ? fileSize : rangeEnd;
        var bytesToRead = (int)(rangeEnd - rangeBegin) + 1;
        
        // Assert - Current behavior
        Assert.Equal(fileSize, rangeEnd);
        Assert.Equal((int)fileSize + 1, bytesToRead); // Issue: +1 causes overflow
    }
    
    [Fact]
    public void CurrentRangeBehavior_WithPartialRange_CalculatesBytesToRead()
    {
        // Arrange - Simulate current SongService behavior  
        const long fileSize = 1000000;
        const long rangeBegin = 100;
        const long rangeEnd = 500;
        
        // Act - Current logic
        var adjustedRangeEnd = rangeEnd == 0 ? fileSize : rangeEnd;
        var bytesToRead = (int)(adjustedRangeEnd - rangeBegin) + 1;
        
        // Assert - Current behavior
        Assert.Equal(rangeEnd, adjustedRangeEnd);
        Assert.Equal(401, bytesToRead); // 500 - 100 + 1 = 401
    }
    
    [Fact]  
    public void CurrentRangeBehavior_WithBytesToReadExceedingFileSize_ClampsToFileSize()
    {
        // Arrange - Simulate current SongService behavior
        const long fileSize = 1000;
        const long rangeBegin = 0;
        const long rangeEnd = 2000; // Exceeds file size
        
        // Act - Current logic  
        var adjustedRangeEnd = rangeEnd == 0 ? fileSize : rangeEnd;
        var bytesToRead = (int)(adjustedRangeEnd - rangeBegin) + 1;
        
        // Current clamping logic: if (bytesToRead > fileSize) bytesToRead = (int)fileSize;
        if (bytesToRead > fileSize)
        {
            bytesToRead = (int)fileSize;
        }
        
        // Assert - Current behavior
        Assert.Equal(rangeEnd, adjustedRangeEnd);
        Assert.Equal((int)fileSize, bytesToRead);
    }
    
    [Fact]
    public void CurrentRangeBehavior_IntCastingIssue_WithLargeFile()
    {
        // Arrange - Document the int casting issue
        const long fileSize = (long)int.MaxValue + 1000; // > 2GB file
        const long rangeBegin = 0;
        const long rangeEnd = 0;
        
        // Act - Current logic with problematic int casting
        var adjustedRangeEnd = rangeEnd == 0 ? fileSize : rangeEnd;
        
        // This will overflow when cast to int
        var bytesToReadLong = adjustedRangeEnd - rangeBegin + 1;
        var bytesToRead = (int)(adjustedRangeEnd - rangeBegin) + 1; // Problematic cast
        
        // Assert - Demonstrate the overflow issue  
        Assert.True(bytesToReadLong > int.MaxValue);
        Assert.NotEqual(bytesToReadLong, bytesToRead); // Cast causes overflow
    }
    
    #endregion
    
    #region Range Request Format Tests - For Future Implementation
    
    // These tests define what the new range parser should support
    // Based on RFC 7233 HTTP Range Requests
    
    [Theory]
    [InlineData("bytes=0-499", 0L, 499L)]
    [InlineData("bytes=500-999", 500L, 999L)] 
    [InlineData("bytes=0-", 0L, null)] // null means to end of file
    [InlineData("bytes=-500", 500L, 999L)] // Last 500 bytes of 1000 byte file
    public void RangeParser_ShouldParseValidRanges(string rangeHeader, long expectedStart, long? expectedEnd)
    {
        // Arrange
        const long fileSize = 1000;
        
        // Act
        var result = Melodee.Common.Models.Streaming.RangeParser.ParseRange(rangeHeader, fileSize);
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedStart, result.Start);
        Assert.Equal(expectedEnd, result.End);
        Assert.True(result.IsValidForFileSize(fileSize));
    }
    
    [Theory]
    [InlineData("bytes=abc-def")] // Invalid format
    [InlineData("bytes=100-50")] // Start > End
    [InlineData("invalid=0-100")] // Wrong unit
    [InlineData("")] // Empty
    [InlineData("bytes=1000-2000")] // Start beyond file size
    public void RangeParser_ShouldRejectInvalidRanges(string? rangeHeader)
    {
        // Arrange
        const long fileSize = 1000;
        
        // Act
        var result = Melodee.Common.Models.Streaming.RangeParser.ParseRange(rangeHeader, fileSize);
        
        // Assert
        Assert.Null(result);
    }
    
    [Fact]
    public void RangeParser_WithNullInput_ReturnsNull()
    {
        // Arrange
        const long fileSize = 1000;
        
        // Act
        string? nullRange = null;
        var result = Melodee.Common.Models.Streaming.RangeParser.ParseRange(nullRange, fileSize);
        
        // Assert
        Assert.Null(result);
    }
    
    [Fact]
    public void RangeInfo_ShouldCalculateContentLengthCorrectly()
    {
        // Arrange
        var range = new Melodee.Common.Models.Streaming.RangeInfo 
        { 
            Start = 100, 
            End = 200 
        };
        const long fileSize = 1000;
        
        // Act
        var contentLength = range.GetContentLength(fileSize);
        
        // Assert
        Assert.Equal(101, contentLength); // 200 - 100 + 1
    }
    
    [Fact]  
    public void RangeInfo_ShouldGenerateCorrectContentRangeHeader()
    {
        // Arrange
        var range = new Melodee.Common.Models.Streaming.RangeInfo
        {
            Start = 100,
            End = 200
        };
        const long fileSize = 1000;
        
        // Act
        var header = range.ToContentRangeHeader(fileSize);
        
        // Assert
        Assert.Equal("bytes 100-200/1000", header);
    }
    
    #endregion
}