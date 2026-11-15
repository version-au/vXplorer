using System;
using System.Collections.Generic;
using System.IO;

namespace vXplorer.Core
{
    /// <summary>
    /// Watches the open folder (not its subfolders) and says when the list may need reading again. Events come on the watcher's
    /// thread: <c>changed</c> has to get itself to the UI thread.
    /// </summary>
    public sealed class FolderWatcher : IDisposable
    {
        readonly FileSystemWatcher _w;

        /// <param name="showHidden">Hidden items are shown (read on the watcher's thread).</param>
        /// <param name="listed">The paths the list shows now (read on the watcher's thread - hand out a fresh set, never change one).</param>
        public FolderWatcher(string path, Func<bool> showHidden, Func<HashSet<string>> listed, Action changed)
        {
            var w = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes,
                IncludeSubdirectories = false
            };
            // a hidden file that isn't shown changing (a home folder's NTUSER.DAT logs change every few seconds) is no reason to read the
            // folder again - it kept the list (and its icons) reloading
            FileSystemEventHandler any = (s, e) => { if (!Unseen(e.FullPath, showHidden(), listed())) changed(); };
            w.Changed += any;
            w.Created += any;
            w.Deleted += any;
            w.Renamed += (s, e) => { if (!Unseen(e.FullPath, showHidden(), listed()) || listed().Contains(e.OldFullPath ?? "")) changed(); };
            w.Error += (s, e) => changed();
            w.EnableRaisingEvents = true;
            _w = w;
        }

        /// <summary>
        /// A hidden item while hidden items aren't shown, and not in the list. Something gone or unreadable counts as seen, and so does an
        /// item that's listed - one that has just been made hidden has to go from the list.
        /// </summary>
        public static bool Unseen(string path, bool showHidden, HashSet<string> listed)
        {
            if (showHidden || string.IsNullOrEmpty(path)) return false;
            if (listed != null && listed.Contains(path)) return false;
            try { return (File.GetAttributes(path) & FileAttributes.Hidden) != 0; }
            catch { return false; }
        }

        public void Dispose() { _w.Dispose(); }
    }
}
