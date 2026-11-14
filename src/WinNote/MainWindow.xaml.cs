using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using WinNote.Models;
using WinNote.Services;

namespace WinNote;

public partial class MainWindow : Window
{
    private readonly ProfileStore _profiles;
    private readonly NoteStore _store;
    private readonly VoiceService _voice = new();
    private readonly AudioRecorder _recorder = new();
    private ImportService _importer;
    private HwndSource? _hwndSource;
    private IntPtr _hotKeyHandle;
    private const int QuickNoteHotKeyId = 0x574E;
    private const int MoveWindowHotKeyId = 0x574F;
    private bool _allowClose;
    private string _theme = "light";

    public MainWindow()
    {
        InitializeComponent();

        _profiles = new ProfileStore();
        _store = new NoteStore(_profiles.Active.Path);
        _store.Load();
        _theme = _store.Theme;
        _importer = new ImportService(_store.ImagesDir);
        ApplyTheme(_theme);

        _voice.FinalText += OnVoiceFinalText;
        _voice.Hypothesis += OnVoiceHypothesis;
        _voice.ListeningChanged += OnVoiceListeningChanged;
        _voice.Failed += OnVoiceFailed;

        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
        Closing += MainWindow_Closing;
        Closed += (_, _) =>
        {
            _voice.Dispose();
            _recorder.Dispose();
            UnregisterQuickNoteHotKey();
        };
        SourceInitialized += MainWindow_SourceInitialized;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await InitWebAsync();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(
                "电脑上没有检测到 WebView2 运行时。\n请安装 Microsoft Edge WebView2 Runtime 后再打开备忘录。",
                "备忘录", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("界面初始化失败：" + ex.Message, "备忘录", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task InitWebAsync()
    {
        var webDir = WebAssets.Extract();
        var userDataDir = Path.Combine(_store.DataDir, "WebView2");
        Directory.CreateDirectory(userDataDir);

        var environment = await CoreWebView2Environment.CreateAsync(null, userDataDir);
        await Web.EnsureCoreWebView2Async(environment);

        var core = Web.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping("app.local", webDir, CoreWebView2HostResourceAccessKind.Allow);
        core.SetVirtualHostNameToFolderMapping("media.local", _store.ImagesDir, CoreWebView2HostResourceAccessKind.Allow);
        core.SetVirtualHostNameToFolderMapping("files.local", _store.FilesDir, CoreWebView2HostResourceAccessKind.Allow);
        core.SetVirtualHostNameToFolderMapping("appearance.local", _store.AppearanceDir, CoreWebView2HostResourceAccessKind.Allow);

        var settings = core.Settings;
        settings.AreDevToolsEnabled = Environment.GetEnvironmentVariable("WINNOTE_DEV") == "1";
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsBuiltInErrorPageEnabled = false;

        core.WebMessageReceived += OnWebMessageReceived;
        core.NavigationCompleted += OnNavigationCompleted;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;

        UpdateWebBackground();
        core.Navigate("https://app.local/index.html");
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Uri)
            || e.Uri.StartsWith("https://app.local/", StringComparison.OrdinalIgnoreCase)
            || e.Uri.StartsWith("https://media.local/", StringComparison.OrdinalIgnoreCase)
            || e.Uri.StartsWith("https://files.local/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
        try
        {
            if (e.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || e.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || e.Uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
            }
        }
        catch
        {
            // ignored
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        try
        {
            if (e.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || e.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
            }
        }
        catch
        {
            // ignored
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            return;
        }

        MessageBox.Show("页面加载失败：" + e.WebErrorStatus, "备忘录", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        BridgeMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<BridgeMessage>(e.WebMessageAsJson, JsonDefaults.Options);
        }
        catch
        {
            return;
        }

        if (message?.Cmd is null)
        {
            return;
        }

        var payload = message.Payload;
        try
        {
            switch (message.Cmd)
            {
                case "load":
                    Reply(message, new
                    {
                        notes = _store.Notes,
                        settings = new
                        {
                            theme = _theme,
                            folders = _store.Settings.Folders,
                            hasPasscode = _store.HasPasscode,
                            ocrLanguage = EnsureOcrLanguage(),
                            ocrLanguages = OcrService.AvailableLanguages(),
                            appearance = BuildAppearancePayload(),
                            customThemes = ListCustomThemes(),
                            appearanceDir = _store.AppearanceDir,
                        },
                        profile = new
                        {
                            id = _profiles.ActiveId,
                            name = _profiles.Active.Name,
                            path = _profiles.Active.Path,
                            profiles = _profiles.Profiles,
                        },
                    });
                    break;

                case "saveNotes":
                {
                    var notes = payload.Deserialize<List<Note>>(JsonDefaults.Options) ?? new List<Note>();
                    _store.SaveAll(notes);
                    Reply(message, new { savedAt = DateTimeOffset.Now.ToString("o") });
                    break;
                }

                case "setTheme":
                {
                    var theme = PayloadString(payload, "theme") == "dark" ? "dark" : "light";
                    ApplyTheme(theme);
                    _store.SaveTheme(theme);
                    Reply(message, new { theme });
                    break;
                }

                case "setOcrLanguage":
                {
                    var language = PayloadString(payload, "language") ?? "zh-Hans";
                    _store.SaveOcrLanguage(language);
                    Reply(message, new { language });
                    break;
                }

                case "saveAppearance":
                {
                    var oldInset = _store.Settings.TopSafeInset;
                    _store.SaveAppearance(
                        PayloadString(payload, "themeId") ?? "flat",
                        PayloadString(payload, "accent") ?? "blue",
                        PayloadString(payload, "radiusStyle") ?? "squircle",
                        PayloadBool(payload, "animations", true),
                        PayloadInt(payload, "topSafeInset", 0));
                    if (oldInset != _store.Settings.TopSafeInset && WindowState == WindowState.Maximized)
                    {
                        WindowState = WindowState.Normal;
                        WindowState = WindowState.Maximized;
                    }
                    Reply(message, BuildAppearancePayload());
                    break;
                }

                case "setChrome":
                {
                    ApplyChromeColors(
                        ParseColor(PayloadString(payload, "bg"), Color.FromRgb(0xF2, 0xF3, 0xF5)),
                        ParseColor(PayloadString(payload, "fg"), Color.FromRgb(0x1A, 0x1A, 0x1C)),
                        ParseColor(PayloadString(payload, "sub"), Color.FromRgb(0x8E, 0x90, 0x96)),
                        ParseColor(PayloadString(payload, "hover"), Color.FromRgb(0xE4, 0xE6, 0xEA)),
                        ParseColor(PayloadString(payload, "pressed"), Color.FromRgb(0xD9, 0xDB, 0xE0)),
                        ParseColor(PayloadString(payload, "line"), Color.FromRgb(0xE6, 0xE8, 0xEC)));
                    Reply(message, null);
                    break;
                }

                case "listCustomThemes":
                    Reply(message, new { files = ListCustomThemes(), dir = _store.AppearanceDir });
                    break;

                case "importTheme":
                {
                    var dialog = new OpenFileDialog
                    {
                        Title = "导入外观主题（JavaScript 配置文件）",
                        Filter = "WinNote 主题|*.js|所有文件|*.*",
                        CheckFileExists = true,
                        Multiselect = false,
                        InitialDirectory = Directory.Exists(@"D:\app\appearance")
                            ? @"D:\app\appearance"
                            : "",
                    };
                    if (dialog.ShowDialog(this) != true)
                    {
                        Reply(message, null);
                        break;
                    }

                    var fileName = SanitizeThemeFileName(Path.GetFileName(dialog.FileName));
                    var destination = Path.Combine(_store.AppearanceDir, fileName);
                    File.Copy(dialog.FileName, destination, true);
                    Reply(message, new { file = fileName });
                    break;
                }

                case "importThemeLibrary":
                {
                    var source = @"D:\app\appearance";
                    if (!Directory.Exists(source))
                    {
                        throw new InvalidOperationException("没有找到 D:\\app\\appearance 目录。");
                    }

                    var count = 0;
                    foreach (var file in Directory.GetFiles(source, "*.js"))
                    {
                        var name = SanitizeThemeFileName(Path.GetFileName(file));
                        File.Copy(file, Path.Combine(_store.AppearanceDir, name), true);
                        count++;
                    }

                    Reply(message, new { count, files = ListCustomThemes() });
                    break;
                }

                case "deleteTheme":
                {
                    var fileName = Path.GetFileName(PayloadString(payload, "file") ?? "");
                    if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("主题文件名不正确。");
                    }

                    var path = Path.Combine(_store.AppearanceDir, fileName);
                    if (!path.StartsWith(_store.AppearanceDir, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("主题路径不正确。");
                    }
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                    Reply(message, new { files = ListCustomThemes() });
                    break;
                }

                case "openAppearanceFolder":
                    Directory.CreateDirectory(_store.AppearanceDir);
                    Process.Start(new ProcessStartInfo("explorer.exe", _store.AppearanceDir) { UseShellExecute = true });
                    Reply(message, new { path = _store.AppearanceDir });
                    break;

                case "setWindowInset":
                {
                    var inset = Math.Clamp(PayloadInt(payload, "topSafeInset", 0), 0, 80);
                    _store.SaveAppearance(
                        _store.Settings.ThemeId,
                        _store.Settings.Accent,
                        _store.Settings.RadiusStyle,
                        _store.Settings.Animations,
                        inset);
                    if (WindowState == WindowState.Maximized)
                    {
                        WindowState = WindowState.Normal;
                        WindowState = WindowState.Maximized;
                    }
                    Reply(message, BuildAppearancePayload());
                    break;
                }

                case "moveWindow":
                    StartMoveWindow();
                    Reply(message, null);
                    break;

                case "switchProfile":
                {
                    var id = PayloadString(payload, "id") ?? "";
                    var profile = _profiles.SetActive(id);
                    _store.SwitchTo(profile.Path);
                    _theme = _store.Theme;
                    _importer = new ImportService(_store.ImagesDir);
                    ApplyTheme(_theme);
                    RemapMediaHosts();
                    Reply(message, BuildDataPayload());
                    break;
                }

                case "createProfile":
                {
                    var name = PayloadString(payload, "name") ?? "新数据文件夹";
                    var profile = _profiles.Add(name);
                    _store.SwitchTo(profile.Path);
                    _theme = _store.Theme;
                    _importer = new ImportService(_store.ImagesDir);
                    ApplyTheme(_theme);
                    RemapMediaHosts();
                    Reply(message, BuildDataPayload());
                    break;
                }

                case "chooseDataFolder":
                {
                    var dialog = new OpenFolderDialog
                    {
                        Title = "选择数据文件夹（原有内容不会被移动或删除）",
                        Multiselect = false,
                    };
                    if (dialog.ShowDialog(this) != true)
                    {
                        Reply(message, null);
                        break;
                    }

                    var selected = dialog.FolderName;
                    var name = Path.GetFileName(selected.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = "数据文件夹";
                    }

                    var profile = _profiles.Add(name, selected);
                    _store.SwitchTo(profile.Path);
                    _theme = _store.Theme;
                    _importer = new ImportService(_store.ImagesDir);
                    ApplyTheme(_theme);
                    RemapMediaHosts();
                    Reply(message, BuildDataPayload());
                    break;
                }

                case "renameProfile":
                {
                    var id = PayloadString(payload, "id") ?? "";
                    var name = PayloadString(payload, "name") ?? "";
                    var profile = _profiles.Rename(id, name);
                    Reply(message, new
                    {
                        profile = new
                        {
                            id = _profiles.ActiveId,
                            name = _profiles.Active.Name,
                            path = _profiles.Active.Path,
                            profiles = _profiles.Profiles,
                        },
                    });
                    break;
                }

                case "saveFolders":
                {
                    var folders = payload.Deserialize<List<FolderInfo>>(JsonDefaults.Options)
                                  ?? new List<FolderInfo>();
                    _store.SaveFolders(folders);
                    Reply(message, new { folders = _store.Settings.Folders });
                    break;
                }

                case "pickFile":
                {
                    var dialog = new OpenFileDialog
                    {
                        Title = "插入文件",
                        Filter =
                            "音频|*.mp3;*.wav;*.m4a;*.aac;*.ogg;*.flac;*.wma" +
                            "|视频|*.mp4;*.webm;*.mov;*.mkv;*.avi;*.wmv" +
                            "|图片|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp" +
                            "|所有文件|*.*",
                        CheckFileExists = true,
                        Multiselect = false,
                    };

                    if (dialog.ShowDialog(this) != true)
                    {
                        Reply(message, null);
                        break;
                    }

                    var sourceName = Path.GetFileName(dialog.FileName);
                    var extension = Path.GetExtension(dialog.FileName);
                    var kind = GetFileKind(extension);
                    if (kind == "image")
                    {
                        Reply(message, ImportImage(dialog.FileName));
                    }
                    else
                    {
                        var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}{extension}";
                        var destination = Path.Combine(_store.FilesDir, fileName);
                        File.Copy(dialog.FileName, destination, false);
                        Reply(message, new
                        {
                            file = fileName,
                            url = "https://files.local/" + fileName,
                            name = sourceName,
                            size = new FileInfo(destination).Length,
                            kind,
                        });
                    }

                    break;
                }

                case "startRecording":
                {
                    _recorder.Start();
                    Reply(message, new { recording = true });
                    break;
                }

                case "stopRecording":
                {
                    var fileName = $"rec_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}.wav";
                    var path = Path.Combine(_store.FilesDir, fileName);
                    _recorder.Stop(path);
                    Reply(message, new
                    {
                        file = fileName,
                        url = "https://files.local/" + fileName,
                        name = "录音 " + DateTime.Now.ToString("MM-dd HH:mm"),
                        size = new FileInfo(path).Length,
                        kind = "audio",
                    });
                    break;
                }

                case "openFile":
                {
                    var fileName = Path.GetFileName(PayloadString(payload, "file") ?? "");
                    if (string.IsNullOrEmpty(fileName))
                    {
                        throw new InvalidOperationException("没有指定文件。");
                    }

                    var path = Path.Combine(_store.FilesDir, fileName);
                    if (!File.Exists(path))
                    {
                        throw new InvalidOperationException("文件不存在。");
                    }

                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    Reply(message, new { path });
                    break;
                }

                case "setPasscode":
                {
                    var oldPasscode = PayloadString(payload, "oldPasscode") ?? "";
                    var newPasscode = PayloadString(payload, "newPasscode") ?? "";
                    if (newPasscode.Length < 4)
                    {
                        throw new InvalidOperationException("备忘录密码至少需要 4 位。");
                    }

                    if (_store.HasPasscode)
                    {
                        if (!NoteCrypto.Verify(oldPasscode, _store.Settings.LockSalt, _store.Settings.LockHash))
                        {
                            throw new InvalidOperationException("原密码不正确。");
                        }

                        if (_store.Notes.Any(n => n.Locked))
                        {
                            throw new InvalidOperationException("请先解锁所有已锁定的备忘录，再修改密码。");
                        }
                    }

                    var (salt, hash) = NoteCrypto.CreatePasscode(newPasscode);
                    _store.SaveLock(salt, hash);
                    Reply(message, new { hasPasscode = true });
                    break;
                }

                case "lockNote":
                {
                    var id = PayloadString(payload, "noteId") ?? "";
                    var passcode = PayloadString(payload, "passcode") ?? "";
                    if (_store.Settings.LockSalt is null
                        || !NoteCrypto.Verify(passcode, _store.Settings.LockSalt, _store.Settings.LockHash))
                    {
                        throw new InvalidOperationException("密码不正确。");
                    }

                    var note = _store.Notes.FirstOrDefault(n => n.Id == id && !n.Deleted)
                               ?? throw new InvalidOperationException("找不到这条备忘录。");
                    if (note.Locked)
                    {
                        throw new InvalidOperationException("这条备忘录已经锁定了。");
                    }

                    var content = new NoteLockContent
                    {
                        Title = note.Title,
                        Html = note.Html,
                        Text = note.Text,
                        Tags = note.Tags,
                        Images = note.Images,
                        Files = note.Files,
                    };
                    note.LockedPayload = NoteCrypto.Encrypt(content, passcode, _store.Settings.LockSalt);
                    note.Locked = true;
                    note.Title = "";
                    note.Html = "";
                    note.Text = "";
                    note.Tags = Array.Empty<string>();
                    note.Images = Array.Empty<string>();
                    note.Files = Array.Empty<string>();
                    note.UpdatedAt = DateTimeOffset.Now.ToString("o");
                    _store.SaveAll(_store.Notes);
                    Reply(message, note);
                    break;
                }

                case "unlockNote":
                {
                    var id = PayloadString(payload, "noteId") ?? "";
                    var passcode = PayloadString(payload, "passcode") ?? "";
                    if (_store.Settings.LockSalt is null
                        || !NoteCrypto.Verify(passcode, _store.Settings.LockSalt, _store.Settings.LockHash))
                    {
                        throw new InvalidOperationException("密码不正确。");
                    }

                    var note = _store.Notes.FirstOrDefault(n => n.Id == id)
                               ?? throw new InvalidOperationException("找不到这条备忘录。");
                    if (!note.Locked || string.IsNullOrEmpty(note.LockedPayload))
                    {
                        throw new InvalidOperationException("这条备忘录没有被锁定。");
                    }

                    var content = NoteCrypto.Decrypt(note.LockedPayload, passcode, _store.Settings.LockSalt);
                    note.Title = content.Title;
                    note.Html = content.Html;
                    note.Text = content.Text;
                    note.Tags = content.Tags ?? Array.Empty<string>();
                    note.Images = content.Images ?? Array.Empty<string>();
                    note.Files = content.Files ?? Array.Empty<string>();
                    note.Locked = false;
                    note.LockedPayload = null;
                    note.UpdatedAt = DateTimeOffset.Now.ToString("o");
                    _store.SaveAll(_store.Notes);
                    Reply(message, note);
                    break;
                }

                case "pickImage":
                {
                    var dialog = new OpenFileDialog
                    {
                        Title = "选择图片",
                        Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*",
                        CheckFileExists = true,
                        Multiselect = false,
                    };

                    if (dialog.ShowDialog(this) != true)
                    {
                        Reply(message, null);
                        break;
                    }

                    Reply(message, ImportImage(dialog.FileName));
                    break;
                }

                case "saveImage":
                {
                    var dataUrl = PayloadString(payload, "dataUrl");
                    if (string.IsNullOrEmpty(dataUrl))
                    {
                        throw new InvalidOperationException("图片数据为空。");
                    }

                    Reply(message, SaveImageData(dataUrl));
                    break;
                }

                case "ocr":
                {
                    var fileName = Path.GetFileName(PayloadString(payload, "file") ?? "");
                    if (string.IsNullOrEmpty(fileName))
                    {
                        throw new InvalidOperationException("没有指定要识别的图片。");
                    }

                    var path = Path.Combine(_store.ImagesDir, fileName);
                    if (!File.Exists(path))
                    {
                        throw new InvalidOperationException("图片文件不存在。");
                    }

                    var text = await OcrService.RecognizeAsync(path, _store.Settings.OcrLanguage);
                    Reply(message, new { text });
                    break;
                }

                case "exportText":
                {
                    var title = PayloadString(payload, "title") ?? "备忘录";
                    var text = PayloadString(payload, "text") ?? "";
                    var path = ChooseExportPath(payload, "导出备忘录", title, "文本文件|*.txt", ".txt");
                    if (path is null)
                    {
                        Reply(message, null);
                        break;
                    }

                    ExportService.ExportText(path, title, text);
                    Reply(message, new { path });
                    break;
                }

                case "importFile":
                {
                    var dialog = new OpenFileDialog
                    {
                        Title = "导入备忘录文件",
                        Filter =
                            "所有支持的备忘录文件|*.txt;*.md;*.markdown;*.html;*.htm;*.mht;*.mhtml;*.eml;*.docx;*.doc;*.pdf;*.rtf;*.hdoc;*.xlsx;*.xlsm;*.xls;*.csv;*.pptx;*.pptm;*.ppt;*.wps;*.et;*.dps;*.one;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff" +
                            "|文本 / Markdown|*.txt;*.md;*.markdown" +
                            "|网页 / 邮件|*.html;*.htm;*.mht;*.mhtml;*.eml" +
                            "|Word / WPS 文字|*.docx;*.doc;*.rtf;*.wps" +
                            "|Excel / WPS 表格|*.xlsx;*.xlsm;*.xls;*.csv;*.et" +
                            "|PowerPoint / WPS 演示|*.pptx;*.pptm;*.ppt;*.dps" +
                            "|OneNote|*.one" +
                            "|PDF|*.pdf" +
                            "|图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff" +
                            "|所有文件|*.*",
                        CheckFileExists = true,
                        Multiselect = false,
                    };

                    if (dialog.ShowDialog(this) != true)
                    {
                        Reply(message, null);
                        break;
                    }

                    var result = await _importer.ImportFileAsync(dialog.FileName);
                    Reply(message, ToImportDto(result));
                    break;
                }

                case "importPath":
                case "importData":
                {
                    ImportResult result;
                    if (message.Cmd == "importPath")
                    {
                        var path = PayloadString(payload, "path");
                        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                        {
                            throw new InvalidOperationException("找不到要打开的文件。");
                        }

                        result = await _importer.ImportFileAsync(path);
                    }
                    else
                    {
                        var name = PayloadString(payload, "name") ?? "导入的文件";
                        var base64 = PayloadString(payload, "base64") ?? "";
                        var comma = base64.IndexOf(',');
                        if (comma >= 0)
                        {
                            base64 = base64[(comma + 1)..];
                        }

                        result = await _importer.ImportBytesAsync(name, Convert.FromBase64String(base64), null);
                    }

                    Reply(message, ToImportDto(result));
                    break;
                }

                case "startupFile":
                    Reply(message, GetStartupFile());
                    break;

                case "exportMarkdown":
                {
                    var title = PayloadString(payload, "title") ?? "备忘录";
                    var markdown = PayloadString(payload, "markdown") ?? "";
                    var path = ChooseExportPath(payload, "导出为 Markdown", title, "Markdown 文件|*.md", ".md");
                    if (path is null)
                    {
                        Reply(message, null);
                        break;
                    }

                    ExportService.ExportMarkdown(path, markdown, _store.ImagesDir);
                    Reply(message, new { path });
                    break;
                }

                case "exportHtml":
                {
                    var title = PayloadString(payload, "title") ?? "备忘录";
                    var html = PayloadString(payload, "html") ?? "";
                    var path = ChooseExportPath(payload, "导出为网页", title, "网页文件|*.html", ".html");
                    if (path is null)
                    {
                        Reply(message, null);
                        break;
                    }

                    ExportService.ExportHtml(path, title, html, _store.ImagesDir);
                    Reply(message, new { path });
                    break;
                }

                case "exportDocx":
                {
                    var document = payload.Deserialize<ExportDoc>(JsonDefaults.Options)
                                   ?? throw new InvalidOperationException("导出内容为空。");
                    var path = ChooseExportPath(payload, "导出为 Word 文档", document.Title, "Word 文档|*.docx", ".docx");
                    if (path is null)
                    {
                        Reply(message, null);
                        break;
                    }

                    DocxExport.Export(path, document, _store.ImagesDir);
                    Reply(message, new { path });
                    break;
                }

                case "exportPdf":
                {
                    var title = PayloadString(payload, "title") ?? "备忘录";
                    var path = ChooseExportPath(payload, "导出为 PDF", title, "PDF 文件|*.pdf", ".pdf");
                    if (path is null)
                    {
                        Reply(message, null);
                        break;
                    }

                    var printSettings = Web.CoreWebView2.Environment.CreatePrintSettings();
                    printSettings.PageWidth = 8.27;
                    printSettings.PageHeight = 11.69;
                    printSettings.MarginTop = 0.45;
                    printSettings.MarginBottom = 0.45;
                    printSettings.MarginLeft = 0.45;
                    printSettings.MarginRight = 0.45;
                    printSettings.ShouldPrintBackgrounds = true;
                    printSettings.ShouldPrintHeaderAndFooter = false;

                    var success = await Web.CoreWebView2.PrintToPdfAsync(path, printSettings);
                    if (!success)
                    {
                        throw new InvalidOperationException("PDF 生成失败。");
                    }

                    Reply(message, new { path });
                    break;
                }

                case "openDataFolder":
                    Process.Start(new ProcessStartInfo("explorer.exe", _store.DataDir) { UseShellExecute = true });
                    Reply(message, new { path = _store.DataDir });
                    break;

                case "voiceStart":
                {
                    if (_voice.Start(out var error))
                    {
                        Reply(message, new { listening = true });
                    }
                    else
                    {
                        ReplyError(message, error ?? "无法启动语音识别。");
                    }

                    break;
                }

                case "voiceStop":
                    _voice.Stop();
                    Reply(message, new { listening = false });
                    break;

                case "winMin":
                    WindowState = WindowState.Minimized;
                    Reply(message, null);
                    break;

                case "winMax":
                    ToggleMaximize();
                    Reply(message, null);
                    break;

                case "winClose":
                    Reply(message, null);
                    _ = Dispatcher.BeginInvoke(Close);
                    break;

                default:
                    ReplyError(message, "未知命令：" + message.Cmd);
                    break;
            }
        }
        catch (Exception ex)
        {
            ReplyError(message, ex.Message);
        }
    }

    private object ImportImage(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".webp" or ".tif" or ".tiff"))
        {
            extension = ".png";
        }

        var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}{extension}";
        File.Copy(sourcePath, Path.Combine(_store.ImagesDir, fileName), false);
        return new { file = fileName, url = "https://media.local/" + fileName };
    }

