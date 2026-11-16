using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Vanara.Windows.Shell;
using vXplorer.Core;
using DragEventArgs = System.Windows.DragEventArgs;

namespace vXplorer.Ui
{
    /// <summary>
    /// This PC, like Explorer's: "Devices and drives" and "Network locations" as tiles with the drive's icon, a bar for the space
    /// used and "N GB free of M GB". It's a place like a folder (tabs, Back, Up from a drive, breadcrumbs, the address bar, the sidebar's
    /// "This PC"); its path is the shell's own name for This PC. Slow drives (a network drive that's offline) fill in when they answer.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>This PC's shell parsing name - the shell (icons, Properties, its menu) understands it too.</summary>
        public const string ThisPcPath = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";

        static bool IsThisPc(string p)
        {
            if (p == null) return false;
            p = p.Trim();
            return string.Equals(p, ThisPcPath, StringComparison.OrdinalIgnoreCase) || string.Equals(p, "This PC", StringComparison.OrdinalIgnoreCase)
                || string.Equals(p, "shell:MyComputerFolder", StringComparison.OrdinalIgnoreCase) || string.Equals(p, "Computer", StringComparison.OrdinalIgnoreCase);
        }

        bool InThisPc { get { return IsThisPc(_current); } }

        /// <summary>A folder that's there, or This PC / Network / a computer (a computer that's gone says so when it's read).</summary>
        static bool PlaceExists(string p) { return IsPlace(p) || (p != null && Paths.FolderThere(p)); }

        /// <summary>The name shown for a place (tab, title, breadcrumbs).</summary>
        static string NameOf(string p)
        {
            if (IsThisPc(p)) return "This PC";
            if (IsNetwork(p)) return "Network";
            if (IsServer(p)) return NetworkPlaces.ServerPath(p).Substring(2);
            return Native.DisplayName(p);
        }

        readonly ObservableCollection<DriveTile> _pcTiles = new ObservableCollection<DriveTile>();
        ListCollectionView _pcView;
        /// <summary>Selected when This PC next shows (Up from a drive lands on it).</summary>
        string _pcSelectNext;

        void ShowPcView(bool on)
        {
            PcList.Visibility = on ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            Files.Visibility = on ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            if (on) EmptyText.Visibility = System.Windows.Visibility.Collapsed;
        }

        void ThisPcHeader_Click(object sender, MouseButtonEventArgs e) { Navigate(ThisPcPath); e.Handled = true; }

