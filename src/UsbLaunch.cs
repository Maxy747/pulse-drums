using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Threading;
using Microsoft.Win32;

namespace Pulse {
    public static class UsbLaunch {
        const string StopName = "Local\\PulseDrumsUsbWatchStop";
        public static bool Matches(PortInfo port, Settings settings) {
            if (!String.IsNullOrEmpty(settings.DeviceId)) return String.Equals(port.Id,settings.DeviceId,StringComparison.OrdinalIgnoreCase);
            return port.Candidate && (settings.Port == "" || port.Name == settings.Port);
        }
        public static bool Enabled {
            get { using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) return key != null && key.GetValue("PulseDrumsUsbWatch") != null; }
        }
        public static void SetEnabled(bool enabled) {
            using (var key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) {
                if (enabled) key.SetValue("PulseDrumsUsbWatch","\"" + Assembly.GetExecutingAssembly().Location + "\" --watch-usb");
                else key.DeleteValue("PulseDrumsUsbWatch",false);
            }
            if (enabled) Start(); else Stop();
        }
        public static void Start() {
            using (var p = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--watch-usb") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })) { }
        }
        public static void Stop() { try { using (var stop = EventWaitHandle.OpenExisting(StopName)) stop.Set(); } catch (WaitHandleCannotBeOpenedException) { } }
        public static int Run() {
            bool created;
            using (var mutex = new Mutex(true,"Local\\PulseDrumsUsbWatch",out created)) {
                if (!created) return 0;
                using (var stop = new EventWaitHandle(false,EventResetMode.ManualReset,StopName))
                using (var changed = new AutoResetEvent(false)) {
                    ManagementEventWatcher watcher = null;
                    try {
                        try { watcher = new ManagementEventWatcher("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2 OR EventType = 3"); watcher.EventArrived += (s,e) => { try { changed.Set(); } catch (ObjectDisposedException) { } }; watcher.Start(); } catch { if (watcher != null) watcher.Dispose(); watcher = null; }
                        string previous = ""; int retries = 3;
                        while (!stop.WaitOne(0)) {
                            try {
                                string warning; var settings = SettingsStore.Load(SettingsStore.PathName,out warning); settings.Normalize();
                                // Never open the COM port here. The main app verifies the drum protocol.
                                var ports = DrumConnection.Scan().Where(p => Matches(p,settings)).ToArray();
                                string current = String.Join("|",ports.Select(p => p.Id + ":" + p.Name));
                                if (current != "" && current != previous) {
                                    Mutex appMutex;
                                    if (Mutex.TryOpenExisting("Local\\PulseDrums",out appMutex)) appMutex.Dispose();
                                    else using (var process = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Normal })) { }
                                }
                                previous = current;
                            } catch { /* Transient PnP errors are retried at the next event/poll. */ }
                            int wake = WaitHandle.WaitAny(new WaitHandle[]{stop,changed},retries > 0 ? 1000 : 30000);
                            if (wake == 0) break;
                            retries = wake == 1 ? 3 : Math.Max(0,retries-1);
                            if (stop.WaitOne(300)) break; // Allow Windows to finish enumerating the COM port.
                        }
                    } finally { if (watcher != null) { try { watcher.Stop(); } catch { } watcher.Dispose(); } }
                }
            }
            return 0;
        }
    }
}
