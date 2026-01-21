using FluentValidation;
using FluentValidation.Results;

namespace IntelliTrader.Core.Validation;

/// <summary>
/// Service for validating configuration objects using FluentValidation.
/// Registers and manages validators for different configuration types.
/// </summary>
public class ConfigValidationService : IConfigValidationService
{
    private readonly Dictionary<Type, object> _validators = new();

    public ConfigValidationService()
    {
        // Register all configuration validators
        RegisterValidator<ITradingConfig>(new TradingConfigValidator());
        RegisterValidator<ICoreConfig>(new CoreConfigValidator());
        RegisterValidator<ISignalsConfig>(new SignalsConfigValidator());
        RegisterValidator<IRulesConfig>(new RulesConfigValidator());
    }

    /// <summary>
    /// Registers a validator for a specific configuration type.
    /// </summary>
    public void RegisterValidator<T>(IValidator<T> validator) where T : class
    {
        _validators[typeof(T)] = validator;
    }

    /// <inheritdoc />
    public void ValidateAndThrow<T>(T config, string sectionName) where T : class
    {
        if (config == null)
        {
            throw new ConfigValidationException(sectionName, new[]
            {
                new ValidationFailure("Config", $"Configuration for '{sectionName}' is null")
            });
        }

        var result = Validate(config);
        if (!result.IsValid)
        {
            throw new ConfigValidationException(sectionName, result.Errors);
        }
    }

    /// <inheritdoc />
    public ValidationResult Validate<T>(T config) where T : class
    {
        if (config == null)
        {
            return new ValidationResult(new[]
            {
                new ValidationFailure("Config", "Configuration object is null")
            });
        }

        var validator = GetValidator<T>();
        if (validator == null)
        {
            // No validator registered, assume valid
            return new ValidationResult();
        }

        return validator.Validate(config);
    }

    /// <inheritdoc />
    public bool IsValid<T>(T config) where T : class
    {
        var result = Validate(config);
        return result.IsValid;
    }

    private IValidator<T>? GetValidator<T>() where T : class
    {
        // Try to get validator for exact type
        if (_validators.TryGetValue(typeof(T), out var validator))
        {
            return validator as IValidator<T>;
        }

        // Try to find validator for interface that T implements
        foreach (var kvp in _validators)
        {
            if (kvp.Key.IsAssignableFrom(typeof(T)))
            {
                // Return the interface validator - it will work with the implementation
                return kvp.Value as IValidator<T>;
            }
        }

        return null;
    }

    /// <summary>
    /// Validates a configuration using a specific interface type.
    /// Useful when the actual object type is unknown at compile time.
    /// </summary>
    internal ValidationResult ValidateAsInterface<TInterface>(object config) where TInterface : class
    {
        if (config == null)
        {
            return new ValidationResult(new[]
            {
                new ValidationFailure("Config", "Configuration object is null")
            });
        }

        if (config is not TInterface typedConfig)
        {
            return new ValidationResult(new[]
            {
                new ValidationFailure("Config", $"Configuration object does not implement {typeof(TInterface).Name}")
            });
        }

        if (!_validators.TryGetValue(typeof(TInterface), out var validatorObj))
        {
            // No validator registered for this interface, assume valid
            return new ValidationResult();
        }

        var validator = validatorObj as IValidator<TInterface>;
        if (validator == null)
        {
            return new ValidationResult();
        }

        return validator.Validate(typedConfig);
    }
}
