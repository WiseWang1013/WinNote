# WinNote 备忘录 3.0

仿华为 / 苹果 / 小米备忘录的 Windows 桌面备忘录，WPF + WebView2 界面，数据全部保存在本机。

![WinNote 界面](docs/preview.png)

当前版本：**3.0.0**（WinNote 3.0）

## 这个仓库

- GitHub：<https://github.com/WiseWang1013/WinNote>
- Gitee 镜像：<https://gitee.com/WiseWang1013/WinNote>
- 仓库名 **WinNote**，与程序 `WinNote.exe` 同名；界面里的中文名是「备忘录」。
- 详细架构、技术选型、导入导出流程、开发模型说明：[ARCHITECTURE.md](ARCHITECTURE.md)

## 下载

- GitHub 发布页：<https://github.com/WiseWang1013/WinNote/releases/tag/v3.0.0>
- GitHub 直接下载：<https://github.com/WiseWang1013/WinNote/releases/download/v3.0.0/WinNote.exe>
- GitHub 主题包：<https://github.com/WiseWang1013/WinNote/releases/download/v3.0.0/WinNote-appearance.zip>
- Gitee 发布页：<https://gitee.com/WiseWang1013/WinNote/releases/tag/v3.0.0>
- Gitee 直接下载：<https://gitee.com/WiseWang1013/WinNote/releases/download/v3.0.0/WinNote.exe>
- Gitee 主题包：<https://gitee.com/WiseWang1013/WinNote/releases/download/v3.0.0/WinNote-appearance.zip>
- 旧版 2.0：<https://github.com/WiseWang1013/WinNote/releases/tag/v2.0.0>
- 旧版 1.0：<https://github.com/WiseWang1013/WinNote/releases/tag/v1.0.0>

## 3.0 新增

- **完整设置页**：类似浏览器 / Zen 的设置界面，左侧分栏、右侧滚动内容，支持搜索设置。
- **4 套内置主题**：平面、拟物、简约、液态玻璃，全部打包进 exe；每套都有独立的浅色 / 深色配色。
- **主题色与深浅**：蓝、浅蓝、浅绿、薰衣草、玫瑰、琥珀、石墨；浅色 / 深色独立保存。
- **自定义 JS 主题**：导入 `.js` 主题配置到当前数据文件夹的 `appearance` 目录，立即出现在主题列表里；导入入口默认打开 `D:\app\appearance`。
- **圆角风格**：直角（2px）/ 小圆角（8px）/ 圆角（14px）/ 大圆角（22px）/ 苹果连续圆角（18px superellipse）/ 超圆（34px），整体组件跟着变。
- **动画开关与过渡**：面板、卡片、主题切换和设置页都有平滑过渡；可以一键关闭动画。
- **切换笔记不再整页闪**：切换时只更新当前卡片和编辑区，不再重绘整个笔记列表。
- **窗口安全边距**：最大化时按显示器工作区排布，避开任务栏 / AppBar；设置页可调顶部安全边距；`Ctrl+Alt+M` 或设置里的「移动窗口」可在标题栏被悬浮窗挡住时拖动。
- **1.0 / 2.0 / 3.0 数据互通**：直接替换 exe 即可读取旧 `data`；旧 `notes.json` 缺的新字段自动按空值处理。
- 继续保留 2.0 的多数据文件夹、文件夹 / 标签 / 智能文件夹、附件 / 录音、手写、密码锁定、Office / WPS / OneNote 导入、语音输入等。

## 主题文件格式

自定义主题是一个 `.js` 文件，放在当前数据文件夹的 `data\appearance`（导入时自动复制），例如：

```js
WinNote.registerTheme({
  id: 'acrylic',
  name: '亚克力',
  subtitle: 'Acrylic',
  description: '半透明亚克力材质',
  light: {
    bg: '#E8EFF8',
    panel: 'rgba(255,255,255,.62)',
    panel2: 'rgba(248,251,255,.45)',
    text: '#16202C', text2: '#4C5A6D', text3: '#76869A',
    line: 'rgba(255,255,255,.55)', lineStrong: 'rgba(255,255,255,.75)',
    hover: 'rgba(255,255,255,.35)', active: 'rgba(255,255,255,.5)',
    danger: '#D14332', dangerSoft: 'rgba(209,67,50,.1)',
    searchBg: 'rgba(255,255,255,.45)', cardLine: 'rgba(255,255,255,.5)',
    cardShadow: '0 4px 18px rgba(40,70,110,.12)',
    cardShadowHover: '0 12px 30px rgba(40,70,110,.2)',
    shadowLg: '0 24px 60px rgba(40,70,110,.25)',
    toolbarShadow: '0 12px 30px rgba(40,70,110,.2)',
    scrollThumb: 'rgba(40,70,110,.18)',
    appBg: 'linear-gradient(135deg,#e9f2ff,#f4ecff 55%,#e3fbf1)',
    panelSolid: '#E9F2FF', glassBlur: '24px'
  },
  dark: { /* 同样的字段，深色值 */ },
  css: 'body[data-style="acrylic"] .sidebar { backdrop-filter: blur(24px); }'
});
```

