[CmdletBinding()]
param(
    [ValidateSet('Plan', 'Full', 'AssemblePortable', 'EmitChecksums', 'TestTransaction')][string]$Mode = 'Full',
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [string]$Publisher = 'CN=LumaTherm Local',
    [string]$SdkBuildToolsPath,
    [string]$InnoSetupPath,
    [string]$PublishedAppPath,
    [string]$SparsePackagePath,
    [string]$CertificatePublicPath,
    [string]$RepositoryRootForTest,
    [ValidateSet('None', 'Compile', 'Sign', 'Verify', 'Checksums')][string]$FailurePointForTest = 'None'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$version = '1.2.0'
$stableAppId = '{9F6F5FEA-A89E-4D1C-9D0C-6C7C9FB5D310}'
$setupName = "LumaTherm-$version-win-x64-setup.exe"
$portableZipName = "LumaTherm-$version-portable-win-x64.zip"
$sparsePackageName = "LumaTherm-$version-sparse.msix"
$isTest = $env:LUMATHERM_PACKAGING_TEST -eq '1'
if (-not [string]::IsNullOrWhiteSpace($RepositoryRootForTest) -and -not $isTest) { throw 'Repository root override is reserved for controlled packaging tests.' }
if ($FailurePointForTest -ne 'None' -and (-not $isTest -or $Mode -ne 'TestTransaction')) { throw 'Failure injection is reserved for the controlled transaction test mode.' }
$repositoryRoot = if ([string]::IsNullOrWhiteSpace($RepositoryRootForTest)) { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\') } else { [IO.Path]::GetFullPath($RepositoryRootForTest).TrimEnd('\') }
$artifactsRoot = Join-Path $repositoryRoot 'artifacts\release'
$publishRoot = Join-Path $artifactsRoot 'publish'
$sparseLayoutRoot = Join-Path $artifactsRoot 'sparse-layout'
$portableRoot = Join-Path $artifactsRoot 'portable'
$publicStagingRoot = Join-Path $artifactsRoot 'public-staging'
$distBackupRoot = Join-Path $artifactsRoot 'previous-dist'
$distRoot = Join-Path $repositoryRoot 'dist'
$manifestPath = Join-Path $repositoryRoot 'packaging\sparse\AppxManifest.xml'
$projectBuildWriteRoots = @()
foreach ($sourceRootName in @('src', 'tests', 'tools', 'packaging')) {
    $sourceRoot = Join-Path $repositoryRoot $sourceRootName
    if (Test-Path -LiteralPath $sourceRoot -PathType Container) {
        foreach ($project in @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.csproj' -File -Recurse -ErrorAction SilentlyContinue)) {
            $projectBuildWriteRoots += (Join-Path $project.Directory.FullName 'bin'), (Join-Path $project.Directory.FullName 'obj')
        }
    }
}

function Test-IsBelow([string]$Path, [string]$Root) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    return $full.Equals($rootFull, [StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith($rootFull + '\', [StringComparison]::OrdinalIgnoreCase)
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

function Assert-SafeTree([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    Assert-NoReparseComponents $full
    if (-not (Test-Path -LiteralPath $full)) { throw "Required path is missing: $full" }
    $item = Get-Item -LiteralPath $full -Force
    if ($item.PSIsContainer) {
        foreach ($child in @(Get-ChildItem -LiteralPath $full -Recurse -Force)) {
            if (($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Refusing a tree containing a reparse point: $($child.FullName)" }
        }
    }
}

function Assert-OwnedTarget([string]$Path) {
    if (-not ((Test-IsBelow $Path $artifactsRoot) -or (Test-IsBelow $Path $distRoot))) { throw "Refusing release write outside owned roots: $Path" }
    Assert-NoReparseComponents $Path
}

function Remove-OwnedDirectory([string]$Path) {
    Assert-OwnedTarget $Path
    if (Test-Path -LiteralPath $Path) { Assert-SafeTree $Path; Remove-Item -LiteralPath $Path -Recurse -Force }
}

function Resolve-SdkTools([string]$Override) {
    if (-not [string]::IsNullOrWhiteSpace($Override) -and (-not $isTest -or $Mode -notin @('Plan', 'Full'))) { throw 'SDK tool override is reserved for controlled Plan/Full packaging tests.' }
    $packageRoot = if ([string]::IsNullOrWhiteSpace($Override)) { Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools\10.0.26100.8249' } else { [IO.Path]::GetFullPath($Override) }
    Assert-NoReparseComponents $packageRoot
    $makeAppx = $null; $signTool = $null
    if (Test-Path -LiteralPath $packageRoot -PathType Container) {
        $makeAppx = Get-ChildItem -LiteralPath (Join-Path $packageRoot 'bin') -Filter MakeAppx.exe -File -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
        $signTool = Get-ChildItem -LiteralPath (Join-Path $packageRoot 'bin') -Filter SignTool.exe -File -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
    }
    return [pscustomobject]@{ MakeAppx = if ($null -eq $makeAppx) { '' } else { $makeAppx.FullName }; SignTool = if ($null -eq $signTool) { '' } else { $signTool.FullName } }
}

function Resolve-Inno([string]$Override) {
    if (-not [string]::IsNullOrWhiteSpace($Override)) {
        if ($Mode -notin @('Plan', 'Full')) { throw 'An explicit Inno Setup compiler path is supported only in Plan/Full mode.' }
        Assert-NoReparseComponents $Override
        return [IO.Path]::GetFullPath($Override)
    }
    $candidate = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    Assert-NoReparseComponents $candidate
    return [IO.Path]::GetFullPath($candidate)
}

function Open-ExternalCertificate {
    if ([string]::IsNullOrWhiteSpace($CertificatePath)) { throw 'An external PFX is required for a Full release.' }
    $full = [IO.Path]::GetFullPath($CertificatePath)
    Assert-NoReparseComponents $full
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw 'The external PFX does not exist.' }
    $resolved = [IO.Path]::GetFullPath((Get-Item -LiteralPath $full -Force).FullName)
    if ((Test-IsBelow $resolved $repositoryRoot) -or (Test-IsBelow $resolved $distRoot)) { throw 'The resolved external PFX must be outside the repository and dist roots.' }
    try {
        $flags = [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
        return [Security.Cryptography.X509Certificates.X509Certificate2]::new($resolved, $CertificatePassword, $flags)
    } catch { throw 'Unable to open the external PFX with the supplied password.' }
}

function Assert-ExpectedSigner([string]$Path, [Security.Cryptography.X509Certificates.X509Certificate2]$Certificate) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($null -eq $signature.SignerCertificate -or
        -not $signature.SignerCertificate.Thumbprint.Equals($Certificate.Thumbprint, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Signer verification failed: $([IO.Path]::GetFileName($Path))"
    }

    if ($signature.Status -eq 'Valid') { return }
    $isExpectedSelfSignedUntrustedRoot = $Certificate.Subject.Equals($Certificate.Issuer, [StringComparison]::OrdinalIgnoreCase) -and
        $signature.Status -eq 'UnknownError'
    if (-not $isExpectedSelfSignedUntrustedRoot) {
        throw "Signer trust verification failed: $([IO.Path]::GetFileName($Path)) ($($signature.Status))"
    }
}

function Assert-AuthenticodeSignature(
    [string]$Path,
    [Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
    [string]$SignTool) {
    & $SignTool verify /pa /v $Path
    $signToolExit = $LASTEXITCODE
    Assert-ExpectedSigner $Path $Certificate

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    $isExpectedSelfSignedUntrustedRoot = $Certificate.Subject.Equals($Certificate.Issuer, [StringComparison]::OrdinalIgnoreCase) -and
        $signature.Status -eq 'UnknownError'
    if ($signToolExit -ne 0 -and -not $isExpectedSelfSignedUntrustedRoot) {
        throw "Authenticode verification failed: $([IO.Path]::GetFileName($Path))"
    }
}

function Get-Sha256Hex([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try { $algorithm = [Security.Cryptography.SHA256]::Create(); try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') } finally { $algorithm.Dispose() } }
    finally { $stream.Dispose() }
}

function Write-Checksums([string]$Root, [string[]]$Names) {
    Assert-OwnedTarget $Root
    $lines = foreach ($name in @($Names | Sort-Object)) {
        $path = Join-Path $Root $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required artifact is missing: $name" }
        "$(Get-Sha256Hex $path) *$name"
    }
    Set-Content -LiteralPath (Join-Path $Root 'SHA256SUMS.txt') -Value $lines -Encoding ASCII
}

function New-DeterministicZip([string]$Source, [string]$Destination) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Assert-OwnedTarget $Destination
    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Force }
    $stream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            foreach ($file in @(Get-ChildItem -LiteralPath $Source -File -Recurse | Sort-Object FullName)) {
                $relative = $file.FullName.Substring($Source.TrimEnd('\').Length + 1).Replace('\', '/')
                $entry = $archive.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                $input = [IO.File]::OpenRead($file.FullName); $output = $entry.Open()
                try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
            }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
}

function Write-SignedPayloadAnchor([string]$Destination, [string]$Published, [string]$Cer) {
    $inputs = [ordered]@{
        'app/LumaTherm.exe' = Join-Path $Published 'LumaTherm.exe'
        'LICENSE' = Join-Path $repositoryRoot 'LICENSE'
        'LumaTherm.cer' = $Cer
        'README.md' = Join-Path $repositoryRoot 'README.md'
        'Register-LumaTherm.cmd' = Join-Path $repositoryRoot 'scripts\Register-LumaTherm.cmd'
        'Register-LumaTherm.ps1' = Join-Path $repositoryRoot 'scripts\Register-LumaTherm.ps1'
        'Unregister-LumaTherm.ps1' = Join-Path $repositoryRoot 'scripts\Unregister-LumaTherm.ps1'
        'Set-LumaThermLighting.ps1' = Join-Path $repositoryRoot 'scripts\Set-LumaThermLighting.ps1'
    }
    foreach ($path in $inputs.Values) { Assert-SafeTree $path }
    $files = @($inputs.GetEnumerator() | ForEach-Object { [ordered]@{ path = $_.Key; sha256 = Get-Sha256Hex $_.Value } })
    [ordered]@{ version = 1; files = $files } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $Destination -Encoding UTF8
}

function Assemble-Portable([string]$Published, [string]$Sparse, [string]$Cer, [string]$OutputRoot) {
    foreach ($path in @($Published, $Sparse, $Cer)) { Assert-SafeTree $path }
    if (-not (Test-Path -LiteralPath $Published -PathType Container)) { throw 'Published app path must be a directory.' }
    $publishedFiles = @(Get-ChildItem -LiteralPath $Published -File -Recurse -Force)
    if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -cne 'LumaTherm.exe' -or $publishedFiles[0].DirectoryName -cne $Published.TrimEnd('\')) {
        throw 'Unexpected published output; self-contained payload must contain exactly app\LumaTherm.exe.'
    }
    Remove-OwnedDirectory $portableRoot
    Assert-OwnedTarget $OutputRoot
    New-Item -ItemType Directory -Path (Join-Path $portableRoot 'app'), $OutputRoot -Force | Out-Null
    Get-ChildItem -LiteralPath $Published -Force | Copy-Item -Destination (Join-Path $portableRoot 'app') -Recurse -Force
    Copy-Item -LiteralPath $Sparse -Destination (Join-Path $portableRoot $sparsePackageName) -Force
    Copy-Item -LiteralPath $Cer -Destination (Join-Path $portableRoot 'LumaTherm.cer') -Force
    foreach ($name in @('Register-LumaTherm.cmd', 'Register-LumaTherm.ps1', 'Unregister-LumaTherm.ps1', 'Set-LumaThermLighting.ps1')) { Copy-Item -LiteralPath (Join-Path $repositoryRoot "scripts\$name") -Destination $portableRoot -Force }
    foreach ($name in @('README.md', 'LICENSE')) { $source = Join-Path $repositoryRoot $name; if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required portable input is missing: $name" }; Copy-Item -LiteralPath $source -Destination $portableRoot -Force }
    $internalNames = @(Get-ChildItem -LiteralPath $portableRoot -File -Recurse | ForEach-Object { $_.FullName.Substring($portableRoot.Length + 1).Replace('\', '/') })
    Write-Checksums $portableRoot $internalNames
    New-DeterministicZip $portableRoot (Join-Path $OutputRoot $portableZipName)
}

function Write-PublicChecksums([string]$Root) {
    $allowed = @($setupName, $portableZipName)
    $actual = @(Get-ChildItem -LiteralPath $Root -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' })
    $unexpected = @($actual | Where-Object { $_.Name -notin $allowed })
    if ($unexpected.Count -gt 0) { throw "Unexpected public artifact: $($unexpected[0].Name)" }
    Write-Checksums $Root $allowed
}

function Promote-PublicArtifacts {
    Assert-SafeTree $publicStagingRoot
    $names = @(Get-ChildItem -LiteralPath $publicStagingRoot -File | Select-Object -ExpandProperty Name | Sort-Object)
    $expected = @($portableZipName, $setupName, 'SHA256SUMS.txt' | Sort-Object)
    if (($names -join '|') -cne ($expected -join '|')) { throw 'Public staging is incomplete or contains unexpected files.' }
    Remove-OwnedDirectory $distBackupRoot
    $hadPrevious = Test-Path -LiteralPath $distRoot
    if ($hadPrevious) { Assert-SafeTree $distRoot; Move-Item -LiteralPath $distRoot -Destination $distBackupRoot }
    try {
        Move-Item -LiteralPath $publicStagingRoot -Destination $distRoot
        if ($hadPrevious) { Remove-OwnedDirectory $distBackupRoot }
    } catch {
        if (Test-Path -LiteralPath $distRoot) { Remove-OwnedDirectory $distRoot }
        if ($hadPrevious -and (Test-Path -LiteralPath $distBackupRoot)) { Move-Item -LiteralPath $distBackupRoot -Destination $distRoot }
        throw
    }
}

function Invoke-TestTransaction {
    if (-not $isTest) { throw 'TestTransaction is a controlled packaging-test mode.' }
    Assert-OwnedTarget $publicStagingRoot
    Remove-OwnedDirectory $publicStagingRoot
    try {
        New-Item -ItemType Directory -Path $publicStagingRoot -Force | Out-Null
        if ($FailurePointForTest -eq 'Compile') { throw 'Injected Compile failure' }
        Set-Content -LiteralPath (Join-Path $publicStagingRoot $setupName) -Value 'validated setup' -Encoding ASCII
        Set-Content -LiteralPath (Join-Path $publicStagingRoot $portableZipName) -Value 'validated zip' -Encoding ASCII
        if ($FailurePointForTest -eq 'Sign') { throw 'Injected Sign failure' }
        if ($FailurePointForTest -eq 'Verify') { throw 'Injected Verify failure' }
        if ($FailurePointForTest -eq 'Checksums') { throw 'Injected Checksums failure' }
        Write-PublicChecksums $publicStagingRoot
        Promote-PublicArtifacts
        [pscustomobject]@{ promoted = $true; dist = $distRoot } | ConvertTo-Json -Compress | Write-Output
    } catch { if (Test-Path -LiteralPath $publicStagingRoot) { Remove-OwnedDirectory $publicStagingRoot }; throw }
}

if ($Mode -eq 'TestTransaction') { Invoke-TestTransaction; exit 0 }
if ($Mode -eq 'AssemblePortable') {
    if (-not $isTest) { throw 'AssemblePortable is a controlled packaging-test mode.' }
    Assemble-Portable $PublishedAppPath $SparsePackagePath $CertificatePublicPath $distRoot
    [pscustomobject]@{ portableZip = Join-Path $distRoot $portableZipName } | ConvertTo-Json -Compress | Write-Output
    exit 0
}
if ($Mode -eq 'EmitChecksums') {
    if (-not $isTest) { throw 'EmitChecksums is a controlled packaging-test mode.' }
    Write-PublicChecksums $distRoot
    [pscustomobject]@{ checksumPath = Join-Path $distRoot 'SHA256SUMS.txt' } | ConvertTo-Json -Compress | Write-Output
    exit 0
}

$tools = Resolve-SdkTools $SdkBuildToolsPath
$iscc = Resolve-Inno $InnoSetupPath
$certificate = $null
try {
    if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) { $certificate = Open-ExternalCertificate; if (-not $Publisher.Equals($certificate.Subject, [StringComparison]::OrdinalIgnoreCase)) { throw 'Publisher does not match the external signing certificate subject.' }; $certificateSource = 'external-provided' }
    else { $certificateSource = 'not-provided' }
    $dotnet = Join-Path $repositoryRoot '.dotnet\dotnet.exe'
    $publishArguments = @('publish', (Join-Path $repositoryRoot 'src\LumaTherm.App\LumaTherm.App.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None', '-p:DebugSymbols=false', '-p:NuGetAudit=false', '-o', $publishRoot)
    $plan = [ordered]@{
        version = $version; setupName = $setupName; portableZipName = $portableZipName; sparsePackageName = $sparsePackageName; checksumName = 'SHA256SUMS.txt'; stableAppId = $stableAppId; certificateSource = $certificateSource
        toolDiscoveryStrategy = 'filesystem-only'; planWriteRoots = @(); allowedWriteRoots = @(); fullBuildWriteRoots = @($artifactsRoot, $distRoot) + @($projectBuildWriteRoots | Sort-Object -Unique); cacheWriteRoots = @(Join-Path $env:USERPROFILE '.nuget\packages')
        tools = [ordered]@{
            SignTool = [ordered]@{ path = $tools.SignTool; available = (-not [string]::IsNullOrWhiteSpace($tools.SignTool) -and (Test-Path -LiteralPath $tools.SignTool -PathType Leaf)) }
            MakeAppx = [ordered]@{ path = $tools.MakeAppx; available = (-not [string]::IsNullOrWhiteSpace($tools.MakeAppx) -and (Test-Path -LiteralPath $tools.MakeAppx -PathType Leaf)) }
            ISCC = [ordered]@{ path = $iscc; available = (Test-Path -LiteralPath $iscc -PathType Leaf) }
        }
        plannedCommands = @(
            [ordered]@{ name = 'restore'; file = $dotnet; arguments = @('restore', 'LumaTherm.sln', '-r', 'win-x64', '-p:NuGetAudit=false') },
            [ordered]@{ name = 'test'; file = $dotnet; arguments = @('test', 'LumaTherm.sln', '-c', 'Release', '-m:1', '-p:NuGetAudit=false') },
            [ordered]@{ name = 'publish'; file = $dotnet; arguments = $publishArguments },
            [ordered]@{ name = 'sign-app'; file = $tools.SignTool; arguments = @('sign', '/fd', 'SHA256', '/f', '<external-pfx>', '/p', '<secure-password>', (Join-Path $publishRoot 'LumaTherm.exe')) },
            [ordered]@{ name = 'verify-app'; file = $tools.SignTool; arguments = @('verify', '/pa', '/v', (Join-Path $publishRoot 'LumaTherm.exe')) },
            [ordered]@{ name = 'emit-signed-payload-anchor'; file = 'internal'; arguments = @('PayloadHashes.json') },
            [ordered]@{ name = 'make-sparse-package'; file = $tools.MakeAppx; arguments = @('pack', '/d', $sparseLayoutRoot, '/p', (Join-Path $artifactsRoot $sparsePackageName), '/o', '/nv') },
            [ordered]@{ name = 'sign-sparse-package'; file = $tools.SignTool; arguments = @('sign', '/fd', 'SHA256', '/f', '<external-pfx>', '/p', '<secure-password>', (Join-Path $artifactsRoot $sparsePackageName)) },
            [ordered]@{ name = 'verify-sparse-package'; file = $tools.SignTool; arguments = @('verify', '/pa', '/v', (Join-Path $artifactsRoot $sparsePackageName)) },
            [ordered]@{ name = 'assemble-portable'; file = 'internal'; arguments = @($portableZipName) },
            [ordered]@{ name = 'compile-installer'; file = $iscc; arguments = @('/Qp', "/O$publicStagingRoot", "/DPayloadRoot=$portableRoot", (Join-Path $repositoryRoot 'packaging\LumaTherm.iss')) },
            [ordered]@{ name = 'sign-installer'; file = $tools.SignTool; arguments = @('sign', '/fd', 'SHA256', '/f', '<external-pfx>', '/p', '<secure-password>', (Join-Path $publicStagingRoot $setupName)) },
            [ordered]@{ name = 'verify-installer'; file = $tools.SignTool; arguments = @('verify', '/pa', '/v', (Join-Path $publicStagingRoot $setupName)) },
            [ordered]@{ name = 'emit-checksums'; file = 'internal'; arguments = @('SHA256SUMS.txt') },
            [ordered]@{ name = 'promote-public-artifacts'; file = 'internal'; arguments = @($distRoot) }
        )
        forbiddenSideEffects = [ordered]@{ certificateImport = $false; packageRegistration = $false; systemStoreWrite = $false; install = $false; uninstall = $false; hardwareWrite = $false; signing = $false; processStart = $false; fileWrite = $false }
        privateKeyOutputs = @(); inputs = [ordered]@{ manifest = $manifestPath; innoScript = Join-Path $repositoryRoot 'packaging\LumaTherm.iss'; licenseAvailable = Test-Path -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -PathType Leaf }
    }
    if ($Mode -eq 'Plan') { $plan | ConvertTo-Json -Depth 8 -Compress | Write-Output; exit 0 }

    if ($null -eq $certificate) { throw 'An external PFX is required for a Full release.' }
    foreach ($namedTool in @([pscustomobject]@{ Name='MakeAppx'; Path=$tools.MakeAppx }, [pscustomobject]@{ Name='SignTool'; Path=$tools.SignTool }, [pscustomobject]@{ Name='ISCC'; Path=$iscc })) { if ([string]::IsNullOrWhiteSpace($namedTool.Path) -or -not (Test-Path -LiteralPath $namedTool.Path -PathType Leaf)) { throw "$($namedTool.Name) is required for a Full release." } }
    foreach ($input in @($dotnet, $manifestPath, (Join-Path $repositoryRoot 'packaging\LumaTherm.iss'), (Join-Path $repositoryRoot 'README.md'), (Join-Path $repositoryRoot 'LICENSE'))) { if (-not (Test-Path -LiteralPath $input -PathType Leaf)) { throw "Required release input is missing: $([IO.Path]::GetFileName($input))" } }
    foreach ($target in @($artifactsRoot, $distRoot, $publishRoot, $sparseLayoutRoot, $portableRoot, $publicStagingRoot)) { Assert-OwnedTarget $target }

    Remove-OwnedDirectory $artifactsRoot
    New-Item -ItemType Directory -Path $artifactsRoot, $publishRoot, $sparseLayoutRoot, $publicStagingRoot -Force | Out-Null
    try {
        & $dotnet restore (Join-Path $repositoryRoot 'LumaTherm.sln') -r win-x64 '-p:NuGetAudit=false'; if ($LASTEXITCODE -ne 0) { throw 'Release restore failed.' }
        & $dotnet test (Join-Path $repositoryRoot 'LumaTherm.sln') -c Release '-m:1' '--no-restore' '-p:NuGetAudit=false'; if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }
        & $dotnet @publishArguments; if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
        $projectExe = Join-Path $publishRoot 'LumaTherm.App.exe'; $appExe = Join-Path $publishRoot 'LumaTherm.exe'
        if (Test-Path -LiteralPath $projectExe) { Move-Item -LiteralPath $projectExe -Destination $appExe }
        if (-not (Test-Path -LiteralPath $appExe -PathType Leaf)) { throw 'Published LumaTherm executable is missing.' }
        & $tools.SignTool sign /fd SHA256 /f $CertificatePath /p $CertificatePassword $appExe; if ($LASTEXITCODE -ne 0) { throw 'Application signing failed.' }
        Assert-AuthenticodeSignature $appExe $certificate $tools.SignTool
        $cerPath = Join-Path $artifactsRoot 'LumaTherm.cer'; [IO.File]::WriteAllBytes($cerPath, $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
        Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $sparseLayoutRoot 'AppxManifest.xml')
        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\Assets') -Destination $sparseLayoutRoot -Recurse
        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\public') -Destination $sparseLayoutRoot -Recurse
        Write-SignedPayloadAnchor (Join-Path $sparseLayoutRoot 'PayloadHashes.json') $publishRoot $cerPath
        $sparsePath = Join-Path $artifactsRoot $sparsePackageName
        & $tools.MakeAppx pack /d $sparseLayoutRoot /p $sparsePath /o /nv; if ($LASTEXITCODE -ne 0) { throw 'MakeAppx sparse package build failed.' }
        & $tools.SignTool sign /fd SHA256 /f $CertificatePath /p $CertificatePassword $sparsePath; if ($LASTEXITCODE -ne 0) { throw 'Sparse package signing failed.' }
        Assert-AuthenticodeSignature $sparsePath $certificate $tools.SignTool
        Assemble-Portable $publishRoot $sparsePath $cerPath $publicStagingRoot
        & $iscc '/Qp' "/O$publicStagingRoot" "/DPayloadRoot=$portableRoot" (Join-Path $repositoryRoot 'packaging\LumaTherm.iss'); if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
        $setupPath = Join-Path $publicStagingRoot $setupName
        & $tools.SignTool sign /fd SHA256 /f $CertificatePath /p $CertificatePassword $setupPath; if ($LASTEXITCODE -ne 0) { throw 'Installer signing failed.' }
        Assert-AuthenticodeSignature $setupPath $certificate $tools.SignTool
        Write-PublicChecksums $publicStagingRoot
        Promote-PublicArtifacts
        Write-Host "Release artifacts created in $distRoot"
    } catch { if (Test-Path -LiteralPath $publicStagingRoot) { Remove-OwnedDirectory $publicStagingRoot }; throw }
} finally { $CertificatePassword = $null; if ($null -ne $certificate) { $certificate.Dispose() } }
