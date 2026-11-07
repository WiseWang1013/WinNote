using System.Text;
using System.Text.RegularExpressions;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace WinNote.Services;

public static class OcrService
{
    public static async Task<string> RecognizeAsync(string imagePath)
    {
        var file = await StorageFile.GetFileFromPathAsync(imagePath);
        using var stream = await file.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);

        var maxSide = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
        var transform = new BitmapTransform();
        var scale = maxSide > 4000 ? 4000.0 / maxSide : 1.0;
        if (scale < 1.0)
        {
            transform.ScaledWidth = (uint)Math.Max(1, decoder.PixelWidth * scale);
            transform.ScaledHeight = (uint)Math.Max(1, decoder.PixelHeight * scale);
            transform.InterpolationMode = BitmapInterpolationMode.Fant;
        }

        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);

        var engine = OcrEngine.TryCreateFromLanguage(new Language("zh-Hans"))
                     ?? OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? throw new InvalidOperationException("系统没有安装 OCR 语言包。");

        var result = await engine.RecognizeAsync(bitmap);
        var builder = new StringBuilder();
        foreach (var line in result.Lines)
        {
            var text = CleanLine(line.Text);
            if (text.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(text);
        }

        return builder.ToString();
    }

    private static string CleanLine(string text)
    {
        text = Regex.Replace(text.Trim(), @"\s+", " ");
        text = Regex.Replace(text, @"(?<=[\u4e00-\u9fa5])\s+(?=[\u4e00-\u9fa5])", "");
        text = Regex.Replace(text, @"(?<=[\u4e00-\u9fa5])\s+", "");
        text = Regex.Replace(text, @"\s+(?=[\u4e00-\u9fa5])", "");
        text = Regex.Replace(text, @"(?<=\d)\s+(?=\d)", "");
        return text.Trim();
    }
}
