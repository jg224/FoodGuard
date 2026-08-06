# Verify the status-effect API for a 'is the player Rested?' check. Read-only Cecil introspection.
$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\ValheimServer\server\BepInEx\core\Mono.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('C:\ValheimServer\server\valheim_server_Data\Managed\assembly_valheim.dll')
$mod = $asm.MainModule
function Get-Type($n) { return $mod.GetType($n) }

Write-Host "=== Player: how to reach the StatusEffectManager ===" -ForegroundColor Cyan
$player = Get-Type 'Player'
foreach ($f in $player.Fields | Where-Object { $_.Name -match 'seman|StatusEffect' }) {
    Write-Host "  Player.$($f.Name) : $($f.FieldType.FullName)"
}
foreach ($m in $player.Methods | Where-Object { $_.Name -match 'GetSEMan|HaveStatus|StatusEffect' }) {
    Write-Host "  Player.$($m.Name)($(($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ',')) -> $($m.ReturnType.Name)"
}

Write-Host ""
Write-Host "=== StatusEffectManager (SE_Man) query methods ===" -ForegroundColor Cyan
$sem = Get-Type 'SE_Man'
if ($sem) {
    foreach ($m in $sem.Methods | Where-Object { $_.Name -match 'HaveStatus|GetStatus|HaveEffect' }) {
        $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
        Write-Host "  SE_Man.$($m.Name)($ps) -> $($m.ReturnType.Name)"
    }
    Write-Host "  SE_Man fields holding active effects:"
    foreach ($f in $sem.Fields | Where-Object { $_.FieldType.Name -match 'List|Dictionary' }) {
        Write-Host "    $($f.Name) : $($f.FieldType.FullName)"
    }
}

Write-Host ""
Write-Host "=== StatusEffect base: how names/hashes work ===" -ForegroundColor Cyan
$se = Get-Type 'StatusEffect'
if ($se) {
    foreach ($p in $se.Properties | Where-Object { $_.Name -match 'Name|Hash' }) {
        Write-Host "  StatusEffect.$($p.Name) : $($p.PropertyType.FullName)"
    }
    foreach ($f in $se.Fields | Where-Object { $_.Name -match 'm_name|m_hash|^name$' }) {
        Write-Host "  StatusEffect.$($f.Name) : $($f.FieldType.FullName)"
    }
}

Write-Host ""
Write-Host "=== Confirm a 'Rested' effect exists in the object DB prefab set ===" -ForegroundColor Cyan
# ObjectDB.GetStatusEffect(string) is the canonical name-based lookup if present.
$od = Get-Type 'ObjectDB'
if ($od) {
    foreach ($m in $od.Methods | Where-Object { $_.Name -match 'StatusEffect|GetStatus' }) {
        $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', '
        Write-Host "  ObjectDB.$($m.Name)($ps) -> $($m.ReturnType.Name)"
    }
}

$asm.Dispose()
Write-Host "=== DONE ==="
