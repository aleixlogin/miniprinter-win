using System.Diagnostics;

namespace MiniPrinter.Ipp;

/// <summary>
/// Minimal IPP Everywhere printer: answers the operations the Windows IPP Class Driver, CUPS,
/// iOS and Android use, and forwards jobs to an <see cref="IPrintBackend"/>.
/// </summary>
public sealed class IppPrinterService
{
    public const long MaxDocumentBytes = 64L * 1024 * 1024;

    private static readonly short[] SupportedOperations =
    [
        IppOperation.PrintJob, IppOperation.ValidateJob, IppOperation.CreateJob, IppOperation.SendDocument,
        IppOperation.CancelJob, IppOperation.GetJobAttributes, IppOperation.GetJobs,
        IppOperation.GetPrinterAttributes, IppOperation.CloseJob, IppOperation.IdentifyPrinter,
    ];

    private readonly IppPrinterDescription _description;
    private readonly IPrintBackend _backend;
    private readonly Stopwatch _upTime = Stopwatch.StartNew();
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;

    public IppPrinterService(IppPrinterDescription description, IPrintBackend backend)
    {
        _description = description;
        _backend = backend;
    }

    public IppPrinterDescription Description => _description;

    private int UpTime => (int)Math.Max(1, _upTime.Elapsed.TotalSeconds);

    /// <summary>
    /// Handles one request. <paramref name="document"/> is the request body positioned after the
    /// attributes; <paramref name="authority"/> is the "host:port" the client used.
    /// </summary>
    public async Task<IppMessage> HandleAsync(IppMessage request, Stream document, string authority, CancellationToken cancellationToken)
    {
        if (request.VersionMajor is not (1 or 2))
            return IppMessage.Response(request, IppStatus.ServerErrorVersionNotSupported, "Only IPP/1.1 and IPP/2.0 are supported.");

        var printerUri = $"ipp://{authority}{_description.ResourcePath}";
        try
        {
            return request.Code switch
            {
                IppOperation.GetPrinterAttributes => GetPrinterAttributes(request, printerUri),
                IppOperation.ValidateJob => ValidateJob(request),
                IppOperation.PrintJob => await PrintJobAsync(request, document, printerUri, cancellationToken),
                IppOperation.CreateJob => CreateJob(request, printerUri),
                IppOperation.SendDocument => await SendDocumentAsync(request, document, printerUri, cancellationToken),
                IppOperation.CancelJob => CancelJob(request),
                IppOperation.GetJobAttributes => GetJobAttributes(request, printerUri),
                IppOperation.GetJobs => GetJobs(request, printerUri),
                IppOperation.CloseJob => IppMessage.Response(request, IppStatus.SuccessfulOk),
                IppOperation.IdentifyPrinter => await IdentifyAsync(request, cancellationToken),
                _ => IppMessage.Response(request, IppStatus.ServerErrorOperationNotSupported, $"Operation 0x{request.Code:X4} is not supported."),
            };
        }
        catch (NotSupportedException ex)
        {
            return IppMessage.Response(request, IppStatus.ClientErrorDocumentFormatNotSupported, ex.Message);
        }
        catch (DocumentTooLargeException ex)
        {
            return IppMessage.Response(request, IppStatus.ClientErrorNotPossible, ex.Message);
        }
    }

    private IppMessage GetPrinterAttributes(IppMessage request, string printerUri)
    {
        var requested = request.Operation("requested-attributes")?.Values.Select(v => v.AsString()).ToHashSet()
                        ?? ["all"];
        var response = IppMessage.Response(request, IppStatus.SuccessfulOk);
        var group = response.GetOrAddGroup(IppGroupTag.Printer);
        foreach (var attribute in PrinterAttributes(printerUri))
        {
            if (IsRequested(attribute.Name, requested))
                group.Attributes.Add(attribute);
        }
        return response;
    }

    private static bool IsRequested(string name, HashSet<string> requested)
    {
        if (requested.Contains(name))
            return true;
        // RFC 8011 §4.2.5.1: "all" does not include media-col-database.
        if (name == "media-col-database")
            return false;
        return requested.Contains("all") || requested.Contains("printer-description") || requested.Contains("job-template");
    }

