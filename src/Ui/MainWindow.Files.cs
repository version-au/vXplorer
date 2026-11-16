using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Vanara.PInvoke;
using Vanara.Windows.Shell;
using vXplorer.Core;
using DragEventArgs = System.Windows.DragEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using IDataObject = System.Windows.IDataObject;
using DataObject = System.Windows.DataObject;

namespace vXplorer.Ui
{
    /// <summary>File operations: delete, rename, new folder, the clipboard, drag and drop, the shell's right-click menu and Properties.</summary>
    public partial class MainWindow
    {
        FileOps _ops;
        /// <summary>Items cut to the clipboard (shown faded until pasted or replaced).</summary>
        readonly HashSet<string> _cutPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Selected (and scrolled to) by the next load: new folders, pasted items, the folder Up came out of.</summary>
        HashSet<string> _selectNext;
        /// <summary>Renamed in place once the next load shows it (a new folder).</summary>
        string _renameNext;

        FileItem _renaming;
        TextBox _renameBox;
        bool _renameKeepsExt;
        /// <summary>The folder changed on disk during a rename: refresh once it's done.</summary>
        bool _refreshPending;

        static HashSet<string> PathSet(IEnumerable<string> paths) { return new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase); }

        List<string> SelectedPaths() { return Files.SelectedItems.Cast<FileItem>().Select(f => f.Path).ToList(); }

        // =================================================================
        // Keys (from the window's PreviewKeyDown, not while typing)
        // =================================================================

        bool HandleFileKeys(Key key, ModifierKeys mods)
        {
            bool inList = Files.IsKeyboardFocusWithin;
            const ModifierKeys CtrlShift = ModifierKeys.Control | ModifierKeys.Shift;

            if (mods == ModifierKeys.Control && key == Key.V) { Paste(); return true; }
            if (mods == CtrlShift && key == Key.N) { NewFolder(); return true; }
            if (mods == ModifierKeys.Control && key == Key.Z) { Undo(); return true; }
            if ((mods == ModifierKeys.Control && key == Key.Y) || (mods == CtrlShift && key == Key.Z)) { Redo(); return true; }
            if (mods == ModifierKeys.Alt && key == Key.Enter && InThisPc)
            {
                var d = PcList.SelectedItem as DriveTile;
                ShowProperties(new List<string> { d != null ? d.Path : ThisPcPath });
                return true;
            }
            if (mods == ModifierKeys.Alt && key == Key.Enter) { ShowProperties(inList && Files.SelectedItems.Count > 0 ? SelectedPaths() : new List<string> { _current }); return true; }
            if (!inList) return false;
            // computers / shares aren't files; a zip is read-only (but Ctrl+C copies out of it)
            if (InPlace && (key == Key.Delete || key == Key.F2 || key == Key.X || (key == Key.C && !_inZip))) return true;

            if (mods == ModifierKeys.None && key == Key.Delete) { Delete(false); return true; }
            if (mods == ModifierKeys.Shift && key == Key.Delete) { Delete(true); return true; }
            if (mods == ModifierKeys.None && key == Key.F2) { BeginRename(Files.SelectedItem as FileItem); return true; }
            if (mods == ModifierKeys.Control && key == Key.C) { ToClipboard(false); return true; }
            if (mods == ModifierKeys.Control && key == Key.X) { ToClipboard(true); return true; }
            if (mods == CtrlShift && key == Key.C) { CopyAsPath(); return true; }
            if ((mods == ModifierKeys.Shift && key == Key.F10) || key == Key.Apps) { MenuForSelection(); return true; }
            return false;
        }

        // =================================================================
        // Delete
        // =================================================================

        void Delete(bool permanently)
        {
            List<string> paths = SelectedPaths();
            if (paths.Count == 0) return;
            if (!permanently && _settings.ConfirmRecycle)   // Options > Ask before moving to the Recycle Bin
            {
                string what = paths.Count == 1 ? "“" + Path.GetFileName(paths[0].TrimEnd('\\')) + "”" : paths.Count.ToString("N0") + " items";
                if (!Ask.YesNo(this, "Move to the Recycle Bin?", "Move " + what + " to the Recycle Bin? You can get " + (paths.Count == 1 ? "it" : "them") +
                               " back from there, or with Ctrl+Z.", "Move to Recycle Bin", "Cancel", true)) return;
            }
            UndoStep step = null;
            Operation op = _ops.Delete(paths, permanently, r =>
            {
                if (!permanently) step = RememberOperation(UndoKind.Recycle, r);
                AfterOperation(r);
            });
            if (!permanently) LinkUndo(op, () => step);
        }

