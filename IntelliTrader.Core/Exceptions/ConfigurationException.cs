using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace IntelliTrader.Core.Exceptions
{
    /// <summary>
    /// Exception thrown when configuration is invalid or cannot be loaded.
    /// </summary>
    [Serializable]
    public class ConfigurationException : IntelliTraderException
    {
        private const string ErrorCodeValue = "CONFIGURATION_ERROR";

        /// <summary>
        /// The configuration section name.
        /// </summary>
        public string SectionName { get; }

        /// <summary>
        /// The configuration file path if applicable.
        /// </summary>
        public string? FilePath { get; }

        /// <summary>
        /// The specific configuration key that caused the error (if applicable).
        /// </summary>
        public string? ConfigKey { get; }

        /// <summary>
        /// The type of configuration error.
        /// </summary>
        public ConfigurationErrorType ErrorType { get; }

        public ConfigurationException(string sectionName, string message)
            : this(sectionName, null, null, ConfigurationErrorType.Unknown, message, null)
        {
        }

        public ConfigurationException(string sectionName, string message, Exception innerException)
            : this(sectionName, null, null, ConfigurationErrorType.Unknown, message, innerException)
        {
        }

        public ConfigurationException(string sectionName, ConfigurationErrorType errorType, string message)
            : this(sectionName, null, null, errorType, message, null)
        {
        }

        public ConfigurationException(
            string sectionName,
            string? filePath,
            string? configKey,
            ConfigurationErrorType errorType,
            string message,
            Exception? innerException = null)
            : base(
                CreateMessage(sectionName, errorType, message),
                ErrorCodeValue,
                CreateContext(sectionName, filePath, configKey, errorType),
                innerException)
        {
            SectionName = sectionName;
            FilePath = filePath;
            ConfigKey = configKey;
            ErrorType = errorType;
        }

        protected ConfigurationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            SectionName = info.GetString(nameof(SectionName)) ?? "UNKNOWN";
            FilePath = info.GetString(nameof(FilePath));
            ConfigKey = info.GetString(nameof(ConfigKey));
            ErrorType = (ConfigurationErrorType)info.GetInt32(nameof(ErrorType));
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(SectionName), SectionName);
            info.AddValue(nameof(FilePath), FilePath);
            info.AddValue(nameof(ConfigKey), ConfigKey);
            info.AddValue(nameof(ErrorType), (int)ErrorType);
        }

        private static string CreateMessage(string sectionName, ConfigurationErrorType errorType, string details)
        {
            return errorType switch
            {
                ConfigurationErrorType.FileNotFound => $"Configuration file not found for section '{sectionName}': {details}",
                ConfigurationErrorType.ParseError => $"Failed to parse configuration section '{sectionName}': {details}",
                ConfigurationErrorType.ValidationError => $"Configuration validation failed for section '{sectionName}': {details}",
                ConfigurationErrorType.MissingRequired => $"Required configuration missing in section '{sectionName}': {details}",
                ConfigurationErrorType.InvalidValue => $"Invalid configuration value in section '{sectionName}': {details}",
                ConfigurationErrorType.WriteError => $"Failed to write configuration section '{sectionName}': {details}",
                _ => $"Configuration error in section '{sectionName}': {details}"
            };
        }

        private static Dictionary<string, object> CreateContext(
            string sectionName,
            string? filePath,
            string? configKey,
            ConfigurationErrorType errorType)
        {
            var context = new Dictionary<string, object>
            {
                ["SectionName"] = sectionName,
                ["ErrorType"] = errorType.ToString()
            };

            if (!string.IsNullOrEmpty(filePath))
                context["FilePath"] = filePath;

            if (!string.IsNullOrEmpty(configKey))
                context["ConfigKey"] = configKey;

            return context;
        }
    }

    /// <summary>
    /// Types of configuration errors.
    /// </summary>
    public enum ConfigurationErrorType
    {
        Unknown = 0,
        FileNotFound = 1,
        ParseError = 2,
        ValidationError = 3,
        MissingRequired = 4,
        InvalidValue = 5,
        WriteError = 6
    }
}
