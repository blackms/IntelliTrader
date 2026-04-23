using System.Reflection;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.Json;

/// <summary>
/// Maps between Portfolio domain aggregates and DTOs for JSON persistence.
/// Uses reflection to rehydrate aggregate internals without exposing mutation APIs.
/// </summary>
internal static class PortfolioMapper
{
    public static PortfolioDto ToDto(Portfolio portfolio, bool isDefault)
    {
        ArgumentNullException.ThrowIfNull(portfolio);

        return new PortfolioDto
        {
            Id = portfolio.Id.Value,
            Name = portfolio.Name,
            Market = portfolio.Market,
            Balance = new PortfolioBalanceDto
            {
                Currency = portfolio.Balance.Currency,
                Total = portfolio.Balance.Total.Amount,
                Available = portfolio.Balance.Available.Amount,
                Reserved = portfolio.Balance.Reserved.Amount
            },
            MaxPositions = portfolio.MaxPositions,
            MinPositionCost = portfolio.MinPositionCost.Amount,
            CreatedAt = portfolio.CreatedAt,
            IsDefault = isDefault,
            ActivePositions = portfolio.ActivePositions
                .Select(kvp => new ActivePositionDto
                {
                    Pair = new TradingPairDto
                    {
                        Symbol = kvp.Key.Symbol,
                        QuoteCurrency = kvp.Key.QuoteCurrency
                    },
                    PositionId = kvp.Value.Value
                })
                .ToList(),
            PositionCosts = portfolio.ActivePositions.Values
                .Select(positionId => new PositionCostDto
                {
                    PositionId = positionId.Value,
                    Amount = portfolio.GetPositionCost(positionId)?.Amount ?? 0m,
                    Currency = portfolio.GetPositionCost(positionId)?.Currency ?? portfolio.Market
                })
                .ToList()
        };
    }

    public static Portfolio FromDto(PortfolioDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var portfolio = Portfolio.Create(
            dto.Name,
            dto.Market,
            dto.Balance.Total,
            dto.MaxPositions,
            dto.MinPositionCost);

        SetPrivateProperty(portfolio, "Id", PortfolioId.From(dto.Id));
        SetPrivateProperty(portfolio, "Name", dto.Name);
        SetPrivateProperty(portfolio, "Market", dto.Market);
        SetPrivateProperty(portfolio, "Balance", CreateBalance(dto.Balance));
        SetPrivateProperty(portfolio, "MaxPositions", dto.MaxPositions);
        SetPrivateProperty(portfolio, "MinPositionCost", Money.Create(dto.MinPositionCost, dto.Market));
        SetPrivateProperty(portfolio, "CreatedAt", dto.CreatedAt);

        var activePositionsField = GetPrivateField(portfolio, "_activePositions");
        if (activePositionsField?.GetValue(portfolio) is Dictionary<TradingPair, PositionId> activePositions)
        {
            activePositions.Clear();

            foreach (var activePosition in dto.ActivePositions)
            {
                activePositions[TradingPair.Create(activePosition.Pair.Symbol, activePosition.Pair.QuoteCurrency)] =
                    PositionId.From(activePosition.PositionId);
            }
        }

        var positionCostsField = GetPrivateField(portfolio, "_positionCosts");
        if (positionCostsField?.GetValue(portfolio) is Dictionary<PositionId, Money> positionCosts)
        {
            positionCosts.Clear();

            foreach (var positionCost in dto.PositionCosts)
            {
                positionCosts[PositionId.From(positionCost.PositionId)] =
                    Money.Create(positionCost.Amount, positionCost.Currency);
            }
        }

        portfolio.ClearDomainEvents();
        return portfolio;
    }

    private static PortfolioBalance CreateBalance(PortfolioBalanceDto dto)
    {
        var balance = PortfolioBalance.Create(dto.Total, dto.Currency);
        SetPrivateProperty(balance, "Total", Money.Create(dto.Total, dto.Currency));
        SetPrivateProperty(balance, "Available", Money.Create(dto.Available, dto.Currency));
        SetPrivateProperty(balance, "Reserved", Money.Create(dto.Reserved, dto.Currency));
        return balance;
    }

    private static void SetPrivateProperty(object obj, string propertyName, object? value)
    {
        var type = obj.GetType();
        while (type != null)
        {
            var backingField = type.GetField(
                $"<{propertyName}>k__BackingField",
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
        return obj.GetType().GetField(
            fieldName,
            BindingFlags.NonPublic | BindingFlags.Instance);
    }
}
