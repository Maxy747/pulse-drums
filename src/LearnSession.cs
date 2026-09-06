using System;
using System.Collections.Generic;
using System.Linq;

namespace Pulse {
    public sealed class LearnSession {
        public int Part, Candidate = -1, Peak;
        public int[] Map;
        public bool All, Complete;
        public int Confirmations;
        public string Hint = "";
        readonly HashSet<int> used = new HashSet<int>();
        sealed class Step { public int Part, Input; public int[] Map; }
        readonly Stack<Step> history = new Stack<Step>();
        readonly int[] peaks = new int[8];
        long until, listenAfter;
        public LearnSession(int[] map, int part, bool all, long now) { Map = (int[])map.Clone(); All = all; Part = all ? 0 : part; Retry(now); }
        public void Retry(long now) { Candidate = -1; Confirmations = 0; Hint = ""; Peak = 0; until = 0; listenAfter = now + 220; Array.Clear(peaks,0,8); }
        public void Feed(int input, int peak, long now) {
            if (Complete || input < 0 || input > 7 || now < listenAfter || peak < 1) return;
            if (until != 0 && now >= until) { Tick(now); return; }
            if (until == 0) until = now + 160;
            peaks[input] = Math.Max(peaks[input],peak);
        }
        public void Tick(long now) {
            if (Complete || until == 0 || now < until) return;
            Peak = peaks.Max(); int found = Array.IndexOf(peaks,Peak); until = 0; Array.Clear(peaks,0,8); listenAfter = now + 140;
            if (used.Contains(found)) { Candidate = -1; Confirmations = 0; Hint = "That input is already assigned. Strike " + Kit.Names[Part] + " twice."; return; }
            if (Candidate == found) Confirmations++; else { Hint = Candidate >= 0 ? "Different input heard. Strike this same piece once more." : "First strike heard. Strike the same piece once more."; Candidate = found; Confirmations = 1; }
            if (Confirmations >= 2) Accept(now);
        }
        public bool IsDuplicate { get { return Candidate >= 0 && used.Contains(Candidate); } }
        public bool CanUndo { get { return history.Count > 0 || Candidate >= 0; } }
        public void Undo(long now) {
            if (history.Count > 0) {
                var step = history.Pop(); Map = step.Map; Part = step.Part; used.Remove(step.Input); Complete = false;
            }
            Retry(now);
        }
        public bool Accept(long now) {
            if (Complete || Candidate < 0 || IsDuplicate || Confirmations < 2) return false;
            history.Push(new Step { Part = Part, Input = Candidate, Map = (int[])Map.Clone() });
            Map = Kit.Assign(Map,Part,Candidate); used.Add(Candidate);
            if (!All || Part == 7) { Complete = true; return true; }
            Part++; Retry(now); return false;
        }
    }
}
