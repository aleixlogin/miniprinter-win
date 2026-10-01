using MiniPrinter.Protocol.Catalog;

namespace MiniPrinter.Protocol.Tests;

public class CatalogTests
{
    [Fact]
    public void Default_catalog_loads_tiny_profiles_and_models()
    {
        var catalog = PrinterCatalog.Default;
        Assert.NotEmpty(catalog.Models);
        Assert.NotNull(catalog.GetProfile("d1"));
        Assert.NotNull(catalog.GetProfile("x6h"));
    }

    [Theory]
    [InlineData("X5h-E07A", "pocket_printer", "d1")]
    [InlineData("X5H-ABCD", "ewtto_et_z0504", "x6h")]
    [InlineData("X6h-1234", "pocket_printer", "d1")]
    public void Detect_is_case_sensitive_and_prefers_exact_case(string name, string model, string profile)
    {
        var match = PrinterCatalog.Default.Detect(name);
        Assert.NotNull(match);
        Assert.Equal(model, match.Model.Key);
        Assert.Equal(profile, match.Profile.Key);
    }

    [Fact]
    public void Detect_returns_null_for_unknown_or_ambiguous_names()
    {
        Assert.Null(PrinterCatalog.Default.Detect("Brother DCP-7055"));
        Assert.Null(PrinterCatalog.Default.Detect(""));
        // Neither "X5h" nor "X5H" matches with exact case, and both match case-insensitively.
        Assert.Null(PrinterCatalog.Default.Detect("x5h-0000"));
    }

    [Fact]
    public void D1_profile_matches_the_x5h_recipe_parameters()
    {
        var d1 = PrinterCatalog.Default.RequireProfile("d1");
        Assert.Equal(384, d1.WidthPx);
        Assert.Equal(48, d1.WidthBytes);
        Assert.Equal(200, d1.Dpi);
        Assert.True(d1.UseSpp);
        Assert.Equal(RowEncoding.Rle, d1.Encoding);
        Assert.Equal(180, d1.ChunkSize);
        Assert.Equal(4, d1.DelayMs);
        Assert.Equal(2, d1.PostPrintFeedCount);
        Assert.Equal(10, d1.Speed.Select(isText: false));
        Assert.Equal(5000, d1.Energy.Select(isText: false, darkness: 3));
        Assert.Equal(8000, d1.Energy.Select(isText: true, darkness: 3));
    }

    [Theory]
    [InlineData(0, 1)] [InlineData(1, 1)] [InlineData(2, 1)] [InlineData(3, 2)] [InlineData(4, 3)] [InlineData(9, 3)]
    public void Level_profile_maps_darkness_to_bands(int darkness, int expected)
    {
        Assert.Equal(expected, new LevelProfile(1, 2, 3).Select(darkness));
    }
}
