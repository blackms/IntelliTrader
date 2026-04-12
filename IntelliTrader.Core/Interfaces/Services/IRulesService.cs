using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Service for evaluating trading rules against signal and position data.
    /// </summary>
    public interface IRulesService : IConfigurableService
    {
        /// <summary>
        /// The rules configuration containing all rule modules.
        /// </summary>
        IRulesConfig Config { get; }

        /// <summary>
        /// Gets the rule entries for a specific module.
        /// </summary>
        /// <param name="module">The module name (e.g., "Signals", "Trading").</param>
        /// <returns>The module rules.</returns>
        IModuleRules GetRules(string module);

        /// <summary>
        /// Evaluates a set of rule conditions against current signal and position data.
        /// </summary>
        /// <param name="conditions">The conditions to check.</param>
        /// <param name="signals">Current signal data keyed by signal name.</param>
        /// <param name="globalRating">The current global market rating.</param>
        /// <param name="pair">The trading pair being evaluated, if applicable.</param>
        /// <param name="tradingPair">The active trading pair position, if applicable.</param>
        /// <returns>True if all conditions are satisfied.</returns>
        bool CheckConditions(IEnumerable<IRuleCondition> conditions, Dictionary<string, ISignal> signals, double? globalRating, string? pair, ITradingPair? tradingPair);

        /// <summary>
        /// Registers a callback to be invoked when rules configuration changes.
        /// </summary>
        /// <param name="callback">The callback action.</param>
        void RegisterRulesChangeCallback(Action callback);

        /// <summary>
        /// Unregisters a previously registered rules change callback.
        /// </summary>
        /// <param name="callback">The callback action to remove.</param>
        void UnregisterRulesChangeCallback(Action callback);
    }
}
