using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using MiniPrinter.Control;
using MiniPrinter.Imaging;

namespace MiniPrinter.Service.Tests;

/// <summary>Template management, copies and batches through the control and automation APIs.</summary>
public sealed class TemplateApiTests : IAsyncLifetime
{
    private const string ReceiptJson = """
        { "name": "recibo", "title": "Recibo", "fields": [ { "name": "cliente", "kind": "text", "required": true } ],
          "blocks": [ { "type": "text", "value": "Cliente: {{cliente}} Nº {{counter:recibo}}" } ] }
        """;

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

    private HttpClient Api(string token)
    {
        var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_ippPort}/api/v1/") };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static StringContent JsonBody(string json) => new(json, System.Text.Encoding.UTF8, "application/json");

    private long NextNumber(string counter) => new FileCounterStore(Path.Combine(_dataDir, "counters.json")).Peek(counter);

    private static Dictionary<string, string> Client_(string name) => new() { ["cliente"] = name };

    [Fact]
    public async Task Template_management_through_the_control_api()
    {
        using var client = Client();
        await client.SelectPrinterAsync(TestEnv.Simulated);

        var saved = await client.SaveTemplateAsync("recibo", ReceiptJson);
        Assert.Equal("user", saved.Source);
        Assert.Contains(await client.GetTemplatesAsync(), t => t.Name == "recibo");
        Assert.Contains("Cliente", await client.GetTemplateJsonAsync("recibo"));
        Assert.Contains("\"qr\"", await client.GetTemplateJsonAsync("qr")); // built-ins are readable too
        Assert.True(File.Exists(Path.Combine(_dataDir, "templates", "recibo.json")));

        var job = await client.PrintTemplateAsync("recibo", Client_("Ana"));
        await TestEnv.WaitUntil(() => client.GetJobsAsync().Result.Single(j => j.Id == job.Id).State == "Completed");

        await client.ValidateTemplateAsync(ReceiptJson);
        var invalid = await Assert.ThrowsAsync<ControlApiException>(() =>
            client.ValidateTemplateAsync("""{ "name": "x", "blocks": [ { "type": "video" } ] }"""));
        Assert.Equal(400, invalid.StatusCode);
        Assert.Contains("video", invalid.Message);
        Assert.DoesNotContain(await client.GetTemplatesAsync(), t => t.Name == "x");

        var builtIn = await Assert.ThrowsAsync<ControlApiException>(() => client.DeleteTemplateAsync("qr"));
        Assert.Contains("integrada", builtIn.Message);
        var traversal = await Assert.ThrowsAsync<ControlApiException>(() => client.SaveTemplateAsync("..%2Fconfig", ReceiptJson));
        Assert.True(traversal.StatusCode is 400 or 404);
        Assert.False(File.Exists(Path.Combine(_dataDir, "config.json")));

        await client.SaveTemplateAssetAsync("recibo", "logo.png", TestEnv.Png(100, 50));
        Assert.True(File.Exists(Path.Combine(_dataDir, "templates", "assets", "recibo", "logo.png")));
        var tooBig = await Assert.ThrowsAsync<ControlApiException>(() =>
            client.SaveTemplateAssetAsync("recibo", "big.png", new byte[2 * 1024 * 1024]));
        Assert.Equal(400, tooBig.StatusCode);

        await client.DeleteTemplateAsync("recibo");
        Assert.DoesNotContain(await client.GetTemplatesAsync(), t => t.Name == "recibo");
        Assert.False(Directory.Exists(Path.Combine(_dataDir, "templates", "assets", "recibo")));
    }

