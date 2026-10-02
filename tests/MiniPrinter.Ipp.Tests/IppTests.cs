using System.Net;
using System.Net.Sockets;

namespace MiniPrinter.Ipp.Tests;

internal sealed class FakeBackend : IPrintBackend
{
    private readonly List<JobInfo> _jobs = [];
    public List<(int Id, byte[] Data, string? Format)> Documents { get; } = [];
    public DocumentOptions? LastOptions { get; private set; }
    public PrinterSnapshot Snapshot { get; set; } = new(PrinterState.Idle, ["none"], null, true, 0);
    public int Identified { get; private set; }

    public PrinterSnapshot GetPrinter() => Snapshot;

    public JobInfo CreateJob(string name, string userName)
    {
        var job = new JobInfo { Id = _jobs.Count + 1, Name = name, UserName = userName };
        _jobs.Add(job);
        return job;
    }

    public JobInfo SubmitDocument(int jobId, Stream document, string? format, bool lastDocument, DocumentOptions? options = null)
    {
        LastOptions = options;
        if (format == "application/postscript")
            throw new NotSupportedException("PostScript is not supported.");
        using var memory = new MemoryStream();
        document.CopyTo(memory);
        Documents.Add((jobId, memory.ToArray(), format));
        return Replace(jobId, j => j with { State = JobState.Completed, StateReasons = ["job-completed-successfully"], SizeBytes = memory.Length });
    }

    public JobInfo? GetJob(int jobId) => _jobs.FirstOrDefault(j => j.Id == jobId);

    public IReadOnlyList<JobInfo> GetJobs(bool completed, int limit) =>
        _jobs.Where(j => j.IsTerminal == completed).Take(limit).ToList();

    public bool CancelJob(int jobId)
    {
        var job = GetJob(jobId);
        if (job is null || job.IsTerminal)
            return false;
        Replace(jobId, j => j with { State = JobState.Canceled });
        return true;
    }

    public Task IdentifyAsync(CancellationToken cancellationToken)
    {
        Identified++;
        return Task.CompletedTask;
    }

    private JobInfo Replace(int id, Func<JobInfo, JobInfo> change)
    {
        var index = _jobs.FindIndex(j => j.Id == id);
        _jobs[index] = change(_jobs[index]);
        return _jobs[index];
    }
}

public class IppCodecTests
{
    [Fact]
    public async Task Round_trips_all_value_types_and_nested_collections()
    {
        var message = new IppMessage { Code = IppOperation.GetPrinterAttributes, RequestId = 42 };
        message.GetOrAddGroup(IppGroupTag.Operation)
            .Add("attributes-charset", IppValue.Charset("utf-8"))
            .Add("requested-attributes", IppValue.Keyword("all"), IppValue.Keyword("media-col-database"));
        message.GetOrAddGroup(IppGroupTag.Printer)
            .Add("printer-state", IppValue.Enum(3))
            .Add("color-supported", IppValue.Boolean(false))
            .Add("printer-resolution-default", IppValue.Resolution(203))
            .Add("copies-supported", IppValue.Range(1, 1))
            .Add("time-at-processing", IppValue.NoValue)
            .Add("media-col-database",
                IppValue.Collection([
                    new("media-size", IppValue.Collection([new("x-dimension", IppValue.Integer(4800)), new("y-dimension", IppValue.Integer(21000))])),
                    new("media-source", IppValue.Keyword("main-roll")),
                ]),
                IppValue.Collection([new("media-type", IppValue.Keyword("continuous"))]));

        var bytes = IppCodec.Encode(message);
        var decoded = await IppCodec.ReadAsync(new MemoryStream(bytes));

        Assert.Equal(42, decoded.RequestId);
        Assert.Equal(IppOperation.GetPrinterAttributes, decoded.Code);
        Assert.Equal(["all", "media-col-database"], decoded.Operation("requested-attributes")!.Values.Select(v => v.AsString()));
        var printer = decoded.Group(IppGroupTag.Printer)!;
        Assert.Equal(3, printer["printer-state"]!.Value.AsInt());
        Assert.Equal(false, printer["color-supported"]!.Value.Value);
        Assert.Equal(new IppResolution(203, 203), printer["printer-resolution-default"]!.Value.Value);
        Assert.Equal(new IppRange(1, 1), printer["copies-supported"]!.Value.Value);
        Assert.Equal(IppTag.NoValue, printer["time-at-processing"]!.Value.Tag);

        var database = printer["media-col-database"]!;
        Assert.Equal(2, database.Values.Count);
        var first = (IppCollection)database.Values[0].Value!;
        var size = (IppCollection)first.Single(m => m.Name == "media-size").Value.Value!;
        Assert.Equal(4800, size.Single(m => m.Name == "x-dimension").Value.AsInt());
        Assert.Equal("main-roll", first.Single(m => m.Name == "media-source").Value.AsString());
        Assert.Equal("continuous", ((IppCollection)database.Values[1].Value!)[0].Value.AsString());

        // Re-encoding the decoded message is byte-identical.
        Assert.Equal(bytes, IppCodec.Encode(decoded));
    }

