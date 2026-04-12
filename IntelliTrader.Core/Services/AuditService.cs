using System.Globalization;
using System.Text;
using System.Text.Json;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Writes structured audit log entries to a dedicated file, separate from application logs.
    /// Thread-safe; entries are flushed immediately so nothing is lost on crash.
    /// Supports configurable retention and file-size rolling.
    /// </summary>
    internal sealed class AuditService : IAuditService, IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = null,
            WriteIndented = false
        };

        private readonly object _syncRoot = new();
        private readonly AuditConfig _config;
        private readonly string _logFilePath;
        private StreamWriter _writer;
        private bool _disposed;

        public AuditService(AuditConfig config)
        {
            _config = config ?? new AuditConfig();

            var logDir = Path.IsPathRooted(_config.LogDirectory)
                ? _config.LogDirectory
                : Path.Combine(Directory.GetCurrentDirectory(), _config.LogDirectory);

            Directory.CreateDirectory(logDir);

            _logFilePath = Path.Combine(logDir, _config.LogFileName);

            if (_config.Enabled)
            {
                _writer = CreateWriter();
                CleanUpOldFiles(logDir);
            }
        }

        public void LogAudit(string action, string details, string user = null, string ipAddress = null)
        {
            if (!_config.Enabled || _disposed)
                return;

            var entry = new
            {
                Timestamp = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Action = action ?? string.Empty,
                Details = details ?? string.Empty,
                User = user ?? "anonymous",
                IpAddress = ipAddress ?? "unknown"
            };

            var json = JsonSerializer.Serialize(entry, JsonOptions);

            lock (_syncRoot)
            {
                if (_disposed) return;
                RollFileIfNeeded();
                _writer.WriteLine(json);
                _writer.Flush();
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed) return;
                _disposed = true;
                _writer?.Dispose();
            }
        }

        // --- internals ---

        private StreamWriter CreateWriter()
        {
            return new StreamWriter(_logFilePath, append: true, encoding: Encoding.UTF8)
            {
                AutoFlush = false
            };
        }

        private void RollFileIfNeeded()
        {
            if (!File.Exists(_logFilePath))
                return;

            var info = new FileInfo(_logFilePath);
            if (info.Length < (long)_config.MaxFileSizeMB * 1024 * 1024)
                return;

            _writer?.Dispose();

            var rolled = _logFilePath + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(_logFilePath, rolled);

            _writer = CreateWriter();
        }

        private void CleanUpOldFiles(string logDir)
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-_config.RetentionDays);
                foreach (var file in Directory.GetFiles(logDir, Path.GetFileNameWithoutExtension(_config.LogFileName) + "*"))
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch
            {
                // Retention cleanup is best-effort; do not crash the service.
            }
        }
    }
}
