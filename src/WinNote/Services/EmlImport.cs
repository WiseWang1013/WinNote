using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WinNote.Services;

public static class EmlImport
{
    private sealed class MimePart
    {
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public byte[] Body { get; set; } = Array.Empty<byte>();
        public List<MimePart> Children { get; } = new();
        public string ContentType { get; set; } = "text/plain";
        public string? Charset { get; set; }
        public string? Boundary { get; set; }
        public string TransferEncoding { get; set; } = "";
        public string? ContentId { get; set; }
        public string? FileName { get; set; }
        public bool IsAttachment { get; set; }
    }

    public static ImportResult FromFile(string path, MediaStore media)
    {
        var bytes = File.ReadAllBytes(path);
        return FromBytes(bytes, Path.GetFileName(path), media);
    }

    public static ImportResult FromBytes(byte[] bytes, string sourceName, MediaStore media)
    {
        var root = ParsePart(bytes, "text/plain");
        var htmlPart = FindPart(root, "text/html");
        var textPart = FindPart(root, "text/plain");

        var images = new List<string>();
        var cidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var attachments = new List<string>();

        foreach (var part in Enumerate(root))
        {
            if (part.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                var extension = MediaStore.GuessExtension(part.ContentType);
                var file = media.SaveBytes(part.Body, extension);
                images.Add(file);
                if (!string.IsNullOrWhiteSpace(part.ContentId))
                {
                    var key = part.ContentId.Trim().Trim('<', '>');
                    cidMap[key] = media.UrlFor(file);
                }
            }
            else if (part.IsAttachment && part.ContentType is not "text/html" and not "text/plain")
            {
                var name = part.FileName;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    attachments.Add(name);
                }
            }
        }

        var subject = DecodeHeader(root.Headers.GetValueOrDefault("Subject", ""));
        var from = DecodeHeader(root.Headers.GetValueOrDefault("From", ""));
        var to = DecodeHeader(root.Headers.GetValueOrDefault("To", ""));
        var date = DecodeHeader(root.Headers.GetValueOrDefault("Date", ""));

        if (string.IsNullOrWhiteSpace(subject))
        {
            subject = Path.GetFileNameWithoutExtension(sourceName);
        }

        string bodyHtml;
        string plainText;
        var needsSanitize = false;

        if (htmlPart is not null)
        {
            var html = DecodeText(htmlPart);
            foreach (var pair in cidMap)
            {
                html = Regex.Replace(
                    html,
                    "cid:" + Regex.Escape(pair.Key),
                    pair.Value,
                    RegexOptions.IgnoreCase);
                html = Regex.Replace(
                    html,
                    "cid:" + Regex.Escape("<" + pair.Key + ">"),
                    pair.Value,
                    RegexOptions.IgnoreCase);
            }

            bodyHtml = html;
            plainText = textPart is null ? TextDecoding.StripHtml(html) : DecodeText(textPart);
            needsSanitize = true;
        }
        else if (textPart is not null)
        {
            plainText = DecodeText(textPart);
            bodyHtml = PlainTextImport.FromPlainText(plainText, subject, sourceName).Html;
        }
        else
        {
            plainText = "";
            bodyHtml = "<div>（这封邮件没有可读的正文）</div>";
        }

        var header = new StringBuilder();
        header.Append("<div><b>主题：</b>").Append(TextDecoding.HtmlEscape(subject)).Append("</div>");
        if (!string.IsNullOrWhiteSpace(from))
        {
            header.Append("<div><b>发件人：</b>").Append(TextDecoding.HtmlEscape(from)).Append("</div>");
        }
        if (!string.IsNullOrWhiteSpace(to))
        {
            header.Append("<div><b>收件人：</b>").Append(TextDecoding.HtmlEscape(to)).Append("</div>");
        }
        if (!string.IsNullOrWhiteSpace(date))
        {
            header.Append("<div><b>时间：</b>").Append(TextDecoding.HtmlEscape(date)).Append("</div>");
        }
        header.Append("<div><br></div><hr><div><br></div>");

