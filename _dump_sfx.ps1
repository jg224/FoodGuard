# Dump every 'sfx_' string literal referenced in assembly_valheim.dll. These are guaranteed-valid
# networked prefab names (the game itself uses them), so any of them works as AlertSfxName.
# Read-only Cecil introspection.
$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\ValheimServer\server\BepInEx\core\Mono.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('C:\ValheimServer\server\valheim_server_Data\Managed\assembly_valheim.dll')
$mod = $asm.MainModule

# Collect every string literal starting with sfx_ from all method bodies + custom attributes.
$names = New-Object System.Collections.Generic.HashSet[string]
foreach ($t in $mod.Types) {
    foreach ($m in $t.Methods) {
        if ($m.Body -eq $null) { continue }
        foreach ($inst in $m.Body.Instructions) {
            if ($inst.OpCode.Code.ToString() -eq 'ldstr') {
                $s = [string]$inst.Operand
                if ($s -ne $null -and $s.StartsWith('sfx_')) { [void]$names.Add($s) }
            }
        }
    }
}

Write-Host "=== All sfx_ prefab names referenced in assembly_valheim.dll ===" -ForegroundColor Cyan
$sorted = $names | Sort-Object
$i = 0
foreach ($n in $sorted) {
    Write-Host "  $n"
    $i++
}
Write-Host ""
Write-Host "Total: $i distinct sfx_ names" -ForegroundColor Green

# Also surface the ones most likely to be good 'alert' sounds (loud/attention-grabbing).
Write-Host ""
Write-Host "=== Candidates that sound like alerts/attention ===" -ForegroundColor Yellow
foreach ($n in $sorted) {
    if ($n -match 'alert|warn|bell|horn|guard|alarm|notify|levelup|power|guardian') {
        Write-Host "  $n"
    }
}

$asm.Dispose()
