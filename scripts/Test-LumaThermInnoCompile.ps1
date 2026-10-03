[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $OfficialInstallerPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$officialUri = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe'
$officialSha256 = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
$expectedSetupName = 'LumaTherm-1.2.0-win-x64-setup.exe'
$validationRoot = $null
$validationResult = $null
$portableProcess = $null
$compilerProcess = $null

function Assert-NoReparseComponent {
    param(
        [Parameter(Mandatory = $true)]
        [string] $LiteralPath
    )

    $candidate = [System.IO.Path]::GetFullPath($LiteralPath)
    while ($candidate) {
        if (Test-Path -LiteralPath $candidate) {
            $item = Get-Item -Force -LiteralPath $candidate
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse points are not allowed in the official compiler validation path: $candidate"
            }
        }

        $parent = [System.IO.Directory]::GetParent($candidate)
        if ($null -eq $parent) {
            break
        }
        $candidate = $parent.FullName
    }
}

function Assert-SafeTree {
    param(
        [Parameter(Mandatory = $true)]
        [string] $LiteralPath
    )

    Assert-NoReparseComponent -LiteralPath $LiteralPath
    if (-not (Test-Path -LiteralPath $LiteralPath)) {
        throw "Compiler validation path is missing: $LiteralPath"
    }
    foreach ($child in @(Get-ChildItem -Force -Recurse -LiteralPath $LiteralPath)) {
        if (($child.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Official compiler validation tree contains a reparse point: $($child.FullName)"
        }
    }
}

function Assert-ContainedPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Child,
        [Parameter(Mandatory = $true)]
        [string] $Parent
    )

    $separators = [char[]]@(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $parentFull = [System.IO.Path]::GetFullPath($Parent).TrimEnd($separators)
    $parentPrefix = [string]::Concat($parentFull, [System.IO.Path]::DirectorySeparatorChar)
    $childFull = [System.IO.Path]::GetFullPath($Child)
    $childComparable = $childFull.TrimEnd($separators)
    if ($childComparable.Equals($parentFull, [System.StringComparison]::OrdinalIgnoreCase) -or
        -not $childComparable.StartsWith($parentPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Compiler validation path escaped its owned temporary root: $childFull"
    }
}

function Get-Sha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string] $LiteralPath
    )

    $stream = [System.IO.File]::OpenRead($LiteralPath)
    try {
        $algorithm = [System.Security.Cryptography.SHA256]::Create()
        try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
        finally { $algorithm.Dispose() }
    }
    finally { $stream.Dispose() }
}

