#define AppVersion "1.1.0"
#ifndef PayloadRoot
  #define PayloadRoot "..\artifacts\release\portable"
#endif

[Setup]
AppId={{9F6F5FEA-A89E-4D1C-9D0C-6C7C9FB5D310}
AppName=LumaTherm
AppVersion=1.1.0
DefaultDirName={autopf}\LumaTherm
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
CloseApplications=yes
CloseApplicationsFilter=LumaTherm.exe
RestartApplications=no
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\payload\app\LumaTherm.exe
OutputBaseFilename=LumaTherm-1.1.0-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
Name: "startmenuicon"; Description: "Create a Start menu shortcut"; Flags: checkedonce

[InstallDelete]
Type: filesandordirs; Name: "{app}\payload"
Type: files; Name: "{autodesktop}\LumaTherm.lnk"
Type: files; Name: "{autoprograms}\LumaTherm.lnk"

[Files]
Source: "{#PayloadRoot}\*"; DestDir: "{app}\payload"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\LumaTherm"; Filename: "{app}\payload\app\LumaTherm.exe"; Tasks: desktopicon
Name: "{autoprograms}\LumaTherm"; Filename: "{app}\payload\app\LumaTherm.exe"; Tasks: startmenuicon

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\payload\Unregister-LumaTherm.ps1"" -Force"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterLumaThermIdentity"

[Code]
#include "LumaTherm.Consent.iss"

var
  RegistrationSucceeded: Boolean;

function BuildNativePowerShellCommandParameters(const PowerShellParameters: String): String;
var
  PowerShell: String;
begin
  PowerShell := ExpandConstant('{sysnative}\WindowsPowerShell\v1.0\powershell.exe');
  Result := '/d /s /c ""' + PowerShell + '" ' + PowerShellParameters + '""';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  Helper: String;
  Parameters: String;
begin
  if CurStep = ssPostInstall then
  begin
    Helper := ExpandConstant('{app}\payload\Register-LumaTherm.ps1');
    Parameters := BuildRegistrationParameters;
    if Parameters = '' then
      RaiseException('Registration consent policy refused to authorize certificate import.');
    Parameters := BuildNativePowerShellCommandParameters(Parameters);
    if not Exec(ExpandConstant('{cmd}'), Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      RaiseException('Registration failed: unable to start the LumaTherm identity helper.');
    if ResultCode <> 0 then
      RaiseException(Format('Registration failed with exit code %d. The installer did not complete successfully.', [ResultCode]));
    RegistrationSucceeded := True;
  end;
end;

function GetCustomSetupExitCode: Integer;
begin
  if RegistrationSucceeded then
    Result := 0
  else
    Result := 1;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  UserData: String;
  Helper: String;
  Parameters: String;
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    UserData := ExpandConstant('{localappdata}\LumaTherm');
    if (not UninstallSilent) and DirExists(UserData) and (MsgBox('Remove LumaTherm user settings and logs?', mbConfirmation, MB_YESNO) = IDYES) then
    begin
      Helper := ExpandConstant('{app}\payload\Unregister-LumaTherm.ps1');
      if not FileExists(Helper) then
        RaiseException('User-data cleanup failed: the guarded LumaTherm helper is missing.');
      Parameters := ExpandConstant('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\payload\Unregister-LumaTherm.ps1"" -Force -RemoveUserData');
      Parameters := BuildNativePowerShellCommandParameters(Parameters);
      if not Exec(ExpandConstant('{cmd}'), Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
        RaiseException('User-data cleanup failed: unable to start the guarded LumaTherm helper.');
      if ResultCode <> 0 then
        RaiseException(Format('User-data cleanup failed with exit code %d. No unsafe recursive deletion was attempted.', [ResultCode]));
    end;
  end;
end;