    private object SaveImageData(string dataUrl)
    {
        var comma = dataUrl.IndexOf(',');
        if (comma < 0)
        {
            throw new InvalidOperationException("图片数据格式不正确。");
        }

        var header = dataUrl[..comma];
        var extension = header.Contains("image/jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg"
            : header.Contains("image/webp", StringComparison.OrdinalIgnoreCase) ? ".webp"
            : ".png";
        var bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
        var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}{extension}";
        File.WriteAllBytes(Path.Combine(_store.ImagesDir, fileName), bytes);
        return new { file = fileName, url = "https://media.local/" + fileName };
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        name = name.Trim();
        return string.IsNullOrWhiteSpace(name) ? "备忘录" : name;
    }

    private string? ChooseExportPath(
        JsonElement payload,
        string dialogTitle,
        string fileTitle,
        string filter,
        string extension)
    {
        var explicitPath = PayloadString(payload, "path");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var fullPath = Path.GetFullPath(explicitPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            return fullPath;
        }

        var dialog = new SaveFileDialog
        {
            Title = dialogTitle,
            Filter = filter,
            FileName = SanitizeFileName(string.IsNullOrWhiteSpace(fileTitle) ? "无标题备忘录" : fileTitle.Trim()) + extension,
        };

        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private static string? PayloadString(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static object ToImportDto(ImportResult result)
    {
        return new
        {
            title = result.Title,
            html = result.Html,
            text = result.Text,
            warning = result.Warning,
            sourceName = result.SourceName,
            needsSanitize = result.NeedsSanitize,
            images = result.Images,
        };
    }

    private static string? GetStartupFile()
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("-", StringComparison.Ordinal) || arg.StartsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            if (File.Exists(arg))
            {
                return Path.GetFullPath(arg);
            }
        }

        return null;
    }

