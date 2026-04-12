using IntelliTrader.Core.Exceptions;
using IntelliTrader.Core.Security;
using IntelliTrader.Core.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Concurrent;
using System.IO;

namespace IntelliTrader.Core
{
    internal class ConfigProvider : IConfigProvider
    {
        private const string ROOT_CONFIG_DIR = "config";
        private const string PATHS_CONFIG_PATH = "paths.json";
        private const string PATHS_SECTION_NAME = "Paths";
        private IConfigurationSection paths;

        // Logging service is injected lazily to avoid bootstrap issues
        // since ConfigProvider is instantiated before the DI container is built
        private Func<ILoggingService> loggingServiceFactory;

        // Validation service for configuration validation
        private readonly IConfigValidationService _validationService;

        // Flag to enable/disable validation (can be toggled for testing or specific scenarios)
        private bool _validationEnabled = true;

        // Tracks which config files were originally encrypted so we re-encrypt on save
        private readonly ConcurrentDictionary<string, bool> _encryptedFiles = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private const string MasterPasswordEnvVar = "INTELLITRADER_MASTER_PASSWORD";

        public ConfigProvider() : this(new ConfigValidationService())
        {
        }

        public ConfigProvider(IConfigValidationService validationService)
        {
            _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));

            IConfigurationRoot pathsConfig = GetConfig(PATHS_CONFIG_PATH, changedPathsConfig =>
            {
                paths = changedPathsConfig.GetSection(PATHS_SECTION_NAME);
            });
            paths = pathsConfig.GetSection(PATHS_SECTION_NAME);
        }

        /// <summary>
        /// Enables or disables configuration validation.
        /// When disabled, configurations are loaded without validation checks.
        /// </summary>
        public void SetValidationEnabled(bool enabled)
        {
            _validationEnabled = enabled;
        }

        /// <summary>
        /// Sets the logging service factory for deferred logging.
        /// Call this after the DI container is built.
        /// </summary>
        public void SetLoggingServiceFactory(Func<ILoggingService> factory)
        {
            loggingServiceFactory = factory;
        }

        private void LogError(string message, Exception ex = null)
        {
            // Try to use the injected logging service if available
            var loggingService = loggingServiceFactory?.Invoke();
            if (loggingService != null)
            {
                loggingService.Error(message, ex);
            }
            else
            {
                // Fallback to console for bootstrap errors (before logging is available)
                Console.Error.WriteLine($"[CONFIG ERROR] {message}");
                if (ex != null)
                {
                    Console.Error.WriteLine($"[CONFIG ERROR] Exception: {ex}");
                }
            }
        }

        public string GetSectionJson(string sectionName)
        {
            string configPath = null;
            string fullConfigPath = null;

            try
            {
                configPath = paths.GetValue<string>(sectionName);
                if (string.IsNullOrEmpty(configPath))
                {
                    LogError($"Configuration path not found for section '{sectionName}' in paths.json");
                    return null;
                }

                fullConfigPath = Path.Combine(Directory.GetCurrentDirectory(), ROOT_CONFIG_DIR, configPath);
                string content = File.ReadAllText(fullConfigPath);
                return DecryptIfNeeded(content, fullConfigPath);
            }
            catch (FileNotFoundException ex)
            {
                LogError($"Configuration file not found for section '{sectionName}' at path: {fullConfigPath ?? configPath}", ex);
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                LogError($"Access denied reading configuration file for section '{sectionName}' at path: {fullConfigPath ?? configPath}", ex);
                return null;
            }
            catch (IOException ex)
            {
                LogError($"I/O error reading configuration file for section '{sectionName}' at path: {fullConfigPath ?? configPath}", ex);
                return null;
            }
            catch (Exception ex)
            {
                LogError($"Unexpected error loading config section '{sectionName}'. Path: {fullConfigPath ?? configPath ?? "unknown"}", ex);
                return null;
            }
        }

        public void SetSectionJson(string sectionName, string definition)
        {
            string configPath = null;
            string fullConfigPath = null;

            try
            {
                configPath = paths.GetValue<string>(sectionName);
                if (string.IsNullOrEmpty(configPath))
                {
                    LogError($"Configuration path not found for section '{sectionName}' in paths.json. Cannot save configuration.");
                    return;
                }

                fullConfigPath = Path.Combine(Directory.GetCurrentDirectory(), ROOT_CONFIG_DIR, configPath);
                string contentToWrite = EncryptIfNeeded(definition, fullConfigPath);
                File.WriteAllText(fullConfigPath, contentToWrite);
            }
            catch (UnauthorizedAccessException ex)
            {
                LogError($"Access denied writing configuration file for section '{sectionName}' at path: {fullConfigPath ?? configPath}", ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                LogError($"Directory not found when saving configuration for section '{sectionName}' at path: {fullConfigPath ?? configPath}", ex);
            }
            catch (IOException ex)
            {
                LogError($"I/O error writing configuration file for section '{sectionName}' at path: {fullConfigPath ?? configPath}", ex);
            }
            catch (Exception ex)
            {
                LogError($"Unexpected error saving config section '{sectionName}'. Path: {fullConfigPath ?? configPath ?? "unknown"}", ex);
            }
        }

        public T GetSection<T>(string sectionName, Action<T> onChange = null) where T : class
        {
            IConfigurationSection configSection = GetSection(sectionName, changedConfigSection =>
            {
                var changedConfig = changedConfigSection.Get<T>();
                ValidateConfig(changedConfig, sectionName);
                onChange?.Invoke(changedConfig);
            });

            var config = configSection.Get<T>();
            ValidateConfig(config, sectionName);
            return config;
        }

        /// <summary>
        /// Validates a configuration object if validation is enabled.
        /// </summary>
        /// <typeparam name="T">The configuration type.</typeparam>
        /// <param name="config">The configuration to validate.</param>
        /// <param name="sectionName">The section name for error messages.</param>
        /// <exception cref="ConfigValidationException">Thrown when validation fails.</exception>
        private void ValidateConfig<T>(T config, string sectionName) where T : class
        {
            if (!_validationEnabled || config == null)
                return;

            try
            {
                _validationService.ValidateAndThrow(config, sectionName);
            }
            catch (ConfigValidationException ex)
            {
                LogError($"Configuration validation failed for section '{sectionName}':\n{ex.Message}");
                throw;
            }
        }

        public IConfigurationSection GetSection(string sectionName, Action<IConfigurationSection> onChange = null)
        {
            string configPath = paths.GetValue<string>(sectionName);
            IConfigurationRoot configRoot = GetConfig(configPath, changedConfigRoot =>
            {
                onChange?.Invoke(changedConfigRoot.GetSection(sectionName));
            });
            return configRoot.GetSection(sectionName);
        }

        private IConfigurationRoot GetConfig(string configPath, Action<IConfigurationRoot> onChange)
        {
            var fullConfigDir = Path.Combine(Directory.GetCurrentDirectory(), ROOT_CONFIG_DIR);
            var fullFilePath = Path.Combine(fullConfigDir, configPath);

            // Check if the file is encrypted; if so, decrypt to a temp file
            // so the JSON configuration provider can parse it.
            string effectivePath = configPath;
            string effectiveBaseDir = fullConfigDir;

            if (File.Exists(fullFilePath))
            {
                string raw = File.ReadAllText(fullFilePath);
                if (ConfigEncryption.IsEncrypted(raw))
                {
                    _encryptedFiles[fullFilePath] = true;
                    string masterPassword = Environment.GetEnvironmentVariable(MasterPasswordEnvVar);
                    if (string.IsNullOrEmpty(masterPassword))
                    {
                        LogError($"Config file '{configPath}' is encrypted but {MasterPasswordEnvVar} is not set. Cannot decrypt.");
                    }
                    else
                    {
                        try
                        {
                            string decrypted = ConfigEncryption.Decrypt(raw, masterPassword);
                            // Write a decrypted temporary file that the JSON provider can read.
                            // Place it in the system temp directory so we don't pollute the config folder.
                            string tempFile = Path.Combine(Path.GetTempPath(), $"intellitrader_cfg_{Path.GetFileName(configPath)}");
                            File.WriteAllText(tempFile, decrypted);
                            effectivePath = Path.GetFileName(tempFile);
                            effectiveBaseDir = Path.GetDirectoryName(tempFile);
                        }
                        catch (Exception ex)
                        {
                            LogError($"Failed to decrypt config file '{configPath}'.", ex);
                        }
                    }
                }
            }

            var configBuilder = new ConfigurationBuilder()
                 .SetBasePath(effectiveBaseDir)
                 .AddJsonFile(effectivePath, optional: false, reloadOnChange: true)
                 .AddEnvironmentVariables(prefix: "INTELLITRADER_");

            var configRoot = configBuilder.Build();
            ChangeToken.OnChange(configRoot.GetReloadToken, () => onChange(configRoot));
            return configRoot;
        }

        /// <summary>
        /// Decrypts content if it starts with the ENC: prefix.
        /// Returns plaintext JSON on success, or the original content on failure / if not encrypted.
        /// </summary>
        private string DecryptIfNeeded(string content, string filePath)
        {
            if (!ConfigEncryption.IsEncrypted(content))
                return content;

            _encryptedFiles[filePath] = true;

            string masterPassword = Environment.GetEnvironmentVariable(MasterPasswordEnvVar);
            if (string.IsNullOrEmpty(masterPassword))
            {
                LogError($"Config file '{filePath}' is encrypted but {MasterPasswordEnvVar} is not set. Cannot decrypt.");
                return null;
            }

            try
            {
                return ConfigEncryption.Decrypt(content, masterPassword);
            }
            catch (Exception ex)
            {
                LogError($"Failed to decrypt config file '{filePath}'.", ex);
                return null;
            }
        }

        /// <summary>
        /// Re-encrypts content if the file was originally loaded as encrypted.
        /// </summary>
        private string EncryptIfNeeded(string content, string filePath)
        {
            if (!_encryptedFiles.TryGetValue(filePath, out bool wasEncrypted) || !wasEncrypted)
                return content;

            string masterPassword = Environment.GetEnvironmentVariable(MasterPasswordEnvVar);
            if (string.IsNullOrEmpty(masterPassword))
            {
                LogError($"Cannot re-encrypt config file '{filePath}': {MasterPasswordEnvVar} is not set. Saving as plaintext.");
                return content;
            }

            try
            {
                return ConfigEncryption.Encrypt(content, masterPassword);
            }
            catch (Exception ex)
            {
                LogError($"Failed to re-encrypt config file '{filePath}'. Saving as plaintext.", ex);
                return content;
            }
        }
    }
}