try {
    if (-not (Test-Path -LiteralPath $OfficialInstallerPath -PathType Leaf)) {
        throw "Official Inno Setup 6.7.3 installer was not found: $OfficialInstallerPath"
    }

    Assert-NoReparseComponent -LiteralPath $OfficialInstallerPath
    $installerFullPath = (Get-Item -Force -LiteralPath $OfficialInstallerPath).FullName
    $actualSha256 = Get-Sha256 -LiteralPath $installerFullPath
    if ($actualSha256 -cne $officialSha256) {
        throw "Official Inno Setup 6.7.3 SHA-256 mismatch. Expected $officialSha256 for $officialUri; got $actualSha256. The file was not executed."
    }

    $repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $innoScript = Join-Path $repositoryRoot 'packaging\LumaTherm.iss'
    if (-not (Test-Path -LiteralPath $innoScript -PathType Leaf)) {
        throw "The actual LumaTherm Inno script was not found: $innoScript"
    }

    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar)
    Assert-NoReparseComponent -LiteralPath $tempRoot
    $validationRoot = Join-Path $tempRoot ("LumaTherm-inno-validation-{0}" -f [Guid]::NewGuid().ToString('N'))
    Assert-ContainedPath -Child $validationRoot -Parent $tempRoot
    [System.IO.Directory]::CreateDirectory($validationRoot) | Out-Null

    $ownedInstaller = Join-Path $validationRoot 'innosetup-6.7.3.exe'
    [System.IO.File]::Copy($installerFullPath, $ownedInstaller, $false)
    Assert-ContainedPath -Child $ownedInstaller -Parent $validationRoot
    Assert-NoReparseComponent -LiteralPath $ownedInstaller
    $ownedInstallerSha256 = Get-Sha256 -LiteralPath $ownedInstaller
    if ($ownedInstallerSha256 -cne $officialSha256) {
        throw 'The official Inno Setup artifact changed while it was copied into the owned validation root. Nothing was executed.'
    }

    $compilerRoot = Join-Path $validationRoot 'compiler'
    $payloadRoot = Join-Path $validationRoot 'payload'
    $outputRoot = Join-Path $validationRoot 'output'
    foreach ($ownedPath in @($compilerRoot, $payloadRoot, $outputRoot)) {
        Assert-ContainedPath -Child $ownedPath -Parent $validationRoot
        [System.IO.Directory]::CreateDirectory($ownedPath) | Out-Null
    }
    Set-Content -LiteralPath (Join-Path $payloadRoot 'compile-probe.txt') -Encoding Ascii -NoNewline `
        -Value 'compile-only validation payload; the produced installer must never be executed'

    $portableLog = Join-Path $validationRoot 'portable-extraction.log'
    $portableArguments = '/PORTABLE=1 /CURRENTUSER /VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /DIR="{0}" /TASKS="" /LOG="{1}"' -f `
        $compilerRoot, $portableLog
    $portableProcess = Start-Process -FilePath $ownedInstaller -ArgumentList $portableArguments `
        -PassThru -WindowStyle Hidden
    if (-not $portableProcess.WaitForExit(120000)) {
        Stop-Process -Id $portableProcess.Id -Force -ErrorAction SilentlyContinue
        throw 'Official Inno Setup portable extraction exceeded its 120 second bound.'
    }
    $portableProcess.WaitForExit()
    $portableProcess.Refresh()
    if ($portableProcess.ExitCode -ne 0) {
        throw "Official Inno Setup portable extraction failed with exit code $($portableProcess.ExitCode)."
    }

    Assert-SafeTree -LiteralPath $validationRoot
    $uninstaller = Get-ChildItem -Force -Recurse -File -LiteralPath $compilerRoot -Filter 'unins*.exe' |
        Select-Object -First 1
    if ($null -ne $uninstaller) {
        throw "Portable compiler extraction unexpectedly created an uninstaller: $($uninstaller.FullName)"
    }

    $iscc = Join-Path $compilerRoot 'ISCC.exe'
    if (-not (Test-Path -LiteralPath $iscc -PathType Leaf)) {
        throw "The hash-verified Inno Setup distribution did not provide ISCC.exe."
    }
    Assert-ContainedPath -Child $iscc -Parent $validationRoot
    $compilerSha256 = Get-Sha256 -LiteralPath $iscc

    $compilerStdOut = Join-Path $validationRoot 'iscc.stdout.txt'
    $compilerStdErr = Join-Path $validationRoot 'iscc.stderr.txt'
    $compilerArguments = '/Qp /O"{0}" /D"PayloadRoot={1}" "{2}"' -f $outputRoot, $payloadRoot, $innoScript
    $compilerStartInfo = New-Object System.Diagnostics.ProcessStartInfo
    $compilerStartInfo.FileName = $iscc
    $compilerStartInfo.Arguments = $compilerArguments
    $compilerStartInfo.WorkingDirectory = $repositoryRoot
    $compilerStartInfo.UseShellExecute = $false
    $compilerStartInfo.CreateNoWindow = $true
    $compilerStartInfo.RedirectStandardOutput = $true
    $compilerStartInfo.RedirectStandardError = $true
    $compilerProcess = New-Object System.Diagnostics.Process
    $compilerProcess.StartInfo = $compilerStartInfo
    if (-not $compilerProcess.Start()) {
        throw 'The hash-verified ISCC process could not be started.'
    }
    $compilerOutputTask = $compilerProcess.StandardOutput.ReadToEndAsync()
    $compilerErrorTask = $compilerProcess.StandardError.ReadToEndAsync()
    if (-not $compilerProcess.WaitForExit(120000)) {
        Stop-Process -Id $compilerProcess.Id -Force -ErrorAction SilentlyContinue
        throw 'Official ISCC compilation exceeded its 120 second bound.'
    }
    $compilerProcess.WaitForExit()
    $compilerOutput = $compilerOutputTask.Result
    $compilerError = $compilerErrorTask.Result
    Set-Content -NoNewline -Encoding UTF8 -LiteralPath $compilerStdOut -Value $compilerOutput
    Set-Content -NoNewline -Encoding UTF8 -LiteralPath $compilerStdErr -Value $compilerError
    $compilerExitCode = $compilerProcess.ExitCode
    if ($compilerExitCode -ne 0) {
        throw "Official ISCC failed to compile the actual LumaTherm.iss with exit code ${compilerExitCode}: $compilerOutput$compilerError"
    }

    $expectedSetup = Join-Path $outputRoot $expectedSetupName
    if (-not (Test-Path -LiteralPath $expectedSetup -PathType Leaf)) {
        throw "Official ISCC reported success but the expected setup artifact was not produced: $expectedSetupName"
    }
    $unexpectedOutput = Get-ChildItem -Force -File -LiteralPath $outputRoot | Where-Object { $_.Name -cne $expectedSetupName }
    if ($unexpectedOutput) {
        throw "Official ISCC produced unexpected public artifacts: $($unexpectedOutput.Name -join ', ')"
    }

    $validationResult = [ordered]@{
        officialUri = $officialUri
        expectedSha256 = $officialSha256
        actualSha256 = $actualSha256
        compiledScript = 'packaging/LumaTherm.iss'
        compilerProvenance = 'ISCC.exe extracted from the pinned hash-verified official distribution'
        compilerSha256 = $compilerSha256
        expectedSetupArtifact = $expectedSetupName
        setupArtifactVerified = $true
        setupArtifactExecuted = $false
        temporaryRootCleaned = $false
    }
}
finally {
    foreach ($ownedProcess in @($compilerProcess, $portableProcess)) {
        if ($null -ne $ownedProcess) {
            try {
                if (-not $ownedProcess.HasExited) {
                    Stop-Process -Id $ownedProcess.Id -Force -ErrorAction SilentlyContinue
                    $ownedProcess.WaitForExit(10000) | Out-Null
                }
            }
            catch { }
        }
    }
    if ($validationRoot -and (Test-Path -LiteralPath $validationRoot)) {
        $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar)
        Assert-ContainedPath -Child $validationRoot -Parent $tempRoot
        if ([System.IO.Path]::GetFileName($validationRoot) -notlike 'LumaTherm-inno-validation-*') {
            throw "Refusing to clean an unexpected compiler validation path: $validationRoot"
        }
        Assert-SafeTree -LiteralPath $validationRoot
        Remove-Item -Force -Recurse -LiteralPath $validationRoot
    }
}

if ($null -ne $validationResult) {
    if (Test-Path -LiteralPath $validationRoot) {
        throw "Compiler validation temporary root was not removed: $validationRoot"
    }
    $validationResult.temporaryRootCleaned = $true
    [pscustomobject]$validationResult | ConvertTo-Json -Depth 3
}
