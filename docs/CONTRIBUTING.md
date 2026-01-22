# Contributing to IntelliTrader

Thank you for your interest in contributing to IntelliTrader! This guide covers the development workflow, coding standards, and PR process.

---

## Development Workflow

### 1. Fork and Clone

```bash
# Fork the repository on GitHub, then clone your fork
git clone https://github.com/YOUR_USERNAME/IntelliTrader.git
cd IntelliTrader

# Add upstream remote for syncing
git remote add upstream https://github.com/blackms/IntelliTrader.git
```

### 2. Set Up Development Environment

**Prerequisites**:
- .NET SDK 9.0 or later ([Download](https://dotnet.microsoft.com/download))
- IDE: Visual Studio 2022, VS Code with C# extension, or JetBrains Rider

**Verify setup**:
```bash
dotnet --version   # Should be 9.0.x or later
dotnet restore
dotnet build
dotnet test        # Should pass all 1,000+ tests
```

### 3. Branch Naming

Create a descriptive branch from `main`:

| Type | Pattern | Example |
|------|---------|---------|
| Feature | `feat/short-description` | `feat/multi-exchange-support` |
| Bug fix | `fix/short-description` | `fix/trailing-stop-calculation` |
| Docs | `docs/short-description` | `docs/api-reference` |
| Refactor | `refactor/short-description` | `refactor/trading-service-split` |
| Test | `test/short-description` | `test/exchange-adapter-coverage` |
| Chore | `chore/short-description` | `chore/update-dependencies` |

```bash
# Create and switch to feature branch
git checkout -b feat/your-feature-name

# Keep your branch updated with upstream
git fetch upstream
git rebase upstream/main
```

### 4. Making Changes

**Code style**:
- Follow existing patterns in the codebase
- Use meaningful variable and method names
- Add XML documentation for public APIs
- Keep methods focused and small

**Commit messages** (Conventional Commits):
```
<type>(<scope>): <description>

[optional body]

[optional footer]
```

**Types**: `feat`, `fix`, `docs`, `style`, `refactor`, `test`, `chore`, `perf`

**Examples**:
```bash
git commit -m "feat(trading): add Kelly Criterion position sizing"
git commit -m "fix(exchange): handle rate limit errors with exponential backoff"
git commit -m "docs(readme): add architecture diagram"
git commit -m "test(trading): add TradingService.CanBuy coverage"
```

### 5. Running Tests

**Run all tests**:
```bash
dotnet test
```

**Run specific test project**:
```bash
dotnet test tests/IntelliTrader.Trading.Tests/
```

**Run with coverage**:
```bash
dotnet test --collect:"XPlat Code Coverage"
```

**Filter tests by name**:
```bash
dotnet test --filter "FullyQualifiedName~CanBuy"
dotnet test --filter "Category=Integration"
```

**Test requirements**:
- All existing tests must pass
- New code should include tests
- Maintain or improve code coverage

---

## PR Guidelines

### PR Template Expectations

When creating a PR, include:

**Title**: Follow conventional commit format
```
feat(trading): add support for limit orders
```

**Description**:
```markdown
## Summary
Brief description of what this PR does and why.

## Changes
- Added LimitOrder support in TradingService
- Updated ExchangeService interface with new method
- Added 15 new tests for limit order scenarios

## Testing
- [ ] All existing tests pass
- [ ] New tests added for new functionality
- [ ] Manually tested in virtual trading mode

## Screenshots (if applicable)
[Add screenshots for UI changes]

## Related Issues
Fixes #123
```

### Review Checklist

Before requesting review, ensure:

**Code Quality**:
- [ ] Code follows existing patterns and conventions
- [ ] No commented-out code or debug statements
- [ ] No hardcoded values (use `Constants` class)
- [ ] Meaningful names for variables, methods, classes

**Architecture**:
- [ ] Changes align with Clean Architecture layers
- [ ] Dependencies flow inward (Infrastructure -> Application -> Domain)
- [ ] New services registered in appropriate `AppModule.cs`

**Documentation**:
- [ ] XML docs for new public APIs
- [ ] README updated if adding features
- [ ] Config changes documented

**Testing**:
- [ ] Unit tests for new business logic
- [ ] Integration tests for new adapters
- [ ] Edge cases covered
- [ ] Tests follow `MethodName_Scenario_ExpectedResult` naming

**Safety**:
- [ ] No secrets or API keys in code
- [ ] Error handling for external calls
- [ ] Null checks where appropriate
- [ ] Thread-safety considered for shared state

### CI Requirements

PRs must pass all CI checks:

1. **Build**: Solution must compile without errors
2. **Tests**: All 1,000+ tests must pass
3. **Coverage**: Coverage should not decrease significantly
4. **Linting**: No compiler warnings for new code

---

## Code Style Guide

### C# Conventions

**Naming**:
```csharp
// Interfaces: I prefix
public interface ITradingService { }

// Classes: PascalCase
public class TradingService { }

// Private fields: underscore prefix
private readonly ILoggingService _loggingService;

// Methods: PascalCase
public void PlaceOrder(BuyOptions options) { }

// Parameters/locals: camelCase
public void Buy(BuyOptions options)
{
    var currentPrice = GetCurrentPrice(options.Pair);
}

// Constants: PascalCase in static class
public static class Constants
{
    public const int DefaultTimeoutSeconds = 30;
}
```

**Async methods**:
```csharp
// Async methods end with Async suffix
public async Task<IOrderDetails> PlaceOrderAsync(
    IOrder order,
    CancellationToken cancellationToken = default)
{
    // Use ConfigureAwait(false) in library code
    var result = await _exchangeService
        .ExecuteOrderAsync(order, cancellationToken)
        .ConfigureAwait(false);

    return result;
}
```

**DI and interfaces**:
```csharp
// Constructor injection with primary constructor (C# 12+)
internal class TradingService(
    ILoggingService loggingService,
    INotificationService notificationService,
    IExchangeService exchangeService)
    : ITradingService
{
    // Store as readonly if needed beyond constructor
    private readonly IExchangeService _exchangeService = exchangeService;
}
```

### Domain Layer Patterns

**Value Objects**:
```csharp
public sealed class Price : ValueObject, IComparable<Price>
{
    public decimal Value { get; }

    private Price(decimal value) => Value = value;

    // Factory method with validation
    public static Price Create(decimal value)
    {
        if (value < 0)
            throw new ArgumentException("Price cannot be negative", nameof(value));
        return new Price(value);
    }

    // Static factory for common cases
    public static Price Zero => new(0m);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
```

### Test Patterns

**Test structure**:
```csharp
public class TradingServiceTests
{
    // Mocks as fields
    private readonly Mock<IExchangeService> _exchangeServiceMock;
    private readonly TradingService _sut;  // System Under Test

    public TradingServiceTests()
    {
        // Setup in constructor
        _exchangeServiceMock = new Mock<IExchangeService>();
        _sut = new TradingService(_exchangeServiceMock.Object, ...);
    }

    #region CanBuy Tests

    [Fact]
    public void CanBuy_WhenTradingSuspended_ReturnsFalse()
    {
        // Arrange
        SetupTradingSuspended(true);
        var options = new BuyOptions("BTCUSDT") { MaxCost = 100m };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("trading suspended");
    }

    [Theory]
    [InlineData(100, 50, true)]   // Greater
    [InlineData(50, 100, false)]  // Less
    public void Compare_ReturnsExpectedResult(decimal a, decimal b, bool expected)
    {
        // Parameterized test
    }

    #endregion

    // Helper methods at the bottom
    private void SetupTradingSuspended(bool suspended) { ... }
}
```

---

## Getting Help

- **Questions**: Open a GitHub Discussion
- **Bugs**: Open an Issue with reproduction steps
- **Features**: Open an Issue to discuss before implementing
- **Security**: Email maintainers directly (do not open public issue)

---

## Recognition

Contributors are recognized in:
- Release notes
- Contributors section of README
- Git commit history

Thank you for helping make IntelliTrader better!
