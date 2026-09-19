using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Pulse {
    public sealed class PreviewNetwork : IDisposable {
        LiveViewServer secure,setup;
        public string Url { get { return secure.Url; } }
        public string SetupUrl { get { return setup.Url; } }
        public static IPAddress FindAddress() {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()) {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var props=adapter.GetIPProperties(); bool gateway=false;
                foreach(var g in props.GatewayAddresses) if(g.Address.AddressFamily==AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)) gateway=true;
                if (!gateway) continue;
                foreach (var a in props.UnicastAddresses) {
                    byte[] b=a.Address.GetAddressBytes();
                    if (b.Length==4 && (b[0]==10 || (b[0]==192 && b[1]==168) || (b[0]==172 && b[1]>=16 && b[1]<=31))) return a.Address;
                }
            }
            throw new IOException("Connect this PC to your home network, then retry iPad setup.");
        }
        public void Start(LiveViewState state) {
            var address=FindAddress(); string script;
            using (var r=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Pulse.PreviewCertificate.ps1"))) script=r.ReadToEnd();
            string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell\\v1.0\\powershell.exe");
            var info=new ProcessStartInfo(powershell,"-NoProfile -NonInteractive -OutputFormat Text -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script))) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
            info.EnvironmentVariables["PSModulePath"]=Path.Combine(Path.GetDirectoryName(powershell),"Modules");
            info.EnvironmentVariables["PULSE_PREVIEW_IP"]=address.ToString();
            string thumb;
            using (var p=Process.Start(info)) {
                if (!p.WaitForExit(30000)) { p.Kill(); throw new IOException("HTTPS certificate setup timed out. Retry iPad setup."); }
                thumb=p.StandardOutput.ReadToEnd().Trim(); string error=p.StandardError.ReadToEnd();
                if (p.ExitCode!=0) throw new IOException("HTTPS setup: "+error);
            }
            X509Certificate2 cert;
            using(var store=new X509Store(StoreName.My,StoreLocation.CurrentUser)) {
                store.Open(OpenFlags.ReadOnly); var found=store.Certificates.Find(X509FindType.FindByThumbprint,thumb,false);
                if(found.Count==0) throw new IOException("Preview certificate not found."); cert=found[0];
            }
            secure=new LiveViewServer(state); secure.StartNetwork(address,8766,cert,null,null);
            byte[] root=File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PulseDrums\\Preview\\Pulse-Preview.cer"));
            setup=new LiveViewServer(state); setup.StartNetwork(address,8776,null,Encoding.UTF8.GetBytes(SetupPage(secure.Url,root)),root);
        }
        static string SetupPage(string url,byte[] root) {
            string fingerprint; using(var hash=System.Security.Cryptography.SHA256.Create()) fingerprint=BitConverter.ToString(hash.ComputeHash(root));
            return "<!doctype html><html><meta name='viewport' content='width=device-width,initial-scale=1'><title>Pulse · iPad setup</title><style>body{color:#edf4e9;background:#0b100d;font:18px system-ui;max-width:640px;margin:40px auto;padding:24px;line-height:1.6}a{color:#b7f76c}li{margin:20px 0}code{overflow-wrap:anywhere;font-size:12px}</style><h1>Pulse on your iPad</h1><p>Keep Pulse open on the PC. Connect your iPad to the same home network.</p><ol><li><a href='/Pulse-Preview.cer'>Download this PC’s Pulse certificate</a> in Safari. Only install this certificate on devices you own.</li><li>In Settings → General → VPN &amp; Device Management, install the downloaded Pulse profile.</li><li>In Settings → General → About → Certificate Trust Settings, enable full trust for <b>Pulse Local Preview CA</b>.</li><li><a href='"+url+"'>Open the secure live kit →</a><br>"+url+"</li></ol><p>Optional: Safari → Share → Add to Home Screen for a full-screen kit view. Use Focus to hide the statistics.</p><p>This certificate is generated only for your PC; its private key stays on Windows. You can remove the profile and trust later in Settings.</p><p>Certificate SHA-256 (compare with the setup page on your PC):<br><code>"+fingerprint+"</code></p><p>If the page cannot open: allow Pulse through Windows Firewall with the desktop app’s Allow iPad access button. Guest Wi-Fi or client isolation can block devices from reaching each other.</p></html>";
        }
        public static void AllowFirewall() {
            string exe=Process.GetCurrentProcess().MainModule.FileName.Replace("'","''");
            string script="$env:PSModulePath=Join-Path $PSHOME 'Modules'; $ErrorActionPreference='Stop'; Get-NetFirewallRule -Name 'PulsePreviewLocal' -ErrorAction SilentlyContinue | Remove-NetFirewallRule; New-NetFirewallRule -Name 'PulsePreviewLocal' -DisplayName 'Pulse local iPad preview' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8766-8785 -Program '"+exe+"' -RemoteAddress LocalSubnet -Profile Any | Out-Null";
            Process.Start(new ProcessStartInfo("powershell.exe","-NoProfile -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script))) { UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden });
        }
        public void Dispose() { if(setup!=null)setup.Dispose(); if(secure!=null)secure.Dispose(); }
    }
}