    [Fact]
    public void Text_falls_back_to_latin1_when_not_utf8()
    {
        Assert.Equal("Página de prueba", IppCodec.DecodeText("Página de prueba"u8));
        Assert.Equal("Página de prueba", IppCodec.DecodeText([0x50, 0xE1, .. "gina de prueba"u8]));
    }

    [Fact]
    public async Task Leaves_stream_at_document_data()
    {
        var message = new IppMessage { Code = IppOperation.PrintJob, RequestId = 1 };
        message.GetOrAddGroup(IppGroupTag.Operation).Add("attributes-charset", IppValue.Charset("utf-8"));
        var stream = new MemoryStream([.. IppCodec.Encode(message), 1, 2, 3]);
        await IppCodec.ReadAsync(stream);
        Assert.Equal(3, stream.Length - stream.Position);
    }
}

public class IppPrinterServiceTests
{
    private static IppMessage Request(short operation, Action<IppGroup>? operationAttributes = null)
    {
        var message = new IppMessage { Code = operation, RequestId = 7 };
        var group = message.GetOrAddGroup(IppGroupTag.Operation)
            .Add("attributes-charset", IppValue.Charset("utf-8"))
            .Add("attributes-natural-language", IppValue.Language("en"))
            .Add("printer-uri", IppValue.Uri("ipp://localhost:8631/ipp/print"));
        operationAttributes?.Invoke(group);
        return message;
    }

    private static Task<IppMessage> Handle(IppPrinterService service, IppMessage request, byte[]? data = null) =>
        service.HandleAsync(request, new MemoryStream(data ?? []), "localhost:8631", CancellationToken.None);

    [Fact]
    public async Task Get_printer_attributes_includes_ipp_everywhere_essentials()
    {
        var service = new IppPrinterService(new IppPrinterDescription(), new FakeBackend());
        var response = await Handle(service, Request(IppOperation.GetPrinterAttributes));

        Assert.Equal(IppStatus.SuccessfulOk, response.Code);
        var printer = response.Group(IppGroupTag.Printer)!;
        Assert.Equal("ipp://localhost:8631/ipp/print", printer["printer-uri-supported"]!.Value.AsString());
        Assert.Contains("image/pwg-raster", printer["document-format-supported"]!.Values.Select(v => v.AsString()));
        Assert.Equal(new IppResolution(203, 203), printer["pwg-raster-document-resolution-supported"]!.Value.Value);
        Assert.Contains("sgray_8", printer["pwg-raster-document-type-supported"]!.Values.Select(v => v.AsString()));
        Assert.Equal("ipp-everywhere", printer["ipp-features-supported"]!.Value.AsString());
        Assert.Null(printer["media-col-database"]); // excluded from "all"
    }

    [Fact]
    public async Task Roll_size_is_offered()
    {
        var service = new IppPrinterService(new IppPrinterDescription(), new FakeBackend());
        var printer = (await Handle(service, Request(IppOperation.GetPrinterAttributes))).Group(IppGroupTag.Printer)!;
        Assert.Contains("om_x5h-48x1000mm_48x1000mm", printer["media-supported"]!.Values.Select(v => v.AsString()));
        Assert.Equal("om_x5h-48x210mm_48x210mm", printer["media-default"]!.Value.AsString());
    }

