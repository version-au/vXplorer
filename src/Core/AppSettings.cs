using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace vXplorer.Core
{
    /// <summary>A folder's own view and sort (Options > Look > Remember each folder's view).</summary>
    public sealed class FolderView
    {
        public string View { get; set; } = "Details";
        public string Sort { get; set; } = "Name";
        public bool Desc { get; set; }
        public string Group { get; set; } = "None";
        public DateTime Used { get; set; }
        /// <summary>View menu > Use this view for all sub-folders: folders under this one without their own view use it too
        /// (the nearest such folder above wins). Never dropped by the 500-folder clean-up.</summary>
        public bool Subfolders { get; set; }
    }

    /// <summary>vXplorer's own settings (%APPDATA%\vXplorer\settings.json). The colours are vShell's - see VShellThemeSource.</summary>
    public sealed class AppSettings : vSuite.Ui.IUpdateSettings
    {
        /// <summary>Window position and size in DIPs; Width 0 = never saved (centre on the main monitor).</summary>
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
        public double SidebarWidth { get; set; } = 230;
        public string LastFolder { get; set; } = "";
        public bool ShowHidden { get; set; }
        /// <summary>The file list's sort column: Name, Date, Type or Size.</summary>
        public string SortKey { get; set; } = "Name";
        public bool SortDescending { get; set; }
        /// <summary>The file list's view: Details, Medium, Large or ExtraLarge (icons).</summary>
        public string ViewMode { get; set; } = "Details";
        public bool PreviewPane { get; set; }
        public double PreviewWidth { get; set; } = 320;
        /// <summary>The open tabs' folders, and which one was showing.</summary>
        public List<string> Tabs { get; set; } = new List<string>();
        public int ActiveTab { get; set; }

        // ---- Options ----
        /// <summary>When vXplorer starts: "Restore" the last tabs, "Home" (your user folder) or "ThisPc".</summary>
        public string StartWith { get; set; } = "Restore";
        /// <summary>What a new tab (and a new window) opens: "Home", "ThisPc" or "Same" (the folder of the tab you're on).</summary>
        public string NewTabOpens { get; set; } = "Home";
        /// <summary>The sidebar opens down to and highlights the open folder.</summary>
        public bool SidebarFollows { get; set; } = true;
        /// <summary>Folders before files in the list, whatever the sort.</summary>
        public bool FoldersFirst { get; set; } = true;

        /// <summary>Space between rows in Details: "Compact", "Normal" or "Roomy".</summary>
        public string RowSpacing { get; set; } = "Normal";
        /// <summary>The file list's text: "Small", "Normal" or "Large".</summary>
        public string ListTextSize { get; set; } = "Normal";
        /// <summary>Show ".txt", ".pdf" … (off: hidden for file types Windows knows, as in Explorer).</summary>
        public bool ShowExtensions { get; set; } = true;
        /// <summary>One click opens files and folders (instead of a double-click).</summary>
        public bool SingleClick { get; set; }
        /// <summary>A folder opened from the list opens in a new tab.</summary>
        public bool FoldersInNewTab { get; set; }
        /// <summary>Ask (in the theme) before moving things to the Recycle Bin.</summary>
        public bool ConfirmRecycle { get; set; }
        /// <summary>The status bar adds up what's inside selected folders (counted in the background).</summary>
        public bool CountFolderSizes { get; set; } = true;
        /// <summary>The sidebar's Quick access folders, in order; null = the standard ones (your user folder, Desktop, Documents …).</summary>
        public List<string> QuickAccess { get; set; }
        /// <summary>Network drives in the sidebar's This PC part.</summary>
        public bool SidebarNetwork { get; set; } = true;
        /// <summary>Windows' previewers for text and pictures too (instead of vXplorer's own, in the theme).</summary>
        public bool PreviewWindowsForAll { get; set; }
        /// <summary>How much of a text file the preview pane shows, in KB: 64, 128 or 1024.</summary>
        public int TextPreviewKB { get; set; } = 128;
        /// <summary>vXplorer opens folders, drives and Win+E instead of Explorer (registry keys under HKCU, see DefaultFileManager).</summary>
        public bool DefaultFileManager { get; set; }
        /// <summary>Copies, moves and deletes show Windows' own progress window instead of vXplorer's panel under the list.</summary>
        public bool WindowsCopyDialog { get; set; }
        /// <summary>The file operations panel is folded away into the status bar.</summary>
        public bool OpsFolded { get; set; }
        /// <summary>The file operations panel is in its own window (OpsWindow) that moves independently, not under the list.</summary>
        public bool OpsPopOut { get; set; }
        /// <summary>Where the popped-out panel was put (width 0 = not yet: the bottom right of the window it came from).</summary>
        public double OpsLeft { get; set; }
        public double OpsTop { get; set; }
        public double OpsWidth { get; set; }
        /// <summary>Each operation's progress graph, as vShell's CPU module draws them: Meter, Line (speed along the way) or Ring.</summary>
        public string OpsGraph { get; set; } = "Meter";
        /// <summary>The line graph's look, as vShell's Graph settings: thickness (Thin / Normal / Thick / Heavy), fill under it
        /// (None / Light / Solid / Fade) and the box behind it.</summary>
        public string OpsGraphLine { get; set; } = "Normal";
        public string OpsGraphFill { get; set; } = "Light";
        public bool OpsGraphBack { get; set; } = true;

        // ---- view and sort per folder (MainWindow.FolderViews.cs) ----
        /// <summary>Each folder keeps the view and sort you last gave it; other folders use the defaults below.</summary>
        public bool ViewPerFolder { get; set; } = true;
        /// <summary>For folders with none of their own; null until first set (then: the view / sort in use when this came in).</summary>
        public string DefaultViewMode { get; set; }
        public string DefaultSortKey { get; set; }
        public bool DefaultSortDescending { get; set; }
        /// <summary>The Details columns shown besides Name (right-click a column header): Date, Type, Size, Created, Dimensions, Length.</summary>
        public List<string> Columns { get; set; }
        /// <summary>Group the list by None / Name / Date / Type / Size (kept per folder like the sort); the default for other folders.</summary>
        public string GroupBy { get; set; } = "None";
        public string DefaultGroupBy { get; set; } = "None";
        /// <summary>Folder path (lower case) -> its view and sort. The 500 used most recently are kept.</summary>
        public Dictionary<string, FolderView> FolderViews { get; set; } = new Dictionary<string, FolderView>();

        /// <summary>The key a folder's view is kept under: no trailing \, lower case.</summary>
        public static string FolderViewKey(string path) { return path.TrimEnd('\\').ToLowerInvariant(); }

        /// <summary>The view a folder opens with: its own, else the nearest folder above it set for all its sub-folders, else null
        /// (the defaults).</summary>
        public FolderView FolderViewFor(string path)
        {
            if (string.IsNullOrEmpty(path) || FolderViews == null) return null;
            FolderView fv;
            if (FolderViews.TryGetValue(FolderViewKey(path), out fv)) return fv;
            string up = path.TrimEnd('\\');
            while (true)
            {
                try { up = Path.GetDirectoryName(up); } catch (ArgumentException) { return null; }
                if (string.IsNullOrEmpty(up)) return null;
                if (FolderViews.TryGetValue(FolderViewKey(up), out fv) && fv.Subfolders) return fv;
                up = up.TrimEnd('\\');
            }
        }

        /// <summary>Options > Look: the windows see-through at vShell's opacity, like its bars (the wallpaper behind); off = solid.</summary>
        public bool ThemeOpacity { get; set; } = true;

        // ---- updates (Options > Updates; see Updater / UpdateCenter) ----
        /// <summary>Look for a new version on the update site a minute after starting, then about once a day.</summary>
        public bool UpdateAuto { get; set; } = true;
        /// <summary>Offer pre-releases too.</summary>
        public bool UpdatePre { get; set; }
        public DateTime UpdateLastCheck { get; set; } = DateTime.MinValue;
        /// <summary>A version you chose to skip (or that didn't start and was rolled back): not offered again; a newer one is.</summary>
        public string UpdateSkip { get; set; } = "";

        static string FilePath { get { return Path.Combine(Log.Folder, "settings.json"); } }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                    if (s != null) { s.Normalize(); return s; }
                }
            }
            catch (Exception ex) { Log.Write("Settings: " + ex.Message); }
            // the first start (no settings yet): the defaults filled in too - without them the Details columns failed to set up
            var fresh = new AppSettings();
            fresh.Normalize();
            return fresh;
        }

        /// <summary>Values from an old or hand-edited file put right (internal: the tests call it).</summary>
        internal void Normalize()
        {
            if (LastFolder == null) LastFolder = "";
            if (Tabs == null) Tabs = new List<string>();
            if (ViewMode != "Medium" && ViewMode != "Large" && ViewMode != "ExtraLarge") ViewMode = "Details";
            if (PreviewWidth < 200 || PreviewWidth > 900) PreviewWidth = 320;
            if (SidebarWidth < 140 || SidebarWidth > 500) SidebarWidth = 230;
            if (!SortKeyOk(SortKey)) SortKey = "Name";
            if (StartWith != "Home" && StartWith != "ThisPc") StartWith = "Restore";
            if (NewTabOpens != "ThisPc" && NewTabOpens != "Same") NewTabOpens = "Home";
            if (RowSpacing != "Compact" && RowSpacing != "Roomy") RowSpacing = "Normal";
            if (ListTextSize != "Small" && ListTextSize != "Large") ListTextSize = "Normal";
            if (OpsGraph != "Line" && OpsGraph != "Ring") OpsGraph = "Meter";
            if (OpsGraphLine != "Thin" && OpsGraphLine != "Thick" && OpsGraphLine != "Heavy") OpsGraphLine = "Normal";
            if (OpsGraphFill != "None" && OpsGraphFill != "Solid" && OpsGraphFill != "Fade") OpsGraphFill = "Light";
            if (TextPreviewKB != 64 && TextPreviewKB != 1024) TextPreviewKB = 128;
            if (QuickAccess != null) QuickAccess.RemoveAll(string.IsNullOrWhiteSpace);
            if (UpdateSkip == null) UpdateSkip = "";
            if (FolderViews == null) FolderViews = new Dictionary<string, FolderView>();
            if (DefaultViewMode == null) { DefaultViewMode = ViewMode; DefaultSortKey = SortKey; DefaultSortDescending = SortDescending; }
            if (DefaultViewMode != "Medium" && DefaultViewMode != "Large" && DefaultViewMode != "ExtraLarge") DefaultViewMode = "Details";
            if (!SortKeyOk(DefaultSortKey)) DefaultSortKey = "Name";
            if (Columns == null) Columns = new List<string> { "Date", "Type", "Size" };
            Columns.RemoveAll(c => !SortKeyOk(c) || c == "Name");
            if (!GroupOk(GroupBy)) GroupBy = "None";
            if (!GroupOk(DefaultGroupBy)) DefaultGroupBy = "None";
        }

        static bool SortKeyOk(string k) { return k == "Name" || k == "Date" || k == "Type" || k == "Size" || k == "Created" || k == "Dimensions" || k == "Length"; }
        static bool GroupOk(string g) { return g == "None" || g == "Name" || g == "Date" || g == "Type" || g == "Size"; }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Log.Folder);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, FilePath, true);
            }
            catch (Exception ex) { Log.Write("Settings save: " + ex.Message); }
        }
    }
}
