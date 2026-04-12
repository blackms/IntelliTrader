using System;
using System.Threading.Tasks;

namespace IntelliTrader.Core
{
    public interface ISecretRotationService
    {
        void Start();
        void Stop();
        Task<bool> RotateCredentialsAsync();
    }
}
