using IntelliTrader.Application.Common;
using IntelliTrader.Application.Trading.Commands;

namespace IntelliTrader.Application.Trading.Handlers;

/// <summary>
/// Handler for PlaceBuyOrderCommand that delegates to the legacy ITradingService.
/// This provides a clean Application layer facade while leveraging existing functionality.
/// </summary>
/// <remarks>
/// This handler acts as an adapter/bridge between the modern Application layer pattern
/// and the legacy service-oriented architecture. It enables gradual migration by:
/// 1. Providing a clean command/result interface for new code
/// 2. Delegating actual execution to the proven legacy service
/// 3. Translating between the two paradigms
/// </remarks>
public sealed class PlaceBuyOrderHandler : ICommandHandler<PlaceBuyOrderCommand, PlaceBuyOrderResult>
{
    private readonly ILegacyTradingServiceAdapter _tradingServiceAdapter;

    public PlaceBuyOrderHandler(ILegacyTradingServiceAdapter tradingServiceAdapter)
    {
        _tradingServiceAdapter = tradingServiceAdapter ?? throw new ArgumentNullException(nameof(tradingServiceAdapter));
    }

    public async Task<Result<PlaceBuyOrderResult>> HandleAsync(
        PlaceBuyOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // 1. Validate the command
        var validationResult = ValidateCommand(command);
        if (validationResult.IsFailure)
        {
            return Result<PlaceBuyOrderResult>.Failure(validationResult.Error);
        }

        // 2. Check if we can buy (using legacy service logic)
        var canBuyResult = await _tradingServiceAdapter.CanBuyAsync(
            command.Pair,
            command.Amount,
            command.MaxCost,
            command.IgnoreExisting,
            cancellationToken);

        if (canBuyResult.IsFailure)
        {
            return Result<PlaceBuyOrderResult>.Failure(canBuyResult.Error);
        }

        if (!canBuyResult.Value.CanExecute)
        {
            return Result<PlaceBuyOrderResult>.Failure(
                Error.Validation(canBuyResult.Value.Reason ?? "Cannot place buy order"));
        }

        // 3. Execute the buy order through the legacy service
        var buyResult = await _tradingServiceAdapter.PlaceBuyOrderAsync(
            command.Pair,
            command.Amount,
            command.MaxCost,
            command.IgnoreExisting,
            command.IsManualOrder,
            command.Metadata,
            cancellationToken);

        if (buyResult.IsFailure)
        {
            return Result<PlaceBuyOrderResult>.Failure(buyResult.Error);
        }

        return Result<PlaceBuyOrderResult>.Success(buyResult.Value);
    }

    private static Result ValidateCommand(PlaceBuyOrderCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Pair))
        {
            return Result.Failure(Error.Validation("Trading pair is required"));
        }

        if (command.Amount.HasValue && command.Amount.Value <= 0)
        {
            return Result.Failure(Error.Validation("Amount must be greater than zero"));
        }

        if (command.MaxCost.HasValue && command.MaxCost.Value <= 0)
        {
            return Result.Failure(Error.Validation("MaxCost must be greater than zero"));
        }

        return Result.Success();
    }
}

