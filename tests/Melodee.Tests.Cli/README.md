# Melodee.Tests.Cli

This test project provides comprehensive unit tests for the Melodee.Cli application, focusing on command functionality, validation, and CLI behavior.

## Overview

The test suite validates the CLI application's core functionality:

- **Command Execution**: Tests for individual CLI commands like `ProcessInboundCommand` and `ConfigurationSetCommand`
- **Command Settings Validation**: Tests for command parameter validation and default values
- **Command Base Infrastructure**: Tests for the shared `CommandBase` functionality and service provider setup
- **CLI Configuration**: Integration tests for the overall CLI application structure

## Test Structure

```
tests/Melodee.Tests.Cli/
├── Commands/
│   ├── CommandBaseTests.cs              # Tests for base command functionality
│   ├── ConfigurationSetCommandTests.cs  # Tests for configuration management command
│   └── ProcessInboundCommandTests.cs    # Tests for library processing command
├── CommandSettings/
│   ├── ConfigurationSetSettingTests.cs  # Tests for configuration command settings
│   ├── LibraryProcessSettingsTests.cs   # Tests for library processing settings
│   └── LibrarySettingsTests.cs          # Tests for base library settings
├── Integration/
│   └── ProgramTests.cs                   # Integration tests for CLI app structure
├── Helpers/
│   └── CliTestBase.cs                    # Base test class with common setup
└── README.md                             # This file
```

## Key Test Categories

### Command Tests

1. **ProcessInboundCommand Tests**
   - Validates library processing workflow
   - Tests success/failure exit codes
   - Verifies verbose output functionality
   - Tests process limits and force mode

2. **ConfigurationSetCommand Tests**
   - Tests configuration key/value management
   - Validates create vs update behavior
   - Tests remove functionality
   - Verifies error handling for failed operations

3. **CommandBase Tests**
   - Tests service provider configuration
   - Validates database context setup
   - Tests configuration loading
   - Verifies service registration

### Command Settings Tests

1. **Validation Tests**
   - Tests required field validation
   - Validates default values
   - Tests edge cases and boundary conditions

2. **Attribute Tests**
   - Verifies command argument/option attributes
   - Tests description attributes
   - Validates default value attributes

### Integration Tests

1. **Program Configuration Tests**
   - Tests overall CLI application structure
   - Validates command branch configuration
   - Tests version display functionality

## Running Tests

```bash
# Run all CLI tests
dotnet test tests/Melodee.Tests.Cli/

# Run specific test category
dotnet test tests/Melodee.Tests.Cli/ --filter Category=Commands

# Run with verbose output
dotnet test tests/Melodee.Tests.Cli/ --verbosity normal

# Generate test coverage report
dotnet test tests/Melodee.Tests.Cli/ --collect:"XPlat Code Coverage"
```

## Test Framework and Tools

- **xUnit**: Primary testing framework
- **FluentAssertions**: Assertion library for readable test code
- **Moq**: Mocking framework for dependencies
- **Spectre.Console.Testing**: Testing utilities for console applications
- **EntityFrameworkCore.InMemory**: In-memory database for testing

## Key CLI Commands Tested

### Library Processing (`library process`)
- **Command**: `ProcessInboundCommand`
- **Settings**: `LibraryProcessSettings`
- **Tests**: Process limits, force mode, copy vs move, verbose output

### Configuration Management (`configuration set`)
- **Command**: `ConfigurationSetCommand`
- **Settings**: `ConfigurationSetSetting`
- **Tests**: Key/value updates, creation, removal, error handling

## Testing Patterns

### Command Testing Pattern
```csharp
[Fact]
public async Task ExecuteAsync_WithValidInput_ReturnsSuccessExitCode()
{
    // Arrange
    var settings = new CommandSettings { /* setup */ };
    SetupMocks();
    
    // Act
    var result = await command.ExecuteAsync(context, settings);
    
    // Assert
    result.Should().Be(0); // Success exit code
}
```

### Settings Validation Pattern
```csharp
[Fact]
public void Validate_WithInvalidInput_ReturnsError()
{
    // Arrange
    var settings = new Settings { /* invalid setup */ };
    
    // Act
    var result = settings.Validate();
    
    // Assert
    result.Successful.Should().BeFalse();
}
```

## Mock Setup

The `CliTestBase` class provides common mocking infrastructure:
- Mocked configuration factory and services
- Test console for output verification
- In-memory database contexts
- Service provider setup

## CLI-Specific Considerations

1. **Exit Codes**: Tests verify correct exit codes (0 = success, 1 = error)
2. **Console Output**: Tests can verify console output using `TestConsole`
3. **Service Dependencies**: Commands depend on various services that are mocked for testing
4. **Configuration**: Tests use in-memory configuration for isolation

## Future Enhancements

The test infrastructure supports:
- Additional command testing as new CLI commands are added
- End-to-end CLI testing with actual command line arguments
- Performance testing for library processing operations
- Integration testing with real databases

## Dependencies

All package versions are managed centrally via `Directory.Packages.props` in the solution root.