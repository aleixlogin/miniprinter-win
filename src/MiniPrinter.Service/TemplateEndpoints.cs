using System.Text;
using System.Text.Json;
using MiniPrinter.Control;
using MiniPrinter.Imaging;

namespace MiniPrinter.Service;

/// <summary>A template print/preview request: field values plus the optional copies, rows and darkness.</summary>
public sealed record TemplateRequest(
    Dictionary<string, string> Fields,
    int Copies = 1,
    IReadOnlyList<IReadOnlyDictionary<string, string>>? Rows = null,
    int? Darkness = null)
{
    /// <summary>Fields of the first label (base fields overridden by the first row), for previews.</summary>
    public IReadOnlyDictionary<string, string> PreviewFields()
    {
        if (Rows is not { Count: > 0 })
            return Fields;
        var merged = new Dictionary<string, string>(Fields, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in Rows[0])
            merged[key] = value;
        return merged;
    }
}

/// <summary>
/// Template management shared by the control API (<c>/api/templates</c>) and the automation API
/// (<c>/api/v1/templates</c>): list, read, create/replace, validate and delete templates and their
/// images (design.md D5). Writes go through the service, which owns the shared templates folder.
/// </summary>
public static class TemplateEndpoints
{
    public static async Task<TemplateRequest> ReadRequest(HttpContext http)
    {
        JsonElement element;
        try
        {
            element = await http.Request.ReadFromJsonAsync<JsonElement>(ControlDefaults.Json);
        }
        catch (JsonException ex)
        {
            throw new PrintRequestException($"Invalid JSON: {ex.Message}");
        }
        if (element.ValueKind != JsonValueKind.Object)
            throw new PrintRequestException("Expected a JSON object with the template fields.");

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var copies = 1;
        int? darkness = null;
        List<IReadOnlyDictionary<string, string>>? rows = null;
        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name.ToLowerInvariant())
            {
                case "copies":
                    copies = property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var c)
                        ? c : throw new PrintRequestException("copies must be a whole number.");
                    break;
                case "darkness":
                    darkness = property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var d) ? d : null;
                    break;
                case "rows":
                    if (property.Value.ValueKind != JsonValueKind.Array)
                        throw new PrintRequestException("rows must be a list of objects with the fields of each label.");
                    rows = [];
                    foreach (var row in property.Value.EnumerateArray())
                    {
                        if (row.ValueKind != JsonValueKind.Object)
                            throw new PrintRequestException("Each entry of rows must be an object.");
                        rows.Add(ToStrings(row));
                    }
                    break;
                default:
                    fields[property.Name] = ToString(property.Value);
                    break;
            }
        }
        return new TemplateRequest(fields, copies, rows, darkness);
    }

    private static Dictionary<string, string> ToStrings(JsonElement obj) =>
        obj.EnumerateObject().ToDictionary(p => p.Name, p => ToString(p.Value), StringComparer.OrdinalIgnoreCase);

    private static string ToString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Null => "",
        _ => value.GetRawText(),
    };

    public static void Map(RouteGroupBuilder api, TemplateCatalog catalog, DraftStore drafts, PrintRequests print)
    {
        api.MapGet("/templates", () => Results.Json(catalog.Definitions, ControlDefaults.Json));

        // What the editor needs to build its controls (generated from the engine's block registry).
        api.MapGet("/templates/schema", () => Results.Json(BlockSchema.Build(), ControlDefaults.Json));

        api.MapGet("/templates/{name}", (string name) =>
            catalog.GetJson(name) is { } json
                ? Results.Text(json, "application/json; charset=utf-8")
                : Results.Json(new ApiError($"Unknown template '{name}'."), ControlDefaults.Json, statusCode: 404));

        api.MapPut("/templates/{name}", async (string name, HttpContext http) =>
        {
            var json = await ReadText(http, TemplateLayout.MaxJsonBytes);
            var draftId = http.Request.Query["draft"].ToString();
            var draftDir = drafts.Resolve(draftId);
            var layout = Guard(() => catalog.Save(name, json, draftDir));
            drafts.Discard(draftId);   // only reached when the save succeeded
            return Results.Json(layout.ToDefinition(catalog.Find(layout.Name)?.Source ?? TemplateCatalog.User), ControlDefaults.Json);
        });

        api.MapDelete("/templates/{name}", (string name) =>
        {
            Guard(() => catalog.Delete(name));
            return Results.NoContent();
        });

        api.MapPost("/templates/validate", async (HttpContext http) =>
        {
            var json = await ReadText(http, TemplateLayout.MaxJsonBytes);
            var layout = Guard(() => catalog.Validate(json));
            return Results.Json(new TemplateValidation(true, layout.Name, layout.Blocks.Count), ControlDefaults.Json);
        });

        // Preview of a saved template: the PNG, with the rows of each block in X-Template-Blocks.
        api.MapPost("/templates/{name}/preview", async (string name, HttpContext http) =>
        {
            var request = await ReadRequest(http);
            return PreviewResult(http, print.RenderTemplateDetailed(name, request.PreviewFields()));
        });

        // Preview of a template that is not saved: { "template": {...}, "fields": {...}, "draft": "<id>" }.
        api.MapPost("/templates/preview", async (HttpContext http) =>
        {
            var body = await ReadText(http, TemplateLayout.MaxJsonBytes + 64 * 1024);
            string template;
            Dictionary<string, string> fields = new(StringComparer.OrdinalIgnoreCase);
            string? draft = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("template", out var t)
                    || t.ValueKind != JsonValueKind.Object)
                    throw new PrintRequestException("Expected {\"template\": {...}, \"fields\": {...}, \"draft\": \"id\"}.");
                template = t.GetRawText();
                if (doc.RootElement.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Object)
                    fields = ToStrings(f);
                if (doc.RootElement.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.String)
                    draft = d.GetString();
            }
            catch (JsonException ex)
            {
                throw new PrintRequestException($"Invalid JSON: {ex.Message}");
            }
            return PreviewResult(http, print.PreviewDraft(template, fields, drafts.Resolve(draft)));
        });

        api.MapPut("/templates/{name}/assets/{file}", async (string name, string file, HttpContext http) =>
        {
            using var body = new MemoryStream();
            await http.Request.Body.CopyToAsync(body);
            Guard(() => catalog.SaveAsset(name, file, body.ToArray()));
            return Results.NoContent();
        });

        api.MapDelete("/templates/{name}/assets/{file}", (string name, string file) =>
        {
            Guard(() => catalog.DeleteAsset(name, file));
            return Results.NoContent();
        });

        // Drafts: images of a template that is not saved yet.
        api.MapPost("/templates/drafts", () => Results.Json(new DraftInfo(drafts.Create()), ControlDefaults.Json));

        api.MapPut("/templates/drafts/{id}/assets/{file}", async (string id, string file, HttpContext http) =>
        {
            drafts.Resolve(id);
            using var body = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = await http.Request.Body.ReadAsync(buffer)) > 0)
            {
                body.Write(buffer, 0, read);
                if (body.Length > TemplateAssets.MaxBytes)
                    throw new PrintRequestException($"La imagen supera el máximo de {TemplateAssets.MaxBytes / (1024 * 1024)} MB.");
            }
            drafts.SaveAsset(id, file, body.ToArray());
            return Results.NoContent();
        });

        api.MapDelete("/templates/drafts/{id}/assets/{file}", (string id, string file) =>
        {
            drafts.DeleteAsset(id, file);
            return Results.NoContent();
        });

        api.MapDelete("/templates/drafts/{id}", (string id) =>
        {
            drafts.Discard(id);
            return Results.NoContent();
        });
    }

    /// <summary>The preview PNG plus the rows of each block in the <c>X-Template-Blocks</c> header.</summary>
    private static IResult PreviewResult(HttpContext http, RenderResult result)
    {
        using var png = new MemoryStream();
        MonoPng.Save(result.Bitmap, png);
        http.Response.Headers["X-Template-Blocks"] = JsonSerializer.Serialize(result.Blocks, ControlDefaults.Json);
        return Results.File(png.ToArray(), "image/png");
    }

    private static async Task<string> ReadText(HttpContext http, int maxBytes)
    {
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await http.Request.Body.ReadAsync(buffer)) > 0)
        {
            body.Write(buffer, 0, read);
            if (body.Length > maxBytes)
                throw new PrintRequestException($"The template exceeds {maxBytes / 1024} KB.");
        }
        return Encoding.UTF8.GetString(body.ToArray());
    }

    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (TemplateException ex)
        {
            throw new PrintRequestException(ex.Message, ex.Block, ex.Property);
        }
    }

    private static void Guard(Action action) => Guard(() =>
    {
        action();
        return 0;
    });
}

public sealed record TemplateValidation(bool Valid, string Name, int Blocks);

public sealed record DraftInfo(string Id);
