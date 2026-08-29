[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$RemoveUserData,
    [switch]$AuditOnly
)

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Unregister-LumaTherm.ps1') -Force:$Force -RemoveUserData:$RemoveUserData -AuditOnly:$AuditOnly
exit $LASTEXITCODE
