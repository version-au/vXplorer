using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>
    /// vXplorer's Options (from the menu on vXplorer's icon at the top left): the suite's Options window (vSuite's OptionsWindowBase - the
    /// page list, cards, rows, switches and the Updates page) with vXplorer's pages. Every change is saved and applied to all open
    /// vXplorer windows at once - there's no OK button. One Options window at a time.
    /// </summary>
    public sealed class OptionsWindow : OptionsWindowBase
    {
        /// <summary>The open Options window, if there is one.</summary>
        public static OptionsWindow Current { get; private set; }

        const string PgGeneral = "General", PgLook = "Look", PgFiles = "Files", PgSidebar = "Sidebar", PgPreview = "Preview pane",
                     PgWindows = "Windows", PgAbout = "About";

        readonly AppSettings _s;
        readonly VShellThemeSource _themeSource;
        readonly Action _changed;

        /// <summary>Show Options (or bring the open one forward); about = open it on About.</summary>
        public static void Open(Window owner, AppSettings s, VShellThemeSource themeSource, Theme theme, bool blur, Action changed, bool about)
        {
            if (Current == null)
            {
                Current = new OptionsWindow(s, themeSource, theme, blur, changed) { Owner = owner };
                Current.Show();
            }
            else Current.Activate();
            if (about) Current.ShowPage(PgAbout);
        }

        OptionsWindow(AppSettings s, VShellThemeSource themeSource, Theme theme, bool blur, Action changed) : base(theme, blur)
        {
            _s = s;
            _themeSource = themeSource;
            _changed = changed;
            Closed += (o, e) => { if (Current == this) Current = null; };
            Finish();
        }

        // =================================================================
        // The pages
        // =================================================================

        protected override void BuildPages()
        {
            // ---- General ----
            StackPanel general = Page(PgGeneral, "", "Starting up, new tabs and how things open");
            StackPanel c = Card(general, "Starting up");
            Row(c, "When vXplorer opens", "What the first window shows", null, Segmented(
                Opt("Last tabs", _s.StartWith == "Restore", () => _s.StartWith = "Restore"),
                Opt("Home", _s.StartWith == "Home", () => _s.StartWith = "Home"),
                Opt("This PC", _s.StartWith == "ThisPc", () => _s.StartWith = "ThisPc")));
            Row(c, "A new tab or window opens", "Home is your user folder", null, Segmented(
                Opt("Home", _s.NewTabOpens == "Home", () => _s.NewTabOpens = "Home"),
                Opt("This PC", _s.NewTabOpens == "ThisPc", () => _s.NewTabOpens = "ThisPc"),
                Opt("Same folder", _s.NewTabOpens == "Same", () => _s.NewTabOpens = "Same")));
            c = Card(general, "Opening");
            Row(c, "Open files and folders with", "Ctrl / Shift + click still select", null, Segmented(
                Opt("Double-click", !_s.SingleClick, () => _s.SingleClick = false),
                Opt("Single click", _s.SingleClick, () => _s.SingleClick = true)));
            Row(c, "A folder opened from the list opens", null, null, Segmented(
                Opt("In the same tab", !_s.FoldersInNewTab, () => _s.FoldersInNewTab = false),
                Opt("In a new tab", _s.FoldersInNewTab, () => _s.FoldersInNewTab = true)));

            // ---- Look ----
            StackPanel look = Page(PgLook, "", "How the file list looks");
            c = Card(look, "The list");
            Row(c, "Space between rows", "In the Details view", null, Segmented(
                Opt("Compact", _s.RowSpacing == "Compact", () => _s.RowSpacing = "Compact"),
                Opt("Normal", _s.RowSpacing == "Normal", () => _s.RowSpacing = "Normal"),
                Opt("Roomy", _s.RowSpacing == "Roomy", () => _s.RowSpacing = "Roomy")));
            Row(c, "Text size", "Names in every view", null, Segmented(
                Opt("Small", _s.ListTextSize == "Small", () => _s.ListTextSize = "Small"),
                Opt("Normal", _s.ListTextSize == "Normal", () => _s.ListTextSize = "Normal"),
                Opt("Large", _s.ListTextSize == "Large", () => _s.ListTextSize = "Large")));
            c = Card(look, "Names and views");
            Switch(c, "Remember each folder's view and sort", "Icons in Pictures, Details elsewhere …",
                   "Off: one view and sort for every folder. View > Use this view for all folders sets what the others use.",
                   _s.ViewPerFolder, v => _s.ViewPerFolder = v);
            Switch(c, "Show file name extensions", ".txt, .pdf … on names",
                   "Off: extensions are left off the names of file types Windows knows, as in Explorer. Renaming keeps them.",
                   _s.ShowExtensions, v => _s.ShowExtensions = v);
            c = Card(look, "The window");
            Switch(c, "Use the theme's opacity", "See-through like vShell's bars - off: solid",
                   "On: the window shows your wallpaper through its colour at the opacity set in vShell (blurred with Blur or Frosted), like the bars and widgets. Off: solid colours.",
                   _s.ThemeOpacity, v => _s.ThemeOpacity = v);
            Note(look, "Colours, opacity, blur and corners come from your vShell theme - change them in vShell's settings.");

            // ---- Files ----
            StackPanel files = Page(PgFiles, "", "Hidden items, sorting and deleting");
            c = Card(files, "Files and folders");
            Switch(c, "Show hidden items", "Also Ctrl+H", "Hidden files and folders, shown faded as in Explorer.", _s.ShowHidden, v => _s.ShowHidden = v);
            Switch(c, "Folders before files", "Whatever the list is sorted by", null, _s.FoldersFirst, v => _s.FoldersFirst = v);
            Switch(c, "Ask before moving to the Recycle Bin", "Shift+Delete always asks", "A question in the theme when you press Delete.",
                   _s.ConfirmRecycle, v => _s.ConfirmRecycle = v);
            Switch(c, "Count the size of selected folders", "In the status bar",
                   "vXplorer adds up everything inside the folders you select and shows the total next to the selection. It stops as soon " +
                   "as you select something else; switch it off if counting big folders on a slow network drive bothers you.",
                   _s.CountFolderSizes, v => _s.CountFolderSizes = v);
            c = Card(files, "Copying and moving");
            Switch(c, "Use Windows' copy window", "Off: progress in vXplorer's panel under the list",
                   "On: copies, moves and deletes show Windows' own progress window and questions, as in Explorer. vXplorer can't offer to " +
                   "finish them if it closes part way.", _s.WindowsCopyDialog, v => _s.WindowsCopyDialog = v);
            Switch(c, "Pop the panel out into its own window", "Off: docked under the list",
                   "On: copies, moves and deletes show in a small window of their own that you can move anywhere - another screen, say - " +
                   "apart from vXplorer's windows. It shows up when something starts; closing it folds it into the status bar. The pop-out " +
                   "button on the panel does the same.", _s.OpsPopOut, v => _s.OpsPopOut = v);
            c = Card(files, "Progress graph");
            // the line's own rows show only while Line is picked
            var lineRows = new List<UIElement>();
            Action showLineRows = () => { foreach (UIElement r in lineRows) r.Visibility = _s.OpsGraph == "Line" ? Visibility.Visible : Visibility.Collapsed; };
            Row(c, "Graph", "How each copy shows how far it's got", "Meter: a bar under the text. Line: how fast it's going all the way " +
                "through, like Windows' copy window - across is how far, up is how fast. Ring: a round gauge with the percent in it, in place " +
                "of the icon. The same graphs as vShell's CPU module.", Segmented(
                Opt("Meter", _s.OpsGraph == "Meter", () => { _s.OpsGraph = "Meter"; showLineRows(); }),
                Opt("Line", _s.OpsGraph == "Line", () => { _s.OpsGraph = "Line"; showLineRows(); }),
                Opt("Ring", _s.OpsGraph == "Ring", () => { _s.OpsGraph = "Ring"; showLineRows(); })));
            Row(c, "Line thickness", "The Line graph", null, Segmented(
                Opt("Thin", _s.OpsGraphLine == "Thin", () => _s.OpsGraphLine = "Thin"),
                Opt("Normal", _s.OpsGraphLine == "Normal", () => _s.OpsGraphLine = "Normal"),
                Opt("Thick", _s.OpsGraphLine == "Thick", () => _s.OpsGraphLine = "Thick"),
                Opt("Heavy", _s.OpsGraphLine == "Heavy", () => _s.OpsGraphLine = "Heavy")));
            lineRows.Add(c.Children[c.Children.Count - 1]);
            Row(c, "Fill under the line", "The Line graph", null, Segmented(
                Opt("None", _s.OpsGraphFill == "None", () => _s.OpsGraphFill = "None"),
                Opt("Light", _s.OpsGraphFill == "Light", () => _s.OpsGraphFill = "Light"),
                Opt("Solid", _s.OpsGraphFill == "Solid", () => _s.OpsGraphFill = "Solid"),
                Opt("Fade", _s.OpsGraphFill == "Fade", () => _s.OpsGraphFill = "Fade")));
            lineRows.Add(c.Children[c.Children.Count - 1]);
            Switch(c, "Box behind the line", "The Line graph", null, _s.OpsGraphBack, v => _s.OpsGraphBack = v);
            lineRows.Add(c.Children[c.Children.Count - 1]);
            showLineRows();

            // ---- Sidebar ----
            StackPanel side = Page(PgSidebar, "", "Quick access and the folder tree");
            c = Card(side, "The tree");
            Switch(c, "Follow the open folder", "Opens the sidebar down to the folder you're in", null, _s.SidebarFollows, v => _s.SidebarFollows = v);
            Switch(c, "Show network drives", "Mapped drives under This PC", null, _s.SidebarNetwork, v => _s.SidebarNetwork = v);
            c = Card(side, "Quick access");
            _quickList = new StackPanel();
            Place(c, _quickList);
            var add = TextButton("Add a folder…", () =>
            {
                var pick = new Microsoft.Win32.OpenFolderDialog { Title = "Add to Quick access" };
                if (pick.ShowDialog(this) == true) SetQuick(Quick().Concat(new[] { pick.FolderName }).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            });
            var reset = TextButton("Back to the standard folders", () => SetQuick(null));
            Place(c, Buttons(add, reset));
            RefreshQuickList();
            Note(side, "You can also pin a folder from its right-click menu, or a tab's.");

            // ---- Preview pane ----
            StackPanel prev = Page(PgPreview, "", "What the preview beside the list shows");
            c = Card(prev, "Previews");
            Switch(c, "Use Windows' previewers for pictures and text too", "Off: vXplorer shows them itself, in the theme",
                   "On: Windows' (or PowerToys') previewer, where there is one.", _s.PreviewWindowsForAll, v => _s.PreviewWindowsForAll = v);
            Row(c, "How much of a text file to show", "From the start of the file", null, Segmented(
                Opt("64 KB", _s.TextPreviewKB == 64, () => _s.TextPreviewKB = 64),
                Opt("128 KB", _s.TextPreviewKB == 128, () => _s.TextPreviewKB = 128),
                Opt("1 MB", _s.TextPreviewKB == 1024, () => _s.TextPreviewKB = 1024)));

            // ---- Windows ----
            StackPanel win = Page(PgWindows, "", "vXplorer and the rest of Windows");
            c = Card(win, "File Explorer");
            Switch(c, "Open folders with vXplorer", "Instead of File Explorer - Win+E too",
                   "Folders and drives you open (on the desktop, from other apps, folder shortcuts) open in vXplorer. Only for you (no admin); " +
                   "switch it off to put Explorer back. Uses vXplorer from where it is now: " + Environment.ProcessPath,
                   DefaultFileManager.IsOn(), v =>
                   {
                       try
                       {
                           if (v) DefaultFileManager.TurnOn(Environment.ProcessPath); else DefaultFileManager.TurnOff();
                           _s.DefaultFileManager = v;
                       }
                       catch (Exception ex) { Log.Write("Default file manager: " + ex); }
                   });

            // ---- Updates ----
            UpdatesPage(_s);

            // ---- About ----
            StackPanel about = Page(PgAbout, "", null);
            c = Card(about, "vXplorer");
            Version ver = Assembly.GetExecutingAssembly().GetName().Version;
            var info = new StackPanel();
            info.Children.Add(new TextBlock { Text = "vXplorer " + ver.Major + "." + ver.Minor + "." + ver.Build, FontSize = 13.5, FontWeight = FontWeights.SemiBold });
            info.Children.Add(Dim("A file manager in your vShell theme.", 12, new Thickness(0, 2, 0, 0)));
            Place(c, info);
            Row(c, "Colours", _themeSource.Found ? "Your vShell theme - vXplorer follows it as you change it" : "vShell's default theme (vShell's settings weren't found)", null, null);
            var folder = TextButton("Open", () =>
            {
                var w = Owner as MainWindow;
                if (w != null) { w.OpenFolderInTab(Log.Folder); w.Activate(); }
            });
            folder.Margin = new Thickness(0);
            Row(c, "Settings folder", Log.Folder, null, folder);
        }

        protected override void Saved()
        {
            _s.Save();
            _changed?.Invoke();
        }
        // =================================================================
        // The Quick access list
        // =================================================================

        StackPanel _quickList;

        List<string> Quick() { return _s.QuickAccess ?? MainWindow.DefaultQuickFolders(); }

        void SetQuick(List<string> list)
        {
            _s.QuickAccess = list;
            Saved();
            RefreshQuickList();
        }

        /// <summary>One row per folder: its name and path, move up / down, remove.</summary>
        public void RefreshQuickList()
        {
            if (_quickList == null) return;
            _quickList.Children.Clear();
            var list = Quick();
            for (int i = 0; i < list.Count; i++)
            {
                string p = list[i];
                int at = i;
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                // docked right, so added right to left: up, down, remove on screen
                row.Children.Add(Tool("", "Remove from Quick access", true, () => { var l = Quick().ToList(); l.RemoveAt(at); SetQuick(l); }));
                row.Children.Add(Tool("", "Move down", at < list.Count - 1, () => { var l = Quick().ToList(); l.RemoveAt(at); l.Insert(at + 1, p); SetQuick(l); }));
                row.Children.Add(Tool("", "Move up", at > 0, () => { var l = Quick().ToList(); l.RemoveAt(at); l.Insert(at - 1, p); SetQuick(l); }));
                var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                label.Children.Add(new TextBlock { Text = Native.DisplayName(p), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
                TextBlock path = Dim(p, 11, new Thickness(0));
                path.TextWrapping = TextWrapping.NoWrap;
                path.TextTrimming = TextTrimming.CharacterEllipsis;
                label.Children.Add(path);
                row.Children.Add(label);
                _quickList.Children.Add(row);
            }
            if (list.Count == 0) _quickList.Children.Add(Dim("No folders - Quick access is empty.", 12.5, new Thickness(0)));
        }

        Button Tool(string glyph, string tip, bool enabled, Action click)
        {
            var b = new Button { Content = glyph, ToolTip = tip, IsEnabled = enabled, Width = 28, Height = 26, FontSize = 11 };
            b.SetResourceReference(StyleProperty, "vx.IconButton");
            b.Click += (o, e) => click();
            DockPanel.SetDock(b, Dock.Right);
            return b;
        }
    }
}
