using IntelliTrader.Core;
using IntelliTrader.Rules.Specifications;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelliTrader.Rules
{
    internal class RulesService(
        ILoggingService loggingService,
        IApplicationContext applicationContext,
        IConfigProvider configProvider) : ConfigurableServiceBase<RulesConfig>(configProvider), IRulesService
    {
        private readonly IApplicationContext _applicationContext = applicationContext ?? throw new ArgumentNullException(nameof(applicationContext));

        public override string ServiceName => Constants.ServiceNames.RulesService;

        protected override ILoggingService LoggingService => loggingService;

        IRulesConfig IRulesService.Config => Config;

        private readonly List<Action> rulesChangeCallbacks = new List<Action>();
        private readonly object _callbackLock = new object();

        public IModuleRules GetRules(string module)
        {
            IModuleRules moduleRules = Config.Modules.FirstOrDefault(m => m.Module == module);
            if (moduleRules != null)
            {
                return moduleRules;
            }
            else
            {
                throw new Exception($"Unable to find rules for {module}");
            }
        }

        /// <summary>
        /// Evaluates a collection of rule conditions against the provided context.
        /// All conditions must be satisfied for the method to return true.
        /// Uses the Specification pattern for clean, maintainable condition evaluation.
        /// </summary>
        /// <param name="conditions">The conditions to evaluate</param>
        /// <param name="signals">Dictionary of available signals by name</param>
        /// <param name="globalRating">The global rating across all signals</param>
        /// <param name="pair">The trading pair symbol</param>
        /// <param name="tradingPair">The trading pair data (may be null)</param>
        /// <returns>True if all conditions are satisfied, false otherwise</returns>
        public bool CheckConditions(
            IEnumerable<IRuleCondition> conditions,
            Dictionary<string, ISignal> signals,
            double? globalRating,
            string? pair,
            ITradingPair? tradingPair)
        {
            foreach (var condition in conditions)
            {
                // Create the context for this condition
                var context = ConditionContext.Create(
                    signals,
                    condition.Signal,
                    globalRating,
                    pair,
                    tradingPair,
                    _applicationContext.Speed);

                // Build the specification from the condition
                var specification = ConditionSpecificationBuilder.Build(condition);

                // Evaluate the specification
                if (!specification.IsSatisfiedBy(context))
                {
                    return false;
                }
            }

            return true;
        }

        public void RegisterRulesChangeCallback(Action callback)
        {
            lock (_callbackLock)
            {
                rulesChangeCallbacks.Add(callback);
            }
        }

        public void UnregisterRulesChangeCallback(Action callback)
        {
            lock (_callbackLock)
            {
                rulesChangeCallbacks.Remove(callback);
            }
        }

        protected override void OnConfigReloaded()
        {
            Action[] callbacks;
            lock (_callbackLock)
            {
                callbacks = rulesChangeCallbacks.ToArray();
            }
            foreach (var callback in callbacks)
            {
                callback();
            }
        }
    }
}
