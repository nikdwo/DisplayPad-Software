using System.Drawing;
using System.Resources;
using System.Xml.Linq;
using Xunit;

namespace DisplayPad.Host.Tests;

public sealed class AppIconTests
{
    [Theory]
    [InlineData("displaypad", null)]
    [InlineData("tray-connected", true)]
    [InlineData("tray-disconnected", false)]
    public void EmbeddedIconsContainEverySizeWithTransparentEdgesAndCorrectBadge(string name, bool? connected)
    {
        var assembly = typeof(MainWindow).Assembly;
        var resources = new ResourceManager("DisplayPad.Host.g", assembly);
        using var stream = name == "displaypad"
            ? resources.GetStream("assets/displaypad.ico")!
            : assembly.GetManifestResourceStream($"DisplayPad.Host.Assets.{name}.ico")!;
        Assert.NotNull(stream);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        byte[] data = copy.ToArray();
        using var reader = new BinaryReader(new MemoryStream(data));
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        int[] sizes = [16, 20, 24, 32, 48, 64, 128, 256];
        Assert.Equal(sizes.Length, reader.ReadUInt16());
        foreach (int size in sizes)
        {
            Assert.Equal(size == 256 ? 0 : size, reader.ReadByte());
            Assert.Equal(size == 256 ? 0 : size, reader.ReadByte());
            Assert.Equal(0, reader.ReadByte());
            Assert.Equal(0, reader.ReadByte());
            Assert.Equal(1, reader.ReadUInt16());
            Assert.Equal(32, reader.ReadUInt16());
            int length = checked((int)reader.ReadUInt32());
            int offset = checked((int)reader.ReadUInt32());
            Assert.InRange(offset, 6 + sizes.Length * 16, data.Length - length);
            using var frame = new MemoryStream(data, offset, length);
            using var bitmap = new Bitmap(frame);
            Assert.Equal(size, bitmap.Width);
            Assert.Equal(size, bitmap.Height);
            Assert.Equal(0, bitmap.GetPixel(0, 0).A);
            Assert.Equal(0, bitmap.GetPixel(size - 1, size - 1).A);
            AssertColors(bitmap, connected);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TraySelectsTheCorrectBadgeAndIconSurvivesClosingItsResourceStream(bool connected)
    {
        using var icon = MainWindow.LoadTrayIcon(connected);
        Assert.NotEqual(IntPtr.Zero, icon.Handle);
        using var bitmap = icon.ToBitmap();
        AssertColors(bitmap, connected);
    }

    [Fact]
    public void WindowAndExecutableUseTheSameIconWithoutACircle()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 5; i++) root = root.Parent!;
        string host = Path.Combine(root.FullName, "src", "DisplayPad.Host");
        var project = XDocument.Load(Path.Combine(host, "DisplayPad.Host.csproj"));
        var window = XDocument.Load(Path.Combine(host, "MainWindow.xaml"));
        string appIcon = project.Descendants("ApplicationIcon").Single().Value.Replace('\\', '/');
        Assert.Equal("Assets/displaypad.ico", appIcon);
        Assert.Equal(appIcon, window.Root!.Attribute("Icon")!.Value);
    }

    private static void AssertColors(Bitmap image, bool? connected)
    {
        int red = 0, green = 0, cyan = 0, white = 0;
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
        {
            var pixel = image.GetPixel(x, y);
            if (pixel.A < 128) continue;
            if (pixel.R > 180 && pixel.G < 100 && pixel.B < 100) red++;
            if (pixel.G > 140 && pixel.R < 140 && pixel.B < 100) green++;
            if (pixel.G > 150 && pixel.B > 150 && pixel.R < 100) cyan++;
            if (pixel.R > 210 && pixel.G > 210 && pixel.B > 210) white++;
        }
        Assert.True(cyan > 0, "Pad keys must remain cyan.");
        Assert.True(white > 0, "Pad keys must remain white.");
        Assert.Equal(connected == true, green > 0);
        Assert.Equal(connected == false, red > 0);
    }
}
