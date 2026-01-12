using System.Reflection;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.Json;

/// <summary>
/// Maps between Position domain aggregates and DTOs for JSON persistence.
/// Uses reflection to reconstruct aggregates from persisted data to preserve domain encapsulation.
/// </summary>
internal static class PositionMapper
{
    /// <summary>
    /// Converts a Position aggregate to a DTO for persistence.
    /// </summary>
    public static PositionDto ToDto(Position position, PortfolioId? portfolioId = null)
    {
        return new PositionDto
        {
            Id = position.Id.Value,
            Pair = new TradingPairDto
            {
                Symbol = position.Pair.Symbol,
                QuoteCurrency = position.Pair.QuoteCurrency
            },
            Currency = position.Currency,
            SignalRule = position.SignalRule,
            Entries = position.Entries.Select(ToDto).ToList(),
            OpenedAt = position.OpenedAt,
            LastBuyAt = position.LastBuyAt,
            IsClosed = position.IsClosed,
            ClosedAt = position.ClosedAt,
            PortfolioId = portfolioId?.Value
        };
    }

    /// <summary>
    /// Converts a DTO to a Position aggregate.
    /// </summary>
    public static Position FromDto(PositionDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.Entries.Count == 0)
            throw new InvalidOperationException("Position must have at least one entry");

        var pair = TradingPair.Create(dto.Pair.Symbol, dto.Pair.QuoteCurrency);
        var firstEntry = dto.Entries[0];

        // Create the position with the first entry using the factory method
        var position = Position.Open(
            pair,
            OrderId.From(firstEntry.OrderId),
            Price.Create(firstEntry.Price),
            Quantity.Create(firstEntry.Quantity),
            Money.Create(firstEntry.FeesAmount, firstEntry.FeesCurrency),
            dto.SignalRule,
            firstEntry.Timestamp);

        // Set the correct ID and timestamps using reflection
        SetPrivateProperty(position, "Id", PositionId.From(dto.Id));
        SetPrivateProperty(position, "OpenedAt", dto.OpenedAt);
        SetPrivateProperty(position, "LastBuyAt", dto.LastBuyAt);

        // Clear entries and rebuild from DTOs (the Open method adds one entry)
        var entriesField = GetPrivateField(position, "_entries");
        if (entriesField != null)
        {
            var entries = (List<PositionEntry>)entriesField.GetValue(position)!;
            entries.Clear();

            foreach (var entryDto in dto.Entries)
            {
                var entry = CreatePositionEntry(entryDto);
                entries.Add(entry);
            }
        }

        // Set closed state if applicable
        if (dto.IsClosed)
        {
            SetPrivateProperty(position, "IsClosed", true);
            SetPrivateProperty(position, "ClosedAt", dto.ClosedAt);
        }

        // Clear domain events since we're reconstituting from storage
        position.ClearDomainEvents();

        return position;
    }

    /// <summary>
    /// Gets the portfolio ID from a DTO.
    /// </summary>
    public static PortfolioId? GetPortfolioId(PositionDto dto)
    {
        return dto.PortfolioId.HasValue ? PortfolioId.From(dto.PortfolioId.Value) : null;
    }

    private static PositionEntryDto ToDto(PositionEntry entry)
    {
        return new PositionEntryDto
        {
            OrderId = entry.OrderId.Value,
            Price = entry.Price.Value,
            Quantity = entry.Quantity.Value,
            FeesAmount = entry.Fees.Amount,
            FeesCurrency = entry.Fees.Currency,
            Timestamp = entry.Timestamp,
            IsMigrated = entry.IsMigrated
        };
    }

    private static PositionEntry CreatePositionEntry(PositionEntryDto dto)
    {
        return PositionEntry.Create(
            OrderId.From(dto.OrderId),
            Price.Create(dto.Price),
            Quantity.Create(dto.Quantity),
            Money.Create(dto.FeesAmount, dto.FeesCurrency),
            dto.Timestamp,
            dto.IsMigrated);
    }

    private static void SetPrivateProperty(object obj, string propertyName, object? value)
    {
        // Search for the property in the type hierarchy
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

    private static FieldInfo? GetPrivateField(object obj, string fieldName)
    {
        return obj.GetType().GetField(fieldName,
            BindingFlags.NonPublic | BindingFlags.Instance);
    }
}
