namespace IntelliTrader.Core
{
    /// <summary>
    /// Provides application-wide runtime context.
    /// </summary>
    public class ApplicationContext : IApplicationContext
    {
        /// <inheritdoc/>
        public double Speed { get; set; } = 1;
    }
}
