using MiniPrinter.Control;

namespace MiniPrinter.Gui.Tests;

/// <summary>template-gallery and tray-quick-templates: the cards, the search, the pictures and the menu of the icon.</summary>
public class TemplateGalleryTests
{
    private static TemplateDto Template(string name, string title, string description = "", string source = "builtin") =>
        new(name, title, description, [], false, source);

    private static readonly TemplateDto[] Templates =
    [
        Template("qr", "Código QR", "Un código QR con un texto"),
        Template("label", "Etiqueta", "Etiqueta con título y subtítulo"),
        Template("todo", "Lista de tareas", "Tareas con casillas"),
        Template("mia", "Mi plantilla", "Hecha por mí", "user"),
        Template("wifi", "Wi-Fi", "Código QR para unirse a la red", "override"),
    ];

    private sealed class FakeThumbnails : IThumbnailSource
    {
        private int _running;
        public int MaxRunning { get; private set; }
        public List<string> Asked { get; } = [];
        public HashSet<string> Failing { get; } = [];
        public HashSet<string> Missing { get; } = [];
        public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(30);

        public async Task<byte[]?> GetAsync(string name, int width)
        {
            lock (Asked)
                Asked.Add($"{name}@{width}");
            var now = Interlocked.Increment(ref _running);
            MaxRunning = Math.Max(MaxRunning, now);
            try
            {
                await Task.Delay(Delay);
                if (Failing.Contains(name))
                    throw new HttpRequestException("down");
                return Missing.Contains(name) ? null : [0x89, (byte)'P', (byte)name[0]];
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }
    }

    private static TemplateGalleryViewModel Create(out FakeThumbnails thumbnails, IReadOnlyList<string>? recent = null)
    {
        thumbnails = new FakeThumbnails();
        var gallery = new TemplateGalleryViewModel(thumbnails, recent: recent);
        gallery.SetTemplates(Templates);
        return gallery;
    }

    // ---- cards ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Every_template_is_a_card_with_its_origin_in_words()
    {
        var gallery = Create(out _);
        Assert.Equal(["qr", "label", "todo", "mia", "wifi"], gallery.Cards.Select(c => c.Name));
        Assert.Equal(["Integrada", "Integrada", "Integrada", "Propia", "Sustituye a la integrada"], gallery.Cards.Select(c => c.SourceText));
    }

    [Theory]
    [InlineData("qr", new[] { "qr", "wifi" })] // the description counts too ("Código QR para unirse…")
    [InlineData("TAREAS", new[] { "todo" })]
    [InlineData("etiqueta", new[] { "label" })]
    [InlineData("codigo", new[] { "qr", "wifi" })] // no accents needed
    [InlineData("inexistente", new string[0])]
    [InlineData("", new[] { "qr", "label", "todo", "mia", "wifi" })]
    public void The_search_filters_by_title_and_description(string query, string[] expected)
    {
        var gallery = Create(out _);
        gallery.Search = query;
        Assert.Equal(expected, gallery.Cards.Select(c => c.Name));
        Assert.Equal(expected.Length == 0, gallery.HasNoMatches);
    }

    [Fact]
    public void The_recently_used_come_first_in_the_order_of_use()
    {
        var gallery = Create(out _, recent: ["wifi", "todo"]);
        Assert.Equal(["wifi", "todo", "qr", "label", "mia"], gallery.Cards.Select(c => c.Name));
        gallery.SetRecent(["mia"]);
        Assert.Equal(["mia", "qr", "label", "todo", "wifi"], gallery.Cards.Select(c => c.Name));
    }

    [Fact]
    public void A_recent_one_that_no_longer_exists_is_ignored()
    {
        var gallery = Create(out _, recent: ["borrada", "todo"]);
        Assert.Equal("todo", gallery.Cards[0].Name);
        Assert.Equal(5, gallery.Cards.Count);
    }

    [Fact]
    public void Cards_that_stay_are_reused_with_their_picture_when_the_list_is_refreshed()
    {
        var gallery = Create(out _);
        var qr = gallery.Cards.Single(c => c.Name == "qr");
        gallery.LoadThumbnailsAsync().GetAwaiter().GetResult();
        gallery.SetTemplates([Templates[0] with { Title = "QR nuevo" }, Templates[1]]);
        Assert.Equal(["qr", "label"], gallery.Cards.Select(c => c.Name));
        Assert.Same(qr, gallery.Cards[0]);
        Assert.Equal("QR nuevo", qr.Title);
        Assert.Equal(ThumbnailState.Ready, qr.State);
    }

