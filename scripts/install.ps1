[CmdletBinding()]
param(
    [switch]$AuditOnly,
    [ValidateSet('Accept', 'Decline')][string]$CertificateDecisionForTest,
    [ValidateSet('Valid', 'NotTrusted', 'Invalid', 'NotSigned')][string]$SignatureStatusForTest,
    [ValidateSet('Valid', 'NotTrusted', 'Invalid', 'NotSigned', 'Exception')][string]$ReverifiedSignatureStatusForTest,
    [string]$SignatureThumbprintForTest,
    [ValidateSet('Success', 'Failure')][string]$InstallOutcomeForTest,
    [switch]$TrustedCertificatePresentForTest,
    [ValidateSet('Admin', 'NonAdmin')][string]$AdministratorStatusForTest
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
    [pscustomobject]@{
        events = $Events
        packagePath = $PackagePath
        certificateStore = 'Cert:\LocalMachine\TrustedPeople'
    } | ConvertTo-Json -Depth 3 -Compress | Write-Output
}

$isTest = $env:LUMATHERM_PACKAGING_TEST -eq '1'
if ((-not [string]::IsNullOrWhiteSpace($SignatureStatusForTest) -or
     -not [string]::IsNullOrWhiteSpace($ReverifiedSignatureStatusForTest) -or
     -not [string]::IsNullOrWhiteSpace($SignatureThumbprintForTest) -or
     -not [string]::IsNullOrWhiteSpace($CertificateDecisionForTest) -or
     -not [string]::IsNullOrWhiteSpace($InstallOutcomeForTest) -or
     $TrustedCertificatePresentForTest -or
     -not [string]::IsNullOrWhiteSpace($AdministratorStatusForTest)) -and (-not $AuditOnly -or -not $isTest)) {
    throw 'Test verification overrides require AuditOnly and LUMATHERM_PACKAGING_TEST=1.'
}

function Test-IsAdministrator {
    if ($isTest -and $AuditOnly) {
        if ([string]::IsNullOrWhiteSpace($AdministratorStatusForTest)) { return $true }
        return $AdministratorStatusForTest -eq 'Admin'
    }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

$releaseRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$checksumPath = Join-Path $releaseRoot 'SHA256SUMS.txt'
$certificatePath = Join-Path $releaseRoot 'LumaTherm.cer'
$packageName = 'LumaTherm-1.0.0-win-x64.msix'
$requiredArtifacts = @($packageName, 'LumaTherm-1.0.0-portable-win-x64.zip', 'LumaTherm.cer', 'install.ps1', 'uninstall.ps1')
$requiredReleaseEntries = @($requiredArtifacts) + @('SHA256SUMS.txt')
$releaseEntries = @(Get-ChildItem -LiteralPath $releaseRoot -Force)
if ($releaseEntries.Count -ne $requiredReleaseEntries.Count -or
    @($releaseEntries | Where-Object { $_.PSIsContainer -or -not ($requiredReleaseEntries -ccontains $_.Name) }).Count -gt 0) {
    throw 'Release directory must contain exactly the five case-exact artifacts and SHA256SUMS.txt.'
}
$packagePath = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot $packageName))
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) { throw "Exact sibling package is missing: $packageName" }
if (-not (Get-Item -LiteralPath $packagePath).Name.Equals($packageName, [System.StringComparison]::Ordinal)) { throw "Package filename casing is not exact: $packageName" }
if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) { throw 'Sibling SHA256SUMS.txt is missing.' }
if (-not (Test-Path -LiteralPath $certificatePath -PathType Leaf)) { throw 'Sibling LumaTherm.cer is missing.' }

$checksumLines = @(Get-Content -LiteralPath $checksumPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($checksumLines.Count -eq 0) { throw 'Checksum manifest is empty.' }
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
$trustedPath = 'Cert:\LocalMachine\TrustedPeople\' + $certificate.Thumbprint
$ownedTrustedCertificate = $false
$installSucceeded = $false
$operationFailure = $null
try {
    if ($signatureStatus -eq 'NotTrusted') {
        $events.Add('signatureMatchedUntrusted')
        if (-not (Test-IsAdministrator)) {
            $events.Add('administratorPreflightFailed')
            throw 'Installing a matching untrusted LumaTherm package requires an elevated Administrator PowerShell to trust the exact certificate in LocalMachine\TrustedPeople.'
        }
        $events.Add('administratorPreflightPassed')
        if ($isTest -and $AuditOnly) {
            $accepted = $CertificateDecisionForTest -eq 'Accept'
        } else {
            $answer = Read-Host "Import only certificate $($certificate.Thumbprint) ($($certificate.Subject)) into LocalMachine\TrustedPeople? Type YES"
            $accepted = $answer -ceq 'YES'
        }
        if (-not $accepted) {
            $events.Add('certificateImportDeclined')
            Write-AuditResult -Events $events.ToArray() -PackagePath $packagePath
            exit 3
        }
        $events.Add('certificateImportConfirmed')
        if ($AuditOnly) {
            if ($TrustedCertificatePresentForTest) {
                $events.Add('certificateAlreadyPresent')
            } else {
                $ownedTrustedCertificate = $true
                $events.Add('certificateImportPlanned')
            }
        } elseif (Test-Path -LiteralPath $trustedPath) {
            $events.Add('certificateAlreadyPresent')
        } else {
            $ownedTrustedCertificate = $true
            Import-Certificate -FilePath $certificatePath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
            $events.Add('certificateImported')
        }

        if ($AuditOnly) {
            $events.Add('signatureReverificationPlanned')
            if ($ReverifiedSignatureStatusForTest -eq 'Exception') {
                $events.Add('signatureReverificationFailed')
                throw 'Simulated Authenticode re-verification exception.'
            }
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
            if ($AuditOnly) { $events.Add('signatureReverificationFailed') }
            throw 'MSIX signature did not become Valid with the matching certificate after trust import.'
        }
        $events.Add('signatureReverified')
    } else {
        $events.Add('signatureVerified')
    }

    if ($AuditOnly) {
        $events.Add('packageInstallPlanned')
        if ($InstallOutcomeForTest -eq 'Failure') {
            $events.Add('packageInstallFailed')
            throw 'Simulated Add-AppxPackage failure.'
        }
        $installSucceeded = $true
    } else {
        Add-AppxPackage -Path $packagePath
        $installSucceeded = $true
        $installedPackages = @(Get-AppxPackage -Name 'LumaTherm' | Where-Object { $_.Name -ceq 'LumaTherm' })
        if ($installedPackages.Count -ne 1) { throw 'Install completed but the exact installed LumaTherm package identity is ambiguous.' }
    }
} catch {
    $operationFailure = $_
} finally {
    if ($ownedTrustedCertificate -and -not $installSucceeded) {
        if ($AuditOnly) {
            $events.Add('certificateTrustCleanupPlanned')
        } elseif (Test-Path -LiteralPath $trustedPath) {
            Remove-Item -LiteralPath $trustedPath -Force
        }
    }
}

if ($null -ne $operationFailure) {
    if ($AuditOnly) { Write-AuditResult -Events $events.ToArray() -PackagePath $packagePath }
    throw $operationFailure
}
if ($AuditOnly) {
    if ($ownedTrustedCertificate) { $events.Add('certificateTrustRetentionPlanned') }
    Write-AuditResult -Events $events.ToArray() -PackagePath $packagePath
    exit 0
}

Write-Host "LumaTherm package installed: $($installedPackages[0].PackageFullName)"
Write-Host 'Launch LumaTherm from the Windows Start menu. Autostart remains disabled until you enable it in the app.'
