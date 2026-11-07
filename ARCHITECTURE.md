# WinNote 架构与开发说明

> 仓库名：**WinNote**，与程序 `WinNote.exe` 同名；界面里的中文名叫「备忘录」。
>
> 在线仓库：<https://github.com/WiseWang1013/WinNote>

## 1. 项目是什么

WinNote 是一个仿华为备忘录的 Windows 桌面备忘录。它把「分类 + 笔记列表 + 编辑区」三栏、便签配色、待办清单、置顶、回收站、深色模式这些交互做进了一个本地应用里，并额外支持：

- 系统中文语音输入（离线）
- 图片文字识别 OCR（离线）
- 手写涂鸦
- 导入各品牌备忘录常见的导出格式
- 导出 TXT / Markdown / Word / PDF / HTML
- 直接打开 HTML、MHTML、EML 邮件文件

所有笔记只保存在本机 `data` 文件夹，不联网、不上传。

## 2. 用什么写的

| 层次 | 技术 |
| --- | --- |
| 桌面外壳 | C# / .NET 10 / WPF（`net10.0-windows10.0.19041.0`） |
| 窗口与标题栏 | WPF `WindowChrome` + DWM 圆角 / 深色模式 |
| 界面 | 原生 HTML5 + CSS3 + JavaScript（没有 Vue / React / 任何前端框架） |
| 内嵌浏览器 | Microsoft Edge WebView2 |
| 应用间通信 | WebView2 `postMessage` / `PostWebMessageAsJson` 自建 Promise 桥 |
| 数据 | JSON（`System.Text.Json`），图片独立存文件 |
| 语音 | `System.Speech`（SAPI，`zh-CN` 识别器） |
| OCR | `Windows.Media.Ocr`（`zh-Hans`） |
| PDF 导入 | `Windows.Data.Pdf` 逐页渲染 + OCR |
| PDF 导出 | WebView2 `PrintToPdfAsync`（Chromium 排版引擎） |
| Word 导入 | `System.IO.Compression` 解 `.docx`，读 `word/document.xml` |
| Word 导出 | 手写 Open XML 包（`[Content_Types].xml` + `document.xml` + `styles.xml` + media） |
| 邮件 | 自己实现的 MIME 解析（multipart / base64 / quoted-printable / RFC 2047） |
| 编码识别 | UTF-8 / UTF-16 / GB18030 自动判断 |
| 发布 | `dotnet publish` 自包含单文件，exe 约 69 MB |

NuGet 依赖只有两个：`Microsoft.Web.WebView2` 和 `System.Speech`。没有数据库、没有云服务、没有第三方 UI 库。

## 3. 用什么模型、什么工具写的

- 需求提出与验收：王智涵
- 编码代理：**DeepSeek Harness（DSH）** 的 AI 编码 Agent
- 主要模型：**DeepSeek Flash**（`deepseek-account/deepseek-flash`，界面显示为 `DeepSeek-V4.1-Flash High`）
- 会话早期（项目起步阶段）也使用过：**GLM-5.3-Flash**（`our-free-model/glm-5.3-flash-free`）
- 主要工具：
  - DSH Web GUI（对话、读写文件、执行命令）
  - PowerShell / `dotnet` CLI（还原、编译、发布）
  - WebView2 远程调试（CDP，用来检查页面状态、截图、验证交互）
  - Python + Pillow（生成应用图标和测试图片）
  - Word COM（测试旧版 `.doc` 导入）
  - Windows 自带语音合成/识别与 OCR（联调）

代码由 AI 代理在 DSH 中生成，用 `dotnet` CLI 编译验证；不是用 Visual Studio 手工敲出来的。模型负责写代码，需求、取舍和最终验收由人决定。

## 4. 总体思路

```
┌──────────────────────────────────────────────┐
│ WPF 窗口（C#）                                │
│  · 无边框标题栏 / 最小化 / 最大化 / 关闭       │
│  · DWM 圆角、主题同步、关闭前保存              │
│  ┌────────────────────────────────────────┐  │
│  │ WebView2                                │  │
│  │  HTML + CSS + 原生 JS 界面              │  │
│  │  三栏布局 / 便签卡片 / 编辑器 / 弹层     │  │
│  └───────────────┬────────────────────────┘  │
└──────────────────┼───────────────────────────┘
                   │ postMessage / ExecuteScriptAsync
┌──────────────────▼───────────────────────────┐
│ C# 服务层                                     │
│  NoteStore / ImportService / ExportService    │
│  OcrService / VoiceService / DocxImport/Export│
│  MediaStore / WebAssets                       │
└──────────────────┬───────────────────────────┘
                   │
        data\notes.json + data\images\
```

### 为什么是 WPF + WebView2？

- WPF 负责原生窗口、DPI、拖拽缩放、标题栏按钮和关闭前保存，这些用原生最稳。
- 界面用 WebView2：便签卡片、圆角、弹层、动画、深色主题、`contenteditable` 富文本编辑，用 HTML/CSS 写比 XAML 快得多，也更容易做得像鸿蒙备忘录。
- 代价是窗口里嵌了一个 Chromium，exe 变大；所以发布成自包含单文件，用户不用装 .NET。

