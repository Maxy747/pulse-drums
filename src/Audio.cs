using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Pulse {
    // Stereo sample player with a synthesizer fallback.
    public sealed class AudioEngine : IDisposable {
        [StructLayout(LayoutKind.Sequential, Pack = 2)] struct Format { public ushort Tag, Channels; public uint Rate, BytesPerSecond; public ushort Align, Bits, Extra; }
        [StructLayout(LayoutKind.Sequential)] struct Header { public IntPtr Data; public uint Length, Recorded; public IntPtr User; public uint Flags, Loops; public IntPtr Next, Reserved; }
        [DllImport("winmm.dll")] static extern uint waveOutOpen(out IntPtr device, uint id, ref Format format, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] static extern uint waveOutPrepareHeader(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] static extern uint waveOutWrite(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] static extern uint waveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] static extern uint waveOutReset(IntPtr device);
        [DllImport("winmm.dll")] static extern uint waveOutClose(IntPtr device);
        [DllImport("avrt.dll", CharSet = CharSet.Unicode)] static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);
        [DllImport("avrt.dll")] static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);
        class Voice { public float[] Sample; public int Position; public float Gain; }
        public const int Rate = 48000;
        const int Block = 256, Buffers = 6;
        public int Underruns;
        public long RenderedBlocks, MaxServiceGapMs;
        public bool PriorityScheduled;
        readonly object gate = new object();
        readonly List<Voice> voices = new List<Voice>();
        readonly float[][] samples = new float[8][];
        readonly IntPtr[] headers = new IntPtr[Buffers], data = new IntPtr[Buffers];
        readonly bool[] prepared = new bool[Buffers];
        readonly short[] pcm = new short[Block * 2];
        readonly ManualResetEvent signal = new ManualResetEvent(false);
        readonly uint headerSize = (uint)Marshal.SizeOf(typeof(Header));
        readonly int flagsOffset = (int)Marshal.OffsetOf(typeof(Header),"Flags");
        IntPtr device;
        Thread worker;
        double limiterGain = 1;
        double smoothGain = 1;
        public volatile float OutputGain = 1;
        readonly RoomReverb reverb = new RoomReverb();
        public volatile bool ReverbEnabled;
        public volatile float ReverbAmount = .25f;
        public AsioOutput Asio;
        public string OutputStatus = "Windows default · 32 ms buffer";
        public void CopySamplesTo(AudioEngine target) { lock (gate) for (int i = 0; i < 8; i++) target.SetSample(i,samples[i]); }
        volatile bool stop;
        public volatile string Error = "";
        public float Volume = .7f;
        public AudioEngine() { for (int i = 0; i < 8; i++) samples[i] = WaveFile.Stereo(Synthesize(Kit.DefaultInputs[i])); }
        public void SetSample(int part, float[] sample) { lock (gate) samples[part] = sample; }
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
        public void Start(string asioDriver = "") {
            try {
                if (!String.IsNullOrEmpty(asioDriver)) { Asio = new AsioOutput(this,asioDriver); OutputStatus = Asio.Status; return; }
                var f = new Format { Tag = 1, Channels = 2, Rate = Rate, BytesPerSecond = Rate * 4, Align = 4, Bits = 16 };
                uint error = waveOutOpen(out device, UInt32.MaxValue, ref f, signal.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, 0x50000);
                if (error != 0) throw new InvalidOperationException("Audio device unavailable (" + error + ")");
                for (int i = 0; i < Buffers; i++) {
                    data[i] = Marshal.AllocHGlobal(Block * 4); headers[i] = Marshal.AllocHGlobal((int)headerSize);
                    Marshal.Copy(pcm, 0, data[i], pcm.Length);
                    Marshal.StructureToPtr(new Header { Data = data[i], Length = Block * 4 }, headers[i], false);
                    if (waveOutPrepareHeader(device, headers[i], headerSize) != 0) throw new InvalidOperationException("Audio buffer setup failed");
                    prepared[i] = true;
                    if (waveOutWrite(device, headers[i], headerSize) != 0) throw new InvalidOperationException("Audio playback failed");
                }
                worker = new Thread(Pump) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "Pulse audio" }; worker.Start();
            } catch (Exception e) { Error = e.Message; Release(); }
        }
        public void Hit(int pad, int velocity) { lock (gate) { if (voices.Count >= 48) voices.RemoveAt(0); voices.Add(new Voice { Sample = samples[pad], Gain = velocity / 127f }); } }
        public void Panic() { lock (gate) { voices.Clear(); reverb.Clear(); limiterGain = 1; } }
        internal void MixBlock(short[] output, int length = -1) {
            if (length < 0) length = output.Length;
            lock (gate) {
                for (int i = 0; i < length; i += 2) {
                    double left = 0, right = 0;
                    foreach (var v in voices) if (v.Position + 1 < v.Sample.Length) {
                        left += v.Sample[v.Position++] * v.Gain;
                        right += v.Sample[v.Position++] * v.Gain;
                    }
                    reverb.Process(ref left,ref right,ReverbAmount,ReverbEnabled);
                    // Clean headroom; stereo-linked protection acts only on overloads.
                    smoothGain += (OutputGain-smoothGain)*.002;
                    left *= Volume * .65 * smoothGain; right *= Volume * .65 * smoothGain;
                    double peak = Math.Max(Math.Abs(left), Math.Abs(right));
                    double target = peak > .98 ? .98 / peak : 1;
                    limiterGain = target < limiterGain ? target : Math.Min(target, limiterGain + .0002);
                    output[i] = (short)(left * limiterGain * 32767);
                    output[i+1] = (short)(right * limiterGain * 32767);
                }
                voices.RemoveAll(v => v.Position >= v.Sample.Length);
            }
        }
        void Pump() {
            IntPtr scheduling = IntPtr.Zero;
            try {
                uint taskIndex = 0;
                scheduling = AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex); PriorityScheduled = scheduling != IntPtr.Zero;
                int nextBuffer = 0;
                var elapsed = Stopwatch.StartNew(); long lastService = 0;
                while (!stop) {
                    signal.WaitOne(5);
                    signal.Reset();
                    int completed = 0;
                    // Read only the flags: avoid allocating a boxed header on the
                    // real-time thread for every buffer check.
                    for (int b = 0; b < Buffers; b++) if ((Marshal.ReadInt32(headers[b],flagsOffset) & 1) != 0) completed++;
                    if (completed == Buffers) Interlocked.Increment(ref Underruns);
                    // Always refill in playback order, including when completions
                    // straddle the ring boundary. Events can coalesce under load.
                    for (int count = 0; count < Buffers && !stop; count++) {
                        int b = nextBuffer;
                        if ((Marshal.ReadInt32(headers[b],flagsOffset) & 1) == 0) break;
                        MixBlock(pcm);
                        Marshal.Copy(pcm, 0, data[b], pcm.Length);
                        if (waveOutWrite(device, headers[b], headerSize) != 0) throw new InvalidOperationException("Audio device stopped. Restart Pulse after changing audio devices.");
                        nextBuffer = (nextBuffer + 1) % Buffers;
                        long now = elapsed.ElapsedMilliseconds;
                        if (lastService != 0) Interlocked.Exchange(ref MaxServiceGapMs,Math.Max(Interlocked.Read(ref MaxServiceGapMs),now-lastService));
                        lastService = now; Interlocked.Increment(ref RenderedBlocks);
                    }
                }
            } catch (Exception e) { Error = e.Message; }
            finally { if (scheduling != IntPtr.Zero) AvRevertMmThreadCharacteristics(scheduling); }
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
        public void Dispose() { stop = true; if (Asio != null) { Asio.Dispose(); Asio = null; } signal.Set(); if (worker != null) worker.Join(); Release(); signal.Dispose(); }
    }
}
