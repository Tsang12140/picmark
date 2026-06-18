# PicMark Handoff Memo

## 🔵 当前交接任务：看图浏览器模块（2026-06-18 新增，优先看这里）

**要做什么**：实现 `PRD.md` 第 12 节"看图浏览器模块（新功能规划，参考 2345看图王）"。那一节是讨论后定稿的完整大纲（背景目标、设计原则、P0/P1/Out of Scope 功能表、用户流程、技术方案要点、待定问题），**开始写代码前先完整读一遍 PRD 第 12 节，它是唯一的需求来源**，本节只补充 PRD 里没写的、纯工程实现层面的上下文。

**三条不可违反的硬约束**（PRD 12.2 已锁定，不要重新和用户论证）：
1. 浏览窗口和编辑器窗口是**同一个 exe 内的两个 `Window`**，不是两个独立进程/项目。右键"编辑"必须是进程内窗口切换，不能 `Process.Start` 拉起新进程（会有可感知的延迟和窗口闪烁，违背"秒进编辑"的要求）。
2. **不引入新的运行时依赖**（不要为了缩略图加 OpenCV/第三方图像库，WPF 自带的 `BitmapImage.DecodePixelWidth` + 虚拟化容器够用）。
3. 双击图片要**接管系统默认打开方式**（不是只加右键菜单选项）。Win10/11 上应用不能静默抢占默认关联，只能引导用户跳系统设置页手动确认一次；Win7 没有这个限制。**这两个系统的实现路径必须分开处理**，不要假设行为一致。

**已有的代码钩子，照着接，不要重新发明**：
- `src\PicMark\App.xaml.cs` 的 `Application_Startup` → `MainWindow.OpenInitialFiles(e.Args)` 是当前"命令行传入文件路径"的入口（双击文件触发的就是这条路径）。新的浏览/编辑双模式分支大概率要从这里开始改，先读懂 `OpenInitialFiles` 现在做了什么。
- `installer\PicMark.iss` 第 64-93 行：已经注册了 `Applications\PicMark.exe`（让 PicMark 出现在系统"打开方式"候选列表里）和 `.jpg/.jpeg/.png/.bmp/.webp` 各扩展名的右键"用简标打开"菜单。**这是现成的右键菜单基础设施**，要做的是在此之上新增"设为默认程序"的逻辑（写 `HKCR\.ext` 的默认值指向一个 ProgID，Win10/11 还需要弹出 `ms-settings:defaultapps` 引导用户确认），不是从零写注册表注册。
- 新窗口的视觉风格直接照抄 `src\PicMark\BatchCropWindow.xaml`/`.xaml.cs` 这套已验证好看的深色无边框窗口模式（`WindowStyle="None"` + `AllowsTransparency="True"` + 自绘标题栏 + `DragMove()`），不要另起一套视觉语言。

**构建验证**：本仓库下方"构建命令"一节的 MSBuild 命令仍然有效，照着跑。如果你的执行环境没有现成的 VS Build Tools / .NET 4.7.2 参考程序集，需要先用 NuGet 还原（`Microsoft.NETFramework.ReferenceAssemblies.net472`），不要尝试装 `dotnet` CLI 或升级目标框架来"图省事"——Win7 兼容是硬约束，见 PRD 第 9 节。

**当前仓库状态**：`git status` 显示有大量未提交的改动（裁剪工具、批量裁切、水印相关对话框等），这些都是已完成且应保留的工作，**不要用 `git checkout`/`git reset` 清理工作区**，正常在现状之上继续开发。

**开工前必须找用户确认的开放问题**（PRD 12.6，不要自己拍板）：
1. 浏览窗口要不要做独立的文件夹导航（左侧目录树），还是只看"当前文件所在的同一文件夹"
2. P1 的文件操作（重命名/删除/移动）要不要在这一轮做，这是真实文件系统破坏性操作，优先级需要用户明确
3. "设为默认程序"的引导提示，是装机时弹还是首次启动时弹

**完成后**：按 `PRD.md` 现有"进度记录"的格式（编号列表，完成项用 `~~删除线~~` 标注），在末尾追加一条记录当前实现到了哪一步，方便下一个接手的人（不管是 AI 还是人）快速知道状态。

---

本项目路径：`D:\personal\cc\picmark`

GitHub 仓库：`https://github.com/Tsang12140/picmark`

当前主分支：`main`

## 项目定位

PicMark 是一个面向 Windows 的图片标注工具，主打“打开图片后快速画框、画圈、箭头、文字、画笔、马赛克并保存”。界面方向参考 WPS 图片/PixPin，但当前优先做好标注，不急着做抠图、AI 消除、变清晰等重功能。

## 技术栈

- WPF 桌面应用
- .NET Framework 4.7.2
- Visual Studio 2019 Build Tools / MSBuild
- SkiaSharp 用于部分图片格式处理

解决方案文件：

`src\PicMark.sln`

主项目：

`src\PicMark\PicMark.csproj`

## 构建命令

在仓库根目录执行：

