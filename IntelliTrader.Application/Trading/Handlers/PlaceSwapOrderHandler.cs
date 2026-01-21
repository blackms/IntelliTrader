using IntelliTrader.Application.Common;
using IntelliTrader.Application.Trading.Commands;

namespace IntelliTrader.Application.Trading.Handlers;

/// <summary>
/// Handler for PlaceSwapOrderCommand that delegates to the legacy ITradingService.
/// A swap operation sells one trading pair and buys another with the proceeds.
/// </summary>
public sealed class PlaceSwapOrderHandler : ICommandHandler<PlaceSwapOrderCommand, PlaceSwapOrderResult>
{
    private readonly ILegacyTradingServiceAdapter _tradingServiceAdapter;

    public PlaceSwapOrderHandler(ILegacyTradingServiceAdapter tradingServiceAdapter)
    {
        _tradingServiceAdapter = tradingServiceAdapter ?? throw new ArgumentNullException(nameof(tradingServiceAdapter));
    }

    public async Task<Result<PlaceSwapOrderResult>> HandleAsync(
        PlaceSwapOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // 1. Validate the command
        var validationResult = ValidateCommand(command);
        if (validationResult.IsFailure)
        {
            return Result<PlaceSwapOrderResult>.Failure(validationResult.Error);
        }

        // 2. Check if we can swap (using legacy service logic)
        var canSwapResult = await _tradingServiceAdapter.CanSwapAsync(
            command.OldPair,
            command.NewPair,
            cancellationToken);

        if (canSwapResult.IsFailure)
        {
            return Result<PlaceSwapOrderResult>.Failure(canSwapResult.Error);
        }

        if (!canSwapResult.Value.CanExecute)
        {
            return Result<PlaceSwapOrderResult>.Failure(
                Error.Validation(canSwapResult.Value.Reason ?? "Cannot place swap order"));
        }

        // 3. Execute the swap order through the legacy service
        var swapResult = await _tradingServiceAdapter.PlaceSwapOrderAsync(
            command.OldPair,
            command.NewPair,
            command.IsManualOrder,
            command.NewPairMetadata,
            cancellationToken);

        if (swapResult.IsFailure)
        {
            return Result<PlaceSwapOrderResult>.Failure(swapResult.Error);
        }

        return Result<PlaceSwapOrderResult>.Success(swapResult.Value);
    }

    private static Result ValidateCommand(PlaceSwapOrderCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.OldPair))
        {
            return Result.Failure(Error.Validation("Old trading pair is required"));
        }

        if (string.IsNullOrWhiteSpace(command.NewPair))
        {
            return Result.Failure(Error.Validation("New trading pair is required"));
        }

        if (string.Equals(command.OldPair, command.NewPair, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(Error.Validation("Cannot swap a pair with itself"));
        }

        return Result.Success();
    }
}
