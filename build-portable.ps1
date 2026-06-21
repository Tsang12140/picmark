# PicMark 便携版构建脚本
# 用途：编译 Release 版本并生成免安装预览包到 dist\PicMark-portable
# 用法：在 PowerSheel 中运行此脚本，或在项目根目录下手动执行下方命令

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$SlnPath = Join-Path $ProjectRoot "src\PicMark.sln"
$ReleaseOutput = Join-Path $ProjectRoot "src\PicMark\bin\Release"
$PortableDir = Join-Path $ProjectRoot "dist\PicMark-portable"

Write-Host "=== PicMark 构建 ===" -ForegroundColor Cyan

# 1. 先结束可能运行的旧进程
Write-Host "[1/4] 检查运行中的 PicMark..." -ForegroundColor Yellow
try {
    Stop-Process -Name PicMark -Force -ErrorAction SilentlyContinue
    Write-Host "  已结束旧进程" -ForegroundColor Green
} catch {
    Write-Host "  无运行中的进程" -ForegroundColor Gray
}

# 2. 构建 Release
Write-Host "[2/4] 编译 Release..." -ForegroundColor Yellow
$MsBuild = "$env:ProgramFiles (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe"

# 如果 VS2019 BuildTools 不存在，尝试 VS2022
if (-not (Test-Path $MsBuild)) {
    $MsBuild = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
}
if (-not (Test-Path $MsBuild)) {
    $MsBuild = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
}

if (-not (Test-Path $MsBuild)) {
    Write-Host "错误：找不到 MSBuild.exe，请安装 Visual Studio 2022 Community 或 Build Tools" -ForegroundColor Red
    exit 1
}

Write-Host "  使用 MSBuild: $MsBuild" -ForegroundColor Gray

& $MsBuild $SlnPath /p:Configuration=Release /p:Platform="Any CPU" `
    /p:FrameworkPathOverride="C:\tmp\picmark-net472-refs\pkg\build\.NETFramework\v4.7.2" /m

if ($LASTEXITCODE -ne 0) {
    Write-Host "构建失败！退出码: $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "  编译成功" -ForegroundColor Green

# 3. 复制到便携版目录
Write-Host "[3/4] 生成便携版..." -ForegroundColor Yellow

# 确保目录存在
New-Item -ItemType Directory -Force -Path $PortableDir | Out-Null

# 复制必要文件
$FilesToCopy = @(
    "PicMark.exe",
    "PicMark.pdb",
    "SkiaSharp.dll",
    "SkiaSharp.pdb",
    "SkiaSharp.xml",
    "System.Buffers.dll",
    "System.Buffers.xml",
    "System.Memory.dll",
    "System.Memory.xml",
    "System.Numerics.Vectors.dll",
    "System.Numerics.Vectors.xml",
    "System.Runtime.CompilerServices.Unsafe.dll",
    "System.Runtime.CompilerServices.Unsafe.xml"
)

foreach ($file in $FilesToCopy) {
    $src = Join-Path $ReleaseOutput $file
    $dst = Join-Path $PortableDir $file
    if (Test-Path $src) {
        Copy-Item $src $dst -Force
        Write-Host "  $file" -ForegroundColor Gray
    }
}

# 复制 SkiaSharp 原生 DLL（x64/x86/arm64）
foreach ($arch in @("x64", "x86", "arm64")) {
    $archSrc = Join-Path $ReleaseOutput $arch
    $archDst = Join-Path $PortableDir $arch
    if (Test-Path $archSrc) {
        New-Item -ItemType Directory -Force -Path $archDst | Out-Null
        Copy-Item (Join-Path $archSrc "*.dll") $archDst -Force
        Write-Host "  $arch\libSkiaSharp.dll" -ForegroundColor Gray
    }
}

$PortableToolsDir = Join-Path $ProjectRoot "installer\portable"
$LicensePath = Join-Path $ProjectRoot "LICENSE"
if (Test-Path $LicensePath) {
    Copy-Item $LicensePath (Join-Path $PortableDir "LICENSE.txt") -Force
    Write-Host "  LICENSE.txt" -ForegroundColor Gray
}
if (Test-Path $PortableToolsDir) {
    Copy-Item (Join-Path $PortableToolsDir "*") $PortableDir -Force
    Write-Host "  注册打开方式脚本" -ForegroundColor Gray
}

Write-Host "  便携版已就绪" -ForegroundColor Green

# 4. 启动预览
Write-Host "[4/4] 启动预览版..." -ForegroundColor Yellow
$PreviewExe = Join-Path $PortableDir "PicMark.exe"
Start-Process $PreviewExe

Write-Host ""
Write-Host "=== 完成 ===" -ForegroundColor Cyan
Write-Host "便携版路径: $PortableDir" -ForegroundColor White
Write-Host "直接运行: $PreviewExe" -ForegroundColor White
