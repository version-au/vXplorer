using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;

namespace vXplorer.Core
{
    /// <summary>One tab: its folder, its own back / forward history, and the selection and scroll position to come back to.</summary>
    public sealed class TabState : INotifyPropertyChanged
    {
        public readonly Stack<string> Back = new Stack<string>();
        public readonly Stack<string> Forward = new Stack<string>();
        /// <summary>Selected paths and scroll offset, saved when another tab is shown.</summary>
        public HashSet<string> Selected;
        public double Scroll;

        string _path, _title;
        ImageSource _icon;
        bool _active, _dropTarget;

        public string Path { get { return _path; } set { _path = value; Changed("Path"); } }
        public string Title { get { return _title; } set { _title = value; Changed("Title"); } }
        public ImageSource Icon { get { return _icon; } set { _icon = value; Changed("Icon"); } }
        public bool IsActive { get { return _active; } set { if (_active != value) { _active = value; Changed("IsActive"); } } }
        /// <summary>Files dragged over the tab would go into its folder.</summary>
        public bool IsDropTarget { get { return _dropTarget; } set { if (_dropTarget != value) { _dropTarget = value; Changed("IsDropTarget"); } } }

        void Changed(string name) { var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(name)); }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
