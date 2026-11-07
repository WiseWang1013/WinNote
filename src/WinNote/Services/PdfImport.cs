using System.IO;
using System.Text;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace WinNote.Services;

public static class PdfImport
{
    public static async Task<ImportResult> FromFileAsync(string path, MediaStore media)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        var document = await PdfDocument.LoadFromFileAsync(file);
        var images = new List<string>();
        var html = new StringBuilder();
        var text = new StringBuilder();
        var limit = (int)Math.Min(document.PageCount, 30);

        for (uint index = 0; index < limit; index++)
        {
            var page = document.GetPage(index);
            var fileName = media.SaveBytes(await RenderPageAsync(page), ".png");
            images.Add(fileName);

            html.Append("<div class=\"pdf-page\">第 ")
                .Append(index + 1)
                .Append(" 页</div>")
                .Append("<figure class=\"img\" contenteditable=\"false\" data-file=\"")
                .Append(fileName)
                .Append("\"><img src=\"")
                .Append(media.UrlFor(fileName))
                .Append("\" data-file=\"")
                .Append(fileName)
                .Append("\"><button class=\"img-del\" type=\"button\"></button></figure>");

            try
            {
                var pageText = await OcrService.RecognizeAsync(Path.Combine(media.ImagesDir, fileName));
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    text.AppendLine(pageText);
                    foreach (var line in pageText.Split('\n'))
                    {
                        html.Append("<div>").Append(TextDecoding.HtmlEscape(line.TrimEnd())).Append("</div>");
                    }
                }
            }
            catch
            {
                // 扫描件识别失败时仍然保留页面图片
            }
        }

        var warning = document.PageCount > limit
            ? $"PDF 共 {document.PageCount} 页，仅导入了前 {limit} 页。"
            : null;

        return new ImportResult
        {
            Title = Path.GetFileNameWithoutExtension(path),
            Html = html.ToString(),
            Text = text.ToString().Trim(),
            SourceName = Path.GetFileName(path),
            Images = images,
            Warning = warning,
        };
    }

    private static async Task<byte[]> RenderPageAsync(PdfPage page)
    {
        using var stream = new InMemoryRandomAccessStream();
        var options = new PdfPageRenderOptions
        {
            DestinationWidth = 1600,
        };
        await page.RenderToStreamAsync(stream, options);
        stream.Seek(0);

        var length = (uint)stream.Size;
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync(length);
        var bytes = new byte[length];
        reader.ReadBytes(bytes);
        return bytes;
    }
}
