param([switch]$Test, [switch]$NoAudio)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$output = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$references = @('System.dll','System.Core.dll','System.Xml.dll','System.Management.dll','System.Drawing.dll','System.Windows.Forms.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','System.Xaml.dll') | ForEach-Object { '/r:' + (Join-Path $framework $_) }
$facade = Get-ChildItem (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\netstandard') -Filter netstandard.dll -Recurse | Select-Object -First 1
if (!$facade) { throw '.NET Framework 4.8 netstandard facade is required.' }
$references += '/r:' + $facade.FullName
$embedded = @()
$references += '/r:' + (Join-Path $framework 'System.Web.Extensions.dll')
$embedded += '/resource:' + (Join-Path $PSScriptRoot 'src\PreviewCertificate.ps1') + ',Pulse.PreviewCertificate.ps1'
$embedded += '/resource:' + (Join-Path $PSScriptRoot 'src\LiveView.html') + ',Pulse.LiveView.html'
foreach ($dependency in Get-ChildItem (Join-Path $PSScriptRoot 'vendor\NAudio') -Filter *.dll) {
    $references += '/r:' + $dependency.FullName
    $embedded += '/resource:' + $dependency.FullName + ',Pulse.Dependencies.' + $dependency.Name
}
foreach ($notice in Get-ChildItem (Join-Path $PSScriptRoot 'vendor\NAudio') -Filter *.txt) { $embedded += '/resource:' + $notice.FullName + ',Pulse.Licenses.' + $notice.Name }
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName
if (!(Test-Path (Join-Path $PSScriptRoot 'src\pulse.ico'))) { & (Join-Path $PSScriptRoot 'make-icon.ps1') }
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ /utf8output /main:Pulse.Program ('/out:' + (Join-Path $output 'Pulse.exe')) ('/win32icon:' + (Join-Path $PSScriptRoot 'src\pulse.ico')) ('/win32manifest:' + (Join-Path $PSScriptRoot 'src\app.manifest')) ('/resource:' + (Join-Path $PSScriptRoot 'src\Main.xaml') + ',Pulse.Main.xaml') @embedded @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
if ($Test) {
    & (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /optimize+ /main:Pulse.Tests ('/out:' + (Join-Path $output 'Pulse.Tests.exe')) @embedded @references @sources (Join-Path $PSScriptRoot 'tests\Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
    if ($NoAudio) { & (Join-Path $output 'Pulse.Tests.exe') --no-audio } else { & (Join-Path $output 'Pulse.Tests.exe') }
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
}
Get-Item -LiteralPath (Join-Path $output 'Pulse.exe') | Select-Object Name,Length
