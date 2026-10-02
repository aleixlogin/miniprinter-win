// Generates assets/miniprinter.ico (and a preview PNG) from vector drawing code.
//   dotnet run --project tools/IconGen -- [output-directory]
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

var outputDir = args.Length > 0 ? args[0] : System.IO.Path.Combine(FindRepoRoot(), "assets");
Directory.CreateDirectory(outputDir);

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];
var pngs = sizes.Select(size => (Size: size, Png: Render(size))).ToList();

var icoPath = System.IO.Path.Combine(outputDir, "miniprinter.ico");
WriteIco(icoPath, pngs);
Console.WriteLine($"{icoPath} ({string.Join(", ", sizes)} px)");

// Preview sheet with every size on a light and a dark background.
using (var sheet = new Image<Rgba32>(sizes.Sum(s => s + 24) + 24, 2 * (256 + 48), new Rgba32(255, 255, 255)))
{
    sheet.Mutate(c => c.Fill(Color.ParseHex("1F2328"), new RectangleF(0, 256 + 48, sheet.Width, 256 + 48)));
    foreach (var row in new[] { 24, 256 + 48 + 24 })
    {
        var x = 24;
        foreach (var (size, png) in pngs)
        {
            using var icon = Image.Load<Rgba32>(png);
            sheet.Mutate(c => c.DrawImage(icon, new Point(x, row + (256 - size)), 1f));
            x += size + 24;
        }
    }
    var preview = System.IO.Path.Combine(outputDir, "miniprinter-icon-preview.png");
    sheet.SaveAsPng(preview);
    Console.WriteLine(preview);
}

static byte[] Render(int s)
{
    var body = Color.ParseHex("2B3440");
    var bodyTop = Color.ParseHex("3D4856");
    var ink = Color.ParseHex("2B3440");
    var paper = Color.White;
    var slot = Color.ParseHex("11161C");
    var led = Color.ParseHex("2EA043");
    float u = s / 32f;                   // design unit: the motif is drawn on a 32×32 grid
    var stroke = Math.Max(1f, 1.4f * u);

    using var image = new Image<Rgba32>(s, s, Color.Transparent);
    image.Mutate(c =>
    {
        c.SetGraphicsOptions(o => o.Antialias = s >= 20);

        // Printer body (rounded) with a lighter top band.
        var bodyRect = new RectangleF(2 * u, 13 * u, 28 * u, 15.5f * u);
        c.Fill(body, RoundedRect(bodyRect, 3.5f * u));
        c.Fill(bodyTop, RoundedRect(new RectangleF(2 * u, 13 * u, 28 * u, 5 * u), 3.5f * u));
        // Light outline so the dark body stays visible on dark taskbars and title bars.
        c.Draw(Color.ParseHex("7D8794"), Math.Max(1f, 0.9f * u), RoundedRect(bodyRect, 3.5f * u));

        // Receipt coming out of the slot: a white strip from the top down into the body.
        var paperRect = new RectangleF(8.5f * u, 2.5f * u, 15 * u, 15 * u);
        IPath paperShape = s >= 48 ? TornTop(paperRect, 1.2f * u) : new RectangularPolygon(paperRect);
        c.Fill(paper, paperShape);
        c.Draw(ink, stroke * 0.8f, paperShape);

        // Printed lines on the receipt (only where they can be resolved).
        if (s >= 24)
        {
            var lineThickness = Math.Max(1f, 1.3f * u);
            float[] lineWidths = s >= 40 ? [10f, 7.5f, 9f] : [10f, 7f];
            for (var i = 0; i < lineWidths.Length; i++)
            {
                var y = (6.3f + i * 2.9f) * u;
                c.Fill(ink, new RectangleF(11 * u, y, lineWidths[i] * u, lineThickness));
            }
        }
        else
        {
            c.Fill(ink, new RectangleF(11 * u, 7 * u, 10 * u, Math.Max(1f, 1.5f * u)));
        }

        // Paper slot across the body.
        c.Fill(slot, new RectangleF(6 * u, 16.5f * u, 20 * u, Math.Max(1f, 1.6f * u)));

        // Status LED (neutral green) and a small button, from 32 px.
        if (s >= 32)
        {
            c.Fill(led, new EllipsePolygon(24.5f * u, 23.5f * u, 1.7f * u));
            c.Fill(Color.ParseHex("56616E"), new RectangleF(6 * u, 22.5f * u, 7 * u, 2 * u));
        }
    });

    using var stream = new MemoryStream();
    image.SaveAsPng(stream);
    return stream.ToArray();
}

