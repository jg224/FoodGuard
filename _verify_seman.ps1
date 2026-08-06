# Confirm SEMan query API + the m_seman field on Character, and inspect SE_Rested's name field.
$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\ValheimServer\server\BepInEx\core\Mono.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('C:\ValheimServer\server\valheim_server_Data\Managed\assembly_valheim.dll')
$mod = $asm.MainModule

Write-Host "=== SEMan public query methods ===" -ForegroundColor Cyan
$sem = $mod.GetType('SEMan')
foreach ($m in $sem.Methods | Where-Object { $_.IsPublic -and ($_.Name -match 'HaveStatus|GetStatus|ListStatus|HasStatus') }) {
    $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', '
    Write-Host "  SEMan.$($m.Name)($ps) -> $($m.ReturnType.Name)"
}
Write-Host "  SEMan.m_statusEffects field:"
foreach ($f in $sem.Fields | Where-Object { $_.Name -match 'statusEffect' }) {
    Write-Host "    $($f.Name) : $($f.FieldType.FullName) (public=$($f.IsPublic))"
}

Write-Host ""
Write-Host "=== Character.m_seman field ===" -ForegroundColor Cyan
$char = $mod.GetType('Character')
$sef = $char.Fields | Where-Object { $_.Name -match 'seman|SEMan' }
foreach ($f in $sef) { Write-Host "  Character.$($f.Name) : $($f.FieldType.FullName) (public=$($f.IsPublic))" }

Write-Host ""
Write-Host "=== SE_Rested name (the prefab name string the effect uses) ===" -ForegroundColor Cyan
$ser = $mod.GetType('SE_Rested')
if ($ser) {
    Write-Host "  SE_Rested base: $($ser.BaseType.FullName)"
    # Inherited m_name field from StatusEffect; check if SE_Rested sets a default via .cctor or attributes
}

# StatusEffect.m_name is the string used at runtime; the prefab name in ObjectDB is the lookup key.
# The canonical Valheim 'Rested' effect prefab name is literally "Rested". Confirm GetStatusEffect
# is the lookup we use (hash of the name string).
Write-Host ""
Write-Host "=== StatusEffect.m_name / m_nameHash fields ===" -ForegroundColor Cyan
$se = $mod.GetType('StatusEffect')
foreach ($f in $se.Fields | Where-Object { $_.Name -match 'm_name|m_nameHash' }) {
    Write-Host "  StatusEffect.$($f.Name) : $($f.FieldType.FullName) (public=$($f.IsPublic))"
}

$asm.Dispose()
Write-Host "=== DONE ==="
