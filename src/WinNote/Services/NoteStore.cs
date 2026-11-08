using System.IO;
using System.Text;
using System.Text.Json;
using WinNote.Models;

namespace WinNote.Services;

public sealed class NoteStore
{
    private readonly object _gate = new();
    private string _notesFile = "";
    private string _settingsFile = "";
    private List<Note> _notes = new();
    private AppSettings _settings = new();

    public NoteStore(string? dataDir = null)
    {
        Configure(dataDir);
    }

    public string DataDir { get; private set; } = "";
    public string ImagesDir { get; private set; } = "";
    public string FilesDir { get; private set; } = "";
    public AppSettings Settings => _settings;
    public string Theme => _settings.Theme;
    public bool HasPasscode => !string.IsNullOrEmpty(_settings.LockHash);
    public IReadOnlyList<Note> Notes => _notes;

    public void SwitchTo(string dataDir)
    {
        lock (_gate)
        {
            Configure(dataDir);
            _notes = new List<Note>();
            _settings = new AppSettings();
            Load();
        }
    }

    private void Configure(string? dataDir)
    {
        DataDir = System.IO.Path.GetFullPath(
            string.IsNullOrWhiteSpace(dataDir)
                ? System.IO.Path.Combine(AppContext.BaseDirectory, "data")
                : dataDir);
        ImagesDir = System.IO.Path.Combine(DataDir, "images");
        FilesDir = System.IO.Path.Combine(DataDir, "files");
        Directory.CreateDirectory(ImagesDir);
        Directory.CreateDirectory(FilesDir);
        Directory.CreateDirectory(System.IO.Path.Combine(DataDir, "web"));
        _notesFile = System.IO.Path.Combine(DataDir, "notes.json");
        _settingsFile = System.IO.Path.Combine(DataDir, "settings.json");
    }

    public List<Note> Load()
    {
        lock (_gate)
        {
            LoadSettings();
            if (!File.Exists(_notesFile))
            {
                _notes = CreateSeedNotes();
                WriteNotes();
                return _notes;
            }

            try
            {
                var json = File.ReadAllText(_notesFile, Encoding.UTF8);
                _notes = JsonSerializer.Deserialize<List<Note>>(json, JsonDefaults.Options) ?? new List<Note>();
            }
            catch
            {
                try
                {
                    File.Move(_notesFile, _notesFile + ".bad-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
                }
                catch
                {
                    // ignored
                }

                _notes = CreateSeedNotes();
                WriteNotes();
                return _notes;
            }

            var cutoff = DateTimeOffset.Now.AddDays(-30);
            var removed = _notes.RemoveAll(n =>
                n.Deleted &&
                DateTimeOffset.TryParse(n.DeletedAt, out var deletedAt) &&
                deletedAt < cutoff);
            if (removed > 0)
            {
                WriteNotes();
            }

            return _notes;
        }
    }

    public void SaveAll(IEnumerable<Note> notes)
    {
        lock (_gate)
        {
            _notes = notes.ToList();
            WriteNotes();
        }
    }

    public void SaveTheme(string theme)
    {
        lock (_gate)
        {
            _settings.Theme = theme == "dark" ? "dark" : "light";
            WriteSettings();
        }
    }

    public void SaveOcrLanguage(string language)
    {
        lock (_gate)
        {
            _settings.OcrLanguage = string.IsNullOrWhiteSpace(language) ? "zh-Hans" : language;
            WriteSettings();
        }
    }

    public void SaveFolders(IEnumerable<FolderInfo> folders)
    {
        lock (_gate)
        {
            _settings.Folders = folders
                .Where(f => !string.IsNullOrWhiteSpace(f.Id))
                .Select(f => new FolderInfo
                {
                    Id = f.Id.Trim(),
                    Name = string.IsNullOrWhiteSpace(f.Name) ? "未命名文件夹" : f.Name.Trim(),
                    Color = NormalizeColor(f.Color),
                })
                .ToList();

            if (_settings.Folders.Count == 0)
            {
                _settings.Folders = CreateDefaultFolders();
            }

            WriteSettings();
        }
    }

    public void SaveLock(string? salt, string? hash)
    {
        lock (_gate)
        {
            _settings.LockSalt = salt;
            _settings.LockHash = hash;
            WriteSettings();
        }
    }

    private static string NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return "#4C8DFF";
        }