内置主题也在 `src/WinNote/wwwroot/themes.js`，可以直接参考。主题文件是本地受信任的 JS，导入前请确认来源。

### 已生成的 10 个额外主题

`D:\app\appearance` 里已经放好 10 个可直接导入的主题：

`亚克力`、`Aero`、`磨砂玻璃`、`极光`、`海盐`、`莫兰迪`、`暗夜霓虹`、`纸张`、`咖啡`、`樱花`。

主题源文件也放在仓库的 `appearance/` 目录，Release 里提供 `WinNote-appearance.zip`。打开设置 → 主题，点「导入 D:\app\appearance」，会把这 10 个主题复制到当前 data 文件夹的 `appearance` 目录并显示在主题列表中。

## 2.0 新增

- **文件夹 / 标签 / 智能文件夹**：自定义文件夹、彩色标签、今天 / 最近 7 天 / 带图片 / 待办 / 已锁定 / 未分类，按修改时间、创建时间、标题排序。
- **待办、表格、链接、附件**：待办清单、3×3 表格、普通链接；可插入音频、视频、任意文件并直接在笔记里播放 / 打开。
- **录音**：调用 Windows 录音设备，录完直接插入音频块。
- **多笔手写**：水笔、毛笔、铅笔、橡皮；笔和橡皮共用粗细滑杆，支持鼠标 / 触控笔压力。
- **本地智能工具（离线）**：生成摘要、生成标题、把列表转成待办、生成结构化脑图；全部在本地规则下完成，不联网、不上传。
- **备忘录密码**：单条笔记 AES-GCM 加密锁定，列表只显示「已锁定的备忘录」，需要密码解锁。
- **数据文件夹（多档案）**：设置里可以新建 / 选择任意数据文件夹，每个档案有独立名称和路径；切换时旧文件夹原样保留，不会移动、覆盖或删除。
- **Office / WPS / OneNote 导入**：Excel、PowerPoint、CSV、Word、WPS 文字 / 表格 / 演示、OneNote `.one` 分区。
- **快速新建**：全局快捷键 `Ctrl + Alt + N`。
- **最大化修复**：最大化时严格贴合显示器工作区，不再被任务栏 / 注册为 AppBar 的顶部栏盖住。
- **图片文字识别语言**：可在设置里选择 Windows 已安装的 OCR 语言包；正文本身支持任意 Unicode 语言。

## 技术栈与开发方式

- 桌面外壳：**C# / .NET 10 / WPF**
- 界面：**原生 HTML5 + CSS3 + JavaScript**，运行在 **WebView2** 里（没有 Vue / React）
- 语音：Windows 自带 **SAPI** 中文识别（离线）
- 图片文字识别：Windows 自带 **Windows.Media.Ocr**（离线，语言取决于系统语言包）
- 导入导出：自写 MIME 邮件解析、Markdown 解析、DOCX / XLSX / PPTX Open XML、PDF 渲染/打印管线
- Office 旧格式与 OneNote：调用本机 Office COM / OneNote 导出，不联网
- 数据：本地 JSON + 图片 / 附件文件，不联网
- 开发工具：**DeepSeek Harness（DSH）** AI 编码代理 + PowerShell + `dotnet` CLI
- 开发模型：主要使用 **DeepSeek Flash**（`deepseek-account/deepseek-flash`，界面标识 `DeepSeek-V4.1-Flash High`）；会话早期（项目起步阶段）也使用过 **GLM-5.3-Flash**（`our-free-model/glm-5.3-flash-free`）
- 总体思路：WPF 负责原生窗口 / DPI / 关闭前保存，WebView2 负责界面与富文本编辑，JS 与 C# 用 `postMessage` 自建 Promise 桥通信；所有数据落在本机。

