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
UninstallDisplayName={#MyAppName}
#ifexist "..\XboxGuideTray\Assets\Tray.ico"
SetupIconFile=..\XboxGuideTray\Assets\Tray.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start {#MyAppName} when Windows starts"; GroupDescription: "Additional options:"; Flags: unchecked
Name: "installhidhide"; Description: "Install HidHide (stops the power menu from sending controller input to other apps)"; GroupDescription: "Additional options:"; Check: not IsHidHideInstalled; Flags: checkedonce

[Files]
Source: "..\XboxGuideTray\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "ThirdPartyNotices.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; DestName: "README.md"; Flags: ignoreversion
#ifdef HidHideBundled
Source: "redist\{#HidHideFileName}"; DestDir: "{app}\Redist"; DestName: "{#HidHideFileName}"; Flags: ignoreversion
Source: "redist\{#HidHideFileName}"; DestDir: "{tmp}"; DestName: "{#HidHideFileName}"; Flags: deleteafterinstall; Check: ShouldInstallHidHide
#endif

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\Tray.ico"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{app}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "XboxGuideTray"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  HidHideInstallerPath: string;
  HidHideRebootRequired: Boolean;
  ShouldRemoveHidHide: Boolean;
  UninstallPromptDone: Boolean;

function GetHidHideUninstallCommand(): String; forward;

function IsHidHideInstalled(): Boolean;
begin
  Result :=
    RegKeyExists(HKCR, 'Installer\Dependencies\NSS.Drivers.HidHide.x64') or
    RegKeyExists(HKLM, 'SOFTWARE\Nefarius Software Solutions e.U.\Nefarius Software Solutions e.U. HidHide') or
    RegKeyExists(HKLM, 'SOFTWARE\Nefarius Software Solutions\HidHide') or
    (GetHidHideUninstallCommand() <> '');
end;

procedure RemoveHidHideDesktopShortcuts();
var
  ShortcutPaths: TArrayOfString;
  i: Integer;
begin
  SetArrayLength(ShortcutPaths, 6);
  ShortcutPaths[0] := ExpandConstant('{userdesktop}\HidHide Configuration Client.lnk');
  ShortcutPaths[1] := ExpandConstant('{commondesktop}\HidHide Configuration Client.lnk');
  ShortcutPaths[2] := ExpandConstant('{userdesktop}\HidHide.lnk');
  ShortcutPaths[3] := ExpandConstant('{commondesktop}\HidHide.lnk');
  ShortcutPaths[4] := ExpandConstant('{userdesktop}\HidHide Client.lnk');
  ShortcutPaths[5] := ExpandConstant('{commondesktop}\HidHide Client.lnk');

  for i := 0 to GetArrayLength(ShortcutPaths) - 1 do
  begin
    if FileExists(ShortcutPaths[i]) then
      DeleteFile(ShortcutPaths[i]);
  end;
end;

function ShouldInstallHidHide(): Boolean;
begin
  Result := WizardIsTaskSelected('installhidhide') and not IsHidHideInstalled();
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

  if FileExists(ExpandConstant('{app}\Redist\{#HidHideFileName}')) then
  begin
    HidHideInstallerPath := ExpandConstant('{app}\Redist\{#HidHideFileName}');
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
      'Check your internet connection and try again, or install HidHide later from the tray menu.',
      mbError, MB_OK);
    Exit;
  end;

  Launched := Exec(HidHideInstallerPath, '/VERYSILENT /NORESTART /TASKS="!desktopicon"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if not Launched then
    Launched := Exec(HidHideInstallerPath, '/quiet /norestart /TASKS="!desktopicon"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if not Launched then
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
    RemoveHidHideDesktopShortcuts();
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

function GetHidHideUninstallCommand(): String;
var
  UninstallRoots: TArrayOfString;
  SubKeyNames: TArrayOfString;
  SubKeyName: String;
  DisplayName: String;
  UninstallString: String;
  i, j: Integer;
begin
  Result := '';

  if RegQueryStringValue(
    HKLM,
    'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Nefarius Software Solutions e.U. HidHide_is1',
    'UninstallString',
    UninstallString) then
  begin
    Result := UninstallString;
    Exit;
  end;

  SetArrayLength(UninstallRoots, 2);
  UninstallRoots[0] := 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall';
  UninstallRoots[1] := 'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall';

  for i := 0 to 1 do
  begin
    if RegGetSubkeyNames(HKLM, UninstallRoots[i], SubKeyNames) then
    begin
      for j := 0 to GetArrayLength(SubKeyNames) - 1 do
      begin
        SubKeyName := UninstallRoots[i] + '\' + SubKeyNames[j];
        if RegQueryStringValue(HKLM, SubKeyName, 'DisplayName', DisplayName) then
        begin
          if Pos('HidHide', DisplayName) > 0 then
          begin
            if RegQueryStringValue(HKLM, SubKeyName, 'UninstallString', UninstallString) then
            begin
              Result := UninstallString;
              Exit;
            end;
          end;
        end;
      end;
    end;
  end;
end;

function UninstallHidHide(): Boolean;
var
  ResultCode: Integer;
  UninstallCommand: String;
  UninstallExe: String;
  UninstallParams: String;
  QuotePos: Integer;
begin
  Result := False;
  UninstallCommand := GetHidHideUninstallCommand();
  if UninstallCommand = '' then
  begin
    MsgBox(
      'Could not find the HidHide uninstaller in Add/Remove Programs.' + #13#10 +
      'You can remove HidHide manually from Settings > Apps.',
      mbError, MB_OK);
    Exit;
  end;

  if Pos('msiexec', LowerCase(UninstallCommand)) > 0 then
  begin
    UninstallParams := UninstallCommand;
    if Pos('/quiet', LowerCase(UninstallParams)) = 0 then
      UninstallParams := UninstallParams + ' /quiet';
    if Pos('/norestart', LowerCase(UninstallParams)) = 0 then
      UninstallParams := UninstallParams + ' /norestart';

    if Exec('cmd.exe', '/C ' + UninstallParams, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Result := (ResultCode = 0) or (ResultCode = 3010);
  end
  else
  begin
    UninstallExe := UninstallCommand;
    UninstallParams := '';

    if (Length(UninstallExe) > 0) and (UninstallExe[1] = '"') then
    begin
      QuotePos := Pos('"', Copy(UninstallExe, 2, MaxInt));
      if QuotePos > 0 then
      begin
        UninstallExe := Copy(UninstallExe, 2, QuotePos - 1);
        UninstallParams := Trim(Copy(UninstallCommand, QuotePos + 2, MaxInt));
      end;
    end
    else
    begin
      QuotePos := Pos(' ', UninstallCommand);
      if QuotePos > 0 then
      begin
        UninstallExe := Copy(UninstallCommand, 1, QuotePos - 1);
        UninstallParams := Trim(Copy(UninstallCommand, QuotePos + 1, MaxInt));
      end;
    end;

    if Exec(UninstallExe, UninstallParams + ' /VERYSILENT /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Result := (ResultCode = 0) or (ResultCode = 3010);

    if not Result then
    begin
      if Exec(UninstallExe, '/VERYSILENT /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
        Result := (ResultCode = 0) or (ResultCode = 3010);
    end;
  end;

  if not Result then
  begin
    MsgBox(
      'HidHide uninstall did not complete successfully.' + #13#10 +
      'You can remove it manually from Settings > Apps.',
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

function InitializeUninstall(): Boolean;
begin
  ShouldRemoveHidHide := False;
  UninstallPromptDone := False;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usAppMutexCheck) and (not UninstallPromptDone) then
  begin
    UninstallPromptDone := True;

    if IsHidHideInstalled() then
    begin
      if MsgBox(
        'Also uninstall the HidHide driver?' + #13#10#13#10 +
        'HidHide is a separate system component used to block controller input from reaching other apps while the power menu is open.' + #13#10#13#10 +
        'Choose Yes only if you no longer use Xbox Guide Tray or other software that relies on HidHide.',
        mbConfirmation,
        MB_YESNO) = IDYES then
      begin
        ShouldRemoveHidHide := True;
      end;
    end;
  end;

  if (CurUninstallStep = usPostUninstall) and ShouldRemoveHidHide then
  begin
    UninstallHidHide();
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
end;
