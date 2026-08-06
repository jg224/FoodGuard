# Confirm the API for the marked-base feature: player transform (position), and how to persist a
# Vector3 + float to BepInEx config. Read-only Cecil introspection.
$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\ValheimServer\server\BepInEx\core\Mono.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('C:\ValheimServer\server\valheim_server_Data\Managed\assembly_valheim.dll')
$player = $asm.MainModule.GetType('Player')
$char = $asm.MainModule.GetType('Character')

Write-Host "=== Character/Player transform + position API ===" -ForegroundColor Cyan
foreach ($n in @('transform','GetComponent')) {
    $found = $char.Methods | Where-Object { $_.Name -eq $n }
    foreach ($m in $found) { Write-Host "  Character.$($m.Name) -> $($m.ReturnType.Name)" }
}
# transform is inherited from MonoBehaviour/Object; check the property
foreach ($p in $char.Properties) { if ($p.Name -eq 'transform' -or $p.Name -eq 'Transform') { Write-Host "  Character prop $($p.Name) : $($p.PropertyType.Name)" } }

Write-Host ""
Write-Host "=== Vector3 magnitude/distance helpers (for radius check) ===" -ForegroundColor Cyan
$v3 = $asm.MainModule.GetType('UnityEngine.Vector3')
if ($v3) {
    foreach ($m in $v3.Methods | Where-Object { $_.Name -in @('Distance','Magnitude','SqrMagnitude') }) {
        Write-Host "  Vector3.$($m.Name)($(($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ',')) -> $($m.ReturnType.Name) (static=$($m.IsStatic))"
    }
}

Write-Host ""
Write-Host "=== EffectArea.IsPointInsideArea (kept for optional ward-based fallback later) ===" -ForegroundColor Cyan
$ea = $asm.MainModule.GetType('EffectArea')
$m = $ea.Methods | Where-Object { $_.Name -eq 'IsPointInsideArea' -and $_.IsStatic }
if ($m) { Write-Host "  $([string]::Join(',', ($m.Parameters | ForEach-Object { $_.ParameterType.Name }))) -> $($m.ReturnType.Name)" }

$asm.Dispose()
Write-Host "=== DONE ==="
