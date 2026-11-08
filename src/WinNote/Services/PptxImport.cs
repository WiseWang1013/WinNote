using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace WinNote.Services;

public static class PptxImport
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static ImportResult FromFile(string path, MediaStore media, string? displayName = null)
    {
        using var archive = ZipFile.OpenRead(path);
        var sourceName = displayName ?? Path.GetFileName(path);
        var title = Path.GetFileNameWithoutExtension(sourceName);
        var slidePaths = ReadSlidePaths(archive);
        var html = new StringBuilder();
        var text = new StringBuilder();
        var images = new List<string>();

        var index = 0;
        foreach (var slidePath in slidePaths.Take(60))
        {
            index++;
            var entry = archive.GetEntry(slidePath);
            if (entry is null)
            {
                continue;
            }

            XDocument slide;
            using (var stream = entry.Open())
            {
                slide = XDocument.Load(stream);
            }

            var paragraphs = slide.Descendants(A + "p")
                .Select(paragraph => string.Concat(paragraph.Descendants(A + "t").Select(t => t.Value)).Trim())
                .Where(value => value.Length > 0)
                .ToList();

            if (paragraphs.Count > 0)
            {
                html.Append("<div class=\"md-h2\">第 ").Append(index).Append(" 页</div>");
                foreach (var paragraph in paragraphs)
                {
                    html.Append("<div>").Append(TextDecoding.HtmlEscape(paragraph)).Append("</div>");
                    text.AppendLine(paragraph);
                }
            }

            var rels = ReadRelationships(archive, slidePath);
            var imageCount = 0;
            foreach (var blip in slide.Descendants(A + "blip"))
            {
                var embed = blip.Attribute(R + "embed")?.Value;
                if (embed is null || !rels.TryGetValue(embed, out var target))
                {
                    continue;
                }

                var imagePath = ResolvePath(Path.GetDirectoryName(slidePath)!.Replace('\\', '/'), target);
                var imageEntry = archive.GetEntry(imagePath);
                if (imageEntry is null)
                {
                    continue;
                }

                var extension = Path.GetExtension(imagePath);
                if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff"))
                {
                    continue;
                }

                using var stream = imageEntry.Open();
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
                imageCount++;
                if (imageCount >= 4 || images.Count >= 50)
                {
                    break;
                }
            }

            var notes = ReadNotes(archive, slidePath);
            if (notes.Count > 0)
            {
                html.Append("<div class=\"pdf-page\">演讲者备注</div>");
                foreach (var note in notes)
                {
                    html.Append("<div>").Append(TextDecoding.HtmlEscape(note)).Append("</div>");
                }
            }
        }

        if (index == 0)
        {
            throw new InvalidOperationException("这个 PowerPoint 文件里没有可读取的幻灯片。");
        }

        return new ImportResult
        {
            Title = string.IsNullOrWhiteSpace(title) ? "导入的演示文稿" : title,
            Html = html.ToString(),
            Text = text.ToString().Trim(),
            SourceName = sourceName,
            Images = images,
        };
    }

    public static ImportResult FromFileTemp(string fileName, byte[] bytes, MediaStore media)
    {
        var path = Path.Combine(Path.GetTempPath(), "WinNote_pptx_" + Guid.NewGuid().ToString("N") + ".pptx");
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

    private static List<string> ReadSlidePaths(ZipArchive archive)
    {
        var paths = new List<string>();
        var presentationEntry = archive.GetEntry("ppt/presentation.xml");
        var relEntry = archive.GetEntry("ppt/_rels/presentation.xml.rels");
        if (presentationEntry is not null && relEntry is not null)
        {
            XDocument presentation;
            XDocument relationships;
            using (var stream = presentationEntry.Open())
            {
                presentation = XDocument.Load(stream);
            }
            using (var stream = relEntry.Open())
            {
                relationships = XDocument.Load(stream);
            }

            var rels = relationships.Root?.Elements(Rel + "Relationship")
                .Where(r => r.Attribute("Id") is not null && r.Attribute("Target") is not null)
                .ToDictionary(r => r.Attribute("Id")!.Value, r => r.Attribute("Target")!.Value)
                ?? new Dictionary<string, string>();

            foreach (var slideId in presentation.Descendants(P + "sldId"))
            {
                var rid = slideId.Attribute(R + "id")?.Value;
                if (rid is not null && rels.TryGetValue(rid, out var target))
                {
                    paths.Add(ResolvePath("ppt", target));
                }
            }
        }

        if (paths.Count == 0)
        {
            paths.AddRange(archive.Entries
                .Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                            && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(e => e.FullName));
        }

        return paths;
    }

    private static Dictionary<string, string> ReadRelationships(ZipArchive archive, string slidePath)
    {
        var directory = Path.GetDirectoryName(slidePath)!.Replace('\\', '/');
        var file = Path.GetFileName(slidePath);
        var relPath = $"{directory}/_rels/{file}.rels";
        var entry = archive.GetEntry(relPath);
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

    private static List<string> ReadNotes(ZipArchive archive, string slidePath)
    {
        var slideFile = Path.GetFileName(slidePath);
        var notePath = $"ppt/notesSlides/notesSlide{new string(slideFile.Where(char.IsDigit).ToArray())}.xml";
        var entry = archive.GetEntry(notePath);
        if (entry is null)
        {
            return new List<string>();
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document.Descendants(A + "p")
            .Select(p => string.Concat(p.Descendants(A + "t").Select(t => t.Value)).Trim())
            .Where(value => value.Length > 0)
            .ToList();
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
