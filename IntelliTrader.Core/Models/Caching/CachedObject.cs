using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    internal class CachedObject
    {
        public DateTimeOffset LastUpdated { get; set; }
        public object Value { get; set; }
    }
}
