# Find a reliable 'seconds since player logged in / spawned' signal. Read-only Cecil introspection.
# Candidates: Player fields matching time/spawn/login, or a spawn timestamp we can diff against.
$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\ValheimServer\server\BepInEx\core\Mono.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('C:\ValheimServer\server\valheim_server_Data\Managed\assembly_valheim.dll')
$player = $asm.MainModule.GetType('Player')

Write-Host "=== Player fields with 'time' or 'spawn' in the name ===" -ForegroundColor Cyan
foreach ($f in $player.Fields | Where-Object { $_.Name -match 'time|spawn|login|first' }) {
    Write-Host "  $($f.Name) : $($f.FieldType.Name) (static=$($f.IsStatic), public=$($f.IsPublic))"
}

Write-Host ""
Write-Host "=== All Player fields of type float/double (potential timers) ===" -ForegroundColor Cyan
foreach ($f in $player.Fields | Where-Object { $_.FieldType.Name -eq 'Single' -or $_.FieldType.Name -eq 'Double' }) {
    Write-Host "  $($f.Name) : $($f.FieldType.Name) (public=$($f.IsPublic))"
}

Write-Host ""
Write-Host "=== Player methods mentioning Spawn/Login ===" -ForegroundColor Cyan
foreach ($m in $player.Methods | Where-Object { $_.Name -match 'Spawn|Login|OnSpawned' }) {
    Write-Host "  $($m.Name)($(($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ',')) -> $($m.ReturnType.Name)"
}

# Also check ZNet for a per-player join time or spawn timestamp.
Write-Host ""
Write-Host "=== ZNet: per-player join / spawn timestamp? ===" -ForegroundColor Cyan
$znet = $asm.MainModule.GetType('ZNet')
foreach ($f in $znet.Fields | Where-Object { $_.Name -match 'time|join|spawn|player' }) {
    Write-Host "  ZNet.$($f.Name) : $($f.FieldType.Name)"
}

# Character base may have a spawn time too.
Write-Host ""
Write-Host "=== Character fields with time/spawn ===" -ForegroundColor Cyan
$char = $asm.MainModule.GetType('Character')
foreach ($f in $char.Fields | Where-Object { $_.Name -match 'time|spawn' }) {
    Write-Host "  Character.$($f.Name) : $($f.FieldType.Name) (public=$($f.IsPublic))"
}

$asm.Dispose()
Write-Host "=== DONE ==="
