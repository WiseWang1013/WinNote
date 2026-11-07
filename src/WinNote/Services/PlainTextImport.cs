using System.Text;

namespace WinNote.Services;

public static class PlainTextImport
{
    public static ImportResult FromPlainText(string text, string title, string sourceName)
    {
        text = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        var html = new StringBuilder();
        var lines = text.Split('\n');

        var start = 0;
        while (start < lines.Length && string.IsNullOrWhiteSpace(lines[start]))
        {
            start++;
        }

        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                if (html.Length > 0)
                {
                    html.Append("<div><br></div>");
                }
                continue;
            }

            html.Append("<div>").Append(TextDecoding.HtmlEscape(line.TrimEnd())).Append("</div>");
        }

        return new ImportResult
        {
            Title = string.IsNullOrWhiteSpace(title) ? "导入的备忘录" : title.Trim(),
            Html = html.ToString(),
            Text = text.Trim(),
            SourceName = sourceName,
        };
    }
}