    [Fact]
    public void Choosing_a_card_selects_its_template_and_says_so()
    {
        var gallery = Create(out _);
        var chosen = new List<string?>();
        gallery.Chosen += chosen.Add;
        gallery.SelectedCard = gallery.Cards[2];
        Assert.Equal("todo", gallery.SelectedName);
        gallery.SelectedName = "todo"; // the same again says nothing
        gallery.SelectedName = "label";
        Assert.Equal(["todo", "label"], chosen);
        Assert.Equal("label", gallery.SelectedCard?.Name);
    }

    [Fact]
    public void The_choice_between_gallery_and_list_is_remembered_through_an_event()
    {
        var gallery = Create(out _);
        Assert.True(gallery.IsGallery);
        var seen = new List<TemplatesViewChoice>();
        gallery.ViewChanged += seen.Add;
        gallery.IsGallery = false;
        gallery.View = TemplatesViewChoice.List; // the same again
        gallery.IsGallery = true;
        Assert.Equal([TemplatesViewChoice.List, TemplatesViewChoice.Gallery], seen);
    }

    [Fact]
    public void The_two_buttons_switch_the_view_and_say_which_one_is_in_use()
    {
        var gallery = Create(out _);
        gallery.ShowList.Execute(null);
        Assert.True(gallery.IsList);
        Assert.False(gallery.IsGallery);
        gallery.ShowGallery.Execute(null);
        Assert.True(gallery.IsGallery);
        Assert.False(gallery.IsList);
    }

    // ---- pictures -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_pictures_are_asked_for_at_most_two_at_a_time()
    {
        var gallery = Create(out var thumbnails);
        await gallery.LoadThumbnailsAsync();
        Assert.Equal(5, thumbnails.Asked.Count);
        Assert.True(thumbnails.MaxRunning is >= 1 and <= TemplateGalleryViewModel.MaxConcurrentThumbnails, $"at once: {thumbnails.MaxRunning}");
        Assert.All(thumbnails.Asked, a => Assert.EndsWith("@160", a));
        Assert.All(gallery.Cards, c => Assert.Equal(ThumbnailState.Ready, c.State));
        Assert.All(gallery.Cards, c => Assert.NotNull(c.Thumbnail));
    }

    [Fact]
    public async Task A_card_shows_a_marker_until_its_picture_arrives()
    {
        var gallery = Create(out var thumbnails);
        thumbnails.Delay = TimeSpan.FromMilliseconds(200);
        var card = gallery.Cards[0];
        Assert.Equal("…", card.Marker);
        var loading = gallery.LoadThumbnailsAsync();
        Assert.Equal(ThumbnailState.Loading, card.State);
        Assert.Equal("…", card.Marker);
        await loading;
        Assert.Equal("", card.Marker);
    }

    [Fact]
    public async Task A_picture_that_fails_or_is_missing_leaves_a_marker_and_the_others_still_arrive()
    {
        var gallery = Create(out var thumbnails);
        thumbnails.Failing.Add("qr");
        thumbnails.Missing.Add("todo");
        await gallery.LoadThumbnailsAsync();
        Assert.Equal(ThumbnailState.Failed, gallery.Cards.Single(c => c.Name == "qr").State);
        Assert.Equal(ThumbnailState.Failed, gallery.Cards.Single(c => c.Name == "todo").State);
        Assert.Equal("Sin vista previa", gallery.Cards.Single(c => c.Name == "qr").Marker);
        Assert.Equal(ThumbnailState.Ready, gallery.Cards.Single(c => c.Name == "label").State);
    }

    [Fact]
    public async Task Only_the_missing_pictures_are_asked_for_and_a_changed_template_is_asked_for_again()
    {
        var gallery = Create(out var thumbnails);
        await gallery.LoadThumbnailsAsync();
        thumbnails.Asked.Clear();
        await gallery.LoadThumbnailsAsync();
        Assert.Empty(thumbnails.Asked);

        gallery.Invalidate("label");
        await gallery.LoadThumbnailsAsync();
        Assert.Equal(["label@160"], thumbnails.Asked);
    }

    [Fact]
    public async Task A_new_template_gets_its_picture_without_asking_for_the_others_again()
    {
        var gallery = Create(out var thumbnails);
        await gallery.LoadThumbnailsAsync();
        thumbnails.Asked.Clear();
        gallery.SetTemplates([.. Templates, Template("nueva", "Nueva")]);
        await gallery.LoadThumbnailsAsync();
        Assert.Equal(["nueva@160"], thumbnails.Asked);
    }

