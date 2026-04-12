namespace IntelliTrader.Core
{
    /// <summary>
    /// Configuration for the audit logging subsystem.
    /// Loaded from the "Audit" section in core.json.
    /// </summary>
    public class AuditConfig
    {
        /// <summary>
        /// Whether audit logging is enabled. Default: true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Directory where audit log files are written.
        /// Relative paths are resolved from the application working directory.
        /// Default: "log".
        /// </summary>
        public string LogDirectory { get; set; } = "log";

        /// <summary>
        /// Base file name for the audit log. Default: "audit.log".
        /// </summary>
        public string LogFileName { get; set; } = "audit.log";

        /// <summary>
        /// Number of days to retain audit log files. Files older than this are deleted
        /// on service startup and at midnight. Default: 90.
        /// </summary>
        public int RetentionDays { get; set; } = 90;

        /// <summary>
        /// Maximum size in megabytes for a single audit log file before rolling over.
        /// Default: 50.
        /// </summary>
        public int MaxFileSizeMB { get; set; } = 50;
    }
}
