param(
    [string]$ExePath = "",
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"

function Set-RegString {
    param(
        [string]$Path,
        [string]$Name,
        [string]$Value
    )

    $subPath = $Path
    $subPath = $subPath -replace '^Registry::HKEY_CURRENT_USER\\', ''
    $subPath = $subPath -replace '^HKCU:\\', ''

    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($subPath)
    if ($null -eq $key) {
        throw "Cannot open registry key: $Path"
    }

    try {
        $key.SetValue($Name, $Value, [Microsoft.Win32.RegistryValueKind]::String)
    } finally {
        $key.Close()
    }
}

function Notify-Shell {
    try {
        Add-Type -Namespace NativeMethods -Name Shell -MemberDefinition @"
[System.Runtime.InteropServices.DllImport("shell32.dll")]
public static extern void SHChangeNotify(int wEventId, uint uFlags, System.IntPtr dwItem1, System.IntPtr dwItem2);
"@
        [NativeMethods.Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
    } catch {
        Write-Host "Shell refresh skipped: $($_.Exception.Message)" -ForegroundColor DarkYellow
    }
}

if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path $PSScriptRoot "PicMark.exe"
}

$ExePath = [IO.Path]::GetFullPath($ExePath)
if (-not (Test-Path -LiteralPath $ExePath)) {
    Write-Host "PicMark.exe was not found: $ExePath" -ForegroundColor Red
    if (-not $NoPause) { Read-Host "Press Enter to close" | Out-Null }
    exit 1
}

$appName = "见微 PicMark"
$description = "见微 PicMark 本地图片查看与标注工具"
$imageProgId = "PicMark.Image"
$projectProgId = "PicMark.Project"
$exeName = [IO.Path]::GetFileName($ExePath)
$openCommand = '"' + $ExePath + '" "%1"'
$quotedExe = '"' + $ExePath + '"'
$imageExtensions = @(".jpg", ".jpeg", ".png", ".bmp", ".webp")
$projectExtensions = @(".picmark")
$classesRoot = "Registry::HKEY_CURRENT_USER\Software\Classes"
$capabilitiesRoot = "HKCU:\Software\PicMark\Capabilities"

Set-RegString "$classesRoot\Applications\$exeName" "FriendlyAppName" $appName
Set-RegString "$classesRoot\Applications\$exeName\DefaultIcon" "" "$ExePath,0"
Set-RegString "$classesRoot\Applications\$exeName\shell\open\command" "" $openCommand

foreach ($ext in $imageExtensions + $projectExtensions) {
    Set-RegString "$classesRoot\Applications\$exeName\SupportedTypes" $ext ""
}

Set-RegString "HKCU:\Software\RegisteredApplications" "PicMark" "Software\PicMark\Capabilities"
Set-RegString $capabilitiesRoot "ApplicationName" $appName
Set-RegString $capabilitiesRoot "ApplicationDescription" $description

foreach ($ext in $imageExtensions) {
    Set-RegString "$capabilitiesRoot\FileAssociations" $ext $imageProgId
}
Set-RegString "$capabilitiesRoot\FileAssociations" ".picmark" $projectProgId

Set-RegString "$classesRoot\$imageProgId" "" "见微 PicMark 图片"
Set-RegString "$classesRoot\$imageProgId" "FriendlyTypeName" "见微 PicMark 图片"
Set-RegString "$classesRoot\$imageProgId\DefaultIcon" "" "$ExePath,0"
Set-RegString "$classesRoot\$imageProgId\shell\open" "" "用见微打开"
Set-RegString "$classesRoot\$imageProgId\shell\open\command" "" $openCommand

foreach ($ext in $imageExtensions) {
    Set-RegString "$classesRoot\$ext\OpenWithProgids" $imageProgId ""
    Set-RegString "$classesRoot\$ext\shell\PicMark" "" "用见微打开"
    Set-RegString "$classesRoot\$ext\shell\PicMark" "Icon" $quotedExe
    Set-RegString "$classesRoot\$ext\shell\PicMark\command" "" $openCommand
}

Set-RegString "$classesRoot\$projectProgId" "" "见微 PicMark 项目"
Set-RegString "$classesRoot\$projectProgId" "FriendlyTypeName" "见微 PicMark 项目"
Set-RegString "$classesRoot\$projectProgId\DefaultIcon" "" "$ExePath,0"
Set-RegString "$classesRoot\$projectProgId\shell\open" "" "打开 PicMark 项目"
Set-RegString "$classesRoot\$projectProgId\shell\open\command" "" $openCommand
Set-RegString "$classesRoot\.picmark" "" $projectProgId
Set-RegString "$classesRoot\.picmark\OpenWithProgids" $projectProgId ""
Set-RegString "$classesRoot\.picmark\shell\PicMark" "" "打开 PicMark 项目"
Set-RegString "$classesRoot\.picmark\shell\PicMark" "Icon" $quotedExe
Set-RegString "$classesRoot\.picmark\shell\PicMark\command" "" $openCommand

Notify-Shell

Write-Host ""
Write-Host "PicMark has been registered as an image and project app for the current user." -ForegroundColor Green
Write-Host "You can now double-click .picmark project files, use Open with > PicMark, or choose PicMark as the default image app in Windows." -ForegroundColor Green

if (-not $NoPause) {
    Write-Host ""
    Read-Host "Press Enter to close" | Out-Null
}
