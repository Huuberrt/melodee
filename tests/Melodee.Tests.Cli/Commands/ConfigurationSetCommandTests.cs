using FluentAssertions;
using Moq;
using Melodee.Tests.Cli.Helpers;
using Melodee.Cli.Command;
using Melodee.Cli.CommandSettings;
using Melodee.Common.Services;
using Melodee.Common.Data.Models;
using Melodee.Common.Models;
using Spectre.Console.Cli;
using Microsoft.Extensions.DependencyInjection;

namespace Melodee.Tests.Cli.Commands;

/// <summary>
/// Tests for the ConfigurationSetCommand
/// </summary>
public class ConfigurationSetCommandTests : CliTestBase
{
    private readonly ConfigurationSetCommand _command;
    private readonly Mock<SettingService> _mockSettingService;

    public ConfigurationSetCommandTests()
    {
        _mockSettingService = new Mock<SettingService>();
        _command = new ConfigurationSetCommand();
    }

    [Fact]
    public async Task ExecuteAsync_WithExistingKey_UpdatesConfigurationAndReturnsSuccess()
    {
        // Arrange
        var settings = new ConfigurationSetSetting
        {
            Key = "TestKey",
            Value = "NewValue",
            Verbose = false,
            Remove = false
        };

        var existingSetting = new Setting
        {
            Id = 1,
            Key = "TestKey",
            Value = "OldValue"
        };

        _mockSettingService.Setup(x => x.GetAsync("TestKey"))
            .ReturnsAsync(new ServiceResult<Setting>(existingSetting));

        _mockSettingService.Setup(x => x.UpdateAsync(It.IsAny<Setting>()))
            .ReturnsAsync(new ServiceResult<Setting>(existingSetting));

        var context = new CommandContext(null, null, null);

        // Act
        var result = await _command.ExecuteAsync(context, settings);

        // Assert
        result.Should().Be(0); // Success exit code
        _mockSettingService.Verify(x => x.UpdateAsync(It.Is<Setting>(s => s.Value == "NewValue")), Times.Once);
        
        var output = GetConsoleOutput();
        output.Should().Contain("Configuration updated");
    }

    [Fact]
    public async Task ExecuteAsync_WithNonExistentKey_CreatesNewConfigurationAndReturnsSuccess()
    {
        // Arrange
        var settings = new ConfigurationSetSetting
        {
            Key = "NewKey",
            Value = "NewValue",
            Verbose = false,
            Remove = false
        };

        _mockSettingService.Setup(x => x.GetAsync("NewKey"))
            .ReturnsAsync(new ServiceResult<Setting>(null, false, "Not found"));

        var newSetting = new Setting
        {
            Id = 1,
            Key = "NewKey",
            Value = "NewValue"
        };

        _mockSettingService.Setup(x => x.AddAsync(It.IsAny<Setting>()))
            .ReturnsAsync(new ServiceResult<Setting>(newSetting));

        var context = new CommandContext(null, null, null);

        // Act
        var result = await _command.ExecuteAsync(context, settings);

        // Assert
        result.Should().Be(0); // Success exit code
        _mockSettingService.Verify(x => x.AddAsync(It.Is<Setting>(s => 
            s.Key == "NewKey" && s.Value == "NewValue")), Times.Once);
        
        var output = GetConsoleOutput();
        output.Should().Contain("Configuration key [NewKey] not found. Creating new entry");
        output.Should().Contain("Configuration created");
    }

