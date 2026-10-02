using System.Text;

namespace MiniPrinter.Imaging.Tests;

/// <summary>Builds small valid PDFs for tests: each page (A4) has a black bar whose width encodes the page number.</summary>
public static class MiniPdf
{
    public static byte[] Create(int pages)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "", // pages tree, filled below
        };
        var kids = new List<string>();
        for (var i = 0; i < pages; i++)
        {
            var pageObj = objects.Count + 1;
            var contentObj = pageObj + 1;
            kids.Add($"{pageObj} 0 R");
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents {contentObj} 0 R >>");
            // Bar from x=70 with width 100*(page) points, near the top of the page.
            var content = $"0 0 0 rg 70 700 {100 * (i + 1)} 60 re f";
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        }
        objects[1] = $"<< /Type /Pages /Kids [{string.Join(' ', kids)}] /Count {pages} >>";

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    public static byte[] Damaged() => Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog garbage");
}
