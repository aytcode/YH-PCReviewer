#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishTrimmed=false
#:property PublishAot=false

// logo.svg ile aynı geometriden PNG'leri ve çok boyutlu app.ico'yu üretir.
// Kullanım: dotnet run branding/render-logo.cs -- <çıktı klasörü> <ico yolu>
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

var outDir = args.Length > 0 ? args[0] : "branding";
var icoPath = args.Length > 1 ? args[1] : Path.Combine(outDir, "app.ico");
Directory.CreateDirectory(outDir);

var thread = new Thread(() =>
{
    const string Tile = "M22,0 L78,0 C90.15,0 100,9.85 100,22 L100,78 C100,90.15 90.15,100 78,100 L22,100 C9.85,100 0,90.15 0,78 L0,22 C0,9.85 9.85,0 22,0 Z";
    const string Mark = "F0 M10,18 L44,38 L50,56 L56,38 L90,18 L84,50 L70,57 L56,84 L50,90 L44,84 L30,57 L16,50 Z M20,30 L40,42 L43,52 L25,45 Z M80,30 L60,42 L57,52 L75,45 Z";
    const string Spark = "M50,23 C50.8,29.5 53,32 59,33 C53,34 50.8,36.5 50,43 C49.2,36.5 47,34 41,33 C47,32 49.2,29.5 50,23 Z";

    static LinearGradientBrush V(params (double o, string c)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (o, c) in stops) b.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c), o));
        return b;
    }

    var tileBrush = V((0, "#121A30"), (1, "#05070D"));
    var markBrush = V((0, "#46DBFF"), (0.55, "#1A8CFF"), (1, "#0A4FE0"));
    var sparkBrush = V((0, "#F2FCFF"), (1, "#5FD4FF"));

    byte[] Render(int size, bool withTile)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 100.0, size / 100.0));
            if (withTile) dc.DrawGeometry(tileBrush, null, Geometry.Parse(Tile));
            dc.DrawGeometry(markBrush, null, Geometry.Parse(Mark));
            dc.DrawGeometry(sparkBrush, null, Geometry.Parse(Spark));
        }
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    foreach (var s in new[] { 128, 256, 512, 1024 })
        File.WriteAllBytes(Path.Combine(outDir, $"logo-{s}.png"), Render(s, true));
    File.WriteAllBytes(Path.Combine(outDir, "mark-512.png"), Render(512, false));

    // ICO: PNG gömülü çerçeveler (Vista+).
    int[] sizes = [16, 24, 32, 48, 64, 128, 256];
    var pngs = sizes.Select(s => Render(s, true)).ToArray();
    using var ico = new BinaryWriter(File.Create(icoPath));
    ico.Write((ushort)0); ico.Write((ushort)1); ico.Write((ushort)sizes.Length);
    var offset = 6 + 16 * sizes.Length;
    for (var i = 0; i < sizes.Length; i++)
    {
        var d = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
        ico.Write(d); ico.Write(d); ico.Write((byte)0); ico.Write((byte)0);
        ico.Write((ushort)1); ico.Write((ushort)32);
        ico.Write(pngs[i].Length); ico.Write(offset);
        offset += pngs[i].Length;
    }
    foreach (var p in pngs) ico.Write(p);
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
Console.WriteLine("ok");
