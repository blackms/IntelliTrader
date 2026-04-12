namespace IntelliTrader.Exchange.Base
{
    public class SecretRotationConfig
    {
        public bool Enabled { get; set; } = false;
        public int VerificationTimeoutSeconds { get; set; } = 30;
        public string KeysFilePath { get; set; } = "keys.bin";
    }
}
