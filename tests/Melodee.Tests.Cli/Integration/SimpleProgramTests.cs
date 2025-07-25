using FluentAssertions;

namespace Melodee.Tests.Cli.Integration;

/// <summary>
/// Simple tests for CLI program structure
/// </summary>
public class SimpleProgramTests
{
    [Fact]
    public void Program_Assembly_CanBeLoaded()
    {
        // Test that we can access the CLI program
        var programType = typeof(Melodee.Cli.Program);
        
        programType.Should().NotBeNull();
        programType.Assembly.GetName().Name.Should().Be("Melodee.Cli");
    }

    [Fact]
    public void Program_HasMainMethod()
    {
        var programType = typeof(Melodee.Cli.Program);
        var mainMethod = programType.GetMethod("Main", new[] { typeof(string[]) });

        mainMethod.Should().NotBeNull();
        mainMethod!.IsStatic.Should().BeTrue();
        mainMethod.IsPublic.Should().BeTrue();
        mainMethod.ReturnType.Should().Be(typeof(int));
    }

    [Fact]
    public void CommandSettings_CanBeInstantiated()
    {
        // Test that command setting classes can be created
        var librarySettings = new Melodee.Cli.CommandSettings.LibrarySettings();
        var processSettings = new Melodee.Cli.CommandSettings.LibraryProcessSettings();
        var configSettings = new Melodee.Cli.CommandSettings.ConfigurationSetSetting();

        librarySettings.Should().NotBeNull();
        processSettings.Should().NotBeNull();
        configSettings.Should().NotBeNull();
    }

    [Fact]
    public void Commands_CanBeInstantiated()
    {
        // Test that command classes can be created
        var configCommand = new Melodee.Cli.Command.ConfigurationSetCommand();
        var processCommand = new Melodee.Cli.Command.ProcessInboundCommand();

        configCommand.Should().NotBeNull();
        processCommand.Should().NotBeNull();
    }

    [Fact]
    public void CommandSettings_InheritFromCorrectBase()
    {
        // Test inheritance hierarchy
        var librarySettings = new Melodee.Cli.CommandSettings.LibrarySettings();
        var processSettings = new Melodee.Cli.CommandSettings.LibraryProcessSettings();

        librarySettings.Should().BeAssignableTo<Spectre.Console.Cli.CommandSettings>();
        processSettings.Should().BeAssignableTo<Melodee.Cli.CommandSettings.LibrarySettings>();
    }
}