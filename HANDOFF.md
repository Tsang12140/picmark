# PicMark Handoff Memo

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
