using System;

namespace Pulse {
    // A damped stereo room: parallel feedback delays followed by diffusion.
    // Buffers are allocated once; slider changes are smoothed on the audio thread.
    public sealed class RoomReverb {
        sealed class Delay {
            readonly double[] data; int at; double low;
            public Delay(int size) { data = new double[size]; }
            public double Comb(double input) {
                double value = data[at]; low = value*.7 + low*.3;
                data[at] = input + low*.78; if (++at == data.Length) at = 0;
                return value;
            }
            public double Diffuse(double input) {
                double value = data[at]; data[at] = input + value*.5;
                if (++at == data.Length) at = 0; return value-input;
            }
            public void Clear() { Array.Clear(data,0,data.Length); at = 0; low = 0; }
        }
        readonly Delay[] left = new Delay[4], right = new Delay[4];
        readonly Delay a = new Delay(607), b = new Delay(479), c = new Delay(631), d = new Delay(503);
        double wet;
        bool active;
        public RoomReverb() {
            int[] lengths = {1693,1759,1623,1549};
            for (int i = 0; i < 4; i++) { left[i] = new Delay(lengths[i]); right[i] = new Delay(lengths[i]+29); }
        }
        public void Clear() {
            foreach (var delay in left) delay.Clear(); foreach (var delay in right) delay.Clear();
            a.Clear(); b.Clear(); c.Clear(); d.Clear(); wet = 0; active = false;
        }
        public void Process(ref double l, ref double r, double amount, bool enabled) {
            double target = enabled ? Math.Max(0,Math.Min(1,amount))*.5 : 0;
            wet += (target-wet)*.002;
            if (target == 0 && wet < .000001) { if (active) Clear(); return; }
            active = true;
            double input = (l+r)*.12, outL = 0, outR = 0;
            for (int i = 0; i < 4; i++) { outL += left[i].Comb(input); outR += right[i].Comb(input); }
            l += a.Diffuse(b.Diffuse(outL)) * wet;
            r += c.Diffuse(d.Diffuse(outR)) * wet;
        }
    }
}
