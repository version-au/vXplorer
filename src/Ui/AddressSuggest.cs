using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>
    /// The list under the address bar while you type a path (PathSuggest): Down / Up pick one (the box shows it), Tab fills it in and
    /// goes on to its folders, Enter or a click goes there, Esc closes the list (and puts back what you typed). The box keeps the focus.
    /// </summary>
    sealed class AddressSuggest
    {
        readonly TextBox _box;
        readonly FrameworkElement _anchor;
        readonly ShellIcons _icons;
        readonly Func<bool> _showHidden;
        readonly Func<List<Tuple<string, string>>> _places;
        readonly Action<string> _go;
        readonly Popup _popup;
        readonly StackPanel _rows = new StackPanel();
        readonly DispatcherTimer _timer;

        List<Tuple<string, string>> _shown = new List<Tuple<string, string>>();
        int _index = -1;
        int _version;
        /// <summary>The text is being set here (a picked suggestion): no new lookup for it.</summary>
        bool _quiet;
        /// <summary>What was typed before Down / Up put a suggestion in the box (Esc puts it back).</summary>
        string _typed;

        /// <param name="places">Before any backslash: what to offer (This PC, Network, Quick access, drives), as (name, path) - on the UI thread.</param>
        /// <param name="go">A suggestion was picked to go to.</param>
        public AddressSuggest(TextBox box, FrameworkElement anchor, ShellIcons icons, Func<bool> showHidden,
                              Func<List<Tuple<string, string>>> places, Action<string> go)
        {
            _box = box;
            _anchor = anchor;
            _icons = icons;
            _showHidden = showHidden;
            _places = places;
            _go = go;

            var card = new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 10, 10),   // room for the shadow
                Child = _rows,
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Direction = 270, Opacity = 0.35 }
            };
            card.SetResourceReference(Border.BackgroundProperty, "vx.Popup");
            card.SetResourceReference(Border.BorderBrushProperty, "vx.PanelBorder");
            card.SetResourceReference(Border.CornerRadiusProperty, "vx.MenuRadius");
            card.SetResourceReference(TextElement.ForegroundProperty, "vx.Fg");
            TextElement.SetFontFamily(card, new FontFamily("Segoe UI"));
            TextElement.SetFontSize(card, 12.5);
            _popup = new Popup
            {
                PlacementTarget = anchor,
                Placement = PlacementMode.Bottom,
                VerticalOffset = 4,
                AllowsTransparency = true,
                StaysOpen = true,   // closed here: when the box closes, loses the focus or the window moves
                Focusable = false,
                Child = card
            };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _timer.Tick += (s, e) => { _timer.Stop(); Lookup(); };
            _box.TextChanged += (s, e) =>
            {
                if (_quiet || _box.Visibility != Visibility.Visible) return;
                _typed = null;
                _timer.Stop();
                _timer.Start();
            };
            _box.PreviewKeyDown += Box_PreviewKeyDown;
        }

        public bool IsOpen { get { return _popup.IsOpen; } }

        /// <summary>Close the list (the address bar stops typing, the window moves or loses the focus).</summary>
        public void Close()
        {
            _timer.Stop();
            _version++;   // a lookup on its way is dropped
            _popup.IsOpen = false;
            _shown = new List<Tuple<string, string>>();
            _index = -1;
            _typed = null;
        }

        async void Lookup()
        {
            int v = ++_version;
            string text = _box.Text;
            bool hidden = _showHidden();
            List<Tuple<string, string>> places = _places();
            string folder, prefix;
            bool inFolder = PathSuggest.Split(text, out folder, out prefix);
            if (string.IsNullOrWhiteSpace(text)) { Close(); return; }
            List<Tuple<string, string>> found = await Task.Run(() => PathSuggest.For(text, hidden, places));
            if (v != _version || _box.Visibility != Visibility.Visible) return;   // typed on, or done typing
            // the folder exactly as typed is no suggestion ("C:\Users" offering "Users")
            found.RemoveAll(f => Same(f.Item2.TrimEnd('\\'), text.Trim().TrimEnd('\\')));
            Show(found, !inFolder);
        }

        void Show(List<Tuple<string, string>> list, bool withPath)
        {
            _shown = list;
            _index = -1;
            _rows.Children.Clear();
            if (list.Count == 0) { _popup.IsOpen = false; return; }
            int version = _version;
            for (int i = 0; i < list.Count; i++)
            {
                int at = i;
                Tuple<string, string> s = list[i];
                var img = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                var name = new TextBlock { Text = s.Item1, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.Children.Add(img);
                Grid.SetColumn(name, 1);
                grid.Children.Add(name);
                if (withPath && !s.Item2.StartsWith("::"))   // places by name: where they are, dimmed
                {
                    var where = new TextBlock { Text = s.Item2, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                                                TextTrimming = TextTrimming.CharacterEllipsis };
                    where.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
                    Grid.SetColumn(where, 2);
                    grid.Children.Add(where);
                }
                var row = new Border { Padding = new Thickness(8, 5, 8, 5), Background = Brushes.Transparent, Child = grid, Cursor = Cursors.Hand };
                row.SetResourceReference(Border.CornerRadiusProperty, "vx.ItemRadius");
                // the list can open under a mouse that isn't moving: only a real move highlights (else Enter would pick that row)
                row.MouseMove += (o, e) => { if (_index != at && MouseMoved()) Highlight(at); };
                row.MouseLeave += (o, e) => { if (_index == at && MouseMoved()) Highlight(-1); };
                row.MouseLeftButtonUp += (o, e) => { e.Handled = true; Pick(s.Item2); };
                _rows.Children.Add(row);
                _icons.Request(s.Item2, ShellIcons.NodeKey(s.Item2), () => version == _version, icon => img.Source = icon);
            }
            GetCursorPos(out _mouseAtOpen);
            _popup.MinWidth = Math.Max(240, _anchor.ActualWidth + 10);
            _popup.MaxWidth = Math.Max(240, _anchor.ActualWidth + 10);
            _popup.IsOpen = true;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }
        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT p);

        /// <summary>Where the mouse was when the list (re)opened.</summary>
        POINT _mouseAtOpen;

        bool MouseMoved()
        {
            POINT p;
            return GetCursorPos(out p) && (p.X != _mouseAtOpen.X || p.Y != _mouseAtOpen.Y);
        }

        void Highlight(int i)
        {
            _index = i;
            for (int k = 0; k < _rows.Children.Count; k++)
            {
                var row = (Border)_rows.Children[k];
                if (k == i) row.SetResourceReference(Border.BackgroundProperty, "vx.Hover");
                else row.Background = Brushes.Transparent;
            }
        }

        /// <summary>Down / Up: the next suggestion, shown in the box too (Enter then goes there as usual).</summary>
        void Move(int by)
        {
            if (_shown.Count == 0) return;
            if (_typed == null) _typed = _box.Text;
            int i = _index + by;
            if (i < -1) i = _shown.Count - 1;
            if (i >= _shown.Count) i = -1;   // past the end: back to what was typed
            Highlight(i);
            SetText(i < 0 ? _typed : InBox(_shown[i]));
        }

        /// <summary>What the box shows for a suggestion: its path, or a place's name ("This PC", not its "::{…}" path).</summary>
        static string InBox(Tuple<string, string> s) { return s.Item2.StartsWith("::") ? s.Item1 : s.Item2; }

        void SetText(string text)
        {
            _quiet = true;
            _box.Text = text;
            _box.CaretIndex = text.Length;
            _quiet = false;
        }

        void Pick(string path)
        {
            Close();
            _go(path);
        }

        void Box_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.None && Key(e.Key)) e.Handled = true;
        }

        /// <summary>For the test hooks: the list's state, "index: name=path | …".</summary>
        public string Describe()
        {
            return (IsOpen ? "open" : "closed") + " " + _index + ": " + string.Join(" | ", _shown.ConvertAll(s => s.Item1 + "=" + s.Item2));
        }

        /// <summary>A key pressed in the box (unmodified); true when the list used it.</summary>
        public bool Key(Key key)
        {
            switch (key)
            {
                case System.Windows.Input.Key.Down:
                    if (IsOpen) Move(1); else Lookup();   // closed: the list for what's there
                    return true;
                case System.Windows.Input.Key.Up:
                    if (!IsOpen) return false;
                    Move(-1);
                    return true;
                case System.Windows.Input.Key.Tab:
                {
                    if (!IsOpen || _shown.Count == 0) return false;
                    // fill in the highlighted (or first) one and list what's inside it
                    Tuple<string, string> s = _shown[Math.Max(0, _index)];
                    bool place = s.Item2.StartsWith("::") || NetworkPlaces.IsNetwork(s.Item2);
                    SetText(place ? InBox(s) : s.Item2.EndsWith("\\") ? s.Item2 : s.Item2 + "\\");
                    _typed = null;
                    if (place) Close(); else Lookup();
                    return true;
                }
                case System.Windows.Input.Key.Escape:
                    if (!IsOpen) return false;   // the next Esc leaves the address bar
                    if (_typed != null) SetText(_typed);
                    Close();
                    return true;
                case System.Windows.Input.Key.Enter:
                    if (!IsOpen || _index < 0) return false;   // nothing picked: the box's text, as before
                    Pick(_shown[_index].Item2);   // by its path (certain, even for a place shown by name)
                    return true;
                default:
                    return false;
            }
        }
    }
}
