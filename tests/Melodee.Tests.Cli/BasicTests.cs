using FluentAssertions;

namespace Melodee.Tests.Cli;

/// <summary>
/// Basic tests to validate test project setup
/// </summary>
public class BasicTests
{
    [Fact]
    public void TestProject_IsConfiguredCorrectly()
    {
        // This is a basic test to ensure the test project compiles and runs
        true.Should().BeTrue();
    }

    [Fact]
    public void CliAssembly_CanBeLoaded()
    {
        // Test that we can load the CLI assembly
        var cliAssembly = typeof(Melodee.Cli.Program).Assembly;
        cliAssembly.Should().NotBeNull();
        cliAssembly.GetName().Name.Should().Be("Melodee.Cli");
    }

    [Fact]
    public void CommonAssembly_CanBeLoaded()
    {
        // Test that we can load the Common assembly
        var commonAssembly = typeof(Melodee.Common.Services.LibraryService).Assembly;
        commonAssembly.Should().NotBeNull();
        commonAssembly.GetName().Name.Should().Be("Melodee.Common");
    }

    [Theory]
    [InlineData("TestValue1")]
    [InlineData("TestValue2")]
    [InlineData("TestValue3")]
    public void ParameterizedTest_Works(string testValue)
    {
        // Test that parameterized tests work
        testValue.Should().NotBeNullOrEmpty();
        testValue.Should().StartWith("Test");
    }
}