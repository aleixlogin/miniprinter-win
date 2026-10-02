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

    public static void Map(RouteGroupBuilder api, TemplateCatalog catalog)
    {
        api.MapGet("/templates", () => Results.Json(catalog.Definitions, ControlDefaults.Json));

        api.MapGet("/templates/{name}", (string name) =>
            catalog.GetJson(name) is { } json
                ? Results.Text(json, "application/json; charset=utf-8")
                : Results.Json(new ApiError($"Unknown template '{name}'."), ControlDefaults.Json, statusCode: 404));

        api.MapPut("/templates/{name}", async (string name, HttpContext http) =>
        {
            var json = await ReadText(http, TemplateLayout.MaxJsonBytes);
            var layout = Guard(() => catalog.Save(name, json));
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
            throw new PrintRequestException(ex.Message);
        }
    }

    private static void Guard(Action action) => Guard(() =>
    {
        action();
        return 0;
    });
}

public sealed record TemplateValidation(bool Valid, string Name, int Blocks);
