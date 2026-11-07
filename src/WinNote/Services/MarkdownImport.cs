using System.Text;
using System.Text.RegularExpressions;

namespace WinNote.Services;

public static class MarkdownImport
{
    public static ImportResult FromMarkdown(string markdown, string fallbackTitle, string sourceName)
    {
        markdown = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = markdown.Split('\n');
        var title = fallbackTitle;
        var start = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var heading = Regex.Match(lines[i], @"^\s*#\s+(.+?)\s*#*\s*$");
            if (heading.Success)
            {
                title = TextDecoding.StripHtml(Inline(heading.Groups[1].Value)).Trim();
                start = i + 1;
            }

            break;
        }

        var html = new StringBuilder();
        var index = start;

        while (index < lines.Length)
        {
            var line = lines[index];
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                index++;
                continue;
            }

            var fence = Regex.Match(trimmed, @"^```([\w+-]*)\s*$");
            if (fence.Success)
            {
                var language = fence.Groups[1].Value;
                var code = new StringBuilder();
                index++;
                while (index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    code.AppendLine(lines[index]);
                    index++;
                }
                index++;

                html.Append("<pre><code");
                if (language.Length > 0)
                {
                    html.Append(" data-lang=\"").Append(TextDecoding.HtmlEscape(language)).Append('"');
                }
                html.Append('>').Append(TextDecoding.HtmlEscape(code.ToString().TrimEnd())).Append("</code></pre>");
                continue;
            }

            var headingMatch = Regex.Match(trimmed, @"^(#{1,6})\s+(.*?)\s*#*\s*$");
            if (headingMatch.Success)
            {
                var level = headingMatch.Groups[1].Value.Length;
                html.Append($"<div class=\"md-h{level}\">")
                    .Append(Inline(headingMatch.Groups[2].Value))
                    .Append("</div>");
                index++;
                continue;
            }

            if (trimmed is "---" or "***" or "___")
            {
                html.Append("<hr>");
                index++;
                continue;
            }

            if (trimmed.StartsWith(">", StringComparison.Ordinal))
            {
                var quote = new StringBuilder();
                while (index < lines.Length && lines[index].TrimStart().StartsWith(">", StringComparison.Ordinal))
                {
                    quote.AppendLine(Regex.Replace(lines[index], @"^\s*>\s?", ""));
                    index++;
                }
                html.Append("<blockquote>").Append(Inline(quote.ToString().TrimEnd())).Append("</blockquote>");
                continue;
            }

            if (Regex.IsMatch(line, @"^\s*(?:[-*+])\s+"))
            {
                var items = new List<(bool IsTask, string Html)>();
                while (index < lines.Length && Regex.IsMatch(lines[index], @"^\s*(?:[-*+])\s+"))
                {
                    var item = Regex.Replace(lines[index], @"^\s*(?:[-*+])\s+", "");
                    index++;

                    var task = Regex.Match(item, @"^\[([ xX])\]\s*(.*)$");
                    if (task.Success)
                    {
                        var isChecked = task.Groups[1].Value is "x" or "X";
                        var taskText = Inline(task.Groups[2].Value);
                        items.Add((true,
                            "<div class=\"todo-item\" data-checked=\"" +
                            (isChecked ? "true" : "false") +
                            "\"><span class=\"todo-box\" contenteditable=\"false\"></span><span class=\"todo-text\">" +
                            taskText +
                            "</span></div>"));
                    }
                    else
                    {
                        items.Add((false, "<li>" + Inline(item) + "</li>"));
                    }
                }

                var listOutput = new StringBuilder();
                var inList = false;
                foreach (var item in items)
                {
                    if (item.IsTask)
                    {
                        if (inList)
                        {
                            listOutput.Append("</ul>");
                            inList = false;
                        }
                        listOutput.Append(item.Html);
                    }
                    else
                    {
                        if (!inList)
                        {
                            listOutput.Append("<ul>");
                            inList = true;
                        }
                        listOutput.Append(item.Html);
                    }
                }
                if (inList)
                {
                    listOutput.Append("</ul>");
                }

                html.Append(listOutput);
                continue;
            }

            if (Regex.IsMatch(line, @"^\s*\d+[.)]\s+"))
            {
                var items = new StringBuilder();
                while (index < lines.Length && Regex.IsMatch(lines[index], @"^\s*\d+[.)]\s+"))
                {
                    var item = Regex.Replace(lines[index], @"^\s*\d+[.)]\s+", "");
                    items.Append("<li>").Append(Inline(item)).Append("</li>");
                    index++;
                }
                html.Append("<ol>").Append(items).Append("</ol>");
                continue;
            }

            var paragraph = new StringBuilder();
            while (index < lines.Length)
            {
                var current = lines[index];
                var currentTrimmed = current.Trim();
                if (currentTrimmed.Length == 0
                    || Regex.IsMatch(currentTrimmed, @"^(#{1,6})\s+")
                    || currentTrimmed.StartsWith("```", StringComparison.Ordinal)
                    || currentTrimmed.StartsWith(">", StringComparison.Ordinal)
                    || Regex.IsMatch(current, @"^\s*(?:[-*+])\s+")
                    || Regex.IsMatch(current, @"^\s*\d+[.)]\s+")
                    || currentTrimmed is "---" or "***" or "___")
                {
                    break;
                }

                if (paragraph.Length > 0)
                {
                    paragraph.Append("<br>");
                }
                paragraph.Append(Inline(currentTrimmed));
                index++;
            }
            html.Append("<div>").Append(paragraph).Append("</div>");
        }

        var resultHtml = html.ToString();
        return new ImportResult
        {
            Title = string.IsNullOrWhiteSpace(title) ? "导入的 Markdown" : title,
            Html = resultHtml,
            Text = TextDecoding.StripHtml(resultHtml),
            SourceName = sourceName,
        };
    }

    private static string Inline(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var value = TextDecoding.HtmlEscape(text);

        value = Regex.Replace(value, @"!\[([^\]]*)\]\(([^)\s]+)(?:\s+""[^""]*"")?\)", match =>
        {
            var src = System.Net.WebUtility.HtmlDecode(match.Groups[2].Value);
            if (src.StartsWith("https://media.local/", StringComparison.OrdinalIgnoreCase)
                || src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
                || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || src.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return $"<img src=\"{TextDecoding.HtmlEscape(src)}\" alt=\"{match.Groups[1].Value}\">";
            }
            return match.Groups[1].Value;
        });

        value = Regex.Replace(value, @"\[([^\]]+)\]\(([^)\s]+)(?:\s+""[^""]*"")?\)", match =>
        {
            var href = System.Net.WebUtility.HtmlDecode(match.Groups[2].Value);
            if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                return $"<a href=\"{TextDecoding.HtmlEscape(href)}\">{match.Groups[1].Value}</a>";
            }
            return match.Groups[1].Value;
        });

        value = Regex.Replace(value, @"`([^`]+)`", "<code>$1</code>");
        value = Regex.Replace(value, @"\*\*(.+?)\*\*", "<b>$1</b>");
        value = Regex.Replace(value, @"__(.+?)__", "<b>$1</b>");
        value = Regex.Replace(value, @"~~(.+?)~~", "<s>$1</s>");
        value = Regex.Replace(value, @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", "<i>$1</i>");
        value = Regex.Replace(value, @"(?<!_)_(?!_)(.+?)(?<!_)_(?!_)", "<i>$1</i>");
        return value;
    }
}
