using MTool.App.Hardware;
using MTool.App.Resources;
using MTool.Core;
using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;
using MTool.Core.Localization;

namespace MTool.App.Startup;

/// <summary>The reports-only window instead of the main one.</summary>
/// <param name="Message">In the language of the window.</param>
/// <param name="LogMessage">The same in English, for the log.</param>
/// <param name="Detected">WMI2 when the live values can be offered; otherwise null.</param>
internal sealed record ReportsOnlyRoute(string Message, string LogMessage, WmiInterface? Detected);

/// <summary>Exactly one of the three: the session, the reports-only window, or the error box with its cause.</summary>
internal sealed record StartupDecision<TSession>(TSession? Session, ReportsOnlyRoute? ReportsOnly, Exception? SessionError)
    where TSession : class;

/// <summary>
/// What start-up opens. A probe that answers decides: WMI1 opens the session, anything else the
/// reports-only window. A probe that fails (WMI still starting at log-on) does not stop a session
/// that opens all the same, as before. Only when neither answers is the probe tried once more after
/// a pause; then the window opens with the step and code instead of an error box. The error box is
/// left for a WMI1 that was confirmed and still cannot open.
/// </summary>
internal static class StartupRoute
{
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan RetryBudget = TimeSpan.FromSeconds(5);

    /// <param name="probe">The interface probe with a time budget; null for its default.</param>
    /// <param name="openSession">Opens the EC session; throws when it cannot.</param>
    /// <param name="wait">Waits before the second probe.</param>
    /// <param name="readSchema">Whether MSI's WMI schema is registered, for the MSI Center hint.</param>
    public static StartupDecision<TSession> Decide<TSession>(
        Func<TimeSpan?, WmiProbeOutcome> probe, Func<TSession> openSession, Action<TimeSpan> wait, Func<MsiSchemaInfo> readSchema, IAppLog log)
        where TSession : class
    {
        var first = probe(null);
        if (first.Failure is null)
        {
            return first.Interface == WmiInterface.Wmi1 ? Open(openSession) : Reports<TSession>(Unsupported(first.Interface, readSchema, log));
        }

        log.Warn($"MSI WMI interface not detected ({first.Failure}), trying to open the session anyway.");
        try
        {
            return Opened(openSession());
        }
        catch (UnsupportedDeviceException ex)
        {
            return Reports<TSession>(new ReportsOnlyRoute(ex.Message, ex.LogMessage, ex.Detected));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Warn($"The session did not open either ({ex.ForLog()}); probing again in {RetryDelay.TotalSeconds:0} s.");
        }

        wait(RetryDelay);
        var second = probe(RetryBudget);
        if (second.Failure is { } failure)
        {
            return Reports<TSession>(NotAnswering(failure, readSchema, log));
        }

        return second.Interface == WmiInterface.Wmi1 ? Open(openSession) : Reports<TSession>(Unsupported(second.Interface, readSchema, log));
    }

    private static StartupDecision<TSession> Open<TSession>(Func<TSession> openSession)
        where TSession : class
    {
        try
        {
            return Opened(openSession());
        }
        catch (UnsupportedDeviceException ex)
        {
            return Reports<TSession>(new ReportsOnlyRoute(ex.Message, ex.LogMessage, ex.Detected));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(null, null, ex);
        }
    }

    private static StartupDecision<TSession> Opened<TSession>(TSession session)
        where TSession : class => new(session, null, null);

    private static StartupDecision<TSession> Reports<TSession>(ReportsOnlyRoute route)
        where TSession : class => new(null, route, null);

    private static ReportsOnlyRoute Unsupported(WmiInterface? detected, Func<MsiSchemaInfo> readSchema, IAppLog log)
    {
        var route = new ReportsOnlyRoute(WmiProbe.UnsupportedMessage(detected), WmiProbe.UnsupportedLogMessage(detected), detected);
        return detected is null ? WithHint(route, readSchema, log) : route;
    }

    private static ReportsOnlyRoute NotAnswering(WmiFailure failure, Func<MsiSchemaInfo> readSchema, IAppLog log)
    {
        var (text, english) = AppText.Say(nameof(Strings.Probe_WmiNotAnswering), failure.ToString());
        return WithHint(new ReportsOnlyRoute(text, english, null), readSchema, log);
    }

    /// <summary>Only when no interface was found and the schema is known to be missing: a read that fails only loses the hint.</summary>
    private static ReportsOnlyRoute WithHint(ReportsOnlyRoute route, Func<MsiSchemaInfo> readSchema, IAppLog log)
    {
        bool missing;
        try
        {
            missing = readSchema().SchemaMissing;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Warn($"MSI WMI schema not checked: {ex.ForLog()}");
            return route;
        }

        if (!missing)
        {
            return route;
        }

        var (text, english) = AppText.Say(nameof(Strings.Probe_MsiCenterOnce));
        return route with { Message = $"{route.Message}\n\n{text}", LogMessage = $"{route.LogMessage} Hint: {english}" };
    }
}
