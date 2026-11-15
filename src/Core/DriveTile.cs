using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace vXplorer.Core
{
    /// <summary>A drive or network location on This PC: its icon, name, and how full it is.</summary>
    public sealed class DriveTile : INotifyPropertyChanged
    {
        public const string Drives = "Devices and drives", Network = "Network locations";

        /// <summary>"C:\", or a network location's folder (\\server\share).</summary>
        public string Path { get; set; }
        /// <summary>Drives or Network (the group it's shown in).</summary>
        public string Section { get; set; }
        /// <summary>Where it sorts: drives by letter, then network locations by name.</summary>
        public string SortKey { get; set; }

        string _name, _kind, _space, _fileSystem;
        ImageSource _icon;
        double _used;
        bool _hasSpace, _low, _drop;
        long _total, _free;

        public string Name { get { return _name; } set { _name = value; Changed("Name"); } }
        /// <summary>"Local Disk", "USB Drive", "CD Drive", "Network drive" …</summary>
        public string Kind { get { return _kind; } set { _kind = value; Changed("Kind"); } }
        public ImageSource Icon { get { return _icon; } set { _icon = value; Changed("Icon"); } }
        /// <summary>"123 GB free of 465 GB", or the kind when the size isn't known.</summary>
        public string SpaceText { get { return _space; } set { _space = value; Changed("SpaceText"); } }
        public string FileSystem { get { return _fileSystem; } set { _fileSystem = value; Changed("FileSystem"); } }
        public long Total { get { return _total; } }
        public long Free { get { return _free; } }

        /// <summary>The size is known: the bar shows.</summary>
        public bool HasSpace { get { return _hasSpace; } }
        /// <summary>Less than a tenth free: the bar is drawn in the warning colour, like Explorer's red.</summary>
        public bool IsLow { get { return _low; } }
        public GridLength UsedPart { get { return new GridLength(_used, GridUnitType.Star); } }
        public GridLength FreePart { get { return new GridLength(1 - _used, GridUnitType.Star); } }

        /// <summary>Files dragged over it would go onto it.</summary>
        public bool IsDropTarget { get { return _drop; } set { if (_drop != value) { _drop = value; Changed("IsDropTarget"); } } }

        public void SetSpace(long total, long free)
        {
            _total = total;
            _free = free;
            _hasSpace = total > 0;
            _used = total > 0 ? System.Math.Max(0, System.Math.Min(1, (total - free) / (double)total)) : 0;
            _low = total > 0 && free < total / 10;
            SpaceText = total > 0 ? Format.Size(free) + " free of " + Format.Size(total) : Kind;
            Changed("HasSpace"); Changed("IsLow"); Changed("UsedPart"); Changed("FreePart");
        }

        void Changed(string name) { var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(name)); }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
