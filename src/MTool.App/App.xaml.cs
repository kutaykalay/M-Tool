using System.Windows;
using MTool.App.Cli;

namespace MTool.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (CliRunner.IsCliInvocation(e.Args))
        {
            Shutdown(CliRunner.Run(e.Args));
            return;
        }

        // Tray and main window arrive in stage 4.
        new MainWindow().Show();
    }
}
