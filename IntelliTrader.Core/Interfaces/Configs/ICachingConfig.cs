using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    public interface ICachingConfig
    {
        bool Enabled { get; }
        bool Shared { get; }
        string SharedCachePath { get; }
        double SharedCacheCleanupInterval { get; }
        IEnumerable<KeyValuePair<string, double>> MaxAge { get; }
    }
}
