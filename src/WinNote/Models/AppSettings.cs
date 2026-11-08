namespace WinNote.Models;

public sealed class FolderInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#4C8DFF";
}

public sealed class AppSettings
{
    public string Theme { get; set; } = "light";
    public string OcrLanguage { get; set; } = "zh-Hans";
    public List<FolderInfo> Folders { get; set; } = new();
    public string? LockSalt { get; set; }
    public string? LockHash { get; set; }
}
