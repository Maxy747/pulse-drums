$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$images = @()
foreach ($size in @(16,32,48,64,128,256)) {
    $bitmap = New-Object System.Drawing.Bitmap $size,$size
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'
    $g.ScaleTransform(($size / 64.0),($size / 64.0))
    $lime = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#C5F36B'))
    $dark = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#172010'))
    $rim = New-Object System.Drawing.Pen $lime,3.5
    $fine = New-Object System.Drawing.Pen $lime,1.2
    $g.DrawLine($rim,15,43,9,56); $g.DrawLine($rim,49,43,55,56)
    $g.FillEllipse($dark,8,4,48,48); $g.DrawEllipse($rim,8,4,48,48)
    $g.DrawEllipse($fine,14,10,36,36)
    foreach ($angle in @(0,60,120,180,240,300)) {
        $x = 32 + 23 * [Math]::Cos($angle * [Math]::PI / 180)
        $y = 28 + 23 * [Math]::Sin($angle * [Math]::PI / 180)
        $g.FillRectangle($lime,[single]($x-2),[single]($y-2),4,4)
    }
    $g.DrawLine($rim,32,35,32,55)
    $g.FillRectangle($lime,27,29,10,8); $g.FillRectangle($lime,26,50,12,11)
    $memory = New-Object System.IO.MemoryStream
    $bitmap.Save($memory,[System.Drawing.Imaging.ImageFormat]::Png)
    $images += ,@{Size=$size; Bytes=$memory.ToArray()}
    if ($size -eq 256) {
        New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'artifacts') | Out-Null
        $bitmap.Save((Join-Path $PSScriptRoot 'artifacts\pulse-icon.png'),[System.Drawing.Imaging.ImageFormat]::Png)
    }
    $memory.Dispose(); $g.Dispose(); $bitmap.Dispose(); $rim.Dispose(); $fine.Dispose(); $lime.Dispose(); $dark.Dispose()
}
$writer = New-Object System.IO.BinaryWriter ([System.IO.File]::Create((Join-Path $PSScriptRoot 'src\pulse.ico')))
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($entry in $images) {
        $dimension = if ($entry.Size -eq 256) { 0 } else { $entry.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$entry.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $entry.Bytes.Length
    }
    foreach ($entry in $images) { $writer.Write([byte[]]$entry.Bytes) }
} finally { $writer.Dispose() }
