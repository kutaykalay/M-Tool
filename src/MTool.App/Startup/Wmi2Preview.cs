#if DEBUG
using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Ec;

namespace MTool.App.Startup;

/// <summary>What the preview shows: values, failing reads, a closed gate, or no interface at all.</summary>
internal enum Wmi2PreviewKind
{
    Values,
    Fail,
    Paused,
    Unavailable,
}

/// <summary>
/// Debug builds only: with MTOOL_WMI2_PREVIEW set, the reports-only window opens with the WMI2
/// section on any laptop, fed by made-up values, so its look (theme, DPI, languages, messages) can
/// be checked on a WMI1 laptop. Nothing is read from the laptop for the live values; the report
/// buttons and their WMI check work as in any build. Release builds have no such type.
/// </summary>
internal static class Wmi2Preview
{
    public const string Variable = "MTOOL_WMI2_PREVIEW";

    public static Wmi2PreviewKind? Current => Parse(Environment.GetEnvironmentVariable(Variable));

    /// <summary>"1" values, "fail", "paused", "none"; anything else leaves the preview off.</summary>
    public static Wmi2PreviewKind? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "1" => Wmi2PreviewKind.Values,
        "fail" => Wmi2PreviewKind.Fail,
        "paused" => Wmi2PreviewKind.Paused,
        "none" => Wmi2PreviewKind.Unavailable,
        _ => null,
    };

    public static Func<IWmi2LiveSource?> Opener(Wmi2PreviewKind kind) =>
        kind == Wmi2PreviewKind.Unavailable ? () => null : () => new Source(kind);

    /// <summary>Each round moves the values a little; the GPU speed is always one the bytes cannot tell.</summary>
    private sealed class Source(Wmi2PreviewKind kind) : IWmi2LiveSource
    {
        private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(300); // long enough to see the busy state
        private static readonly byte[] Modes = [0xC1, 0xC6, 0xC4, 0xC3]; // C6 is this family's eco; the last one is unknown: raw only

        private int _round;

        public Task<string?> ReadFirmwareAsync(CancellationToken cancellationToken) =>
            Answer(() => (string?)"15M1IMS2.105", cancellationToken); // in the msi-ec list, with a family override

        public Task<Wmi2Status> ReadStatusAsync(CancellationToken cancellationToken) =>
            Answer(() => new Wmi2Status((byte)(_round % 2 == 0 ? 0x00 : 0x80), Modes[_round % Modes.Length], 0x0D, 0xD0), cancellationToken);

        public Task<SensorSnapshot> ReadSensorsAsync(CancellationToken cancellationToken) =>
            Answer(() =>
            {
                var round = _round++;
                return new SensorSnapshot(55 + round % 10, 44 + round % 5, 40 + round % 20, 30, 2987, 0, new Wmi2Tach(0, 0xA0, 0x01, 0xA0));
            }, cancellationToken);

        public void Dispose()
        {
        }

        private async Task<T> Answer<T>(Func<T> value, CancellationToken cancellationToken)
        {
            await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
            return kind switch
            {
                Wmi2PreviewKind.Fail => throw new EcAccessException("WMI2 preview: a made-up failure"),
                Wmi2PreviewKind.Paused => throw new EcAccessException("WMI2 preview: a made-up closed gate") { IsAccessPaused = true },
                _ => value(),
            };
        }
    }
}
#endif
