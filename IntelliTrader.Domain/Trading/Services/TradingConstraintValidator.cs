using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Trading.Services;

/// <summary>
/// Domain service for validating trading constraints before executing trades.
/// Provides detailed validation results explaining why trades cannot be executed.
/// </summary>
public sealed class TradingConstraintValidator
{
    /// <summary>
    /// Validates whether a new position can be opened.
    /// </summary>
    public OpenPositionValidationResult ValidateOpenPosition(
        Portfolio portfolio,
        TradingPair pair,
        Money cost)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(cost);

        var errors = new List<ValidationError>();
        var currencyMatches = cost.Currency == portfolio.Market;

        // Check currency match
        if (!currencyMatches)
        {
            errors.Add(new ValidationError(
                ValidationErrorCode.CurrencyMismatch,
                $"Cost currency '{cost.Currency}' does not match portfolio market '{portfolio.Market}'"));
        }

        // Check if position already exists
        if (portfolio.HasPositionFor(pair))
        {
            errors.Add(new ValidationError(
                ValidationErrorCode.PositionAlreadyExists,
                $"Position already exists for {pair}"));
        }

        // Check max positions
        if (portfolio.IsAtMaxPositions)
        {
            errors.Add(new ValidationError(
                ValidationErrorCode.MaxPositionsReached,
                $"Maximum positions ({portfolio.MaxPositions}) already reached. Current: {portfolio.ActivePositionCount}"));
        }

        // Only check money-based constraints if currency matches
        if (currencyMatches)
        {
            // Check minimum position cost
            if (cost < portfolio.MinPositionCost)
            {
                errors.Add(new ValidationError(
                    ValidationErrorCode.BelowMinimumPositionCost,
                    $"Position cost {cost} is below minimum {portfolio.MinPositionCost}"));
            }

            // Check available funds
            if (!portfolio.CanAfford(cost))
            {
                errors.Add(new ValidationError(
                    ValidationErrorCode.InsufficientFunds,
                    $"Insufficient funds. Available: {portfolio.Balance.Available}, Required: {cost}"));
            }
        }

