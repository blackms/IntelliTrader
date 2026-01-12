using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Trading.ValueObjects;

/// <summary>
/// Strongly-typed identifier for a Portfolio.
/// </summary>
public sealed class PortfolioId : ValueObject
{
    public Guid Value { get; }

    private PortfolioId(Guid value)
    {
        Value = value;
    }

    public static PortfolioId Create() => new(Guid.NewGuid());

    public static PortfolioId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("PortfolioId cannot be empty", nameof(value));

        return new PortfolioId(value);
    }

    public static PortfolioId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("PortfolioId cannot be null or empty", nameof(value));

        if (!Guid.TryParse(value, out var guid))
            throw new ArgumentException("Invalid PortfolioId format", nameof(value));

        return From(guid);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(PortfolioId id) => id.Value;
}
