using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DisplayPad.Host.Services;

namespace DisplayPad.Host;

public partial class InstalledProgramsWindow : Window
{
    private readonly CancellationTokenSource _closed = new();
    private IReadOnlyList<InstalledProgram>? _programs;
    internal InstalledProgram? SelectedProgram { get; private set; }

    public InstalledProgramsWindow() => InitializeComponent();

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        var token = _closed.Token;
        try
        {
            _programs = await Task.Run(() => InstalledProgramCatalog.Load(token), token);
            if (!token.IsCancellationRequested) FilterPrograms();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!token.IsCancellationRequested) ListStatus.Text = Loc.Get("MsgProgramsLoadFailed");
        }
    }

    private void FilterPrograms()
    {
        if (_programs is null) return;
        var query = SearchBox.Text.Trim();
        var matches = _programs.Where(program =>
            program.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            program.ProgramPath.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        ProgramsList.ItemsSource = matches;
        ListStatus.Text = matches.Length == 0 ? Loc.Get("MsgProgramsEmpty") :
            string.Format(Loc.Get("FmtProgramsFound"), matches.Length);
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => FilterPrograms();
    private void Programs_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SelectButton.IsEnabled = ProgramsList.SelectedItem is InstalledProgram;
    private void Select_Click(object sender, RoutedEventArgs e)
    {
        if (ProgramsList.SelectedItem is not InstalledProgram program) return;
        SelectedProgram = program;
        DialogResult = true;
    }
    private void Programs_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left &&
            ItemsControl.ContainerFromElement(ProgramsList, e.OriginalSource as DependencyObject) is ListBoxItem)
            Select_Click(sender, e);
    }
    private void Window_Closed(object? sender, EventArgs e)
    {
        _closed.Cancel();
        _closed.Dispose();
    }
}