        return new OpenPositionValidationResult(
            IsValid: errors.Count == 0,
            Errors: errors,
            Pair: pair,
            RequestedCost: cost,
            AvailableBalance: portfolio.Balance.Available,
            CurrentPositionCount: portfolio.ActivePositionCount,
            MaxPositions: portfolio.MaxPositions);
    }

    /// <summary>
    /// Validates whether a DCA (Dollar Cost Averaging) entry can be added to an existing position.
    /// </summary>
    public DCAValidationResult ValidateDCA(
        Portfolio portfolio,
        Position position,
        Money additionalCost,
        Price currentPrice,
        DCAConstraints constraints)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(additionalCost);
        ArgumentNullException.ThrowIfNull(currentPrice);
        ArgumentNullException.ThrowIfNull(constraints);

        var errors = new List<ValidationError>();

        // Check currency match
        if (additionalCost.Currency != portfolio.Market)
        {
            errors.Add(new ValidationError(
                ValidationErrorCode.CurrencyMismatch,
                $"Cost currency '{additionalCost.Currency}' does not match portfolio market '{portfolio.Market}'"));
        }

        // Check if position exists in portfolio
        if (!portfolio.HasPositionFor(position.Pair))
        {
            errors.Add(new ValidationError(
                ValidationErrorCode.PositionNotFound,
                $"Position for {position.Pair} not found in portfolio"));
        }

        // Check max DCA levels
        if (position.DCALevel >= constraints.MaxDCALevels)
        {
            errors.Add(new ValidationError(
                ValidationErrorCode.MaxDCALevelsReached,
                $"Maximum DCA levels ({constraints.MaxDCALevels}) reached. Current: {position.DCALevel}"));
        }

        // Check minimum price drop
        if (constraints.MinPriceDropPercent > 0)
        {
            var priceDrop = CalculatePriceDropPercent(position.AveragePrice, currentPrice);
            if (priceDrop < constraints.MinPriceDropPercent)
            {
                errors.Add(new ValidationError(
                    ValidationErrorCode.InsufficientPriceDrop,
                    $"Price drop {priceDrop:F2}% is below minimum {constraints.MinPriceDropPercent:F2}% required for DCA"));
            }
        }

        // Check minimum margin drop (position must be in loss)
        if (constraints.MinMarginDropPercent > 0)
        {
            var margin = position.CalculateMargin(currentPrice);
            if (margin.Percentage > -constraints.MinMarginDropPercent)
            {
                errors.Add(new ValidationError(
                    ValidationErrorCode.InsufficientMarginDrop,
                    $"Current margin {margin.Percentage:F2}% is above -{constraints.MinMarginDropPercent:F2}% threshold"));
            }
        }

        // Check cooldown period
        if (constraints.MinTimeBetweenDCA > TimeSpan.Zero)
        {
            var lastEntry = position.Entries.LastOrDefault();
            if (lastEntry != null)
            {
                var timeSinceLastDCA = DateTimeOffset.UtcNow - lastEntry.Timestamp;
                if (timeSinceLastDCA < constraints.MinTimeBetweenDCA)
                {
                    errors.Add(new ValidationError(
                        ValidationErrorCode.DCACooldownActive,
                        $"DCA cooldown active. Time since last DCA: {timeSinceLastDCA.TotalMinutes:F1} minutes. Required: {constraints.MinTimeBetweenDCA.TotalMinutes:F1} minutes"));
                }
            }
        }

        // Check available funds
        if (!portfolio.CanAfford(additionalCost))
        {
            errors.Add(new ValidationError(
                ValidationErrorCode.InsufficientFunds,
                $"Insufficient funds for DCA. Available: {portfolio.Balance.Available}, Required: {additionalCost}"));
        }

        // Check max total cost per position
        if (constraints.MaxTotalPositionCost > 0)
        {
            var newTotalCost = position.TotalCost + additionalCost;
            if (newTotalCost.Amount > constraints.MaxTotalPositionCost)
            {
                errors.Add(new ValidationError(
                    ValidationErrorCode.MaxPositionCostExceeded,
                    $"DCA would exceed max position cost. Current: {position.TotalCost}, Additional: {additionalCost}, Max: {constraints.MaxTotalPositionCost}"));
            }
        }

        var currentPriceDrop = CalculatePriceDropPercent(position.AveragePrice, currentPrice);
        var currentMargin = position.CalculateMargin(currentPrice);

        return new DCAValidationResult(
            IsValid: errors.Count == 0,
            Errors: errors,
            Pair: position.Pair,
            CurrentDCALevel: position.DCALevel,
            MaxDCALevels: constraints.MaxDCALevels,
            CurrentPriceDropPercent: currentPriceDrop,
            RequiredPriceDropPercent: constraints.MinPriceDropPercent,
            CurrentMargin: currentMargin,
            RequestedCost: additionalCost,
            AvailableBalance: portfolio.Balance.Available);
    }

    /// <summary>
    /// Validates whether a position can be closed.
    /// </summary>
    public ClosePositionValidationResult ValidateClosePosition(
        Portfolio portfolio,
        Position position,
        Price currentPrice,
        ClosePositionConstraints? constraints = null)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(currentPrice);

        constraints ??= ClosePositionConstraints.Default;
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        // Check if position exists in portfolio
        if (!portfolio.HasPositionFor(position.Pair))
        {
            errors.Add(new ValidationError(
                ValidationErrorCode.PositionNotFound,
                $"Position for {position.Pair} not found in portfolio"));
        }

        var margin = position.CalculateMargin(currentPrice);

        // Check minimum profit (if configured)
        if (constraints.MinProfitPercent > 0 && margin.Percentage < constraints.MinProfitPercent)
        {
            if (constraints.EnforceMinProfit)
            {
                errors.Add(new ValidationError(
                    ValidationErrorCode.BelowMinimumProfit,
                    $"Current margin {margin.Percentage:F2}% is below minimum profit target {constraints.MinProfitPercent:F2}%"));
            }
            else
            {
                warnings.Add(new ValidationWarning(
                    ValidationWarningCode.BelowTargetProfit,
                    $"Closing below target profit. Current: {margin.Percentage:F2}%, Target: {constraints.MinProfitPercent:F2}%"));
            }
        }

        // Check stop-loss (if closing at a loss)
        if (margin.IsLoss && constraints.MaxLossPercent > 0)
        {
            var lossPercent = Math.Abs(margin.Percentage);
            if (lossPercent > constraints.MaxLossPercent)
            {
                warnings.Add(new ValidationWarning(
                    ValidationWarningCode.ExceedsMaxLoss,
                    $"Loss {lossPercent:F2}% exceeds configured max loss {constraints.MaxLossPercent:F2}%"));
            }
        }

        // Check holding period
        if (constraints.MinHoldingPeriod > TimeSpan.Zero)
        {
            var holdingPeriod = DateTimeOffset.UtcNow - position.OpenedAt;
            if (holdingPeriod < constraints.MinHoldingPeriod)
            {
                if (constraints.EnforceMinHoldingPeriod)
                {
                    errors.Add(new ValidationError(
                        ValidationErrorCode.MinHoldingPeriodNotMet,
                        $"Position held for {holdingPeriod.TotalHours:F1} hours. Minimum: {constraints.MinHoldingPeriod.TotalHours:F1} hours"));
                }
                else
                {
                    warnings.Add(new ValidationWarning(
                        ValidationWarningCode.BelowMinHoldingPeriod,
                        $"Closing before minimum holding period. Held: {holdingPeriod.TotalHours:F1} hours, Minimum: {constraints.MinHoldingPeriod.TotalHours:F1} hours"));
                }
            }
        }

        var pnl = position.CalculateUnrealizedPnL(currentPrice);

        return new ClosePositionValidationResult(
            IsValid: errors.Count == 0,
            Errors: errors,
            Warnings: warnings,
            Pair: position.Pair,
            CurrentMargin: margin,
            UnrealizedPnL: pnl,
            HoldingPeriod: DateTimeOffset.UtcNow - position.OpenedAt);
    }

    /// <summary>
    /// Validates order parameters.
    /// </summary>
    public OrderValidationResult ValidateOrder(
        Money cost,
        Quantity quantity,
        Price price,
        OrderConstraints constraints)
    {
        ArgumentNullException.ThrowIfNull(cost);
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(constraints);

        var errors = new List<ValidationError>();

        // Check minimum order value
        if (cost.Amount < constraints.MinOrderValue)
        {
            errors.Add(new ValidationError(
                ValidationErrorCode.BelowMinimumOrderValue,
                $"Order value {cost} is below minimum {constraints.MinOrderValue}"));
        }

        // Check maximum order value
        if (constraints.MaxOrderValue > 0 && cost.Amount > constraints.MaxOrderValue)
        {
            errors.Add(new ValidationError(
                ValidationErrorCode.ExceedsMaximumOrderValue,
                $"Order value {cost} exceeds maximum {constraints.MaxOrderValue}"));
        }

        // Check minimum quantity
        if (quantity.Value < constraints.MinQuantity)
        {
            errors.Add(new ValidationError(
                ValidationErrorCode.BelowMinimumQuantity,
                $"Quantity {quantity} is below minimum {constraints.MinQuantity}"));
        }

        // Check price validity
        if (price.Value <= 0)
        {
            errors.Add(new ValidationError(
                ValidationErrorCode.InvalidPrice,
                "Price must be greater than zero"));
        }

        // Verify cost = price * quantity (within tolerance)
        var expectedCost = price.Value * quantity.Value;
        var tolerance = expectedCost * 0.001m; // 0.1% tolerance for rounding
        if (Math.Abs(cost.Amount - expectedCost) > tolerance)
        {
            errors.Add(new ValidationError(
                ValidationErrorCode.CostQuantityMismatch,
                $"Cost {cost.Amount} does not match price * quantity ({expectedCost:F8})"));
        }

        return new OrderValidationResult(
            IsValid: errors.Count == 0,
            Errors: errors,
            Cost: cost,
            Quantity: quantity,
            Price: price);
    }

    /// <summary>
    /// Validates a trading pair against allowed pairs.
    /// </summary>
    public TradingPairValidationResult ValidateTradingPair(
        TradingPair pair,
        string requiredMarket,
        IReadOnlySet<string>? allowedPairs = null,
        IReadOnlySet<string>? blockedPairs = null)
    {
        ArgumentNullException.ThrowIfNull(pair);

        var errors = new List<ValidationError>();

        // Check market match
        if (!string.IsNullOrEmpty(requiredMarket) && !pair.IsInMarket(requiredMarket))
        {
            errors.Add(new ValidationError(
                ValidationErrorCode.InvalidMarket,
                $"Trading pair {pair} is not in required market '{requiredMarket}'"));
        }

        // Check if pair is blocked
        if (blockedPairs != null && blockedPairs.Contains(pair.Symbol))
        {
            errors.Add(new ValidationError(
                ValidationErrorCode.PairBlocked,
                $"Trading pair {pair} is blocked"));
        }

        // Check if pair is in allowed list (if list is provided)
        if (allowedPairs != null && allowedPairs.Count > 0 && !allowedPairs.Contains(pair.Symbol))
        {
            errors.Add(new ValidationError(
                ValidationErrorCode.PairNotAllowed,
                $"Trading pair {pair} is not in allowed pairs list"));
        }

        return new TradingPairValidationResult(
            IsValid: errors.Count == 0,
            Errors: errors,
            Pair: pair);
    }

    /// <summary>
    /// Performs comprehensive pre-trade validation.
    /// </summary>
    public PreTradeValidationResult ValidatePreTrade(
        Portfolio portfolio,
        TradingPair pair,
        Money cost,
        Quantity quantity,
        Price price,
        OrderConstraints orderConstraints,
        IReadOnlySet<string>? allowedPairs = null,
        IReadOnlySet<string>? blockedPairs = null)
    {
        var pairValidation = ValidateTradingPair(pair, portfolio.Market, allowedPairs, blockedPairs);
        var orderValidation = ValidateOrder(cost, quantity, price, orderConstraints);
        var positionValidation = ValidateOpenPosition(portfolio, pair, cost);

        var allErrors = pairValidation.Errors
            .Concat(orderValidation.Errors)
            .Concat(positionValidation.Errors)
            .ToList();

        return new PreTradeValidationResult(
            IsValid: allErrors.Count == 0,
            Errors: allErrors,
            PairValidation: pairValidation,
            OrderValidation: orderValidation,
            PositionValidation: positionValidation);
    }

    private static decimal CalculatePriceDropPercent(Price entryPrice, Price currentPrice)
    {
        if (entryPrice.Value == 0) return 0;
        return ((entryPrice.Value - currentPrice.Value) / entryPrice.Value) * 100m;
    }
}

