using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>
    /// Shows a file with Windows' preview handler for its type (PDF pages, Word / Excel / PowerPoint, fonts, video …), as Explorer's
    /// preview pane does. The handler draws into a child window of ours; it's always created out of process (CLSCTX_LOCAL_SERVER: Windows'
    /// prevhost.exe or the handler's own server), so a handler that crashes can't take vXplorer down. All the COM calls are made on a
    /// thread of our own, so a slow handler never freezes the window. While a file is shown the handler may keep it open - Release it
    /// before deleting, renaming or moving the file.
    /// </summary>
    public sealed class PreviewHost : HwndHost
    {
        // ---- which handler a file type has ----

        const string IPreviewHandlerId = "{8895b1c6-b41f-4c1c-a562-0d564250836f}";

        /// <summary>The preview handler registered for ext (".pdf"), or null.</summary>
        public static Guid? HandlerFor(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return null;
            try
            {
                var sb = new StringBuilder(64);
                uint n = (uint)sb.Capacity;
                const uint ASSOCF_INIT_DEFAULTTOSTAR = 0x4, ASSOCF_NOTRUNCATE = 0x20;
                const int ASSOCSTR_SHELLEXTENSION = 16;
                if (AssocQueryString(ASSOCF_INIT_DEFAULTTOSTAR | ASSOCF_NOTRUNCATE, ASSOCSTR_SHELLEXTENSION, ext, IPreviewHandlerId, sb, ref n) != 0) return null;
                Guid g;
                return Guid.TryParse(sb.ToString(), out g) ? g : (Guid?)null;
            }
            catch { return null; }
        }

        // ---- the child window the handler draws in ----

        IntPtr _child;
        static WndProcDelegate s_proc;   // kept alive for the window class
        static IntPtr s_brush;
        static string s_class;

        /// <summary>The colour behind the handler (shown while it loads and around handlers that don't fill the window).</summary>
        public void SetBackground(Color c)
        {
            IntPtr old = s_brush;
            s_brush = CreateSolidBrush(ColorRef(c));
            if (old != IntPtr.Zero) DeleteObject(old);
            if (_child != IntPtr.Zero) InvalidateRect(_child, IntPtr.Zero, true);
        }

        protected override HandleRef BuildWindowCore(HandleRef parent)
        {
            if (s_class == null)
            {
                s_proc = ChildProc;
                var wc = new WNDCLASSEX
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_proc),
                    hInstance = GetModuleHandle(null),
                    hCursor = LoadCursor(IntPtr.Zero, 32512),   // IDC_ARROW
                    lpszClassName = "vXplorerPreviewHost"
                };
                RegisterClassEx(ref wc);
                s_class = wc.lpszClassName;
            }
            const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPCHILDREN = 0x02000000, WS_EX_LAYERED = 0x80000, LWA_ALPHA = 0x2;
            // Layered (fully opaque): with Blur / Frosted the window is glass, and a handler drawing with plain GDI (the font previewer)
            // would otherwise show the wallpaper through. A layered child is composited as one opaque surface.
            _child = CreateWindowEx(WS_EX_LAYERED, s_class, "", WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN, 0, 0, 1, 1, parent.Handle, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            if (_child != IntPtr.Zero) SetLayeredWindowAttributes(_child, 0, 255, LWA_ALPHA);
            return new HandleRef(this, _child);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            Shutdown();
            DestroyWindow(hwnd.Handle);
            _child = IntPtr.Zero;
        }

        static IntPtr ChildProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            const uint WM_ERASEBKGND = 0x14;
            if (msg == WM_ERASEBKGND && s_brush != IntPtr.Zero)
            {
                RECT r;
                GetClientRect(hwnd, out r);
                FillRect(wParam, ref r, s_brush);
                return (IntPtr)1;
            }
            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            int w = (int)Math.Round(info.NewSize.Width * dpi.DpiScaleX), h = (int)Math.Round(info.NewSize.Height * dpi.DpiScaleY);
            _w = w; _h = h;
            Post(() =>
            {
                if (_handler == null) return;
                var r = new RECT { right = w, bottom = h };
                try { _handler.SetRect(ref r); } catch { }
            });
        }

        // ---- the handler's thread ----

        Dispatcher _worker;
        volatile int _wanted;
        volatile int _w = 1, _h = 1;
        IPreviewHandler _handler;   // worker thread only
        readonly Frame _frame = new Frame();

        /// <summary>A handler is loaded (or loading).</summary>
        public bool Active { get; private set; }

        void Post(Action a)
        {
            if (_worker == null)
            {
                var ready = new ManualResetEventSlim();
                var t = new Thread(() =>
                {
                    _worker = Dispatcher.CurrentDispatcher;
                    ready.Set();
                    Dispatcher.Run();
                }) { IsBackground = true, Name = "Preview handler" };
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                ready.Wait();
            }
            _worker.BeginInvoke(new Action(() =>
            {
                try { a(); }
                catch (Exception ex) { Log.Write("Preview handler: " + ex.Message); }
            }));
        }

        /// <summary>
        /// Show path with the handler clsid. done(true) is called on the UI thread once it's showing; done(false) if the handler couldn't
        /// show it. Nothing is called if another Load or Release came first.
        /// </summary>
        public void Load(string path, Guid clsid, Color bg, Color fg, double fontPx, Action<bool> done)
        {
            int v = ++_wanted;
            IntPtr hwnd = _child;
            if (hwnd == IntPtr.Zero) { done(false); return; }
            Active = true;
            Dispatcher ui = Dispatcher;
            Post(() =>
            {
                if (v != _wanted) return;
                UnloadNow();
                bool ok = false;
                try { ok = Open(path, clsid, hwnd, bg, fg, fontPx); }
                catch (Exception ex)
                {
                    Log.Write("Preview handler " + clsid.ToString("B") + " for " + System.IO.Path.GetExtension(path) + ": " + ex.Message);
                    UnloadNow();
                }
                ui.BeginInvoke(new Action(() =>
                {
                    if (v != _wanted) return;
                    if (!ok) Active = false;
                    done(ok);
                }));
            });
        }

        /// <summary>Let the shown file go (async). The handler window disappears.</summary>
        public void Unload()
        {
            ++_wanted;
            if (!Active) return;
            Active = false;
            Post(UnloadNow);
        }

        /// <summary>Let the shown file go and wait (up to 3 seconds) until the handler has closed it - before a file operation.</summary>
        public void Release()
        {
            ++_wanted;
            if (!Active || _worker == null) return;
            Active = false;
            try { _worker.Invoke(new Action(UnloadNow), DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(3)); }
            catch (Exception ex) { Log.Write("Preview handler unload: " + ex.Message); }
        }

        /// <summary>vXplorer is closing: unload and stop the thread.</summary>
        public void Shutdown()
        {
            Release();
            if (_worker != null) _worker.BeginInvokeShutdown(DispatcherPriority.Normal);
        }

        bool Open(string path, Guid clsid, IntPtr hwnd, Color bg, Color fg, double fontPx)
        {
            const uint CLSCTX_LOCAL_SERVER = 0x4, STGM_READ = 0;
            Guid unk = new Guid("00000000-0000-0000-C000-000000000046");
            object o;
            int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_LOCAL_SERVER, ref unk, out o);
            if (hr != 0 || o == null) { Log.Write("Preview handler " + clsid.ToString("B") + " didn't start: 0x" + hr.ToString("X8")); return false; }
            var handler = o as IPreviewHandler;
            if (handler == null) { Marshal.FinalReleaseComObject(o); return false; }
            _handler = handler;

            // hand it the file: as a shell item, a path or a stream - whichever it takes (in Explorer's order)
            if (o is IInitializeWithItem)
            {
                Guid iid = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");   // IShellItem
                IntPtr item;
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out item));
                try { ((IInitializeWithItem)o).Initialize(item, STGM_READ); }
                finally { Marshal.Release(item); }
            }
            else if (o is IInitializeWithFile)
            {
                ((IInitializeWithFile)o).Initialize(path, STGM_READ);
            }
            else if (o is IInitializeWithStream)
            {
                const uint STGM_SHARE_DENY_NONE = 0x40;
                IntPtr stream;
                Marshal.ThrowExceptionForHR(SHCreateStreamOnFileEx(path, STGM_READ | STGM_SHARE_DENY_NONE, 0, false, IntPtr.Zero, out stream));
                try { ((IInitializeWithStream)o).Initialize(stream, STGM_READ); }
                finally { Marshal.Release(stream); }
            }
            else return false;

            // the theme's colours and font, for the handlers that take them (text, fonts, mail …; PDF and Office draw their own)
            var vis = o as IPreviewHandlerVisuals;
            if (vis != null)
            {
                try
                {
                    vis.SetBackgroundColor(ColorRef(bg));
                    vis.SetTextColor(ColorRef(fg));
                    var lf = new LOGFONTW { lfHeight = -(int)Math.Round(fontPx), lfWeight = 400, lfCharSet = 1, lfQuality = 5, lfFaceName = "Segoe UI" };
                    vis.SetFont(ref lf);
                }
                catch { }
            }
            var site = o as IObjectWithSite;
            if (site != null) { try { site.SetSite(_frame); } catch { } }

            var r = new RECT { right = _w, bottom = _h };
            handler.SetWindow(hwnd, ref r);
            handler.SetRect(ref r);
            handler.DoPreview();
            return true;
        }

        void UnloadNow()
        {
            IPreviewHandler h = _handler;
            if (h == null) return;
            _handler = null;
            try { h.Unload(); } catch { }
            try { var site = h as IObjectWithSite; if (site != null) site.SetSite(null); } catch { }
            try { Marshal.FinalReleaseComObject(h); } catch { }
        }

        static uint ColorRef(Color c) { return (uint)(c.R | (c.G << 8) | (c.B << 16)); }

        // ---- COM ----

        [ComImport, Guid("8895b1c6-b41f-4c1c-a562-0d564250836f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPreviewHandler
        {
            void SetWindow(IntPtr hwnd, ref RECT rect);
            void SetRect(ref RECT rect);
            void DoPreview();
            void Unload();
            void SetFocus();
            void QueryFocus(out IntPtr hwnd);
            [PreserveSig] int TranslateAccelerator(ref MSG msg);
        }

        [ComImport, Guid("196bf9a5-b346-4ef0-aa1e-5dcdb76768b1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPreviewHandlerVisuals
        {
            void SetBackgroundColor(uint color);
            void SetFont(ref LOGFONTW font);
            void SetTextColor(uint color);
        }

        [ComImport, Guid("fec87aaf-35f9-447a-adb7-20234491401a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IPreviewHandlerFrame
        {
            [PreserveSig] int GetWindowContext(IntPtr info);
            [PreserveSig] int TranslateAccelerator(ref MSG msg);
        }

        [ComImport, Guid("7f73be3f-fb79-493c-a6c7-7ee14e245841"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IInitializeWithItem { void Initialize(IntPtr shellItem, uint mode); }

        [ComImport, Guid("b7d14566-0509-4cce-a71f-0a554233bd9b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IInitializeWithFile { void Initialize([MarshalAs(UnmanagedType.LPWStr)] string path, uint mode); }

        [ComImport, Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IInitializeWithStream { void Initialize(IntPtr stream, uint mode); }

        [ComImport, Guid("FC4801A3-2BA9-11CF-A229-00AA003D7352"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IObjectWithSite
        {
            void SetSite([MarshalAs(UnmanagedType.IUnknown)] object site);
            void GetSite(ref Guid iid, out IntPtr site);
        }

        /// <summary>The site handed to handlers: no accelerator table, keys stay with the handler.</summary>
        [ComVisible(true)]
        public sealed class Frame : IPreviewHandlerFrame
        {
            public int GetWindowContext(IntPtr info) { return unchecked((int)0x80004001); }   // E_NOTIMPL
            public int TranslateAccelerator(ref MSG msg) { return 1; }                          // S_FALSE
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct LOGFONTW
        {
            public int lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight;
            public byte lfItalic, lfUnderline, lfStrikeOut, lfCharSet, lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string lfFaceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASSEX
        {
            public uint cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; public IntPtr hIconSm;
        }

        delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int AssocQueryString(uint flags, int str, string assoc, string extra, StringBuilder outStr, ref uint outLen);

        [DllImport("ole32.dll")]
        static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object obj);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr bindCtx, ref Guid iid, out IntPtr item);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateStreamOnFileEx(string file, uint mode, uint attributes, bool create, IntPtr template, out IntPtr stream);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateWindowEx(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

        [DllImport("user32.dll")]
        static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

        [DllImport("user32.dll")]
        static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool GetClientRect(IntPtr hwnd, out RECT r);

        [DllImport("user32.dll")]
        static extern int FillRect(IntPtr hdc, ref RECT r, IntPtr brush);

        [DllImport("user32.dll")]
        static extern bool InvalidateRect(IntPtr hwnd, IntPtr r, bool erase);

        [DllImport("user32.dll")]
        static extern IntPtr LoadCursor(IntPtr inst, int id);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string name);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateSolidBrush(uint color);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr obj);
    }
}
