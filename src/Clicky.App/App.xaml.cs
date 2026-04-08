using System.Windows;

namespace Clicky.App;

public partial class App : Application
{
    private AppBootstrapper? appBootstrapper;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            appBootstrapper = new AppBootstrapper();
            await appBootstrapper.StartAsync();
        }
        catch (Exception startupException)
        {
            MessageBox.Show(
                startupException.ToString(),
                "Clicky failed to start",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        appBootstrapper?.Dispose();
        base.OnExit(e);
    }
}

