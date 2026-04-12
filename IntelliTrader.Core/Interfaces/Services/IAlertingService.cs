using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    public interface IAlertingService
    {
        void Start();
        void Stop();
        void CheckAlerts();
    }
}
