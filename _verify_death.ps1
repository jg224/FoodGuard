# Verify the death/respawn API surface on Player. Read-only Cecil introspection.
$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\ValheimServer\server\BepInEx\core\Mono.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('C:\ValheimServer\server\valheim_server_Data\Managed\assembly_valheim.dll')
$player = $asm.MainModule.GetType('Player')
$char = $asm.MainModule.GetType('Character')

Write-Host "=== Player/Character methods + props mentioning Dead/Death/Respawn/Spawn ===" -ForegroundColor Cyan
foreach ($t in @($char, $player)) {
    foreach ($m in $t.Methods | Where-Object { $_.Name -match 'Dead|Death|Respawn|Spawn|Alive' }) {
        Write-Host "  $($t.Name).$($m.Name)($(($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ',')) -> $($m.ReturnType.Name)"
    }
    foreach ($p in $t.Properties | Where-Object { $_.Name -match 'Dead|Death|Alive' }) {
        Write-Host "  $($t.Name).prop $($p.Name) : $($p.PropertyType.Name)"
    }
}

Write-Host ""
Write-Host "=== Player fields mentioning Dead/Death/Respawn ===" -ForegroundColor Cyan
foreach ($f in $player.Fields | Where-Object { $_.Name -match 'Dead|Death|Respawn|Spawn' }) {
    Write-Host "  Player.$($f.Name) : $($f.FieldType.Name) (static=$($f.IsStatic))"
}

Write-Host ""
Write-Host "=== Player.GetMaxHealth / health fields (low HP = just respawned) ===" -ForegroundColor Cyan
foreach ($m in $char.Methods | Where-Object { $_.Name -match 'GetMaxHealth|GetHealth' }) {
    Write-Host "  Character.$($m.Name) -> $($m.ReturnType.Name)"
}

$asm.Dispose()
Write-Host "=== DONE ==="
