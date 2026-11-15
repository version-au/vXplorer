using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace vXplorer.Core
{
    /// <summary>
    /// The Dimensions and Length columns: Windows' file properties (System.Image.Dimensions, System.Video.FrameWidth / Height,
    /// System.Media.Duration), read on a thread of its own (STA, as property handlers expect) only for file types that have them, and only
    /// while those columns are showing. Its own small COM interop - nothing from Vanara, whose PIDL handling has bitten before.
    /// </summary>
    public sealed class FileProps
    {
        static readonly HashSet<string> Pictures = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".heif", ".ico", ".jxr", ".avif", ".dng", ".cr2", ".nef", ".arw" };
        static readonly HashSet<string> Videos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts", ".m2ts", ".3gp", ".flv" };
        static readonly HashSet<string> Audio = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".wma", ".opus", ".aiff", ".alac" };

        public static bool HasProps(string name)
        {
            string ext = System.IO.Path.GetExtension(name ?? "");
            return Pictures.Contains(ext) || Videos.Contains(ext) || Audio.Contains(ext);
        }

        readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();
        readonly Dispatcher _ui;

        public FileProps(Dispatcher ui)
        {
            _ui = ui;
            var t = new Thread(() =>
            {
                foreach (Action a in _queue.GetConsumingEnumerable())
                {
                    try { a(); } catch (Exception ex) { Log.Write("File properties: " + ex.Message); }
                }
            }) { IsBackground = true, Name = "File properties" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        /// <summary>Read the items' dimensions / length; skipped once wanted() is false (moved to another folder).</summary>
        public void Request(IList<FileItem> items, Func<bool> wanted)
        {
            var list = new List<FileItem>();
            foreach (FileItem f in items) if (!f.IsFolder && !f.PropsRead && HasProps(f.Name)) list.Add(f);
            foreach (FileItem f in list)
            {
                FileItem item = f;
                _queue.Add(() =>
                {
                    if (!wanted() || item.PropsRead) return;
                    item.PropsRead = true;
                    string ext = System.IO.Path.GetExtension(item.Name);
                    string dims = null;
                    long px = 0;
                    TimeSpan len = TimeSpan.Zero;
                    IPropertyStore store = Open(item.Path);
                    if (store == null) return;
                    try
                    {
                        if (Pictures.Contains(ext))
                        {
                            uint w = UInt(store, ImageFmt, 3), h = UInt(store, ImageFmt, 4);
                            if (w > 0 && h > 0) { dims = w + " x " + h; px = (long)w * h; }
                        }
                        else if (Videos.Contains(ext))
                        {
                            uint w = UInt(store, VideoFmt, 3), h = UInt(store, VideoFmt, 4);
                            if (w > 0 && h > 0) { dims = w + " x " + h; px = (long)w * h; }
                        }
                        if (Videos.Contains(ext) || Audio.Contains(ext))
                        {
                            ulong ticks = ULong(store, MediaFmt, 3);   // 100 ns units
                            if (ticks > 0) len = TimeSpan.FromTicks((long)ticks);
                        }
                    }
                    finally { Marshal.ReleaseComObject(store); }
                    if (dims == null && len == TimeSpan.Zero) return;
                    _ui.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        if (dims != null) { item.Pixels = px; item.Dimensions = dims; }
                        if (len > TimeSpan.Zero) item.Length = len;
                    }));
                });
            }
        }

        // ---- property system interop ----

        static readonly Guid ImageFmt = new Guid("6444048F-4C8B-11D1-8B70-080036B11A03");   // System.Image.HorizontalSize (3) / VerticalSize (4)
        static readonly Guid VideoFmt = new Guid("64440491-4C8B-11D1-8B70-080036B11A03");   // System.Video.FrameWidth (3) / FrameHeight (4)
        static readonly Guid MediaFmt = new Guid("64440490-4C8B-11D1-8B70-080036B11A03");   // System.Media.Duration (3)

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct PROPERTYKEY { public Guid fmtid; public uint pid; }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        struct PROPVARIANT
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public uint uintVal;
            [FieldOffset(8)] public ulong ulongVal;
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
            [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
            [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
            [PreserveSig] int Commit();
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr bindCtx, int flags, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PROPVARIANT pv);

        static IPropertyStore Open(string path)
        {
            Guid iid = typeof(IPropertyStore).GUID;
            IPropertyStore s;
            return SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0x40 /* GPS_BESTEFFORT */, ref iid, out s) >= 0 ? s : null;
        }

        static uint UInt(IPropertyStore s, Guid fmt, uint pid)
        {
            var k = new PROPERTYKEY { fmtid = fmt, pid = pid };
            PROPVARIANT v;
            if (s.GetValue(ref k, out v) < 0) return 0;
            uint r = v.vt == 19 /* VT_UI4 */ ? v.uintVal : 0;
            PropVariantClear(ref v);
            return r;
        }

        static ulong ULong(IPropertyStore s, Guid fmt, uint pid)
        {
            var k = new PROPERTYKEY { fmtid = fmt, pid = pid };
            PROPVARIANT v;
            if (s.GetValue(ref k, out v) < 0) return 0;
            ulong r = v.vt == 21 /* VT_UI8 */ ? v.ulongVal : 0;
            PropVariantClear(ref v);
            return r;
        }

        public void Dispose() { _queue.CompleteAdding(); }
    }
}
