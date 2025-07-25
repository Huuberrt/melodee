using FluentAssertions;
using Melodee.Cli.CommandSettings;

namespace Melodee.Tests.Cli.CommandSettings;

/// <summary>
/// Tests for ConfigurationSetSetting validation and behavior
/// </summary>
public class ConfigurationSetSettingTests
{
    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        // Arrange & Act
        var settings = new ConfigurationSetSetting();

        // Assert
        settings.Verbose.Should().BeTrue(); // Default should be true
        settings.Remove.Should().BeFalse(); // Default should be false
        settings.Key.Should().Be(string.Empty); // Default empty string
        settings.Value.Should().Be(string.Empty); // Default empty string
    }

    [Fact]
    public void Verbose_CanBeSetToFalse()
    {
        // Arrange & Act
        var settings = new ConfigurationSetSetting
        {
            Verbose = false
        };

        // Assert
        settings.Verbose.Should().BeFalse();
    }

    [Fact]
    public void Remove_CanBeSetToTrue()
    {
        // Arrange & Act
        var settings = new ConfigurationSetSetting
        {
            Remove = true
        };

        // Assert
        settings.Remove.Should().BeTrue();
    }

    [Fact]
    public void Key_CanBeSet()
    {
        // Arrange & Act
        var settings = new ConfigurationSetSetting
        {
            Key = "TestKey"
        };

        // Assert
        settings.Key.Should().Be("TestKey");
    }

    [Fact]
    public void Value_CanBeSet()
    {
        // Arrange & Act
        var settings = new ConfigurationSetSetting
        {
            Value = "TestValue"
        };

        // Assert
        settings.Value.Should().Be("TestValue");
    }

    [Fact]
    public void Key_IsRequiredByAttribute()
    {
        // This test verifies the Required attribute is present on Key

        // Arrange
        var property = typeof(ConfigurationSetSetting).GetProperty(nameof(ConfigurationSetSetting.Key));

        // Act
        var requiredAttribute = property?.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), false);

        // Assert
        requiredAttribute.Should().NotBeNull();
        requiredAttribute.Should().HaveCount(1);
    }

    [Fact]
    public void Value_IsRequiredByAttribute()
    {
        // This test verifies the Required attribute is present on Value

        // Arrange
        var property = typeof(ConfigurationSetSetting).GetProperty(nameof(ConfigurationSetSetting.Value));

        // Act
        var requiredAttribute = property?.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), false);

        // Assert
        requiredAttribute.Should().NotBeNull();
        requiredAttribute.Should().HaveCount(1);
    }

    [Fact]
    public void Key_HasCorrectCommandArgumentAttribute()
    {
        // This test verifies the CommandArgument attribute is correctly configured

        // Arrange
        var property = typeof(ConfigurationSetSetting).GetProperty(nameof(ConfigurationSetSetting.Key));

        // Act
        var commandArgAttribute = property?.GetCustomAttributes(typeof(Spectre.Console.Cli.CommandArgumentAttribute), false)
            .Cast<Spectre.Console.Cli.CommandArgumentAttribute>()
            .FirstOrDefault();

        // Assert
        commandArgAttribute.Should().NotBeNull();
        commandArgAttribute!.Position.Should().Be(0);
        commandArgAttribute.Template.Should().Be("[KEY]");
    }

    [Fact]
    public void Value_HasCorrectCommandArgumentAttribute()
    {
        // This test verifies the CommandArgument attribute is correctly configured

        // Arrange
        var property = typeof(ConfigurationSetSetting).GetProperty(nameof(ConfigurationSetSetting.Value));

        // Act
        var commandArgAttribute = property?.GetCustomAttributes(typeof(Spectre.Console.Cli.CommandArgumentAttribute), false)
            .Cast<Spectre.Console.Cli.CommandArgumentAttribute>()
            .FirstOrDefault();

        // Assert
        commandArgAttribute.Should().NotBeNull();
        commandArgAttribute!.Position.Should().Be(0); // Note: Both Key and Value have position 0, this might be an issue
        commandArgAttribute.Template.Should().Be("[VALUE]");
    }

    [Fact]
    public void Verbose_HasCorrectCommandOptionAttribute()
    {
        // This test verifies the CommandOption attribute is correctly configured

        // Arrange
        var property = typeof(ConfigurationSetSetting).GetProperty(nameof(ConfigurationSetSetting.Verbose));

        // Act
        var commandOptionAttribute = property?.GetCustomAttributes(typeof(Spectre.Console.Cli.CommandOptionAttribute), false)
            .Cast<Spectre.Console.Cli.CommandOptionAttribute>()
            .FirstOrDefault();

        // Assert
        commandOptionAttribute.Should().NotBeNull();
        commandOptionAttribute!.Template.Should().Be("--verbose");
    }

    [Fact]
    public void Remove_HasCorrectCommandOptionAttribute()
    {
        // This test verifies the CommandOption attribute is correctly configured

        // Arrange
        var property = typeof(ConfigurationSetSetting).GetProperty(nameof(ConfigurationSetSetting.Remove));

        // Act
        var commandOptionAttribute = property?.GetCustomAttributes(typeof(Spectre.Console.Cli.CommandOptionAttribute), false)
            .Cast<Spectre.Console.Cli.CommandOptionAttribute>()
            .FirstOrDefault();

        // Assert
        commandOptionAttribute.Should().NotBeNull();
        commandOptionAttribute!.Template.Should().Be("--remove");
    }

    [Fact]
    public void Properties_HaveDescriptionAttributes()
    {
        // Arrange
        var properties = new[]
        {
            typeof(ConfigurationSetSetting).GetProperty(nameof(ConfigurationSetSetting.Verbose)),
            typeof(ConfigurationSetSetting).GetProperty(nameof(ConfigurationSetSetting.Remove)),
            typeof(ConfigurationSetSetting).GetProperty(nameof(ConfigurationSetSetting.Key)),
            typeof(ConfigurationSetSetting).GetProperty(nameof(ConfigurationSetSetting.Value))
        };

        // Act & Assert
        foreach (var property in properties)
        {
            var descriptionAttribute = property?.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
                .Cast<System.ComponentModel.DescriptionAttribute>()
                .FirstOrDefault();

            descriptionAttribute.Should().NotBeNull($"Property {property?.Name} should have a Description attribute");
            descriptionAttribute!.Description.Should().NotBeNullOrEmpty($"Property {property?.Name} should have a non-empty description");
        }
    }

    [Theory]
    [InlineData("DatabasePath")]
    [InlineData("LogLevel")]
    [InlineData("Feature.EnableAdvanced")]
    [InlineData("Server.Port")]
    [InlineData("Connection.Timeout")]
    public void Key_AcceptsVariousValidValues(string key)
    {
        // Arrange & Act
        var settings = new ConfigurationSetSetting
        {
            Key = key
        };

        // Assert
        settings.Key.Should().Be(key);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("123")]
    [InlineData("/path/to/file")]
    [InlineData("Complex value with spaces and symbols!@#")]
    [InlineData("")]
    public void Value_AcceptsVariousValidValues(string value)
    {
        // Arrange & Act
        var settings = new ConfigurationSetSetting
        {
            Value = value
        };

        // Assert
        settings.Value.Should().Be(value);
    }
}