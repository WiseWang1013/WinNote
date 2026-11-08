using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace WinNote.Services;

public sealed class ImportService
{
    private readonly MediaStore _media;

    public ImportService(string imagesDir)
    {
        _media = new MediaStore(imagesDir);
    }

    public async Task<ImportResult> ImportFileAsync(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("找不到要导入的文件。", path);
        }

        var bytes = await File.ReadAllBytesAsync(path);
        return await ImportBytesAsync(Path.GetFileName(path), bytes, Path.GetDirectoryName(path));
    }

    public async Task<ImportResult> ImportBytesAsync(string fileName, byte[] bytes, string? baseDirectory)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var title = Path.GetFileNameWithoutExtension(fileName);
        var text = SafeDecode(bytes);

        switch (extension)
        {
            case ".eml":
            case ".mht":
            case ".mhtml":
                return EmlImport.FromBytes(bytes, fileName, _media);

            case ".html":
            case ".htm":
            case ".xhtml":
                return HtmlImport.FromHtml(text, baseDirectory ?? "", _media, fileName);

            case ".md":
            case ".markdown":
            case ".mdown":
                return MarkdownImport.FromMarkdown(text, title, fileName);

            case ".rtf":
                return RtfImport.FromRtf(text, title, fileName);

            case ".docx":
                return DocxImport.FromFileTemp(fileName, bytes, _media);

            case ".xlsx":
            case ".xlsm":
            case ".xltx":
            case ".xltm":
                return XlsxImport.FromFileTemp(fileName, bytes, _media);

            case ".pptx":
            case ".pptm":
            case ".potx":
            case ".potm":
                return PptxImport.FromFileTemp(fileName, bytes, _media);

            case ".csv":
                return CsvImport.FromText(text, title, fileName);

            case ".xls":
            case ".et":
                return ImportOfficeTemp(fileName, bytes, (path, name) => OfficeComImport.FromExcel(path, _media, name));

            case ".ppt":
            case ".dps":
                return ImportOfficeTemp(fileName, bytes, (path, name) => OfficeComImport.FromPowerPoint(path, _media, name));

            case ".wps":
                return ImportOfficeTemp(fileName, bytes, (path, name) => OfficeComImport.FromWord(path, _media, name));

            case ".one":
                return await ImportOneNoteAsync(fileName, bytes);

            case ".doc":
                return await ImportDocAsync(fileName, bytes, text, baseDirectory);

            case ".pdf":
                return await ImportPdfTempAsync(fileName, bytes);

            case ".txt":
            case ".text":
                return PlainTextImport.FromPlainText(text, title, fileName);

            case ".png":
            case ".jpg":
            case ".jpeg":
            case ".bmp":
            case ".gif":
            case ".webp":
            case ".tif":
            case ".tiff":
                return ImportImage(fileName, bytes, extension);

            case ".hdoc":
                return ImportHdoc(fileName, bytes);

            case ".msg":
                throw new InvalidOperationException(
                    "暂时不支持 Outlook 的二进制 .msg 邮件格式。\n" +
                    "请在邮件客户端里把邮件另存为 .eml，再导入到这里。");
        }

        if (LooksLikePdf(bytes))
        {
            return await ImportPdfTempAsync(fileName, bytes);
        }
        if (TextDecoding.LooksLikeZip(bytes))
        {
            var kind = DetectZipKind(bytes);
            if (kind == "xlsx")
            {
                return XlsxImport.FromFileTemp(fileName, bytes, _media);
            }
            if (kind == "pptx")
            {
                return PptxImport.FromFileTemp(fileName, bytes, _media);
            }
            if (kind == "docx")
            {
                return DocxImport.FromFileTemp(fileName, bytes, _media);
            }

            try
            {
                return DocxImport.FromFileTemp(fileName, bytes, _media);
            }
            catch
            {
                // 不是 docx，继续按文本处理
            }
        }
        if (TextDecoding.LooksLikeOle(bytes))
        {
            return ImportDocWithWord(fileName, bytes);
        }
        if (TextDecoding.LooksLikeRtf(text))
        {
            return RtfImport.FromRtf(text, title, fileName);
        }
        if (TextDecoding.LooksLikeHtml(text))
        {
            return HtmlImport.FromHtml(text, baseDirectory ?? "", _media, fileName);
        }

        return PlainTextImport.FromPlainText(text, title, fileName);
    }

    private ImportResult ImportOfficeTemp(
        string fileName,
        byte[] bytes,
        Func<string, string, ImportResult> importer)
    {
        var directory = Path.Combine(Path.GetTempPath(), "WinNote_office_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Path.GetFileName(fileName));
        try
        {
            File.WriteAllBytes(path, bytes);
            return importer(path, fileName);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    private async Task<ImportResult> ImportOneNoteAsync(string fileName, byte[] bytes)
    {
        var directory = Path.Combine(Path.GetTempPath(), "WinNote_one_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Path.GetFileName(fileName));
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            return await OfficeComImport.FromOneNoteViaPowerShellAsync(path, _media, fileName);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    private static string DetectZipKind(byte[] bytes)
    {
        try
        {
            using var memory = new MemoryStream(bytes);
            using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
            var names = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (names.Contains("xl/workbook.xml"))
            {
                return "xlsx";
            }
            if (names.Contains("ppt/presentation.xml"))
            {
                return "pptx";
            }
            if (names.Contains("word/document.xml"))
            {
                return "docx";
            }
        }
        catch
        {
            // ignored
        }
        return "";
    }

    private async Task<ImportResult> ImportDocAsync(string fileName, byte[] bytes, string decoded, string? baseDirectory)    {
        if (TextDecoding.LooksLikeHtml(decoded))
        {
            return HtmlImport.FromHtml(decoded, baseDirectory ?? "", _media, fileName);
        }
        if (TextDecoding.LooksLikeRtf(decoded))
        {
            return RtfImport.FromRtf(decoded, Path.GetFileNameWithoutExtension(fileName), fileName);
        }
        if (TextDecoding.LooksLikeZip(bytes))
        {
            return DocxImport.FromFileTemp(fileName, bytes, _media);
        }
        if (TextDecoding.LooksLikeOle(bytes))
        {
            return ImportDocWithWord(fileName, bytes);
        }

        return PlainTextImport.FromPlainText(decoded, Path.GetFileNameWithoutExtension(fileName), fileName);
    }

    private ImportResult ImportDocWithWord(string fileName, byte[] bytes)
    {
        var type = Type.GetTypeFromProgID("Word.Application");
        if (type is null)
        {
            throw new InvalidOperationException(
                "这台电脑没有安装 Word，无法打开旧版 .doc。请用 WPS/Word 另存为 .docx 或 .txt 后再导入。");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "WinNote_doc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var originalPath = Path.Combine(tempDir, fileName);
        var htmlPath = Path.Combine(tempDir, "word_content.html");

        object? word = null;
        object? document = null;
        try
        {
            File.WriteAllBytes(originalPath, bytes);
            word = Activator.CreateInstance(type);
            if (word is null)
            {
                throw new InvalidOperationException("无法启动 Word。");
            }

            dynamic application = word;
            application.Visible = false;
            application.DisplayAlerts = 0;

            document = application.Documents.Open(originalPath, false, true);
            dynamic doc = document;
            doc.SaveAs(htmlPath, 10); // wdFormatFilteredHTML
            doc.Close(0);
            document = null;

            var html = TextDecoding.DecodeFile(htmlPath);
            var result = HtmlImport.FromHtml(html, tempDir, _media, fileName);
            result.Warning = "已通过本机 Word 转换旧版 .doc 文件。";
            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("用 Word 打开旧版 .doc 失败：" + ex.Message, ex);
        }
        finally
        {
            try
            {
                if (document is not null)
                {
                    ((dynamic)document).Close(0);
                }
            }
            catch
            {
                // ignored
            }

            try
            {
                if (word is not null)
                {
                    ((dynamic)word).Quit(0);
                }
            }
            catch
            {
                // ignored
            }

            if (document is not null)
            {
                Marshal.FinalReleaseComObject(document);
            }
            if (word is not null)
            {
                Marshal.FinalReleaseComObject(word);
            }

            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    private async Task<ImportResult> ImportPdfTempAsync(string fileName, byte[] bytes)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WinNote_pdf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, fileName);
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            return await PdfImport.FromFileAsync(path, _media);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    private ImportResult ImportImage(string fileName, byte[] bytes, string extension)
    {
        var file = _media.SaveBytes(bytes, extension);
        var title = Path.GetFileNameWithoutExtension(fileName);
        return new ImportResult
        {
            Title = string.IsNullOrWhiteSpace(title) ? "导入的图片" : title,
            Html = FigureHtml(file, _media.UrlFor(file)),
            Text = "",
            SourceName = fileName,
            Images = new List<string> { file },
            Warning = "图片已导入。选中图片后点底部的「识图」，可以提取图片里的文字。",
        };
    }

    private ImportResult ImportHdoc(string fileName, byte[] bytes)
    {
        if (Encoding.ASCII.GetString(bytes, 0, Math.Min(15, bytes.Length)) == "SQLite format 3")
        {
            throw new InvalidOperationException(
                "华为 .hdoc 是加密的备忘录备份包，只能在华为设备上恢复，电脑端无法直接解析。\n" +
                "请在华为备忘录里先导出为 TXT / HTML / PDF，再导入到这里。");
        }

        if (TextDecoding.LooksLikeZip(bytes))
        {
            try
            {
                using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
                var pieces = new List<string>();
                foreach (var entry in archive.Entries)
                {
                    var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                    if (ext is not (".txt" or ".html" or ".htm" or ".md" or ".json"))
                    {
                        continue;
                    }

                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream, Encoding.UTF8, true);
                    pieces.Add(reader.ReadToEnd());
                    if (pieces.Count >= 20)
                    {
                        break;
                    }
                }

                if (pieces.Count > 0)
                {
                    var joined = string.Join("\n\n", pieces);
                    var result = TextDecoding.LooksLikeHtml(joined)
                        ? HtmlImport.FromHtml(joined, "", _media, fileName)
                        : PlainTextImport.FromPlainText(joined, Path.GetFileNameWithoutExtension(fileName), fileName);
                    result.Warning = "已从 .hdoc 压缩包中提取出文本，原备份里的排版和加密信息无法完整保留。";
                    return result;
                }
            }
            catch
            {
                // 继续走友好提示
            }
        }

        throw new InvalidOperationException(
            "这个 .hdoc 是华为备忘录的私有备份格式，电脑端无法直接打开。\n" +
            "请在华为备忘录里导出为 TXT / HTML / PDF 后再导入。");
    }

    private static string FigureHtml(string file, string url)
    {
        return "<figure class=\"img\" contenteditable=\"false\" data-file=\"" + file +
               "\"><img src=\"" + url + "\" data-file=\"" + file +
               "\"><button class=\"img-del\" type=\"button\"></button></figure>";
    }

    private static bool LooksLikePdf(byte[] bytes)
    {
        return bytes.Length >= 4
               && bytes[0] == 0x25 && bytes[1] == 0x50
               && bytes[2] == 0x44 && bytes[3] == 0x46;
    }

    private static string SafeDecode(byte[] bytes)
    {
        try
        {
            return TextDecoding.Decode(bytes);
        }
        catch
        {
            return "";
        }
    }
}
