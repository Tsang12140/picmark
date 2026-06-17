# 运行库安装包（不入库，打包前手动放入）

这三个文件体积较大（共约 40MB），不提交到源码仓库。正式打包安装器前，请下载到本目录：

| 文件名 | 来源 |
|---|---|
| `ndp472-web.exe` | https://go.microsoft.com/fwlink/?LinkId=863262 （.NET Framework 4.7.2 联网安装器，约 1.4MB） |
| `vc_redist.x86.exe` | https://aka.ms/vs/17/release/vc_redist.x86.exe （VC++ 2015-2022 可再发行组件 32位） |
| `vc_redist.x64.exe` | https://aka.ms/vs/17/release/vc_redist.x64.exe （VC++ 2015-2022 可再发行组件 64位） |

PicMark.iss 中这三项 `[Files]` 使用了 `skipifsourcedoesntexist`，缺失时仍可编译安装器，
但生成的安装包将不会自动安装运行库（适合已知目标机器已具备运行库的场景）。
