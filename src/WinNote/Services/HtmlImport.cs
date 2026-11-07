using System.IO;
using System.Text.RegularExpressions;

namespace WinNote.Services;

public static class HtmlImport
{
    private static readonly Regex ImageTag = new(
        "(?is)(<img\\b[^>]*?\\bsrc\\s*=\\s*)([\"'])(.*?)\\2",
        RegexOptions.Compiled);

    public static ImportResult FromFile(string path, MediaStore media)
    {
        var html = TextDecoding.DecodeFile(path);
        return FromHtml(html, Path.GetDirectoryName(path) ?? "", media, Path.GetFileName(path));
    }

    public static ImportResult FromHtml(string html, string baseDirectory, MediaStore media, string sourceName)
    {
        var images = new List<string>();
        var rewritten = ImageTag.Replace(html, match =>
        {
            var prefix = match.Groups[1].Value;
            var quote = match.Groups[2].Value;
            var source = System.Net.WebUtility.HtmlDecode(match.Groups[3].Value).Trim();
            var resolved = ResolveImage(source, baseDirectory, media, images);
            return prefix + quote + resolved + quote;
        });

        var title = ExtractTitle(rewritten);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = Path.GetFileNameWithoutExtension(sourceName);
        }

        return new ImportResult
        {
            Title = title.Trim(),
            Html = rewritten,
            Text = TextDecoding.StripHtml(rewritten),
            SourceName = sourceName,
            NeedsSanitize = true,
            Images = images,
        };
    }

    private static string ResolveImage(string source, string baseDirectory, MediaStore media, List<string> images)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(source)
                || source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || source.StartsWith("//", StringComparison.Ordinal)
                || source.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
            {
                return source;
            }

            if (source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var file = media.SaveDataUrl(source);
                images.Add(file);
                return media.UrlFor(file);
            }

            string? localPath = null;
            if (source.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                localPath = new Uri(source).LocalPath;
            }
            else if (!source.Contains("://", StringComparison.Ordinal))
            {
                var relative = Uri.UnescapeDataString(source.Split('?', '#')[0]);
                localPath = Path.GetFullPath(Path.Combine(baseDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));
            }

            if (localPath is null || !File.Exists(localPath))
            {
                return source;
            }

            var extension = Path.GetExtension(localPath);
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff"))
            {
                return source;
            }

            var fileName = media.SaveBytes(File.ReadAllBytes(localPath), extension);
            images.Add(fileName);
            return media.UrlFor(fileName);
        }
        catch
        {
            return source;
        }
    }

    public static string ExtractTitle(string html)
    {
        var title = Regex.Match(html, @"(?is)<title[^>]*>(.*?)</title>");
        if (title.Success)
        {
            var value = TextDecoding.StripHtml(title.Groups[1].Value).Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }

        var heading = Regex.Match(html, @"(?is)<h1[^>]*>(.*?)</h1>");
        if (heading.Success)
        {
            var value = TextDecoding.StripHtml(heading.Groups[1].Value).Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }

        return "";
    }
}
