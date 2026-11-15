using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Threading;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>
    /// The sidebar follows the open folder (an option, on by default): the nearest Quick access folder or drive above it is opened down to
    /// it, and it's highlighted and scrolled into view - as Explorer does. Folders the sidebar doesn't list (hidden ones) leave nothing
    /// highlighted.
    /// </summary>
    public partial class MainWindow
    {
        int _revealVersion;

        static bool IsSameOrUnder(string path, string folder)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(folder)) return false;
            string f = folder.TrimEnd('\\');
            return Same(path.TrimEnd('\\'), f) || path.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase);
        }

        void ClearSidebarSelection()
        {
            if (_selectedNode != null) { _selectedNode.IsSelected = false; _selectedNode = null; }
        }

        async void RevealInSidebar(string path)
        {
            int v = ++_revealVersion;
            ShowThisPcRowOpen(IsThisPc(path));
            if (!_settings.SidebarFollows || path == null || IsThisPc(path)) { if (_settings.SidebarFollows) ClearSidebarSelection(); return; }
            if (IsNetwork(path)) path = NetworkPlaces.NetworkPath;

            // where to start: the closest Quick access folder or drive that holds it (Documents rather than your user folder or C:)
            var roots = new List<Tuple<TreeView, FolderNode>>();
            foreach (FolderNode n in (QuickTree.ItemsSource as IEnumerable<FolderNode>) ?? Enumerable.Empty<FolderNode>()) roots.Add(Tuple.Create(QuickTree, n));
            foreach (FolderNode n in (DriveTree.ItemsSource as IEnumerable<FolderNode>) ?? Enumerable.Empty<FolderNode>()) roots.Add(Tuple.Create(DriveTree, n));
            Tuple<TreeView, FolderNode> start = roots.Where(r => IsSameOrUnder(path, r.Item2.Path)).OrderByDescending(r => r.Item2.Path.TrimEnd('\\').Length).FirstOrDefault();
            // the network: Network itself, or (\\computer\share …) down through Network - but only once Network has been opened, so following
            // a folder never starts a search for computers by itself
            FolderNode net = ((NetTree.ItemsSource as IEnumerable<FolderNode>) ?? Enumerable.Empty<FolderNode>()).FirstOrDefault();
            if (start == null && net != null && (IsNetwork(path) || (path.StartsWith(@"\\") && net.IsExpanded))) start = Tuple.Create(NetTree, net);
            if (start == null) { ClearSidebarSelection(); return; }

            // down to it, a level at a time (each read as it's opened)
            var chain = new List<FolderNode> { start.Item2 };
            FolderNode node = start.Item2;
            while (!Same(node.Path.TrimEnd('\\'), path.TrimEnd('\\')))
            {
                await node.EnsureLoaded();
                if (v != _revealVersion) return;   // moved on meanwhile
                FolderNode next = node.Children.FirstOrDefault(c => !c.IsPlaceholder && IsSameOrUnder(path, c.Path));
                if (next == null) break;
                node.IsExpanded = true;
                node = next;
                chain.Add(node);
            }
            if (!Same(node.Path.TrimEnd('\\'), path.TrimEnd('\\'))) { ClearSidebarSelection(); return; }

            if (_selectedNode != null && _selectedNode != node) _selectedNode.IsSelected = false;
            _selectedNode = node;
            node.IsSelected = true;   // the tree's SelectedItemChanged sees it's the open folder already: no second navigation

            // scroll it into view once the tree has made its rows
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            if (v != _revealVersion) return;
            ItemsControl ic = start.Item1;
            TreeViewItem row = null;
            foreach (FolderNode n in chain)
            {
                ic.UpdateLayout();
                row = ic.ItemContainerGenerator.ContainerFromItem(n) as TreeViewItem;
                if (row == null) return;
                ic = row;
            }
            // just its own line, not the folders open under it
            var line = row.Template.FindName("Row", row) as System.Windows.FrameworkElement;
            (line ?? row).BringIntoView();
        }
    }
}
