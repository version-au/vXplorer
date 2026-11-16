using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using vXplorer.Core;
using DragEventArgs = System.Windows.DragEventArgs;

namespace vXplorer.Ui
{
    /// <summary>
    /// Tabs in the title strip. Each tab has its own folder and back / forward history; the list shows the active one, and a tab's
    /// selection and scroll position come back when you return to it. Tabs are remembered between runs.
    /// </summary>
    public partial class MainWindow
    {
        readonly ObservableCollection<TabState> _tabs = new ObservableCollection<TabState>();
        TabState _tab;

        /// <summary>The selection and scroll position to put back once the next load finishes (switching tabs).</summary>
        HashSet<string> _restoreSelected;
        double? _restoreScroll;

        Stack<string> _back { get { return _tab.Back; } }
        Stack<string> _forward { get { return _tab.Forward; } }

        static string HomeFolder { get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } }

        /// <summary>Where a new tab or window opens (Options > Tabs): Home, This PC, or the folder of the tab you're on.</summary>
        string NewTabPath()
        {
            if (_settings.NewTabOpens == "ThisPc") return ThisPcPath;
            if (_settings.NewTabOpens == "Same" && _current != null) return _current;
            return HomeFolder;
        }

        /// <summary>The tab strip: vSuite's TabStrip (the same in vPad and vTerm), kept in step with _tabs.</summary>
        readonly vSuite.Ui.TabStrip _strip = new vSuite.Ui.TabStrip { TabHeight = 34, Reserve = 56 };
        readonly Dictionary<TabState, StripTab> _stripTabs = new Dictionary<TabState, StripTab>();
        bool _syncing;

        void InitTabs()
        {
            var newBtn = new Button { Content = "", FontSize = 12, Width = 32, Height = 30, ToolTip = "New tab (Ctrl+T)", VerticalAlignment = VerticalAlignment.Center };
            newBtn.SetResourceReference(StyleProperty, "vx.IconButton");
            newBtn.Click += NewTab_Click;
            _strip.AddButton(newBtn);
            TabArea.Child = _strip;
            HookStrip();   // MainWindow.TabDrag.cs
            _tabs.CollectionChanged += (s, e) => { SyncStrip(); TouchSession(); };
            Loaded += (s, e) => TouchSession();
            Closed += (s, e) => TouchSession();   // the other windows' tabs (on the last one's close the session file is deleted)
        }

        /// <summary>A tab's StripTab (made the first time): its name, icon and folder follow the TabState.</summary>
        StripTab StripOf(TabState t)
        {
            StripTab st;
            if (_stripTabs.TryGetValue(t, out st)) return st;
            st = new StripTab(t, t.Title) { Icon = t.Icon, ToolTip = t.Path };
            st.Element.AllowDrop = true;    // files dragged onto a tab go into its folder
            st.Element.DragOver += Tab_DragOver;
            st.Element.Drop += Tab_Drop;
            st.Element.DragLeave += Tab_DragLeave;
            t.PropertyChanged += TabChanged;
            _stripTabs[t] = st;
            return st;
        }

