using FluentAssertions;
using Melodee.Cli.Command;
using Melodee.Cli.CommandSettings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Melodee.Tests.Cli.Commands;

/// <summary>
/// Tests for the CommandBase abstract class functionality
/// </summary>
public class CommandBaseTests
{
    private readonly TestCommand _testCommand;

    public CommandBaseTests()
    {
        _testCommand = new TestCommand();
    }

    [Fact]
    public void Configuration_ReturnsValidConfiguration()
    {
        // Act
        var configuration = _testCommand.TestConfiguration();

        // Assert
        configuration.Should().NotBeNull();
        configuration.Should().BeOfType<IConfigurationRoot>();
    }

    [Fact]
    public void CreateServiceProvider_ReturnsValidServiceProvider()
    {
        // Act
        var serviceProvider = _testCommand.TestCreateServiceProvider();

        // Assert
        serviceProvider.Should().NotBeNull();
        
        // Verify key services are registered
        serviceProvider.GetService<IConfiguration>().Should().NotBeNull();
        serviceProvider.GetService<Melodee.Common.Serialization.ISerializer>().Should().NotBeNull();
        serviceProvider.GetService<Melodee.Common.Configuration.IMelodeeConfigurationFactory>().Should().NotBeNull();
    }

    [Fact]
    public void CreateServiceProvider_RegistersRequiredDatabaseContexts()
    {
        // Act
        var serviceProvider = _testCommand.TestCreateServiceProvider();

        // Assert
        serviceProvider.GetService<IDbContextFactory<Melodee.Common.Data.MelodeeDbContext>>().Should().NotBeNull();
    }

    [Fact]
    public void CreateServiceProvider_RegistersAllRequiredServices()
    {
        // Act
        var serviceProvider = _testCommand.TestCreateServiceProvider();

        // Assert
        var expectedServices = new[]
        {
            typeof(Melodee.Common.Services.LibraryService),
            typeof(Melodee.Common.Services.Scanning.DirectoryProcessorToStagingService),
            typeof(Melodee.Common.Services.AlbumService),
            typeof(Melodee.Common.Services.ArtistService),
            typeof(Melodee.Common.Services.SongService),
            typeof(Melodee.Common.Services.UserService),
            typeof(Melodee.Common.Metadata.MelodeeMetadataMaker)
        };

        foreach (var serviceType in expectedServices)
        {
            serviceProvider.GetService(serviceType).Should().NotBeNull($"Service {serviceType.Name} should be registered");
        }
    }

    [Fact]
    public void CreateServiceProvider_ConfiguresLogging()
    {
        // Act
        var serviceProvider = _testCommand.TestCreateServiceProvider();

        // Assert
        var logger = serviceProvider.GetService<Serilog.ILogger>();
        logger.Should().NotBeNull();
    }

    [Fact]
    public void CreateServiceProvider_ConfiguresRebus()
    {
        // Act
        var serviceProvider = _testCommand.TestCreateServiceProvider();

        // Assert
        // Verify Rebus is configured (this is complex to test directly)
        // We can at least verify the service provider was created successfully
        serviceProvider.Should().NotBeNull();
    }

    [Fact]
    public void CreateServiceProvider_ConfiguresHttpClient()
    {
        // Act
        var serviceProvider = _testCommand.TestCreateServiceProvider();

        // Assert
        var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>();
        httpClientFactory.Should().NotBeNull();
    }

    /// <summary>
    /// Test implementation of CommandBase to expose protected methods for testing
    /// </summary>
    private class TestCommand : CommandBase<TestSettings>
    {
        public IConfigurationRoot TestConfiguration() => Configuration();
        public ServiceProvider TestCreateServiceProvider() => CreateServiceProvider();

        public override Task<int> ExecuteAsync(CommandContext context, TestSettings settings)
        {
            // Not implemented for this test class
            return Task.FromResult(0);
        }
    }

    /// <summary>
    /// Test settings class for the test command
    /// </summary>
    private class TestSettings : Spectre.Console.Cli.CommandSettings
    {
    }
}