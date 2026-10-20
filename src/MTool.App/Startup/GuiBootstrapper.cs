using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using MTool.App.Hardware;
using MTool.App.Net;
using MTool.App.Reports;
using MTool.App.Resources;
using MTool.App.Services;
using MTool.App.Theme;
using MTool.App.Tray;
using MTool.App.Updates;
using MTool.App.ViewModels;
using MTool.App.Views;
using MTool.Core;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Ec;
using MTool.Core.Localization;
using MTool.Core.Power;
using MTool.Core.Profiles;
using MTool.Core.Sensors;
using MTool.Core.Settings;
using MTool.Core.Updates;

namespace MTool.App.Startup;

/// <summary>
/// Composition root of the GUI: settings, EC session, write access, services, view model, window
/// and tray. The only writes it starts are the automatic reapplies (start-up and after resume),
/// which never touch the raw port. Sleep closes the EC access gate for the worker and the sensor
/// poller alike. If start-up fails half way, what was built is torn down again, so no tray icon or
/// PawnIO handle is left. Shutdown: automatic reapplying and polling stop, a running EC write may
/// finish, the tray icon goes, the EC session closes.
/// </summary>
internal sealed class GuiBootstrapper : IDisposable
{
    /// <summary>A gateway plan retries for at most ~1.5 s; this covers it with room to spare.</summary>
    private static readonly TimeSpan RunningWriteGrace = TimeSpan.FromSeconds(3);

    private readonly Stack<(string Name, Action Dispose)> _teardown;
    private readonly ProfileService _service;
    private readonly IAppLog _log;

    private GuiBootstrapper(Stack<(string, Action)> teardown, ProfileService service, MainWindow window, IAppLog log) =>
        (_teardown, _service, Window, _log) = (teardown, service, window, log);

    public MainWindow Window { get; }

    /// <summary>
    /// Reads the settings and sets the UI language. Call it outside any async method: a culture set
    /// inside one is undone when that method first awaits, and whatever runs later from a click (the
    /// curve editor, messages) would come up in the Windows language.
    /// </summary>
    public static LoadedSettings LoadSettings(FileLog log) => LoadSettings(ProfileCatalog.BuiltIn, log);

    /// <param name="settings">From <see cref="LoadSettings(FileLog)"/>, which has already set the UI language.</param>
    /// <returns>
    /// Null when the app cannot run: PawnIO missing or the EC unreachable (the user was told why), or a
    /// model M-Tool cannot control yet (the reports-only window was shown and has been closed).
    /// </returns>
    /// <param name="instance">Held by this process; a second start asks it to show its window.</param>
    public static async Task<GuiBootstrapper?> StartAsync(Application app, FileLog log, Action exit, LoadedSettings settings, SingleInstance instance)
    {
        var catalog = ProfileCatalog.BuiltIn;
        var reapplyOptions = AutoReapplyOptions.Default;
        var coordinator = new PowerStateCoordinator(TimeProvider.System, reapplyOptions.GateDelay);
        if (OpenSession(app, instance, log, () => coordinator.IsEcAccessAllowed) is not { } session)
        {
            return null;
        }

        var teardown = new Stack<(string Name, Action Dispose)>();
        teardown.Push(("EC session", session.Dispose));
        try
        {
            return await BuildAsync(app, log, exit, catalog, settings, session, coordinator, reapplyOptions, teardown);
        }
        catch
        {
            TearDown(teardown, log);
            throw;
        }
    }

    public void Dispose()
    {
        WaitForRunningWrite(() => _service.IsBusy, _log);
        TearDown(_teardown, _log);
    }

    /// <summary>Lets a write that is already on the EC finish; its continuation does not need the UI thread.</summary>
    private static void WaitForRunningWrite(Func<bool> isWriting, IAppLog log)
    {
        if (!SpinWait.SpinUntil(() => !isWriting(), RunningWriteGrace))
        {
            log.Warn("Exit: an EC write took longer than expected; closing the session anyway.");
        }
    }

