using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    public interface ICachingService : IConfigurableService
    {
        ICachingConfig Config { get; }
        T GetOrRefresh<T>(string objectName, Func<T> refresh);
    }
}
