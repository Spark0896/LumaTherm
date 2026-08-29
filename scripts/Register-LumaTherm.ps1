[CmdletBinding()]
param(
    [string]$PortableDirectory,
    [string]$IdentityPackage,
    [string]$ApplicationDirectory,
    [string]$CertificatePath,
    [string]$ChecksumPath,
    [switch]$ConfirmCertificateImport,
    [switch]$NonInteractive,
    [switch]$AuditOnly,
    [ValidateSet('Valid', 'NotTrusted', 'UnknownError', 'HashMismatch', 'NotSigned')][string]$SignatureStatusForTest,
    [string]$SignatureThumbprintForTest,
    [ValidateSet('UntrustedRoot', 'Other')][string]$SignatureTrustIssueForTest,
    [ValidateSet('Admin', 'NonAdmin')][string]$AdministratorStatusForTest,
    [ValidateSet('Accept', 'Decline')][string]$CertificateDecisionForTest,
    [ValidateSet('Valid', 'Invalid')][string]$ReverifiedSignatureStatusForTest = 'Valid',
    [switch]$TrustedCertificatePresentForTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$isTest = $env:LUMATHERM_PACKAGING_TEST -eq '1'
$hasTestOverride = -not [string]::IsNullOrWhiteSpace($SignatureStatusForTest) -or
    -not [string]::IsNullOrWhiteSpace($SignatureThumbprintForTest) -or
    -not [string]::IsNullOrWhiteSpace($SignatureTrustIssueForTest) -or
    -not [string]::IsNullOrWhiteSpace($AdministratorStatusForTest) -or
    -not [string]::IsNullOrWhiteSpace($CertificateDecisionForTest) -or $TrustedCertificatePresentForTest
if ($hasTestOverride -and (-not $AuditOnly -or -not $isTest)) { throw 'Test overrides require AuditOnly and LUMATHERM_PACKAGING_TEST=1.' }

function Test-IsAdministrator {
    if ($isTest -and $AuditOnly -and -not [string]::IsNullOrWhiteSpace($AdministratorStatusForTest)) {
        return $AdministratorStatusForTest -eq 'Admin'
    }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-OnlyUntrustedRootChainIssue {
    param([Parameter(Mandatory = $true)][Security.Cryptography.X509Certificates.X509Certificate2]$SignerCertificate)
    $chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
    try {
        $chain.ChainPolicy.RevocationMode = [Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
        $null = $chain.Build($SignerCertificate)
        $statuses = @($chain.ChainStatus)
        return $statuses.Count -gt 0 -and @($statuses | Where-Object {
            $_.Status -ne [Security.Cryptography.X509Certificates.X509ChainStatusFlags]::UntrustedRoot
        }).Count -eq 0
    } finally { $chain.Dispose() }
}


function Assert-SafePath {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Root, [switch]$Directory, [switch]$AllowMissing)
    try {
        $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\')
        $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    } catch {
        throw "Invalid path '$Path' under portable root '$Root': $($_.Exception.Message)"
    }
    if (-not ($full.Equals($rootFull, [StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith($rootFull + '\', [StringComparison]::OrdinalIgnoreCase))) {
        throw "Resolved path is outside the explicit portable directory: $full"
    }
    $current = $rootFull
    foreach ($segment in @($full.Substring($rootFull.Length).TrimStart('\').Split('\') | Where-Object { $_.Length -gt 0 })) {
        $current = Join-Path $current $segment
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Refusing path through a reparse point: $current" }
        }
    }
    if (-not $AllowMissing) {
        $pathType = if ($Directory) { 'Container' } else { 'Leaf' }
        if (-not (Test-Path -LiteralPath $full -PathType $pathType)) { throw "Required path is missing or has the wrong type: $full" }
    }
    return $full
}

function Get-Sha256Hex([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
        finally { $algorithm.Dispose() }
    } finally { $stream.Dispose() }
}

if ([string]::IsNullOrWhiteSpace($PortableDirectory)) { $PortableDirectory = $PSScriptRoot }
$portableRoot = [IO.Path]::GetFullPath($PortableDirectory).TrimEnd('\')
if (-not (Test-Path -LiteralPath $portableRoot -PathType Container)) { throw "Portable directory not found: $portableRoot" }
if ((Get-Item -LiteralPath $portableRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Portable directory is a reparse point: $portableRoot" }
if ([string]::IsNullOrWhiteSpace($IdentityPackage)) { $IdentityPackage = Join-Path $portableRoot 'LumaTherm-1.1.0-sparse.msix' }
if ([string]::IsNullOrWhiteSpace($ApplicationDirectory)) { $ApplicationDirectory = Join-Path $portableRoot 'app' }
if ([string]::IsNullOrWhiteSpace($CertificatePath)) { $CertificatePath = Join-Path $portableRoot 'LumaTherm.cer' }
if ([string]::IsNullOrWhiteSpace($ChecksumPath)) { $ChecksumPath = Join-Path $portableRoot 'SHA256SUMS.txt' }
$identityPackage = Assert-SafePath -Path $IdentityPackage -Root $portableRoot
$applicationDirectory = Assert-SafePath -Path $ApplicationDirectory -Root $portableRoot -Directory
$certificatePath = Assert-SafePath -Path $CertificatePath -Root $portableRoot
$checksumPath = Assert-SafePath -Path $ChecksumPath -Root $portableRoot
if (-not ([IO.Path]::GetFileName($identityPackage).Equals('LumaTherm-1.1.0-sparse.msix', [StringComparison]::Ordinal))) { throw 'Unexpected sparse identity package name.' }
$applicationExecutable = Assert-SafePath -Path (Join-Path $applicationDirectory 'LumaTherm.exe') -Root $portableRoot
$events = [Collections.Generic.List[string]]::new()
$events.Add('pathsVerified')

$seen = @{}
foreach ($line in @(Get-Content -LiteralPath $checksumPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
    if ($line -notmatch '^([0-9A-Fa-f]{64}) \*([^\\]+(?:/[^\\]+)*)$') { throw "Invalid checksum entry: $line" }
    $relative = $matches[2]
    if ($relative.Split('/') -contains '..') { throw "Checksum traversal is forbidden: $relative" }
    $path = Assert-SafePath -Path (Join-Path $portableRoot ($relative.Replace('/', '\'))) -Root $portableRoot
    if ($seen.ContainsKey($relative.ToUpperInvariant())) { throw "Duplicate checksum entry: $relative" }
    if ((Get-Sha256Hex $path) -ne $matches[1].ToUpperInvariant()) { throw "Checksum verification failed: $relative" }
    $seen[$relative.ToUpperInvariant()] = $true
}
$actualFiles = @(Get-ChildItem -LiteralPath $portableRoot -File -Recurse -Force | Where-Object { $_.FullName -ne $checksumPath })
foreach ($file in $actualFiles) {
    $safe = Assert-SafePath -Path $file.FullName -Root $portableRoot
    $relative = $safe.Substring($portableRoot.Length + 1).Replace('\', '/')
    if (-not $seen.ContainsKey($relative.ToUpperInvariant())) { throw "Unexpected or unchecksummed portable file: $relative" }
}
if ($seen.Count -ne $actualFiles.Count) { throw 'Checksum manifest does not exactly cover the portable files.' }
$events.Add('checksumsVerified')

$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
try {
    if ($isTest -and $AuditOnly) {
        $signatureStatus = $SignatureStatusForTest
        $signatureThumbprint = $SignatureThumbprintForTest
    } else {
        $signature = Get-AuthenticodeSignature -LiteralPath $identityPackage
        $signatureStatus = [string]$signature.Status
        $signatureThumbprint = if ($null -eq $signature.SignerCertificate) { '' } else { $signature.SignerCertificate.Thumbprint }
    }
    if ($signatureStatus -notin @('Valid', 'NotTrusted', 'UnknownError')) { throw "Sparse identity Authenticode signature is invalid: $signatureStatus" }
    if ([string]::IsNullOrWhiteSpace($signatureThumbprint) -or -not $certificate.Thumbprint.Equals($signatureThumbprint, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Sparse identity signer certificate does not match bundled LumaTherm.cer.'
    }
    $trustedPath = 'Cert:\LocalMachine\TrustedPeople\' + $certificate.Thumbprint
    if ($signatureStatus -eq 'UnknownError') {
        $onlyUntrustedRoot = if ($isTest -and $AuditOnly) {
            $SignatureTrustIssueForTest -eq 'UntrustedRoot'
        } else {
            $null -ne $signature.SignerCertificate -and (Test-OnlyUntrustedRootChainIssue -SignerCertificate $signature.SignerCertificate)
        }
        if (-not $onlyUntrustedRoot) { throw 'Sparse identity Authenticode signature has an unsupported UnknownError.' }
        $signatureStatus = 'NotTrusted'
    }
    $ownedTrust = $false
    if ($signatureStatus -eq 'NotTrusted') {
        $events.Add('signatureMatchedUntrusted')
        if (-not (Test-IsAdministrator)) { throw 'An elevated Administrator PowerShell is required before certificate import.' }
        $events.Add('administratorPreflightPassed')
        if ($isTest -and $AuditOnly) { $accepted = $CertificateDecisionForTest -eq 'Accept' }
        elseif ($ConfirmCertificateImport) { $accepted = $true }
        elseif ($NonInteractive) { $accepted = $false }
        else { $accepted = (Read-Host "Import public certificate $($certificate.Thumbprint) into LocalMachine\TrustedPeople? Type YES") -ceq 'YES' }
        if (-not $accepted) {
            $events.Add('certificateImportDeclined')
            [pscustomobject]@{ events = $events.ToArray() } | ConvertTo-Json -Compress | Write-Output
            exit 3
        }
        $events.Add('certificateImportConfirmed')
        if (($isTest -and $AuditOnly -and $TrustedCertificatePresentForTest) -or (-not $AuditOnly -and (Test-Path -LiteralPath $trustedPath))) {
            $events.Add('certificateAlreadyPresent')
        } elseif ($AuditOnly) {
            $ownedTrust = $true
            $events.Add('certificateImportPlanned')
        } else {
            Import-Certificate -FilePath $certificatePath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
            $ownedTrust = $true
            $events.Add('certificateImported')
        }
        if ($AuditOnly) { $reverifiedStatus = $ReverifiedSignatureStatusForTest }
        else { $reverifiedStatus = [string](Get-AuthenticodeSignature -LiteralPath $identityPackage).Status }
        if ($reverifiedStatus -ne 'Valid') { throw 'Sparse identity signature did not verify after certificate import.' }
        $events.Add('signatureReverified')
    } else { $events.Add('signatureVerified') }

    $command = @('Add-AppxPackage', '-Path', $identityPackage, '-ExternalLocation', $applicationDirectory)
    if ($AuditOnly) { $events.Add('registrationPlanned') }
    else {
        Add-AppxPackage -Path $identityPackage -ExternalLocation $applicationDirectory
        $installed = @(Get-AppxPackage -Name 'LumaTherm' | Where-Object { $_.Name -ceq 'LumaTherm' -and $_.Publisher -ceq 'CN=LumaTherm Local' })
        if ($installed.Count -ne 1) { throw 'Sparse identity registration did not produce the exact LumaTherm identity.' }
        $events.Add('registrationVerified')
    }
    [pscustomobject]@{
        events = $events.ToArray()
        identityPackage = $identityPackage
        applicationDirectory = $applicationDirectory
        applicationExecutable = $applicationExecutable
        certificateThumbprint = $certificate.Thumbprint
        registrationCommand = $command
    } | ConvertTo-Json -Depth 3 -Compress | Write-Output
} finally { $certificate.Dispose() }