    private static async Task<GuiBootstrapper> BuildAsync(
        Application app, FileLog log, Action exit, ProfileCatalog catalog, LoadedSettings settings,
        EcSession session, PowerStateCoordinator coordinator,
        AutoReapplyOptions reapplyOptions, Stack<(string Name, Action Dispose)> teardown)
    {
        var setup = await WriteAccessBootstrap.CreateAsync(
            session.Worker, session.Layout, AppPaths.Root, settings.Settings.DryRun, session.PortAvailable, log);
        setup = setup with { Match = session.Match(setup.Firmware) };
        var control = new P65Control(session.Worker, setup, session.Layout, log);
        var service = new ProfileService(control, catalog, settings.Store, settings.Settings, log);

        var poller = new SensorPoller(
            control.ReadSensorsAsync, () => coordinator.IsEcAccessAllowed, () => service.IsBusy, TimeProvider.System, log,
            MainViewModel.HiddenInterval);
        teardown.Push(("sensor polling", poller.Dispose));

        var theme = new ThemeManager(app);
        teardown.Push(("theme", theme.Dispose));

        MainWindow? window = null;
        SettingsWindowHost? settingsWindow = null;
        var tray = new TrayIconHost(theme, () => window?.ToggleFromTray(), () => window?.ShowNearTray(), () => settingsWindow?.Show(), exit);
        teardown.Push(("tray icon", tray.Dispose));

        // Before the view model: with switching on, the window must first show the current source's pair,
        // and the start-up reapply below writes that pair.
        var powerEvents = new SystemPowerEvents(log);
        teardown.Push(("power events", powerEvents.Dispose));

        // The resume reapply's refresh must not show Cooler Boost from before sleep. The clear is immediate
        // unless a port access holds the gate, and the reapply waits its resume delay first. An open window
        // shows it unknown at once (set once the view model exists).
        Action? showWakeState = null;
        async void ForgetPortState()
        {
            try
            {
                await control.ForgetPortStateAsync();
                showWakeState?.Invoke();
            }
            catch (Exception ex)
            {
                log.Error("Port state could not be reset after wake", ex);
            }
        }

        powerEvents.Resumed += ForgetPortState;
        teardown.Push(("port state on wake", () => powerEvents.Resumed -= ForgetPortState));
        await AlignToStartSourceAsync(service, powerEvents.Current, log);

        var ui = new DispatcherUi(app);
        var viewModel = new MainViewModel(poller, service, control, powerEvents, tray, ui, TimeProvider.System, settings.WindowsUi);
        showWakeState = () => ui.Post(viewModel.Controls.ShowPortCacheAfterWake);
        teardown.Push(("status message timer", viewModel.Status.Dispose));
        window = new MainWindow(viewModel, () =>
        {
            FanCurveEditorWindow? editor = null;
            editor = new FanCurveEditorWindow(viewModel.CreateEditor(new DialogConfirm(() => editor, theme)), theme);
            return editor;
        }, () => settingsWindow?.Show());
        teardown.Push(("window", window.CloseForExit));
        window.SourceInitialized += (_, _) => window.ApplyTitleBarTheme(theme.IsDark);
        theme.Changed += () => window.ApplyTitleBarTheme(theme.IsDark);

        log.Info($"GUI started ({AppVersion}). Firmware: {setup.Firmware?.Version ?? "unreadable"}, writes: {control.Access.WriteMode}" +
                 (setup.Gateway.LockReasonForLog is { } reason ? $" ({reason})" : ""));

        var reapplier = new AutoReapplier(service.ReapplyAsync, powerEvents, coordinator, TimeProvider.System, log, reapplyOptions);
        var switcher = new PowerSourceSwitcher(
            service.SwitchPowerSourceAsync, powerEvents, powerEvents, coordinator, TimeProvider.System, log, PowerSwitchOptions.Default);
        var stopped = false;

        // First to go on shutdown or a failed start-up: no new reapply or switch starts, a running one
        // may finish before the EC session closes, and a result still queued for the UI is dropped.
        void StopAutoReapply()
        {
            stopped = true;
            switcher.Dispose();
            reapplier.Dispose();
            WaitForRunningWrite(() => switcher.IsRunning || reapplier.IsRunning || service.IsBusy, log);
        }

        teardown.Push(("auto reapply", StopAutoReapply));

        // Results arrive on pool threads and are shown on the UI thread.
        void ShowOnUi(AutoReapplyResult result) => ui.Post(() =>
        {
            if (!stopped)
            {
                _ = ShowAutoReapplyAsync(viewModel, result, log);
            }
        });

        reapplier.Reapplied += ShowOnUi;
        switcher.Switched += ShowOnUi;

        await viewModel.InitializeAsync([.. settings.Warnings, .. session.StartupWarnings]);
        var signInStart = new SignInStartViewModel(new StartupTask(), CurrentExe, viewModel.Status, log, File.Exists);
        var submissions = new SubmissionClient(SafeHttp.CreateHandler(), SubmissionEndpoint.Uri, AppVersion, SubmissionClient.DefaultTimeout);
        var stopSubmission = new CancellationTokenSource();
        teardown.Push(("EC data submission", () => StopSubmissions(stopSubmission, submissions)));
        var report = new ReportViewModel(
            new ReportService(new LiveReportSources(log), log, AppPaths.Reports, AppVersion, ReportService.CurrentUserWords()),
            new DialogConfirm(() => window, theme), new DialogPreview(() => window, theme), submissions, new ShellLauncher(log),
            viewModel.Status, tray, log, stop: stopSubmission.Token);
        settingsWindow = CreateSettingsWindow(viewModel, signInStart, service, control, tray, ui, theme, log, teardown, out var updates);
        tray.Attach(viewModel, signInStart, report); // Only now does the icon appear.
        _ = signInStart.LoadAsync(); // Off the UI thread; the menu item stays disabled until it answers.
        _ = report.LoadAsync(); // Off the UI thread; the WMI2 item stays hidden until it answers.

        // After Attach, so a failure balloon has a visible icon and the start-up refresh is done.
        _ = updates.RunStartupCheckAsync(); // Returns at once unless the user turned it on; never throws.
        reapplier.Start();
        switcher.Start();
        poller.Start();
        return new GuiBootstrapper(teardown, service, window, log);
    }

