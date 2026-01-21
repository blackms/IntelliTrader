namespace IntelliTrader.Rules.Specifications
{
    /// <summary>
    /// Specification that evaluates volume-related conditions against a signal.
    /// Checks MinVolume, MaxVolume, MinVolumeChange, and MaxVolumeChange.
    /// </summary>
    public class VolumeSpecification : Specification<ConditionContext>
    {
        private readonly long? _minVolume;
        private readonly long? _maxVolume;
        private readonly double? _minVolumeChange;
        private readonly double? _maxVolumeChange;

        public VolumeSpecification(
            long? minVolume,
            long? maxVolume,
            double? minVolumeChange,
            double? maxVolumeChange)
        {
            _minVolume = minVolume;
            _maxVolume = maxVolume;
            _minVolumeChange = minVolumeChange;
            _maxVolumeChange = maxVolumeChange;
        }

        public override bool IsSatisfiedBy(ConditionContext context)
        {
            var signal = context.Signal;

            // MinVolume check
            if (_minVolume != null)
            {
                if (signal == null || signal.Volume == null || signal.Volume < _minVolume)
                {
                    return false;
                }
            }

            // MaxVolume check
            if (_maxVolume != null)
            {
                if (signal == null || signal.Volume == null || signal.Volume > _maxVolume)
                {
                    return false;
                }
            }

            // MinVolumeChange check
            if (_minVolumeChange != null)
            {
                if (signal == null || signal.VolumeChange == null || signal.VolumeChange < _minVolumeChange)
                {
                    return false;
                }
            }

            // MaxVolumeChange check
            if (_maxVolumeChange != null)
            {
                if (signal == null || signal.VolumeChange == null || signal.VolumeChange > _maxVolumeChange)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns true if this specification has any constraints to check.
        /// </summary>
        public bool HasConstraints =>
            _minVolume != null ||
            _maxVolume != null ||
            _minVolumeChange != null ||
            _maxVolumeChange != null;
    }
}
