using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Pulse {
    public static class Program {
        [STAThread] public static int Main(string[] args) {
            if (args.Contains("--exit")) { try { using (var quit = EventWaitHandle.OpenExisting("Local\\PulseDrumsExit")) quit.Set(); return 0; } catch (WaitHandleCannotBeOpenedException) { return 0; } }
            bool smoke = args.Contains("--smoke");
            bool created;
            using (var mutex = new Mutex(true, smoke ? "Local\\PulseDrumsSmoke" : "Local\\PulseDrums", out created)) {
                if (!created) { using (var wake = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\PulseDrumsShow")) wake.Set(); return 0; }
                try { var app = new Application(); var controller = new Controller(smoke, args.Contains("--background")); app.Run(controller.Window); return Environment.ExitCode; }
                catch (Exception e) {
                    Directory.CreateDirectory(SettingsStore.Folder); File.WriteAllText(System.IO.Path.Combine(SettingsStore.Folder, "error.log"), e.ToString());
                    if (!smoke) MessageBox.Show("Pulse could not start. " + e.Message, "Pulse");
                    return 1;
                }
            }
        }
    }
    public sealed class Controller {
        public Window Window;
        Settings settings;
        volatile Settings live;
        readonly bool smoke, background;
        bool updating, exiting, dirty;
        int selected, hits;
        readonly Stopwatch clock = Stopwatch.StartNew();
        long saveAt, lastMidiScan;
        readonly long[] lastHits = Enumerable.Repeat(-1000L, 8).ToArray();
        readonly double[] meters = new double[8];
        readonly Border[] padCards = new Border[8], meterFills = new Border[8];
        readonly TextBlock[] rawLabels = new TextBlock[8], padNotes = new TextBlock[8];
        readonly ConcurrentQueue<Frame> frames = new ConcurrentQueue<Frame>();
        readonly ConcurrentQueue<string> logs = new ConcurrentQueue<string>();
        volatile PortInfo[] discovered;
        volatile string connectionText = "Looking for your drums";
        volatile bool ready;
        volatile string identified;
        string portsSignature = "", midiSignature = "", midiState = "MIDI off";
        readonly Midi midi = new Midi();
        AudioEngine audio;
        DrumConnection connection;
        Forms.NotifyIcon tray;
        EventWaitHandle wake, quit;
        RegisteredWaitHandle wakeRegistration, quitRegistration;
        DispatcherTimer timer;
        System.Threading.Timer midiTimer;
        string savePath;
        public Controller(bool isSmoke, bool startHidden) {
            smoke = isSmoke; background = startHidden;
            string warning = ""; settings = smoke ? new Settings() : SettingsStore.Load(SettingsStore.PathName, out warning); live = settings.Copy(); savePath = SettingsStore.PathName;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Pulse.Main.xaml")) Window = (Window)XamlReader.Load(stream);
            Window.SourceInitialized += delegate { int dark = 1; try { DwmSetWindowAttribute(new WindowInteropHelper(Window).Handle, 20, ref dark, 4); } catch { } };
            MakePads(); BindControls(); SelectPad(0);
            if (warning != "") logs.Enqueue(warning);
            Window.Loaded += delegate {
                if (smoke) {
                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                    t.Tick += delegate { t.Stop(); Smoke(); }; t.Start(); return;
                }
                audio = new AudioEngine(); audio.Volume = (float)settings.Volume; audio.Start();
                connection = new DrumConnection(settings);
                connection.Status += (message, verified) => { connectionText = message; ready = verified; };
                connection.PortsChanged += p => discovered = p;
                connection.Identified += id => identified = id;
                connection.Log += message => logs.Enqueue(message);
                connection.Received += f => { if (f.Kind == "HIT") Hit(f.Index, f.Value, false); else Enqueue(f); };
                connection.Start();
                midiTimer = new System.Threading.Timer(_ => midi.Tick(clock.ElapsedMilliseconds), null, 0, 5);
                MakeTray();
                wake = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\PulseDrumsShow");
                wakeRegistration = ThreadPool.RegisterWaitForSingleObject(wake, (_, timedOut) => Window.Dispatcher.BeginInvoke(new Action(Show)), null, Timeout.Infinite, false);
                quit = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\PulseDrumsExit");
                quitRegistration = ThreadPool.RegisterWaitForSingleObject(quit, (_, timedOut) => Window.Dispatcher.BeginInvoke(new Action(() => { exiting = true; Window.Close(); })), null, Timeout.Infinite, false);
                timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) }; timer.Tick += delegate { Tick(); }; timer.Start();
                if (background) Window.Hide();
            };
            Window.PreviewKeyDown += (s, e) => {
                if (e.IsRepeat || e.OriginalSource is TextBox || e.OriginalSource is ComboBox || e.OriginalSource is ComboBoxItem || e.OriginalSource is Slider || e.OriginalSource is Thumb) return;
                int key = (int)e.Key - (int)System.Windows.Input.Key.D1;
                if (key >= 0 && key < 8) { SelectPad(key); Hit(key, 100, true); e.Handled = true; }
            };
            Window.Closing += (s, e) => { if (!smoke && !exiting && settings.MinimizeToTray && tray != null) { e.Cancel = true; Window.Hide(); return; } Shutdown(); };
        }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        T Get<T>(string name) where T : class { return Window.FindName(name) as T; }
        static Brush Brush(string color) { return (Brush)new BrushConverter().ConvertFromString(color); }
        static TextBlock Text(string value, double size, string color) { return new TextBlock { Text = value, FontSize = size, Foreground = Brush(color) }; }
        void MakePads() {
            var grid = Get<UniformGrid>("PadsGrid");
            for (int i = 0; i < 8; i++) {
                int index = i;
                var card = new Border { Background = Brush("#1B1F19"), CornerRadius = new CornerRadius(10), BorderBrush = Brush("#30372B"), BorderThickness = new Thickness(1), Padding = new Thickness(14), Margin = new Thickness(5,0,5,10), Height = 134 };
                var contents = new Grid();
                contents.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); contents.RowDefinitions.Add(new RowDefinition()); contents.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var top = new Grid(); top.Children.Add(Text("A" + i, 10, "#88937F")); var key = Text((i+1).ToString(), 10, "#849576"); key.HorizontalAlignment = HorizontalAlignment.Right; top.Children.Add(key); contents.Children.Add(top);
                var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; center.Children.Add(Text(Protocol.Names[i], 17, "#E7ECDF"));
                padNotes[i] = Text("NOTE " + settings.Pads[i].Note, 9, "#929C89"); padNotes[i].Margin = new Thickness(0,5,0,0); center.Children.Add(padNotes[i]); Grid.SetRow(center,1); contents.Children.Add(center);
                var bottom = new StackPanel(); rawLabels[i] = Text("PEAK  —", 9, "#77846E"); rawLabels[i].Margin = new Thickness(0,0,0,6); bottom.Children.Add(rawLabels[i]);
                var track = new Border { Background = Brush("#30392A"), Height = 4, CornerRadius = new CornerRadius(2), ClipToBounds = true };
                meterFills[i] = new Border { Background = Brush("#C5F36B"), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, CornerRadius = new CornerRadius(2) }; track.Child = meterFills[i]; bottom.Children.Add(track); Grid.SetRow(bottom,2); contents.Children.Add(bottom);
                card.Child = contents; padCards[i] = card;
                var button = new Button { Content = card, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = "Tune " + Protocol.Names[i] + ". Double-click to audition." };
                System.Windows.Automation.AutomationProperties.SetName(button, Protocol.Names[i] + " pad settings");
                button.Click += delegate { SelectPad(index); };
                button.MouseDoubleClick += delegate { Hit(index, 100, true); };
                grid.Children.Add(button);
            }
        }
        void BindControls() {
            updating = true;
            Get<CheckBox>("SoundToggle").IsChecked = settings.Sound;
            Get<CheckBox>("TrayToggle").IsChecked = settings.MinimizeToTray;
            Get<Slider>("VolumeSlider").Value = settings.Volume * 100;
            Get<ComboBox>("NoteCombo").ItemsSource = Enumerable.Range(0,128).ToArray();
            Get<ComboBox>("ChannelCombo").ItemsSource = Enumerable.Range(1,16).ToArray(); Get<ComboBox>("ChannelCombo").SelectedItem = settings.Channel;
            Get<ComboBox>("MidiCombo").Items.Add("Off — built-in sounds only"); Get<ComboBox>("MidiCombo").SelectedIndex = 0;
            Get<ComboBox>("PortCombo").Items.Add(new PortInfo { Name = "", Label = "Auto-detect Arduino", Id = "" }); Get<ComboBox>("PortCombo").SelectedIndex = 0;
            try { using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) Get<CheckBox>("StartupToggle").IsChecked = key != null && key.GetValue("PulseDrums") != null; } catch { }
            updating = false;
            foreach (string name in new[] {"Threshold", "Reset", "Gain", "Floor", "Curve", "Guard"}) Get<Slider>(name + "Slider").ValueChanged += delegate { if (!updating) PadChanged(); };
            Get<Slider>("VolumeSlider").ValueChanged += delegate { if (updating) return; settings.Volume = Get<Slider>("VolumeSlider").Value / 100; Get<TextBlock>("VolumeValue").Text = Math.Round(settings.Volume * 100) + "%"; Changed(false); };
            Get<CheckBox>("SoundToggle").Click += delegate { settings.Sound = Get<CheckBox>("SoundToggle").IsChecked == true; if (!settings.Sound && audio != null) audio.Panic(); Changed(false); };
            Get<CheckBox>("TrayToggle").Click += delegate { settings.MinimizeToTray = Get<CheckBox>("TrayToggle").IsChecked == true; Changed(false); };
            Get<CheckBox>("MuteToggle").Click += delegate { settings.Pads[selected].Muted = Get<CheckBox>("MuteToggle").IsChecked == true; if (settings.Pads[selected].Muted) { midi.Panic(); if (audio != null) audio.Panic(); } Changed(false); SelectPad(selected); };
            Get<ComboBox>("NoteCombo").SelectionChanged += delegate { if (updating) return; settings.Pads[selected].Note = (int)Get<ComboBox>("NoteCombo").SelectedItem; Changed(false); RefreshValues(); };
            Get<ComboBox>("ChannelCombo").SelectionChanged += delegate { if (updating) return; settings.Channel = (int)Get<ComboBox>("ChannelCombo").SelectedItem; midi.Panic(); Changed(false); };
            Get<ComboBox>("PortCombo").SelectionChanged += delegate { if (updating) return; var p = Get<ComboBox>("PortCombo").SelectedItem as PortInfo; if (p == null) return; settings.Port = p.Name; Changed(false); };
            Get<ComboBox>("MidiCombo").SelectionChanged += delegate { if (updating) return; var cb = Get<ComboBox>("MidiCombo"); settings.MidiOutput = cb.SelectedIndex <= 0 ? "" : Convert.ToString(cb.SelectedItem); midi.Panic(); lastMidiScan = -10000; Changed(false); };
            Get<CheckBox>("StartupToggle").Click += delegate {
                if (smoke) return;
                try {
                    using (var key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) {
                        if (Get<CheckBox>("StartupToggle").IsChecked == true) key.SetValue("PulseDrums", "\"" + Assembly.GetExecutingAssembly().Location + "\" --background"); else key.DeleteValue("PulseDrums", false);
                    }
                    logs.Enqueue("Windows launch preference updated.");
                } catch (Exception e) { Get<CheckBox>("StartupToggle").IsChecked = false; logs.Enqueue("Could not change startup: " + e.Message); }
            };
            Get<Button>("TestPad").Click += delegate { Hit(selected,100,true); };
            Get<Button>("ResetPad").Click += delegate { settings.Pads[selected] = new Pad { Note = Protocol.Notes[selected] }; Changed(true); SelectPad(selected); };
            Get<Button>("PanicButton").Click += delegate { midi.Panic(); if (audio != null) audio.Panic(); Get<TextBlock>("LastHit").Text = "Active sounds stopped."; };
            Get<Button>("LogButton").Click += delegate { var panel = Get<Border>("LogPanel"); panel.Visibility = panel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; };
        }
        void SelectPad(int index) {
            selected = index; updating = true; var p = settings.Pads[index];
            Get<TextBlock>("SelectedName").Text = Protocol.Names[index]; Get<CheckBox>("MuteToggle").IsChecked = p.Muted;
            Get<Slider>("ThresholdSlider").Value = p.Hit; Get<Slider>("ResetSlider").Maximum = p.Hit - 1; Get<Slider>("ResetSlider").Value = p.Reset;
            Get<Slider>("GainSlider").Value = p.Gain; Get<Slider>("CurveSlider").Value = p.Curve; Get<Slider>("GuardSlider").Value = p.RetriggerMs;
            Get<Slider>("FloorSlider").Value = p.VelocityFloor;
            Get<ComboBox>("NoteCombo").SelectedItem = p.Note;
            for (int i = 0; i < 8; i++) { padCards[i].BorderBrush = Brush(i == index ? "#C5F36B" : "#30372B"); padCards[i].Background = Brush(i == index ? "#28321F" : "#1B1F19"); padCards[i].Opacity = settings.Pads[i].Muted ? .5 : 1; }
            updating = false; RefreshValues();
        }
        void PadChanged() {
            var p = settings.Pads[selected]; p.Hit = (int)Get<Slider>("ThresholdSlider").Value; p.Reset = (int)Get<Slider>("ResetSlider").Value;
            p.Gain = Get<Slider>("GainSlider").Value; p.Curve = Get<Slider>("CurveSlider").Value; p.RetriggerMs = (int)Get<Slider>("GuardSlider").Value;
            p.VelocityFloor = (int)Get<Slider>("FloorSlider").Value;
            settings.Normalize(); updating = true; Get<Slider>("ResetSlider").Maximum = p.Hit - 1; Get<Slider>("ResetSlider").Value = p.Reset; updating = false;
            Changed(true); RefreshValues();
        }
        void RefreshValues() {
            var p = settings.Pads[selected];
            Get<TextBlock>("ThresholdValue").Text = p.Hit.ToString(); Get<TextBlock>("ResetValue").Text = p.Reset.ToString();
            Get<TextBlock>("GainValue").Text = p.Gain.ToString("0.00") + "×"; Get<TextBlock>("CurveValue").Text = p.Curve.ToString("0.00"); Get<TextBlock>("GuardValue").Text = p.RetriggerMs + " ms";
            Get<TextBlock>("FloorValue").Text = p.VelocityFloor.ToString();
            Get<TextBlock>("SelectedInput").Text = "A" + selected + " / " + p.Note;
            padNotes[selected].Text = p.Muted ? "MUTED" : "NOTE " + p.Note;
            Get<TextBlock>("VolumeValue").Text = Math.Round(settings.Volume * 100) + "%";
        }
        void Changed(bool thresholds) {
            settings.Normalize(); live = settings.Copy(); dirty = true; saveAt = clock.ElapsedMilliseconds + 450;
            if (audio != null) audio.Volume = (float)settings.Volume;
            if (connection != null) connection.Configure(settings, thresholds);
            Get<TextBlock>("SaveStatus").Text = "Saving settings…";
        }
        void Save() {
            if (smoke) { dirty = false; return; }
            try { SettingsStore.Save(settings, savePath); dirty = false; Get<TextBlock>("SaveStatus").Text = "Saved on this PC · restored on reconnect"; }
            catch (Exception e) { dirty = false; Get<TextBlock>("SaveStatus").Text = "Could not save settings · see diagnostics"; logs.Enqueue(e.Message); }
        }
        void Enqueue(Frame f) { if (frames.Count > 512) { Frame discard; frames.TryDequeue(out discard); } frames.Enqueue(f); }
        void Hit(int index, int velocity, bool preview) {
            var cfg = live; var p = cfg.Pads[index]; long now = clock.ElapsedMilliseconds;
            if (!preview && now - lastHits[index] < p.RetriggerMs) return;
            if (!preview) lastHits[index] = now;
            int output = Protocol.Velocity(velocity,p);
            Enqueue(new Frame(preview ? "PREVIEW" : "HIT",index,output));
            if (p.Muted || smoke) return;
            if (cfg.Sound && audio != null) audio.Hit(index,output);
            midi.Hit(p.Note,output,cfg.Channel,now);
        }
        void Tick() {
            if (timer != null) timer.Interval = TimeSpan.FromMilliseconds(Window.IsVisible ? 25 : 250);
            Get<TextBlock>("StatusText").Text = connectionText;
            Get<System.Windows.Shapes.Ellipse>("StatusDot").Fill = Brush(ready ? "#C5F36B" : "#B7A26B");
            if (identified != null) { settings.DeviceId = identified; identified = null; Changed(false); }
            var ports = discovered;
            if (ports != null) {
                string signature = String.Join("|",ports.Select(p => p.Name + p.Id)) + settings.Port;
                if (signature != portsSignature) {
                    portsSignature = signature; updating = true; var cb = Get<ComboBox>("PortCombo"); cb.Items.Clear();
                    cb.Items.Add(new PortInfo { Name = "", Label = "Auto-detect Arduino", Id = "" }); foreach (var p in ports) cb.Items.Add(p);
                    if (settings.Port != "" && !ports.Any(p => p.Name == settings.Port)) cb.Items.Add(new PortInfo {Name = settings.Port, Label = settings.Port + " (unplugged)", Id = ""});
                    cb.SelectedItem = cb.Items.Cast<PortInfo>().First(p => p.Name == settings.Port); updating = false;
                }
            }
            if (clock.ElapsedMilliseconds - lastMidiScan > 2000 || lastMidiScan == 0) {
                lastMidiScan = clock.ElapsedMilliseconds;
                try {
                    string[] names = Midi.Outputs(); string signature = String.Join("|", names) + settings.MidiOutput;
                    if (signature != midiSignature) {
                        midiSignature = signature; updating = true; var cb = Get<ComboBox>("MidiCombo"); cb.Items.Clear(); cb.Items.Add("Off — built-in sounds only"); foreach (string name in names) cb.Items.Add(name);
                        if (settings.MidiOutput != "" && !names.Contains(settings.MidiOutput)) cb.Items.Add(settings.MidiOutput);
                        cb.SelectedIndex = settings.MidiOutput == "" ? 0 : cb.Items.IndexOf(settings.MidiOutput); updating = false;
                    }
                    midiState = midi.Ensure(settings.MidiOutput); Get<TextBlock>("MidiStatus").Text = midiState;
                } catch (Exception e) { logs.Enqueue("MIDI: " + e.Message); }
            }
            Frame f; int count = 0;
            while (count++ < 128 && frames.TryDequeue(out f)) {
                if (f.Kind == "RAW") { rawLabels[f.Index].Text = "PEAK  " + f.Value; }
                else { meters[f.Index] = Math.Max(.06, f.Value / 127.0); if (f.Kind == "HIT") { hits++; Get<TextBlock>("HitCount").Text = hits.ToString("N0"); } Get<TextBlock>("LastHit").Text = (f.Kind == "PREVIEW" ? "Audition · " : "Last hit · ") + Protocol.Names[f.Index] + " · velocity " + f.Value + (settings.Pads[f.Index].Muted ? " · muted" : ""); }
            }
            for (int i = 0; i < 8; i++) { meterFills[i].Width = Math.Max(0, padCards[i].ActualWidth - 30) * meters[i]; meters[i] *= .91; }
            string line; while (logs.TryDequeue(out line)) {
                string entry = DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine;
                var box = Get<TextBox>("LogText"); box.AppendText(entry); if (box.Text.Length > 14000) box.Text = box.Text.Substring(box.Text.Length - 10000); box.ScrollToEnd();
                if (!smoke) try { Directory.CreateDirectory(SettingsStore.Folder); string logPath = System.IO.Path.Combine(SettingsStore.Folder,"device.log"); if (File.Exists(logPath) && new FileInfo(logPath).Length > 512000) File.WriteAllText(logPath,""); File.AppendAllText(logPath,entry); } catch { }
            }
            if (audio != null && audio.Error != "") Get<TextBlock>("AudioStatus").Text = audio.Error;
            if (dirty && clock.ElapsedMilliseconds >= saveAt) Save();
        }
        void MakeTray() {
            tray = new Forms.NotifyIcon { Text = "Pulse · Drum control", Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location), Visible = true };
            var menu = new Forms.ContextMenuStrip(); menu.Items.Add("Open Pulse",null,(s,e) => Show()); menu.Items.Add("Silence all",null,(s,e) => { midi.Panic(); if (audio != null) audio.Panic(); }); menu.Items.Add("Exit",null,(s,e) => { exiting = true; Window.Close(); }); tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Show(); };
        }
        void Show() { Window.Show(); Window.WindowState = WindowState.Normal; Window.Activate(); }
        void Shutdown() {
            if (timer != null) timer.Stop(); if (dirty) Save();
            if (connection != null) connection.Dispose();
            if (midiTimer != null) { using (var done = new ManualResetEvent(false)) { if (midiTimer.Dispose(done)) done.WaitOne(); } }
            if (audio != null) audio.Dispose(); midi.Dispose();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (wakeRegistration != null) wakeRegistration.Unregister(null); if (wake != null) wake.Dispose();
            if (quitRegistration != null) quitRegistration.Unregister(null); if (quit != null) quit.Dispose();
        }
        void Screenshot(string file) {
            Window.UpdateLayout(); var content = (FrameworkElement)Window.Content;
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32); bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(file)) encoder.Save(stream);
        }
        void Smoke() {
            string folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts")); Directory.CreateDirectory(folder);
            try {
                connectionText = "Preview · no hardware connection"; Tick(); Window.UpdateLayout(); Screenshot(System.IO.Path.Combine(folder,"pulse-preview.png"));
                SelectPad(1); Get<Slider>("ThresholdSlider").Value = 70; Get<Slider>("ResetSlider").Value = 32; Get<Slider>("GainSlider").Value = 1.35;
                if (settings.Pads[1].Hit != 70 || settings.Pads[1].Reset != 32 || Math.Abs(settings.Pads[1].Gain - 1.35) > .01) throw new Exception("Pad sliders did not update settings");
                Get<Slider>("ThresholdSlider").Value = 20; if (settings.Pads[1].Reset >= 20) throw new Exception("Reset threshold invariant broken");
                Get<Slider>("ThresholdSlider").Value = 70; Get<Slider>("ResetSlider").Value = 32;
                Get<Button>("TestPad").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick();
                if (hits != 0 || !Get<TextBlock>("LastHit").Text.StartsWith("Audition")) throw new Exception("Audition incorrectly counted as a real hit");
                Get<ComboBox>("NoteCombo").SelectedItem = 40; if (settings.Pads[1].Note != 40) throw new Exception("MIDI remap failed");
                Get<Button>("ResetPad").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (settings.Pads[1].Note != 38 || settings.Pads[1].Hit != 40) throw new Exception("Pad reset failed");
                Screenshot(System.IO.Path.Combine(folder,"pulse-tuning.png"));
                File.WriteAllText(System.IO.Path.Combine(folder,"ui-smoke.txt"),"PASS: XAML load, 8 pads, sliders, reset invariant, audition, MIDI remap, pad reset, screenshots. No hardware or settings writes.");
            } catch (Exception e) { File.WriteAllText(System.IO.Path.Combine(folder,"ui-smoke.txt"),"FAIL: " + e); Environment.ExitCode = 1; }
            exiting = true; Window.Close();
        }
    }
}
