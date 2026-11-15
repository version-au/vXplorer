using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using vXplorer.Core;
using Vanara.PInvoke;

namespace vXplorer.Ui
{
    /// <summary>
    /// The file operations panel under the list (every window shows the same operations): a row per copy / move / delete with its
    /// progress, speed and time left, pause / cancel, and once done: Undo, Open folder, or what went wrong and Try again. Folded away,
    /// it's a small chip in the status bar. The window's taskbar button shows the progress too.
    /// </summary>
    public sealed class OpsPanel : Border
    {
        static readonly List<OpsPanel> Panels = new List<OpsPanel>();

        /// <summary>In the popped-out window (OpsWindow) rather than under a vXplorer window's list.</summary>
        readonly bool _floating;
        MainWindow _owner;
        /// <summary>The vXplorer window this panel works with: its own, or for the popped-out one the last one used.</summary>
        MainWindow _win { get { return _floating ? OpsWindow.Host : _owner; } }
        readonly StackPanel _rows = new StackPanel();
        readonly TextBlock _count;
        readonly DispatcherTimer _tick;
        readonly Dictionary<int, Row> _byId = new Dictionary<int, Row>();
        /// <summary>The status bar's chip (the window puts it there): shown while the panel is folded away.</summary>
        public readonly Button Chip;
        readonly Grid _chipBar;
        readonly TextBlock _chipText;
        bool _attached;

        public OpsPanel() : this(false) { }