    [Fact]
    public async Task Copies_and_rows_print_one_job_and_number_each_label()
    {
        using var client = Client();
        await client.SelectPrinterAsync(TestEnv.Simulated);
        await client.SaveTemplateAsync("recibo", ReceiptJson);

        var rows = new IReadOnlyDictionary<string, string>[] { Client_("Ana"), Client_("Luis"), Client_("Eva") };
        var job = await client.PrintTemplateAsync("recibo", new Dictionary<string, string>(), copies: 2, rows: rows);
        await TestEnv.WaitUntil(() => client.GetJobsAsync().Result.Single(j => j.Id == job.Id).State == "Completed");

        var jobs = await client.GetJobsAsync();
        Assert.Single(jobs);                       // one job for the whole batch
        Assert.Equal(6, jobs[0].Pages);   // 3 labels × 2 copies
        Assert.Equal(4, NextNumber("recibo"));     // one number per distinct label
    }

    [Fact]
    public async Task Invalid_row_prints_nothing_and_does_not_consume_numbers()
    {
        using var client = Client();
        await client.SelectPrinterAsync(TestEnv.Simulated);
        await client.SaveTemplateAsync("recibo", ReceiptJson);
        var rows = new IReadOnlyDictionary<string, string>[] { Client_("Ana"), Client_("") };
        var error = await Assert.ThrowsAsync<ControlApiException>(() =>
            client.PrintTemplateAsync("recibo", new Dictionary<string, string>(), rows: rows));
        Assert.Equal(400, error.StatusCode);
        Assert.StartsWith("Fila 2:", error.Message);
        Assert.Empty(await client.GetJobsAsync());
        Assert.Equal(1, NextNumber("recibo"));

        var tooMany = Enumerable.Range(0, 201).Select(_ => (IReadOnlyDictionary<string, string>)Client_("x")).ToList();
        var limit = await Assert.ThrowsAsync<ControlApiException>(() =>
            client.PrintTemplateAsync("recibo", new Dictionary<string, string>(), rows: tooMany));
        Assert.Contains("200", limit.Message);
        var copies = await Assert.ThrowsAsync<ControlApiException>(() =>
            client.PrintTemplateAsync("recibo", Client_("x"), copies: 51));
        Assert.Contains("50", copies.Message);
    }

    [Fact]
    public async Task Preview_does_not_consume_the_counter()
    {
        using var client = Client();
        await client.SaveTemplateAsync("recibo", ReceiptJson);
        for (var i = 0; i < 3; i++)
            await client.PreviewTemplateAsync("recibo", Client_("Ana"));
        Assert.Equal(1, NextNumber("recibo"));
    }

    [Fact]
    public async Task Automation_api_manages_templates_with_its_token_and_prints_batches()
    {
        using var client = Client();
        await client.SelectPrinterAsync(TestEnv.Simulated);
        using var off = Api("x");
        Assert.Equal(HttpStatusCode.NotFound, (await off.GetAsync("templates")).StatusCode);   // disabled

        await client.SaveSettingsAsync((await client.GetSettingsAsync()) with { AutomationApiEnabled = true });
        Assert.Equal(HttpStatusCode.Unauthorized, (await off.PutAsync("templates/recibo", JsonBody(ReceiptJson))).StatusCode);
        Assert.False(File.Exists(Path.Combine(_dataDir, "templates", "recibo.json")));

        using var api = Api((await client.GetAutomationAsync()).Token);
        Assert.Equal(HttpStatusCode.OK, (await api.PutAsync("templates/recibo", JsonBody(ReceiptJson))).StatusCode);
        Assert.Contains("recibo", await api.GetStringAsync("templates"));
        Assert.Equal(HttpStatusCode.OK, (await api.PostAsync("templates/validate", JsonBody(ReceiptJson))).StatusCode);

        var oversized = await api.PutAsync("templates/grande", JsonBody(new string(' ', 100 * 1024)));
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.Contains("64", await oversized.Content.ReadAsStringAsync());

        var batch = await api.PostAsync("print/template/recibo",
            JsonBody("""{ "copies": 2, "rows": [ { "cliente": "Ana" }, { "cliente": "Luis" } ] }"""));
        Assert.Equal(HttpStatusCode.Accepted, batch.StatusCode);
        await TestEnv.WaitUntil(() => client.GetJobsAsync().Result is [{ State: "Completed", Pages: 4 }]);

        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync("templates/recibo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync("templates/recibo")).StatusCode);
    }
}