        var combined = header + bodyHtml;

        if (attachments.Count > 0)
        {
            combined += "<div><br></div><div><b>附件（" + attachments.Count + "）：</b>"
                        + TextDecoding.HtmlEscape(string.Join("、", attachments.Distinct()))
                        + "</div>";
        }

        var warning = attachments.Count > 0
            ? $"邮件包含 {attachments.Count} 个附件，附件名称已列在笔记末尾（未保存附件文件）。"
            : null;

        return new ImportResult
        {
            Title = subject,
            Html = combined,
            Text = string.IsNullOrWhiteSpace(plainText) ? TextDecoding.StripHtml(combined) : plainText,
            NeedsSanitize = needsSanitize,
            SourceName = sourceName,
            Images = images,
            Warning = warning,
        };
    }

    private static MimePart? FindPart(MimePart part, string contentType)
    {
        if (!part.IsAttachment
            && part.ContentType.Equals(contentType, StringComparison.OrdinalIgnoreCase)
            && part.Children.Count == 0)
        {
            return part;
        }

        foreach (var child in part.Children)
        {
            var found = FindPart(child, contentType);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static IEnumerable<MimePart> Enumerate(MimePart part)
    {
        yield return part;
        foreach (var child in part.Children)
        {
            foreach (var item in Enumerate(child))
            {
                yield return item;
            }
        }
    }

    private static MimePart ParsePart(byte[] data, string defaultContentType)
    {
        var part = new MimePart();
        var headerEnd = FindHeaderEnd(data);
        var headerBytes = headerEnd.index < 0 ? Array.Empty<byte>() : data[..headerEnd.index];
        part.Body = headerEnd.index < 0 ? data : data[(headerEnd.index + headerEnd.length)..];

        var headerText = Encoding.Latin1.GetString(headerBytes);
        var currentName = "";
        var currentValue = new StringBuilder();

        void CommitHeader()
        {
            if (currentName.Length > 0)
            {
                part.Headers[currentName] = currentValue.ToString().Trim();
            }
            currentName = "";
            currentValue.Clear();
        }

        foreach (var rawLine in headerText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            if ((line[0] == ' ' || line[0] == '\t') && currentName.Length > 0)
            {
                currentValue.Append(' ').Append(line.Trim());
                continue;
            }

            CommitHeader();
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            currentName = line[..colon].Trim();
            currentValue.Append(line[(colon + 1)..].Trim());
        }
        CommitHeader();

        var contentType = part.Headers.GetValueOrDefault("Content-Type", defaultContentType);
        var contentParams = ParseParameters(contentType);
        part.ContentType = (contentParams.GetValueOrDefault("", contentType) ?? contentType)
            .Split(';')[0]
            .Trim()
            .ToLowerInvariant();
        part.Charset = contentParams.GetValueOrDefault("charset");
        part.Boundary = contentParams.GetValueOrDefault("boundary");
        part.TransferEncoding = part.Headers.GetValueOrDefault("Content-Transfer-Encoding", "").Trim().ToLowerInvariant();
        part.ContentId = part.Headers.GetValueOrDefault("Content-ID", "").Trim();

        var disposition = part.Headers.GetValueOrDefault("Content-Disposition", "");
        var dispositionParams = ParseParameters(disposition);
        part.IsAttachment = disposition.TrimStart().StartsWith("attachment", StringComparison.OrdinalIgnoreCase);
        var fileName = dispositionParams.GetValueOrDefault("filename", "");
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = contentParams.GetValueOrDefault("name", "");
        }
        part.FileName = DecodeHeader(fileName ?? "");

        part.Body = DecodeTransfer(part.Body, part.TransferEncoding);

        if (part.ContentType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(part.Boundary))
        {
            var bodyText = Encoding.Latin1.GetString(part.Body);
            var marker = "--" + part.Boundary;
            var segments = bodyText.Split(marker);
            var segmentIndex = 0;
            foreach (var segment in segments)
            {
                segmentIndex++;
                if (segmentIndex == 1)
                {
                    continue; // 第一个分段是 MIME 前言
                }

                var value = segment;
                if (value.StartsWith("--", StringComparison.Ordinal))
                {
                    break;
                }

                value = value.TrimStart('\r', '\n');
                value = value.TrimEnd('\r', '\n');
                if (value.Length == 0)
                {
                    continue;
                }

                part.Children.Add(ParsePart(Encoding.Latin1.GetBytes(value), "text/plain"));
            }
        }

        return part;
    }

    private static (int index, int length) FindHeaderEnd(byte[] data)
    {
        for (var i = 0; i < data.Length - 3; i++)
        {
            if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n')
            {
                return (i, 4);
            }
            if (data[i] == '\n' && data[i + 1] == '\n')
            {
                return (i, 2);
            }
        }

        return (-1, 0);
    }

    private static Dictionary<string, string> ParseParameters(string headerValue)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pieces = SplitSemicolons(headerValue);
        if (pieces.Count == 0)
        {
            return result;
        }

        result[""] = pieces[0].Trim();
        foreach (var piece in pieces.Skip(1))
        {
            var equals = piece.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var key = piece[..equals].Trim().ToLowerInvariant();
            var value = piece[(equals + 1)..].Trim().Trim('"');
            result[key] = value;
        }

        return result;
    }

    private static List<string> SplitSemicolons(string value)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in value)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                current.Append(ch);
            }
            else if (ch == ';' && !inQuotes)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        parts.Add(current.ToString());
        return parts;
    }

    private static byte[] DecodeTransfer(byte[] body, string encoding)
    {
        if (encoding.Contains("base64", StringComparison.Ordinal))
        {
            var text = Encoding.Latin1.GetString(body);
            var cleaned = Regex.Replace(text, @"[^A-Za-z0-9+/=]", "");
            var padding = cleaned.Length % 4;
            if (padding != 0)
            {
                cleaned = cleaned.PadRight(cleaned.Length + (4 - padding), '=');
            }

            try
            {
                return Convert.FromBase64String(cleaned);
            }
            catch
            {
                return body;
            }
        }

        if (encoding.Contains("quoted-printable", StringComparison.Ordinal))
        {
            var text = Encoding.Latin1.GetString(body);
            text = text.Replace("=\r\n", "").Replace("=\n", "");
            var output = new List<byte>(text.Length);

            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '='
                    && i + 2 < text.Length
                    && Uri.IsHexDigit(text[i + 1])
                    && Uri.IsHexDigit(text[i + 2]))
                {
                    output.Add(Convert.ToByte(text.Substring(i + 1, 2), 16));
                    i += 2;
                }
                else
                {
                    output.Add((byte)text[i]);
                }
            }

            return output.ToArray();
        }

        return body;
    }

    private static string DecodeText(MimePart part)
    {
        return TextDecoding.Decode(part.Body, part.Charset);
    }

    private static string DecodeHeader(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        value = Regex.Replace(value, @"=\?([^?]+)\?([BbQq])\?([^?]*)\?=", match =>
        {
            var charset = match.Groups[1].Value;
            var encoding = match.Groups[2].Value.ToUpperInvariant();
            var payload = match.Groups[3].Value;

            try
            {
                if (encoding == "B")
                {
                    var bytes = Convert.FromBase64String(payload);
                    return TextDecoding.Decode(bytes, charset);
                }

                var text = payload.Replace('_', ' ');
                text = Regex.Replace(text, @"=([0-9A-Fa-f]{2})", m => ((char)Convert.ToByte(m.Groups[1].Value, 16)).ToString());
                return TextDecoding.Decode(Encoding.Latin1.GetBytes(text), charset);
            }
            catch
            {
                return match.Value;
            }
        });

        return Regex.Replace(value, @"\s+", " ").Trim();
    }
}
