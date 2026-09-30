using System.Windows;
using System.Windows.Threading;
using MTool.App.Cli;
using MTool.App.Startup;

namespace MTool.App;

/// <summary>
/// With arguments: command-line mode (plan.md §3), unchanged. Without: the tray app, one per
/// session. Unhandled errors are logged and never write to the EC.
/// </summary>
public partial class App : Application
{
    private readonly FileLog _log = new();
    private SingleInstance? _instance;
    private GuiBootstrapper? _gui;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (CliRunner.IsCliInvocation(e.Args))
        {
            Shutdown(CliRunner.Run(e.Args));
            return;
        }

        _instance = SingleInstance.TryAcquire();
        if (_instance is null)
        {
            Shutdown(0); // The running instance was asked to show its window.
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
        _gui.Window.ShowNearTray();
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