static IPath RoundedRect(RectangleF r, float radius)
{
    var builder = new PathBuilder();
    builder.AddArc(new PointF(r.Left + radius, r.Top + radius), radius, radius, 0, 180, 90);
    builder.AddArc(new PointF(r.Right - radius, r.Top + radius), radius, radius, 0, 270, 90);
    builder.AddArc(new PointF(r.Right - radius, r.Bottom - radius), radius, radius, 0, 0, 90);
    builder.AddArc(new PointF(r.Left + radius, r.Bottom - radius), radius, radius, 0, 90, 90);
    builder.CloseFigure();
    return builder.Build();
}

/// <summary>Rectangle whose top edge is a small zigzag, like a torn receipt.</summary>
static IPath TornTop(RectangleF r, float tooth)
{
    var points = new List<PointF>();
    var teeth = Math.Max(4, (int)(r.Width / (tooth * 2)));
    var step = r.Width / teeth;
    for (var i = 0; i <= teeth; i++)
        points.Add(new PointF(r.Left + i * step, r.Top + (i % 2 == 0 ? 0 : tooth)));
    points.Add(new PointF(r.Right, r.Bottom));
    points.Add(new PointF(r.Left, r.Bottom));
    return new Polygon(points.ToArray());
}

/// <summary>
/// Writes an .ico the conventional way: 32-bit BMP (DIB) entries up to 64 px and a PNG entry for
/// 256 px. Some Windows components (thumbnail/icon extractors) expect DIB data for small sizes.
/// </summary>
static void WriteIco(string path, IReadOnlyList<(int Size, byte[] Png)> images)
{
    var entries = images.Select(i => (i.Size, Data: i.Size >= 256 ? i.Png : ToDib(i.Png, i.Size))).ToList();
    using var file = File.Create(path);
    using var w = new BinaryWriter(file);
    w.Write((ushort)0);              // reserved
    w.Write((ushort)1);              // type: icon
    w.Write((ushort)entries.Count);
    var offset = 6 + 16 * entries.Count;
    foreach (var (size, data) in entries)
    {
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)0);            // palette colours
        w.Write((byte)0);            // reserved
        w.Write((ushort)1);          // colour planes
        w.Write((ushort)32);         // bits per pixel
        w.Write(data.Length);
        w.Write(offset);
        offset += data.Length;
    }
    foreach (var (_, data) in entries)
        w.Write(data);
}

/// <summary>BITMAPINFOHEADER + bottom-up BGRA pixels + 1-bit AND mask, as stored in .ico files.</summary>
static byte[] ToDib(byte[] png, int size)
{
    using var image = Image.Load<Rgba32>(png);
    using var stream = new MemoryStream();
    using var w = new BinaryWriter(stream);
    var maskStride = ((size + 31) / 32) * 4;
    w.Write(40);                     // header size
    w.Write(size);                   // width
    w.Write(size * 2);               // height: colour + mask
    w.Write((ushort)1);              // planes
    w.Write((ushort)32);             // bits per pixel
    w.Write(0);                      // BI_RGB
    w.Write(size * size * 4 + maskStride * size);
    w.Write(0); w.Write(0); w.Write(0); w.Write(0);
    for (var y = size - 1; y >= 0; y--)
    for (var x = 0; x < size; x++)
    {
        var p = image[x, y];
        w.Write(p.B); w.Write(p.G); w.Write(p.R); w.Write(p.A);
    }
    for (var y = size - 1; y >= 0; y--)
    {
        var row = new byte[maskStride];
        for (var x = 0; x < size; x++)
            if (image[x, y].A == 0)
                row[x / 8] |= (byte)(0x80 >> (x % 8));
        w.Write(row);
    }
    return stream.ToArray();
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "MiniPrinter.slnx")))
        dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
