using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace vXplorer.Core
{
    /// <summary>A folder in the sidebar tree; its subfolders are read the first time it's expanded.</summary>
    public sealed class FolderNode : INotifyPropertyChanged
    {
        public string Name { get; private set; }
        public string Path { get; private set; }
        public int Depth { get; private set; }
        public bool IsPlaceholder { get; private set; }
        public Thickness IndentMargin { get { return new Thickness(Depth * 14, 0, 0, 0); } }
        public ObservableCollection<FolderNode> Children { get; private set; }

        /// <summary>Asks for the node's icon (the sidebar hands it to the icon loader).</summary>
        public static Action<FolderNode> RequestIcon;

        bool _expanded, _loaded, _selected;
        ImageSource _icon;

        public FolderNode(string name, string path, int depth)
        {
            Name = name;
            Path = path;
            Depth = depth;
            Children = new ObservableCollection<FolderNode>();
            Children.Add(Placeholder(depth + 1));   // shows the arrow until we know
            if (RequestIcon != null) RequestIcon(this);
        }

        FolderNode() { Children = new ObservableCollection<FolderNode>(); }

        static FolderNode Placeholder(int depth)
        {
            return new FolderNode { Name = "Loading…", IsPlaceholder = true, Depth = depth, _loaded = true };
        }

        public bool IsExpanded
        {
            get { return _expanded; }
            set
            {
                if (_expanded == value) return;
                _expanded = value;
                Changed("IsExpanded");
                if (value && !_loaded) EnsureLoaded();
            }
        }

        Task _loading;

        /// <summary>The subfolders, read if they haven't been (for the sidebar following the open folder).</summary>
        public Task EnsureLoaded()
        {
            if (!_loaded) _loading = Load();
            return _loading ?? Task.CompletedTask;
        }

        public bool IsSelected
        {
            get { return _selected; }
            set { if (_selected == value) return; _selected = value; Changed("IsSelected"); }
        }

        public ImageSource Icon
        {
            get { return _icon; }
            set { _icon = value; Changed("Icon"); }
        }

        bool _dropTarget;
        /// <summary>Files dragged over this folder would go into it.</summary>
        public bool IsDropTarget
        {
            get { return _dropTarget; }
            set { if (_dropTarget != value) { _dropTarget = value; Changed("IsDropTarget"); } }
        }

        async Task Load()
        {
            _loaded = true;
            string path = Path;
            List<Tuple<string, string>> subs;   // (name, path)
            if (NetworkPlaces.IsNetwork(path))
            {
                // only now, when Network is opened: the mapped drives' computers straight away, then the ones a search finds
                var known = await Task.Run(() => NetworkPlaces.KnownComputers());
                if (known.Count > 0)
                {
                    Children.Clear();
                    foreach (var k in known) Children.Add(new FolderNode(k.Item1, k.Item2, Depth + 1));
                }
                List<Tuple<string, string>> all = await NetworkPlaces.AllComputers();
                if (known.Count == 0) Children.Clear();   // (the placeholder)
                // add only the newly found ones: the known computers stay as they are (one may have been opened meanwhile)
                foreach (var c in all)
                    if (!known.Any(k => string.Equals(k.Item2, c.Item2, StringComparison.OrdinalIgnoreCase)))
                        Children.Add(new FolderNode(c.Item1, c.Item2, Depth + 1));
                return;
            }
            else subs = await Task.Run(() =>
            {
                var r = new List<Tuple<string, string>>();
                try
                {
                    if (NetworkPlaces.IsServer(path)) return NetworkPlaces.Shares(path);   // a computer: its shared folders
                    var opts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
                    foreach (string d in Directory.EnumerateDirectories(path, "*", opts)) r.Add(Tuple.Create(System.IO.Path.GetFileName(d), d));
                }
                catch (Exception ex) { Log.Write("Tree " + path + ": " + ex.Message); }
                r.Sort((a, b) => Native.CompareNatural(a.Item1, b.Item1));
                return r;
            });
            Children.Clear();
            foreach (var s in subs) Children.Add(new FolderNode(s.Item1, s.Item2, Depth + 1));
        }

        /// <summary>Read the subfolders again next time it's expanded (or now, if it is).</summary>
        public void Invalidate()
        {
            if (IsPlaceholder) return;
            _loaded = false;
            if (_expanded) EnsureLoaded();
        }

        void Changed(string name) { var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(name)); }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