#region Validation Results

/// <summary>
/// Result of open position validation.
/// </summary>
public sealed record OpenPositionValidationResult(
    bool IsValid,
    IReadOnlyList<ValidationError> Errors,
    TradingPair Pair,
    Money RequestedCost,
    Money AvailableBalance,
    int CurrentPositionCount,
    int MaxPositions)
{
    public bool HasSufficientFunds => !Errors.Any(e => e.Code == ValidationErrorCode.InsufficientFunds);
    public bool HasPositionSlot => !Errors.Any(e => e.Code == ValidationErrorCode.MaxPositionsReached);
    public bool IsNewPair => !Errors.Any(e => e.Code == ValidationErrorCode.PositionAlreadyExists);
}

/// <summary>
/// Result of DCA validation.
/// </summary>
public sealed record DCAValidationResult(
    bool IsValid,
    IReadOnlyList<ValidationError> Errors,
    TradingPair Pair,
    int CurrentDCALevel,
    int MaxDCALevels,
    decimal CurrentPriceDropPercent,
    decimal RequiredPriceDropPercent,
    Margin CurrentMargin,
    Money RequestedCost,
    Money AvailableBalance)
{
    public bool CanDCA => CurrentDCALevel < MaxDCALevels;
    public bool HasSufficientPriceDrop => CurrentPriceDropPercent >= RequiredPriceDropPercent;
    public bool HasSufficientFunds => !Errors.Any(e => e.Code == ValidationErrorCode.InsufficientFunds);
}

