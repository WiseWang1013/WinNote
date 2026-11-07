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
    private readonly NoteStore _store = new();
    private readonly VoiceService _voice = new();
    private readonly ImportService _importer;
    private bool _allowClose;
    private string _theme = "light";

    public MainWindow()
    {
        InitializeComponent();

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
        Closed += (_, _) => _voice.Dispose();
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

        UpdateWebBackground();
        core.Navigate("https://app.local/index.html");
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
                        settings = new { theme = _theme },
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

                    var text = await OcrService.RecognizeAsync(path);
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
                            "所有支持的备忘录文件|*.txt;*.md;*.markdown;*.html;*.htm;*.mht;*.mhtml;*.eml;*.docx;*.doc;*.pdf;*.rtf;*.hdoc;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff" +
                            "|文本 / Markdown|*.txt;*.md;*.markdown" +
                            "|网页 / 邮件|*.html;*.htm;*.mht;*.mhtml;*.eml" +
                            "|Word|*.docx;*.doc;*.rtf" +
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
            var handle = new WindowInteropHelper(this).Handle;
            var preference = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
        }
        catch
        {
            // ignored
        }
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
        Close();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public sealed class BridgeMessage
    {
        public long Id { get; set; }
        public string? Cmd { get; set; }
        public JsonElement Payload { get; set; }
    }
}
