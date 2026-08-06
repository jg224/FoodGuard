# Generate a 256x256 icon.png for the Thunderstore package.
# Design: a warm shield shape (safety/protection) with a stylized food bowl + fork, in Valheim-ish
# earthy tones. Renders crisply at the small sizes Thunderstore shows (the listing tile).
# Idempotent: overwrites foodguard/icon.png. Run via publish.ps1 or directly.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot 'icon.png'
$size = 256

$bmp = New-Object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

# Palette (earthy Valheim-like)
$shieldOutline = [System.Drawing.Color]::FromArgb(255, 28, 22, 16)     # near-black brown
$shieldFill    = [System.Drawing.Color]::FromArgb(255, 120, 78, 42)     # warm wood brown
$shieldHi      = [System.Drawing.Color]::FromArgb(255, 168, 118, 66)    # lighter brown
$bowlOuter     = [System.Drawing.Color]::FromArgb(255, 232, 200, 140)   # cream
$bowlInner     = [System.Drawing.Color]::FromArgb(255, 196, 140, 70)    # stew
$accent        = [System.Drawing.Color]::FromArgb(255, 220, 90, 60)     # alert red-orange
$steam         = [System.Drawing.Color]::FromArgb(180, 240, 240, 240)   # translucent white

function Fill($g, $brush, $path) { $g.FillPath($brush, $path) }
function Draw($g, $pen, $path) { $g.DrawPath($pen, $path) }

# ---- Shield (rounded, slightly pointed bottom) ----
$shield = New-Object System.Drawing.Drawing2D.GraphicsPath
$cx = 128
$topY = 28
$botY = 230
$halfW = 92
$shield.AddArc($cx - $halfW, $topY, 56, 56, 180, 90)                          # top-left corner
$shield.AddArc($cx + $halfW - 56, $topY, 56, 56, 270, 90)                      # top-right corner
$shield.AddLine($cx + $halfW, $topY + 28, $cx + 44, $botY - 50)               # right side down
# bottom point via a few line segments
$shield.AddLine($cx + 44, $botY - 50, $cx + 16, $botY - 18)
$shield.AddLine($cx + 16, $botY - 18, $cx, $botY)
$shield.AddLine($cx, $botY, $cx - 16, $botY - 18)
$shield.AddLine($cx - 16, $botY - 18, $cx - 44, $botY - 50)
$shield.AddLine($cx - 44, $botY - 50, $cx - $halfW, $topY + 28)               # left side up
$shield.CloseFigure()

$fillBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point(0, $topY)),
    (New-Object System.Drawing.Point(0, $botY)),
    $shieldHi, $shieldFill)
$g.FillPath($fillBrush, $shield)
$outPen = New-Object System.Drawing.Pen($shieldOutline, 6)
$g.DrawPath($outPen, $shield)

# ---- Bowl (centered, lower-mid of shield) ----
$bowlCx = $cx
$bowlCy = 150
$bowlRx = 52
$bowlRy = 26
# Bowl body (half-ellipse)
$bowl = New-Object System.Drawing.Drawing2D.GraphicsPath
$bowl.AddArc($bowlCx - $bowlRx, $bowlCy - $bowlRy, $bowlRx * 2, $bowlRy * 2, 0, 180)
$bowl.AddLine($bowlCx + $bowlRx, $bowlCy, $bowlCx - $bowlRx, $bowlCy)
$bowl.CloseFigure()
$bowlBrush = New-Object System.Drawing.SolidBrush($bowlOuter)
$g.FillPath($bowlBrush, $bowl)
# Inner stew (smaller ellipse)
$stew = New-Object System.Drawing.SolidBrush($bowlInner)
$g.FillEllipse($stew, $bowlCx - $bowlRx + 8, $bowlCy - $bowlRy + 4, ($bowlRx - 8) * 2, ($bowlRy - 2) * 2)
# Rim outline
$rimPen = New-Object System.Drawing.Pen($shieldOutline, 4)
$g.DrawEllipse($rimPen, $bowlCx - $bowlRx, $bowlCy - $bowlRy, $bowlRx * 2, $bowlRy * 2)

# ---- Fork (sticking up out of the bowl) ----
$forkX = $bowlCx + 22
$forkTopY = 70
$forkBotY = $bowlCy + 6
$handlePen = New-Object System.Drawing.Pen($bowlOuter, 8)
$handlePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($handlePen, $forkX, $forkTopY + 8, $forkX, $forkBotY)
# Tines (3 short lines at the top)
$tinePen = New-Object System.Drawing.Pen($bowlOuter, 5)
$tinePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
foreach ($dx in -6, 0, 6) {
    $g.DrawLine($tinePen, $forkX + $dx, $forkTopY, $forkX + $dx, $forkTopY + 16)
}

# ---- Steam (two wavy translucent lines above the bowl) ----
$steamPen = New-Object System.Drawing.Pen($steam, 6)
$steamPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
foreach ($sx in ($bowlCx - 14), ($bowlCx + 10)) {
    $pts = New-Object System.Collections.Generic.List[System.Drawing.PointF]
    $y = 96
    $phase = 0
    while ($y -gt 52) {
        $x = $sx + [math]::Sin($phase) * 7
        $pts.Add((New-Object System.Drawing.PointF($x, $y)))
        $y -= 8
        $phase += 1.1
    }
    $g.DrawLines($steamPen, $pts.ToArray())
}

# ---- Alert dot (top-right of shield, the "reminder" signal) ----
$dotCx = 196
$dotCy = 56
$dotR = 20
$dot = New-Object System.Drawing.SolidBrush($accent)
$g.FillEllipse($dot, $dotCx - $dotR, $dotCy - $dotR, $dotR * 2, $dotR * 2)
$g.DrawEllipse($outPen, $dotCx - $dotR, $dotCy - $dotR, $dotR * 2, $dotR * 2)

$g.Dispose()
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "Wrote $out ($size x $size)"
