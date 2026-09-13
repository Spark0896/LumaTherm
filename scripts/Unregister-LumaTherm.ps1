[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$RemoveUserData,
    [switch]$RemoveCertificate,
    [switch]$AuditOnly,
    [string]$InstalledPackageNameForTest,
    [string]$InstalledPublisherForTest,
    [string]$InstalledPackageFullNameForTest,
    [string]$InstalledPackagesJsonForTest,
    [ValidateSet('Accept', 'Decline')][string]$ConfirmationDecisionForTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$isTest = $env:LUMATHERM_PACKAGING_TEST -eq '1'
$hasOverride = -not [string]::IsNullOrWhiteSpace($InstalledPackageNameForTest) -or
    -not [string]::IsNullOrWhiteSpace($InstalledPublisherForTest) -or
    -not [string]::IsNullOrWhiteSpace($InstalledPackageFullNameForTest) -or
    -not [string]::IsNullOrWhiteSpace($InstalledPackagesJsonForTest) -or
    -not [string]::IsNullOrWhiteSpace($ConfirmationDecisionForTest)
if ($hasOverride -and (-not $AuditOnly -or -not $isTest)) { throw 'Test overrides require AuditOnly and LUMATHERM_PACKAGING_TEST=1.' }

if ($isTest -and $AuditOnly -and -not [string]::IsNullOrWhiteSpace($InstalledPackagesJsonForTest)) {
    $parsedPackages = $InstalledPackagesJsonForTest | ConvertFrom-Json
    $packages = @()
    for ($index = 0; $index -lt $parsedPackages.Count; $index++) { $packages += $parsedPackages[$index] }
} elseif ($isTest -and $AuditOnly -and -not [string]::IsNullOrWhiteSpace($InstalledPackageFullNameForTest)) {
    $packages = @([pscustomobject]@{ Name = $InstalledPackageNameForTest; Publisher = $InstalledPublisherForTest; PackageFullName = $InstalledPackageFullNameForTest })
} elseif ($isTest -and $AuditOnly) { $packages = @() }
else { $packages = @(Get-AppxPackage -Name 'LumaTherm' | Where-Object { $_.Name -ceq 'LumaTherm' -and $_.Publisher -ceq 'CN=LumaTherm Local' }) }
if ($packages.Count -gt 1) { throw 'Exact LumaTherm package identity is ambiguous; nothing was removed.' }
if ($packages.Count -eq 1 -and ($packages[0].Name -cne 'LumaTherm' -or $packages[0].Publisher -cne 'CN=LumaTherm Local')) { throw 'Selected package is not the exact LumaTherm name and publisher; nothing was removed.' }

if (-not $Force) {
    if ($isTest -and $AuditOnly) { $confirmed = $ConfirmationDecisionForTest -eq 'Accept' }
    else { $confirmed = (Read-Host 'Unregister the exact LumaTherm sparse identity? Type YES') -ceq 'YES' }
    if (-not $confirmed) { exit 3 }
}

$events = [Collections.Generic.List[string]]::new()
$command = @()
if ($packages.Count -eq 0) { $events.Add('packageAlreadyAbsent') }
else {
    $fullName = [string]$packages[0].PackageFullName
    $command = @('Remove-AppxPackage', '-Package', $fullName)
    if ($AuditOnly) { $events.Add('packageRemovalPlanned') }
    else { Remove-AppxPackage -Package $fullName; $events.Add('packageRemoved') }
}

if ($RemoveCertificate) {
    $scriptRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
    $certificatePath = [IO.Path]::GetFullPath((Join-Path $scriptRoot 'LumaTherm.cer'))
    if (-not $certificatePath.StartsWith($scriptRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $certificatePath -PathType Leaf)) { throw 'Bundled LumaTherm certificate is missing or outside the helper directory.' }
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
    try {
        if ($certificate.Subject -cne 'CN=LumaTherm Local') { throw 'Bundled certificate subject is not the exact LumaTherm publisher.' }
        $trustedPath = 'Cert:\LocalMachine\Root\' + $certificate.Thumbprint
        if ($AuditOnly) { $events.Add('certificateRemovalPlanned') }
        elseif (Test-Path -LiteralPath $trustedPath) { Remove-Item -LiteralPath $trustedPath -Force; $events.Add('certificateRemoved') }
        else { $events.Add('certificateAlreadyAbsent') }
    } finally { $certificate.Dispose() }
}

$userDataPath = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'LumaTherm'))
if ($RemoveUserData) {
    $localRoot = [IO.Path]::GetFullPath($env:LOCALAPPDATA).TrimEnd('\')
    if (-not $userDataPath.Equals((Join-Path $localRoot 'LumaTherm'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe user-data target.' }
    foreach ($path in @($localRoot, $userDataPath)) {
        if (Test-Path -LiteralPath $path) { $item = Get-Item -LiteralPath $path -Force; if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Refusing user-data removal through reparse point: $path" } }
    }
    if (Test-Path -LiteralPath $userDataPath -PathType Container) {
        $pending = [Collections.Generic.Stack[string]]::new(); $pending.Push($userDataPath)
        while ($pending.Count -gt 0) {
            $directory = $pending.Pop()
            foreach ($child in @(Get-ChildItem -LiteralPath $directory -Force)) {
                if (($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Refusing user-data removal containing a reparse point: $($child.FullName)" }
                if ($child.PSIsContainer) { $pending.Push($child.FullName) }
            }
        }
    }
    if ($AuditOnly) { $events.Add('userDataRemovalPlanned') }
    elseif (Test-Path -LiteralPath $userDataPath) { Remove-Item -LiteralPath $userDataPath -Recurse -Force; $events.Add('userDataRemoved') }
}

[pscustomobject]@{ events = $events.ToArray(); removalCommand = $command; packageIdentityName = 'LumaTherm'; publisher = 'CN=LumaTherm Local'; removeUserData = [bool]$RemoveUserData; removeCertificate = [bool]$RemoveCertificate; userDataPath = $userDataPath } | ConvertTo-Json -Depth 3 -Compress | Write-Output
