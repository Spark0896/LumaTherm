[CmdletBinding()]
param(
    [ValidateSet('Full', 'Plan', 'PrepareLayout', 'EmitChecksums')]
    [string]$Mode = 'Full',
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [string]$Publisher,
    [string]$SdkBuildToolsPath,
    [string]$PublishedAppPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
$distRoot = Join-Path $repositoryRoot 'dist'
$localSigningRoot = Join-Path $repositoryRoot 'packaging\local-signing'
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
    $allowed = @($artifactsRoot, $distRoot, $localSigningRoot)
    foreach ($root in $allowed) {
        $fullRoot = [System.IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
        if ($fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
            $fullPath.TrimEnd('\').Equals($fullRoot.TrimEnd('\'), [System.StringComparison]::OrdinalIgnoreCase)) {
            return
        }
    }
    throw "Refusing to write outside artifacts, dist, or packaging/local-signing: $fullPath"
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
    if (Test-Path -LiteralPath $layoutRoot) { Remove-Item -LiteralPath $layoutRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $layoutRoot | Out-Null
    Get-ChildItem -LiteralPath $PublishedPath -Force | Copy-Item -Destination $layoutRoot -Recurse -Force
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $layoutRoot 'AppxManifest.xml') -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\Assets') -Destination $layoutRoot -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\public') -Destination $layoutRoot -Recurse -Force
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
        allowedWriteRoots = @($artifactsRoot, $distRoot, $localSigningRoot)
    }
    Write-Output ($plan | ConvertTo-Json -Depth 4 -Compress)
    exit 0
}

New-Item -ItemType Directory -Path $artifactsRoot, $distRoot, $localSigningRoot -Force | Out-Null
$dotnetPath = Join-Path $repositoryRoot '.dotnet\dotnet.exe'
& $dotnetPath @restoreArguments
if ($LASTEXITCODE -ne 0) { throw 'Release restore failed.' }
& $dotnetPath @testArguments
if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }
$tools = Resolve-SdkTools -PackageRoot $SdkBuildToolsPath
Assert-PathUnderAllowedRoot -Path $distRoot
if (Test-Path -LiteralPath $distRoot) { Remove-Item -LiteralPath $distRoot -Recurse -Force }
New-Item -ItemType Directory -Path $distRoot | Out-Null
if (Test-Path -LiteralPath $publishRoot) { Remove-Item -LiteralPath $publishRoot -Recurse -Force }
& $dotnetPath @publishArguments
if ($LASTEXITCODE -ne 0) { throw 'Self-contained win-x64 publish failed.' }
New-CleanPackageLayout -PublishedPath $publishRoot

$createdCertificate = $null
$certificate = $null
$plainPassword = $CertificatePassword
$pfxPath = $CertificatePath
try {
    if ([string]::IsNullOrWhiteSpace($pfxPath)) {
        $randomBytes = New-Object byte[] 32
        $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
        try { $random.GetBytes($randomBytes) }
        finally { $random.Dispose() }
        $plainPassword = [Convert]::ToBase64String($randomBytes)
        $securePassword = ConvertTo-SecureString -String $plainPassword -AsPlainText -Force
        $createdCertificate = New-SelfSignedCertificate -Type Custom -Subject 'CN=LumaTherm Local' -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyExportPolicy Exportable -CertStoreLocation 'Cert:\CurrentUser\My' -NotAfter (Get-Date).AddYears(1)
        $pfxPath = Join-Path $localSigningRoot 'LumaTherm-local.pfx'
        Export-PfxCertificate -Cert $createdCertificate -FilePath $pfxPath -Password $securePassword | Out-Null
        Export-Certificate -Cert $createdCertificate -FilePath (Join-Path $localSigningRoot 'LumaTherm.cer') -Type CERT | Out-Null
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
    & $tools.SignTool verify /pa /v $msixPath
    if ($LASTEXITCODE -ne 0) { throw 'SignTool verification failed.' }

    $zipPath = Join-Path $distRoot $zipName
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Compress-Archive -Path (Join-Path $publishRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
    if ($null -ne $createdCertificate) {
        Copy-Item -LiteralPath (Join-Path $localSigningRoot 'LumaTherm.cer') -Destination (Join-Path $distRoot 'LumaTherm.cer') -Force
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
    if ($null -ne $createdCertificate) {
        Remove-Item -LiteralPath ("Cert:\CurrentUser\My\" + $createdCertificate.Thumbprint) -Force
        Write-Host 'Removed the temporary local signing certificate from CurrentUser\My.'
    }
}
