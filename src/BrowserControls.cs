using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Pulse {
    public sealed partial class Controller {
        static readonly string[][] BrowserGroups={
            new[]{"Pad", "SampleCombo","MuteToggle","ThresholdSlider","ResetSlider","GainSlider","FloorSlider","CeilingSlider","CurveSlider","GuardSlider","NoteCombo","AssignOneButton","TestPad","ResetPad","ResetTomsButton"},
            new[]{"Output","AudioDeviceCombo","RouteCombo","VolumeSlider","OutputGainSlider","ReverbToggle","ReverbSlider","PlayerStereoToggle","StereoWidthSlider","AudioRetryButton","AsioPanelButton","PanicButton"},
            new[]{"MIDI","MidiCombo","ChannelCombo","TransposeCombo","LengthSlider"},
            new[]{"Trigger protection & setup","HighSensitivity","MediumSensitivity","LowSensitivity","DefaultsButton","CrosstalkSlider","SetupAllButton","SetupUndo","SetupCancel"},
            new[]{"Pedals","PedalsToggle","PedalPortCombo","PedalSwapToggle","PedalKickOnlyToggle","MainKickOnlyToggle","PedalCloseToggle","PedalCloseSlider","KickRestButton","KickDownButton","HatRestButton","HatDownButton"},
            new[]{"Sounds & presets","KitPresetCombo","ApplyKitButton","LibraryFolderButton","BrowseSampleButton"},
            new[]{"App & devices","PortCombo","StartupToggle","UsbLaunchToggle","TrayToggle","GreenThemeButton","RedThemeButton","BlueThemeButton","KitViewButton","ClassicViewButton"}
        };
        static string Label(Control control) {
            string label=AutomationProperties.GetName(control);
            var content=control as ContentControl;
            if(String.IsNullOrEmpty(label) && content!=null && content.Content is string) label=(string)content.Content;
            if(String.IsNullOrEmpty(label)) label=Regex.Replace(Regex.Replace(control.Name,"(Slider|Combo|Toggle|Button)$",""),"([a-z])([A-Z])","$1 $2");
            return label;
        }
        string BrowserControl(string form) {
            try { return (string)Window.Dispatcher.Invoke(DispatcherPriority.Normal,TimeSpan.FromSeconds(3),new Func<string>(() => BrowserControlUi(form))); }
            catch(Exception e) { return "{\"error\":"+LiveViewState.Quote(e.Message)+"}"; }
        }
        string BrowserControlUi(string form) {
            var serializer=new JavaScriptSerializer();
            var args=new Dictionary<string,string>();
            foreach(string field in form.Split('&')) { var pair=field.Split(new[]{'='},2); if(pair.Length==2)args[WebUtility.UrlDecode(pair[0])]=WebUtility.UrlDecode(pair[1]); }
            string action; args.TryGetValue("action",out action);
            int part=selected; string partText;
            if(args.TryGetValue("part",out partText) && (!Int32.TryParse(partText,out part) || part<0 || part>7)) throw new InvalidDataException("Invalid drum");
            if(action=="hit") {
                int velocity; if(!args.ContainsKey("value") || !Int32.TryParse(args["value"],out velocity) || velocity<1 || velocity>127) throw new InvalidDataException("Invalid velocity");
                if(!learning) Hit(part,velocity,true); return "{\"ok\":true}";
            }
            if(action=="select") SelectPad(part);
            if(action=="set") {
                string id=args["id"],value=args["value"];
                if(!BrowserGroups.Any(g=>g.Skip(1).Contains(id))) throw new InvalidDataException("Unknown control");
                if(BrowserGroups[0].Contains(id) || id=="BrowseSampleButton") SelectPad(part);
                var control=Get<Control>(id); if(control==null || !control.IsEnabled) throw new InvalidDataException("Control is unavailable");
                var slider=control as Slider; var combo=control as ComboBox; var check=control as CheckBox; var button=control as Button;
                if(slider!=null) { double number; if(!Double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out number) || Double.IsNaN(number) || Double.IsInfinity(number) || number<slider.Minimum || number>slider.Maximum) throw new InvalidDataException("Value outside allowed range"); slider.Value=number; }
                else if(combo!=null) { int choice; if(!Int32.TryParse(value,out choice) || choice<0 || choice>=combo.Items.Count) throw new InvalidDataException("Invalid selection"); combo.SelectedIndex=choice; }
                else if(check!=null) { if(value!="true" && value!="false") throw new InvalidDataException("Invalid toggle"); check.IsChecked=value=="true"; check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
                else if(button!=null) {
                    // OS dialogs remain on the PC and must not block the network request.
                    if(id=="AsioPanelButton" || id=="LibraryFolderButton" || id=="BrowseSampleButton") Window.Dispatcher.BeginInvoke(new Action(()=>button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent))));
                    else button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                }
            }
            string presetFolder=Path.Combine(SettingsStore.Folder,"Presets");
            if(action=="savePreset" || action=="loadPreset") {
                string name=args.ContainsKey("value")?args["value"]:"";
                if(name.Length<1 || name.Length>64 || name.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || name=="." || name=="..") throw new InvalidDataException("Use a simple preset name, up to 64 characters");
                string path=Path.Combine(presetFolder,name+".pulse.xml");
                if(action=="savePreset") { Directory.CreateDirectory(presetFolder); SettingsStore.Save(settings,path); }
                else { if(!File.Exists(path))throw new InvalidDataException("Preset not found"); string warning; var loaded=SettingsStore.Load(path,out warning); if(warning!="")throw new InvalidDataException(warning); ApplyPreset(loaded); }
            }
            var groups=new List<object>();
            foreach(var group in BrowserGroups) {
                var controls=new List<object>();
                foreach(string id in group.Skip(1)) {
                    var c=Get<Control>(id); if(c==null)continue;
                    var data=new Dictionary<string,object>{{"id",id},{"label",Label(c)},{"enabled",c.IsEnabled},{"hint",c.ToolTip as string ?? ""}};
                    var s=c as Slider; var cb=c as ComboBox; var ch=c as CheckBox;
                    if(s!=null) { data["type"]="range";data["value"]=s.Value;data["min"]=s.Minimum;data["max"]=s.Maximum;data["step"]=s.TickFrequency>0?s.TickFrequency:1; }
                    else if(cb!=null) { data["type"]="select";data["value"]=cb.SelectedIndex;data["options"]=cb.Items.Cast<object>().Select(Convert.ToString).ToArray(); }
                    else if(ch!=null) { data["type"]="checkbox";data["value"]=ch.IsChecked==true; }
                    else data["type"]="button";
                    controls.Add(data);
                }
                groups.Add(new {label=group[0],controls=controls});
            }
            return serializer.Serialize(new {part=selected,name=Kit.Names[selected],input=Get<TextBlock>("SelectedInput").Text,groups=groups,presets=Directory.Exists(presetFolder)?Directory.GetFiles(presetFolder,"*.pulse.xml").Select(p=>Path.GetFileName(p).Replace(".pulse.xml","")).ToArray():new string[0],setup=Get<TextBlock>("SetupTitle").Text+" · "+Get<TextBlock>("SetupHint").Text,pedals=Get<TextBlock>("PedalStatus").Text+" · "+Get<TextBlock>("KickPedalValue").Text+" · "+Get<TextBlock>("HatPedalValue").Text,calibration=Get<TextBlock>("PedalCalibrationHint").Text,audio=Get<TextBlock>("AudioStatus").Text});
        }
    }
}
