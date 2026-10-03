using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using MiniPrinter.Control;
using MiniPrinter.Ipp;

namespace MiniPrinter.Service.Tests;

/// <summary>The sizes chosen in the settings reach Windows over IPP, and the listener restarts when they change.</summary>
public sealed class PaperSizesIppTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private string _dataDir = null!;
    private int _ippPort;

    public Task InitializeAsync()
    {
        _dataDir = TestEnv.TempDirectory();
        _ippPort = TestEnv.FreePort();
        File.WriteAllText(Path.Combine(_dataDir, "settings.json"),
            JsonSerializer.Serialize(new ServiceSettings { IppPort = _ippPort }, ControlDefaults.Json));
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("DataDirectory", _dataDir);
            b.UseEnvironment("Development");
        });
        _ = _factory.Server; // start
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private ControlClient Client() =>
        new(File.ReadAllText(Path.Combine(_dataDir, "control.token")), handler: _factory.Server.CreateHandler());

    private static PaperSizeSetting Own(int w, int l, string name = "Mío") => new(PaperCatalog.CustomId(w, l), name, w, l);

    private async Task<IppGroup?> PrinterAttributes()
    {
        var request = new IppMessage { Code = IppOperation.GetPrinterAttributes, RequestId = 1 };
        request.GetOrAddGroup(IppGroupTag.Operation)
            .Add("attributes-charset", IppValue.Charset("utf-8"))
            .Add("attributes-natural-language", IppValue.Language("en"))
            .Add("printer-uri", IppValue.Uri($"ipp://127.0.0.1:{_ippPort}/ipp/print"))
            .Add("requested-attributes", IppValue.Keyword("media-supported"), IppValue.Keyword("media-default"),
                IppValue.Keyword("media-col-database"));
        using var http = new HttpClient();
        var body = new ByteArrayContent(IppCodec.Encode(request));
        body.Headers.ContentType = new("application/ipp");
        try
        {
            var response = await http.PostAsync($"http://127.0.0.1:{_ippPort}/ipp/print", body);
            return (await IppCodec.ReadAsync(await response.Content.ReadAsStreamAsync())).Group(IppGroupTag.Printer);
        }
        catch (HttpRequestException)
        {
            return null;   // the listener is restarting
        }
    }

    private async Task<string[]> OfferedSizes()
    {
        string[] sizes = [];
        await TestEnv.WaitUntilAsync(async () =>
        {
            sizes = (await PrinterAttributes())?["media-supported"]?.Values.Select(v => v.AsString()).ToArray() ?? [];
            return sizes.Length > 0;
        });
        return sizes;
    }

    [Fact]
    public async Task Without_saved_sizes_the_seven_factory_sizes_are_announced_with_48x210_first()
    {
        var sizes = await OfferedSizes();
        Assert.Equal(7, sizes.Length);
        Assert.Equal("om_x5h-48x210mm_48x210mm", sizes[0]);
    }

    [Fact]
    public async Task Changing_the_sizes_restarts_the_listener_with_the_new_list()
    {
        using var client = Client();
        var settings = await client.GetSettingsAsync();
        var list = PaperCatalog.Presets.Select(p => p with { Enabled = p.Id is "48x100" or "48x50" }).Append(Own(57, 150, "Ticket")).ToList();
        await client.SaveSettingsAsync(settings with { PaperSizes = list, DefaultPaperId = "c57x150" });

        await TestEnv.WaitUntilAsync(async () => (await PrinterAttributes())?["media-default"]?.Value.AsString() == "om_x5h-57x150mm_57x150mm");
        var printer = (await PrinterAttributes())!;
        Assert.Equal(["om_x5h-57x150mm_57x150mm", "om_x5h-48x100mm_48x100mm", "om_x5h-48x50mm_48x50mm"],
            printer["media-supported"]!.Values.Select(v => v.AsString()));

        var wide = printer["media-col-database"]!.Values.Select(v => (IppCollection)v.Value!)
            .First(c => c.Single(a => a.Name == "media-size-name").Value.AsString() == "om_x5h-57x150mm_57x150mm");
        Assert.Equal(450, wide.Single(a => a.Name == "media-left-margin").Value.AsInt());
    }

    [Fact]
    public async Task Other_settings_do_not_change_what_is_announced()
    {
        using var client = Client();
        var before = await OfferedSizes();
        var settings = await client.GetSettingsAsync();
        await client.SaveSettingsAsync(settings with { Darkness = 4 });
        Assert.Equal(before, await OfferedSizes());
    }

    [Fact]
    public async Task The_api_rejects_invalid_sizes_with_a_clear_message()
    {
        using var client = Client();
        var settings = await client.GetSettingsAsync();
        var tooWide = PaperCatalog.Presets.Append(Own(60, 100)).ToList();
        var error = await Assert.ThrowsAsync<ControlApiException>(() => client.SaveSettingsAsync(settings with { PaperSizes = tooWide }));
        Assert.Equal(400, error.StatusCode);
        Assert.Contains("57", error.Message);
        Assert.Null((await client.GetSettingsAsync()).PaperSizes);   // nothing was saved
        Assert.Equal(7, (await OfferedSizes()).Length);              // and nothing was applied
    }
}
