namespace IntelliTrader.Core
{
    /// <summary>
    /// Service for recording security-relevant audit events to a dedicated audit log.
    /// Audit entries are written to a separate file from application logs and include
    /// timestamp, user, IP address, action, and details.
    /// </summary>
    public interface IAuditService
    {
        /// <summary>
        /// Logs an audit event.
        /// </summary>
        /// <param name="action">The action being audited (e.g., "Login", "Buy", "ConfigChange").</param>
        /// <param name="details">Human-readable details about the event.</param>
        /// <param name="user">The authenticated user, if available.</param>
        /// <param name="ipAddress">The client IP address, if available.</param>
        void LogAudit(string action, string details, string user = null, string ipAddress = null);
    }
}
