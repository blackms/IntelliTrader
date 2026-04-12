using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    internal class AlertingConfig : IAlertingConfig
    {
        public bool Enabled { get; set; } = true;
        public int CheckIntervalSeconds { get; set; } = 60;
        public bool TradingSuspendedAlert { get; set; } = true;
        public int HealthCheckFailureThreshold { get; set; } = 3;
        public int SignalStalenessMinutes { get; set; } = 5;
        public bool ConnectivityAlertEnabled { get; set; } = true;
        public int ConsecutiveOrderFailureThreshold { get; set; } = 3;
        public double HighErrorRateThreshold { get; set; } = 0.5;
    }
}
