using System.Windows;
using System.Windows.Threading;
using MTool.App.Cli;
using MTool.App.Startup;

namespace MTool.App;

/// <summary>
/// Without arguments: the tray app with its window, one per session. With <c>--tray</c>: the same
/// app with the icon only (sign-in task). Any other arguments: command-line mode. Unhandled errors
/// are logged and never write to the EC.
/// </summary>
public partial class App : Application
{
    private readonly FileLog _log = new();
    private SingleInstance? _instance;
    private GuiBootstrapper? _gui;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mode = StartupArgs.Parse(e.Args);
        if (mode == StartupMode.CommandLine)
        {
            Shutdown(CliRunner.Run(e.Args));
            return;
        }

        _instance = SingleInstance.TryAcquire(askToShow: mode == StartupMode.Window);
        if (_instance is null)
        {
            Shutdown(0); // A GUI already runs; for a plain start it was asked to show its window.
            return;
        }

        HookUnhandledErrors();
        try
        {
            _gui = await GuiBootstrapper.StartAsync(this, _log, ExitFromTray);
        }
        catch (Exception ex)
        {
            _log.Error("GUI başlatılamadı", ex);
            MessageBox.Show($"M-Tool başlatılamadı: {ex.Message}\n\nAyrıntı: {AppPaths.Logs}", "M-Tool",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        if (_gui is null)
        {
            Shutdown(1);
            return;
        }

        _instance.ListenForShowRequests(() => Dispatcher.BeginInvoke(() => _gui?.Window.ShowNearTray()));
        if (mode == StartupMode.Window)
        {
            _gui.Window.ShowNearTray();
        }
        else
        {
            _log.Info("Tepside gizli başladı (--tray); pencere açılana kadar port okunmaz.");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _gui?.Dispose();
        }
        finally
        {
            _instance?.Dispose();
            base.OnExit(e);
        }
    }

    /// <summary>The window is closed for real first, so its "closing only hides" rule does not block shutdown.</summary>
    private void ExitFromTray()
    {
        _gui?.Window.CloseForExit();
        Shutdown(0);
    }

    private void HookUnhandledErrors()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _log.Error("Gözlenmeyen görev hatası", e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            _log.Error("İşlenmeyen hata (uygulama kapanıyor)", e.ExceptionObject as Exception);
    }

    /// <summary>UI-thread errors are logged and shown; the app keeps running (the EC keeps its last table).</summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log.Error("Arayüz hatası", e.Exception);
        MessageBox.Show($"Beklenmeyen hata: {e.Exception.Message}\n\nAyrıntı: {AppPaths.Logs}", "M-Tool",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