### 前后端怎么通信

前端发：

```js
window.chrome.webview.postMessage({ id, cmd, payload })
```

后端回：

```json
{ "id": 1, "ok": true, "data": { ... } }
```

后端主动推事件：

```json
{ "event": "voiceText", "data": { "text": "..." } }
```

JS 用 `Map<id, {resolve, reject}>` 把回调式消息封装成 `await call('cmd', payload)`，所以界面代码写起来像普通异步函数。

常用命令：

| 命令 | 作用 |
| --- | --- |
| `load` / `saveNotes` | 读取、保存全部笔记 |
| `pickImage` / `saveImage` | 选图、保存剪贴板/手写图片 |
| `ocr` | 识别图片文字 |
| `voiceStart` / `voiceStop` | 开始/结束语音识别 |
| `importFile` / `importPath` / `importData` | 导入文件 |
| `exportText` / `exportMarkdown` / `exportHtml` / `exportDocx` / `exportPdf` | 各种格式导出 |
| `setTheme` | 同步 WPF 标题栏主题 |
| `winMin` / `winMax` / `winClose` | 窗口按钮 |

## 5. 数据模型

`Note` 字段：

```json
{
  "id": "n...",
  "title": "标题",
  "html": "<div>正文 HTML</div>",
  "text": "用于搜索/摘要的纯文本",
  "color": "yellow",
  "category": "work",
  "pinned": false,
  "createdAt": "2026-11-07T20:00:00+08:00",
  "updatedAt": "2026-11-07T20:00:00+08:00",
  "deleted": false,
  "deletedAt": null,
  "images": ["20261107_...png"]
}
```

- 正文存 HTML，保留粗体、图片、待办、表格等结构。
- 同时存一份 `text`，搜索和列表摘要不用每次都解析 HTML。
- 图片存 `data\images`，正文里用 `https://media.local/<file>` 引用；WebView2 通过虚拟主机映射到本地目录。
- 写文件用「临时文件 + `File.Move(overwrite)`」，避免写一半崩溃损坏。
- 删除是软删除；启动时清理 30 天前的回收站。

## 6. 编辑器

- 正文是 `contenteditable` 的 `div`。
- 待办是自定义 DOM：

```html
<div class="todo-item" data-checked="false">
  <span class="todo-box" contenteditable="false"></span>
  <span class="todo-text">买牛奶</span>
</div>
```

  点方框切换 `data-checked`；回车新建下一条；空条退格退出清单。

- 图片是 `figure.img[data-file]`，里面是 `img` + 删除按钮；选中图片后「识图」会拿 `data-file` 去 OCR。
- 粘贴默认只取纯文本，避免从网页带进一堆脏 HTML；粘贴图片会存文件再插入。
- 输入后 550 ms 防抖保存；切换笔记、关闭窗口前强制 `flushSave`。
- 纯文本由 DOM 遍历生成，跳过图片按钮和待办方框，避免把「×」写进摘要。

## 7. 导入管线

统一转成：

```csharp
ImportResult { Title, Html, Text, Images, NeedsSanitize, Warning }
```

流程：

```
文件字节
  → 按扩展名 / 文件头判断类型
  → 对应解析器
  → 图片落盘、src 改写成 media.local
  → 返回 ImportResult
  → JS 侧：raw HTML 走白名单 sanitize；自己的 HTML 走图片包装
  → 新建 Note，打开编辑器，立即持久化
```

| 格式 | 解析方式 |
| --- | --- |
| TXT | 编码嗅探（BOM → UTF-8 严格 → GB18030），按行转 `<div>` |
| Markdown | 自己写的轻量解析：标题、列表、待办、引用、代码块、粗斜体、链接、表格 |
| HTML/MHTML | 读正文，正则重写本地图片路径，JS 侧 DOMParser 白名单清理脚本/样式/iframe |
| EML | MIME 树解析，优先 `text/html`，内嵌 `cid:` 图片落盘，附件列在末尾 |
| DOCX | 解 ZIP，读 `word/document.xml` 的段落/run/表格/drawing，图片按关系 ID 插入 |
| DOC | 先嗅探：HTML/RTF/ZIP 直接走对应解析；OLE 二进制调用本机 Word 另存 HTML 再读 |
| PDF | `Windows.Data.Pdf` 逐页渲染 PNG，再对每页 OCR，页面图和文字一起进笔记 |
| RTF | 解析控制字、`\par`、`\uN`、`\'hh`，转纯文本段落 |
| 图片 | 直接存成图片笔记，提示可再点「识图」 |
| HDOC | 华为私有备份：SQLite 头直接提示；ZIP 则尝试提取其中的 txt/html，并给出警告 |

安全处理：

- 原始 HTML 只保留白名单标签（div/p/b/i/u/ul/ol/li/h1-h6/table/img/a/pre/code/blockquote 等）。
- 去掉 `script/style/link/iframe/object/embed/form/input/button/svg` 等。
- 去掉事件属性、`style`、`id`、`class`（只保留 todo/figure 需要的 class）。
- 图片只允许 `https://media.local`、`https:`、`data:image`；链接只允许 http/https/mailto。

