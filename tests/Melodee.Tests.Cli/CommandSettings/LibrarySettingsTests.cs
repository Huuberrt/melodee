using FluentAssertions;
using Melodee.Cli.CommandSettings;

namespace Melodee.Tests.Cli.CommandSettings;

/// <summary>
/// Tests for LibrarySettings base class
/// </summary>
public class LibrarySettingsTests
{
    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        // Arrange & Act
        var settings = new LibrarySettings();

        // Assert
        settings.LibraryName.Should().Be(string.Empty);
        settings.Verbose.Should().BeTrue(); // Default should be true
    }

    [Fact]
    public void LibraryName_CanBeSet()
    {
        // Arrange & Act
        var settings = new LibrarySettings
        {
            LibraryName = "TestLibrary"
        };

        // Assert
        settings.LibraryName.Should().Be("TestLibrary");
    }

    [Fact]
    public void Verbose_CanBeSetToFalse()
    {
        // Arrange & Act
        var settings = new LibrarySettings
        {
            Verbose = false
        };

        // Assert
        settings.Verbose.Should().BeFalse();
    }

    [Fact]
    public void LibraryName_IsRequiredByAttribute()
    {
        // This test verifies the Required attribute is present
        // We can check this through reflection

        // Arrange
        var property = typeof(LibrarySettings).GetProperty(nameof(LibrarySettings.LibraryName));

        // Act
        var requiredAttribute = property?.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), false);

        // Assert
        requiredAttribute.Should().NotBeNull();
        requiredAttribute.Should().HaveCount(1);
    }

    [Fact]
    public void LibraryName_HasCorrectCommandArgumentAttribute()
    {
        // This test verifies the CommandArgument attribute is correctly configured

        // Arrange
        var property = typeof(LibrarySettings).GetProperty(nameof(LibrarySettings.LibraryName));

        // Act
        var commandArgAttribute = property?.GetCustomAttributes(typeof(Spectre.Console.Cli.CommandArgumentAttribute), false)
            .Cast<Spectre.Console.Cli.CommandArgumentAttribute>()
            .FirstOrDefault();

        // Assert
        commandArgAttribute.Should().NotBeNull();
        commandArgAttribute!.Position.Should().Be(0);
        commandArgAttribute.Template.Should().Be("[NAME]");
    }

    [Fact]
    public void Verbose_HasCorrectCommandOptionAttribute()
    {
        // This test verifies the CommandOption attribute is correctly configured

        // Arrange
        var property = typeof(LibrarySettings).GetProperty(nameof(LibrarySettings.Verbose));

        // Act
        var commandOptionAttribute = property?.GetCustomAttributes(typeof(Spectre.Console.Cli.CommandOptionAttribute), false)
            .Cast<Spectre.Console.Cli.CommandOptionAttribute>()
            .FirstOrDefault();

        // Assert
        commandOptionAttribute.Should().NotBeNull();
        commandOptionAttribute!.Template.Should().Be("--verbose");
    }

    [Fact]
    public void Verbose_HasCorrectDefaultValueAttribute()
    {
        // This test verifies the DefaultValue attribute is correctly set

        // Arrange
        var property = typeof(LibrarySettings).GetProperty(nameof(LibrarySettings.Verbose));

        // Act
        var defaultValueAttribute = property?.GetCustomAttributes(typeof(System.ComponentModel.DefaultValueAttribute), false)
            .Cast<System.ComponentModel.DefaultValueAttribute>()
            .FirstOrDefault();

        // Assert
        defaultValueAttribute.Should().NotBeNull();
        defaultValueAttribute!.Value.Should().Be(true);
    }

    [Fact]
    public void Properties_HaveDescriptionAttributes()
    {
        // Arrange
        var libraryNameProperty = typeof(LibrarySettings).GetProperty(nameof(LibrarySettings.LibraryName));
        var verboseProperty = typeof(LibrarySettings).GetProperty(nameof(LibrarySettings.Verbose));

        // Act
        var libraryNameDescription = libraryNameProperty?.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
            .Cast<System.ComponentModel.DescriptionAttribute>()
            .FirstOrDefault();

        var verboseDescription = verboseProperty?.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
            .Cast<System.ComponentModel.DescriptionAttribute>()
            .FirstOrDefault();

        // Assert
        libraryNameDescription.Should().NotBeNull();
        libraryNameDescription!.Description.Should().Be("Name of library to process.");

        verboseDescription.Should().NotBeNull();
        verboseDescription!.Description.Should().Be("Output verbose debug and timing results to console.");
    }
}