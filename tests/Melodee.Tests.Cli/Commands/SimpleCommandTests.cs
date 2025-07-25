using FluentAssertions;
using Melodee.Cli.CommandSettings;

namespace Melodee.Tests.Cli.Commands;

/// <summary>
/// Simplified command tests that will compile and run
/// </summary>
public class SimpleCommandTests
{
    [Fact]
    public void LibraryProcessSettings_WithValidLibraryName_PassesValidation()
    {
        // Test that we can create and validate settings
        var settings = new LibraryProcessSettings
        {
            LibraryName = "TestLibrary"
        };

        var result = settings.Validate();
        result.Successful.Should().BeTrue();
    }

    [Fact]
    public void LibraryProcessSettings_WithEmptyLibraryName_FailsValidation()
    {
        var settings = new LibraryProcessSettings
        {
            LibraryName = string.Empty
        };

        var result = settings.Validate();
        result.Successful.Should().BeFalse();
        result.Message.Should().Be("Library name is required");
    }

    [Fact]
    public void ConfigurationSetSetting_DefaultValues_AreCorrect()
    {
        var settings = new ConfigurationSetSetting();

        settings.Verbose.Should().BeTrue();
        settings.Remove.Should().BeFalse();
        settings.Key.Should().Be(string.Empty);
        settings.Value.Should().Be(string.Empty);
    }

    [Fact] 
    public void LibrarySettings_DefaultValues_AreCorrect()
    {
        var settings = new LibrarySettings();

        settings.LibraryName.Should().Be(string.Empty);
        settings.Verbose.Should().BeTrue();
    }

    [Theory]
    [InlineData("Library1")]
    [InlineData("My Library")]
    [InlineData("LIBRARY_123")]
    public void LibraryProcessSettings_WithValidNames_PassesValidation(string libraryName)
    {
        var settings = new LibraryProcessSettings
        {
            LibraryName = libraryName
        };

        var result = settings.Validate();
        result.Successful.Should().BeTrue();
    }

    [Fact]
    public void LibraryProcessSettings_ProcessLimit_CanBeSet()
    {
        var settings = new LibraryProcessSettings
        {
            LibraryName = "Test",
            ProcessLimit = 100
        };

        settings.ProcessLimit.Should().Be(100);
    }

    [Fact]
    public void ConfigurationSetSetting_Properties_CanBeSet()
    {
        var settings = new ConfigurationSetSetting
        {
            Key = "TestKey",
            Value = "TestValue",
            Remove = true,
            Verbose = false
        };

        settings.Key.Should().Be("TestKey");
        settings.Value.Should().Be("TestValue");
        settings.Remove.Should().BeTrue();
        settings.Verbose.Should().BeFalse();
    }
}