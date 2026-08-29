[CmdletBinding()]
param(
    [string]$PortableDirectory = $PSScriptRoot,
    [switch]$ConfirmCertificateImport,
    [switch]$NonInteractive,
    [switch]$AuditOnly
)

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Register-LumaTherm.ps1') -PortableDirectory $PortableDirectory -ConfirmCertificateImport:$ConfirmCertificateImport -NonInteractive:$NonInteractive -AuditOnly:$AuditOnly
exit $LASTEXITCODE
