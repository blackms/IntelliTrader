using IntelliTrader.Application.Common;
using IntelliTrader.Application.Trading.Commands;

namespace IntelliTrader.Application.Trading.Handlers;

/// <summary>
/// Handler for PlaceSellOrderCommand that delegates to the legacy ITradingService.
/// This provides a clean Application layer facade while leveraging existing functionality.
/// </summary>
public sealed class PlaceSellOrderHandler : ICommandHandler<PlaceSellOrderCommand, PlaceSellOrderResult>
{
    private readonly ILegacyTradingServiceAdapter _tradingServiceAdapter;

    public PlaceSellOrderHandler(ILegacyTradingServiceAdapter tradingServiceAdapter)
    {
        _tradingServiceAdapter = tradingServiceAdapter ?? throw new ArgumentNullException(nameof(tradingServiceAdapter));
    }

    public async Task<Result<PlaceSellOrderResult>> HandleAsync(
        PlaceSellOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // 1. Validate the command
        var validationResult = ValidateCommand(command);
        if (validationResult.IsFailure)
        {
            return Result<PlaceSellOrderResult>.Failure(validationResult.Error);
        }

        // 2. Check if we can sell (using legacy service logic)
        var canSellResult = await _tradingServiceAdapter.CanSellAsync(
            command.Pair,
            command.Amount,
            cancellationToken);

        if (canSellResult.IsFailure)
        {
            return Result<PlaceSellOrderResult>.Failure(canSellResult.Error);
        }

        if (!canSellResult.Value.CanExecute)
        {
            return Result<PlaceSellOrderResult>.Failure(
                Error.Validation(canSellResult.Value.Reason ?? "Cannot place sell order"));
        }

        // 3. Execute the sell order through the legacy service
        var sellResult = await _tradingServiceAdapter.PlaceSellOrderAsync(
            command.Pair,
            command.Amount,
            command.IsManualOrder,
            command.SwapPair,
            command.Metadata,
            cancellationToken);

        if (sellResult.IsFailure)
        {
            return Result<PlaceSellOrderResult>.Failure(sellResult.Error);
        }

        return Result<PlaceSellOrderResult>.Success(sellResult.Value);
    }

    private static Result ValidateCommand(PlaceSellOrderCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Pair))
        {
            return Result.Failure(Error.Validation("Trading pair is required"));
        }

        if (command.Amount.HasValue && command.Amount.Value <= 0)
        {
            return Result.Failure(Error.Validation("Amount must be greater than zero"));
        }

        return Result.Success();
    }
}
