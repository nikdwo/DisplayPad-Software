using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using DisplayPad.Shared.Models;

namespace DisplayPad.Host.Services;

/// <summary>
/// Rendert Icon + Beschriftung einer Taste in eine PNG-Datei,
/// die anschließend per SDK auf das Pad geladen wird (das SDK skaliert selbst).
/// </summary>
public static class KeyImageRenderer
{
    private const int Size = 128;

    public static string RenderDirectory => Path.Combine(ConfigStore.ConfigDirectory, "rendered");

    /// <summary>Rendert das Tastenbild und speichert es als PNG-Datei (für den Upload aufs Pad).</summary>
    public static string Render(KeyConfig key)
    {
        Directory.CreateDirectory(RenderDirectory);
        string outputPath = Path.Combine(RenderDirectory, $"{Guid.NewGuid():N}-key{key.KeyIndex}.png");

        using var bmp = Draw(key);
        bmp.Save(outputPath, ImageFormat.Png);
        return outputPath;
    }

    /// <summary>Rendert das Tastenbild als PNG-Bytes (für die Vorschau in der GUI).</summary>
    public static byte[] RenderPngBytes(KeyConfig key)
    {
        using var bmp = Draw(key);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    public static string RenderBackButton(string label)
    {
        Directory.CreateDirectory(RenderDirectory);
        int keyIndex = AppConfig.KeyCount - 1;
        string outputPath = Path.Combine(RenderDirectory, $"{Guid.NewGuid():N}-key{keyIndex}.png");
        using var bmp = DrawBackButton(label);
        bmp.Save(outputPath, ImageFormat.Png);
        return outputPath;
    }

    private static Bitmap DrawBackButton(string label)
    {
        var bmp = new Bitmap(Size, Size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Black);

        using var arrowBrush = new SolidBrush(Color.FromArgb(0x00, 0xB4, 0xD8));
        var arrow = new PointF[]
        {
            new(26f, 52f), new(72f, 18f), new(72f, 36f),
            new(96f, 36f), new(96f, 68f), new(72f, 68f), new(72f, 86f),
        };
        g.FillPolygon(arrowBrush, arrow);

        using var font = new Font("Segoe UI", 15, System.Drawing.FontStyle.Bold);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Far,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        var textArea = new RectangleF(2, 4, Size - 4, Size - 8);
        var shadowArea = textArea;
        shadowArea.Offset(1, 1);
        g.DrawString(label, font, Brushes.Black, shadowArea, format);
        g.DrawString(label, font, Brushes.White, textArea, format);
        return bmp;
    }

    private static Bitmap Draw(KeyConfig key)
    {
        var bmp = new Bitmap(Size, Size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Black);

        // Icon randlos auf die volle Tastenfläche
        if (!string.IsNullOrWhiteSpace(key.IconPath) && File.Exists(key.IconPath))
        {
            using var icon = Image.FromFile(key.IconPath);
            g.DrawImage(icon, 0, 0, Size, Size);
        }

        // Beschriftung als Overlay über dem Icon
        if (!string.IsNullOrWhiteSpace(key.Label))
        {
            var style = System.Drawing.FontStyle.Regular;
            if (key.Bold) style |= System.Drawing.FontStyle.Bold;
            if (key.Italic) style |= System.Drawing.FontStyle.Italic;
            using var font = new Font("Segoe UI", Math.Clamp(key.FontSize, 6, 72), style);

            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = key.LabelPosition switch
                {
                    LabelPosition.Top => StringAlignment.Near,
                    LabelPosition.Center => StringAlignment.Center,
                    _ => StringAlignment.Far
                },
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };

            var textArea = new RectangleF(2, 4, Size - 4, Size - 8);
            // Schatten für Lesbarkeit auf hellen Icons, dann Text in Weiß
            var shadowArea = textArea;
            shadowArea.Offset(1, 1);
            g.DrawString(key.Label, font, Brushes.Black, shadowArea, format);
            g.DrawString(key.Label, font, Brushes.White, textArea, format);
        }

        return bmp;
    }
}
