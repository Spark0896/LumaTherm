using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LumaTherm.AssetBuilder;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: LumaTherm.AssetBuilder <repository-root>");
            return 2;
        }

        var root = Path.GetFullPath(args[0]);
        var sourcePath = Path.Combine(root, "src", "LumaTherm.App", "Assets", "LogoGeometry.xaml");
        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine($"Logo source not found: {sourcePath}");
            return 2;
        }

        var drawing = LoadDrawing(sourcePath);
        var packaging = Path.Combine(root, "packaging", "Assets");
        var application = Path.Combine(root, "src", "LumaTherm.App", "Assets");
        Directory.CreateDirectory(packaging);
        Directory.CreateDirectory(application);

        WritePng(Path.Combine(packaging, "StoreLogo.png"), RenderPng(drawing, 50, 50, 50));
        WritePng(Path.Combine(packaging, "Square44x44Logo.png"), RenderPng(drawing, 44, 44, 44));
        WritePng(Path.Combine(packaging, "Square150x150Logo.png"), RenderPng(drawing, 150, 150, 150));
        WritePng(Path.Combine(packaging, "Wide310x150Logo.png"), RenderPng(drawing, 310, 150, 150));

        var icon44 = RenderPng(drawing, 44, 44, 44);
        var icon256 = RenderPng(drawing, 256, 256, 256);
        WriteIcon(Path.Combine(application, "LumaTherm.ico"), icon44, icon256);
        Console.WriteLine("Generated deterministic LumaTherm logo assets.");
        return 0;
    }

    private static DrawingGroup LoadDrawing(string sourcePath)
    {
        using var source = File.OpenRead(sourcePath);
        var dictionary = (ResourceDictionary)XamlReader.Load(source);
        return ((DrawingGroup)dictionary["LogoDrawing"]).CloneCurrentValue();
    }

    private static byte[] RenderPng(Drawing drawing, int width, int height, int safeSize)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var left = (width - safeSize) / 2d;
            var top = (height - safeSize) / 2d;
            var scale = safeSize / 48d;
            context.PushTransform(new MatrixTransform(scale, 0, 0, scale, left, top));
            context.DrawDrawing(drawing);
            context.Pop();
        }

        RenderOptions.SetEdgeMode(visual, EdgeMode.Unspecified);
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        var metadata = new BitmapMetadata("png");
        metadata.SetQuery("/tEXt/{str=impeccable:prompt}",
            "Origin: deterministic WPF rendering of src/LumaTherm.App/Assets/LogoGeometry.xaml; " +
            "Arctic two-peak LumaTherm logo, authored vector geometry from the user-selected reference, 2026-10-03.");
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private static void WritePng(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);

    private static void WriteIcon(string path, byte[] icon44, byte[] icon256)
    {
        using var output = File.Create(path);
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)2);
        var firstOffset = 6 + (16 * 2);
        WriteDirectoryEntry(writer, 44, icon44.Length, firstOffset);
        WriteDirectoryEntry(writer, 256, icon256.Length, firstOffset + icon44.Length);
        writer.Write(icon44);
        writer.Write(icon256);
    }

    private static void WriteDirectoryEntry(BinaryWriter writer, int size, int payloadLength, int payloadOffset)
    {
        writer.Write((byte)(size == 256 ? 0 : size));
        writer.Write((byte)(size == 256 ? 0 : size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(payloadLength);
        writer.Write(payloadOffset);
    }
}
