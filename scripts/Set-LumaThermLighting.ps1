[CmdletBinding()]
param(
    [ValidateSet('Configure', 'Restore')][string]$Action = 'Configure',
    [Parameter(Mandatory = $true)][string]$FamilyName,
    [string]$RegistryRootForTest,
    [switch]$BackupFailureForTest,
    [switch]$InterruptAfterFirstWriteForTest,
    [ValidateSet('Global', 'Device')][string]$InterruptTargetForTest = 'Global'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($FamilyName -cnotmatch '^LumaTherm_[a-z0-9]{13}$') { throw 'Unsupported lighting package family.' }
$lightingRoot = 'Software\Microsoft\Lighting'
$backupRoot = 'Software\LumaTherm\Installation'
if ($RegistryRootForTest -or $BackupFailureForTest -or $InterruptAfterFirstWriteForTest -or $InterruptTargetForTest -ne 'Global') {
    if ($env:LUMATHERM_PACKAGING_TEST -ne '1' -or $RegistryRootForTest -notmatch '^Software\\LumaTherm.Tests\\[a-f0-9]{32}$') {
        throw 'Lighting test overrides require an isolated test registry root and LUMATHERM_PACKAGING_TEST=1.'
    }
    $lightingRoot = $RegistryRootForTest
    $backupRoot = $RegistryRootForTest + '\Installation'
}
$userRoot = [Microsoft.Win32.Registry]::CurrentUser
function Write-Backup($json) {
    if ($null -eq $json) {
        $key = $userRoot.OpenSubKey($backupRoot, $true)
        try { if ($null -ne $key) { $key.DeleteValue('LightingBackup', $false) } }
        finally { if ($null -ne $key) { $key.Dispose() } }
    } else {
        $key = $userRoot.CreateSubKey($backupRoot, $true)
        try { $key.SetValue('LightingBackup', [string]$json, [Microsoft.Win32.RegistryValueKind]::String) }
        finally { $key.Dispose() }
    }
}
function Is-Index([string]$name) {
    $number = 0
    return [int]::TryParse($name, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref]$number) -and $number -gt 0 -and $number -le 128
}
function Read-Preference([string]$path, [string]$name) {
    $key = $userRoot.OpenSubKey($path)
    try {
        if ($null -eq $key -or $null -eq $key.GetValue($name)) { return $null }
        $kind = $key.GetValueKind($name).ToString()
        if ($kind -notin @('String', 'DWord')) { throw 'Unrecognized Windows lighting preference type.' }
        return [pscustomobject]@{ Kind = $kind; Text = [string]$key.GetValue($name) }
    } finally { if ($null -ne $key) { $key.Dispose() } }
}
function Same-Value($left, $right) {
    if ($null -eq $left -or $null -eq $right) { return $null -eq $left -and $null -eq $right }
    return $left.Kind -ceq $right.Kind -and $left.Text -ceq $right.Text
}
function Write-Preference([string]$path, [string]$name, $value) {
    if ($null -eq $value) {
        $key = $userRoot.OpenSubKey($path, $true)
        try { if ($null -ne $key) { $key.DeleteValue($name, $false) } }
        finally { if ($null -ne $key) { $key.Dispose() } }
        return
    }
    $key = $userRoot.CreateSubKey($path, $true)
    try {
        if ($value.Kind -ceq 'String') { $key.SetValue($name, [string]$value.Text, [Microsoft.Win32.RegistryValueKind]::String) }
        else { $key.SetValue($name, [int]$value.Text, [Microsoft.Win32.RegistryValueKind]::DWord) }
    } finally { $key.Dispose() }
}
function Valid-Value($value, [bool]$providers) {
    if ($null -eq $value) { return $true }
    if ($providers) { return $value.Kind -ceq 'String' -and -not [string]::IsNullOrWhiteSpace($value.Text) }
    $number = 0
    return $value.Kind -ceq 'DWord' -and [int]::TryParse($value.Text, [ref]$number)
}
function Apply-Changes($changes, [string]$target) {
    foreach ($change in $changes) {
        Write-Preference $change.Key $change.Name $change.$target
        Interrupt-ForTest $change.Key
    }
}
function Interrupt-ForTest([string]$path) {
    $target = if ($InterruptTargetForTest -ceq 'Global') { $lightingRoot + '\Providers' } else { $lightingRoot + '\Devices\device-a\Providers' }
    if ($InterruptAfterFirstWriteForTest -and $path -ceq $target) { [Environment]::Exit(42) }
}
$previous = $null
$previousJson = $null
$backupKey = $userRoot.OpenSubKey($backupRoot)
try {
    if ($null -ne $backupKey -and $null -ne $backupKey.GetValue('LightingBackup')) {
        if ($backupKey.GetValueKind('LightingBackup') -ne [Microsoft.Win32.RegistryValueKind]::String) { throw 'Unsupported lighting backup type.' }
        $previousJson = [string]$backupKey.GetValue('LightingBackup')
    }
} finally { if ($null -ne $backupKey) { $backupKey.Dispose() } }
if ($null -ne $previousJson) {
    if ($previousJson.Length -gt 1048576) { throw 'Lighting installation backup is too large.' }
    $previous = $previousJson | ConvertFrom-Json
    if ($previous.Family -cne $FamilyName -or $null -eq $previous.Changes) { throw 'Lighting installation backup belongs to another identity.' }
    $pending = $previous.PSObject.Properties['Pending']
    $validatedChanges = @($previous.Changes)
    if ($null -ne $pending -and $null -ne $pending.Value) {
        if ($pending.Value.Direction -cnotin @('Configure', 'Restore')) { throw 'Unsupported lighting recovery direction.' }
        $validatedChanges += @($pending.Value.Changes)
    }
    foreach ($change in $validatedChanges) {
        $relative = ''
        if ($change.Key.StartsWith($lightingRoot + '\', [StringComparison]::Ordinal)) { $relative = $change.Key.Substring($lightingRoot.Length + 1) }
        $isRoot = $change.Key -ceq $lightingRoot
        $isDevice = $relative -cmatch '^Devices\\[^\\]+$'
        $isProviders = $relative -ceq 'Providers' -or $relative -cmatch '^Devices\\[^\\]+\\Providers$'
        if (-not ($isRoot -or $isDevice -or $isProviders) -or
            ($isProviders -and -not (Is-Index $change.Name)) -or
            (-not $isProviders -and $change.Name -cnotin @('AmbientLightingEnabled', 'ControlledByForegroundApp')) -or
            -not (Valid-Value $change.Before $isProviders) -or -not (Valid-Value $change.After $isProviders)) {
            throw 'Lighting installation backup contains an unsupported preference.'
        }
    }
    if ($null -ne $pending -and $null -ne $pending.Value) {
        # A previous process may have ended between writes. Recover only values
        # belonging to that transaction; preserve the backup on external conflict.
        foreach ($group in @($pending.Value.Changes | Group-Object Key)) {
            foreach ($change in $group.Group) {
                $current = Read-Preference $change.Key $change.Name
                if (-not (Same-Value $current $change.Before) -and -not (Same-Value $current $change.After)) { throw 'Lighting recovery conflicts with a subsequent preference change; backup retained.' }
            }
            if ($group.Name.EndsWith('\Providers', [StringComparison]::Ordinal)) {
                $key = $userRoot.OpenSubKey($group.Name)
                try { $names = @(); if ($null -ne $key) { $names = @($key.GetValueNames() | Where-Object { Is-Index $_ }) } }
                finally { if ($null -ne $key) { $key.Dispose() } }
                $allowed = @($group.Group | ForEach-Object { $_.Name })
                if (@($names | Where-Object { $_ -cnotin $allowed }).Count -gt 0) { throw 'Lighting recovery conflicts with a subsequent provider change; backup retained.' }
            }
        }
        $target = if ($pending.Value.Direction -ceq 'Configure') { 'Before' } else { 'After' }
        Apply-Changes $pending.Value.Changes $target
        $previous.Pending = $null
        $previousJson = $previous | ConvertTo-Json -Depth 7
        Write-Backup $previousJson
    }
}
if ($Action -eq 'Restore') {
    if ($null -eq $previous) { return }
    $restoreChanges = [Collections.Generic.List[object]]::new()
    foreach ($group in @($previous.Changes | Group-Object Key)) {
        if ($group.Name.EndsWith('\Providers', [StringComparison]::Ordinal)) {
            $key = $userRoot.OpenSubKey($group.Name)
            try { $names = @(); if ($null -ne $key) { $names = @($key.GetValueNames() | Where-Object { Is-Index $_ }) } }
            finally { if ($null -ne $key) { $key.Dispose() } }
            $expected = @($group.Group | Where-Object { $null -ne $_.After } | ForEach-Object { $_.Name })
            $different = @($group.Group | Where-Object { -not (Same-Value (Read-Preference $_.Key $_.Name) $_.After) })
            if ($names.Count -ne $expected.Count -or @($names | Where-Object { $_ -cnotin $expected }).Count -gt 0 -or $different.Count -gt 0) { continue }
            foreach ($change in $group.Group) { $restoreChanges.Add([pscustomobject]@{ Key = $change.Key; Name = $change.Name; Before = (Read-Preference $change.Key $change.Name); After = $change.Before }) }
        } else {
            foreach ($change in $group.Group) {
                if (Same-Value (Read-Preference $change.Key $change.Name) $change.After) { $restoreChanges.Add([pscustomobject]@{ Key = $change.Key; Name = $change.Name; Before = $change.After; After = $change.Before }) }
            }
        }
    }
    $transaction = [pscustomobject]@{ Family = $FamilyName; Changes = $previous.Changes; Pending = [pscustomobject]@{ Direction = 'Restore'; Changes = @($restoreChanges) } }
    Write-Backup ($transaction | ConvertTo-Json -Depth 7)
    Apply-Changes $restoreChanges 'After'
    Write-Backup $null
    return
}
$roots = @($lightingRoot)
$devices = $userRoot.OpenSubKey($lightingRoot + '\Devices')
try { if ($null -ne $devices) { $roots += @($devices.GetSubKeyNames() | ForEach-Object { $lightingRoot + '\Devices\' + $_ }) } }
finally { if ($null -ne $devices) { $devices.Dispose() } }
$changes = [Collections.Generic.List[object]]::new()
foreach ($root in $roots) {
    foreach ($name in @('AmbientLightingEnabled', 'ControlledByForegroundApp')) {
        $changes.Add([pscustomobject]@{ Key = $root; Name = $name; Before = (Read-Preference $root $name); After = [pscustomobject]@{ Kind = 'DWord'; Text = $(if ($name -eq 'AmbientLightingEnabled') { '1' } else { '0' }) } })
    }
    $path = $root + '\Providers'
    $key = $userRoot.OpenSubKey($path)
    try { $names = @(); if ($null -ne $key) { $names = @($key.GetValueNames() | Where-Object { Is-Index $_ } | Sort-Object { [int]$_ }) } }
    finally { if ($null -ne $key) { $key.Dispose() } }
    $order = [Collections.Generic.List[string]]::new()
    foreach ($name in $names) {
        $value = Read-Preference $path $name
        if ($value.Kind -cne 'String' -or [string]::IsNullOrWhiteSpace($value.Text)) { throw 'Unrecognized Windows lighting provider format.' }
        if ($value.Text -cne $FamilyName) { $order.Add($value.Text) }
    }
    if ($order.Count -eq 0) { $order.Add('WindowsLighting') }
    $order.Insert(0, $FamilyName)
    if ($order.Count -gt 128) { throw 'Windows lighting provider list is too large.' }
    $planned = @(1..$order.Count | ForEach-Object { [string]$_ })
    foreach ($name in @(@($names) + @($planned) | Select-Object -Unique)) {
        $after = $null
        if ([int]$name -le $order.Count) { $after = [pscustomobject]@{ Kind = 'String'; Text = $order[[int]$name - 1] } }
        $changes.Add([pscustomobject]@{ Key = $path; Name = $name; Before = (Read-Preference $path $name); After = $after })
    }
}
$saved = @{}
if ($null -ne $previous) { foreach ($change in $previous.Changes) { $saved[$change.Key + '|' + $change.Name] = $change } }
$rebasedProviders = @{}
if ($null -ne $previous) {
    foreach ($group in @($previous.Changes | Group-Object Key)) {
        if (-not $group.Name.EndsWith('\Providers', [StringComparison]::Ordinal)) { continue }
        $key = $userRoot.OpenSubKey($group.Name)
        try { $currentNames = @(); if ($null -ne $key) { $currentNames = @($key.GetValueNames() | Where-Object { Is-Index $_ } | Sort-Object { [int]$_ }) } }
        finally { if ($null -ne $key) { $key.Dispose() } }
        $expectedNames = @($group.Group | Where-Object { $null -ne $_.After } | ForEach-Object { $_.Name })
        $different = @($group.Group | Where-Object { -not (Same-Value (Read-Preference $_.Key $_.Name) $_.After) })
        if ($currentNames.Count -eq $expectedNames.Count -and @($currentNames | Where-Object { $_ -cnotin $expectedNames }).Count -eq 0 -and $different.Count -eq 0) { continue }
        $baseline = @{}
        $index = 1
        foreach ($name in $currentNames) {
            $value = Read-Preference $group.Name $name
            if ($value.Text -cne $FamilyName) { $baseline[[string]$index] = $value; $index++ }
        }
        $rebasedProviders[$group.Name] = $baseline
        foreach ($change in $group.Group) { $saved.Remove($change.Key + '|' + $change.Name) }
    }
}
foreach ($change in $changes) {
    $id = $change.Key + '|' + $change.Name
    $original = $change.Before
    if ($rebasedProviders.ContainsKey($change.Key)) { $original = $rebasedProviders[$change.Key][$change.Name] }
    elseif ($saved.ContainsKey($id) -and (Same-Value $change.Before $saved[$id].After)) { $original = $saved[$id].Before }
    $saved[$id] = [pscustomobject]@{ Key = $change.Key; Name = $change.Name; Before = $original; After = $change.After }
}
$written = [Collections.Generic.List[object]]::new()
try {
    # Persist recovery information before changing Windows preferences. HKCU avoids
    # privileged filesystem writes through a user-controlled backup path.
    if ($BackupFailureForTest) { throw 'Injected registry backup failure for test.' }
    $transaction = [pscustomobject]@{ Family = $FamilyName; Changes = @($saved.Values); Pending = [pscustomobject]@{ Direction = 'Configure'; Changes = @($changes) } }
    $json = $transaction | ConvertTo-Json -Depth 7
    Write-Backup $json
    foreach ($change in $changes) {
        $written.Add($change); Write-Preference $change.Key $change.Name $change.After
        Interrupt-ForTest $change.Key
    }
    foreach ($change in $changes) {
        if (-not (Same-Value (Read-Preference $change.Key $change.Name) $change.After)) { throw 'Windows lighting preferences failed verification.' }
    }
    $transaction.Pending = $null
    Write-Backup ($transaction | ConvertTo-Json -Depth 7)
} catch {
    for ($i = $written.Count - 1; $i -ge 0; $i--) { $change = $written[$i]; Write-Preference $change.Key $change.Name $change.Before }
    if (-not $BackupFailureForTest) { Write-Backup $previousJson }
    throw
}
