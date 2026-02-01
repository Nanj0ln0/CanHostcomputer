namespace CanHostcomputer
{
    public sealed class DriverInfo
    {
        public int VersionRaw { get; init; }
        public int VersionMajor { get; init; }
        public int VersionMinor { get; init; }
        public int NumberOfChannels { get; init; }

        public override string ToString()
        {
            return $"CANlib v{VersionMajor}.{VersionMinor} (raw: {VersionRaw}), channels: {NumberOfChannels}";
        }
    }
}