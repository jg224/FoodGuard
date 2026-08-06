# Find the actual StatusEffectManager type name + the field on Player, plus the canonical Rested
# effect name/hash. Read-only Cecil introspection.
$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\ValheimServer\server\BepInEx\core\Mono.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('C:\ValheimServer\server\valheim_server_Data\Managed\assembly_valheim.dll')
$mod = $asm.MainModule

Write-Host "=== All types with 'Status' or 'SE_' in the name ===" -ForegroundColor Cyan
foreach ($t in $mod.Types) {
    if ($t.Name -match 'Status|SE_|SEMan') { Write-Host "  $($t.FullName)" }
}

Write-Host ""
Write-Host "=== Player fields of class type (looking for the SEM field) ===" -ForegroundColor Cyan
$player = $mod.GetType('Player')
foreach ($f in $player.Fields) {
    if (-not $f.FieldType.IsPrimitive -and -not $f.FieldType.IsValueType) {
        Write-Host "  Player.$($f.Name) : $($f.FieldType.Name)"
    }
}

Write-Host ""
Write-Host "=== SE_Man (if found): HaveStatusEffect variants ===" -ForegroundColor Cyan
$sem = $mod.Types | Where-Object { $_.Name -eq 'SE_Man' } | Select-Object -First 1
if ($sem) {
    Write-Host "  Found: $($sem.FullName)"
    foreach ($m in $sem.Methods | Where-Object { $_.Name -match 'HaveStatus|GetStatus|ListStatus' }) {
        $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
        Write-Host "  $($m.Name)($ps) -> $($m.ReturnType.Name)"
    }
    Write-Host "  m_statusEffects list field:"
    foreach ($f in $sem.Fields | Where-Object { $_.Name -match 'm_status|m_effect' }) {
        Write-Host "    $($f.Name) : $($f.FieldType.FullName)"
    }
} else {
    Write-Host "  SE_Man NOT found by exact name; dumping all SE_* types above."
}

Write-Host ""
Write-Host "=== StatusEffect subclasses named like Rested ===" -ForegroundColor Cyan
$se = $mod.GetType('StatusEffect')
if ($se) {
    # subclasses
    foreach ($t in $mod.Types) {
        if ($t.BaseType -and $t.BaseType.Name -eq 'StatusEffect') {
            if ($t.Name -match 'Rested|Camp|Cold|Wet|Rest') { Write-Host "  $($t.FullName) : base $($t.BaseType.Name)" }
        }
    }
}

Write-Host ""
Write-Host "=== ObjectDB.GetStatusEffect + a known Rested hash probe ===" -ForegroundColor Cyan
$od = $mod.GetType('ObjectDB')
foreach ($m in $od.Methods | Where-Object { $_.Name -eq 'GetStatusEffect' }) {
    $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', '
    Write-Host "  ObjectDB.$($m.Name)($ps) -> $($m.ReturnType.Name)"
}

$asm.Dispose()
Write-Host "=== DONE ==="
