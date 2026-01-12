using System.Text.Json.Serialization;

namespace IntelliTrader.Infrastructure.Migration.LegacyModels;

/// <summary>
/// Legacy order metadata DTO matching the old JSON structure.
/// Used for deserializing existing account files during migration.
/// </summary>
public sealed class LegacyOrderMetadata
{
    [JsonPropertyName("TradingRules")]
    public List<string>? TradingRules { get; set; }

    [JsonPropertyName("SignalRule")]
    public string? SignalRule { get; set; }

    [JsonPropertyName("Signals")]
    public List<string>? Signals { get; set; }

    [JsonPropertyName("BoughtRating")]
    public double? BoughtRating { get; set; }

    [JsonPropertyName("CurrentRating")]
    public double? CurrentRating { get; set; }

    [JsonPropertyName("BoughtGlobalRating")]
    public double? BoughtGlobalRating { get; set; }

    [JsonPropertyName("CurrentGlobalRating")]
    public double? CurrentGlobalRating { get; set; }

    [JsonPropertyName("LastBuyMargin")]
    public decimal? LastBuyMargin { get; set; }

    [JsonPropertyName("AdditionalDCALevels")]
    public int? AdditionalDCALevels { get; set; }

    [JsonPropertyName("AdditionalCosts")]
    public decimal? AdditionalCosts { get; set; }

    [JsonPropertyName("SwapPair")]
    public string? SwapPair { get; set; }
}
