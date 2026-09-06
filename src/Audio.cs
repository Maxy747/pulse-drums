using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Pulse {
    // Small polyphonic drum synthesizer. No sample packs, browser, or MIDI driver required.
    public sealed class AudioEngine : IDisposable {
        [StructLayout(LayoutKind.Sequential, Pack = 2)] struct Format { public ushort Tag, Channels; public uint Rate, BytesPerSecond; public ushort Align, Bits, Extra; }
        [StructLayout(LayoutKind.Sequential)] struct Header { public IntPtr Data; public uint Length, Recorded; public IntPtr User; public uint Flags, Loops; public IntPtr Next, Reserved; }
        [DllImport("winmm.dll")] static extern uint waveOutOpen(out IntPtr device, uint id, ref Format format, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] static extern uint waveOutPrepareHeader(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] static extern uint waveOutWrite(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] static extern uint waveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] static extern uint waveOutReset(IntPtr device);
        [DllImport("winmm.dll")] static extern uint waveOutClose(IntPtr device);
        class Voice { public float[] Sample; public int Position; public float Gain; }
        public const int Rate = 48000;
        const int Block = 256, Buffers = 4;
        readonly object gate = new object();
        readonly List<Voice> voices = new List<Voice>();
        readonly float[][] samples = new float[8][];
        readonly IntPtr[] headers = new IntPtr[Buffers], data = new IntPtr[Buffers];
        readonly bool[] prepared = new bool[Buffers];
        readonly short[] pcm = new short[Block];
        readonly AutoResetEvent signal = new AutoResetEvent(false);
        readonly uint headerSize = (uint)Marshal.SizeOf(typeof(Header));
        IntPtr device;
        Thread worker;
        volatile bool stop;
        public volatile string Error = "";
        public float Volume = .7f;
        public AudioEngine() { for (int i = 0; i < 8; i++) samples[i] = Synthesize(i); }
        public static float[] Synthesize(int pad) {
            double length = pad == 6 ? 1.5 : pad == 7 ? 1.1 : pad == 2 ? .16 : .65;
            var sample = new float[(int)(length * Rate)]; var random = new Random(101 + pad);
            double phase = 0, prevNoise = 0;
            for (int i = 0; i < sample.Length; i++) {
                double t = i / (double)Rate, noise = random.NextDouble() * 2 - 1, hp = (noise - prevNoise) * .5;
                prevNoise = noise; double v;
                if (pad == 0) { phase += 2 * Math.PI * (49 + 115 * Math.Exp(-t * 45)) / Rate; v = Math.Sin(phase) * Math.Exp(-t * 9) * .9 + noise * Math.Exp(-t * 160) * .15; }
                else if (pad == 1) { v = (Math.Sin(2 * Math.PI * 180 * t) * .36 * Math.Exp(-t * 22) + hp * .95 * Math.Exp(-t * 15)); }
                else if (pad >= 3 && pad <= 5) { double hz = pad == 3 ? 155 : pad == 4 ? 120 : 86; phase += 2 * Math.PI * (hz + 35 * Math.Exp(-t * 30)) / Rate; v = Math.Sin(phase) * .8 * Math.Exp(-t * 8) + hp * .14 * Math.Exp(-t * 60); }
                else { double decay = pad == 2 ? 40 : pad == 6 ? 4.5 : 6; double metal = Math.Sin(2 * Math.PI * 4307 * t) * Math.Sin(2 * Math.PI * 3181 * t); v = (hp * .8 + metal * .19) * Math.Exp(-t * decay); if (pad == 7) v += Math.Sin(2 * Math.PI * 740 * t) * Math.Exp(-t * 10) * .17; }
                double fadeIn = Math.Min(1, t * 3000), fadeOut = Math.Min(1, (length - t) * 100);
                sample[i] = (float)(v * fadeIn * fadeOut);
            }
            return sample;
        }
        public void Start() {
            try {
                var f = new Format { Tag = 1, Channels = 1, Rate = Rate, BytesPerSecond = Rate * 2, Align = 2, Bits = 16 };
                uint error = waveOutOpen(out device, UInt32.MaxValue, ref f, signal.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, 0x50000);
                if (error != 0) throw new InvalidOperationException("Audio device unavailable (" + error + ")");
                for (int i = 0; i < Buffers; i++) {
                    data[i] = Marshal.AllocHGlobal(Block * 2); headers[i] = Marshal.AllocHGlobal((int)headerSize);
                    Marshal.Copy(pcm, 0, data[i], Block);
                    Marshal.StructureToPtr(new Header { Data = data[i], Length = Block * 2 }, headers[i], false);
                    if (waveOutPrepareHeader(device, headers[i], headerSize) != 0) throw new InvalidOperationException("Audio buffer setup failed");
                    prepared[i] = true;
                    if (waveOutWrite(device, headers[i], headerSize) != 0) throw new InvalidOperationException("Audio playback failed");
                }
                worker = new Thread(Pump) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "Pulse audio" }; worker.Start();
            } catch (Exception e) { Error = e.Message; Release(); }
        }
        public void Hit(int pad, int velocity) { lock (gate) { if (voices.Count >= 48) voices.RemoveAt(0); voices.Add(new Voice { Sample = samples[pad], Gain = velocity / 127f }); } }
        public void Panic() { lock (gate) voices.Clear(); }
        void Pump() {
            try {
                while (!stop) {
                    signal.WaitOne(20);
                    for (int b = 0; b < Buffers && !stop; b++) {
                        Header h = (Header)Marshal.PtrToStructure(headers[b], typeof(Header));
                        if ((h.Flags & 1) == 0) continue;
                        lock (gate) {
                            for (int i = 0; i < Block; i++) {
                                double value = 0;
                                foreach (var v in voices) if (v.Position < v.Sample.Length) value += v.Sample[v.Position++] * v.Gain;
                                // Smooth saturation keeps simultaneous hits below digital full scale.
                                pcm[i] = (short)(Math.Tanh(value * Volume * .8) * 30000);
                            }
                            voices.RemoveAll(v => v.Position >= v.Sample.Length);
                        }
                        Marshal.Copy(pcm, 0, data[b], Block);
                        if (waveOutWrite(device, headers[b], headerSize) != 0) throw new InvalidOperationException("Audio device stopped. Restart Pulse after changing audio devices.");
                    }
                }
            } catch (Exception e) { Error = e.Message; }
        }
        void Release() {
            if (device != IntPtr.Zero) waveOutReset(device);
            for (int i = 0; i < Buffers; i++) {
                if (prepared[i]) { waveOutUnprepareHeader(device, headers[i], headerSize); prepared[i] = false; }
                if (headers[i] != IntPtr.Zero) { Marshal.FreeHGlobal(headers[i]); headers[i] = IntPtr.Zero; }
                if (data[i] != IntPtr.Zero) { Marshal.FreeHGlobal(data[i]); data[i] = IntPtr.Zero; }
            }
            if (device != IntPtr.Zero) { waveOutClose(device); device = IntPtr.Zero; }
        }
        public void Dispose() { stop = true; signal.Set(); if (worker != null) worker.Join(); Release(); signal.Dispose(); }
    }
}
