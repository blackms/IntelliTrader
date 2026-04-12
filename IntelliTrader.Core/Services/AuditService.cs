using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Writes structured audit log entries to a dedicated file, separate from application logs.
    /// Thread-safe; entries are flushed immediately so nothing is lost on crash.
    /// Supports configurable retention and file-size rolling.
    ///
    /// Each entry includes a SHA-256 hash chain: the IntegrityHash field is computed from
    /// the previous entry's hash concatenated with the current entry's content. Any tampering
    /// (modification or deletion of entries) breaks the chain and is detectable by walking
    /// the log file and recomputing hashes.
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

        /// <summary>
        /// The hash of the most recent audit entry. Used to build the hash chain.
        /// Initialized to a zero hash; on startup the last entry's hash is recovered from disk.
        /// </summary>
        private string _previousHash;

        public AuditService(AuditConfig config)
        {
            _config = config ?? new AuditConfig();

            var logDir = Path.IsPathRooted(_config.LogDirectory)
                ? _config.LogDirectory
                : Path.Combine(Directory.GetCurrentDirectory(), _config.LogDirectory);

            Directory.CreateDirectory(logDir);

            _logFilePath = Path.Combine(logDir, _config.LogFileName);

            // Seed the hash chain: recover the last IntegrityHash from the existing log,
            // or start with a zero hash if the file is empty / missing.
            _previousHash = RecoverLastHash(_logFilePath);

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

            lock (_syncRoot)
            {
                if (_disposed) return;

                var timestamp = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                var actionVal = action ?? string.Empty;
                var detailsVal = details ?? string.Empty;
                var userVal = user ?? "anonymous";
                var ipVal = ipAddress ?? "unknown";

                // Compute integrity hash: SHA256(previousHash + timestamp + action + details + user + ip)
                var integrityHash = ComputeHash(_previousHash, timestamp, actionVal, detailsVal, userVal, ipVal);

                var entry = new
                {
                    Timestamp = timestamp,
                    Action = actionVal,
                    Details = detailsVal,
                    User = userVal,
                    IpAddress = ipVal,
                    IntegrityHash = integrityHash
                };

                var json = JsonSerializer.Serialize(entry, JsonOptions);

                RollFileIfNeeded();
                _writer.WriteLine(json);
                _writer.Flush();

                _previousHash = integrityHash;
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

        /// <summary>
        /// Computes a SHA-256 hash over the concatenation of the previous hash and the
        /// current entry fields, forming a tamper-evident hash chain.
        /// </summary>
        private static string ComputeHash(string previousHash, string timestamp, string action, string details, string user, string ip)
        {
            var payload = $"{previousHash}|{timestamp}|{action}|{details}|{user}|{ip}";
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        /// <summary>
        /// Reads the last line of an existing audit log and extracts its IntegrityHash
        /// to continue the hash chain after a restart. Returns a zero hash if the file
        /// does not exist or cannot be parsed.
        /// </summary>
        private static string RecoverLastHash(string logFilePath)
        {
            const string zeroHash = "0000000000000000000000000000000000000000000000000000000000000000";
            try
            {
                if (!File.Exists(logFilePath))
                    return zeroHash;

                // Read the last non-empty line.
                string lastLine = null;
                foreach (var line in File.ReadLines(logFilePath, Encoding.UTF8))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        lastLine = line;
                }

                if (lastLine == null)
                    return zeroHash;

                using var doc = JsonDocument.Parse(lastLine);
                if (doc.RootElement.TryGetProperty("IntegrityHash", out var hashProp))
                {
                    var hash = hashProp.GetString();
                    if (!string.IsNullOrEmpty(hash))
                        return hash;
                }
            }
            catch
            {
                // If recovery fails, start a new chain. The break in continuity is
                // itself an indicator that the log may have been tampered with.
            }

            return zeroHash;
        }
    }
}
