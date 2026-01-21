using FluentValidation.Results;

namespace IntelliTrader.Core.Validation;

/// <summary>
/// Exception thrown when configuration validation fails.
/// Provides detailed information about all validation errors.
/// </summary>
public class ConfigValidationException : Exception
{
    public IReadOnlyList<ValidationFailure> Errors { get; }
    public string ConfigSection { get; }

    public ConfigValidationException(string configSection, IEnumerable<ValidationFailure> errors)
        : base(FormatMessage(configSection, errors))
    {
        ConfigSection = configSection;
        Errors = errors.ToList().AsReadOnly();
    }

    private static string FormatMessage(string configSection, IEnumerable<ValidationFailure> errors)
    {
        var errorMessages = errors.Select(e => $"  - {e.PropertyName}: {e.ErrorMessage}");
        return $"Configuration validation failed for '{configSection}':\n{string.Join("\n", errorMessages)}";
    }
}
