using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace Pulse {
    public class Pad {
        public int Hit { get; set; }
        public int Reset { get; set; }
        public int Note { get; set; }
        public double Gain { get; set; }
        public double Curve { get; set; }
        public bool Muted { get; set; }
        public int RetriggerMs { get; set; }
        public int VelocityFloor { get; set; }
        public int VelocityCeiling { get; set; }
        public Pad() { Hit = 40; Reset = 20; Gain = 1; Curve = 1; RetriggerMs = 25; VelocityFloor = 1; VelocityCeiling = 127; }
        public Pad Copy() { return (Pad)MemberwiseClone(); }
    }
    public class Settings {
        public bool PedalsEnabled { get; set; }
        public string PedalPort { get; set; }
        public string PedalDeviceId { get; set; }
        public bool PedalCloseHit { get; set; }
        public bool PedalOnlyKick { get; set; }
        public int PedalCloseVelocity { get; set; }
        public int KickRest { get; set; }
        public int KickDown { get; set; }
        public int HatRest { get; set; }
        public int HatDown { get; set; }
        public Pad[] Pads { get; set; }
        public double Volume { get; set; }
        public double OutputGainDb { get; set; }
        public bool PlayerStereoEnabled { get; set; }
        public double PlayerStereoWidth { get; set; }
        public bool ReverbEnabled { get; set; }
        public double ReverbAmount { get; set; }
        public bool Sound { get; set; }
        public bool MinimizeToTray { get; set; }
        public string Port { get; set; }
        public string DeviceId { get; set; }
        public string MidiOutput { get; set; }
        public int Channel { get; set; }
        public int[] Inputs { get; set; }
        public int[] InstrumentNotes { get; set; }
        public string[] SampleFiles { get; set; }
        public bool ClassicView { get; set; }
        public string ThemeName { get; set; }
        public string AsioDriver { get; set; }
        public int CrosstalkPercent { get; set; }
        public bool ProtectionDefaultsApplied { get; set; }
        public string SampleFolder { get; set; }
        public bool SampleDefaultsApplied { get; set; }
        public bool MidiEnabled { get; set; }
        public int Transpose { get; set; }
        public int NoteOffMs { get; set; }
        public Settings() {
            PedalPort = ""; PedalDeviceId = ""; KickDown = HatDown = 1023; PedalCloseVelocity = 50;
            PlayerStereoEnabled = true; PlayerStereoWidth = 1;
            ReverbAmount = .25;
            Pads = Enumerable.Range(0,8).Select(i => new Pad { Note = Protocol.Notes[i], Hit = Kit.TriggerDefaults[i], Reset = Kit.ResetDefaults[i], Gain = 1, Curve = .6, VelocityFloor = 50, RetriggerMs = 0 }).ToArray();
            Volume = .7; Sound = true; MinimizeToTray = true; Port = ""; DeviceId = ""; MidiOutput = ""; Channel = 1; MidiEnabled = true; NoteOffMs = 10;
        }
        public Settings Copy() { var s = (Settings)MemberwiseClone(); s.Pads = Pads.Select(p => p.Copy()).ToArray(); s.Inputs = Inputs == null ? null : (int[])Inputs.Clone(); s.InstrumentNotes = InstrumentNotes == null ? null : (int[])InstrumentNotes.Clone(); s.SampleFiles = SampleFiles == null ? null : (string[])SampleFiles.Clone(); return s; }
        public void Normalize() {
            PedalPort = PedalPort ?? ""; PedalDeviceId = PedalDeviceId ?? "";
            PedalCloseVelocity = Math.Max(1,Math.Min(127,PedalCloseVelocity));
            KickRest = Math.Max(0,Math.Min(1023,KickRest)); KickDown = Math.Max(0,Math.Min(1023,KickDown));
            HatRest = Math.Max(0,Math.Min(1023,HatRest)); HatDown = Math.Max(0,Math.Min(1023,HatDown));
            PlayerStereoWidth = Clamp(PlayerStereoWidth,0,1);
            OutputGainDb = Clamp(OutputGainDb,0,18);
            ReverbAmount = Clamp(ReverbAmount,0,1);
            AsioDriver = AsioDriver ?? ""; CrosstalkPercent = Math.Max(0,Math.Min(70,CrosstalkPercent));
            if (ThemeName != "Red" && ThemeName != "Blue") ThemeName = "Green";
            if (Pads == null || Pads.Length != 8) Pads = new Settings().Pads;
            for (int i = 0; i < 8; i++) {
                if (Pads[i] == null) Pads[i] = new Settings().Pads[i];
                var p = Pads[i]; p.Hit = Math.Max(2, Math.Min(1022, p.Hit)); p.Reset = Math.Max(1, Math.Min(p.Hit - 1, p.Reset));
                p.Note = Math.Max(0, Math.Min(127, p.Note)); p.Gain = Clamp(p.Gain, .25, 3); p.Curve = Clamp(p.Curve, .4, 2.5);
                p.RetriggerMs = Math.Max(0, Math.Min(150, p.RetriggerMs));
                p.VelocityCeiling = Math.Max(1, Math.Min(127, p.VelocityCeiling));
                p.VelocityFloor = Math.Max(1, Math.Min(p.VelocityCeiling, p.VelocityFloor));
            }
            Volume = Clamp(Volume, 0, 1); Channel = Math.Max(1, Math.Min(16, Channel));
            Port = Port ?? ""; DeviceId = DeviceId ?? ""; MidiOutput = MidiOutput ?? "";
            if (Inputs == null || Inputs.Length != 8 || Inputs.Distinct().Count() != 8 || Inputs.Any(i => i < 0 || i > 7)) Inputs = (int[])Kit.DefaultInputs.Clone();
            if (InstrumentNotes == null || InstrumentNotes.Length != 8) InstrumentNotes = Inputs.Select(i => Pads[i].Note).ToArray();
            for (int i = 0; i < 8; i++) InstrumentNotes[i] = Math.Max(0, Math.Min(127, InstrumentNotes[i]));
            if (SampleFiles == null || SampleFiles.Length != 8) SampleFiles = new string[8];
            for (int i = 0; i < 8; i++) SampleFiles[i] = SampleFiles[i] ?? "";
            SampleFolder = SampleFolder ?? Path.Combine(SettingsStore.Folder, "Samples", "GSCW");
            Transpose = Math.Max(-48,Math.Min(48,Transpose)); NoteOffMs = Math.Max(1,Math.Min(500,NoteOffMs));
        }
        public int PartForInput(int input) { return Array.IndexOf(Inputs, input); }
        static double Clamp(double v, double min, double max) { return Double.IsNaN(v) || Double.IsInfinity(v) ? min : Math.Max(min, Math.Min(max, v)); }
    }
    public static class Kit {
        public static readonly string[] Names = {"Hi-hat", "Crash", "Low tom", "Snare", "Mid tom", "Bass / Kick", "Floor tom", "Ride"};
        public static readonly int[] DefaultInputs = {2,6,4,1,3,0,5,7};
        public static readonly int[] Notes = {42,49,45,38,48,36,41,51};
        public static readonly int[] TriggerDefaults = {140,20,20,20,10,60,10,10};
        public static readonly int[] ResetDefaults = {40,5,10,5,1,1,5,9};
        // Swap the displaced assignment rather than silently binding two parts to one sensor.
        public static int[] Assign(int[] original, int part, int input) {
            if (part < 0 || part > 7 || input < 0 || input > 7) throw new ArgumentOutOfRangeException();
            int[] result = (int[])original.Clone(); int previous = Array.IndexOf(result, input);
            if (previous < 0) throw new ArgumentException("Input map must be a permutation");
            result[previous] = result[part]; result[part] = input; return result;
        }
    }
    public static class SettingsStore {
        public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PulseDrums");
        public static readonly string PathName = Path.Combine(Folder, "settings.xml");
        public static Settings Load(string path, out string warning) {
            warning = "";
            if (!File.Exists(path)) return new Settings();
            try { using (var f = File.OpenRead(path)) { var s = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(f); s.Normalize(); return s; } }
            catch (Exception e) { warning = "Settings could not be read; using defaults. " + e.Message; return new Settings(); }
        }
        public static void Save(Settings s, string path) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            using (var f = File.Create(tmp)) new XmlSerializer(typeof(Settings)).Serialize(f, s);
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak"); else File.Move(tmp, path);
        }
    }
    public struct Frame {
        public string Kind;
        public int Index, Value, Peak;
        public Frame(string kind, int index, int value) { Kind = kind; Index = index; Value = value; Peak = 0; }
    }
    public static class Protocol {
        public static readonly int[] Notes = {36,38,42,48,45,41,49,51};
        public static readonly string[] Names = {"Kick", "Snare", "Hi-hat", "Tom 1", "Tom 2", "Floor tom", "Crash", "Ride"};
        public static bool Parse(string line, out Frame frame) {
            frame = new Frame(); if (line == null || line.Length > 64) return false;
            if (line.Trim() == "PULSE_PEDALS,1") { frame = new Frame("PEDALDEVICE",0,1); return true; }
            var p = line.Trim().Split(','); int a, b;
            if (p.Length != 3 || !Int32.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out a) || !Int32.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out b)) return false;
            if (p[0] == "NOTE" && a >= 0 && a <= 127 && b >= 0 && b <= 127) { frame = new Frame("NOTE", a, b); return true; }
            if (p[0] == "RAW" && a >= 0 && a < 8 && b >= 0 && b <= 1023) { frame = new Frame("RAW", a, b); return true; }
            return false;
        }
        public static int Velocity(int input, Pad p) { return Math.Max(p.VelocityFloor, Math.Min(p.VelocityCeiling, (int)Math.Round(Math.Pow(input / 127.0, p.Curve) * p.Gain * 127))); }
        public static string[] Thresholds(Settings s) {
            var lines = new List<string>();
            for (int i = 0; i < 8; i++) { lines.Add("SET,RESET," + i + "," + s.Pads[i].Reset + "\n"); lines.Add("SET,HIT," + i + "," + s.Pads[i].Hit + "\n"); }
            return lines.ToArray();
        }
    }
    public sealed class HitPairer {
        Frame note;
        long at;
        bool pending;
        public bool Accept(Frame frame, long now, out Frame hit) {
            hit = new Frame();
            if (frame.Kind == "NOTE") { note = frame; at = now; pending = true; return false; }
            if (frame.Kind != "RAW") return false;
            bool matched = pending && now >= at && now - at < 250;
            pending = false;
            if (matched) hit = new Frame("HIT", frame.Index, note.Value) { Peak = frame.Value };
            return matched;
        }
    }
    // Serial input may arrive one byte at a time or contain many complete lines.
    public sealed class LineDecoder {
        string pending = ""; bool dropping;
        public void Feed(string data, Action<Frame> receive) {
            foreach (char c in data) {
                if (c == '\n') { Frame f; if (!dropping && Protocol.Parse(pending, out f)) receive(f); pending = ""; dropping = false; }
                else if (!dropping) { pending += c; if (pending.Length > 64) { pending = ""; dropping = true; } }
            }
        }
    }
}
