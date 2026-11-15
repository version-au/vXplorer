using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;

namespace vXplorer.Core
{
    /// <summary>One row of the file list.</summary>
    public sealed class FileItem : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool IsFolder { get; set; }
        public DateTime Modified { get; set; }
        /// <summary>Bytes; -1 for folders.</summary>
        public long Size { get; set; }
        public string TypeName { get; set; }
        /// <summary>Items with the same key share an icon (one per extension); null = this file's own icon (.exe, .lnk …).</summary>
        public string IconKey { get; set; }

        /// <summary>Options > Show file name extensions (set by the window; the list is reloaded when it changes).</summary>
        public static bool ShowExtensions = true;

        /// <summary>The extension is left off the name shown (a type Windows knows, with extensions hidden), as in Explorer.</summary>
        public bool HidesExtension
        {
            get
            {
                if (ShowExtensions || IsFolder || string.IsNullOrEmpty(Name)) return false;
                string ext = System.IO.Path.GetExtension(Name);
                return ext.Length > 1 && ext.Length < Name.Length && Native.IsKnownType(ext);
            }
        }

        /// <summary>The name as the list shows it.</summary>
        public string DisplayName { get { return HidesExtension ? System.IO.Path.GetFileNameWithoutExtension(Name) : Name; } }

        public string ModifiedText { get { return Format.Stamp(Modified); } }

        // ---- the optional columns (right-click a column header) ----
        public DateTime Created { get; set; }
        public string CreatedText { get { return Format.Stamp(Created); } }
        string _dimensions = "";
        long _pixels;
        /// <summary>"1920 x 1080" for pictures and videos, read in the background (FileProps) - "" until then or for anything else.</summary>
        public string Dimensions { get { return _dimensions; } set { _dimensions = value ?? ""; Changed("Dimensions"); } }
        /// <summary>Width x height, for sorting by Dimensions.</summary>
        public long Pixels { get { return _pixels; } set { _pixels = value; } }
        TimeSpan _length;
        /// <summary>How long a song or video is (read in the background); zero = unknown or not media.</summary>
        public TimeSpan Length { get { return _length; } set { _length = value; Changed("LengthText"); } }
        public string LengthText
        {
            get { return _length <= TimeSpan.Zero ? "" : _length.TotalHours >= 1 ? _length.ToString(@"h\:mm\:ss") : _length.ToString(@"m\:ss"); }
        }
        /// <summary>Its group's name when the list is grouped (Type, Date, Size, Name).</summary>
        public string Group { get; set; } = "";
        /// <summary>The group's place in the list (groups show in this order).</summary>
        public int GroupOrder { get; set; }
        /// <summary>The background reader has looked at this one (FileProps).</summary>
        public bool PropsRead;
        public string SizeText { get { return IsFolder ? "" : Format.KiloBytes(Size); } }
        /// <summary>The folder it's in (the search results' Folder column's tooltip).</summary>
        public string Folder { get { return System.IO.Path.GetDirectoryName(Path); } }
        /// <summary>Search results: the folder it's in, from where the search started ("Projects\alpha").</summary>
        public string Where { get; set; }

        ImageSource _icon;
        public ImageSource Icon
        {
            get { return _icon; }
            set { _icon = value; Changed("Icon"); }
        }

        ImageSource _thumb;
        /// <summary>The thumbnail for the icon views (or the icon at that size); null until loaded, or when scrolled away.</summary>
        public ImageSource Thumb
        {
            get { return _thumb; }
            set { _thumb = value; Changed("Thumb"); Changed("HasThumb"); }
        }
        public bool HasThumb { get { return _thumb != null; } }
        /// <summary>The size (px) Thumb was loaded at; a request is on its way; the tile is on screen (read by the thumbnail thread).</summary>
        public int ThumbPx;
        public bool ThumbPending;
        public volatile bool InView;

        bool _renaming, _cut, _dropTarget;
        /// <summary>The name is being edited in place.</summary>
        public bool IsRenaming { get { return _renaming; } set { if (_renaming != value) { _renaming = value; Changed("IsRenaming"); } } }
        /// <summary>Cut to the clipboard: drawn faded until it's pasted.</summary>
        public bool IsCut { get { return _cut; } set { if (_cut != value) { _cut = value; Changed("IsCut"); } } }
        /// <summary>Files dragged over this folder would go into it.</summary>
        public bool IsDropTarget { get { return _dropTarget; } set { if (_dropTarget != value) { _dropTarget = value; Changed("IsDropTarget"); } } }

        void Changed(string name) { var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(name)); }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public static class FolderReader
    {
        /// <summary>Files whose icon is their own rather than their extension's.</summary>
        static readonly HashSet<string> OwnIcon = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".lnk", ".ico", ".url", ".cur", ".ani", ".msc", ".appref-ms", ".scr", ".cpl"
        };

        /// <summary>The folder's contents (throws when the folder itself can't be opened).</summary>
        public static List<FileItem> Read(string folder, bool showHidden)
        {
            var opts = new EnumerationOptions
            {
                IgnoreInaccessible = false,
                RecurseSubdirectories = false,
                AttributesToSkip = showHidden ? 0 : FileAttributes.Hidden | FileAttributes.System,
                ReturnSpecialDirectories = false
            };
            var list = new List<FileItem>();
            foreach (FileSystemInfo fsi in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", opts)) list.Add(Make(fsi));
            return list;
        }

        /// <summary>A list row for a file or folder (also used by search).</summary>
        public static FileItem Make(FileSystemInfo fsi)
        {
            bool dir = fsi is DirectoryInfo;
            long size = -1;
            if (!dir) { try { size = ((FileInfo)fsi).Length; } catch { size = 0; } }
            string ext = dir ? "" : System.IO.Path.GetExtension(fsi.Name);
            DateTime modified, created;
            try { modified = fsi.LastWriteTime; } catch { modified = DateTime.MinValue; }
            try { created = fsi.CreationTime; } catch { created = DateTime.MinValue; }
            return new FileItem
            {
                Name = fsi.Name,
                Path = fsi.FullName,
                IsFolder = dir,
                Modified = modified,
                Created = created,
                Size = size,
                TypeName = Native.TypeName(fsi.Name, dir),
                IconKey = dir ? "<folder>" : OwnIcon.Contains(ext) ? null : "ext:" + ext.ToLowerInvariant()
            };
        }
    }

    public static class Format
    {
        /// <summary>A date and time as the file list shows it (12-hour).</summary>
        public static string Stamp(DateTime d)
        {
            if (d == DateTime.MinValue) return "";
            return d.ToString("d") + " " + d.ToString("h:mm tt");
        }

        /// <summary>Explorer's Size column: whole KB, rounded up.</summary>
        public static string KiloBytes(long bytes)
        {
            if (bytes < 0) return "";
            long kb = (bytes + 1023) / 1024;
            return kb.ToString("N0") + " KB";
        }

        /// <summary>A size for the status bar: B, KB, MB, GB or TB.</summary>
        public static string Size(long bytes)
        {
            string[] units = { "bytes", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return u == 0 ? bytes.ToString("N0") + " bytes" : v.ToString(v < 10 ? "0.0" : "0") + " " + units[u];
        }
    }
}
