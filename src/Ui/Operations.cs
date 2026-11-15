using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using Vanara.PInvoke;
using vXplorer.Core;
using static Vanara.PInvoke.Shell32;

namespace vXplorer.Ui
{
    public enum OpState { Waiting, Preparing, Running, Paused, Done, Problems, Cancelled }

    /// <summary>A file that couldn't be copied / moved / deleted, and why (in plain words).</summary>
    public sealed class OpFailure
    {
        public string Source;
        /// <summary>Where it was going (copies and moves), for Try again.</summary>
        public string DestFolder;
        public string Reason;
        public string Name { get { return Path.GetFileName(Source.TrimEnd('\\')); } }
    }

    /// <summary>
    /// One copy / move / delete as the operations panel shows it. The worker thread writes the live numbers (through OpProgress, which
    /// Windows' copy engine reports to); the panels read them a few times a second. State changes happen on the UI thread.
    /// </summary>
    public sealed class Operation
    {
        static int _ids;
        public readonly int Id = Interlocked.Increment(ref _ids);

        public string Kind;          // "copy", "move", "delete", "recycle", or "" (hidden: rename, undo …)
        public string Verb;          // "Copying"
        public string DoneVerb;      // "Copied"
        /// <summary>The destination folder's name ("Photos"); null for deletes.</summary>
        public string Target;
        public string TargetPath;
        /// <summary>Copies and moves to the same drive wait for each other (running them side by side slows both down).</summary>
        public string Root;
        /// <summary>No row in the panel (renames, undo steps): still runs through vXplorer's progress, so Windows' window doesn't flash up.</summary>
        public bool Hidden;
        public int TopCount;
        public MainWindow Owner;

        // ---- live numbers (worker thread writes, UI reads) ----
        public long Points, PointsTotal, Bytes, BytesTotal, Items, ItemsTotal;
        public string CurrentName = "";
        public double BytesPerSecond;
        public bool Preflight;

        // ---- control (UI thread writes, the engine reads through GetOperationStatus) ----
        public volatile PDOPSTATUS Want = PDOPSTATUS.PDOPS_RUNNING;
        /// <summary>Set when it may start (the queue): the worker waits on it.</summary>
        public readonly ManualResetEventSlim Gate = new ManualResetEventSlim(false);

        public OpState State = OpState.Waiting;
        public readonly List<OpFailure> Failures = new List<OpFailure>();
        public DateTime StartedAt, FinishedAt;

        /// <summary>Undo link: shown while this is still the last thing its window would undo.</summary>
        public Func<bool> CanUndo;
        public Action Undo;

        public CopyJournal Journal;
        /// <summary>Stopped by closing vXplorer: keep the journal so it's offered next time.</summary>
        public bool KeepJournal;

        public bool Finished { get { return State == OpState.Done || State == OpState.Problems || State == OpState.Cancelled; } }
        public bool Active { get { return !Finished; } }

        // speed: bytes over the last few seconds
        readonly Queue<Tuple<DateTime, long>> _samples = new Queue<Tuple<DateTime, long>>();

        internal void Sample(long bytes)
        {
            DateTime now = DateTime.UtcNow;
            _samples.Enqueue(Tuple.Create(now, bytes));
            while (_samples.Count > 2 && (now - _samples.Peek().Item1).TotalSeconds > 4) _samples.Dequeue();
            Tuple<DateTime, long> first = _samples.Peek();
            double secs = (now - first.Item1).TotalSeconds;
            if (secs > 0.5) BytesPerSecond = Math.Max(0, (bytes - first.Item2) / secs);
        }

        /// <summary>
        /// The line graph's points (OpsGraph): how far along it was (0..1) and how fast it was going then (the engine's work points a
        /// second - bytes for a copy, items for a delete; the graph scales to its own peak, so the unit doesn't matter). Lock it to read.
        /// </summary>
        public readonly List<Tuple<double, double>> Speeds = new List<Tuple<double, double>>();
        readonly Queue<Tuple<DateTime, long>> _pointSamples = new Queue<Tuple<DateTime, long>>();
        DateTime _lastSpeed;

