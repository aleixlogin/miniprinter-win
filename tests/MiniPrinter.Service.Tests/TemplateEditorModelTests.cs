using System.Text.Json;
using MiniPrinter.Control;
using MiniPrinter.Imaging;

namespace MiniPrinter.Service.Tests;

/// <summary>The editor's model (blocks, fields, undo/redo, canonical JSON) without any UI.</summary>
public class TemplateEditorModelTests
{
    private static readonly TemplateSchemaDto Schema = JsonSerializer.Deserialize<TemplateSchemaDto>(
        JsonSerializer.Serialize(BlockSchema.Build(), ControlDefaults.Json), ControlDefaults.Json)!;

    private static SchemaPropDto Prop(string block, string name) =>
        Schema.Blocks.Single(b => b.Type == block).Props.Single(p => p.Name == name);

    private const string Sample = """
        { "name": "recibo", "title": "Recibo",
          "fields": [ { "name": "cliente", "kind": "text", "required": true }, { "name": "total", "kind": "text" } ],
          "blocks": [
            { "type": "text", "value": "Hola {{cliente}}", "size": 14 },
            { "type": "line" },
            { "type": "columns", "when": "{{total}}", "left": "TOTAL", "right": "{{total}}" } ] }
        """;

    private static TemplateEditorModel Model(string json = Sample, Func<DateTime>? clock = null) => new(json, Schema, clock);

    // ---- JSON -------------------------------------------------------------------------------------

    [Fact]
    public void Json_is_canonical_and_round_trips()
    {
        var model = Model();
        Assert.Contains("\n", model.Json);   // indented
        Assert.Equal(model.Json, Model(model.Json).Json);
        Assert.Equal("recibo", model.Name);
        Assert.False(model.IsDirty);
    }

    [Fact]
    public void Comments_are_detected_so_the_user_can_be_warned()
    {
        Assert.False(Model().HadComments);
        Assert.True(Model("{ // nota\n \"name\": \"t\", \"blocks\": [] }").HadComments);
        Assert.False(Model("{ \"name\": \"http://x\", \"blocks\": [] }").HadComments);   // // inside a string is not a comment
    }

    [Fact]
    public void Invalid_json_is_rejected_by_the_constructor()
    {
        Assert.Throws<InvalidDataException>(() => Model("{ no"));
        Assert.Throws<InvalidDataException>(() => Model("[1, 2]"));
    }

    [Fact]
    public void Blank_and_duplicate_make_valid_templates()
    {
        var blank = Model(TemplateEditorModel.Blank("nueva"));
        Assert.Equal("nueva", blank.Name);
        Assert.Equal(1, blank.BlockCount);

        var copy = Model(TemplateEditorModel.Duplicate(Sample, "recibo2"));
        Assert.Equal("recibo2", copy.Name);
        Assert.Equal("Recibo (copia)", copy.GetTemplateProperty("title"));
        Assert.Equal(3, copy.BlockCount);
    }

    [Theory]
    [InlineData("recibo", true)]
    [InlineData("mi-etiqueta_2", true)]
    [InlineData("../config", false)]
    [InlineData("con espacio", false)]
    [InlineData("", false)]
    [InlineData("list", false)]
    [InlineData("schema", false)]
    public void Template_names_follow_the_service_rule(string name, bool valid) =>
        Assert.Equal(valid, TemplateEditorModel.IsValidName(name));

    // ---- blocks ------------------------------------------------------------------------------------

    [Fact]
    public void Blocks_can_be_added_duplicated_moved_and_removed()
    {
        var model = Model();
        var qr = model.AddBlock("qr");
        Assert.Equal(3, qr);
        Assert.Equal("qr", model.BlockType(3));
        Assert.Equal("https://example.com", model.GetBlockProperty(3, "data"));

        var inserted = model.AddBlock("spacer", at: 0);
        Assert.Equal(0, inserted);
        Assert.Equal("spacer", model.BlockType(0));

        var copy = model.DuplicateBlock(1);   // the text block
        Assert.Equal(2, copy);
        Assert.Equal("Hola {{cliente}}", model.GetBlockProperty(2, "value"));

        model.MoveBlock(2, 0);
        Assert.Equal("text", model.BlockType(0));

        model.RemoveBlock(0);
        Assert.Equal(["spacer", "text", "line", "columns", "qr"], Enumerable.Range(0, model.BlockCount).Select(model.BlockType));
    }

