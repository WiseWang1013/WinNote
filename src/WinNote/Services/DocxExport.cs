using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace WinNote.Services;

public static class DocxExport
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace PIC = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private static readonly XNamespace CP = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace DC = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace DCTERMS = "http://purl.org/dc/terms/";
    private static readonly XNamespace XSI = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly XNamespace EP = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";

    public static void Export(string path, ExportDoc document, string imagesDir)
    {
        var body = new XElement(W + "body");
        var imageCounter = 0;
        var drawingId = 1;
        var media = new List<(string Id, string Target, string SourcePath)>();
        var nextRelId = 100;

        body.Add(Paragraph("Title", new[] { Run(new ExportRun { Text = document.Title, Bold = true }) }));

        foreach (var block in document.Blocks)
        {
            switch (block.Type)
            {
                case "h1":
                case "h2":
                case "h3":
                {
                    var level = block.Type switch { "h1" => 1, "h2" => 2, _ => 3 };
                    body.Add(Paragraph("Heading" + level, RunsOf(block)));
                    break;
                }

                case "todo":
                {
                    var prefix = block.Checked ? "☑ " : "☐ ";
                    var runs = new List<XElement> { Run(new ExportRun { Text = prefix }) };
                    runs.AddRange(RunsOf(block));
                    body.Add(Paragraph(null, runs));
                    break;
                }

                case "bullet":
                    body.Add(Paragraph(null, new[] { Run(new ExportRun { Text = "• " }) }.Concat(RunsOf(block))));
                    break;

                case "quote":
                    body.Add(QuoteParagraph(RunsOf(block)));
                    break;

                case "image":
                {
                    var image = TryAddImage(block.File, imagesDir, ref nextRelId, ref imageCounter, ref drawingId, media);
                    if (image is not null)
                    {
                        body.Add(image);
                    }
                    break;
                }

                default:
                    body.Add(Paragraph(null, RunsOf(block)));
                    break;
            }
        }

        body.Add(new XElement(W + "sectPr",
            new XElement(W + "pgSz", new XAttribute(W + "w", "11906"), new XAttribute(W + "h", "16838")),
            new XElement(W + "pgMar",
                new XAttribute(W + "top", "1440"),
                new XAttribute(W + "right", "1440"),
                new XAttribute(W + "bottom", "1440"),
                new XAttribute(W + "left", "1440"))));

        var documentXml = new XElement(W + "document",
            new XAttribute(XNamespace.Xmlns + "w", W),
            new XAttribute(XNamespace.Xmlns + "r", R),
            new XAttribute(XNamespace.Xmlns + "wp", WP),
            new XAttribute(XNamespace.Xmlns + "a", A),
            new XAttribute(XNamespace.Xmlns + "pic", PIC),
            body);

        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);

        WriteEntry(archive, "[Content_Types].xml", ContentTypes());
        WriteEntry(archive, "_rels/.rels", PackageRelationships());
        WriteEntry(archive, "docProps/core.xml", CoreProperties(document.Title));
        WriteEntry(archive, "docProps/app.xml", AppProperties());
        WriteEntry(archive, "word/styles.xml", Styles());
        WriteEntry(archive, "word/document.xml", new XDocument(documentXml));
        WriteEntry(archive, "word/_rels/document.xml.rels", DocumentRelationships(media));

        foreach (var item in media)
        {
            if (!File.Exists(item.SourcePath))
            {
                continue;
            }

            var entry = archive.CreateEntry("word/" + item.Target, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            using var source = File.OpenRead(item.SourcePath);
            source.CopyTo(entryStream);
        }
    }

    private static IEnumerable<XElement> RunsOf(ExportBlock block)
    {
        if (block.Runs.Count == 0)
        {
            if (!string.IsNullOrEmpty(block.Text))
            {
                yield return Run(new ExportRun { Text = block.Text });
            }
            yield break;
        }

        foreach (var run in block.Runs)
        {
            yield return Run(run);
        }
    }

    private static XElement Run(ExportRun run)
    {
        var element = new XElement(W + "r");
        var properties = new XElement(W + "rPr");
        if (run.Bold)
        {
            properties.Add(new XElement(W + "b"));
        }
        if (run.Italic)
        {
            properties.Add(new XElement(W + "i"));
        }
        if (run.Underline)
        {
            properties.Add(new XElement(W + "u", new XAttribute(W + "val", "single")));
        }
        if (properties.HasElements)
        {
            element.Add(properties);
        }

        var lines = (run.Text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                element.Add(new XElement(W + "br"));
            }
            if (lines[i].Length > 0)
            {
                element.Add(new XElement(W + "t",
                    new XAttribute(XNamespace.Xml + "space", "preserve"),
                    lines[i]));
            }
        }

        return element;
    }

    private static XElement Paragraph(string? style, IEnumerable<XElement> runs, string? prefix = null)
    {
        var paragraph = new XElement(W + "p");
        var properties = new XElement(W + "pPr");
        if (style is not null)
        {
            properties.Add(new XElement(W + "pStyle", new XAttribute(W + "val", style)));
        }
        properties.Add(new XElement(W + "spacing",
            new XAttribute(W + "after", "120"),
            new XAttribute(W + "line", "300"),
            new XAttribute(W + "lineRule", "auto")));
        paragraph.Add(properties);

        if (prefix is not null)
        {
            paragraph.Add(Run(new ExportRun { Text = prefix }));
        }

        foreach (var run in runs)
        {
            paragraph.Add(run);
        }

        return paragraph;
    }

    private static XElement QuoteParagraph(IEnumerable<XElement> runs)
    {
        var paragraph = new XElement(W + "p",
            new XElement(W + "pPr",
                new XElement(W + "ind", new XAttribute(W + "left", "420")),
                new XElement(W + "spacing", new XAttribute(W + "after", "120"))),
            Run(new ExportRun { Text = "│ " }));
        foreach (var run in runs)
        {
            paragraph.Add(run);
        }
        return paragraph;
    }

    private static XElement? TryAddImage(
        string? fileName,
        string imagesDir,
        ref int nextRelId,
        ref int imageCounter,
        ref int drawingId,
        List<(string Id, string Target, string SourcePath)> media)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var path = Path.Combine(imagesDir, Path.GetFileName(fileName));
        if (!File.Exists(path))
        {
            return null;
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tif" or ".tiff"))
        {
            return null;
        }

        double pixelWidth;
        double pixelHeight;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            pixelWidth = bitmap.PixelWidth;
            pixelHeight = bitmap.PixelHeight;
        }
        catch
        {
            return null;
        }

        const double maxWidthInches = 6.0;
        const double maxHeightInches = 7.5;
        var widthInches = Math.Min(pixelWidth / 96.0, maxWidthInches);
        var heightInches = widthInches * pixelHeight / pixelWidth;
        if (heightInches > maxHeightInches)
        {
            heightInches = maxHeightInches;
            widthInches = heightInches * pixelWidth / pixelHeight;
        }

        var cx = (long)(widthInches * 914400);
        var cy = (long)(heightInches * 914400);

        imageCounter++;
        nextRelId++;
        var relId = "rId" + nextRelId;
        var target = $"media/image{imageCounter}{extension}";
        media.Add((relId, target, path));

        var name = $"Picture {imageCounter}";
        var inline = new XElement(WP + "inline",
            new XAttribute("distT", "0"),
            new XAttribute("distB", "0"),
            new XAttribute("distL", "0"),
            new XAttribute("distR", "0"),
            new XElement(WP + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
            new XElement(WP + "docPr", new XAttribute("id", drawingId++), new XAttribute("name", name)),
            new XElement(A + "graphic",
                new XElement(A + "graphicData",
                    new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/picture"),
                    new XElement(PIC + "pic",
                        new XElement(PIC + "nvPicPr",
                            new XElement(PIC + "cNvPr", new XAttribute("id", "0"), new XAttribute("name", name)),
                            new XElement(PIC + "cNvPicPr")),
                        new XElement(PIC + "blipFill",
                            new XElement(A + "blip", new XAttribute(R + "embed", relId)),
                            new XElement(A + "stretch", new XElement(A + "fillRect"))),
                        new XElement(PIC + "spPr",
                            new XElement(A + "xfrm",
                                new XElement(A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                            new XElement(A + "prstGeom",
                                new XAttribute("prst", "rect"),
                                new XElement(A + "avLst")))))));

        return new XElement(W + "p",
            new XElement(W + "pPr", new XElement(W + "jc", new XAttribute(W + "val", "center"))),
            new XElement(W + "r", new XElement(W + "drawing", inline)));
    }

    private static string ContentTypes()
    {
        return """
               <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
               <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                 <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                 <Default Extension="xml" ContentType="application/xml"/>
                 <Default Extension="png" ContentType="image/png"/>
                 <Default Extension="jpg" ContentType="image/jpeg"/>
                 <Default Extension="jpeg" ContentType="image/jpeg"/>
                 <Default Extension="gif" ContentType="image/gif"/>
                 <Default Extension="bmp" ContentType="image/bmp"/>
                 <Default Extension="tif" ContentType="image/tiff"/>
                 <Default Extension="tiff" ContentType="image/tiff"/>
                 <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                 <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
                 <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
                 <Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
               </Types>
               """;
    }

    private static string PackageRelationships()
    {
        return """
               <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
               <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                 <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                 <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
                 <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>
               </Relationships>
               """;
    }

    private static string DocumentRelationships(List<(string Id, string Target, string SourcePath)> media)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.AppendLine("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        builder.AppendLine("  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
        foreach (var item in media)
        {
            builder.AppendLine(
                $"  <Relationship Id=\"{item.Id}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"{item.Target}\"/>");
        }
        builder.AppendLine("</Relationships>");
        return builder.ToString();
    }

    private static string CoreProperties(string title)
    {
        var now = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        return $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                                   xmlns:dc="http://purl.org/dc/elements/1.1/"
                                   xmlns:dcterms="http://purl.org/dc/terms/"
                                   xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                  <dc:title>{TextDecoding.HtmlEscape(title)}</dc:title>
                  <dc:creator>WinNote</dc:creator>
                  <dcterms:created xsi:type="dcterms:W3CDTF">{now}</dcterms:created>
                  <dcterms:modified xsi:type="dcterms:W3CDTF">{now}</dcterms:modified>
                </cp:coreProperties>
                """;
    }

    private static string AppProperties()
    {
        return """
               <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
               <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"
                           xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
                 <Application>WinNote</Application>
               </Properties>
               """;
    }

    private static string Styles()
    {
        return """
               <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
               <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                 <w:docDefaults>
                   <w:rPrDefault>
                     <w:rPr>
                       <w:rFonts w:ascii="Segoe UI" w:hAnsi="Segoe UI" w:eastAsia="Microsoft YaHei"/>
                       <w:sz w:val="22"/>
                       <w:szCs w:val="22"/>
                     </w:rPr>
                   </w:rPrDefault>
                   <w:pPrDefault>
                     <w:pPr>
                       <w:spacing w:after="120" w:line="300" w:lineRule="auto"/>
                     </w:pPr>
                   </w:pPrDefault>
                 </w:docDefaults>
                 <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
                   <w:name w:val="Normal"/>
                   <w:qFormat/>
                 </w:style>
                 <w:style w:type="paragraph" w:styleId="Title">
                   <w:name w:val="Title"/>
                   <w:basedOn w:val="Normal"/>
                   <w:pPr><w:spacing w:after="240"/></w:pPr>
                   <w:rPr><w:b/><w:sz w:val="52"/></w:rPr>
                 </w:style>
                 <w:style w:type="paragraph" w:styleId="Heading1">
                   <w:name w:val="heading 1"/>
                   <w:basedOn w:val="Normal"/>
                   <w:pPr><w:spacing w:before="240" w:after="120"/></w:pPr>
                   <w:rPr><w:b/><w:sz w:val="36"/></w:rPr>
                 </w:style>
                 <w:style w:type="paragraph" w:styleId="Heading2">
                   <w:name w:val="heading 2"/>
                   <w:basedOn w:val="Normal"/>
                   <w:pPr><w:spacing w:before="200" w:after="100"/></w:pPr>
                   <w:rPr><w:b/><w:sz w:val="30"/></w:rPr>
                 </w:style>
                 <w:style w:type="paragraph" w:styleId="Heading3">
                   <w:name w:val="heading 3"/>
                   <w:basedOn w:val="Normal"/>
                   <w:pPr><w:spacing w:before="160" w:after="80"/></w:pPr>
                   <w:rPr><w:b/><w:sz w:val="26"/></w:rPr>
                 </w:style>
               </w:styles>
               """;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void WriteEntry(ZipArchive archive, string name, XDocument document)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        document.Save(stream, SaveOptions.DisableFormatting);
    }
}