    [Fact]
    public async Task Requested_attributes_filter_the_response()
    {
        var service = new IppPrinterService(new IppPrinterDescription(), new FakeBackend());
        var response = await Handle(service, Request(IppOperation.GetPrinterAttributes,
            g => g.Add("requested-attributes", IppValue.Keyword("printer-state"), IppValue.Keyword("media-col-database"))));
        var names = response.Group(IppGroupTag.Printer)!.Attributes.Select(a => a.Name).ToList();
        Assert.Equal(["media-col-database", "printer-state"], names.Order());
    }

    [Fact]
    public async Task Printer_state_reflects_backend()
    {
        var backend = new FakeBackend { Snapshot = new(PrinterState.Stopped, ["media-empty-error"], "Out of paper", true, 1) };
        var service = new IppPrinterService(new IppPrinterDescription(), backend);
        var printer = (await Handle(service, Request(IppOperation.GetPrinterAttributes))).Group(IppGroupTag.Printer)!;
        Assert.Equal((int)PrinterState.Stopped, printer["printer-state"]!.Value.AsInt());
        Assert.Equal("media-empty-error", printer["printer-state-reasons"]!.Value.AsString());
    }

    [Fact]
    public async Task Print_job_hands_document_to_backend()
    {
        var backend = new FakeBackend();
        var service = new IppPrinterService(new IppPrinterDescription(), backend);
        var response = await Handle(service, Request(IppOperation.PrintJob, g => g
            .Add("requesting-user-name", IppValue.Name("aleix"))
            .Add("job-name", IppValue.Name("Receipt"))
            .Add("document-format", IppValue.Mime("image/png"))), [9, 8, 7]);

        Assert.Equal(IppStatus.SuccessfulOk, response.Code);
        Assert.Equal(1, response.Group(IppGroupTag.Job)!["job-id"]!.Value.AsInt());
        var (id, data, format) = Assert.Single(backend.Documents);
        Assert.Equal([9, 8, 7], data);
        Assert.Equal("image/png", format);
        Assert.Equal("aleix", backend.GetJob(id)!.UserName);
    }

    [Fact]
    public async Task Unsupported_format_is_rejected()
    {
        var service = new IppPrinterService(new IppPrinterDescription(), new FakeBackend());
        var response = await Handle(service, Request(IppOperation.PrintJob, g => g.Add("document-format", IppValue.Mime("application/postscript"))), [1]);
        Assert.Equal(IppStatus.ClientErrorDocumentFormatNotSupported, response.Code);
    }

    [Fact]
    public async Task Page_ranges_reach_the_backend()
    {
        var backend = new FakeBackend();
        var service = new IppPrinterService(new IppPrinterDescription(), backend);
        var request = Request(IppOperation.PrintJob, g => g.Add("document-format", IppValue.Mime("application/pdf")));
        request.GetOrAddGroup(IppGroupTag.Job).Add("page-ranges", IppValue.Range(2, 3), IppValue.Range(5, 5));
        Assert.Equal(IppStatus.SuccessfulOk, (await Handle(service, request, [1])).Code);

        var options = backend.LastOptions!;
        Assert.False(options.Includes(1));
        Assert.True(options.Includes(2));
        Assert.True(options.Includes(3));
        Assert.False(options.Includes(4));
        Assert.True(options.Includes(5));
    }

    [Theory]
    [InlineData("2-3,5", new[] { 2, 3, 5 })]
    [InlineData("", new[] { 1, 2, 3, 4, 5, 6 })]
    public void Cli_page_syntax(string pages, int[] included)
    {
        var options = DocumentOptions.ParsePages(pages);
        Assert.Equal(included, Enumerable.Range(1, 6).Where(options.Includes));
        Assert.Throws<FormatException>(() => DocumentOptions.ParsePages("3-1"));
    }

