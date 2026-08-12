using DisplayPad.Host.Services;
using DisplayPad.Host.ViewModels;
using System.Windows;
using System.Windows.Media;
using DisplayPad.Shared.Models;

namespace DisplayPad.Host;

public partial class ObsSettingsWindow : Window
{
    private readonly MainViewModel _viewModel;

    public ObsSettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        HostBox.Text = viewModel.ObsHost;
        PortBox.Text = viewModel.ObsPort.ToString();
        PasswordBox.Password = viewModel.ObsPassword;
    }

    private bool TryReadPort(out int port)
    {
        if (int.TryParse(PortBox.Text, out port) && port is >= 1 and <= 65535)
            return true;
        MessageBox.Show(Loc.Get("MsgInvalidPort"), Loc.Get("DlgObsTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadPort(out int port))
            return;

        TestButton.IsEnabled = false;
        TestResult.Text = Loc.Get("TxtTesting");
        TestResult.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));

        string host = HostBox.Text.Trim();
        string password = PasswordBox.Password;
        OperationResult result = await Task.Run(() => ObsService.TestConnection(host, port, password));

        TestResult.Text = result.Success
            ? Loc.Get("MsgConnSuccess")
            : string.Format(Loc.Get("MsgConnectionError"), OperationText.Format(result));
        TestResult.Foreground = new SolidColorBrush(result.Success
            ? Color.FromRgb(0x5C, 0xB8, 0x5C)
            : Color.FromRgb(0xD9, 0x53, 0x4F));
        TestButton.IsEnabled = true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadPort(out int port))
            return;

        _viewModel.ObsHost = HostBox.Text.Trim();
        _viewModel.ObsPort = port;
        _viewModel.ObsPassword = PasswordBox.Password;
        _viewModel.SaveCommand.Execute(null);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