        /// <summary>After an operation: show its error, select what it made in this folder (or the search), show the folder as it is now.</summary>
        void AfterOperation(FileOps.Result r)
        {
            if (r.Error != null) Status(r.Error);
            if (_current == null) return;
            List<string> here = r.Created.Where(p => Searching || Same(Path.GetDirectoryName(p), _current)).ToList();
            if (here.Count > 0) _selectNext = PathSet(here);
            Refresh();
        }

        // =================================================================
        // New folder and rename
        // =================================================================

        /// <summary>This PC has no files of its own: say so instead of making / pasting something.</summary>
        bool NotHere()
        {
            if (_current == null) return true;
            if (!InPlace) return false;
            if (_inZip) Status("vXplorer can't change what's in a zip - Extract all first, then work on the folder.");
            else Status("Open a drive or folder first - " + NameOf(_current) + " can't hold files itself.");
            return true;
        }

        void NewFolder()
        {
            if (NotHere()) return;
            string path = Path.Combine(_current, "New folder");
            for (int n = 2; Directory.Exists(path) || File.Exists(path); n++) path = Path.Combine(_current, "New folder (" + n + ")");
            try { Directory.CreateDirectory(path); }
            catch (Exception ex) { Status("vXplorer couldn't make a new folder here: " + ex.Message); return; }
            RememberCreated(new[] { path }, "new folder");
            _selectNext = PathSet(new[] { path });
            _renameNext = path;
            ShowFolderAgain();
        }

