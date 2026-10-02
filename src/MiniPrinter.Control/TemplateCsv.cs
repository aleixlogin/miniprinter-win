using System.Text;

namespace MiniPrinter.Control;

/// <summary>A CSV read for a label batch: one dictionary of field values per row.</summary>
public sealed record CsvBatch(IReadOnlyList<IReadOnlyDictionary<string, string>> Rows, IReadOnlyList<string> IgnoredColumns);

/// <summary>
/// Reads the CSV of a label batch (design.md D8): UTF-8, comma or semicolon separator (detected from the
/// header), a header row with the field names, quoted values with doubled quotes and line breaks inside.
/// </summary>
public static class TemplateCsv
{
    public static CsvBatch Parse(string text, IEnumerable<string>? knownFields = null)
    {
        text = text.TrimStart('﻿');
        var records = Records(text, DetectSeparator(text));
        if (records.Count == 0)
            throw new InvalidDataException("El CSV está vacío.");

        var headers = records[0].Select(h => h.Trim()).ToList();
        if (headers.Any(h => h.Length == 0))
            throw new InvalidDataException("La cabecera del CSV tiene una columna sin nombre.");
        var known = knownFields?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ignored = known is null ? [] : headers.Where(h => !known.Contains(h)).ToList();

        var rows = new List<IReadOnlyDictionary<string, string>>();
        foreach (var record in records.Skip(1))
        {
            if (record.All(string.IsNullOrWhiteSpace))
                continue;
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count && i < record.Count; i++)
                if (known is null || known.Contains(headers[i]))
                    row[headers[i]] = record[i];
            rows.Add(row);
        }
        if (rows.Count == 0)
            throw new InvalidDataException("El CSV no tiene filas de datos debajo de la cabecera.");
        return new CsvBatch(rows, ignored);
    }

    private static char DetectSeparator(string text)
    {
        var header = text.Split('\n', 2)[0];
        return header.Count(c => c == ';') > header.Count(c => c == ',') ? ';' : ',';
    }

    private static List<List<string>> Records(string text, char separator)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (c == separator)
            {
                record.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                record.Add(field.ToString());
                field.Clear();
                records.Add(record);
                record = [];
            }
            else
            {
                field.Append(c);
            }
        }
        if (quoted)
            throw new InvalidDataException("El CSV tiene unas comillas sin cerrar.");
        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }
        return records;
    }
}
