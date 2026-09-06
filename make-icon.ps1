$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap 32,32
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = 'AntiAlias'
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$path.AddArc(0,0,14,14,180,90); $path.AddArc(17,0,14,14,270,90); $path.AddArc(17,17,14,14,0,90); $path.AddArc(0,17,14,14,90,90); $path.CloseFigure()
$lime = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#C5F36B'))
$dark = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#172010'))
$graphics.FillPath($lime,$path)
$graphics.FillRectangle($dark,11,8,3,16); $graphics.FillRectangle($dark,18,8,3,16)
$icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
$stream = [System.IO.File]::Create((Join-Path $PSScriptRoot 'src\pulse.ico'))
$icon.Save($stream)
$stream.Dispose(); $icon.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $path.Dispose(); $lime.Dispose(); $dark.Dispose()
