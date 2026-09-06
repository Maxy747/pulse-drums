using System;
using System.Collections.Generic;
using System.Linq;

namespace Pulse {
    public sealed class LearnSession {
        public int Part, Candidate = -1, Peak;
        public int[] Map;
        public bool All, Complete;
        readonly HashSet<int> used = new HashSet<int>();
        readonly int[] peaks = new int[8];
        long until, listenAfter;
        public LearnSession(int[] map, int part, bool all, long now) { Map = (int[])map.Clone(); All = all; Part = all ? 0 : part; Retry(now); }
        public void Retry(long now) { Candidate = -1; Peak = 0; until = 0; listenAfter = now + 350; Array.Clear(peaks,0,8); }
        public void Feed(int input, int peak, long now) {
            if (Complete || Candidate >= 0 || input < 0 || input > 7 || now < listenAfter || peak < 1) return;
            if (until != 0 && now >= until) { Tick(now); return; }
            if (until == 0) until = now + 160;
            peaks[input] = Math.Max(peaks[input],peak);
        }
        public void Tick(long now) { if (Candidate < 0 && until > 0 && now >= until) { Peak = peaks.Max(); Candidate = Array.IndexOf(peaks,Peak); } }
        public bool IsDuplicate { get { return Candidate >= 0 && used.Contains(Candidate); } }
        public bool Accept(long now) {
            if (Complete || Candidate < 0 || IsDuplicate) return false;
            Map = Kit.Assign(Map,Part,Candidate); used.Add(Candidate);
            if (!All || Part == 7) { Complete = true; return true; }
            Part++; Retry(now); return false;
        }
    }
}
