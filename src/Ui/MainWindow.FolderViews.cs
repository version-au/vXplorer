using System;
using System.Linq;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>
    /// Each folder remembers its view (Details / icons) and sort, as Explorer does: changing either in a folder keeps it for that folder;
    /// "Use this view for all sub-folders" (View menu) makes a folder's view the one for every folder under it that has none of its own
    /// (the nearest such folder above wins, so a folder set on its own - or its own sub-folder rule - is never overwritten);
    /// a folder with neither uses the defaults, which "Use this view for all folders" sets. Options > Look turns it off -
    /// then one view and sort for everything, as before. Search results and This PC / Network keep whatever's in use.
    /// </summary>
    public partial class MainWindow
    {
        static string ViewKey(string path) { return AppSettings.FolderViewKey(path); }

        /// <summary>On opening a folder: its own view and sort, a folder above's (for its sub-folders), or the defaults.</summary>
        void ApplyFolderView(string path)
        {
            if (!_settings.ViewPerFolder || path == null || IsPlace(path)) return;
            FolderView fv = _settings.FolderViewFor(path);
            string view, sort, group;
            bool desc;
            if (fv != null) { view = fv.View; sort = fv.Sort; desc = fv.Desc; group = fv.Group; }
            else { view = _settings.DefaultViewMode; sort = _settings.DefaultSortKey; desc = _settings.DefaultSortDescending; group = _settings.DefaultGroupBy; }
            if (Array.IndexOf(ViewModes, view) < 0) view = "Details";
            if (Array.IndexOf(SortKeys, sort) < 0) sort = "Name";
            if (Array.IndexOf(GroupKeys, group) < 0) group = "None";
            _settings.SortKey = sort;
            _settings.SortDescending = desc;
            UpdateSortHeaders();
            bool regroup = _settings.GroupBy != group;
            _settings.GroupBy = group;
            if (_settings.ViewMode != view || regroup)
            {
                _settings.ViewMode = view;
                ApplyView();
            }
        }

        /// <summary>The view or sort was changed here: keep it for this folder (one set for its sub-folders stays so - they follow).</summary>
        void RememberFolderView()
        {
            if (!_settings.ViewPerFolder || _current == null || InPlace || Searching) return;
            string key = ViewKey(_current);
            FolderView old;
            bool subfolders = _settings.FolderViews.TryGetValue(key, out old) && old.Subfolders;
            _settings.FolderViews[key] = new FolderView
            {
                View = _settings.ViewMode, Sort = _settings.SortKey, Desc = _settings.SortDescending, Group = _settings.GroupBy, Used = DateTime.UtcNow,
                Subfolders = subfolders
            };
            if (_settings.FolderViews.Count > 520)   // keep the 500 used most recently (sub-folder ones are always kept)
            {
                var plain = _settings.FolderViews.Where(p => !p.Value.Subfolders).ToList();
                int extra = _settings.FolderViews.Count - 500;
                foreach (string k in plain.OrderBy(p => p.Value.Used).Take(Math.Min(extra, plain.Count)).Select(p => p.Key).ToList())
                    _settings.FolderViews.Remove(k);
            }
        }

        /// <summary>Is this folder's view set for its sub-folders?</summary>
        bool ViewForSubfolders()
        {
            FolderView fv;
            return _current != null && _settings.FolderViews.TryGetValue(ViewKey(_current), out fv) && fv.Subfolders;
        }

        /// <summary>View menu: this folder's view and sort for every folder under it without its own (again: just this folder).</summary>
        void ToggleViewForSubfolders()
        {
            if (!_settings.ViewPerFolder || _current == null || InPlace || Searching) return;
            bool on = !ViewForSubfolders();
            RememberFolderView();
            _settings.FolderViews[ViewKey(_current)].Subfolders = on;
            _settings.Save();
            Status(on ? "Folders inside this one now use " + ViewNames[Array.IndexOf(ViewModes, _settings.ViewMode)].ToLowerInvariant() + ", sorted by " +
                        Headers[Math.Max(0, Array.IndexOf(SortKeys, _settings.SortKey))].ToLowerInvariant() + " (those with their own view keep it)."
                      : "This view is for this folder only now.");
        }

        /// <summary>View menu: this folder's view and sort become the default, and every folder's own is forgotten.</summary>
        void UseViewForAllFolders()
        {
            _settings.DefaultViewMode = _settings.ViewMode;
            _settings.DefaultSortKey = _settings.SortKey;
            _settings.DefaultSortDescending = _settings.SortDescending;
            _settings.DefaultGroupBy = _settings.GroupBy;
            _settings.FolderViews.Clear();
            _settings.Save();
            Status("All folders now use " + ViewNames[Array.IndexOf(ViewModes, _settings.ViewMode)].ToLowerInvariant() + ", sorted by " +
                   Headers[Math.Max(0, Array.IndexOf(SortKeys, _settings.SortKey))].ToLowerInvariant() + ".");
        }
    }
}
