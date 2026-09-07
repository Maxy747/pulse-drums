using System;
using NAudio.Wave;

namespace Pulse {
    public sealed class AsioOutput : IDisposable {
        sealed class Provider : IWaveProvider {
            readonly AudioEngine engine;
            public short[] Samples;
            public Provider(AudioEngine audio) { engine = audio; }
            public WaveFormat WaveFormat { get { return new WaveFormat(AudioEngine.Rate,16,2); } }
            public int Read(byte[] buffer, int offset, int count) {
                try { engine.MixBlock(Samples,count/2); Buffer.BlockCopy(Samples,0,buffer,offset,count); System.Threading.Interlocked.Increment(ref engine.RenderedBlocks); }
                catch (Exception e) { engine.Error = "ASIO playback: " + e.Message; Array.Clear(buffer,offset,count); }
                return count;
            }
        }
        AsioOut output;
        public string Status;
        public volatile bool ResetRequested;
        public static string[] Drivers() { return AsioOut.GetDriverNames(); }
        public AsioOutput(AudioEngine engine, string driver) {
            try {
                output = new AsioOut(driver);
                if (output.DriverOutputChannelCount < 2) throw new InvalidOperationException("This ASIO driver has no stereo output.");
                var provider = new Provider(engine); output.Init(provider);
                provider.Samples = new short[output.FramesPerBuffer*2];
                output.DriverResetRequest += delegate { ResetRequested = true; };
                output.PlaybackStopped += (s,e) => { if (e.Exception != null) engine.Error = "ASIO: " + e.Exception.Message; };
                Status = driver + " · outputs 1/2 · " + output.FramesPerBuffer + " samples · driver latency " + (output.PlaybackLatency*1000.0/AudioEngine.Rate).ToString("0.0") + " ms";
                output.Play();
            } catch { Dispose(); throw; }
        }
        public void ControlPanel() { if (output != null) output.ShowControlPanel(); }
        public void Dispose() { if (output != null) { output.Dispose(); output = null; } }
    }
}