/// <summary>
/// Result of close position validation.
/// </summary>
public sealed record ClosePositionValidationResult(
    bool IsValid,
    IReadOnlyList<ValidationError> Errors,
    IReadOnlyList<ValidationWarning> Warnings,
    TradingPair Pair,
    Margin CurrentMargin,
    Money UnrealizedPnL,
    TimeSpan HoldingPeriod)
{
    public bool IsProfitable => CurrentMargin.IsProfit;
    public bool HasWarnings => Warnings.Count > 0;
}

/// <summary>
/// Result of order validation.
/// </summary>
public sealed record OrderValidationResult(
    bool IsValid,
    IReadOnlyList<ValidationError> Errors,
    Money Cost,
    Quantity Quantity,
    Price Price);

/// <summary>
/// Result of trading pair validation.
/// </summary>
public sealed record TradingPairValidationResult(
    bool IsValid,
    IReadOnlyList<ValidationError> Errors,
    TradingPair Pair);

/// <summary>
/// Result of comprehensive pre-trade validation.
/// </summary>
public sealed record PreTradeValidationResult(
    bool IsValid,
    IReadOnlyList<ValidationError> Errors,
    TradingPairValidationResult PairValidation,
    OrderValidationResult OrderValidation,
    OpenPositionValidationResult PositionValidation);

#endregion

#region Validation Errors and Warnings

/// <summary>
/// Represents a validation error.
/// </summary>
public sealed record ValidationError(ValidationErrorCode Code, string Message);

