using System.Reflection;
using System.Windows;
using MTool.App.Hardware;
using MTool.App.Services;
using MTool.App.Theme;
using MTool.App.Tray;
using MTool.App.ViewModels;
using MTool.App.Views;
using MTool.Core;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Power;
using MTool.Core.Profiles;
using MTool.Core.Sensors;
using MTool.Core.Settings;

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

    /// <summary>Null when the app cannot run (PawnIO missing, EC unreachable); the user was told why.</summary>
    public static async Task<GuiBootstrapper?> StartAsync(Application app, FileLog log, Action exit)
    {
        var catalog = ProfileCatalog.BuiltIn;
        var settings = LoadSettings(catalog, log);
        var reapplyOptions = AutoReapplyOptions.Default;
        var coordinator = new PowerStateCoordinator(TimeProvider.System, reapplyOptions.GateDelay);
        if (OpenSession(log, () => coordinator.IsEcAccessAllowed) is not { } session)
        {
            return null;
        }

        var teardown = new Stack<(string Name, Action Dispose)>();
        teardown.Push(("EC oturumu", session.Dispose));
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
            log.Warn("Çıkış: süren EC yazması beklenenden uzun sürdü; oturum yine de kapatılıyor.");
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
        teardown.Push(("sensör yoklama", poller.Dispose));

        var theme = new ThemeManager(app);
        teardown.Push(("tema", theme.Dispose));

        MainWindow? window = null;
        var tray = new TrayIconHost(theme, () => window?.ToggleFromTray(), () => window?.ShowNearTray(), exit);
        teardown.Push(("tepsi ikonu", tray.Dispose));

        // Before the view model: with switching on, the window must first show the current source's pair,
        // and the start-up reapply below writes that pair.
        var powerEvents = new SystemPowerEvents(log);
        teardown.Push(("güç olayları", powerEvents.Dispose));
        await AlignToStartSourceAsync(service, powerEvents.Current, log);

        var ui = new DispatcherUi(app);
        var viewModel = new MainViewModel(poller, service, control, powerEvents, tray, ui, TimeProvider.System);
        teardown.Push(("durum mesajı zamanlayıcısı", viewModel.Status.Dispose));
        window = new MainWindow(viewModel, () =>
        {
            FanCurveEditorWindow? editor = null;
            editor = new FanCurveEditorWindow(viewModel.CreateEditor(new DialogConfirm(() => editor, theme)), theme);
            return editor;
        });
        teardown.Push(("pencere", window.CloseForExit));
        window.SourceInitialized += (_, _) => window.ApplyTitleBarTheme(theme.IsDark);
        theme.Changed += () => window.ApplyTitleBarTheme(theme.IsDark);

        log.Info($"GUI başladı ({AppVersion}). Firmware: {setup.Firmware?.Version ?? "okunamadı"}, yazma: {control.Access.WriteMode}" +
                 (control.Access.LockReason is { } reason ? $" ({reason})" : ""));

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

        teardown.Push(("otomatik yeniden uygulama", StopAutoReapply));

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

        await viewModel.InitializeAsync(settings.Warnings);
        var signInStart = new SignInStartViewModel(new StartupTask(), CurrentExe, viewModel.Status, log);
        tray.Attach(viewModel, signInStart); // Only now does the icon appear.
        _ = signInStart.LoadAsync(); // Off the UI thread; the menu item stays disabled until it answers.

        // After Attach, so a failure balloon has a visible icon and the start-up refresh is done.
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
            log.Error("Açılışta güç kaynağına göre ayar seçilemedi", ex);
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
            log.Error("Otomatik yeniden uygulama sonucu gösterilemedi", ex);
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
                log.Error($"Kapanış: {step.Name} kapatılamadı", ex);
            }
        }
    }

    private static LoadedSettings LoadSettings(ProfileCatalog catalog, FileLog log)
    {
        var store = new SettingsStore(AppPaths.Root);
        var loaded = store.Load();
        var sanitized = SettingsSanitizer.Sanitize(loaded.Settings, catalog);

        // The next save writes the file without the dropped profiles, so keep the user's original first.
        var copied = sanitized.DroppedProfiles > 0 ? store.PreserveCopy() : null;
        var warnings = new[] { loaded.Warning }.Concat(sanitized.Warnings).Append(copied).OfType<string>().ToArray();
        foreach (var warning in warnings)
        {
            log.Warn(warning);
        }

        return new LoadedSettings(store, sanitized.Settings, warnings);
    }

    /// <param name="Store">
    /// The store that read the file. Saves must go through this same store: it remembers that the file
    /// could not be read and then refuses to save over it, which a new store would not know.
    /// </param>
    private sealed record LoadedSettings(SettingsStore Store, AppSettings Settings, IReadOnlyList<string> Warnings);

    private static string CurrentExe =>
        Environment.ProcessPath ?? throw new InvalidOperationException("Çalışan exe'nin yolu bulunamadı.");

    internal static string AppVersion =>
        typeof(GuiBootstrapper).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "sürüm yok";

    private static EcSession? OpenSession(FileLog log, Func<bool> accessGate)
    {
        try
        {
            // Before the PawnIO check: installing PawnIO would not help a model M-Tool cannot read.
            WmiInterfaceCheck.Default.EnsureWmi1(log);
        }
        catch (UnsupportedDeviceException ex)
        {
            log.Warn($"Desteklenmeyen model; GUI açılmadı. {ex.Message}");
            StartupProblem($"{ex.Message}\n\nAyrıntı: {AppPaths.Logs}");
            return null;
        }

        if (PawnIoInstallation.InstalledVersion() is null)
        {
            log.Error("PawnIO kurulu değil; GUI açılmadı.");
            StartupProblem(
                $"PawnIO kurulu değil. M-Tool EC'ye PawnIO sürücüsüyle erişir.\n\nKurmak için (yönetici komut isteminde):\n" +
                $"{PawnIoInstallation.InstallCommand}\n\nAyrıntı: {AppPaths.Logs}");
            return null;
        }

        try
        {
            // A GUI without PawnIO (WMI only) is deferred; until then the set-up screen above stays.
            return EcSession.Open(log, EcBackends.Hybrid, accessGate);
        }
        catch (Exception ex)
        {
            log.Error("EC oturumu açılamadı; GUI açılmadı.", ex);
            StartupProblem($"EC'ye erişilemedi: {ex.Message}\n\nAyrıntı: {AppPaths.Logs}");
            return null;
        }
    }

    private static void StartupProblem(string message) =>
        MessageBox.Show(message, "M-Tool", MessageBoxButton.OK, MessageBoxImage.Error);

    private sealed class DispatcherUi(Application app) : IUiDispatcher
    {
        public void Post(Action action) => app.Dispatcher.BeginInvoke(action);
    }

    /// <summary>Asks over the window that is asking, so that window can close the question on exit.</summary>
    private sealed class DialogConfirm(Func<Window?> owner, ThemeManager theme) : IConfirm
    {
        public bool Ask(string question) =>
            // A window not shown yet or already closed cannot own the question; it then opens on its own.
            new ConfirmDialog(question, theme.IsDark) { Owner = owner() is { IsLoaded: true } shown ? shown : null }
                .ShowDialog() == true;
    }
}
