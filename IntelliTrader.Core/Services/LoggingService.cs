using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace IntelliTrader.Core
{
    internal class LoggingService : ConfigurableServiceBase<LoggingConfig>, ILoggingService
    {
        private int LOG_ENTRIES_MAX_LENGTH = 50000;

        public override string ServiceName => Constants.ServiceNames.LoggingService;

        private Logger logger;
        private StringWriter writer;
        private StringBuilder writerStringBuilder;
        private string logsPath;

        private readonly object syncRoot = new object();
        private readonly ConcurrentDictionary<string, long> _samplingCounters = new ConcurrentDictionary<string, long>();

        public LoggingService(IConfigProvider configProvider) : base(configProvider)
        {
            if (Config.Enabled)
            {
                logger = CreateLogger();
            }
        }

        public void Verbose(string message, Exception exception = null)
        {
            lock (syncRoot)
            {
                if (Config.Enabled)
                {
                    logger.Verbose(exception, message);
                    CleanUpOldLogEntries();
                }
            }
        }

        public void Verbose(string message, params object[] propertyValues)
        {
            lock (syncRoot)
            {
                if (Config.Enabled)
                {
                    logger.Verbose(message, propertyValues);
                    CleanUpOldLogEntries();
                }
            }
        }

        public void Debug(string message, Exception exception = null)
        {
            lock (syncRoot)
            {
                if (Config.Enabled)
                {
                    logger.Debug(exception, message);
                    CleanUpOldLogEntries();
                }
            }
        }

        public void Debug(string message, params object[] propertyValues)
        {
            lock (syncRoot)
            {
                if (Config.Enabled)
                {
                    logger.Debug(message, propertyValues);
                    CleanUpOldLogEntries();
                }
            }
        }

        public void Info(string message, Exception exception = null)
        {
            lock (syncRoot)
            {
                if (Config.Enabled)
                {
                    logger.Information(exception, message);
                    CleanUpOldLogEntries();
                }
            }
        }

        public void Info(string message, params object[] propertyValues)
        {
            lock (syncRoot)
            {
                if (Config.Enabled)
                {
                    logger.Information(message, propertyValues);
                    CleanUpOldLogEntries();
                }
            }
        }

        public void Warning(string message, Exception exception = null)
        {
            lock (syncRoot)
            {
                if (Config.Enabled)
                {
                    logger.Warning(exception, message);
                    CleanUpOldLogEntries();
                }
            }
        }

        public void Warning(string message, params object[] propertyValues)
        {
            lock (syncRoot)
            {
                if (Config.Enabled)
                {
                    logger.Warning(message, propertyValues);
                    CleanUpOldLogEntries();
                }
            }
        }

        public void Error(string message, Exception exception = null)
        {
            lock (syncRoot)
            {
                if (Config.Enabled)
                {
                    logger.Error(exception, message);
                    CleanUpOldLogEntries();
                }
            }
        }

        public void Error(string message, params object[] propertyValues)
        {
            lock (syncRoot)
            {
                if (Config.Enabled)
                {
                    logger.Error(message, propertyValues);
                    CleanUpOldLogEntries();
                }
            }
        }

        public void Fatal(string message, Exception exception = null)
        {
            lock (syncRoot)
            {
                if (Config.Enabled)
                {
                    logger.Fatal(exception, message);
                    CleanUpOldLogEntries();
                }
            }
        }

        public void Fatal(string message, params object[] propertyValues)
        {
            lock (syncRoot)
            {
                if (Config.Enabled)
                {
                    logger.Fatal(message, propertyValues);
                    CleanUpOldLogEntries();
                }
            }
        }

        public IDisposable BeginCorrelationScope(string? correlationId = null)
        {
            var id = correlationId ?? Guid.NewGuid().ToString("N");
            return LogContext.PushProperty(LogProperties.CorrelationId, id);
        }

        public IDisposable TimeOperation(string operationName)
        {
            return new OperationTimer(this, operationName);
        }

        public void InfoSampled(string eventKey, int sampleRate, string message, params object[] propertyValues)
        {
            if (sampleRate <= 0) sampleRate = 1;
            var count = _samplingCounters.AddOrUpdate(eventKey, 1, (_, prev) => prev + 1);
            if (count % sampleRate == 0)
            {
                Info(message, propertyValues);
            }
        }

        public void DebugSampled(string eventKey, int sampleRate, string message, params object[] propertyValues)
        {
            if (sampleRate <= 0) sampleRate = 1;
            var count = _samplingCounters.AddOrUpdate(eventKey, 1, (_, prev) => prev + 1);
            if (count % sampleRate == 0)
            {
                Debug(message, propertyValues);
            }
        }

        public void DeleteAllLogs()
        {
            lock(syncRoot)
            {
                logger.Dispose();
                Directory.Delete(Path.Combine(Directory.GetCurrentDirectory(), logsPath), true);
                logger = CreateLogger();
            }
        }

        public string[] GetLogEntries()
        {
            lock (syncRoot)
            {
                if (writer != null)
                {
                    writer.Flush();
                    return writer.GetStringBuilder().ToString().Split(new string[] { writer.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                }
                else
                {
                    return new string[0];
                }
            }
        }

        public IDisposable BeginScope(IDictionary<string, object> properties)
        {
            if (properties == null || properties.Count == 0)
            {
                return new NoOpDisposable();
            }

            var disposables = new List<IDisposable>();
            foreach (var kvp in properties)
            {
                disposables.Add(LogContext.PushProperty(kvp.Key, kvp.Value));
            }
            return new CompositeDisposable(disposables);
        }

        public IDisposable BeginScope(string propertyName, object value)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return new NoOpDisposable();
            }

            return LogContext.PushProperty(propertyName, value);
        }

        protected override void OnConfigReloaded()
        {
            lock (syncRoot)
            {
                logger?.Dispose();
                logger = CreateLogger();
            }
        }

        private Logger CreateLogger()
        {
            lock (syncRoot)
            {
                string outputTemplate = GetConfigValue("outputTemplate", RawConfig.GetChildren());
                string filterExpression = GetConfigValue("expression", RawConfig.GetChildren());
                string pathFormat = GetConfigValue("pathFormat", RawConfig.GetChildren());
                logsPath = Path.GetDirectoryName(pathFormat);

                writerStringBuilder = new StringBuilder();
                writer = new StringWriter(writerStringBuilder);

                var loggerConfig = new LoggerConfiguration()
                    .ReadFrom.ConfigurationSection(RawConfig)
                    .Enrich.FromLogContext()
                    .WriteTo.Logger(config => config.WriteTo.Memory(writer, LogEventLevel.Information, outputTemplate).Filter.ByIncludingOnly(filterExpression));

                // Add JSON file sink if configured
                if (Config.JsonOutputEnabled)
                {
                    var jsonPath = Config.JsonOutputPath ?? "log/structured-.json";
                    loggerConfig.WriteTo.File(
                        new JsonFormatter(renderMessage: true),
                        jsonPath,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 31);
                }

                return loggerConfig.CreateLogger();
            }
        }

        private void CleanUpOldLogEntries()
        {
            lock (syncRoot)
            {
                if (writerStringBuilder.Length > LOG_ENTRIES_MAX_LENGTH)
                {
                    writerStringBuilder.Remove(0, writerStringBuilder.Length - LOG_ENTRIES_MAX_LENGTH);
                }
            }
        }

        private string GetConfigValue(string key, IEnumerable<IConfigurationSection> sections)
        {
            foreach (var section in sections)
            {
                if (section.Key == key)
                {
                    return section.Value;
                }
                else
                {
                    string value = GetConfigValue(key, section.GetChildren());
                    if (value != null)
                    {
                        return value;
                    }
                }
            }
            return null;
        }
    }

    /// <summary>
    /// A disposable that does nothing when disposed.
    /// Used when no scope properties are provided.
    /// </summary>
    internal sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }

    /// <summary>
    /// A disposable that disposes multiple disposables when disposed.
    /// Used to manage multiple LogContext.PushProperty scopes.
    /// </summary>
    internal sealed class CompositeDisposable : IDisposable
    {
        private readonly IReadOnlyList<IDisposable> _disposables;
        private bool _disposed;

        public CompositeDisposable(IReadOnlyList<IDisposable> disposables)
        {
            _disposables = disposables ?? throw new ArgumentNullException(nameof(disposables));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Dispose in reverse order to properly unwind the scope stack
            for (int i = _disposables.Count - 1; i >= 0; i--)
            {
                _disposables[i]?.Dispose();
            }
        }
    }

    /// <summary>
    /// Tracks the duration of an operation and logs completion with elapsed time.
    /// </summary>
    internal sealed class OperationTimer : IDisposable
    {
        private readonly ILoggingService _loggingService;
        private readonly string _operationName;
        private readonly Stopwatch _stopwatch;
        private readonly IDisposable _scope;
        private bool _disposed;

        public OperationTimer(ILoggingService loggingService, string operationName)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _operationName = operationName ?? throw new ArgumentNullException(nameof(operationName));
            _stopwatch = Stopwatch.StartNew();

            _scope = loggingService.BeginScope(LogProperties.Operation, operationName);
            _loggingService.Info("Operation {Operation} started", operationName);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopwatch.Stop();

            using (LogContext.PushProperty(LogProperties.Duration, _stopwatch.ElapsedMilliseconds))
            {
                _loggingService.Info(
                    "Operation {Operation} completed in {DurationMs}ms",
                    _operationName,
                    _stopwatch.ElapsedMilliseconds);
            }

            _scope?.Dispose();
        }
    }
}
