using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Pulse {
    public sealed partial class Controller {
        KitView kitView;
        volatile bool learning;
        int learnPart;
        LearnSession learn;
        int[] learnOriginal;
        SampleChoice[] library = new SampleChoice[0];
        readonly int[] sampleVersions = new int[8];
        readonly string[] sampleStates = Enumerable.Repeat("Synth fallback",8).ToArray();
        bool sampleUpdating;
        Pad SelectedPad { get { return settings.Pads[settings.Inputs[selected]]; } }
        void BuildKitControls() {
            var themes = Get<ComboBox>("ThemeCombo");
            themes.ItemsSource = new[] { "Green", "Red", "Blue" }; themes.SelectedItem = settings.ThemeName;
            themes.SelectionChanged += delegate {
                settings.ThemeName = (string)themes.SelectedItem; Theme.Apply(settings.ThemeName);
                if (kitView != null) kitView.InvalidateVisual(); Changed(false);
            };
            Get<ComboBox>("TransposeCombo").ItemsSource = Enumerable.Range(-48,97).ToArray(); Get<ComboBox>("TransposeCombo").SelectedItem = settings.Transpose;
            Get<ComboBox>("TransposeCombo").SelectionChanged += delegate { if (updating) return; settings.Transpose = (int)Get<ComboBox>("TransposeCombo").SelectedItem; midi.Panic(); Changed(false); };
            Get<Slider>("LengthSlider").Value = settings.NoteOffMs; Get<TextBlock>("LengthValue").Text = settings.NoteOffMs.ToString();
            Get<Slider>("LengthSlider").ValueChanged += delegate { if (updating) return; settings.NoteOffMs = (int)Get<Slider>("LengthSlider").Value; Get<TextBlock>("LengthValue").Text = settings.NoteOffMs.ToString(); Changed(false); };
            kitView = new KitView(); Get<ContentControl>("KitHost").Content = kitView;
            kitView.Selected += SelectPad; kitView.Audition += i => Hit(i,100,true);
            Get<Button>("KitViewButton").Click += delegate { SetView(false); };
            Get<Button>("ClassicViewButton").Click += delegate { SetView(true); };
            Get<Button>("SetupAllButton").Click += delegate { BeginLearn(true); };
            Get<Button>("AssignOneButton").Click += delegate { BeginLearn(false); };
            Get<Button>("SetupCancel").Click += delegate { EndLearn(learn != null && learn.Complete); };
            Get<Button>("SetupUndo").Click += delegate {
                if (learn == null || !learn.CanUndo) return;
                if (learn.Complete) { settings.Inputs = (int[])learnOriginal.Clone(); Changed(false); }
                learn.Undo(clock.ElapsedMilliseconds); learning = true;
                Frame discarded; while (frames.TryDequeue(out discarded)) { }
                midi.Panic(); if (audio != null) audio.Panic(); UpdateLearn();
            };
            var route = Get<ComboBox>("RouteCombo"); route.Items.Add("Samples · play through speakers"); route.Items.Add("Ableton / MIDI only"); route.Items.Add("Samples + MIDI");
            route.SelectedIndex = settings.MidiEnabled && settings.Sound ? 2 : settings.Sound ? 0 : 1;
            route.SelectionChanged += delegate { if (updating) return; settings.Sound = route.SelectedIndex != 1; settings.MidiEnabled = route.SelectedIndex != 0; Get<CheckBox>("SoundToggle").IsChecked = settings.Sound; midi.Panic(); if (audio != null) audio.Panic(); lastMidiScan = -10000; Changed(false); };
            Get<ComboBox>("KitPresetCombo").ItemsSource = new[] {"GSCW Kit 1", "GSCW Kit 2", "Pulse synth"}; Get<ComboBox>("KitPresetCombo").SelectedIndex = 1;
            Get<Button>("ApplyKitButton").Click += delegate { ApplySoundKit(Get<ComboBox>("KitPresetCombo").SelectedIndex); };
            Get<ComboBox>("SampleCombo").SelectionChanged += delegate { if (sampleUpdating) return; var choice = Get<ComboBox>("SampleCombo").SelectedItem as SampleChoice; if (choice != null) LoadSample(selected,choice.Path,true); };
            Get<Button>("BrowseSampleButton").Click += delegate { var dialog = new OpenFileDialog { Title = "Choose a sound for " + Kit.Names[selected], Filter = "WAV samples|*.wav" }; if (dialog.ShowDialog(Window) == true) LoadSample(selected,dialog.FileName,true); };
            Get<Button>("LibraryFolderButton").Click += delegate {
                using (var dialog = new Forms.FolderBrowserDialog { Description = "Choose the downloaded drum-samples folder", SelectedPath = settings.SampleFolder }) if (dialog.ShowDialog() == Forms.DialogResult.OK) { settings.SampleFolder = dialog.SelectedPath; ScanLibrary(); Changed(false); }
            };
            Get<Button>("SavePresetButton").Click += delegate {
                string folder = Path.Combine(SettingsStore.Folder,"Presets"); Directory.CreateDirectory(folder);
                var dialog = new SaveFileDialog { Title = "Save your drum preset", Filter = "Pulse preset|*.pulse.xml", FileName = "My kit.pulse.xml", InitialDirectory = folder };
                if (dialog.ShowDialog(Window) == true) try { SettingsStore.Save(settings,dialog.FileName); Get<TextBlock>("SaveStatus").Text = "Preset saved · " + Path.GetFileName(dialog.FileName); } catch (Exception e) { Report("Could not save preset",e.Message); }
            };
            Get<Button>("LoadPresetButton").Click += delegate {
                var dialog = new OpenFileDialog { Title = "Load a Pulse preset", Filter = "Pulse preset|*.pulse.xml;*.xml", InitialDirectory = Path.Combine(SettingsStore.Folder,"Presets") };
                if (dialog.ShowDialog(Window) == true) try { string warning; var loaded = SettingsStore.Load(dialog.FileName,out warning); if (warning != "") throw new InvalidDataException(warning); ApplyPreset(loaded); } catch (Exception e) { Report("Could not load preset",e.Message); }
            };
            Get<Button>("DefaultsButton").Click += delegate { EndLearn(false); settings.Pads = new Settings().Pads; settings.InstrumentNotes = (int[])Kit.Notes.Clone(); settings.Channel = 1; settings.Transpose = 0; settings.NoteOffMs = 10; Changed(true); SelectPad(selected); Get<ComboBox>("ChannelCombo").SelectedItem = 1; Get<ComboBox>("TransposeCombo").SelectedItem = 0; Get<Slider>("LengthSlider").Value = 10; Get<TextBlock>("LastHit").Text = "Your calibrated trigger and velocity defaults restored. Input assignments and sounds kept."; };
            SetView(settings.ClassicView);
        }
        void SetView(bool classic) {
            settings.ClassicView = classic;
            Get<ContentControl>("KitHost").Visibility = classic ? Visibility.Collapsed : Visibility.Visible;
            Get<System.Windows.Controls.Primitives.UniformGrid>("PadsGrid").Visibility = classic ? Visibility.Visible : Visibility.Collapsed;
            Get<Button>("KitViewButton").Background = Brush(classic ? "#101211" : "#31452B");
            Get<Button>("ClassicViewButton").Background = Brush(classic ? "#31452B" : "#101211");
            Changed(false);
        }
        void ReceiveInput(Frame f) {
            if (learning) { if (f.Kind == "RAW") Enqueue(new Frame("LEARN",f.Index,f.Value)); return; }
            int part = live.PartForInput(f.Index); if (part < 0) return;
            if (f.Kind == "HIT") Hit(part,f.Value,false); else if (f.Kind == "RAW") Enqueue(new Frame("RAW",part,f.Value));
        }
        void BeginLearn(bool all) {
            learnOriginal = (int[])settings.Inputs.Clone();
            learn = new LearnSession(settings.Inputs,selected,all,clock.ElapsedMilliseconds); learnPart = learn.Part; learning = true;
            midi.Panic(); if (audio != null) audio.Panic(); Frame f; while (frames.TryDequeue(out f)) { }
            SetView(false); SelectPad(learnPart); Get<Border>("SetupPanel").Visibility = Visibility.Visible; UpdateLearn();
            Get<Border>("SetupPanel").BringIntoView();
        }
        void CaptureLearn(int input, int raw) { if (learn != null && learning) learn.Feed(input,raw,clock.ElapsedMilliseconds); }
        void UpdateLearn() {
            if (!learning || learn == null) return;
            learn.Tick(clock.ElapsedMilliseconds);
            if (learn.Complete) {
                settings.Inputs = (int[])learn.Map.Clone(); learning = false; Changed(false); SelectPad(selected);
                Get<TextBlock>("SetupTitle").Text = "Setup complete";
                Get<TextBlock>("SetupHint").Text = "Assignments saved. Play your kit, or undo the last part to record it again.";
                Get<Button>("SetupUndo").IsEnabled = learn.CanUndo; Get<Button>("SetupCancel").Content = "Close setup";
                if (kitView != null) kitView.Update(meters,selected,-1); return;
            }
            Get<Button>("SetupUndo").IsEnabled = learn.CanUndo; Get<Button>("SetupCancel").Content = "Cancel setup";
            if (learnPart != learn.Part) { learnPart = learn.Part; SelectPad(learnPart); }
            Get<TextBlock>("SetupTitle").Text = (learn.All ? "Step " + (learn.Part+1) + " of 8 · " : "Assign · ") + Kit.Names[learn.Part];
            Get<TextBlock>("SetupHint").Text = learn.Confirmations == 0 ? (learn.Hint == "" ? "Strike " + Kit.Names[learn.Part] + " twice. Waiting for your first strike… No buttons needed." : learn.Hint) : "1 of 2 · A" + learn.Candidate + " heard. Strike " + Kit.Names[learn.Part] + " once more to confirm.";
            if (kitView != null) kitView.Update(meters,selected,learnPart);
        }
        void EndLearn(bool saved) {
            learning = false; learn = null; Get<Border>("SetupPanel").Visibility = Visibility.Collapsed;
            Frame f; while (frames.TryDequeue(out f)) { }
            if (kitView != null) kitView.Update(meters,selected,-1);
            Get<TextBlock>("LastHit").Text = saved ? "Input assignments saved. Play your kit to check the mapping." : "Setup closed. Previous assignments kept.";
        }
        void ScanLibrary() {
            try { library = SampleLibrary.Scan(settings.SampleFolder); Get<TextBlock>("AudioStatus").Text = library.Length + " WAVs · stereo playback"; logs.Enqueue("Sample library: " + library.Length + " WAVs found."); RefreshSampleChoices(); }
            catch (Exception e) { logs.Enqueue("Sample library: " + e.Message); }
        }
        void StartSampleLibrary() {
            ScanLibrary();
            if (!settings.SampleDefaultsApplied && library.Length > 0) { settings.SampleFiles = SampleLibrary.Preset(library,2); settings.SampleDefaultsApplied = true; settings.Sound = true; settings.MidiEnabled = false; Get<ComboBox>("RouteCombo").SelectedIndex = 0; Changed(false); }
            FixSampleTypes();
            for (int i = 0; i < 8; i++) LoadSample(i,settings.SampleFiles[i],false);
            RefreshSampleChoices();
        }
        void RefreshSampleChoices() {
            var cb = Get<ComboBox>("SampleCombo"); if (cb == null) return;
            sampleUpdating = true; cb.Items.Clear(); cb.Items.Add(new SampleChoice {Path = "",Label = "Pulse synth"});
            foreach (var c in library.Where(c => c.Part == selected)) cb.Items.Add(c);
            string path = settings.SampleFiles[selected];
            var match = cb.Items.Cast<SampleChoice>().FirstOrDefault(c => c.Path == path);
            if (match == null) { match = new SampleChoice { Path = path, Label = Path.GetFileNameWithoutExtension(path) }; cb.Items.Add(match); }
            cb.SelectedItem = match; cb.ToolTip = path == "" ? "Built-in synthesized percussion" : path;
            Get<TextBlock>("SampleStatus").Text = sampleStates[selected]; sampleUpdating = false;
        }
        void LoadSample(int part, string path, bool saveSelection) {
            if (!SampleLibrary.Matches(part,path,settings.SampleFolder)) { Report("Choose a matching sound", "This sample belongs to a different instrument. Choose a " + Kit.Names[part] + " sound instead."); return; }
            if (smoke) { if (saveSelection) settings.SampleFiles[part] = path; return; }
            int version = Interlocked.Increment(ref sampleVersions[part]); sampleStates[part] = "Loading sample…";
            if (part == selected) Get<TextBlock>("SampleStatus").Text = sampleStates[part];
            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    var sample = path == "" ? WaveFile.Stereo(AudioEngine.Synthesize(Kit.DefaultInputs[part])) : WaveFile.Load(path);
                    if (exiting || Window.Dispatcher.HasShutdownStarted) return;
                    Window.Dispatcher.BeginInvoke(new Action(() => {
                        if (exiting || sampleVersions[part] != version) return;
                        if (audio != null) audio.SetSample(part,sample);
                        sampleStates[part] = path == "" ? "Pulse synth · ready" : "WAV ready · " + (sample.Length / 2.0 / AudioEngine.Rate).ToString("0.0") + " s · stereo";
                        logs.Enqueue(Kit.Names[part] + " sound ready: " + (path == "" ? "Pulse synth" : Path.GetFileName(path)));
                        if (saveSelection) { settings.SampleFiles[part] = path; settings.SampleDefaultsApplied = true; Changed(false); }
                        RefreshSampleChoices();
                    }));
                } catch (Exception e) { if (exiting || Window.Dispatcher.HasShutdownStarted) return; Window.Dispatcher.BeginInvoke(new Action(() => { if (exiting || sampleVersions[part] != version) return; sampleStates[part] = "Could not load WAV · previous sound kept"; logs.Enqueue(Kit.Names[part] + ": " + e.Message); RefreshSampleChoices(); })); }
            });
        }
        void ApplySoundKit(int choice) {
            if (choice < 2 && library.Length == 0) { Report("No sample library", "Choose the downloaded drum-samples folder first."); return; }
            string[] files = choice == 2 ? Enumerable.Repeat("",8).ToArray() : SampleLibrary.Preset(library,choice+1);
            for (int i = 0; i < 8; i++) LoadSample(i,files[i],true);
            Get<ComboBox>("RouteCombo").SelectedIndex = 0;
        }
        void ApplyPreset(Settings preset) {
            EndLearn(false); preset.Normalize();
            settings.Pads = preset.Pads; settings.Inputs = preset.Inputs; settings.InstrumentNotes = preset.InstrumentNotes; settings.SampleFiles = preset.SampleFiles;
            settings.Channel = preset.Channel; settings.Volume = preset.Volume; settings.Sound = preset.Sound; settings.MidiEnabled = preset.MidiEnabled;
            settings.Transpose = preset.Transpose; settings.NoteOffMs = preset.NoteOffMs;
            FixSampleTypes();
            // Keep this PC's device identity, sample library root and chosen MIDI port.
            for (int i = 0; i < 8; i++) {
                string path = settings.SampleFiles[i];
                if (path != "" && !File.Exists(path)) { var matches = library.Where(c => Path.GetFileName(c.Path) == Path.GetFileName(path)).ToArray(); if (matches.Length == 1) settings.SampleFiles[i] = matches[0].Path; }
                LoadSample(i,settings.SampleFiles[i],false);
            }
            settings.SampleDefaultsApplied = true; updating = true;
            Get<ComboBox>("ChannelCombo").SelectedItem = settings.Channel; Get<Slider>("VolumeSlider").Value = settings.Volume * 100;
            Get<ComboBox>("TransposeCombo").SelectedItem = settings.Transpose; Get<Slider>("LengthSlider").Value = settings.NoteOffMs; Get<TextBlock>("LengthValue").Text = settings.NoteOffMs.ToString();
            Get<ComboBox>("RouteCombo").SelectedIndex = settings.Sound ? settings.MidiEnabled ? 2 : 0 : 1;
            Get<CheckBox>("SoundToggle").IsChecked = settings.Sound; updating = false;
            midi.Panic(); if (audio != null) audio.Panic(); lastMidiScan = -10000; Changed(true); SelectPad(selected);
        }
        void FixSampleTypes() {
            string[] defaults = SampleLibrary.Preset(library,2); bool changed = false;
            for (int i = 0; i < 8; i++) if (!SampleLibrary.Matches(i,settings.SampleFiles[i],settings.SampleFolder)) {
                settings.SampleFiles[i] = defaults[i]; changed = true; logs.Enqueue("Replaced mismatched " + Kit.Names[i] + " sample with its matching default.");
            }
            if (changed) Changed(false);
        }
        void Report(string title, string message) { logs.Enqueue(title + ": " + message); if (!smoke) MessageBox.Show(Window,message,title,MessageBoxButton.OK,MessageBoxImage.Information); }
    }
}