    /// <summary>Best effort: without it the switcher's first check after start-up aligns instead.</summary>
    private static async Task AlignToStartSourceAsync(ProfileService service, PowerSource? source, IAppLog log)
    {
        if (source is not { } current)
        {
            return;
        }

        try
        {
            await service.SwitchPowerSourceAsync(current, write: false);
        }
        catch (Exception ex)
        {
            log.Error("Could not choose settings for the power source at start", ex);
        }
    }

    private static async Task ShowAutoReapplyAsync(MainViewModel viewModel, AutoReapplyResult result, IAppLog log)
    {
        try
        {
            await viewModel.OnAutoReappliedAsync(result);
        }
        catch (Exception ex)
        {
            log.Error("Could not show the auto reapply result", ex);
        }
    }

    /// <summary>Newest first; one failing step does not stop the rest.</summary>
    private static void TearDown(Stack<(string Name, Action Dispose)> teardown, IAppLog log)
    {
        while (teardown.TryPop(out var step))
        {
            try
            {
                step.Dispose();
            }
            catch (Exception ex)
            {
                log.Error($"Shutdown: could not close {step.Name}", ex);
            }
        }
    }

    private static LoadedSettings LoadSettings(ProfileCatalog catalog, FileLog log)
    {
        var store = new SettingsStore(AppPaths.Root);
        var loaded = store.Load();
        var windowsUi = CultureInfo.CurrentUICulture; // before the chosen language replaces it

        // Before the sanitizer: its warnings are shown in the chosen language. An unusable choice in
        // the file is ignored here and dropped (with a warning) by the sanitizer.
        var language = UiLanguage.Resolve(loaded.Settings.Language, windowsUi, SupportedLanguages.All);
        SetUiCulture(language);
        log.Info($"UI language: {language.Name}");

        var sanitized = SettingsSanitizer.Sanitize(loaded.Settings, catalog);

        // The next save writes the file without the dropped profiles, so keep the user's original first.
        var copied = sanitized.DroppedProfiles > 0 ? store.PreserveCopyNotice() : null;
        var warnings = new[] { loaded.Warning }.Concat(sanitized.Warnings).Append(copied?.Text).OfType<string>().ToArray();

        // The log is English whatever language the banner is in.
        foreach (var warning in new[] { loaded.LogWarning }.Concat(sanitized.LogWarnings).Append(copied?.Log).OfType<string>())
        {
            log.Warn(warning);
        }

        return new LoadedSettings(store, sanitized.Settings, warnings, windowsUi);
    }

