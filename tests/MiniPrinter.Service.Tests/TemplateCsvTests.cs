using MiniPrinter.Control;

namespace MiniPrinter.Service.Tests;

public class TemplateCsvTests
{
    [Fact]
    public void Csv_detects_semicolons_quotes_and_unknown_columns()
    {
        var csv = TemplateCsv.Parse("﻿title;line1;extra\r\nCajón 1;Cables;x\r\nCajón 2;\"Pilas; AA\";y\r\n\r\n", ["title", "line1"]);
        Assert.Equal(2, csv.Rows.Count);
        Assert.Equal("Cajón 1", csv.Rows[0]["title"]);
        Assert.Equal("Pilas; AA", csv.Rows[1]["line1"]);
        Assert.Equal(["extra"], csv.IgnoredColumns);
        Assert.False(csv.Rows[0].ContainsKey("extra"));
    }

    [Fact]
    public void Csv_handles_commas_doubled_quotes_and_line_breaks_in_values()
    {
        var csv = TemplateCsv.Parse("title,line1\n\"Dijo \"\"hola\"\"\",\"a\nb\"\nB,c");
        Assert.Equal("Dijo \"hola\"", csv.Rows[0]["title"]);
        Assert.Equal("a\nb", csv.Rows[0]["line1"]);
        Assert.Equal("B", csv.Rows[1]["title"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("title,line1\n")]
    [InlineData("title,\n1,2")]
    [InlineData("title\n\"sin cerrar")]
    public void Invalid_csv_is_rejected(string text) => Assert.Throws<InvalidDataException>(() => TemplateCsv.Parse(text));
}
