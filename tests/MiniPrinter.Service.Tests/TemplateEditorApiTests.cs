using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using MiniPrinter.Control;
using MiniPrinter.Imaging;

namespace MiniPrinter.Service.Tests;

/// <summary>Schema, draft previews, block rows and editor drafts through the control and automation APIs.</summary>
public sealed class TemplateEditorApiTests : IAsyncLifetime
{
    private const string Receipt = """
        { "name": "recibo", "title": "Recibo", "fields": [ { "name": "cliente", "kind": "text", "required": true } ],
          "blocks": [ { "type": "text", "value": "Cliente: {{cliente}} Nº {{counter:recibo}}" }, { "type": "line" } ] }
        """;

    private const string WithLogo = """
        { "name": "recibo", "blocks": [ { "type": "image", "source": "logo.png" }, { "type": "text", "value": "Hola" } ] }
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

    private static readonly IReadOnlyDictionary<string, string> NoFields = new Dictionary<string, string>();

    private string DraftDir(string id) => Path.Combine(_dataDir, "drafts", id);

    private string SavedAssets(string template) => Path.Combine(_dataDir, "templates", "assets", template);

    // ---- schema ---------------------------------------------------------------------------------

    [Fact]
    public async Task Schema_publishes_the_blocks_of_the_engine()
    {
        using var client = Client();
        var schema = await client.GetTemplateSchemaAsync();
        Assert.Equal(8, schema.Blocks.Count);
        var text = schema.Blocks.Single(b => b.Type == "text");
        var size = text.Props.Single(p => p.Name == "size");
        Assert.Equal("number", size.Kind);
        Assert.Equal((6.0, 48.0), (size.Min, size.Max));
        Assert.Equal(["left", "center", "right"], text.Props.Single(p => p.Name == "align").Values);
        Assert.Contains(schema.Common, p => p.Name == "when");
        Assert.Contains("upper", schema.Filters);
        Assert.Equal(100, schema.Template.MaxBlocks);
    }

    [Fact]
    public async Task Schema_requires_the_token()
    {
        using var wrong = new ControlClient("wrong", handler: _factory.Server.CreateHandler());
        var error = await Assert.ThrowsAsync<ControlApiException>(() => wrong.GetTemplateSchemaAsync());
        Assert.Equal(401, error.StatusCode);
    }

    // ---- block rows ---------------------------------------------------------------------------------

    [Fact]
    public async Task Saved_preview_reports_the_rows_of_each_block_and_keeps_the_same_png()
    {
        using var client = Client();
        await client.SaveTemplateAsync("recibo", Receipt);
        var detailed = await client.PreviewTemplateDetailedAsync("recibo", new Dictionary<string, string> { ["cliente"] = "Ana" });
        Assert.Equal([1, 2], detailed.Blocks.Select(b => b.Index));
        Assert.Equal(["text", "line"], detailed.Blocks.Select(b => b.Type));
        Assert.True(detailed.Blocks[1].Top > detailed.Blocks[0].Top);
        Assert.Equal(0x89, detailed.Png[0]);

        // The plain call (what older clients use) returns exactly the same PNG.
        var plain = await client.PreviewTemplateAsync("recibo", new Dictionary<string, string> { ["cliente"] = "Ana" });
        Assert.Equal(detailed.Png, plain);
    }

    // ---- draft previews -------------------------------------------------------------------------------

    [Fact]
    public async Task Draft_preview_renders_without_saving_and_without_consuming_the_counter()
    {
        using var client = Client();
        for (var i = 0; i < 3; i++)
        {
            var preview = await client.PreviewDraftAsync(Receipt, new Dictionary<string, string> { ["cliente"] = "Ana" });
            Assert.Equal(2, preview.Blocks.Count);
        }
        Assert.False(File.Exists(Path.Combine(_dataDir, "templates", "recibo.json")));
        Assert.Equal(1, new FileCounterStore(Path.Combine(_dataDir, "counters.json")).Peek("recibo"));
    }

    [Fact]
    public async Task Draft_preview_is_lenient_with_required_fields_but_validates_the_rest()
    {
        using var client = Client();
        var preview = await client.PreviewDraftAsync(Receipt, NoFields);   // 'cliente' is required and empty
        Assert.Equal(0x89, preview.Png[0]);

        var error = await Assert.ThrowsAsync<ControlApiException>(() => client.PreviewDraftAsync(
            """{ "name": "t", "blocks": [ { "type": "text", "value": "a" }, { "type": "text", "size": 500 } ] }""", NoFields));
        Assert.Equal(400, error.StatusCode);
        Assert.Equal(2, error.Block);
        Assert.Equal("size", error.Property);
    }

    [Fact]
    public async Task Draft_preview_of_an_empty_template_is_a_blank_page_not_an_error()
    {
        using var client = Client();
        var preview = await client.PreviewDraftAsync("""{ "name": "t", "blocks": [ { "type": "text", "value": "{{x}}" } ], "fields": [ { "name": "x" } ] }""", NoFields);
        Assert.Empty(preview.Blocks);
        Assert.Equal(0x89, preview.Png[0]);
    }

    [Fact]
    public async Task Draft_preview_rejects_oversized_and_malformed_input()
    {
        using var client = Client();
        var big = await Assert.ThrowsAsync<ControlApiException>(() =>
            client.PreviewDraftAsync("{ \"name\": \"t\", \"blocks\": [ { \"type\": \"text\", \"value\": \"" + new string('a', 70 * 1024) + "\" } ] }", NoFields));
        Assert.Equal(400, big.StatusCode);
        Assert.Contains("64", big.Message);

        using var http = _factory.Server.CreateClient();
        http.DefaultRequestHeaders.Add(ControlDefaults.TokenHeader, File.ReadAllText(Path.Combine(_dataDir, "control.token")));
        var response = await http.PostAsync("/api/templates/preview", new StringContent("{ \"fields\": {} }", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- structured errors -------------------------------------------------------------------------------

    [Fact]
    public async Task Validation_errors_include_block_and_property_only_when_known()
    {
        using var client = Client();
        var withBlock = await Assert.ThrowsAsync<ControlApiException>(() =>
            client.ValidateTemplateAsync("""{ "name": "t", "blocks": [ { "type": "line", "style": "wavy" } ] }"""));
        Assert.Equal((1, "style"), (withBlock.Block, withBlock.Property));

        var general = await Assert.ThrowsAsync<ControlApiException>(() => client.ValidateTemplateAsync("{ esto no es json"));
        Assert.Null(general.Block);
        Assert.Null(general.Property);

        // Render-time errors of a saved template are attributed too.
        await client.SaveTemplateAsync("ean", """{ "name": "ean", "blocks": [ { "type": "text", "value": "x" }, { "type": "barcode", "data": "5901234123450", "format": "ean13" } ] }""");
        var render = await Assert.ThrowsAsync<ControlApiException>(() => client.PreviewTemplateDetailedAsync("ean", NoFields));
        Assert.Equal(2, render.Block);
    }

    // ---- drafts ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Logo_can_be_uploaded_to_a_draft_and_previewed_before_the_template_exists()
    {
        using var client = Client();
        await Assert.ThrowsAsync<ControlApiException>(() => client.PreviewDraftAsync(WithLogo, NoFields));   // no logo yet

        var draft = await client.CreateDraftAsync();
        Assert.True(Directory.Exists(DraftDir(draft)));
        await client.SaveDraftAssetAsync(draft, "logo.png", TestEnv.Png(100, 50));
        var preview = await client.PreviewDraftAsync(WithLogo, NoFields, draft);
        Assert.Equal([1, 2], preview.Blocks.Select(b => b.Index));
        Assert.True(preview.Blocks[0].Height > 40);   // the logo, scaled to the paper width
        Assert.False(Directory.Exists(SavedAssets("recibo")));   // nothing was written for the template
    }

    [Fact]
    public async Task Saving_with_a_draft_copies_the_used_images_and_removes_the_draft()
    {
        using var client = Client();
        var draft = await client.CreateDraftAsync();
        await client.SaveDraftAssetAsync(draft, "logo.png", TestEnv.Png(100, 50));
        await client.SaveDraftAssetAsync(draft, "otra.png", TestEnv.Png(40, 40));   // not used by the template

        await client.SaveTemplateAsync("recibo", WithLogo, draft);

        Assert.True(File.Exists(Path.Combine(SavedAssets("recibo"), "logo.png")));
        Assert.False(File.Exists(Path.Combine(SavedAssets("recibo"), "otra.png")));
        Assert.False(Directory.Exists(DraftDir(draft)));
        var job = await client.PreviewTemplateDetailedAsync("recibo", NoFields);   // the saved template works on its own
        Assert.Equal(2, job.Blocks.Count);
    }

    [Fact]
    public async Task A_failed_save_keeps_the_draft_and_copies_nothing()
    {
        using var client = Client();
        var draft = await client.CreateDraftAsync();
        await client.SaveDraftAssetAsync(draft, "logo.png", TestEnv.Png(100, 50));
        const string bad = """{ "name": "recibo", "blocks": [ { "type": "image", "source": "logo.png" }, { "type": "text", "size": 999 } ] }""";

        var error = await Assert.ThrowsAsync<ControlApiException>(() => client.SaveTemplateAsync("recibo", bad, draft));
        Assert.Equal(400, error.StatusCode);
        Assert.True(Directory.Exists(DraftDir(draft)));
        Assert.False(Directory.Exists(SavedAssets("recibo")));
        Assert.False(File.Exists(Path.Combine(_dataDir, "templates", "recibo.json")));
    }

    [Fact]
    public async Task Discarding_a_draft_leaves_a_saved_template_untouched()
    {
        using var client = Client();
        await client.SaveTemplateAsync("recibo", Receipt);
        await client.SaveTemplateAssetAsync("recibo", "logo.png", TestEnv.Png(80, 40));

        var draft = await client.CreateDraftAsync();
        await client.SaveDraftAssetAsync(draft, "logo.png", TestEnv.Png(30, 30));   // a different logo, never saved
        await client.DiscardDraftAsync(draft);

        Assert.False(Directory.Exists(DraftDir(draft)));
        Assert.Equal(TestEnv.Png(80, 40), File.ReadAllBytes(Path.Combine(SavedAssets("recibo"), "logo.png")));
    }

    [Fact]
    public async Task Draft_previews_fall_back_to_the_images_of_the_saved_template()
    {
        using var client = Client();
        await client.SaveTemplateAsync("recibo", Receipt);
        await client.SaveTemplateAssetAsync("recibo", "logo.png", TestEnv.Png(100, 50));
        var draft = await client.CreateDraftAsync();   // empty draft: the saved logo is still found
        var preview = await client.PreviewDraftAsync(WithLogo, NoFields, draft);
        Assert.True(preview.Blocks[0].Height > 40);   // the logo, scaled to the paper width
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef")]   // well formed, does not exist
    [InlineData("..%2F..%2Fsettings")]
    [InlineData("not-a-draft")]
    public async Task Unknown_or_malformed_draft_ids_are_rejected(string id)
    {
        using var client = Client();
        var error = await Assert.ThrowsAsync<ControlApiException>(() => client.SaveDraftAssetAsync(id, "logo.png", TestEnv.Png(30, 30)));
        Assert.True(error.StatusCode is 400 or 404);
        Assert.False(File.Exists(Path.Combine(_dataDir, "logo.png")));
        await client.SaveTemplateAsync("recibo", Receipt);   // saving with a bad ?draft= is refused too
        var save = await Assert.ThrowsAsync<ControlApiException>(() => client.SaveTemplateAsync("otro", Receipt.Replace("recibo", "otro"), id));
        Assert.True(save.StatusCode is 400 or 404);
        await client.DiscardDraftAsync(id);   // discarding garbage is harmless
    }

    [Fact]
    public async Task Draft_images_have_the_same_limits_as_template_images()
    {
        using var client = Client();
        var draft = await client.CreateDraftAsync();
        var big = await Assert.ThrowsAsync<ControlApiException>(() => client.SaveDraftAssetAsync(draft, "big.png", new byte[3 * 1024 * 1024]));
        Assert.Contains("1 MB", big.Message);
        await Assert.ThrowsAsync<ControlApiException>(() => client.SaveDraftAssetAsync(draft, "../x.png", TestEnv.Png(30, 30)));
        await Assert.ThrowsAsync<ControlApiException>(() => client.SaveDraftAssetAsync(draft, "fake.png", new byte[100]));
        await client.SaveDraftAssetAsync(draft, "ok.png", TestEnv.Png(30, 30));
        await client.DeleteDraftAssetAsync(draft, "ok.png");
        Assert.False(File.Exists(Path.Combine(DraftDir(draft), "ok.png")));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("preview")]
    [InlineData("drafts")]
    public async Task Names_used_by_api_routes_cannot_be_templates(string name)
    {
        using var client = Client();
        var error = await Assert.ThrowsAsync<ControlApiException>(() =>
            client.SaveTemplateAsync(name, Receipt.Replace("recibo", name)));
        Assert.Equal(400, error.StatusCode);
    }

    // ---- automation API ----------------------------------------------------------------------------------

    [Fact]
    public async Task Automation_api_offers_schema_draft_preview_and_drafts_with_its_token()
    {
        using var client = Client();
        await client.SaveSettingsAsync((await client.GetSettingsAsync()) with { AutomationApiEnabled = true });
        using var api = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_ippPort}/api/v1/") };
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync("templates/schema")).StatusCode);
        api.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", (await client.GetAutomationAsync()).Token);

        Assert.Contains("\"blocks\"", await api.GetStringAsync("templates/schema"));

        var body = new StringContent("{\"template\":{\"name\":\"t\",\"blocks\":[{\"type\":\"text\",\"value\":\"Hola\"},{\"type\":\"line\"}]},\"fields\":{}}",
            System.Text.Encoding.UTF8, "application/json");
        var preview = await api.PostAsync("templates/preview", body);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("image/png", preview.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"index\":2", string.Join("", preview.Headers.GetValues("X-Template-Blocks")));

        var bad = await api.PostAsync("templates/preview", new StringContent("{\"template\":{\"name\":\"t\",\"blocks\":[{\"type\":\"text\",\"size\":500}]}}",
            System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var error = JsonDocument.Parse(await bad.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, error.GetProperty("block").GetInt32());
        Assert.Equal("size", error.GetProperty("property").GetString());

        var created = await api.PostAsync("templates/drafts", null);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"templates/drafts/{id}")).StatusCode);
    }
}

public class DraftStoreTests
{
    private static string Root() => Path.Combine(TestEnv.TempDirectory(), "drafts");

    [Fact]
    public void Old_drafts_are_pruned_on_start_and_when_creating_a_new_one()
    {
        var root = Root();
        var now = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
        var store = new DraftStore(root, () => now);
        var old = store.Create();
        now = now.AddHours(23);
        var recent = store.Create();
        now = now.AddHours(2);   // `old` is now 25 h old, `recent` 2 h
        var fresh = store.Create();   // creating prunes

        Assert.False(Directory.Exists(Path.Combine(root, old)));
        Assert.True(Directory.Exists(Path.Combine(root, recent)));
        Assert.True(Directory.Exists(Path.Combine(root, fresh)));

        now = now.AddHours(30);
        _ = new DraftStore(root, () => now);   // a restarted service prunes everything stale
        Assert.Empty(Directory.GetDirectories(root));
    }

    [Fact]
    public void Using_a_draft_keeps_it_alive()
    {
        var root = Root();
        var now = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
        var store = new DraftStore(root, () => now);
        var id = store.Create();
        now = now.AddHours(20);
        store.Resolve(id);   // activity
        now = now.AddHours(20);
        store.Prune();
        Assert.True(Directory.Exists(Path.Combine(root, id)));
    }

    [Fact]
    public void At_most_twenty_drafts_and_ten_megabytes_each()
    {
        var store = new DraftStore(Root());
        var ids = Enumerable.Range(0, DraftStore.MaxDrafts).Select(_ => store.Create()).ToList();
        var error = Assert.Throws<PrintRequestException>(() => store.Create());
        Assert.Contains("20", error.Message);
        store.Discard(ids[0]);
        store.Create();   // one slot free again

        var png = TestEnv.Png(400, 400);
        var big = new byte[TemplateAssets.MaxBytes - 10];
        png.AsSpan(0, 8).CopyTo(big);   // PNG signature
        big[0] = 0x89; big[1] = 0x50; big[2] = 0x4E; big[3] = 0x47;
        var id = ids[1];
        for (var i = 0; i < 10; i++)
            store.SaveAsset(id, $"i{i}.png", big);   // ~10 MB in total
        var overflow = Assert.Throws<PrintRequestException>(() => store.SaveAsset(id, "x.png", big));
        Assert.Contains("10 MB", overflow.Message);
        store.SaveAsset(id, "i0.png", big);   // replacing a file does not count twice
    }

    [Fact]
    public void Identifiers_are_never_paths()
    {
        var store = new DraftStore(Root());
        Assert.Throws<PrintRequestException>(() => store.Resolve("../.."));
        Assert.Throws<PrintRequestException>(() => store.Resolve("0123456789ABCDEF0123456789ABCDEF"));   // upper case is not generated
        Assert.Null(store.Resolve(null));
        store.Discard("../..");   // ignored
        Assert.True(Directory.Exists(store.Root));
    }
}
