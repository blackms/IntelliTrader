namespace IntelliTrader.Core
{
    /// <summary>
    /// Provides application-wide runtime context.
    /// Replaces the static Application.Speed property.
    /// </summary>
    public class ApplicationContext : IApplicationContext
    {
        /// <inheritdoc/>
        public double Speed { get; set; } = 1;
    }
}
