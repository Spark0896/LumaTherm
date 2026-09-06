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
    [ValidateSet('Valid', 'NotTrusted', 'UnknownError', 'HashMismatch', 'NotSigned')][string]$ApplicationSignatureStatusForTest,
    [string]$ApplicationSignatureThumbprintForTest,
    [ValidateSet('UntrustedRoot', 'Other')][string]$SignatureTrustIssueForTest,
    [ValidateSet('Admin', 'NonAdmin')][string]$AdministratorStatusForTest,
    [ValidateSet('Accept', 'Decline')][string]$CertificateDecisionForTest,
    [ValidateSet('Valid', 'Invalid')][string]$ReverifiedSignatureStatusForTest = 'Valid',
    [ValidateSet('Valid', 'Invalid')][string]$ReverifiedApplicationSignatureStatusForTest = 'Valid',
    [switch]$TrustedCertificatePresentForTest,
    [switch]$SimulateRegistrationForTest,
    [ValidateSet('None', 'Add', 'PostVerify')][string]$RegistrationFailureForTest = 'None',
    [string]$PreRegistrationPackagesJsonForTest,
    [string]$InstalledPackageNameForTest,
    [string]$InstalledPublisherForTest,
    [string]$InstalledVersionForTest,
    [string]$InstalledExternalLocationForTest,
    [string]$InstalledApplicationIdForTest,
    [string]$InstalledExtensionForTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$isTest = $env:LUMATHERM_PACKAGING_TEST -eq '1'
$testValues = @($SignatureStatusForTest, $SignatureThumbprintForTest, $ApplicationSignatureStatusForTest,
    $ApplicationSignatureThumbprintForTest, $SignatureTrustIssueForTest, $AdministratorStatusForTest,
    $CertificateDecisionForTest, $PreRegistrationPackagesJsonForTest, $InstalledPackageNameForTest, $InstalledPublisherForTest, $InstalledVersionForTest,
    $InstalledExternalLocationForTest, $InstalledApplicationIdForTest, $InstalledExtensionForTest)