        /// <summary>From the engine's progress (worker thread): a point for the line graph about five times a second.</summary>
        internal void TrackSpeed()
        {
            DateTime now = DateTime.UtcNow;
            long pts = Points;
            _pointSamples.Enqueue(Tuple.Create(now, pts));
            while (_pointSamples.Count > 2 && (now - _pointSamples.Peek().Item1).TotalSeconds > 3) _pointSamples.Dequeue();
            if ((now - _lastSpeed).TotalMilliseconds < 200) return;
            Tuple<DateTime, long> first = _pointSamples.Peek();
            double secs = (now - first.Item1).TotalSeconds;
            if (secs < 0.5 || PointsTotal <= 0) return;
            _lastSpeed = now;
            double rate = Math.Max(0, (pts - first.Item2) / secs);
            double f = Math.Min(1, (double)pts / PointsTotal);
            lock (Speeds)
            {
                // a second step (Keep both after Replace) starts again from 0: the graph does too
                if (Speeds.Count > 0 && f < Speeds[Speeds.Count - 1].Item1 - 0.01) { Speeds.Clear(); _pointSamples.Clear(); }
                if (Speeds.Count == 0 && f > 0) Speeds.Add(Tuple.Create(0.0, rate));   // the line starts at the left edge
                Speeds.Add(Tuple.Create(f, rate));
                if (Speeds.Count > 600)   // a long copy: every other point, so drawing stays cheap
                {
                    var keep = Speeds.Where((s, i) => i % 2 == 0 || i == Speeds.Count - 1).ToList();
                    Speeds.Clear();
                    Speeds.AddRange(keep);
                }
            }
        }

        /// <summary>0..1, or -1 while it's still counting.</summary>
        public double Fraction
        {
            get
            {
                if (State == OpState.Done) return 1;
                if (PointsTotal > 0) return Math.Min(1, (double)Points / PointsTotal);
                if (BytesTotal > 0) return Math.Min(1, (double)Bytes / BytesTotal);
                return Preflight ? -1 : 0;
            }
        }
    }

    /// <summary>Every operation in every window (all the panels show the same list), and the queue that starts them.</summary>
    public static class Operations
    {
        public static readonly ObservableCollection<Operation> All = new ObservableCollection<Operation>();
        /// <summary>Something started, finished, paused … (UI thread). The panels redraw.</summary>
        public static event Action Changed;

        public static bool AnyActive { get { return All.Any(o => o.Active && !o.Hidden); } }

        public static void Add(Operation op)
        {
            if (!op.Hidden) All.Add(op);
            Pump(op);
            Raise();
        }

        /// <summary>Start what may start: deletes and hidden steps at once; copies / moves when nothing else is going to the same drive.</summary>
        static void Pump(Operation fresh)
        {
            if (fresh != null && (fresh.Hidden || fresh.Root == null)) { fresh.Gate.Set(); return; }
            foreach (Operation op in All.Where(o => o.State == OpState.Waiting && !o.Gate.IsSet).ToList())
            {
                bool busy = All.Any(o => o != op && o.Active && o.Gate.IsSet && o.Root != null &&
                                         string.Equals(o.Root, op.Root, StringComparison.OrdinalIgnoreCase));
                if (!busy) op.Gate.Set();
            }
        }

        public static void StartNow(Operation op) { op.Gate.Set(); Raise(); }

        public static void Pause(Operation op, bool pause)
        {
            if (op.Finished) return;
            op.Want = pause ? PDOPSTATUS.PDOPS_PAUSED : PDOPSTATUS.PDOPS_RUNNING;
            if (op.State == OpState.Running || op.State == OpState.Paused) op.State = pause ? OpState.Paused : OpState.Running;
            Raise();
        }

        public static void Cancel(Operation op)
        {
            if (op.Finished) return;
            op.Want = PDOPSTATUS.PDOPS_CANCELLED;
            op.Gate.Set();   // a waiting one ends straight away
            Raise();
        }