    // ---- the menu of the icon ------------------------------------------------------------------------------------------

    private static Dictionary<string, Dictionary<string, Dictionary<string, string>>> Favorites(params (string Template, string[] Names)[] entries) =>
        entries.ToDictionary(e => e.Template, e => e.Names.ToDictionary(n => n, _ => new Dictionary<string, string> { ["a"] = "1" }));

    [Fact]
    public void Favorites_are_listed_as_template_dash_name_in_order()
    {
        var items = TrayTemplatesMenu.Favorites(Favorites(("label", ["Envío", "Aviso"]), ("qr", ["Web"])), Templates);
        Assert.All(items, i => Assert.Equal(TrayTemplateKind.Favorite, i.Kind));
        Assert.Equal(["Código QR — Web", "Etiqueta — Aviso", "Etiqueta — Envío"], items.Select(i => i.Text));
        Assert.Equal(("qr", "Web"), (items[0].Template, items[0].Favorite));
    }

    [Fact]
    public void A_favorite_of_a_template_that_was_deleted_is_left_out()
    {
        var items = TrayTemplatesMenu.Favorites(Favorites(("borrada", ["Vieja"]), ("qr", ["Web"])), Templates);
        Assert.Equal(["Código QR — Web"], items.Select(i => i.Text));
    }

    [Fact]
    public void Without_favorites_the_menu_says_so_in_a_line_that_does_nothing()
    {
        var items = TrayTemplatesMenu.Favorites([], Templates);
        var empty = Assert.Single(items);
        Assert.Equal(TrayTemplateKind.Empty, empty.Kind);
        Assert.Equal("(sin favoritos)", empty.Text);
    }

    [Fact]
    public void Up_to_fifteen_favorites_are_flat_and_more_are_grouped_by_template()
    {
        var fifteen = TrayTemplatesMenu.Favorites(Favorites(("qr", [.. Enumerable.Range(1, 15).Select(i => $"f{i:00}")])), Templates);
        Assert.Equal(15, fifteen.Count);
        Assert.All(fifteen, i => Assert.Equal(TrayTemplateKind.Favorite, i.Kind));

        var sixteen = TrayTemplatesMenu.Favorites(Favorites(("qr", [.. Enumerable.Range(1, 10).Select(i => $"q{i:00}")]), ("label", [.. Enumerable.Range(1, 6).Select(i => $"l{i:00}")])), Templates);
        Assert.Equal(["Código QR", "Etiqueta"], sixteen.Select(i => i.Text).Order());
        Assert.All(sixteen, g => Assert.Equal(TrayTemplateKind.Group, g.Kind));
        var group = sixteen.Single(g => g.Template == "label");
        Assert.Equal(6, group.Children!.Count);
        Assert.All(group.Children, c => Assert.Equal(("label", TrayTemplateKind.Favorite), (c.Template, c.Kind)));
        Assert.Equal("l01", group.Children[0].Text);
    }

    [Fact]
    public void Recent_templates_keep_their_order_skip_deleted_ones_and_are_five_at_most()
    {
        var items = TrayTemplatesMenu.Recent(["wifi", "borrada", "todo", "qr", "label", "mia", "extra"], Templates);
        Assert.All(items, i => Assert.Equal(TrayTemplateKind.Recent, i.Kind));
        Assert.Equal(["wifi", "todo", "qr", "label", "mia"], items.Select(i => i.Template));
        Assert.Equal("Wi-Fi", items[0].Text);
    }

    [Fact]
    public void Without_recent_templates_the_menu_says_so()
    {
        var empty = Assert.Single(TrayTemplatesMenu.Recent([], Templates));
        Assert.Equal(TrayTemplateKind.Empty, empty.Kind);
        Assert.Equal("(ninguna reciente)", empty.Text);
    }

    // ---- the favorites file ----------------------------------------------------------------------------------------------

    [Fact]
    public void The_favorites_file_round_trips_and_a_damaged_one_gives_none()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mp-fav-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "favorites.json");
        try
        {
            Assert.Empty(TemplateFavorites.Read(path));
            var favorites = Favorites(("qr", ["Web"]));
            TemplateFavorites.Write(path, favorites);
            Assert.Equal("1", TemplateFavorites.Read(path)["qr"]["Web"]["a"]);

            File.WriteAllText(path, "{ no es json");
            Assert.Empty(TemplateFavorites.Read(path));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
