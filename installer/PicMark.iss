; 见微 PicMark 安装脚本
; 用 Inno Setup (https://jrsoftware.org/isinfo.php) 编译: ISCC.exe PicMark.iss
;
; 运行库策略：
;  - .NET Framework 4.7.2：安装前检测注册表 Release 值，缺失时静默运行微软官方 Web 安装器。
;  - Visual C++ 2015-2022 可再发行组件（SkiaSharp 的 webp 解码原生库需要）：同样检测后静默安装。
;  以上均需要安装时联网（与绝大多数 Windows 应用安装器的前提一致）；若用户机器已满足条件，则不会触发任何下载。

#define MyAppName "见微 PicMark"
#ifndef MyAppVersion
  #define MyAppVersion "0.3.0"
#endif
#define MyAppPublisher "PicMark"
#define MyAppExeName "PicMark.exe"
#define MyBuildOutput "..\src\PicMark\bin\Release"

[Setup]
AppId={{B7E1B6B0-6B2E-4E8B-9C8B-2F6F1F3A9001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\PicMark
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=PicMark-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\PicMark\App.ico
LicenseFile=..\LICENSE.zh-CN.txt
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=admin
WizardStyle=modern
MinVersion=6.1sp1

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："
Name: "contextmenu"; Description: "在图片右键菜单中添加“用见微打开”"; GroupDescription: "附加任务："; Flags: checkedonce
Name: "batchcropmenu"; Description: "在图片和文件夹右键菜单中添加“批量裁切”"; GroupDescription: "附加任务："; Flags: checkedonce
Name: "fileassoc"; Description: "注册为图片打开方式候选应用"; GroupDescription: "附加任务："; Flags: unchecked
; 首次安装时才显示；选择会在 PicMark 第一次启动时写入本地设置。
Name: "autoupdate"; Description: "自动检查更新（推荐）"; GroupDescription: "更新与隐私："; Flags: checkedonce; Check: IsFirstPicMarkInstall
Name: "telemetry"; Description: "发送匿名启动与兼容性信息，帮助改进 PicMark"; GroupDescription: "更新与隐私："; Flags: unchecked; Check: IsFirstPicMarkInstall

[Files]
Source: "{#MyBuildOutput}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyBuildOutput}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyBuildOutput}\x86\*"; DestDir: "{app}\x86"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#MyBuildOutput}\x64\*"; DestDir: "{app}\x64"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#MyBuildOutput}\arm64\*"; DestDir: "{app}\arm64"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\LICENSE.zh-CN.txt"; DestDir: "{app}"; DestName: "许可协议（中文）.txt"; Flags: ignoreversion
; 运行库静默安装包（编译前需放入 redist\ 目录，见本目录 README）
Source: "redist\ndp472-web.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist
Source: "redist\vc_redist.x86.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist
Source: "redist\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\ndp472-web.exe"; Parameters: "/q /norestart"; StatusMsg: "正在安装 .NET Framework 4.7.2（首次安装需要联网，请稍候）..."; Check: NeedsDotNet472; Flags: waituntilterminated skipifdoesntexist
Filename: "{tmp}\vc_redist.x86.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "正在安装 Visual C++ 运行库（32位）..."; Check: NeedsVCRedistX86; Flags: waituntilterminated skipifdoesntexist
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "正在安装 Visual C++ 运行库（64位）..."; Check: NeedsVCRedistX64; Flags: waituntilterminated skipifdoesntexist
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Registry]
; 注册为标准 Windows 应用，让"打开方式"/属性里的应用选择对话框能直接选中见微。
; 全部写入当前用户，避免安装时申请管理员权限，也避免静默接管全局文件关联。
Root: HKCU; Subkey: "Software\Classes\Applications\{#MyAppExeName}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#MyAppName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Applications\{#MyAppExeName}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Applications\{#MyAppExeName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".jpg"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".jpeg"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".png"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".bmp"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".webp"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".picmark"; ValueData: ""

; 标准默认应用注册。Windows 10/11 不允许安装器静默抢占默认应用，
; 但这些项会让“打开方式/默认应用”里直接出现见微，不需要手动浏览 exe。
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#MyAppPublisher}"; ValueData: "Software\{#MyAppPublisher}\Capabilities"; Tasks: fileassoc; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\{#MyAppPublisher}\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#MyAppName}"; Tasks: fileassoc; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\{#MyAppPublisher}\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "轻量本地图片查看和标注工具"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\{#MyAppPublisher}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".jpg"; ValueData: "PicMark.Image"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\{#MyAppPublisher}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".jpeg"; ValueData: "PicMark.Image"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\{#MyAppPublisher}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".png"; ValueData: "PicMark.Image"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\{#MyAppPublisher}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".bmp"; ValueData: "PicMark.Image"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\{#MyAppPublisher}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".webp"; ValueData: "PicMark.Image"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\{#MyAppPublisher}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".picmark"; ValueData: "PicMark.Project"; Tasks: fileassoc

Root: HKCU; Subkey: "Software\Classes\PicMark.Image"; ValueType: string; ValueName: ""; ValueData: "见微 PicMark 图片"; Tasks: fileassoc; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\PicMark.Image"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "见微 PicMark 图片"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\PicMark.Image\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\PicMark.Image\shell\open"; ValueType: string; ValueName: ""; ValueData: "用见微打开"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\PicMark.Image\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc

Root: HKCU; Subkey: "Software\Classes\.jpg\OpenWithProgids"; ValueType: string; ValueName: "PicMark.Image"; ValueData: ""; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.jpeg\OpenWithProgids"; ValueType: string; ValueName: "PicMark.Image"; ValueData: ""; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.png\OpenWithProgids"; ValueType: string; ValueName: "PicMark.Image"; ValueData: ""; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.bmp\OpenWithProgids"; ValueType: string; ValueName: "PicMark.Image"; ValueData: ""; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.webp\OpenWithProgids"; ValueType: string; ValueName: "PicMark.Image"; ValueData: ""; Tasks: fileassoc

Root: HKCU; Subkey: "Software\Classes\PicMark.Project"; ValueType: string; ValueName: ""; ValueData: "见微 PicMark 项目"; Tasks: fileassoc; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\PicMark.Project"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "见微 PicMark 项目"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\PicMark.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\PicMark.Project\shell\open"; ValueType: string; ValueName: ""; ValueData: "打开 PicMark 项目"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\PicMark.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc

Root: HKCU; Subkey: "Software\Classes\.picmark"; ValueType: string; ValueName: ""; ValueData: "PicMark.Project"; Tasks: fileassoc; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\.picmark\OpenWithProgids"; ValueType: string; ValueName: "PicMark.Project"; ValueData: ""; Tasks: fileassoc
Root: HKCR; Subkey: ".picmark\shell\PicMark"; ValueType: string; ValueName: ""; ValueData: "打开 PicMark 项目"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKCR; Subkey: ".picmark\shell\PicMark"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKCR; Subkey: ".picmark\shell\PicMark\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu

; 右键菜单：用见微打开（按扩展名分别注册，避免污染所有文件类型）
Root: HKCR; Subkey: "SystemFileAssociations\.jpg\shell\PicMark"; ValueType: string; ValueName: ""; ValueData: "用见微打开"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.jpg\shell\PicMark"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKCR; Subkey: "SystemFileAssociations\.jpg\shell\PicMark\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu

Root: HKCR; Subkey: "SystemFileAssociations\.jpeg\shell\PicMark"; ValueType: string; ValueName: ""; ValueData: "用见微打开"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.jpeg\shell\PicMark"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKCR; Subkey: "SystemFileAssociations\.jpeg\shell\PicMark\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu

Root: HKCR; Subkey: "SystemFileAssociations\.png\shell\PicMark"; ValueType: string; ValueName: ""; ValueData: "用见微打开"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.png\shell\PicMark"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKCR; Subkey: "SystemFileAssociations\.png\shell\PicMark\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu

Root: HKCR; Subkey: "SystemFileAssociations\.bmp\shell\PicMark"; ValueType: string; ValueName: ""; ValueData: "用见微打开"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.bmp\shell\PicMark"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKCR; Subkey: "SystemFileAssociations\.bmp\shell\PicMark\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu

Root: HKCR; Subkey: "SystemFileAssociations\.webp\shell\PicMark"; ValueType: string; ValueName: ""; ValueData: "用见微打开"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.webp\shell\PicMark"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKCR; Subkey: "SystemFileAssociations\.webp\shell\PicMark\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu

; 右键菜单：批量裁切（当前文件夹）...
Root: HKCR; Subkey: "SystemFileAssociations\.jpg\shell\PicMarkBatchCrop"; ValueType: string; ValueName: ""; ValueData: "批量裁切（当前文件夹）..."; Tasks: batchcropmenu; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.jpg\shell\PicMarkBatchCrop"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: batchcropmenu
Root: HKCR; Subkey: "SystemFileAssociations\.jpg\shell\PicMarkBatchCrop\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" /batchcrop ""%1"""; Tasks: batchcropmenu

Root: HKCR; Subkey: "SystemFileAssociations\.jpeg\shell\PicMarkBatchCrop"; ValueType: string; ValueName: ""; ValueData: "批量裁切（当前文件夹）..."; Tasks: batchcropmenu; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.jpeg\shell\PicMarkBatchCrop"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: batchcropmenu
Root: HKCR; Subkey: "SystemFileAssociations\.jpeg\shell\PicMarkBatchCrop\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" /batchcrop ""%1"""; Tasks: batchcropmenu

Root: HKCR; Subkey: "SystemFileAssociations\.png\shell\PicMarkBatchCrop"; ValueType: string; ValueName: ""; ValueData: "批量裁切（当前文件夹）..."; Tasks: batchcropmenu; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.png\shell\PicMarkBatchCrop"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: batchcropmenu
Root: HKCR; Subkey: "SystemFileAssociations\.png\shell\PicMarkBatchCrop\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" /batchcrop ""%1"""; Tasks: batchcropmenu

Root: HKCR; Subkey: "SystemFileAssociations\.bmp\shell\PicMarkBatchCrop"; ValueType: string; ValueName: ""; ValueData: "批量裁切（当前文件夹）..."; Tasks: batchcropmenu; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.bmp\shell\PicMarkBatchCrop"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: batchcropmenu
Root: HKCR; Subkey: "SystemFileAssociations\.bmp\shell\PicMarkBatchCrop\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" /batchcrop ""%1"""; Tasks: batchcropmenu

Root: HKCR; Subkey: "SystemFileAssociations\.webp\shell\PicMarkBatchCrop"; ValueType: string; ValueName: ""; ValueData: "批量裁切（当前文件夹）..."; Tasks: batchcropmenu; Flags: uninsdeletekey
Root: HKCR; Subkey: "SystemFileAssociations\.webp\shell\PicMarkBatchCrop"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: batchcropmenu
Root: HKCR; Subkey: "SystemFileAssociations\.webp\shell\PicMarkBatchCrop\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" /batchcrop ""%1"""; Tasks: batchcropmenu

; 右键文件夹：批量裁切（当前文件夹）
Root: HKCR; Subkey: "Directory\shell\PicMarkBatchCrop"; ValueType: string; ValueName: ""; ValueData: "批量裁切（当前文件夹）..."; Tasks: batchcropmenu; Flags: uninsdeletekey
Root: HKCR; Subkey: "Directory\shell\PicMarkBatchCrop"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: batchcropmenu
Root: HKCR; Subkey: "Directory\shell\PicMarkBatchCrop\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" /batchcrop ""%1"""; Tasks: batchcropmenu

[Code]
const
  PerUserInstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B7E1B6B0-6B2E-4E8B-9C8B-2F6F1F3A9001}_is1';
  PicMarkInstallOptionsKey = 'Software\PicMark\InstallOptions';

function IsFirstPicMarkInstall(): Boolean;
begin
  Result := not FileExists(ExpandConstant('{localappdata}\PicMark\settings.txt'));
end;

procedure SaveFirstRunOnlineChoices();
begin
  if not IsFirstPicMarkInstall() then
    Exit;

  if WizardIsTaskSelected('autoupdate') then
    RegWriteStringValue(HKCU, PicMarkInstallOptionsKey, 'AutoCheckUpdates', 'true')
  else
    RegWriteStringValue(HKCU, PicMarkInstallOptionsKey, 'AutoCheckUpdates', 'false');

  if WizardIsTaskSelected('telemetry') then
    RegWriteStringValue(HKCU, PicMarkInstallOptionsKey, 'TelemetryConsent', 'Allowed')
  else
    RegWriteStringValue(HKCU, PicMarkInstallOptionsKey, 'TelemetryConsent', 'Denied');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SaveFirstRunOnlineChoices();
end;
function ExtractExecutablePath(const CommandLine: String): String;
var
  EndQuote: Integer;
begin
  Result := CommandLine;
  if (Length(Result) > 0) and (Result[1] = '"') then
  begin
    Delete(Result, 1, 1);
    EndQuote := Pos('"', Result);
    if EndQuote > 0 then
      Result := Copy(Result, 1, EndQuote - 1);
  end
  else
  begin
    EndQuote := Pos(' ', Result);
    if EndQuote > 0 then
      Result := Copy(Result, 1, EndQuote - 1);
  end;
end;

function RemoveLegacyPerUserInstall(): String;
var
  UninstallString, Uninstaller: String;
  ResultCode: Integer;
begin
  Result := '';
  if not RegQueryStringValue(HKCU, PerUserInstallKey, 'UninstallString', UninstallString) then
    Exit;

  Uninstaller := ExtractExecutablePath(UninstallString);
  if not FileExists(Uninstaller) then
  begin
    Result := '检测到旧的当前用户版 PicMark，但找不到其卸载程序。请先在 Geek 卸载程序中卸载该版本，再重新运行本安装包。';
    Exit;
  end;

  if not Exec(Uninstaller, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := '无法自动卸载旧的当前用户版 PicMark。请先在 Geek 卸载程序中卸载该版本，再重新运行本安装包。';
    Exit;
  end;

  if ResultCode <> 0 then
    Result := '旧的当前用户版 PicMark 卸载未完成（错误代码 ' + IntToStr(ResultCode) + '）。请先在 Geek 卸载程序中卸载该版本，再重新运行本安装包。';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  NeedsRestart := False;
  Result := RemoveLegacyPerUserInstall();
end;

function NeedsDotNet472: Boolean;
var
  release: Cardinal;
begin
  Result := True;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', release) then
  begin
    // 461808 = .NET Framework 4.7.2
    if release >= 461808 then
      Result := False;
  end;
end;

function NeedsVCRedistX64: Boolean;
var
  installed: Cardinal;
begin
  Result := True;
  if IsWin64 then
  begin
    if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64', 'Installed', installed) then
    begin
      if installed = 1 then
        Result := False;
    end;
  end
  else
    Result := False; // 32位系统不需要 x64 运行库
end;

function NeedsVCRedistX86: Boolean;
var
  installed: Cardinal;
  keyPath: String;
begin
  Result := True;
  if IsWin64 then
    keyPath := 'SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\X86'
  else
    keyPath := 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X86';

  if RegQueryDWordValue(HKLM, keyPath, 'Installed', installed) then
  begin
    if installed = 1 then
      Result := False;
  end;
end;
