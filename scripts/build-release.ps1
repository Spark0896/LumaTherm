[CmdletBinding()]
param(
    [ValidateSet('Full', 'Plan', 'PrepareLayout', 'EmitChecksums')]
    [string]$Mode = 'Full',
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [string]$Publisher,
    [string]$SdkBuildToolsPath,
    [string]$PublishedAppPath,
    [ValidateSet('NonAdmin')][string]$AdministratorStatusForTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not [string]::IsNullOrWhiteSpace($SdkBuildToolsPath) -and
    ($Mode -ne 'Plan' -or $env:LUMATHERM_PACKAGING_TEST -ne '1')) {
    throw '-SdkBuildToolsPath is permitted only in Plan mode with LUMATHERM_PACKAGING_TEST=1.'
}
if (-not [string]::IsNullOrWhiteSpace($AdministratorStatusForTest) -and $env:LUMATHERM_PACKAGING_TEST -ne '1') {
    throw '-AdministratorStatusForTest requires LUMATHERM_PACKAGING_TEST=1.'
}

function Test-IsAdministrator {
    if (-not [string]::IsNullOrWhiteSpace($AdministratorStatusForTest)) {
        return $false
    }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if ($Mode -eq 'Full' -and -not (Test-IsAdministrator)) {
    throw 'Full release signing requires an elevated Administrator PowerShell because exact temporary trust is added to LocalMachine\TrustedPeople. Re-run explicitly with UAC elevation.'
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
$distRoot = Join-Path $repositoryRoot 'dist'
$localSigningBaseRoot = Join-Path $repositoryRoot 'packaging\local-signing'
$localSigningRunRoot = Join-Path $localSigningBaseRoot ('run-' + [Guid]::NewGuid().ToString('N'))
$manifestPath = Join-Path $repositoryRoot 'packaging\AppxManifest.xml'
$layoutRoot = Join-Path $artifactsRoot 'package-layout'
$publishRoot = Join-Path $artifactsRoot 'publish\win-x64'
$msixName = 'LumaTherm-1.0.0-win-x64.msix'
$zipName = 'LumaTherm-1.0.0-portable-win-x64.zip'
$dotnetArtifactsRoot = Join-Path $artifactsRoot 'dotnet'
$safeDotnetOutputArguments = @('-p:UseArtifactsOutput=true', "-p:ArtifactsPath=$dotnetArtifactsRoot")

function Assert-PathUnderAllowedRoot {
    param([Parameter(Mandatory = $true)][string]$Path)
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $allowed = @($artifactsRoot, $distRoot, $localSigningBaseRoot)
    foreach ($root in $allowed) {
        $fullRoot = [System.IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
        if ($fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
            $fullPath.TrimEnd('\').Equals($fullRoot.TrimEnd('\'), [System.StringComparison]::OrdinalIgnoreCase)) {
            $repository = [System.IO.Path]::GetFullPath($repositoryRoot).TrimEnd('\')
            if (-not ($fullPath.Equals($repository, [System.StringComparison]::OrdinalIgnoreCase) -or
                $fullPath.StartsWith($repository + '\', [System.StringComparison]::OrdinalIgnoreCase))) {
                throw "Path is outside the physical repository root: $fullPath"
            }
            $repositoryItem = Get-Item -LiteralPath $repository -Force
            if (($repositoryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Repository root itself is a reparse point: $repository"
            }
            $current = $repository
            $relative = $fullPath.Substring($repository.Length).TrimStart('\')
            foreach ($segment in @($relative.Split('\') | Where-Object { $_.Length -gt 0 })) {
                $current = Join-Path $current $segment
                if (Test-Path -LiteralPath $current) {
                    $item = Get-Item -LiteralPath $current -Force
                    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                        throw "Refusing a sensitive operation through a reparse point: $current"
                    }
                }
            }
            return
        }
    }
    throw "Refusing to write outside artifacts, dist, or packaging/local-signing: $fullPath"
}

function Assert-SafeRecursiveTree {
    param([Parameter(Mandatory = $true)][string]$Path)
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $repository = [System.IO.Path]::GetFullPath($repositoryRoot).TrimEnd('\')
    if (-not ($fullPath.Equals($repository, [System.StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($repository + '\', [System.StringComparison]::OrdinalIgnoreCase))) {
        throw "Recursive operation path is outside the physical repository root: $fullPath"
    }

    $current = $repository
    foreach ($segment in @($fullPath.Substring($repository.Length).TrimStart('\').Split('\') | Where-Object { $_.Length -gt 0 })) {
        $current = Join-Path $current $segment
        if (Test-Path -LiteralPath $current) {
            $ancestor = Get-Item -LiteralPath $current -Force
            if (($ancestor.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing a recursive operation through a reparse point: $current"
            }
        }
    }
    if (-not (Test-Path -LiteralPath $fullPath)) { return }

    $pending = [System.Collections.Generic.Stack[string]]::new()
    $pending.Push($fullPath)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        $directoryItem = Get-Item -LiteralPath $directory -Force
        if (($directoryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing a recursive operation containing a reparse point: $directory"
        }
        if (-not $directoryItem.PSIsContainer) { continue }
        foreach ($child in @(Get-ChildItem -LiteralPath $directory -Force)) {
            if (($child.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing a recursive operation containing a reparse point: $($child.FullName)"
            }
            if ($child.PSIsContainer) { $pending.Push($child.FullName) }
        }
    }
}

function Get-ManifestPublisher {
    [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
    return [string]$manifest.Package.Identity.Publisher
}

function Open-SigningCertificate {
    param([Parameter(Mandatory = $true)][string]$Path, [string]$Password)
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "Certificate PFX not found: $fullPath" }
    $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
    return [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($fullPath, $Password, $flags)
}

function Resolve-SdkTools {
    param([string]$PackageRoot)
    $isTestOverride = -not [string]::IsNullOrWhiteSpace($PackageRoot)
    if ([string]::IsNullOrWhiteSpace($PackageRoot)) {
        $dotnet = Join-Path $repositoryRoot '.dotnet\dotnet.exe'
        if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
            throw "Local dotnet SDK was not found: $dotnet"
        }
        $project = Join-Path $repositoryRoot 'packaging\LumaTherm.Packaging.csproj'
        $propertyOutput = & $dotnet msbuild $project '-getProperty:PkgMicrosoft_Windows_SDK_BuildTools' '-p:NuGetAudit=false' $safeDotnetOutputArguments[0] $safeDotnetOutputArguments[1]
        $resolvedLine = $propertyOutput | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($resolvedLine)) {
            $propertyOutput = & $dotnet msbuild $project '-getProperty:PkgMicrosoft_Windows_SDK_BuildTools' '-p:NuGetAudit=false'
            $resolvedLine = $propertyOutput | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1
        }
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($resolvedLine)) { throw 'Unable to resolve pinned Microsoft.Windows.SDK.BuildTools package. Restore the packaging project first.' }
        $PackageRoot = $resolvedLine.Trim()
    }
    $PackageRoot = [System.IO.Path]::GetFullPath($PackageRoot)
    if (-not $isTestOverride) {
        $expectedSuffix = [System.IO.Path]::Combine('microsoft.windows.sdk.buildtools', '10.0.26100.8249')
        if (-not $PackageRoot.TrimEnd('\').EndsWith($expectedSuffix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Resolved SDK BuildTools path is not the pinned package/version: $PackageRoot"
        }
    }
    $makeAppx = Get-ChildItem -LiteralPath (Join-Path $PackageRoot 'bin') -Filter 'MakeAppx.exe' -File -Recurse |
        Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
    $signTool = Get-ChildItem -LiteralPath (Join-Path $PackageRoot 'bin') -Filter 'SignTool.exe' -File -Recurse |
        Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
    if ($null -eq $makeAppx -or $null -eq $signTool) {
        throw "Pinned SDK BuildTools package does not contain x64 MakeAppx.exe and SignTool.exe: $PackageRoot"
    }
    return [pscustomobject]@{
        MakeAppx = [System.IO.Path]::GetFullPath($makeAppx.FullName)
        SignTool = [System.IO.Path]::GetFullPath($signTool.FullName)
    }
}

function New-CleanPackageLayout {
    param([Parameter(Mandatory = $true)][string]$PublishedPath)
    $PublishedPath = [System.IO.Path]::GetFullPath($PublishedPath)
    Assert-PathUnderAllowedRoot -Path $PublishedPath
    if (-not (Test-Path -LiteralPath $PublishedPath -PathType Container)) { throw "Published app folder not found: $PublishedPath" }
    Assert-SafeRecursiveTree -Path $PublishedPath
    $manifestExecutable = Join-Path $PublishedPath 'LumaTherm.exe'
    $projectExecutable = Join-Path $PublishedPath 'LumaTherm.App.exe'
    if ((Test-Path -LiteralPath $manifestExecutable) -and (Test-Path -LiteralPath $projectExecutable)) {
        throw 'Published app contains ambiguous LumaTherm executables.'
    }
    if (Test-Path -LiteralPath $projectExecutable -PathType Leaf) {
        Move-Item -LiteralPath $projectExecutable -Destination $manifestExecutable
    }
    if (-not (Test-Path -LiteralPath $manifestExecutable -PathType Leaf)) {
        throw 'Published app does not contain the LumaTherm executable.'
    }
    Assert-PathUnderAllowedRoot -Path $layoutRoot
    if (Test-Path -LiteralPath $layoutRoot) {
        Assert-SafeRecursiveTree -Path $layoutRoot
        Remove-Item -LiteralPath $layoutRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $layoutRoot | Out-Null
    Assert-SafeRecursiveTree -Path $layoutRoot
    Assert-SafeRecursiveTree -Path $PublishedPath
    Get-ChildItem -LiteralPath $PublishedPath -Force | Copy-Item -Destination $layoutRoot -Recurse -Force
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $layoutRoot 'AppxManifest.xml') -Force
    $packageAssets = Join-Path $repositoryRoot 'packaging\Assets'
    Assert-SafeRecursiveTree -Path $layoutRoot
    Assert-SafeRecursiveTree -Path $packageAssets
    Copy-Item -LiteralPath $packageAssets -Destination $layoutRoot -Recurse -Force
    $publicFolder = Join-Path $repositoryRoot 'packaging\public'
    Assert-SafeRecursiveTree -Path $layoutRoot
    Assert-SafeRecursiveTree -Path $publicFolder
    Copy-Item -LiteralPath $publicFolder -Destination $layoutRoot -Recurse -Force
}

function Write-DistributionChecksums {
    Assert-PathUnderAllowedRoot -Path $distRoot
    $required = @($msixName, $zipName, 'LumaTherm.cer', 'install.ps1', 'uninstall.ps1')
    $distributionFiles = @(Get-ChildItem -LiteralPath $distRoot -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' })
    $caseGroups = $distributionFiles | Group-Object { $_.Name.ToUpperInvariant() }
    if ($caseGroups | Where-Object { $_.Count -gt 1 }) { throw 'Distribution contains case-ambiguous artifact names.' }
    $requiredNames = @{}
    foreach ($requiredName in $required) { $requiredNames[$requiredName.ToUpperInvariant()] = $true }
    $unexpected = @($distributionFiles | Where-Object { -not $requiredNames.ContainsKey($_.Name.ToUpperInvariant()) })
    if ($unexpected.Count -gt 0) { throw "Distribution contains an unchecksummed artifact: $($unexpected[0].Name)" }
    $sortedRequired = [string[]]$required.Clone()
    [Array]::Sort($sortedRequired, [System.StringComparer]::OrdinalIgnoreCase)
    $lines = foreach ($name in $sortedRequired) {
        $path = Join-Path $distRoot $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required distribution artifact is missing: $name" }
        $stream = [System.IO.File]::OpenRead($path)
        try {
            $algorithm = [System.Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
            finally { $algorithm.Dispose() }
        } finally { $stream.Dispose() }
        "$hash *$name"
    }
    Set-Content -LiteralPath (Join-Path $distRoot 'SHA256SUMS.txt') -Value $lines -Encoding ASCII
}

$publishArguments = @(
    'publish', (Join-Path $repositoryRoot 'src\LumaTherm.App\LumaTherm.App.csproj'),
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore',
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None', '-p:DebugSymbols=false', '-p:NuGetAudit=false',
    $safeDotnetOutputArguments[0], $safeDotnetOutputArguments[1],
    '-o', $publishRoot
)
$restoreArguments = @('restore', (Join-Path $repositoryRoot 'LumaTherm.sln'), '-r', 'win-x64', '-p:NuGetAudit=false', $safeDotnetOutputArguments[0], $safeDotnetOutputArguments[1])
$testArguments = @('test', (Join-Path $repositoryRoot 'LumaTherm.sln'), '-c', 'Release', '--no-restore', '-p:NuGetAudit=false', $safeDotnetOutputArguments[0], $safeDotnetOutputArguments[1], '--results-directory', (Join-Path $artifactsRoot 'TestResults'))

if ($Mode -eq 'PrepareLayout') {
    if ([string]::IsNullOrWhiteSpace($PublishedAppPath)) { throw '-PublishedAppPath is required for PrepareLayout.' }
    New-CleanPackageLayout -PublishedPath $PublishedAppPath
    Write-Output ([pscustomobject]@{ layoutPath = $layoutRoot } | ConvertTo-Json -Compress)
    exit 0
}

if ($Mode -eq 'EmitChecksums') {
    Write-DistributionChecksums
    Write-Output ([pscustomobject]@{ checksumPath = (Join-Path $distRoot 'SHA256SUMS.txt') } | ConvertTo-Json -Compress)
    exit 0
}

$manifestPublisher = Get-ManifestPublisher
if ([string]::IsNullOrWhiteSpace($Publisher)) { $Publisher = $manifestPublisher }

if ($Mode -eq 'Plan') {
    $planCertificate = $null
    try {
        $certificateSubject = $manifestPublisher
        if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
            $planCertificate = Open-SigningCertificate -Path $CertificatePath -Password $CertificatePassword
            $certificateSubject = $planCertificate.Subject
        }
        if (-not $Publisher.Equals($certificateSubject, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Publisher '$Publisher' does not match signing certificate subject '$certificateSubject'."
        }
    } finally {
        if ($null -ne $planCertificate) { $planCertificate.Dispose() }
    }
    $tools = Resolve-SdkTools -PackageRoot $SdkBuildToolsPath
    $plan = [ordered]@{
        makeAppxPath = $tools.MakeAppx
        signToolPath = $tools.SignTool
        publishArguments = $publishArguments
        restoreArguments = $restoreArguments
        testArguments = $testArguments
        packageLayout = $layoutRoot
        msixName = $msixName
        zipName = $zipName
        allowedWriteRoots = @($artifactsRoot, $distRoot, $localSigningBaseRoot)
        localSigningDirectory = $localSigningRunRoot
        temporaryTrustStore = 'Cert:\LocalMachine\TrustedPeople'
        localCertificateWorkflow = if ([string]::IsNullOrWhiteSpace($CertificatePath)) { @(
            'require:elevated-administrator', 'create:CurrentUser/My', 'export:owned-run-directory', 'sign:SHA256',
            'import-if-absent:LocalMachine/TrustedPeople', 'verify:/pa',
            'remove-if-owned:LocalMachine/TrustedPeople', 'remove:CurrentUser/My',
            'verify:owned-store-cleanup', 'remove:owned-run-directory'
        ) } else { @() }
        localCertificateProfile = [ordered]@{
            subject = 'CN=LumaTherm Local'
            enhancedKeyUsage = '1.3.6.1.5.5.7.3.3'
            keyUsage = 'DigitalSignature'
        }
    }
    Write-Output ($plan | ConvertTo-Json -Depth 4 -Compress)
    exit 0
}

Assert-PathUnderAllowedRoot -Path $artifactsRoot
Assert-PathUnderAllowedRoot -Path $distRoot
New-Item -ItemType Directory -Path $artifactsRoot, $distRoot -Force | Out-Null
$dotnetPath = Join-Path $repositoryRoot '.dotnet\dotnet.exe'
& $dotnetPath @restoreArguments
if ($LASTEXITCODE -ne 0) { throw 'Release restore failed.' }
& $dotnetPath @testArguments
if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }
$tools = Resolve-SdkTools -PackageRoot $SdkBuildToolsPath
Assert-PathUnderAllowedRoot -Path $distRoot
if (Test-Path -LiteralPath $distRoot) {
    Assert-SafeRecursiveTree -Path $distRoot
    Remove-Item -LiteralPath $distRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $distRoot | Out-Null
Assert-PathUnderAllowedRoot -Path $publishRoot
if (Test-Path -LiteralPath $publishRoot) {
    Assert-SafeRecursiveTree -Path $publishRoot
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}
& $dotnetPath @publishArguments
if ($LASTEXITCODE -ne 0) { throw 'Self-contained win-x64 publish failed.' }
New-CleanPackageLayout -PublishedPath $publishRoot

$createdCertificate = $null
$certificate = $null
$plainPassword = $CertificatePassword
$pfxPath = $CertificatePath
$machineTrustedCertificateAdded = $false
$machineTrustedCertificatePath = $null
$transientThumbprint = $null
Assert-PathUnderAllowedRoot -Path $localSigningRunRoot
New-Item -ItemType Directory -Path $localSigningRunRoot -Force | Out-Null
try {
    if ([string]::IsNullOrWhiteSpace($pfxPath)) {
        $randomBytes = New-Object byte[] 32
        $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
        try { $random.GetBytes($randomBytes) }
        finally { $random.Dispose() }
        $plainPassword = [Convert]::ToBase64String($randomBytes)
        $securePassword = ConvertTo-SecureString -String $plainPassword -AsPlainText -Force
        $createdCertificate = New-SelfSignedCertificate -Type Custom -Subject 'CN=LumaTherm Local' -FriendlyName 'LumaTherm Local Package Signing' -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyUsage DigitalSignature -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3') -KeyExportPolicy Exportable -CertStoreLocation 'Cert:\CurrentUser\My' -NotAfter (Get-Date).AddYears(1)
        $transientThumbprint = $createdCertificate.Thumbprint
        $pfxPath = Join-Path $localSigningRunRoot 'LumaTherm-local.pfx'
        Assert-PathUnderAllowedRoot -Path $localSigningRunRoot
        Export-PfxCertificate -Cert $createdCertificate -FilePath $pfxPath -Password $securePassword | Out-Null
        Export-Certificate -Cert $createdCertificate -FilePath (Join-Path $localSigningRunRoot 'LumaTherm.cer') -Type CERT | Out-Null
    } else {
        $pfxPath = [System.IO.Path]::GetFullPath($pfxPath)
        if (-not (Test-Path -LiteralPath $pfxPath -PathType Leaf)) { throw "Certificate PFX not found: $pfxPath" }
    }

    $certificate = Open-SigningCertificate -Path $pfxPath -Password $plainPassword
    if (-not $Publisher.Equals($certificate.Subject, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Publisher '$Publisher' does not match signing certificate subject '$($certificate.Subject)'."
    }

    [xml]$layoutManifest = Get-Content -LiteralPath (Join-Path $layoutRoot 'AppxManifest.xml') -Raw
    $layoutManifest.Package.Identity.Publisher = $Publisher
    $layoutManifest.Save((Join-Path $layoutRoot 'AppxManifest.xml'))
    $msixPath = Join-Path $distRoot $msixName
    if (Test-Path -LiteralPath $msixPath) { Remove-Item -LiteralPath $msixPath -Force }
    & $tools.MakeAppx pack /d $layoutRoot /p $msixPath /o
    if ($LASTEXITCODE -ne 0) { throw 'MakeAppx packaging failed.' }
    & $tools.SignTool sign /fd SHA256 /f $pfxPath /p $plainPassword $msixPath
    if ($LASTEXITCODE -ne 0) { throw 'SignTool signing failed.' }
    if ($null -ne $createdCertificate) {
        $machineTrustedCertificatePath = 'Cert:\LocalMachine\TrustedPeople\' + $createdCertificate.Thumbprint
        if (-not (Test-Path -LiteralPath $machineTrustedCertificatePath)) {
            $machineTrustedCertificateAdded = $true
            Import-Certificate -FilePath (Join-Path $localSigningRunRoot 'LumaTherm.cer') -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
        }
    }
    & $tools.SignTool verify /pa /v $msixPath
    if ($LASTEXITCODE -ne 0) { throw 'SignTool verification failed.' }

    $zipPath = Join-Path $distRoot $zipName
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Assert-SafeRecursiveTree -Path $publishRoot
    Compress-Archive -Path (Join-Path $publishRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
    if ($null -ne $createdCertificate) {
        Copy-Item -LiteralPath (Join-Path $localSigningRunRoot 'LumaTherm.cer') -Destination (Join-Path $distRoot 'LumaTherm.cer') -Force
    } else {
        Export-Certificate -Cert $certificate -FilePath (Join-Path $distRoot 'LumaTherm.cer') -Type CERT -Force | Out-Null
    }
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'scripts\install.ps1') -Destination $distRoot -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'scripts\uninstall.ps1') -Destination $distRoot -Force
    Write-DistributionChecksums
    Write-Host "Release artifacts created in $distRoot"
} finally {
    $plainPassword = $null
    $CertificatePassword = $null
    if ($null -ne $certificate) { $certificate.Dispose() }
    $cleanupFailures = [System.Collections.Generic.List[string]]::new()
    if ($machineTrustedCertificateAdded -and -not [string]::IsNullOrWhiteSpace($machineTrustedCertificatePath)) {
        try { if (Test-Path -LiteralPath $machineTrustedCertificatePath) { Remove-Item -LiteralPath $machineTrustedCertificatePath -Force } }
        catch { $cleanupFailures.Add("LocalMachine/TrustedPeople cleanup failed: $($_.Exception.Message)") }
    }
    if ($null -ne $createdCertificate) {
        $myCertificatePath = "Cert:\CurrentUser\My\" + $createdCertificate.Thumbprint
        try { if (Test-Path -LiteralPath $myCertificatePath) { Remove-Item -LiteralPath $myCertificatePath -Force } }
        catch { $cleanupFailures.Add("CurrentUser/My cleanup failed: $($_.Exception.Message)") }
        try {
            if (($machineTrustedCertificateAdded -and (Test-Path -LiteralPath $machineTrustedCertificatePath)) -or (Test-Path -LiteralPath $myCertificatePath)) {
                throw 'Owned transient certificate remains in a certificate store.'
            }
        } catch { $cleanupFailures.Add("Certificate cleanup verification failed: $($_.Exception.Message)") }
    }
    try {
        Assert-PathUnderAllowedRoot -Path $localSigningRunRoot
        if (Test-Path -LiteralPath $localSigningRunRoot) {
            Assert-SafeRecursiveTree -Path $localSigningRunRoot
            Remove-Item -LiteralPath $localSigningRunRoot -Recurse -Force
        }
    } catch { $cleanupFailures.Add("Owned signing directory cleanup failed: $($_.Exception.Message)") }
    if ($cleanupFailures.Count -gt 0) { throw ($cleanupFailures -join [Environment]::NewLine) }
    if (-not [string]::IsNullOrWhiteSpace($transientThumbprint)) {
        Write-Host "Removed transient signing certificate $transientThumbprint from owned CurrentUser/My and LocalMachine/TrustedPeople entries and verified cleanup."
    }
}
