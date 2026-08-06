# Build FoodGuard and copy the DLL (+ README + Thunderstore manifest) to the hand-off folder.
# Usage:
#   powershell -ExecutionPolicy Bypass -File C:\Users\User\Documents\Valheim-Server\foodguard\build.ps1
#
# Output (in $OutputDir):
#   FoodGuard.dll              the mod (drop into BepInEx\plugins, or import via r2modman)
#   FoodGuard-README.txt       install + config instructions
#   manifest.json              Thunderstore manifest (author/version prefilled for r2modman import)
#   FoodGuard-1.0.0.zip        Thunderstore-format package: DLL + manifest + README at the zip root,
#                              ready for r2modman "Import local mod" with NO manual author/version prompt.
#
# This is a CLIENT mod. It does NOT go anywhere near the server.
param(
    [string]$OutputDir = "C:\Users\User\Downloads\New folder (2)",
    [string]$ModName   = "FoodGuard",
    [string]$Author    = "jg224",
    [string]$Version   = "0.1.2"
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $here
try {
    Write-Host "Building $ModName (Release)..." -ForegroundColor Cyan
    dotnet build -c Release 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)." }

    $dll = Join-Path $here "bin\Release\netstandard2.1\$ModName.dll"
    if (-not (Test-Path $dll)) { throw "Built DLL not found at expected path: $dll" }

    if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }

    # Copy the DLL, README, and LICENSE.
    Copy-Item $dll (Join-Path $OutputDir "$ModName.dll") -Force
    Copy-Item (Join-Path $here 'README.md') (Join-Path $OutputDir "$ModName-README.txt") -Force
    Copy-Item (Join-Path $here 'LICENSE') (Join-Path $OutputDir 'LICENSE') -Force

    # --- Thunderstore manifest.json (so r2modman prefills author/version on import) ---
    $manifest = [ordered]@{
        name        = $ModName.ToLower()      # Thunderstore convention: lowercase package name
        version_number = $Version
        website_url = ""
        description = "Client-side food-reminder mod for Valheim. Center-screen popup (and an optional combat alert sound) when food is low, when leaving base, or when entering combat without food."
        dependencies = @(
            "denikson-BepInExPack_Valheim-5.4.2333"
        )
    }
    $manifestPath = Join-Path $OutputDir 'manifest.json'
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8

    # --- Thunderstore-format zip: <modname>.dll + manifest.json + README.md at the zip root ---
    # r2modman's "Import local mod" on a zip reads author/version straight from manifest.json with no prompt.
    # (Windows PowerShell 5.x has no `using` statement, so we Dispose explicitly.)
    $zipPath = Join-Path $OutputDir "$ModName-$Version.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $fs = [System.IO.File]::Open($zipPath, [System.IO.FileMode]::Create)
    $archive = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
            $entries = @(
                @{ path = $dll;            entry = "$ModName.dll" },
                @{ path = $manifestPath;   entry = 'manifest.json' },
                @{ path = (Join-Path $here 'README.md'); entry = 'README.md' },
                @{ path = (Join-Path $here 'LICENSE');   entry = 'LICENSE' }
            )
        foreach ($e in $entries) {
            $entry = $archive.CreateEntry($e.entry)
            $entryStream = $entry.Open()
            try {
                $bytes = [System.IO.File]::ReadAllBytes($e.path)
                $entryStream.Write($bytes, 0, $bytes.Length)
            }
            finally { $entryStream.Dispose() }
        }
    }
    finally {
        $archive.Dispose()
        $fs.Dispose()
    }

    $sizeKb = [math]::Round((Get-Item $dll).Length / 1KB, 1)
    Write-Host ""
    Write-Host "OK. Output in $OutputDir :" -ForegroundColor Green
    Write-Host "    $ModName.dll           ($sizeKb KB)" -ForegroundColor Green
    Write-Host "    $ModName-README.txt" -ForegroundColor Green
    Write-Host "    manifest.json          (author=$Author, version=$Version)" -ForegroundColor Green
    Write-Host "    $ModName-$Version.zip  (r2modman-ready, no import prompt)" -ForegroundColor Green
    Write-Host ""
    Write-Host "r2modman: Profile -> Import/Update -> From file -> pick the .zip (author/version autofill)."
    Write-Host "Manual  : drop $ModName.dll into BepInEx\plugins\ and Start modded."
}
finally {
    Pop-Location
}
