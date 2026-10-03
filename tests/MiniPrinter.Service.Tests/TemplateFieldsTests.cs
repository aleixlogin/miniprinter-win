using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using MiniPrinter.Control;
using MiniPrinter.Imaging;

namespace MiniPrinter.Service.Tests;

/// <summary>GET /templates/{name}/fields: the fields of one template and the print parameters, in both APIs.</summary>
public sealed class TemplateFieldsTests : IAsyncLifetime
{
    private const string Receipt = """
        { "name": "recibo", "title": "Recibo",
          "fields": [ { "name": "cliente", "label": "Cliente", "kind": "text", "required": true },
                      { "name": "pago", "kind": "choice", "choices": ["efectivo", "tarjeta"], "default": "efectivo" } ],
          "blocks": [ { "type": "text", "value": "{{cliente}} {{pago}}" } ] }
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
        return TestEnv.WaitForPortAsync(_ippPort);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private ControlClient Client() =>
        new(File.ReadAllText(Path.Combine(_dataDir, "control.token")), handler: _factory.Server.CreateHandler());

    [Fact]
    public async Task A_built_in_template_lists_its_fields_with_kinds_defaults_and_choices()
    {
        using var client = Client();
        var label = await client.GetTemplateFieldsAsync("label");
        Assert.Equal(("label", "builtin"), (label.Name, label.Source));
        Assert.Equal(["title", "line1", "line2", "size", "align", "border"], label.Fields.Select(f => f.Name));
        Assert.True(label.Fields.Single(f => f.Name == "title").Required);
        Assert.False(label.Fields.Single(f => f.Name == "line1").Required);
        Assert.Equal("26", label.Fields.Single(f => f.Name == "size").Default);
        var align = label.Fields.Single(f => f.Name == "align");
        Assert.Equal(["center", "left", "right"], align.Choices);
        Assert.Equal("Boolean", label.Fields.Single(f => f.Name == "border").Kind);
    }

    [Fact]
    public async Task The_print_parameters_are_listed_with_their_limits()
    {
        using var client = Client();
        var fields = await client.GetTemplateFieldsAsync("qr");
        Assert.Equal(["copies", "rows", "darkness"], fields.PrintParameters.Select(p => p.Name));
        var copies = fields.PrintParameters.Single(p => p.Name == "copies");
        Assert.Equal((1, 50), (copies.Min, copies.Max));
        var rows = fields.PrintParameters.Single(p => p.Name == "rows");
        Assert.Equal(200, rows.Max);
        var darkness = fields.PrintParameters.Single(p => p.Name == "darkness");
        Assert.Equal((1, 5), (darkness.Min, darkness.Max));
    }

    [Fact]
    public void The_listed_limits_are_the_ones_the_service_enforces()
    {
        Assert.Equal(TemplateCatalog.MaxCopies, TemplatePrintParameters.All.Single(p => p.Name == "copies").Max);
        Assert.Equal(TemplateCatalog.MaxRows, TemplatePrintParameters.All.Single(p => p.Name == "rows").Max);
    }

    [Fact]
    public async Task A_user_template_reflects_its_own_fields_and_a_replaced_built_in_reports_its_source()
    {
        using var client = Client();
        await client.SaveTemplateAsync("recibo", Receipt);
        var fields = await client.GetTemplateFieldsAsync("recibo");
        Assert.Equal("user", fields.Source);
        Assert.Equal(["cliente", "pago"], fields.Fields.Select(f => f.Name));
        Assert.Equal(("efectivo", "Choice"), (fields.Fields[1].Default, fields.Fields[1].Kind));
        Assert.Equal("pago", fields.Fields[1].Label);   // no label in the JSON: the name is used

        await client.SaveTemplateAsync("label", """{ "name": "label", "fields": [ { "name": "solo" } ], "blocks": [ { "type": "text", "value": "{{solo}}" } ] }""");
        var replaced = await client.GetTemplateFieldsAsync("label");
        Assert.Equal("override", replaced.Source);
        Assert.Equal(["solo"], replaced.Fields.Select(f => f.Name));
    }

    [Fact]
    public async Task An_unknown_template_is_a_404_that_lists_the_available_ones()
    {
        using var client = Client();
        var error = await Assert.ThrowsAsync<ControlApiException>(() => client.GetTemplateFieldsAsync("no-existe"));
        Assert.Equal(404, error.StatusCode);
        Assert.Contains("no-existe", error.Message);
        Assert.Contains("label", error.Message);
    }

    [Fact]
    public async Task The_automation_api_offers_it_with_its_token_and_not_without()
    {
        using var client = Client();
        await client.SaveSettingsAsync((await client.GetSettingsAsync()) with { AutomationApiEnabled = true });
        using var api = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_ippPort}/api/v1/") };

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync("templates/label/fields")).StatusCode);
        api.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", (await client.GetAutomationAsync()).Token);

        var body = await api.GetStringAsync("templates/label/fields");
        using var json = JsonDocument.Parse(body);
        Assert.Equal("label", json.RootElement.GetProperty("name").GetString());
        Assert.Equal(6, json.RootElement.GetProperty("fields").GetArrayLength());
        Assert.Equal(3, json.RootElement.GetProperty("printParameters").GetArrayLength());

        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync("templates/no-existe/fields")).StatusCode);
    }

    [Fact]
    public async Task The_wrong_control_token_is_refused()
    {
        using var wrong = new ControlClient("wrong", handler: _factory.Server.CreateHandler());
        var error = await Assert.ThrowsAsync<ControlApiException>(() => wrong.GetTemplateFieldsAsync("label"));
        Assert.Equal(401, error.StatusCode);
    }

    [Theory]
    [InlineData("fields")]
    [InlineData("list")]
    public async Task Names_that_are_cli_subcommands_cannot_be_templates(string name)
    {
        using var client = Client();
        var error = await Assert.ThrowsAsync<ControlApiException>(() =>
            client.SaveTemplateAsync(name, Receipt.Replace("recibo", name)));
        Assert.Equal(400, error.StatusCode);
    }
}
