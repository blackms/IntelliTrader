using FluentValidation;

namespace IntelliTrader.Core.Validation;

/// <summary>
/// Validates signals configuration (signals.json).
/// Ensures signal definitions are properly configured.
/// </summary>
public class SignalsConfigValidator : AbstractValidator<ISignalsConfig>
{
    private static readonly string[] ValidReceivers = { "TradingViewCryptoSignalReceiver" };

    public SignalsConfigValidator()
    {
        // When signals are enabled, validate the configuration
        When(x => x.Enabled, () =>
        {
            // Global rating signals validation
            RuleFor(x => x.GlobalRatingSignals)
                .NotEmpty()
                .WithMessage("GlobalRatingSignals must be specified when signals are enabled");

            When(x => x.GlobalRatingSignals != null, () =>
            {
                RuleFor(x => x.GlobalRatingSignals)
                    .Must(HaveAtLeastOneSignal)
                    .WithMessage("At least one global rating signal must be configured");

                RuleFor(x => x)
                    .Must(GlobalSignalsExistInDefinitions)
                    .WithMessage("All GlobalRatingSignals must reference signals defined in Definitions");
            });

            // Signal definitions validation
            RuleFor(x => x.Definitions)
                .NotEmpty()
                .WithMessage("At least one signal Definition must be provided when signals are enabled");

            When(x => x.Definitions != null, () =>
            {
                RuleFor(x => x.Definitions)
                    .Must(HaveUniqueNames)
                    .WithMessage("Signal definitions must have unique names");

                RuleForEach(x => x.Definitions)
                    .SetValidator(new SignalDefinitionValidator());
            });
        });
    }

    private static bool HaveAtLeastOneSignal(IEnumerable<string>? signals)
    {
        return signals != null && signals.Any();
    }

    private static bool GlobalSignalsExistInDefinitions(ISignalsConfig config)
    {
        if (config.GlobalRatingSignals == null || config.Definitions == null)
            return true;

        var definedNames = config.Definitions.Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return config.GlobalRatingSignals.All(s => definedNames.Contains(s));
    }

    private static bool HaveUniqueNames(IEnumerable<ISignalDefinition>? definitions)
    {
        if (definitions == null)
            return true;

        var names = definitions.Select(d => d.Name).ToList();
        return names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Count;
    }
}

/// <summary>
/// Validates individual signal definition entries.
/// </summary>
public class SignalDefinitionValidator : AbstractValidator<ISignalDefinition>
{
    private static readonly string[] ValidReceivers = { "TradingViewCryptoSignalReceiver" };

    public SignalDefinitionValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Signal definition Name is required")
            .MaximumLength(50)
            .WithMessage("Signal definition Name should not exceed 50 characters")
            .Matches(@"^[a-zA-Z0-9_\-]+$")
            .When(x => !string.IsNullOrEmpty(x.Name))
            .WithMessage("Signal definition Name should only contain letters, numbers, underscores, and hyphens. Current value: {PropertyValue}");

        RuleFor(x => x.Receiver)
            .NotEmpty()
            .WithMessage("Signal definition Receiver is required")
            .Must(BeValidReceiver)
            .WithMessage($"Receiver must be one of: {string.Join(", ", ValidReceivers)}. Current value: {{PropertyValue}}");

        RuleFor(x => x.Configuration)
            .NotNull()
            .WithMessage("Signal definition Configuration is required");
    }

    private static bool BeValidReceiver(string? receiver)
    {
        return !string.IsNullOrEmpty(receiver) &&
               ValidReceivers.Contains(receiver, StringComparer.OrdinalIgnoreCase);
    }
}
