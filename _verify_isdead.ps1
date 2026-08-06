# Verify the death-detection path: what IsDead() checks, where m_dead is set, and when food is
# cleared relative to death. Read-only Cecil introspection.
$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\ValheimServer\server\BepInEx\core\Mono.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('C:\ValheimServer\server\valheim_server_Data\Managed\assembly_valheim.dll')
$mod = $asm.MainModule
function Get-Type($n) { return $mod.GetType($n) }

Write-Host "=== Character.IsDead() IL ===" -ForegroundColor Cyan
$char = Get-Type 'Character'
$id = $char.Methods | Where-Object { $_.Name -eq 'IsDead' }
foreach ($ins in $id.Body.Instructions) { Write-Host "  $($ins.ToString())" }

Write-Host ""
Write-Host "=== Where is m_dead SET? (Character.OnDeath) ===" -ForegroundColor Cyan
$od = $char.Methods | Where-Object { $_.Name -eq 'OnDeath' }
foreach ($ins in $od.Body.Instructions) {
    $s = $ins.ToString()
    if ($s -match 'm_dead|m_foods|Clear|Remove|set_Item') { Write-Host "  $s" }
}

Write-Host ""
Write-Host "=== Player.OnDeath() -- does it clear food here? ===" -ForegroundColor Cyan
$player = Get-Type 'Player'
$pod = $player.Methods | Where-Object { $_.Name -eq 'OnDeath' }
foreach ($ins in $pod.Body.Instructions) {
    $s = $ins.ToString()
    if ($s -match 'm_foods|m_dead|Clear|RemoveAt|MessageHud|ShowMessage') { Write-Host "  $s" }
}

Write-Host ""
Write-Host "=== m_timeSinceDeath: where updated / reset ===" -ForegroundColor Cyan
foreach ($t in @($player)) {
    foreach ($m in $t.Methods) {
        if ($m.Body -eq $null) { continue }
        foreach ($ins in $m.Body.Instructions) {
            if ($ins.Operand -ne $null -and $ins.Operand.ToString() -match 'm_timeSinceDeath') {
                # Only show stores (stfld) and the method context
                if ($ins.OpCode.Code.ToString() -match 'stfld|ldfld') {
                    Write-Host "  $($t.Name).$($m.Name): $($ins.OpCode.Name) m_timeSinceDeath"
                }
            }
        }
    }
}

$asm.Dispose()
Write-Host "=== DONE ==="