$hasTestOverride = @($testValues | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count -gt 0 -or
    $TrustedCertificatePresentForTest -or $SimulateRegistrationForTest -or $RegistrationFailureForTest -ne 'None'
if ($hasTestOverride -and (-not $AuditOnly -or -not $isTest)) { throw 'Test overrides require AuditOnly and LUMATHERM_PACKAGING_TEST=1.' }

function Test-IsAdministrator {
    if ($isTest -and $AuditOnly -and -not [string]::IsNullOrWhiteSpace($AdministratorStatusForTest)) { return $AdministratorStatusForTest -eq 'Admin' }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-NoReparseComponents([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetPathRoot($full)
    $current = $root
    foreach ($segment in @($full.Substring($root.Length).TrimEnd('\').Split('\') | Where-Object { $_.Length -gt 0 })) {
        $current = Join-Path $current $segment
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Refusing path through a reparse point: $current" }
        }
    }
}

function Assert-SafePath {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Root, [switch]$Directory)
    try { $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\'); $full = [IO.Path]::GetFullPath($Path).TrimEnd('\') }
    catch { throw "Invalid path '$Path' under portable root '$Root': $($_.Exception.Message)" }
    if (-not ($full.Equals($rootFull, [StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith($rootFull + '\', [StringComparison]::OrdinalIgnoreCase))) {
        throw "Resolved path is outside the explicit portable directory: $full"
    }
    Assert-NoReparseComponents $rootFull
    Assert-NoReparseComponents $full
    $pathType = if ($Directory) { 'Container' } else { 'Leaf' }
    if (-not (Test-Path -LiteralPath $full -PathType $pathType)) { throw "Required path is missing or has the wrong type: $full" }
    return $full
}

function Get-Sha256Hex([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try { $algorithm = [Security.Cryptography.SHA256]::Create(); try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') } finally { $algorithm.Dispose() } }
    finally { $stream.Dispose() }
}

function Test-OnlyUntrustedRootChainIssue([Security.Cryptography.X509Certificates.X509Certificate2]$SignerCertificate) {
    $chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
    try {
        $chain.ChainPolicy.RevocationMode = [Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
        $null = $chain.Build($SignerCertificate)
        $statuses = @($chain.ChainStatus)
        return $statuses.Count -gt 0 -and @($statuses | Where-Object { $_.Status -ne [Security.Cryptography.X509Certificates.X509ChainStatusFlags]::UntrustedRoot }).Count -eq 0
    } finally { $chain.Dispose() }
}

function Get-SignedPayloadAnchor([string]$PackagePath) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $entries = @($archive.Entries | Where-Object { $_.FullName -ceq 'PayloadHashes.json' })
        if ($entries.Count -ne 1) { throw 'Signed sparse package must contain exactly one PayloadHashes.json anchor.' }
        $reader = [IO.StreamReader]::new($entries[0].Open(), [Text.Encoding]::UTF8, $true)
        try { return ($reader.ReadToEnd() | ConvertFrom-Json) } finally { $reader.Dispose() }
    } finally { $archive.Dispose() }
}

function Get-IncomingPackageIdentity([string]$PackagePath) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $entries = @($archive.Entries | Where-Object { $_.FullName -ceq 'AppxManifest.xml' })
        if ($entries.Count -ne 1) { throw 'Sparse identity package must contain exactly one AppxManifest.xml.' }
        $reader = [IO.StreamReader]::new($entries[0].Open(), [Text.Encoding]::UTF8, $true)
        try { $manifestText = $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $archive.Dispose() }

    $manifest = [Xml.XmlDocument]::new()
    $manifest.XmlResolver = $null
    $manifest.LoadXml($manifestText)
    $identities = @($manifest.SelectNodes("/*[local-name()='Package']/*[local-name()='Identity']"))
    if ($identities.Count -ne 1) { throw 'Sparse identity manifest must contain exactly one package identity.' }
    $name = $identities[0].GetAttribute('Name')
    $publisher = $identities[0].GetAttribute('Publisher')
    $versionText = $identities[0].GetAttribute('Version')
    if ($name -cne 'LumaTherm' -or $publisher -cne 'CN=LumaTherm Local') { throw 'Sparse identity manifest name or publisher is not the exact LumaTherm identity.' }
    try { $version = [Version]::Parse($versionText) } catch { throw "Sparse identity manifest version is invalid: $versionText" }
    $versionParts = @($version.Major, $version.Minor, $version.Build, $version.Revision)
    if ($version.ToString(4) -cne $versionText -or @($versionParts | Where-Object { $_ -lt 0 -or $_ -gt [UInt16]::MaxValue }).Count -ne 0) {
        throw "Sparse identity manifest version is invalid: $versionText"
    }
    return [pscustomobject]@{ Name = $name; Publisher = $publisher; Version = $versionText }
}

function Assert-ExactHashes([object[]]$Entries, [string[]]$ActualRelativePaths, [string]$Root, [string]$Label) {
    $map = @{}
    foreach ($entry in @($Entries)) {
        $relative = [string]$entry.path
        $hash = [string]$entry.sha256
        if ($relative -notmatch '^[^\\]+(?:/[^\\]+)*$' -or $relative.Split('/') -contains '..' -or $hash -notmatch '^[0-9A-Fa-f]{64}$') { throw "Invalid $Label entry." }
        $key = $relative.ToUpperInvariant()
        if ($map.ContainsKey($key)) { throw "Duplicate $Label entry: $relative" }
        $map[$key] = $hash.ToUpperInvariant()
    }
    $actual = @($ActualRelativePaths | ForEach-Object { $_.ToUpperInvariant() } | Sort-Object)
    $expected = @($map.Keys | Sort-Object)
    if (($actual -join '|') -cne ($expected -join '|')) { throw "$Label does not exactly cover portable content." }
    foreach ($relative in $ActualRelativePaths) {
        $path = Assert-SafePath -Path (Join-Path $Root $relative.Replace('/', '\')) -Root $Root
        if ((Get-Sha256Hex $path) -cne $map[$relative.ToUpperInvariant()]) { throw "$Label mismatch: $relative" }
    }
}

if ([string]::IsNullOrWhiteSpace($PortableDirectory)) { $PortableDirectory = $PSScriptRoot }
$portableRoot = [IO.Path]::GetFullPath($PortableDirectory).TrimEnd('\')
Assert-NoReparseComponents $portableRoot
if (-not (Test-Path -LiteralPath $portableRoot -PathType Container)) { throw "Portable directory not found: $portableRoot" }
if ([string]::IsNullOrWhiteSpace($IdentityPackage)) { $IdentityPackage = Join-Path $portableRoot 'LumaTherm-1.1.0-sparse.msix' }
if ([string]::IsNullOrWhiteSpace($ApplicationDirectory)) { $ApplicationDirectory = Join-Path $portableRoot 'app' }
if ([string]::IsNullOrWhiteSpace($CertificatePath)) { $CertificatePath = Join-Path $portableRoot 'LumaTherm.cer' }
if ([string]::IsNullOrWhiteSpace($ChecksumPath)) { $ChecksumPath = Join-Path $portableRoot 'SHA256SUMS.txt' }
$identityPackage = Assert-SafePath $IdentityPackage $portableRoot
$applicationDirectory = Assert-SafePath $ApplicationDirectory $portableRoot -Directory
$certificatePath = Assert-SafePath $CertificatePath $portableRoot
$checksumPath = Assert-SafePath $ChecksumPath $portableRoot
if (-not ([IO.Path]::GetFileName($identityPackage).Equals('LumaTherm-1.1.0-sparse.msix', [StringComparison]::Ordinal))) { throw 'Unexpected sparse identity package name.' }
$applicationExecutable = Assert-SafePath (Join-Path $applicationDirectory 'LumaTherm.exe') $portableRoot
$events = [Collections.Generic.List[string]]::new()
$events.Add('pathsVerified')

$checksumEntries = @()
foreach ($line in @(Get-Content -LiteralPath $checksumPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
    if ($line -notmatch '^([0-9A-Fa-f]{64}) \*([^\\]+(?:/[^\\]+)*)$') { throw "Invalid checksum entry: $line" }
    $checksumEntries += [pscustomobject]@{ path = $matches[2]; sha256 = $matches[1] }
}
$actualFiles = @(Get-ChildItem -LiteralPath $portableRoot -File -Recurse -Force | Where-Object { $_.FullName -ne $checksumPath })
$actualRelative = @($actualFiles | ForEach-Object { Assert-NoReparseComponents $_.FullName; $_.FullName.Substring($portableRoot.Length + 1).Replace('\', '/') })
Assert-ExactHashes $checksumEntries $actualRelative $portableRoot 'Published checksum manifest'
$events.Add('checksumsVerified')

$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
$ownedTrust = $false
$registrationAdded = $false
$registeredFullName = $null
$sameVersionPackageRemoved = $false
$sameVersionRemovalCommand = $null
try {
    if ($isTest -and $AuditOnly) {
        $packageStatus = $SignatureStatusForTest; $packageThumbprint = $SignatureThumbprintForTest
        $appStatus = $ApplicationSignatureStatusForTest; $appThumbprint = $ApplicationSignatureThumbprintForTest
    } else {
        $packageSignature = Get-AuthenticodeSignature -LiteralPath $identityPackage
        $appSignature = Get-AuthenticodeSignature -LiteralPath $applicationExecutable
        $packageStatus = [string]$packageSignature.Status; $packageThumbprint = if ($null -eq $packageSignature.SignerCertificate) { '' } else { $packageSignature.SignerCertificate.Thumbprint }
        $appStatus = [string]$appSignature.Status; $appThumbprint = if ($null -eq $appSignature.SignerCertificate) { '' } else { $appSignature.SignerCertificate.Thumbprint }
    }
    if ($packageStatus -notin @('Valid', 'NotTrusted', 'UnknownError')) { throw "Sparse identity Authenticode signature is invalid: $packageStatus" }
    if ([string]::IsNullOrWhiteSpace($packageThumbprint) -or -not $certificate.Thumbprint.Equals($packageThumbprint, [StringComparison]::OrdinalIgnoreCase)) { throw 'Sparse identity signer certificate does not match bundled LumaTherm.cer.' }
    if ($appStatus -notin @('Valid', 'NotTrusted', 'UnknownError')) { throw "LumaTherm executable Authenticode signature is invalid: $appStatus" }
    if ([string]::IsNullOrWhiteSpace($appThumbprint) -or -not $certificate.Thumbprint.Equals($appThumbprint, [StringComparison]::OrdinalIgnoreCase)) { throw 'LumaTherm executable signer certificate does not match bundled LumaTherm.cer.' }
    if ($packageStatus -eq 'Valid' -and $appStatus -ne 'Valid') { throw 'LumaTherm executable Authenticode must be Valid when the sparse identity is trusted.' }

    $anchor = Get-SignedPayloadAnchor $identityPackage
    if ([int]$anchor.version -ne 1) { throw 'Unsupported signed payload anchor version.' }
    $externalRelative = @($actualRelative | Where-Object { $_ -cne 'LumaTherm-1.1.0-sparse.msix' })
    Assert-ExactHashes @($anchor.files) $externalRelative $portableRoot 'Signed payload anchor'
    $events.Add('signedAnchorVerified')
    $events.Add('applicationSignatureVerified')

    if ($packageStatus -eq 'UnknownError') {
        $onlyUntrustedRoot = if ($isTest -and $AuditOnly) { $SignatureTrustIssueForTest -eq 'UntrustedRoot' } else { Test-OnlyUntrustedRootChainIssue $packageSignature.SignerCertificate }
        if (-not $onlyUntrustedRoot) { throw 'Sparse identity Authenticode signature has an unsupported UnknownError.' }
        $packageStatus = 'NotTrusted'
    }
    $trustedPath = 'Cert:\LocalMachine\TrustedPeople\' + $certificate.Thumbprint
    if ($packageStatus -eq 'NotTrusted') {
        $events.Add('signatureMatchedUntrusted')
        if (-not (Test-IsAdministrator)) { throw 'An elevated Administrator PowerShell is required before certificate import.' }
        $events.Add('administratorPreflightPassed')
        if ($NonInteractive -and -not $ConfirmCertificateImport) { $accepted = $false }
        elseif ($isTest -and $AuditOnly -and -not [string]::IsNullOrWhiteSpace($CertificateDecisionForTest)) { $accepted = $CertificateDecisionForTest -eq 'Accept' }
        elseif ($ConfirmCertificateImport) { $accepted = $true }
        else { $accepted = (Read-Host "Import public certificate $($certificate.Thumbprint) into LocalMachine\TrustedPeople? Type YES") -ceq 'YES' }
        if (-not $accepted) { $events.Add('certificateImportDeclined'); [pscustomobject]@{ events = $events.ToArray() } | ConvertTo-Json -Compress | Write-Output; exit 3 }
        $events.Add('certificateImportConfirmed')
        if (($isTest -and $AuditOnly -and $TrustedCertificatePresentForTest) -or (-not $AuditOnly -and (Test-Path -LiteralPath $trustedPath))) { $events.Add('certificateAlreadyPresent') }
        elseif ($AuditOnly) { $ownedTrust = $true; $events.Add('certificateImportPlanned') }
        else { Import-Certificate -FilePath $certificatePath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null; $ownedTrust = $true; $events.Add('certificateImported') }
        if ($AuditOnly) { $packageReverified = $ReverifiedSignatureStatusForTest; $appReverified = $ReverifiedApplicationSignatureStatusForTest }
        else { $packageReverified = [string](Get-AuthenticodeSignature -LiteralPath $identityPackage).Status; $appReverified = [string](Get-AuthenticodeSignature -LiteralPath $applicationExecutable).Status }
        if ($packageReverified -ne 'Valid' -or $appReverified -ne 'Valid') { throw 'Package and executable signatures must both verify after certificate import.' }
        $events.Add('signaturesReverified')
    } else { $events.Add('signaturesVerified') }

    $incomingIdentity = Get-IncomingPackageIdentity $identityPackage
    $command = @('Add-AppxPackage', '-Path', $identityPackage, '-ExternalLocation', $applicationDirectory)
    $installed = @()
    if ($AuditOnly -and -not $SimulateRegistrationForTest) {
        $events.Add('registrationPlanned')
        [pscustomobject]@{ events = $events.ToArray(); identityPackage = $identityPackage; applicationDirectory = $applicationDirectory; applicationExecutable = $applicationExecutable; certificateThumbprint = $certificate.Thumbprint; registrationCommand = $command } | ConvertTo-Json -Depth 3 -Compress | Write-Output
        exit 0
    }
    elseif ($AuditOnly) {
        $preRegistrationPackages = if ([string]::IsNullOrWhiteSpace($PreRegistrationPackagesJsonForTest)) { @() } else { @($PreRegistrationPackagesJsonForTest | ConvertFrom-Json) }
        $exactPreRegistrationPackages = @($preRegistrationPackages | Where-Object { $_.Name -ceq $incomingIdentity.Name -and $_.Publisher -ceq $incomingIdentity.Publisher })
        if ($exactPreRegistrationPackages.Count -eq 1 -and [string]$exactPreRegistrationPackages[0].Version -ceq $incomingIdentity.Version) {
            $sameVersionFullName = [string]$exactPreRegistrationPackages[0].PackageFullName
            if ([string]::IsNullOrWhiteSpace($sameVersionFullName)) { throw 'Exact same-version LumaTherm identity has no PackageFullName; refusing removal.' }
            $sameVersionRemovalCommand = @('Remove-AppxPackage', '-Package', $sameVersionFullName)
            $sameVersionPackageRemoved = $true
            $events.Add('sameVersionPackageRemoved')
        }
        if ($RegistrationFailureForTest -eq 'Add') { throw 'Injected Add-AppxPackage failure for test.' }
        $registrationAdded = $true; $registeredFullName = 'LumaTherm_1.1.0.0_x64__test'; $events.Add('registrationExecutedForTest')
        $installed = @([pscustomobject]@{ Name = $InstalledPackageNameForTest; Publisher = $InstalledPublisherForTest; Version = $InstalledVersionForTest; PackageExternalLocation = $InstalledExternalLocationForTest; PackageFullName = $registeredFullName; ApplicationId = $InstalledApplicationIdForTest; Extension = $InstalledExtensionForTest })
    } else {
        $preRegistrationPackages = @(Get-AppxPackage -Name 'LumaTherm')
        $exactPreRegistrationPackages = @($preRegistrationPackages | Where-Object { $_.Name -ceq $incomingIdentity.Name -and $_.Publisher -ceq $incomingIdentity.Publisher })
        if ($exactPreRegistrationPackages.Count -eq 1 -and [string]$exactPreRegistrationPackages[0].Version -ceq $incomingIdentity.Version) {
            $sameVersionFullName = [string]$exactPreRegistrationPackages[0].PackageFullName
            if ([string]::IsNullOrWhiteSpace($sameVersionFullName)) { throw 'Exact same-version LumaTherm identity has no PackageFullName; refusing removal.' }
            $sameVersionRemovalCommand = @('Remove-AppxPackage', '-Package', $sameVersionFullName)
            Remove-AppxPackage -Package $sameVersionFullName
            $sameVersionPackageRemoved = $true
            $events.Add('sameVersionPackageRemoved')
        }
        Add-AppxPackage -Path $identityPackage -ExternalLocation $applicationDirectory
        $registrationAdded = $true
        $installed = @(Get-AppxPackage -Name 'LumaTherm' | Where-Object { $_.Name -ceq 'LumaTherm' -and $_.Publisher -ceq 'CN=LumaTherm Local' })
        if ($installed.Count -eq 1) {
            $registeredFullName = [string]$installed[0].PackageFullName
            $packageManager = [Windows.Management.Deployment.PackageManager, Windows.Management.Deployment, ContentType = WindowsRuntime]::new()
            $runtimePackage = $packageManager.FindPackageForUser('', $registeredFullName)
            $runtimeExternalLocation = if ($null -eq $runtimePackage -or $null -eq $runtimePackage.EffectiveExternalLocation) { '' } else { [string]$runtimePackage.EffectiveExternalLocation.Path }
            $installed[0] | Add-Member -NotePropertyName PackageExternalLocation -NotePropertyValue $runtimeExternalLocation -Force
            $installedManifest = Get-AppxPackageManifest -Package $installed[0]
            $application = @($installedManifest.Package.Applications.Application | Where-Object { $_.Id -ceq 'LumaTherm' })
            $lightingExtensions = @($application.Extensions.Extension | Where-Object { [string]$_.Category -ceq 'windows.appExtension' } | ForEach-Object { [string]$_.AppExtension.Name })
            $installed[0] | Add-Member -NotePropertyName ApplicationId -NotePropertyValue $(if ($application.Count -eq 1) { [string]$application[0].Id } else { '' }) -Force
            $installed[0] | Add-Member -NotePropertyName Extension -NotePropertyValue $(if ($lightingExtensions -ccontains 'com.microsoft.windows.lighting') { 'com.microsoft.windows.lighting' } else { '' }) -Force
        }
    }
    if ($installed.Count -ne 1) { throw 'Post-registration verification did not find exactly one LumaTherm identity.' }
    $package = $installed[0]
    $externalMatches = -not [string]::IsNullOrWhiteSpace([string]$package.PackageExternalLocation) -and ([IO.Path]::GetFullPath([string]$package.PackageExternalLocation).TrimEnd('\')).Equals($applicationDirectory, [StringComparison]::OrdinalIgnoreCase)
    if ($RegistrationFailureForTest -eq 'PostVerify' -or $package.Name -cne $incomingIdentity.Name -or $package.Publisher -cne $incomingIdentity.Publisher -or [string]$package.Version -cne $incomingIdentity.Version -or -not $externalMatches -or $package.ApplicationId -cne 'LumaTherm' -or $package.Extension -cne 'com.microsoft.windows.lighting') {
        throw 'Post-registration identity, version, external location, application, or lighting extension verification failed.'
    }
    $events.Add('postRegistrationVerified')
    [pscustomobject]@{ events = $events.ToArray(); identityPackage = $identityPackage; applicationDirectory = $applicationDirectory; applicationExecutable = $applicationExecutable; certificateThumbprint = $certificate.Thumbprint; registrationCommand = $command; sameVersionRemovalCommand = $sameVersionRemovalCommand } | ConvertTo-Json -Depth 3 -Compress | Write-Output
} catch {
    $errorMessage = $_.Exception.Message
    if ($registrationAdded) {
        if ($AuditOnly) {
            $events.Add($(if ($sameVersionPackageRemoved) { 'failedReplacementPackageRemovalPlanned' } else { 'packageRollbackPlanned' }))
        }
        elseif (-not [string]::IsNullOrWhiteSpace($registeredFullName)) {
            Remove-AppxPackage -Package $registeredFullName -ErrorAction SilentlyContinue
            $events.Add($(if ($sameVersionPackageRemoved) { 'failedReplacementPackageRemoved' } else { 'packageRolledBack' }))
        }
    }
    if ($ownedTrust) {
        if ($AuditOnly) { $events.Add('certificateRollbackPlanned') }
        elseif (Test-Path -LiteralPath $trustedPath) { Remove-Item -LiteralPath $trustedPath -Force; $events.Add('certificateRolledBack') }
    }
    if ($sameVersionPackageRemoved) {
        $events.Add('sameVersionReplacementFailed')
        $errorMessage = "Same-version replacement registration failed after removing the previous exact LumaTherm identity; the previous identity is no longer installed and cannot be rolled back safely. $errorMessage"
    }
    [Console]::Error.WriteLine(([pscustomobject]@{ events = $events.ToArray(); error = $errorMessage; sameVersionRemovalCommand = $sameVersionRemovalCommand } | ConvertTo-Json -Compress))
    throw $errorMessage
} finally { $certificate.Dispose() }
