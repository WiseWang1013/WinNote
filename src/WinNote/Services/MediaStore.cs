using System.IO;

namespace WinNote.Services;

public sealed class MediaStore
{
    public MediaStore(string imagesDir)
    {
        ImagesDir = imagesDir;
        Directory.CreateDirectory(imagesDir);
    }

    public string ImagesDir { get; }

    public string SaveBytes(byte[] bytes, string extension)
    {
        var normalized = NormalizeExtension(extension);
        var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}{normalized}";
        File.WriteAllBytes(Path.Combine(ImagesDir, fileName), bytes);
        return fileName;
    }

    public string SaveDataUrl(string dataUrl)
    {
        var comma = dataUrl.IndexOf(',');
        if (comma < 0)
        {
            throw new InvalidOperationException("图片数据格式不正确。");
        }

        var header = dataUrl[..comma];
        var extension = header.Contains("image/jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg"
            : header.Contains("image/webp", StringComparison.OrdinalIgnoreCase) ? ".webp"
            : header.Contains("image/gif", StringComparison.OrdinalIgnoreCase) ? ".gif"
            : header.Contains("image/bmp", StringComparison.OrdinalIgnoreCase) ? ".bmp"
            : ".png";
        var bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
        return SaveBytes(bytes, extension);
    }

    public string UrlFor(string fileName) => "https://media.local/" + fileName;

    public static string NormalizeExtension(string extension)
    {
        extension = (extension ?? "").ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff" => extension,
            _ => ".png",
        };
    }

    public static string GuessExtension(string contentType)
    {
        contentType = (contentType ?? "").ToLowerInvariant();
        if (contentType.Contains("jpeg")) return ".jpg";
        if (contentType.Contains("gif")) return ".gif";
        if (contentType.Contains("bmp")) return ".bmp";
        if (contentType.Contains("webp")) return ".webp";
        if (contentType.Contains("tiff")) return ".tiff";
        return ".png";
    }
}
