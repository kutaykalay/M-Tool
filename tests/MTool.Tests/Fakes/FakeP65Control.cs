using System.Collections.Concurrent;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.Tests.Fakes;

/// <summary>
/// Records every call; answers with <see cref="NextStatus"/>. An Applied write also changes
/// <see cref="State"/>, like the EC would. No EC behind it.
/// </summary>
internal sealed class FakeP65Control : IP65Control
{
    public static readonly FirmwareInfo SupportedFirmware = new("16Q4EMS2.107", "05132019");

    public DeviceAccess Access { get; set; } = new(SupportedFirmware, WriteMode.Enabled, null, PortFeaturesAvailable: true, TestLayouts.P65.Capabilities);

    public ControlState State { get; set; } = new(
        FactoryDefaults.FanCurves, PerformanceMode.High, 0xC0, FanMode.Advanced, new PortState(CoolerBoostRaw: 0x02, ChargeLimitRaw: 0xD0),
        new KeyboardBacklightState(Level: 1, Raw: 0x81));

    public SensorSnapshot Sensors { get; set; } = new(60, 46, 50, 0, 3044, 0);

    /// <summary>Status every write returns.</summary>
    public WriteStatus NextStatus { get; set; } = WriteStatus.Applied;

    /// <summary>When set, every write waits for it before answering.</summary>
    public TaskCompletionSource? WriteGate { get; set; }

    public ConcurrentQueue<string> Calls { get; } = new();

    public int WritesRunning;

    public int MaxWritesRunning;

    public Task<SensorSnapshot> ReadSensorsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Sensors);

    /// <summary>When true, <see cref="ReadKeyboardBacklightAsync"/> throws like a silent EC.</summary>
    public bool BacklightReadFails { get; set; }

    /// <summary>When set, the next backlight read answers with this task instead of <see cref="State"/>.</summary>
    public TaskCompletionSource<KeyboardBacklightState?>? BacklightReadResult { get; set; }

    public Task<KeyboardBacklightState?> ReadKeyboardBacklightAsync(CancellationToken cancellationToken = default)
    {
        Calls.Enqueue("read backlight");
        if (BacklightReadResult is { } result)
        {
            BacklightReadResult = null;
            return result.Task;
        }

        return BacklightReadFails
            ? Task.FromException<KeyboardBacklightState?>(new EcAccessException("EC silent"))
            : Task.FromResult(State.KeyboardBacklight);
    }

    /// <summary>The <see cref="PortUse"/> of every state read, in order.</summary>
    public ConcurrentQueue<PortUse> StateReadPortUses { get; } = new();

    /// <summary>When set, the next state read returns the state of its start only once this completes.</summary>
    public TaskCompletionSource? NextReadGate { get; set; }

    /// <summary>The port part of <see cref="State"/>: what a real cache would hold now.</summary>
    public PortState? CachedPortState => State.Port;

    public async Task<ControlState> ReadControlStateAsync(PortUse portUse, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue("read state");
        StateReadPortUses.Enqueue(portUse);
        var state = State;
        if (NextReadGate is { } gate)
        {
            NextReadGate = null;
            await gate.Task;
        }

        return state;
    }

    public Task<WriteOutcome> ApplyFanProfileAsync(FanProfile profile, CancellationToken cancellationToken = default) =>
        WriteAsync($"fan {profile.Name}", s => s with { FanCurves = profile.Curves });

    public Task<WriteOutcome> SetCoolerBoostAsync(bool on, CancellationToken cancellationToken = default) =>
        WriteAsync($"boost {on}", s => s with { Port = s.Port is null ? null : s.Port with { CoolerBoostRaw = on ? (byte)0x82 : (byte)0x02 } });

    public Task<WriteOutcome> SetPerformanceAsync(PerformanceMode mode, CancellationToken cancellationToken = default) =>
        WriteAsync($"performance {mode}", s => s with { Performance = mode, PerformanceRaw = ModeCodes.PerformanceByte(mode) });

    public Task<WriteOutcome> SetChargeLimitAsync(int percent, CancellationToken cancellationToken = default) =>
        WriteAsync($"charge {percent}", s => s with { Port = s.Port is null ? null : s.Port with { ChargeLimitRaw = ModeCodes.ChargeLimitByte(percent) } });

    public Task<WriteOutcome> SetFanModeAsync(FanMode mode, CancellationToken cancellationToken = default) =>
        WriteAsync($"fan mode {mode}", s => s with { FanMode = mode });

    public Task<WriteOutcome> SetKeyboardBacklightAsync(int level, CancellationToken cancellationToken = default) =>
        WriteAsync($"keyboard backlight {level}", s => s with { KeyboardBacklight = new(level, ModeCodes.KeyboardBacklightByte(level)) });

    public async Task<IReadOnlyList<WriteOutcome>> ApplyDesiredAsync(
        DesiredState desired, ProfileCatalog catalog, CancellationToken cancellationToken = default) =>
        [await WriteAsync($"desired {desired}", s => s with { FanCurves = catalog.Find(desired.FanProfile)!.Curves })];

    private async Task<WriteOutcome> WriteAsync(string call, Func<ControlState, ControlState> effect)
    {
        Calls.Enqueue(call);
        var running = Interlocked.Increment(ref WritesRunning);
        InterlockedMax(ref MaxWritesRunning, running);
        try
        {
            if (WriteGate is { } gate)
            {
                await gate.Task;
            }

            if (NextStatus == WriteStatus.Applied)
            {
                State = effect(State);
            }

            return new WriteOutcome(NextStatus, [], $"{NextStatus}: {call}");
        }
        finally
        {
            Interlocked.Decrement(ref WritesRunning);
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
