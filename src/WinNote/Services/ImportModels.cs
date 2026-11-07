namespace WinNote.Services;

public sealed class ImportResult
{
    public string Title { get; set; } = "";
    public string Html { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Warning { get; set; }
    public string? SourceName { get; set; }
    public bool NeedsSanitize { get; set; }
    public List<string> Images { get; set; } = new();
}

public sealed class ExportRun
{
    public string Text { get; set; } = "";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
}

public sealed class ExportBlock
{
    public string Type { get; set; } = "p";
    public string Text { get; set; } = "";
    public bool Checked { get; set; }
    public int Level { get; set; }
    public string? File { get; set; }
    public List<ExportRun> Runs { get; set; } = new();
}

public sealed class ExportDoc
{
    public string Title { get; set; } = "";
    public List<ExportBlock> Blocks { get; set; } = new();
}
