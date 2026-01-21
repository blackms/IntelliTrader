using System.Collections.Generic;

namespace IntelliTrader.Rules.Specifications
{
    /// <summary>
    /// Represents a specification that can evaluate whether a candidate satisfies certain criteria.
    /// Implements the Specification pattern for composable business rules.
    /// </summary>
    /// <typeparam name="T">The type of candidate being evaluated</typeparam>
    public interface ISpecification<T>
    {
        /// <summary>
        /// Determines whether the candidate satisfies this specification.
        /// </summary>
        /// <param name="candidate">The candidate to evaluate</param>
        /// <returns>True if the specification is satisfied, false otherwise</returns>
        bool IsSatisfiedBy(T candidate);
    }

    /// <summary>
    /// Base class for specifications providing common functionality.
    /// </summary>
    /// <typeparam name="T">The type of candidate being evaluated</typeparam>
    public abstract class Specification<T> : ISpecification<T>
    {
        public abstract bool IsSatisfiedBy(T candidate);

        /// <summary>
        /// Combines this specification with another using logical AND.
        /// </summary>
        public ISpecification<T> And(ISpecification<T> other)
        {
            return new AndSpecification<T>(this, other);
        }

        /// <summary>
        /// Combines this specification with another using logical OR.
        /// </summary>
        public ISpecification<T> Or(ISpecification<T> other)
        {
            return new OrSpecification<T>(this, other);
        }

        /// <summary>
        /// Negates this specification.
        /// </summary>
        public ISpecification<T> Not()
        {
            return new NotSpecification<T>(this);
        }
    }

    /// <summary>
    /// Specification that always returns true. Used as identity element for AND operations.
    /// </summary>
    /// <typeparam name="T">The type of candidate being evaluated</typeparam>
    public class TrueSpecification<T> : Specification<T>
    {
        public override bool IsSatisfiedBy(T candidate) => true;
    }

    /// <summary>
    /// Specification that combines two specifications with logical AND.
    /// </summary>
    /// <typeparam name="T">The type of candidate being evaluated</typeparam>
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
    /// Specification that combines two specifications with logical OR.
    /// </summary>
    /// <typeparam name="T">The type of candidate being evaluated</typeparam>
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
    /// Specification that negates another specification.
    /// </summary>
    /// <typeparam name="T">The type of candidate being evaluated</typeparam>
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
    /// Specification that combines multiple specifications with logical AND.
    /// All specifications must be satisfied for the composite to be satisfied.
    /// </summary>
    /// <typeparam name="T">The type of candidate being evaluated</typeparam>
    public class CompositeAndSpecification<T> : Specification<T>
    {
        private readonly IEnumerable<ISpecification<T>> _specifications;

        public CompositeAndSpecification(IEnumerable<ISpecification<T>> specifications)
        {
            _specifications = specifications;
        }

        public override bool IsSatisfiedBy(T candidate)
        {
            foreach (var specification in _specifications)
            {
                if (!specification.IsSatisfiedBy(candidate))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
