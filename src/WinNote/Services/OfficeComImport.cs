using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using WinNote.Models;

namespace WinNote.Services;

public static class OfficeComImport
{
    public static ImportResult FromExcel(string path, MediaStore media, string displayName)
    {
        var type = Type.GetTypeFromProgID("Excel.Application")
                   ?? throw new InvalidOperationException(
                       "这台电脑没有安装 Excel，无法读取旧版 .xls / WPS 表格。请另存为 .xlsx 或 .csv。");

        var tempDir = CreateTempDir("excel");
        var target = Path.Combine(tempDir, "book.xlsx");
        object? app = null;
        object? workbook = null;
        try
        {
            app = Activator.CreateInstance(type)!;
            dynamic excel = app;
            excel.Visible = false;
            excel.DisplayAlerts = false;
            excel.ScreenUpdating = false;

            workbook = excel.Workbooks.Open(path, 0, true);
            dynamic book = workbook;
            book.SaveAs(target, 51); // xlOpenXMLWorkbook
            book.Close(false);
            workbook = null;
            excel.Quit();
            app = null;

            return XlsxImport.FromFile(target, media, displayName);
        }
        finally
        {
            SafeClose(workbook);
            SafeQuit(app);
            TryDeleteDir(tempDir);
        }
    }

    public static ImportResult FromPowerPoint(string path, MediaStore media, string displayName)
    {
        var type = Type.GetTypeFromProgID("PowerPoint.Application")
                   ?? throw new InvalidOperationException(
                       "这台电脑没有安装 PowerPoint，无法读取旧版 .ppt / WPS 演示。请另存为 .pptx。");

        var tempDir = CreateTempDir("ppt");
        var target = Path.Combine(tempDir, "slides.pptx");
        object? app = null;
        object? presentation = null;
        try
        {
            app = Activator.CreateInstance(type)!;
            dynamic powerPoint = app;
            // ReadOnly = -1, Untitled = 0, WithWindow = 0
            presentation = powerPoint.Presentations.Open(path, -1, 0, 0);
            dynamic show = presentation;
            show.SaveAs(target, 24); // ppSaveAsOpenXMLPresentation
            show.Close();
            presentation = null;
            powerPoint.Quit();
            app = null;

            return PptxImport.FromFile(target, media, displayName);
        }
        finally
        {
            SafeClose(presentation);
            SafeQuit(app);
            TryDeleteDir(tempDir);
        }
    }

    public static ImportResult FromWord(string path, MediaStore media, string displayName)
    {
        var type = Type.GetTypeFromProgID("Word.Application")
                   ?? throw new InvalidOperationException(
                       "这台电脑没有安装 Word，无法读取这个文档。请另存为 .docx 或 .txt。");

        var tempDir = CreateTempDir("word");
        var target = Path.Combine(tempDir, "document.docx");
        object? app = null;
        object? document = null;
        try
        {
            app = Activator.CreateInstance(type)!;
            dynamic word = app;
            word.Visible = false;
            word.DisplayAlerts = 0;

            document = word.Documents.Open(path, false, true);
            dynamic doc = document;
            try
            {
                doc.SaveAs2(target, 16); // wdFormatDocumentDefault
            }
            catch
            {
                doc.SaveAs(target, 12); // wdFormatXMLDocument
            }

            doc.Close(0);
            document = null;
            word.Quit(0);
            app = null;

            return DocxImport.FromFile(target, media, displayName);
        }
        finally
        {
            SafeClose(document);
            SafeQuit(app);
            TryDeleteDir(tempDir);
        }
    }

