using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace WinNote.Services;

public static class XlsxImport
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static ImportResult FromFile(string path, MediaStore media, string? displayName = null)
    {        using var archive = ZipFile.OpenRead(path);
        var sourceName = displayName ?? Path.GetFileName(path);
        var title = Path.GetFileNameWithoutExtension(sourceName);

        var shared = ReadSharedStrings(archive);
        var sheets = ReadSheets(archive);
        if (sheets.Count == 0)
        {
            throw new InvalidOperationException("这个 Excel 文件里没有工作表。");
        }

        var html = new StringBuilder();
        var text = new StringBuilder();
        var images = new List<string>();

        foreach (var (name, entryPath) in sheets)
        {
            var entry = archive.GetEntry(entryPath);
            if (entry is null)
            {
                continue;
            }

            XDocument sheet;
            using (var stream = entry.Open())
            {
                sheet = XDocument.Load(stream);
            }

            var table = BuildSheetTable(sheet, shared, out var sheetText);
            if (table is null)
            {
                continue;
            }

            html.Append("<div class=\"md-h2\">").Append(TextDecoding.HtmlEscape(name)).Append("</div>");
            html.Append(table);
            text.AppendLine(name);
            text.AppendLine(sheetText);
        }

        foreach (var entry in archive.Entries.Where(e =>
                     e.FullName.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase)))
        {
            var extension = Path.GetExtension(entry.Name);
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff"))
            {
                continue;
            }

            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var file = media.SaveBytes(memory.ToArray(), extension);
            images.Add(file);
            html.Append("<figure class=\"img\" contenteditable=\"false\" data-file=\"")
                .Append(file)
                .Append("\"><img src=\"")
                .Append(media.UrlFor(file))
                .Append("\" data-file=\"")
                .Append(file)
                .Append("\"><button class=\"img-del\" type=\"button\"></button></figure>");
            if (images.Count >= 30)
            {
                break;
            }
        }

        return new ImportResult
        {
            Title = string.IsNullOrWhiteSpace(title) ? "导入的表格" : title,
            Html = html.ToString(),
            Text = text.ToString().Trim(),
            SourceName = sourceName,
            Images = images,
        };
    }

    public static ImportResult FromFileTemp(string fileName, byte[] bytes, MediaStore media)
    {
        var path = Path.Combine(Path.GetTempPath(), "WinNote_xlsx_" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            File.WriteAllBytes(path, bytes);
            return FromFile(path, media, fileName);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // ignored
            }
        }
    }

    private static List<KeyValuePair<string, string>> ReadSheets(ZipArchive archive)
    {
        var result = new List<KeyValuePair<string, string>>();
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
        {
            return result;
        }

        XDocument workbook;
        using (var stream = workbookEntry.Open())
        {
            workbook = XDocument.Load(stream);
        }

        var rels = ReadRelationships(archive.GetEntry("xl/_rels/workbook.xml.rels"));
        foreach (var sheet in workbook.Descendants(S + "sheet"))
        {
            var name = sheet.Attribute("name")?.Value ?? "工作表";
            var relId = sheet.Attribute(R + "id")?.Value;
            var target = relId is not null && rels.TryGetValue(relId, out var value)
                ? value
                : $"worksheets/sheet{result.Count + 1}.xml";
            result.Add(new KeyValuePair<string, string>(name, ResolvePath("xl", target)));
        }

        if (result.Count == 0)
        {
            var fallback = archive.Entries
                .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var entry in fallback)
            {
                result.Add(new KeyValuePair<string, string>(
                    Path.GetFileNameWithoutExtension(entry.Name),
                    entry.FullName));
            }
        }

        return result;
    }

    private static Dictionary<string, string> ReadRelationships(ZipArchiveEntry? entry)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (entry is null)
        {
            return result;
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        foreach (var relationship in document.Root?.Elements(Rel + "Relationship") ?? Enumerable.Empty<XElement>())
        {
            var id = relationship.Attribute("Id")?.Value;
            var target = relationship.Attribute("Target")?.Value;
            if (id is not null && target is not null)
            {
                result[id] = target;
            }
        }
        return result;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var result = new List<string>();
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return result;
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        foreach (var item in document.Descendants(S + "si"))
        {
            result.Add(string.Concat(item.Descendants(S + "t").Select(t => t.Value)));
        }
        return result;
    }

    private static string? BuildSheetTable(XDocument sheet, List<string> shared, out string plainText)
    {
        plainText = "";
        var rows = sheet.Descendants(S + "row").Take(300).ToList();
        if (rows.Count == 0)
        {
            return null;
        }

        var matrix = new SortedDictionary<int, SortedDictionary<int, string>>();
        var maxColumn = 0;

        foreach (var row in rows)
        {
            var rowAttribute = row.Attribute("r")?.Value;
            var rowIndex = int.TryParse(rowAttribute, out var parsedRow)
                ? parsedRow - 1
                : matrix.Count;

            var cells = row.Elements(S + "c").ToList();
            var columnCursor = 0;
            foreach (var cell in cells)
            {
                var reference = cell.Attribute("r")?.Value;
                var columnIndex = reference is not null ? ColumnIndex(reference) : columnCursor;
                columnCursor = columnIndex + 1;
                if (columnIndex >= 40)
                {
                    continue;
                }

                var value = ReadCellValue(cell, shared);
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (!matrix.TryGetValue(rowIndex, out var rowValues))
                {
                    rowValues = new SortedDictionary<int, string>();
                    matrix[rowIndex] = rowValues;
                }
                rowValues[columnIndex] = value;
                maxColumn = Math.Max(maxColumn, columnIndex);
            }
        }

        if (matrix.Count == 0)
        {
            return null;
        }

        var html = new StringBuilder("<table>");
        var text = new StringBuilder();
        foreach (var row in matrix)
        {
            html.Append("<tr>");
            for (var column = 0; column <= maxColumn; column++)
            {
                var value = row.Value.GetValueOrDefault(column, "");
                html.Append("<td>").Append(TextDecoding.HtmlEscape(value)).Append("</td>");
                if (value.Length > 0)
                {
                    text.Append(value).Append('\t');
                }
            }
            html.Append("</tr>");
            text.AppendLine();
        }
        html.Append("</table>");
        plainText = text.ToString().TrimEnd();
        return html.ToString();
    }

    private static string ReadCellValue(XElement cell, List<string> shared)
    {
        var type = cell.Attribute("t")?.Value;
        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(S + "t").Select(t => t.Value));
        }

        var value = cell.Element(S + "v")?.Value ?? "";
        if (type == "s" && int.TryParse(value, out var index)
            && index >= 0 && index < shared.Count)
        {
            return shared[index];
        }

        if (type == "b")
        {
            return value == "1" ? "是" : "否";
        }

        return value;
    }

    private static int ColumnIndex(string reference)
    {
        var index = 0;
        foreach (var ch in reference)
        {
            if (ch < 'A' || ch > 'Z')
            {
                break;
            }
            index = index * 26 + (ch - 'A' + 1);
        }
        return Math.Max(0, index - 1);
    }

    private static string ResolvePath(string baseFolder, string target)
    {
        var parts = new List<string>();
        foreach (var piece in (baseFolder + "/" + target.Replace('\\', '/')).Split('/'))
        {
            if (piece is "" or ".")
            {
                continue;
            }
            if (piece == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
                continue;
            }
            parts.Add(piece);
        }
        return string.Join("/", parts);
    }
}
