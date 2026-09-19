$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Import-Module "$PSHOME\Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1"
$folder = Join-Path $env:LOCALAPPDATA 'PulseDrums\Preview'
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$rootPath = Join-Path $folder 'root.txt'
$root = $null
if (Test-Path $rootPath) { $root = Get-Item ('Cert:\CurrentUser\My\' + (Get-Content $rootPath -Raw).Trim()) -ErrorAction SilentlyContinue }
if (!$root -or !$root.HasPrivateKey -or $root.NotAfter -lt (Get-Date).AddDays(370)) {
    $root = New-SelfSignedCertificate -Type Custom -Subject 'CN=Pulse Local Preview CA' -FriendlyName 'Pulse local preview trust' -CertStoreLocation Cert:\CurrentUser\My -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -KeyUsage CertSign,CRLSign,DigitalSignature -TextExtension @('2.5.29.19={critical}{text}ca=1&pathlength=0') -NotAfter (Get-Date).AddYears(5)
    Set-Content -Path $rootPath -Value $root.Thumbprint
}
Export-Certificate -Cert $root -FilePath (Join-Path $folder 'Pulse-Preview.cer') -Force | Out-Null
$ip = $env:PULSE_PREVIEW_IP
$parsed = $null
if (![System.Net.IPAddress]::TryParse($ip, [ref]$parsed)) { throw 'Invalid preview IP address' }
$leafPath = Join-Path $folder ('leaf-' + $ip + '-' + $root.Thumbprint + '.txt')
$leaf = $null
if (Test-Path $leafPath) { $leaf = Get-Item ('Cert:\CurrentUser\My\' + (Get-Content $leafPath -Raw).Trim()) -ErrorAction SilentlyContinue }
if (!$leaf -or !$leaf.HasPrivateKey -or $leaf.NotAfter -lt (Get-Date).AddDays(7) -or $leaf.Issuer -ne $root.Subject) {
    $leaf = New-SelfSignedCertificate -Type Custom -Subject 'CN=Pulse local preview' -FriendlyName ('Pulse HTTPS ' + $ip) -Signer $root -CertStoreLocation Cert:\CurrentUser\My -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -KeyUsage DigitalSignature,KeyEncipherment -TextExtension @('2.5.29.19={text}ca=0', '2.5.29.37={text}1.3.6.1.5.5.7.3.1', ('2.5.29.17={text}IPAddress=' + $ip)) -NotAfter (Get-Date).AddDays(365)
    Set-Content -Path $leafPath -Value $leaf.Thumbprint
}
Write-Output $leaf.Thumbprint