    public static async Task<ImportResult> FromOneNoteAsync(string path, MediaStore media, string displayName)
    {
        var type = Type.GetTypeFromProgID("OneNote.Application")
                   ?? throw new InvalidOperationException(
                       "这台电脑没有安装 OneNote，无法直接读取 .one 分区文件。\n" +
                       "请在 OneNote 里把这一页导出为 PDF 或 Word 后再导入。");

        object? app = null;
        try
        {
            app = Activator.CreateInstance(type)!;
            dynamic oneNote = app;

            string sectionXml;
            try
            {
                oneNote.GetHierarchy("", OneNoteEnum("HierarchyScope", 3), out sectionXml);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("读取 OneNote 分区列表失败：" + ex.Message, ex);
            }

            var sectionDocument = XDocument.Parse(sectionXml);
            XNamespace ns = "http://schemas.microsoft.com/office/onenote/2013/onenote";
            var fullPath = Path.GetFullPath(path);
            var section = sectionDocument.Descendants(ns + "Section")
                .FirstOrDefault(element =>
                    string.Equals(element.Attribute("path")?.Value, fullPath, StringComparison.OrdinalIgnoreCase))
                ?? sectionDocument.Descendants(ns + "Section")
                    .FirstOrDefault(element =>
                        string.Equals(Path.GetFileName(element.Attribute("path")?.Value ?? ""),
                            Path.GetFileName(fullPath), StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    "OneNote 里没有找到这个分区。请先在 OneNote 中打开它，或导出为 PDF/Word 再导入。");

            var sectionId = section.Attribute("ID")?.Value
                            ?? throw new InvalidOperationException("OneNote 分区没有 ID。");
            var sectionName = section.Attribute("name")?.Value ?? Path.GetFileNameWithoutExtension(displayName);

            string pagesXml;
            try
            {
                oneNote.GetHierarchy(sectionId, OneNoteEnum("HierarchyScope", 4), out pagesXml);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("读取 OneNote 页面列表失败：" + ex.Message, ex);
            }

            var pagesDocument = XDocument.Parse(pagesXml);
            var pages = pagesDocument.Descendants(ns + "Page").Take(120).ToList();
            if (pages.Count == 0)
            {
                throw new InvalidOperationException("这个 OneNote 分区里没有页面。");
            }

            var html = new StringBuilder();
            var text = new StringBuilder();
            var pageIndex = 0;
            foreach (var page in pages)
            {
                pageIndex++;
                var pageId = page.Attribute("ID")?.Value;
                if (string.IsNullOrEmpty(pageId))
                {
                    continue;
                }

                string pageXml;
                try
                {
                    oneNote.GetPageContent(pageId, out pageXml);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("读取 OneNote 页面失败：" + ex.Message, ex);
                }

                var pageDocument = XDocument.Parse(pageXml);
                var pageName = pageDocument.Root?.Attribute("name")?.Value ?? $"第 {pageIndex} 页";
                var lines = pageDocument.Descendants(ns + "T")
                    .Select(node => node.Value.Trim())
                    .Where(value => value.Length > 0)
                    .ToList();

                if (lines.Count == 0)
                {
                    continue;
                }

                html.Append("<div class=\"md-h2\">")
                    .Append(TextDecoding.HtmlEscape(pageName))
                    .Append("</div>");
                foreach (var line in lines)
                {
                    html.Append("<div>").Append(TextDecoding.HtmlEscape(line)).Append("</div>");
                    text.AppendLine(line);
                }
                text.AppendLine();
            }

            if (html.Length == 0)
            {
                throw new InvalidOperationException("OneNote 页面里没有可读取的文字。");
            }

            return await Task.FromResult(new ImportResult
            {
                Title = sectionName,
                Html = html.ToString(),
                Text = text.ToString().Trim(),
                SourceName = displayName,
                Warning = "已直接读取 OneNote 文字；图片和手写笔迹暂不导入，可在 OneNote 里导出 PDF 保留版式。",
            });
        }
        finally
        {
            SafeQuit(app);
        }
    }

    public static async Task<ImportResult> FromOneNoteViaPowerShellAsync(string path, MediaStore media, string displayName)
    {
        var directory = CreateTempDir("onenote_ps");
        var scriptPath = Path.Combine(directory, "export.ps1");
        var outputPath = Path.Combine(directory, "pages.json");
        try
        {
            await File.WriteAllTextAsync(scriptPath, OneNoteScript, new UTF8Encoding(true));
            var startInfo = new System.Diagnostics.ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add("-OnePath");
            startInfo.ArgumentList.Add(Path.GetFullPath(path));
            startInfo.ArgumentList.Add("-OutPath");
            startInfo.ArgumentList.Add(outputPath);

            using var process = System.Diagnostics.Process.Start(startInfo)
                               ?? throw new InvalidOperationException("无法启动 OneNote 导出程序。");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync();
            var completed = await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(90)));
            if (completed != exitTask)
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // ignored
                }
                throw new InvalidOperationException("OneNote 导出超时，请在 OneNote 里另存为 PDF/Word 后再导入。");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                throw new InvalidOperationException(
                    "读取 OneNote 失败：" + (string.IsNullOrWhiteSpace(stderr) ? stdout : stderr).Trim());
            }

            var json = await File.ReadAllTextAsync(outputPath, Encoding.UTF8);
            var export = JsonSerializer.Deserialize<OneNoteExport>(json, JsonDefaults.Options)
                         ?? throw new InvalidOperationException("OneNote 导出的内容为空。");
            var html = new StringBuilder();
            var text = new StringBuilder();
            foreach (var page in export.Pages ?? new List<OneNotePage>())
            {
                var lines = (page.Lines ?? new List<string>())
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0)
                    .ToList();
                if (lines.Count == 0)
                {
                    continue;
                }

                html.Append("<div class=\"md-h2\">")
                    .Append(TextDecoding.HtmlEscape(string.IsNullOrWhiteSpace(page.Name) ? "OneNote 页面" : page.Name))
                    .Append("</div>");
                foreach (var line in lines)
                {
                    html.Append("<div>").Append(TextDecoding.HtmlEscape(line)).Append("</div>");
                    text.AppendLine(line);
                }
                text.AppendLine();
            }

            if (html.Length == 0)
            {
                throw new InvalidOperationException("OneNote 页面里没有可读取的文字。");
            }

            return new ImportResult
            {
                Title = string.IsNullOrWhiteSpace(export.Title)
                    ? Path.GetFileNameWithoutExtension(displayName)
                    : export.Title,
                Html = html.ToString(),
                Text = text.ToString().Trim(),
                SourceName = displayName,
                Warning = "已通过本机 OneNote 读取文字；图片和手写笔迹暂不导入。",
            };
        }
        finally
        {
            TryDeleteDir(directory);
        }
    }

    private sealed class OneNoteExport
    {
        public string Title { get; set; } = "";
        public List<OneNotePage>? Pages { get; set; }
    }

    private sealed class OneNotePage
    {
        public string Name { get; set; } = "";
        public List<string>? Lines { get; set; }
    }

    private const string OneNoteScript = """
param([string]$OnePath,[string]$OutPath)
$ErrorActionPreference = 'Stop'
$app = New-Object -ComObject OneNote.Application
$xml = ''
$app.GetHierarchy('', 3, [ref]$xml)
[xml]$doc = $xml
$sections = @($doc.Notebooks.Notebook.Section)
$section = $sections | Where-Object { $_.path -eq $OnePath } | Select-Object -First 1
if (-not $section) {
  $section = $sections | Where-Object { [IO.Path]::GetFileName($_.path) -eq [IO.Path]::GetFileName($OnePath) } | Select-Object -First 1
}
if (-not $section) { throw '找不到 OneNote 分区，请先在 OneNote 里打开它' }
$pagesXml = ''
$app.GetHierarchy($section.ID, 4, [ref]$pagesXml)
[xml]$pagesDoc = $pagesXml
$pages = @()
foreach ($page in @($pagesDoc.Section.Page)) {
  $content = ''
  $app.GetPageContent($page.ID, [ref]$content)
  $lines = @([regex]::Matches($content, '(?s)<one:T(?:\s[^>]*)?>(.*?)</one:T>') | ForEach-Object {
    $raw = $_.Groups[1].Value
    $raw = $raw -replace '(?s)^<!\[CDATA\[', '' -replace '(?s)\]\]>$', ''
    $raw = [System.Net.WebUtility]::HtmlDecode($raw)
    $raw = [regex]::Replace($raw, '<[^>]+>', ' ')
    $raw = [System.Net.WebUtility]::HtmlDecode($raw)
    ($raw -replace '\s+', ' ').Trim()
  } | Where-Object { $_.Length -gt 0 })
  $pages += [pscustomobject]@{ name = $page.name; lines = $lines }
}
@{ title = $section.name; pages = $pages } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutPath -Encoding UTF8
""";

    private static object OneNoteEnum(string name, int value)
    {
        try
        {
            var assembly = Assembly.Load("Microsoft.Office.Interop.OneNote");
            var type = assembly.GetType("Microsoft.Office.Interop.OneNote." + name);
            if (type is not null && type.IsEnum)
            {
                return Enum.ToObject(type, value);
            }
        }
        catch
        {
            // ignored
        }

        return value;
    }

    private static string CreateTempDir(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), $"WinNote_{name}_{Guid.NewGuid().ToString("N")}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void SafeClose(object? comObject)
    {
        if (comObject is null)
        {
            return;
        }

        try
        {
            dynamic value = comObject;
            value.Close();
        }
        catch
        {
            // ignored
        }
        finally
        {
            try
            {
                Marshal.FinalReleaseComObject(comObject);
            }
            catch
            {
                // ignored
            }
        }
    }

    private static void SafeQuit(object? comObject)
    {
        if (comObject is null)
        {
            return;
        }

        try
        {
            dynamic value = comObject;
            value.Quit();
        }
        catch
        {
            // ignored
        }
        finally
        {
            try
            {
                Marshal.FinalReleaseComObject(comObject);
            }
            catch
            {
                // ignored
            }
        }
    }

    private static void TryDeleteDir(string path)
    {
        try
        {
            Directory.Delete(path, true);
        }
        catch
        {
            // ignored
        }
    }
}