        public OpsPanel(bool floating)
        {
            _floating = floating;
            Visibility = floating ? Visibility.Visible : Visibility.Collapsed;
            Margin = new Thickness(0, floating ? 0 : 8, 0, 0);
            Padding = new Thickness(0, 2, 0, 4);
            SetResourceReference(StyleProperty, "vx.Card");

            var outer = new DockPanel();
            var head = new DockPanel { Margin = new Thickness(14, 6, 6, 2) };
            var fold = IconBtn("", floating ? "Hide (it stays in the status bar)" : "Fold away (it stays in the status bar)", () => SetFolded(true));
            DockPanel.SetDock(fold, Dock.Right);
            head.Children.Add(fold);
            var pop = IconBtn(floating ? "" : "", floating ? "Put it back under the list" : "Pop out into its own window", () => SetPopOut(!floating));
            DockPanel.SetDock(pop, Dock.Right);
            head.Children.Add(pop);
            var title = new TextBlock { Text = "File operations", FontWeight = FontWeights.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            head.Children.Add(title);
            _count = Dim("", 12);
            _count.Margin = new Thickness(8, 0, 0, 0);
            _count.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(_count);
            DockPanel.SetDock(head, Dock.Top);
            outer.Children.Add(head);
            outer.Children.Add(new ScrollViewer { Content = _rows, MaxHeight = floating ? 520 : 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            Child = outer;

            // the chip: a little bar and a line of text, in the status bar
            var chipRow = new StackPanel { Orientation = Orientation.Horizontal };
            _chipBar = Bar(70);
            _chipBar.VerticalAlignment = VerticalAlignment.Center;
            _chipBar.Margin = new Thickness(0, 0, 8, 0);
            chipRow.Children.Add(_chipBar);
            _chipText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            chipRow.Children.Add(_chipText);
            Chip = new Button { Content = chipRow, ToolTip = "Show the file operations", Visibility = Visibility.Collapsed, Margin = new Thickness(12, 0, 0, 0), Height = 22, Padding = new Thickness(10, 0, 10, 0) };
            Chip.SetResourceReference(StyleProperty, "vx.TextButton");
            Chip.Click += (o, e) => SetFolded(false);

            _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            _tick.Tick += (o, e) => Draw();
            Loaded += (o, e) => Attach();
        }

        void Attach()
        {
            if (_attached) return;
            _attached = true;
            _owner = Window.GetWindow(this) as MainWindow;
            Panels.Add(this);
            Operations.PanelHovered = () => Panels.Any(p => p.IsMouseOver);
            Operations.Changed += OnChanged;
            OnChanged();
        }

        /// <summary>The window is closing.</summary>
        public void Detach()
        {
            if (!_attached) return;
            _attached = false;
            Operations.Changed -= OnChanged;
            Panels.Remove(this);
            _tick.Stop();
        }

        bool Folded { get { return _win != null && _win.Settings.OpsFolded; } }
        bool PoppedOut { get { return _win != null && _win.Settings.OpsPopOut; } }

        /// <summary>Folded away (into the status bar's chip) or shown - docked, or the popped-out window.</summary>
        public void SetFolded(bool folded)
        {
            if (_win == null) return;
            _win.Settings.OpsFolded = folded;
            _win.Settings.Save();
            Operations.Raise();   // every window's panel
        }

        /// <summary>The panel's pop-out / dock button (Options > Files has the same switch).</summary>
        void SetPopOut(bool on)
        {
            if (_win == null) return;
            _win.Settings.OpsPopOut = on;
            _win.Settings.OpsFolded = false;
            _win.Settings.Save();
            Operations.Raise();
        }

        void OnChanged()
        {
            // rows for new operations, none for the ones that have gone
            var ops = Operations.All.Where(o => !o.Hidden).ToList();
            foreach (int id in _byId.Keys.ToList())
                if (!ops.Any(o => o.Id == id)) { _rows.Children.Remove(_byId[id].Root); _byId.Remove(id); }
            foreach (Operation op in ops)
                if (!_byId.ContainsKey(op.Id))
                {
                    var r = new Row(this, op);
                    _byId[op.Id] = r;
                    _rows.Children.Add(r.Root);
                }
            for (int i = 0; i < _rows.Children.Count; i++)
                ((Border)_rows.Children[i]).BorderThickness = new Thickness(0, i == 0 ? 0 : 1, 0, 0);
            if (ops.Any(o => o.Active)) _tick.Start(); else _tick.Stop();
            Draw();
            if (!_floating) OpsWindow.Sync();   // the popped-out window shows / hides with the operations
        }

        void Draw()
        {
            var ops = Operations.All.Where(o => !o.Hidden).ToList();
            bool any = ops.Count > 0;
            if (!_floating)
            {
                Visibility = any && !Folded && !PoppedOut ? Visibility.Visible : Visibility.Collapsed;
                Chip.Visibility = any && Folded ? Visibility.Visible : Visibility.Collapsed;
            }
            int running = ops.Count(o => o.Active && o.State != OpState.Waiting), waiting = ops.Count(o => o.State == OpState.Waiting);
            _count.Text = running == 0 && waiting == 0 ? "" : running + " running" + (waiting > 0 ? " · " + waiting + " waiting" : "");
            foreach (Row r in _byId.Values) r.Update();

            // the chip and the taskbar: the first one going
            Operation first = ops.FirstOrDefault(o => o.Active && o.State != OpState.Waiting) ?? ops.FirstOrDefault(o => o.Active);
            if (first != null)
            {
                double f = Math.Max(0, first.Fraction);
                SetBar(_chipBar, f, first.State == OpState.Paused);
                _chipText.Text = Title(first, false) + " · " + Math.Round(f * 100) + "%" + (waiting > 0 && first.State != OpState.Waiting ? " · " + waiting + " more waiting" : "");
            }
            else
            {
                SetBar(_chipBar, 1, false);
                Operation last = ops.LastOrDefault();
                _chipText.Text = last == null ? "" : Title(last, false);
            }
            Window shell = _floating ? Window.GetWindow(this) : _owner;   // its taskbar button shows the progress
            if (shell != null)
            {
                if (shell.TaskbarItemInfo == null) shell.TaskbarItemInfo = new TaskbarItemInfo();
                if (first == null || first.State == OpState.Waiting) shell.TaskbarItemInfo.ProgressState = TaskbarItemProgressState.None;
                else
                {
                    shell.TaskbarItemInfo.ProgressState = first.State == OpState.Paused ? TaskbarItemProgressState.Paused
                                                        : first.Fraction < 0 ? TaskbarItemProgressState.Indeterminate : TaskbarItemProgressState.Normal;
                    shell.TaskbarItemInfo.ProgressValue = Math.Max(0, first.Fraction);
                }
            }
        }

        // =================================================================
        // Wording
        // =================================================================

        static string Count(long n, string one) { return n.ToString("N0") + " " + (n == 1 ? one : one + "s"); }

        static string Noun(Operation op) { return op.Target != null ? "file" : "item"; }

        /// <summary>The row's first line (target: with "to Photos").</summary>
        static string Title(Operation op, bool withTarget = true)
        {
            string to = op.Target != null && withTarget ? " to " : "";
            switch (op.State)
            {
                case OpState.Waiting:
                case OpState.Preparing:
                    return op.Verb + " " + Count(op.TopCount, "item") + to;
                case OpState.Running:
                case OpState.Paused:
                    string t = op.ItemsTotal > 0 ? op.Verb + " " + Math.Min(op.Items + 1, op.ItemsTotal).ToString("N0") + " of " + Count(op.ItemsTotal, Noun(op)) + to
                                                 : op.Verb + " " + Count(op.TopCount, "item") + to;
                    return op.State == OpState.Paused ? "Paused - " + char.ToLowerInvariant(t[0]) + t.Substring(1) : t;
                case OpState.Problems:
                    long total = Math.Max(op.ItemsTotal, op.Failures.Count);
                    return op.DoneVerb + " " + Math.Max(0, total - op.Failures.Count).ToString("N0") + " of " + Count(total, Noun(op)) + to;
                case OpState.Cancelled:
                    return "Stopped - " + op.Verb.ToLowerInvariant().TrimEnd(':') + to;
                default:
                    return op.DoneVerb + " " + Count(op.ItemsTotal > 0 ? op.ItemsTotal : op.TopCount, op.ItemsTotal > 0 ? Noun(op) : "item") + to;
            }
        }

        static string Duration(double secs)
        {
            if (secs < 1) return "under a second";
            if (secs < 60) return Math.Round(secs) + (Math.Round(secs) == 1 ? " second" : " seconds");
            if (secs < 3600) { int m = (int)Math.Round(secs / 60); return m + (m == 1 ? " min" : " min"); }
            int h = (int)(secs / 3600), mm = (int)Math.Round((secs - h * 3600) / 60);
            return h + " hr" + (mm > 0 ? " " + mm + " min" : "");
        }

        static string Detail(Operation op)
        {
            switch (op.State)
            {
                case OpState.Waiting:
                    return "Waiting - starts when the other copy to " + (op.Root ?? "that drive").TrimEnd('\\') + " finishes";
                case OpState.Preparing:
                    return "Getting ready…";
                case OpState.Running:
                {
                    var parts = new List<string>();
                    if (op.BytesTotal > 0) parts.Add(Format.Size(op.Bytes) + " of " + Format.Size(op.BytesTotal));
                    if (op.BytesPerSecond > 1024) parts.Add(Format.Size((long)op.BytesPerSecond) + "/s");
                    if (op.BytesPerSecond > 1024 && op.BytesTotal > op.Bytes && (DateTime.Now - op.StartedAt).TotalSeconds > 2)
                        parts.Add("about " + Duration((op.BytesTotal - op.Bytes) / op.BytesPerSecond) + " left");
                    if (!string.IsNullOrEmpty(op.CurrentName)) parts.Add(op.CurrentName);
                    return string.Join(" · ", parts);
                }
                case OpState.Paused:
                    return (op.BytesTotal > 0 ? Format.Size(op.Bytes) + " of " + Format.Size(op.BytesTotal) + " done · " : "") + "Resume to carry on";
                case OpState.Problems:
                    return Count(op.Failures.Count, "file") + " couldn't be " + (op.Kind == "copy" ? "copied" : op.Kind == "move" ? "moved" : "deleted");
                case OpState.Cancelled:
                    return op.ItemsTotal > 0 ? op.Items.ToString("N0") + " of " + Count(op.ItemsTotal, Noun(op)) + " were done" : "";
                default:
                    string size = op.BytesTotal > 0 ? Format.Size(op.BytesTotal) + " in " : "In ";
                    return size + Duration((op.FinishedAt - op.StartedAt).TotalSeconds) + " · " + op.FinishedAt.ToString("h:mm tt").ToLowerInvariant();
            }
        }

        // =================================================================
        // Building blocks
        // =================================================================

        static TextBlock Dim(string text, double size)
        {
            var t = new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis };
            t.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
            return t;
        }

        static Button IconBtn(string glyph, string tip, Action click)
        {
            var b = new Button { Content = glyph, ToolTip = tip, Width = 30, Height = 28, FontSize = 11 };
            b.SetResourceReference(StyleProperty, "vx.IconButton");
            b.Click += (o, e) => click();
            return b;
        }

        static Button Link(string text, Action click)
        {
            var b = new Button { Content = text, Height = 26, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 2, 0), FontSize = 12.5 };
            b.SetResourceReference(StyleProperty, "vx.CommandButton");
            b.SetResourceReference(Control.ForegroundProperty, "vx.Accent");
            b.Click += (o, e) => click();
            return b;
        }

        /// <summary>A thin progress bar: two star columns (done / to go).</summary>
        static Grid Bar(double width = double.NaN)
        {
            var g = new Grid { Height = 4, Width = width, ClipToBounds = true };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var track = new Border { CornerRadius = new CornerRadius(2) };
            track.SetResourceReference(Border.BackgroundProperty, "vx.Track");
            Grid.SetColumnSpan(track, 2);
            g.Children.Add(track);
            var fill = new Border { CornerRadius = new CornerRadius(2) };
            fill.SetResourceReference(Border.BackgroundProperty, "vx.Accent");
            g.Children.Add(fill);
            return g;
        }

        static void SetBar(Grid bar, double f, bool paused)
        {
            f = Math.Max(0, Math.Min(1, f));
            bar.ColumnDefinitions[0].Width = new GridLength(f, GridUnitType.Star);
            bar.ColumnDefinitions[1].Width = new GridLength(1 - f, GridUnitType.Star);
            ((Border)bar.Children[1]).SetResourceReference(Border.BackgroundProperty, paused ? "vx.FgDim" : "vx.Accent");
        }

        // =================================================================
        // A row
        // =================================================================

        sealed class Row
        {
            readonly OpsPanel _panel;
            readonly Operation _op;
            public readonly Border Root;
            readonly Border _glyphBox;
            readonly TextBlock _glyph, _title, _detail;
            /// <summary>Meter / Line under the text; Ring in the glyph's place while it runs (Options > Files > Progress graph).</summary>
            readonly OpsGraph _graph, _ring;
            readonly StackPanel _buttons, _failures;
            string _sig;

            public Row(OpsPanel panel, Operation op)
            {
                _panel = panel;
                _op = op;
                var g = new Grid { Margin = new Thickness(14, 9, 8, 9) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                _glyph = new TextBlock { FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                _glyph.SetResourceReference(TextBlock.FontFamilyProperty, "vx.Icons");
                _glyphBox = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(7), Child = _glyph, VerticalAlignment = VerticalAlignment.Top };
                _glyphBox.SetResourceReference(Border.BackgroundProperty, "vx.Hover");
                g.Children.Add(_glyphBox);
                _ring = NewGraph();
                _ring.GraphStyle = "Ring";
                _ring.GraphW = _ring.GraphH = 40;
                _ring.VerticalAlignment = VerticalAlignment.Top;
                _ring.Visibility = Visibility.Collapsed;
                g.Children.Add(_ring);

                var text = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
                _title = new TextBlock { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
                _detail = Dim("", 12);
                _detail.Margin = new Thickness(0, 2, 0, 0);
                _graph = NewGraph();
                _graph.HorizontalAlignment = HorizontalAlignment.Stretch;
                text.Children.Add(_title);
                text.Children.Add(_detail);
                text.Children.Add(_graph);
                Grid.SetColumn(text, 1);
                g.Children.Add(text);

                _buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(_buttons, 2);
                g.Children.Add(_buttons);

                _failures = new StackPanel { Margin = new Thickness(12, 8, 0, 0), Visibility = Visibility.Collapsed };
                Grid.SetRow(_failures, 1);
                Grid.SetColumn(_failures, 1);
                Grid.SetColumnSpan(_failures, 2);
                g.Children.Add(_failures);

                Root = new Border { Child = g };
                Root.SetResourceReference(Border.BorderBrushProperty, "vx.Line");
            }

            public void Update()
            {
                Operation op = _op;
                // the first line, with the destination's name in semibold
                _title.Inlines.Clear();
                _title.Inlines.Add(new Run(Title(op)));
                if (op.Target != null) _title.Inlines.Add(new Run(op.Target) { FontWeight = FontWeights.SemiBold });
                _detail.Text = Detail(op);
                if (op.State == OpState.Problems) _detail.SetResourceReference(TextBlock.ForegroundProperty, "vx.Secondary");
                else _detail.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
                ShowGraph(op);

                bool canUndo = op.State == OpState.Done && op.CanUndo != null && op.CanUndo();
                string sig = op.State + "|" + canUndo + "|" + op.Failures.Count;
                if (sig == _sig) return;
                _sig = sig;

                // the glyph and the buttons change with the state
                string glyph;
                string tint = null;
                switch (op.State)
                {
                    case OpState.Waiting: glyph = ""; break;
                    case OpState.Paused: glyph = ""; break;
                    case OpState.Done: glyph = ""; tint = "vx.Accent"; break;
                    case OpState.Problems: glyph = ""; tint = "vx.Secondary"; break;
                    case OpState.Cancelled: glyph = ""; break;
                    default: glyph = op.Kind == "move" ? "" : op.Kind == "copy" ? "" : ""; break;
                }
                _glyph.Text = glyph;
                if (tint != null) _glyph.SetResourceReference(TextBlock.ForegroundProperty, tint);
                else _glyph.SetResourceReference(TextBlock.ForegroundProperty, op.State == OpState.Waiting || op.State == OpState.Paused || op.State == OpState.Cancelled ? "vx.FgDim" : "vx.Fg");

                _buttons.Children.Clear();
                switch (op.State)
                {
                    case OpState.Waiting:
                        _buttons.Children.Add(IconBtn("", "Start it now", () => Operations.StartNow(op)));
                        _buttons.Children.Add(IconBtn("", "Cancel", () => Operations.Cancel(op)));
                        break;
                    case OpState.Preparing:
                    case OpState.Running:
                        _buttons.Children.Add(IconBtn("", "Pause", () => Operations.Pause(op, true)));
                        _buttons.Children.Add(IconBtn("", "Cancel", () => Operations.Cancel(op)));
                        break;
                    case OpState.Paused:
                        _buttons.Children.Add(IconBtn("", "Resume", () => Operations.Pause(op, false)));
                        _buttons.Children.Add(IconBtn("", "Cancel", () => Operations.Cancel(op)));
                        break;
                    case OpState.Done:
                        if (canUndo) _buttons.Children.Add(Link("Undo", () => op.Undo?.Invoke()));
                        if (op.TargetPath != null && _panel._win != null)
                            _buttons.Children.Add(Link("Open folder", () => _panel._win.ShowFolder(op.TargetPath)));
                        _buttons.Children.Add(IconBtn("", "Clear", () => Operations.Clear(op)));
                        break;
                    case OpState.Problems:
                        if (_panel._win != null) _buttons.Children.Add(Link("Try again", () => _panel._win.RetryFailures(op)));
                        _buttons.Children.Add(IconBtn("", "Clear", () => Operations.Clear(op)));
                        break;
                    default:
                        _buttons.Children.Add(IconBtn("", "Clear", () => Operations.Clear(op)));
                        break;
                }

                // what went wrong, a line each (the first eight)
                _failures.Children.Clear();
                List<OpFailure> failed;
                lock (op.Failures) failed = op.Failures.ToList();
                _failures.Visibility = op.State == OpState.Problems && failed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                foreach (OpFailure f in failed.Take(8))
                {
                    var line = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                    var name = new TextBlock { Text = f.Name, Width = 190, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = f.Source };
                    line.Children.Add(name);
                    line.Children.Add(Dim(f.Reason, 12));
                    _failures.Children.Add(line);
                }
                if (failed.Count > 8) _failures.Children.Add(Dim("and " + (failed.Count - 8) + " more (the list is in vxplorer.log)", 12));
            }

            static OpsGraph NewGraph()
            {
                var gr = new OpsGraph();
                gr.SetResourceReference(OpsGraph.TrackProperty, "vx.Track");
                gr.SetResourceReference(OpsGraph.CenterBrushProperty, "vx.Fg");
                gr.SetResourceReference(OpsGraph.FillProperty, "vx.Accent");
                return gr;
            }

            /// <summary>The progress graph in the chosen style (a ring takes the glyph's place while it runs; finished rows show the glyph).</summary>
            void ShowGraph(Operation op)
            {
                AppSettings s = _panel._win != null ? _panel._win.Settings : null;
                string style = s != null ? s.OpsGraph : "Meter";
                bool active = op.Active, ring = active && style == "Ring";
                double v = op.State == OpState.Waiting ? 0 : Math.Max(0, op.Fraction) * 100;
                string fill = op.State == OpState.Paused || op.State == OpState.Waiting ? "vx.FgDim" : "vx.Accent";

                _glyphBox.Visibility = ring ? Visibility.Collapsed : Visibility.Visible;
                _ring.Visibility = ring ? Visibility.Visible : Visibility.Collapsed;
                _graph.Visibility = active && !ring ? Visibility.Visible : Visibility.Collapsed;
                if (ring)
                {
                    _ring.SetResourceReference(OpsGraph.FillProperty, fill);
                    _ring.Value = v;
                    _ring.Center = op.Fraction < 0 || op.State == OpState.Waiting ? "…" : Math.Floor(v) + "%";
                    _ring.InvalidateVisual();
                    return;
                }
                if (!active) return;
                bool line = style == "Line";
                double h = line ? 34 : 6;
                if (_graph.GraphStyle != style || _graph.GraphH != h)
                {
                    _graph.GraphStyle = line ? "Line" : "Meter";
                    _graph.GraphH = h;
                    _graph.Margin = new Thickness(0, line ? 8 : 6, 0, 0);
                    _graph.InvalidateMeasure();
                }
                _graph.SetResourceReference(OpsGraph.FillProperty, fill);
                _graph.Value = v;
                _graph.Speeds = op.Speeds;
                if (s != null)
                {
                    _graph.LineWidth = OpsGraph.LineWidthOf(s.OpsGraphLine);
                    _graph.AreaFill = s.OpsGraphFill;
                    _graph.ShowBack = s.OpsGraphBack;
                }
                _graph.InvalidateVisual();
            }
        }
    }
}