## 运行

从 Releases 下载 `WinNote.exe`，或直接双击本地：

```
D:\app\WinNote\WinNote.exe
```

- 无需安装 .NET（发布的是自包含单文件 exe）。
- 电脑需要 Microsoft Edge WebView2 Runtime（Windows 11 一般已自带）。
- 默认数据文件夹在 exe 旁边：
  - `data\notes.json`：全部笔记
  - `data\images\`：插入的图片、扫描图、手写涂鸦
  - `data\files\`：音频、视频、附件
  - `data\appearance\`：当前数据文件夹的自定义主题
  - `data\settings.json`：主题、深浅、主题色、圆角、动画、文件夹、OCR 语言、密码哈希等
- 便携使用：把 `WinNote.exe` 和 `data` 文件夹一起拷走即可。

### 兼容 1.0 的 data

直接把 2.0 的 `WinNote.exe` 放进旧版目录（旁边有 `data`）即可。新版会：

- 自动认出旧 `data`，把它登记为「默认数据（data）」；
- 读取旧 `notes.json`，缺少的新字段（标签、附件、锁定）自动按空值处理；
- 不移动、不覆盖旧 `data` 里的笔记和图片。

## 数据文件夹（多档案 / 隐私）

2.0 支持多个数据文件夹，例如：

- `data`：从 1.0 升级上来的旧数据
- `data-私人`：自己的私人笔记
- `data-公开`：给家人 / 同事看的笔记

在「设置 → 数据文件夹」里可以：

- 查看当前档案名称和完整路径；
- **新建数据文件夹**：自动创建 `data-名称`，不会和别的档案重名；
- **选择已有文件夹**：把任意已有数据文件夹接入，原内容不动；
- **重命名当前**：只改显示名称，不改磁盘路径；
- **切换**：切换后旧文件夹原样留在磁盘，随时可以切回来。

档案列表保存在 exe 旁边的 `profiles.json`（如果目录不可写，则放在 `%LOCALAPPDATA%\WinNote\profiles.json`）。不同档案的笔记、图片、附件、设置、密码都相互独立。

> 说明：数据文件夹是「分开存放」而不是「整盘加密」。真正的保密请配合单条笔记的「锁定备忘录」；Windows 账户本身的权限也仍然有效。

## 功能

- 左侧文件夹 / 标签 / 智能文件夹 + 笔记列表，右侧编辑，输入后自动保存
- 笔记颜色（10 种便签色）、自定义文件夹、彩色标签、置顶、搜索、最近删除（30 天）、深色模式
- 待办清单（点方框打勾）、表格、链接、图片、音频、视频、普通文件附件、录音
- 语音输入、图片文字识别、手写涂鸦（水笔 / 毛笔 / 铅笔 / 橡皮）
- 本地智能：摘要、标题、列表转待办、结构化脑图（离线）
- 单条笔记密码锁定（AES-GCM + PBKDF2）
- 多数据文件夹切换
- 全局 `Ctrl + Alt + N` 快速新建
- Apple 式连续曲率圆角（`corner-shape: superellipse`）
- 最大化时按显示器工作区排布，避开任务栏和注册为 AppBar 的顶部栏

## 多语言

- **笔记正文**：UTF-8 / UTF-16 / GB18030 自动识别，中文、英文、法文、俄文、阿拉伯文等都可以正常输入、保存、搜索、导入导出；导入 Office / PDF / 网页时按原文字保留。
- **图片 OCR**：设置里可选 Windows 已安装的识别语言。当前机器若只装了简体中文，下拉框就只显示简体中文；在 Windows「设置 → 时间和语言 → 语言」里装好语言包后会出现更多选项。
- **语音输入**：使用 Windows SAPI 已安装的识别器；当前版本默认中文。
- **界面**：目前界面文案为中文；正文和导入内容不受语言限制。

## 导入

支持拖拽文件到窗口，或点侧边栏「导入文件」，也支持用 exe 打开文件（命令行参数）。

| 格式 | 说明 |
| --- | --- |
| `.txt` | 自动识别 UTF-8 / UTF-16 / GBK/GB18030 |
| `.md` / `.markdown` | 标题、列表、待办、引用、代码块、粗体斜体 |
| `.html` / `.htm` | 取网页正文，脚本/样式会清理，图片会导入 |
| `.mht` / `.mhtml` | 单文件网页 |
| `.eml` | 邮件正文、发件人/收件人/时间、内嵌图片（cid）、附件名列表 |
| `.docx` | Word 正文、标题、粗体/斜体、表格、图片 |
| `.doc` | 老版二进制 Word，调用本机 Word 转换 |
| `.xlsx` / `.xlsm` / `.xltx` | Excel 工作表，保留多工作表、单元格文字和表格结构，附带工作表图片 |
| `.xls` | 老版 Excel，调用本机 Excel 转成 xlsx 再读 |
| `.csv` | 逗号 / 制表符 / 分号分隔的表格 |
| `.pptx` / `.pptm` / `.potx` | PowerPoint 每页文字、演讲者备注、幻灯片图片 |
| `.ppt` | 老版 PowerPoint，调用本机 PowerPoint 转成 pptx 再读 |
| `.wps` | WPS 文字，调用本机 Word 尝试转换；失败时提示另存为 docx |
| `.et` | WPS 表格，调用本机 Excel 尝试转换；失败时提示另存为 xlsx |
| `.dps` | WPS 演示，调用本机 PowerPoint 尝试转换；失败时提示另存为 pptx |
| `.one` | OneNote 分区：通过本机 OneNote 读取页面文字；图片 / 手写暂不导入（建议同时导出 PDF 保留版式） |
| `.rtf` | 基本文本与段落 |
| `.pdf` | 逐页渲染成图片，并自动 OCR 出文字 |
| `.png/.jpg/...` | 作为图片笔记导入，可再点「识图」提取文字 |
| `.hdoc` | 华为私有备份：如果是压缩包会尝试提取文本，否则给出提示 |

说明：

- `.msg` 是 Outlook 的二进制邮件格式，暂不支持；请在邮件客户端另存为 `.eml`。
- `.doc` / `.xls` / `.ppt` / `.wps` / `.et` / `.dps` 依赖本机安装对应的 Office；没有 Office 时会提示先另存为新格式。
- 飞书 / 钉钉 / 语雀等导出的 `.docx` / `.xlsx` / `.pptx` / `.pdf` 可以直接导入。
- 华为 `.hdoc` 是加密备份包，电脑端通常无法直接解析；请在华为备忘录里先导出 TXT / HTML / PDF。
- OneNote 的 `.one` 是分区文件；导入的是**文字**，图片和手写笔迹请用 OneNote 导出 PDF 后再导入。

## 导出

编辑器右上角的下载图标，或「更多 → 导出 / 分享…」。

| 格式 | 说明 |
| --- | --- |
| `.txt` | 纯文本，保留换行 |
| `.md` | Markdown，图片复制到 `文件名_assets` 文件夹 |
| `.docx` | Word 文档，保留标题、粗体/斜体、图片、待办 |
| `.pdf` | 通过 WebView2 打印为 A4 PDF，中文和图片都保留 |
| `.html` | 单文件网页，图片自动转成 base64 内嵌 |

## 快捷键

- `Ctrl + N`：新建备忘录
- `Ctrl + Alt + N`：全局快速新建（窗口在后台也可以）
- `Ctrl + O`：导入文件
- `Ctrl + F`：搜索
- `Ctrl + S`：立即保存
- `Esc`：关闭弹层 / 结束语音

## 已知边界

- 没有账号、云同步、多人实时协作；所有内容只在本机。
- 没有真正的云端大模型：摘要 / 标题 / 待办 / 脑图是本地规则实现；不调用网络。
- 视频摘要、AI 生成 PPT、手写公式计算 / 函数绘图、S Pen 压感协议等未实现。
- OneNote 图片 / 手写不导入；WPS 旧格式依赖本机 Office 转换。
- 多语言界面文案目前是中文；正文和导入内容支持多语言。

## 从源码编译

环境：Windows 10/11、.NET 10 SDK。

```powershell
dotnet publish .\src\WinNote\WinNote.csproj -c Release -r win-x64 --self-contained true -o .\dist
Copy-Item .\dist\WinNote.exe .\WinNote.exe -Force
```

或者直接运行：

```powershell
.\build.ps1
```

## 目录

```
WinNote.exe              发布后的单文件程序
profiles.json            多数据文件夹的档案列表（首次运行生成）
data\                    默认数据文件夹
src\WinNote\             WPF + WebView2 源码
  wwwroot\               界面 HTML / CSS / JS
  Services\              导入导出、OCR、语音、存储、加密、Office、档案
build.ps1                重新发布
README.md
```