/// <summary>
/// Adapter interface to bridge the Application layer to the legacy ITradingService.
/// This interface provides an async-first, Result-based API on top of the legacy service.
/// </summary>
/// <remarks>
/// The implementation of this interface will live in the Infrastructure layer and will
/// wrap the actual ITradingService from IntelliTrader.Core.
/// </remarks>
public interface ILegacyTradingServiceAdapter
{
    /// <summary>
    /// Checks if a buy order can be placed.
    /// </summary>
    Task<Result<OrderValidationResult>> CanBuyAsync(
        string pair,
        decimal? amount,
        decimal? maxCost,
        bool ignoreExisting,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Places a buy order through the legacy trading service.
    /// </summary>
    Task<Result<PlaceBuyOrderResult>> PlaceBuyOrderAsync(
        string pair,
        decimal? amount,
        decimal? maxCost,
        bool ignoreExisting,
        bool isManualOrder,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a sell order can be placed.
    /// </summary>
    Task<Result<OrderValidationResult>> CanSellAsync(
        string pair,
        decimal? amount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Places a sell order through the legacy trading service.
    /// </summary>
    Task<Result<PlaceSellOrderResult>> PlaceSellOrderAsync(
        string pair,
        decimal? amount,
        bool isManualOrder,
        string? swapPair,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a swap order can be placed.
    /// </summary>
    Task<Result<OrderValidationResult>> CanSwapAsync(
        string oldPair,
        string newPair,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Places a swap order through the legacy trading service.
    /// </summary>
    Task<Result<PlaceSwapOrderResult>> PlaceSwapOrderAsync(
        string oldPair,
        string newPair,
        bool isManualOrder,
        IReadOnlyDictionary<string, string>? newPairMetadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current trading pairs available in the market.
    /// </summary>
    Task<Result<IReadOnlyList<string>>> GetTradingPairsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current portfolio status including positions and balances.
    /// </summary>
    Task<Result<LegacyPortfolioStatus>> GetPortfolioStatusAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of an order validation check.
/// </summary>
public sealed record OrderValidationResult
{
    /// <summary>
    /// Whether the order can be executed.
    /// </summary>
    public required bool CanExecute { get; init; }

    /// <summary>
    /// Reason why the order cannot be executed, if applicable.
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Represents the portfolio status from the legacy trading service.
/// </summary>
public sealed record LegacyPortfolioStatus
{
    /// <summary>
    /// Total balance in the account.
    /// </summary>
    public required decimal TotalBalance { get; init; }

    /// <summary>
    /// Available balance for new orders.
    /// </summary>
    public required decimal AvailableBalance { get; init; }

    /// <summary>
    /// Balance reserved in open positions.
    /// </summary>
    public required decimal ReservedBalance { get; init; }

    /// <summary>
    /// Number of active trading positions.
    /// </summary>
    public required int ActivePositionCount { get; init; }

    /// <summary>
    /// Whether trading is currently suspended.
    /// </summary>
    public required bool IsTradingSuspended { get; init; }

    /// <summary>
    /// List of active trading pairs with positions.
    /// </summary>
    public IReadOnlyList<LegacyTradingPairStatus> Positions { get; init; } = Array.Empty<LegacyTradingPairStatus>();

    /// <summary>
    /// List of pairs with trailing buy orders.
    /// </summary>
    public IReadOnlyList<string> TrailingBuys { get; init; } = Array.Empty<string>();

    /// <summary>
    /// List of pairs with trailing sell orders.
    /// </summary>
    public IReadOnlyList<string> TrailingSells { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Status of a single trading pair position from the legacy service.
/// </summary>
public sealed record LegacyTradingPairStatus
{
    /// <summary>
    /// The trading pair (e.g., "BTCUSDT").
    /// </summary>
    public required string Pair { get; init; }

    /// <summary>
    /// Amount held.
    /// </summary>
    public required decimal Amount { get; init; }

    /// <summary>
    /// Total cost of the position.
    /// </summary>
    public required decimal TotalCost { get; init; }

    /// <summary>
    /// Current value of the position.
    /// </summary>
    public required decimal CurrentValue { get; init; }

    /// <summary>
    /// Current profit/loss percentage.
    /// </summary>
    public required decimal CurrentMargin { get; init; }

    /// <summary>
    /// DCA level for the position.
    /// </summary>
    public required int DCALevel { get; init; }

    /// <summary>
    /// Average price of the position.
    /// </summary>
    public required decimal AveragePrice { get; init; }

    /// <summary>
    /// Current market price.
    /// </summary>
    public required decimal CurrentPrice { get; init; }
}
