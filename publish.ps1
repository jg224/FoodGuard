# Build a Thunderstore-ready package for FoodGuard.
#
# Produces (in foodguard/dist/):
#   FoodGuard-<version>.zip   the Thunderstore package: icon.png, manifest.json, README.md,
#                             LICENSE, and FoodGuard.dll all at the archive root.
#
# Thunderstore requires:
#   - icon.png exactly 256x256
#   - manifest.json with name, version_number, website_url, description, dependencies
#   - at least one .dll at the root (the mod)
# The archive must NOT contain a top-level folder -- files go at the zip root.
#
# After this runs, upload with the Thunderstore CLI:
#   tcli publish --package dist\FoodGuard-<version>.zip
# (run `tcli login` first to authenticate).
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File publish.ps1
#   powershell -ExecutionPolicy Bypass -File publish.ps1 -Version 0.2.0
param(
    [string]$ModName = "FoodGuard",
    [string]$Author  = "jg224",
    [string]$Version = "0.1.1"
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $here
try {
    # 1. (Re)generate the icon so it's always present and current.
    Write-Host "Generating icon.png..." -ForegroundColor Cyan
    & (Join-Path $here 'make_icon.ps1')

    $iconPath = Join-Path $here 'icon.png'
    if (-not (Test-Path $iconPath)) { throw "icon.png not generated." }

    # Validate icon dimensions (Thunderstore enforces 256x256).
    Add-Type -AssemblyName System.Drawing
    $img = [System.Drawing.Image]::FromFile($iconPath)
    $w = $img.Width; $h = $img.Height
    $img.Dispose()
    if ($w -ne 256 -or $h -ne 256) { throw "icon.png is ${w}x${h}; Thunderstore requires exactly 256x256." }

    # 2. Build the mod DLL.
    Write-Host "Building $ModName (Release)..." -ForegroundColor Cyan
    dotnet build -c Release 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)." }
    $dll = Join-Path $here "bin\Release\netstandard2.1\$ModName.dll"
    if (-not (Test-Path $dll)) { throw "Built DLL not found at $dll" }

    # 3. Stage the package in a clean folder (avoid picking up stale files).
    $stage = Join-Path $here 'dist\stage'
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Path $stage -Force | Out-Null

    Copy-Item $dll                          (Join-Path $stage "$ModName.dll") -Force
    Copy-Item (Join-Path $here 'README.md') (Join-Path $stage 'README.md')    -Force
    Copy-Item (Join-Path $here 'LICENSE')   (Join-Path $stage 'LICENSE')      -Force
    Copy-Item $iconPath                     (Join-Path $stage 'icon.png')     -Force

    # 4. Write the manifest (Thunderstore format).
    $manifest = [ordered]@{
        name        = $ModName.ToLower()
        version_number = $Version
        website_url = "https://github.com/$Author/$ModName"
        description = "Client-side Valheim mod that reminds forgetful players to eat. Center-screen popups + alert sound when food is low, leaving base, or entering combat without food or Rested. Mark your base with F7. Configurable."
        dependencies = @(
            "denikson-BepInExPack_Valheim-5.4.2333"
        )
    }
    $manifestPath = Join-Path $stage 'manifest.json'
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8

    # 5. ALSO stage the DLL + LICENSE into ./tcli_dist (flat) so `tcli build` can copy them as a
    #    folder via thunderstore.toml's [[build.copy]] source = "./tcli_dist". We use a SEPARATE
    #    folder from ./dist (which holds the zip built in step 6) so tcli doesn't pick up the zip
    #    and nest it inside the package. tcli 0.2.4 rejects per-file targets but accepts a folder.
    #    This makes BOTH upload paths work:
    #      a) tcli publish --file ./dist/FoodGuard-<v>.zip   (the prebuilt zip)
    #      b) tcli build && tcli publish                      (uses thunderstore.toml + ./tcli_dist)
    $tcliDist = Join-Path $here 'tcli_dist'
    if (Test-Path $tcliDist) { Remove-Item $tcliDist -Recurse -Force }
    New-Item -ItemType Directory -Path $tcliDist -Force | Out-Null
    Copy-Item $dll                        (Join-Path $tcliDist "$ModName.dll") -Force
    Copy-Item (Join-Path $here 'LICENSE') (Join-Path $tcliDist 'LICENSE')      -Force

    # 5. Zip the staged files at the archive ROOT (no top-level folder).
    $dist = Join-Path $here 'dist'
    $zipPath = Join-Path $dist "$ModName-$Version.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $fs = [System.IO.File]::Open($zipPath, [System.IO.FileMode]::Create)
    $archive = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($f in (Get-ChildItem -Path $stage -File)) {
            $entry = $archive.CreateEntry($f.Name)   # root-level entry, no folder prefix
            $es = $entry.Open()
            try {
                $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
                $es.Write($bytes, 0, $bytes.Length)
            }
            finally { $es.Dispose() }
        }
    }
    finally {
        $archive.Dispose()
        $fs.Dispose()
    }

    # Clean up the staging folder.
    Remove-Item $stage -Recurse -Force

    $sizeKb = [math]::Round((Get-Item $zipPath).Length / 1KB, 1)
    Write-Host ""
    Write-Host "OK: Thunderstore package built." -ForegroundColor Green
    Write-Host "    $zipPath ($sizeKb KB)" -ForegroundColor Green
    Write-Host "    Contents: $ModName.dll, manifest.json, README.md, LICENSE, icon.png" -ForegroundColor Green
    Write-Host ""
    Write-Host "To publish:" -ForegroundColor Cyan
    Write-Host "    tcli login" -ForegroundColor Cyan
    Write-Host "    tcli publish --file `"$zipPath`"" -ForegroundColor Cyan
}
finally {
    Pop-Location
}
