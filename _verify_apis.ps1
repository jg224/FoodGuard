# Cecil introspection of assembly_valheim.dll to verify FoodGuard API signatures.
# Read-only. Run after any Valheim update to confirm the mod's assumptions still hold.
# Mirrors the repo's _verify_apis.ps1 convention (nosmokeguard, sleepguard).
$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\ValheimServer\server\BepInEx\core\Mono.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('C:\ValheimServer\server\valheim_server_Data\Managed\assembly_valheim.dll')
$valheim = $asm.MainModule
function Get-Type($name) { return $valheim.GetType($name) }
function Dump($label, $member) {
    if ($member -eq $null) { Write-Host "  [MISSING] $label" -ForegroundColor Yellow }
    else { Write-Host "  [OK] $label -> $($member.FullName)" -ForegroundColor Green }
}

Write-Host "=== Player food API ==="
$player = Get-Type 'Player'
Dump 'Player.m_foods (field)' ($player.Fields | Where-Object { $_.Name -eq 'm_foods' })
Dump 'Player.m_localPlayer (static)' ($player.Fields | Where-Object { $_.Name -eq 'm_localPlayer' })
Dump 'Player.m_maxFoods (static)' ($player.Fields | Where-Object { $_.Name -eq 'm_maxFoods' })
$food = Get-Type 'Player/Food'
foreach ($f in $food.Fields) { Write-Host "    Player/Food.$($f.Name) : $($f.FieldType.FullName)" }
$shared = Get-Type 'ItemDrop/ItemData/SharedData'
Dump 'SharedData.m_foodBurnTime' ($shared.Fields | Where-Object { $_.Name -eq 'm_foodBurnTime' })

Write-Host ""
Write-Host "=== EffectArea (base zone) ==="
$ea = Get-Type 'EffectArea'
Dump 'EffectArea.IsPointInsideArea (static)' ($ea.Methods | Where-Object { $_.Name -eq 'IsPointInsideArea' -and $_.IsStatic })
$eaType = Get-Type 'EffectArea/Type'
foreach ($v in $eaType.Fields | Where-Object { $_.IsLiteral }) {
    Write-Host "    EffectArea/Type.$($v.Name) = $($v.Constant)"
}

Write-Host ""
Write-Host "=== Character / BaseAI (combat scan) ==="
$char = Get-Type 'Character'
Dump 'Character.GetAllCharacters (static)' ($char.Methods | Where-Object { $_.Name -eq 'GetAllCharacters' -and $_.IsStatic })
Dump 'Character.m_baseAI (field)' ($char.Fields | Where-Object { $_.Name -eq 'm_baseAI' })
$bai = Get-Type 'BaseAI'
Dump 'BaseAI.GetTargetCreature' ($bai.Methods | Where-Object { $_.Name -eq 'GetTargetCreature' })

Write-Host ""
Write-Host "=== MessageHud (popup) ==="
$mh = Get-Type 'MessageHud'
Dump 'MessageHud.instance (property)' ($mh.Properties | Where-Object { $_.Name -eq 'instance' })
Dump 'MessageHud.ShowMessage' ($mh.Methods | Where-Object { $_.Name -eq 'ShowMessage' })

Write-Host ""
Write-Host "=== ZNetScene (SFX lookup) ==="
$zs = Get-Type 'ZNetScene'
Dump 'ZNetScene.GetPrefab(int)' ($zs.Methods | Where-Object { $_.Name -eq 'GetPrefab' -and $_.Parameters.Count -eq 1 })
Dump 'ZNetScene.instance (property)' ($zs.Properties | Where-Object { $_.Name -eq 'instance' })

Write-Host ""
Write-Host "=== StringExtension.GetStableHashCode (SFX hash) ==="
$se = Get-Type 'StringExtension'
if ($se) {
    $m = $se.Methods | Where-Object { $_.Name -eq 'GetStableHashCode' }
    Dump 'StringExtension.GetStableHashCode' $m
    Write-Host "    (extension method on System.String -- call as name.GetStableHashCode())"
} else { Write-Host "  [MISSING] StringExtension type" -ForegroundColor Yellow }

$asm.Dispose()
Write-Host ""
Write-Host "=== DONE ==="
