using System.IO;
using System.Text;
using System.Text.Json;
using WinNote.Models;

namespace WinNote.Services;

public sealed class NoteStore
{
    private readonly object _gate = new();
    private readonly string _notesFile;
    private readonly string _settingsFile;
    private List<Note> _notes = new();

    public NoteStore()
    {
        DataDir = Path.Combine(AppContext.BaseDirectory, "data");
        ImagesDir = Path.Combine(DataDir, "images");
        Directory.CreateDirectory(ImagesDir);
        Directory.CreateDirectory(Path.Combine(DataDir, "web"));
        _notesFile = Path.Combine(DataDir, "notes.json");
        _settingsFile = Path.Combine(DataDir, "settings.json");
    }

    public string DataDir { get; }
    public string ImagesDir { get; }
    public string Theme { get; private set; } = "light";
    public IReadOnlyList<Note> Notes => _notes;

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
            Theme = theme == "dark" ? "dark" : "light";
            try
            {
                var payload = JsonSerializer.Serialize(new { theme = Theme }, JsonDefaults.Options);
                File.WriteAllText(_settingsFile, payload, new UTF8Encoding(false));
            }
            catch
            {
                // ignored
            }
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsFile))
            {
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(_settingsFile, Encoding.UTF8));
            if (doc.RootElement.TryGetProperty("theme", out var theme) && theme.GetString() == "dark")
            {
                Theme = "dark";
            }
        }
        catch
        {
            Theme = "light";
        }
    }

    private void WriteNotes()
    {
        var json = JsonSerializer.Serialize(_notes, JsonDefaults.Options);
        var temp = _notesFile + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));
        File.Move(temp, _notesFile, true);
    }

    private static List<Note> CreateSeedNotes()
    {
        var now = DateTimeOffset.Now;
        return new List<Note>
        {
            new()
            {
                Id = "seed-welcome",
                Title = "欢迎使用备忘录",
                Color = "yellow",
                Category = "life",
                Pinned = true,
                CreatedAt = now.AddHours(-2).ToString("o"),
                UpdatedAt = now.AddHours(-1).ToString("o"),
                Text = "这是一款参照华为备忘录做的桌面备忘录。左边分类和笔记列表，右边编辑，输入即自动保存。",
                Html =
                    """
                    <div>这是一款参照华为备忘录做的桌面备忘录，功能都在这一页里：</div>
                    <div><br></div>
                    <div>· 左边是分类和笔记列表，右边编辑，输入即自动保存</div>
                    <div>· 给笔记换颜色、分到「工作 / 生活 / 学习 / 灵感」、置顶</div>
                    <div>· 底部的工具栏：待办清单、插入图片、语音输入、图片文字识别、手写涂鸦</div>
                    <div>· 搜索、最近删除（保留 30 天）、深色模式</div>
                    <div><br></div>
                    <div>试试底部的「语音」或「识图」吧。所有内容都只存在这台电脑上。</div>
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
