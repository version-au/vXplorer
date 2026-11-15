using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>
    /// Dragging tabs - vSuite's TabStrip does it (the same in vPad and vTerm), this hears it. Along the strip: the tab follows the mouse, the
    /// others slide out of its way, and on release it glides into its place (Reordered moves it in _tabs too). Pulled out of the strip: a
    /// copy follows the mouse; let go over another vXplorer window's tabs and it moves in there (MovedTo), in the open and a new window
    /// opens with it (TornOut - its folder, history and selection come along). A window's only tab moves the window (Windows' own move);
    /// let go on another window's tabs and it joins them, this one closing. Esc or losing the mouse puts it back.
    /// </summary>
    public partial class MainWindow
    {
        void HookStrip()
        {
            _strip.Picked += st => { var t = st.Tag as TabState; if (t != null) SwitchTo(t); };
            _strip.CloseClicked += st => CloseTab(st.Tag as TabState);
            _strip.MenuRequested += st => TabMenu(st.Tag as TabState);
            _strip.Reordered += (st, from, to) =>
            {
                var t = st.Tag as TabState;
                int i = t != null ? _tabs.IndexOf(t) : -1;
                if (i < 0) return;
                to = Math.Max(0, Math.Min(_tabs.Count - 1, to));
                if (i == to) return;
                _syncing = true;     // the strip has it there already
                try { _tabs.Move(i, to); } finally { _syncing = false; }
            };
            _strip.MovedTo += (st, other, index) =>
            {
                var t = st.Tag as TabState;
                MainWindow w = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(x => x._strip == other);
                if (t == null || w == null || w == this) return;
                if (_tabs.Count == 1)
                {
                    // the window's only tab (the window was dragged by it): it joins the other window, this one closes
                    KeepPlace(t);
                    _tabs.Remove(t);
                    t.IsActive = false;
                    w.TakeTab(t, index);
                    Close();
                    return;
                }
                LetGoOfTab(t);
                w.TakeTab(t, index);
            };
            _strip.TornOut += (st, screen) => { var t = st.Tag as TabState; if (t != null) TearOut(t, screen); };
        }

        /// <summary>The tab's selection and scroll position, kept as it leaves (if it was showing).</summary>
        void KeepPlace(TabState t)
        {
            if (t != _tab) return;
            t.Selected = PathSet(SelectedPaths());
            System.Windows.Controls.ScrollViewer sv = ListScroller();
            t.Scroll = sv != null ? sv.VerticalOffset : 0;
        }

        /// <summary>Take t out of this window (keeping where it was: selection and scroll if it was showing).</summary>
        void LetGoOfTab(TabState t)
        {
            KeepPlace(t);
            int i = _tabs.IndexOf(t);
            bool wasActive = t == _tab;
            _tabs.RemoveAt(i);
            t.IsActive = false;
            if (wasActive)
            {
                _tab = null;
                SwitchTo(_tabs[Math.Min(i, _tabs.Count - 1)]);
            }
        }

        /// <summary>A new window with just this tab, its tab under the mouse (screen pixels).</summary>
        void TearOut(TabState t, Point screen)
        {
            LetGoOfTab(t);
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            Rect size = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            var w = new MainWindow(_settings, _themeSource, null, t);
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.WindowState = WindowState.Normal;
            w.Width = size.Width;
            w.Height = size.Height;
            // the tab lands under the mouse: past the mark at the left of the strip, held where it was grabbed
            w.Left = screen.X / dpi.DpiScaleX - (TabArea.TranslatePoint(new Point(0, 0), this).X + _strip.GrabOffset);
            w.Top = screen.Y / dpi.DpiScaleY - 20;
            w.Show();
            w.Activate();
        }

        /// <summary>A tab from another window, at index in the strip (-1 = the end), shown.</summary>
        void TakeTab(TabState t, int index)
        {
            if (index < 0 || index > _tabs.Count) index = _tabs.Count;
            _tabs.Insert(index, t);
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            SwitchTo(t);
        }
    }
}
