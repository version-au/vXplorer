using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>The menu on vXplorer's icon at the top left: New tab, New window, Options, About and Close; and applying Options.</summary>
    public partial class MainWindow
    {
        bool _blur;

        /// <summary>For Ask and Options: the theme the window is in, and whether the glass is on.</summary>
        public Theme CurrentTheme { get { return _theme; } }
        public bool Blurred { get { return _blur; } }

        void AppMenu_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = AppMenuBtn, Placement = PlacementMode.Bottom };
            menu.Items.Add(Item("New tab", "", "Ctrl+T", () => OpenTab(NewTabPath(), true)));
            menu.Items.Add(Item("New window", "", "Ctrl+N", NewWindow));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Options", "", "Ctrl+,", () => ShowOptions(false)));
            menu.Items.Add(Item("Check for updates", "", null, CheckForUpdates, !UpdateCenter.Busy && !UpdateCenter.Installing));
            menu.Items.Add(Item("About vXplorer", "", null, () => ShowOptions(true)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Close window", "", "Alt+F4", Close));
            menu.IsOpen = true;
        }

        /// <summary>The menu's Check for updates: the notice if there's a new version (even a skipped one), else a line in the status bar.</summary>
        void CheckForUpdates()
        {
            Status("Looking for a new version of vXplorer…");
            Action done = null;
            done = () => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (UpdateCenter.Busy) return;
                UpdateCenter.Changed -= done;
                if (UpdateCenter.LastError.Length > 0) Status("vXplorer couldn't check for updates: " + UpdateCenter.LastError);
                else if (UpdateCenter.Available == null) Status("vXplorer is up to date (" + Updater.CurrentText + ").");
                else Status("vXplorer " + UpdateCenter.VersionText(UpdateCenter.Available) + " is available.");
            }));
            UpdateCenter.Changed += done;
            UpdateCenter.CheckNow(true);
        }

        void ShowOptions(bool about)
        {
            OptionsWindow.Open(this, _settings, _themeSource, _theme, _blur, ApplyOptionsEverywhere, about);
        }

        /// <summary>A second window, a little down and right of this one, on what a new tab opens.</summary>
        void NewWindow() { OpenWindowAt(null); }

        /// <summary>A new window on folder (null / empty: what a new tab opens) - New window, or vXplorer started again.</summary>
        MainWindow OpenWindowAt(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) folder = NewTabPath();
            Rect r = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            var w = new MainWindow(_settings, _themeSource, folder);
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.WindowState = WindowState.Normal;
            w.Left = r.Left + 32;
            w.Top = r.Top + 32;
            w.Width = r.Width;
            w.Height = r.Height;
            w.Show();
            w.Activate();
            return w;
        }

        public void OpenFolderInTab(string path) { OpenTab(path, true); }

        /// <summary>Options > Look: row spacing, the list's text size, file name extensions.</summary>
        void ApplyLook()
        {
            double pad = _settings.RowSpacing == "Compact" ? 1.5 : _settings.RowSpacing == "Roomy" ? 7 : 4;
            Resources["vx.RowPad"] = new Thickness(0, pad, 0, pad);
            Resources["vx.ListFont"] = _settings.ListTextSize == "Small" ? 11.5 : _settings.ListTextSize == "Large" ? 14.0 : 12.5;
            FileItem.ShowExtensions = _settings.ShowExtensions;
            PreviewTextLimit = Math.Max(64, _settings.TextPreviewKB) * 1024;
        }

        // ---- Quick access: pin, unpin, move (the list lives in the settings) ----

        bool IsPinned(string path) { return path != null && QuickFolders().Any(p => Same(p.TrimEnd('\\'), path.TrimEnd('\\'))); }

        void SetQuick(List<string> list)
        {
            _settings.QuickAccess = list;
            _settings.Save();
            ApplyOptionsEverywhere();
            if (OptionsWindow.Current != null) OptionsWindow.Current.RefreshQuickList();
        }

        void PinQuick(string path)
        {
            if (path == null || IsPlace(path) || IsPinned(path)) return;
            var list = QuickFolders().ToList();
            list.Add(path);
            SetQuick(list);
            Status("Pinned " + Native.DisplayName(path) + " to Quick access.");
        }

        void UnpinQuick(string path)
        {
            var list = QuickFolders().Where(p => !Same(p.TrimEnd('\\'), path.TrimEnd('\\'))).ToList();
            SetQuick(list);
            Status("Unpinned " + Native.DisplayName(path) + " from Quick access.");
        }

        void MoveQuick(string path, int step)
        {
            var list = QuickFolders().ToList();
            int i = list.FindIndex(p => Same(p.TrimEnd('\\'), path.TrimEnd('\\')));
            int j = i + step;
            if (i < 0 || j < 0 || j >= list.Count) return;
            string p0 = list[i];
            list.RemoveAt(i);
            list.Insert(j, p0);
            SetQuick(list);
        }

        /// <summary>"Pin to Quick access" / "Unpin from Quick access" for a folder, for the menus.</summary>
        MenuItem PinItem(string path)
        {
            bool pinned = IsPinned(path);
            return Item(pinned ? "Unpin from Quick access" : "Pin to Quick access", pinned ? "" : "", null,
                        () => { if (pinned) UnpinQuick(path); else PinQuick(path); });
        }

        /// <summary>Right-click a Quick access folder in the sidebar: vXplorer's menu (open, move, unpin), with Windows' menu behind "Show more options".</summary>
        void ShowQuickMenu(FolderNode node, Point screen)
        {
            var list = QuickFolders();
            int i = list.FindIndex(p => Same(p.TrimEnd('\\'), node.Path.TrimEnd('\\')));
            var menu = new ContextMenu { Placement = PlacementMode.MousePoint, PlacementTarget = QuickTree };
            menu.Items.Add(Item("Open", null, null, () => Navigate(node.Path)));
            menu.Items.Add(Item("Open in new tab", "", null, () => OpenTab(node.Path, true)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Move up", "", null, () => MoveQuick(node.Path, -1), i > 0));
            menu.Items.Add(Item("Move down", "", null, () => MoveQuick(node.Path, 1), i >= 0 && i < list.Count - 1));
            menu.Items.Add(Item("Unpin from Quick access", "", null, () => UnpinQuick(node.Path)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Show more options", "", null, () => ShowShellMenu(new List<string> { node.Path }, screen, false)));
            menu.IsOpen = true;
        }

        /// <summary>Options changed: every vXplorer window shows them (list order, hidden items, the sidebar).</summary>
        void ApplyOptionsEverywhere()
        {
            foreach (MainWindow w in Application.Current.Windows.OfType<MainWindow>()) w.ApplyOptions();
            Operations.Raise();   // the file operations panel docked or popped out
        }

        void ApplyOptions()
        {
            if (_blur != _settings.ThemeOpacity) ApplyTheme();   // Use the theme's opacity changed
            ApplyLook();
            _ops.OwnProgress = !_settings.WindowsCopyDialog;
            if (SidebarSignature() != _sidebarSig) BuildSidebar();   // Quick access or network drives changed
            Refresh();
            if (_settings.SidebarFollows) RevealInSidebar(_current);
            UpdateStatus();
        }
    }
}
