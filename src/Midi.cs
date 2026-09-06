using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Pulse {
    public sealed class Midi : IDisposable {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct Caps { public ushort Mid, Pid; public uint Version; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; public ushort Technology, Voices, Notes, ChannelMask; public uint Support; }
        [DllImport("winmm.dll")] static extern uint midiOutGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern uint midiOutGetDevCapsW(UIntPtr id, out Caps caps, uint size);
        [DllImport("winmm.dll")] static extern uint midiOutOpen(out IntPtr handle, uint id, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] static extern uint midiOutShortMsg(IntPtr handle, uint message);
        [DllImport("winmm.dll")] static extern uint midiOutReset(IntPtr handle);
        [DllImport("winmm.dll")] static extern uint midiOutClose(IntPtr handle);
        readonly object gate = new object();
        IntPtr handle; string selected = "";
        readonly Dictionary<int, long> noteOffs = new Dictionary<int, long>();
        public static string[] Outputs() { var names = new List<string>(); for (uint i = 0; i < midiOutGetNumDevs(); i++) { Caps c; if (midiOutGetDevCapsW((UIntPtr)i, out c, (uint)Marshal.SizeOf(typeof(Caps))) == 0) names.Add(c.Name); } return names.ToArray(); }
        public string Ensure(string name) {
            lock (gate) {
                if (name == "") { Close(); return "MIDI off"; }
                var names = Outputs(); int id = Array.IndexOf(names, name);
                if (id < 0) { Close(); return "MIDI output missing · retrying"; }
                if (handle != IntPtr.Zero && selected == name) return "MIDI → " + name;
                Close(); uint error = midiOutOpen(out handle, (uint)id, IntPtr.Zero, IntPtr.Zero, 0);
                if (error != 0) { handle = IntPtr.Zero; return "MIDI unavailable (" + error + ") · retrying"; }
                selected = name; return "MIDI → " + name;
            }
        }
        public void Hit(int note, int velocity, int channel, long now, int noteOffMs = 10) {
            lock (gate) {
                if (handle == IntPtr.Zero) return;
                int ch = channel - 1, key = (ch << 8) | note;
                if (noteOffs.ContainsKey(key)) midiOutShortMsg(handle, (uint)(0x80 | ch | (note << 8)));
                uint result = midiOutShortMsg(handle, (uint)(0x90 | ch | (note << 8) | (velocity << 16)));
                if (result != 0) { Close(); return; }
                noteOffs[key] = now + noteOffMs;
            }
        }
        public void Tick(long now) {
            lock (gate) {
                var expired = new List<int>();
                foreach (var n in noteOffs) if (now >= n.Value) { if (handle != IntPtr.Zero) midiOutShortMsg(handle, (uint)(0x80 | (n.Key >> 8) | ((n.Key & 255) << 8))); expired.Add(n.Key); }
                foreach (int n in expired) noteOffs.Remove(n);
            }
        }
        public void Panic() { lock (gate) { if (handle != IntPtr.Zero) midiOutReset(handle); noteOffs.Clear(); } }
        void Close() { if (handle != IntPtr.Zero) { midiOutReset(handle); midiOutClose(handle); handle = IntPtr.Zero; } noteOffs.Clear(); selected = ""; }
        public void Dispose() { lock (gate) Close(); }
    }
}