    /// <summary>Resource lookups on this and later threads follow it; number and date formats stay the Windows regional ones.</summary>
    private static void SetUiCulture(CultureInfo language)
    {
        CultureInfo.DefaultThreadCurrentUICulture = language;
        CultureInfo.CurrentUICulture = language;
    }

    /// <param name="Store">
    /// The store that read the file. Saves must go through this same store: it remembers that the file
    /// could not be read and then refuses to save over it, which a new store would not know.
    /// </param>
    internal sealed record LoadedSettings(SettingsStore Store, AppSettings Settings, IReadOnlyList<string> Warnings, CultureInfo WindowsUi);

    /// <summary>
    /// The settings window shares the tray's view models; it reads SMBIOS for the About tab, never the EC.
    /// GitHub is asked only by the update button, or once after start-up when the user turned that on.
    /// </summary>
    private static SettingsWindowHost CreateSettingsWindow(
        MainViewModel viewModel, SignInStartViewModel signInStart, ProfileService service, IP65Control control, INotifier notifier,
        IUiDispatcher ui, ThemeManager theme, IAppLog log, Stack<(string Name, Action Dispose)> teardown, out UpdatesViewModel updates)
    {
        var shell = new ShellLauncher(log);
        var stopUpdates = new CancellationTokenSource();
        var client = SemVersion.TryParse(AppVersion, out var current)
            ? new UpdateClient(SafeHttp.CreateHandler(), UpdateEndpoint.Uri, AppVersion, current, UpdateClient.DefaultTimeout, TimeProvider.System)
            : null;
        updates = new UpdatesViewModel(client, service, viewModel.Status, notifier, shell, ui, log, TimeProvider.System, stopUpdates.Token);
        var updatesToStop = updates;
        void StopUpdates()
        {
            stopUpdates.Cancel();
            updatesToStop.Dispose();
            client?.Dispose();
            stopUpdates.Dispose();
        }

        teardown.Push(("update check", StopUpdates));

        var about = new AboutViewModel(
            AppVersion, () => control.Access, new LiveReportSources(log).ReadSystem, PawnIoInstallation.InstalledVersion,
            EmbeddedLicenses.Read, shell, new WpfClipboard(), viewModel.Status, log, AppPaths.Logs);
        var settings = new SettingsViewModel(signInStart, viewModel.Language, viewModel.PowerSwitch, viewModel.Status, about, updates);
        var host = new SettingsWindowHost(() => new SettingsWindow(settings, theme));
        teardown.Push(("settings window", host.CloseForExit));
        return host;
    }

    private static string CurrentExe =>
        Environment.ProcessPath ?? throw new InvalidOperationException("The path of the running exe is unknown.");

    internal static string AppVersion =>
        typeof(GuiBootstrapper).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "no version";

    private static EcSession? OpenSession(Application app, SingleInstance instance, FileLog log, Func<bool> accessGate)
    {
#if DEBUG
        if (Wmi2Preview.Current is { } preview)
        {
            log.Warn($"{Wmi2Preview.Variable}={preview}: reports-only window with made-up WMI2 values (Debug build).");
            ShowReportsOnly(app, instance, log, WmiProbe.UnsupportedMessage(WmiInterface.Wmi2), WmiInterface.Wmi2, _ => Wmi2Preview.Opener(preview)());
            return null;
        }
#endif

        try
        {
            // First: no PawnIO question for a model M-Tool cannot read at all.
            WmiInterfaceCheck.Default.EnsureWmi1(log);
        }
        catch (UnsupportedDeviceException ex)
        {
            log.Warn($"Unsupported model; reports-only window opened. {ex.LogMessage}");
            ShowReportsOnly(app, instance, log, ex.Message, ex.Detected, gate => Wmi2LiveSource.Open(gate, log));
            return null;
        }

        try
        {
            // Hybrid only where writes are verified and PawnIO is installed; elsewhere the session opens
            // WMI only, without Cooler Boost and the charge limit, and the window says why.
            return EcSession.Open(log, EcBackends.PreferHybrid, accessGate);
        }
        catch (Exception ex)
        {
            log.Error("Could not open the EC session; GUI not opened.", ex);
            StartupProblem(string.Format(Strings.Startup_EcUnreachable, ex.Message, AppPaths.Logs));
            return null;
        }
    }

