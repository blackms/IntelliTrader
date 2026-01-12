using System.Reflection;
using System.Text.Json;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;
using IntelliTrader.Infrastructure.Migration.LegacyModels;

namespace IntelliTrader.Infrastructure.Migration;

/// <summary>
/// Migrates legacy account data (exchange-account.json, virtual-account.json) to the new domain format.
/// </summary>
public sealed class LegacyAccountMigrator
{
    private readonly IPositionRepository _positionRepository;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Creates a new LegacyAccountMigrator.
    /// </summary>
    /// <param name="positionRepository">The repository to save migrated positions.</param>
    public LegacyAccountMigrator(IPositionRepository positionRepository)
    {
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Migrates a legacy account file to the new domain format.
    /// </summary>
    /// <param name="legacyFilePath">Path to the legacy account JSON file.</param>
    /// <param name="market">The market/quote currency (e.g., "USDT").</param>
    /// <param name="portfolioId">The portfolio ID to associate migrated positions with.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Migration result containing statistics and any errors.</returns>
    public async Task<MigrationResult> MigrateAsync(
        string legacyFilePath,
        string market,
        PortfolioId portfolioId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(legacyFilePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(legacyFilePath));

        if (!File.Exists(legacyFilePath))
            return MigrationResult.FileNotFound(legacyFilePath);

        var result = new MigrationResult { SourceFile = legacyFilePath };

        try
        {
            // Load legacy data
            var json = await File.ReadAllTextAsync(legacyFilePath, cancellationToken);
            var legacyData = JsonSerializer.Deserialize<LegacyTradingAccountData>(json, _jsonOptions);

            if (legacyData == null)
            {
                result.Errors.Add("Failed to deserialize legacy account data");
                return result;
            }

            result.LegacyBalance = legacyData.Balance;
            result.TotalLegacyPositions = legacyData.TradingPairs.Count;

            // Convert each trading pair to a Position
            var positions = new List<Position>();

            foreach (var kvp in legacyData.TradingPairs)
            {
                var pairSymbol = kvp.Key;
                var legacyPair = kvp.Value;

                try
                {
                    var position = ConvertToPosition(legacyPair, market, portfolioId);
                    if (position != null)
                    {
                        positions.Add(position);
                        result.MigratedPositions++;
                    }
                    else
                    {
                        result.SkippedPositions++;
                        result.Warnings.Add($"Skipped {pairSymbol}: Empty or invalid position");
                    }
                }
                catch (Exception ex)
                {
                    result.FailedPositions++;
                    result.Errors.Add($"Failed to migrate {pairSymbol}: {ex.Message}");
                }
            }

            // Save all migrated positions
            if (positions.Count > 0)
            {
                await _positionRepository.SaveManyAsync(positions, cancellationToken);
            }

            result.Success = result.FailedPositions == 0;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Migration failed: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Converts a legacy TradingPair to a Position aggregate.
    /// </summary>
    private Position? ConvertToPosition(LegacyTradingPair legacyPair, string market, PortfolioId portfolioId)
    {
        if (legacyPair.OrderIds.Count == 0 || legacyPair.OrderDates.Count == 0)
            return null;

        if (legacyPair.TotalAmount <= 0)
            return null;

        var pairSymbol = legacyPair.Pair;
        var tradingPair = TradingPair.Create(pairSymbol, market);

        // Calculate per-entry values
        // Since we only have totals, we'll distribute evenly among entries
        var entryCount = legacyPair.OrderIds.Count;
        var quantityPerEntry = legacyPair.TotalAmount / entryCount;
        var feesPerEntry = legacyPair.FeesMarketCurrency / entryCount;

        // Use average price for all entries (marked as migrated)
        var price = Price.Create(legacyPair.AveragePricePaid);

        // Create first entry
        var firstOrderId = OrderId.From(legacyPair.OrderIds[0]);
        var firstTimestamp = legacyPair.OrderDates[0];
        var fees = Money.Create(feesPerEntry, market);
        var quantity = Quantity.Create(quantityPerEntry);

        // Create the position with first entry
        var position = Position.Open(
            tradingPair,
            firstOrderId,
            price,
            quantity,
            fees,
            legacyPair.Metadata?.SignalRule,
            firstTimestamp);

        // Set the correct timestamps
        SetPrivateProperty(position, "OpenedAt", legacyPair.OrderDates.Min());
        SetPrivateProperty(position, "LastBuyAt", legacyPair.OrderDates.Max());

        // Replace entries with migrated entries (marked as migrated)
        var entriesField = position.GetType().GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance);
        if (entriesField != null)
        {
            var entries = (List<PositionEntry>)entriesField.GetValue(position)!;
            entries.Clear();

            for (int i = 0; i < entryCount; i++)
            {
                var orderId = OrderId.From(legacyPair.OrderIds[i]);
                var timestamp = legacyPair.OrderDates[i];

                // Create entry marked as migrated
                var entry = PositionEntry.Create(
                    orderId,
                    price,
                    Quantity.Create(quantityPerEntry),
                    Money.Create(feesPerEntry, market),
                    timestamp,
                    isMigrated: true);

                entries.Add(entry);
            }
        }

        // Clear domain events since we're migrating, not creating new
        position.ClearDomainEvents();

        return position;
    }

    private static void SetPrivateProperty(object obj, string propertyName, object? value)
    {
        var type = obj.GetType();
        while (type != null)
        {
            var backingField = type.GetField($"<{propertyName}>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (backingField != null)
            {
                backingField.SetValue(obj, value);
                return;
            }

            type = type.BaseType;
        }
    }
}

/// <summary>
/// Result of a migration operation.
/// </summary>
public sealed class MigrationResult
{
    /// <summary>
    /// Whether the migration completed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The source file that was migrated.
    /// </summary>
    public string SourceFile { get; set; } = string.Empty;

    /// <summary>
    /// The balance from the legacy account.
    /// </summary>
    public decimal LegacyBalance { get; set; }

    /// <summary>
    /// Total number of positions in the legacy file.
    /// </summary>
    public int TotalLegacyPositions { get; set; }

    /// <summary>
    /// Number of positions successfully migrated.
    /// </summary>
    public int MigratedPositions { get; set; }

    /// <summary>
    /// Number of positions skipped (e.g., empty or invalid).
    /// </summary>
    public int SkippedPositions { get; set; }

    /// <summary>
    /// Number of positions that failed to migrate.
    /// </summary>
    public int FailedPositions { get; set; }

    /// <summary>
    /// List of errors encountered during migration.
    /// </summary>
    public List<string> Errors { get; } = new();

    /// <summary>
    /// List of warnings (non-fatal issues).
    /// </summary>
    public List<string> Warnings { get; } = new();

    /// <summary>
    /// Creates a result indicating the source file was not found.
    /// </summary>
    public static MigrationResult FileNotFound(string filePath)
    {
        return new MigrationResult
        {
            Success = false,
            SourceFile = filePath,
            Errors = { $"File not found: {filePath}" }
        };
    }
}
