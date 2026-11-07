using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WinNote.Services;

public static class ExportService
{
    private static readonly Regex MediaUrl = new(
        @"https://media\.local/([A-Za-z0-9._-]+)",
        RegexOptions.Compiled);

    public static void ExportText(string path, string title, string text)
    {
        var content = (title + "\r\n\r\n" + text).TrimStart();
        File.WriteAllText(path, content, new UTF8Encoding(true));
    }

    public static void ExportMarkdown(string path, string markdown, string imagesDir)
    {
        var directory = Path.GetDirectoryName(path) ?? ".";
        var baseName = SanitizeFileName(Path.GetFileNameWithoutExtension(path));
        var assetsName = baseName + "_assets";
        var assetsDir = Path.Combine(directory, assetsName);
        var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        markdown = MediaUrl.Replace(markdown, match =>
        {
            var file = match.Groups[1].Value;
            var source = Path.Combine(imagesDir, file);
            if (!File.Exists(source))
            {
                return match.Value;
            }

            Directory.CreateDirectory(assetsDir);
            var target = Path.Combine(assetsDir, file);
            if (copied.Add(file))
            {
                File.Copy(source, target, true);
            }

            return "./" + Uri.EscapeDataString(assetsName) + "/" + Uri.EscapeDataString(file);
        });

        File.WriteAllText(path, markdown, new UTF8Encoding(true));
    }

    public static void ExportHtml(string path, string title, string bodyHtml, string imagesDir)
    {
        bodyHtml = InlineImages(bodyHtml, imagesDir);
        var html = """
                   <!DOCTYPE html>
                   <html lang="zh-CN">
                   <head>
                   <meta charset="utf-8">
                   <meta name="viewport" content="width=device-width, initial-scale=1">
                   <title>__TITLE__</title>
                   <style>
                     :root { color-scheme: light; }
                     body { margin: 0; background: #f5f6f8; color: #1b1b1f;
                            font-family: "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", sans-serif; }
                     .note { max-width: 780px; margin: 48px auto; padding: 48px 56px;
                             background: #fff; border-radius: 18px;
                             box-shadow: 0 10px 40px rgba(20, 24, 40, .08); }
                     h1 { font-size: 30px; margin: 0 0 8px; }
                     .meta { color: #8a8c92; font-size: 12px; margin-bottom: 28px; }
                     .content { font-size: 15px; line-height: 1.9; word-break: break-word; }
                     .content img { max-width: 100%; height: auto; border-radius: 12px; }
                     .content figure { margin: 16px 0; }
                     .content table { border-collapse: collapse; width: 100%; margin: 14px 0; }
                     .content td, .content th { border: 1px solid #e2e4e8; padding: 7px 9px; vertical-align: top; }
                     .content blockquote { margin: 12px 0; padding: 4px 0 4px 14px; border-left: 3px solid #d8dae0; color: #555; }
                     .content pre { background: #f4f5f7; padding: 12px 14px; border-radius: 10px; overflow: auto; }
                     .content code { background: #f1f2f4; padding: 1px 4px; border-radius: 4px; }
                     .content hr { border: 0; border-top: 1px solid #e6e8ec; margin: 22px 0; }
                     .todo-item { display: flex; gap: 9px; align-items: flex-start; }
                     .todo-box { width: 18px; height: 18px; margin-top: 5px; border: 1.6px solid #555;
                                 border-radius: 5px; position: relative; flex: none; }
                     .todo-item[data-checked="true"] .todo-box { background: #0a59f7; border-color: #0a59f7; }
                     .todo-item[data-checked="true"] .todo-box::after { content: ""; position: absolute;
                       left: 5px; top: 2px; width: 5px; height: 9px; border: solid #fff;
                       border-width: 0 2px 2px 0; transform: rotate(45deg); }
                     .todo-item[data-checked="true"] .todo-text { text-decoration: line-through; color: #999; }
                     .md-h1 { font-size: 22px; font-weight: 700; margin: 10px 0 4px; }
                     .md-h2 { font-size: 19px; font-weight: 700; margin: 8px 0 3px; }
                     .md-h3 { font-size: 16px; font-weight: 700; margin: 6px 0 2px; }
                     @media print {
                       body { background: #fff; }
                       .note { box-shadow: none; margin: 0; padding: 0; max-width: none; }
                     }
                   </style>
                   </head>
                   <body>
                   <article class="note">
                     <h1>__TITLE__</h1>
                     <div class="meta">导出自 WinNote 备忘录 · __DATE__</div>
                     <div class="content">__BODY__</div>
                   </article>
                   </body>
                   </html>
                   """;

        html = html
            .Replace("__TITLE__", TextDecoding.HtmlEscape(title))
            .Replace("__DATE__", DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
            .Replace("__BODY__", bodyHtml);

        File.WriteAllText(path, html, new UTF8Encoding(true));
    }

    private static string InlineImages(string bodyHtml, string imagesDir)
    {
        return MediaUrl.Replace(bodyHtml, match =>
        {
            var file = match.Groups[1].Value;
            var source = Path.Combine(imagesDir, file);
            if (!File.Exists(source))
            {
                return match.Value;
            }

            var extension = Path.GetExtension(source).ToLowerInvariant();
            var mime = extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                ".tif" or ".tiff" => "image/tiff",
                _ => "image/png",
            };

            try
            {
                return $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(source))}";
            }
            catch
            {
                return match.Value;
            }
        });
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(ch, '_');
        }
        return string.IsNullOrWhiteSpace(name) ? "备忘录" : name;
    }
}
