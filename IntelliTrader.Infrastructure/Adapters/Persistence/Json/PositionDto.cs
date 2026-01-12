using System.Text.Json.Serialization;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.Json;

/// <summary>
/// Data transfer object for Position aggregate persistence.
/// </summary>
internal sealed class PositionDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("pair")]
    public TradingPairDto Pair { get; set; } = null!;

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = null!;

    [JsonPropertyName("signalRule")]
    public string? SignalRule { get; set; }

    [JsonPropertyName("entries")]
    public List<PositionEntryDto> Entries { get; set; } = new();

    [JsonPropertyName("openedAt")]
    public DateTimeOffset OpenedAt { get; set; }

    [JsonPropertyName("lastBuyAt")]
    public DateTimeOffset LastBuyAt { get; set; }

    [JsonPropertyName("isClosed")]
    public bool IsClosed { get; set; }

    [JsonPropertyName("closedAt")]
    public DateTimeOffset? ClosedAt { get; set; }

    [JsonPropertyName("portfolioId")]
    public Guid? PortfolioId { get; set; }
}

/// <summary>
/// Data transfer object for TradingPair value object.
/// </summary>
internal sealed class TradingPairDto
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = null!;

    [JsonPropertyName("quoteCurrency")]
    public string QuoteCurrency { get; set; } = null!;
}

/// <summary>
/// Data transfer object for PositionEntry value object.
/// </summary>
internal sealed class PositionEntryDto
{
    [JsonPropertyName("orderId")]
    public string OrderId { get; set; } = null!;

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("feesAmount")]
    public decimal FeesAmount { get; set; }

    [JsonPropertyName("feesCurrency")]
    public string FeesCurrency { get; set; } = null!;

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("isMigrated")]
    public bool IsMigrated { get; set; }
}
