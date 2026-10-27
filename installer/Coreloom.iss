; Inno Setup 6 script for Coreloom.
;
; Built by build/build.ps1 (and the release workflow):
;   ISCC.exe /DAppVersion=1.2.3 /DSourceDir=<publish folder> /DOutputDir=<folder> installer\Coreloom.iss
;
; Design notes
; - Per-user install (no administrator rights needed to install): %LOCALAPPDATA%\Programs\Coreloom.
;   Coreloom.exe itself always runs as administrator (its manifest requires it), so it is started with
;   "shellexec" below: that lets Windows show its normal elevation prompt when Setup is not elevated.
; - No drivers or services are installed. When the user turns on "Start with Windows", Coreloom itself creates
;   a "Coreloom startup" Task Scheduler task (highest privileges, so no UAC prompt at sign-in). Uninstall asks
;   Coreloom to remove it (one elevation prompt, only if the task exists).
; - In-app updates run this installer with /SILENT (or /VERYSILENT when installing in the background)
;   /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /UPDATE=1 [/MINIMIZED=1] as Coreloom exits. Setup is started by
;   the elevated app, so it inherits elevation and relaunches the new version without another prompt (with
;   --minimized when /MINIMIZED=1, so a background update returns to the notification area). If an update can't
;   finish, Setup rolls back and starts the existing version with --update-failed, so Coreloom never disappears and
;   stops retrying that version automatically. The command line is built by UpdateInstallOptions in Coreloom.Core.
; - Installing by hand while Coreloom is running: exit it from the tray menu first (an installer that is not
;   elevated cannot close an elevated app).
; - The icon comes from the approved branding kit in assets\branding (never regenerate it).

#ifndef AppVersion
  #define AppVersion "0.0.0-local"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\release"
#endif
#ifndef RepositoryUrl
  #define RepositoryUrl "https://github.com"
#endif

#define AppName "Coreloom"
#define AppExe "Coreloom.exe"

[Setup]
; Never change AppId: it identifies the installation for upgrades and uninstall.
AppId={{529F2172-DB3E-48B5-8475-4EF89656D21E}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Jerry Ubah
AppPublisherURL={#RepositoryUrl}
AppSupportURL={#RepositoryUrl}/issues
AppUpdatesURL={#RepositoryUrl}/releases
VersionInfoVersion={#Copy(AppVersion, 1, Pos("-", AppVersion + "-") - 1)}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} hardware monitor setup
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
DisableReadyPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=Coreloom-{#AppVersion}-win-x64-Setup
SetupIconFile=..\assets\branding\coreloom.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter={#AppExe},CoreloomDiag.exe
RestartApplications=no
UsedUserAreasWarning=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The whole self-contained publish folder (Coreloom.exe, CoreloomDiag.exe, .NET runtime, libraries).
; portable.txt is excluded: its presence would switch Coreloom to portable mode.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "portable.txt,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Hardware monitor"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Interactive install: offer to start Coreloom.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent shellexec
; In-app update (silent, /UPDATE=1): start the new version automatically.
Filename: "{app}\{#AppExe}"; Parameters: "{code:RelaunchParameters}"; Flags: nowait shellexec; Check: IsUpdateMode

[UninstallRun]
; Remove the optional sign-in task and Coreloom Connect's firewall rule. Both can only be deleted by an elevated
; process, so ask Coreloom to do it (Windows shows one elevation prompt). If the user declines, uninstall continues;
; the task can be deleted in Task Scheduler and the rule ("Coreloom Connect") in Windows Defender Firewall.
Filename: "{app}\{#AppExe}"; Parameters: "--remove-startup-task"; Flags: shellexec waituntilterminated; RunOnceId: "RemoveStartupTask"; Check: StartupTaskExists or ConnectRuleExists

[UninstallDelete]
; Downloaded updates and logs. Settings (%APPDATA%\Coreloom) are kept so a reinstall keeps your layout.
Type: filesandordirs; Name: "{localappdata}\Coreloom\Updates"
Type: filesandordirs; Name: "{localappdata}\Coreloom\Logs"
Type: dirifempty; Name: "{localappdata}\Coreloom"

[Code]
var
  InstallSucceeded: Boolean;

function IsUpdateMode: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

function IsMinimizedRelaunch: Boolean;
begin
  Result := ExpandConstant('{param:MINIMIZED|0}') = '1';
end;

{ Arguments for the Coreloom started after an in-app update. }
function RelaunchParameters(Param: String): String;
begin
  Result := '--updated';
  if IsMinimizedRelaunch then
    Result := Result + ' --minimized';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    InstallSucceeded := True;
end;

{ An in-app update that could not finish (cancelled, files in use, disk full...) has been rolled back. Start the
  version that is still installed so Coreloom keeps monitoring; --update-failed tells it to stop retrying this
  version automatically and to explain what happened. }
procedure DeinitializeSetup;
var
  Exe: String;
  Params: String;
  ErrorCode: Integer;
begin
  if not IsUpdateMode or InstallSucceeded then
    Exit;
  try
    Exe := ExpandConstant('{app}\{#AppExe}');
  except
    Exit;
  end;
  if not FileExists(Exe) then
    Exit;
  Params := '--update-failed';
  if IsMinimizedRelaunch then
    Params := Params + ' --minimized';
  if not ShellExec('', Exe, Params, ExpandConstant('{app}'), SW_SHOWNORMAL, ewNoWait, ErrorCode) then
    Log('Could not restart Coreloom after a failed update (error ' + IntToStr(ErrorCode) + ').');
end;

function ConnectRuleExists: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\netsh.exe'), 'advfirewall firewall show rule name="Coreloom Connect"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
            and (ResultCode = 0);
end;

function StartupTaskExists: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\schtasks.exe'), '/Query /TN "Coreloom startup"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
            and (ResultCode = 0);
end;
