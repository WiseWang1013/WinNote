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
    public string ThemeId { get; set; } = "flat";
    public string Accent { get; set; } = "blue";
    public string RadiusStyle { get; set; } = "squircle";
    public bool Animations { get; set; } = true;
    public int TopSafeInset { get; set; }
    public string OcrLanguage { get; set; } = "zh-Hans";
    public List<FolderInfo> Folders { get; set; } = new();
    public string? LockSalt { get; set; }
    public string? LockHash { get; set; }
}
