[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$RemoveUserData,
    [switch]$RemoveCertificate,
    [switch]$AuditOnly,
    [string]$InstalledPackageForTest,
    [ValidateSet('Accept', 'Decline')][string]$ConfirmationDecisionForTest,
    [string]$CertificateThumbprintForTest,
    [ValidateSet('Admin', 'NonAdmin')][string]$AdministratorStatusForTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$isTest = $env:LUMATHERM_PACKAGING_TEST -eq '1'
if ((-not [string]::IsNullOrWhiteSpace($InstalledPackageForTest) -or
     -not [string]::IsNullOrWhiteSpace($ConfirmationDecisionForTest) -or
     -not [string]::IsNullOrWhiteSpace($CertificateThumbprintForTest) -or
     -not [string]::IsNullOrWhiteSpace($AdministratorStatusForTest)) -and (-not $AuditOnly -or -not $isTest)) {
    throw 'Test discovery overrides require AuditOnly and LUMATHERM_PACKAGING_TEST=1.'
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

$events = [System.Collections.Generic.List[string]]::new()
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValue = 'LumaTherm'
$userDataPath = Join-Path $env:LOCALAPPDATA 'LumaTherm'
$certificatePath = Join-Path $PSScriptRoot 'LumaTherm.cer'

if ($isTest -and $AuditOnly) {
    $packageNames = [string[]]@($InstalledPackageForTest.Split(';') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
} else {
    $packages = @(Get-AppxPackage -Name 'LumaTherm' | Where-Object { $_.Name -ceq 'LumaTherm' })
    $packageNames = [string[]]@($packages | ForEach-Object { $_.PackageFullName })
}
if ($packageNames.Count -gt 1) { throw 'LumaTherm package discovery is ambiguous; no package was removed.' }
$packageFullName = if ($packageNames.Count -eq 1) { $packageNames[0] } else { '' }
if (-not [string]::IsNullOrWhiteSpace($packageFullName) -and -not $packageFullName.StartsWith('LumaTherm_', [System.StringComparison]::Ordinal)) {
    throw 'Discovered package is outside the exact LumaTherm identity; no package was removed.'
}

function Assert-SafeUserDataTarget {
    $localRoot = [System.IO.Path]::GetFullPath($env:LOCALAPPDATA).TrimEnd('\')
    $target = [System.IO.Path]::GetFullPath($userDataPath)
    if (-not $target.Equals((Join-Path $localRoot 'LumaTherm'), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "User-data target is outside the exact LumaTherm path: $target"
    }
    foreach ($path in @($localRoot, $target)) {
        if (Test-Path -LiteralPath $path) {
            $item = Get-Item -LiteralPath $path -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing user-data removal through a reparse point: $path"
            }
        }
    }
    if (Test-Path -LiteralPath $target) {
        $pending = [System.Collections.Generic.Stack[string]]::new()
        $pending.Push($target)
        while ($pending.Count -gt 0) {
            $directory = $pending.Pop()
            foreach ($child in @(Get-ChildItem -LiteralPath $directory -Force)) {
                if (($child.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Refusing user-data removal containing a reparse point: $($child.FullName)"
                }
                if ($child.PSIsContainer) { $pending.Push($child.FullName) }
            }
        }
    }
}
if ($RemoveUserData) { Assert-SafeUserDataTarget }

$certificateThumbprint = ''
$trustedPath = ''
if ($RemoveCertificate) {
    if (-not (Test-Path -LiteralPath $certificatePath -PathType Leaf)) { throw 'Sibling LumaTherm.cer is required to remove a certificate.' }
    $distributedCertificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certificatePath)
    $certificateThumbprint = $distributedCertificate.Thumbprint
    if ($isTest -and $AuditOnly -and -not [string]::IsNullOrWhiteSpace($CertificateThumbprintForTest) -and
        -not $certificateThumbprint.Equals($CertificateThumbprintForTest, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Certificate test thumbprint does not match sibling LumaTherm.cer.'
    }
    $trustedPath = 'Cert:\LocalMachine\TrustedPeople\' + $certificateThumbprint
    if (-not (Test-IsAdministrator)) {
        $events.Add('administratorPreflightFailed')
        if ($AuditOnly) {
            [pscustomobject]@{ events = $events.ToArray(); certificateStorePath = $trustedPath } | ConvertTo-Json -Compress | Write-Output
        }
        throw 'Removing the exact LumaTherm machine trust certificate requires an elevated Administrator PowerShell.'
    }
    $events.Add('administratorPreflightPassed')
}
$events.Add('targetsPreflighted')

if (-not $Force) {
    if ($isTest -and $AuditOnly) {
        $confirmed = $ConfirmationDecisionForTest -eq 'Accept'
    } else {
        $answer = Read-Host 'Remove only the installed LumaTherm package and portable Run value? Type YES'
        $confirmed = $answer -ceq 'YES'
    }
    if (-not $confirmed) {
        $events.Add('uninstallDeclined')
        [pscustomobject]@{ events = $events.ToArray() } | ConvertTo-Json -Compress | Write-Output
        exit 3
    }
}
$events.Add('confirmationAccepted')

if ([string]::IsNullOrWhiteSpace($packageFullName)) {
    $events.Add('packageAlreadyAbsent')
} elseif ($AuditOnly) {
    $events.Add('packageRemovalPlanned')
} else {
    Remove-AppxPackage -Package $packageFullName
    $events.Add('packageRemoved')
}

if ($AuditOnly) {
    $events.Add('runValueRemovalPlanned')
} elseif (Test-Path -LiteralPath $runKey) {
    Remove-ItemProperty -LiteralPath $runKey -Name $runValue -ErrorAction SilentlyContinue
    $events.Add('runValueRemoved')
}

if ($RemoveUserData) {
    if ($AuditOnly) {
        $events.Add('userDataRemovalPlanned')
    } elseif (Test-Path -LiteralPath $userDataPath) {
        Assert-SafeUserDataTarget
        Remove-Item -LiteralPath $userDataPath -Recurse -Force
        $events.Add('userDataRemoved')
    }
}

if ($RemoveCertificate) {
    if ($AuditOnly) {
        $events.Add('certificateRemovalPlanned')
    } elseif (Test-Path -LiteralPath $trustedPath) {
        Remove-Item -LiteralPath $trustedPath -Force
        $events.Add('certificateRemoved')
    }
}

$result = [ordered]@{
    events = $events.ToArray()
    packageFullName = $packageFullName
    runKey = $runKey
    runValue = $runValue
    userDataPath = $userDataPath
    certificateThumbprint = $certificateThumbprint
    certificateStorePath = $trustedPath
}
$result | ConvertTo-Json -Depth 3 -Compress | Write-Output
if (-not $AuditOnly) { Write-Host 'LumaTherm uninstall completed. User data was preserved unless -RemoveUserData was supplied.' }
