using ExchangeSharp;
using IntelliTrader.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliTrader.Exchange.Base
{
    internal class SecretRotationService : ISecretRotationService, IDisposable
    {
        private readonly ILoggingService _loggingService;
        private readonly Lazy<IExchangeService> _exchangeService;
        private readonly Lazy<INotificationService> _notificationService;
        private readonly SecretRotationConfig _config;

        private FileSystemWatcher? _fileWatcher;
        private Timer? _debounceTimer;
        private readonly object _rotationLock = new();
        private bool _isRotating;
        private bool _started;

        public SecretRotationService(
            ILoggingService loggingService,
            Lazy<IExchangeService> exchangeService,
            Lazy<INotificationService> notificationService,
            SecretRotationConfig config)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _exchangeService = exchangeService ?? throw new ArgumentNullException(nameof(exchangeService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public void Start()
        {
            if (!_config.Enabled)
            {
                _loggingService.Info("Secret rotation is disabled");
                return;
            }

            var keysPath = Path.GetFullPath(_config.KeysFilePath);
            var directory = Path.GetDirectoryName(keysPath);
            var fileName = Path.GetFileName(keysPath);

            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
            {
                _loggingService.Error($"Invalid keys file path: {_config.KeysFilePath}");
                return;
            }

            if (!Directory.Exists(directory))
            {
                _loggingService.Warning($"Keys directory does not exist, creating: {directory}");
                Directory.CreateDirectory(directory);
            }

            _fileWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };

            _fileWatcher.Changed += OnKeysFileChanged;
            _fileWatcher.Created += OnKeysFileChanged;

            _started = true;
            _loggingService.Info($"Secret rotation service started, watching: {keysPath}");
        }

        public void Stop()
        {
            _started = false;

            if (_fileWatcher != null)
            {
                _fileWatcher.Changed -= OnKeysFileChanged;
                _fileWatcher.Created -= OnKeysFileChanged;
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Dispose();
                _fileWatcher = null;
            }

            _debounceTimer?.Dispose();
            _debounceTimer = null;

            _loggingService.Info("Secret rotation service stopped");
        }

        public async Task<bool> RotateCredentialsAsync()
        {
            lock (_rotationLock)
            {
                if (_isRotating)
                {
                    _loggingService.Warning("Credential rotation already in progress, skipping");
                    return false;
                }
                _isRotating = true;
            }

            try
            {
                var keysPath = Path.GetFullPath(_config.KeysFilePath);
                _loggingService.Info($"Starting credential rotation from: {keysPath}");

                if (!File.Exists(keysPath))
                {
                    _loggingService.Error($"Keys file not found: {keysPath}");
                    await NotifyAsync("Secret rotation FAILED: keys file not found");
                    return false;
                }

                // Step 1: Verify new credentials by creating a temporary API client
                var verified = await VerifyNewCredentialsAsync(keysPath);
                if (!verified)
                {
                    _loggingService.Error("New credentials failed verification, keeping current credentials");
                    await NotifyAsync("Secret rotation FAILED: new credentials did not pass verification. Old credentials retained.");
                    return false;
                }

                // Step 2: Swap credentials in the active exchange service
                var exchangeService = _exchangeService.Value;
                var updated = exchangeService.UpdateCredentials(keysPath);

                if (!updated)
                {
                    _loggingService.Error("Failed to apply new credentials to exchange service");
                    await NotifyAsync("Secret rotation FAILED: could not apply new credentials. Old credentials retained.");
                    return false;
                }

                // Step 3: Verify the live service still works after swap
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_config.VerificationTimeoutSeconds));
                    await exchangeService.GetAvailableAmounts();
                    _loggingService.Info("Credential rotation completed successfully");
                    await NotifyAsync("Secret rotation completed successfully. New API credentials are now active.");
                    return true;
                }
                catch (Exception ex)
                {
                    _loggingService.Error("Post-rotation verification failed, credentials may need manual intervention", ex);
                    await NotifyAsync($"Secret rotation WARNING: credentials were applied but post-swap verification failed: {ex.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _loggingService.Error("Unexpected error during credential rotation", ex);
                await NotifyAsync($"Secret rotation FAILED with unexpected error: {ex.Message}");
                return false;
            }
            finally
            {
                lock (_rotationLock)
                {
                    _isRotating = false;
                }
            }
        }

        private async Task<bool> VerifyNewCredentialsAsync(string keysFilePath)
        {
            try
            {
                _loggingService.Info("Verifying new credentials...");

                var testApi = new ExchangeBinanceAPI();
                testApi.LoadAPIKeys(keysFilePath);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_config.VerificationTimeoutSeconds));

                // Use a lightweight call to verify the credentials work
                var amounts = await testApi.GetAmountsAvailableToTradeAsync();

                _loggingService.Info("New credentials verified successfully");
                return true;
            }
            catch (Exception ex)
            {
                _loggingService.Error("New credential verification failed", ex);
                return false;
            }
        }

        private void OnKeysFileChanged(object sender, FileSystemEventArgs e)
        {
            if (!_started) return;

            _loggingService.Info($"Keys file change detected: {e.FullPath} ({e.ChangeType})");

            // Debounce: file writes can trigger multiple events.
            // Wait 2 seconds after the last change before rotating.
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(
                _ => _ = RotateCredentialsAsync(),
                null,
                TimeSpan.FromSeconds(2),
                Timeout.InfiniteTimeSpan);
        }

        private async Task NotifyAsync(string message)
        {
            try
            {
                await _notificationService.Value.NotifyAsync($"[SecretRotation] {message}");
            }
            catch (Exception ex)
            {
                _loggingService.Error("Failed to send secret rotation notification", ex);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
