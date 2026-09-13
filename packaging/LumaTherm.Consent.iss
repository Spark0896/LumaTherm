var
  CertificateImportApproved: Boolean;

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
  CertificateImportApproved := ResolveCertificateImportApproval;
  Result := CertificateImportApproved;
  if not Result then
    Log('LumaTherm certificate import was not explicitly approved; setup stopped before replacement or registration.');
end;
