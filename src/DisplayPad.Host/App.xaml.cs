using System.Windows;
using DisplayPad.Host.Services;
using DisplayPad.Shared.Models;

namespace DisplayPad.Host;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var config = ConfigStore.Load(new HostConfigNameProvider());
            AppTheme.Switch(config.Theme);
            Loc.Switch(config.Language);
            new MainWindow(config).Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(OperationText.Format(OperationText.FromException(ex)), Loc.Get("HostStartFailedTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
