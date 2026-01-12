using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.Services;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Tests.Trading.Services;

public class TradingConstraintValidatorTests
{
    private readonly TradingConstraintValidator _validator = new();

    private static Portfolio CreateTestPortfolio(
        decimal balance = 10000m,
        int maxPositions = 5,
        decimal minPositionCost = 100m)
    {
        return Portfolio.Create("Test", "USDT", balance, maxPositions, minPositionCost);
    }

    private static Position CreateTestPosition(
        string pair = "BTCUSDT",
        decimal entryPrice = 100m,
        decimal quantity = 10m)
    {
        return Position.Open(
            TradingPair.Create(pair, "USDT"),
            OrderId.From("order-1"),
            Price.Create(entryPrice),
            Quantity.Create(quantity),
            Money.Create(0m, "USDT"));
    }

    #region ValidateOpenPosition

    [Fact]
    public void ValidateOpenPosition_WithValidParameters_ReturnsValid()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var cost = Money.Create(1000m, "USDT");

        // Act
        var result = _validator.ValidateOpenPosition(portfolio, pair, cost);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.HasSufficientFunds.Should().BeTrue();
        result.HasPositionSlot.Should().BeTrue();
        result.IsNewPair.Should().BeTrue();
    }

    [Fact]
    public void ValidateOpenPosition_WithInsufficientFunds_ReturnsInvalid()
    {
        // Arrange
        var portfolio = CreateTestPortfolio(balance: 500m);
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var cost = Money.Create(1000m, "USDT");

        // Act
        var result = _validator.ValidateOpenPosition(portfolio, pair, cost);

        // Assert
        result.IsValid.Should().BeFalse();
        result.HasSufficientFunds.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ValidationErrorCode.InsufficientFunds);
    }

    [Fact]
    public void ValidateOpenPosition_AtMaxPositions_ReturnsInvalid()
    {
        // Arrange
        var portfolio = CreateTestPortfolio(maxPositions: 1);
        var position = CreateTestPosition();
        portfolio.RecordPositionOpened(
            PositionId.Create(),
            position.Pair,
            Money.Create(500m, "USDT"));

        var newPair = TradingPair.Create("ETHUSDT", "USDT");
        var cost = Money.Create(500m, "USDT");

        // Act
        var result = _validator.ValidateOpenPosition(portfolio, newPair, cost);

        // Assert
        result.IsValid.Should().BeFalse();
        result.HasPositionSlot.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ValidationErrorCode.MaxPositionsReached);
    }

    [Fact]
    public void ValidateOpenPosition_WithExistingPosition_ReturnsInvalid()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        portfolio.RecordPositionOpened(
            PositionId.Create(),
            pair,
            Money.Create(500m, "USDT"));

        var cost = Money.Create(500m, "USDT");

        // Act
        var result = _validator.ValidateOpenPosition(portfolio, pair, cost);

        // Assert
        result.IsValid.Should().BeFalse();
        result.IsNewPair.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ValidationErrorCode.PositionAlreadyExists);
    }

    [Fact]
    public void ValidateOpenPosition_BelowMinimumCost_ReturnsInvalid()
    {
        // Arrange
        var portfolio = CreateTestPortfolio(minPositionCost: 100m);
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var cost = Money.Create(50m, "USDT");

        // Act
        var result = _validator.ValidateOpenPosition(portfolio, pair, cost);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ValidationErrorCode.BelowMinimumPositionCost);
    }

    [Fact]
    public void ValidateOpenPosition_WithCurrencyMismatch_ReturnsInvalid()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var cost = Money.Create(1000m, "BTC"); // Wrong currency

        // Act
        var result = _validator.ValidateOpenPosition(portfolio, pair, cost);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == ValidationErrorCode.CurrencyMismatch);
    }

    [Fact]
    public void ValidateOpenPosition_WithMultipleErrors_ReturnsAllErrors()
    {
        // Arrange
        var portfolio = CreateTestPortfolio(balance: 50m, maxPositions: 1, minPositionCost: 100m);
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        portfolio.RecordPositionOpened(
            PositionId.Create(),
            pair,
            Money.Create(50m, "USDT"));

        var cost = Money.Create(1000m, "USDT");

        // Act
        var result = _validator.ValidateOpenPosition(portfolio, pair, cost);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThan(1);
        result.Errors.Should().Contain(e => e.Code == ValidationErrorCode.PositionAlreadyExists);
        result.Errors.Should().Contain(e => e.Code == ValidationErrorCode.MaxPositionsReached);
        result.Errors.Should().Contain(e => e.Code == ValidationErrorCode.InsufficientFunds);
    }

    #endregion

    #region ValidateDCA

    [Fact]
    public void ValidateDCA_WithValidParameters_ReturnsValid()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m);
        portfolio.RecordPositionOpened(
            position.Id,
            position.Pair,
            Money.Create(1000m, "USDT"));

        var additionalCost = Money.Create(500m, "USDT");
        var currentPrice = Price.Create(80m); // 20% drop
        var constraints = DCAConstraints.Create(maxDCALevels: 5, minPriceDropPercent: 10m);

        // Act
        var result = _validator.ValidateDCA(portfolio, position, additionalCost, currentPrice, constraints);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.CanDCA.Should().BeTrue();
        result.HasSufficientPriceDrop.Should().BeTrue();
    }

    [Fact]
    public void ValidateDCA_AtMaxDCALevels_ReturnsInvalid()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var position = CreateTestPosition();
        portfolio.RecordPositionOpened(
            position.Id,
            position.Pair,
            Money.Create(1000m, "USDT"));

        // Add DCA entries to reach max level
        position.AddDCAEntry(OrderId.From("order-2"), Price.Create(90m), Quantity.Create(10m), Money.Create(0m, "USDT"));

        var constraints = DCAConstraints.Create(maxDCALevels: 1);
        var currentPrice = Price.Create(80m);

        // Act
        var result = _validator.ValidateDCA(portfolio, position, Money.Create(500m, "USDT"), currentPrice, constraints);

        // Assert
        result.IsValid.Should().BeFalse();
        result.CanDCA.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ValidationErrorCode.MaxDCALevelsReached);
    }

    [Fact]
    public void ValidateDCA_WithInsufficientPriceDrop_ReturnsInvalid()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var position = CreateTestPosition(entryPrice: 100m);
        portfolio.RecordPositionOpened(
            position.Id,
            position.Pair,
            Money.Create(1000m, "USDT"));

        var currentPrice = Price.Create(95m); // Only 5% drop
        var constraints = DCAConstraints.Create(minPriceDropPercent: 10m);

        // Act
        var result = _validator.ValidateDCA(portfolio, position, Money.Create(500m, "USDT"), currentPrice, constraints);

        // Assert
        result.IsValid.Should().BeFalse();
        result.HasSufficientPriceDrop.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ValidationErrorCode.InsufficientPriceDrop);
    }

    [Fact]
    public void ValidateDCA_WithInsufficientMarginDrop_ReturnsInvalid()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var position = CreateTestPosition(entryPrice: 100m);
        portfolio.RecordPositionOpened(
            position.Id,
            position.Pair,
            Money.Create(1000m, "USDT"));

        var currentPrice = Price.Create(98m); // Only ~2% loss
        var constraints = DCAConstraints.Create(minMarginDropPercent: 5m);

        // Act
        var result = _validator.ValidateDCA(portfolio, position, Money.Create(500m, "USDT"), currentPrice, constraints);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ValidationErrorCode.InsufficientMarginDrop);
    }

    [Fact]
    public void ValidateDCA_WithInsufficientFunds_ReturnsInvalid()
    {
        // Arrange
        var portfolio = CreateTestPortfolio(balance: 1100m);
        var position = CreateTestPosition();
        portfolio.RecordPositionOpened(
            position.Id,
            position.Pair,
            Money.Create(1000m, "USDT"));

        var additionalCost = Money.Create(500m, "USDT"); // Only 100 available
        var constraints = DCAConstraints.Default;

        // Act
        var result = _validator.ValidateDCA(portfolio, position, additionalCost, Price.Create(80m), constraints);

        // Assert
        result.IsValid.Should().BeFalse();
        result.HasSufficientFunds.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ValidationErrorCode.InsufficientFunds);
    }

    [Fact]
    public void ValidateDCA_ExceedsMaxTotalCost_ReturnsInvalid()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m);
        portfolio.RecordPositionOpened(
            position.Id,
            position.Pair,
            Money.Create(1000m, "USDT"));

        var constraints = DCAConstraints.Create(maxTotalPositionCost: 1200m);
        var additionalCost = Money.Create(500m, "USDT"); // Would exceed 1200

        // Act
        var result = _validator.ValidateDCA(portfolio, position, additionalCost, Price.Create(80m), constraints);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ValidationErrorCode.MaxPositionCostExceeded);
    }

    [Fact]
    public void ValidateDCA_PositionNotInPortfolio_ReturnsInvalid()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var position = CreateTestPosition();
        // Position not added to portfolio

        var constraints = DCAConstraints.Default;

        // Act
        var result = _validator.ValidateDCA(portfolio, position, Money.Create(500m, "USDT"), Price.Create(80m), constraints);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ValidationErrorCode.PositionNotFound);
    }

    #endregion

    #region ValidateClosePosition

    [Fact]
    public void ValidateClosePosition_WithProfitablePosition_ReturnsValid()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var position = CreateTestPosition(entryPrice: 100m);
        portfolio.RecordPositionOpened(
            position.Id,
            position.Pair,
            Money.Create(1000m, "USDT"));

        var currentPrice = Price.Create(110m); // 10% profit

        // Act
        var result = _validator.ValidateClosePosition(portfolio, position, currentPrice);

        // Assert
        result.IsValid.Should().BeTrue();
        result.IsProfitable.Should().BeTrue();
        result.CurrentMargin.Percentage.Should().Be(10m);
    }

    [Fact]
    public void ValidateClosePosition_BelowMinProfit_WhenEnforced_ReturnsInvalid()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var position = CreateTestPosition(entryPrice: 100m);
        portfolio.RecordPositionOpened(
            position.Id,
            position.Pair,
            Money.Create(1000m, "USDT"));

        var currentPrice = Price.Create(105m); // 5% profit
        var constraints = ClosePositionConstraints.Create(minProfitPercent: 10m, enforceMinProfit: true);

        // Act
        var result = _validator.ValidateClosePosition(portfolio, position, currentPrice, constraints);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ValidationErrorCode.BelowMinimumProfit);
    }

    [Fact]
    public void ValidateClosePosition_BelowMinProfit_WhenNotEnforced_ReturnsValidWithWarning()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var position = CreateTestPosition(entryPrice: 100m);
        portfolio.RecordPositionOpened(
            position.Id,
            position.Pair,
            Money.Create(1000m, "USDT"));

        var currentPrice = Price.Create(105m); // 5% profit
        var constraints = ClosePositionConstraints.Create(minProfitPercent: 10m, enforceMinProfit: false);

        // Act
        var result = _validator.ValidateClosePosition(portfolio, position, currentPrice, constraints);

        // Assert
        result.IsValid.Should().BeTrue();
        result.HasWarnings.Should().BeTrue();
        result.Warnings.Should().ContainSingle(w => w.Code == ValidationWarningCode.BelowTargetProfit);
    }

    [Fact]
    public void ValidateClosePosition_PositionNotInPortfolio_ReturnsInvalid()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var position = CreateTestPosition();
        // Position not added to portfolio

        var currentPrice = Price.Create(110m);

        // Act
        var result = _validator.ValidateClosePosition(portfolio, position, currentPrice);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ValidationErrorCode.PositionNotFound);
    }

    [Fact]
    public void ValidateClosePosition_WithLoss_IncludesWarningIfConfigured()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var position = CreateTestPosition(entryPrice: 100m);
        portfolio.RecordPositionOpened(
            position.Id,
            position.Pair,
            Money.Create(1000m, "USDT"));

        var currentPrice = Price.Create(85m); // 15% loss
        var constraints = ClosePositionConstraints.Create(maxLossPercent: 10m);

        // Act
        var result = _validator.ValidateClosePosition(portfolio, position, currentPrice, constraints);

        // Assert
        result.IsValid.Should().BeTrue();
        result.IsProfitable.Should().BeFalse();
        result.HasWarnings.Should().BeTrue();
        result.Warnings.Should().ContainSingle(w => w.Code == ValidationWarningCode.ExceedsMaxLoss);
    }

    [Fact]
    public void ValidateClosePosition_CalculatesUnrealizedPnL()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m);
        portfolio.RecordPositionOpened(
            position.Id,
            position.Pair,
            Money.Create(1000m, "USDT"));

        var currentPrice = Price.Create(110m); // Value = 1100

        // Act
        var result = _validator.ValidateClosePosition(portfolio, position, currentPrice);

        // Assert
        result.UnrealizedPnL.Amount.Should().Be(100m); // 1100 - 1000
    }

    #endregion

    #region ValidateOrder

    [Fact]
    public void ValidateOrder_WithValidParameters_ReturnsValid()
    {
        // Arrange
        var cost = Money.Create(1000m, "USDT");
        var quantity = Quantity.Create(10m);
        var price = Price.Create(100m);
        var constraints = OrderConstraints.Create(minOrderValue: 10m);

        // Act
        var result = _validator.ValidateOrder(cost, quantity, price, constraints);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateOrder_BelowMinimumValue_ReturnsInvalid()
    {
        // Arrange
        var cost = Money.Create(5m, "USDT");
        var quantity = Quantity.Create(0.05m);
        var price = Price.Create(100m);
        var constraints = OrderConstraints.Create(minOrderValue: 10m);

        // Act
        var result = _validator.ValidateOrder(cost, quantity, price, constraints);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ValidationErrorCode.BelowMinimumOrderValue);
    }

    [Fact]
    public void ValidateOrder_ExceedsMaximumValue_ReturnsInvalid()
    {
        // Arrange
        var cost = Money.Create(10000m, "USDT");
        var quantity = Quantity.Create(100m);
        var price = Price.Create(100m);
        var constraints = OrderConstraints.Create(maxOrderValue: 5000m);

        // Act
        var result = _validator.ValidateOrder(cost, quantity, price, constraints);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ValidationErrorCode.ExceedsMaximumOrderValue);
    }

    [Fact]
    public void ValidateOrder_BelowMinimumQuantity_ReturnsInvalid()
    {
        // Arrange
        var cost = Money.Create(100m, "USDT");
        var quantity = Quantity.Create(0.001m);
        var price = Price.Create(100000m);
        var constraints = OrderConstraints.Create(minQuantity: 0.01m);

        // Act
        var result = _validator.ValidateOrder(cost, quantity, price, constraints);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ValidationErrorCode.BelowMinimumQuantity);
    }

    [Fact]
    public void ValidateOrder_WithCostQuantityMismatch_ReturnsInvalid()
    {
        // Arrange
        var cost = Money.Create(1000m, "USDT");
        var quantity = Quantity.Create(10m);
        var price = Price.Create(50m); // Should be 500, not 1000
        var constraints = OrderConstraints.Default;

        // Act
        var result = _validator.ValidateOrder(cost, quantity, price, constraints);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ValidationErrorCode.CostQuantityMismatch);
    }

    #endregion

    #region ValidateTradingPair

    [Fact]
    public void ValidateTradingPair_WithValidPair_ReturnsValid()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        // Act
        var result = _validator.ValidateTradingPair(pair, "USDT");

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateTradingPair_WithWrongMarket_ReturnsInvalid()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        // Act
        var result = _validator.ValidateTradingPair(pair, "BTC");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ValidationErrorCode.InvalidMarket);
    }

    [Fact]
    public void ValidateTradingPair_WhenBlocked_ReturnsInvalid()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var blockedPairs = new HashSet<string> { "BTCUSDT" };

        // Act
        var result = _validator.ValidateTradingPair(pair, "USDT", blockedPairs: blockedPairs);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ValidationErrorCode.PairBlocked);
    }

    [Fact]
    public void ValidateTradingPair_NotInAllowedList_ReturnsInvalid()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var allowedPairs = new HashSet<string> { "ETHUSDT", "XRPUSDT" };

        // Act
        var result = _validator.ValidateTradingPair(pair, "USDT", allowedPairs: allowedPairs);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ValidationErrorCode.PairNotAllowed);
    }

    [Fact]
    public void ValidateTradingPair_InAllowedList_ReturnsValid()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var allowedPairs = new HashSet<string> { "BTCUSDT", "ETHUSDT" };

        // Act
        var result = _validator.ValidateTradingPair(pair, "USDT", allowedPairs: allowedPairs);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region ValidatePreTrade

    [Fact]
    public void ValidatePreTrade_WithAllValid_ReturnsValid()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var cost = Money.Create(1000m, "USDT");
        var quantity = Quantity.Create(10m);
        var price = Price.Create(100m);
        var orderConstraints = OrderConstraints.Create(minOrderValue: 10m);

        // Act
        var result = _validator.ValidatePreTrade(
            portfolio, pair, cost, quantity, price, orderConstraints);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.PairValidation.IsValid.Should().BeTrue();
        result.OrderValidation.IsValid.Should().BeTrue();
        result.PositionValidation.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidatePreTrade_WithMultipleFailures_CollectsAllErrors()
    {
        // Arrange
        var portfolio = CreateTestPortfolio(balance: 50m);
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var cost = Money.Create(1000m, "USDT");
        var quantity = Quantity.Create(10m);
        var price = Price.Create(100m);
        var orderConstraints = OrderConstraints.Create(minOrderValue: 10m, maxOrderValue: 500m);
        var blockedPairs = new HashSet<string> { "BTCUSDT" };

        // Act
        var result = _validator.ValidatePreTrade(
            portfolio, pair, cost, quantity, price, orderConstraints, blockedPairs: blockedPairs);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThan(1);
        result.Errors.Should().Contain(e => e.Code == ValidationErrorCode.PairBlocked);
        result.Errors.Should().Contain(e => e.Code == ValidationErrorCode.ExceedsMaximumOrderValue);
        result.Errors.Should().Contain(e => e.Code == ValidationErrorCode.InsufficientFunds);
    }

    #endregion

    #region Constraint Classes

    [Fact]
    public void DCAConstraints_Default_HasReasonableDefaults()
    {
        // Act
        var constraints = DCAConstraints.Default;

        // Assert
        constraints.MaxDCALevels.Should().Be(5);
        constraints.MinPriceDropPercent.Should().Be(0);
        constraints.MinMarginDropPercent.Should().Be(0);
        constraints.MinTimeBetweenDCA.Should().Be(TimeSpan.Zero);
        constraints.MaxTotalPositionCost.Should().Be(0);
    }

    [Fact]
    public void DCAConstraints_Create_SetsAllValues()
    {
        // Act
        var constraints = DCAConstraints.Create(
            maxDCALevels: 3,
            minPriceDropPercent: 5m,
            minMarginDropPercent: 3m,
            minTimeBetweenDCA: TimeSpan.FromMinutes(30),
            maxTotalPositionCost: 5000m);

        // Assert
        constraints.MaxDCALevels.Should().Be(3);
        constraints.MinPriceDropPercent.Should().Be(5m);
        constraints.MinMarginDropPercent.Should().Be(3m);
        constraints.MinTimeBetweenDCA.Should().Be(TimeSpan.FromMinutes(30));
        constraints.MaxTotalPositionCost.Should().Be(5000m);
    }

    [Fact]
    public void ClosePositionConstraints_Default_HasReasonableDefaults()
    {
        // Act
        var constraints = ClosePositionConstraints.Default;

        // Assert
        constraints.MinProfitPercent.Should().Be(0);
        constraints.EnforceMinProfit.Should().BeFalse();
        constraints.MaxLossPercent.Should().Be(0);
        constraints.EnforceMaxLoss.Should().BeFalse();
        constraints.MinHoldingPeriod.Should().Be(TimeSpan.Zero);
        constraints.EnforceMinHoldingPeriod.Should().BeFalse();
    }

    [Fact]
    public void OrderConstraints_Create_SetsAllValues()
    {
        // Act
        var constraints = OrderConstraints.Create(
            minOrderValue: 10m,
            maxOrderValue: 10000m,
            minQuantity: 0.001m);

        // Assert
        constraints.MinOrderValue.Should().Be(10m);
        constraints.MaxOrderValue.Should().Be(10000m);
        constraints.MinQuantity.Should().Be(0.001m);
    }

    #endregion
}
