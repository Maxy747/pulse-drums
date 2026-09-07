param([switch]$NoStartup, [switch]$NoLaunch, [switch]$ImportCurrentKit, [switch]$WithoutSamples, [switch]$EnableUsbLaunch)
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'Pulse.exe'
if (!(Test-Path -LiteralPath $source)) { $source = Join-Path $PSScriptRoot 'bin\Pulse.exe' }
if (!(Test-Path -LiteralPath $source)) { throw 'Build first with .\build.ps1, or use the release zip.' }
if (!$WithoutSamples) { & (Join-Path $PSScriptRoot 'download-samples.ps1') }
$installFolder = Join-Path $env:LOCALAPPDATA 'Programs\Pulse'
$exePath = Join-Path $installFolder 'Pulse.exe'
New-Item -ItemType Directory -Force -Path $installFolder | Out-Null
if (Test-Path -LiteralPath $exePath) {
    $runningPulse = Get-Process -Name Pulse -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exePath }
    if ($runningPulse) {
        # Use the new build to stop the watcher, including when upgrading an older app.
        Start-Process -FilePath $source -ArgumentList '--stop-usb-watch' -WindowStyle Hidden -Wait
        Start-Process -FilePath $exePath -ArgumentList '--exit' -WindowStyle Hidden -Wait
        foreach ($process in $runningPulse) { if (!$process.WaitForExit(8000)) { throw 'Exit Pulse from its tray menu, then run the installer again.' } }
    }
}
Copy-Item -LiteralPath $source -Destination $exePath -Force
if ($ImportCurrentKit) {
    $settingsFolder = Join-Path $env:LOCALAPPDATA 'PulseDrums'
    $settingsPath = Join-Path $settingsFolder 'settings.xml'
    if (!(Test-Path -LiteralPath $settingsPath)) {
        New-Item -ItemType Directory -Force -Path $settingsFolder | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'profiles\current-kit.xml') -Destination $settingsPath
    }
}
$shortcutFolder = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $shortcutFolder 'Pulse.lnk'))
$shortcut.TargetPath = $exePath; $shortcut.WorkingDirectory = $installFolder; $shortcut.Description = 'Pulse — Arduino drum control'; $shortcut.Save()
if (!$NoStartup) {
    New-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'PulseDrums' -Value ('"' + $exePath + '" --background') -PropertyType String -Force | Out-Null
}
if ($EnableUsbLaunch) { New-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'PulseDrumsUsbWatch' -Value ('"' + $exePath + '" --watch-usb') -PropertyType String -Force | Out-Null }
if (!$NoLaunch) {
    Start-Process -FilePath $exePath -WindowStyle Hidden
    if ((Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'PulseDrumsUsbWatch' -ErrorAction SilentlyContinue)) { Start-Process -FilePath $exePath -ArgumentList '--watch-usb' -WindowStyle Hidden }
}
Write-Output "Installed: $exePath"
Write-Output 'Open Pulse from the Start menu. Launch with Windows can be changed inside the app.'