        /// <summary>Right-click the sidebar's This PC: Windows' menu for it (Manage, Properties …).</summary>
        void ThisPcRow_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ShowShellMenu(new List<string> { ThisPcPath }, PointToScreen(e.GetPosition(this)), false);
        }

        /// <summary>The sidebar's This PC row: Windows' icon for it, highlighted while This PC is open.</summary>
        void SetUpThisPcRow()
        {
            _icons.Request(ThisPcPath, "node:" + ThisPcPath, null, img => ThisPcIcon.Source = img);
        }

        void ShowThisPcRowOpen(bool open) { ThisPcRow.Tag = open ? "open" : null; }

        async void LoadThisPc(string select)
        {
            int v = ++_loadVersion;
            ShowPcView(true);
            _items = new ObservableCollection<FileItem>();   // nothing in the file list behind it
            Files.ItemsSource = _items;
            if (_pcView == null)
            {
                // Options > Opening: one click opens a drive too
                PcList.MouseLeftButtonUp += (s, e) =>
                {
                    if (!_settings.SingleClick || Keyboard.Modifiers != ModifierKeys.None) return;
                    DriveTile t = TileAt(e.OriginalSource as DependencyObject);
                    if (t != null) OpenTile(t);
                };
                _pcView = new ListCollectionView(_pcTiles);
                _pcView.GroupDescriptions.Add(new PropertyGroupDescription("Section"));
                _pcView.SortDescriptions.Add(new SortDescription("SortKey", ListSortDirection.Ascending));
                PcList.ItemsSource = _pcView;
            }

            List<DriveTile> tiles = await Task.Run(() => ListPlaces());
            if (v != _loadVersion) return;
            _pcTiles.Clear();
            foreach (DriveTile t in tiles) _pcTiles.Add(t);
            DriveTile sel = tiles.FirstOrDefault(t => select != null && string.Equals(t.Path.TrimEnd('\\'), select.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            if (sel != null) { PcList.SelectedItem = sel; PcList.ScrollIntoView(sel); }
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(FocusPcList));
            UpdateStatus();
            SchedulePreview();

            int px = (int)Math.Round(48 * VisualTreeHelper.GetDpi(this).DpiScaleX);
            foreach (DriveTile t in tiles)
            {
                DriveTile tile = t;
                _icons.RequestThumb(tile.Path, px, () => v == _loadVersion, img => tile.Icon = img);
                // name and size off the UI thread, each on its own: an offline network drive doesn't hold up the others
                Task.Run(() => FillTile(tile)).ContinueWith(_ => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (v != _loadVersion) return;
                    if (PcList.SelectedItem == tile) SchedulePreview();
                    UpdateStatus();
                })));
            }
        }

        void FocusPcList()
        {
            if (!InThisPc) return;
            var c = (PcList.SelectedItem != null ? PcList.ItemContainerGenerator.ContainerFromItem(PcList.SelectedItem) : null) as ListBoxItem;
            if (c != null) c.Focus(); else PcList.Focus();
        }

        /// <summary>The drives (letters and types only - quick) and the network locations made with "Add a network location".</summary>
        static List<DriveTile> ListPlaces()
        {
            var list = new List<DriveTile>();
            foreach (string root in Environment.GetLogicalDrives())
            {
                DriveType type;
                try { type = new DriveInfo(root).DriveType; } catch { continue; }
                if (type == DriveType.NoRootDirectory) continue;
                bool net = type == DriveType.Network;
                var t = new DriveTile
                {
                    Path = root,
                    Section = net ? DriveTile.Network : DriveTile.Drives,
                    SortKey = (net ? "1|" : "0|") + root,
                    Kind = KindOf(type),
                    Name = root.TrimEnd('\\')
                };
                t.SpaceText = t.Kind;
                list.Add(t);
            }

            try
            {
                string shortcuts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Network Shortcuts");
                if (Directory.Exists(shortcuts))
                {
                    var found = new List<Tuple<string, string>>();   // (name, target)
                    foreach (string dir in Directory.EnumerateDirectories(shortcuts))
                    {
                        string lnk = Path.Combine(dir, "target.lnk");
                        string target = File.Exists(lnk) ? LinkTarget(lnk) : null;
                        if (!string.IsNullOrEmpty(target)) found.Add(Tuple.Create(Path.GetFileName(dir), target));
                    }
                    foreach (string lnk in Directory.EnumerateFiles(shortcuts, "*.lnk"))
                    {
                        string target = LinkTarget(lnk);
                        if (!string.IsNullOrEmpty(target)) found.Add(Tuple.Create(Path.GetFileNameWithoutExtension(lnk), target));
                    }
                    foreach (Tuple<string, string> f in found)
                    {
                        var t = new DriveTile { Path = f.Item2, Section = DriveTile.Network, SortKey = "2|" + f.Item1, Kind = "Network location", Name = f.Item1 };
                        t.SpaceText = f.Item2;
                        list.Add(t);
                    }
                }
            }
            catch (Exception ex) { Log.Write("Network locations: " + ex.Message); }
            return list;
        }

        static string LinkTarget(string lnk)
        {
            try { using (var l = new ShellLink(lnk)) return l.TargetPath; }
            catch { return null; }
        }

        static string KindOf(DriveType t)
        {
            switch (t)
            {
                case DriveType.Fixed: return "Local Disk";
                case DriveType.Removable: return "USB Drive";
                case DriveType.CDRom: return "CD Drive";
                case DriveType.Network: return "Network drive";
                case DriveType.Ram: return "RAM disk";
                default: return "Drive";
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetDiskFreeSpaceEx(string dir, out ulong freeToCaller, out ulong total, out ulong totalFree);

        /// <summary>Its name as Explorer shows it ("Local Disk (C:)") and its space (on a worker thread; may be slow).</summary>
        static void FillTile(DriveTile t)
        {
            bool drive = t.Path.Length <= 3;
            string name = drive ? Native.DisplayName(t.Path) : null;
            string fs = null;
            if (drive) { try { var di = new DriveInfo(t.Path); if (di.IsReady) fs = di.DriveFormat; } catch { } }
            ulong free, total, totalFree;
            bool ok = GetDiskFreeSpaceEx(t.Path.EndsWith("\\") ? t.Path : t.Path + "\\", out free, out total, out totalFree);
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!string.IsNullOrEmpty(name)) t.Name = name;
                t.FileSystem = fs;
                if (ok && total > 0) t.SetSpace((long)total, (long)free);
                else if (t.Section == DriveTile.Network && drive) t.SpaceText = "Disconnected network drive";
            }));
        }

        // ---- using the tiles ----

        DriveTile TileAt(DependencyObject d)
        {
            var c = d == null ? null : ItemsControl.ContainerFromElement(PcList, d) as ListBoxItem;
            return c == null ? null : c.Content as DriveTile;
        }

        void OpenTile(DriveTile t)
        {
            if (t == null) return;
            if (!Directory.Exists(t.Path)) { Status(t.Name + " isn't ready - " + (t.Kind == "CD Drive" ? "is there a disc in it?" : "is it connected?")); return; }
            if (_settings.FoldersInNewTab) OpenTab(t.Path, true); else Navigate(t.Path);
        }

        void PcList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || _settings.SingleClick) return;
            DriveTile t = TileAt(e.OriginalSource as DependencyObject);
            if (t != null) OpenTile(t);
        }

        void PcList_KeyDown(object sender, KeyEventArgs e)
        {
            var t = PcList.SelectedItem as DriveTile;
            if (e.Key == Key.Enter && t != null) { OpenTile(t); e.Handled = true; }
            else if ((e.Key == Key.Apps || (e.Key == Key.System && e.SystemKey == Key.F10)) || (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift))
            {
                e.Handled = true;
                var c = t == null ? null : PcList.ItemContainerGenerator.ContainerFromItem(t) as ListBoxItem;
                Point at = c != null ? c.PointToScreen(new Point(24, c.ActualHeight - 8)) : PcList.PointToScreen(new Point(24, 24));
                if (t != null) ShowShellMenu(new List<string> { t.Path }, at, false);
                else ShowPcBackgroundMenu(null);
            }
        }

        void PcList_SelectionChanged(object sender, SelectionChangedEventArgs e) { UpdateStatus(); SchedulePreview(); }

        void PcList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // a click on the empty part clears the selection, like Explorer
            var src = e.OriginalSource as DependencyObject;
            if (TileAt(src) == null && ContainerOf<ScrollBar>(src) == null) { PcList.SelectedItem = null; PcList.Focus(); }
        }

        void PcList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DriveTile t = TileAt(e.OriginalSource as DependencyObject);
            PcList.SelectedItem = t;
            PcList.Focus();
        }

        void PcList_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var src = e.OriginalSource as DependencyObject;
            if (ContainerOf<ScrollBar>(src) != null) return;
            e.Handled = true;
            Point screen = PointToScreen(e.GetPosition(this));
            DriveTile t = TileAt(src);
            // a drive: Windows' own menu (Open, Eject, Format, Properties …)
            if (t != null) ShowShellMenu(new List<string> { t.Path }, screen, false);
            else ShowPcBackgroundMenu(screen);
        }

        /// <summary>The empty part of This PC: Refresh, Properties (Windows' About page), and Windows' own menu (Map network drive …).</summary>
        void ShowPcBackgroundMenu(Point? screen)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && screen != null) { ShowNativeBackgroundMenu(screen.Value); return; }
            var menu = new ContextMenu { PlacementTarget = PcList };
            if (screen == null) { menu.Placement = PlacementMode.Relative; menu.HorizontalOffset = 24; menu.VerticalOffset = 24; }
            else menu.Placement = PlacementMode.MousePoint;
            menu.Items.Add(Item("Refresh", "", "F5", Refresh));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Properties", "", "Alt+Enter", () => ShowProperties(new List<string> { ThisPcPath })));
            menu.Items.Add(new Separator());
            Point at = screen ?? PcList.PointToScreen(new Point(24, 24));
            menu.Items.Add(Item("Show more options", "", null, () => ShowNativeBackgroundMenu(at)));
            menu.IsOpen = true;
        }

        void PcList_DragOver(object sender, DragEventArgs e)
        {
            DriveTile t = TileAt(e.OriginalSource as DependencyObject);
            e.Effects = t == null ? DragDropEffects.None : DropEffect(e, t.Path);
            SetDropHighlight(e.Effects != DragDropEffects.None ? t : null);
            e.Handled = true;
        }

        void PcList_Drop(object sender, DragEventArgs e)
        {
            DriveTile t = TileAt(e.OriginalSource as DependencyObject);
            if (t == null) { ClearDropHighlight(); e.Effects = DragDropEffects.None; e.Handled = true; return; }
            DoDrop(e, t.Path);
        }

        string PcStatus()
        {
            int n = _pcTiles.Count;
            string text = n == 1 ? "1 item" : n + " items";
            var t = PcList.SelectedItem as DriveTile;
            if (t != null) text += "   ·   " + t.Name + (t.HasSpace ? "   ·   " + t.SpaceText : "");
            return text;
        }

        /// <summary>The preview pane on This PC: the selected drive's details, or This PC itself.</summary>
        void PreviewThisPc(int v, List<KeyValuePair<string, string>> props)
        {
            var t = PcList.SelectedItem as DriveTile;
            int px = (int)Math.Round(Math.Max(128, Math.Min(256, PreviewStage.ActualWidth)) * VisualTreeHelper.GetDpi(this).DpiScaleX);
            if (t == null)
            {
                PreviewName.Text = "This PC";
                PreviewKind.Text = Environment.MachineName;
                int drives = _pcTiles.Count(d => d.Section == DriveTile.Drives);
                props.Add(Prop("Drives", drives.ToString()));
                long total = _pcTiles.Where(d => d.Section == DriveTile.Drives && d.HasSpace).Sum(d => d.Total);
                long free = _pcTiles.Where(d => d.Section == DriveTile.Drives && d.HasSpace).Sum(d => d.Free);
                if (total > 0) props.Add(Prop("Space", Format.Size(free) + " free of " + Format.Size(total)));
                PreviewProps.ItemsSource = props;
                _icons.RequestThumb(ThisPcPath, px, () => v == _previewVersion, s => { if (v == _previewVersion) ShowImage(s); });
                return;
            }
            PreviewName.Text = t.Name;
            PreviewKind.Text = t.Kind;
            if (t.HasSpace)
            {
                props.Add(Prop("Used", Format.Size(t.Total - t.Free) + "  (" + Math.Round((t.Total - t.Free) * 100.0 / t.Total) + "%)"));
                props.Add(Prop("Free", Format.Size(t.Free)));
                props.Add(Prop("Capacity", Format.Size(t.Total)));
            }
            if (!string.IsNullOrEmpty(t.FileSystem)) props.Add(Prop("File system", t.FileSystem));
            if (t.Path.Length > 3) props.Add(Prop("Location", t.Path));
            PreviewProps.ItemsSource = props;
            _icons.RequestThumb(t.Path, px, () => v == _previewVersion, s => { if (v == _previewVersion) ShowImage(s); });
        }
    }
}
