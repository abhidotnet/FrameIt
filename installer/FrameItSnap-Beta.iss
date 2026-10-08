; FrameIt Snap beta installer.
; Compile on Windows, from the repository root:
;   ./build.ps1 -Installer
; Inno Setup 6 must be installed. ISCC.exe writes dist/installer/FrameItSnap-Beta-Setup.exe.
; The files it installs are the framework-dependent publish in artifacts/framework-dependent.
; That folder is build scratch. This script does not read or write dist/portable.
; AppId is unchanged so this upgrades an existing FrameIt Beta install in place.
; That publish needs the .NET 8 Desktop Runtime (Windows x64).

#define MyAppName "FrameIt Snap"
#define MyAppExeName "FrameItSnap.exe"
#define MyAppVersion "0.5.0"
#define SourceDir "..\artifacts\framework-dependent"
#define IconFile "..\src\FrameItSnap\Assets\FrameItSnap.ico"

[Setup]
AppId={{B7E4C1A2-5D38-4F0E-9A6B-2C8F1E7D4A90}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=FrameIt Snap
DefaultDirName={autopf}\FrameIt Snap
DefaultGroupName=FrameIt Snap
DisableProgramGroupPage=yes
OutputDir=..\dist\installer
OutputBaseFilename=FrameItSnap-Beta-Setup
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
PrivilegesRequired=lowest
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[InstallDelete]
Type: files; Name: "{app}\FrameIt.exe"
Type: files; Name: "{app}\FrameIt.dll"
Type: files; Name: "{app}\FrameIt.deps.json"
Type: files; Name: "{app}\FrameIt.runtimeconfig.json"
Type: files; Name: "{group}\FrameIt Beta.lnk"
Type: files; Name: "{autodesktop}\FrameIt Beta.lnk"
Type: files; Name: "{userdesktop}\FrameIt Beta.lnk"
Type: files; Name: "{userprograms}\FrameIt Beta\FrameIt Beta.lnk"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\FrameIt Snap"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\FrameIt Snap"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch FrameIt Snap"; Flags: nowait postinstall skipifsilent

[Code]
function KeyHasDesktop8(RootKey: Integer): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if not RegGetValueNames(RootKey, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
  begin
    Exit;
  end;

  for I := 0 to GetArrayLength(Names) - 1 do
  begin
    if (Length(Names[I]) >= 2) and (CompareText(Copy(Names[I], 1, 2), '8.') = 0) then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function DesktopRuntime8Installed: Boolean;
begin
  Result := KeyHasDesktop8(HKLM) or KeyHasDesktop8(HKLM64);
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if DesktopRuntime8Installed then
  begin
    Exit;
  end;

  if MsgBox('FrameIt Snap needs the .NET 8 Desktop Runtime for Windows x64.' + #13#10 +
            'If it is missing, FrameItSnap.exe will not start.' + #13#10 + #13#10 +
            'Download it from:' + #13#10 +
            'https://dotnet.microsoft.com/en-us/download/dotnet/8.0' + #13#10 +
            'Choose Desktop Runtime, Windows, x64 (not the SDK).' + #13#10 + #13#10 +
            'Open that page now?',
            mbConfirmation, MB_YESNO) = IDYES then
  begin
    ShellExec('open', 'https://dotnet.microsoft.com/en-us/download/dotnet/8.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;
end;
