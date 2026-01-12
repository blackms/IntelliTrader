using System.Text.Json.Serialization;

namespace IntelliTrader.Infrastructure.Migration.LegacyModels;

/// <summary>
/// Legacy trading account data DTO matching the old JSON structure.
/// Used for deserializing existing account files during migration.
/// </summary>
public sealed class LegacyTradingAccountData
{
    [JsonPropertyName("Balance")]
    public decimal Balance { get; set; }

    [JsonPropertyName("TradingPairs")]
    public Dictionary<string, LegacyTradingPair> TradingPairs { get; set; } = new();
}
