using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Settings;

namespace MTool.Tests.Fakes;

/// <summary>
/// Scripted experimental control: reads come from <see cref="Reads"/>, every write call is recorded in order and
/// answered from the scripted outcomes. Never touches WMI or a gateway.
/// </summary>
internal sealed class FakeWmi2Control : IWmi2Control
{
    public FakeWmi2LiveSource Reads { get; } = new() { Firmware = "15M3EMS1.105" };

    public Wmi2LayoutResult Layout { get; set; } = new([]);

    public bool IsWritePathTested { get; set; }

    /// <summary>"test", "apply &lt;change&gt;" or "revert", in call order.</summary>
    public List<string> Calls { get; } = [];

    public Wmi2WriteOutcome TestOutcome { get; set; } = new(WriteStatus.Applied, Wmi2WriteCeiling.PerformanceRegister, 0xC1, new RegisterWrite(0xD2, 0xC1));

    /// <summary>The answer to a change; by default applied and read back.</summary>
    public Func<Wmi2Change, Wmi2WriteOutcome> ApplyOutcome { get; set; } =
        change => new Wmi2WriteOutcome(WriteStatus.Applied, change.Register, 0x00, new RegisterWrite(change.Register, 0x01));

    public IReadOnlyList<Wmi2WriteOutcome> RevertOutcomes { get; set; } = Wmi2WriteCeiling.Registers
        .Select(r => new Wmi2WriteOutcome(WriteStatus.Applied, r, 0x00)).ToArray();

    /// <summary>Thrown by every write call while set.</summary>
    public Exception? Error { get; set; }

    public bool Disposed { get; private set; }

    public Task<string?> ReadFirmwareAsync(CancellationToken cancellationToken) => Reads.ReadFirmwareAsync(cancellationToken);

    public Task<Wmi2Status> ReadStatusAsync(CancellationToken cancellationToken) => Reads.ReadStatusAsync(cancellationToken);

    public Task<SensorSnapshot> ReadSensorsAsync(CancellationToken cancellationToken) => Reads.ReadSensorsAsync(cancellationToken);

    public Task<Wmi2WriteOutcome> TestWritePathAsync(CancellationToken cancellationToken)
    {
        Calls.Add("test");
        if (Error is null && TestOutcome.Status == WriteStatus.Applied)
        {
            IsWritePathTested = true;
        }

        return Answer(TestOutcome);
    }

    /// <summary>When set, a change waits for it, like a write that takes long.</summary>
    public TaskCompletionSource? HoldApply { get; set; }

    public async Task<Wmi2WriteOutcome> ApplyAsync(Wmi2Change change, CancellationToken cancellationToken)
    {
        Calls.Add($"apply {change}");
        if (HoldApply is { } hold)
        {
            await hold.Task;
        }

        return await Answer(Error is null ? ApplyOutcome(change) : null!);
    }

    public Task<IReadOnlyList<Wmi2WriteOutcome>> RevertAsync(CancellationToken cancellationToken)
    {
        Calls.Add("revert");
        return Answer(RevertOutcomes);
    }

    public void Dispose() => Disposed = true;

    private Task<T> Answer<T>(T value) => Error is { } error ? Task.FromException<T>(error) : Task.FromResult(value);
}

/// <summary>The consent in memory; a save can be made to fail.</summary>
internal sealed class FakeWmi2Consent : IWmi2Consent
{
    public Wmi2ExperimentalConsent? Current { get; set; }

    /// <summary>Every consent asked to be saved, failed or not.</summary>
    public List<Wmi2ExperimentalConsent?> Saves { get; } = [];

    public Exception? Error { get; set; }

    public void Save(Wmi2ExperimentalConsent? consent)
    {
        Saves.Add(consent);
        if (Error is { } error)
        {
            throw error;
        }

        Current = consent;
    }
}
