# Verify how m_foods behaves (shrinks on expire vs fixed-size-with-nulls) by reading UpdateFood IL
# around the RemoveAt / Count calls. Read-only Cecil introspection.
$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\ValheimServer\server\BepInEx\core\Mono.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('C:\ValheimServer\server\valheim_server_Data\Managed\assembly_valheim.dll')
$player = $asm.MainModule.GetType('Player')

Write-Host "=== UpdateFood: every instruction touching m_foods (full context) ===" -ForegroundColor Cyan
$m = $player.Methods | Where-Object { $_.Name -eq 'UpdateFood' }
$instr = $m.Body.Instructions
for ($i = 0; $i -lt $instr.Count; $i++) {
    $s = $instr[$i].ToString()
    if ($s -match 'm_foods|RemoveAt|Count|get_Item|Add') {
        # show a little surrounding context
        Write-Host "  $($instr[$i].Offset.ToString('X4')): $s"
    }
}

Write-Host ""
Write-Host "=== Player.m_maxFoods value (default) ===" -ForegroundColor Cyan
# It's a static field; try reading the default via the .cctor. Just report it's int.
$mf = $player.Fields | Where-Object { $_.Name -eq 'm_maxFoods' }
Write-Host "  m_maxFoods : $($mf.FieldType.FullName) (HasConstant=$($mf.HasConstant), Constant=$($mf.Constant))"

Write-Host ""
Write-Host "=== Player.GetFoods return + who reads it ===" -ForegroundColor Cyan
$g = $player.Methods | Where-Object { $_.Name -eq 'GetFoods' }
Write-Host "  GetFoods -> $($g.ReturnType.FullName)"

$asm.Dispose()
Write-Host "=== DONE ==="
