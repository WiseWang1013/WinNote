using System.Text;
using System.Text.RegularExpressions;

namespace WinNote.Services;

public static class RtfImport
{
    public static ImportResult FromRtf(string rtf, string title, string sourceName)
    {
        var text = ExtractText(rtf);
        var result = PlainTextImport.FromPlainText(text, title, sourceName);
        result.Warning = null;
        return result;
    }

    private static string ExtractText(string rtf)
    {
        var output = new StringBuilder();
        var bytes = new List<byte>();
        var encoding = Encoding.GetEncoding("GB18030");

        var codepage = Regex.Match(rtf, @"\\ansicpg(\d+)");
        if (codepage.Success)
        {
            try
            {
                encoding = Encoding.GetEncoding(int.Parse(codepage.Groups[1].Value));
            }
            catch
            {
                encoding = Encoding.GetEncoding("GB18030");
            }
        }

        void FlushBytes()
        {
            if (bytes.Count == 0)
            {
                return;
            }

            output.Append(encoding.GetString(bytes.ToArray()));
            bytes.Clear();
        }

        for (var i = 0; i < rtf.Length; i++)
        {
            var ch = rtf[i];

            if (ch == '{' || ch == '}')
            {
                FlushBytes();
                continue;
            }

            if (ch == '\\')
            {
                FlushBytes();
                if (i + 1 >= rtf.Length)
                {
                    break;
                }

                var next = rtf[i + 1];
                if (next == '\\' || next == '{' || next == '}')
                {
                    output.Append(next);
                    i++;
                    continue;
                }

                if (next == '\'')
                {
                    if (i + 3 < rtf.Length
                        && Uri.IsHexDigit(rtf[i + 2])
                        && Uri.IsHexDigit(rtf[i + 3]))
                    {
                        bytes.Add(Convert.ToByte(rtf.Substring(i + 2, 2), 16));
                        i += 3;
                    }
                    continue;
                }

                if (next == '*')
                {
                    i++;
                    continue;
                }

                var j = i + 1;
                while (j < rtf.Length && char.IsLetter(rtf[j]))
                {
                    j++;
                }

                var word = rtf.Substring(i + 1, j - i - 1);
                var sign = 1;
                if (j < rtf.Length && rtf[j] == '-')
                {
                    sign = -1;
                    j++;
                }

                var numberStart = j;
                while (j < rtf.Length && char.IsDigit(rtf[j]))
                {
                    j++;
                }

                var hasNumber = j > numberStart;
                var number = hasNumber ? int.Parse(rtf.Substring(numberStart, j - numberStart)) * sign : 0;

                switch (word)
                {
                    case "par":
                    case "line":
                        output.Append('\n');
                        break;
                    case "tab":
                        output.Append('\t');
                        break;
                    case "u":
                        if (hasNumber)
                        {
                            var code = number < 0 ? number + 65536 : number;
                            output.Append((char)code);
                            if (j < rtf.Length && rtf[j] == '?')
                            {
                                j++;
                            }
                        }
                        break;
                    case "emdash":
                        output.Append('—');
                        break;
                    case "endash":
                        output.Append('–');
                        break;
                    default:
                        break;
                }

                i = j - 1;
                continue;
            }

            if (ch == '\r' || ch == '\n')
            {
                continue;
            }

            FlushBytes();
            output.Append(ch);
        }

        FlushBytes();
        return output.ToString();
    }
}
