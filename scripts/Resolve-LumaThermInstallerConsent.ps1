[CmdletBinding()]
param(
    [ValidateSet('Evaluate', 'GenerateInno')][string]$Mode = 'Evaluate',
    [switch]$WizardSilent,
    [ValidateSet('Default', 'Accept', 'Decline')][string]$VisibleDecision = 'Default',
    [AllowEmptyString()][string]$InstallerArgument = '',
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$optInParameter = '/ALLOWCERTIMPORT'
$confirmationArgument = '-ConfirmCertificateImport'
$baseRegistrationArguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{app}\payload\Register-LumaTherm.ps1" -PortableDirectory "{app}\payload" -NonInteractive'

function Resolve-Approval {
    if ($WizardSilent) {
        return [string]::Equals($InstallerArgument, $optInParameter, [StringComparison]::OrdinalIgnoreCase)
    }
    return $VisibleDecision -ceq 'Accept'
}

if ($Mode -eq 'Evaluate') {
    $approved = Resolve-Approval
    [ordered]@{
        approved = $approved
        registrationArguments = if ($approved) { "$baseRegistrationArguments $confirmationArgument" } else { '' }
    } | ConvertTo-Json -Compress | Write-Output
    exit 0
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) { throw 'GenerateInno requires an explicit output path.' }
$fullOutput = [IO.Path]::GetFullPath($OutputPath)
$parent = [IO.Path]::GetDirectoryName($fullOutput)
if ([string]::IsNullOrWhiteSpace($parent) -or -not (Test-Path -LiteralPath $parent -PathType Container)) {
    throw 'The generated Inno include parent directory must already exist.'
}
$pascalBase = $baseRegistrationArguments.Replace("'", "''").Replace('"', '""')
$template = @'
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
    Result := HasExactCommandLineParameter('__OPT_IN__')
  else
    Result := SuppressibleMsgBox(
      'LumaTherm must import the bundled public signing certificate into LocalMachine\TrustedPeople to register Windows lighting identity. No private key is imported. Allow this certificate import?',
      mbConfirmation, MB_YESNO, IDNO) = IDYES;
end;

function BuildRegistrationParameters: String;
begin
  if not CertificateImportApproved then
  begin
    Result := '';
    Exit;
  end;
  Result := ExpandConstant('__BASE_ARGUMENTS__');
  Result := Result + ' __CONFIRM_ARGUMENT__';
end;

function InitializeSetup: Boolean;
begin
  CertificateImportApproved := ResolveCertificateImportApproval;
  Result := CertificateImportApproved;
  if not Result then
    Log('LumaTherm certificate import was not explicitly approved; setup stopped before replacement or registration.');
end;
'@
$content = $template.Replace('__OPT_IN__', $optInParameter.Replace("'", "''"))
$content = $content.Replace('__BASE_ARGUMENTS__', $pascalBase)
$content = $content.Replace('__CONFIRM_ARGUMENT__', $confirmationArgument.Replace("'", "''"))
[IO.File]::WriteAllText($fullOutput, ($content.TrimEnd() + "`r`n"), [Text.UTF8Encoding]::new($false))
[ordered]@{ generated = $fullOutput; optInParameter = $optInParameter; confirmationArgument = $confirmationArgument } |
    ConvertTo-Json -Compress | Write-Output
