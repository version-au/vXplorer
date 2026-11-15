using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;

namespace vXplorer.Core
{
    /// <summary>Small Win32 / Shell helpers that don't need Vanara.</summary>
    public static class Native
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        const uint SHGFI_DISPLAYNAME = 0x200, SHGFI_TYPENAME = 0x400, SHGFI_USEFILEATTRIBUTES = 0x10;
        const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_NORMAL = 0x80;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int StrCmpLogicalW(string a, string b);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, [MarshalAs(UnmanagedType.LPWStr)] out string path);

        static readonly ConcurrentDictionary<string, string> TypeNames = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly ConcurrentDictionary<string, string> DisplayNames = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Explorer's "Type" text ("Text Document", "File folder" …), cached per extension.</summary>
        public static string TypeName(string name, bool folder)
        {
            string key = folder ? "<folder>" : Path.GetExtension(name);
            if (string.IsNullOrEmpty(key)) key = "<none>";
            return TypeNames.GetOrAdd(key, k =>
            {
                var info = new SHFILEINFO();
                string probe = folder ? "folder" : "file" + (k == "<none>" ? "" : k);
                SHGetFileInfo(probe, folder ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
                              SHGFI_TYPENAME | SHGFI_USEFILEATTRIBUTES);
                if (!string.IsNullOrEmpty(info.szTypeName)) return info.szTypeName;
                return folder ? "File folder" : k == "<none>" ? "File" : k.TrimStart('.').ToUpperInvariant() + " File";
            });
        }

        /// <summary>The name Explorer shows for a path ("Local Disk (C:)", "Documents" …), cached.</summary>
        public static string DisplayName(string path)
        {
            return DisplayNames.GetOrAdd(path, p =>
            {
                var info = new SHFILEINFO();
                SHGetFileInfo(p, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_DISPLAYNAME);
                if (!string.IsNullOrEmpty(info.szDisplayName)) return info.szDisplayName;
                string n = Path.GetFileName(p.TrimEnd('\\'));
                return string.IsNullOrEmpty(n) ? p : n;
            });
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int SHLoadIndirectString(string source, System.Text.StringBuilder buffer, int size, IntPtr reserved);

        /// <summary>A registry text that may point into a resource ("@C:\x.exe,-1000"); null when empty or unreadable.</summary>
        public static string IndirectString(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (!s.StartsWith("@")) return s;
            var b = new System.Text.StringBuilder(512);
            try { if (SHLoadIndirectString(s, b, b.Capacity, IntPtr.Zero) == 0 && b.Length > 0) return b.ToString(); }
            catch { }
            return null;
        }

        static readonly ConcurrentDictionary<string, bool> KnownTypes = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A file type Windows has registered (Explorer hides the extension of these when "Hide extensions" is on).</summary>
        public static bool IsKnownType(string ext)
        {
            return KnownTypes.GetOrAdd(ext, e =>
            {
                try { using (var k = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(e)) return k != null; }
                catch { return false; }
            });
        }

        /// <summary>Explorer's sort order: "file2" before "file10".</summary>
        public static int CompareNatural(string a, string b) { return StrCmpLogicalW(a ?? "", b ?? ""); }

        static readonly Guid DownloadsId = new Guid("374DE290-123F-4565-9164-39C4925E467B");

        public static string DownloadsFolder()
        {
            try
            {
                Guid id = DownloadsId;
                string path;
                if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out path) == 0) return path;
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }
}
