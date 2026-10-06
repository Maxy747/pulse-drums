using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Pulse {
    public static class Tests {
        static Tests() { Program.EnsureDependencies(); }
        static int passed;
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); passed++; }
        [STAThread] public static int Main(string[] args) {
            try {
                string mobilePayload;string session=Guid.NewGuid().ToString();var gate=new MobilePacketGate("12345678");
                string packet="PULSE_M1|12345678|"+session+"|1|HIT,2,90,300";
                Check(gate.Accept(packet,"127.0.0.1",0,out mobilePayload)&&mobilePayload=="HIT,2,90,300","Phone relay accepts authenticated bounded hit");
                Check(!gate.Accept(packet,"127.0.0.1",1,out mobilePayload),"Phone relay rejects duplicate UDP hits");
                Check(!gate.Accept(packet.Replace("12345678","87654321").Replace("|1|","|2|"),"127.0.0.1",2,out mobilePayload),"Phone relay rejects wrong pairing code");
                Check(!gate.Accept(packet.Replace(session,Guid.NewGuid().ToString()),"127.0.0.2",2,out mobilePayload),"Phone relay locks active session");
                foreach(string invalid in new[]{"HIT,8,90,300","HIT,0,128,300","HIT,0,90,1024","RAW,0,-1","TOUCH,9,127","PEDALS,1,0,1024","HIT,0,0,1"})Check(!MobilePacketGate.ValidPayload(invalid),"Phone payload range validation");
                Check(MobilePacketGate.ValidPayload("PEDALS,4294967295,1023,0"),"Phone relay preserves pedal clock wrap");
                Check(MobilePacketGate.DiscoveryReply("PULSE_FIND1|abcd-1234","DESK|TOP","12345678",true)=="PULSE_HERE1|abcd-1234|DESKTOP|12345678","Discovery reply carries sanitized name and code while pairing");
                Check(MobilePacketGate.DiscoveryReply("PULSE_FIND1|abcd-1234","Desk","12345678",false)=="PULSE_HERE1|abcd-1234|Desk|","Discovery reply withholds code after pairing window");
                foreach(string bad in new[]{null,"PULSE_FIND1|short","PULSE_FIND1|bad|nonce1","PULSE_M1|12345678|x|1|PING"})Check(MobilePacketGate.DiscoveryReply(bad,"Desk","12345678",true)==null,"Discovery ignores malformed requests");
                using(var receiver=new MobileReceiver())using(var client=new System.Net.Sockets.UdpClient()) {
                    client.Client.ReceiveTimeout=1500;var find=System.Text.Encoding.ASCII.GetBytes("PULSE_FIND1|nonce-0001");client.Send(find,find.Length,"127.0.0.1",9876);
                    var found=new System.Net.IPEndPoint(System.Net.IPAddress.Any,0);Check(System.Text.Encoding.ASCII.GetString(client.Receive(ref found)).EndsWith("|"+receiver.Code)&&receiver.PairingSecondsLeft>0,"Real UDP discovery during pairing window");
                }
                using(var receiver=new MobileReceiver())using(var client=new System.Net.Sockets.UdpClient()) {
                    client.Client.ReceiveTimeout=1500;string id=Guid.NewGuid().ToString();var bytes=System.Text.Encoding.ASCII.GetBytes("PULSE_M1|"+receiver.Code+"|"+id+"|1|PING");client.Send(bytes,bytes.Length,"127.0.0.1",9876);
                    var endpoint=new System.Net.IPEndPoint(System.Net.IPAddress.Any,0);Check(System.Text.Encoding.ASCII.GetString(client.Receive(ref endpoint))=="PULSE_ACK|"+id&&receiver.Connected,"Real UDP phone handshake and heartbeat");
                }
                Frame f;
                var calibrationSettings=new Settings();calibrationSettings.Normalize();
                var calibrationTest=new ThresholdLearning(calibrationSettings,3,false,0);int sensor=calibrationSettings.Inputs[3];
                for(int t=1000;t<5000;t+=100)calibrationTest.Feed(sensor,3,t);
                calibrationTest.Tick(6500);Check(!calibrationTest.Complete && calibrationTest.Count==0,"Calibration waits for actual strikes after idle phase");
                for(int strike=0;strike<6;strike++) { long time=7000+strike*1100;calibrationTest.Feed(sensor,100+strike*20,time);calibrationTest.Feed(sensor,80,time+30);calibrationTest.Tick(time+181);calibrationTest.Feed(sensor,90,time+200); }
                Check(calibrationTest.Complete && calibrationTest.Count==6,"Peak bursts and cooldown echoes count as six distinct strikes");
                Check(calibrationTest.Hit[sensor]>3 && calibrationTest.Hit[sensor]<100 && calibrationTest.Reset[sensor]<calibrationTest.Hit[sensor],"Learned thresholds reject idle noise and retain softer hits");
                Check(calibrationTest.Hit[0]==calibrationSettings.Pads[0].Hit,"Single-pad calibration preserves other thresholds");
                var allThresholds=new ThresholdLearning(calibrationSettings,0,true,0);long calTime=7000;
                for(int part=0;part<8;part++)for(int hit=0;hit<6;hit++){int input=calibrationSettings.Inputs[part];allThresholds.Feed(input,150,calTime);allThresholds.Feed((input+1)%8,10,calTime+10);allThresholds.Tick(calTime+181);calTime+=1100;}
                Check(allThresholds.Complete && allThresholds.Hit.All(v=>v>10 && v<150),"All-pad calibration advances automatically and uses cross-pad peaks");
                Check(allThresholds.Hit.Select((v,i)=>allThresholds.Reset[i]<v).All(v=>v),"All learned reset thresholds remain below hit thresholds");
                calibrationSettings.SampleGainDb[3]=12;var gainCopy=calibrationSettings.Copy();gainCopy.SampleGainDb[3]=0;Check(calibrationSettings.SampleGainDb[3]==12,"Sample gain snapshots are independent");
                using(var sampleMixer=new AudioEngine()) {
                    sampleMixer.Volume=1;sampleMixer.SetSample(3,Enumerable.Repeat(.01f,4096).ToArray());var block=new short[512];
                    sampleMixer.Hit(3,127,0,1);sampleMixer.MixBlock(block);int original=block[100];sampleMixer.Panic();sampleMixer.Hit(3,127,0,2);sampleMixer.MixBlock(block);
                    Check(Math.Abs(block[100]-original*2)<=1,"Individual sample gain scales audio independently of velocity");
                    sampleMixer.Panic();sampleMixer.SetSample(5,Enumerable.Repeat(.01f,4096).ToArray());sampleMixer.Hit(5,127);sampleMixer.MixBlock(block);Check(block[100]==original,"One drum gain does not affect another drum");
                }
                var hatNotes=new Settings(); hatNotes.Normalize();
                Check(hatNotes.OpenHatNote==46 && hatNotes.HiHatController==4,"Legacy settings get standard open hat and CC defaults");
                hatNotes.InstrumentNotes[0]=55; hatNotes.OpenHatNote=59; hatNotes.Transpose=2;
                Check(hatNotes.MidiNoteForPart(0,false)==57,"Closed pad and pedal hits use configured hi-hat note");
                Check(hatNotes.MidiNoteForPart(0,true)==61,"Open hat and choke use configured note plus transpose");
                Check(hatNotes.MidiNoteForPart(5,true)==38,"Hi-hat articulation does not alter kick mapping");
                hatNotes.OpenHatNote=126; hatNotes.Transpose=48; Check(hatNotes.MidiNoteForPart(0,true)==127,"Open hi-hat MIDI output clamps after transpose");
                var browserState=new LiveViewState(); browserState.Hit(3,112,false); browserState.Hit(5,80,true);
                Check(browserState.Json().Contains("\"total\":1") && browserState.Json().Contains("\"preview\":true"),"Browser counts accepted hits but not app auditions");
                Check(LiveViewState.Quote("a\n\"b\\") == "\"a\\u000a\\\"b\\\\\"","Browser JSON escapes status strings");
                using (var server=new LiveViewServer(browserState)) {
                    browserState.Controls=body=>"{\"ok\":true}";
                    server.Start(0);
                    using (var client=new System.Net.WebClient()) {
                        Check(client.DownloadString(server.Url).Contains("Eight-piece kit"),"Embedded browser page served on loopback");
                        Check(client.DownloadString(server.Url+"state").Contains("\"total\":1"),"Browser snapshot uses actual hit state");
                        bool forbidden=false; try { client.UploadString(server.Url+"control","action=hit"); } catch(System.Net.WebException e) { if(e.Response==null)throw new Exception("Browser HTTP rejection: "+e.ToString()); forbidden=((System.Net.HttpWebResponse)e.Response).StatusCode==System.Net.HttpStatusCode.Forbidden; }
                        Check(forbidden,"Browser rejects writes without origin and token");
                        client.Headers["Origin"]=server.Url.TrimEnd('/'); client.Headers["X-Pulse-Token"]=browserState.ControlToken;
                        Check(client.UploadString(server.Url+"control","action=hit").Contains("true"),"Same-origin token authorizes touch control");
                        client.Headers["Origin"]="https://unrelated.example"; forbidden=false;
                        try { client.UploadString(server.Url+"control","action=hit"); } catch(System.Net.WebException e) { if(e.Response==null)throw new Exception("Browser HTTP rejection: "+e.ToString()); forbidden=((System.Net.HttpWebResponse)e.Response).StatusCode==System.Net.HttpStatusCode.Forbidden; }
                        Check(forbidden,"Cross-origin writes are rejected even with token");
                    }
                    using (var socket=new System.Net.Sockets.TcpClient("127.0.0.1",new Uri(server.Url).Port)) {
                        socket.ReceiveTimeout=2000; var stream=socket.GetStream(); var request=System.Text.Encoding.ASCII.GetBytes("GET /events HTTP/1.1\r\nHost: "+new Uri(server.Url).Authority+"\r\n\r\n"); stream.Write(request,0,request.Length);
                        using (var reader=new StreamReader(stream)) { string line; do { line=reader.ReadLine(); } while (line != null && !line.StartsWith("data: ")); Check(line != null && line.Contains("\"total\":1"),"Browser receives real server-sent events"); browserState.Hit(0,100,false); do { line=reader.ReadLine(); } while (line != null && !line.Contains("\"total\":2")); Check(line != null,"New hit streamed without a desktop UI tick"); }
                    }
                    using (var socket=new System.Net.Sockets.TcpClient("127.0.0.1",new Uri(server.Url).Port)) {
                        socket.ReceiveTimeout=2000; var stream=socket.GetStream(); var request=System.Text.Encoding.ASCII.GetBytes("GET /state HTTP/1.1\r\nHost: external.example\r\n\r\n"); stream.Write(request,0,request.Length); using (var reader=new StreamReader(stream)) Check(reader.ReadLine().Contains("403"),"Browser server rejects unrelated hosts");
                    }
                }
                PedalFrame pf;
                Check(PedalDecoder.Parse("PEDALS,4294967295,0,1023",out pf) && pf.Time == UInt32.MaxValue && pf.Hat == 1023,"Pedal full ADC and clock range");
                foreach (string bad in new[] { "PEDALS,1,-1,4","PEDALS,1,4,1024","PEDALS,-1,4,5","PEDALS,1,4","RAW,0,12","PEDALS,1,NaN,4" }) Check(!PedalDecoder.Parse(bad,out pf),"Reject invalid pedal frame");
                var pedalFrames=new List<PedalFrame>(); int identities=0; var pedalDecoder=new PedalDecoder();
                pedalDecoder.Feed("PULSE_PE",() => identities++,pedalFrames.Add);
                pedalDecoder.Feed("DALS,1\r\nPEDALS,5,10,20\n"+new string('x',90)+"\nPEDALS,10,11,21\n",() => identities++,pedalFrames.Add);
                Check(identities == 1 && pedalFrames.Count == 2,"Pedal fragmented handshake and oversized-line recovery");
                Check(Protocol.Parse("PULSE_PEDALS,1",out f) && f.Kind == "PEDALDEVICE","Main Nano recognizes and yields pedal port");
                Check(SerialClaims.Take("COM_TEST") && !SerialClaims.Take("com_test"),"Serial roles cannot claim same port"); SerialClaims.Release("COM_TEST"); Check(SerialClaims.Take("com_test"),"Serial port released for other role"); SerialClaims.Release("com_test");
                var pedalSettings=new Settings(); pedalSettings.Normalize(); pedalSettings.PedalsEnabled=true; pedalSettings.DeviceId="MAIN";
                Check(!PedalConnection.Candidate(new PortInfo {Name="COM5",Id="MAIN",Candidate=true},pedalSettings),"Pedal scan excludes saved main Nano");
                Check(PedalConnection.Candidate(new PortInfo {Name="COM4",Id="PEDAL",Candidate=true},pedalSettings),"Pedal scan accepts separate Nano");
                pedalSettings.PedalDeviceId="PEDAL"; Check(UsbLaunch.Matches(new PortInfo {Name="COM9",Id="PEDAL"},pedalSettings),"USB launch follows pedal identity after COM change");
                var motion=new PedalMotion();
                var sourceSettings=new Settings {PedalOnlyKick=true,MainDrumsOnlyKick=true}; sourceSettings.Normalize();
                Check(sourceSettings.MainDrumsOnlyKick && !sourceSettings.PedalOnlyKick,"Conflicting saved kick modes normalize to one source");
                var swappedSettings=pedalSettings.Copy(); swappedSettings.KickRest=100; swappedSettings.KickDown=800; swappedSettings.HatRest=900; swappedSettings.HatDown=200;
                swappedSettings.SetPedalSwap(true);
                Check(swappedSettings.SwapPedalInputs && swappedSettings.KickRest == 900 && swappedSettings.KickDown == 200 && swappedSettings.HatRest == 100 && swappedSettings.HatDown == 800,"Swap preserves physical input calibration");
                swappedSettings.SetPedalSwap(true); Check(swappedSettings.KickRest == 900,"Same swap state cannot swap calibration twice");
                var mapped=PedalMotion.MapInputs(new PedalFrame {Time=7,Kick=100,Hat=900},true);
                Check(mapped.Time == 7 && mapped.Kick == 900 && mapped.Hat == 100,"Swap maps A1 to kick and A0 to hi-hat");
                swappedSettings.SetPedalSwap(false); Check(swappedSettings.KickRest == 100 && swappedSettings.HatRest == 900,"Turning swap off restores calibration");
                var pr=motion.Accept(new PedalFrame {Time=0,Kick=1023,Hat=1023},pedalSettings);
                Check(pr.Closed && !pr.JustClosed && pr.KickVelocity == 0,"Connected while pressed does not generate phantom hit");
                motion.Accept(new PedalFrame {Time=5,Kick=0,Hat=0},pedalSettings);
                pr=motion.Accept(new PedalFrame {Time=15,Kick=900,Hat=900},pedalSettings);
                Check(pr.KickVelocity == 127 && pr.JustClosed && pr.CloseVelocity == 127,"Independent fast kick and hat close detected together");
                for (uint t=20;t<200;t+=5) { pr=motion.Accept(new PedalFrame {Time=t,Kick=850,Hat=740},pedalSettings); Check(pr.KickVelocity == 0 && !pr.JustClosed && pr.Closed,"Holding and threshold jitter cannot repeat hits"); }
                motion.Accept(new PedalFrame {Time=200,Kick=0,Hat=0},pedalSettings);
                pr=motion.Accept(new PedalFrame {Time=205,Kick=900,Hat=900},pedalSettings); Check(pr.KickVelocity > 0 && pr.JustClosed,"Release rearms both pedals");
                pr=motion.Accept(new PedalFrame {Time=1000,Kick=900,Hat=900},pedalSettings); Check(pr.KickVelocity == 0 && !pr.JustClosed,"Stale serial gap resets safely");
                motion.Reset(); motion.Accept(new PedalFrame {Time=0,Kick=0,Hat=0},pedalSettings);
                int slowVelocity=0; for (uint t=5;t<=1000;t+=5) { pr=motion.Accept(new PedalFrame {Time=t,Kick=0,Hat=(int)t},pedalSettings); if (pr.JustClosed) slowVelocity=pr.CloseVelocity; }
                Check(slowVelocity > 0 && slowVelocity < pedalSettings.PedalCloseVelocity,"Slow closing stays below optional close-hit threshold");
                pedalSettings.KickRest=900; pedalSettings.KickDown=100; pedalSettings.HatRest=850; pedalSettings.HatDown=150; motion.Reset();
                motion.Accept(new PedalFrame {Time=UInt32.MaxValue-3,Kick=900,Hat=850},pedalSettings);
                pr=motion.Accept(new PedalFrame {Time=3,Kick=100,Hat=150},pedalSettings);
                Check(pr.KickVelocity > 0 && pr.JustClosed,"Reversed calibration and millis wrap work");
                Check(PedalMotion.Position(700,500,500) == 0,"Degenerate calibration cannot trigger");
                using (var hatMixer=new AudioEngine()) {
                    hatMixer.Volume=1; hatMixer.SetHatSamples(Enumerable.Repeat(.1f,4096).ToArray(),Enumerable.Repeat(.4f,4096).ToArray());
                    var b=new short[1024]; hatMixer.Hit(0,127,1); hatMixer.MixBlock(b); int closedLevel=b[100];
                    hatMixer.Panic(); hatMixer.Hit(0,127,2); hatMixer.MixBlock(b); Check(b[100] > closedLevel*3,"Mixer selects distinct open and closed hi-hat samples");
                    hatMixer.ChokeHat(); hatMixer.MixBlock(b); Check(b[0] > 0 && b[500] == 0 && b[100] < b[0],"Pedal close fades open voice then silences it");
                    using (var restarted=new AudioEngine()) { hatMixer.CopySamplesTo(restarted); restarted.Volume=1; restarted.Hit(0,127,2); restarted.MixBlock(b); Check(b[100] > closedLevel*3,"ASIO restart retains pedal samples"); }
                }
                Check(AsioOutput.Drivers() != null,"Embedded ASIO dependencies and driver discovery");
                if (args.Contains("--asio-probe")) using (var asio = new AudioEngine()) {
                    asio.Volume = 0; asio.Start("Focusrite USB ASIO"); Thread.Sleep(1000);
                    Console.WriteLine("ASIO probe: " + asio.OutputStatus + " " + asio.Error);
                    Check(asio.Error == "" && asio.Asio != null && asio.RenderedBlocks > 100,"Focusrite USB ASIO stereo output initializes and pulls audio callbacks");
                }
                Check(Protocol.Parse("NOTE,38,127\r\n", out f) && f.Value == 127, "Valid note");
                Check(Protocol.Parse("RAW,7,1023", out f) && f.Index == 7, "Valid ADC peak");
                foreach (string invalid in new[] {"", "NOTE,128,50", "NOTE,38,128", "RAW,8,10", "RAW,0,1024", "RAW,-1,3", "NOTE,38,-1", "SET,HIT,1,40", "NOTE,38", "NOTE,38,10,2", "NOTE,38,abc", new string('x', 300)}) Check(!Protocol.Parse(invalid,out f), "Reject " + invalid);
                var frames = new List<Frame>(); var decoder = new LineDecoder();
                decoder.Feed("NO",frames.Add); decoder.Feed("TE,36,100\r\nRAW,0,",frames.Add); decoder.Feed("512\nNOTE,38,64\nRAW,1,300\n",frames.Add);
                Check(frames.Count == 4 && frames[0].Value == 100 && frames[3].Value == 300, "Fragmented and coalesced serial lines");
                decoder.Feed(new string('a',10000) + "NOTE,42,90\nRAW,2,99\n",frames.Add);
                Check(frames.Count == 5 && frames[4].Kind == "RAW", "Recover after oversize noise");
                var pairer = new HitPairer(); Frame paired;
                Check(!pairer.Accept(new Frame("RAW",1,100),0,out paired),"RAW alone cannot verify device");
                Check(!pairer.Accept(new Frame("NOTE",77,80),10,out paired) && pairer.Accept(new Frame("RAW",3,500),12,out paired) && paired.Index == 3 && paired.Value == 80,"Modified MIDI notes map through RAW pad index");
                Check(!pairer.Accept(new Frame("RAW",3,500),13,out paired),"Duplicate RAW does not produce a second hit");
                pairer.Accept(new Frame("NOTE",36,80),20,out paired); Check(!pairer.Accept(new Frame("RAW",0,500),280,out paired),"Stale NOTE cannot pair with unrelated RAW");
                pairer.Accept(new Frame("NOTE",36,0),300,out paired); Check(pairer.Accept(new Frame("RAW",0,200),301,out paired) && paired.Value == 0,"Velocity-zero note remains a non-hit");
                var s = new Settings(); s.Pads[0].Hit = 0; s.Pads[0].Reset = 100; s.Pads[1].Gain = Double.NaN; s.Pads[2].Curve = Double.PositiveInfinity; s.Volume = 9; s.Channel = 99; s.Normalize();
                Check(s.Pads[0].Hit == 2 && s.Pads[0].Reset == 1, "Prevent impossible rearm and map division by zero");
                Check(!Double.IsNaN(s.Pads[1].Gain) && !Double.IsInfinity(s.Pads[2].Curve) && s.Volume == 1 && s.Channel == 16, "Sanitize corrupt settings");
                var pad = new Pad(); Check(Protocol.Velocity(64,pad) == 64,"Linear response");
                pad.Curve = .6; pad.VelocityFloor = 50; Check(Protocol.Velocity(1,pad) == 50,"Preserve screenshot velocity floor");
                pad.Gain = 3; Check(Protocol.Velocity(127,pad) == 127,"Velocity clipping");
                pad.VelocityCeiling = 90; Check(Protocol.Velocity(127,pad) == 90,"Custom velocity ceiling");
                var copied = s.Copy(); copied.Pads[0].Hit = 90; Check(s.Pads[0].Hit == 2,"Immutable processing snapshot");
                var commands = Protocol.Thresholds(new Settings());
                Check(commands.Length == 16 && commands[0] == "SET,RESET,0,40\n" && commands[15] == "SET,HIT,7,10\n", "Calibrated firmware wire commands");
                var calibrated = new Settings(); calibrated.Normalize();
                var usbSettings = calibrated.Copy(); usbSettings.DeviceId = "USB\\VID_1A86\\SAVED";
                Check(UsbLaunch.Matches(new PortInfo { Id = usbSettings.DeviceId, Name = "COM8" },usbSettings),"USB launch follows saved device across COM changes");
                Check(!UsbLaunch.Matches(new PortInfo { Id = "USB\\VID_1A86\\OTHER", Name = "COM5", Candidate = true },usbSettings),"USB launch rejects unrelated adapter when device is known");
                usbSettings.DeviceId = "";
                Check(UsbLaunch.Matches(new PortInfo { Id = "USB\\VID_2341", Candidate = true },usbSettings) && !UsbLaunch.Matches(new PortInfo { Id = "USB\\MOUSE" },usbSettings),"USB first-run matching requires Arduino-compatible serial adapter");
                var protectedSettings = calibrated.Copy(); Sensitivity.Apply(protectedSettings,"Medium");
                var filter = new TriggerFilter(); var accepted = new List<Frame>();
                for (int i = 0; i < 20; i++) { filter.Push(new Frame("HIT",1,100) { Peak = 300 },protectedSettings,1000+i*5); filter.Flush(protectedSettings,1000+i*5,accepted.Add); }
                filter.Flush(protectedSettings,1200,accepted.Add); Check(accepted.Count == 1,"Twenty ringing events become one hit");
                filter.Push(new Frame("HIT",1,100) { Peak = 300 },protectedSettings,1250); filter.Flush(protectedSettings,1260,accepted.Add); Check(accepted.Count == 2,"New strike after quiet interval is accepted");
                filter.Clear(); accepted.Clear();
                filter.Push(new Frame("HIT",4,80) { Peak = 80 },protectedSettings,2000); filter.Push(new Frame("HIT",1,120) { Peak = 800 },protectedSettings,2003); filter.Flush(protectedSettings,2020,accepted.Add);
                Check(accepted.Count == 1 && accepted[0].Index == 1,"Weak neighbour before hard strike is rejected by raw peak");
                filter.Clear(); accepted.Clear(); filter.Push(new Frame("HIT",4,100) { Peak = 600 },protectedSettings,3000); filter.Push(new Frame("HIT",1,120) { Peak = 800 },protectedSettings,3002); filter.Flush(protectedSettings,3020,accepted.Add);
                Check(accepted.Count == 2,"Two strong simultaneous hits survive crosstalk filtering");
                filter.Clear(); accepted.Clear(); protectedSettings.CrosstalkPercent = 0;
                filter.Push(new Frame("HIT",4,80) { Peak = 80 },protectedSettings,4000); filter.Flush(protectedSettings,4000,accepted.Add);
                Check(accepted.Count == 1,"Crosstalk off has no comparison delay");
                Sensitivity.Apply(protectedSettings,"High"); int highThreshold = protectedSettings.Pads[0].Hit;
                Sensitivity.Apply(protectedSettings,"Low"); Check(protectedSettings.Pads[0].Hit > highThreshold && protectedSettings.Pads[0].RetriggerMs == 110,"Low sensitivity rejects more than High");
                Sensitivity.Apply(protectedSettings,"Medium");
                Check(protectedSettings.Inputs.SequenceEqual(calibrated.Inputs) && protectedSettings.Pads.All(p => p.RetriggerMs == 70),"Sensitivity keeps mapping and adds ringing guard");
                Check(calibrated.NoteOffMs == 10 && calibrated.Transpose == 0 && calibrated.Pads.All(p => p.VelocityCeiling == 127),"Screenshot MIDI timing, transpose and ceiling defaults");
                Check(calibrated.Pads.Select(p => p.Hit).SequenceEqual(new[]{140,20,20,20,10,60,10,10}) && calibrated.Pads.All(p => p.VelocityFloor == 50 && p.Curve == .6) && calibrated.Channel == 1,"Screenshot factory defaults");
                var swapped = Kit.Assign(calibrated.Inputs,0,0);
                Check(swapped[0] == 0 && swapped[5] == 2 && swapped.Distinct().Count() == 8,"Single assignment swaps displaced part without duplicate inputs");
                Check(calibrated.Inputs[0] == 2 && calibrated.Pads[0].Hit == 140,"Mapping leaves sensor calibration and original map untouched");
                var assignment = new LearnSession(calibrated.Inputs,0,true,0);
                assignment.Feed(2,200,100); assignment.Tick(300); Check(assignment.Candidate == -1,"Setup settling guard rejects previous ringing");
                assignment.Feed(2,70,400); assignment.Feed(7,600,405); assignment.Tick(570); Check(assignment.Candidate == 7,"Strongest raw input wins within capture window");
                Check(assignment.Confirmations == 1 && assignment.Part == 0 && !assignment.Accept(600),"One strike cannot confirm an input");
                assignment.Feed(7,500,600); assignment.Tick(800); Check(assignment.Confirmations == 1,"Immediate ringing cannot count as the second confirmation");
                assignment.Feed(7,500,900); assignment.Tick(1100); Check(assignment.Part == 1,"Two strikes automatically advance to the next piece");
                assignment.Feed(7,500,1500); assignment.Tick(1700); Check(assignment.Part == 1 && assignment.Confirmations == 0 && assignment.Hint.Contains("already assigned"),"Already-assigned input is rejected without requiring a button");
                assignment.Feed(6,500,2000); assignment.Tick(2200); assignment.Feed(6,500,2500); assignment.Tick(2700);
                for (int step = 2; step < 8; step++) { long now = 4000 + step*1000; assignment.Feed(7-step,500,now); assignment.Tick(now+200); assignment.Feed(7-step,500,now+450); assignment.Tick(now+650); }
                Check(assignment.Complete && assignment.Map.SequenceEqual(new[]{7,6,5,4,3,2,1,0}),"Full kit setup commits a complete one-to-one map");
                assignment.Undo(20000); Check(!assignment.Complete && assignment.Part == 7 && assignment.Confirmations == 0,"Undo final part reopens setup");
                assignment.Undo(21000); Check(assignment.Part == 6 && assignment.Map.Distinct().Count() == 8,"Repeated undo restores previous bijective map");
                assignment.Feed(1,500,21400); assignment.Tick(21600); assignment.Feed(1,500,21900); assignment.Tick(22100);
                Check(assignment.Part == 7,"Undone sensor can be assigned again");
                var mismatch = new LearnSession(calibrated.Inputs,3,false,0); mismatch.Feed(1,400,400); mismatch.Tick(600); mismatch.Feed(2,400,900); mismatch.Tick(1100);
                Check(!mismatch.Complete && mismatch.Candidate == 2 && mismatch.Confirmations == 1,"Mismatched strikes need another matching strike");
                mismatch.Feed(2,400,1500); mismatch.Tick(1700); Check(mismatch.Complete && mismatch.Map[3] == 2,"Single-piece setup completes hands-free");
                Check(DrumConnection.IsCandidate("USB\\VID_1A86&PID_7523") && !DrumConnection.IsCandidate("ACPI\\PNP0501"),"USB discovery excludes unrelated onboard COM");
                string folder = Path.Combine(Path.GetTempPath(), "pulse-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
                try {
                    string path = Path.Combine(folder,"settings.xml"), warning;
                    s.ThemeName = "Blue"; s.AsioDriver = "Focusrite USB ASIO"; s.CrosstalkPercent = 45; s.ReverbEnabled = true; s.ReverbAmount = .37; s.Pads[4].VelocityFloor = 50; SettingsStore.Save(s,path); var loaded = SettingsStore.Load(path,out warning);
                    Check(loaded.ReverbEnabled && loaded.ReverbAmount == .37,"Reverb toggle and amount persist");
                    Check(loaded.AsioDriver == s.AsioDriver && loaded.CrosstalkPercent == 45,"ASIO selection and crosstalk persist");
                    Check(loaded.ThemeName == "Blue", "Theme survives restart");
                    Check(loaded.Pads[4].VelocityFloor == 50 && warning == "", "Settings persistence");
                    s.Pads[4].Note = 80; SettingsStore.Save(s,path); loaded = SettingsStore.Load(path,out warning);
                    Check(loaded.Pads[4].Note == 80 && File.Exists(path + ".bak"),"Atomic replacement with backup");
                    File.WriteAllText(path,"corrupt"); loaded = SettingsStore.Load(path,out warning); Check(warning != "" && loaded.Pads.Length == 8,"Corrupt settings recovery");
                    var preset = new Settings(); preset.Normalize(); preset.Inputs = swapped; preset.SampleFiles[0] = "custom.wav"; preset.InstrumentNotes[0] = 44; preset.SampleGainDb[2]=8.5; preset.OpenHatNote=63; preset.HiHatController=11; SettingsStore.Save(preset,path); loaded = SettingsStore.Load(path,out warning);
                    Check(loaded.SampleGainDb[2]==8.5,"Sample gain persists in presets");
                    Check(loaded.OpenHatNote==63 && loaded.HiHatController==11,"Preset roundtrip includes open hi-hat and pedal CC");
                    Check(loaded.Inputs.SequenceEqual(swapped) && loaded.SampleFiles[0] == "custom.wav" && loaded.InstrumentNotes[0] == 44,"Preset roundtrip includes assignments, sounds and notes");
                    string wav = Path.Combine(folder,"stereo24.wav"); WriteWave(wav,48000,24,2,new byte[]{0,0,64,0,0,192,0,0,32,0,0,224}); var decoded = WaveFile.Load(wav);
                    Check(decoded.Length == 4 && decoded[0] == .5f && decoded[1] == -.5f && decoded[2] == .25f,"24-bit stereo sign extension and channel preservation");
                    WriteWave(wav,24000,16,1,new byte[]{0,0,0,64,0,0,0,192}); decoded = WaveFile.Load(wav);
                    Check(decoded.Length == 16 && decoded[2] == decoded[3] && Math.Abs(decoded[2] - .25f) < .001,"Mono duplication and resampling");
                    File.WriteAllText(wav,"bad WAV"); bool rejected = false; try { WaveFile.Load(wav); } catch (InvalidDataException) { rejected = true; } Check(rejected,"Reject malformed sample safely");
                } finally { foreach (string file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
                for (int i = 0; i < 8; i++) { var sample = AudioEngine.Synthesize(i); Check(sample.Length > 5000 && sample.All(v => !Single.IsNaN(v) && Math.Abs(v) < 1.5) && sample.Any(v => Math.Abs(v) > .1),"Synth pad " + i); }
                using (var mixer = new AudioEngine()) {
                    mixer.Volume = 1;
                    var wave = Enumerable.Range(0,2048).Select(i => (float)(Math.Sin(i/2 * .03) * (i % 2 == 0 ? .5 : -.25))).ToArray();
                    mixer.SetSample(0,wave); mixer.Hit(0,127); var output = new short[512];
                    bool clean = true;
                    for (int block = 0; block < 4; block++) { mixer.MixBlock(output); for (int i = 0; i < output.Length; i++) if (Math.Abs(output[i] - wave[block*512+i]*.65*32767) > 1.1) clean = false; }
                    Check(clean,"Sample amplitude and stereo phase remain linear across buffer boundaries");
                    mixer.MixBlock(output); Check(output.All(v => v == 0),"Finished sample becomes silence");
                    mixer.SetSample(0,Enumerable.Repeat(1f,2048).ToArray()); for (int i = 0; i < 48; i++) mixer.Hit(0,127);
                    mixer.MixBlock(output); Check(output.All(v => v > 0 && v <= 32112),"Overlapping loud hits cannot overflow or wrap PCM");
                }
                if (!args.Contains("--no-audio")) using (var audio = new AudioEngine()) { audio.Start(); Thread.Sleep(250); Check(audio.Error == "", "Native waveOut initialization: " + audio.Error); }
                if (args.Contains("--audio-stress")) using (var audio = new AudioEngine()) {
                    audio.Volume = .05f; audio.ReverbEnabled = true; audio.ReverbAmount = 1; audio.ConfigureStereo(true,1); audio.Start();
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    while (watch.ElapsedMilliseconds < 8000) {
                        for (int part = 0; part < 8; part++) audio.Hit(part,100);
                        var garbage = new byte[1000000]; garbage[0] = 1;
                        Thread.Sleep(30);
                    }
                    Console.WriteLine("Audio stress: blocks=" + audio.RenderedBlocks + ", empty queues=" + audio.Underruns + ", max service gap=" + audio.MaxServiceGapMs + " ms, MMCSS=" + audio.PriorityScheduled);
                    Check(audio.Error == "" && audio.RenderedBlocks > 1000 && audio.Underruns == 0,"Eight-second polyphony / allocation stress without empty queues");
                }
                var room = new RoomReverb(); double tail = 0, lateTail = 0, stereoDifference = 0;
                var stage = new PlayerStereo(); stage.Configure(true,1); for (int i = 0; i < 2048; i++) stage.Step();
                for (int part = 0; part < 8; part++) {
                    double l = .25, r = .25; stage.Process(part,ref l,ref r);
                    Check(PlayerStereo.Positions[part] < 0 ? l > r : PlayerStereo.Positions[part] > 0 ? r > l : l == r,"Player perspective direction for " + Kit.Names[part]);
                    Check(Math.Abs(l*l+r*r-.125) < .000001,"Pan keeps mono source power for " + Kit.Names[part]);
                }
                stage.Configure(false,1); for (int i = 0; i < 2048; i++) stage.Step();
                double originalL = .31, originalR = -.17; stage.Process(0,ref originalL,ref originalR);
                Check(originalL == .31 && originalR == -.17,"Stereo toggle off preserves original channels");
                stage.Configure(true,0); for (int i = 0; i < 2048; i++) stage.Step(); stage.Process(7,ref originalL,ref originalR);
                Check(originalL == .31 && originalR == -.17,"Zero stereo width preserves original channels");
                using (var positioned = new AudioEngine()) {
                    positioned.ConfigureStereo(true,1); var settle = new short[4096]; positioned.MixBlock(settle);
                    positioned.SetSample(7,Enumerable.Repeat(.1f,4096).ToArray()); positioned.Hit(7,100); positioned.MixBlock(settle);
                    Check(settle[1] > settle[0],"Mixer uses instrument identity for ride placement");
                }
                using (var boosted = new AudioEngine()) {
                    boosted.Volume = 1; boosted.OutputGain = 2; boosted.SetSample(0,Enumerable.Repeat(.1f,24000).ToArray()); boosted.Hit(0,127);
                    var block = new short[22000]; boosted.MixBlock(block);
                    Check(Math.Abs(block[21998] - .1*.65*2*32767) < 2,"Overall gain boosts quiet audio after smoothing");
                    boosted.Panic(); boosted.OutputGain = 1000; boosted.SetSample(0,Enumerable.Repeat(1f,24000).ToArray()); boosted.Hit(0,127); boosted.MixBlock(block);
                    Check(block.All(v => v > 0 && v <= 32112),"Maximum gain remains inside PCM limits");
                }
                for (int i = 0; i < AudioEngine.Rate*4; i++) {
                    double l = i == 0 ? 1 : 0, r = l; room.Process(ref l,ref r,1,true);
                    if (i > 1000 && i < AudioEngine.Rate) { tail += l*l+r*r; stereoDifference += Math.Abs(l-r); }
                    if (i > AudioEngine.Rate*3) lateTail += l*l+r*r;
                }
                Check(tail > .001 && stereoDifference > .01 && lateTail < tail*.01,"Reverb creates a stereo tail that decays");
                for (int i = 0; i < AudioEngine.Rate; i++) { double l = 0, r = 0; room.Process(ref l,ref r,1,false); }
                double dryL = .25, dryR = -.125; room.Process(ref dryL,ref dryR,1,false);
                Check(dryL == .25 && dryR == -.125,"Disabled reverb returns unchanged dry signal");
                using (var wetMixer = new AudioEngine()) {
                    wetMixer.ReverbEnabled = true; wetMixer.ReverbAmount = 1; wetMixer.Hit(0,127); var block = new short[48000]; wetMixer.MixBlock(block); wetMixer.Panic(); wetMixer.MixBlock(block);
                    Check(block.All(v => v == 0),"Silence all clears the reverb tail");
                }
                using (var midi = new Midi()) { Check(midi.Ensure("") == "MIDI off", "Optional MIDI without a loopback driver"); midi.Panic(); }
                var library = SampleLibrary.Scan(Path.Combine(SettingsStore.Folder,"Samples","GSCW"));
                Check(SampleLibrary.Classify("6-Splash-V01-SABIAN-HH-6.wav") == -1,"Splash is not a crash");
                Check(SampleLibrary.Classify("HHats-Crash-V01-SABIAN-AAX.wav") == 0,"Hi-hat articulation remains on hi-hat");
                Check(SampleLibrary.Classify("TOM13-V01-StarClassic-13x13.wav") == 6 && SampleLibrary.Classify("V01-TTom-12.wav") == 2,"Low and floor tom categories stay separate");
                Check(!SampleLibrary.Matches(1,"Ride-V01-ROBMOR-SABIAN-22.wav",SettingsStore.Folder),"Crash rejects a ride sample");
                Check(SampleLibrary.Matches(2,"TOM13-V05-StarClassic-13x13.wav",SettingsStore.Folder),"Low tom accepts Kit 1 tom alternatives");
                Check(!SampleLibrary.Matches(2,"SNARE-V05-CustomWorks-6x13.wav",SettingsStore.Folder),"Tom selector still excludes snare");
                var tomChoices = new[] {
                    new SampleChoice { Part=6, KitNumber=1, Path="TOM13-V05-StarClassic-13x13.wav" },
                    new SampleChoice { Part=4, KitNumber=1, Path="TOM10-V05-StarClassic-10x10.wav" },
                    new SampleChoice { Part=2, KitNumber=2, Path="V08-TFlam-12.wav" },
                    new SampleChoice { Part=2, KitNumber=2, Path="V05-TTom-12.wav" },
                    new SampleChoice { Part=3, KitNumber=1, Path="SNARE-V05-CustomWorks-6x13.wav" }
                };
                foreach (int part in new[] {2,4,6}) {
                    var options = SampleLibrary.ForPart(tomChoices,part);
                    Check(options.Length == 4 && options.Select(c => c.KitNumber).Distinct().Count() == 2,"Both kits available on tom " + part);
                    Check(options.Where(c => c.KitNumber == 2).First().Path == "V05-TTom-12.wav","Single V05 precedes flam on tom " + part);
                }
                Check(SampleLibrary.Description("V08-TFlam-12.wav") == "12-inch · V08 · Flam (double hit)","Recorded flam clearly labeled");
                Check(SampleLibrary.Description("TOM13-V05-StarClassic-13x13.wav") == "13-inch · V05 · Single hit","Kit 1 real tom size labeled");
                if (library.Length > 0) {
                    for (int kit=1;kit<=2;kit++) {
                        string closed=SampleLibrary.HatArticulation(library,kit,false),open=SampleLibrary.HatArticulation(library,kit,true);
                        Check(File.Exists(closed) && File.Exists(open) && closed != open && Path.GetFileName(closed).Contains("V05") && Path.GetFileName(open).Contains("V05"),"Real distinct V05 hi-hat pair for kit " + kit);
                    }
                    for (int kit = 1; kit <= 2; kit++) { var presetFiles = SampleLibrary.Preset(library,kit); Check(presetFiles.All(File.Exists),"Eight working defaults for GSCW kit " + kit); Check(presetFiles.All(p => Path.GetFileName(p).ToUpperInvariant().Contains("V05")),"All eight preset defaults use V05 in kit " + kit); for (int part = 0; part < 8; part++) Check(SampleLibrary.Classify(Path.GetFileName(presetFiles[part])) == part,"Preset sample matches instrument " + part); }
                    foreach (var sample in library) { var data = WaveFile.Load(sample.Path); Check(data.Length % 2 == 0 && data.Any(v => Math.Abs(v) > .001),"Decode " + sample.Label); }
                    Console.WriteLine("Validated " + library.Length + " downloaded WAV samples.");
                }
                Console.WriteLine("PASS: " + passed + " assertions; protocol, settings, audio, MIDI. Serial hardware untouched."); return 0;
            } catch (Exception e) { Console.Error.WriteLine("FAIL: " + e); return 1; }
        }
        static void WriteWave(string path, int rate, short bits, short channels, byte[] data) {
            using (var w = new BinaryWriter(File.Create(path))) { w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36+data.Length); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write(channels); w.Write(rate); w.Write(rate*channels*bits/8); w.Write((short)(channels*bits/8)); w.Write(bits); w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(data.Length); w.Write(data); }
        }
    }
}
