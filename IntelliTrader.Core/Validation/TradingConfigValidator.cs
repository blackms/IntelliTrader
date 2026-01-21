using FluentValidation;

namespace IntelliTrader.Core.Validation;

/// <summary>
/// Validates trading configuration (trading.json).
/// Ensures all trading parameters are within valid ranges and logically consistent.
/// </summary>
public class TradingConfigValidator : AbstractValidator<ITradingConfig>
{
    private static readonly string[] ValidMarkets = { "BTC", "ETH", "USDT", "BUSD", "BNB", "EUR", "USD", "GBP" };
    private static readonly string[] ValidExchanges = { "Binance" };

    public TradingConfigValidator()
    {
        // Basic configuration
        RuleFor(x => x.Market)
            .NotEmpty()
            .WithMessage("Market is required (e.g., 'BTC', 'USDT')")
            .Must(BeValidMarket)
            .WithMessage($"Market must be one of: {string.Join(", ", ValidMarkets)}. Current value: {{PropertyValue}}");

        RuleFor(x => x.Exchange)
            .NotEmpty()
            .WithMessage("Exchange is required")
            .Must(BeValidExchange)
            .WithMessage($"Exchange must be one of: {string.Join(", ", ValidExchanges)}. Current value: {{PropertyValue}}");

        RuleFor(x => x.MaxPairs)
            .GreaterThan(0)
            .WithMessage("MaxPairs must be greater than 0. Current value: {PropertyValue}")
            .LessThanOrEqualTo(100)
            .WithMessage("MaxPairs should not exceed 100 for practical trading. Current value: {PropertyValue}");

        RuleFor(x => x.MinCost)
            .GreaterThan(0)
            .WithMessage("MinCost must be positive. Current value: {PropertyValue}");

        // Excluded pairs validation
        When(x => x.ExcludedPairs != null && x.ExcludedPairs.Count > 0, () =>
        {
            RuleForEach(x => x.ExcludedPairs)
                .Must(BeValidPairFormat)
                .WithMessage("Excluded pair '{PropertyValue}' does not match expected format (e.g., 'BNBBTC', 'ETHUSDT')");
        });

        // Buy configuration
        ConfigureBuyValidation();

        // Buy DCA configuration
        ConfigureBuyDCAValidation();

        // Sell configuration
        ConfigureSellValidation();

        // Sell DCA configuration
        ConfigureSellDCAValidation();

        // DCA Levels
        ConfigureDCALevelsValidation();

        // Account configuration
        ConfigureAccountValidation();
    }

    private void ConfigureBuyValidation()
    {
        RuleFor(x => x.BuyMaxCost)
            .GreaterThan(0)
            .WithMessage("BuyMaxCost must be positive. Current value: {PropertyValue}");

        RuleFor(x => x.BuyMultiplier)
            .GreaterThan(0)
            .WithMessage("BuyMultiplier must be positive. Current value: {PropertyValue}");

        RuleFor(x => x.BuyMinBalance)
            .GreaterThanOrEqualTo(0)
            .WithMessage("BuyMinBalance cannot be negative. Current value: {PropertyValue}");

        RuleFor(x => x.BuySamePairTimeout)
            .GreaterThanOrEqualTo(0)
            .WithMessage("BuySamePairTimeout cannot be negative. Current value: {PropertyValue}");

        // Trailing should typically be negative for buys (buying on dips)
        RuleFor(x => x.BuyTrailing)
            .LessThanOrEqualTo(0)
            .When(x => x.BuyTrailing != 0)
            .WithMessage("BuyTrailing should be negative or zero (represents price drop trigger). Current value: {PropertyValue}");

        RuleFor(x => x.BuyTrailingStopMargin)
            .GreaterThanOrEqualTo(0)
            .WithMessage("BuyTrailingStopMargin cannot be negative. Current value: {PropertyValue}");
    }

    private void ConfigureBuyDCAValidation()
    {
        RuleFor(x => x.BuyDCAMultiplier)
            .GreaterThan(0)
            .WithMessage("BuyDCAMultiplier must be positive. Current value: {PropertyValue}");

        RuleFor(x => x.BuyDCAMinBalance)
            .GreaterThanOrEqualTo(0)
            .WithMessage("BuyDCAMinBalance cannot be negative. Current value: {PropertyValue}");

        RuleFor(x => x.BuyDCASamePairTimeout)
            .GreaterThanOrEqualTo(0)
            .WithMessage("BuyDCASamePairTimeout cannot be negative. Current value: {PropertyValue}");

        RuleFor(x => x.BuyDCATrailing)
            .LessThanOrEqualTo(0)
            .When(x => x.BuyDCATrailing != 0)
            .WithMessage("BuyDCATrailing should be negative or zero (represents price drop trigger). Current value: {PropertyValue}");

        RuleFor(x => x.BuyDCATrailingStopMargin)
            .GreaterThanOrEqualTo(0)
            .WithMessage("BuyDCATrailingStopMargin cannot be negative. Current value: {PropertyValue}");
    }

