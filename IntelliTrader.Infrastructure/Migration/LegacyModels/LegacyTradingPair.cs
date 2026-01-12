using System.Text.Json.Serialization;

namespace IntelliTrader.Infrastructure.Migration.LegacyModels;

/// <summary>
/// Legacy trading pair DTO matching the old JSON structure.
/// Used for deserializing existing account files during migration.
/// </summary>
public sealed class LegacyTradingPair
{
    [JsonPropertyName("Pair")]
    public string Pair { get; set; } = null!;

    [JsonPropertyName("OrderIds")]
    public List<string> OrderIds { get; set; } = new();

    [JsonPropertyName("OrderDates")]
    public List<DateTimeOffset> OrderDates { get; set; } = new();

    [JsonPropertyName("TotalAmount")]
    public decimal TotalAmount { get; set; }

    [JsonPropertyName("AveragePricePaid")]
    public decimal AveragePricePaid { get; set; }

    [JsonPropertyName("FeesPairCurrency")]
    public decimal FeesPairCurrency { get; set; }

    [JsonPropertyName("FeesMarketCurrency")]
    public decimal FeesMarketCurrency { get; set; }

    [JsonPropertyName("CurrentPrice")]
    public decimal CurrentPrice { get; set; }

    [JsonPropertyName("Metadata")]
    public LegacyOrderMetadata? Metadata { get; set; }

    /// <summary>
    /// Calculates the number of DCA levels (entries - 1 + additional manual DCAs).
    /// </summary>
    public int DCALevel => (OrderDates.Count - 1) + (Metadata?.AdditionalDCALevels ?? 0);

    /// <summary>
    /// Calculates the average cost paid including fees.
    /// </summary>
    public decimal AverageCostPaid => AveragePricePaid * (TotalAmount + FeesPairCurrency) + FeesMarketCurrency;
}
