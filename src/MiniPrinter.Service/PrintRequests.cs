using System.Text;
using MiniPrinter.Imaging;
using MiniPrinter.Ipp;

namespace MiniPrinter.Service;

/// <summary>Text printing request shared by the control API, the automation API and the tray.</summary>
public sealed record TextPrintRequest
{
    public string Text { get; init; } = "";
    public float? SizePt { get; init; }
    public string? Font { get; init; }
    public bool Bold { get; init; }
    public TextAlign Align { get; init; } = TextAlign.Left;
    public int? Darkness { get; init; }
    public string? Name { get; init; }
}

public sealed class PrintRequestException(string message, int? block = null, string? property = null) : Exception(message)
{
    public int? Block { get; } = block;
    public string? Property { get; } = property;
}

/// <summary>
/// Common entry points that turn text, files, QR codes and templates into queued jobs
/// (design.md D5). All rendering happens here so every client gets the same result.
/// </summary>
public sealed class PrintRequests
{
    public const int MaxTextLength = 20_000;

    private readonly JobQueue _queue;
    private readonly SettingsStore _settings;
    private readonly PrinterManager _printer;
    private readonly TemplateCatalog _templates;

    public PrintRequests(JobQueue queue, SettingsStore settings, PrinterManager printer, TemplateCatalog templates)
    {
        _queue = queue;
        _settings = settings;
        _printer = printer;
        _templates = templates;
    }

    public Protocol.MonoBitmap RenderText(TextPrintRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new PrintRequestException("The text is empty.");
        if (request.Text.Length > MaxTextLength)
            throw new PrintRequestException($"The text exceeds {MaxTextLength} characters.");
        var settings = _settings.Current;
        var style = new TextStyle
        {
            FontFamily = request.Font ?? settings.TextFont,
            SizePt = request.SizePt ?? settings.TextSizePt,
            Bold = request.Bold,
            Align = request.Align,
        };
        return TextRenderer.Render(request.Text, style, _printer.Profile.WidthPx);
    }

    public JobInfo PrintText(TextPrintRequest request, string user)
    {
        var bitmap = RenderText(request);
        return _queue.SubmitBitmap(request.Name ?? FirstLine(request.Text), bitmap, isText: true, user, request.Darkness);
    }

    public Protocol.MonoBitmap RenderTemplate(string name, IReadOnlyDictionary<string, string> fields) =>
        RenderTemplateDetailed(name, fields).Bitmap;

    /// <summary>Preview of a saved template, with the rows each block occupies.</summary>
    public RenderResult RenderTemplateDetailed(string name, IReadOnlyDictionary<string, string> fields)
    {
        try
        {
            return _templates.RenderDetailed(name, fields, new RenderOptions { Width = _printer.Profile.WidthPx });
        }
        catch (TemplateException ex)
        {
            throw new PrintRequestException(ex.Message, ex.Block, ex.Property);
        }
    }

    /// <summary>
    /// Preview of a template that is not saved (editor). Lenient: a required field without a value is not
    /// an error. Images are looked up in the draft first, then in the saved template's folder.
    /// </summary>
    public RenderResult PreviewDraft(string json, IReadOnlyDictionary<string, string> fields, string? draftDirectory)
    {
        try
        {
            var layout = _templates.ValidateDraft(json, draftDirectory);
            return _templates.RenderLayout(layout, fields, new RenderOptions
            {
                Width = _printer.Profile.WidthPx,
                Lenient = true,
                AssetsDir = draftDirectory,
                FallbackAssetsDir = _templates.AssetsDirectory(layout.Name),
            });
        }
        catch (TemplateException ex)
        {
            throw new PrintRequestException(ex.Message, ex.Block, ex.Property);
        }
    }

    /// <summary>
    /// Prints a template as one queued job. <paramref name="rows"/> prints one label per row (each row
    /// overrides the base fields) and <paramref name="copies"/> repeats every label in a row; counters
    /// advance once per distinct label. Nothing is printed (and no counter consumed) if any row is invalid.
    /// </summary>
    public JobInfo PrintTemplate(string name, IReadOnlyDictionary<string, string> fields, string user, int? darkness = null,
        int copies = 1, IReadOnlyList<IReadOnlyDictionary<string, string>>? rows = null)
    {
        var definition = _templates.Find(name) ?? throw new PrintRequestException(
            $"Unknown template '{name}'. Available: {string.Join(", ", _templates.Definitions.Select(d => d.Name))}.");
        try
        {
            var pages = _templates.RenderBatch(name, fields, rows, copies, new RenderOptions { Width = _printer.Profile.WidthPx });
            return _queue.SubmitBitmaps(definition.Title, pages, isText: !definition.IsImage, user, darkness);
        }
        catch (TemplateException ex)
        {
            throw new PrintRequestException(ex.Message, ex.Block, ex.Property);
        }
    }

    public JobInfo PrintQr(string data, string? caption, string user, int? darkness = null) =>
        PrintTemplate("qr", caption is null ? new Dictionary<string, string> { ["data"] = data }
            : new Dictionary<string, string> { ["data"] = data, ["caption"] = caption }, user, darkness);

    /// <summary>Prints a file: TXT is rendered as text, other formats go through the document pipeline.</summary>
    public JobInfo PrintFile(byte[] content, string? fileName, string? contentType, string user, int? darkness = null)
    {
        if (content.Length == 0)
            throw new PrintRequestException("The file is empty.");
        var name = string.IsNullOrWhiteSpace(fileName) ? "Document" : Path.GetFileName(fileName);
        if (IsText(fileName, contentType))
            return PrintText(new TextPrintRequest { Text = DecodeText(content), Name = name, Darkness = darkness }, user);

        var job = _queue.CreateJob(name, user);
        var format = contentType is "image/png" or "image/jpeg" or "image/pwg-raster" or "application/pdf" ? contentType : null;
        try
        {
            return _queue.SubmitDocument(job.Id, new MemoryStream(content), format, lastDocument: true, darkness);
        }
        catch (NotSupportedException ex)
        {
            _queue.CancelJob(job.Id);
            throw new PrintRequestException(ex.Message);
        }
    }

    private static bool IsText(string? fileName, string? contentType) =>
        contentType?.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase) == true
        || string.Equals(Path.GetExtension(fileName), ".txt", StringComparison.OrdinalIgnoreCase);

    /// <summary>UTF-8 (with or without BOM), falling back to Windows-1252 for legacy Notepad files.</summary>
    private static string DecodeText(byte[] content)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(content).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(content);
        }
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', 2)[0].Trim();
        return line.Length == 0 ? "Text" : line.Length > 40 ? line[..40] + "…" : line;
    }
}
