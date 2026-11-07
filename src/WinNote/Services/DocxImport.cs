using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace WinNote.Services;

public static class DocxImport
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static ImportResult FromFile(string path, MediaStore media, string? displayName = null)
    {
        using var archive = ZipFile.OpenRead(path);
        var sourceName = displayName ?? Path.GetFileName(path);
        var documentEntry = archive.GetEntry("word/document.xml")
                            ?? throw new InvalidOperationException("这个 Word 文件里没有正文内容。");
        var relEntry = archive.GetEntry("word/_rels/document.xml.rels");
        var rels = ParseRelationships(relEntry);

        XDocument document;
        using (var stream = documentEntry.Open())
        {
            document = XDocument.Load(stream);
        }

        var images = new List<string>();
        var html = new StringBuilder();
        var plain = new StringBuilder();
        var body = document.Root?.Element(W + "body");

        if (body is null)
        {
            throw new InvalidOperationException("无法读取 Word 正文。");
        }

        foreach (var element in body.Elements())
        {
            if (element.Name == W + "p")
            {
                ProcessParagraph(element, html, plain, media, archive, rels, images);
            }
            else if (element.Name == W + "tbl")
            {
                ProcessTable(element, html, plain);
            }
        }

        var title = ReadCoreTitle(archive);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = Path.GetFileNameWithoutExtension(sourceName);
        }
        title = title.Trim();

        if (title.Length > 0)
        {
            var heading = "<div class=\"md-h1\">" + TextDecoding.HtmlEscape(title) + "</div>";
            var htmlText = html.ToString();
            if (htmlText.StartsWith(heading, StringComparison.OrdinalIgnoreCase))
            {
                html.Clear();
                html.Append(htmlText[heading.Length..]);
            }
        }

        return new ImportResult
        {
            Title = title,
            Html = html.ToString(),
            Text = plain.ToString().Trim(),
            SourceName = sourceName,
            Images = images,
        };
    }

    public static ImportResult FromFileTemp(string fileName, byte[] bytes, MediaStore media)
    {
        var path = Path.Combine(Path.GetTempPath(), "WinNote_docx_" + Guid.NewGuid().ToString("N") + ".docx");
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

    private static void ProcessParagraph(
        XElement paragraph,
        StringBuilder html,
        StringBuilder plain,
        MediaStore media,
        ZipArchive archive,
        Dictionary<string, string> rels,
        List<string> images)
    {
        var style = paragraph.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val")?.Value ?? "";
        var isBullet = paragraph.Element(W + "pPr")?.Element(W + "numPr") is not null
                       || style.Contains("ListBullet", StringComparison.OrdinalIgnoreCase)
                       || style.Contains("ListParagraph", StringComparison.OrdinalIgnoreCase)
                       || style.Contains("List Paragraph", StringComparison.OrdinalIgnoreCase);

        var runHtml = new StringBuilder();
        var runText = new StringBuilder();
        var imageInserted = false;

        foreach (var node in paragraph.Descendants())
        {
            if (node.Name == W + "r")
            {
                var rPr = node.Element(W + "rPr");
                var bold = rPr?.Element(W + "b") is not null && rPr.Element(W + "b")?.Attribute(W + "val")?.Value != "0";
                var italic = rPr?.Element(W + "i") is not null && rPr.Element(W + "i")?.Attribute(W + "val")?.Value != "0";
                var underline = rPr?.Element(W + "u") is not null && rPr.Element(W + "u")?.Attribute(W + "val")?.Value != "none";
                var strike = rPr?.Element(W + "strike") is not null;

                var text = new StringBuilder();
                foreach (var child in node.Elements())
                {
                    if (child.Name == W + "t")
                    {
                        text.Append(child.Value);
                    }
                    else if (child.Name == W + "br")
                    {
                        text.Append('\n');
                    }
                    else if (child.Name == W + "tab")
                    {
                        text.Append('\t');
                    }
                    else if (child.Name == W + "drawing" || child.Name == W + "pict")
                    {
                        var blip = child.Descendants(A + "blip").FirstOrDefault();
                        var embed = blip?.Attribute(R + "embed")?.Value;
                        if (embed is not null && rels.TryGetValue(embed, out var target))
                        {
                            var file = TrySaveZipImage(archive, target, media, images);
                            if (file is not null)
                            {
                                if (text.Length > 0)
                                {
                                    AppendRun(runHtml, runText, text.ToString(), bold, italic, underline, strike);
                                    text.Clear();
                                }
                                if (runHtml.Length > 0)
                                {
                                    html.Append("<div>").Append(runHtml).Append("</div>");
                                    runHtml.Clear();
                                }
                                html.Append("<figure class=\"img\" contenteditable=\"false\" data-file=\"")
                                    .Append(file)
                                    .Append("\"><img src=\"")
                                    .Append(media.UrlFor(file))
                                    .Append("\" data-file=\"")
                                    .Append(file)
                                    .Append("\"><button class=\"img-del\" type=\"button\"></button></figure>");
                                imageInserted = true;
                            }
                        }
                    }
                }

                if (text.Length > 0)
                {
                    AppendRun(runHtml, runText, text.ToString(), bold, italic, underline, strike);
                }
            }
            else if (node.Name == W + "hyperlink")
            {
                // 超链接文字由内部的 w:r 处理
            }
        }

        var textValue = runText.ToString().TrimEnd();
        if (textValue.Length > 0)
        {
            plain.AppendLine(textValue);
        }

        if (runHtml.Length == 0)
        {
            if (!imageInserted)
            {
                html.Append("<div><br></div>");
            }
            return;
        }

        var headingLevel = 0;
        if (style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
        {
            int.TryParse(style[7..], out headingLevel);
        }
        else if (style.StartsWith("标题", StringComparison.Ordinal))
        {
            int.TryParse(style[2..], out headingLevel);
        }
        else if (style.Equals("Title", StringComparison.OrdinalIgnoreCase))
        {
            headingLevel = 1;
        }

        if (headingLevel > 0)
        {
            html.Append($"<div class=\"md-h{Math.Min(6, headingLevel)}\">")
                .Append(runHtml)
                .Append("</div>");
        }
        else if (isBullet)
        {
            html.Append("<div class=\"doc-bullet\">• ").Append(runHtml).Append("</div>");
        }
        else
        {
            html.Append("<div>").Append(runHtml).Append("</div>");
        }
    }

    private static void AppendRun(
        StringBuilder html,
        StringBuilder plain,
        string text,
        bool bold,
        bool italic,
        bool underline,
        bool strike)
    {
        plain.Append(text);
        var encoded = TextDecoding.HtmlEscape(text).Replace("\n", "<br>").Replace("\t", "    ");
        if (strike)
        {
            encoded = "<s>" + encoded + "</s>";
        }
        if (underline)
        {
            encoded = "<u>" + encoded + "</u>";
        }
        if (italic)
        {
            encoded = "<i>" + encoded + "</i>";
        }
        if (bold)
        {
            encoded = "<b>" + encoded + "</b>";
        }
        html.Append(encoded);
    }

    private static void ProcessTable(XElement table, StringBuilder html, StringBuilder plain)
    {
        var rows = table.Elements(W + "tr").ToList();
        if (rows.Count == 0)
        {
            return;
        }

        html.Append("<table>");
        foreach (var row in rows)
        {
            html.Append("<tr>");
            foreach (var cell in row.Elements(W + "tc"))
            {
                var text = string.Join(" ", cell.Descendants(W + "t").Select(t => t.Value)).Trim();
                html.Append("<td>").Append(TextDecoding.HtmlEscape(text)).Append("</td>");
                if (text.Length > 0)
                {
                    plain.Append(text).Append(' ');
                }
            }
            html.Append("</tr>");
            plain.AppendLine();
        }
        html.Append("</table>");
    }

    private static string? TrySaveZipImage(
        ZipArchive archive,
        string target,
        MediaStore media,
        List<string> images)
    {
        try
        {
            var entryName = ResolveZipPath("word", target);
            var entry = archive.GetEntry(entryName);
            if (entry is null)
            {
                return null;
            }

            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var extension = Path.GetExtension(entryName);
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff"))
            {
                return null;
            }

            var file = media.SaveBytes(memory.ToArray(), extension);
            images.Add(file);
            return file;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveZipPath(string baseFolder, string target)
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

    private static Dictionary<string, string> ParseRelationships(ZipArchiveEntry? entry)
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

    private static string ReadCoreTitle(ZipArchive archive)
    {
        try
        {
            var entry = archive.GetEntry("docProps/core.xml");
            if (entry is null)
            {
                return "";
            }

            using var stream = entry.Open();
            var document = XDocument.Load(stream);
            XNamespace dc = "http://purl.org/dc/elements/1.1/";
            return document.Root?.Element(dc + "title")?.Value?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }
}
