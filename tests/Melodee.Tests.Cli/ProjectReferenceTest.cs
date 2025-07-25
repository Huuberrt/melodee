using Xunit;
using FluentAssertions;

namespace Melodee.Tests.Cli;

public class ProjectReferenceTest
{
    [Fact]
    public void CanReference_CliAssembly()
    {
        // This test verifies we can reference the CLI project
        var assembly = typeof(Melodee.Cli.Program).Assembly;
        assembly.Should().NotBeNull();
    }

    [Fact]
    public void CanCreate_LibrarySettings()
    {
        var settings = new Melodee.Cli.CommandSettings.LibrarySettings
        {
            LibraryName = "Test"
        };
        
        settings.LibraryName.Should().Be("Test");
    }

    [Fact]
    public void CanCreate_ProcessSettings()
    {
        var settings = new Melodee.Cli.CommandSettings.LibraryProcessSettings
        {
            LibraryName = "Test"
        };
        
        settings.LibraryName.Should().Be("Test");
        settings.CopyMode.Should().BeTrue(); // Default value
    }

    [Fact]
    public void CanValidate_ProcessSettings()
    {
        var settings = new Melodee.Cli.CommandSettings.LibraryProcessSettings
        {
            LibraryName = "ValidName"
        };
        
        var result = settings.Validate();
        result.Successful.Should().BeTrue();
    }

    [Fact]
    public void Validation_Fails_WithEmptyLibraryName()
    {
        var settings = new Melodee.Cli.CommandSettings.LibraryProcessSettings
        {
            LibraryName = ""
        };
        
        var result = settings.Validate();
        result.Successful.Should().BeFalse();
        result.Message.Should().Be("Library name is required");
    }
}