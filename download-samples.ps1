param([string]$Destination = (Join-Path $env:LOCALAPPDATA 'PulseDrums\Samples\GSCW'))
$ErrorActionPreference = 'Stop'
$revision = 'ea524c8a952545fc099565426fc673e79076136f'
if ((Test-Path -LiteralPath $Destination) -and (Get-ChildItem -LiteralPath $Destination -Filter '*.wav' -Recurse | Measure-Object).Count -eq 360) {
    Write-Output 'GSCW sample library is already installed (360 WAVs).'
    return
}
$downloadFolder = Join-Path $env:LOCALAPPDATA ('PulseDrums\Downloads\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $downloadFolder,$Destination | Out-Null
$archivePath = Join-Path $downloadFolder 'gscw.zip'
Write-Output 'Downloading both GSCW kits from gregharvey/drum-samples (301 MB uncompressed)…'
Invoke-WebRequest -Uri "https://codeload.github.com/gregharvey/drum-samples/zip/$revision" -OutFile $archivePath -UseBasicParsing
Expand-Archive -LiteralPath $archivePath -DestinationPath $downloadFolder
$sourceFolder = Join-Path $downloadFolder "drum-samples-$revision"
foreach ($name in @('GSCW Drums Kit 1 Samples','GSCW Drums Kit 2 Samples','LICENSE.md','GSCW 2005 LICENSE AGREEMENT.rtf','README.md')) {
    Copy-Item -LiteralPath (Join-Path $sourceFolder $name) -Destination $Destination -Recurse -Force
}
$sampleCount = (Get-ChildItem -LiteralPath $Destination -Filter '*.wav' -Recurse | Measure-Object).Count
if ($sampleCount -ne 360) { throw "Download is incomplete: found $sampleCount of 360 WAVs. Run this script again." }
Write-Output "Installed $sampleCount original WAV files: $Destination"
Write-Output 'GSCW samples have their own license; see LICENSE.md in that folder. Original files were not converted or edited.'
