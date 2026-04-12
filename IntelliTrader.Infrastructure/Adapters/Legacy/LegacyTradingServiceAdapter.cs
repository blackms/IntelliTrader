using IntelliTrader.Application.Common;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Application.Trading.Handlers;
using IntelliTrader.Core;
using Microsoft.Extensions.Logging;

namespace IntelliTrader.Infrastructure.Adapters.Legacy;

/// <summary>
/// Adapter that bridges the Application layer to the legacy ITradingService.
/// This enables gradual migration by wrapping the legacy service with a modern interface.
/// </summary>
/// <remarks>
/// Migration strategy:
/// 1. Initially all operations delegate to the legacy service
/// 2. As the DDD domain matures, operations can be migrated one by one
/// 3. Eventually this adapter can be replaced with the new implementation
/// </remarks>
public sealed class LegacyTradingServiceAdapter : ILegacyTradingServiceAdapter
{
    private readonly ITradingService _tradingService;
    private readonly ILogger<LegacyTradingServiceAdapter> _logger;

    public LegacyTradingServiceAdapter(
        ITradingService tradingService,
        ILogger<LegacyTradingServiceAdapter> logger)
    {
        _tradingService = tradingService ?? throw new ArgumentNullException(nameof(tradingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<Result<OrderValidationResult>> CanBuyAsync(
        string pair,
        decimal? amount,
        decimal? maxCost,
        bool ignoreExisting,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = new BuyOptions(pair)
            {
                Amount = amount,
                MaxCost = maxCost,
                IgnoreExisting = ignoreExisting
            };

            var canBuy = _tradingService.CanBuy(options, out var message);

            return Task.FromResult(Result<OrderValidationResult>.Success(new OrderValidationResult
            {
                CanExecute = canBuy,
                Reason = canBuy ? null : message
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if can buy {Pair}", pair);
            return Task.FromResult(Result<OrderValidationResult>.Failure(
                Error.ExchangeError($"Failed to validate buy order: {ex.Message}")));
        }
    }

    public async Task<Result<PlaceBuyOrderResult>> PlaceBuyOrderAsync(
        string pair,
        decimal? amount,
        decimal? maxCost,
        bool ignoreExisting,
        bool isManualOrder,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = new BuyOptions(pair)
            {
                Amount = amount,
                MaxCost = maxCost,
                IgnoreExisting = ignoreExisting,
                ManualOrder = isManualOrder,
                Metadata = ConvertMetadata(metadata)
            };

            await _tradingService.BuyAsync(options, cancellationToken).ConfigureAwait(false);

            return Result<PlaceBuyOrderResult>.Success(new PlaceBuyOrderResult
            {
                Success = true,
                Pair = pair,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error placing buy order for {Pair}", pair);
            return Result<PlaceBuyOrderResult>.Failure(
                Error.ExchangeError($"Failed to place buy order: {ex.Message}"));
        }
    }

    public Task<Result<OrderValidationResult>> CanSellAsync(
        string pair,
        decimal? amount,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = new SellOptions(pair)
            {
                Amount = amount
            };

            var canSell = _tradingService.CanSell(options, out var message);

            return Task.FromResult(Result<OrderValidationResult>.Success(new OrderValidationResult
            {
                CanExecute = canSell,
                Reason = canSell ? null : message
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if can sell {Pair}", pair);
            return Task.FromResult(Result<OrderValidationResult>.Failure(
                Error.ExchangeError($"Failed to validate sell order: {ex.Message}")));
        }
    }

    public async Task<Result<PlaceSellOrderResult>> PlaceSellOrderAsync(
        string pair,
        decimal? amount,
        bool isManualOrder,
        string? swapPair,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = new SellOptions(pair)
            {
                Amount = amount,
                ManualOrder = isManualOrder,
                SwapPair = swapPair,
                Swap = !string.IsNullOrEmpty(swapPair)
            };

            await _tradingService.SellAsync(options, cancellationToken).ConfigureAwait(false);

            return Result<PlaceSellOrderResult>.Success(new PlaceSellOrderResult
            {
                Success = true,
                Pair = pair,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error placing sell order for {Pair}", pair);
            return Result<PlaceSellOrderResult>.Failure(
                Error.ExchangeError($"Failed to place sell order: {ex.Message}"));
        }
    }

    public Task<Result<OrderValidationResult>> CanSwapAsync(
        string oldPair,
        string newPair,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = new SwapOptions(oldPair, newPair, new OrderMetadata());

            var canSwap = _tradingService.CanSwap(options, out var message);

            return Task.FromResult(Result<OrderValidationResult>.Success(new OrderValidationResult
            {
                CanExecute = canSwap,
                Reason = canSwap ? null : message
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if can swap {OldPair} to {NewPair}", oldPair, newPair);
            return Task.FromResult(Result<OrderValidationResult>.Failure(
                Error.ExchangeError($"Failed to validate swap order: {ex.Message}")));
        }
    }

    public async Task<Result<PlaceSwapOrderResult>> PlaceSwapOrderAsync(
        string oldPair,
        string newPair,
        bool isManualOrder,
        IReadOnlyDictionary<string, string>? newPairMetadata,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = new SwapOptions(oldPair, newPair, ConvertMetadata(newPairMetadata))
            {
                ManualOrder = isManualOrder
            };

            await _tradingService.SwapAsync(options, cancellationToken).ConfigureAwait(false);

            return Result<PlaceSwapOrderResult>.Success(new PlaceSwapOrderResult
            {
                Success = true,
                OldPair = oldPair,
                NewPair = newPair,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error placing swap order from {OldPair} to {NewPair}", oldPair, newPair);
            return Result<PlaceSwapOrderResult>.Failure(
                Error.ExchangeError($"Failed to place swap order: {ex.Message}"));
        }
    }

    public async Task<Result<IReadOnlyList<string>>> GetTradingPairsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pairs = await _tradingService.GetMarketPairsAsync().ConfigureAwait(false);
            return Result<IReadOnlyList<string>>.Success(pairs.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting trading pairs");
            return Result<IReadOnlyList<string>>.Failure(
                Error.ExchangeError($"Failed to get trading pairs: {ex.Message}"));
        }
    }

    public Task<Result<LegacyPortfolioStatus>> GetPortfolioStatusAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var account = _tradingService.Account;
            var trailingBuys = _tradingService.GetTrailingBuys();
            var trailingSells = _tradingService.GetTrailingSells();

            var positions = account?.GetTradingPairs()?.Select(pair => new LegacyTradingPairStatus
            {
                Pair = pair.Pair,
                Amount = pair.TotalAmount,
                TotalCost = pair.AverageCostPaid * pair.TotalAmount,
                CurrentValue = pair.CurrentCost,
                CurrentMargin = pair.CurrentMargin,
                DCALevel = pair.DCALevel,
                AveragePrice = pair.AveragePricePaid,
                CurrentPrice = pair.CurrentPrice
            }).ToList() ?? new List<LegacyTradingPairStatus>();

            var status = new LegacyPortfolioStatus
            {
                TotalBalance = account?.GetBalance() ?? 0,
                AvailableBalance = account?.GetBalance() ?? 0,
                ReservedBalance = positions.Sum(p => p.TotalCost),
                ActivePositionCount = positions.Count,
                IsTradingSuspended = _tradingService.IsTradingSuspended,
                Positions = positions,
                TrailingBuys = trailingBuys,
                TrailingSells = trailingSells
            };

            return Task.FromResult(Result<LegacyPortfolioStatus>.Success(status));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting portfolio status");
            return Task.FromResult(Result<LegacyPortfolioStatus>.Failure(
                Error.ExchangeError($"Failed to get portfolio status: {ex.Message}")));
        }
    }

    private static OrderMetadata ConvertMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return new OrderMetadata();
        }

        var orderMetadata = new OrderMetadata();

        if (metadata.TryGetValue("SignalRule", out var signalRule))
        {
            orderMetadata.SignalRule = signalRule;
        }

        if (metadata.TryGetValue("TradingRules", out var tradingRulesStr))
        {
            orderMetadata.TradingRules = tradingRulesStr.Split(',').ToList();
        }

        if (metadata.TryGetValue("Signals", out var signalsStr))
        {
            orderMetadata.Signals = signalsStr.Split(',').ToList();
        }

        if (metadata.TryGetValue("SwapPair", out var swapPair))
        {
            orderMetadata.SwapPair = swapPair;
        }

        return orderMetadata;
    }
}
