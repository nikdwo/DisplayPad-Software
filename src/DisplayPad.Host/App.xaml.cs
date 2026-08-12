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
            var config = ConfigStore.Load();
            Loc.Switch(config.Language);
            new MainWindow().Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "DisplayPad Remote – Konfiguration konnte nicht geladen werden",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
