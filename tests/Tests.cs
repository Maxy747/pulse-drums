using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Pulse {
    public static class Tests {
        static int passed;
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); passed++; }
        public static int Main(string[] args) {
            try {
                Frame f;
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
                var copied = s.Copy(); copied.Pads[0].Hit = 90; Check(s.Pads[0].Hit == 2,"Immutable processing snapshot");
                var commands = Protocol.Thresholds(new Settings());
                Check(commands.Length == 16 && commands[0] == "SET,RESET,0,20\n" && commands[15] == "SET,HIT,7,40\n", "Firmware wire commands");
                Check(DrumConnection.IsCandidate("USB\\VID_1A86&PID_7523") && !DrumConnection.IsCandidate("ACPI\\PNP0501"),"USB discovery excludes unrelated onboard COM");
                string folder = Path.Combine(Path.GetTempPath(), "pulse-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
                try {
                    string path = Path.Combine(folder,"settings.xml"), warning;
                    s.Pads[4].VelocityFloor = 50; SettingsStore.Save(s,path); var loaded = SettingsStore.Load(path,out warning);
                    Check(loaded.Pads[4].VelocityFloor == 50 && warning == "", "Settings persistence");
                    s.Pads[4].Note = 80; SettingsStore.Save(s,path); loaded = SettingsStore.Load(path,out warning);
                    Check(loaded.Pads[4].Note == 80 && File.Exists(path + ".bak"),"Atomic replacement with backup");
                    File.WriteAllText(path,"corrupt"); loaded = SettingsStore.Load(path,out warning); Check(warning != "" && loaded.Pads.Length == 8,"Corrupt settings recovery");
                } finally { foreach (string file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
                for (int i = 0; i < 8; i++) { var sample = AudioEngine.Synthesize(i); Check(sample.Length > 5000 && sample.All(v => !Single.IsNaN(v) && Math.Abs(v) < 1.5) && sample.Any(v => Math.Abs(v) > .1),"Synth pad " + i); }
                if (!args.Contains("--no-audio")) using (var audio = new AudioEngine()) { audio.Start(); Thread.Sleep(250); Check(audio.Error == "", "Native waveOut initialization: " + audio.Error); }
                using (var midi = new Midi()) { Check(midi.Ensure("") == "MIDI off", "Optional MIDI without a loopback driver"); midi.Panic(); }
                Console.WriteLine("PASS: " + passed + " assertions; protocol, settings, audio, MIDI. Serial hardware untouched."); return 0;
            } catch (Exception e) { Console.Error.WriteLine("FAIL: " + e); return 1; }
        }
    }
}
