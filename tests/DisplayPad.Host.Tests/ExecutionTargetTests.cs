using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using DisplayPad.Host.ViewModels;
using DisplayPad.Shared.Models;
using Xunit;

namespace DisplayPad.Host.Tests;

public sealed class ExecutionTargetTests
{
    [Fact]
    public void NewAndUnassignedKeysUseLocalWhileOldActionsKeepTheirTarget()
    {
        var page = new PageConfig();
        page.EnsureKeys();
        Assert.All(page.Keys, key => Assert.Equal(ActionTarget.Local, key.Target));
        var empty = new KeyViewModel(new KeyConfig());
        Assert.Equal(ActionTarget.Local, empty.Target);
        empty.EnsureFolderPage("Folder");
        Assert.All(empty.FolderPage!.Keys, key => Assert.Equal(ActionTarget.Local, key.Target));
        empty.EditorAction = EditorActionType.LaunchProgram;
        Assert.Equal(ActionTarget.Local, empty.Target);
        var legacy = JsonSerializer.Deserialize<KeyConfig>("{\"Action\":{\"Type\":\"Hotkey\",\"Hotkey\":\"Ctrl+F1\"}}")!;
        Assert.Equal(ActionTarget.Remote, new KeyViewModel(legacy).Target);
        foreach (var target in Enum.GetValues<ActionTarget>())
        {
            var key = new KeyViewModel(new KeyConfig { Target = target, Action = new KeyAction { Type = KeyActionType.Command } });
            key.EditorAction = EditorActionType.None;
            key.EditorAction = EditorActionType.LaunchProgram;
            Assert.Equal(target, key.Target);
            Assert.Equal(target, new KeyViewModel(JsonSerializer.Deserialize<KeyConfig>(JsonSerializer.Serialize(key.ToModel()))!).Target);
        }
    }

    [Fact]
    public void ActualDropdownListsLocalFirstAndPreservesAndWritesSelectedTargets()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var root = new DirectoryInfo(AppContext.BaseDirectory);
                for (int i = 0; i < 5; i++) root = root.Parent!;
                XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                var source = XDocument.Load(Path.Combine(root.FullName, "src", "DisplayPad.Host", "MainWindow.xaml"));
                var element = new XElement(source.Descendants().Single(node => (string?)node.Attribute(x + "Name") == "ExecutionTargetPicker"));
                element.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
                element.SetAttributeValue(XNamespace.Xmlns + "models", "clr-namespace:DisplayPad.Shared.Models;assembly=DisplayPad.Shared");
                var picker = (ComboBox)XamlReader.Parse(element.ToString());
                Assert.Equal(new[] { ActionTarget.Local, ActionTarget.Remote, ActionTarget.Both }, picker.Items.Cast<ComboBoxItem>().Select(item => (ActionTarget)item.Tag));
                foreach (var target in Enum.GetValues<ActionTarget>())
                {
                    var key = new KeyViewModel(new KeyConfig { Target = target, Action = new KeyAction { Type = KeyActionType.Hotkey } });
                    picker.DataContext = key;
                    picker.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.Equal(target, picker.SelectedValue);
                    Assert.Equal(target, key.Target);
                    foreach (ComboBoxItem item in picker.Items)
                    {
                        picker.SelectedItem = item;
                        picker.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                        Assert.Equal(item.Tag, key.Target);
                    }
                }
                var fresh = new KeyViewModel(new KeyConfig());
                picker.DataContext = fresh;
                picker.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.Equal(0, picker.SelectedIndex);
                Assert.Equal(ActionTarget.Local, fresh.Target);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
