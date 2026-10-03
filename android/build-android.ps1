param(
    [string]$JavaHome = $env:JAVA_HOME,
    [string]$AndroidSdk = $env:ANDROID_HOME,
    [switch]$Release
)
$ErrorActionPreference = 'Stop'
if (!$JavaHome -or !(Test-Path -LiteralPath (Join-Path $JavaHome 'bin\java.exe'))) { throw 'Set JAVA_HOME to JDK 17 or newer, or pass -JavaHome.' }
if (!$AndroidSdk -or !(Test-Path -LiteralPath (Join-Path $AndroidSdk 'platforms\android-35'))) { throw 'Set ANDROID_HOME to an SDK containing platforms;android-35 and build-tools;35.0.0, or pass -AndroidSdk.' }
$env:JAVA_HOME = $JavaHome
$env:ANDROID_HOME = $AndroidSdk
$env:PATH = "$JavaHome\bin;$env:PATH"
Push-Location $PSScriptRoot
try {
    $task = ':app:assembleDebug'
    if ($Release) {
        $signingFolder = Join-Path $env:LOCALAPPDATA 'PulseDrums\AndroidSigning'
        New-Item -ItemType Directory -Force -Path $signingFolder | Out-Null
        $env:PULSE_ANDROID_KEYSTORE = Join-Path $signingFolder 'pulse-mobile.jks'
        $passwordFile = Join-Path $signingFolder 'password.dpapi'
        if (!(Test-Path -LiteralPath $passwordFile)) {
            if (Test-Path -LiteralPath $env:PULSE_ANDROID_KEYSTORE) { throw 'Existing signing key has no stored password. Restore the password before building; do not replace the key.' }
            $bytes = New-Object byte[] 32
            $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
            $rng.GetBytes($bytes); $rng.Dispose()
            $secret = ConvertTo-SecureString ([Convert]::ToBase64String($bytes)) -AsPlainText -Force
            ConvertFrom-SecureString $secret | Set-Content -LiteralPath $passwordFile
        }
        $secure = Get-Content -LiteralPath $passwordFile | ConvertTo-SecureString
        $credential = New-Object System.Net.NetworkCredential('', $secure)
        $env:PULSE_ANDROID_STORE_PASSWORD = $credential.Password
        if (!(Test-Path -LiteralPath $env:PULSE_ANDROID_KEYSTORE)) {
            & (Join-Path $JavaHome 'bin\keytool.exe') -genkeypair -keystore $env:PULSE_ANDROID_KEYSTORE -storepass:env PULSE_ANDROID_STORE_PASSWORD -keypass:env PULSE_ANDROID_STORE_PASSWORD -alias pulse-mobile -keyalg RSA -keysize 3072 -validity 10000 -dname 'CN=Pulse Mobile, OU=Personal Apps, O=Pulse, C=IN'
            if ($LASTEXITCODE -ne 0) { throw 'Signing key generation failed' }
        }
        $task = ':app:assembleRelease'
    }
    & .\gradlew.bat $task :app:testDebugUnitTest :app:lintDebug --console=plain
    if ($LASTEXITCODE -ne 0) { throw 'Android build or validation failed' }
    $variant = if ($Release) { 'release' } else { 'debug' }
    $out = Join-Path $PSScriptRoot '..\bin\Pulse-Mobile-Android.apk'
    Copy-Item -LiteralPath "app\build\outputs\apk\$variant\app-$variant.apk" -Destination $out -Force
    Get-Item -LiteralPath $out | Select-Object FullName,Length
} finally {
    Remove-Item Env:PULSE_ANDROID_STORE_PASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:PULSE_ANDROID_KEYSTORE -ErrorAction SilentlyContinue
    Pop-Location
}
