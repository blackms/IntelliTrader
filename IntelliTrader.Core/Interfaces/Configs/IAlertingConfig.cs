using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    public interface IAlertingConfig
    {
        bool Enabled { get; }
        int CheckIntervalSeconds { get; }
        bool TradingSuspendedAlert { get; }
        int HealthCheckFailureThreshold { get; }
        int SignalStalenessMinutes { get; }
        bool ConnectivityAlertEnabled { get; }
        int ConsecutiveOrderFailureThreshold { get; }
        double HighErrorRateThreshold { get; }
    }
}
