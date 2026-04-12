using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Configuration containing the set of rule modules used for signal and trading evaluation.
    /// </summary>
    public interface IRulesConfig
    {
        /// <summary>
        /// The collection of rule modules (e.g., signal rules, trading rules) with their entries and configurations.
        /// </summary>
        IEnumerable<IModuleRules> Modules { get; }
    }
}
