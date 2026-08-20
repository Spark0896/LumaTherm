[CmdletBinding()]
param(
    [switch]$AuditOnly,
    [ValidateSet('Accept', 'Decline')][string]$CertificateDecisionForTest,
    [ValidateSet('Valid', 'Invalid', 'NotSigned')][string]$SignatureStatusForTest,
    [string]$SignatureThumbprintForTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)][string]$Path)
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $algorithm = [System.Security.Cryptography.SHA256]::Create()
        try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
        finally { $algorithm.Dispose() }
    } finally { $stream.Dispose() }
}

function Write-AuditResult {
    param([string[]]$Events, [string]$PackagePath)
    [pscustomobject]@{ events = $Events; packagePath = $PackagePath } | ConvertTo-Json -Depth 3 -Compress | Write-Output
}

$isTest = $env:LUMATHERM_PACKAGING_TEST -eq '1'
if ((-not [string]::IsNullOrWhiteSpace($SignatureStatusForTest) -or
     -not [string]::IsNullOrWhiteSpace($SignatureThumbprintForTest) -or
     -not [string]::IsNullOrWhiteSpace($CertificateDecisionForTest)) -and (-not $AuditOnly -or -not $isTest)) {
    throw 'Test verification overrides require AuditOnly and LUMATHERM_PACKAGING_TEST=1.'
}

$releaseRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$checksumPath = Join-Path $releaseRoot 'SHA256SUMS.txt'
$certificatePath = Join-Path $releaseRoot 'LumaTherm.cer'
$packageFiles = @(Get-ChildItem -LiteralPath $releaseRoot -Filter 'LumaTherm-*-win-x64.msix' -File)
if ($packageFiles.Count -ne 1) { throw 'Expected exactly one sibling LumaTherm win-x64 MSIX package.' }
$packagePath = [System.IO.Path]::GetFullPath($packageFiles[0].FullName)
if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) { throw 'Sibling SHA256SUMS.txt is missing.' }
if (-not (Test-Path -LiteralPath $certificatePath -PathType Leaf)) { throw 'Sibling LumaTherm.cer is missing.' }

$checksumLines = @(Get-Content -LiteralPath $checksumPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($checksumLines.Count -eq 0) { throw 'Checksum manifest is empty.' }
$seenNames = @{}
foreach ($line in $checksumLines) {
    if ($line -notmatch '^([0-9A-Fa-f]{64}) \*([^\\/]+)$') { throw "Invalid checksum entry: $line" }
    $expected = $matches[1].ToUpperInvariant()
    $name = $matches[2]
    $key = $name.ToUpperInvariant()
    if ($seenNames.ContainsKey($key)) { throw "Ambiguous checksum name: $name" }
    $seenNames[$key] = $true
    $path = Join-Path $releaseRoot $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Checksum artifact is missing: $name" }
    if ((Get-Sha256Hex -Path $path) -ne $expected) { throw "Checksum verification failed: $name" }
}
foreach ($requiredName in @($packageFiles[0].Name, 'LumaTherm.cer', 'install.ps1', 'uninstall.ps1')) {
    if (-not $seenNames.ContainsKey($requiredName.ToUpperInvariant())) { throw "Checksum manifest does not cover required artifact: $requiredName" }
}
$events = [System.Collections.Generic.List[string]]::new()
$events.Add('checksumVerified')

if ($isTest -and $AuditOnly) {
    $signatureStatus = $SignatureStatusForTest
    $signatureThumbprint = $SignatureThumbprintForTest
} else {
    $signature = Get-AuthenticodeSignature -LiteralPath $packagePath
    $signatureStatus = [string]$signature.Status
    $signatureThumbprint = if ($null -eq $signature.SignerCertificate) { '' } else { $signature.SignerCertificate.Thumbprint }
}
if ($signatureStatus -ne 'Valid') { throw "MSIX Authenticode signature is not valid: $signatureStatus" }
$certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certificatePath)
if ([string]::IsNullOrWhiteSpace($signatureThumbprint) -or
    -not $certificate.Thumbprint.Equals($signatureThumbprint, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'MSIX signer certificate does not match sibling LumaTherm.cer.'
}
$events.Add('signatureVerified')

$trustedPath = 'Cert:\CurrentUser\TrustedPeople\' + $certificate.Thumbprint
$isTrusted = Test-Path -LiteralPath $trustedPath
if (-not $isTrusted) {
    if ($isTest -and $AuditOnly) {
        $accepted = $CertificateDecisionForTest -eq 'Accept'
    } else {
        $answer = Read-Host "Import only certificate $($certificate.Thumbprint) ($($certificate.Subject)) into CurrentUser\TrustedPeople? Type YES"
        $accepted = $answer -ceq 'YES'
    }
    if (-not $accepted) {
        $events.Add('certificateImportDeclined')
        Write-AuditResult -Events $events.ToArray() -PackagePath $packagePath
        exit 3
    }
    $events.Add('certificateImportConfirmed')
    if ($AuditOnly) {
        $events.Add('certificateImportPlanned')
    } else {
        Import-Certificate -FilePath $certificatePath -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' | Out-Null
        $events.Add('certificateImported')
    }
} else {
    $events.Add('certificateAlreadyTrusted')
}

if ($AuditOnly) {
    $events.Add('packageInstallPlanned')
    Write-AuditResult -Events $events.ToArray() -PackagePath $packagePath
    exit 0
}

Add-AppxPackage -Path $packagePath
Write-Host "LumaTherm package installed: $($packageFiles[0].Name)"
Write-Host 'Launch LumaTherm from the Windows Start menu. Autostart remains disabled until you enable it in the app.'
