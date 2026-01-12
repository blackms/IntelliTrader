using IntelliTrader.Application.Common;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Application.Trading.Queries;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Ports.Driving;

/// <summary>
/// Primary port for trading operations.
/// This is a driving (primary) port - external systems use it to interact with the application.
/// </summary>
public interface ITradingUseCase
{
    #region Commands

    /// <summary>
    /// Opens a new trading position.
    /// </summary>
    Task<Result<OpenPositionResult>> OpenPositionAsync(
        OpenPositionCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes an existing trading position.
    /// </summary>
    Task<Result<ClosePositionResult>> ClosePositionAsync(
        ClosePositionCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes a position by trading pair.
    /// </summary>
    Task<Result<ClosePositionResult>> ClosePositionByPairAsync(
        ClosePositionByPairCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a DCA (Dollar Cost Averaging) entry for an existing position.
    /// </summary>
    Task<Result<ExecuteDCAResult>> ExecuteDCAAsync(
        ExecuteDCACommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a DCA entry by trading pair.
    /// </summary>
    Task<Result<ExecuteDCAResult>> ExecuteDCAByPairAsync(
        ExecuteDCAByPairCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes all open positions.
    /// </summary>
    Task<Result<IReadOnlyList<ClosePositionResult>>> CloseAllPositionsAsync(
        CloseReason reason = CloseReason.Manual,
        CancellationToken cancellationToken = default);

    #endregion

    #region Queries

    /// <summary>
    /// Gets a position by ID or trading pair.
    /// </summary>
    Task<Result<PositionView>> GetPositionAsync(
        GetPositionQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active positions.
    /// </summary>
    Task<Result<IReadOnlyList<PositionView>>> GetActivePositionsAsync(
        GetActivePositionsQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets closed positions within a date range.
    /// </summary>
    Task<Result<IReadOnlyList<PositionView>>> GetClosedPositionsAsync(
        GetClosedPositionsQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a summary of all active positions.
    /// </summary>
    Task<Result<PositionsSummary>> GetPositionsSummaryAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current portfolio information.
    /// </summary>
    Task<Result<PortfolioView>> GetPortfolioAsync(
        GetPortfolioQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets detailed portfolio statistics.
    /// </summary>
    Task<Result<PortfolioStatistics>> GetPortfolioStatisticsAsync(
        GetPortfolioStatisticsQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets trading history.
    /// </summary>
    Task<Result<IReadOnlyList<TradeHistoryEntry>>> GetTradingHistoryAsync(
        GetTradingHistoryQuery query,
        CancellationToken cancellationToken = default);

    #endregion

    #region Validation

    /// <summary>
    /// Validates whether a new position can be opened.
    /// </summary>
    Task<Result<CanOpenPositionResult>> CanOpenPositionAsync(
        TradingPair pair,
        Money cost,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates whether a DCA entry can be executed.
    /// </summary>
    Task<Result<CanDCAResult>> CanExecuteDCAAsync(
        PositionId positionId,
        Money cost,
        CancellationToken cancellationToken = default);

    #endregion
}

/// <summary>
/// Result of checking if a position can be opened.
/// </summary>
public sealed record CanOpenPositionResult
{
    public required bool CanOpen { get; init; }
    public required TradingPair Pair { get; init; }
    public required Money RequestedCost { get; init; }
    public required Money AvailableBalance { get; init; }
    public required int CurrentPositionCount { get; init; }
    public required int MaxPositions { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Result of checking if DCA can be executed.
/// </summary>
public sealed record CanDCAResult
{
    public required bool CanDCA { get; init; }
    public required PositionId PositionId { get; init; }
    public required TradingPair Pair { get; init; }
    public required int CurrentDCALevel { get; init; }
    public required int MaxDCALevels { get; init; }
    public required Money RequestedCost { get; init; }
    public required Money AvailableBalance { get; init; }
    public required Margin CurrentMargin { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
}
