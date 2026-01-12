namespace IntelliTrader.Domain.SharedKernel;

/// <summary>
/// Specification pattern interface for encapsulating business rules.
/// </summary>
/// <typeparam name="T">The type being evaluated</typeparam>
public interface ISpecification<in T>
{
    bool IsSatisfiedBy(T candidate);
}

/// <summary>
/// Base class for specifications with composite support.
/// </summary>
/// <typeparam name="T">The type being evaluated</typeparam>
public abstract class Specification<T> : ISpecification<T>
{
    public abstract bool IsSatisfiedBy(T candidate);

    public Specification<T> And(Specification<T> other)
    {
        return new AndSpecification<T>(this, other);
    }

    public Specification<T> Or(Specification<T> other)
    {
        return new OrSpecification<T>(this, other);
    }

    public Specification<T> Not()
    {
        return new NotSpecification<T>(this);
    }
}

/// <summary>
/// Combines two specifications with AND logic.
/// </summary>
public class AndSpecification<T> : Specification<T>
{
    private readonly ISpecification<T> _left;
    private readonly ISpecification<T> _right;

    public AndSpecification(ISpecification<T> left, ISpecification<T> right)
    {
        _left = left;
        _right = right;
    }

    public override bool IsSatisfiedBy(T candidate)
    {
        return _left.IsSatisfiedBy(candidate) && _right.IsSatisfiedBy(candidate);
    }
}

/// <summary>
/// Combines two specifications with OR logic.
/// </summary>
public class OrSpecification<T> : Specification<T>
{
    private readonly ISpecification<T> _left;
    private readonly ISpecification<T> _right;

    public OrSpecification(ISpecification<T> left, ISpecification<T> right)
    {
        _left = left;
        _right = right;
    }

    public override bool IsSatisfiedBy(T candidate)
    {
        return _left.IsSatisfiedBy(candidate) || _right.IsSatisfiedBy(candidate);
    }
}

/// <summary>
/// Negates a specification.
/// </summary>
public class NotSpecification<T> : Specification<T>
{
    private readonly ISpecification<T> _specification;

    public NotSpecification(ISpecification<T> specification)
    {
        _specification = specification;
    }

    public override bool IsSatisfiedBy(T candidate)
    {
        return !_specification.IsSatisfiedBy(candidate);
    }
}

/// <summary>
/// A specification that always returns true.
/// </summary>
public class TrueSpecification<T> : Specification<T>
{
    public override bool IsSatisfiedBy(T candidate) => true;
}

/// <summary>
/// A specification that always returns false.
/// </summary>
public class FalseSpecification<T> : Specification<T>
{
    public override bool IsSatisfiedBy(T candidate) => false;
}
