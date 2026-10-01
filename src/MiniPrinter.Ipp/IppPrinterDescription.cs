namespace MiniPrinter.Ipp;

/// <summary>A media size offered to clients (dimensions in hundredths of millimetres).</summary>
public sealed record MediaSize(string Name, int Width, int Length);

/// <summary>Static description of the printer advertised over IPP and mDNS.</summary>
public sealed record IppPrinterDescription
{
    public string Name { get; init; } = "X5h Thermal Printer";
    public string MakeAndModel { get; init; } = "MiniPrinter X5h Thermal Printer";
    public string Info { get; init; } = "X5h thermal printer (Bluetooth)";
    public string Location { get; init; } = "";
    public Guid Uuid { get; init; } = Guid.Parse("4a8e2d3c-5b6f-4e1a-9c7d-58e3a1f0b2c4");

    /// <summary>Resource path of the printer, e.g. "/ipp/print".</summary>
    public string ResourcePath { get; init; } = "/ipp/print";

    public int Dpi { get; init; } = 203;

    public IReadOnlyList<string> DocumentFormats { get; init; } =
        ["image/pwg-raster", "image/jpeg", "image/png", "application/octet-stream"];

    /// <summary>
    /// 48 mm sizes (the printable width of a 58 mm roll, 384 dots) render 1:1 at 203 dpi; 48×210 is
    /// the default. The 80 mm sizes are virtual pages for apps with wide margins: the service crops
    /// the blank sides and fits the content to the head, never enlarging it.
    /// </summary>
    public IReadOnlyList<MediaSize> Media { get; init; } =
    [
        new("om_x5h-48x210mm_48x210mm", 4800, 21000),
        new("om_x5h-48x100mm_48x100mm", 4800, 10000),
        new("om_x5h-48x50mm_48x50mm", 4800, 5000),
        new("om_x5h-48x297mm_48x297mm", 4800, 29700),
        new("om_x5h-80x297mm_80x297mm", 8000, 29700),
        new("om_x5h-80x100mm_80x100mm", 8000, 10000),
    ];

    public MediaSize DefaultMedia => Media[0];

    public string DeviceId =>
        $"MFG:MiniPrinter;MDL:{Name};CMD:PWGRaster,JPEG,PNG;CLS:PRINTER;DES:{Info};";
}