        void TabChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            var t = sender as TabState;
            StripTab st;
            if (t == null || !_stripTabs.TryGetValue(t, out st)) return;
            switch (e.PropertyName)
            {
                case "Title": st.Title = t.Title; break;
                case "Path": st.ToolTip = t.Path; break;
                case "Icon": st.Icon = t.Icon; break;
                case "IsActive": if (t.IsActive && _tabs.Contains(t)) _strip.Select(st); break;
                case "IsDropTarget": st.DropHighlight = t.IsDropTarget; break;
            }
        }

        /// <summary>The strip shows _tabs, in its order (tabs gone are taken out, new ones put in).</summary>
        void SyncStrip()
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                foreach (TabState gone in _stripTabs.Keys.Where(k => !_tabs.Contains(k)).ToList())
                {
                    _strip.Remove(_stripTabs[gone]);
                    gone.PropertyChanged -= TabChanged;
                    _stripTabs.Remove(gone);
                }
                for (int i = 0; i < _tabs.Count; i++)
                {
                    StripTab st = StripOf(_tabs[i]);
                    int at = -1;
                    for (int k = 0; k < _strip.Tabs.Count; k++) if (_strip.Tabs[k] == st) { at = k; break; }
                    if (at < 0) _strip.Insert(st, i);
                    else if (at != i) _strip.Move(st, i);
                }
                if (_tab != null && _tabs.Contains(_tab)) _strip.Select(StripOf(_tab));
            }
            finally { _syncing = false; }
        }

        TabState StateOf(object sender)
        {
            foreach (var kv in _stripTabs) if (kv.Value.Element == sender) return kv.Key;
            return null;
        }

        /// <summary>The tabs from last time (or the folder on the command line), the remembered one shown.</summary>
        void RestoreTabs(string arg)
        {
            var paths = new List<string>();
            int active = 0;
            if (!string.IsNullOrWhiteSpace(arg)) paths.Add(StartFolder(arg));
            else if (_settings.StartWith == "Home") paths.Add(HomeFolder);       // Options > Starting up
            else if (_settings.StartWith == "ThisPc") paths.Add(ThisPcPath);
            else
            {
                for (int i = 0; i < _settings.Tabs.Count; i++)
                {
                    string c = NormalPlace(_settings.Tabs[i]);
                    if (c == null || !PlaceExists(c)) continue;
                    if (i == _settings.ActiveTab) active = paths.Count;
                    paths.Add(c);
                }
            }
            if (paths.Count == 0) paths.Add(StartFolder(null));
            foreach (string p in paths) _tabs.Add(NewTab(p));
            SwitchTo(_tabs[Math.Max(0, Math.Min(active, _tabs.Count - 1))]);
        }

        TabState NewTab(string path)
        {
            var t = new TabState { Path = path, Title = NameOf(path) };
            RequestTabIcon(t);
            return t;
        }

        void RequestTabIcon(TabState t)
        {
            string p = t.Path;
            _icons.Request(p, "node:" + p, null, img => { if (Same(t.Path, p)) t.Icon = img; });
        }

        /// <summary>A new tab after the current one; activate = show it now (middle-click opens it behind, like a browser).</summary>
        void OpenTab(string path, bool activate)
        {
            string p = NormalPlace(path);
            if (p == null || !PlaceExists(p)) { Status("vXplorer can't find " + path + "."); return; }
            TabState t = NewTab(p);
            int at = _tab == null ? _tabs.Count : _tabs.IndexOf(_tab) + 1;
            _tabs.Insert(at, t);
            if (activate) SwitchTo(t);
            else Status("Opened " + t.Title + " in a new tab.");
        }

        void SwitchTo(TabState tab)
        {
            if (tab == null || tab == _tab) return;
            if (_renaming != null) EndRename(true);
            if (_tab != null)
            {
                _tab.Selected = PathSet(SelectedPaths());
                ScrollViewer sv = ListScroller();
                _tab.Scroll = sv != null ? sv.VerticalOffset : 0;
                _tab.IsActive = false;
            }
            _tab = tab;
            tab.IsActive = true;
            _restoreSelected = tab.Selected;
            _restoreScroll = tab.Scroll;
            string path = tab.Path;
            if (!PlaceExists(path)) path = NearestExisting(path);
            Navigate(path, false);
            BringTabIntoView(tab);
        }

        static string NearestExisting(string path)
        {
            if (Paths.Away(path)) return HomeFolder;      // its network place isn't answering: every folder above it would be waited for too
            DirectoryInfo d = null;
            try { d = Directory.GetParent(path); } catch { }
            while (d != null && !Paths.FolderThere(d.FullName)) d = d.Parent;
            return d != null ? d.FullName : HomeFolder;
        }

        void CloseTab(TabState t)
        {
            if (t == null) return;
            if (_tabs.Count == 1) { Close(); return; }   // the last tab closes the window, like Explorer
            int i = _tabs.IndexOf(t);
            bool wasActive = t == _tab;
            _tabs.RemoveAt(i);
            if (wasActive)
            {
                _tab = null;
                SwitchTo(_tabs[Math.Min(i, _tabs.Count - 1)]);
            }
        }

        void CloseOtherTabs(TabState keep, bool onlyToTheRight)
        {
            int k = _tabs.IndexOf(keep);
            for (int i = _tabs.Count - 1; i >= 0; i--)
            {
                if (i == k || (onlyToTheRight && i < k)) continue;
                _tabs.RemoveAt(i);
            }
            if (!_tabs.Contains(_tab)) { _tab = null; SwitchTo(keep); }
        }

        void CycleTab(int step)
        {
            if (_tabs.Count < 2) return;
            int i = (_tabs.IndexOf(_tab) + step + _tabs.Count) % _tabs.Count;
            SwitchTo(_tabs[i]);
        }

        /// <summary>Ctrl+T / W / Tab / Shift+Tab / 1-9.</summary>
        bool HandleTabKeys(Key key, ModifierKeys mods)
        {
            if (mods == ModifierKeys.Control && key == Key.T) { OpenTab(NewTabPath(), true); return true; }
            if (mods == ModifierKeys.Control && key == Key.W) { CloseTab(_tab); return true; }
            if (mods == ModifierKeys.Control && key == Key.N) { NewWindow(); return true; }
            if (mods == ModifierKeys.Control && key == Key.OemComma) { ShowOptions(false); return true; }
            if (mods == ModifierKeys.Control && key == Key.Tab) { CycleTab(1); return true; }
            if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.Tab) { CycleTab(-1); return true; }
            if (mods == ModifierKeys.Control && key >= Key.D1 && key <= Key.D9)
            {
                int n = key - Key.D1;
                SwitchTo(key == Key.D9 ? _tabs[_tabs.Count - 1] : n < _tabs.Count ? _tabs[n] : null);
                return true;
            }
            return false;
        }

        /// <summary>Scroll the strip to the chosen tab (the strip does it as a tab is chosen).</summary>
        void BringTabIntoView(TabState t)
        {
            if (t != null && t == _tab && _tabs.Contains(t)) _strip.Select(StripOf(t));
        }

        void NewTab_Click(object sender, RoutedEventArgs e) { OpenTab(NewTabPath(), true); }

        /// <summary>A tab's right-click menu.</summary>
        void TabMenu(TabState t)
        {
            if (t == null) return;
            int i = _tabs.IndexOf(t);
            var menu = new ContextMenu { PlacementTarget = _strip, Placement = PlacementMode.MousePoint };
            menu.Items.Add(Item("New tab", "", "Ctrl+T", () => OpenTab(NewTabPath(), true)));
            if (!IsPlace(t.Path)) menu.Items.Add(PinItem(t.Path));
            menu.Items.Add(Item("Duplicate tab", "", null, () => { SwitchTo(t); OpenTab(t.Path, true); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Close tab", "", "Ctrl+W", () => CloseTab(t)));
            menu.Items.Add(Item("Close other tabs", null, null, () => CloseOtherTabs(t, false), _tabs.Count > 1));
            menu.Items.Add(Item("Close tabs to the right", null, null, () => CloseOtherTabs(t, true), i < _tabs.Count - 1));
            menu.IsOpen = true;
        }

        // ---- dragging files onto a tab: hover to show it, drop to put them in its folder ----

        readonly DispatcherTimer _tabHover = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        TabState _tabHoverTarget;

        void Tab_DragOver(object sender, DragEventArgs e)
        {
            TabState t = StateOf(sender);
            if (t == null) return;
            e.Effects = DropEffect(e, t.Path);
            SetDropHighlight(e.Effects != DragDropEffects.None ? t : null);
            e.Handled = true;
            if (t != _tab && t != _tabHoverTarget)
            {
                _tabHoverTarget = t;
                _tabHover.Stop();
                _tabHover.Tick -= TabHover_Tick;
                _tabHover.Tick += TabHover_Tick;
                _tabHover.Start();
            }
        }

        void TabHover_Tick(object sender, EventArgs e)
        {
            _tabHover.Stop();
            if (_tabHoverTarget != null) SwitchTo(_tabHoverTarget);
            _tabHoverTarget = null;
        }

        void Tab_DragLeave(object sender, DragEventArgs e)
        {
            _tabHover.Stop();
            _tabHoverTarget = null;
            ClearDropHighlight();
        }

        void Tab_Drop(object sender, DragEventArgs e)
        {
            _tabHover.Stop();
            _tabHoverTarget = null;
            TabState t = StateOf(sender);
            if (t == null) return;
            e.Handled = true;
            DoDrop(e, t.Path);
        }

        /// <summary>Middle-click: a folder (list or sidebar) opens in a new tab; a tab closes.</summary>
        bool HandleMiddleClick(MouseButtonEventArgs e)
        {
            object dc = (e.OriginalSource as FrameworkElement)?.DataContext ?? (e.OriginalSource as FrameworkContentElement)?.DataContext;
            var tab = dc as TabState;
            if (tab != null) { CloseTab(tab); return true; }
            var f = dc as FileItem;
            if (f != null && f.IsFolder) { OpenTab(f.Path, false); return true; }
            var n = dc as FolderNode;
            if (n != null && !n.IsPlaceholder) { OpenTab(n.Path, false); return true; }
            var d = dc as DriveTile;
            if (d != null && Directory.Exists(d.Path)) { OpenTab(d.Path, false); return true; }
            return false;
        }
    }
}