    private string EnsureOcrLanguage()
    {
        var languages = OcrService.AvailableLanguages();
        if (languages.Count == 0)
        {
            return _store.Settings.OcrLanguage;
        }

        if (languages.Any(language =>
                string.Equals(language.Tag, _store.Settings.OcrLanguage, StringComparison.OrdinalIgnoreCase)))
        {
            return _store.Settings.OcrLanguage;
        }

        var fallback = languages[0].Tag;
        _store.SaveOcrLanguage(fallback);
        return fallback;
    }

    private object BuildAppearancePayload()
    {
        return new
        {
            themeId = _store.Settings.ThemeId,
            accent = _store.Settings.Accent,
            radiusStyle = _store.Settings.RadiusStyle,
            animations = _store.Settings.Animations,
            topSafeInset = _store.Settings.TopSafeInset,
        };
    }

    private List<string> ListCustomThemes()
    {
        try
        {
            return Directory.GetFiles(_store.AppearanceDir, "*.js")
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList()!;
        }
        catch
        {
            return new List<string>();
        }
    }

    private void ApplyChromeColors(
        Color background,
        Color foreground,
        Color sub,
        Color hover,
        Color pressed,
        Color line)
    {
        Resources["ChromeBg"] = new SolidColorBrush(background);
        Resources["ChromeFg"] = new SolidColorBrush(foreground);
        Resources["ChromeSub"] = new SolidColorBrush(sub);
        Resources["ChromeHover"] = new SolidColorBrush(hover);
        Resources["ChromePressed"] = new SolidColorBrush(pressed);
        Resources["ChromeLine"] = new SolidColorBrush(line);

        RootGrid.Background = new SolidColorBrush(background);
        TitleBar.Background = new SolidColorBrush(background);
        TitleBar.BorderBrush = new SolidColorBrush(line);
        AppTitle.Foreground = new SolidColorBrush(foreground);
        AppSub.Foreground = new SolidColorBrush(sub);
        BtnMin.Foreground = new SolidColorBrush(foreground);
        BtnMax.Foreground = new SolidColorBrush(foreground);
        BtnClose.Foreground = new SolidColorBrush(foreground);
        Background = new SolidColorBrush(background);
        UpdateWebBackground();
    }

