[CmdletBinding()]
param(
    [switch]$AuditOnly,
    [ValidateSet('Accept', 'Decline')][string]$CertificateDecisionForTest,
    [ValidateSet('Valid', 'NotTrusted', 'Invalid', 'NotSigned')][string]$SignatureStatusForTest,
    [ValidateSet('Valid', 'NotTrusted', 'Invalid', 'NotSigned')][string]$ReverifiedSignatureStatusForTest,
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
     -not [string]::IsNullOrWhiteSpace($ReverifiedSignatureStatusForTest) -or
     -not [string]::IsNullOrWhiteSpace($SignatureThumbprintForTest) -or
     -not [string]::IsNullOrWhiteSpace($CertificateDecisionForTest)) -and (-not $AuditOnly -or -not $isTest)) {
    throw 'Test verification overrides require AuditOnly and LUMATHERM_PACKAGING_TEST=1.'
}

$releaseRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$checksumPath = Join-Path $releaseRoot 'SHA256SUMS.txt'
$certificatePath = Join-Path $releaseRoot 'LumaTherm.cer'
$packageName = 'LumaTherm-1.0.0-win-x64.msix'
$packagePath = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot $packageName))
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) { throw "Exact sibling package is missing: $packageName" }
if (-not (Get-Item -LiteralPath $packagePath).Name.Equals($packageName, [System.StringComparison]::Ordinal)) { throw "Package filename casing is not exact: $packageName" }
if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) { throw 'Sibling SHA256SUMS.txt is missing.' }
if (-not (Test-Path -LiteralPath $certificatePath -PathType Leaf)) { throw 'Sibling LumaTherm.cer is missing.' }

$checksumLines = @(Get-Content -LiteralPath $checksumPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($checksumLines.Count -eq 0) { throw 'Checksum manifest is empty.' }
$requiredArtifacts = @($packageName, 'LumaTherm-1.0.0-portable-win-x64.zip', 'LumaTherm.cer', 'install.ps1', 'uninstall.ps1')
$seenNames = @{}
foreach ($line in $checksumLines) {
    if ($line -notmatch '^([0-9A-Fa-f]{64}) \*([^\\/]+)$') { throw "Invalid checksum entry: $line" }
    $expected = $matches[1].ToUpperInvariant()
    $name = $matches[2]
    if (-not ($requiredArtifacts -ccontains $name)) { throw "Checksum manifest contains an unexpected or incorrectly-cased artifact: $name" }
    $key = $name.ToUpperInvariant()
    if ($seenNames.ContainsKey($key)) { throw "Ambiguous checksum name: $name" }
    $seenNames[$key] = $true
    $path = Join-Path $releaseRoot $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Checksum artifact is missing: $name" }
    if ((Get-Sha256Hex -Path $path) -ne $expected) { throw "Checksum verification failed: $name" }
}
foreach ($requiredName in $requiredArtifacts) {
    if (-not $seenNames.ContainsKey($requiredName.ToUpperInvariant())) { throw "Checksum manifest does not cover required artifact: $requiredName" }
}
if ($seenNames.Count -ne $requiredArtifacts.Count) { throw 'Checksum manifest must cover exactly the five LumaTherm release artifacts.' }
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
$certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certificatePath)
if ($signatureStatus -ne 'Valid' -and $signatureStatus -ne 'NotTrusted') { throw "MSIX Authenticode signature is not acceptable: $signatureStatus" }
if ([string]::IsNullOrWhiteSpace($signatureThumbprint) -or
    -not $certificate.Thumbprint.Equals($signatureThumbprint, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'MSIX signer certificate does not match sibling LumaTherm.cer.'
}
$trustedPath = 'Cert:\CurrentUser\TrustedPeople\' + $certificate.Thumbprint
$importedCertificate = $false
if ($signatureStatus -eq 'NotTrusted') {
    $events.Add('signatureMatchedUntrusted')
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
        if (-not (Test-Path -LiteralPath $trustedPath)) {
            Import-Certificate -FilePath $certificatePath -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' | Out-Null
            $importedCertificate = $true
        }
        $events.Add('certificateImported')
    }
    if ($AuditOnly) {
        $events.Add('signatureReverificationPlanned')
        $reverifiedStatus = $ReverifiedSignatureStatusForTest
        $reverifiedThumbprint = $SignatureThumbprintForTest
    } else {
        $reverifiedSignature = Get-AuthenticodeSignature -LiteralPath $packagePath
        $reverifiedStatus = [string]$reverifiedSignature.Status
        $reverifiedThumbprint = if ($null -eq $reverifiedSignature.SignerCertificate) { '' } else { $reverifiedSignature.SignerCertificate.Thumbprint }
    }
    if ($reverifiedStatus -ne 'Valid' -or
        [string]::IsNullOrWhiteSpace($reverifiedThumbprint) -or
        -not $certificate.Thumbprint.Equals($reverifiedThumbprint, [System.StringComparison]::OrdinalIgnoreCase)) {
        if ($importedCertificate -and (Test-Path -LiteralPath $trustedPath)) { Remove-Item -LiteralPath $trustedPath -Force }
        throw 'MSIX signature did not become Valid with the matching certificate after trust import.'
    }
    $events.Add('signatureReverified')
} else {
    $events.Add('signatureVerified')
}

if ($AuditOnly) {
    $events.Add('packageInstallPlanned')
    Write-AuditResult -Events $events.ToArray() -PackagePath $packagePath
    exit 0
}

Add-AppxPackage -Path $packagePath
$installedPackages = @(Get-AppxPackage -Name 'LumaTherm' | Where-Object { $_.Name -ceq 'LumaTherm' })
if ($installedPackages.Count -ne 1) { throw 'Install completed but the exact installed LumaTherm package identity is ambiguous.' }
Write-Host "LumaTherm package installed: $($installedPackages[0].PackageFullName)"
Write-Host 'Launch LumaTherm from the Windows Start menu. Autostart remains disabled until you enable it in the app.'
