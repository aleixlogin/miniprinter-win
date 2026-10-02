namespace MiniPrinter.Imaging;

/// <summary>One property of a block as the editor sees it. <c>Kind</c> is text, int, number, bool or enum.</summary>
public sealed record SchemaProp(string Name, string Label, string Kind, double? Min, double? Max, double? Default,
    IReadOnlyList<string>? Values, bool Multiline);

public sealed record SchemaBlock(string Type, string Title, IReadOnlyList<SchemaProp> Props);

public sealed record SchemaTemplateProps(int MaxBlocks, int MaxJsonKb, int MinGap, int MaxGap, int DefaultGap, IReadOnlyList<string> Modes);

/// <summary>What the editor needs to build its controls without duplicating the engine's rules (design.md D2).</summary>
public sealed record TemplateSchema(
    IReadOnlyList<SchemaBlock> Blocks,
    IReadOnlyList<SchemaProp> Common,
    IReadOnlyList<string> FieldKinds,
    IReadOnlyList<string> Filters,
    SchemaTemplateProps Template);

/// <summary>Builds the <see cref="TemplateSchema"/> from the block registry, the single source of truth.</summary>
public static class BlockSchema
{
    public static readonly string[] FieldKinds = ["text", "multiline", "choice", "image", "number", "boolean"];

    public static TemplateSchema Build() => new(
        [.. LayoutBlocks.All.Select(t => new SchemaBlock(t.Name, t.Title,
            [.. t.Props.Select(p => ToProp(p.Key, p.Value))]))],
        [new SchemaProp("when", "Solo si este campo tiene valor", "text", null, null, null, null, false)],
        FieldKinds,
        Interpolator.Filters,
        new SchemaTemplateProps(TemplateLayout.MaxBlocks, TemplateLayout.MaxJsonBytes / 1024, 0, 100, 8, ["text", "image"]));

    private static SchemaProp ToProp(string name, PropDef def) => def.Kind switch
    {
        PropKind.Int => new SchemaProp(name, def.Label ?? name, "int", def.Min, def.Max, def.Default, null, false),
        PropKind.Number => new SchemaProp(name, def.Label ?? name, "number", def.Min, def.Max, def.Default, null, false),
        PropKind.Bool => new SchemaProp(name, def.Label ?? name, "bool", null, null, null, null, false),
        PropKind.Enum => new SchemaProp(name, def.Label ?? name, "enum", null, null, null, def.Values, false),
        _ => new SchemaProp(name, def.Label ?? name, "text", null, null, null, null, def.Multiline),
    };
}
