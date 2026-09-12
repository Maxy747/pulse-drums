using System;
using System.Globalization;

namespace Pulse {
    public struct PedalFrame { public uint Time; public int Kick, Hat; }
    public sealed class PedalDecoder {
        string pending = ""; bool dropping;
        public void Feed(string data, Action identify, Action<PedalFrame> receive) {
            foreach (char c in data) {
                if (c == '\n') {
                    if (!dropping) {
                        string line = pending.Trim(); PedalFrame frame;
                        if (line == "PULSE_PEDALS,1") identify();
                        else if (Parse(line,out frame)) receive(frame);
                    }
                    pending = ""; dropping = false;
                } else if (!dropping) { pending += c; if (pending.Length > 80) { pending = ""; dropping = true; } }
            }
        }
        public static bool Parse(string line, out PedalFrame f) {
            f = new PedalFrame(); if (line == null || line.Length > 80) return false;
            var p = line.Split(','); uint t; int k,h;
            if (p.Length != 4 || p[0] != "PEDALS" || !UInt32.TryParse(p[1],NumberStyles.None,CultureInfo.InvariantCulture,out t) ||
                !Int32.TryParse(p[2],NumberStyles.None,CultureInfo.InvariantCulture,out k) || !Int32.TryParse(p[3],NumberStyles.None,CultureInfo.InvariantCulture,out h) || k < 0 || k > 1023 || h < 0 || h > 1023) return false;
            f = new PedalFrame { Time=t,Kick=k,Hat=h }; return true;
        }
    }
    public struct PedalResult { public bool Closed, JustClosed; public int KickVelocity, CloseVelocity; }
    // Potentiometers measure travel; velocity is estimated from the fastest downward motion.
    public sealed class PedalMotion {
        bool initialized, closed, kickArmed; uint last;
        double kick,hat,kickSpeed,hatSpeed;
        public void Reset() { initialized = false; }
        public static double Position(int raw, int rest, int down) {
            return Math.Abs(down-rest) < 20 ? 0 : Math.Max(0,Math.Min(1,(raw-rest)/(double)(down-rest)));
        }
        public PedalResult Accept(PedalFrame f, Settings s) {
            double k = Position(f.Kick,s.KickRest,s.KickDown), h = Position(f.Hat,s.HatRest,s.HatDown);
            uint dt = unchecked(f.Time-last); last = f.Time;
            if (!initialized || dt > 100) {
                initialized = true; kick=k; hat=h; closed=h >= .75; kickArmed=k <= .25; kickSpeed=hatSpeed=0;
                return new PedalResult { Closed=closed };
            }
            if (dt == 0) return new PedalResult { Closed=closed };
            double decay=Math.Exp(-dt/80.0);
            kickSpeed = Math.Max(kickSpeed*decay,(k-kick)*1000/dt); hatSpeed = Math.Max(hatSpeed*decay,(h-hat)*1000/dt);
            var r = new PedalResult();
            if (k <= .25) { kickArmed=true; kickSpeed=0; }
            if (kickArmed && k >= .75) { r.KickVelocity=Velocity(kickSpeed); kickArmed=false; }
            if (closed && h <= .60) { closed=false; hatSpeed=0; }
            if (!closed && h >= .75) { closed=true; r.JustClosed=true; r.CloseVelocity=Velocity(hatSpeed); hatSpeed=0; }
            if (!closed && h < .15) hatSpeed=0;
            kick=k; hat=h; r.Closed=closed; return r;
        }
        static int Velocity(double speed) { return Math.Max(1,Math.Min(127,(int)Math.Round(speed*16))); }
    }
}
