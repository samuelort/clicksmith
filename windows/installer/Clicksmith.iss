; Clicksmith for Windows - Setup.exe (Inno Setup 6.3+). Built by GitHub Actions:
;   ISCC /DAppVersion=2.5.0-beta.1 /DNumericVersion=2.5.0.0 /DStage=..\build\pkg /DOutputDir=..\build\release Clicksmith.iss
; Per-user install (no administrator rights), same folder as the terminal installer.

#ifndef AppVersion
  #define AppVersion "2.5.0-beta.1"
#endif
#ifndef NumericVersion
  #define NumericVersion "2.5.0.0"
#endif
#ifndef Stage
  #define Stage "..\build\pkg"
#endif
#ifndef OutputDir
  #define OutputDir "..\build\release"
#endif

[Setup]
AppId={{6F1C2C61-5D7A-4F5B-9F64-2B3E1C0A1D2C}
AppName=Clicksmith
AppVersion={#AppVersion}
AppVerName=Clicksmith {#AppVersion}
AppPublisher=Clicksmith contributors
AppPublisherURL=https://github.com/samuelort/clicksmith
AppSupportURL=https://github.com/samuelort/clicksmith/issues
VersionInfoVersion={#NumericVersion}
DefaultDirName={localappdata}\Programs\Clicksmith
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible or arm64
ArchitecturesInstallIn64BitMode=x64compatible or arm64
OutputDir={#OutputDir}
OutputBaseFilename=Clicksmith-Setup-{#AppVersion}
SetupIconFile=..\assets\clicksmith.ico
UninstallDisplayIcon={app}\Clicksmith.exe
UninstallDisplayName=Clicksmith
WizardStyle=modern
WizardSizePercent=100
WizardImageFile=wizard-large.bmp,wizard-large@2x.bmp
WizardSmallImageFile=wizard-small.bmp,wizard-small@2x.bmp
Compression=lzma2/max
SolidCompression=yes
CloseApplications=no
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
StartEngine=Start Clicksmith Engine when I sign in
OpenApp=Open Clicksmith
NeedRuntime=Clicksmith needs the free Microsoft .NET 8 Desktop Runtime (about 55 MB, installed once).%n%nInstall it now with winget? Windows may ask for permission.
RuntimeMissing=.NET 8 Desktop Runtime is still missing. The download page opens now; run Setup again afterwards.

[Tasks]
Name: "startengine"; Description: "{cm:StartEngine}"

[Files]
Source: "{#Stage}\win-x64\app\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion; Check: not IsArm64
Source: "{#Stage}\win-arm64\app\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion; Check: IsArm64
Source: "{#Stage}\win-x64\scripts\verify.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "{#Stage}\win-x64\scripts\rollback.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "{#Stage}\win-x64\scripts\lib\*"; DestDir: "{app}\tools\lib"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Clicksmith"; Filename: "{app}\Clicksmith.exe"; WorkingDir: "{app}"; Comment: "Mouse button remapper"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Clicksmith Engine"; ValueData: """{app}\Clicksmith.Engine.exe"""; Flags: uninsdeletevalue; Tasks: startengine
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Clicksmith"; Flags: uninsdeletevalue dontcreatekey
; Remove the terminal installer's Apps & features entry; this Setup now owns the install.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\Clicksmith"; Flags: deletekey dontcreatekey

[Run]
Filename: "{app}\Clicksmith.Engine.exe"; WorkingDir: "{app}"; Flags: nowait skipifdoesntexist
Filename: "{app}\Clicksmith.exe"; WorkingDir: "{app}"; Description: "{cm:OpenApp}"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM Clicksmith.Engine.exe /F"; Flags: runhidden; RunOnceId: "StopEngine"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM Clicksmith.exe /F"; Flags: runhidden; RunOnceId: "StopApp"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\Programs\Clicksmith.previous"

[Code]
function RuntimeInstalled(): Boolean;
var
  Rec: TFindRec;
  Base: String;
begin
  Result := False;
  Base := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\');
  if FindFirst(Base + '*', Rec) then
  begin
    try
      repeat
        if (Rec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0) and (Length(Rec.Name) > 1) and
           (StrToIntDef(Copy(Rec.Name, 1, Pos('.', Rec.Name) - 1), 0) >= 8) then
          Result := True;
      until Result or not FindNext(Rec);
    finally
      FindClose(Rec);
    end;
  end;
end;

function InitializeSetup(): Boolean;
var
  Code: Integer;
  Winget: String;
begin
  Result := True;
  if RuntimeInstalled() then Exit;
  Winget := ExpandConstant('{localappdata}\Microsoft\WindowsApps\winget.exe');
  if FileExists(Winget) and (MsgBox(CustomMessage('NeedRuntime'), mbConfirmation, MB_YESNO) = IDYES) then
    Exec(Winget, 'install --id Microsoft.DotNet.DesktopRuntime.8 -e --accept-source-agreements --accept-package-agreements', '', SW_SHOW, ewWaitUntilTerminated, Code);
  if not RuntimeInstalled() then
  begin
    MsgBox(CustomMessage('RuntimeMissing'), mbInformation, MB_OK);
    ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0', '', '', SW_SHOWNORMAL, ewNoWait, Code);
    Result := False;
  end;
end;

procedure StopClicksmith();
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM Clicksmith.Engine.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM Clicksmith.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(500);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  App, Prev, Config, Backups: String;
begin
  if CurStep = ssInstall then
  begin
    StopClicksmith();
    { Back up the setup, like every Clicksmith install. }
    Config := ExpandConstant('{userappdata}\Clicksmith\config.json');
    Backups := ExpandConstant('{userappdata}\Clicksmith\Configuration Backups');
    if FileExists(Config) then
    begin
      ForceDirectories(Backups);
      FileCopy(Config, Backups + '\config-' + GetDateTimeString('yyyymmdd"T"hhnnss', #0, #0) + '-setup.json', False);
    end;
    { Keep the current version for rollback (tools\rollback.ps1). }
    App := ExpandConstant('{app}');
    Prev := App + '.previous';
    if DirExists(App) then
    begin
      DelTree(Prev, True, True, True);
      RenameFile(App, Prev);
    end;
  end;
end;
