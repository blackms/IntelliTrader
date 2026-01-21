using FluentValidation;

namespace IntelliTrader.Core.Validation;

/// <summary>
/// Validates rules configuration (rules.json).
/// Ensures trading and signal rules are properly configured.
/// </summary>
public class RulesConfigValidator : AbstractValidator<IRulesConfig>
{
    private static readonly string[] ValidModules = { "Signals", "Trading" };

    public RulesConfigValidator()
    {
        RuleFor(x => x.Modules)
            .NotEmpty()
            .WithMessage("At least one rules module must be configured");

        When(x => x.Modules != null, () =>
        {
            RuleForEach(x => x.Modules)
                .SetValidator(new ModuleRulesValidator());

            RuleFor(x => x.Modules)
                .Must(HaveUniqueModules)
                .WithMessage("Duplicate module names detected. Each module should appear only once.");
        });
    }

    private static bool HaveUniqueModules(IEnumerable<IModuleRules>? modules)
    {
        if (modules == null)
            return true;

        var names = modules.Select(m => m.Module).ToList();
        return names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Count;
    }
}

/// <summary>
/// Validates individual module rules entries.
/// </summary>
public class ModuleRulesValidator : AbstractValidator<IModuleRules>
{
    private static readonly string[] ValidModules = { "Signals", "Trading" };

    public ModuleRulesValidator()
    {
        RuleFor(x => x.Module)
            .NotEmpty()
            .WithMessage("Module name is required")
            .Must(BeValidModule)
            .WithMessage($"Module must be one of: {string.Join(", ", ValidModules)}. Current value: {{PropertyValue}}");

        RuleFor(x => x.Configuration)
            .NotNull()
            .WithMessage("Module Configuration is required");

        RuleFor(x => x.Entries)
            .NotEmpty()
            .WithMessage("At least one rule Entry must be defined for each module");

        When(x => x.Entries != null, () =>
        {
            RuleForEach(x => x.Entries)
                .SetValidator(new RuleValidator());

            RuleFor(x => x.Entries)
                .Must(HaveUniqueNames)
                .WithMessage("Rule names within a module must be unique");
        });
    }

    private static bool BeValidModule(string? module)
    {
        return !string.IsNullOrEmpty(module) &&
               ValidModules.Contains(module, StringComparer.OrdinalIgnoreCase);
    }

    private static bool HaveUniqueNames(IEnumerable<IRule>? rules)
    {
        if (rules == null)
            return true;

        var names = rules.Where(r => !string.IsNullOrEmpty(r.Name)).Select(r => r.Name).ToList();
        return names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Count;
    }
}

/// <summary>
/// Validates individual rule entries.
/// </summary>
public class RuleValidator : AbstractValidator<IRule>
{
    private static readonly string[] ValidActions = { "Buy", "Sell", "Swap", null!, "" }; // null/empty means default buy

    public RuleValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Rule Name is required")
            .MaximumLength(50)
            .WithMessage("Rule Name should not exceed 50 characters")
            .Matches(@"^[a-zA-Z0-9_\-\s]+$")
            .When(x => !string.IsNullOrEmpty(x.Name))
            .WithMessage("Rule Name should only contain letters, numbers, underscores, hyphens, and spaces. Current value: {PropertyValue}");

        // Action is optional (defaults to Buy for signal rules)
        When(x => !string.IsNullOrEmpty(x.Action), () =>
        {
            RuleFor(x => x.Action)
                .Must(BeValidAction)
                .WithMessage($"Action must be one of: Buy, Sell, Swap (or empty for default). Current value: {{PropertyValue}}");
        });

        // Rules must have at least one condition
        RuleFor(x => x.Conditions)
            .NotEmpty()
            .When(x => x.Enabled)
            .WithMessage("Enabled rules must have at least one Condition defined");

        When(x => x.Conditions != null, () =>
        {
            RuleForEach(x => x.Conditions)
                .SetValidator(new RuleConditionValidator());
        });

