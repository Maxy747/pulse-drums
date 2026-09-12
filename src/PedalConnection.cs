using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Threading;

namespace Pulse {
    public static class SerialClaims {
        static readonly HashSet<string> ports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static bool Take(string port) { lock (ports) return ports.Add(port); }
        public static void Release(string port) { lock (ports) ports.Remove(port); }
    }
    public sealed class PedalConnection : IDisposable {
        public event Action<PedalFrame> Received;
        public event Action<string,bool> Status;
        public event Action<string> Identified;
        volatile Settings config;
        volatile PortInfo[] ports = new PortInfo[0];
        volatile bool stop; Thread worker;
        public volatile bool Verified;
        string lastStatus = "";
        public PedalConnection(Settings settings) { Configure(settings); }
        public void Configure(Settings settings) { config=settings.Copy(); }
        public void SetPorts(PortInfo[] value) { ports=value; }
        public void Start() { worker=new Thread(Run) { IsBackground=true,Name="Pulse pedal Nano" }; worker.Start(); }
        void Report(string text,bool ready) { Verified=ready; if (text == lastStatus) return; lastStatus=text; if (Status != null) Status(text,ready); }
        public static bool Candidate(PortInfo p, Settings s) {
            if (!s.PedalsEnabled || p.Name == s.Port || (s.DeviceId != "" && p.Id == s.DeviceId)) return false;
            return s.PedalPort != "" ? p.Name == s.PedalPort : p.Candidate;
        }
        void Run() {
            int attempt=0;
            while (!stop) {
                var cfg=config;
                if (!cfg.PedalsEnabled) { Report("Pedals disabled",false); Thread.Sleep(100); continue; }
                var candidates=ports.Where(p => Candidate(p,cfg)).OrderByDescending(p => p.Id == cfg.PedalDeviceId && p.Id != "").ToArray();
                if (candidates.Length == 0) { Report("Waiting for pedal Nano",false); Thread.Sleep(200); continue; }
                var next=candidates[attempt++ % candidates.Length];
                if (!SerialClaims.Take(next.Name)) { Thread.Sleep(100); continue; }
                try {
                    using (var serial=new SerialPort(next.Name,115200) { DtrEnable=true,RtsEnable=false,ReadTimeout=100 }) {
                        serial.Open(); var watch=Stopwatch.StartNew(); long lastData=0; bool identity=false;
                        var decoder=new PedalDecoder(); Report(next.Name + " · checking pedal sketch",false);
                        while (!stop && config.PedalsEnabled && config.PedalPort == cfg.PedalPort && Candidate(next,config) && ports.Any(p => p.Name == next.Name && p.Id == next.Id)) {
                            if (serial.BytesToRead > 0) decoder.Feed(serial.ReadExisting(),() => identity=true,f => {
                                if (!identity) return;
                                lastData=watch.ElapsedMilliseconds;
                                if (!Verified) { Report(next.Name + " · pedals ready",true); if (Identified != null) Identified(next.Id); }
                                if (Received != null) Received(f);
                            });
                            if ((!Verified && watch.ElapsedMilliseconds > 3000) || (Verified && watch.ElapsedMilliseconds-lastData > 600)) break;
                            Thread.Sleep(2);
                        }
                    }
                    Report(next.Name + " · waiting for PulsePedals sketch/data",false);
                } catch (Exception) { Report(next.Name + " · unavailable, retrying",false); }
                finally { Verified=false; SerialClaims.Release(next.Name); }
                for (int i=0;i<10 && !stop;i++) Thread.Sleep(50);
            }
        }
        public void Dispose() { stop=true; if (worker != null) worker.Join(2500); }
    }
}
