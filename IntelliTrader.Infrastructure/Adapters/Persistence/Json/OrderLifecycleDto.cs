using System.Text.Json.Serialization;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.Json;

/// <summary>
/// Data transfer object for OrderLifecycle persistence.
/// </summary>
internal sealed class OrderLifecycleDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("pair")]
    public TradingPairDto Pair { get; set; } = null!;

    [JsonPropertyName("side")]
    public string Side { get; set; } = null!;

    [JsonPropertyName("type")]
    public string Type { get; set; } = null!;

    [JsonPropertyName("status")]
    public string Status { get; set; } = null!;

    [JsonPropertyName("requestedQuantity")]
    public decimal RequestedQuantity { get; set; }

    [JsonPropertyName("filledQuantity")]
    public decimal FilledQuantity { get; set; }

    [JsonPropertyName("submittedPrice")]
    public decimal SubmittedPrice { get; set; }

    [JsonPropertyName("averagePrice")]
    public decimal AveragePrice { get; set; }

    [JsonPropertyName("costAmount")]
    public decimal CostAmount { get; set; }

    [JsonPropertyName("costCurrency")]
    public string CostCurrency { get; set; } = null!;

    [JsonPropertyName("feesAmount")]
    public decimal FeesAmount { get; set; }

    [JsonPropertyName("feesCurrency")]
    public string FeesCurrency { get; set; } = null!;

    [JsonPropertyName("appliedQuantity")]
    public decimal AppliedQuantity { get; set; }

    [JsonPropertyName("appliedCostAmount")]
    public decimal AppliedCostAmount { get; set; }

    [JsonPropertyName("appliedCostCurrency")]
    public string? AppliedCostCurrency { get; set; }

    [JsonPropertyName("appliedFeesAmount")]
    public decimal AppliedFeesAmount { get; set; }

    [JsonPropertyName("appliedFeesCurrency")]
    public string? AppliedFeesCurrency { get; set; }

    [JsonPropertyName("signalRule")]
    public string? SignalRule { get; set; }

    [JsonPropertyName("intent")]
    public string? Intent { get; set; }

    [JsonPropertyName("relatedPositionId")]
    public string? RelatedPositionId { get; set; }

    [JsonPropertyName("submittedAt")]
    public DateTimeOffset SubmittedAt { get; set; }
}
