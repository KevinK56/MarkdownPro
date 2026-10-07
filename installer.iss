; ════════════════════════════════════════════════════════════════════════════
;  Markdown Pro — Inno Setup 6 Installer Script
;  Compiles into: MarkdownPro-Setup-v{MyAppVersion}.exe
;
;  CLI usage:
;    ISCC.exe "/DMyAppVersion=1.0.0" "/DSourceDir=publish\win-x64" "/DOutputDir=installer_output" installer.iss
; ════════════════════════════════════════════════════════════════════════════

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "publish\win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "installer_output"
#endif

#define MyAppName "Markdown Pro"
#define MyAppPublisher "KevinK56 / Star Systems"
#define MyAppURL "https://github.com/KevinK56/MarkdownPro"
#define MyAppExeName "MarkdownPro.exe"
#define MyAppAssocName "Markdown Pro Document"
#define MyAppAssocKey "MarkdownPro.Document"
#define MyAppId "{8F94C64A-4B27-4972-9C77-52E32D68B190}"

[Setup]
AppId={{#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
; Always install per-user to %LOCALAPPDATA%\Programs\Markdown Pro so updates never split between HKLM and HKCU
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Reuse the existing installation folder and tasks when upgrading
UsePreviousAppDir=yes
UsePreviousTasks=yes
; Require user to review and accept the License & Terms of Use before installing
LicenseFile=LICENSE
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=MarkdownPro-Setup-v{#MyAppVersion}
SetupIconFile=MarkdownPro\MarkdownPro\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
CloseApplicationsFilter=*{#MyAppExeName}
RestartApplications=yes
ChangesAssociations=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "associate_md"; Description: "Associate .md, .markdown, .mdown, and .mkd files with {#MyAppName}"; GroupDescription: "File Associations:"; Flags: checkedonce

[InstallDelete]
; Clean up previous application binaries & web assets before laying down the updated files
Type: filesandordirs; Name: "{app}\Assets"
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.exe"
Type: files; Name: "{app}\*.pri"
Type: files; Name: "{app}\*.xbf"
Type: files; Name: "{app}\*.winmd"
Type: files; Name: "{app}\*.json"

[Files]
; Package all WinUI 3 self-contained binaries, XBFs, PRIs, and offline Assets/Web (Marked.js + Mermaid.js)
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "Markdown Pro — Editor & Mermaid v11.4.0 Previewer"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; Comment: "Markdown Pro — Editor & Mermaid v11.4.0 Previewer"

[Registry]
; Register ProgId for Markdown Pro Document
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocKey}"; ValueType: string; ValueName: ""; ValueData: "{#MyAppAssocName}"; Flags: uninsdeletekey; Tasks: associate_md
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocKey}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: associate_md
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocKey}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: associate_md

; Register supported Markdown extensions in OpenWithProgids
Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "{#MyAppAssocKey}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associate_md
Root: HKA; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "{#MyAppAssocKey}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associate_md
Root: HKA; Subkey: "Software\Classes\.mdown\OpenWithProgids"; ValueType: string; ValueName: "{#MyAppAssocKey}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associate_md
Root: HKA; Subkey: "Software\Classes\.mkd\OpenWithProgids"; ValueType: string; ValueName: "{#MyAppAssocKey}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associate_md

; Register Application Capabilities
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""; Flags: uninsdeletekey; Tasks: associate_md
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".markdown"; ValueData: ""; Tasks: associate_md
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".mdown"; ValueData: ""; Tasks: associate_md
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".mkd"; ValueData: ""; Tasks: associate_md
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: associate_md

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  UninstallRegPath = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F94C64A-4B27-4972-9C77-52E32D68B190}_is1';

procedure RemoveUninstallEntryFromRoot(RootKey: Integer);
var
  UninstallExe: String;
  ResultCode: Integer;
begin
  if RegQueryStringValue(RootKey, UninstallRegPath, 'UninstallString', UninstallExe) then
  begin
    UninstallExe := RemoveQuotes(UninstallExe);
    if FileExists(UninstallExe) then
    begin
      if (RootKey = HKLM) or (RootKey = HKLM64) then
      begin
        ShellExec('open', UninstallExe, '/VERYSILENT /NORESTART /SUPPRESSMSGBOXES', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      end
      else
      begin
        Exec(UninstallExe, '/VERYSILENT /NORESTART /SUPPRESSMSGBOXES', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      end;
    end
    else
    begin
      RegDeleteKeyIncludingSubkeys(RootKey, UninstallRegPath);
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    // Ensure any running MarkdownPro.exe instance is closed before replacing/removing files
    Exec('taskkill.exe', '/F /IM MarkdownPro.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // Remove any existing All-Users (HKLM / C:\Program Files\Markdown Pro) or Current-User (HKCU) installation
    // so there is never more than 1 installed instance in Windows Installed Apps
    RemoveUninstallEntryFromRoot(HKLM);
    RemoveUninstallEntryFromRoot(HKLM64);
    RemoveUninstallEntryFromRoot(HKCU);
  end;
end;