        /// <summary>The worker finished (UI thread): show the result, start what was waiting, tidy the row away later.</summary>
        public static void Finish(Operation op, bool cancelled)
        {
            op.FinishedAt = DateTime.Now;
            op.State = cancelled ? OpState.Cancelled : op.Failures.Count > 0 ? OpState.Problems : OpState.Done;
            lock (op.Failures)
                foreach (OpFailure f in op.Failures) Log.Write("Couldn't " + op.Kind + " " + f.Source + ": " + f.Reason);
            if (op.Journal != null && !op.KeepJournal) op.Journal.Delete();
            if (op.Hidden) All.Remove(op);
            Pump(null);
            Raise();
            if (op.State == OpState.Done || op.State == OpState.Cancelled)
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(op.State == OpState.Done ? 8 : 3) };
                t.Tick += (s, e) =>
                {
                    if (PanelHovered != null && PanelHovered()) return;   // not while the mouse is on a panel: try again later
                    t.Stop();
                    Clear(op);
                };
                t.Start();
            }
        }

        public static void Clear(Operation op)
        {
            if (op.Active) return;
            All.Remove(op);
            Raise();
        }

        /// <summary>Is the mouse over any panel (finished rows wait)?</summary>
        public static Func<bool> PanelHovered;

        public static void Raise() { Changed?.Invoke(); }

        /// <summary>A copy engine error in plain words.</summary>
        public static string Reason(int hr)
        {
            switch (hr)
            {
                case unchecked((int)0x80070020): return "It's open in another app.";
                case unchecked((int)0x80070021): return "Part of it is locked by another app.";
                case unchecked((int)0x80070005): return "You don't have permission.";
                case unchecked((int)0x80070070): return "There isn't enough space on the drive.";
                case unchecked((int)0x80070002): case unchecked((int)0x80070003): return "It isn't there any more.";
                case unchecked((int)0x800700CE): case unchecked((int)0x8007006F): return "The name or path is too long.";
                case unchecked((int)0x80070035): case unchecked((int)0x80070040): return "The network location couldn't be reached.";
                case unchecked((int)0x80070050): case unchecked((int)0x800700B7): return "There's already something with that name.";
                case unchecked((int)0x80070570): return "It's damaged or unreadable.";
                // the copy engine's own codes (COPYENGINE_E_…, winerror.h)
                case unchecked((int)0x80270002): return "It needs administrator permission.";
                case unchecked((int)0x80270003): return "It's the same file as the one there.";
                case unchecked((int)0x80270021): return "You don't have permission to read it.";
                case unchecked((int)0x80270022): return "You don't have permission to put it there.";
                case unchecked((int)0x80270023): case unchecked((int)0x80270024): return "The path is too long.";
                case unchecked((int)0x80270025): return "The network drive it was on went away.";
                case unchecked((int)0x80270026): return "The network drive it was going to went away.";
                case unchecked((int)0x80270027): return "It's open in another app.";
                case unchecked((int)0x80270028): return "The one there is open in another app.";
                case unchecked((int)0x80270029): case unchecked((int)0x8027002A): case unchecked((int)0x8027002B): case unchecked((int)0x8027002C):
                    return "There's already something with that name.";
                case unchecked((int)0x80270032): case unchecked((int)0x80270033): return "There isn't enough space on the drive.";
                case unchecked((int)0x80270035): return "Windows couldn't reach it.";
                case unchecked((int)0x80270037): return "It's too big for the Recycle Bin.";
                case unchecked((int)0x8027003F): return "It's read-only.";
            }
            if (((hr >> 16) & 0x1FFF) == 0x27) return "Windows couldn't do it (copy engine error 0x" + hr.ToString("X8") + ").";
            try
            {
                string m = new System.ComponentModel.Win32Exception(hr & 0xFFFF).Message;
                if (!string.IsNullOrWhiteSpace(m)) return m;
            }
            catch { }
            return "Windows said 0x" + hr.ToString("X8") + ".";
        }
    }

    /// <summary>
    /// Windows' copy engine reports to this (IFileOperation.Advise) while its own progress window is hidden (FOF_SILENT): how far it
    /// is, which item is next, what finished. Pause = holding its callback until resumed; cancel = answering "cancelled". (Windows
    /// doesn't let anyone replace its progress window: SetProgressDialog isn't implemented.)
    /// </summary>
    [ComVisible(true)]
    sealed class OpProgress : IFileOperationProgressSink
    {
        static readonly HRESULT CancelledHr = unchecked((int)0x800704C7);   // HRESULT_FROM_WIN32(ERROR_CANCELLED)
        readonly Operation _op;
        readonly Dispatcher _ui;
        readonly HashSet<string> _top;
        /// <summary>The top-level folder being copied: done once the engine starts on the next top-level item.</summary>
        string _topFolder;
        DateTime _lastJournal;

        public OpProgress(Operation op, Dispatcher ui, HashSet<string> top) { _op = op; _ui = ui; _top = top; }

        void UiState(OpState s)
        {
            _ui.BeginInvoke(new Action(() =>
            {
                if (_op.Finished) return;
                if (_op.Want == PDOPSTATUS.PDOPS_PAUSED && s == OpState.Running) s = OpState.Paused;
                if (_op.State != s) { _op.State = s; Operations.Raise(); }
            }));
        }

        /// <summary>Paused: wait here (the engine waits with us). Cancelled: say so, and the engine stops.</summary>
        HRESULT Go()
        {
            while (_op.Want == PDOPSTATUS.PDOPS_PAUSED) Thread.Sleep(100);
            return _op.Want == PDOPSTATUS.PDOPS_CANCELLED ? CancelledHr : HRESULT.S_OK;
        }

        static string NameOf(IShellItem i)
        {
            try { return i?.GetDisplayName(SIGDN.SIGDN_PARENTRELATIVEPARSING) ?? ""; } catch { return ""; }
        }

        static string PathOf(IShellItem i)
        {
            try { return i?.GetDisplayName(SIGDN.SIGDN_FILESYSPATH); } catch { return null; }
        }

        static bool IsFolder(IShellItem i)
        {
            try { return i != null && (i.GetAttributes(SFGAO.SFGAO_FOLDER) & SFGAO.SFGAO_FOLDER) != 0; } catch { return false; }
        }

        HRESULT Before(IShellItem item, IShellItem destFolder, string newName)
        {
            string name = NameOf(item);
            _op.CurrentName = name;
            if (_op.Journal != null && _top != null)
            {
                // the engine works through the top-level items in turn: starting on one means the folder before it is finished
                string path = PathOf(item);
                if (path != null && _top.Contains(path))
                {
                    if (_topFolder != null && !string.Equals(_topFolder, path, StringComparison.OrdinalIgnoreCase)) _op.Journal.MarkDone(_topFolder);
                    _topFolder = IsFolder(item) ? path : null;
                }
            }
            if (_op.Journal != null && destFolder != null)
            {
                string folder = PathOf(destFolder);
                if (!string.IsNullOrEmpty(folder) && !IsFolder(item)) _op.Journal.Current = Path.Combine(folder, string.IsNullOrEmpty(newName) ? name : newName);
                SaveJournalNowAndThen();
            }
            return Go();
        }

        void After(IShellItem item, HRESULT hr)
        {
            if (hr.Succeeded && !IsFolder(item)) Interlocked.Increment(ref _op.Items);
        }

        /// <summary>The journal is written about once a second, not for every file.</summary>
        public void SaveJournalNowAndThen()
        {
            if (_op.Journal == null || (DateTime.UtcNow - _lastJournal).TotalSeconds < 1) return;
            _lastJournal = DateTime.UtcNow;
            _op.Journal.Save();
        }

        public HRESULT StartOperations() { UiState(OpState.Running); return HRESULT.S_OK; }
        public HRESULT FinishOperations(HRESULT hrResult) { return HRESULT.S_OK; }
        public HRESULT PreRenameItem(TRANSFER_SOURCE_FLAGS dwFlags, IShellItem psiItem, string pszNewName) { return Go(); }
        public HRESULT PostRenameItem(TRANSFER_SOURCE_FLAGS dwFlags, IShellItem psiItem, string pszNewName, HRESULT hrRename, IShellItem psiNewlyCreated) { return HRESULT.S_OK; }
        public HRESULT PreMoveItem(TRANSFER_SOURCE_FLAGS dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName) { return Before(psiItem, psiDestinationFolder, pszNewName); }
        public HRESULT PostMoveItem(TRANSFER_SOURCE_FLAGS dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName, HRESULT hrMove, IShellItem psiNewlyCreated) { After(psiItem, hrMove); return HRESULT.S_OK; }
        public HRESULT PreCopyItem(TRANSFER_SOURCE_FLAGS dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName) { return Before(psiItem, psiDestinationFolder, pszNewName); }
        public HRESULT PostCopyItem(TRANSFER_SOURCE_FLAGS dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName, HRESULT hrCopy, IShellItem psiNewlyCreated) { After(psiItem, hrCopy); return HRESULT.S_OK; }
        public HRESULT PreDeleteItem(TRANSFER_SOURCE_FLAGS dwFlags, IShellItem psiItem) { return Before(psiItem, null, null); }
        public HRESULT PostDeleteItem(TRANSFER_SOURCE_FLAGS dwFlags, IShellItem psiItem, HRESULT hrDelete, IShellItem psiNewlyCreated) { After(psiItem, hrDelete); return HRESULT.S_OK; }
        public HRESULT PreNewItem(TRANSFER_SOURCE_FLAGS dwFlags, IShellItem psiDestinationFolder, string pszNewName) { return Go(); }
        public HRESULT PostNewItem(TRANSFER_SOURCE_FLAGS dwFlags, IShellItem psiDestinationFolder, string pszNewName, string pszTemplateName, uint dwFileAttributes, HRESULT hrNew, IShellItem psiNewItem) { return HRESULT.S_OK; }

        public HRESULT UpdateProgress(uint iWorkTotal, uint iWorkSoFar)
        {
            _op.PointsTotal = iWorkTotal;
            _op.Points = iWorkSoFar;
            if (_op.BytesTotal > 0 && iWorkTotal > 0) _op.Bytes = (long)(_op.BytesTotal * Math.Min(1.0, (double)iWorkSoFar / iWorkTotal));
            _op.Sample(_op.Bytes);
            _op.TrackSpeed();
            if (_op.State == OpState.Preparing || _op.State == OpState.Waiting) UiState(OpState.Running);
            SaveJournalNowAndThen();   // during a long file too, so the journal knows which file is half-written
            return Go();
        }

        public HRESULT ResetTimer() { return HRESULT.S_OK; }
        public HRESULT PauseTimer() { return HRESULT.S_OK; }
        public HRESULT ResumeTimer() { return HRESULT.S_OK; }
    }
}