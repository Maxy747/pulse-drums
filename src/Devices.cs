using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Threading;

namespace Pulse {
    public sealed class PortInfo {
        public string Name, Label, Id;
        public bool Candidate;
        public override string ToString() { return Label; }
    }
    public sealed class DrumConnection : IDisposable {
        public event Action<string, bool> Status;
        public event Action<Frame> Received;
        public event Action<PortInfo[]> PortsChanged;
        public event Action<string> Log;
        public event Action<string> Identified;
        volatile Settings config;
        volatile bool stop;
        Thread worker, scanner;
        SerialPort port;
        string desired = "", connectedPreference = "", currentId = "";
        volatile bool settingsDirty = true;
        public volatile bool Verified;
        string lastStatus = "";
        int candidateIndex;
        readonly Stopwatch watch = Stopwatch.StartNew();
        long opened, lastCommand;
        HitPairer pairer = new HitPairer();
        Queue<string> commands = new Queue<string>();
        LineDecoder decoder = new LineDecoder();
        volatile PortInfo[] ports = new PortInfo[0];
        public DrumConnection(Settings settings) { config = settings.Copy(); }
        public void Start() {
            scanner = new Thread(ScanLoop) { IsBackground = true, Name = "Pulse USB discovery" }; scanner.Start();
            worker = new Thread(Run) { IsBackground = true, Name = "Pulse serial" }; worker.Start();
        }
        void ScanLoop() {
            string lastScanError = "";
            while (!stop) {
                try { ports = Scan(); if (PortsChanged != null) PortsChanged(ports); }
                catch (Exception e) { if (lastScanError != e.Message && Log != null) Log("USB scan: " + e.Message); lastScanError = e.Message; }
                for (int i = 0; i < 15 && !stop; i++) Thread.Sleep(100);
            }
        }
        public void Configure(Settings settings, bool thresholdsChanged) { config = settings.Copy(); if (thresholdsChanged) settingsDirty = true; }
        void SetStatus(string message, bool ready) { if (lastStatus == message) return; lastStatus = message; if (Status != null) Status(message, ready); }
        public static PortInfo[] Scan() {
            var result = new List<PortInfo>();
            using (var query = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPClass='Ports'")) {
                foreach (ManagementObject d in query.Get()) {
                    using (d) {
                        string label = Convert.ToString(d["Name"]), id = Convert.ToString(d["PNPDeviceID"]);
                        int start = label.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
                        if (start < 0 || !label.EndsWith(")")) continue;
                        string name = label.Substring(start + 1, label.Length - start - 2);
                        result.Add(new PortInfo { Name = name, Label = label, Id = id, Candidate = IsCandidate(id) });
                    }
                }
            }
            foreach (string name in SerialPort.GetPortNames()) if (!result.Any(p => p.Name == name)) result.Add(new PortInfo { Name = name, Label = name, Id = "", Candidate = false });
            return result.OrderBy(p => p.Name).ToArray();
        }
        public static bool IsCandidate(string id) {
            return new[] {"VID_1A86", "VID_2341", "VID_2A03", "VID_0403", "VID_10C4"}.Any(v => (id ?? "").IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        void Run() {
            while (!stop) {
                try {
                    var cfg = config;
                    if (port != null && !ports.Any(p => p.Name == port.PortName && p.Id == currentId)) Disconnect();
                    if (port != null && connectedPreference != cfg.Port) Disconnect();
                    if (port == null) {
                        var candidates = String.IsNullOrEmpty(cfg.Port) ? ports.Where(p => p.Candidate).OrderByDescending(p => p.Id == cfg.DeviceId).ToArray() : ports.Where(p => p.Name == cfg.Port).ToArray();
                        if (candidates.Length == 0) { SetStatus(String.IsNullOrEmpty(cfg.Port) ? "Waiting for your drums" : cfg.Port + " unplugged · waiting", false); Thread.Sleep(200); continue; }
                        var next = candidates[candidateIndex % candidates.Length]; desired = next.Name; currentId = next.Id;
                        connectedPreference = cfg.Port;
                        port = new SerialPort(next.Name, 115200) { ReadTimeout = 80, WriteTimeout = 200, DtrEnable = true, RtsEnable = false, NewLine = "\n" };
                        port.Open(); opened = watch.ElapsedMilliseconds; decoder = new LineDecoder(); pairer = new HitPairer();
                        Verified = false; settingsDirty = true; commands.Clear();
                        SetStatus(next.Name + " open · hit a pad to verify", false);
                        if (Log != null) Log("Listening on " + next.Label + " at 115200 baud.");
                    }
                    if (port.BytesToRead > 0) decoder.Feed(port.ReadExisting(), HandleFrame);
                    if (Verified && watch.ElapsedMilliseconds - opened > 1800) {
                        if (settingsDirty) { settingsDirty = false; commands = new Queue<string>(Protocol.Thresholds(config)); }
                        // Pace complete lines to avoid overflowing the Nano's small receive buffer.
                        if (commands.Count > 0 && watch.ElapsedMilliseconds - lastCommand >= 25) {
                            port.Write(commands.Dequeue()); lastCommand = watch.ElapsedMilliseconds;
                            if (commands.Count == 0 && Log != null) Log("Thresholds sent. This sketch does not acknowledge settings.");
                        }
                    }
                    if (!Verified && String.IsNullOrEmpty(cfg.Port) && ports.Count(p => p.Candidate) > 1 && watch.ElapsedMilliseconds - opened > 8000) { candidateIndex++; Disconnect(); }
                    Thread.Sleep(3);
                }
                catch (Exception e) {
                    Disconnect(); candidateIndex++;
                    SetStatus((String.IsNullOrEmpty(desired) ? "USB scan" : desired) + " unavailable · retrying", false);
                    if (Log != null && lastError != e.Message) { lastError = e.Message; Log(e.Message + " Close other serial apps if the port is busy."); }
                    for (int i = 0; i < 15 && !stop; i++) Thread.Sleep(100);
                }
            }
            Disconnect();
        }
        string lastError = "";
        void HandleFrame(Frame frame) {
            long now = watch.ElapsedMilliseconds;
            Frame hit;
            if (pairer.Accept(frame, now, out hit)) {
                if (!Verified) {
                    Verified = true; SetStatus(port.PortName + " · drums ready", true);
                    if (Identified != null) Identified(currentId);
                    if (Log != null) Log("NOTE / RAW protocol verified. Your saved thresholds will be restored.");
                }
                // Pair with RAW's pad index so modified note mappings also work.
                if (hit.Value > 0 && Received != null) Received(hit);
            }
            if (Received != null && frame.Kind == "RAW") Received(frame);
        }
        void Disconnect() {
            Verified = false; commands.Clear();
            if (port != null) { try { port.Dispose(); } catch { } port = null; }
        }
        public void Dispose() { stop = true; if (worker != null) worker.Join(3000); if (scanner != null) scanner.Join(2000); }
    }
}
