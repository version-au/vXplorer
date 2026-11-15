using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Vanara.PInvoke;
using Vanara.Windows.Shell;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>
    /// The right-click menu on the empty part of a folder: vXplorer's own, in the theme (Sort by, Refresh, Paste, New, Show hidden items,
    /// Properties), with "Show more options" for Windows' full menu - as Windows 11's Explorer does it. Shift+right-click goes straight to
    /// Windows' menu.
    /// </summary>
    public partial class MainWindow
    {
        void ShowBackgroundMenu(Point? screen)
        {
            if (_current == null) return;
            // Shift+right-click (the mouse, not Shift+F10) goes straight to Windows' menu
            if (screen != null && (Keyboard.Modifiers & ModifierKeys.Shift) != 0) { ShowNativeBackgroundMenu(screen.Value); return; }

            var menu = new ContextMenu { PlacementTarget = Files };
            if (screen == null)
            {
                menu.Placement = PlacementMode.Relative;
                menu.HorizontalOffset = 24;
                menu.VerticalOffset = 24;
            }
            else menu.Placement = PlacementMode.MousePoint;

            // View
            var view = Item("View", "", null, null);
            AddViewItems(view.Items);
            menu.Items.Add(view);

            // Sort by
            var sort = Item("Sort by", "", null, null);
            FillSortItems(sort.Items);
            menu.Items.Add(sort);
            menu.Items.Add(Item("Refresh", "", "F5", Refresh));
            menu.Items.Add(new Separator());

            if (UndoText != null) menu.Items.Add(Item(UndoText, null, "Ctrl+Z", Undo));
            if (RedoText != null) menu.Items.Add(Item(RedoText, null, "Ctrl+Y", Redo));
            if (UndoText != null || RedoText != null) menu.Items.Add(new Separator());

            bool canPaste = ClipboardHasFiles();
            menu.Items.Add(Item("Paste", "", "Ctrl+V", Paste, canPaste));
            menu.Items.Add(Item("Paste shortcut", "", null, PasteShortcut, canPaste));
            menu.Items.Add(new Separator());

            // New
            var newMenu = Item("New", "", null, null);
            FillNewItems(newMenu.Items);
            menu.Items.Add(newMenu);
            menu.Items.Add(new Separator());

            menu.Items.Add(Check("Show hidden items", _settings.ShowHidden, ToggleHidden, "Ctrl+H"));
            if (!Searching && !InPlace) menu.Items.Add(PinItem(_current));
            menu.Items.Add(Item("Copy folder path", "", null, () => { if (TryClipboard(() => Clipboard.SetText(_current))) Status("Copied the folder's path."); }));
            menu.Items.Add(Item("Properties", "", "Alt+Enter", () => ShowProperties(new List<string> { _current })));
            menu.Items.Add(new Separator());
            Point at = screen ?? Files.PointToScreen(new Point(24, 24));
            menu.Items.Add(Item("Show more options", "", null, () => ShowNativeBackgroundMenu(at)));

            menu.IsOpen = true;
        }

        /// <summary>Sort by: the columns, then ascending / descending (the background menu and the command bar's Sort button).</summary>
        void FillSortItems(ItemCollection items)
        {
            for (int i = 0; i < SortKeys.Length; i++)
            {
                string key = SortKeys[i];
                // the standard four, and the extra columns when they're showing (or sorted by)
                if (i >= 4 && !_settings.Columns.Contains(key) && _settings.SortKey != key) continue;
                items.Add(Check(Headers[i], _settings.SortKey == key, () => SetSort(key, _settings.SortDescending)));
            }
            items.Add(new Separator());
            items.Add(Check("Ascending", !_settings.SortDescending, () => SetSort(_settings.SortKey, false)));
            items.Add(Check("Descending", _settings.SortDescending, () => SetSort(_settings.SortKey, true)));
            if (!InPlace && !Searching)
            {
                items.Add(new Separator());
                AddGroupItems(items);
            }
        }

        /// <summary>New: Folder, then every type Windows' New menu offers (the background menu and the command bar's New button).</summary>
        void FillNewItems(ItemCollection items)
        {
            var folderItem = Item("Folder", null, "Ctrl+Shift+N", NewFolder);
            SetTypeIcon(folderItem, null, true);
            items.Add(folderItem);
            List<NewItemType> types = ShellNew.Types();
            if (types.Count > 0) items.Add(new Separator());
            foreach (NewItemType t in types)
            {
                NewItemType type = t;
                var mi = Item(t.Name, null, null, () => NewFile(type));
                SetTypeIcon(mi, t.Extension, false);
                items.Add(mi);
            }
        }

        MenuItem Item(string header, string glyph, string gesture, Action click, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture ?? "", IsEnabled = enabled };
            if (glyph != null) mi.Icon = new TextBlock { Text = glyph, Style = (Style)FindResource("vx.MenuGlyph") };
            if (click != null) mi.Click += (s, e) => click();
            return mi;
        }

        MenuItem Check(string header, bool on, Action click, string gesture = null)
        {
            MenuItem mi = Item(header, null, gesture, click);
            mi.IsChecked = on;
            return mi;
        }

        void SetTypeIcon(MenuItem mi, string ext, bool folder)
        {
            var img = new Image { Width = 16, Height = 16 };
            mi.Icon = img;
            _icons.RequestForType(ext, folder, s => img.Source = s);
        }

        void SetSort(string key, bool descending)
        {
            _settings.SortKey = key;
            _settings.SortDescending = descending;
            RememberFolderView();
            UpdateSortHeaders();
            var keep = Files.SelectedItems.Cast<FileItem>().ToList();
            ShowItems();
            foreach (FileItem f in keep) Files.SelectedItems.Add(f);
        }

        bool ClipboardHasFiles()
        {
            bool has = false;
            try { has = Clipboard.ContainsFileDropList(); } catch { }
            return has;
        }

        void NewFile(NewItemType t)
        {
            if (NotHere()) return;
            string path;
            try { path = ShellNew.Create(_current, t); }
            catch (Exception ex) { Status("vXplorer couldn't make a new " + t.Name.ToLowerInvariant() + " here: " + ex.Message); return; }
            RememberCreated(new[] { path }, "new " + t.Name.ToLowerInvariant());
            _selectNext = PathSet(new[] { path });
            _renameNext = path;
            ShowFolderAgain();
        }

        /// <summary>A shortcut here to each file on the clipboard ("name - Shortcut.lnk").</summary>
        void PasteShortcut()
        {
            if (NotHere()) return;
            string[] files = null;
            if (!TryClipboard(() => { if (Clipboard.ContainsFileDropList()) files = Clipboard.GetFileDropList().Cast<string>().ToArray(); })) return;
            if (files == null || files.Length == 0) { Status("There's nothing to paste."); return; }
            var made = new List<string>();
            foreach (string f in files)
            {
                try
                {
                    string name = Path.GetFileName(f.TrimEnd('\\'));
                    if (string.IsNullOrEmpty(name)) name = f.Substring(0, 1) + " drive";
                    string link = ShellNew.Unique(_current, (Directory.Exists(f) ? name : Path.GetFileNameWithoutExtension(f)) + " - Shortcut", ".lnk");
                    using (ShellLink.Create(link, f, null, Path.GetDirectoryName(f), null)) { }
                    made.Add(link);
                }
                catch (Exception ex) { Status("Couldn't make a shortcut to " + Path.GetFileName(f) + ": " + ex.Message); }
            }
            if (made.Count > 0)
            {
                RememberCreated(made, made.Count == 1 ? "new shortcut" : "new shortcuts");
                _selectNext = PathSet(made);
                ShowFolderAgain();
            }
        }

        /// <summary>Windows' own menu for the folder background (New, Paste, Open in Terminal, extensions …).</summary>
        void ShowNativeBackgroundMenu(Point screen)
        {
            if (_current == null) return;
            try
            {
                using (var folder = new ShellFolder(_current))
                {
                    Shell32.IContextMenu cm = folder.GetViewObject<Shell32.IContextMenu>(new HWND(_hwnd));
                    if (cm == null) return;
                    using (var menu = new ShellContextMenu(cm))
                    {
                        Shell32.CMF flags = Shell32.CMF.CMF_NORMAL;
                        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) flags |= Shell32.CMF.CMF_EXTENDEDVERBS;
                        var at = new POINT((int)screen.X, (int)screen.Y);
                        menu.ShowContextMenu(at, flags, (hmenu, id, hwnd) => RunMenuItem(menu, id, hwnd, at, true), new HWND(_hwnd));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("Background menu: " + ex);
                Status("Windows' menu couldn't be shown: " + ex.Message);
            }
        }

        /// <summary>
        /// Runs the item picked in one of Windows' menus, telling Windows the folder that's open and where you clicked - as Explorer
        /// does. Without the folder, an app's own "Open in …" (a registry command with %V, the folder: vTerm's, vXplorer's own) failed
        /// with "No application is associated with the specified file for this operation".
        /// </summary>
        void RunMenuItem(ShellContextMenu menu, int id, HWND hwnd, POINT at, bool background = false)
        {
            if (background && _current != null && Directory.Exists(_current))
            {
                string verb = null;
                try { verb = menu.GetVerbForCommand(id); } catch { }
                if (verb != null && RunFolderCommand(verb, _current)) return;
            }
            var info = new Shell32.CMINVOKECOMMANDINFOEX(id);
            info.hwnd = hwnd;
            info.nShow = ShowWindowCommand.SW_SHOWNORMAL;
            string dir = _current != null && Directory.Exists(_current) ? _current : null;
            if (dir != null) { info.lpDirectory = dir; info.lpDirectoryW = dir; }
            info.ptInvoke = at;
            info.fMask |= Shell32.CMIC.CMIC_MASK_PTINVOKE;
            menu.Invoke(ref info);
        }

        /// <summary>
        /// An app's own item on a folder's background menu ("Open in vTerm", "Open with Code"): a registry command under
        /// Directory\Background\shell\&lt;verb&gt;\command. Run here with the folder filled in (%V / %W / %1 / %L), as Explorer does -
        /// Windows' menu object from the folder view couldn't run them. false = not such a command (Windows runs it).
        /// </summary>
        internal static bool RunFolderCommand(string verb, string folder)
        {
            if (string.IsNullOrWhiteSpace(verb) || verb.IndexOfAny(new[] { '\\', '/' }) >= 0) return false;
            string cmd = null;
            foreach (var root in new[] { Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes"), Microsoft.Win32.Registry.ClassesRoot })
            {
                if (root == null) continue;
                using (root)
                using (var k = root.OpenSubKey(@"Directory\Background\shell\" + verb + @"\command"))
                {
                    if (k == null) continue;
                    if (k.GetValue("DelegateExecute") != null) return false;     // a COM handler: Windows' job
                    cmd = k.GetValue("") as string;     // REG_EXPAND_SZ comes back expanded
                    if (!string.IsNullOrWhiteSpace(cmd)) break;
                }
            }
            if (string.IsNullOrWhiteSpace(cmd)) return false;
            foreach (string token in new[] { "%V", "%v", "%W", "%w", "%1", "%L", "%l" }) cmd = cmd.Replace(token, folder);
            cmd = cmd.Trim();
            string exe, args;
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                if (end < 0) return false;
                exe = cmd.Substring(1, end - 1);
                args = cmd.Substring(end + 1).Trim();
            }
            else
            {
                int sp = cmd.IndexOf(' ');
                exe = sp < 0 ? cmd : cmd.Substring(0, sp);
                args = sp < 0 ? "" : cmd.Substring(sp + 1).Trim();
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = true, WorkingDirectory = folder });
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("Folder menu '" + verb + "': " + ex.Message);
                return false;
            }
        }

        // ---- the sidebar: Windows' menu for the folder ----

        void Tree_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            FolderNode node = NodeAt(e.OriginalSource as DependencyObject);
            if (node == null) return;
            e.Handled = true;
            Point screen = PointToScreen(e.GetPosition(this));
            // a Quick access folder: vXplorer's menu (move, unpin); other folders: Windows' menu
            if (sender == QuickTree && node.Depth == 0 && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) ShowQuickMenu(node, screen);
            else ShowShellMenu(new List<string> { node.Path }, screen, false);
        }
    }
}
