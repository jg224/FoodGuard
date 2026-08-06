# Disassemble Player.UpdateFood to confirm m_time direction (counts up vs down), and look for
# the teleport-in-progress signal. Read-only Cecil introspection.
$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\ValheimServer\server\BepInEx\core\Mono.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('C:\ValheimServer\server\valheim_server_Data\Managed\assembly_valheim.dll')
$valheim = $asm.MainModule
function Get-Type($name) { return $valheim.GetType($name) }

Write-Host "=== Player.UpdateFood IL (how m_time changes) ===" -ForegroundColor Cyan
$player = Get-Type 'Player'
$m = $player.Methods | Where-Object { $_.Name -eq 'UpdateFood' }
if ($m) {
    Write-Host "Signature: UpdateFood($(($m.Parameters | ForEach-Object { $_.ParameterType.Name.ToString() + ' ' + $_.Name }) -join ', '))"
    Write-Host "IL bytes: $($m.Body.Instructions.Count) instructions"
    Write-Host "---- (filtered to m_time / m_foods / food refs) ----"
    foreach ($ins in $m.Body.Instructions) {
        $s = $ins.ToString()
        if ($s -match 'm_time|m_foods|m_foodBurnTime|RemoveAt|UpdateFood') {
            Write-Host "  $s"
        }
    }
}

Write-Host ""
Write-Host "=== Player methods/props mentioning teleport ===" -ForegroundColor Cyan
foreach ($mm in $player.Methods) {
    if ($mm.Name -match 'eleport') { Write-Host "  method $($mm.Name)($(($mm.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ',')) -> $($mm.ReturnType.Name)" }
}
foreach ($pp in $player.Properties) {
    if ($pp.Name -match 'eleport') { Write-Host "  prop $($pp.Name) : $($pp.PropertyType.Name)" }
}
foreach ($ff in $player.Fields) {
    if ($ff.Name -match 'eleport') { Write-Host "  field $($ff.Name) : $($ff.FieldType.Name)" }
}

Write-Host ""
Write-Host "=== Player fields that look like flags/state for transition ===" -ForegroundColor Cyan
foreach ($ff in $player.Fields) {
    if ($ff.Name -match 'eleport|Loading|Waiting|Transition|m_teleport|Spawn') {
        Write-Host "  field $($ff.Name) : $($ff.FieldType.Name) (static=$($ff.IsStatic))"
    }
}

Write-Host ""
Write-Host "=== Game/Hud teleport/transition signals ===" -ForegroundColor Cyan
foreach ($tname in @('Game','Hud')) {
    $t = Get-Type $tname
    if (-not $t) { continue }
    foreach ($ff in $t.Fields) {
        if ($ff.Name -match 'eleport|Loading|Transition') {
            Write-Host "  $tname field $($ff.Name) : $($ff.FieldType.Name) (static=$($ff.IsStatic))"
        }
    }
}

Write-Host ""
Write-Host "=== Player.Food.m_time read check: is it in UpdateFood's increment? ===" -ForegroundColor Cyan
# Confirm m_time is incremented by dt in UpdateFood (=> counts UP).
$food = Get-Type 'Player/Food'
Write-Host "  Player/Food.m_time field type: $(($food.Fields | Where-Object { $_.Name -eq 'm_time' }).FieldType.FullName)"

$asm.Dispose()
Write-Host ""
Write-Host "=== DONE ==="