    /// <summary>All printer attributes (IPP Everywhere / PWG 5100.14 required set plus extras).</summary>
    public IEnumerable<IppAttribute> PrinterAttributes(string printerUri)
    {
        var d = _description;
        var snapshot = _backend.GetPrinter();
        var media = d.Media;

        IppAttribute A(string name, params IppValue[] values) => new(name, values);
        IppValue[] Keywords(IEnumerable<string> values) => values.Select(IppValue.Keyword).ToArray();

        yield return A("charset-configured", IppValue.Charset("utf-8"));
        yield return A("charset-supported", IppValue.Charset("utf-8"));
        yield return A("color-supported", IppValue.Boolean(false));
        yield return A("compression-supported", IppValue.Keyword("none"));
        yield return A("copies-default", IppValue.Integer(1));
        yield return A("copies-supported", IppValue.Range(1, 1));
        yield return A("document-format-default", IppValue.Mime("application/octet-stream"));
        yield return A("document-format-supported", d.DocumentFormats.Select(IppValue.Mime).ToArray());
        yield return A("generated-natural-language-supported", IppValue.Language("en"));
        yield return A("identify-actions-default", IppValue.Keyword("display"));
        yield return A("identify-actions-supported", IppValue.Keyword("display"));
        yield return A("ipp-features-supported", IppValue.Keyword("ipp-everywhere"));
        yield return A("ipp-versions-supported", IppValue.Keyword("1.1"), IppValue.Keyword("2.0"));
        yield return A("job-creation-attributes-supported", Keywords(["copies", "media", "media-col", "orientation-requested",
            "page-ranges", "print-color-mode", "print-quality", "printer-resolution", "sides"]));
        yield return A("job-ids-supported", IppValue.Boolean(true));
        yield return A("media-bottom-margin-supported", IppValue.Integer(0));
        yield return A("media-left-margin-supported", IppValue.Integer(0));
        yield return A("media-right-margin-supported", IppValue.Integer(0));
        yield return A("media-top-margin-supported", IppValue.Integer(0));
        yield return A("media-col-default", IppValue.Collection(MediaCol(d.DefaultMedia)));
        yield return A("media-col-ready", IppValue.Collection(MediaCol(d.DefaultMedia)));
        yield return A("media-col-database", media.Select(m => IppValue.Collection(MediaCol(m))).ToArray());
        yield return A("media-col-supported", Keywords(["media-bottom-margin", "media-left-margin", "media-right-margin",
            "media-size", "media-source", "media-top-margin", "media-type"]));
        yield return A("media-default", IppValue.Keyword(d.DefaultMedia.Name));
        yield return A("media-ready", IppValue.Keyword(d.DefaultMedia.Name));
        yield return A("media-supported", media.Select(m => IppValue.Keyword(m.Name)).ToArray());
        yield return A("media-size-supported", media.Select(m => IppValue.Collection(MediaSizeCol(m))).ToArray());
        yield return A("media-source-default", IppValue.Keyword("main-roll"));
        yield return A("media-source-supported", IppValue.Keyword("main-roll"));
        yield return A("media-type-default", IppValue.Keyword("continuous"));
        yield return A("media-type-supported", IppValue.Keyword("continuous"));
        yield return A("multiple-document-handling-supported", Keywords(["separate-documents-uncollated-copies"]));
        yield return A("multiple-document-jobs-supported", IppValue.Boolean(false));
        yield return A("multiple-operation-time-out", IppValue.Integer(60));
        yield return A("natural-language-configured", IppValue.Language("en"));
        yield return A("operations-supported", SupportedOperations.Select(o => IppValue.Enum(o)).ToArray());
        yield return A("orientation-requested-default", IppValue.Enum(3));
        yield return A("orientation-requested-supported", IppValue.Enum(3), IppValue.Enum(4));
        yield return A("output-bin-default", IppValue.Keyword("face-up"));
        yield return A("output-bin-supported", IppValue.Keyword("face-up"));
        yield return A("pages-per-minute", IppValue.Integer(2));
        yield return A("pdl-override-supported", IppValue.Keyword("attempted"));
        yield return A("page-ranges-supported", IppValue.Boolean(true));
        yield return A("print-color-mode-default", IppValue.Keyword("monochrome"));
        yield return A("print-color-mode-supported", IppValue.Keyword("monochrome"), IppValue.Keyword("auto"));
        yield return A("print-quality-default", IppValue.Enum(4));
        yield return A("print-quality-supported", IppValue.Enum(3), IppValue.Enum(4), IppValue.Enum(5));
        yield return A("printer-config-change-time", IppValue.Integer(1));
        yield return A("printer-current-time", new IppValue(IppTag.DateTime, DateTime(DateTimeOffset.UtcNow)));
        yield return A("printer-device-id", IppValue.Text(d.DeviceId));
        yield return A("printer-info", IppValue.Text(d.Info));
        yield return A("printer-is-accepting-jobs", IppValue.Boolean(snapshot.AcceptingJobs));
        yield return A("printer-kind", IppValue.Keyword("document"), IppValue.Keyword("receipt"), IppValue.Keyword("label"));
        yield return A("printer-location", IppValue.Text(d.Location));
        yield return A("printer-make-and-model", IppValue.Text(d.MakeAndModel));
        yield return A("printer-more-info", IppValue.Uri(printerUri.Replace("ipp://", "http://", StringComparison.Ordinal)));
        yield return A("printer-name", IppValue.Name(d.Name));
        yield return A("printer-resolution-default", IppValue.Resolution(d.Dpi));
        yield return A("printer-resolution-supported", IppValue.Resolution(d.Dpi));
        yield return A("printer-state", IppValue.Enum((int)snapshot.State));
        yield return A("printer-state-change-time", IppValue.Integer(UpTime));
        yield return A("printer-state-message", IppValue.Text(snapshot.StateMessage ?? ""));
        yield return A("printer-state-reasons", Keywords(snapshot.StateReasons.Count > 0 ? snapshot.StateReasons : ["none"]));
        yield return A("printer-up-time", IppValue.Integer(UpTime));
        yield return A("printer-uri-supported", IppValue.Uri(printerUri));
        yield return A("printer-uuid", IppValue.Uri($"urn:uuid:{d.Uuid}"));
        yield return A("pwg-raster-document-resolution-supported", IppValue.Resolution(d.Dpi));
        yield return A("pwg-raster-document-sheet-back", IppValue.Keyword("normal"));
        yield return A("pwg-raster-document-type-supported", IppValue.Keyword("black_1"), IppValue.Keyword("sgray_8"));
        yield return A("queued-job-count", IppValue.Integer(snapshot.QueuedJobs));
        yield return A("sides-default", IppValue.Keyword("one-sided"));
        yield return A("sides-supported", IppValue.Keyword("one-sided"));
        yield return A("uri-authentication-supported", IppValue.Keyword("none"));
        yield return A("uri-security-supported", IppValue.Keyword("none"));
        yield return A("which-jobs-supported", IppValue.Keyword("completed"), IppValue.Keyword("not-completed"));
    }

