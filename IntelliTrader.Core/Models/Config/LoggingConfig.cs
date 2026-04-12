using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    internal class LoggingConfig : ILoggingConfig
    {
        public bool Enabled { get; set; }
        public bool JsonOutputEnabled { get; set; }
        public string? JsonOutputPath { get; set; }
    }
}
