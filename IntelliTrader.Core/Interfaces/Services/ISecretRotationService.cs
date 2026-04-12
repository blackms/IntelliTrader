using System;
using System.Threading.Tasks;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Service for automatic rotation of API credentials and secrets.
    /// </summary>
    public interface ISecretRotationService
    {
        /// <summary>
        /// Starts the secret rotation monitoring service.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops the secret rotation monitoring service.
        /// </summary>
        void Stop();

        /// <summary>
        /// Attempts to rotate the exchange API credentials.
        /// </summary>
        /// <returns>True if rotation was successful; false otherwise.</returns>
        Task<bool> RotateCredentialsAsync();
    }
}