    private static IppCollection MediaSizeCol(MediaSize m) =>
    [
        new("x-dimension", IppValue.Integer(m.Width)),
        new("y-dimension", IppValue.Integer(m.Length)),
    ];

    private static IppCollection MediaCol(MediaSize m) =>
    [
        new("media-size", IppValue.Collection(MediaSizeCol(m))),
        new("media-size-name", IppValue.Keyword(m.Name)),
        new("media-bottom-margin", IppValue.Integer(0)),
        new("media-left-margin", IppValue.Integer(0)),
        new("media-right-margin", IppValue.Integer(0)),
        new("media-top-margin", IppValue.Integer(0)),
        new("media-source", IppValue.Keyword("main-roll")),
        new("media-type", IppValue.Keyword("continuous")),
    ];

    private IppMessage ValidateJob(IppMessage request)
    {
        var format = request.Operation("document-format")?.Value.AsString();
        if (format is not null && !_description.DocumentFormats.Contains(format))
        {
            var response = IppMessage.Response(request, IppStatus.ClientErrorDocumentFormatNotSupported, $"{format} is not supported.");
            response.GetOrAddGroup(IppGroupTag.Unsupported).Add("document-format", IppValue.Mime(format));
            return response;
        }
        return IppMessage.Response(request, IppStatus.SuccessfulOk);
    }

