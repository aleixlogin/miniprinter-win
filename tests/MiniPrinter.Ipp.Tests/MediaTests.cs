using MiniPrinter.Ipp;

namespace MiniPrinter.Ipp.Tests;

/// <summary>Media sizes and margins announced by the printer (spec paper-sizes).</summary>
public class MediaTests
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

    private static IppPrinterDescription Description(params MediaSize[] media) => new() { Media = media };

    private static async Task<IppGroup> Printer(IppPrinterDescription description)
    {
        var response = await Handle(new IppPrinterService(description, new FakeBackend()), Request(IppOperation.GetPrinterAttributes,
            g => g.Add("requested-attributes", IppValue.Keyword("media-col-database"), IppValue.Keyword("media-supported"),
                IppValue.Keyword("media-default"), IppValue.Keyword("media-ready"), IppValue.Keyword("media-size-supported"),
                IppValue.Keyword("media-left-margin-supported"), IppValue.Keyword("media-right-margin-supported"),
                IppValue.Keyword("media-top-margin-supported"), IppValue.Keyword("media-bottom-margin-supported"))));
        return response.Group(IppGroupTag.Printer)!;
    }

    private static IppCollection Database(IppGroup printer, string name) =>
        printer["media-col-database"]!.Values.Select(v => (IppCollection)v.Value!)
            .Single(c => c.Single(a => a.Name == "media-size-name").Value.AsString() == name);

    private static int Margin(IppCollection media, string name) => media.Single(a => a.Name == name).Value.AsInt();

    [Fact]
    public async Task Only_the_given_sizes_are_offered_and_the_first_is_default_and_ready()
    {
        var printer = await Printer(Description(
            new("om_x5h-48x100mm_48x100mm", 4800, 10000),
            new("om_x5h-48x50mm_48x50mm", 4800, 5000)));
        Assert.Equal(["om_x5h-48x100mm_48x100mm", "om_x5h-48x50mm_48x50mm"], printer["media-supported"]!.Values.Select(v => v.AsString()));
        Assert.Equal(2, printer["media-col-database"]!.Values.Count);
        Assert.Equal(2, printer["media-size-supported"]!.Values.Count);
        Assert.Equal("om_x5h-48x100mm_48x100mm", printer["media-default"]!.Value.AsString());
        Assert.Equal("om_x5h-48x100mm_48x100mm", printer["media-ready"]!.Value.AsString());
    }

    [Fact]
    public async Task A_wide_size_announces_its_side_margins_and_the_others_keep_zero()
    {
        var printer = await Printer(Description(
            new("om_x5h-57x150mm_57x150mm", 5700, 15000, SideMargin: 450),
            new("om_x5h-48x100mm_48x100mm", 4800, 10000)));

        var wide = Database(printer, "om_x5h-57x150mm_57x150mm");
        Assert.Equal((450, 450, 0, 0), (Margin(wide, "media-left-margin"), Margin(wide, "media-right-margin"),
            Margin(wide, "media-top-margin"), Margin(wide, "media-bottom-margin")));
        var narrow = Database(printer, "om_x5h-48x100mm_48x100mm");
        Assert.Equal((0, 0), (Margin(narrow, "media-left-margin"), Margin(narrow, "media-right-margin")));

        Assert.Equal([0, 450], printer["media-left-margin-supported"]!.Values.Select(v => v.AsInt()).Order());
        Assert.Equal([0, 450], printer["media-right-margin-supported"]!.Values.Select(v => v.AsInt()).Order());
        Assert.Equal([0], printer["media-top-margin-supported"]!.Values.Select(v => v.AsInt()));
    }

    [Fact]
    public async Task Default_sizes_still_have_no_margins_and_the_default_description_is_unchanged()
    {
        var printer = await Printer(new IppPrinterDescription());
        Assert.Equal(7, printer["media-supported"]!.Values.Count);
        Assert.Equal("om_x5h-48x210mm_48x210mm", printer["media-default"]!.Value.AsString());
        Assert.Equal([0], printer["media-left-margin-supported"]!.Values.Select(v => v.AsInt()));
    }

    [Fact]
    public async Task A_job_that_asks_for_a_size_that_is_not_offered_is_still_accepted()
    {
        var backend = new FakeBackend();
        var service = new IppPrinterService(Description(new MediaSize("om_x5h-48x100mm_48x100mm", 4800, 10000)), backend);
        var request = Request(IppOperation.PrintJob, g => g.Add("document-format", IppValue.Mime("image/png")));
        request.GetOrAddGroup(IppGroupTag.Job).Add("media", IppValue.Keyword("om_x5h-80x297mm_80x297mm"));

        var response = await Handle(service, request, [1, 2, 3]);

        Assert.Equal(IppStatus.SuccessfulOk, response.Code);
        Assert.Single(backend.Documents);
    }
}
