using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace Pulse {
    public sealed partial class Controller {
        PedalConnection pedals;
        readonly object pedalGate = new object();
        readonly PedalMotion pedalMotion = new PedalMotion();
        volatile bool pedalReady, hatClosed;
        volatile string pedalStatus = "Pedals disabled", pedalIdentified;
        volatile int kickRaw,hatRaw;
        int lastPedalCc = -1, hatLoadVersion;
        bool pedalUpdating;
        string pedalPortSignature = "";
        void BuildPedalControls() {
            RefreshPedalControls();
            RoutedEventHandler swapInputs = delegate {
                if (pedalUpdating) return;
                lock (pedalGate) {
                    bool swap=Get<CheckBox>("PedalSwapToggle").IsChecked == true;
                    if (settings.SwapPedalInputs == swap) return;
                    settings.SetPedalSwap(swap);
                    int oldKick=kickRaw; kickRaw=hatRaw; hatRaw=oldKick;
                    hatClosed=PedalMotion.Position(hatRaw,settings.HatRest,settings.HatDown) >= .75;
                    pedalMotion.Reset(); lastPedalCc=-1; Changed(false);
                }
                midi.Panic(); var a=audio; if (a != null) a.ChokeHat(); TickPedals();
            };
            Get<CheckBox>("PedalSwapToggle").Checked += swapInputs; Get<CheckBox>("PedalSwapToggle").Unchecked += swapInputs;
            RoutedEventHandler change = delegate {
                if (pedalUpdating) return;
                settings.PedalsEnabled=Get<CheckBox>("PedalsToggle").IsChecked == true;
                settings.PedalCloseHit=Get<CheckBox>("PedalCloseToggle").IsChecked == true;
                settings.PedalOnlyKick=Get<CheckBox>("PedalKickOnlyToggle").IsChecked == true;
                lock (pedalGate) { Changed(false); pedalMotion.Reset(); }
                LoadPedalHats();
            };
            foreach (string name in new[] { "PedalsToggle","PedalCloseToggle","PedalKickOnlyToggle" }) { Get<CheckBox>(name).Checked += change; Get<CheckBox>(name).Unchecked += change; }
            Get<Slider>("PedalCloseSlider").ValueChanged += delegate { if (pedalUpdating) return; settings.PedalCloseVelocity=(int)Get<Slider>("PedalCloseSlider").Value; Get<TextBlock>("PedalCloseValue").Text=settings.PedalCloseVelocity.ToString(); Changed(false); };
            Get<ComboBox>("PedalPortCombo").SelectionChanged += delegate { if (pedalUpdating) return; var choice=Get<ComboBox>("PedalPortCombo").SelectedItem as PortInfo; if (choice == null) return; settings.PedalPort=choice.Name; settings.PedalDeviceId=choice.Id; Changed(false); };
            foreach (string name in new[] { "KickRest","KickDown","HatRest","HatDown" }) {
                string target=name; Get<Button>(name + "Button").Click += delegate {
                    if (!pedalReady) return;
                    if (target == "KickRest") settings.KickRest=kickRaw;
                    else if (target == "KickDown") settings.KickDown=kickRaw;
                    else if (target == "HatRest") settings.HatRest=hatRaw;
                    else settings.HatDown=hatRaw;
                    lock (pedalGate) { Changed(false); pedalMotion.Reset(); } TickPedals();
                };
            }
        }
        void RefreshPedalControls() {
            pedalUpdating=true;
            Get<CheckBox>("PedalsToggle").IsChecked=settings.PedalsEnabled;
            Get<CheckBox>("PedalSwapToggle").IsChecked=settings.SwapPedalInputs;
            Get<CheckBox>("PedalCloseToggle").IsChecked=settings.PedalCloseHit;
            Get<CheckBox>("PedalKickOnlyToggle").IsChecked=settings.PedalOnlyKick;
            Get<Slider>("PedalCloseSlider").Value=settings.PedalCloseVelocity;
            Get<TextBlock>("PedalCloseValue").Text=settings.PedalCloseVelocity.ToString();
            pedalUpdating=false;
        }
        void StartPedals() {
            pedals=new PedalConnection(settings);
            pedals.Status += (message,verified) => {
                pedalStatus=message; pedalReady=verified; logs.Enqueue("Pedals: " + message);
                if (!verified) { lock (pedalGate) { pedalMotion.Reset(); lastPedalCc=-1; } var a=audio; if (a != null) a.ChokeHat(); }
            };
            pedals.Identified += id => pedalIdentified=id;
            pedals.Received += ReceivePedals; pedals.Start();
        }
        void ReceivePedals(PedalFrame f) {
            if (exiting) return;
            Settings cfg; PedalResult result;
            lock (pedalGate) {
                cfg=live;
                f=PedalMotion.MapInputs(f,cfg.SwapPedalInputs);
                kickRaw=f.Kick; hatRaw=f.Hat; result=pedalMotion.Accept(f,cfg); hatClosed=result.Closed;
                if (learning || !cfg.PedalsEnabled) return;
                int cc=(int)Math.Round(PedalMotion.Position(f.Hat,cfg.HatRest,cfg.HatDown)*127);
                if (cfg.MidiEnabled && cc != lastPedalCc) { midi.Control(4,cc,cfg.Channel); lastPedalCc=cc; }
            }
            if (result.JustClosed) {
                var a=audio; if (a != null) a.ChokeHat();
                if (cfg.MidiEnabled) midi.StopNote(Math.Max(0,Math.Min(127,46+cfg.Transpose)),cfg.Channel);
                if (cfg.PedalCloseHit && result.CloseVelocity >= cfg.PedalCloseVelocity) Hit(0,result.CloseVelocity,false,true);
            }
            if (result.KickVelocity > 0) Hit(5,result.KickVelocity,false);
        }
        void TickPedals() {
            if (pedalIdentified != null) { settings.PedalDeviceId=pedalIdentified; pedalIdentified=null; Changed(false); }
            Get<TextBlock>("PedalStatus").Text=pedalStatus + (pedalReady ? (hatClosed ? " · hi-hat closed" : " · hi-hat open") : "");
            Get<TextBlock>("KickPedalValue").Text=(settings.SwapPedalInputs ? "A1" : "A0") + " kick · " + kickRaw + "   Released " + settings.KickRest + " / Pressed " + settings.KickDown;
            Get<TextBlock>("HatPedalValue").Text=(settings.SwapPedalInputs ? "A0" : "A1") + " hi-hat · " + hatRaw + "   Released " + settings.HatRest + " / Pressed " + settings.HatDown;
            Get<ProgressBar>("KickPedalMeter").Value=PedalMotion.Position(kickRaw,settings.KickRest,settings.KickDown)*100;
            Get<ProgressBar>("HatPedalMeter").Value=PedalMotion.Position(hatRaw,settings.HatRest,settings.HatDown)*100;
            Get<TextBlock>("PedalCalibrationHint").Text=Math.Abs(settings.KickDown-settings.KickRest) < 20 || Math.Abs(settings.HatDown-settings.HatRest) < 20 ? "Calibration needs at least 20 ADC counts of travel. Capture released and fully pressed again." : "Hold each position, then capture it. Reversed potentiometers are supported. Close velocity is estimated from pedal speed.";
            foreach (string name in new[] { "KickRest","KickDown","HatRest","HatDown" }) Get<Button>(name + "Button").IsEnabled=pedalReady;
            var ports=discovered ?? new PortInfo[0]; string signature=String.Join("|",ports.Select(p => p.Name+p.Id)) + settings.PedalPort;
            if (signature == pedalPortSignature && Get<ComboBox>("PedalPortCombo").Items.Count > 0) return;
            pedalPortSignature=signature; pedalUpdating=true; var combo=Get<ComboBox>("PedalPortCombo"); combo.Items.Clear();
            combo.Items.Add(new PortInfo { Name="",Id="",Label="Auto-detect pedal Nano" });
            foreach (var p in ports) combo.Items.Add(p);
            if (settings.PedalPort != "" && !ports.Any(p => p.Name == settings.PedalPort)) combo.Items.Add(new PortInfo { Name=settings.PedalPort,Id="",Label=settings.PedalPort + " (unplugged)" });
            combo.SelectedItem=combo.Items.Cast<PortInfo>().First(p => p.Name == settings.PedalPort); pedalUpdating=false;
        }
        void LoadPedalHats() {
            if (smoke || library.Length == 0) return;
            int kit=settings.SampleFiles[0].IndexOf("Kit 2",StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 1;
            string closed=SampleLibrary.HatArticulation(library,kit,false), open=SampleLibrary.HatArticulation(library,kit,true);
            if (closed == "" || open == "") { logs.Enqueue("Pedal hi-hat needs open and closed WAV recordings in the sample library."); return; }
            int version=Interlocked.Increment(ref hatLoadVersion);
            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    float[] c=WaveFile.Load(closed),o=WaveFile.Load(open);
                    if (exiting || Window.Dispatcher.HasShutdownStarted) return;
                    Window.Dispatcher.BeginInvoke(new Action(() => { if (exiting || version != hatLoadVersion) return; var a=audio; if (a != null) a.SetHatSamples(c,o); logs.Enqueue("Pedal hi-hat: V05 open/closed Kit " + kit + " ready."); }));
                } catch (Exception e) { logs.Enqueue("Pedal hi-hat: " + e.Message); }
            });
        }
    }
}