    private async Task<IppMessage> PrintJobAsync(IppMessage request, Stream document, string printerUri, CancellationToken ct)
    {
        var validation = ValidateJob(request);
        if (validation.Code != IppStatus.SuccessfulOk)
            return validation;

        var buffered = await BufferAsync(document, ct);
        var job = _backend.CreateJob(JobName(request), UserName(request));
        job = _backend.SubmitDocument(job.Id, buffered, request.Operation("document-format")?.Value.AsString(), lastDocument: true, DocumentOptionsOf(request));
        return JobResponse(request, job, printerUri);
    }

    private IppMessage CreateJob(IppMessage request, string printerUri)
    {
        var job = _backend.CreateJob(JobName(request), UserName(request));
        return JobResponse(request, job, printerUri);
    }

    private async Task<IppMessage> SendDocumentAsync(IppMessage request, Stream document, string printerUri, CancellationToken ct)
    {
        if (JobId(request) is not { } id || _backend.GetJob(id) is not { } job)
            return IppMessage.Response(request, IppStatus.ClientErrorNotFound, "Job not found.");
        if (job.IsTerminal)
            return IppMessage.Response(request, IppStatus.ClientErrorNotPossible, "Job is already finished.");

        var format = request.Operation("document-format")?.Value.AsString();
        if (format is not null && !_description.DocumentFormats.Contains(format))
            return IppMessage.Response(request, IppStatus.ClientErrorDocumentFormatNotSupported, $"{format} is not supported.");

        var last = request.Operation("last-document")?.Value.Value as bool? ?? true;
        var buffered = await BufferAsync(document, ct);
        job = _backend.SubmitDocument(id, buffered, format, last, DocumentOptionsOf(request));
        return JobResponse(request, job, printerUri);
    }

    private IppMessage CancelJob(IppMessage request)
    {
        if (JobId(request) is not { } id || _backend.GetJob(id) is null)
            return IppMessage.Response(request, IppStatus.ClientErrorNotFound, "Job not found.");
        return _backend.CancelJob(id)
            ? IppMessage.Response(request, IppStatus.SuccessfulOk)
            : IppMessage.Response(request, IppStatus.ClientErrorNotPossible, "Job is already finished.");
    }

    private IppMessage GetJobAttributes(IppMessage request, string printerUri)
    {
        if (JobId(request) is not { } id || _backend.GetJob(id) is not { } job)
            return IppMessage.Response(request, IppStatus.ClientErrorNotFound, "Job not found.");
        var response = IppMessage.Response(request, IppStatus.SuccessfulOk);
        response.Groups.Add(new IppGroup(IppGroupTag.Job, JobAttributes(job, printerUri)));
        return response;
    }

    private IppMessage GetJobs(IppMessage request, string printerUri)
    {
        var which = request.Operation("which-jobs")?.Value.AsString() ?? "not-completed";
        var limit = request.Operation("limit")?.Value.Value as int? ?? 100;
        var response = IppMessage.Response(request, IppStatus.SuccessfulOk);
        foreach (var job in _backend.GetJobs(completed: which == "completed", limit))
            response.Groups.Add(new IppGroup(IppGroupTag.Job, JobAttributes(job, printerUri)));
        return response;
    }

    private async Task<IppMessage> IdentifyAsync(IppMessage request, CancellationToken ct)
    {
        await _backend.IdentifyAsync(ct);
        return IppMessage.Response(request, IppStatus.SuccessfulOk);
    }

