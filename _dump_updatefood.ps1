# Dump the FULL IL of Player.UpdateFood so we can see exactly how a food slot is expired/removed.
$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\ValheimServer\server\BepInEx\core\Mono.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('C:\ValheimServer\server\valheim_server_Data\Managed\assembly_valheim.dll')
$player = $asm.MainModule.GetType('Player')
$m = $player.Methods | Where-Object { $_.Name -eq 'UpdateFood' }
Write-Host "=== Full IL of UpdateFood ($($m.Body.Instructions.Count) instructions) ===" -ForegroundColor Cyan
foreach ($ins in $m.Body.Instructions) {
    Write-Host "  $($ins.Offset.ToString('X4')): $($ins.ToString())"
}
$asm.Dispose()
