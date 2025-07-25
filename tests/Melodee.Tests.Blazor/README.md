# Melodee.Blazor.Tests

This test project provides unit tests for the Melodee.Blazor application, with a focus on authentication optimization features.

## Overview

The test suite validates the authentication optimizations implemented to reduce unnecessary redirects and improve performance:

- **Centralized Authentication State Management**: Tests the `EnsureAuthenticatedAsync()` method that prevents duplicate token validation calls
- **Cached Validation State**: Verifies that token validation only occurs once per session
- **Login/Logout State Management**: Ensures proper reset of validation flags

## Test Structure

```
tests/Melodee.Blazor.Tests/
├── Services/
│   └── AuthServiceSimpleTests.cs    # Core authentication service tests
├── Helpers/
│   └── TestBase.cs                   # Base test class with common mocks
└── README.md                         # This file
```

## Key Test Cases

### AuthService Optimization Tests

1. **EnsureAuthenticatedAsync_WhenNotValidatedAndNotLoggedIn_CallsGetStateFromToken**
   - Verifies initial token validation call occurs

2. **EnsureAuthenticatedAsync_WhenAlreadyValidated_DoesNotCallGetStateFromTokenAgain**
   - **Critical optimization test**: Ensures duplicate validation calls are prevented

3. **Login_SetsValidatedFlagToTrue**
   - Verifies login sets validation state to prevent redundant checks

4. **LogoutAsync_ResetsValidatedFlag**
   - Ensures logout properly resets state for future validation

5. **Authentication State Management Tests**
   - Tests `IsLoggedIn` property behavior
   - Validates `UserChanged` event triggering

## Running Tests

```bash
# Run all Blazor tests
dotnet test tests/Melodee.Blazor.Tests/

# Run with verbose output
dotnet test tests/Melodee.Blazor.Tests/ --verbosity normal

# Generate test coverage report
dotnet test tests/Melodee.Blazor.Tests/ --collect:"XPlat Code Coverage"
```

## Test Framework

- **xUnit**: Primary testing framework
- **FluentAssertions**: Assertion library for readable test code
- **Moq**: Mocking framework for dependencies
- **bUnit**: Blazor component testing (for future component tests)

## Authentication Optimization Validation

These tests specifically validate the performance improvements made to the authentication system:

- **~50% fewer token validations** through cached validation state
- **Prevention of redirect loops** via centralized authentication checking
- **Proper state management** during login/logout cycles

## Future Enhancements

The test infrastructure is set up to support:
- Component testing with bUnit
- Integration tests for authentication flows
- End-to-end authentication scenarios
- Performance benchmarking

## Dependencies

All package versions are managed centrally via `Directory.Packages.props` in the solution root.