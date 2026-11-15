using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Vanara.PInvoke;
using Vanara.Windows.Shell;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>
    /// Copy, move, delete and rename through Windows' own copy engine (IFileOperation, via Vanara): the same progress window, conflict
    /// questions, Recycle Bin and undo as Explorer. Each operation runs on a thread of its own so the window stays responsive; done is
    /// called on the UI thread with the paths that were created (copies, moved items, renamed items) and an error message or null.
    /// </summary>
    public sealed class FileOps
    {
        const int UserCancelled = unchecked((int)0x80270000);   // COPYENGINE_E_USER_CANCELLED
        const int Cancelled = unchecked((int)0x800704C7);       // HRESULT_FROM_WIN32(ERROR_CANCELLED)
        static readonly Guid FileOperationClsid = new Guid("3ad05575-8857-4850-9277-11b85bdb8e09");   // CLSID_FileOperation

        readonly Window _owner;
        readonly Dispatcher _ui;

        const int SHCNE_CREATE = 0x2, SHCNE_MKDIR = 0x8, SHCNE_UPDATEDIR = 0x1000;
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);
        /// <summary>Tell Explorer, the desktop and the Recycle Bin's window that something changed (SHCNF_PATHW).</summary>
        static void Notify(int what, string path) { try { SHChangeNotify(what, 0x5, path, IntPtr.Zero); } catch { } }

        public FileOps(Window owner)
        {
            _owner = owner;
            _ui = owner.Dispatcher;
        }

        /// <summary>What an operation did (for selecting the results and for Ctrl+Z).</summary>
        public sealed class Result
        {
            /// <summary>Paths that were made: copies, moved and renamed items (top-level items only).</summary>
            public readonly List<string> Created = new List<string>();
            /// <summary>(old path, new path) for each top-level item copied, moved or renamed.</summary>
            public readonly List<Tuple<string, string>> Pairs = new List<Tuple<string, string>>();
            /// <summary>Top-level items deleted.</summary>
            public readonly List<string> Deleted = new List<string>();
            /// <summary>When the operation started (to find its items in the Recycle Bin).</summary>
            public DateTime Started;
            public string Error;
        }

        /// <summary>
        /// Copies, moves and deletes report to vXplorer's operations panel (off: Options > Files > Use Windows' copy window, and they
        /// show Windows' progress window as before).
        /// </summary>
        public bool OwnProgress = true;

        Operation NewOp(string kind, IList<string> paths, string dest)
        {
            if (!OwnProgress) return null;
            var op = new Operation { Kind = kind, TopCount = paths.Count, Owner = _owner as MainWindow };
            switch (kind)
            {
                case "copy": op.Verb = "Copying"; op.DoneVerb = "Copied"; break;
                case "move": op.Verb = "Moving"; op.DoneVerb = "Moved"; break;
                case "recycle": op.Verb = "Moving to the Recycle Bin:"; op.DoneVerb = "Moved to the Recycle Bin:"; break;
                default: op.Verb = "Deleting"; op.DoneVerb = "Deleted"; break;
            }
            if (dest != null)
            {
                op.TargetPath = dest;
                op.Target = Native.DisplayName(dest);
                op.Root = Path.GetPathRoot(dest);
            }
            return op;
        }

        static Operation HiddenOp() { return new Operation { Kind = "", Hidden = true }; }

        static Tuple<ShellFileOperations.OperationFlags, Action<ShellFileOperations, List<IDisposable>>> Step(
            ShellFileOperations.OperationFlags flags, Action<ShellFileOperations, List<IDisposable>> queue)
        {
            return Tuple.Create(flags, queue);
        }

        /// <summary>To the Recycle Bin, or gone for good (Windows asks first, as in Explorer).</summary>
        public Operation Delete(IList<string> paths, bool permanently, Action<Result> done)
        {
            var flags = ShellFileOperations.OperationFlags.NoConfirmMkDir;
            flags |= permanently ? ShellFileOperations.OperationFlags.WantNukeWarning
                                 : ShellFileOperations.OperationFlags.AllowUndo | ShellFileOperations.OperationFlags.RecycleOnDelete;
            Operation op = NewOp(permanently ? "delete" : "recycle", paths, null);
            Run(op, new[] { Step(flags, (o, keep) => o.QueueDeleteOperation(Items(paths, keep))) }, paths, done);
            return op;
        }

        /// <summary>
        /// Copy or move into dest. keepBoth: items that get a new name ("photo (2).jpg") because something with their name is there
        /// (copying into the same folder: all of them). replace: the rest replace (or merge into) what's there without asking.
        /// </summary>
        public Operation Transfer(IList<string> paths, string dest, bool move, ICollection<string> keepBoth, bool replace, Action<Result> done)
        {
            var baseFlags = ShellFileOperations.OperationFlags.AllowUndo | ShellFileOperations.OperationFlags.NoConfirmMkDir;
            var both = paths.Where(p => keepBoth != null && keepBoth.Contains(p)).ToList();
            var rest = paths.Where(p => keepBoth == null || !keepBoth.Contains(p)).ToList();
            var steps = new List<Tuple<ShellFileOperations.OperationFlags, Action<ShellFileOperations, List<IDisposable>>>>();
            Action<IList<string>, ShellFileOperations.OperationFlags> add = (list, flags) =>
            {
                if (list.Count == 0) return;
                steps.Add(Step(flags, (o, keep) =>
                {
                    var folder = new ShellFolder(dest);
                    keep.Add(folder);
                    List<ShellItem> items = Items(list, keep);
                    if (move) o.QueueMoveOperation(items, folder);
                    else o.QueueCopyOperation(items, folder);
                }));
            };
            add(rest, baseFlags | (replace ? ShellFileOperations.OperationFlags.NoConfirmation : 0));
            add(both, baseFlags | ShellFileOperations.OperationFlags.RenameOnCollision | ShellFileOperations.OperationFlags.PreserveFileExtensions);
            Operation op = NewOp(move ? "move" : "copy", paths, dest);
            if (op != null) op.Journal = new CopyJournal { Kind = move ? "move" : "copy", Dest = dest, Sources = paths.ToList(), KeepBoth = both };
            Run(op, steps, paths, done);
            return op;
        }

        /// <summary>
        /// Copy or move single items into their own folders, replacing what's there (finishing a copy that stopped part way, Try again).
        /// entries: (item, destination folder); the folders are made first.
        /// </summary>
        public Operation TransferEach(IList<Tuple<string, string>> entries, bool move, string target, Action<Result> done)
        {
            var flags = ShellFileOperations.OperationFlags.AllowUndo | ShellFileOperations.OperationFlags.NoConfirmMkDir | ShellFileOperations.OperationFlags.NoConfirmation;
            List<string> sources = entries.Select(e => e.Item1).ToList();
            Operation op = NewOp(move ? "move" : "copy", sources, target);
            Run(op, new[] { Step(flags, (o, keep) =>
            {
                var folders = new Dictionary<string, ShellFolder>(StringComparer.OrdinalIgnoreCase);
                foreach (Tuple<string, string> e in entries)
                {
                    ShellFolder f;
                    if (!folders.TryGetValue(e.Item2, out f))
                    {
                        Directory.CreateDirectory(e.Item2);
                        f = new ShellFolder(e.Item2);
                        keep.Add(f);
                        folders[e.Item2] = f;
                    }
                    var item = new ShellItem(e.Item1);
                    keep.Add(item);
                    if (move) o.QueueMoveOperation(item, f); else o.QueueCopyOperation(item, f);
                }
            }) }, sources, done);
            return op;
        }

        public void Rename(string path, string newName, Action<Result> done)
        {
            Run(OwnProgress ? HiddenOp() : null, new[] { Step(ShellFileOperations.OperationFlags.AllowUndo,
                (op, keep) => op.QueueRenameOperation(Items(new[] { path }, keep)[0], newName)) }, new[] { path }, done);
        }

        /// <summary>Move each item (path, folder, name) into folder under name - undoing a move.</summary>
        public void MoveBack(IList<Tuple<string, string, string>> moves, Action<Result> done)
        {
            var flags = ShellFileOperations.OperationFlags.AllowUndo | ShellFileOperations.OperationFlags.NoConfirmMkDir;
            Run(OwnProgress ? HiddenOp() : null, new[] { Step(flags, (op, keep) =>
            {
                foreach (Tuple<string, string, string> m in moves)
                {
                    var item = new ShellItem(m.Item1);
                    var folder = new ShellFolder(m.Item2);
                    keep.Add(item);
                    keep.Add(folder);
                    op.QueueMoveOperation(item, folder, m.Item3);
                }
            }) }, moves.Select(m => m.Item1).ToList(), done);
        }

        /// <summary>
        /// Put items deleted to the Recycle Bin back where they were - undoing a delete. originals: their paths before the delete; since:
        /// when it started (so an older deletion of the same name isn't the one brought back). done gets the restored paths as Created.
        /// </summary>
        public void Restore(IList<string> originals, DateTime since, Action<Result> done)
        {
            if (Starting != null) Starting();
            Busy++;
            var thread = new Thread(() =>
            {
                var r = new Result { Started = DateTime.Now };
                try
                {
                    // straight from the bin's own files: the shell's view of the Recycle Bin goes stale within one run (a thing deleted,
                    // restored and deleted again wasn't in it the second time), so its Restore missed it
                    Dictionary<string, string> found = RecycledFiles(originals, since);   // original path -> its $R file
                    if (found.Count == 0) r.Error = "Those items aren't in the Recycle Bin any more.";
                    var blocked = new List<string>();
                    foreach (KeyValuePair<string, string> f in found)
                    {
                        string original = f.Key, data = f.Value;
                        if (File.Exists(original) || Directory.Exists(original)) { blocked.Add(Path.GetFileName(original)); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(original));
                        if (Directory.Exists(data)) Directory.Move(data, original); else File.Move(data, original);
                        string info = Path.Combine(Path.GetDirectoryName(data), "$I" + Path.GetFileName(data).Substring(2));
                        try { File.Delete(info); } catch { }
                        r.Created.Add(original);
                        Notify(Directory.Exists(original) ? SHCNE_MKDIR : SHCNE_CREATE, original);
                        Notify(SHCNE_UPDATEDIR, Path.GetDirectoryName(data));   // the Recycle Bin's own view
                    }
                    if (blocked.Count > 0)
                        r.Error = "Couldn't bring back " + string.Join(", ", blocked) + ": there's already something with that name there.";
                }
                catch (Exception ex)
                {
                    Log.Write("Restore: " + ex);
                    r.Error = "vXplorer couldn't bring them back: " + ex.Message;
                }
                _ui.BeginInvoke(new Action(() => { Busy--; done(r); }));
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = false;
            thread.Name = "Restore";
            thread.Start();
        }

        /// <summary>
        /// The $R files in the Recycle Bin that hold these deleted paths, deleted since then: each drive's $Recycle.Bin\&lt;your SID&gt; has
        /// an $I file per item (original path, deletion time) next to its $R file.
        /// </summary>
        static Dictionary<string, string> RecycledFiles(IList<string> originals, DateTime since)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
            var want = new HashSet<string>(originals, StringComparer.OrdinalIgnoreCase);
            DateTime from = since.AddSeconds(-5);
            foreach (string root in originals.Select(p => Path.GetPathRoot(p)).Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string bin = Path.Combine(root, "$Recycle.Bin", sid);
                if (!Directory.Exists(bin)) continue;
                // the newest deletion of each path wins
                var best = new Dictionary<string, Tuple<DateTime, string>>(StringComparer.OrdinalIgnoreCase);
                foreach (string info in Directory.EnumerateFiles(bin, "$I*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true }))
                {
                    try
                    {
                        byte[] b = File.ReadAllBytes(info);
                        if (b.Length < 28) continue;
                        long version = BitConverter.ToInt64(b, 0);
                        DateTime deleted = DateTime.FromFileTime(BitConverter.ToInt64(b, 16));
                        string original;
                        if (version >= 2) original = System.Text.Encoding.Unicode.GetString(b, 28, Math.Max(0, (BitConverter.ToInt32(b, 24) - 1) * 2));
                        else original = System.Text.Encoding.Unicode.GetString(b, 24, Math.Min(520, b.Length - 24)).Split('\0')[0];
                        if (deleted < from || !want.Contains(original)) continue;
                        string data = Path.Combine(bin, "$R" + Path.GetFileName(info).Substring(2));
                        Tuple<DateTime, string> cur;
                        if (!best.TryGetValue(original, out cur) || deleted > cur.Item1) best[original] = Tuple.Create(deleted, data);
                    }
                    catch { }
                }
                foreach (KeyValuePair<string, Tuple<DateTime, string>> t in best)
                    if (File.Exists(t.Value.Item2) || Directory.Exists(t.Value.Item2)) result[t.Key] = t.Value.Item2;
            }
            return result;
        }

        static List<ShellItem> Items(IEnumerable<string> paths, List<IDisposable> keep)
        {
            var list = paths.Select(p => new ShellItem(p)).ToList();
            keep.AddRange(list);
            return list;
        }

        /// <summary>Raised on the UI thread just before an operation starts.</summary>
        public event Action Starting;

        /// <summary>Operations still running (UI thread).</summary>
        public int Busy { get; private set; }

        /// <summary>
        /// Run the steps (one IFileOperation each - flags differ, e.g. Replace and Keep both) on a thread of their own. op: the panel's
        /// row and the journal (null: Windows' own progress window). Copies wait in the queue (Operations) before they start.
        /// </summary>
        void Run(Operation op, IList<Tuple<ShellFileOperations.OperationFlags, Action<ShellFileOperations, List<IDisposable>>>> steps,
                 IEnumerable<string> topLevel, Action<Result> done)
        {
            if (Starting != null) Starting();
            Busy++;
            IntPtr hwnd = new WindowInteropHelper(_owner).Handle;
            // the Post… events come for everything inside folders too: only the items asked for count
            var top = new HashSet<string>(topLevel, StringComparer.OrdinalIgnoreCase);
            var r = new Result { Started = DateTime.Now };
            if (op != null) Operations.Add(op);
            var thread = new Thread(() =>
            {
                bool cancelled = false;
                var keep = new List<IDisposable>();
                try
                {
                    if (op != null)
                    {
                        op.Gate.Wait();   // the queue: another copy to the same drive goes first
                        if (op.Want == Vanara.PInvoke.Shell32.PDOPSTATUS.PDOPS_CANCELLED) { cancelled = true; return; }
                        op.StartedAt = DateTime.Now;
                        r.Started = op.StartedAt;
                        _ui.BeginInvoke(new Action(() => { if (op.State == OpState.Waiting) { op.State = OpState.Preparing; Operations.Raise(); } }));
                        if (op.Journal != null) op.Journal.Save();
                        if (!op.Hidden) Count(op, top);   // "37 of 120 files", "1.2 GB of 4.0 GB"
                    }
                    var progress = op != null ? new OpProgress(op, _ui, top) : null;
                    foreach (var step in steps)
                    {
                        // our own IFileOperation, so it can be given vXplorer's progress object in place of Windows' window
                        var iop = (Vanara.PInvoke.Shell32.IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(FileOperationClsid));
                        using (var sfo = new ShellFileOperations(iop, new HWND(hwnd)))
                        {
                            var flags = step.Item1;
                            // the panel lists what went wrong (with Try again) instead of Windows stopping to ask about each file
                            if (op != null && !op.Hidden) flags |= ShellFileOperations.OperationFlags.NoErrorUI | ShellFileOperations.OperationFlags.ShowElevationPrompt;
                            // vXplorer shows the progress: no Windows progress window
                            if (progress != null) flags |= ShellFileOperations.OperationFlags.Silent;
                            sfo.Options = flags;
                            sfo.PostCopyItem += (s, e) => Made(e, top, r, op, progress);
                            sfo.PostMoveItem += (s, e) => Made(e, top, r, op, progress);
                            sfo.PostRenameItem += (s, e) => Made(e, top, r, null, null);
                            sfo.PostDeleteItem += (s, e) => Gone(e, top, r, op);
                            step.Item2(sfo, keep);
                            uint cookie = progress != null ? iop.Advise(progress) : 0;
                            try { sfo.PerformOperations(); }
                            finally { if (progress != null) try { iop.Unadvise(cookie); } catch { } }
                        }
                        if (op != null && op.Want == Vanara.PInvoke.Shell32.PDOPSTATUS.PDOPS_CANCELLED) { cancelled = true; break; }
                    }
                }
                catch (COMException ex) when (ex.HResult == UserCancelled || ex.HResult == Cancelled) { cancelled = true; }
                catch (Exception ex)
                {
                    if (op != null && op.Want == Vanara.PInvoke.Shell32.PDOPSTATUS.PDOPS_CANCELLED) cancelled = true;
                    else
                    {
                        Log.Write("File operation: " + ex);
                        r.Error = "That didn't work: " + ex.Message;
                    }
                }
                finally
                {
                    foreach (IDisposable d in keep) { try { d.Dispose(); } catch { } }
                    _ui.BeginInvoke(new Action(() =>
                    {
                        Busy--;
                        if (op != null) Operations.Finish(op, cancelled);
                        done(r);
                        if (op != null) Operations.Raise();   // done() noted the undo step: the row's Undo link can show now
                    }));
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = false;   // closing vXplorer mid-copy lets the copy finish (MainWindow asks first)
            thread.Name = "File operation";
            thread.Start();
        }

        /// <summary>How many files and bytes the operation covers (the engine only says how far through it is).</summary>
        static void Count(Operation op, IEnumerable<string> paths)
        {
            long files = 0, bytes = 0;
            var opts = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true };
            foreach (string p in paths)
            {
                try
                {
                    if (File.Exists(p)) { files++; bytes += new FileInfo(p).Length; continue; }
                    if (!Directory.Exists(p)) continue;
                    foreach (FileInfo f in new DirectoryInfo(p).EnumerateFiles("*", opts))
                    {
                        files++;
                        bytes += f.Length;
                        if (op.Want == Vanara.PInvoke.Shell32.PDOPSTATUS.PDOPS_CANCELLED) return;
                    }
                }
                catch (Exception ex) { Log.Write("Counting " + p + ": " + ex.Message); }
            }
            op.ItemsTotal = files;
            op.BytesTotal = bytes;
        }

        static void Failed(Operation op, ShellFileOperations.ShellFileOpEventArgs e)
        {
            if (op == null || op.Hidden) return;
            try
            {
                if (e.Result == UserCancelled || e.Result == Cancelled) return;
                string src = e.SourceItem?.FileSystemPath;
                if (string.IsNullOrEmpty(src)) return;
                var f = new OpFailure { Source = src, DestFolder = e.DestFolder?.FileSystemPath, Reason = Operations.Reason((int)e.Result) };
                Log.Write("Failed (0x" + ((int)e.Result).ToString("X8") + "): " + src);
                lock (op.Failures) op.Failures.Add(f);
            }
            catch { }
        }

        static void Made(ShellFileOperations.ShellFileOpEventArgs e, HashSet<string> top, Result r, Operation op, OpProgress progress)
        {
            try
            {
                if (e.Result.Failed) { Failed(op, e); return; }
                if (e.DestItem == null || e.SourceItem == null) return;
                string from = e.SourceItem.FileSystemPath, to = e.DestItem.FileSystemPath;
                if (string.IsNullOrEmpty(to) || from == null || !top.Contains(from)) return;
                r.Created.Add(to);
                r.Pairs.Add(Tuple.Create(from, to));
                // a file is done now; a folder is reported as soon as it's made, before what's in it is copied - OpProgress marks
                // it done once the engine moves on to the next top-level item (or the whole operation ends)
                if (op != null && op.Journal != null && !Directory.Exists(to))
                {
                    op.Journal.MarkDone(from);
                    progress?.SaveJournalNowAndThen();
                }
            }
            catch { }
        }

        static void Gone(ShellFileOperations.ShellFileOpEventArgs e, HashSet<string> top, Result r, Operation op)
        {
            try
            {
                if (e.Result.Failed) { Failed(op, e); return; }
                if (e.SourceItem == null) return;
                string from = e.SourceItem.FileSystemPath;
                if (from != null && top.Contains(from)) r.Deleted.Add(from);
            }
            catch { }
        }
    }
}
