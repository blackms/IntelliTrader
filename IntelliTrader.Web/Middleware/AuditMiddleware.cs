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
    ///   - POST /Buy, /Sell, /Swap -> manual trading operations (with pair/amount details)
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

        // Trading endpoints where we capture form data (pair, amount) for audit detail.
        private static readonly HashSet<string> TradingPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/Buy",
            "/Sell",
            "/Swap"
        };

        // Config endpoints where we capture only the config section name (not the full definition).
        private static readonly HashSet<string> ConfigPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/SaveConfig"
        };

        // Form field names that must never appear in audit logs.
        private static readonly HashSet<string> SensitiveFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "password",
            "passwordhash",
            "secret",
            "apikey",
            "privatekey",
            "token"
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

            // For trading endpoints, capture request details (pair, amount) for richer audit trail.
            var details = await BuildAuditDetailsAsync(context.Request, path);

            _auditService.LogAudit(
                action: action,
                details: details,
                user: user,
                ipAddress: ip);

            await _next(context);
        }

        // --- helpers ---

        /// <summary>
        /// Builds a human-readable audit detail string. For trading endpoints the form body
        /// is read to include pair/amount information. Sensitive fields are filtered out.
        /// </summary>
        private static async Task<string> BuildAuditDetailsAsync(HttpRequest request, string path)
        {
            if ((!IsTradingPath(path) && !IsConfigPath(path)) || !request.HasFormContentType)
            {
                return $"POST {path}";
            }

            try
            {
                // Enable request body buffering so downstream middleware/controllers can still read it.
                request.EnableBuffering();

                var form = await request.ReadFormAsync();

                // For config endpoints, only log the config section name, never the definition (may contain secrets).
                if (IsConfigPath(path))
                {
                    var configName = form.ContainsKey("name") ? form["name"].ToString() : "(unknown)";

                    if (request.Body.CanSeek)
                    {
                        request.Body.Position = 0;
                    }

                    return $"POST {path} config={configName}";
                }

                var safeFields = new List<string>();

                foreach (var field in form)
                {
                    if (SensitiveFields.Contains(field.Key))
                        continue;

                    safeFields.Add($"{field.Key}={field.Value}");
                }

                // Reset the request body position so MVC model binding still works.
                if (request.Body.CanSeek)
                {
                    request.Body.Position = 0;
                }

                var fieldSummary = safeFields.Count > 0
                    ? string.Join(", ", safeFields)
                    : "(no form data)";

                return $"POST {path} [{fieldSummary}]";
            }
            catch
            {
                // If form reading fails for any reason, fall back to basic detail.
                return $"POST {path}";
            }
        }

        private static bool IsAuditedPath(PathString path)
        {
            return path.HasValue && AuditedPaths.Contains(path.Value);
        }

        private static bool IsLoginPath(string path)
        {
            return string.Equals(path, "/Login", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTradingPath(string path)
        {
            return path != null && TradingPaths.Contains(path);
        }

        private static bool IsConfigPath(string path)
        {
            return path != null && ConfigPaths.Contains(path);
        }

        private static string DeriveAction(string path)
        {
            // Strip leading slash and use as action name.
            return path?.TrimStart('/') ?? "Unknown";
        }
    }
}
