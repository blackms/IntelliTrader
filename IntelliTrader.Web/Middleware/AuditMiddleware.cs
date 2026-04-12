using IntelliTrader.Core;
using Microsoft.AspNetCore.Http;

namespace IntelliTrader.Web.Middleware
{
    /// <summary>
    /// ASP.NET Core middleware that intercepts security-relevant HTTP requests and
    /// writes structured audit log entries via <see cref="IAuditService"/>.
    ///
    /// Audited endpoints:
    ///   - POST /Login           -> authentication attempts (success / failure)
    ///   - POST /Buy, /Sell, /Swap -> manual trading operations
    ///   - POST /SaveConfig, /Settings -> configuration changes
    ///   - POST /RestartServices, /SuspendTrading, /ResumeTrading -> service lifecycle
    /// </summary>
    public sealed class AuditMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IAuditService _auditService;

        // Endpoints that trigger audit logging (case-insensitive comparison below).
        private static readonly HashSet<string> AuditedPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/Login",
            "/Buy",
            "/Sell",
            "/Swap",
            "/SaveConfig",
            "/Settings",
            "/RestartServices",
            "/SuspendTrading",
            "/ResumeTrading"
        };

        public AuditMiddleware(RequestDelegate next, IAuditService auditService)
        {
            _next = next;
            _auditService = auditService;
        }

        public async Task Invoke(HttpContext context)
        {
            // Only audit POST requests to known endpoints.
            if (!HttpMethods.IsPost(context.Request.Method) || !IsAuditedPath(context.Request.Path))
            {
                await _next(context);
                return;
            }

            var path = context.Request.Path.Value;
            var user = context.User?.Identity?.Name;
            var ip = context.Connection.RemoteIpAddress?.ToString();
            var action = DeriveAction(path);

            // For login attempts we need to inspect the response status to determine success/failure.
            if (IsLoginPath(path))
            {
                await _next(context);
                var success = context.Response.StatusCode < 400;
                _auditService.LogAudit(
                    action: success ? "LoginSuccess" : "LoginFailure",
                    details: $"Authentication attempt to {path} resulted in HTTP {context.Response.StatusCode}",
                    user: user,
                    ipAddress: ip);
                return;
            }

            // For all other audited endpoints, log before delegating.
            _auditService.LogAudit(
                action: action,
                details: $"POST {path}",
                user: user,
                ipAddress: ip);

            await _next(context);
        }

        // --- helpers ---

        private static bool IsAuditedPath(PathString path)
        {
            return path.HasValue && AuditedPaths.Contains(path.Value);
        }

        private static bool IsLoginPath(string path)
        {
            return string.Equals(path, "/Login", StringComparison.OrdinalIgnoreCase);
        }

        private static string DeriveAction(string path)
        {
            // Strip leading slash and use as action name.
            return path?.TrimStart('/') ?? "Unknown";
        }
    }
}
