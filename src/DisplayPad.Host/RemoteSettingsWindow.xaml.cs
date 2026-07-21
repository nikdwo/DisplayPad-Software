using System.Windows;
using DisplayPad.Host.Services;
using DisplayPad.Host.ViewModels;

namespace DisplayPad.Host;

public partial class RemoteSettingsWindow : Window
{
    private readonly MainViewModel _viewModel;

    public RemoteSettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        HostBox.Text = viewModel.AgentHost;
        PortBox.Text = viewModel.AgentPort.ToString();
        TokenBox.Text = viewModel.AgentToken;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text, out int port) || port is < 1 or > 65535)
        {
            MessageBox.Show(Loc.Get("MsgInvalidPort"), Loc.Get("MsgRemoteTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _viewModel.AgentHost = HostBox.Text.Trim();
        _viewModel.AgentPort = port;
        _viewModel.AgentToken = TokenBox.Text.Trim();
        _viewModel.SaveCommand.Execute(null);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
