using FluentValidation;

namespace IntelliTrader.Core.Validation;

/// <summary>
/// Validates core application configuration (core.json).
/// Ensures health check and general application settings are valid.
/// </summary>
public class CoreConfigValidator : AbstractValidator<ICoreConfig>
{
    private const double MinHealthCheckInterval = 10;       // Minimum 10 seconds
    private const double MaxHealthCheckInterval = 3600;     // Maximum 1 hour
    private const double MaxTimezoneOffset = 14;            // UTC+14 is max
    private const double MinTimezoneOffset = -12;           // UTC-12 is min

    public CoreConfigValidator()
    {
        // Instance name validation
        RuleFor(x => x.InstanceName)
            .NotEmpty()
            .WithMessage("InstanceName is required for identifying this trading instance")
            .MaximumLength(50)
            .WithMessage("InstanceName should not exceed 50 characters. Current length: {TotalLength}")
            .Matches(@"^[a-zA-Z0-9_\-\s]+$")
            .When(x => !string.IsNullOrEmpty(x.InstanceName))
            .WithMessage("InstanceName should only contain letters, numbers, underscores, hyphens, and spaces");

        // Timezone validation
        RuleFor(x => x.TimezoneOffset)
            .InclusiveBetween(MinTimezoneOffset, MaxTimezoneOffset)
            .WithMessage($"TimezoneOffset must be between {MinTimezoneOffset} and {MaxTimezoneOffset} (UTC offset in hours). Current value: {{PropertyValue}}");

        // Password protection validation
        When(x => x.PasswordProtected, () =>
        {
            RuleFor(x => x.Password)
                .NotEmpty()
                .WithMessage("Password is required when PasswordProtected is enabled")
                .MinimumLength(8)
                .When(x => !string.IsNullOrEmpty(x.Password) && !IsHashedPassword(x.Password))
                .WithMessage("Password must be at least 8 characters long (or provide a pre-hashed password)");
        });

        // Health check configuration
        ConfigureHealthCheckValidation();
    }

    private void ConfigureHealthCheckValidation()
    {
        When(x => x.HealthCheckEnabled, () =>
        {
            RuleFor(x => x.HealthCheckInterval)
                .GreaterThanOrEqualTo(MinHealthCheckInterval)
                .WithMessage($"HealthCheckInterval must be at least {MinHealthCheckInterval} seconds. Current value: {{PropertyValue}}")
                .LessThanOrEqualTo(MaxHealthCheckInterval)
                .WithMessage($"HealthCheckInterval should not exceed {MaxHealthCheckInterval} seconds. Current value: {{PropertyValue}}");

            RuleFor(x => x.HealthCheckSuspendTradingTimeout)
                .GreaterThan(0)
                .WithMessage("HealthCheckSuspendTradingTimeout must be positive when health checks are enabled. Current value: {PropertyValue}")
                .GreaterThan(x => x.HealthCheckInterval)
                .WithMessage("HealthCheckSuspendTradingTimeout should be greater than HealthCheckInterval to allow for retries");

            RuleFor(x => x.HealthCheckFailuresToRestartServices)
                .GreaterThan(0)
                .WithMessage("HealthCheckFailuresToRestartServices must be at least 1. Current value: {PropertyValue}")
                .LessThanOrEqualTo(10)
                .WithMessage("HealthCheckFailuresToRestartServices should not exceed 10 to avoid prolonged unhealthy states. Current value: {PropertyValue}");
        });
    }

    /// <summary>
    /// Checks if a password appears to be an MD5 hash (32 hex characters).
    /// </summary>
    private static bool IsHashedPassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return false;

        // MD5 hash is 32 hex characters
        return password.Length == 32 &&
               password.All(c => char.IsLetterOrDigit(c) && (char.IsDigit(c) || char.ToLower(c) >= 'a' && char.ToLower(c) <= 'f'));
    }
}
