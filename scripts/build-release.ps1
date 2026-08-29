[CmdletBinding()]
param(
    [ValidateSet('Plan', 'Full', 'AssemblePortable', 'EmitChecksums')][string]$Mode = 'Full',
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [string]$Publisher = 'CN=LumaTherm Local',
    [string]$SdkBuildToolsPath,
    [string]$InnoSetupPath,
    [string]$PublishedAppPath,
    [string]$SparsePackagePath,
    [string]$CertificatePublicPath,
    [string]$RepositoryRootForTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$version = '1.1.0'
$stableAppId = '{9F6F5FEA-A89E-4D1C-9D0C-6C7C9FB5D310}'
$setupName = "LumaTherm-$version-win-x64-setup.exe"
$portableZipName = "LumaTherm-$version-portable-win-x64.zip"
$sparsePackageName = "LumaTherm-$version-sparse.msix"
$isTest = $env:LUMATHERM_PACKAGING_TEST -eq '1'
if (-not [string]::IsNullOrWhiteSpace($RepositoryRootForTest) -and -not $isTest) {
    throw 'Repository root override is reserved for controlled packaging tests.'
}
$repositoryRoot = if ([string]::IsNullOrWhiteSpace($RepositoryRootForTest)) { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\') } else { [IO.Path]::GetFullPath($RepositoryRootForTest).TrimEnd('\') }
$artifactsRoot = Join-Path $repositoryRoot 'artifacts\release'
$publishRoot = Join-Path $artifactsRoot 'publish'
$sparseLayoutRoot = Join-Path $artifactsRoot 'sparse-layout'
$portableRoot = Join-Path $artifactsRoot 'portable'
$distRoot = Join-Path $repositoryRoot 'dist'
$manifestPath = Join-Path $repositoryRoot 'packaging\sparse\AppxManifest.xml'

function Test-IsBelow([string]$Path, [string]$Root) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    return $full.Equals($rootFull, [StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith($rootFull + '\', [StringComparison]::OrdinalIgnoreCase)
}

function Assert-SafeTree([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $full)) { throw "Required path is missing: $full" }
    $item = Get-Item -LiteralPath $full -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Refusing a reparse-point path: $full" }
    if ($item.PSIsContainer) {
        foreach ($child in @(Get-ChildItem -LiteralPath $full -Recurse -Force)) {
            if (($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Refusing a tree containing a reparse point: $($child.FullName)" }
        }
    }
}

function Remove-OwnedDirectory([string]$Path) {
    if (-not ((Test-IsBelow $Path $artifactsRoot) -or (Test-IsBelow $Path $distRoot))) { throw "Refusing cleanup outside release roots: $Path" }
    if (Test-Path -LiteralPath $Path) { Assert-SafeTree $Path; Remove-Item -LiteralPath $Path -Recurse -Force }
}

function Resolve-SdkTools([string]$Override) {
    $packageRoot = $Override
    if (-not [string]::IsNullOrWhiteSpace($Override) -and (-not $isTest -or $Mode -notin @('Plan', 'Full'))) {
        throw 'SDK tool override is reserved for controlled Plan/Full packaging tests.'
    }
    if ([string]::IsNullOrWhiteSpace($packageRoot)) {
        $dotnet = Join-Path $repositoryRoot '.dotnet\dotnet.exe'
        $project = Join-Path $repositoryRoot 'packaging\LumaTherm.Packaging.csproj'
        if (Test-Path -LiteralPath $dotnet -PathType Leaf) {
            $output = & $dotnet msbuild $project '-getProperty:PkgMicrosoft_Windows_SDK_BuildTools' '-p:NuGetAudit=false' 2>$null
            if ($LASTEXITCODE -eq 0) { $packageRoot = ($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1).Trim() }
        }
    }
    $makeAppx = $null
    $signTool = $null
    if (-not [string]::IsNullOrWhiteSpace($packageRoot) -and (Test-Path -LiteralPath $packageRoot -PathType Container)) {
        $makeAppx = Get-ChildItem -LiteralPath (Join-Path $packageRoot 'bin') -Filter MakeAppx.exe -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
        $signTool = Get-ChildItem -LiteralPath (Join-Path $packageRoot 'bin') -Filter SignTool.exe -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
    }
    return [pscustomobject]@{
        MakeAppx = if ($null -eq $makeAppx) { '' } else { [IO.Path]::GetFullPath($makeAppx.FullName) }
        SignTool = if ($null -eq $signTool) { '' } else { [IO.Path]::GetFullPath($signTool.FullName) }
    }
}

function Resolve-Inno([string]$Override) {
    if (-not [string]::IsNullOrWhiteSpace($Override)) {
        if (-not $isTest -or $Mode -notin @('Plan', 'Full')) { throw 'Inno Setup override is reserved for controlled Plan/Full packaging tests.' }
        return [IO.Path]::GetFullPath($Override)
    }
    $candidate = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    if (Test-Path -LiteralPath $candidate -PathType Leaf) { return [IO.Path]::GetFullPath($candidate) }
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) { return [IO.Path]::GetFullPath($command.Source) }
    return [IO.Path]::GetFullPath($candidate)
}

function Open-ExternalCertificate {
    if ([string]::IsNullOrWhiteSpace($CertificatePath)) { throw 'An external PFX is required for a Full release.' }
    $full = [IO.Path]::GetFullPath($CertificatePath)
    if ((Test-IsBelow $full $repositoryRoot) -or (Test-IsBelow $full $distRoot)) { throw 'The external PFX must be outside the repository and dist roots.' }
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw 'The external PFX does not exist.' }
    try {
        $flags = [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
        return [Security.Cryptography.X509Certificates.X509Certificate2]::new($full, $CertificatePassword, $flags)
    } catch { throw 'Unable to open the external PFX with the supplied password.' }
}

function Get-Sha256Hex([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
        finally { $algorithm.Dispose() }
    } finally { $stream.Dispose() }
}

function Write-Checksums([string]$Root, [string[]]$Names) {
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
    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Force }
    $stream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            foreach ($file in @(Get-ChildItem -LiteralPath $Source -File -Recurse | Sort-Object FullName)) {
                $relative = $file.FullName.Substring($Source.TrimEnd('\').Length + 1).Replace('\', '/')
                $entry = $archive.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                $input = [IO.File]::OpenRead($file.FullName)
                $output = $entry.Open()
                try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
            }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
}

function Assemble-Portable([string]$Published, [string]$Sparse, [string]$Cer) {
    foreach ($path in @($Published, $Sparse, $Cer)) { Assert-SafeTree $path }
    if (-not (Test-Path -LiteralPath $Published -PathType Container)) { throw 'Published app path must be a directory.' }
    Remove-OwnedDirectory $portableRoot
    New-Item -ItemType Directory -Path (Join-Path $portableRoot 'app'), $distRoot -Force | Out-Null
    Get-ChildItem -LiteralPath $Published -Force | Copy-Item -Destination (Join-Path $portableRoot 'app') -Recurse -Force
    Copy-Item -LiteralPath $Sparse -Destination (Join-Path $portableRoot $sparsePackageName) -Force
    Copy-Item -LiteralPath $Cer -Destination (Join-Path $portableRoot 'LumaTherm.cer') -Force
    foreach ($name in @('Register-LumaTherm.ps1', 'Unregister-LumaTherm.ps1')) { Copy-Item -LiteralPath (Join-Path $repositoryRoot "scripts\$name") -Destination $portableRoot -Force }
    foreach ($name in @('README.md', 'LICENSE')) {
        $source = Join-Path $repositoryRoot $name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required portable input is missing: $name" }
        Copy-Item -LiteralPath $source -Destination $portableRoot -Force
    }
    $internalNames = @(Get-ChildItem -LiteralPath $portableRoot -File -Recurse | ForEach-Object { $_.FullName.Substring($portableRoot.Length + 1).Replace('\', '/') })
    Write-Checksums -Root $portableRoot -Names $internalNames
    New-DeterministicZip -Source $portableRoot -Destination (Join-Path $distRoot $portableZipName)
}

function Write-PublicChecksums {
    $allowed = @($setupName, $portableZipName)
    $actual = @(Get-ChildItem -LiteralPath $distRoot -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' })
    $unexpected = @($actual | Where-Object { $_.Name -notin $allowed })
    if ($unexpected.Count -gt 0) { throw "Unexpected public artifact: $($unexpected[0].Name)" }
    Write-Checksums -Root $distRoot -Names $allowed
}

if ($Mode -eq 'AssemblePortable') {
    if (-not $isTest) { throw 'AssemblePortable is a controlled packaging-test mode.' }
    Assemble-Portable -Published $PublishedAppPath -Sparse $SparsePackagePath -Cer $CertificatePublicPath
    [pscustomobject]@{ portableZip = (Join-Path $distRoot $portableZipName) } | ConvertTo-Json -Compress | Write-Output
    exit 0
}
if ($Mode -eq 'EmitChecksums') {
    if (-not $isTest) { throw 'EmitChecksums is a controlled packaging-test mode.' }
    Write-PublicChecksums
    [pscustomobject]@{ checksumPath = (Join-Path $distRoot 'SHA256SUMS.txt') } | ConvertTo-Json -Compress | Write-Output
    exit 0
}

$tools = Resolve-SdkTools $SdkBuildToolsPath
$iscc = Resolve-Inno $InnoSetupPath
$certificate = $null
try {
    if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
        $certificate = Open-ExternalCertificate
        if (-not $Publisher.Equals($certificate.Subject, [StringComparison]::OrdinalIgnoreCase)) { throw 'Publisher does not match the external signing certificate subject.' }
        $certificateSource = 'external-provided'
    } else { $certificateSource = 'not-provided' }

    $dotnet = Join-Path $repositoryRoot '.dotnet\dotnet.exe'
    $publishArguments = @('publish', (Join-Path $repositoryRoot 'src\LumaTherm.App\LumaTherm.App.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None', '-p:DebugSymbols=false', '-p:NuGetAudit=false', '-o', $publishRoot)
    $plan = [ordered]@{
        version = $version
        setupName = $setupName
        portableZipName = $portableZipName
        sparsePackageName = $sparsePackageName
        checksumName = 'SHA256SUMS.txt'
        stableAppId = $stableAppId
        certificateSource = $certificateSource
        allowedWriteRoots = @([IO.Path]::GetFullPath($artifactsRoot), [IO.Path]::GetFullPath($distRoot))
        tools = [ordered]@{
            SignTool = [ordered]@{ path = $tools.SignTool; available = (-not [string]::IsNullOrWhiteSpace($tools.SignTool) -and (Test-Path -LiteralPath $tools.SignTool -PathType Leaf)) }
            MakeAppx = [ordered]@{ path = $tools.MakeAppx; available = (-not [string]::IsNullOrWhiteSpace($tools.MakeAppx) -and (Test-Path -LiteralPath $tools.MakeAppx -PathType Leaf)) }
            ISCC = [ordered]@{ path = $iscc; available = (Test-Path -LiteralPath $iscc -PathType Leaf) }
        }
        plannedCommands = @(
            [ordered]@{ name = 'restore'; file = $dotnet; arguments = @('restore', 'LumaTherm.sln', '-r', 'win-x64', '-p:NuGetAudit=false') },
            [ordered]@{ name = 'test'; file = $dotnet; arguments = @('test', 'LumaTherm.sln', '-c', 'Release', '-p:NuGetAudit=false') },
            [ordered]@{ name = 'publish'; file = $dotnet; arguments = $publishArguments },
            [ordered]@{ name = 'make-sparse-package'; file = $tools.MakeAppx; arguments = @('pack', '/d', $sparseLayoutRoot, '/p', (Join-Path $artifactsRoot $sparsePackageName), '/o') },
            [ordered]@{ name = 'sign-app'; file = $tools.SignTool; arguments = @('sign', '/fd', 'SHA256', '/f', '<external-pfx>', '/p', '<secure-password>', (Join-Path $publishRoot 'LumaTherm.exe')) },
            [ordered]@{ name = 'sign-sparse-package'; file = $tools.SignTool; arguments = @('sign', '/fd', 'SHA256', '/f', '<external-pfx>', '/p', '<secure-password>', (Join-Path $artifactsRoot $sparsePackageName)) },
            [ordered]@{ name = 'verify-app'; file = $tools.SignTool; arguments = @('verify', '/pa', '/v', (Join-Path $publishRoot 'LumaTherm.exe')) },
            [ordered]@{ name = 'verify-sparse-package'; file = $tools.SignTool; arguments = @('verify', '/pa', '/v', (Join-Path $artifactsRoot $sparsePackageName)) },
            [ordered]@{ name = 'assemble-portable'; file = 'internal'; arguments = @($portableZipName) },
            [ordered]@{ name = 'compile-installer'; file = $iscc; arguments = @('/Qp', "/O$distRoot", "/DPayloadRoot=$portableRoot", (Join-Path $repositoryRoot 'packaging\LumaTherm.iss')) },
            [ordered]@{ name = 'sign-installer'; file = $tools.SignTool; arguments = @('sign', '/fd', 'SHA256', '/f', '<external-pfx>', '/p', '<secure-password>', (Join-Path $distRoot $setupName)) },
            [ordered]@{ name = 'verify-installer'; file = $tools.SignTool; arguments = @('verify', '/pa', '/v', (Join-Path $distRoot $setupName)) },
            [ordered]@{ name = 'emit-checksums'; file = 'internal'; arguments = @('SHA256SUMS.txt') }
        )
        forbiddenSideEffects = [ordered]@{ certificateImport = $false; packageRegistration = $false; systemStoreWrite = $false; install = $false; uninstall = $false; hardwareWrite = $false; signing = $false }
        privateKeyOutputs = @()
        inputs = [ordered]@{ manifest = $manifestPath; innoScript = (Join-Path $repositoryRoot 'packaging\LumaTherm.iss'); licenseAvailable = (Test-Path -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -PathType Leaf) }
    }
    if ($Mode -eq 'Plan') { $plan | ConvertTo-Json -Depth 8 -Compress | Write-Output; exit 0 }

    if ($null -eq $certificate) { throw 'An external PFX is required for a Full release.' }
    foreach ($tool in @($tools.MakeAppx, $tools.SignTool, $iscc)) { if ([string]::IsNullOrWhiteSpace($tool) -or -not (Test-Path -LiteralPath $tool -PathType Leaf)) { throw 'MakeAppx, SignTool, and ISCC must all be available for a Full release.' } }
    foreach ($input in @($manifestPath, (Join-Path $repositoryRoot 'packaging\LumaTherm.iss'), (Join-Path $repositoryRoot 'README.md'), (Join-Path $repositoryRoot 'LICENSE'))) { if (-not (Test-Path -LiteralPath $input -PathType Leaf)) { throw "Required release input is missing: $([IO.Path]::GetFileName($input))" } }

    Remove-OwnedDirectory $artifactsRoot
    Remove-OwnedDirectory $distRoot
    New-Item -ItemType Directory -Path $artifactsRoot, $distRoot, $publishRoot, $sparseLayoutRoot -Force | Out-Null
    & $dotnet restore (Join-Path $repositoryRoot 'LumaTherm.sln') -r win-x64 '-p:NuGetAudit=false'; if ($LASTEXITCODE -ne 0) { throw 'Release restore failed.' }
    & $dotnet test (Join-Path $repositoryRoot 'LumaTherm.sln') -c Release '--no-restore' '-p:NuGetAudit=false'; if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }
    & $dotnet @publishArguments; if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
    $projectExe = Join-Path $publishRoot 'LumaTherm.App.exe'
    $appExe = Join-Path $publishRoot 'LumaTherm.exe'
    if (Test-Path -LiteralPath $projectExe) { Move-Item -LiteralPath $projectExe -Destination $appExe }
    if (-not (Test-Path -LiteralPath $appExe -PathType Leaf)) { throw 'Published LumaTherm executable is missing.' }
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $sparseLayoutRoot 'AppxManifest.xml')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\Assets') -Destination $sparseLayoutRoot -Recurse
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\public') -Destination $sparseLayoutRoot -Recurse
    $sparsePath = Join-Path $artifactsRoot $sparsePackageName
    & $tools.MakeAppx pack /d $sparseLayoutRoot /p $sparsePath /o; if ($LASTEXITCODE -ne 0) { throw 'MakeAppx sparse package build failed.' }
    & $tools.SignTool sign /fd SHA256 /f $CertificatePath /p $CertificatePassword $appExe; if ($LASTEXITCODE -ne 0) { throw 'Application signing failed.' }
    & $tools.SignTool sign /fd SHA256 /f $CertificatePath /p $CertificatePassword $sparsePath; if ($LASTEXITCODE -ne 0) { throw 'Sparse package signing failed.' }
    foreach ($signed in @($appExe, $sparsePath)) { & $tools.SignTool verify /pa /v $signed; if ($LASTEXITCODE -ne 0) { throw "Authenticode verification failed: $([IO.Path]::GetFileName($signed))" } }
    $cerPath = Join-Path $artifactsRoot 'LumaTherm.cer'
    [IO.File]::WriteAllBytes($cerPath, $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    Assemble-Portable -Published $publishRoot -Sparse $sparsePath -Cer $cerPath
    $iss = Join-Path $repositoryRoot 'packaging\LumaTherm.iss'
    & $iscc '/Qp' "/O$distRoot" "/DPayloadRoot=$portableRoot" $iss; if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
    $setupPath = Join-Path $distRoot $setupName
    & $tools.SignTool sign /fd SHA256 /f $CertificatePath /p $CertificatePassword $setupPath; if ($LASTEXITCODE -ne 0) { throw 'Installer signing failed.' }
    & $tools.SignTool verify /pa /v $setupPath; if ($LASTEXITCODE -ne 0) { throw 'Installer Authenticode verification failed.' }
    foreach ($signed in @($appExe, $sparsePath, $setupPath)) {
        $signature = Get-AuthenticodeSignature -LiteralPath $signed
        if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate -or -not $signature.SignerCertificate.Thumbprint.Equals($certificate.Thumbprint, [StringComparison]::OrdinalIgnoreCase)) { throw "Signer verification failed: $([IO.Path]::GetFileName($signed))" }
    }
    Write-PublicChecksums
    Write-Host "Release artifacts created in $distRoot"
} finally {
    $CertificatePassword = $null
    if ($null -ne $certificate) { $certificate.Dispose() }
}
