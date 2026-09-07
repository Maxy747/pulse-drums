using System;

namespace Pulse {
    public sealed class PlayerStereo {
        // Horizontal locations in the on-screen kit, measured from the kick.
        public static readonly double[] Positions = {-.95,-.7375,-.2375,-.3875,.2125,0,.4,.825};
        readonly double[,] current = new double[8,4], target = new double[8,4];
        int remaining;
        public PlayerStereo() { for (int i = 0; i < 8; i++) current[i,0] = current[i,3] = target[i,0] = target[i,3] = 1; }
        public void Configure(bool enabled, double width) {
            for (int i = 0; i < 8; i++) {
                double pan = enabled ? Positions[i]*Math.Max(0,Math.Min(1,width)) : 0;
                double left = Math.Sqrt(2)*Math.Cos((pan+1)*Math.PI/4), right = Math.Sqrt(2)*Math.Sin((pan+1)*Math.PI/4);
                double side = 1-Math.Abs(pan);
                target[i,0] = (left+side)*.5; target[i,1] = (left-side)*.5;
                target[i,2] = (right-side)*.5; target[i,3] = (right+side)*.5;
                if (pan == 0) { target[i,0] = target[i,3] = 1; target[i,1] = target[i,2] = 0; }
            }
            remaining = 2048;
        }
        public void Step() {
            if (remaining <= 0) return;
            remaining--;
            for (int part = 0; part < 8; part++) for (int channel = 0; channel < 4; channel++)
                current[part,channel] = remaining == 0 ? target[part,channel] : current[part,channel] + (target[part,channel]-current[part,channel])*.004;
        }
        public void Process(int part, ref double left, ref double right) {
            double l = left, r = right;
            left = l*current[part,0] + r*current[part,1];
            right = l*current[part,2] + r*current[part,3];
        }
    }
}
