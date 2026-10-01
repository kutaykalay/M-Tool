using Microsoft.Win32;
using MTool.Core;
using MTool.Core.Power;

namespace MTool.App.Services;

/// <summary>
/// Windows sleep and wake (<see cref="SystemEvents.PowerModeChanged"/>) as <see cref="IPowerEvents"/>.
/// Every event is logged. The static event roots this object, so <see cref="Dispose"/> must run.
/// </summary>
internal sealed class SystemPowerEvents : IPowerEvents, IDisposable
{
    private readonly IAppLog _log;

    public SystemPowerEvents(IAppLog log)
    {
        _log = log;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public event Action? Suspending;

    public event Action? Resumed;

    public void Dispose() => SystemEvents.PowerModeChanged -= OnPowerModeChanged;

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                _log.Info("Güç: uyku");
                Suspending?.Invoke();
                break;
            case PowerModes.Resume:
                _log.Info("Güç: uyanış");
                Resumed?.Invoke();
                break;
        }
    }
}