    private static Color ParseColor(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        try
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
        catch
        {
            return fallback;
        }
    }

    private static string SanitizeThemeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        name = name.Trim();
        if (!name.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
            name += ".js";
        }
        return string.IsNullOrWhiteSpace(name) ? "theme-" + Guid.NewGuid().ToString("N")[..8] + ".js" : name;
    }

    private static bool PayloadBool(JsonElement payload, string name, bool fallback)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) ? parsed : fallback,
            _ => fallback,
        };
    }

    private static int PayloadInt(JsonElement payload, string name, int fallback)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value))
        {
            return fallback;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }
        return fallback;
    }

    private object BuildDataPayload()
    {        return new
        {
            notes = _store.Notes,
            settings = new
            {
                theme = _theme,
                folders = _store.Settings.Folders,
                hasPasscode = _store.HasPasscode,
                ocrLanguage = EnsureOcrLanguage(),
                ocrLanguages = OcrService.AvailableLanguages(),
                appearance = BuildAppearancePayload(),
                customThemes = ListCustomThemes(),
                appearanceDir = _store.AppearanceDir,
            },
            profile = new
            {
                id = _profiles.ActiveId,
                name = _profiles.Active.Name,
                path = _profiles.Active.Path,
                profiles = _profiles.Profiles,
            },
        };
    }

    private void RemapMediaHosts()
    {
        try
        {
            if (Web.CoreWebView2 is not null)
            {
                Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "media.local", _store.ImagesDir, CoreWebView2HostResourceAccessKind.Allow);
                Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "files.local", _store.FilesDir, CoreWebView2HostResourceAccessKind.Allow);
                Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "appearance.local", _store.AppearanceDir, CoreWebView2HostResourceAccessKind.Allow);
                UpdateWebBackground();
            }
        }
        catch
        {
            // ignored
        }
    }

    private void Reply(BridgeMessage message, object? data)
    {
        Post(new { id = message.Id, ok = true, data });
    }

    private void ReplyError(BridgeMessage message, string error)
    {
        Post(new { id = message.Id, ok = false, error });
    }

    private void Post(object payload)
    {
        if (Dispatcher.CheckAccess())
        {
            Send(payload);
        }
        else
        {
            Dispatcher.BeginInvoke(() => Send(payload));
        }
    }

    private void Send(object payload)
    {
        try
        {
            Web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(payload, JsonDefaults.Options));
        }
        catch
        {
            // ignored
        }
    }

    private void OnVoiceFinalText(string text) =>
        Post(new { @event = "voiceText", data = new { text } });

    private void OnVoiceHypothesis(string text) =>
        Post(new { @event = "voiceHypo", data = new { text } });

    private void OnVoiceListeningChanged(bool listening) =>
        Post(new { @event = "voiceState", data = new { state = listening ? "listening" : "stopped" } });

    private void OnVoiceFailed(string message) =>
        Post(new { @event = "voiceState", data = new { state = "error", message } });

    private void ApplyTheme(string theme)
    {
        _theme = theme == "dark" ? "dark" : "light";
        var dark = _theme == "dark";

        var background = dark ? Color.FromRgb(0x0E, 0x0F, 0x12) : Color.FromRgb(0xF2, 0xF3, 0xF5);
        var foreground = dark ? Color.FromRgb(0xEC, 0xED, 0xEF) : Color.FromRgb(0x1A, 0x1A, 0x1C);
        var sub = dark ? Color.FromRgb(0x8A, 0x8D, 0x94) : Color.FromRgb(0x8E, 0x90, 0x96);
        var hover = dark ? Color.FromRgb(0x25, 0x27, 0x2C) : Color.FromRgb(0xE4, 0xE6, 0xEA);
        var pressed = dark ? Color.FromRgb(0x2F, 0x31, 0x37) : Color.FromRgb(0xD9, 0xDB, 0xE0);
        var line = dark ? Color.FromRgb(0x22, 0x24, 0x28) : Color.FromRgb(0xE6, 0xE8, 0xEC);

        Resources["ChromeBg"] = new SolidColorBrush(background);
        Resources["ChromeFg"] = new SolidColorBrush(foreground);
        Resources["ChromeSub"] = new SolidColorBrush(sub);
        Resources["ChromeHover"] = new SolidColorBrush(hover);
        Resources["ChromePressed"] = new SolidColorBrush(pressed);
        Resources["ChromeLine"] = new SolidColorBrush(line);

        RootGrid.Background = new SolidColorBrush(background);
        TitleBar.Background = new SolidColorBrush(background);
        TitleBar.BorderBrush = new SolidColorBrush(line);
        AppTitle.Foreground = new SolidColorBrush(foreground);
        AppSub.Foreground = new SolidColorBrush(sub);
        BtnMin.Foreground = new SolidColorBrush(foreground);
        BtnMax.Foreground = new SolidColorBrush(foreground);
        BtnClose.Foreground = new SolidColorBrush(foreground);
        Background = new SolidColorBrush(background);
        UpdateWebBackground();
    }

    private void UpdateWebBackground()
    {
        if (Web?.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            var c = ((SolidColorBrush)Background).Color;
            Web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, c.R, c.G, c.B);
        }
        catch
        {
            // ignored
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            _hotKeyHandle = new WindowInteropHelper(this).Handle;
            _hwndSource = HwndSource.FromHwnd(_hotKeyHandle);
            _hwndSource?.AddHook(WndProc);

            var preference = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(_hotKeyHandle, 33, ref preference, sizeof(int));
            RegisterHotKey(_hotKeyHandle, QuickNoteHotKeyId, 0x0001 | 0x0002, 0x4E); // Ctrl+Alt+N
            RegisterHotKey(_hotKeyHandle, MoveWindowHotKeyId, 0x0001 | 0x0002, 0x4D); // Ctrl+Alt+M
        }
        catch
        {
            // ignored
        }
    }

    private void UnregisterQuickNoteHotKey()
    {
        try
        {
            if (_hotKeyHandle != IntPtr.Zero)
            {
                UnregisterHotKey(_hotKeyHandle, QuickNoteHotKeyId);
                UnregisterHotKey(_hotKeyHandle, MoveWindowHotKeyId);
            }

            _hwndSource?.RemoveHook(WndProc);
            _hwndSource = null;
        }
        catch
        {
            // ignored
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmGetMinMaxInfo = 0x0024;
        const int wmHotKey = 0x0312;

        if (msg == wmGetMinMaxInfo)
        {
            ApplyMonitorWorkArea(hwnd, lParam);
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == wmHotKey && wParam.ToInt32() == QuickNoteHotKeyId)
        {
            handled = true;
            ShowQuickNote();
        }
        else if (msg == wmHotKey && wParam.ToInt32() == MoveWindowHotKeyId)
        {
            handled = true;
            StartMoveWindow();
        }

        return IntPtr.Zero;
    }

    private void StartMoveWindow()
    {
        Activate();
        _ = Dispatcher.BeginInvoke(() =>
        {
            try
            {
                DragMove();
            }
            catch
            {
                // ignored
            }
        });
    }

    private void ShowQuickNote()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
        Post(new { @event = "quickNote", data = new { } });
    }

    private void ApplyMonitorWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, 0x00000002); // MONITOR_DEFAULTTONEAREST
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var minMax = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var inset = Math.Clamp(_store.Settings.TopSafeInset, 0, 80);
        if (inset > 0)
        {
            // WindowChrome 最大化时会吃掉 8px 边框，补回来保证实际留白等于用户设置值。
            inset += 8;
        }
        minMax.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
        minMax.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top + inset;
        minMax.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
        minMax.ptMaxSize.Y = Math.Max(200, info.rcWork.Bottom - info.rcWork.Top - inset);
        Marshal.StructureToPtr(minMax, lParam, true);
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        MaxGlyph.Text = maximized ? "\uE923" : "\uE922";
        if (WebHost is not null)
        {
            WebHost.Margin = maximized ? new Thickness(0) : new Thickness(5, 0, 5, 5);
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // ignored
            }
        }
    }

    private void BtnMin_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnMax_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        try
        {
            if (Web.CoreWebView2 is not null)
            {
                var flush = Web.CoreWebView2.ExecuteScriptAsync("window.__flushSave ? window.__flushSave() : null");
                await Task.WhenAny(flush, Task.Delay(1500));
                await Task.Delay(300);
            }
        }
        catch
        {
            // ignored
        }

        _allowClose = true;
        _voice.Stop();
        _recorder.Dispose();
        Close();
    }

    private static string GetFileKind(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".mp3" or ".wav" or ".m4a" or ".aac" or ".ogg" or ".flac" or ".wma" => "audio",
            ".mp4" or ".webm" or ".mov" or ".mkv" or ".avi" or ".wmv" => "video",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff" => "image",
            _ => "file",
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint ptReserved;
        public NativePoint ptMaxSize;
        public NativePoint ptMaxPosition;
        public NativePoint ptMinTrackSize;
        public NativePoint ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public sealed class BridgeMessage
    {
        public long Id { get; set; }
        public string? Cmd { get; set; }
        public JsonElement Payload { get; set; }
    }
}
