using System.Text;

namespace WinNote.Services;

public static class CsvImport
{
    public static ImportResult FromText(string text, string title, string sourceName)
    {
        text = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n').Where(line => line.Length > 0).Take(500).ToList();
        if (lines.Count == 0)
        {
            return PlainTextImport.FromPlainText("", title, sourceName);
        }

        var delimiter = DetectDelimiter(lines[0]);
        var rows = lines.Select(line => SplitLine(line, delimiter)).ToList();
        var columns = Math.Min(40, rows.Max(row => row.Count));
        var html = new StringBuilder();
        var plain = new StringBuilder();

        html.Append("<table>");
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            html.Append("<tr>");
            for (var column = 0; column < columns; column++)
            {
                var value = column < rows[rowIndex].Count ? rows[rowIndex][column] : "";
                var tag = rowIndex == 0 ? "th" : "td";
                html.Append('<').Append(tag).Append('>')
                    .Append(TextDecoding.HtmlEscape(value))
                    .Append("</").Append(tag).Append('>');
                if (value.Length > 0)
                {
                    plain.Append(value).Append('\t');
                }
            }
            html.Append("</tr>");
            plain.AppendLine();
        }
        html.Append("</table>");

        return new ImportResult
        {
            Title = string.IsNullOrWhiteSpace(title) ? "导入的表格" : title,
            Html = html.ToString(),
            Text = plain.ToString().Trim(),
            SourceName = sourceName,
        };
    }

    private static char DetectDelimiter(string line)
    {
        var candidates = new[] { ',', '\t', ';' };
        return candidates
            .OrderByDescending(candidate => line.Count(ch => ch == candidate))
            .First();
    }

    private static List<string> SplitLine(string line, char delimiter)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (ch == delimiter && !quoted)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        result.Add(current.ToString());
        return result;
    }
}