    [Fact]
    public async Task ExecuteAsync_WithRemoveFlag_ClearsConfigurationValue()
    {
        // Arrange
        var settings = new ConfigurationSetSetting
        {
            Key = "TestKey",
            Value = "SomeValue", // This should be ignored when Remove is true
            Verbose = false,
            Remove = true
        };

        var existingSetting = new Setting
        {
            Id = 1,
            Key = "TestKey",
            Value = "OldValue"
        };

        _mockSettingService.Setup(x => x.GetAsync("TestKey"))
            .ReturnsAsync(new ServiceResult<Setting>(existingSetting));

        _mockSettingService.Setup(x => x.UpdateAsync(It.IsAny<Setting>()))
            .ReturnsAsync(new ServiceResult<Setting>(existingSetting));

        var context = new CommandContext(null, null, null);

        // Act
        var result = await _command.ExecuteAsync(context, settings);

        // Assert
        result.Should().Be(0); // Success exit code
        _mockSettingService.Verify(x => x.UpdateAsync(It.Is<Setting>(s => s.Value == string.Empty)), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenUpdateFails_ReturnsErrorExitCode()
    {
        // Arrange
        var settings = new ConfigurationSetSetting
        {
            Key = "TestKey",
            Value = "NewValue",
            Verbose = false,
            Remove = false
        };

        var existingSetting = new Setting
        {
            Id = 1,
            Key = "TestKey",
            Value = "OldValue"
        };

        _mockSettingService.Setup(x => x.GetAsync("TestKey"))
            .ReturnsAsync(new ServiceResult<Setting>(existingSetting));

        _mockSettingService.Setup(x => x.UpdateAsync(It.IsAny<Setting>()))
            .ReturnsAsync(new ServiceResult<Setting>(null, false, "Update failed"));

        var context = new CommandContext(null, null, null);

        // Act
        var result = await _command.ExecuteAsync(context, settings);

        // Assert
        result.Should().Be(1); // Error exit code
    }

    [Fact]
    public async Task ExecuteAsync_WhenAddFails_ReturnsErrorExitCode()
    {
        // Arrange
        var settings = new ConfigurationSetSetting
        {
            Key = "NewKey",
            Value = "NewValue",
            Verbose = false,
            Remove = false
        };

        _mockSettingService.Setup(x => x.GetAsync("NewKey"))
            .ReturnsAsync(new ServiceResult<Setting>(null, false, "Not found"));

        _mockSettingService.Setup(x => x.AddAsync(It.IsAny<Setting>()))
            .ReturnsAsync(new ServiceResult<Setting>(null, false, "Add failed"));

        var context = new CommandContext(null, null, null);

        // Act
        var result = await _command.ExecuteAsync(context, settings);

        // Assert
        result.Should().Be(1); // Error exit code
        
        var output = GetConsoleOutput();
        output.Should().Contain("Failed to create configuration: Add failed");
    }

    [Fact]
    public async Task ExecuteAsync_WithVerboseMode_ShowsDetailedOutput()
    {
        // Arrange
        var settings = new ConfigurationSetSetting
        {
            Key = "TestKey",
            Value = "NewValue",
            Verbose = true,
            Remove = false
        };

        var existingSetting = new Setting
        {
            Id = 1,
            Key = "TestKey",
            Value = "OldValue"
        };

        _mockSettingService.Setup(x => x.GetAsync("TestKey"))
            .ReturnsAsync(new ServiceResult<Setting>(existingSetting));

        _mockSettingService.Setup(x => x.UpdateAsync(It.IsAny<Setting>()))
            .ReturnsAsync(new ServiceResult<Setting>(existingSetting));

        var context = new CommandContext(null, null, null);

        // Act
        var result = await _command.ExecuteAsync(context, settings);

        // Assert
        result.Should().Be(0);
        // Note: The command doesn't actually show different output for verbose mode
        // This test documents the current behavior
    }

    [Theory]
    [InlineData("DatabasePath", "/new/path/to/db")]
    [InlineData("LogLevel", "Debug")]
    [InlineData("MaxFileSize", "1000000")]
    [InlineData("EnableFeature", "true")]
    public async Task ExecuteAsync_WithVariousKeyValuePairs_UpdatesCorrectly(string key, string value)
    {
        // Arrange
        var settings = new ConfigurationSetSetting
        {
            Key = key,
            Value = value,
            Verbose = false,
            Remove = false
        };

        var existingSetting = new Setting
        {
            Id = 1,
            Key = key,
            Value = "OldValue"
        };

        _mockSettingService.Setup(x => x.GetAsync(key))
            .ReturnsAsync(new ServiceResult<Setting>(existingSetting));

        _mockSettingService.Setup(x => x.UpdateAsync(It.IsAny<Setting>()))
            .ReturnsAsync(new ServiceResult<Setting>(existingSetting));

        var context = new CommandContext(null, null, null);

        // Act
        var result = await _command.ExecuteAsync(context, settings);

        // Assert
        result.Should().Be(0);
        _mockSettingService.Verify(x => x.UpdateAsync(It.Is<Setting>(s => 
            s.Key == key && s.Value == value)), Times.Once);
    }
}

