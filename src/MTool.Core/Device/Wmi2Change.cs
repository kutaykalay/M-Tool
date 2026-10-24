using MTool.Core.Ec;

namespace MTool.Core.Device;

/// <summary>
/// A WMI2 experimental change as the user asks for it: a feature and its logical value. The byte is
/// worked out by <see cref="Wmi2WriteRules"/> in Core, under its checks; the UI never builds one.
/// The cases are closed: no other feature can be asked for.
/// </summary>
public abstract record Wmi2Change
{
    private Wmi2Change()
    {
    }

    /// <summary>The ceiling register the change writes.</summary>
    public abstract byte Register { get; }

    public sealed record CoolerBoost(bool On) : Wmi2Change
    {
        public override byte Register => Wmi2WriteCeiling.CoolerBoostRegister;
    }

    public sealed record Performance(PerformanceMode Mode) : Wmi2Change
    {
        public override byte Register => Wmi2WriteCeiling.PerformanceRegister;
    }

    public sealed record FanMode(Wmi2FanMode Mode) : Wmi2Change
    {
        public override byte Register => Wmi2WriteCeiling.FanModeRegister;
    }

    public sealed record ChargeLimit(int Percent) : Wmi2Change
    {
        public override byte Register => Wmi2WriteCeiling.ChargeLimitRegister;
    }
}
