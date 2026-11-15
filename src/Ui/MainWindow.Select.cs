using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>
    /// Drag a box from an empty part of the list to select everything it touches, as in Explorer: the list scrolls when the mouse goes past
    /// its top or bottom, Ctrl adds to what's already selected. Works in Details and the icon views. Which items the box covers is
    /// worked out from the layout (row height, tile size), not from the item controls, so items scrolled out of view count too.
    /// </summary>
    public partial class MainWindow
    {
        bool _banding;
        Panel _bandPanel;
        ScrollViewer _bandScroll;
        /// <summary>Where the drag started: x in the panel, y in the content (panel y + scroll offset).</summary>
        Point _bandAnchor;
        HashSet<FileItem> _bandBase;
        DispatcherTimer _bandTimer;

        /// <summary>A press on the empty part of the list: start a box. False if it isn't somewhere a box can start.</summary>
        bool BeginBand(MouseButtonEventArgs e)
        {
            if (e.ClickCount != 1) return false;
            Panel panel = FindChild<VirtualizingPanel>(Files);
            ScrollViewer sv = ListScroller();
            if (panel == null || sv == null) return false;
            Point p = e.GetPosition(panel);
            if (p.X < 0 || p.Y < 0 || p.X > panel.ActualWidth || p.Y > panel.ActualHeight) return false;

            bool add = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (!add) Files.SelectedItems.Clear();
            _bandBase = new HashSet<FileItem>(Files.SelectedItems.Cast<FileItem>());
            _bandPanel = panel;
            _bandScroll = sv;
            _bandAnchor = new Point(p.X, p.Y + sv.VerticalOffset);
            _banding = true;
            Files.Focus();
            Files.CaptureMouse();
            if (_bandTimer == null)
            {
                _bandTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                _bandTimer.Tick += (s, a) => AutoScrollBand();
            }
            _bandTimer.Start();
            return true;
        }

        void UpdateBand()
        {
            if (!_banding) return;
            Panel panel = _bandPanel;
            double offset = _bandScroll.VerticalOffset;
            Point m = Mouse.GetPosition(panel);
            // clamp to the panel's width and the content's height
            double x = Math.Max(0, Math.Min(panel.ActualWidth, m.X));
            double y = Math.Max(0, m.Y + offset);
            Rect box = new Rect(_bandAnchor, new Point(x, y));

            // draw it (in view: content y - offset), clipped to the list's item area
            Rect view = new Rect(box.X, box.Y - offset, box.Width, box.Height);
            view.Intersect(new Rect(0, 0, panel.ActualWidth, panel.ActualHeight));
            bool moved = box.Width >= SystemParameters.MinimumHorizontalDragDistance || box.Height >= SystemParameters.MinimumVerticalDragDistance;
            if (view.IsEmpty || !moved) Band.Visibility = System.Windows.Visibility.Collapsed;
            else
            {
                Point tl = panel.TranslatePoint(view.TopLeft, BandLayer);
                Canvas.SetLeft(Band, tl.X);
                Canvas.SetTop(Band, tl.Y);
                Band.Width = Math.Max(1, view.Width);
                Band.Height = Math.Max(1, view.Height);
                Band.Visibility = System.Windows.Visibility.Visible;
            }
            if (!moved) return;

            // which items it covers
            var want = new HashSet<FileItem>(_bandBase);
            foreach (int i in ItemsInBox(box, offset)) want.Add(_items[i]);
            var selected = Files.SelectedItems.Cast<FileItem>().ToList();
            foreach (FileItem f in selected) if (!want.Contains(f)) Files.SelectedItems.Remove(f);
            var have = new HashSet<FileItem>(Files.SelectedItems.Cast<FileItem>());
            foreach (FileItem f in want) if (!have.Contains(f)) Files.SelectedItems.Add(f);
            UpdateStatus();
            SchedulePreview();
        }

        /// <summary>The indexes of the items the box (content coordinates) touches, from the layout of one item on screen.</summary>
        IEnumerable<int> ItemsInBox(Rect box, double offset)
        {
            int count = _items.Count;
            if (count == 0) yield break;
            if (Grouped)
            {
                // grouped: headers break up the even rows the sums below rely on - take the item controls on screen as they are
                var hits = new List<int>();
                foreach (ListViewItem lvi in AllOf<ListViewItem>(Files))
                {
                    if (!lvi.IsVisible) continue;
                    var f = lvi.Content as FileItem;
                    if (f == null) continue;
                    Point at = lvi.TranslatePoint(new Point(0, 0), _bandPanel);
                    var r = new Rect(at.X, at.Y + offset, lvi.ActualWidth, lvi.ActualHeight);
                    if (!r.IntersectsWith(box)) continue;
                    int i = _items.IndexOf(f);
                    if (i >= 0) hits.Add(i);
                }
                foreach (int i in hits) yield return i;
                yield break;
            }
            // the item controls on screen: their index and where they are (content coordinates)
            var shown = new List<Tuple<int, Rect>>();
            foreach (UIElement c in _bandPanel.Children)
            {
                var lvi = c as ListViewItem;
                if (lvi == null || !lvi.IsVisible) continue;
                int i = Files.ItemContainerGenerator.IndexFromContainer(lvi);
                if (i < 0) continue;
                Point at = lvi.TranslatePoint(new Point(0, 0), _bandPanel);
                Thickness mg = lvi.Margin;
                shown.Add(Tuple.Create(i, new Rect(at.X - mg.Left, at.Y + offset - mg.Top, lvi.ActualWidth + mg.Left + mg.Right, lvi.ActualHeight + mg.Top + mg.Bottom)));
            }
            if (shown.Count == 0) yield break;
            Tuple<int, Rect> first = shown.OrderBy(s => s.Item1).First();
            double w = first.Item2.Width, h = first.Item2.Height;
            if (h < 1 || w < 1) yield break;

            if (Files.View != null)
            {
                // Details: one row per item, full width
                double top0 = first.Item2.Y - first.Item1 * h;
                int r1 = (int)Math.Floor((box.Top - top0) / h), r2 = (int)Math.Floor((box.Bottom - top0) / h);
                for (int i = Math.Max(0, r1); i <= Math.Min(count - 1, r2); i++) yield return i;
                yield break;
            }

            // icon views: a grid of equal tiles - how many per row is how many share the fullest row on screen
            int cols = Math.Max(1, shown.GroupBy(s => Math.Round(s.Item2.Y)).Max(g => g.Count()));
            int row = first.Item1 / cols, col = first.Item1 % cols;
            double left0 = first.Item2.X - col * w, top = first.Item2.Y - row * h;
            int rTop = (int)Math.Floor((box.Top - top) / h), rBottom = (int)Math.Floor((box.Bottom - top) / h);
            int cLeft = (int)Math.Floor((box.Left - left0) / w), cRight = (int)Math.Floor((box.Right - left0) / w);
            cLeft = Math.Max(0, cLeft);
            cRight = Math.Min(cols - 1, cRight);
            for (int r = Math.Max(0, rTop); r <= rBottom; r++)
                for (int c = cLeft; c <= cRight; c++)
                {
                    int i = r * cols + c;
                    if (i >= count) yield break;
                    yield return i;
                }
        }

        static IEnumerable<T> AllOf<T>(DependencyObject d) where T : DependencyObject
        {
            if (d == null) yield break;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
            {
                DependencyObject c = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                var t = c as T;
                if (t != null) yield return t;
                else foreach (T x in AllOf<T>(c)) yield return x;
            }
        }

        /// <summary>Past the top or bottom of the list while dragging: scroll, faster the further out.</summary>
        void AutoScrollBand()
        {
            if (!_banding) { _bandTimer.Stop(); return; }
            Point m = Mouse.GetPosition(_bandPanel);
            double step = 0;
            if (m.Y < 0) step = m.Y;
            else if (m.Y > _bandPanel.ActualHeight) step = m.Y - _bandPanel.ActualHeight;
            if (step != 0)
            {
                step = Math.Sign(step) * Math.Min(60, 4 + Math.Abs(step) / 2);
                _bandScroll.ScrollToVerticalOffset(Math.Max(0, _bandScroll.VerticalOffset + step));
                _bandScroll.UpdateLayout();
            }
            UpdateBand();
        }

        void Files_LostMouseCapture(object sender, MouseEventArgs e) { if (_banding) EndBand(); }

        void EndBand()
        {
            if (!_banding) return;
            _banding = false;
            if (_bandTimer != null) _bandTimer.Stop();
            Band.Visibility = System.Windows.Visibility.Collapsed;
            _bandBase = null;
            if (Files.IsMouseCaptured) Files.ReleaseMouseCapture();
            if (Files.SelectedItem != null)
            {
                // keyboard focus on a selected item, so the arrows carry on from there
                var c = Files.ItemContainerGenerator.ContainerFromItem(Files.SelectedItems[Files.SelectedItems.Count - 1]) as ListViewItem;
                if (c != null) c.Focus();
            }
            UpdateStatus();
            SchedulePreview();
        }
    }
}
