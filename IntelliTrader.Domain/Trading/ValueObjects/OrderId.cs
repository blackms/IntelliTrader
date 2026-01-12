using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Trading.ValueObjects;

/// <summary>
/// Strongly-typed identifier for an Order from the exchange.
/// </summary>
public sealed class OrderId : ValueObject
{
    public string Value { get; }

    private OrderId(string value)
    {
        Value = value;
    }

    public static OrderId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("OrderId cannot be null or empty", nameof(value));

        return new OrderId(value.Trim());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    public static implicit operator string(OrderId id) => id.Value;
}
