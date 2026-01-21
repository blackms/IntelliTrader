using FluentValidation;

namespace IntelliTrader.Core.Validation;

/// <summary>
/// Validates individual DCA (Dollar Cost Averaging) level configuration.
/// </summary>
public class DCALevelValidator : AbstractValidator<DCALevel>
{
    public DCALevelValidator()
    {
        RuleFor(x => x.Margin)
            .LessThan(0)
            .WithMessage("DCA Margin must be negative (represents percentage drop from buy price). Current value: {PropertyValue}");

        When(x => x.BuyMultiplier.HasValue, () =>
        {
            RuleFor(x => x.BuyMultiplier!.Value)
                .GreaterThan(0)
                .WithMessage("BuyMultiplier must be positive when specified. Current value: {PropertyValue}");
        });

        When(x => x.BuySamePairTimeout.HasValue, () =>
        {
            RuleFor(x => x.BuySamePairTimeout!.Value)
                .GreaterThanOrEqualTo(0)
                .WithMessage("BuySamePairTimeout must be non-negative when specified. Current value: {PropertyValue}");
        });

        When(x => x.SellMargin.HasValue, () =>
        {
            RuleFor(x => x.SellMargin!.Value)
                .NotEqual(0)
                .WithMessage("SellMargin should not be zero when specified");
        });
    }
}

/// <summary>
/// Validates the entire DCA levels collection, ensuring proper ordering and consistency.
/// </summary>
public class DCALevelsCollectionValidator : AbstractValidator<List<DCALevel>>
{
    public DCALevelsCollectionValidator()
    {
        RuleFor(x => x)
            .Must(BeInDescendingMarginOrder)
            .WithMessage("DCA levels must be ordered by Margin in descending order (e.g., -1.5, -2.5, -4.5). Each level should trigger at a deeper loss than the previous one.");

        RuleForEach(x => x)
            .SetValidator(new DCALevelValidator());

        RuleFor(x => x)
            .Must(HaveUniqueMargins)
            .WithMessage("DCA levels must have unique Margin values. Duplicate margins detected.");
    }

    private static bool BeInDescendingMarginOrder(List<DCALevel>? levels)
    {
        if (levels == null || levels.Count <= 1)
            return true;

        for (int i = 1; i < levels.Count; i++)
        {
            // Margins should decrease (become more negative)
            // e.g., -1.5 > -2.5 > -4.5
            if (levels[i].Margin >= levels[i - 1].Margin)
                return false;
        }
        return true;
    }

    private static bool HaveUniqueMargins(List<DCALevel>? levels)
    {
        if (levels == null || levels.Count == 0)
            return true;

        var margins = levels.Select(l => l.Margin).ToList();
        return margins.Distinct().Count() == margins.Count;
    }
}
