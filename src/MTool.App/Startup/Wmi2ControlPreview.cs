#if DEBUG
using MTool.App.Hardware;
using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Settings;

namespace MTool.App.Startup;

/// <summary>How the made-up experimental control answers.</summary>
internal enum Wmi2ControlPreviewKind
{
    Works,
    Consented,
    DryRun,
    Fail,
    Layout,
    Unavailable,
}

/// <summary>
/// Debug builds only, next to <see cref="Wmi2Preview"/>: experimental control in the preview window, so its look
/// (buttons, band, questions, results) can be checked on any laptop. MTOOL_WMI2_CONTROL picks how it answers.
/// Nothing reaches the EC or <c>settings.json</c>: writes change a status held in memory, and the consent is kept
/// in memory too. Release builds have no such type.
/// </summary>
internal static class Wmi2ControlPreview
{
    public const string Variable = "MTOOL_WMI2_CONTROL";
    private const string Firmware = "15M1IMS2.105"; // the firmware Wmi2Preview answers: an exact list entry

    public static Wmi2ControlPreviewKind Current => Parse(Environment.GetEnvironmentVariable(Variable));

    /// <summary>"consented", "dry", "fail", "layout", "none"; anything else is a control that works.</summary>
    public static Wmi2ControlPreviewKind Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "consented" => Wmi2ControlPreviewKind.Consented,
        "dry" => Wmi2ControlPreviewKind.DryRun,
        "fail" => Wmi2ControlPreviewKind.Fail,
        "layout" => Wmi2ControlPreviewKind.Layout,
        "none" => Wmi2ControlPreviewKind.Unavailable,
        _ => Wmi2ControlPreviewKind.Works,
    };

    /// <summary>A consent in memory; "consented" starts with one, so control opens by itself after the first round.</summary>
    public static IWmi2Consent Consent(Wmi2ControlPreviewKind kind) => new MemoryConsent(
        kind == Wmi2ControlPreviewKind.Consented ? new Wmi2ExperimentalConsent(Firmware, DateTimeOffset.UtcNow) : null);

    public static Task<Wmi2ControlOpening> OpenAsync(Wmi2ControlPreviewKind kind, CancellationToken cancellationToken) =>
        Task.FromResult(kind == Wmi2ControlPreviewKind.Unavailable
            ? Wmi2ControlOpening.NotOpened(Wmi2ControlUnavailable.FirmwareUnreadable)
            : Wmi2ControlOpening.Opened(new Control(kind)));

    private sealed class MemoryConsent(Wmi2ExperimentalConsent? consent) : IWmi2Consent
    {
        public Wmi2ExperimentalConsent? Current { get; private set; } = consent;

        public void Save(Wmi2ExperimentalConsent? consent) => Current = consent;
    }

    /// <summary>Holds the four status bytes; a write that works changes them, the next round reads them.</summary>
    private sealed class Control : IWmi2Control
    {
        private const byte CoolerBoostBit = 0x80;
        private const byte ChargeLimitOn = 0x80;
        private const byte OutsideLayout = 0x00; // no performance byte of any family

        private readonly Wmi2ControlPreviewKind _kind;
        private readonly Lock _gate = new();
        private Wmi2Status _status;
        private int _round;

        public Control(Wmi2ControlPreviewKind kind)
        {
            _kind = kind;
            _status = new Wmi2Status(0x00, kind == Wmi2ControlPreviewKind.Layout ? OutsideLayout : (byte)0xC1, 0x0D, 0xD0);
            Layout = Wmi2LayoutCheck.Check(_status);
        }

        public Wmi2LayoutResult Layout { get; }

        public bool IsWritePathTested { get; private set; }

        public Task<string?> ReadFirmwareAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(Firmware);

        public Task<Wmi2Status> ReadStatusAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return Task.FromResult(_status);
            }
        }

        public Task<SensorSnapshot> ReadSensorsAsync(CancellationToken cancellationToken)
        {
            var round = Interlocked.Increment(ref _round);
            return Task.FromResult(new SensorSnapshot(55 + round % 10, 44 + round % 5, 40 + round % 20, 30, 2987, 0, new Wmi2Tach(0, 0xA0, 0x01, 0xA0)));
        }

        public Task<Wmi2WriteOutcome> TestWritePathAsync(CancellationToken cancellationToken)
        {
            IsWritePathTested = true;
            return ApplyAsync(new Wmi2Change.Performance(PerformanceMode.Balanced), cancellationToken);
        }

        public Task<Wmi2WriteOutcome> ApplyAsync(Wmi2Change change, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                var before = Byte(_status, change.Register);
                var written = With(_status, change);
                var write = new RegisterWrite(change.Register, Byte(written, change.Register));
                switch (_kind)
                {
                    case Wmi2ControlPreviewKind.DryRun:
                        return Task.FromResult(new Wmi2WriteOutcome(WriteStatus.DryRun, change.Register, before, write));
                    case Wmi2ControlPreviewKind.Fail:
                        return Task.FromResult(new Wmi2WriteOutcome(
                            WriteStatus.FailedRecovered, change.Register, before, write, Problem: Wmi2GatewayProblem.ReadBackDiffers));
                    default:
                        _status = written;
                        return Task.FromResult(new Wmi2WriteOutcome(WriteStatus.Applied, change.Register, before, write));
                }
            }
        }

        public Task<IReadOnlyList<Wmi2WriteOutcome>> RevertAsync(CancellationToken cancellationToken)
        {
            var status = _kind == Wmi2ControlPreviewKind.Fail ? WriteStatus.FailedUnrecovered : WriteStatus.Applied;
            IReadOnlyList<Wmi2WriteOutcome> outcomes = [.. Wmi2WriteCeiling.Registers.Select(r => new Wmi2WriteOutcome(
                status, r, Problem: status == WriteStatus.Applied ? null : Wmi2GatewayProblem.WriteFailed))];
            return Task.FromResult(outcomes);
        }

        public void Dispose()
        {
        }

        private static Wmi2Status With(Wmi2Status status, Wmi2Change change) => change switch
        {
            Wmi2Change.CoolerBoost { On: var on } => status with
            {
                CoolerBoostRaw = (byte)(on ? status.CoolerBoostRaw | CoolerBoostBit : status.CoolerBoostRaw & ~CoolerBoostBit),
            },
            Wmi2Change.Performance { Mode: var mode } => status with { PerformanceRaw = Wmi2WriteCeiling.StandardPerformance[mode] },
            Wmi2Change.FanMode { Mode: var mode } => status with { FanModeRaw = Wmi2WriteCeiling.FanModeValues[mode] },
            Wmi2Change.ChargeLimit { Percent: var percent } => status with { ChargeRaw = (byte)(ChargeLimitOn | percent) },
            _ => status,
        };

        private static byte Byte(Wmi2Status status, byte register) => register switch
        {
            Wmi2WriteCeiling.CoolerBoostRegister => status.CoolerBoostRaw,
            Wmi2WriteCeiling.PerformanceRegister => status.PerformanceRaw,
            Wmi2WriteCeiling.FanModeRegister => status.FanModeRaw,
            _ => status.ChargeRaw,
        };
    }
}
#endif
