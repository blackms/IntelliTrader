using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Service for structured logging with support for scoped contexts, correlation tracking, and sampled output.
    /// </summary>
    public interface ILoggingService : IConfigurableService
    {
        /// <summary>
        /// Logs a debug-level message with an optional exception.
        /// </summary>
        /// <param name="message">The log message template.</param>
        /// <param name="exception">Optional exception to include.</param>
        void Debug(string message, Exception exception = null);

        /// <summary>
        /// Logs a debug-level message with structured property values.
        /// </summary>
        /// <param name="message">The log message template.</param>
        /// <param name="propertyValues">Structured property values.</param>
        void Debug(string message, params object[] propertyValues);

        /// <summary>
        /// Logs an error-level message with an optional exception.
        /// </summary>
        /// <param name="message">The log message template.</param>
        /// <param name="exception">Optional exception to include.</param>
        void Error(string message, Exception exception = null);

        /// <summary>
        /// Logs an error-level message with structured property values.
        /// </summary>
        /// <param name="message">The log message template.</param>
        /// <param name="propertyValues">Structured property values.</param>
        void Error(string message, params object[] propertyValues);

        /// <summary>
        /// Logs a fatal-level message with an optional exception.
        /// </summary>
        /// <param name="message">The log message template.</param>
        /// <param name="exception">Optional exception to include.</param>
        void Fatal(string message, Exception exception = null);

        /// <summary>
        /// Logs a fatal-level message with structured property values.
        /// </summary>
        /// <param name="message">The log message template.</param>
        /// <param name="propertyValues">Structured property values.</param>
        void Fatal(string message, params object[] propertyValues);

        /// <summary>
        /// Logs an info-level message with an optional exception.
        /// </summary>
        /// <param name="message">The log message template.</param>
        /// <param name="exception">Optional exception to include.</param>
        void Info(string message, Exception exception = null);

        /// <summary>
        /// Logs an info-level message with structured property values.
        /// </summary>
        /// <param name="message">The log message template.</param>
        /// <param name="propertyValues">Structured property values.</param>
        void Info(string message, params object[] propertyValues);

        /// <summary>
        /// Logs a verbose-level message with an optional exception.
        /// </summary>
        /// <param name="message">The log message template.</param>
        /// <param name="exception">Optional exception to include.</param>
        void Verbose(string message, Exception exception = null);

        /// <summary>
        /// Logs a verbose-level message with structured property values.
        /// </summary>
        /// <param name="message">The log message template.</param>
        /// <param name="propertyValues">Structured property values.</param>
        void Verbose(string message, params object[] propertyValues);

        /// <summary>
        /// Logs a warning-level message with an optional exception.
        /// </summary>
        /// <param name="message">The log message template.</param>
        /// <param name="exception">Optional exception to include.</param>
        void Warning(string message, Exception exception = null);

        /// <summary>
        /// Logs a warning-level message with structured property values.
        /// </summary>
        /// <param name="message">The log message template.</param>
        /// <param name="propertyValues">Structured property values.</param>
        void Warning(string message, params object[] propertyValues);

        /// <summary>
        /// Deletes all log files.
        /// </summary>
        void DeleteAllLogs();

        /// <summary>
        /// Gets recent log entries as an array of strings.
        /// </summary>
        /// <returns>Array of log entry strings.</returns>
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

        /// <summary>
        /// Creates a logging scope with a correlation ID for tracing related log entries across an operation.
        /// All log entries within the scope will include the CorrelationId property.
        /// </summary>
        /// <param name="correlationId">Optional correlation ID. If null, a new GUID is generated.</param>
        /// <returns>A disposable scope that removes the correlation ID when disposed</returns>
        IDisposable BeginCorrelationScope(string? correlationId = null);

        /// <summary>
        /// Times an operation and logs its start and completion with duration in milliseconds.
        /// Logs at Information level on start and completion, or Error level on failure.
        /// </summary>
        /// <param name="operationName">Name of the operation being timed</param>
        /// <returns>A disposable that logs completion with duration when disposed</returns>
        IDisposable TimeOperation(string operationName);

        /// <summary>
        /// Logs a message only every Nth invocation for the given event key.
        /// Useful for high-volume events like ticker updates or signal polls.
        /// </summary>
        /// <param name="eventKey">Unique key identifying the high-volume event</param>
        /// <param name="sampleRate">Log every Nth occurrence (e.g., 100 means log 1 in 100)</param>
        /// <param name="message">The log message template</param>
        /// <param name="propertyValues">Optional structured property values</param>
        void InfoSampled(string eventKey, int sampleRate, string message, params object[] propertyValues);

        /// <summary>
        /// Logs a debug message only every Nth invocation for the given event key.
        /// </summary>
        /// <param name="eventKey">Unique key identifying the high-volume event</param>
        /// <param name="sampleRate">Log every Nth occurrence</param>
        /// <param name="message">The log message template</param>
        /// <param name="propertyValues">Optional structured property values</param>
        void DebugSampled(string eventKey, int sampleRate, string message, params object[] propertyValues);
    }
}