    [Fact]
    public async Task Create_job_then_send_document()
    {
        var backend = new FakeBackend();
        var service = new IppPrinterService(new IppPrinterDescription(), backend);
        var created = await Handle(service, Request(IppOperation.CreateJob));
        var jobId = created.Group(IppGroupTag.Job)!["job-id"]!.Value.AsInt();

        var sent = await Handle(service, Request(IppOperation.SendDocument, g => g
            .Add("job-id", IppValue.Integer(jobId))
            .Add("last-document", IppValue.Boolean(true))), [1, 2]);

        Assert.Equal(IppStatus.SuccessfulOk, sent.Code);
        Assert.Single(backend.Documents);
        var jobs = await Handle(service, Request(IppOperation.GetJobs, g => g.Add("which-jobs", IppValue.Keyword("completed"))));
        Assert.Single(jobs.Groups, g => g.Tag == IppGroupTag.Job);
    }

    [Fact]
    public async Task Unknown_job_and_operation_errors()
    {
        var service = new IppPrinterService(new IppPrinterDescription(), new FakeBackend());
        Assert.Equal(IppStatus.ClientErrorNotFound,
            (await Handle(service, Request(IppOperation.CancelJob, g => g.Add("job-id", IppValue.Integer(99))))).Code);
        Assert.Equal(IppStatus.ServerErrorOperationNotSupported, (await Handle(service, Request(0x0010))).Code);
        var old = Request(IppOperation.GetPrinterAttributes);
        old.VersionMajor = 3;
        Assert.Equal(IppStatus.ServerErrorVersionNotSupported, (await Handle(service, old)).Code);
    }

    [Fact]
    public async Task Identify_printer_reaches_backend()
    {
        var backend = new FakeBackend();
        var service = new IppPrinterService(new IppPrinterDescription(), backend);
        Assert.Equal(IppStatus.SuccessfulOk, (await Handle(service, Request(IppOperation.IdentifyPrinter))).Code);
        Assert.Equal(1, backend.Identified);
    }
}

/// <summary>Talks to our server with an independent IPP client (SharpIppNext) over real HTTP.</summary>
public sealed class SharpIppInteropTests : IAsyncLifetime
{
    private readonly FakeBackend _backend = new();
    private HttpListener _listener = null!;
    private Task _serverLoop = Task.CompletedTask;
    private int _port;

    public Task InitializeAsync()
    {
        var service = new IppPrinterService(new IppPrinterDescription(), _backend);
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        _port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Start();
        _serverLoop = Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch { return; }
                var request = await IppCodec.ReadAsync(context.Request.InputStream);
                var response = await service.HandleAsync(request, context.Request.InputStream, $"127.0.0.1:{_port}", CancellationToken.None);
                context.Response.ContentType = "application/ipp";
                var bytes = IppCodec.Encode(response);
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _listener.Stop();
        await _serverLoop;
    }

    private Uri PrinterUri => new($"ipp://127.0.0.1:{_port}/ipp/print");

    [Fact]
    public async Task SharpIpp_reads_printer_attributes()
    {
        var client = new SharpIpp.SharpIppClient();
        var response = await client.GetPrinterAttributesAsync(new SharpIpp.Models.Requests.GetPrinterAttributesRequest
        {
            OperationAttributes = new() { PrinterUri = PrinterUri },
        });
        Assert.Equal("X5h Thermal Printer", response.PrinterAttributes.PrinterName);
        Assert.Equal(SharpIpp.Protocol.Models.PrinterState.Idle, response.PrinterAttributes.PrinterState);
        Assert.Contains("image/pwg-raster", response.PrinterAttributes.DocumentFormatSupported!);
    }

    [Fact]
    public async Task SharpIpp_prints_a_job()
    {
        var client = new SharpIpp.SharpIppClient();
        using var document = new MemoryStream([1, 2, 3, 4, 5]);
        var response = await client.PrintJobAsync(new SharpIpp.Models.Requests.PrintJobRequest
        {
            Document = document,
            OperationAttributes = new()
            {
                PrinterUri = PrinterUri,
                DocumentFormat = "image/png",
                JobName = "interop",
            },
        });
        Assert.True(response.JobAttributes.JobId > 0);
        var (_, data, format) = Assert.Single(_backend.Documents);
        Assert.Equal([1, 2, 3, 4, 5], data);
        Assert.Equal("image/png", format);
    }
}
