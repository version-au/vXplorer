using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Vanara.PInvoke;
using Vanara.Windows.Shell;
using vXplorer.Core;

namespace vXplorer.Ui
{
    public partial class MainWindow : Window, IThemedWindow
    {
        readonly AppSettings _settings;
        readonly VShellThemeSource _themeSource;
        readonly ShellIcons _icons;
        IntPtr _hwnd;

        string _current;
        /// <summary>What the list shows: the folder's contents, or search results as they're found.</summary>
        ObservableCollection<FileItem> _items = new ObservableCollection<FileItem>();
        volatile int _loadVersion;

        FolderWatcher _watcher;
        /// <summary>The folder the list shows (null after an error) - a refresh of the same one keeps its items' icons.</summary>
        string _listPath;
        readonly DispatcherTimer _reloadTimer;
        FolderNode _selectedNode;

        /// <summary>The columns (and sort keys), in the order they're shown. The first four are the standard ones.</summary>
        static readonly string[] SortKeys = { "Name", "Date", "Type", "Size", "Created", "Dimensions", "Length" };
        static readonly string[] Headers = { "Name", "Date modified", "Type", "Size", "Date created", "Dimensions", "Length" };

        /// <summary>A tab dragged out of another window: this window opens with just it.</summary>
        readonly TabState _adopt;
        Action _themeChanged;

        public MainWindow(AppSettings settings, VShellThemeSource themeSource, string startPath, TabState adopt = null)
        {
            _adopt = adopt;
            _settings = settings;
            _themeSource = themeSource;
            InitializeComponent();
            _icons = new ShellIcons(Dispatcher);
            _ops = new FileOps(this) { OwnProgress = !_settings.WindowsCopyDialog };
            DockPanel.SetDock(OpsPanel.Chip, Dock.Right);   // the operations panel, folded away, sits in the status bar
            StatusBar.Children.Insert(1, OpsPanel.Chip);
            _ops.Starting += ReleasePreview;   // the preview handler lets go of the file first, so it isn't "in use"
            InitSearch();
            InitAddressSuggest();
            InitTabs();
            ClaimSidebarIcons();
            SetUpThisPcRow();

            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 84,   // the tab strip and the toolbar row: grab anywhere that isn't a button
                ResizeBorderThickness = new Thickness(6),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });
            RestorePlacement();
            SideCol.Width = new GridLength(_settings.SidebarWidth);

            _reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _reloadTimer.Tick += (s, e) => { _reloadTimer.Stop(); Refresh(); };

