using System;
using System.Collections.Generic;
using System.Linq;

namespace Pulse {
    public sealed class TriggerFilter {
        sealed class Pending { public Frame Frame; public long At; }
        readonly List<Pending> pending = new List<Pending>();
        readonly long[] seen = Enumerable.Repeat(-10000L,8).ToArray(), acceptedAt = Enumerable.Repeat(-10000L,8).ToArray();
        readonly int[] acceptedPeak = new int[8];
        readonly object gate = new object();
        public int Suppressed;
        public void Clear() { lock (gate) { pending.Clear(); for (int i = 0; i < 8; i++) { seen[i] = acceptedAt[i] = -10000; acceptedPeak[i] = 0; } } }
        public void Push(Frame frame, Settings settings, long now) {
            lock (gate) {
                int input = frame.Index;
                if (input < 0 || input > 7 || frame.Value <= 0) return;
                if (frame.Peak > 0 && frame.Peak < settings.Pads[input].Hit) { Suppressed++; return; }
                bool ringing = now-seen[input] < settings.Pads[input].RetriggerMs;
                seen[input] = now;
                if (ringing) { Suppressed++; return; }
                pending.Add(new Pending { Frame = frame, At = now });
                if (pending.Count > 128) { pending.RemoveAt(0); Suppressed++; }
            }
        }
        public void Flush(Settings settings, long now, Action<Frame> output) {
            lock (gate) {
                int window = settings.CrosstalkPercent > 0 ? 6 : 0;
                while (pending.Count > 0 && now-pending[0].At >= window) {
                    var item = pending[0]; bool reject = false;
                    if (window > 0 && item.Frame.Peak > 0) {
                        double ratio = settings.CrosstalkPercent / 100.0;
                        foreach (var other in pending) if (other.Frame.Index != item.Frame.Index && Math.Abs(other.At-item.At) <= window && item.Frame.Peak < other.Frame.Peak*ratio) reject = true;
                        for (int input = 0; input < 8; input++) if (input != item.Frame.Index && item.At-acceptedAt[input] >= 0 && item.At-acceptedAt[input] <= 25 && item.Frame.Peak < acceptedPeak[input]*ratio) reject = true;
                    }
                    pending.RemoveAt(0);
                    if (reject) { Suppressed++; continue; }
                    acceptedAt[item.Frame.Index] = item.At; acceptedPeak[item.Frame.Index] = item.Frame.Peak;
                    output(item.Frame);
                }
            }
        }
    }
    public static class Sensitivity {
        public static void Apply(Settings settings, string level) {
            double scale = level == "High" ? .75 : level == "Low" ? 2.5 : 1.5;
            int guard = level == "High" ? 35 : level == "Low" ? 110 : 70;
            for (int i = 0; i < 8; i++) {
                settings.Pads[i].Hit = (int)Math.Round(Kit.TriggerDefaults[i]*scale);
                settings.Pads[i].Reset = Math.Max(1,(int)Math.Round(Kit.ResetDefaults[i]*scale));
                settings.Pads[i].RetriggerMs = guard;
            }
            settings.CrosstalkPercent = level == "High" ? 25 : level == "Low" ? 60 : 45;
            settings.ProtectionDefaultsApplied = true; settings.Normalize();
        }
    }
}
