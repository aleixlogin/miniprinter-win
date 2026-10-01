using System.Text.Json.Serialization;

namespace MiniPrinter.Protocol.Catalog;

/// <summary>Row encoding a profile uses for image data.</summary>
public enum RowEncoding
{
    /// <summary>Always send uncompressed <c>0xA2</c> rows.</summary>
    Raw,
    /// <summary>Send <c>0xBF</c> RLE rows when they fit in one packed row, otherwise <c>0xA2</c>.</summary>
    Rle,
}

/// <summary>Energy per darkness band, as used by the "tiny" protocol family.</summary>
public sealed record LevelProfile(int Low, int Middle, int High)
{
    /// <summary>Darkness 1..2 selects low, 3 middle, 4..5 high (values outside 1..5 are clamped).</summary>
    public int Select(int darkness)
    {
        var level = Math.Clamp(darkness, 1, 5);
        return level <= 2 ? Low : level >= 4 ? High : Middle;
    }
}

public sealed record ModeLevels(LevelProfile Image, LevelProfile Text)
{
    public int Select(bool isText, int darkness) => (isText ? Text : Image).Select(darkness);
}

public sealed record ModeSpeeds(int Image, int Text)
{
    public int Select(bool isText) => isText ? Text : Image;
}

/// <summary>Printing parameters for one printer profile of the "tiny" protocol family.</summary>
public sealed record PrinterProfile
{
    public required string Key { get; init; }
    public int Dpi { get; init; } = 200;

    /// <summary>Width, in dots, that rendered pages are scaled to.</summary>
    public int WidthPx { get; init; } = 384;

    /// <summary>Physical print head width in dots.</summary>
    public int PaperWidthPx { get; init; } = 384;

    /// <summary>True when the printer is reached over Bluetooth Classic SPP instead of BLE.</summary>
    public bool UseSpp { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<RowEncoding>))]
    public RowEncoding Encoding { get; init; } = RowEncoding.Rle;

    /// <summary>Maximum bytes per transport write.</summary>
    public int ChunkSize { get; init; } = 180;

    /// <summary>Pause between transport writes.</summary>
    public int DelayMs { get; init; } = 4;

    /// <summary>Number of <c>0xA1</c> paper-advance packets appended after each page.</summary>
    public int PostPrintFeedCount { get; init; } = 2;

    public required ModeSpeeds Speed { get; init; }
    public required ModeLevels Energy { get; init; }

    public int WidthBytes => (WidthPx + 7) / 8;
}