/// <summary>
/// Represents a validation warning (non-blocking).
/// </summary>
public sealed record ValidationWarning(ValidationWarningCode Code, string Message);

/// <summary>
/// Error codes for validation failures.
/// </summary>
public enum ValidationErrorCode
{
    // Position errors
    PositionAlreadyExists,
    PositionNotFound,
    MaxPositionsReached,

    // Balance errors
    InsufficientFunds,
    BelowMinimumPositionCost,
    MaxPositionCostExceeded,

    // DCA errors
    MaxDCALevelsReached,
    InsufficientPriceDrop,
    InsufficientMarginDrop,
    DCACooldownActive,

    // Close position errors
    BelowMinimumProfit,
    MinHoldingPeriodNotMet,

    // Order errors
    BelowMinimumOrderValue,
    ExceedsMaximumOrderValue,
    BelowMinimumQuantity,
    InvalidPrice,
    CostQuantityMismatch,

    // Pair errors
    CurrencyMismatch,
    InvalidMarket,
    PairBlocked,
    PairNotAllowed
}

/// <summary>
/// Warning codes for non-blocking validation issues.
/// </summary>
public enum ValidationWarningCode
{
    BelowTargetProfit,
    ExceedsMaxLoss,
    BelowMinHoldingPeriod
}

#endregion

#region Constraint Classes

/// <summary>
/// Constraints for DCA operations.
/// </summary>
public sealed record DCAConstraints
{
    public int MaxDCALevels { get; init; } = 5;
    public decimal MinPriceDropPercent { get; init; } = 0m;
    public decimal MinMarginDropPercent { get; init; } = 0m;
    public TimeSpan MinTimeBetweenDCA { get; init; } = TimeSpan.Zero;
    public decimal MaxTotalPositionCost { get; init; } = 0m;

    public static DCAConstraints Default => new();

    public static DCAConstraints Create(
        int maxDCALevels = 5,
        decimal minPriceDropPercent = 0m,
        decimal minMarginDropPercent = 0m,
        TimeSpan? minTimeBetweenDCA = null,
        decimal maxTotalPositionCost = 0m)
    {
        return new DCAConstraints
        {
            MaxDCALevels = maxDCALevels,
            MinPriceDropPercent = minPriceDropPercent,
            MinMarginDropPercent = minMarginDropPercent,
            MinTimeBetweenDCA = minTimeBetweenDCA ?? TimeSpan.Zero,
            MaxTotalPositionCost = maxTotalPositionCost
        };
    }
}

/// <summary>
/// Constraints for closing positions.
/// </summary>
public sealed record ClosePositionConstraints
{
    public decimal MinProfitPercent { get; init; } = 0m;
    public bool EnforceMinProfit { get; init; } = false;
    public decimal MaxLossPercent { get; init; } = 0m;
    public bool EnforceMaxLoss { get; init; } = false;
    public TimeSpan MinHoldingPeriod { get; init; } = TimeSpan.Zero;
    public bool EnforceMinHoldingPeriod { get; init; } = false;

    public static ClosePositionConstraints Default => new();

    public static ClosePositionConstraints Create(
        decimal minProfitPercent = 0m,
        bool enforceMinProfit = false,
        decimal maxLossPercent = 0m,
        bool enforceMaxLoss = false,
        TimeSpan? minHoldingPeriod = null,
        bool enforceMinHoldingPeriod = false)
    {
        return new ClosePositionConstraints
        {
            MinProfitPercent = minProfitPercent,
            EnforceMinProfit = enforceMinProfit,
            MaxLossPercent = maxLossPercent,
            EnforceMaxLoss = enforceMaxLoss,
            MinHoldingPeriod = minHoldingPeriod ?? TimeSpan.Zero,
            EnforceMinHoldingPeriod = enforceMinHoldingPeriod
        };
    }
}

/// <summary>
/// Constraints for order validation.
/// </summary>
public sealed record OrderConstraints
{
    public decimal MinOrderValue { get; init; } = 0m;
    public decimal MaxOrderValue { get; init; } = 0m;
    public decimal MinQuantity { get; init; } = 0m;

    public static OrderConstraints Default => new();

    public static OrderConstraints Create(
        decimal minOrderValue = 0m,
        decimal maxOrderValue = 0m,
        decimal minQuantity = 0m)
    {
        return new OrderConstraints
        {
            MinOrderValue = minOrderValue,
            MaxOrderValue = maxOrderValue,
            MinQuantity = minQuantity
        };
    }
}

#endregion
