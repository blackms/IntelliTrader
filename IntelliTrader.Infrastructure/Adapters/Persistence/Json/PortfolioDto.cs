using System.Text.Json.Serialization;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.Json;

/// <summary>
/// Data transfer object for Portfolio aggregate persistence.
/// </summary>
internal sealed class PortfolioDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("market")]
    public string Market { get; set; } = null!;

    [JsonPropertyName("balance")]
    public PortfolioBalanceDto Balance { get; set; } = null!;

    [JsonPropertyName("maxPositions")]
    public int MaxPositions { get; set; }

    [JsonPropertyName("minPositionCost")]
    public decimal MinPositionCost { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; }

    [JsonPropertyName("activePositions")]
    public List<ActivePositionDto> ActivePositions { get; set; } = new();

    [JsonPropertyName("positionCosts")]
    public List<PositionCostDto> PositionCosts { get; set; } = new();
}

internal sealed class PortfolioBalanceDto
{
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = null!;

    [JsonPropertyName("total")]
    public decimal Total { get; set; }

    [JsonPropertyName("available")]
    public decimal Available { get; set; }

    [JsonPropertyName("reserved")]
    public decimal Reserved { get; set; }
}

internal sealed class ActivePositionDto
{
    [JsonPropertyName("pair")]
    public TradingPairDto Pair { get; set; } = null!;

    [JsonPropertyName("positionId")]
    public Guid PositionId { get; set; }
}

internal sealed class PositionCostDto
{
    [JsonPropertyName("positionId")]
    public Guid PositionId { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = null!;
}
