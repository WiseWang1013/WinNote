using System.IO;
using System.Text;

namespace WinNote.Services;

public static class TextDecoding
{
    static TextDecoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static string DecodeFile(string path, string? charset = null)
    {
        return Decode(File.ReadAllBytes(path), charset);
    }

    public static string Decode(byte[] bytes, string? charset = null)
    {
        if (bytes.Length == 0)
        {
            return "";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset.Trim().Trim('"')).GetString(bytes);
            }
            catch
            {
                // 继续尝试其它编码
            }
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch
        {
            // 不是合法 UTF-8
        }

        try
        {
            return Encoding.GetEncoding("GB18030").GetString(bytes);
        }
        catch
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    public static bool LooksLikeHtml(string text)
    {
        var head = text.Length > 3000 ? text[..3000] : text;
        head = head.TrimStart().ToLowerInvariant();
        return head.StartsWith("<!doctype html")
               || head.StartsWith("<html")
               || head.Contains("<html")
               || head.StartsWith("<?xml")
               || head.Contains("<body")
               || head.Contains("mime-version:");
    }

    public static bool LooksLikeRtf(string text)
    {
        return text.TrimStart().StartsWith("{\\rtf", StringComparison.Ordinal);
    }

    public static bool LooksLikeZip(byte[] bytes)
    {
        return bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B;
    }

    public static bool LooksLikeOle(byte[] bytes)
    {
        return bytes.Length >= 8
               && bytes[0] == 0xD0 && bytes[1] == 0xCF
               && bytes[2] == 0x11 && bytes[3] == 0xE0
               && bytes[4] == 0xA1 && bytes[5] == 0xB1
               && bytes[6] == 0x1A && bytes[7] == 0xE1;
    }

    public static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return "";
        }

        var text = System.Text.RegularExpressions.Regex.Replace(
            html, @"(?is)<(script|style).*?</\1>", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?i)<br\s*/?>", "\n");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?i)</(div|p|h[1-6]|li|blockquote)>", "\n");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?i)</(td|th)>", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?i)</tr>", "\n");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"[ \t\u00A0]+", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    public static string HtmlEscape(string text)
    {
        return System.Net.WebUtility.HtmlEncode(text ?? "");
    }
}
