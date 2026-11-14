var
  CertificateImportApproved: Boolean;

function HasSameInstallationUser: Boolean;
var
  PowerShell: String;
  Parameters: String;
  UserSid: String;
  Output: TExecOutput;
  ResultCode: Integer;
  Index: Integer;
begin
  Result := False;
  { This read-only token check needs no AppX or native-architecture APIs. }
  PowerShell := ExpandConstant('{syswow64}\WindowsPowerShell\v1.0\powershell.exe');
  Parameters := '-NoProfile -NonInteractive -Command "[Security.Principal.WindowsIdentity]::GetCurrent().User.Value"';
  try
    if not ExecAndCaptureOutput(PowerShell, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode, Output) then
      Exit;
    if (ResultCode <> 0) or Output.Error or (GetArrayLength(Output.StdOut) <> 1) then
      Exit;
    UserSid := Trim(Output.StdOut[0]);
    if (Pos('S-1-', UserSid) <> 1) or (Length(UserSid) < 5) or (Length(UserSid) > 184) then
      Exit;
    for Index := 5 to Length(UserSid) do
      if not (((UserSid[Index] >= '0') and (UserSid[Index] <= '9')) or (UserSid[Index] = '-')) then
        Exit;
    Parameters := '-NoProfile -NonInteractive -Command "$identity = [Security.Principal.WindowsIdentity]::GetCurrent(); $principal = [Security.Principal.WindowsPrincipal]::new($identity); if ($identity.User.Value -ceq ''__USER_SID__'' -and -not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { exit 0 } else { exit 2 }"';
    StringChangeEx(Parameters, '__USER_SID__', UserSid, True);
    if not ExecAsOriginalUser(PowerShell, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Exit;
    Result := ResultCode = 0;
  except
    Log('Unable to verify the original installation account: ' + GetExceptionMessage);
  end;
end;

function HasExactCommandLineParameter(const Wanted: String): Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 1 to ParamCount do
    if CompareText(ParamStr(Index), Wanted) = 0 then
    begin
      Result := True;
      Exit;
    end;
end;

function ResolveCertificateImportApproval: Boolean;
begin
  if WizardSilent then
    Result := HasExactCommandLineParameter('/ALLOWCERTIMPORT')
  else
    Result := SuppressibleMsgBox(
      'LumaTherm must import the bundled public signing certificate into LocalMachine\Root (Trusted Root Certification Authorities) to register Windows lighting identity. No private key is imported. The exact certificate is removed when LumaTherm is uninstalled. Allow this certificate import?',
      mbConfirmation, MB_YESNO, IDNO) = IDYES;
end;

function BuildRegistrationParameters: String;
begin
  if not CertificateImportApproved then
  begin
    Result := '';
    Exit;
  end;
  Result := ExpandConstant('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\payload\Register-LumaTherm.ps1"" -PortableDirectory ""{app}\payload"" -NonInteractive');
  Result := Result + ' -ConfirmCertificateImport';
end;

function InitializeSetup: Boolean;
begin
  Result := HasSameInstallationUser;
  if not Result then
  begin
    Log('Setup stopped before replacement or certificate import because the same unelevated original account could not be verified.');
    SuppressibleMsgBox('LumaTherm requires setup to be started normally from the Windows administrator account that will run the app. Let setup request elevation for that same account. Another administrator''s credentials and an already elevated launch are unsupported because they cannot safely establish the intended lighting user. Sign in with the intended administrator account and double-click setup again. No application files or certificate trust were changed.', mbError, MB_OK, IDOK);
    Exit;
  end;
  CertificateImportApproved := ResolveCertificateImportApproval;
  Result := CertificateImportApproved;
  if not Result then
    Log('LumaTherm certificate import was not explicitly approved; setup stopped before replacement or registration.');
end;