        var value = color.Trim();
        return System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9A-Fa-f]{6}$")
            ? value.ToUpperInvariant()
            : "#4C8DFF";
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFile))
            {
                var json = File.ReadAllText(_settingsFile, Encoding.UTF8);
                _settings = JsonSerializer.Deserialize<AppSettings>(json, JsonDefaults.Options)
                            ?? new AppSettings();
            }
            else
            {
                _settings = new AppSettings();
            }
        }
        catch
        {
            _settings = new AppSettings();
        }

        _settings.Theme = _settings.Theme == "dark" ? "dark" : "light";
        _settings.Folders ??= new List<FolderInfo>();
        if (_settings.Folders.Count == 0)
        {
            _settings.Folders = CreateDefaultFolders();
        }

        foreach (var folder in _settings.Folders)
        {
            folder.Name = string.IsNullOrWhiteSpace(folder.Name) ? "未命名文件夹" : folder.Name.Trim();
            folder.Color = NormalizeColor(folder.Color);
        }

        if (!File.Exists(_settingsFile))
        {
            WriteSettings();
        }
    }

    private void WriteSettings()
    {
        var json = JsonSerializer.Serialize(_settings, JsonDefaults.Options);
        var temp = _settingsFile + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));
        File.Move(temp, _settingsFile, true);
    }

    private void WriteNotes()
    {
        var json = JsonSerializer.Serialize(_notes, JsonDefaults.Options);
        var temp = _notesFile + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));
        File.Move(temp, _notesFile, true);
    }

    public static List<FolderInfo> CreateDefaultFolders()
    {
        return new List<FolderInfo>
        {
            new() { Id = "work", Name = "工作", Color = "#4C8DFF" },
            new() { Id = "life", Name = "生活", Color = "#34C759" },
            new() { Id = "study", Name = "学习", Color = "#FF9F0A" },
            new() { Id = "idea", Name = "灵感", Color = "#BF5AF2" },
        };
    }

    private static List<Note> CreateSeedNotes()
    {
        var now = DateTimeOffset.Now;
        return new List<Note>
        {
            new()
            {
                Id = "seed-welcome",
                Title = "欢迎使用备忘录 2.0",
                Color = "yellow",
                Category = "life",
                Pinned = true,
                CreatedAt = now.AddHours(-2).ToString("o"),
                UpdatedAt = now.AddHours(-1).ToString("o"),
                Tags = new[] { "欢迎", "使用说明" },
                Text = "这是一款参照华为/苹果/小米备忘录做的桌面备忘录。左边文件夹和笔记列表，右边编辑，输入即自动保存。",
                Html =
                    """
                    <div>这是一款参照华为、苹果、小米备忘录做的桌面备忘录，功能都在这一页里：</div>
                    <div><br></div>
                    <div>· 左边是文件夹、标签、智能文件夹和笔记列表，右边编辑，输入即自动保存</div>
                    <div>· 笔记颜色、文件夹、标签、置顶、搜索、最近删除、深色模式</div>
                    <div>· 待办清单、表格、链接、插入图片/音频/视频/文件、录音</div>
                    <div>· 语音输入、图片文字识别、手写涂鸦</div>
                    <div>· 备忘录密码锁定、导入导出 TXT/Markdown/HTML/Word/PDF/EML</div>
                    <div><br></div>
                    <div>试试底部的「语音」「识图」或「插入」吧。所有内容都只存在这台电脑上。</div>
                    """,
            },
            new()
            {
                Id = "seed-todo",
                Title = "本周待办",
                Color = "green",
                Category = "work",
                Pinned = false,
                CreatedAt = now.AddHours(-3).ToString("o"),
                UpdatedAt = now.AddHours(-2).ToString("o"),
                Tags = new[] { "待办" },
                Text = "这周要做的几件事：整理会议记录 提交周报 预约牙医",
                Html =
                    """
                    <div>这周要做的几件事：</div>
                    <div><br></div>
                    <div class="todo-item" data-checked="false"><span class="todo-box" contenteditable="false"></span><span class="todo-text">整理会议记录</span></div>
                    <div class="todo-item" data-checked="true"><span class="todo-box" contenteditable="false"></span><span class="todo-text">提交周报</span></div>
                    <div class="todo-item" data-checked="false"><span class="todo-box" contenteditable="false"></span><span class="todo-text">预约牙医</span></div>
                    <div><br></div>
                    <div>点一下小方框就能打勾。</div>
                    """,
            },
        };
    }
}
