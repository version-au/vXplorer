using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>
    /// Network, like Explorer's: the sidebar's Network node and the file list show the computers Windows finds, and a computer (\\NAME)
    /// shows its shared folders, which are ordinary folders from there on. Nothing is looked up until Network or a computer is opened.
    /// Network and \\NAME are "places", not folders: nothing can be made, pasted or searched in them (as with This PC).
    /// </summary>
    public partial class MainWindow
    {
        static bool IsNetwork(string p) { return NetworkPlaces.IsNetwork(p); }
        static bool IsServer(string p) { return NetworkPlaces.IsServer(p); }

        /// <summary>Not a folder you can change: This PC, Network, a computer on the network, or inside a zip (read-only).</summary>
        static bool IsPlace(string p) { return IsThisPc(p) || IsNetwork(p) || IsServer(p) || ZipFolders.IsZipPath(p); }
        bool InPlace { get { return IsThisPc(_current) || IsNetwork(_current) || IsServer(_current) || _inZip; } }

        /// <summary>A path as vXplorer keeps it: the places' own forms, or a cleaned-up folder path (null if it isn't one).</summary>
        static string NormalPlace(string p)
        {
            if (p == null) return null;
            if (IsThisPc(p)) return ThisPcPath;
            if (IsNetwork(p)) return NetworkPlaces.NetworkPath;
            if (IsServer(p)) return NetworkPlaces.ServerPath(p);
            return Clean(p);
        }

        /// <summary>The file list for Network (computers) or a computer (its shares).</summary>
        async void LoadNetworkList(int v)
        {
            string path = _current;
            bool network = IsNetwork(path);
            // straight away: an empty list saying what's happening (not the last folder's files), and for Network the computers behind
            // mapped drives, which need no search
            List<Tuple<string, string>> known = network ? await Task.Run(() => NetworkPlaces.KnownComputers()) : new List<Tuple<string, string>>();
            if (v != _loadVersion) return;
            ShowNetworkItems(v, known, network, network ? "Looking for computers on the network…" : "Reading " + NameOf(path) + "'s shared folders…", true);
            Status(network ? "Looking for more computers on the network - this can take a little while…" : "Reading " + NameOf(path) + "'s shared folders…");

            List<Tuple<string, string>> found;
            string error = null;
            try { found = network ? await NetworkPlaces.AllComputers() : await Task.Run(() => NetworkPlaces.Shares(path)); }
            catch (Exception ex) { found = new List<Tuple<string, string>>(); error = "vXplorer can't see " + NameOf(path) + "'s shared folders: " + ex.Message; }
            if (v != _loadVersion) return;
            ShowNetworkItems(v, found, network, error ?? (network
                ? "No computers found. Windows finds them with network discovery - check it's on in Settings > Network & internet > Advanced network settings > Advanced sharing settings. You can also type \\\\name or \\\\address in the address bar."
                : "This computer has no shared folders you can see."), false);
        }

        void ShowNetworkItems(int v, List<Tuple<string, string>> found, bool network, string emptyText, bool stillLooking)
        {
            var keep = new HashSet<string>(Files.SelectedItems.Cast<FileItem>().Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
            var list = found.Select(t => new FileItem
            {
                Name = t.Item1,
                Path = t.Item2,
                IsFolder = true,
                TypeName = network ? "Computer" : "Shared folder",
                IconKey = network ? "<computer>" : "<share>"
            }).ToList();
            _items = new ObservableCollection<FileItem>(list);
            ShowItems();
            EmptyText.Text = emptyText;
            EmptyText.Visibility = list.Count == 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            // select: what Up came out of (kept until the full list is in), or what was selected in the first, quick list
            HashSet<string> select = _selectNext ?? keep;
            if (!stillLooking) _selectNext = null;
            _restoreSelected = null;
            _restoreScroll = null;
            foreach (FileItem it in _items)
                if (select.Contains(it.Path)) Files.SelectedItems.Add(it);
            if (Files.SelectedItem != null) { Files.ScrollIntoView(Files.SelectedItem); FocusSelected(); }
            if (!stillLooking) UpdateStatus();
            SchedulePreview();
            foreach (FileItem it in list)
            {
                FileItem item = it;
                _icons.Request(item.Path, item.IconKey, () => v == _loadVersion, img => item.Icon = img);
            }
        }

        /// <summary>The sidebar's Network node (its computers are found when it's opened).</summary>
        void BuildNetworkTree()
        {
            NetTree.ItemsSource = new List<FolderNode> { new FolderNode("Network", NetworkPlaces.NetworkPath, 0) };
        }
    }
}
