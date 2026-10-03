using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Pulse {
    // Local LAN control only. Pairing is not encryption; do not forward this port to the Internet.
    public sealed class MobilePacketGate {
        readonly string code; string session="",peer="";long sequence=-1,last;
        public MobilePacketGate(string pairingCode){code=pairingCode;}
        public bool Accept(string packet,string address,long now,out string payload){
            payload="";if(packet==null||packet.Length>256)return false;
            string[] p=packet.Split('|');long n;Guid id;
            if(p.Length!=5||p[0]!="PULSE_M1"||p[1]!=code||!Guid.TryParse(p[2],out id)||!Int64.TryParse(p[3],out n)||n<0)return false;
            if(session!=""&&(session!=p[2]||peer!=address)&&now-last<3000)return false;
            if(session==p[2]&&peer==address&&n<=sequence)return false;
            if(!ValidPayload(p[4]))return false;
            session=p[2];peer=address;sequence=n;last=now;payload=p[4];return true;
        }
        public string Session {get{return session;}}
        public static bool ValidPayload(string payload){
            if(payload=="PING")return true;
            PedalFrame pedal;if(PedalDecoder.Parse(payload,out pedal))return true;
            var p=payload.Split(',');int a,b,c;
            if((p.Length!=3&&p.Length!=4)||!Int32.TryParse(p[1],out a)||a<0||a>7||!Int32.TryParse(p[2],out b)||b<0)return false;
            if(p.Length==3)return (p[0]=="RAW"&&b<=1023)||(p[0]=="TOUCH"&&b>0&&b<=127);
            return p[0]=="HIT"&&b>0&&b<=127&&Int32.TryParse(p[3],out c)&&c>=0&&c<=1023;
        }
    }
    public sealed class MobileReceiver:IDisposable {
        readonly UdpClient udp;readonly Thread thread;readonly MobilePacketGate gate;readonly Stopwatch clock=Stopwatch.StartNew();volatile bool stopped;
        public readonly string Code;public long LastReceived=-10000,LastPedals=-10000; public event Action<string> Received;
        public MobileReceiver(string savedCode=null){byte[] b=new byte[4];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(b);Code=System.Text.RegularExpressions.Regex.IsMatch(savedCode??"",@"^\d{8}$")?savedCode:(BitConverter.ToUInt32(b,0)%100000000).ToString("D8");gate=new MobilePacketGate(Code);udp=new UdpClient(new IPEndPoint(IPAddress.Any,9876));udp.Client.ReceiveTimeout=500;thread=new Thread(Run){IsBackground=true,Name="Pulse mobile receiver"};thread.Start();}
        public bool Connected {get{return clock.ElapsedMilliseconds-Interlocked.Read(ref LastReceived)<2500;}}
        public bool PedalsConnected {get{return clock.ElapsedMilliseconds-Interlocked.Read(ref LastPedals)<500;}}
        void Run(){while(!stopped)try{IPEndPoint endpoint=new IPEndPoint(IPAddress.Any,0);byte[] bytes=udp.Receive(ref endpoint);if(bytes.Length>256)continue;string payload;long now=clock.ElapsedMilliseconds;if(!gate.Accept(Encoding.ASCII.GetString(bytes),endpoint.Address.ToString(),now,out payload))continue;Interlocked.Exchange(ref LastReceived,now);if(payload.StartsWith("PEDALS,"))Interlocked.Exchange(ref LastPedals,now);if(payload=="PING"){byte[] ack=Encoding.ASCII.GetBytes("PULSE_ACK|"+gate.Session);udp.Send(ack,ack.Length,endpoint);}else {var receive=Received;if(receive!=null)receive(payload);}}catch(SocketException){}catch(ObjectDisposedException){break;}catch(Exception){if(stopped)break;}}
        public void Dispose(){stopped=true;udp.Close();thread.Join(1500);}
    }
    public sealed partial class Controller {
        MobileReceiver mobile;volatile bool mobileEnabled;
        void BuildMobileControls(){
            Get<CheckBox>("MobileInputToggle").Checked+=delegate {if(smoke)return;try{if(calibrating)StopCalibration(false);string path=System.IO.Path.Combine(SettingsStore.Folder,"mobile-pairing.txt");mobile=new MobileReceiver(System.IO.File.Exists(path)?System.IO.File.ReadAllText(path).Trim():null);System.IO.Directory.CreateDirectory(SettingsStore.Folder);System.IO.File.WriteAllText(path,mobile.Code);System.IO.File.WriteAllText(System.IO.Path.Combine(SettingsStore.Folder,"mobile-enabled.txt"),"true");mobileEnabled=true;mobile.Received+=ReceiveMobile;triggerFilter.Clear();midi.Panic();Get<TextBlock>("MobileAddress").Text="PC "+PreviewNetwork.FindAddress()+" :9876  ·  Code "+mobile.Code;}catch(Exception e){Get<CheckBox>("MobileInputToggle").IsChecked=false;Report("Phone input",e.Message);}};
            Get<CheckBox>("MobileInputToggle").Unchecked+=delegate {if(!smoke)System.IO.File.WriteAllText(System.IO.Path.Combine(SettingsStore.Folder,"mobile-enabled.txt"),"false");mobileEnabled=false;if(mobile!=null){mobile.Dispose();mobile=null;}lock(pedalGate){pedalMotion.Reset();}pedalReady=false;ready=false;triggerFilter.Clear();midi.Panic();Get<TextBlock>("MobileAddress").Text="Phone USB → Wi-Fi → Pulse audio / MIDI";};
            Get<Button>("MobileFirewallButton").Click+=delegate {if(smoke)return;try{string exe=System.Reflection.Assembly.GetExecutingAssembly().Location.Replace("'","''");string script="New-NetFirewallRule -DisplayName 'Pulse Mobile LAN' -Direction Inbound -Action Allow -Protocol UDP -LocalPort 9876 -RemoteAddress LocalSubnet -Program '"+exe+"' -Profile Any";Process.Start(new ProcessStartInfo("powershell.exe","-NoProfile -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script))){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden});}catch(Exception e){Report("Phone Wi-Fi access",e.Message);}};
        }
        void RestoreMobile(){string path=System.IO.Path.Combine(SettingsStore.Folder,"mobile-enabled.txt");if(System.IO.File.Exists(path)&&System.IO.File.ReadAllText(path).Trim()=="true")Get<CheckBox>("MobileInputToggle").IsChecked=true;}
        void ReceiveMobile(string payload){if(!mobileEnabled||exiting)return;PedalFrame pf;if(PedalDecoder.Parse(payload,out pf)){pedalReady=true;ReceivePedals(pf);return;}string[] p=payload.Split(',');int a=Int32.Parse(p[1]),b=Int32.Parse(p[2]);if(p[0]=="TOUCH"){if(!learning&&!calibrating)Hit(a,b,true);return;}ReceiveInput(new Frame(p[0],a,b){Peak=p.Length==4?Int32.Parse(p[3]):0});}
        void TickMobile(){if(!mobileEnabled||mobile==null)return;ready=mobile.Connected;connectionText=ready?"Phone · Wi-Fi connected":"Waiting for phone · Wi-Fi";bool available=mobile.PedalsConnected;if(pedalReady&&!available){lock(pedalGate){pedalMotion.Reset();lastPedalCc=-1;}if(audio!=null)audio.ChokeHat();}pedalReady=available;pedalStatus=available?"Phone · pedal Nano":"Phone · waiting for pedal Nano";}
    }
}