    private IppMessage JobResponse(IppMessage request, JobInfo job, string printerUri)
    {
        var response = IppMessage.Response(request, IppStatus.SuccessfulOk);
        response.GetOrAddGroup(IppGroupTag.Job)
            .Add("job-id", IppValue.Integer(job.Id))
            .Add("job-uri", IppValue.Uri($"{printerUri}/{job.Id}"))
            .Add("job-state", IppValue.Enum((int)job.State))
            .Add("job-state-reasons", job.StateReasons.Select(IppValue.Keyword).ToArray());
        return response;
    }

    private IEnumerable<IppAttribute> JobAttributes(JobInfo job, string printerUri)
    {
        int Time(DateTimeOffset? t) => t is null ? 0 : (int)Math.Max(1, (t.Value - _started).TotalSeconds);
        yield return new("job-id", IppValue.Integer(job.Id));
        yield return new("job-uri", IppValue.Uri($"{printerUri}/{job.Id}"));
        yield return new("job-printer-uri", IppValue.Uri(printerUri));
        yield return new("job-name", IppValue.Name(job.Name));
        yield return new("job-originating-user-name", IppValue.Name(job.UserName));
        yield return new("job-state", IppValue.Enum((int)job.State));
        yield return new("job-state-reasons", job.StateReasons.Select(IppValue.Keyword).ToArray());
        yield return new("job-state-message", IppValue.Text(job.StateMessage ?? ""));
        yield return new("job-printer-up-time", IppValue.Integer(UpTime));
        yield return new("time-at-creation", IppValue.Integer(Time(job.Created)));
        yield return new("time-at-processing", job.Processing is null ? IppValue.NoValue : IppValue.Integer(Time(job.Processing)));
        yield return new("time-at-completed", job.Completed is null ? IppValue.NoValue : IppValue.Integer(Time(job.Completed)));
        yield return new("job-impressions-completed", IppValue.Integer(job.PagesCompleted));
        yield return new("job-k-octets", IppValue.Integer((int)((job.SizeBytes + 1023) / 1024)));
    }

    /// <summary>Reads <c>page-ranges</c> from the job (IPP/2.0) or operation group.</summary>
    private static DocumentOptions DocumentOptionsOf(IppMessage request)
    {
        var attribute = request.Group(IppGroupTag.Job)?["page-ranges"] ?? request.Operation("page-ranges");
        var ranges = attribute?.Values.Select(v => v.Value).OfType<IppRange>().ToList();
        return new DocumentOptions { PageRanges = ranges is { Count: > 0 } ? ranges : null };
    }

    private static string JobName(IppMessage request) =>
        request.Operation("job-name")?.Value.Value as string
        ?? request.Operation("document-name")?.Value.Value as string
        ?? "Untitled";

    private static string UserName(IppMessage request) =>
        request.Operation("requesting-user-name")?.Value.Value as string ?? "anonymous";

    private static int? JobId(IppMessage request)
    {
        if (request.Operation("job-id")?.Value.Value is int id)
            return id;
        var uri = request.Operation("job-uri")?.Value.Value as string;
        var tail = uri?[(uri.LastIndexOf('/') + 1)..];
        return int.TryParse(tail, out var parsed) ? parsed : null;
    }

    private static async Task<Stream> BufferAsync(Stream document, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await document.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxDocumentBytes)
                throw new DocumentTooLargeException($"Documents are limited to {MaxDocumentBytes / (1024 * 1024)} MB.");
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;
        return buffer;
    }

    /// <summary>RFC 2579 DateAndTime (11 bytes, UTC).</summary>
    private static byte[] DateTime(DateTimeOffset t)
    {
        var u = t.UtcDateTime;
        return
        [
            (byte)(u.Year >> 8), (byte)u.Year, (byte)u.Month, (byte)u.Day, (byte)u.Hour, (byte)u.Minute,
            (byte)u.Second, (byte)(u.Millisecond / 100), (byte)'+', 0, 0,
        ];
    }

    private sealed class DocumentTooLargeException(string message) : Exception(message);
}