    [Fact]
    public void The_block_limit_is_enforced()
    {
        var model = Model(TemplateEditorModel.Blank("t"));
        while (model.CanAddBlock)
            model.AddBlock("line");
        Assert.Equal(100, model.BlockCount);
        Assert.Throws<InvalidOperationException>(() => model.AddBlock("line"));
        Assert.Throws<InvalidOperationException>(() => model.DuplicateBlock(0));
        Assert.Throws<ArgumentException>(() => Model().AddBlock("video"));
    }

    [Fact]
    public void Properties_are_stored_with_their_natural_json_type()
    {
        var model = Model();
        model.SetBlockProperty(0, Prop("text", "size"), "20");
        model.SetBlockProperty(0, Prop("text", "bold"), "true");
        model.SetBlockProperty(0, Prop("text", "align"), "center");
        Assert.Contains("\"size\": 20", model.Json);
        Assert.Contains("\"bold\": true", model.Json);
        Assert.Contains("\"align\": \"center\"", model.Json);

        model.SetBlockProperty(0, Prop("text", "size"), "{{tam}}");   // a placeholder stays text
        Assert.Contains("\"size\": \"{{tam}}\"", model.Json);

        model.SetBlockProperty(0, Prop("text", "bold"), "false");   // false is the default: removed
        Assert.DoesNotContain("bold", model.Json);

        model.SetBlockProperty(0, Prop("text", "align"), "");   // blank removes
        Assert.DoesNotContain("align", model.Json);
        Assert.Equal("", model.GetBlockProperty(0, "align"));
    }

    [Fact]
    public void Setting_the_same_value_changes_nothing()
    {
        var model = Model();
        var changes = 0;
        model.Changed += () => changes++;
        model.SetBlockProperty(0, Prop("text", "size"), "14");
        Assert.Equal(0, changes);
        Assert.False(model.CanUndo);
    }

    // ---- template properties and fields ----------------------------------------------------------------

    [Fact]
    public void Template_properties_title_mode_gap_frame()
    {
        var model = Model();
        model.SetTemplateProperty("gap", "20");
        model.SetTemplateProperty("mode", "image");
        model.SetTemplateProperty("frame", "{{marco}}");
        model.SetTemplateProperty("title", "Otro");
        Assert.Contains("\"gap\": 20", model.Json);
        Assert.Contains("\"mode\": \"image\"", model.Json);
        Assert.Equal("Otro", model.GetTemplateProperty("title"));
        model.SetTemplateProperty("frame", null);
        Assert.DoesNotContain("frame", model.Json);
    }

    [Fact]
    public void Fields_are_validated_added_edited_and_removed()
    {
        var model = Model();
        Assert.Equal(["cliente", "total"], model.FieldNames);
        Assert.Contains("duplicado", model.CheckFieldName("Cliente"));
        Assert.NotNull(model.CheckFieldName("1abc"));
        Assert.NotNull(model.CheckFieldName("now"));
        Assert.Null(model.CheckFieldName("cliente", except: 0));   // renaming a field to its own name is fine
        Assert.Throws<ArgumentException>(() => model.AddField("cliente"));

        var i = model.AddField("nota", "multiline");
        model.SetFieldProperty(i, "label", "Nota al pie");
        model.SetFieldProperty(i, "required", "true");
        model.SetFieldProperty(i, "default", "Gracias");
        model.SetFieldProperty(i, "kind", "choice");
        model.SetFieldProperty(i, "choices", "a, b , c");
        Assert.Equal("a, b, c", model.GetFieldProperty(i, "choices"));
        Assert.Equal("Gracias", model.GetFieldProperty(i, "default"));
        Assert.Contains("\"required\": true", model.Json);
        Assert.Throws<ArgumentException>(() => model.SetFieldProperty(i, "name", "total"));

        model.RemoveField(i);
        Assert.Equal(["cliente", "total"], model.FieldNames);
    }

