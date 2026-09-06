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
        public Pad() { Hit = 40; Reset = 20; Gain = 1; Curve = 1; RetriggerMs = 25; VelocityFloor = 1; }
        public Pad Copy() { return (Pad)MemberwiseClone(); }
    }
    public class Settings {
        public Pad[] Pads { get; set; }
        public double Volume { get; set; }
        public bool Sound { get; set; }
        public bool MinimizeToTray { get; set; }
        public string Port { get; set; }
        public string DeviceId { get; set; }
        public string MidiOutput { get; set; }
        public int Channel { get; set; }
        public Settings() {
            Pads = Protocol.Notes.Select(n => new Pad { Note = n }).ToArray();
            Volume = .7; Sound = true; MinimizeToTray = true; Port = ""; DeviceId = ""; MidiOutput = ""; Channel = 10;
        }
        public Settings Copy() { var s = (Settings)MemberwiseClone(); s.Pads = Pads.Select(p => p.Copy()).ToArray(); return s; }
        public void Normalize() {
            if (Pads == null || Pads.Length != 8) Pads = new Settings().Pads;
            for (int i = 0; i < 8; i++) {
                if (Pads[i] == null) Pads[i] = new Pad { Note = Protocol.Notes[i] };
                var p = Pads[i]; p.Hit = Math.Max(2, Math.Min(1022, p.Hit)); p.Reset = Math.Max(1, Math.Min(p.Hit - 1, p.Reset));
                p.Note = Math.Max(0, Math.Min(127, p.Note)); p.Gain = Clamp(p.Gain, .25, 3); p.Curve = Clamp(p.Curve, .4, 2.5);
                p.RetriggerMs = Math.Max(0, Math.Min(150, p.RetriggerMs));
                p.VelocityFloor = Math.Max(1, Math.Min(127, p.VelocityFloor));
            }
            Volume = Clamp(Volume, 0, 1); Channel = Math.Max(1, Math.Min(16, Channel));
            Port = Port ?? ""; DeviceId = DeviceId ?? ""; MidiOutput = MidiOutput ?? "";
        }
        static double Clamp(double v, double min, double max) { return Double.IsNaN(v) || Double.IsInfinity(v) ? min : Math.Max(min, Math.Min(max, v)); }
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
        public int Index, Value;
        public Frame(string kind, int index, int value) { Kind = kind; Index = index; Value = value; }
    }
    public static class Protocol {
        public static readonly int[] Notes = {36,38,42,48,45,41,49,51};
        public static readonly string[] Names = {"Kick", "Snare", "Hi-hat", "Tom 1", "Tom 2", "Floor tom", "Crash", "Ride"};
        public static bool Parse(string line, out Frame frame) {
            frame = new Frame(); if (line == null || line.Length > 64) return false;
            var p = line.Trim().Split(','); int a, b;
            if (p.Length != 3 || !Int32.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out a) || !Int32.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out b)) return false;
            if (p[0] == "NOTE" && a >= 0 && a <= 127 && b >= 0 && b <= 127) { frame = new Frame("NOTE", a, b); return true; }
            if (p[0] == "RAW" && a >= 0 && a < 8 && b >= 0 && b <= 1023) { frame = new Frame("RAW", a, b); return true; }
            return false;
        }
        public static int Velocity(int input, Pad p) { return Math.Max(p.VelocityFloor, Math.Min(127, (int)Math.Round(Math.Pow(input / 127.0, p.Curve) * p.Gain * 127))); }
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
            if (matched) hit = new Frame("HIT", frame.Index, note.Value);
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