            SourceInitialized += (s, e) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                _icons.PixelSize = IconPixels();
                HwndSource.FromHwnd(_hwnd).AddHook(WndProc);
                HookClipboard();   // the command bar's Paste follows the clipboard
                ApplyTheme();
            };
            StateChanged += (s, e) => UpdateMaximized();
            DpiChanged += (s, e) => { _icons.PixelSize = IconPixels(); _icons.Clear(); };
            ApplyLook();
            // the update notice, over the file list (not the preview pane, whose Windows previewers would draw over it)
            var notice = new UpdateNotice();
            Grid.SetRow(notice, 1);
            Grid.SetColumn(notice, 2);
            ContentGrid.Children.Add(notice);
            Loaded += (s, e) =>
            {
                BuildSidebar();
                UpdateSortHeaders();
                InitViews();
                if (_adopt != null) { _tabs.Add(_adopt); SwitchTo(_adopt); }
                else RestoreTabs(startPath);
                Files.Focus();
            };
            Closing += OnClosing;
            PreviewKeyDown += OnPreviewKeyDown;
            PreviewMouseUp += OnPreviewMouseUp;
            PreviewMouseDown += OnPreviewMouseDown;
            Deactivated += (s, e) => { CancelSlowClick(); if (_renaming != null) EndRename(true); };
            Activated += (s, e) => { _activatedAt = Environment.TickCount; OpsWindow.Host = this; };
            _themeChanged = () => Dispatcher.BeginInvoke(new Action(ApplyTheme));
            _themeSource.Changed += _themeChanged;
        }

        /// <summary>The sidebar's folders (shared code) load their icons through this window's icon loader.</summary>
        void ClaimSidebarIcons()
        {
            ShellIcons icons = _icons;
            // a plain folder on a network drive shares the standard folder icon (see ShellIcons.NodeKey)
            FolderNode.RequestIcon = n => icons.Request(n.Path, ShellIcons.NodeKey(n.Path), null, img => n.Icon = img);
        }

        // =================================================================
        // Theme
        // =================================================================

        Theme _theme;
        /// <summary>The window's icons in the theme's colours (sharp taskbar icon, vShell's live icon, the Instance property).</summary>
        WindowIcons _windowIcons;

        void ApplyTheme()
        {
            Theme t = Theme.From(_themeSource.Current);
            _theme = t;
            if (PreviewWin.Active) SchedulePreview();   // the preview handler takes the new colours on a reload
            bool blur = _settings.ThemeOpacity;   // see-through at vShell's opacity (Options > Look), else solid
            Log.Write("Theme: found=" + _themeSource.Found + " " + _themeSource.Current.Signature + " blur=" + blur);
            t.Apply(Application.Current.Resources, blur);
            WindowEffects.Look(this, t, blur);   // Blur / Frosted: the blurred wallpaper under the theme colour, as vShell's bars
            _blur = blur;
            if (OptionsWindow.Current != null && OptionsWindow.Current.Owner == this) OptionsWindow.Current.ApplyTheme(t, blur);
            OpsWindow.ThemeChanged(this);   // the popped-out file operations panel
            // the icon (taskbar, Alt+Tab, the tab strip's mark) in the theme's colours
            if (_windowIcons == null) _windowIcons = new WindowIcons(this);
            _windowIcons.Apply(t);
            try { AppMark.Source = AppIcon.Image(t, true); }
            catch (Exception ex) { Log.Write("Icon: " + ex.Message); }
            UpdateStatus();
        }
        int IconPixels()
        {
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            return Math.Max(16, (int)Math.Round(16 * scale));
        }

        // =================================================================
        // Window: placement, maximise, caption buttons
        // =================================================================

        void RestorePlacement()
        {
            if (_settings.Width >= MinWidth && _settings.Height >= MinHeight)
            {
                var r = new Rect(_settings.Left, _settings.Top, _settings.Width, _settings.Height);
                var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                      SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                if (screen.IntersectsWith(r))
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = r.Left; Top = r.Top; Width = r.Width; Height = r.Height;
                    if (_settings.Maximized) WindowState = WindowState.Maximized;
                    return;
                }
            }
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        void UpdateMaximized()
        {
            bool max = WindowState == WindowState.Maximized;
            // a borderless window maximised hangs off the screen by its frame: keep the content on screen
            Root.Margin = max ? new Thickness(WindowEffects.MaximizedOverhang(_hwnd)) : new Thickness(0);
            MaxBtn.Content = max ? "\uE923" : "\uE922";
            MaxBtn.ToolTip = max ? "Restore" : "Maximise";
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            if (!MayClose()) { e.Cancel = true; return; }   // a copy still running: ask first
            OpsPanel.Detach();
            if (!Application.Current.Windows.OfType<MainWindow>().Any(w => w != this && w.IsLoaded)) OpsWindow.CloseForGood();   // the last window: the popped-out panel goes too
            Rect r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _settings.Left = r.Left; _settings.Top = r.Top; _settings.Width = r.Width; _settings.Height = r.Height;
            _settings.Maximized = WindowState == WindowState.Maximized;
            _settings.SidebarWidth = SideCol.ActualWidth;
            if (_settings.PreviewPane && PreviewCol.ActualWidth >= 200) _settings.PreviewWidth = PreviewCol.ActualWidth;
            if (_current != null) _settings.LastFolder = _current;
            _settings.Tabs = _tabs.Select(t => t.Path).ToList();
            _settings.ActiveTab = Math.Max(0, _tabs.IndexOf(_tab));
            _settings.Save();
            if (_watcher != null) _watcher.Dispose();
            StopSizeCount();
            PreviewWin.Shutdown();
            _icons.Dispose();
            _themeSource.Changed -= _themeChanged;
            // other vXplorer windows (tabs dragged out) carry on: the sidebar's icons go through one of them now
            MainWindow other = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => w != this && w.IsLoaded);
            if (other != null) other.ClaimSidebarIcons();
        }

        void Minimize_Click(object sender, RoutedEventArgs e) { WindowState = WindowState.Minimized; }
        void Maximize_Click(object sender, RoutedEventArgs e) { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }
        void Close_Click(object sender, RoutedEventArgs e) { Close(); }

        // =================================================================
        // Navigation
        // =================================================================

        string StartFolder(string arg)
        {
            foreach (string p in new[] { arg, _settings.LastFolder })
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                string full = NormalPlace(p);   // a folder, This PC, Network, a computer, or inside a zip
                if (full != null && PlaceExists(full)) return full;
            }
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        void Navigate(string path, bool record = true)
        {
            CancelSlowClick();
            string asked = path;
            path = NormalPlace(path);
            if (path == null || !PlaceExists(path)) { Status("vXplorer can't find " + (path ?? asked) + "."); return; }
            if (record && _current != null && !Same(_current, path))
            {
                _back.Push(_current);
                _forward.Clear();
            }
            StopSearch();   // a new folder: back to its contents
            _current = path;
            _inZip = ZipFolders.IsZipPath(path);
            _settings.LastFolder = path;
            string name = NameOf(path);
            Title = name + " - vXplorer";
            if (!Same(_tab.Path, path) || _tab.Icon == null) { _tab.Path = path; RequestTabIcon(_tab); }
            _tab.Title = name;
            BuildCrumbs();
            UpdateSearchHint();
            UpdateNavButtons();
            ApplyFolderView(path);   // its own view and sort (or the defaults)
            Watch(path);
            Load(null, false);
            RevealInSidebar(path);
            TouchSession();   // the session file (for after a crash) follows the tabs
        }

        void UpdateNavButtons()
        {
            BackBtn.IsEnabled = _back.Count > 0 || Searching;
            ForwardBtn.IsEnabled = _forward.Count > 0;
            UpBtn.IsEnabled = _current != null && !InThisPc && !IsNetwork(_current);   // a drive's Up is This PC, a computer's is Network
        }

        void GoBack()
        {
            if (Searching) { ClearSearch(); return; }   // Back from the results: the folder they were searched in
            while (_back.Count > 0)
            {
                string p = _back.Pop();
                if (!PlaceExists(p)) continue;
                _forward.Push(_current);
                Navigate(p, false);
                return;
            }
            UpdateNavButtons();
        }

        void GoForward()
        {
            while (_forward.Count > 0)
            {
                string p = _forward.Pop();
                if (!PlaceExists(p)) continue;
                _back.Push(_current);
                Navigate(p, false);
                return;
            }
            UpdateNavButtons();
        }

        void GoUp()
        {
            if (_current == null || InThisPc || IsNetwork(_current)) return;
            // the network: a share's root goes up to its computer, a computer to Network
            if (IsServer(_current) || NetworkPlaces.IsShareRoot(_current))
            {
                string up = IsServer(_current) ? NetworkPlaces.NetworkPath : NetworkPlaces.ServerPath(_current.Substring(0, _current.TrimEnd('\\').LastIndexOf('\\')));
                _selectNext = PathSet(new[] { NetworkPlaces.ServerPath(_current) });
                Navigate(up);
                return;
            }
            DirectoryInfo parent = Directory.GetParent(_current);
            if (parent == null)
            {
                // a drive (or a network share's root): up is This PC, with the drive selected
                _pcSelectNext = _current;
                Navigate(ThisPcPath);
                return;
            }
            _selectNext = PathSet(new[] { _current });   // land on the folder we came out of, like Explorer
            Navigate(parent.FullName);
        }

        void Back_Click(object sender, RoutedEventArgs e) { GoBack(); }
        void Forward_Click(object sender, RoutedEventArgs e) { GoForward(); }
        void Up_Click(object sender, RoutedEventArgs e) { GoUp(); }
        void Refresh_Click(object sender, RoutedEventArgs e) { Refresh(); }

        // =================================================================
        // The file list
        // =================================================================

        async void Load(HashSet<string> keepSelected, bool keepScroll)
        {
            if (InThisPc)
            {
                string drive = _pcSelectNext ?? (PcList.SelectedItem as DriveTile)?.Path;
                _pcSelectNext = null;
                _restoreSelected = null;
                _restoreScroll = null;
                LoadThisPc(drive);
                return;
            }
            ShowPcView(false);
            int v = ++_loadVersion;
            if (IsNetwork(_current) || IsServer(_current)) { LoadNetworkList(v); return; }
            if (_inZip) { LoadZipList(v); return; }
            string path = _current;
            bool hidden = _settings.ShowHidden;
            double offset = keepScroll ? ListScroller()?.VerticalOffset ?? 0 : 0;
            if (!keepScroll) Status("Loading…");

            List<FileItem> list;
            string error = null;
            try { list = await Task.Run(() => FolderReader.Read(path, hidden)); }
            catch (UnauthorizedAccessException) { list = new List<FileItem>(); error = "You don't have permission to open this folder."; }
            catch (Exception ex) { list = new List<FileItem>(); error = "vXplorer can't open this folder: " + ex.Message; }
            if (v != _loadVersion) return;   // navigated away meanwhile

            if (_restoreScroll != null)   // back to a tab: its selection and scroll position
            {
                keepSelected = _restoreSelected;
                offset = _restoreScroll.Value;
                keepScroll = true;
                _restoreSelected = null;
                _restoreScroll = null;
            }

            if (_cutPaths.Count > 0)
                foreach (FileItem it in list) it.IsCut = _cutPaths.Contains(it.Path);
            // the same folder read again (something changed, F5): unchanged items keep the icons and thumbnails they had, so the list
            // doesn't blank them and load them again (they flashed)
            if (string.Equals(_listPath, path, StringComparison.OrdinalIgnoreCase))
            {
                var before = new Dictionary<string, FileItem>(StringComparer.OrdinalIgnoreCase);
                foreach (FileItem old in _items) before[old.Path] = old;
                foreach (FileItem it in list)
                {
                    FileItem old;
                    if (!before.TryGetValue(it.Path, out old) || old.IconKey != it.IconKey || old.Modified != it.Modified) continue;
                    it.Icon = old.Icon;
                    if (old.Thumb != null) { it.Thumb = old.Thumb; it.ThumbPx = old.ThumbPx; }
                }
            }
            _listPath = error == null ? path : null;
            StopSizeCount();   // read again: a selected folder's size is counted again too
            _items = new ObservableCollection<FileItem>(list);
            ShowItems();
            EmptyText.Text = error ?? "This folder is empty.";
            EmptyText.Visibility = list.Count == 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            // what to select: items an operation just made (scrolled to), or what was selected before a refresh
            HashSet<string> select = _selectNext ?? keepSelected;
            bool scrollTo = _selectNext != null;
            _selectNext = null;
            string rename = _renameNext;
            _renameNext = null;
            if (select != null)
            {
                // one status update for the lot (below), not one per item - 20,000 selected files froze the window for seconds
                _selecting = true;
                try
                {
                    foreach (FileItem it in _items)
                        if (select.Contains(it.Path)) Files.SelectedItems.Add(it);
                }
                finally { _selecting = false; }
                if (scrollTo && Files.SelectedItem != null)
                {
                    Files.ScrollIntoView(Files.SelectedItem);
                    FocusSelected();
                    keepScroll = false;
                }
            }
            if (rename != null)
            {
                FileItem r = _items.FirstOrDefault(it => Same(it.Path, rename));
                if (r != null) BeginRename(r);
            }
            if (keepScroll)
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { ScrollViewer sv = ListScroller(); if (sv != null) sv.ScrollToVerticalOffset(offset); }));
            UpdateStatus();
            SchedulePreview();

            // in the order they're shown (sorted, grouped), not the order the folder was read: the rows at the top get theirs first
            foreach (FileItem it in _items.ToList())
            {
                FileItem item = it;
                if (item.Icon != null) continue;   // kept from before the refresh
                _icons.Request(item.Path, item.IconKey, () => v == _loadVersion, img => item.Icon = img);
            }
            RequestProps();   // Dimensions / Length, when those columns show
        }

        /// <summary>Read the folder again, keeping the selection and scroll position (F5, or something changed on disk).</summary>
        void Refresh()
        {
            if (_current == null) return;
            if (_renaming != null) { _refreshPending = true; return; }   // reloading would throw the typing away
            if (InPlace) { Load(null, false); return; }   // This PC, Network, a computer: look again
            // (on the network the answer is waited for a few seconds at most - Paths.FolderThere)
            bool there = FolderThere(_current);
            if (Searching && there) { RerunSearch(); return; }
            if (!there)
            {
                // its network place isn't answering just now: what's shown stays (a refresh later, or going there again, looks again)
                if (Away(_current)) { Status(NetworkRoot(_current) + " isn't answering - showing what was there."); return; }
                // the folder went away: go to the nearest one that's still there
                DirectoryInfo d = Directory.GetParent(_current);
                while (d != null && !FolderThere(d.FullName)) d = d.Parent;
                if (d != null) Navigate(d.FullName, false);
                return;
            }
            var keep = new HashSet<string>(Files.SelectedItems.Cast<FileItem>().Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
            Load(keep, true);
        }

        /// <summary>The list's order: folders first, then the sort column, then the name (Explorer's natural order).</summary>
        int CompareItems(FileItem a, FileItem b)
        {
            if (_settings.FoldersFirst && a.IsFolder != b.IsFolder) return a.IsFolder ? -1 : 1;   // folders first, either way round
            string key = _settings.SortKey;
            int c = 0;
            if (key == "Date") c = a.Modified.CompareTo(b.Modified);
            else if (key == "Type") c = string.Compare(a.TypeName, b.TypeName, StringComparison.CurrentCultureIgnoreCase);
            else if (key == "Size") c = a.Size.CompareTo(b.Size);
            else if (key == "Created") c = a.Created.CompareTo(b.Created);
            else if (key == "Dimensions") c = a.Pixels.CompareTo(b.Pixels);
            else if (key == "Length") c = a.Length.CompareTo(b.Length);
            if (c == 0) c = Native.CompareNatural(a.Name, b.Name);
            if (c == 0) c = Native.CompareNatural(a.Path, b.Path);   // search results can share a name
            return _settings.SortDescending ? -c : c;
        }

        void ShowItems()
        {
            List<FileItem> sorted = _items.ToList();
            bool grouped = Grouped;
            if (grouped)
            {
                SetGroups(sorted);
                sorted.Sort((a, b) => a.GroupOrder != b.GroupOrder ? a.GroupOrder.CompareTo(b.GroupOrder) : CompareItems(a, b));
            }
            else sorted.Sort(CompareItems);
            _items = new ObservableCollection<FileItem>(sorted);
            _listed = new HashSet<string>(sorted.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
            Files.ItemsSource = _items;
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_items);
            view.GroupDescriptions.Clear();
            if (grouped) view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("Group"));
        }

        void Header_Click(object sender, RoutedEventArgs e)
        {
            var header = e.OriginalSource as GridViewColumnHeader;
            if (header == null || header.Column == null) return;
            string key;
            if (!_colKey.TryGetValue(header.Column, out key)) return;   // the search's Folder column doesn't sort
            if (_settings.SortKey == key) _settings.SortDescending = !_settings.SortDescending;
            else { _settings.SortKey = key; _settings.SortDescending = key == "Date" || key == "Size" || key == "Created" || key == "Dimensions" || key == "Length"; }
            UpdateSortHeaders();
            RememberFolderView();
            var keep = Files.SelectedItems.Cast<FileItem>().ToList();
            ShowItems();
            _selecting = true;
            try { foreach (FileItem f in keep) Files.SelectedItems.Add(f); }
            finally { _selecting = false; }
            UpdateStatus();
            SchedulePreview();
        }

        /// <summary>Each column's header: its name, and the arrow on the one the list is sorted by.</summary>
        void UpdateSortHeaders()
        {
            foreach (KeyValuePair<GridViewColumn, string> c in _colKey)
            {
                string name = Headers[Array.IndexOf(SortKeys, c.Value)];
                c.Key.Header = c.Value == _settings.SortKey ? name + (_settings.SortDescending ? "  \u02C5" : "  \u02C4") : name;
            }
        }

        void Files_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            CancelSlowClick();   // a double-click opens - it isn't the "click again to rename"
            if (e.ChangedButton != MouseButton.Left || _settings.SingleClick) return;   // single-click: the first click opened it
            FileItem item = ItemAt(e.OriginalSource as DependencyObject);
            if (item != null) Open(new[] { item });
        }

        void Files_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Open(Files.SelectedItems.Cast<FileItem>().ToList());
                e.Handled = true;
            }
        }

        /// <summary>A selection is being put back item by item: the status line and preview follow once, at the end.</summary>
        bool _selecting;

        void Files_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_banding || _selecting) return;   // the selection box updates the status once per move, not per item
            UpdateStatus();
            SchedulePreview();
        }

        /// <summary>Open the items: a single folder is opened here, files (and folders picked with them) with their programs.</summary>
        void Open(IList<FileItem> items)
        {
            if (items.Count == 0) return;
            // a zip: look inside it like a folder, as Explorer does; files inside one: taken out to open them
            if (items.Count == 1 && !items[0].IsFolder && !_inZip && ZipFolders.IsZipFile(items[0].Path)) { Navigate(items[0].Path); return; }
            if (_inZip && !(items.Count == 1 && items[0].IsFolder)) { OpenFromZip(items.Where(i => !i.IsFolder).ToList()); return; }
            if (items.Count == 1 && items[0].IsFolder)
            {
                if (_settings.FoldersInNewTab) OpenTab(items[0].Path, true);   // Options > Opening
                else Navigate(items[0].Path);
                return;
            }
            foreach (FileItem f in items.Take(15))
            {
                try { Process.Start(new ProcessStartInfo(f.Path) { UseShellExecute = true, WorkingDirectory = _current }); }
                catch (Win32Exception ex) { if (ex.NativeErrorCode != 1223) Status("Couldn't open " + f.Name + ": " + ex.Message); }   // 1223 = cancelled
                catch (Exception ex) { Status("Couldn't open " + f.Name + ": " + ex.Message); }
            }
        }

        FileItem ItemAt(DependencyObject d)
        {
            var container = d == null ? null : ItemsControl.ContainerFromElement(Files, d) as ListViewItem;
            return container == null ? null : container.Content as FileItem;
        }

        ScrollViewer ListScroller() { return FindChild<ScrollViewer>(Files); }

        void FocusSelected()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                var c = Files.ItemContainerGenerator.ContainerFromItem(Files.SelectedItem) as ListViewItem;
                if (c != null) c.Focus();
            }));
        }

        static T FindChild<T>(DependencyObject d) where T : DependencyObject
        {
            if (d == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                DependencyObject c = VisualTreeHelper.GetChild(d, i);
                var t = c as T;
                if (t != null) return t;
                t = FindChild<T>(c);
                if (t != null) return t;
            }
            return null;
        }

        // =================================================================
        // Live updates of the open folder
        // =================================================================

        void Watch(string path)
        {
            if (_watcher != null) { _watcher.Dispose(); _watcher = null; }
            if (IsPlace(path)) return;   // drives coming and going: WM_DEVICECHANGE (WndProc); the network: F5
            try { _watcher = new FolderWatcher(path, () => _settings.ShowHidden, () => _listed, () => Dispatcher.BeginInvoke(new Action(Changed))); }
            catch (Exception ex) { Log.Write("Watch " + path + ": " + ex.Message); }
        }

        /// <summary>The paths in the list (a fresh set each time it's filled - the watcher's thread reads it).</summary>
        volatile HashSet<string> _listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Changed()
        {
            if (Searching || InPlace) return;   // search results come from all the subfolders: F5 searches again
            // a copy writes many times: refresh once things go quiet
            _reloadTimer.Stop();
            _reloadTimer.Start();
        }

        // =================================================================
        // Address bar: breadcrumbs, or type a path
        // =================================================================

        void BuildCrumbs()
        {
            Crumbs.Children.Clear();
            var parts = new List<string>();
            if (_current.StartsWith(@"\\"))
            {
                // the network: Network > computer > share > folders …
                string[] segs = _current.TrimEnd('\\').Substring(2).Split('\\');
                string acc = @"\\" + segs[0];
                parts.Add(acc);
                for (int s = 1; s < segs.Length; s++) { acc += "\\" + segs[s]; parts.Add(acc); }
            }
            else if (!InPlace || _inZip)   // (a zip's folders: the same, the zip itself is one of the parts)
                for (DirectoryInfo d = new DirectoryInfo(_current); d != null; d = d.Parent) parts.Insert(0, d.FullName);
            // a drive's folders start from This PC, as in Explorer; the network's from Network
            if (InThisPc || (parts.Count > 0 && parts[0].Length <= 3)) parts.Insert(0, ThisPcPath);
            else if (IsNetwork(_current) || _current.StartsWith(@"\\")) parts.Insert(0, NetworkPlaces.NetworkPath);
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) Crumbs.Children.Add(CrumbArrow(parts[i - 1], parts[i]));
                string p = parts[i];
                var b = new Button
                {
                    Style = (Style)FindResource("vx.CrumbButton"),
                    Content = IsPlace(p) ? NameOf(p) : p.Length <= 3 ? Native.DisplayName(p) : p.Substring(p.TrimEnd('\\').LastIndexOf('\\') + 1),
                    ToolTip = IsThisPc(p) || IsNetwork(p) ? NameOf(p) : p
                };
                b.Click += (s, e) => Navigate(p);
                Crumbs.Children.Add(b);
            }
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => CrumbScroll.ScrollToRightEnd()));
        }

        /// <summary>The arrow after a crumb: click it for a menu of that place's subfolders (the one on the path in bold), as in Explorer.</summary>
        Button CrumbArrow(string parent, string next)
        {
            var b = new Button { Style = (Style)FindResource("vx.CrumbArrowButton"), ToolTip = "Folders in " + NameOf(parent) };
            b.Click += async (s, e) =>
            {
                b.Content = "";   // pointing down while its menu is open
                List<Tuple<string, string>> subs = await Task.Run(() => SubPlaces(parent));
                var menu = new ContextMenu { PlacementTarget = b, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
                bool open = true;   // read by the icon thread (the menu itself belongs to this one)
                menu.Closed += (o, a) => { b.Content = ""; open = false; };
                foreach (var sub in subs)
                {
                    string path = sub.Item2;
                    var img = new Image { Width = 16, Height = 16 };
                    var mi = new MenuItem { Header = sub.Item1, Icon = img };
                    if (Same(path.TrimEnd('\\'), next.TrimEnd('\\'))) mi.FontWeight = FontWeights.SemiBold;
                    mi.Click += (o, a) => Navigate(path);
                    menu.Items.Add(mi);
                    _icons.Request(path, "node:" + path, () => open, i => img.Source = i);
                }
                if (subs.Count == 0) menu.Items.Add(new MenuItem { Header = "(no folders)", IsEnabled = false });
                menu.IsOpen = true;
            };
            return b;
        }

        /// <summary>What's under a place for the address bar's arrows, as (name, path): drives, computers, shares or subfolders.</summary>
        List<Tuple<string, string>> SubPlaces(string p)
        {
            var r = new List<Tuple<string, string>>();
            try
            {
                if (IsThisPc(p))
                {
                    foreach (DriveInfo d in DriveInfo.GetDrives())
                        try { if (d.IsReady || d.DriveType == DriveType.Network) r.Add(Tuple.Create(Native.DisplayName(d.Name), d.Name)); } catch { }
                    return r;
                }
                if (IsNetwork(p)) return NetworkPlaces.KnownComputers();   // no network search just for a menu
                if (IsServer(p)) return NetworkPlaces.Shares(p);
                var opts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = _settings.ShowHidden ? 0 : FileAttributes.Hidden | FileAttributes.System };
                foreach (string d in Directory.EnumerateDirectories(p, "*", opts)) r.Add(Tuple.Create(Path.GetFileName(d), d));
                r.Sort((a, b) => Native.CompareNatural(a.Item1, b.Item1));
            }
            catch (Exception ex) { Log.Write("Crumb menu " + p + ": " + ex.Message); }
            return r;
        }

        /// <summary>
        /// A click anywhere in the address bar but on a folder's name types a path (the crumbs' scroller used to swallow clicks on the
        /// empty part, so this is the Preview event). A folder's name still goes there.
        /// </summary>
        void AddressCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (AddressBox.Visibility == System.Windows.Visibility.Visible) return;
            if (ContainerOf<Button>(e.OriginalSource as DependencyObject) != null) return;   // a crumb
            BeginEditAddress();
            e.Handled = true;
        }

        /// <summary>The list under the address bar while a path is typed.</summary>
        AddressSuggest _suggest;

        void InitAddressSuggest()
        {
            _suggest = new AddressSuggest(AddressBox, AddressCard, _icons, () => _settings.ShowHidden, AddressPlaces, path =>
            {
                EndEditAddress();
                Files.Focus();
                Navigate(path);
            });
            LocationChanged += (s, e) => _suggest.Close();
            Deactivated += (s, e) => _suggest.Close();
        }

        /// <summary>What the address bar offers before a backslash is typed: This PC, Network, Quick access and the drives.</summary>
        List<Tuple<string, string>> AddressPlaces()
        {
            var r = new List<Tuple<string, string>> { Tuple.Create(NameOf(ThisPcPath), ThisPcPath), Tuple.Create(NameOf(NetworkPlaces.NetworkPath), NetworkPlaces.NetworkPath) };
            foreach (var tree in new[] { QuickTree, DriveTree })
                if (tree.ItemsSource is IEnumerable<FolderNode> nodes)
                    foreach (FolderNode n in nodes) r.Add(Tuple.Create(n.Name, n.Path));
            return r;
        }

        void BeginEditAddress()
        {
            CrumbScroll.Visibility = System.Windows.Visibility.Collapsed;
            AddressBox.Visibility = System.Windows.Visibility.Visible;
            AddressBox.Text = InThisPc || IsNetwork(_current) ? NameOf(_current) : _current ?? "";
            AddressBox.Focus();
            AddressBox.SelectAll();
        }

        void EndEditAddress()
        {
            _suggest.Close();
            AddressBox.Visibility = System.Windows.Visibility.Collapsed;
            CrumbScroll.Visibility = System.Windows.Visibility.Visible;
        }

        void AddressBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                EndEditAddress();
                Files.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                string text = AddressBox.Text;
                EndEditAddress();
                Files.Focus();
                GoTo(text);
            }
        }

        void AddressBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) { EndEditAddress(); }

        void GoTo(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (IsPlace(text)) { Navigate(text); return; }   // This PC, Network, \\computer
            string p = Clean(text);
            if (p != null && Directory.Exists(p)) { Navigate(p); return; }
            if (p != null && File.Exists(p))
            {
                Open(new[] { new FileItem { Name = Path.GetFileName(p), Path = p } });
                return;
            }
            Status("vXplorer can't find '" + text.Trim() + "'. Check the spelling and try again.");
        }

        // =================================================================
        // Sidebar
        // =================================================================

        void BuildSidebar()
        {
            var quick = new List<FolderNode>();
            foreach (string p in QuickFolders())
            {
                if (string.IsNullOrEmpty(p)) continue;
                // a pinned folder on the network is listed without asking the network (a NAS that's off held the window up at
                // start); it says so when it's opened and can't be found
                if (NetworkRoot(p) != null)
                {
                    string plain = Path.GetFileName(p.TrimEnd('\\'));
                    quick.Add(new FolderNode(FolderThere(p) ? Native.DisplayName(p) : plain.Length > 0 ? plain : p, p, 0));
                }
                else if (Directory.Exists(p)) quick.Add(new FolderNode(Native.DisplayName(p), p, 0));
            }
            QuickTree.ItemsSource = quick;
            if (NetTree.ItemsSource == null) BuildNetworkTree();   // kept across rebuilds: no new search for computers
            _sidebarSig = SidebarSignature();
            LoadDrives();
        }

        /// <summary>The standard Quick access folders (Options can change the list).</summary>
        public static List<string> DefaultQuickFolders()
        {
            return new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Native.DownloadsFolder(),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            }.Where(p => !string.IsNullOrEmpty(p)).ToList();
        }

        List<string> QuickFolders() { return _settings.QuickAccess ?? DefaultQuickFolders(); }

        /// <summary>What the sidebar was built from - Options only rebuild it (closing what's open) when this changes.</summary>
        string _sidebarSig;
        string SidebarSignature() { return string.Join("|", QuickFolders()) + "|net=" + _settings.SidebarNetwork; }

        async void LoadDrives()
        {
            // a disconnected network drive can take a while to answer: do it off the UI thread
            bool network = _settings.SidebarNetwork;   // Options > Sidebar
            List<Tuple<string, string>> drives = await Task.Run(() =>
            {
                var r = new List<Tuple<string, string>>();
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (d.DriveType == DriveType.Network && !network) continue;
                        if (!d.IsReady && d.DriveType != DriveType.Network) continue;
                        r.Add(Tuple.Create(Native.DisplayName(d.Name), d.Name));
                    }
                    catch { }
                }
                return r;
            });
            DriveTree.ItemsSource = drives.Select(t => new FolderNode(t.Item1, t.Item2, 0)).ToList();
            RevealInSidebar(_current);   // the drives come late: show where the open folder is now they're there
        }

        void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var node = e.NewValue as FolderNode;
            if (node == null || node.IsPlaceholder) return;
            if (_selectedNode != null && _selectedNode != node) _selectedNode.IsSelected = false;   // one selection across both trees
            _selectedNode = node;
            if (_current == null || !Same(node.Path, _current)) Navigate(node.Path);
        }

        void Tree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // clicking the already-selected folder again goes back to it (SelectedItemChanged doesn't fire)
            if (e.OriginalSource is FrameworkElement fe && fe.TemplatedParent is ToggleButton) return;
            var item = ContainerOf<TreeViewItem>(e.OriginalSource as DependencyObject);
            var node = item == null ? null : item.DataContext as FolderNode;
            if (node != null && node == _selectedNode && !node.IsPlaceholder && !Same(node.Path, _current)) Navigate(node.Path);
        }

        static T ContainerOf<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && !(d is T)) d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }

        // =================================================================
        // Keys, mouse buttons, status
        // =================================================================

        void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            CancelSlowClick();
            bool typing = Keyboard.FocusedElement is TextBox;   // the address bar or a rename
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            ModifierKeys mods = Keyboard.Modifiers;

            if (_strip.Pressing && key == Key.Escape) { _strip.CancelDrag(); e.Handled = true; }   // a tab drag: put it back
            else if (HandleTabKeys(key, mods) || HandleViewKeys(key, mods)) e.Handled = true;
            else if (mods == ModifierKeys.Alt && key == Key.Left) { GoBack(); e.Handled = true; }
            else if (mods == ModifierKeys.Alt && key == Key.Right) { GoForward(); e.Handled = true; }
            else if (mods == ModifierKeys.Alt && key == Key.Up) { GoUp(); e.Handled = true; }
            else if (mods == ModifierKeys.Alt && key == Key.D) { BeginEditAddress(); e.Handled = true; }
            else if (mods == ModifierKeys.Control && key == Key.L) { BeginEditAddress(); e.Handled = true; }
            else if (mods == ModifierKeys.None && key == Key.F4) { BeginEditAddress(); e.Handled = true; }
            else if ((mods == ModifierKeys.Control && (key == Key.F || key == Key.E)) || (mods == ModifierKeys.None && key == Key.F3)) { FocusSearch(); e.Handled = true; }
            else if (mods == ModifierKeys.None && key == Key.F5) { Refresh(); e.Handled = true; }
            else if (mods == ModifierKeys.Control && key == Key.H) { ToggleHidden(); e.Handled = true; }
            else if (!typing && mods == ModifierKeys.None && key == Key.Back) { GoBack(); e.Handled = true; }
            else if (!typing && mods == ModifierKeys.None && key == Key.Escape && Searching) { ClearSearch(); e.Handled = true; }
            else if (!typing && HandleFileKeys(key, mods)) e.Handled = true;
        }

        void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.XButton1) { GoBack(); e.Handled = true; }
            else if (e.ChangedButton == MouseButton.XButton2) { GoForward(); e.Handled = true; }
            else if (e.ChangedButton == MouseButton.Middle && HandleMiddleClick(e)) e.Handled = true;
        }

        /// <summary>A click anywhere outside the rename box finishes the rename (like Enter); outside the address bar, cancels typing there.</summary>
        void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) CancelSlowClick();
            if (_renaming != null && (_renameBox == null || !_renameBox.IsMouseOver)) EndRename(true);
            if (AddressBox.Visibility == System.Windows.Visibility.Visible && !AddressCard.IsMouseOver) EndEditAddress();
        }

        /// <summary>Clicks on the title bar / tab strip (non-client, so WPF doesn't see them) also finish a rename.</summary>
        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_NCLBUTTONDOWN = 0xA1, WM_NCRBUTTONDOWN = 0xA4, WM_DEVICECHANGE = 0x219, WM_SYSCOMMAND = 0x112, SC_KEYMENU = 0xF100;
            // vXplorer started again: open what it was given in a new window here (Instance.HandOff)
            if (msg == 0x4A /* WM_COPYDATA */)
            {
                string command = TestHooks.Received(lParam);   // tests only (VXPLORER_TEST_HOOKS=1)
                if (command != null)
                {
                    handled = true;
                    Dispatcher.BeginInvoke(new Action(() => RunTestCommand(command)));
                    return new IntPtr(1);
                }
                string folder = null;
                try { folder = Instance.Received(lParam); } catch { }
                if (folder != null && UpdateCenter.IsCommand(folder))
                {
                    // vShell's Settings: "update now" / "include pre-releases" - answered as handled, so the second start stops; the check runs after this message
                    handled = true;
                    Dispatcher.BeginInvoke(new Action(() => UpdateCenter.HandleCommand(folder)));
                    return new IntPtr(1);
                }
                else if (folder != null)
                {
                    handled = true;
                    Dispatcher.BeginInvoke(new Action(() => OpenWindowAt(folder)));
                    return new IntPtr(1);
                }
            }
            // Alt+Space: the window menu (Windows won't open it itself while the style is off on the glass - WindowEffects.SetSystemMenuStyle)
            if (msg == WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0) == SC_KEYMENU && lParam.ToInt64() == ' ')
            {
                handled = true;
                Point at = new Point(Left + 8, Top + 30);
                if (WindowState == WindowState.Maximized) at = new Point(8, 30);
                Dispatcher.BeginInvoke(new Action(() => SystemCommands.ShowSystemMenu(this, at)));
                return IntPtr.Zero;
            }
            // a USB drive or disc in / out: the sidebar's drives, and This PC if it's showing
            if (msg == WM_DEVICECHANGE && (wParam.ToInt64() == 0x8000 || wParam.ToInt64() == 0x8004))
                Dispatcher.BeginInvoke(new Action(() => { LoadDrives(); if (InThisPc) Refresh(); }));
            if ((msg == WM_NCLBUTTONDOWN || msg == WM_NCRBUTTONDOWN) && _renaming != null)
                Dispatcher.BeginInvoke(new Action(() => EndRename(true)));
            return IntPtr.Zero;
        }

        void ToggleHidden()
        {
            _settings.ShowHidden = !_settings.ShowHidden;
            Refresh();
            Status(_settings.ShowHidden ? "Showing hidden items (Ctrl+H hides them again)." : "Hidden items are hidden (Ctrl+H shows them).");
        }

        void UpdateStatus()
        {
            _messageUntil = DateTime.MinValue;   // whatever message was showing is replaced now
            UpdateCommandBar();
            int n = _items.Count;
            string text = n == 1 ? "1 item" : n.ToString("N0") + " items";
            if (Searching) text = SearchStatus();
            if (InThisPc)
            {
                StopSizeCount();
                StatusText.Text = PcStatus();
                StatusRight.Text = _themeSource.Found ? "" : "vShell not found - using its default theme";
                return;
            }
            int k = Files.SelectedItems.Count;
            if (k > 0)
            {
                text += "   ·   " + k.ToString("N0") + " selected";
                string size = SelectionSize(Files.SelectedItems.Cast<FileItem>().ToList());   // folders: counted in the background
                if (size.Length > 0) text += "  " + size;
            }
            else StopSizeCount();
            StatusText.Text = text;
            var notes = new List<string>();
            if (_settings.ShowHidden) notes.Add("Hidden items shown");
            if (!_themeSource.Found) notes.Add("vShell not found - using its default theme");
            StatusRight.Text = string.Join("   ·   ", notes);
        }

        /// <summary>A message in the status bar for a few seconds.</summary>
        void Status(string message)
        {
            StatusText.Text = message;
            _messageUntil = DateTime.Now.AddSeconds(5);   // a folder size being counted waits for it
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            t.Tick += (s, e) => { t.Stop(); if (StatusText.Text == message) UpdateStatus(); };
            t.Start();
        }
    }
}
