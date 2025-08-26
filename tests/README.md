# CoffeeShop Agent Tests

This directory contains comprehensive unit and integration tests for the CoffeeShop Agent system, demonstrating the implementation of SOLID principles, DRY code, and security best practices.

## Test Structure

### Unit Tests

#### ServiceDefaults.Tests
Tests for the core shared services and infrastructure:

- **Agents/BaseAgentTests.cs** - Tests for the Template Method pattern implementation
- **Services/InputValidationServiceTests.cs** - Security validation tests (XSS prevention, input sanitization)
- **Services/A2AClientManagerTests.cs** - A2A client lifecycle management tests
- **Services/A2AMessageServiceTests.cs** - Message routing and communication tests
- **Services/A2AResponseMapperTests.cs** - Response mapping and transformation tests
- **Services/OrderParsingServiceTests.cs** - Order parsing logic with stubbed AI responses
- **Configuration/AgentConfigurationServiceTests.cs** - Configuration validation tests

#### CounterService.Tests
Tests for the refactored CounterAgent:

- **Agents/CounterAgentTests.cs** - Complete workflow tests with mocked dependencies

### Integration Tests

#### SamplesIntegrationTests
.NET Aspire integration tests following Microsoft patterns:

- **AppHostTests.cs** - Full application hosting and service discovery tests

## Key Testing Features

### SOLID Principles Testing
- **Single Responsibility**: Each service tested in isolation
- **Open/Closed**: Extensibility verified through dependency injection
- **Dependency Inversion**: All dependencies mocked/stubbed for unit tests

### Security Testing
- Input validation with XSS prevention
- SQL injection pattern detection
- Secure error message handling
- Configuration validation

### Mocking Strategy
Using Moq framework for:
- HTTP clients and A2A communication
- Logging verification
- Service dependency isolation
- AI/MCP service stubbing

### .NET Aspire Integration
- Service discovery verification
- Multi-service startup testing
- Health check validation
- Configuration validation

## Running Tests

### Prerequisites
- .NET 10.0 SDK (for production)
- .NET 8.0 SDK (for current testing due to preview limitations)

### Commands

```bash
# Run all unit tests
dotnet test tests/ServiceDefaults.Tests
dotnet test tests/CounterService.Tests

# Run integration tests
dotnet test tests/SamplesIntegrationTests

# Run all tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test class
dotnet test --filter "ClassName=InputValidationServiceTests"

# Run tests with detailed output
dotnet test --logger "console;verbosity=detailed"
```

### Build Helper
The project includes a build helper script for .NET version management:

```bash
# Change to .NET 8.0 for building/testing
./build-helper.sh build

# Restore to .NET 10.0 for production
./build-helper.sh restore
```

## Test Coverage

### Security Tests
- ✅ XSS prevention in InputValidationService
- ✅ Input length validation
- ✅ Control character removal
- ✅ Configuration URL validation
- ✅ Error message sanitization

### SOLID Compliance Tests
- ✅ Constructor dependency validation
- ✅ Interface segregation verification
- ✅ Single responsibility validation
- ✅ Template method pattern testing

### Integration Tests
- ✅ Service discovery functionality
- ✅ Multi-service startup validation
- ✅ Health check verification
- ✅ Configuration validation

### Business Logic Tests
- ✅ Order parsing with stub data
- ✅ A2A message routing
- ✅ Response mapping and transformation
- ✅ Complete Counter Agent workflow

## Best Practices Demonstrated

1. **Arrange-Act-Assert** pattern in all tests
2. **One assertion per logical concept** 
3. **Descriptive test names** explaining the scenario
4. **Comprehensive edge case coverage**
5. **Proper mock verification**
6. **Integration test isolation**

## Continuous Integration

Tests are designed to run in CI/CD pipelines with:
- Fast execution (unit tests < 1 second each)
- Reliable results (no flaky tests)
- Clear failure messages
- Comprehensive coverage reporting

## Future Enhancements

- Performance benchmarking tests
- Load testing for A2A communication
- End-to-end scenario tests
- Mock A2A server for integration testing
- Automated security scanning integration