    [Fact]
    public void Field_usage_lists_the_blocks_that_use_a_placeholder()
    {
        var model = Model();
        Assert.Equal([1], model.FieldUsage("cliente"));
        Assert.Equal([3], model.FieldUsage("total"));   // in 'when' and 'right'
        Assert.Empty(model.FieldUsage("nada"));

        model.SetTemplateProperty("frame", "{{cliente}}");
        Assert.Equal([0, 1], model.FieldUsage("cliente"));   // 0 = the template's frame
        model.Block(0)["value"] = "{{ cliente | upper }}";
        Assert.Contains(1, model.FieldUsage("cliente"));
        model.Block(0)["value"] = "{{cliente2}}";
        Assert.DoesNotContain(1, model.FieldUsage("cliente"));   // another field with the same prefix
    }

    // ---- raw JSON -------------------------------------------------------------------------------------

    [Fact]
    public void Raw_json_replaces_the_model_only_when_it_is_valid()
    {
        var model = Model();
        var before = model.Json;
        Assert.NotNull(model.TryReplace("{ esto no"));
        Assert.Equal(before, model.Json);   // last valid model kept
        Assert.False(model.CanUndo);

        Assert.Null(model.TryReplace("""{ "name": "recibo", "blocks": [ { "type": "line" } ] }"""));
        Assert.Equal(1, model.BlockCount);
        model.Undo();
        Assert.Equal(before, model.Json);

        var changes = 0;
        model.Changed += () => changes++;
        Assert.Null(model.TryReplace(model.Json));   // same content: not a change
        Assert.Equal(0, changes);
    }

    // ---- undo and redo -----------------------------------------------------------------------------------

    [Fact]
    public void Undo_restores_a_removed_block_and_redo_applies_it_again()
    {
        var model = Model();
        var before = model.Json;
        model.RemoveBlock(1);
        Assert.Equal(2, model.BlockCount);
        Assert.True(model.IsDirty);

        model.Undo();
        Assert.Equal(before, model.Json);
        Assert.Equal("line", model.BlockType(1));
        Assert.False(model.IsDirty);   // back to the saved state

        model.Redo();
        Assert.Equal(2, model.BlockCount);
        Assert.False(model.CanRedo);
    }

    [Fact]
    public void A_new_change_clears_the_redo_history()
    {
        var model = Model();
        model.AddBlock("line");
        model.Undo();
        Assert.True(model.CanRedo);
        model.AddBlock("spacer");
        Assert.False(model.CanRedo);
    }

    [Fact]
    public void Continuous_typing_is_a_single_undo_step()
    {
        var now = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
        var model = Model(clock: () => now);
        var value = Prop("text", "value");
        foreach (var text in new[] { "H", "Ho", "Hol", "Hola" })
        {
            model.SetBlockProperty(0, value, text, coalesceKey: "b0.value");
            now = now.AddMilliseconds(100);
        }
        Assert.Equal("Hola", model.GetBlockProperty(0, "value"));
        model.Undo();
        Assert.Equal("Hola {{cliente}}", model.GetBlockProperty(0, "value"));   // one step undoes all of it
        Assert.False(model.CanUndo);
    }

    [Fact]
    public void A_pause_or_another_field_starts_a_new_undo_step()
    {
        var now = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
        var model = Model(clock: () => now);
        model.SetBlockProperty(0, Prop("text", "value"), "A", coalesceKey: "v");
        now = now.AddSeconds(2);   // longer than the 600 ms window
        model.SetBlockProperty(0, Prop("text", "value"), "AB", coalesceKey: "v");
        model.SetBlockProperty(0, Prop("text", "size"), "20", coalesceKey: "s");   // another key
        model.Undo();
        Assert.Equal("14", model.GetBlockProperty(0, "size"));
        model.Undo();
        Assert.Equal("A", model.GetBlockProperty(0, "value"));
    }

    [Fact]
    public void History_keeps_at_most_one_hundred_steps()
    {
        var model = Model();
        for (var i = 0; i < 130; i++)
            model.SetTemplateProperty("title", "t" + i);
        var undone = 0;
        while (model.CanUndo)
        {
            model.Undo();
            undone++;
        }
        Assert.Equal(TemplateEditorModel.MaxHistory, undone);
    }

    [Fact]
    public void Saving_marks_the_current_state_as_clean()
    {
        var model = Model();
        model.AddBlock("line");
        Assert.True(model.IsDirty);
        model.MarkSaved();
        Assert.False(model.IsDirty);
        model.Undo();
        Assert.True(model.IsDirty);
    }
}
