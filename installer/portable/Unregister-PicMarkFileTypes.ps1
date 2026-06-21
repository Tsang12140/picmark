param(
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"

function Remove-RegValue {
    param(
        [string]$Path,
        [string]$Name
    )

    $subPath = $Path
    $subPath = $subPath -replace '^Registry::HKEY_CURRENT_USER\\', ''
    $subPath = $subPath -replace '^HKCU:\\', ''

    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($subPath, $true)
    if ($null -ne $key) {
        try {
            $key.DeleteValue($Name, $false)
        } finally {
            $key.Close()
        }
    }
}

function Remove-RegKey {
    param([string]$Path)

    $subPath = $Path
    $subPath = $subPath -replace '^Registry::HKEY_CURRENT_USER\\', ''
    $subPath = $subPath -replace '^HKCU:\\', ''

    try {
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($subPath, $false)
    } catch {
        # Already gone.
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

$extensions = @(".jpg", ".jpeg", ".png", ".bmp", ".webp")
$classesRoot = "Registry::HKEY_CURRENT_USER\Software\Classes"

Remove-RegValue "HKCU:\Software\RegisteredApplications" "PicMark"
Remove-RegKey "HKCU:\Software\PicMark\Capabilities"
Remove-RegKey "$classesRoot\PicMark.Image"
Remove-RegKey "$classesRoot\Applications\PicMark.exe"

foreach ($ext in $extensions) {
    Remove-RegValue "$classesRoot\$ext\OpenWithProgids" "PicMark.Image"
    Remove-RegKey "$classesRoot\$ext\shell\PicMark"
}

Notify-Shell

Write-Host ""
Write-Host "PicMark image app registration has been removed." -ForegroundColor Green

if (-not $NoPause) {
    Write-Host ""
    Read-Host "Press Enter to close" | Out-Null
}
