using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>
    /// Zip files, as in Explorer: double-click one to look inside like a folder (read-only - Extract all to change it); open, copy or drag
    /// files out of it (they're extracted to a temporary folder first); Extract all and Compress to ZIP in the command bar. Undo takes
    /// back an Extract all or a Compress (the new folder / zip goes to the Recycle Bin).
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>The open folder is inside a zip (set by Navigate - IsZipPath looks at the disk).</summary>
        bool _inZip;

        async void LoadZipList(int v)
        {
            string path = _current, zip, inner;
            if (!ZipFolders.TrySplit(path, out zip, out inner)) return;
            Status("Reading " + Path.GetFileName(zip) + "…");
            List<FileItem> list;
            string error = null;
            try { list = await Task.Run(() => ZipFolders.List(zip, inner)); }
            catch (Exception ex) { list = new List<FileItem>(); error = "vXplorer can't read this zip file: " + ex.Message; }
            if (v != _loadVersion) return;
            HashSet<string> select = _selectNext;
            _selectNext = null;
            _restoreSelected = null;
            _restoreScroll = null;
            _items = new ObservableCollection<FileItem>(list);
            ShowItems();
            EmptyText.Text = error ?? "This folder of the zip is empty.";
            EmptyText.Visibility = list.Count == 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            if (select != null)
            {
                _selecting = true;
                try { foreach (FileItem it in _items) if (select.Contains(it.Path)) Files.SelectedItems.Add(it); }
                finally { _selecting = false; }
            }
            UpdateStatus();
            SchedulePreview();
            foreach (FileItem it in list)
            {
                FileItem item = it;   // there's no file on disk to ask for its icon: the type's
                _icons.RequestForType(Path.GetExtension(item.Name), item.IsFolder, img => { if (v == _loadVersion) item.Icon = img; });
            }
        }

        /// <summary>Files (or folders) from the open zip folder copied out to a new temporary folder; their paths there.</summary>
        List<string> ExtractToTemp(IList<FileItem> items)
        {
            string zip, inner;
            if (items.Count == 0 || !ZipFolders.TrySplit(_current, out zip, out inner)) return new List<string>();
            string prefix = inner.Length == 0 ? "" : inner.TrimEnd('/') + "/";
            try { return ZipFolders.Extract(zip, items.Select(i => prefix + i.Name).ToList(), ZipFolders.TempFolder()); }
            catch (Exception ex) { Status("vXplorer couldn't take that out of the zip: " + ex.Message); return new List<string>(); }
        }

        /// <summary>
        /// The same in the background (Open, Copy): the window stays usable while a big file comes out. (A drag out of a zip still
        /// takes its files out as it starts - Windows needs them there the moment the drag begins.)
        /// </summary>
        async System.Threading.Tasks.Task<List<string>> ExtractToTempAsync(IList<FileItem> items)
        {
            string zip, inner;
            if (items.Count == 0 || !ZipFolders.TrySplit(_current, out zip, out inner)) return new List<string>();
            string prefix = inner.Length == 0 ? "" : inner.TrimEnd('/') + "/";
            List<string> inners = items.Select(i => prefix + i.Name).ToList();
            Status("Taking " + (items.Count == 1 ? items[0].Name : items.Count + " items") + " out of the zip…");
            try
            {
                List<string> made = await System.Threading.Tasks.Task.Run(() => ZipFolders.Extract(zip, inners, ZipFolders.TempFolder()));
                Status("");
                return made;
            }
            catch (Exception ex) { Status("vXplorer couldn't take that out of the zip: " + ex.Message); return new List<string>(); }
        }

        /// <summary>Open files from inside a zip: extracted to a temporary folder, then opened with their programs.</summary>
        async void OpenFromZip(IList<FileItem> files)
        {
            foreach (string p in await ExtractToTempAsync(files.Take(15).ToList()))
            {
                try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(p) }); }
                catch (Exception ex) { Status("Couldn't open " + Path.GetFileName(p) + ": " + ex.Message); }
            }
        }

        /// <summary>The selected zip file, or the zip the open folder is in; null if neither.</summary>
        string ZipToExtract()
        {
            string zip, inner;
            if (_inZip && ZipFolders.TrySplit(_current, out zip, out inner)) return zip;
            if (Files.SelectedItems.Count == 1 && !InPlace)
            {
                var f = (FileItem)Files.SelectedItem;
                if (!f.IsFolder && ZipFolders.IsZipFile(f.Path)) return f.Path;
            }
            return null;
        }

        /// <summary>Extract all: the zip into a new folder next to it (named after it), then show that folder selected.</summary>
        async void ExtractAll()
        {
            string zip = ZipToExtract();
            if (zip == null) return;
            Status("Extracting " + Path.GetFileName(zip) + "…");
            string folder;
            try
            {
                folder = await Task.Run(() => ZipFolders.ExtractAll(zip, p => Dispatcher.BeginInvoke(new Action(() =>
                    StatusText.Text = "Extracting " + Path.GetFileName(zip) + "…  " + Math.Round(p * 100) + "%"))));
            }
            catch (Exception ex) { Status("vXplorer couldn't extract " + Path.GetFileName(zip) + ": " + ex.Message); return; }
            RememberCreated(new[] { folder }, "extract");
            _selectNext = PathSet(new[] { folder });
            if (_inZip) Navigate(Path.GetDirectoryName(zip));   // out of the zip, to the new folder next to it
            else Refresh();
            Status("Extracted to " + Path.GetFileName(folder) + ".");
        }

        /// <summary>Compress to ZIP: the selected items into a new zip here, named after the first one, ready to rename.</summary>
        async void CompressSelection()
        {
            if (InPlace) return;
            List<string> paths = SelectedPaths();
            if (paths.Count == 0) return;
            string first = Path.GetFileName(paths[0].TrimEnd('\\'));
            string name = Directory.Exists(paths[0]) ? first : Path.GetFileNameWithoutExtension(first);
            string zip = ZipFolders.Unique(Path.Combine(_current, name + ".zip"));
            Status("Compressing " + (paths.Count == 1 ? first : paths.Count + " items") + "…");
            try
            {
                await Task.Run(() => ZipFolders.Compress(paths, zip, p => Dispatcher.BeginInvoke(new Action(() =>
                    StatusText.Text = "Compressing…  " + Math.Round(p * 100) + "%"))));
            }
            catch (Exception ex) { Status("vXplorer couldn't make the zip: " + ex.Message); return; }
            RememberCreated(new[] { zip }, "compress");
            _selectNext = PathSet(new[] { zip });
            _renameNext = zip;   // as Explorer: ready to name
            Refresh();
        }
    }
}
