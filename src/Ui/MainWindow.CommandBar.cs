using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>
    /// The command bar above the files (like Explorer's and Files'): New ▾, Cut, Copy, Paste, Rename, Delete. Each does what its key does;
    /// the buttons grey out when there's nothing for them (no selection, nothing on the clipboard, This PC).
    /// </summary>
    public partial class MainWindow
    {
        void UpdateCommandBar()
        {
            if (CmdNew == null) return;
            bool folder = _current != null && !InPlace;
            int selected = InPlace ? 0 : Files.SelectedItems.Count;   // computers and shares aren't files
            CmdNew.IsEnabled = folder;
            CmdCut.IsEnabled = selected > 0;
            CmdCopy.IsEnabled = selected > 0 || (_inZip && Files.SelectedItems.Count > 0);   // copying out of a zip is fine
            CmdRename.IsEnabled = selected > 0;
            CmdDelete.IsEnabled = selected > 0;
            CmdPaste.IsEnabled = folder && ClipboardHasFiles();
            CmdSort.IsEnabled = !InThisPc;   // This PC's tiles keep their own order
            CmdExtract.Visibility = ZipToExtract() != null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            CmdCompress.Visibility = selected > 0 && folder ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        void CmdExtract_Click(object sender, RoutedEventArgs e) { ExtractAll(); }
        void CmdCompress_Click(object sender, RoutedEventArgs e) { CompressSelection(); }

        void CmdNew_Click(object sender, RoutedEventArgs e)
        {
            if (NotHere()) return;
            var menu = new ContextMenu { PlacementTarget = CmdNew, Placement = PlacementMode.Bottom, VerticalOffset = 2 };
            FillNewItems(menu.Items);
            menu.IsOpen = true;
        }

        void CmdSort_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = CmdSort, Placement = PlacementMode.Bottom, VerticalOffset = 2 };
            FillSortItems(menu.Items);
            menu.IsOpen = true;
        }

        // the list keeps the keyboard: these act on its selection, then hand focus back so the keys go on working
        void CmdCut_Click(object sender, RoutedEventArgs e) { ToClipboard(true); Files.Focus(); }
        void CmdCopy_Click(object sender, RoutedEventArgs e) { ToClipboard(false); Files.Focus(); }
        void CmdPaste_Click(object sender, RoutedEventArgs e) { Paste(); Files.Focus(); }
        void CmdRename_Click(object sender, RoutedEventArgs e) { BeginRename(Files.SelectedItem as FileItem); }
        void CmdDelete_Click(object sender, RoutedEventArgs e)
        {
            Delete((Keyboard.Modifiers & ModifierKeys.Shift) != 0);   // Shift+click: for good, as Shift+Delete (Windows asks first)
            Files.Focus();
        }

        // ---- Paste follows the clipboard (copied here, in Explorer or anywhere else) ----

        const int WM_CLIPBOARDUPDATE = 0x031D;

        [DllImport("user32.dll")]
        static extern bool AddClipboardFormatListener(IntPtr hwnd);

        void HookClipboard()
        {
            AddClipboardFormatListener(_hwnd);
            HwndSource.FromHwnd(_hwnd).AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (msg == WM_CLIPBOARDUPDATE) UpdateCommandBar();
                return IntPtr.Zero;
            });
        }
    }
}
