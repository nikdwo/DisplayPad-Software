using System.Windows;
using DisplayPad.Host.Services;
using DisplayPad.Shared.Models;

namespace DisplayPad.Host;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var config = ConfigStore.Load();
        Loc.Switch(config.Language);
        new MainWindow().Show();
    }
}
