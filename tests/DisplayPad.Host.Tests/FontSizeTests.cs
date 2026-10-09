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

public sealed class FontSizeTests
{
    [Fact]
    public void NewPagesFoldersAndMissingFontSizeUseTen()
    {
        var page = new PageConfig();
        page.EnsureKeys();
        Assert.All(page.Keys, key => Assert.Equal(10, key.FontSize));
        var folder = new KeyViewModel(new KeyConfig { Action = new KeyAction { Type = KeyActionType.Folder } });
        folder.EnsureFolderPage("Test");
        Assert.All(folder.FolderPage!.Keys, key => Assert.Equal(10, key.FontSize));
        Assert.Equal(10, new KeyViewModel(JsonSerializer.Deserialize<KeyConfig>("{}")!).FontSize);
    }

    [Fact]
    public void DropdownPreservesOldSizesAndWritesOnlyTheChosenSize()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var root = new DirectoryInfo(AppContext.BaseDirectory);
                for (int index = 0; index < 5; index++) root = root.Parent!;
                XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                var source = XDocument.Load(Path.Combine(root.FullName, "src", "DisplayPad.Host", "MainWindow.xaml"));
                var element = new XElement(source.Descendants().Single(node => (string?)node.Attribute(x + "Name") == "FontSizePicker"));
                element.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
                element.SetAttributeValue(XNamespace.Xmlns + "vm", "clr-namespace:DisplayPad.Host.ViewModels;assembly=DisplayPad.Host");
                var picker = (ComboBox)XamlReader.Parse(element.ToString());
                Assert.True(picker.IsReadOnly);
                Assert.Equal(Enumerable.Range(8, 9), picker.Items.Cast<int>());
                var key = new KeyViewModel(new KeyConfig { FontSize = 24 });
                picker.DataContext = key;
                picker.ApplyTemplate();
                picker.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.Equal(24, key.FontSize);
                Assert.Equal("24", picker.Text);
                foreach (var size in KeyViewModel.FontSizes)
                {
                    picker.SelectedItem = size;
                    picker.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.Equal(size, key.FontSize);
                    Assert.Equal(size.ToString(), picker.Text);
                    var loaded = new KeyViewModel(JsonSerializer.Deserialize<KeyConfig>(JsonSerializer.Serialize(key.ToModel()))!);
                    Assert.Equal(size, loaded.FontSize);
                }
                var previous = new KeyViewModel(new KeyConfig { FontSize = 18 });
                picker.DataContext = previous;
                picker.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.Equal("18", picker.Text);
                Assert.Equal(18, previous.ToModel().FontSize);
                var fresh = new KeyViewModel(new KeyConfig());
                picker.DataContext = fresh;
                picker.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.Equal(10, picker.SelectedItem);
                Assert.Equal("10", picker.Text);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
