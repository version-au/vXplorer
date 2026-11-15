using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>
    /// Copying / moving items whose names are already in the destination: Replace, Skip or Keep both - for all of them at once, or for
    /// each one (with both sides' size and date, and a hint which is newer). A folder that's replaced is merged into the one there, its
    /// files in both replaced. Enter = Carry on, Esc = Cancel.
    /// </summary>
    public sealed class ConflictWindow : Window
    {
        public sealed class Answer
        {
            public readonly HashSet<string> Skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> KeepBoth = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        const string Replace = "Replace", SkipIt = "Skip", Both = "Keep both";

        readonly Dictionary<string, string> _choice = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly List<Action> _repaint = new List<Action>();
        readonly List<RadioButton> _all = new List<RadioButton>();
        bool _ok;

        /// <summary>null = cancelled.</summary>
        public static Answer Ask(MainWindow owner, IList<string> clashes, string dest, bool move)
        {
            // tests: VXPLORER_TEST_CONFLICT=replace|skip|both answers for all without showing it
            string test = TestHooks.On ? Environment.GetEnvironmentVariable("VXPLORER_TEST_CONFLICT") : null;
            if (!string.IsNullOrEmpty(test))
            {
                var t = new Answer();
                foreach (string c in clashes)
                {
                    if (test == "skip") t.Skip.Add(c);
                    else if (test == "both") t.KeepBoth.Add(c);
                }
                Log.Write("Test answered '" + test + "' for " + clashes.Count + " items already there");
                return test == "cancel" ? null : t;
            }
            var w = new ConflictWindow(owner, clashes, dest, move);
            w.ShowDialog();
            if (!w._ok) return null;
            var a = new Answer();
            foreach (KeyValuePair<string, string> c in w._choice)
            {
                if (c.Value == SkipIt) a.Skip.Add(c.Key);
                else if (c.Value == Both) a.KeepBoth.Add(c.Key);
            }
            return a;
        }

        static bool Identical(string a, string b)
        {
            try
            {
                if (!File.Exists(a) || !File.Exists(b)) return false;
                var fa = new FileInfo(a);
                var fb = new FileInfo(b);
                return fa.Length == fb.Length && Math.Abs((fa.LastWriteTimeUtc - fb.LastWriteTimeUtc).TotalSeconds) < 2.1;
            }
            catch { return false; }
        }

        ConflictWindow(MainWindow owner, IList<string> clashes, string dest, bool move)
        {
            Owner = owner;
            string destName = Native.DisplayName(dest);
            Title = clashes.Count == 1 ? "Already in " + destName : clashes.Count + " items already in " + destName;
            Width = 620;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12.5;
            UseLayoutRounding = true;
            Background = Brushes.Transparent;
            SetResourceReference(ForegroundProperty, "vx.Fg");
            bool blur = owner != null && owner.Blurred;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 30, ResizeBorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false
            });
            SourceInitialized += (o, e) =>
            {
                if (owner != null && owner.CurrentTheme != null) WindowEffects.Look(this, owner.CurrentTheme, blur);
            };

            // what's there and what's coming in: identical files default to Skip, the rest to Replace (as Explorer offers)
            foreach (string c in clashes)
                _choice[c] = Identical(c, Path.Combine(dest, Path.GetFileName(c.TrimEnd('\\')))) ? SkipIt : Replace;

            var root = new Border();
            root.SetResourceReference(Border.BackgroundProperty, "vx.Bg");
            var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 0) };
            var outer = new DockPanel();
            root.Child = outer;

            stack.Children.Add(new TextBlock
            {
                Text = clashes.Count == 1 ? "“" + Path.GetFileName(clashes[0].TrimEnd('\\')) + "” is already in " + destName
                                          : clashes.Count + " items are already in " + destName,
                FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap
            });
            stack.Children.Add(Dim((move ? "Moving" : "Copying") + " into " + dest + ". Choose what happens to " + (clashes.Count == 1 ? "it." : "each one."), 13, new Thickness(0, 4, 0, 0)));

            // for all of them
            if (clashes.Count > 1)
            {
                var allRow = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
                var allCard = Card(allRow, new Thickness(14, 10, 14, 10));
                FrameworkElement seg = Segmented(null, new[] { Replace, SkipIt, Both }, null, v =>
                {
                    foreach (string c in clashes) _choice[c] = v;
                    foreach (Action a in _repaint) a();
                });
                DockPanel.SetDock(seg, Dock.Right);
                allRow.Children.Add(seg);
                var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                label.Children.Add(new TextBlock { Text = "For all of them", FontSize = 13.5 });
                label.Children.Add(Dim("Or choose for each one below", 12, new Thickness(0, 2, 0, 0)));
                allRow.Children.Add(label);
                stack.Children.Add(allCard);
            }

            // each one
            var list = new StackPanel();
            for (int i = 0; i < clashes.Count; i++)
            {
                string src = clashes[i];
                string there = Path.Combine(dest, Path.GetFileName(src.TrimEnd('\\')));
                bool folder = Directory.Exists(src);
                var row = new DockPanel();
                FrameworkElement seg = Segmented(src, new[] { Replace, SkipIt, Both }, new[] { Replace, SkipIt, "Both" }, v => _choice[src] = v);
                DockPanel.SetDock(seg, Dock.Right);
                row.Children.Add(seg);
                var g = new TextBlock { Text = folder ? "" : "", FontSize = 16, Width = 28, VerticalAlignment = VerticalAlignment.Center };
                g.SetResourceReference(TextBlock.FontFamilyProperty, "vx.Icons");
                g.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
                row.Children.Add(g);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                text.Children.Add(new TextBlock { Text = Path.GetFileName(src.TrimEnd('\\')), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = src });
                text.Children.Add(Compare(src, there, folder));
                row.Children.Add(text);
                var wrap = new Border { Child = row, Padding = new Thickness(12, 9, 12, 9) };
                if (i > 0) { wrap.BorderThickness = new Thickness(0, 1, 0, 0); wrap.SetResourceReference(Border.BorderBrushProperty, "vx.Line"); }
                list.Children.Add(wrap);
            }
            var scroll = new ScrollViewer { Content = list, MaxHeight = 330, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var listCard = Card(scroll, new Thickness(0));
            listCard.Margin = new Thickness(0, clashes.Count > 1 ? 10 : 16, 0, 0);
            stack.Children.Add(listCard);

            var note = new DockPanel { Margin = new Thickness(2, 12, 0, 0) };
            var ni = new TextBlock { Text = "", FontSize = 12, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Top };
            ni.SetResourceReference(TextBlock.FontFamilyProperty, "vx.Icons");
            ni.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
            note.Children.Add(ni);
            note.Children.Add(Dim("Keep both gives the one coming in a new name, like “photo - Copy.jpg”. Replacing a folder adds to the one " +
                                  "there, and its files that are in both are replaced.", 12, new Thickness(0)));
            stack.Children.Add(note);

            // the buttons, along the bottom
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 16, 24, 20) };
            var cancel = new Button { Content = "Cancel the " + (move ? "move" : "copy"), MinWidth = 90 };
            cancel.SetResourceReference(StyleProperty, "vx.TextButton");
            cancel.Click += (o, e) => Close();
            var go = new Button { Content = "Carry on", MinWidth = 100, Margin = new Thickness(0), FontWeight = FontWeights.SemiBold };
            go.SetResourceReference(StyleProperty, "vx.TextButton");
            go.SetResourceReference(BackgroundProperty, "vx.Accent");
            go.SetResourceReference(ForegroundProperty, "vx.OnAccent");
            go.Click += (o, e) => { _ok = true; Close(); };
            buttons.Children.Add(cancel);
            buttons.Children.Add(go);
            DockPanel.SetDock(buttons, Dock.Bottom);
            outer.Children.Add(buttons);
            outer.Children.Add(stack);
            Content = root;

            PreviewKeyDown += (o, e) =>
            {
                if (e.Key == Key.Escape) { e.Handled = true; Close(); }
                else if (e.Key == Key.Enter) { e.Handled = true; _ok = true; Close(); }
            };
        }

        /// <summary>"Coming in: 4.4 MB · 3/11/2026 8:15 AM (newer) · There now: …" (folders: how many files in each).</summary>
        static TextBlock Compare(string src, string there, bool folder)
        {
            var t = new TextBlock { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
            try
            {
                if (folder)
                {
                    t.Text = "A folder - what's in it is added to the folder that's there.";
                    return t;
                }
                var a = new FileInfo(src);
                var b = new FileInfo(there);
                if (!b.Exists) { t.Text = Format.Size(a.Length) + " · " + Format.Stamp(a.LastWriteTime); return t; }
                bool same = a.Length == b.Length && Math.Abs((a.LastWriteTimeUtc - b.LastWriteTimeUtc).TotalSeconds) < 2.1;
                bool inNewer = !same && a.LastWriteTimeUtc > b.LastWriteTimeUtc, thereNewer = !same && b.LastWriteTimeUtc > a.LastWriteTimeUtc;
                t.Inlines.Add(new Run("Coming in: "));
                var r1 = new Run(Format.Size(a.Length) + " · " + Format.Stamp(a.LastWriteTime) + (inNewer ? " (newer)" : ""));
                if (inNewer) r1.SetResourceReference(TextElement.ForegroundProperty, "vx.Fg");
                t.Inlines.Add(r1);
                t.Inlines.Add(new LineBreak());
                t.Inlines.Add(new Run("There now: "));
                var r2 = new Run(same ? "the same size and date" : Format.Size(b.Length) + " · " + Format.Stamp(b.LastWriteTime) + (thereNewer ? " (newer)" : ""));
                if (thereNewer || same) r2.SetResourceReference(TextElement.ForegroundProperty, "vx.Fg");
                t.Inlines.Add(r2);
                t.ToolTip = "Coming in: " + src + "\nThere now: " + there;
            }
            catch { t.Text = ""; }
            return t;
        }

        static Border Card(UIElement child, Thickness padding)
        {
            var b = new Border { Child = child, Padding = padding, BorderThickness = new Thickness(1) };
            b.SetResourceReference(Border.BackgroundProperty, "vx.Panel");
            b.SetResourceReference(Border.BorderBrushProperty, "vx.Line");
            b.SetResourceReference(Border.CornerRadiusProperty, "vx.MenuRadius");
            return b;
        }

        static TextBlock Dim(string text, double size, Thickness margin)
        {
            var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = margin };
            t.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
            return t;
        }

        /// <summary>Joined buttons. src: the item they set (its choice is shown, and repainted when "for all" changes); null = for all.</summary>
        FrameworkElement Segmented(string src, string[] values, string[] labels, Action<string> pick)
        {
            string group = "c" + Guid.NewGuid().ToString("N");
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var radios = new List<RadioButton>();
            for (int i = 0; i < values.Length; i++)
            {
                string v = values[i];
                var r = new RadioButton { Content = labels != null ? labels[i] : v, GroupName = group, FontSize = src == null ? 12.5 : 12 };
                r.SetResourceReference(StyleProperty, "vx.Segment");
                if (src != null) r.IsChecked = _choice[src] == v;
                r.Checked += (o, e) => { if (!_painting) pick(v); };
                radios.Add(r);
                row.Children.Add(r);
            }
            if (src != null)
                _repaint.Add(() =>
                {
                    _painting = true;
                    for (int i = 0; i < values.Length; i++) radios[i].IsChecked = _choice[src] == values[i];
                    _painting = false;
                });
            var outer = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(1), Child = row, VerticalAlignment = VerticalAlignment.Center };
            outer.SetResourceReference(Border.BorderBrushProperty, "vx.Track");
            return outer;
        }

        bool _painting;
    }
}
