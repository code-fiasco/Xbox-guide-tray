; Xbox Guide Tray installer (Inno Setup 6)
; Build with: installer\build-installer.ps1

#define MyAppName "Xbox Guide Tray"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Xbox Guide Tray"
#define MyAppExeName "XboxGuideTray.exe"
#define HidHideVersion "1.5.230"
#define HidHideFileName "HidHide_" + HidHideVersion + "_x64.exe"
#define HidHideDownloadUrl "https://github.com/nefarius/HidHide/releases/download/v" + HidHideVersion + ".0/" + HidHideFileName

#ifexist "redist\HidHide_1.5.230_x64.exe"
  #define HidHideBundled
#endif

[Setup]
AppId={{A7C4E2B1-9F3D-4E8A-B6C1-2D5F8A9E0C3B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=XboxGuideTray-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
#ifexist "..\XboxGuideTray\Assets\Tray.ico"
SetupIconFile=..\XboxGuideTray\Assets\Tray.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full"; Description: "Full installation"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "main"; Description: "Xbox Guide Tray application"; Types: full custom; Flags: fixed
Name: "hidhide"; Description: "HidHide driver (recommended for power menu - blocks controller input to background apps)"; Types: full

[Tasks]
Name: "startup"; Description: "Start {#MyAppName} when Windows starts"; GroupDescription: "Additional options:"; Flags: unchecked

[Files]
Source: "..\XboxGuideTray\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: main
Source: "ThirdPartyNotices.txt"; DestDir: "{app}"; Flags: ignoreversion; Components: main
#ifdef HidHideBundled
Source: "redist\{#HidHideFileName}"; DestDir: "{tmp}"; DestName: "{#HidHideFileName}"; Flags: deleteafterinstall; Components: hidhide; Check: ShouldInstallHidHide
#endif

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "XboxGuideTray"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  HidHideInstallerPath: string;
  HidHideRebootRequired: Boolean;

function IsHidHideInstalled(): Boolean;
begin
  Result :=
    RegKeyExists(HKCR, 'Installer\Dependencies\NSS.Drivers.HidHide.x64') or
    RegKeyExists(HKLM, 'SOFTWARE\Nefarius Software Solutions e.U.\Nefarius Software Solutions e.U. HidHide');
end;

function ShouldInstallHidHide(): Boolean;
begin
  Result := IsComponentSelected('hidhide') and not IsHidHideInstalled();
end;

function DownloadHidHideInstaller(): Boolean;
var
  ResultCode: Integer;
  PsCommand: String;
begin
  HidHideInstallerPath := ExpandConstant('{tmp}\{#HidHideFileName}');
  PsCommand :=
    '-NoProfile -ExecutionPolicy Bypass -Command ' +
    '"$ProgressPreference = ''SilentlyContinue''; ' +
    'Invoke-WebRequest -Uri ''{#HidHideDownloadUrl}'' -OutFile ''' + HidHideInstallerPath + ''' -UseBasicParsing"';

  if Exec('powershell.exe', PsCommand, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := (ResultCode = 0) and FileExists(HidHideInstallerPath)
  else
    Result := False;
end;

function EnsureHidHideInstaller(): Boolean;
begin
  HidHideInstallerPath := ExpandConstant('{tmp}\{#HidHideFileName}');

  if FileExists(HidHideInstallerPath) then
  begin
    Result := True;
    Exit;
  end;

  Result := DownloadHidHideInstaller();
end;

function InstallHidHide(): Boolean;
var
  ResultCode: Integer;
  Launched: Boolean;
begin
  Result := False;
  HidHideRebootRequired := False;
  Launched := False;

  if not EnsureHidHideInstaller() then
  begin
    MsgBox(
      'Could not download the HidHide installer.' + #13#10 +
      'Check your internet connection and try again, or install HidHide later using the HidHide release from Nefarius.',
      mbError, MB_OK);
    Exit;
  end;

  Launched := Exec(HidHideInstallerPath, '/quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if not Launched then
    Launched := Exec(HidHideInstallerPath, '/passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
  if not Launched then
    Launched := Exec(HidHideInstallerPath, '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if not Launched then
  begin
    MsgBox('Failed to launch the HidHide installer.', mbError, MB_OK);
    Exit;
  end;

  if (ResultCode = 0) or (ResultCode = 3010) then
  begin
    Result := True;
    if ResultCode = 3010 then
      HidHideRebootRequired := True;
  end
  else
  begin
    MsgBox(
      'HidHide installation did not complete successfully (exit code ' + IntToStr(ResultCode) + ').' + #13#10 +
      'Xbox Guide Tray was installed, but power menu input isolation may not work until HidHide is installed.',
      mbError, MB_OK);
  end;
end;

procedure InitializeWizard();
begin
  HidHideRebootRequired := False;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    if ShouldInstallHidHide() then
    begin
      WizardForm.StatusLabel.Caption := 'Installing HidHide driver...';
      if not InstallHidHide() then
      begin
        Log('HidHide installation failed or was skipped.');
      end;
    end;
  end;

  if CurStep = ssPostInstall then
  begin
    if HidHideRebootRequired then
    begin
      if MsgBox(
        'HidHide was installed. A restart is recommended before using power menu input isolation.' + #13#10#13#10 +
        'Restart now?',
        mbInformation, MB_YESNO) = IDYES then
      begin
        Exec('shutdown.exe', '/r /t 0', '', SW_HIDE, ewNoWait, ResultCode);
      end;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpSelectComponents then
  begin
    if IsHidHideInstalled() then
    begin
      WizardForm.ComponentsList.Checked[1] := False;
      WizardForm.ComponentsList.ItemEnabled[1] := False;
    end;
  end;
end;
