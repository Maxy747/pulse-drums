using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Data;
using System.Windows.Media;

namespace Pulse {
    // Original green palette is the source of truth for all three themes.
    public static class Theme {
        public sealed class Swatch : INotifyPropertyChanged {
            public string Original;
            public Color Value { get { return Theme.Color(Original); } }
            public SolidColorBrush Brush;
            public event PropertyChangedEventHandler PropertyChanged;
            public void Refresh() { if (PropertyChanged != null) PropertyChanged(this,new PropertyChangedEventArgs("Value")); }
        }
        static readonly Dictionary<string, Swatch> brushes = new Dictionary<string, Swatch>();
        public static string Name = "Green";
        public static Color Color(string value) {
            var c = (Color)ColorConverter.ConvertFromString(value);
            if (Name == "Green" || c.G <= c.R || c.G <= c.B) return c;
            byte low = Math.Min(c.R, c.B), high = c.G;
            // Keep luminance range and alpha, including subtle tinted backgrounds.
            return Name == "Red" ? System.Windows.Media.Color.FromArgb(c.A, high, low, (byte)(low + (high-low)*.08))
                : System.Windows.Media.Color.FromArgb(c.A, low, (byte)(low + (high-low)*.55), high);
        }
        public static SolidColorBrush Brush(string value) {
            Swatch swatch;
            if (!brushes.TryGetValue(value, out swatch)) {
                swatch = new Swatch { Original = value, Brush = new SolidColorBrush() };
                BindingOperations.SetBinding(swatch.Brush,SolidColorBrush.ColorProperty,new Binding("Value") { Source = swatch });
                brushes.Add(value, swatch);
            }
            return swatch.Brush;
        }
        public static Window Load(string xaml, string name) {
            Name = name;
            xaml = Regex.Replace(xaml, "(?<property>\\w+)=\"(?<color>#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?)\"", m => {
                if (m.Groups["property"].Value == "Color") return m.Value;
                string color = m.Groups["color"].Value; Brush(color);
                return m.Groups["property"].Value + "=\"{DynamicResource Palette" + color.Substring(1) + "}\"";
            });
            var window = (Window)XamlReader.Parse(xaml);
            foreach (var item in brushes) window.Resources["Palette" + item.Key.Substring(1)] = item.Value.Brush;
            return window;
        }
        public static void Apply(string name) {
            Name = name;
            foreach (var item in brushes) item.Value.Refresh();
        }
    }
}
