using System;
using System.Collections.Generic;
using System.Linq;

namespace Pulse {
    // Peak-event calibration, not continuous ADC measurement. Collect distinct strikes.
    public sealed class ThresholdLearning {
        readonly int[] map,targets,peaks=new int[8];
        readonly List<int>[] noise=new List<int>[8],strikes=new List<int>[8],spill=new List<int>[8];
        long baselineEnd,windowEnd,listenAfter; int step;
        public readonly int[] Hit=new int[8],Reset=new int[8];
        public bool Complete { get; private set; }
        public int Part { get { return targets[Math.Min(step,targets.Length-1)]; } }
        public int Count { get { return strikes[map[Part]].Count; } }
        public bool Quiet(long now) { return now<baselineEnd; }
        public string Warning="";
        public ThresholdLearning(Settings s,int part,bool all,long now) {
            map=(int[])s.Inputs.Clone(); targets=all?Enumerable.Range(0,8).ToArray():new[]{part};
            for(int i=0;i<8;i++){noise[i]=new List<int>();strikes[i]=new List<int>();spill[i]=new List<int>();Hit[i]=s.Pads[i].Hit;Reset[i]=s.Pads[i].Reset;}
            baselineEnd=now+6000;listenAfter=now+1000;
        }
        static int Percentile(List<int> values,double fraction) { if(values.Count==0)return 0;var sorted=values.OrderBy(x=>x).ToArray();return sorted[(int)Math.Floor((sorted.Length-1)*fraction)]; }
        public void Feed(int input,int raw,long now) {
            if(Complete||input<0||input>7||raw<0||raw>1023||now<listenAfter)return;
            if(Quiet(now)){if(noise[input].Count<4096)noise[input].Add(raw);return;}
            Tick(now); if(Complete||now<listenAfter)return;
            int target=map[Part];
            if(windowEnd==0) { if(input!=target||raw<Math.Max(8,Percentile(noise[target],.95)+6))return;windowEnd=now+180; }
            peaks[input]=Math.Max(peaks[input],raw);
        }
        public void Tick(long now) {
            if(Complete||windowEnd==0||now<windowEnd)return;
            int target=map[Part],peak=peaks[target];
            if(peak>=peaks.Max()*.7) { strikes[target].Add(peak);for(int i=0;i<8;i++)if(i!=target)spill[i].Add(peaks[i]); }
            Array.Clear(peaks,0,8);windowEnd=0;listenAfter=now+650;
            if(strikes[target].Count<6)return;
            step++; if(step<targets.Length)return;
            foreach(int part in targets) {
                int input=map[part],soft=Percentile(strikes[input],.2),floor=Math.Max(2,Percentile(noise[input],.95));
                int rejection=Math.Max(floor+6,(int)Math.Ceiling(Percentile(spill[input],.9)*1.2));
                int ceiling=Math.Max(2,(int)(soft*.7));
                if(rejection>ceiling)Warning+="Near-overlapping noise/crosstalk on "+Kit.Names[part]+". ";
                Hit[input]=Math.Min(1022,Math.Max(2,Math.Min(ceiling,Math.Max(rejection,(int)(soft*.25)))));
                Reset[input]=Math.Max(1,Math.Min(Hit[input]-1,Math.Max(floor+1,Hit[input]/4)));
            }
            Complete=true;
        }
        public string Summary() { return String.Join("\n",targets.Select(p=>Kit.Names[p]+" · A"+map[p]+" · hit "+Hit[map[p]]+" / reset "+Reset[map[p]])); }
    }
}
