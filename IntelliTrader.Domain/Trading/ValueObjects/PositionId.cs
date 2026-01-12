using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Trading.ValueObjects;

/// <summary>
/// Strongly-typed identifier for a Position.
/// </summary>
public sealed class PositionId : ValueObject
{
    public Guid Value { get; }

    private PositionId(Guid value)
    {
        Value = value;
    }

    public static PositionId Create() => new(Guid.NewGuid());

    public static PositionId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("PositionId cannot be empty", nameof(value));

        return new PositionId(value);
    }

    public static PositionId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("PositionId cannot be null or empty", nameof(value));

        if (!Guid.TryParse(value, out var guid))
            throw new ArgumentException("Invalid PositionId format", nameof(value));

        return From(guid);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(PositionId id) => id.Value;
}