        void BeginRename(FileItem item)
        {
            if (item == null) return;
            if (_renaming != null) EndRename(true);
            Files.SelectedItems.Clear();
            Files.SelectedItem = item;
            Files.ScrollIntoView(item);
            _renaming = item;
            item.IsRenaming = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_renaming != item) return;
                var container = Files.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem;
                TextBox box = container == null ? null : FindChild<TextBox>(container);
                if (box == null) { EndRename(false); return; }
                _renameBox = box;
                _renameKeepsExt = item.HidesExtension;   // extensions hidden: type the name, the extension stays, as in Explorer
                box.Text = _renameKeepsExt ? item.DisplayName : item.Name;
                box.Focus();
                int dot = item.IsFolder || _renameKeepsExt ? -1 : item.Name.LastIndexOf('.');
                if (dot > 0) box.Select(0, dot);   // the name without its extension, like Explorer
                else box.SelectAll();
            }));
        }

        void RenameBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Escape)
            {
                e.Handled = true;
                EndRename(e.Key == Key.Enter);
                FocusSelected();
            }
        }

        void RenameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) { EndRename(true); }

        void EndRename(bool commit)
        {
            FileItem item = _renaming;
            if (item == null) return;
            _renaming = null;
            string text = _renameBox != null ? _renameBox.Text : item.Name;
            if (_renameBox != null && _renameKeepsExt) text = text.Trim() + Path.GetExtension(item.Name);
            _renameBox = null;
            item.IsRenaming = false;
            if (commit) Rename(item, text);
            if (_refreshPending) { _refreshPending = false; Changed(); }
        }

        void Rename(FileItem item, string newName)
        {
            newName = (newName ?? "").Trim().TrimEnd('.');
            if (newName.Length == 0 || newName == item.Name) return;
            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                Status("A file name can't contain any of these characters:  \\ / : * ? \" < > |");
                return;
            }
            if (!item.IsFolder && !string.Equals(Path.GetExtension(item.Name), Path.GetExtension(newName), StringComparison.OrdinalIgnoreCase))
            {
                if (!Ask.YesNo(this, "Change the extension?",
                               "If you change a file name extension (" + Path.GetExtension(item.Name) + " to " +
                               (Path.GetExtension(newName).Length > 0 ? Path.GetExtension(newName) : "none") + "), the file might not open any more.",
                               "Change it", "Keep it", false)) return;
            }
            _ops.Rename(item.Path, newName, r =>
            {
                RememberOperation(UndoKind.Rename, r);
                AfterOperation(r);
            });
        }

        // =================================================================
        // Clipboard (Explorer's format, so copy / paste works both ways)
        // =================================================================

        static DataObject FileData(IList<string> paths, DragDropEffects preferred)
        {
            var data = new DataObject();
            var list = new StringCollection();
            list.AddRange(paths.ToArray());
            data.SetFileDropList(list);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((int)preferred)));
            return data;
        }

        void ToClipboard(bool cut)
        {
            List<string> paths = SelectedPaths();
            if (paths.Count == 0) return;
            if (_inZip)
            {
                // out of a zip: copies, taken out to a temporary folder (nothing can be cut out of a zip)
                cut = false;
                paths = ExtractToTemp(Files.SelectedItems.Cast<FileItem>().ToList());
                if (paths.Count == 0) return;
            }
            DataObject data = FileData(paths, cut ? DragDropEffects.Move : DragDropEffects.Copy | DragDropEffects.Link);
            if (!TryClipboard(() => Clipboard.SetDataObject(data, true))) return;
            _cutPaths.Clear();
            foreach (FileItem f in _items) f.IsCut = false;
            if (cut)
            {
                foreach (string p in paths) _cutPaths.Add(p);
                foreach (FileItem f in Files.SelectedItems) f.IsCut = true;
            }
            string what = paths.Count == 1 ? "1 item" : paths.Count + " items";
            Status((cut ? "Cut " : "Copied ") + what + " - paste with Ctrl+V here or in Explorer.");
        }

        void CopyAsPath()
        {
            List<string> paths = SelectedPaths();
            if (paths.Count == 0) return;
            string text = string.Join(Environment.NewLine, paths.Select(p => "\"" + p + "\""));
            if (TryClipboard(() => Clipboard.SetText(text))) Status(paths.Count == 1 ? "Copied its path." : "Copied their paths.");
        }

        void Paste()
        {
            if (NotHere()) return;
            string[] files = null;
            int effect = (int)(DragDropEffects.Copy | DragDropEffects.Link);
            bool ok = TryClipboard(() =>
            {
                IDataObject data = Clipboard.GetDataObject();
                if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return;
                files = data.GetData(DataFormats.FileDrop) as string[];
                var s = data.GetData("Preferred DropEffect") as MemoryStream;
                if (s != null && s.Length >= 4)
                {
                    var b = new byte[4];
                    s.Read(b, 0, 4);
                    effect = BitConverter.ToInt32(b, 0);
                }
            });
            if (!ok) return;
            if (files == null || files.Length == 0) { Status("There's nothing to paste."); return; }
            bool move = (effect & (int)DragDropEffects.Move) != 0 && (effect & (int)DragDropEffects.Copy) == 0;
            Transfer(files, _current, move, move);
        }

        /// <summary>The clipboard can be busy for a moment (another app has it open): try a few times.</summary>
        bool TryClipboard(Action a)
        {
            for (int i = 0; i < 5; i++)
            {
                try { a(); return true; }
                catch (ExternalException) { Thread.Sleep(60); }
            }
            Status("The clipboard is busy - try again.");
            return false;
        }

        /// <summary>Copy or move files into dest. fromCut: they came from a Cut, so the clipboard is emptied afterwards (as Explorer does).</summary>
        void Transfer(IList<string> sources, string dest, bool move, bool fromCut)
        {
            List<string> list = sources.Where(s => File.Exists(s) || Directory.Exists(s)).ToList();
            if (list.Count == 0) { Status("Those items aren't there any more."); return; }
            if (list.Any(s => IsSameOrInside(dest, s))) { Status("A folder can't go inside itself."); return; }
            if (move) list = list.Where(s => !Same(Path.GetDirectoryName(s), dest)).ToList();
            if (list.Count == 0) return;   // moving into the folder they're already in
            bool sameFolder = !move && list.All(s => Same(Path.GetDirectoryName(s), dest));
            // things with these names already there: ask (in the theme) what to do with each - unless Windows' copy window is wanted
            ICollection<string> keepBoth = sameFolder ? list : null;
            bool replace = false;
            if (!sameFolder && _ops.OwnProgress)
            {
                List<string> clashes = list.Where(s => !Same(Path.GetDirectoryName(s), dest) && PathTaken(Path.Combine(dest, Path.GetFileName(s.TrimEnd('\\'))))).ToList();
                if (clashes.Count > 0)
                {
                    ConflictWindow.Answer a = ConflictWindow.Ask(this, clashes, dest, move);
                    if (a == null) return;   // cancelled
                    list = list.Where(s => !a.Skip.Contains(s)).ToList();
                    if (list.Count == 0) { Status("Nothing to " + (move ? "move" : "copy") + " - every item was skipped."); return; }
                    keepBoth = a.KeepBoth;
                    replace = true;
                }
            }
            UndoStep step = null;
            // folders that go into a folder of the same name that's already there (a merge): undo must leave those alone - taking
            // "the copy" back would take everything that was in that folder before with it
            var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string s in list)
            {
                if (keepBoth != null && keepBoth.Contains(s)) continue;
                string there = Path.Combine(dest, Path.GetFileName(s.TrimEnd('\\')));
                if (Directory.Exists(s) && Directory.Exists(there)) merged.Add(there);
            }
            Operation op = _ops.Transfer(list, dest, move, keepBoth, replace, r =>
            {
                if (fromCut && r.Error == null)
                {
                    TryClipboard(() => Clipboard.Clear());
                    _cutPaths.Clear();
                }
                step = RememberOperation(move ? UndoKind.Move : UndoKind.Copy, r, merged);
                AfterOperation(r);
            });
            LinkUndo(op, () => step);
        }

        static bool IsSameOrInside(string path, string folder)
        {
            if (!Directory.Exists(folder)) return false;
            string f = folder.TrimEnd('\\');
            return Same(path.TrimEnd('\\'), f) || path.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase);
        }

        // =================================================================
        // Drag out (to Explorer, the desktop, other apps or vXplorer itself)
        // =================================================================

        Point _dragStart;
        FileItem _pressItem;
        /// <summary>A press on one item of a multi-selection keeps the selection (to drag them all); it becomes one item on release.</summary>
        bool _collapsePending;

        void Files_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _pressItem = null;
            _collapsePending = false;
            var src = e.OriginalSource as DependencyObject;
            if (ContainerOf<TextBox>(src) != null || ContainerOf<GridViewColumnHeader>(src) != null || ContainerOf<ScrollBar>(src) != null) return;
            FileItem item = ItemAt(src);
            if (item == null)
            {
                if (BeginBand(e)) e.Handled = true;   // the empty part: a selection box
                return;
            }
            _pressItem = item;
            _dragStart = e.GetPosition(Files);
            // click again on the name of the one selected item = rename it (after the double-click time, as in Explorer)
            CancelSlowClick();
            if (e.ClickCount == 1 && Keyboard.Modifiers == ModifierKeys.None && !_settings.SingleClick && !InPlace && _renaming == null &&
                Files.SelectedItems.Count == 1 && Files.SelectedItem == item && IsOnName(src) &&
                unchecked(Environment.TickCount - _activatedAt) > 300)   // not the click that brought the window to the front
                _slowClickItem = item;
            if (e.ClickCount == 1 && Keyboard.Modifiers == ModifierKeys.None && Files.SelectedItems.Count > 1 && Files.SelectedItems.Contains(item))
            {
                _collapsePending = true;
                e.Handled = true;
            }
        }

        /// <summary>The selected item whose name was clicked again: renamed once the double-click time passes with no second click.</summary>
        FileItem _slowClickItem;
        DispatcherTimer _slowClickTimer;
        int _activatedAt;

        /// <summary>The press was on the item's name text (not its icon or another column), as Explorer asks for a click-to-rename.</summary>
        static bool IsOnName(DependencyObject src)
        {
            for (DependencyObject d = src; d != null && !(d is ListViewItem); d = System.Windows.Media.VisualTreeHelper.GetParent(d))
                if (d is TextBlock t && t.Name == "NameText") return true;
            return false;
        }

        void StartSlowClickTimer()
        {
            if (_slowClickTimer == null)
            {
                _slowClickTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher);
                _slowClickTimer.Tick += (s, e) =>
                {
                    _slowClickTimer.Stop();
                    FileItem item = _slowClickItem;
                    _slowClickItem = null;
                    if (item == null || !IsActive || _renaming != null || InPlace || Mouse.LeftButton == MouseButtonState.Pressed) return;
                    if (Files.SelectedItems.Count != 1 || Files.SelectedItem != item || !_items.Contains(item)) return;
                    BeginRename(item);
                };
            }
            _slowClickTimer.Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime());
            _slowClickTimer.Stop();
            _slowClickTimer.Start();
        }

        [DllImport("user32.dll")] static extern uint GetDoubleClickTime();

        void CancelSlowClick()
        {
            _slowClickItem = null;
            if (_slowClickTimer != null) _slowClickTimer.Stop();
        }

        void Files_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_banding) { UpdateBand(); return; }
            if (_pressItem == null || e.LeftButton != MouseButtonState.Pressed) return;
            Point p = e.GetPosition(Files);
            if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            List<string> paths = Files.SelectedItems.Contains(_pressItem) ? SelectedPaths() : new List<string> { _pressItem.Path };
            if (_inZip)   // dragged out of a zip: take them out first, and they can only be copied
                paths = ExtractToTemp(Files.SelectedItems.Contains(_pressItem) ? Files.SelectedItems.Cast<FileItem>().ToList() : new List<FileItem> { _pressItem });
            _pressItem = null;
            _collapsePending = false;
            if (paths.Count == 0) return;
            DataObject data = FileData(paths, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            bool previewed = PreviewWin.Active;
            ReleasePreview();   // the place it's dropped may move it
            try { DragDrop.DoDragDrop(Files, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); }
            catch (Exception ex) { Log.Write("Drag: " + ex.Message); }
            ClearDropHighlight();
            if (previewed) SchedulePreview();
        }

        void Files_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_banding) { EndBand(); e.Handled = true; return; }
            FileItem clicked = _pressItem;
            if (_collapsePending && _pressItem != null)
            {
                Files.SelectedItems.Clear();
                Files.SelectedItem = _pressItem;
            }
            _collapsePending = false;
            _pressItem = null;
            if (_slowClickItem != null)
            {
                if (clicked == _slowClickItem) StartSlowClickTimer();   // released on it without dragging
                else _slowClickItem = null;
            }
            // Options > Opening: one click opens (a plain click - Ctrl / Shift still select, a drag still drags)
            if (_settings.SingleClick && clicked != null && Keyboard.Modifiers == ModifierKeys.None && _renaming == null)
            {
                FileItem item = clicked;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => Open(new[] { item })));
            }
        }

        // =================================================================
        // Drop in (onto the list, a folder in it, or a folder in the sidebar)
        // =================================================================

        object _dropHighlight;

        void Files_DragOver(object sender, DragEventArgs e)
        {
            FileItem item = ItemAt(e.OriginalSource as DependencyObject);
            if (item != null && !item.IsFolder) item = null;
            string target = item != null ? item.Path : _current;
            e.Effects = DropEffect(e, target);
            SetDropHighlight(e.Effects != DragDropEffects.None ? item : null);
            e.Handled = true;
        }

        void Files_Drop(object sender, DragEventArgs e)
        {
            FileItem item = ItemAt(e.OriginalSource as DependencyObject);
            if (item != null && !item.IsFolder) item = null;
            DoDrop(e, item != null ? item.Path : _current);
        }

        void Tree_DragOver(object sender, DragEventArgs e)
        {
            FolderNode node = NodeAt(e.OriginalSource as DependencyObject);
            e.Effects = node == null ? DragDropEffects.None : DropEffect(e, node.Path);
            SetDropHighlight(e.Effects != DragDropEffects.None ? node : null);
            e.Handled = true;
        }

        void Tree_Drop(object sender, DragEventArgs e)
        {
            FolderNode node = NodeAt(e.OriginalSource as DependencyObject);
            if (node == null) { e.Effects = DragDropEffects.None; ClearDropHighlight(); return; }
            DoDrop(e, node.Path);
        }

        void Drop_DragLeave(object sender, DragEventArgs e) { ClearDropHighlight(); }

        static FolderNode NodeAt(DependencyObject d)
        {
            var item = ContainerOf<TreeViewItem>(d);
            var node = item == null ? null : item.DataContext as FolderNode;
            return node == null || node.IsPlaceholder ? null : node;
        }

        static string[] DroppedFiles(DragEventArgs e)
        {
            try { return e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null; }
            catch { return null; }
        }

        /// <summary>
        /// Explorer's rules: Ctrl copies, Shift moves; otherwise the same drive moves and another drive copies. None when it would do
        /// nothing (moving into the folder they're in) or can't be done (a folder into itself).
        /// </summary>
        static DragDropEffects DropEffect(DragEventArgs e, string target)
        {
            string[] files = DroppedFiles(e);
            if (files == null || files.Length == 0 || target == null || IsPlace(target)) return DragDropEffects.None;
            if (files.Any(f => IsSameOrInside(target, f))) return DragDropEffects.None;
            bool ctrl = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
            bool shift = (e.KeyStates & DragDropKeyStates.ShiftKey) != 0;
            DragDropEffects effect;
            if (ctrl && !shift) effect = DragDropEffects.Copy;
            else if (shift && !ctrl) effect = DragDropEffects.Move;
            else effect = string.Equals(Path.GetPathRoot(files[0]), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase)
                          ? DragDropEffects.Move : DragDropEffects.Copy;
            if (effect == DragDropEffects.Move && files.All(f => Same(Path.GetDirectoryName(f), target))) return DragDropEffects.None;
            if ((e.AllowedEffects & effect) == 0)
                effect = (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
            return effect;
        }

        void DoDrop(DragEventArgs e, string target)
        {
            DragDropEffects effect = DropEffect(e, target);
            string[] files = DroppedFiles(e);
            ClearDropHighlight();
            e.Handled = true;
            // vXplorer does the copy / move itself, so the source must not delete anything afterwards
            e.Effects = DragDropEffects.None;
            if (effect == DragDropEffects.None || files == null) return;
            Activate();
            Transfer(files, target, effect == DragDropEffects.Move, false);
        }

        void SetDropHighlight(object target)
        {
            if (target == _dropHighlight) return;
            ClearDropHighlight();
            _dropHighlight = target;
            var f = target as FileItem;
            if (f != null) f.IsDropTarget = true;
            var n = target as FolderNode;
            if (n != null) n.IsDropTarget = true;
            var t = target as TabState;
            if (t != null) t.IsDropTarget = true;
            var d = target as DriveTile;
            if (d != null) d.IsDropTarget = true;
        }

        void ClearDropHighlight()
        {
            var f = _dropHighlight as FileItem;
            if (f != null) f.IsDropTarget = false;
            var n = _dropHighlight as FolderNode;
            if (n != null) n.IsDropTarget = false;
            var t = _dropHighlight as TabState;
            if (t != null) t.IsDropTarget = false;
            var d = _dropHighlight as DriveTile;
            if (d != null) d.IsDropTarget = false;
            _dropHighlight = null;
        }

        // =================================================================
        // The shell's own right-click menu, and Properties
        // =================================================================

        void Files_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // right-clicking an item that isn't selected selects just that one, like Explorer
            var src = e.OriginalSource as DependencyObject;
            FileItem item = ItemAt(src);
            if (item != null && !Files.SelectedItems.Contains(item))
            {
                Files.SelectedItems.Clear();
                Files.SelectedItem = item;
            }
            else if (item == null && ContainerOf<GridViewColumnHeader>(src) == null && ContainerOf<ScrollBar>(src) == null)
            {
                Files.SelectedItems.Clear();   // the empty part: the folder's menu, nothing selected
                Files.Focus();
            }
        }

        void Files_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var src = e.OriginalSource as DependencyObject;
            if (ContainerOf<GridViewColumnHeader>(src) != null) { e.Handled = true; ShowColumnMenu(); return; }   // which columns to show
            if (ContainerOf<ScrollBar>(src) != null) return;
            e.Handled = true;
            Point screen = PointToScreen(e.GetPosition(this));
            if (ItemAt(src) == null) ShowBackgroundMenu(screen);
            else ShowShellMenu(SelectedPaths(), screen);
        }

        /// <summary>Shift+F10 / the menu key: the menu under the selected item, or the folder's menu.</summary>
        void MenuForSelection()
        {
            if (Files.SelectedItem == null) { ShowBackgroundMenu(null); return; }
            var c = Files.ItemContainerGenerator.ContainerFromItem(Files.SelectedItem) as ListViewItem;
            Point p = c != null ? c.PointToScreen(new Point(24, c.ActualHeight)) : Files.PointToScreen(new Point(24, 24));
            ShowShellMenu(SelectedPaths(), p);
        }

        /// <summary>Menu commands that only read the file, so the preview pane can keep showing it.</summary>
        static readonly HashSet<string> SafeVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "open", "opennew", "openas", "properties", "copy", "copyaspath", "cut", "print", "runas", "pintohome", "pintostartscreen", "share"
        };

        /// <summary>Windows' menu for paths. canRename: Rename is offered and done in place in the list (items of the list only).</summary>
        void ShowShellMenu(IList<string> paths, Point screen, bool canRename = true)
        {
            if (paths.Count == 0) return;
            bool rename = false;
            WithShellMenu(paths, menu =>
            {
                // folders: vXplorer's own Pin / Unpin, in place of Windows' "Pin to Quick access" (which pins to File Explorer's)
                bool folders = paths.All(p => p != null && !IsThisPc(p) && Directory.Exists(p));
                bool unpin = folders && paths.All(IsPinned);
                if (folders) menu.MenuCreated += (s, e) => AddPinItem(e.MenuHandle.DangerousGetHandle(), unpin);
                Shell32.CMF flags = Shell32.CMF.CMF_NORMAL;
                if (canRename) flags |= Shell32.CMF.CMF_CANRENAME;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) flags |= Shell32.CMF.CMF_EXTENDEDVERBS;
                menu.ShowContextMenu(new POINT((int)screen.X, (int)screen.Y), flags, (hmenu, id, hwnd) =>
                {
                    if (id == PinCmd - 1)   // ours (the callback gets the id less Windows' first id, 1)
                    {
                        var list = paths.ToList();   // after the menu has gone (the sidebar is rebuilt)
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (unpin) foreach (string p in list) UnpinQuick(p);
                            else foreach (string p in list) PinQuick(p);
                        }));
                        return;
                    }
                    string verb = null;
                    try { verb = menu.GetVerbForCommand(id); } catch { }
                    // Rename is ours (the shell only renames inside its own views)
                    if (string.Equals(verb, "rename", StringComparison.OrdinalIgnoreCase)) { rename = true; return; }
                    if (verb == null || !SafeVerbs.Contains(verb)) ReleasePreview();   // Delete, Move to, 7-Zip … may need the file
                    RunMenuItem(menu, id, hwnd, new POINT((int)screen.X, (int)screen.Y));
                }, new HWND(_hwnd));
            });
            if (rename) BeginRename(Files.SelectedItem as FileItem);
        }

        /// <summary>vXplorer's Pin / Unpin in Windows' menu: well above the ids Windows hands out (from 1, a few hundred at most).</summary>
        const int PinCmd = 0x7000;

        /// <summary>
        /// Puts "Pin to Quick access" / "Unpin from Quick access" (vXplorer's) into Windows' menu for folders, where Windows' own Pin
        /// item was (it's taken out - it pins to File Explorer's Quick access), or after Open and Open in new window.
        /// </summary>
        static void AddPinItem(IntPtr h, bool unpin)
        {
            try
            {
                int n = MenuApi.GetMenuItemCount(h), at = Math.Min(2, Math.Max(0, n));
                for (int i = 0; i < n; i++)
                {
                    uint id = MenuApi.GetMenuItemID(h, i);
                    if (id == 0 || id == uint.MaxValue) continue;   // separators and submenus
                    // by its text: asking every item for its verb calls into each app's menu extension, and one crashed doing it
                    var sb = new System.Text.StringBuilder(128);
                    MenuApi.GetMenuStringW(h, (uint)i, sb, sb.Capacity, 0x400);
                    string text = sb.ToString().Replace("&", "");
                    if (text == "Pin to Quick access" || text == "Unpin from Quick access")
                    {
                        MenuApi.DeleteMenu(h, (uint)i, 0x400);   // MF_BYPOSITION
                        at = i;
                        break;
                    }
                }
                MenuApi.InsertMenuW(h, (uint)at, 0x400, (UIntPtr)PinCmd, unpin ? "Unpin from Quick access" : "Pin to Quick access");
            }
            catch (Exception ex) { Log.Write("Pin item: " + ex.Message); }
        }

        static class MenuApi
        {
            [DllImport("user32.dll")] public static extern int GetMenuItemCount(IntPtr menu);
            [DllImport("user32.dll")] public static extern uint GetMenuItemID(IntPtr menu, int pos);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool InsertMenuW(IntPtr menu, uint pos, uint flags, UIntPtr id, string text);
            [DllImport("user32.dll")] public static extern bool DeleteMenu(IntPtr menu, uint pos, uint flags);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetMenuStringW(IntPtr menu, uint item, System.Text.StringBuilder s, int max, uint flags);
        }

        void ShowProperties(IList<string> paths)
        {
            if (paths.Count == 0 || paths[0] == null) return;
            // search results can be in different folders: Windows shows properties for one folder's items at a time
            foreach (var g in paths.Where(p => p != null).GroupBy(p => Path.GetDirectoryName(p.TrimEnd('\\')) ?? "", StringComparer.OrdinalIgnoreCase))
                WithShellMenu(g.ToList(), menu => menu.InvokeVerb("properties", ShowWindowCommand.SW_SHOWNORMAL, new HWND(_hwnd)));
        }

        /// <summary>
        /// Windows' menu for paths (all in one folder, as Explorer's selections are). Built here, not with Vanara's
        /// ShellContextMenu.CreateFromItems: that one's item IDs were freed a second time by the finalizer later on, which corrupted
        /// memory and crashed vXplorer at the next garbage collection (after any right-click, e.g. when the sidebar opened down to a folder).
        /// Each ID here is allocated by SHParseDisplayName and freed exactly once, in finally.
        /// </summary>
        void WithShellMenu(IList<string> paths, Action<ShellContextMenu> use)
        {
            var pidls = new List<IntPtr>();
            object folder = null, ui = null;
            // Windows' menu works in one folder: the items are handed over as names in the first one's folder, so items from other
            // folders (search results) would be taken for same-named items there - the wrong files deleted, cut or sent
            if (paths.Select(p => Path.GetDirectoryName(p.TrimEnd('\\')) ?? "").Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                Status("Windows' menu works on items in one folder at a time - these are in different folders.");
                return;
            }
            try
            {
                foreach (string p in paths)
                {
                    IntPtr pidl;
                    uint attrs;
                    int hr = ShellApi.SHParseDisplayName(p, IntPtr.Zero, out pidl, 0, out attrs);
                    if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                    pidls.Add(pidl);
                }
                // the first item's folder; the others are given as their last ID in it
                Guid iidFolder = typeof(Shell32.IShellFolder).GUID;
                IntPtr child;
                int bound = ShellApi.SHBindToParent(pidls[0], ref iidFolder, out folder, out child);
                if (bound < 0) Marshal.ThrowExceptionForHR(bound);
                var children = new IntPtr[pidls.Count];
                children[0] = child;
                for (int i = 1; i < pidls.Count; i++) children[i] = ShellApi.ILFindLastID(pidls[i]);
                Guid iidMenu = typeof(Shell32.IContextMenu).GUID;
                ((Shell32.IShellFolder)folder).GetUIObjectOf(new HWND(_hwnd), (uint)children.Length, children, iidMenu, IntPtr.Zero, out ui);
                if (ui == null) throw new InvalidOperationException("Windows has no menu for this.");
                using (var menu = new ShellContextMenu((Shell32.IContextMenu)ui))
                    use(menu);
            }
            catch (Exception ex)
            {
                Log.Write("Shell menu: " + ex);
                Status("Windows' menu couldn't be shown: " + ex.Message);
            }
            finally
            {
                try { if (ui != null && Marshal.IsComObject(ui)) Marshal.ReleaseComObject(ui); } catch { }   // ShellContextMenu may have
                try { if (folder != null && Marshal.IsComObject(folder)) Marshal.ReleaseComObject(folder); } catch { }
                foreach (IntPtr pidl in pidls) Marshal.FreeCoTaskMem(pidl);   // ILFree is CoTaskMemFree
            }
        }

        static class ShellApi
        {
            [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
            public static extern int SHParseDisplayName(string name, IntPtr bindCtx, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
            [DllImport("shell32.dll")]
            public static extern int SHBindToParent(IntPtr pidl, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv, out IntPtr pidlLast);
            [DllImport("shell32.dll")]
            public static extern IntPtr ILFindLastID(IntPtr pidl);
        }
    }
}