```powershell
& 'C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe' src\PicMark.sln /p:Configuration=Release /p:Platform='Any CPU' /p:FrameworkPathOverride='C:\tmp\picmark-net472-refs\pkg\build\.NETFramework\v4.7.2' /m
```

Release 输出：

`src\PicMark\bin\Release\PicMark.exe`

注意：如果构建失败并提示 `PicMark.exe` 被锁定，通常是用户正在运行旧版本。先结束进程再构建：

```powershell
Stop-Process -Name PicMark -Force
```

## 当前重要功能

- 打开/拖入/粘贴图片
- 默认适配窗口缩放
- 右侧标注工具栏
- 画框、画圈、箭头、文字、画笔、马赛克
- 箭头按钮会弹出样式选择，不选则直接使用默认实心箭头
- 文字可直接在图片上输入、拖动、双击编辑
- 保存下拉菜单
- WPS 风格另存为/压缩弹窗
- 保存历史版本缓存，默认 500 MB
- 历史版本入口在顶部
- 自定义深色提示弹窗，已替代原生 `MessageBox.Show`
- 图片画布尺寸已修复为跟随图片本身，不再出现固定大白底

## 最近关键提交

- `92bdca5` Fit canvas frame to image size
- `cecf2a6` Polish dialogs and dark combo boxes
- `a644adc` Restore right annotation panel
- `f61196f` Rework main UI around annotation workspace
- `8821e6e` Add WPS-style save options and workspace polish

## 关键文件

> 注：以下列表是较早一次交接时写的，此后新增了裁剪工具、批量裁切（`BatchCrop*`）、水印相关（`Watermark*`）等文件未补录，**以 `dir src\PicMark` 实际目录为准**，这里只是历史快照。

- `src\PicMark\MainWindow.xaml`：主窗口 UI，顶部功能栏、右侧标注面板、画布区域
- `src\PicMark\MainWindow.xaml.cs`：打开图片、保存、历史版本、工具选择、缩放逻辑
- `src\PicMark\AnnotationCanvas.cs`：标注画布、绘制、选择、拖动、文字编辑、导出渲染
- `src\PicMark\Annotations.cs`：各种标注类型的绘制逻辑
- `src\PicMark\SaveOptionsDialog.xaml`：另存为/压缩弹窗 UI
- `src\PicMark\SaveOptionsDialog.xaml.cs`：另存为/压缩参数逻辑
- `src\PicMark\AppDialog.cs`：自定义深色提示/确认弹窗
- `src\PicMark\HistoryManager.cs`：历史版本缓存
- `src\PicMark\AppSettings.cs`：窗口大小、上次工具、颜色、字号、历史缓存配置

## 已踩过的坑

1. WPF `ComboBox` 只设置 `Foreground/Background` 不够，下拉列表和禁用态会继续用系统浅色模板。保存弹窗里已经重写 `DarkCombo` 和 `DarkComboItem`。
2. 画布外层 `Border` 默认会 Stretch，曾导致图片左上角显示，右侧/下方露出巨大白底。现在 `CanvasFrame` 设置了 `HorizontalAlignment="Left"` 和 `VerticalAlignment="Top"`，`AnnotationCanvas` 会根据 `Image.PixelWidth * Scale` 设置自身尺寸。
3. 当前 Codex 原工作目录曾是 `D:\personal\cc\pic`，但用户希望以后用 `D:\personal\cc\picmark`。新目录已完整复制，后续请以 `D:\personal\cc\picmark` 为准。
4. 构建经常因为正在运行的 `PicMark.exe` 锁住输出文件失败。这不是编译错误，关进程重跑即可。
5. 项目里中文有时在终端输出中显示乱码，但源文件本身是正常中文，优先以编辑器/实际 UI 为准。

## 用户偏好

- UI 尽可能贴近 WPS 图片/PixPin，用户希望“无痛迁移”。
- 顶部功能栏目前用户接受。
- 侧边功能面板必须在右侧，尽量保持之前的右侧面板风格。
- 不喜欢原生 Windows 弹窗和丑的白底/浅色控件。
- 不喜欢添加文字还弹窗；文字应直接在图片上输入和编辑。
- 图片打开后画布必须跟随图片尺寸，不允许出现固定白色大画布。
- 遇到明显 UI 问题，优先找根因，不要只表层改颜色。

## 建议下一步

1. 做一次真实运行验收：打开竖图、横图、小图、大图，确认画布尺寸、缩放、滚动条正常。
2. 继续打磨保存弹窗视觉：路径选择按钮、压缩说明、预览区域可再贴近 WPS。
3. 做“画完圈/方框后按住 1.5 秒自动美化形状”的功能。理解：用户拖出粗略形状并保持鼠标按下，超过阈值后将手绘/不规则形状替换为更规整的矩形、圆形、箭头等。
4. 做更完整的右键菜单：删除、复制、置顶/置底、改颜色、改粗细、编辑文字。
5. Win7 离线测试时注意 .NET Framework 4.7.2 和依赖 DLL 是否齐全。
