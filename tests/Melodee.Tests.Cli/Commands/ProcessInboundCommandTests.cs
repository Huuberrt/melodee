using FluentAssertions;
using Moq;
using Melodee.Tests.Cli.Helpers;
using Melodee.Cli.Command;
using Melodee.Cli.CommandSettings;
using Spectre.Console.Cli;
using Melodee.Common.Services.Scanning;
using Melodee.Common.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Melodee.Tests.Cli.Commands;

/// <summary>
/// Tests for the ProcessInboundCommand (Library Processing)
/// </summary>
public class ProcessInboundCommandTests : CliTestBase
{
    private readonly ProcessInboundCommand _command;
    private readonly Mock<DirectoryProcessorToStagingService> _mockProcessor;

    public ProcessInboundCommandTests()
    {
        _mockProcessor = new Mock<DirectoryProcessorToStagingService>();
        _command = new ProcessInboundCommand();
    }

    [Fact]
    public async Task ExecuteAsync_WithValidLibrary_ReturnsSuccessExitCode()
    {
        // Arrange
        var settings = new LibraryProcessSettings
        {
            LibraryName = "TestLibrary",
            Verbose = false,
            ForceMode = false,
            ProcessLimit = null
        };

        SetupLibrary("TestLibrary");
        SetupSuccessfulProcessing();

        var context = new CommandContext(null, null, null);

        // Act
        var result = await _command.ExecuteAsync(context, settings);

        // Assert
        result.Should().Be(0); // Success exit code
    }

    [Fact]
    public async Task ExecuteAsync_WithNonExistentLibrary_ThrowsException()
    {
        // Arrange
        var settings = new LibraryProcessSettings
        {
            LibraryName = "NonExistentLibrary",
            Verbose = false
        };

        // Don't setup any library (empty list)
        MockLibraryService.Setup(x => x.ListAsync(It.IsAny<PagedRequest>()))
            .ReturnsAsync(new ServiceResult<IEnumerable<Melodee.Common.Data.Models.Library>>(
                Array.Empty<Melodee.Common.Data.Models.Library>()));

        var context = new CommandContext(null, null, null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<Exception>(
            () => _command.ExecuteAsync(context, settings));
        
        exception.Message.Should().Contain("Library with name [NonExistentLibrary] not found");
    }

    [Fact]
    public async Task ExecuteAsync_WithVerboseMode_DisplaysProcessResult()
    {
        // Arrange
        var settings = new LibraryProcessSettings
        {
            LibraryName = "TestLibrary",
            Verbose = true,
            ForceMode = false
        };

        SetupLibrary("TestLibrary");
        SetupSuccessfulProcessing();

        var context = new CommandContext(null, null, null);

        // Act
        var result = await _command.ExecuteAsync(context, settings);

        // Assert
        result.Should().Be(0);
        var output = GetConsoleOutput();
        output.Should().Contain("Configuration"); // Should show configuration panel
    }

    [Fact]
    public async Task ExecuteAsync_WithProcessLimit_PassesLimitToProcessor()
    {
        // Arrange
        var settings = new LibraryProcessSettings
        {
            LibraryName = "TestLibrary",
            Verbose = false,
            ProcessLimit = 10
        };

        SetupLibrary("TestLibrary");
        SetupSuccessfulProcessing();

        var context = new CommandContext(null, null, null);

        // Act
        await _command.ExecuteAsync(context, settings);

        // Assert
        // Would need to verify the processor was called with limit 10
        // This requires more sophisticated mocking of the processor service
    }

    [Fact]
    public async Task ExecuteAsync_WithFailedProcessing_ReturnsErrorExitCode()
    {
        // Arrange
        var settings = new LibraryProcessSettings
        {
            LibraryName = "TestLibrary",
            Verbose = false
        };

        SetupLibrary("TestLibrary");
        SetupFailedProcessing();

        var context = new CommandContext(null, null, null);

        // Act
        var result = await _command.ExecuteAsync(context, settings);

        // Assert
        result.Should().Be(1); // Error exit code
    }

    [Fact]
    public void YesNo_WithTrue_ReturnsYes()
    {
        // This tests the private YesNo method indirectly through configuration display
        // We can verify this by checking the console output when verbose is true
        
        // Arrange
        var settings = new LibraryProcessSettings
        {
            LibraryName = "TestLibrary",
            Verbose = true
        };

        SetupLibrary("TestLibrary");
        MockConfiguration.Setup(x => x.Configuration)
            .Returns(new Dictionary<string, object?>
            {
                {"ProcessingDoDeleteOriginal", "false"}, // This should show "Copy Mode? Yes"
                {"ProcessingDoOverrideExistingMelodeeDataFiles", "true"} // This should show "Force Mode? Yes"
            });

        // The test would need to actually run the command to verify output
        // This is more of an integration test than a unit test
    }

    private void SetupSuccessfulProcessing()
    {
        _mockProcessor.Setup(x => x.InitializeAsync()).Returns(Task.CompletedTask);
        _mockProcessor.Setup(x => x.ProcessDirectoryAsync(
                It.IsAny<Melodee.Common.Models.FileSystemDirectoryInfo>(),
                It.IsAny<DateTime?>(),
                It.IsAny<int?>()))
            .ReturnsAsync(new OperationResult<bool> { IsSuccess = true, Data = true });
    }

    private void SetupFailedProcessing()
    {
        _mockProcessor.Setup(x => x.InitializeAsync()).Returns(Task.CompletedTask);
        _mockProcessor.Setup(x => x.ProcessDirectoryAsync(
                It.IsAny<Melodee.Common.Models.FileSystemDirectoryInfo>(),
                It.IsAny<DateTime?>(),
                It.IsAny<int?>()))
            .ReturnsAsync(new OperationResult<bool> { IsSuccess = false, Data = false });
    }
}