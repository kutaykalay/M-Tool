using System.Windows.Forms;
using Microsoft.Win32;
using MTool.Core;
using MTool.Core.Power;

namespace MTool.App.Services;

/// <summary>
/// Windows sleep, wake and AC/battery changes (<see cref="SystemEvents.PowerModeChanged"/>) as
/// <see cref="IPowerEvents"/> and <see cref="IPowerSource"/>. Sleep and wake are always logged; a
/// status change only when the source changed (battery percentage changes raise it too). The static
/// event roots this object, so <see cref="Dispose"/> must run.
/// </summary>
internal sealed class SystemPowerEvents : IPowerEvents, IPowerSource, IDisposable
{
    private readonly IAppLog _log;
    private readonly Func<PowerLineStatus> _readStatus;
    private PowerSource? _lastSource;

    public SystemPowerEvents(IAppLog log)
        : this(log, () => SystemInformation.PowerStatus.PowerLineStatus) =>
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

    /// <summary>For tests: reads the line status from <paramref name="readStatus"/> and does not listen to Windows.</summary>
    internal SystemPowerEvents(IAppLog log, Func<PowerLineStatus> readStatus)
    {
        _log = log;
        _readStatus = readStatus;
        _lastSource = Current;
        _log.Info($"Power: source {Name(_lastSource)}");
    }

    public event Action? Suspending;

    public event Action? Resumed;

    public event Action? Changed;

    /// <summary>Null also when Windows cannot be asked; no source means no switch.</summary>
    public PowerSource? Current
    {
        get
        {
            try
            {
                return From(_readStatus());
            }
            catch (Exception ex)
            {
                _log.Warn($"Power: source unreadable ({ex.Message})");
                return null;
            }
        }
    }

    public void Dispose() => SystemEvents.PowerModeChanged -= OnPowerModeChanged;

    internal static PowerSource? From(PowerLineStatus status) => status switch
    {
        PowerLineStatus.Online => PowerSource.Ac,
        PowerLineStatus.Offline => PowerSource.Battery,
        _ => null,
    };

    private static string Name(PowerSource? source) => source?.ToString() ?? "unknown";

    // Runs on the SystemEvents thread: anything thrown here would end the process.
    internal void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        try
        {
            Dispatch(e.Mode);
        }
        catch (Exception ex)
        {
            _log.Error($"Power event ({e.Mode}) not handled", ex);
        }
    }

    private void Dispatch(PowerModes mode)
    {
        switch (mode)
        {
            case PowerModes.Suspend:
                _log.Info("Power: sleep");
                Suspending?.Invoke();
                break;
            case PowerModes.Resume:
                _log.Info("Power: resume");
                Resumed?.Invoke();
                break;
            case PowerModes.StatusChange:
                OnStatusChange();
                break;
        }
    }

    // SystemEvents raises its events one at a time on its own thread, so _lastSource needs no lock.
    private void OnStatusChange()
    {
        var source = Current;
        if (source == _lastSource)
        {
            return;
        }

        _log.Info($"Power: source {Name(_lastSource)}→{Name(source)}");
        _lastSource = source;
        Changed?.Invoke();
    }
}
