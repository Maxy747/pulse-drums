using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Pulse {
    public sealed class SampleChoice {
        public string Path, Label;
        public int Part, KitNumber;
        public override string ToString() { return Label; }
    }
    public static class SampleLibrary {
        public static SampleChoice[] Scan(string folder) {
            if (!Directory.Exists(folder)) return new SampleChoice[0];
            return Directory.GetFiles(folder,"*.wav",SearchOption.AllDirectories).OrderBy(p => p).Select(p => new SampleChoice {
                Path = p, Part = Classify(System.IO.Path.GetFileName(p)), KitNumber = p.IndexOf("Kit 2",StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 1,
                Label = (p.IndexOf("Kit 2",StringComparison.OrdinalIgnoreCase) >= 0 ? "Kit 2 · " : "Kit 1 · ") + System.IO.Path.GetFileNameWithoutExtension(p)
            }).ToArray();
        }
        public static int Classify(string file) {
            string n = file.ToLowerInvariant();
            if (n.Contains("hhats") || n.Contains("hats")) return 0;
            if (n.Contains("crash") || n.Contains("crsh") || n.Contains("splash") || n.Contains("chk")) return 1;
            if (n.Contains("ride") || n.Contains("rbell") || n.StartsWith("bell-")) return 7;
            if (n.Contains("kick") || n.Contains("-kd")) return 5;
            if (n.Contains("snare") || n.Contains("-sd") || n.Contains("s stick") || n.Contains("sstick") || n.Contains("cw-6x13")) return 3;
            if (n.Contains("tom") || n.Contains("tflam") || n.Contains("flam10") || n.Contains("flam13")) {
                if (n.Contains("13")) return 6;
                if (n.Contains("12")) return 2;
                return 4;
            }
            return -1;
        }
        public static string[] Preset(SampleChoice[] choices, int kit) {
            var result = new string[8];
            for (int part = 0; part < 8; part++) {
                var candidates = choices.Where(c => c.KitNumber == kit && c.Part == part).ToArray();
                // Kit 1 contains 10" and 13" toms, but no separate 12" tom.
                if (candidates.Length == 0 && part == 2) candidates = choices.Where(c => c.KitNumber == kit && c.Part == 6).ToArray();
                var choice = candidates.OrderByDescending(c => Preferred(c.Path, part)).ThenBy(c => c.Path).FirstOrDefault();
                result[part] = choice == null ? "" : choice.Path;
            }
            return result;
        }
        static int Preferred(string path, int part) {
            string n = System.IO.Path.GetFileName(path).ToLowerInvariant(); int score = n.Contains("v01") ? 10 : 0;
            if (part == 0 && (n.Contains("-cl-") || n.Contains("-cld"))) score += 100;
            if (part == 1 && n.Contains("crash") && !n.Contains("choke")) score += 100;
            if (part == 3 && (n.StartsWith("snare") || n.Contains("eq-sd"))) score += 100;
            if ((part == 2 || part == 4 || part == 6) && n.Contains("tom") && !n.Contains("flam")) score += 100;
            if (part == 7 && n.Contains("ride")) score += 100;
            return score;
        }
    }
    public static class WaveFile {
        // Decodes uncompressed WAV into stereo float frames at the engine's sample rate.
        // Original files remain untouched; all conversion happens in memory.
        public static float[] Load(string path) {
            using (var stream = File.OpenRead(path)) using (var r = new BinaryReader(stream)) {
                if (stream.Length > 128 * 1024 * 1024 || stream.Length < 44) throw new InvalidDataException("WAV must be under 128 MB.");
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "RIFF") throw new InvalidDataException("Not a RIFF WAV file.");
                r.ReadUInt32(); if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "WAVE") throw new InvalidDataException("Not a WAVE file.");
                int format = 0, channels = 0, rate = 0, bits = 0, align = 0; byte[] bytes = null;
                while (stream.Position + 8 <= stream.Length) {
                    string tag = Encoding.ASCII.GetString(r.ReadBytes(4)); uint length = r.ReadUInt32(); long end = stream.Position + length;
                    if (end > stream.Length) throw new InvalidDataException("Truncated WAV chunk.");
                    if (tag == "fmt ") {
                        if (length < 16) throw new InvalidDataException("Invalid WAV format.");
                        format = r.ReadUInt16(); channels = r.ReadUInt16(); rate = r.ReadInt32(); r.ReadUInt32(); align = r.ReadUInt16(); bits = r.ReadUInt16();
                        if (format == 65534 && length >= 40) { r.ReadUInt16(); r.ReadUInt16(); r.ReadUInt32(); format = r.ReadUInt16(); }
                    } else if (tag == "data" && bytes == null) bytes = r.ReadBytes((int)length);
                    stream.Position = Math.Min(stream.Length, end + (length & 1));
                }
                if (bytes == null || (format != 1 && format != 3) || channels < 1 || channels > 2 || rate < 8000 || rate > 192000 || (bits != 8 && bits != 16 && bits != 24 && bits != 32) || (format == 3 && bits != 32) || align != channels * bits / 8) throw new InvalidDataException("Use mono/stereo PCM 8/16/24/32-bit or float32 WAV.");
                int frames = bytes.Length / align;
                if (frames < 2 || frames > rate * 60) throw new InvalidDataException("Choose a one-shot WAV between 2 frames and 60 seconds.");
                int outputFrames = (int)((long)frames * AudioEngine.Rate / rate); var output = new float[outputFrames * 2]; int stride = bits / 8;
                for (int i = 0; i < outputFrames; i++) {
                    double pos = i * (double)rate / AudioEngine.Rate; int a = Math.Min(frames - 1, (int)pos), b = Math.Min(frames - 1,a + 1); double blend = pos - a;
                    for (int ch = 0; ch < 2; ch++) {
                        int channel = Math.Min(ch,channels - 1); float x = Decode(bytes,a * align + channel * stride,bits,format), y = Decode(bytes,b * align + channel * stride,bits,format);
                        output[i * 2 + ch] = (float)(x + (y - x) * blend);
                    }
                }
                return output;
            }
        }
        static float Decode(byte[] b, int p, int bits, int format) {
            float value;
            if (format == 3) value = BitConverter.ToSingle(b,p);
            else if (bits == 8) value = (b[p] - 128) / 128f;
            else if (bits == 16) value = BitConverter.ToInt16(b,p) / 32768f;
            else if (bits == 24) { int v = b[p] | (b[p+1] << 8) | (b[p+2] << 16); if ((v & 0x800000) != 0) v |= unchecked((int)0xff000000); value = v / 8388608f; }
            else value = BitConverter.ToInt32(b,p) / 2147483648f;
            if (Single.IsNaN(value) || Single.IsInfinity(value)) return 0;
            return Math.Max(-1,Math.Min(1,value));
        }
        public static float[] Stereo(float[] mono) { var result = new float[mono.Length * 2]; for (int i = 0; i < mono.Length; i++) result[i*2] = result[i*2+1] = mono[i]; return result; }
    }
}
