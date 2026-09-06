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
                pad.VelocityCeiling = 90; Check(Protocol.Velocity(127,pad) == 90,"Custom velocity ceiling");
                var copied = s.Copy(); copied.Pads[0].Hit = 90; Check(s.Pads[0].Hit == 2,"Immutable processing snapshot");
                var commands = Protocol.Thresholds(new Settings());
                Check(commands.Length == 16 && commands[0] == "SET,RESET,0,40\n" && commands[15] == "SET,HIT,7,10\n", "Calibrated firmware wire commands");
                var calibrated = new Settings(); calibrated.Normalize();
                Check(calibrated.NoteOffMs == 10 && calibrated.Transpose == 0 && calibrated.Pads.All(p => p.VelocityCeiling == 127),"Screenshot MIDI timing, transpose and ceiling defaults");
                Check(calibrated.Pads.Select(p => p.Hit).SequenceEqual(new[]{140,20,20,20,10,60,10,10}) && calibrated.Pads.All(p => p.VelocityFloor == 50 && p.Curve == .6) && calibrated.Channel == 1,"Screenshot factory defaults");
                var swapped = Kit.Assign(calibrated.Inputs,0,0);
                Check(swapped[0] == 0 && swapped[5] == 2 && swapped.Distinct().Count() == 8,"Single assignment swaps displaced part without duplicate inputs");
                Check(calibrated.Inputs[0] == 2 && calibrated.Pads[0].Hit == 140,"Mapping leaves sensor calibration and original map untouched");
                var assignment = new LearnSession(calibrated.Inputs,0,true,0);
                assignment.Feed(2,200,100); assignment.Tick(300); Check(assignment.Candidate == -1,"Setup settling guard rejects previous ringing");
                assignment.Feed(2,70,400); assignment.Feed(7,600,405); assignment.Tick(570); Check(assignment.Candidate == 7,"Strongest raw input wins within capture window");
                assignment.Accept(600); assignment.Feed(7,500,1000); assignment.Tick(1200); Check(assignment.IsDuplicate && !assignment.Accept(1201),"Full setup rejects input already assigned to previous part");
                assignment.Retry(1300); assignment.Feed(6,500,1700); assignment.Tick(1900); assignment.Accept(2000);
                for (int step = 2; step < 8; step++) { long now = 3000 + step*1000; assignment.Feed(7-step,500,now); assignment.Tick(now+200); assignment.Accept(now+201); }
                Check(assignment.Complete && assignment.Map.SequenceEqual(new[]{7,6,5,4,3,2,1,0}),"Full kit setup commits a complete one-to-one map");
                Check(DrumConnection.IsCandidate("USB\\VID_1A86&PID_7523") && !DrumConnection.IsCandidate("ACPI\\PNP0501"),"USB discovery excludes unrelated onboard COM");
                string folder = Path.Combine(Path.GetTempPath(), "pulse-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
                try {
                    string path = Path.Combine(folder,"settings.xml"), warning;
                    s.Pads[4].VelocityFloor = 50; SettingsStore.Save(s,path); var loaded = SettingsStore.Load(path,out warning);
                    Check(loaded.Pads[4].VelocityFloor == 50 && warning == "", "Settings persistence");
                    s.Pads[4].Note = 80; SettingsStore.Save(s,path); loaded = SettingsStore.Load(path,out warning);
                    Check(loaded.Pads[4].Note == 80 && File.Exists(path + ".bak"),"Atomic replacement with backup");
                    File.WriteAllText(path,"corrupt"); loaded = SettingsStore.Load(path,out warning); Check(warning != "" && loaded.Pads.Length == 8,"Corrupt settings recovery");
                    var preset = new Settings(); preset.Normalize(); preset.Inputs = swapped; preset.SampleFiles[0] = "custom.wav"; preset.InstrumentNotes[0] = 44; SettingsStore.Save(preset,path); loaded = SettingsStore.Load(path,out warning);
                    Check(loaded.Inputs.SequenceEqual(swapped) && loaded.SampleFiles[0] == "custom.wav" && loaded.InstrumentNotes[0] == 44,"Preset roundtrip includes assignments, sounds and notes");
                    string wav = Path.Combine(folder,"stereo24.wav"); WriteWave(wav,48000,24,2,new byte[]{0,0,64,0,0,192,0,0,32,0,0,224}); var decoded = WaveFile.Load(wav);
                    Check(decoded.Length == 4 && decoded[0] == .5f && decoded[1] == -.5f && decoded[2] == .25f,"24-bit stereo sign extension and channel preservation");
                    WriteWave(wav,24000,16,1,new byte[]{0,0,0,64,0,0,0,192}); decoded = WaveFile.Load(wav);
                    Check(decoded.Length == 16 && decoded[2] == decoded[3] && Math.Abs(decoded[2] - .25f) < .001,"Mono duplication and resampling");
                    File.WriteAllText(wav,"bad WAV"); bool rejected = false; try { WaveFile.Load(wav); } catch (InvalidDataException) { rejected = true; } Check(rejected,"Reject malformed sample safely");
                } finally { foreach (string file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
                for (int i = 0; i < 8; i++) { var sample = AudioEngine.Synthesize(i); Check(sample.Length > 5000 && sample.All(v => !Single.IsNaN(v) && Math.Abs(v) < 1.5) && sample.Any(v => Math.Abs(v) > .1),"Synth pad " + i); }
                if (!args.Contains("--no-audio")) using (var audio = new AudioEngine()) { audio.Start(); Thread.Sleep(250); Check(audio.Error == "", "Native waveOut initialization: " + audio.Error); }
                using (var midi = new Midi()) { Check(midi.Ensure("") == "MIDI off", "Optional MIDI without a loopback driver"); midi.Panic(); }
                var library = SampleLibrary.Scan(Path.Combine(SettingsStore.Folder,"Samples","GSCW"));
                if (library.Length > 0) {
                    for (int kit = 1; kit <= 2; kit++) Check(SampleLibrary.Preset(library,kit).All(File.Exists),"Eight working defaults for GSCW kit " + kit);
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
