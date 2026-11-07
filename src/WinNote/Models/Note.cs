namespace WinNote.Models;

public sealed class Note
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Html { get; set; } = "";
    public string Text { get; set; } = "";
    public string Color { get; set; } = "default";
    public string? Category { get; set; }
    public bool Pinned { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public bool Deleted { get; set; }
    public string? DeletedAt { get; set; }
    public string[] Images { get; set; } = Array.Empty<string>();
}
