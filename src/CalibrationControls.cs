using System;
using System.Windows;
using System.Windows.Controls;

namespace Pulse {
    public sealed partial class Controller {
        volatile bool calibrating;
        ThresholdLearning calibration;
        bool calibrationRestored;
        void BuildCalibrationControls() {
            Get<Button>("CalibrateAllButton").Click+=delegate { StartCalibration(true); };
            Get<Button>("CalibratePadButton").Click+=delegate { StartCalibration(false); };
            Get<Button>("CalibrationCancel").Click+=delegate { StopCalibration(false); };
            Get<Button>("CalibrationApply").Click+=delegate { StopCalibration(true); };
        }
        void StartCalibration(bool all) {
            if(mobileEnabled){Get<TextBlock>("LastHit").Text="Threshold learning requires the drum Nano connected directly to this PC.";return;}
            if(!ready&&!smoke){Get<TextBlock>("LastHit").Text="Connect the main Arduino and hit a pad before calibrating.";return;}
            if(calibrating)StopCalibration(false); EndLearn(false);
            calibration=new ThresholdLearning(settings,selected,all,clock.ElapsedMilliseconds);calibrating=true;calibrationRestored=false;
            triggerFilter.Clear();midi.Panic();if(audio!=null)audio.Panic();
            Frame discarded;while(frames.TryDequeue(out discarded)){}
            var probe=settings.Copy();foreach(var p in probe.Pads){p.Hit=2;p.Reset=1;}
            if(connection!=null)connection.Configure(probe,true);
            Get<Border>("CalibrationPanel").Visibility=Visibility.Visible;Get<Button>("CalibrationApply").IsEnabled=false;
            UpdateCalibration();Get<Border>("CalibrationPanel").BringIntoView();
        }
        void UpdateCalibration() {
            if(!calibrating||calibration==null)return;
            if(!ready&&!smoke){StopCalibration(false);Get<TextBlock>("LastHit").Text="Calibration canceled: Arduino disconnected. Original thresholds restored on reconnect.";return;}
            long now=clock.ElapsedMilliseconds;calibration.Tick(now);
            if(calibration.Complete) {
                if(!calibrationRestored){if(connection!=null)connection.Configure(settings,true);calibrationRestored=true;}
                Get<TextBlock>("CalibrationTitle").Text="Threshold suggestions ready";
                Get<TextBlock>("CalibrationHint").Text=calibration.Summary()+"\n"+calibration.Warning+"Review, then apply. Cancel keeps your previous thresholds.";
                Get<Button>("CalibrationApply").IsEnabled=true;
            } else {
                Get<TextBlock>("CalibrationTitle").Text=calibration.Quiet(now)?"Listen for idle noise · keep the kit still":"Play "+Kit.Names[calibration.Part]+" · "+calibration.Count+" / 6";
                Get<TextBlock>("CalibrationHint").Text=calibration.Quiet(now)?"Six-second quiet phase. Playback is paused. Temporary low trigger thresholds help expose sensor noise.":"Play six separate hits, soft to firm, about one second apart. Advances automatically. Waiting for your hits…";
            }
        }
        void StopCalibration(bool apply) {
            if(!calibrating)return;
            if(apply && (calibration==null||!calibration.Complete))return;
            if(apply)for(int i=0;i<8;i++){settings.Pads[i].Hit=calibration.Hit[i];settings.Pads[i].Reset=calibration.Reset[i];}
            calibrating=false;calibration=null;triggerFilter.Clear();Frame f;while(frames.TryDequeue(out f)){}
            if(apply){Changed(true);SelectPad(selected);}else if(connection!=null)connection.Configure(settings,true);
            Get<Border>("CalibrationPanel").Visibility=Visibility.Collapsed;
            Get<TextBlock>("LastHit").Text=apply?"Learned thresholds applied and saved.":"Calibration canceled. Previous thresholds kept.";
        }
    }
}