    /// <summary>
    /// The report actions in a window of their own, until it is closed: no gateway, no port, no tray (a
    /// report reads the EC through WMI only, as on the command line). What would be a balloon goes to the
    /// window's band. A second start of M-Tool brings this window to the front. On a WMI2 laptop the
    /// window also offers the live values; sleep closes their access gate, and closing the window
    /// closes the interface before anything else.
    /// </summary>
    /// <param name="openLive">Opens the WMI2 interface with the given access gate; null when there is none.</param>
    private static void ShowReportsOnly(
        Application app, SingleInstance instance, FileLog log, string problem,
        WmiInterface? detected, Func<Func<bool>, IWmi2LiveSource?> openLive)
    {
        using var theme = new ThemeManager(app);
        using var status = new StatusViewModel(BandNotifier.Silent, new DispatcherUi(app), TimeProvider.System);
        using var submissions = new SubmissionClient(SafeHttp.CreateHandler(), SubmissionEndpoint.Uri, AppVersion, SubmissionClient.DefaultTimeout);
        using var stop = new CancellationTokenSource();
        var coordinator = new PowerStateCoordinator(TimeProvider.System, AutoReapplyOptions.Default.GateDelay);
        using var powerEvents = new SystemPowerEvents(log);
        using var following = coordinator.Follow(powerEvents);
        bool AccessAllowed() => coordinator.IsEcAccessAllowed;

        ReportOnlyWindow? window = null;
        var report = new ReportViewModel(
            new ReportService(new LiveReportSources(log), log, AppPaths.Reports, AppVersion, ReportService.CurrentUserWords()),
            new DialogConfirm(() => window, theme), new DialogPreview(() => window, theme), submissions, new ShellLauncher(log),
            status, new BandNotifier(status), log, stop: stop.Token);
        using var live = Wmi2LiveViewModel.For(
            detected, () => openLive(AccessAllowed), new DialogConfirm(() => window, theme), AccessAllowed, log);
        window = new ReportOnlyWindow(problem, report, status, live, theme.IsDark);
        instance.ListenForShowRequests(() => app.Dispatcher.BeginInvoke(() => window.Activate()));
        _ = report.LoadAsync(); // Off the UI thread; the WMI2 button stays hidden until it answers.
        try
        {
            window.ShowDialog();
        }
        finally
        {
            stop.Cancel(); // a send still running when the window closed ends as cancelled, without a message
        }
    }

    private static void StartupProblem(string message) =>
        MessageBox.Show(message, "M-Tool", MessageBoxButton.OK, MessageBoxImage.Error);

    private sealed class DispatcherUi(Application app) : IUiDispatcher
    {
        public void Post(Action action) => app.Dispatcher.BeginInvoke(action);
    }

    /// <summary>A send still running ends as cancelled, without a message.</summary>
    private static void StopSubmissions(CancellationTokenSource stop, SubmissionClient submissions)
    {
        stop.Cancel();
        submissions.Dispose();
        stop.Dispose();
    }

    /// <summary>Shows the text over the window that is asking, so that window can close it on exit (then nothing is sent).</summary>
    private sealed class DialogPreview(Func<Window?> owner, ThemeManager theme) : ISubmissionPreview
    {
        public bool Confirm(string text) =>
            new SubmissionPreviewWindow(text, theme.IsDark, Owned(owner)).ShowDialog() == true;

        private static Window? Owned(Func<Window?> owner) => owner() is { IsLoaded: true, IsVisible: true } shown ? shown : null;
    }

    /// <summary>Asks over the window that is asking, so that window can close the question on exit.</summary>
    private sealed class DialogConfirm(Func<Window?> owner, ThemeManager theme) : IConfirm
    {
        public bool Ask(string question, bool defaultYes = true) =>
            // A window not shown yet or already closed cannot own the question; it then opens on its own.
            new ConfirmDialog(question, theme.IsDark, defaultYes) { Owner = owner() is { IsLoaded: true } shown ? shown : null }
                .ShowDialog() == true;
    }
}
