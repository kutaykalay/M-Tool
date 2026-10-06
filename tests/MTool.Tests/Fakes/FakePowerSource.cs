using MTool.Core.Power;

namespace MTool.Tests.Fakes;

/// <summary>A power source set by hand; <see cref="Set"/> raises <see cref="Changed"/> like Windows would.</summary>
internal sealed class FakePowerSource : IPowerSource
{
    public PowerSource? Current { get; set; } = PowerSource.Ac;

    public event Action? Changed;

    public bool HasSubscribers => Changed is not null;

    public void Set(PowerSource? source)
    {
        Current = source;
        Changed?.Invoke();
    }
}
