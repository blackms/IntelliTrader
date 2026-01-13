using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    public interface ILoggingService : IConfigurableService
    {
        void Debug(string message, Exception exception = null);
        void Debug(string message, params object[] propertyValues);
        void Error(string message, Exception exception = null);
        void Error(string message, params object[] propertyValues);
        void Fatal(string message, Exception exception = null);
        void Fatal(string message, params object[] propertyValues);
        void Info(string message, Exception exception = null);
        void Info(string message, params object[] propertyValues);
        void Verbose(string message, Exception exception = null);
        void Verbose(string message, params object[] propertyValues);
        void Warning(string message, Exception exception = null);
        void Warning(string message, params object[] propertyValues);
        void DeleteAllLogs();
        string[] GetLogEntries();

        /// <summary>
        /// Creates a logging scope with contextual properties that are included in all log entries within the scope.
        /// </summary>
        /// <param name="properties">Dictionary of property names and values to include in log context</param>
        /// <returns>A disposable scope that removes the context when disposed</returns>
        IDisposable BeginScope(IDictionary<string, object> properties);

        /// <summary>
        /// Creates a logging scope with a single contextual property.
        /// </summary>
        /// <param name="propertyName">The property name</param>
        /// <param name="value">The property value</param>
        /// <returns>A disposable scope that removes the context when disposed</returns>
        IDisposable BeginScope(string propertyName, object value);
    }
}