        // Trailing validation
        When(x => x.Trailing != null, () =>
        {
            RuleFor(x => x.Trailing)
                .SetValidator(new RuleTrailingValidator()!);
        });
    }

    private static bool BeValidAction(string? action)
    {
        return string.IsNullOrEmpty(action) ||
               new[] { "Buy", "Sell", "Swap" }.Contains(action, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Validates rule condition entries.
/// </summary>
public class RuleConditionValidator : AbstractValidator<IRuleCondition>
{
    public RuleConditionValidator()
    {
        // Volume constraints
        When(x => x.MinVolume.HasValue && x.MaxVolume.HasValue, () =>
        {
            RuleFor(x => x)
                .Must(x => x.MinVolume!.Value <= x.MaxVolume!.Value)
                .WithMessage("MinVolume must be less than or equal to MaxVolume");
        });

        When(x => x.MinVolume.HasValue, () =>
        {
            RuleFor(x => x.MinVolume!.Value)
                .GreaterThanOrEqualTo(0)
                .WithMessage("MinVolume cannot be negative. Current value: {PropertyValue}");
        });

        // Price constraints
        When(x => x.MinPrice.HasValue && x.MaxPrice.HasValue, () =>
        {
            RuleFor(x => x)
                .Must(x => x.MinPrice!.Value <= x.MaxPrice!.Value)
                .WithMessage("MinPrice must be less than or equal to MaxPrice");
        });

        When(x => x.MinPrice.HasValue, () =>
        {
            RuleFor(x => x.MinPrice!.Value)
                .GreaterThan(0)
                .WithMessage("MinPrice must be positive. Current value: {PropertyValue}");
        });

        // Rating constraints (-1 to 1 range for TradingView)
        When(x => x.MinRating.HasValue, () =>
        {
            RuleFor(x => x.MinRating!.Value)
                .InclusiveBetween(-1, 1)
                .WithMessage("MinRating must be between -1 and 1. Current value: {PropertyValue}");
        });

        When(x => x.MaxRating.HasValue, () =>
        {
            RuleFor(x => x.MaxRating!.Value)
                .InclusiveBetween(-1, 1)
                .WithMessage("MaxRating must be between -1 and 1. Current value: {PropertyValue}");
        });

        When(x => x.MinRating.HasValue && x.MaxRating.HasValue, () =>
        {
            RuleFor(x => x)
                .Must(x => x.MinRating!.Value <= x.MaxRating!.Value)
                .WithMessage("MinRating must be less than or equal to MaxRating");
        });

        // Global rating constraints
        When(x => x.MinGlobalRating.HasValue, () =>
        {
            RuleFor(x => x.MinGlobalRating!.Value)
                .InclusiveBetween(-1, 1)
                .WithMessage("MinGlobalRating must be between -1 and 1. Current value: {PropertyValue}");
        });

        When(x => x.MaxGlobalRating.HasValue, () =>
        {
            RuleFor(x => x.MaxGlobalRating!.Value)
                .InclusiveBetween(-1, 1)
                .WithMessage("MaxGlobalRating must be between -1 and 1. Current value: {PropertyValue}");
        });

        When(x => x.MinGlobalRating.HasValue && x.MaxGlobalRating.HasValue, () =>
        {
            RuleFor(x => x)
                .Must(x => x.MinGlobalRating!.Value <= x.MaxGlobalRating!.Value)
                .WithMessage("MinGlobalRating must be less than or equal to MaxGlobalRating");
        });

        // Volatility constraints (0-100 percentage)
        When(x => x.MinVolatility.HasValue, () =>
        {
            RuleFor(x => x.MinVolatility!.Value)
                .GreaterThanOrEqualTo(0)
                .WithMessage("MinVolatility cannot be negative. Current value: {PropertyValue}");
        });

        When(x => x.MaxVolatility.HasValue, () =>
        {
            RuleFor(x => x.MaxVolatility!.Value)
                .GreaterThanOrEqualTo(0)
                .WithMessage("MaxVolatility cannot be negative. Current value: {PropertyValue}");
        });

        // Age constraints (in hours typically)
        When(x => x.MinAge.HasValue, () =>
        {
            RuleFor(x => x.MinAge!.Value)
                .GreaterThanOrEqualTo(0)
                .WithMessage("MinAge cannot be negative. Current value: {PropertyValue}");
        });

        When(x => x.MinAge.HasValue && x.MaxAge.HasValue, () =>
        {
            RuleFor(x => x)
                .Must(x => x.MinAge!.Value <= x.MaxAge!.Value)
                .WithMessage("MinAge must be less than or equal to MaxAge");
        });

        // DCA level constraints
        When(x => x.MinDCALevel.HasValue, () =>
        {
            RuleFor(x => x.MinDCALevel!.Value)
                .GreaterThanOrEqualTo(0)
                .WithMessage("MinDCALevel cannot be negative. Current value: {PropertyValue}");
        });

        When(x => x.MinDCALevel.HasValue && x.MaxDCALevel.HasValue, () =>
        {
            RuleFor(x => x)
                .Must(x => x.MinDCALevel!.Value <= x.MaxDCALevel!.Value)
                .WithMessage("MinDCALevel must be less than or equal to MaxDCALevel");
        });

        // Amount constraints
        When(x => x.MinAmount.HasValue, () =>
        {
            RuleFor(x => x.MinAmount!.Value)
                .GreaterThan(0)
                .WithMessage("MinAmount must be positive. Current value: {PropertyValue}");
        });

        When(x => x.MinAmount.HasValue && x.MaxAmount.HasValue, () =>
        {
            RuleFor(x => x)
                .Must(x => x.MinAmount!.Value <= x.MaxAmount!.Value)
                .WithMessage("MinAmount must be less than or equal to MaxAmount");
        });

        // Cost constraints
        When(x => x.MinCost.HasValue, () =>
        {
            RuleFor(x => x.MinCost!.Value)
                .GreaterThan(0)
                .WithMessage("MinCost must be positive. Current value: {PropertyValue}");
        });

        When(x => x.MinCost.HasValue && x.MaxCost.HasValue, () =>
        {
            RuleFor(x => x)
                .Must(x => x.MinCost!.Value <= x.MaxCost!.Value)
                .WithMessage("MinCost must be less than or equal to MaxCost");
        });

        // Pairs validation
        When(x => x.Pairs != null && x.Pairs.Count > 0, () =>
        {
            RuleForEach(x => x.Pairs)
                .NotEmpty()
                .WithMessage("Pair names cannot be empty")
                .Matches(@"^[A-Z0-9]+$")
                .WithMessage("Pair names must be uppercase alphanumeric (e.g., 'BTCUSDT', 'ETHBTC')");
        });
    }
}

/// <summary>
/// Validates rule trailing configuration.
/// </summary>
public class RuleTrailingValidator : AbstractValidator<IRuleTrailing>
{
    public RuleTrailingValidator()
    {
        When(x => x.Enabled, () =>
        {
            RuleFor(x => x.MinDuration)
                .GreaterThan(0)
                .WithMessage("MinDuration must be positive when trailing is enabled. Current value: {PropertyValue}");

            RuleFor(x => x.MaxDuration)
                .GreaterThan(0)
                .WithMessage("MaxDuration must be positive when trailing is enabled. Current value: {PropertyValue}")
                .GreaterThanOrEqualTo(x => x.MinDuration)
                .WithMessage("MaxDuration must be greater than or equal to MinDuration");

            RuleFor(x => x.StartConditions)
                .NotEmpty()
                .WithMessage("StartConditions must be defined when trailing is enabled");

            When(x => x.StartConditions != null, () =>
            {
                RuleForEach(x => x.StartConditions)
                    .SetValidator(new RuleConditionValidator());
            });
        });
    }
}
