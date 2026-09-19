using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Pulse {
    // Relative dragging means there is no jump when grabbing the dial again.
    public sealed class GainKnob : FrameworkElement {
        public event Action<double> Adjust;
        double value,lastY; bool dragging;
        public double Value { get { return value; } set { this.value=value; InvalidateVisual(); } }
        public GainKnob() {
            Width=64; Height=64; Focusable=true; Cursor=Cursors.SizeNS;
            ToolTip="Drag up/down or scroll to adjust master gain. Hold Shift for fine adjustment. Double-click resets to 0 dB. Arrow keys also work.";
            System.Windows.Automation.AutomationProperties.SetName(this,"Master gain knob");
            MouseLeftButtonDown+=(s,e)=>{ Focus(); if(e.ClickCount==2) { Change(-value); return; } dragging=true; lastY=e.GetPosition(this).Y; CaptureMouse(); e.Handled=true; };
            MouseMove+=(s,e)=>{ if(!dragging)return; double y=e.GetPosition(this).Y; Change((lastY-y)*(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?.01:.1)); lastY=y; };
            MouseLeftButtonUp+=(s,e)=>{ dragging=false; ReleaseMouseCapture(); };
            LostMouseCapture+=(s,e)=>dragging=false;
            MouseWheel+=(s,e)=>{ Change(Math.Sign(e.Delta)*(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?.01:.5)); e.Handled=true; };
            KeyDown+=(s,e)=>{ if(e.Key==Key.Up||e.Key==Key.Right||e.Key==Key.Down||e.Key==Key.Left) { Change((e.Key==Key.Up||e.Key==Key.Right?1:-1)*(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?.01:.1));e.Handled=true; } };
        }
        void Change(double delta) { if(Adjust!=null)Adjust(Math.Max(-60,Math.Min(60,value+delta))); }
        protected override void OnRender(DrawingContext dc) {
            var accent=Theme.Brush("#C5F36B");
            dc.DrawEllipse(Theme.Brush("#242923"),new Pen(Theme.Brush("#46513A"),2),new Point(32,32),28,28);
            double angle=(value+60)/120*270-225,rad=angle*Math.PI/180;
            dc.DrawLine(new Pen(accent,4),new Point(32+Math.Cos(rad)*14,32+Math.Sin(rad)*14),new Point(32+Math.Cos(rad)*23,32+Math.Sin(rad)*23));
            if(IsKeyboardFocused)dc.DrawEllipse(null,new Pen(accent,1),new Point(32,32),31,31);
        }
    }
}