    private void ConfigureSellValidation()
    {
        // SellMargin is the target profit percentage
        RuleFor(x => x.SellMargin)
            .NotEqual(0)
            .When(x => x.SellEnabled)
            .WithMessage("SellMargin should not be zero when selling is enabled");

        RuleFor(x => x.SellTrailing)
            .GreaterThanOrEqualTo(0)
            .WithMessage("SellTrailing cannot be negative. Current value: {PropertyValue}");

        RuleFor(x => x.SellTrailingStopMargin)
            .GreaterThanOrEqualTo(0)
            .WithMessage("SellTrailingStopMargin cannot be negative. Current value: {PropertyValue}");

        // Stop loss validation
        When(x => x.SellStopLossEnabled, () =>
        {
            RuleFor(x => x.SellStopLossMargin)
                .LessThan(0)
                .WithMessage("SellStopLossMargin must be negative (represents loss threshold). Current value: {PropertyValue}");

            RuleFor(x => x.SellStopLossMinAge)
                .GreaterThanOrEqualTo(0)
                .WithMessage("SellStopLossMinAge cannot be negative. Current value: {PropertyValue}");
        });
    }

    private void ConfigureSellDCAValidation()
    {
        RuleFor(x => x.SellDCATrailing)
            .GreaterThanOrEqualTo(0)
            .WithMessage("SellDCATrailing cannot be negative. Current value: {PropertyValue}");

        RuleFor(x => x.SellDCATrailingStopMargin)
            .GreaterThanOrEqualTo(0)
            .WithMessage("SellDCATrailingStopMargin cannot be negative. Current value: {PropertyValue}");
    }

    private void ConfigureDCALevelsValidation()
    {
        When(x => x.DCALevels != null && x.DCALevels.Count > 0, () =>
        {
            RuleFor(x => x.DCALevels)
                .SetValidator(new DCALevelsCollectionValidator()!);
        });

        // If DCA buying is enabled, should have DCA levels defined
        RuleFor(x => x.DCALevels)
            .NotEmpty()
            .When(x => x.BuyDCAEnabled)
            .WithMessage("DCALevels must be defined when BuyDCAEnabled is true");
    }

    private void ConfigureAccountValidation()
    {
        RuleFor(x => x.TradingCheckInterval)
            .GreaterThan(0)
            .WithMessage("TradingCheckInterval must be positive. Current value: {PropertyValue}");

        RuleFor(x => x.AccountRefreshInterval)
            .GreaterThan(0)
            .WithMessage("AccountRefreshInterval must be positive. Current value: {PropertyValue}");

        RuleFor(x => x.AccountInitialBalance)
            .GreaterThan(0)
            .WithMessage("AccountInitialBalance must be positive. Current value: {PropertyValue}");

        RuleFor(x => x.AccountFilePath)
            .NotEmpty()
            .WithMessage("AccountFilePath is required");

        // Virtual trading validation
        When(x => x.VirtualTrading, () =>
        {
            RuleFor(x => x.VirtualAccountInitialBalance)
                .GreaterThan(0)
                .WithMessage("VirtualAccountInitialBalance must be positive when VirtualTrading is enabled. Current value: {PropertyValue}");

            RuleFor(x => x.VirtualAccountFilePath)
                .NotEmpty()
                .WithMessage("VirtualAccountFilePath is required when VirtualTrading is enabled");
        });
    }

    private static bool BeValidMarket(string? market)
    {
        return !string.IsNullOrEmpty(market) &&
               ValidMarkets.Contains(market, StringComparer.OrdinalIgnoreCase);
    }

    private static bool BeValidExchange(string? exchange)
    {
        return !string.IsNullOrEmpty(exchange) &&
               ValidExchanges.Contains(exchange, StringComparer.OrdinalIgnoreCase);
    }

    private static bool BeValidPairFormat(string? pair)
    {
        if (string.IsNullOrEmpty(pair))
            return false;

        // Pair should be uppercase alphanumeric, typically 6-12 characters
        // e.g., BTCUSDT, ETHBTC, BNBETH
        return pair.Length >= 5 && pair.Length <= 15 &&
               pair.All(char.IsLetterOrDigit) &&
               pair.All(c => char.IsUpper(c) || char.IsDigit(c));
    }
}
