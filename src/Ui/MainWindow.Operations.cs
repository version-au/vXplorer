using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>
    /// The window's side of copies, moves and deletes running in the operations panel: the Undo link, closing while one runs, and
    /// finishing copies that stopped part way (a journal left from last time - Core/CopyJournal).
    /// </summary>
    public partial class MainWindow
    {
        static bool PathTaken(string p) { return File.Exists(p) || Directory.Exists(p); }

        /// <summary>The settings (the operations panel keeps its folded state there).</summary>
        public AppSettings Settings { get { return _settings; } }

        /// <summary>The row's Undo link: while this operation is still the last thing this window would undo.</summary>
        void LinkUndo(Operation op, Func<UndoStep> step)
        {
            if (op == null) return;
            op.CanUndo = () =>
            {
                UndoStep s = step();
                return s != null && IsLoaded && _undo.Count > 0 && _undo[_undo.Count - 1] == s;
            };
            op.Undo = () => { Undo(); Operations.Raise(); };
        }

        /// <summary>The panel's Open folder link.</summary>
        public void ShowFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            Navigate(path);
            Activate();
        }

        /// <summary>
        /// Closing the last window while something runs: ask. Keep going = don't close. Stop = cancel them, keeping their journals, so
        /// next time vXplorer offers to finish them.
        /// </summary>
        bool MayClose()
        {
            if (!Operations.AnyActive) return true;
            if (Application.Current.Windows.OfType<MainWindow>().Count(w => w.IsLoaded) > 1) return true;   // another window carries on
            List<Operation> running = Operations.All.Where(o => o.Active).ToList();
            Operation first = running[0];
            string what = first.Target != null
                ? first.Verb.ToLowerInvariant() + " " + (first.ItemsTotal > 0 ? first.Items + " of " + first.ItemsTotal + " files" : first.TopCount + (first.TopCount == 1 ? " item" : " items")) + " to " + first.Target
                : first.Verb.ToLowerInvariant().TrimEnd(':') + " " + first.TopCount + (first.TopCount == 1 ? " item" : " items");
            string more = running.Count > 1 ? " (and " + (running.Count - 1) + " more)" : "";
            bool keepGoing = Ask.YesNo(this, running.Count == 1 ? "A copy is still running" : "Copies are still running",
                "vXplorer is " + what + more + ". Closing now stops " + (running.Count == 1 ? "it" : "them") + " part way - next time vXplorer " +
                "will offer to finish.", "Keep going", "Stop and close", false);
            if (keepGoing) return false;
            foreach (Operation op in running)
            {
                op.KeepJournal = true;
                Operations.Cancel(op);
            }
            return true;
        }

        // =================================================================
        // Copies that didn't finish last time
        // =================================================================

        /// <summary>At start: copies / moves whose journal is still there stopped part way - offer to finish each.</summary>
        public void OfferUnfinishedCopies()
        {
            foreach (CopyJournal j in CopyJournal.Unfinished())
            {
                string dest = Native.DisplayName(j.Dest);
                string verb = j.Kind == "move" ? "Moving" : "Copying";
                int n = j.Sources.Count, done = j.Done.Count;
                string items = n + (n == 1 ? " item" : " items");
                if (!Directory.Exists(j.Dest))
                {
                    bool keep = Ask.YesNo(this, "A " + (j.Kind == "move" ? "move" : "copy") + " didn't finish",
                        verb + " " + items + " to " + j.Dest + " stopped part way, and that folder can't be reached now (an unplugged drive or an " +
                        "offline network?). vXplorer can offer it again next time.", "Keep it for later", "Forget it", false);
                    if (!keep) j.Delete();
                    continue;
                }
                List<string> left = j.Left();
                if (left.Count == 0) { j.Delete(); continue; }
                bool go = Ask.YesNo(this, "A " + (j.Kind == "move" ? "move" : "copy") + " didn't finish",
                    verb + " " + items + " to " + dest + " stopped part way (started " + Format.Stamp(j.Started) + "): " + done + " of " + n +
                    " were finished. Finish the rest? Anything already there that matches is left as it is.",
                    j.Kind == "move" ? "Move the rest" : "Copy the rest", "Forget it", false);
                if (go) ResumeJournal(j);
                else j.Delete();
            }
        }

        static bool SameFile(string a, string b)
        {
            try
            {
                var fa = new FileInfo(a);
                var fb = new FileInfo(b);
                return fb.Exists && fa.Length == fb.Length && Math.Abs((fa.LastWriteTimeUtc - fb.LastWriteTimeUtc).TotalSeconds) < 2.1;
            }
            catch { return false; }
        }

        static bool Inside(string path, string folder)
        {
            string f = folder.TrimEnd('\\') + "\\";
            return path.StartsWith(f, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Is the journal's Current file really a half-written copy? Only when it's inside the destination, the source it was being
        /// made from (the top-level item of its name, or the file at the same place inside that folder) is still there, isn't a
        /// "Keep both" item (those got a new name - the file of this name is the one that was kept), and differs in size or time.
        /// </summary>
        public static bool HalfWritten(CopyJournal j)
        {
            try
            {
                if (string.IsNullOrEmpty(j.Current) || !Inside(j.Current, j.Dest) || !File.Exists(j.Current)) return false;
                if (j.Sources.Any(s => string.Equals(s, j.Current, StringComparison.OrdinalIgnoreCase))) return false;
                string rel = j.Current.Substring(j.Dest.TrimEnd('\\').Length + 1);
                int cut = rel.IndexOf('\\');
                string top = cut < 0 ? rel : rel.Substring(0, cut);
                string src = j.Sources.FirstOrDefault(s => string.Equals(Path.GetFileName(s.TrimEnd('\\')), top, StringComparison.OrdinalIgnoreCase));
                if (src == null || j.KeepBoth.Contains(src, StringComparer.OrdinalIgnoreCase)) return false;
                string from = cut < 0 ? src : Path.Combine(src, rel.Substring(cut + 1));
                return File.Exists(from) && !SameFile(from, j.Current);
            }
            catch { return false; }
        }

        /// <summary>
        /// Finish a journal's copy / move: the half-written file goes, then whatever is missing (or different) is copied - file by file
        /// inside a folder that was part done. The journal stays until this finishes, so another stop is offered again next time.
        /// </summary>
        async void ResumeJournal(CopyJournal j)
        {
            bool move = j.Kind == "move";
            Status("Working out what's left to " + (move ? "move" : "copy") + "…");
            List<Tuple<string, string>> entries = null;
            List<string> keepBoth = null;
            try
            {
                await Task.Run(() =>
                {
                    // the file that was being written when it stopped is only part there - but the journal is a second or so behind,
                    // so the file it names has usually finished since (and after a move is the only copy): it only goes when the
                    // file it was being made from is still there and isn't the same (HalfWritten)
                    if (HalfWritten(j))
                    {
                        try { File.Delete(j.Current); Log.Write("Resume: removed the half-written " + j.Current); }
                        catch (Exception ex) { Log.Write("Resume: couldn't remove " + j.Current + ": " + ex.Message); }
                    }
                    entries = new List<Tuple<string, string>>();
                    keepBoth = new List<string>();
                    foreach (string src in j.Left())
                    {
                        if (j.KeepBoth.Contains(src, StringComparer.OrdinalIgnoreCase)) { keepBoth.Add(src); continue; }
                        string target = Path.Combine(j.Dest, Path.GetFileName(src.TrimEnd('\\')));
                        if (File.Exists(src))
                        {
                            if (move || !SameFile(src, target)) entries.Add(Tuple.Create(src, j.Dest));
                            continue;
                        }
                        if (!Directory.Exists(target)) { entries.Add(Tuple.Create(src, j.Dest)); continue; }
                        // a folder that was part done: what's missing or different in it, file by file (and its empty folders)
                        var opts = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true };
                        foreach (string d in Directory.EnumerateDirectories(src, "*", opts))
                            Directory.CreateDirectory(Path.Combine(target, d.Substring(src.TrimEnd('\\').Length + 1)));
                        foreach (string f in Directory.EnumerateFiles(src, "*", opts))
                        {
                            string rel = f.Substring(src.TrimEnd('\\').Length + 1);
                            string df = Path.Combine(target, rel);
                            if (move || !SameFile(f, df)) entries.Add(Tuple.Create(f, Path.GetDirectoryName(df)));
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Write("Resume: " + ex);
                Status("vXplorer couldn't work out what was left: " + ex.Message);
                return;
            }
            if (entries.Count == 0 && keepBoth.Count == 0)
            {
                j.Delete();
                Status("Nothing was left to " + (move ? "move" : "copy") + " - it had all made it.");
                return;
            }
            Log.Write("Resume: " + entries.Count + " items to " + (move ? "move" : "copy") + " into " + j.Dest + (keepBoth.Count > 0 ? ", " + keepBoth.Count + " kept both" : ""));
            int pending = (entries.Count > 0 ? 1 : 0) + (keepBoth.Count > 0 ? 1 : 0);
            Action<FileOps.Result> finished = r =>
            {
                AfterOperation(r);
                if (--pending > 0) return;
                j.Delete();
                if (move)   // folders whose files have all moved: nothing left in them
                    foreach (string src in j.Sources)
                        if (Directory.Exists(src) && !Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories).Any())
                            try { Directory.Delete(src, true); } catch { }
            };
            if (entries.Count > 0) _ops.TransferEach(entries, move, j.Dest, finished);
            if (keepBoth.Count > 0) _ops.Transfer(keepBoth, j.Dest, move, keepBoth, false, finished);
        }

        /// <summary>The panel's Try again: the files that failed, once more.</summary>
        public void RetryFailures(Operation op)
        {
            List<OpFailure> failed;
            lock (op.Failures) failed = op.Failures.ToList();
            if (failed.Count == 0) return;
            Operations.Clear(op);
            if (op.Kind == "copy" || op.Kind == "move")
            {
                var again = failed.Where(f => f.DestFolder != null && PathTaken(f.Source)).ToList();
                // an item that was a "Keep both" still is: it gets a new name again, never replaces the one that was kept
                List<string> kept = op.Journal != null ? op.Journal.KeepBoth : new List<string>();
                var both = again.Where(f => kept.Contains(f.Source, StringComparer.OrdinalIgnoreCase)).ToList();
                var entries = again.Except(both).Select(f => Tuple.Create(f.Source, f.DestFolder)).ToList();
                if (again.Count == 0) { Status("Those files aren't there any more."); return; }
                if (entries.Count > 0) _ops.TransferEach(entries, op.Kind == "move", op.TargetPath, AfterOperation);
                foreach (var g in both.GroupBy(f => f.DestFolder, StringComparer.OrdinalIgnoreCase))
                {
                    List<string> list = g.Select(f => f.Source).ToList();
                    _ops.Transfer(list, g.Key, op.Kind == "move", list, false, AfterOperation);
                }
            }
            else
            {
                List<string> paths = failed.Select(f => f.Source).Where(PathTaken).ToList();
                if (paths.Count == 0) { Status("Those files aren't there any more."); return; }
                UndoStep step = null;
                bool recycle = op.Kind == "recycle";
                Operation again = _ops.Delete(paths, !recycle, r => { if (recycle) step = RememberOperation(UndoKind.Recycle, r); AfterOperation(r); });
                if (recycle) LinkUndo(again, () => step);
            }
        }
    }
}
