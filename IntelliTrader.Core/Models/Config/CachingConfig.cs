using IntelliTrader.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    internal class CachingConfig : ICachingConfig
    {
        public bool Enabled { get; set; }
        public bool Shared { get; set; }
        public string SharedCachePath { get; set; }
        public double SharedCacheCleanupInterval { get; set; }
        public IEnumerable<KeyValuePair<string, double>> MaxAge { get; set; }
    }
}