## 8. 导出管线

| 格式 | 做法 |
| --- | --- |
| TXT | 标题 + 纯文本，UTF-8 BOM |
| Markdown | DOM → Markdown；`media.local` 图片复制到 `文件名_assets`，链接改成相对路径 |
| HTML | DOM 克隆 → 去掉编辑按钮 → C# 把图片转 base64 内嵌 → 套一层阅读样式 |
| DOCX | JS 把 DOM 转成 blocks（段落/标题/待办/图片/runs），C# 用 Open XML 写 ZIP 包 |
| PDF | JS 把标题 + 正文放进 `#printRoot`，C# 调 `PrintToPdfAsync`，`@media print` 只打印 `#printRoot` |

DOCX 包结构：

```
[Content_Types].xml
_rels/.rels
docProps/core.xml
docProps/app.xml
word/document.xml
word/styles.xml
word/_rels/document.xml.rels
word/media/image1.png
```

图片用 `wp:inline` + `a:blip r:embed` 放进文档，宽度按 6 英寸上限等比缩放。

PDF 用 Chromium 打印而不是手写 PDF，原因是中文字体嵌入、分页、图片缩放都交给浏览器处理，稳定且不会乱码。

## 9. 语音、OCR、PDF

### 语音

`SpeechRecognitionEngine` + `DictationGrammar`，系统里选第一个 `zh-*` 识别器，`SetInputToDefaultAudioDevice()`，`RecognizeAsync(Multiple)`。识别结果是事件推回 JS，插到当前光标位置；开始/结束通过 `voiceStart` / `voiceStop` 控制。

如果麦克风被隐私设置禁用，会提示去「Windows 设置 → 隐私和安全性 → 麦克风」允许桌面应用。

### OCR

`OcrEngine.TryCreateFromLanguage("zh-Hans")`，找不到就用用户配置语言。图片先按最长边 4000 px 缩放，`SoftwareBitmap` 送进识别，结果按行输出。中文行会做空格清理，避免出现「华 为 备 忘 录」。

### PDF 导入

`PdfDocument.LoadFromFileAsync` → `GetPage` → `RenderToStreamAsync`（PNG）→ 存图片 → 对每页跑 OCR。最多处理 30 页，超过会提示。

## 10. 界面与视觉

- 三栏：分类 236 px / 列表 318 px / 编辑区自适应。
- 10 种便签底色，每张卡片和编辑区共用同一组 CSS 变量（`--note-bg` / `--note-fg`）。
- 深色模式：整套 CSS 变量切换，WPF 标题栏通过 `setTheme` 同步。
- 圆角：`corner-shape: superellipse(4)`，在 Chromium 157 下是连续曲率圆角；不支持时回退到普通 `border-radius`。
- 所有按钮文字 `white-space: nowrap`，可伸缩的标签加 `min-width:0 + ellipsis`，避免竖排或顶出边框。
- 对比度：提高次要文字颜色，降低透明度滥用；便签摘要和日期不会和底色糊在一起。
- 图标：黄色便签纸 + Windows 四格标，Pillow 生成多尺寸 `.ico` 和 PNG。

## 11. 发布

```powershell
dotnet publish .\src\WinNote\WinNote.csproj -c Release -r win-x64 --self-contained true -o .\dist
```

- 自包含：目标机不需要装 .NET。
- 单文件：`PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract`。
- Web 资源作为 EmbeddedResource 打进 exe，启动时释放到 `data\web`，WebView2 用 `app.local` 虚拟主机加载。
- 图片用 `media.local` 虚拟主机映射到 `data\images`。
- 数据在 exe 旁边，便携；换机器时把 exe + data 一起拷走。

## 12. 测试过什么

- 导入：UTF-8 TXT、GBK TXT、Markdown、HTML（含本地图片）、EML（HTML 正文 + cid 图片）、DOCX（标题/粗斜体/表格/图片）、DOC（Word COM 转换）、PDF（渲染 + OCR）。
- 导出：TXT、Markdown（图片复制到 assets）、HTML（base64 内嵌）、DOCX（python-docx 校验结构 + 图片关系）、PDF（再导入验证文字）。
- 交互：新建、编辑、自动保存、关闭前保存、清单打勾、颜色、分类、置顶、搜索、回收站/恢复、深色模式、语音开始/停止、OCR、手写插入。
- 窗口：标题栏拖动/双击最大化、最小化/最大化/关闭、5 px 边框缩放、深色标题栏同步、单实例互斥。

## 13. 已知限制

- `.msg`（Outlook 二进制邮件）不支持，请另存为 `.eml`。
- 华为 `.hdoc` 是加密备份，电脑端无法完整解析；请在手机上先导出 TXT/HTML/PDF。
- `.doc` 依赖本机 Word；没有 Word 会提示另存为 `.docx`。
- PDF 导入最多 30 页，且逐页 OCR 会比较慢。
- Word 导出目前把表格转成文本行，不保留原表格网格。
- 还没有做 PNG 导出、云同步、笔记加密、多人协作。
