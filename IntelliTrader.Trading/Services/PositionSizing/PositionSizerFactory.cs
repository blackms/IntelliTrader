using IntelliTrader.Core;
using System;

namespace IntelliTrader.Trading
{
    /// <summary>
    /// Factory for creating position sizer instances based on configuration.
    /// </summary>
    public interface IPositionSizerFactory
    {
        /// <summary>
        /// Creates a position sizer based on the provided configuration.
        /// </summary>
        /// <param name="config">Position sizing configuration.</param>
        /// <returns>The appropriate position sizer implementation.</returns>
        IPositionSizer Create(PositionSizingConfig config);
    }

    /// <summary>
    /// Default implementation of IPositionSizerFactory.
    /// </summary>
    public class PositionSizerFactory : IPositionSizerFactory
    {
        private readonly ILoggingService _loggingService;

        public PositionSizerFactory(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        public IPositionSizer Create(PositionSizingConfig config)
        {
            if (config == null)
            {
                _loggingService.Debug("Position sizing config is null, using FixedPercent as default");
                return new FixedPercentagePositionSizer(_loggingService);
            }

            switch (config.Type)
            {
                case PositionSizingType.Kelly:
                    _loggingService.Debug($"Creating Kelly Criterion position sizer with fraction {config.KellyFraction}");
                    return new KellyCriterionPositionSizer(_loggingService, config.KellyFraction);

                case PositionSizingType.FixedPercent:
                default:
                    _loggingService.Debug($"Creating Fixed Percentage position sizer with risk {config.RiskPercent}%");
                    return new FixedPercentagePositionSizer(_loggingService);
            }
        }
    }
}
