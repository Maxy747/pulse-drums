using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Pulse {
    // Vector artwork follows the supplied top-down references; each piece is independent.
    public sealed class KitView : FrameworkElement {
        public event Action<int> Selected;
        public event Action<int> Audition;
        public readonly double[] Levels = new double[8];
        public int SelectedPart = 0;
        public int LearningPart = -1;
        public static readonly Point[] Centers = { new Point(125,465),new Point(210,155),new Point(410,195),new Point(350,370),new Point(590,195),new Point(505,373),new Point(665,387),new Point(835,350) };
        static readonly double[] Radii = {110,126,80,72,80,94,85,139};
        static readonly int[] Order = {5,2,4,3,6,1,0,7};
        public KitView() {
            Height = 370; Focusable = true; Cursor = Cursors.Hand;
            ToolTip = "Click a drum to tune it. Double-click to audition. Keys 1–8 follow your numbered kit.";
            MouseLeftButtonDown += (s,e) => { int part = HitTestPart(e.GetPosition(this)); if (part < 0) return; Focus(); if (Selected != null) Selected(part); if (e.ClickCount == 2 && Audition != null) Audition(part); e.Handled = true; };
        }
        public void Update(double[] levels, int selected, int learning) {
            bool changed = selected != SelectedPart || learning != LearningPart;
            for (int i = 0; i < 8; i++) { double level = Math.Round(levels[i] * 50) / 50; if (level != Levels[i]) changed = true; Levels[i] = level; }
            SelectedPart = selected; LearningPart = learning; if (changed) InvalidateVisual();
        }
        double Scale { get { return Math.Min(ActualWidth / 1000,ActualHeight / 620); } }
        Point Offset { get { return new Point((ActualWidth - 1000 * Scale) / 2,(ActualHeight - 620 * Scale) / 2); } }
        public int HitTestPart(Point screen) {
            double scale = Scale; if (scale <= 0) return -1;
            Point p = new Point((screen.X - Offset.X) / scale,(screen.Y - Offset.Y) / scale);
            for (int j = Order.Length - 1; j >= 0; j--) { int i = Order[j]; double dx = (p.X - Centers[i].X) / Radii[i], dy = (p.Y - Centers[i].Y) / (Radii[i] * .91); if (dx*dx + dy*dy <= 1.08) return i; }
            if (new Rect(459,464,95,90).Contains(p)) return 5;
            return -1;
        }
        static SolidColorBrush B(string value) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); b.Freeze(); return b; }
        static Color Mix(string from, string to, double amount) { Color a = (Color)ColorConverter.ConvertFromString(from), b = (Color)ColorConverter.ConvertFromString(to); return Color.FromRgb((byte)(a.R+(b.R-a.R)*amount),(byte)(a.G+(b.G-a.G)*amount),(byte)(a.B+(b.B-a.B)*amount)); }
        static Pen Pen(string color, double width) { return new Pen(B(color),width); }
        static void Label(DrawingContext dc, string text, Point center, double size, string color) {
            var t = new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,B(color),1.0);
            dc.DrawText(t,new Point(center.X - t.Width / 2,center.Y));
        }
        protected override void OnRender(DrawingContext dc) {
            base.OnRender(dc); dc.DrawRoundedRectangle(B("#090E0B"),Pen("#202F25",1),new Rect(0,0,ActualWidth,ActualHeight),12,12);
            if (Scale <= 0) return;
            dc.PushTransform(new TranslateTransform(Offset.X,Offset.Y)); dc.PushTransform(new ScaleTransform(Scale,Scale));
            // Stands are behind the playable surfaces.
            Stand(dc,new Point(195,264),new Point(139,335)); Stand(dc,new Point(139,335),new Point(322,501)); Stand(dc,new Point(322,501),new Point(354,546));
            Stand(dc,new Point(771,439),new Point(698,527)); Stand(dc,new Point(113,565),new Point(102,594));
            for (int j = 0; j < Order.Length; j++) DrawPiece(dc,Order[j]);
            Label(dc,"PLAYER POSITION",new Point(505,592),11,"#496452");
            dc.Pop(); dc.Pop();
        }
        static void Stand(DrawingContext dc, Point a, Point b) { dc.DrawLine(Pen("#183D25",9),a,b); dc.DrawLine(Pen("#285C36",2),a,b); dc.DrawEllipse(B("#102C1A"),Pen("#285C36",2),b,8,8); }
        void DrawPiece(DrawingContext dc, int i) {
            Point c = Centers[i]; double r = Radii[i], ry = r * .91, energy = Math.Min(1,Math.Max(0,Levels[i])); bool cymbal = i == 0 || i == 1 || i == 7;
            var rim = new SolidColorBrush(Mix("#27683B","#B6FFB0",energy));
            if (energy > .02) {
                for (int g = 8; g >= 1; g--) { dc.PushOpacity(energy * .025 * (9-g)); dc.DrawEllipse(null,new Pen(B("#57FF75"),g * 3),c,r + 3,ry + 3); dc.Pop(); }
            }
            if (!cymbal) {
                dc.DrawEllipse(B("#07130C"),new Pen(rim,2),new Point(c.X,c.Y+15),r,ry);
                for (int lug = 0; lug < 8; lug++) {
                    double angle = (lug + .5) * Math.PI / 4; double x = c.X + Math.Cos(angle)*r, y = c.Y + Math.Sin(angle)*ry;
                    dc.DrawRoundedRectangle(B("#163B23"),new Pen(rim,1),new Rect(x-5,y-5,10,19),2,2);
                }
            }
            var fill = new RadialGradientBrush(Mix("#124021","#176F2C",energy),Mix("#091B10","#073C18",energy)); fill.GradientOrigin = new Point(.32,.22);
            dc.DrawEllipse(fill,new Pen(rim,2.4),c,r,ry);
            dc.DrawEllipse(null,Pen("#06140B",4),c,r-5,ry-5);
            dc.DrawEllipse(null,new Pen(rim,cymbal ? .7 : 1.3),c,r-9,ry-9);
            if (cymbal) {
                for (double groove = 18; groove < r - 9; groove += 5) { dc.PushOpacity(.3 + energy * .35); dc.DrawEllipse(null,new Pen(rim,.65),c,groove,groove*.91); dc.Pop(); }
                dc.DrawEllipse(B("#0A2212"),new Pen(rim,2),c,16,14);
                dc.DrawLine(new Pen(rim,4),c,new Point(c.X+49,c.Y-45)); dc.DrawEllipse(B("#112C17"),new Pen(rim,2),c,5,5);
            }
            if (i == SelectedPart || i == LearningPart) {
                var selection = new Pen(B(i == LearningPart ? "#FFD783" : "#86B997"),1.4) { DashStyle = DashStyles.Dash };
                dc.DrawEllipse(null,selection,c,r+11,ry+11);
            }
            double labelY = cymbal ? c.Y + 29 : c.Y - 18;
            dc.DrawRoundedRectangle(B("#B007100A"),null,new Rect(c.X-59,labelY-3,118,54),7,7);
            Label(dc,(i+1).ToString(),new Point(c.X,labelY),21,energy > .1 ? "#E0FFCE" : "#A2C7AC");
            Label(dc,Kit.Names[i],new Point(c.X,labelY+27),17,"#C4D9CA");
            if (i == 5) {
                dc.DrawLine(new Pen(rim,4),new Point(505,444),new Point(505,531));
                for (int p = 0; p < 2; p++) dc.DrawRoundedRectangle(B(energy > .1 ? "#204F27" : "#112B1B"),new Pen(rim,2),new Rect(461 + p*49,467,40,70),5,5);
                dc.DrawRoundedRectangle(B("#215734"),new Pen(rim,2),new Rect(497,424,16,23),3,3);
            }
        }
    }
}
