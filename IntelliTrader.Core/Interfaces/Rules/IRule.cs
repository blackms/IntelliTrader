using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Represents a single trading rule with conditions, trailing settings, and action modifiers.
    /// </summary>
    public interface IRule
    {
        /// <summary>
        /// Whether this rule is active and should be evaluated.
        /// </summary>
        bool Enabled { get; }

        /// <summary>
        /// The display name of this rule.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The action to execute when all conditions are met (e.g., "Buy", "Sell", "DCA").
        /// </summary>
        string Action { get; }

        /// <summary>
        /// The conditions that must all be satisfied for this rule to trigger.
        /// </summary>
        IEnumerable<IRuleCondition> Conditions { get; }

        /// <summary>
        /// Optional trailing configuration that delays action execution for confirmation.
        /// </summary>
        IRuleTrailing Trailing { get; }

        /// <summary>
        /// Raw configuration section containing action-specific modifiers.
        /// </summary>
        IConfigurationSection Modifiers { get; }

        /// <summary>
        /// Gets the action modifiers bound to a strongly-typed object.
        /// </summary>
        /// <typeparam name="T">The modifier type to bind to.</typeparam>
        /// <returns>The bound modifier object.</returns>
        T GetModifiers<T>();
    }
}
