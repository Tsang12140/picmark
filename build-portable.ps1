param(
    [switch]$Preview
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$SlnPath = Join-Path $ProjectRoot "src\PicMark.sln"
$ReleaseOutput = Join-Path $ProjectRoot "src\PicMark\bin\Release"
$PortableDir = Join-Path $ProjectRoot "dist\PicMark-portable"

Write-Host "=== PicMark build ===" -ForegroundColor Cyan

Write-Host "[1/4] Closing running PicMark process..." -ForegroundColor Yellow
Stop-Process -Name PicMark -Force -ErrorAction SilentlyContinue

Write-Host "[2/4] Building Release..." -ForegroundColor Yellow
$ProgramFiles = [Environment]::GetFolderPath("ProgramFiles")
$ProgramFilesX86 = [Environment]::GetFolderPath("ProgramFilesX86")
$MsBuildCandidates = @(
    (Join-Path $ProgramFilesX86 "Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe"),
    (Join-Path $ProgramFiles "Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"),
    (Join-Path $ProgramFilesX86 "Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe")
)
$MsBuild = $MsBuildCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $MsBuild) {
    Write-Host "MSBuild.exe was not found. Please install Visual Studio 2022 Community or Build Tools." -ForegroundColor Red
    exit 1
}

Write-Host "  MSBuild: $MsBuild" -ForegroundColor Gray
& $MsBuild $SlnPath /p:Configuration=Release /p:Platform="Any CPU" `
    /p:FrameworkPathOverride="C:\tmp\picmark-net472-refs\pkg\build\.NETFramework\v4.7.2" /m

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed. Exit code: $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "[3/4] Creating portable package folder..." -ForegroundColor Yellow
if (Test-Path $PortableDir) {
    Remove-Item -LiteralPath $PortableDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $PortableDir | Out-Null

$FilesToCopy = @(
    "PicMark.exe",
    "SkiaSharp.dll",
    "System.Buffers.dll",
    "System.Memory.dll",
    "System.Numerics.Vectors.dll",
    "System.Runtime.CompilerServices.Unsafe.dll"
)

foreach ($file in $FilesToCopy) {
    $src = Join-Path $ReleaseOutput $file
    if (Test-Path $src) {
        Copy-Item -LiteralPath $src -Destination (Join-Path $PortableDir $file) -Force
        Write-Host "  $file" -ForegroundColor Gray
    }
}

foreach ($arch in @("x64", "x86", "arm64")) {
    $archSrc = Join-Path $ReleaseOutput $arch
    $archDst = Join-Path $PortableDir $arch
    if (Test-Path $archSrc) {
        New-Item -ItemType Directory -Force -Path $archDst | Out-Null
        Copy-Item -LiteralPath (Join-Path $archSrc "libSkiaSharp.dll") -Destination $archDst -Force -ErrorAction SilentlyContinue
        Write-Host "  $arch\libSkiaSharp.dll" -ForegroundColor Gray
    }
}

$LicensePath = Join-Path $ProjectRoot "LICENSE"
if (Test-Path $LicensePath) {
    Copy-Item -LiteralPath $LicensePath -Destination (Join-Path $PortableDir "LICENSE.txt") -Force
    Write-Host "  LICENSE.txt" -ForegroundColor Gray
}

Write-Host "[4/4] Done." -ForegroundColor Yellow
$PreviewExe = Join-Path $PortableDir "PicMark.exe"
if ($Preview) {
    Start-Process $PreviewExe
}

Write-Host ""
Write-Host "=== Complete ===" -ForegroundColor Cyan
Write-Host "Portable folder: $PortableDir" -ForegroundColor White
Write-Host "Executable: $PreviewExe" -ForegroundColor White
