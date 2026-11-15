using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>
    /// Ctrl+Z: undo the last file operations done in vXplorer (up to 30) - renames, moves, copies, new items and deletes to the Recycle Bin -
    /// as Explorer does. Deleting for good can't be undone. Ctrl+Y (or Ctrl+Shift+Z) redoes what was undone; any new operation clears redo.
    /// </summary>
    public partial class MainWindow
    {
        enum UndoKind { Rename, Move, Copy, Create, Recycle }

        sealed class UndoStep
        {
            public UndoKind Kind;
            /// <summary>Rename / Move: (old path, new path).</summary>
            public List<Tuple<string, string>> Pairs = new List<Tuple<string, string>>();
            /// <summary>Copy / Create: what was made. Recycle: the paths that were deleted.</summary>
            public List<string> Paths = new List<string>();
            public DateTime Started;
            public string What;   // "rename", "move" … for "Undo rename"
        }

        readonly List<UndoStep> _undo = new List<UndoStep>();
        /// <summary>
        /// What Ctrl+Y does again, newest last. The kind is the original operation's: Rename / Move = (from, to) pairs to do again;
        /// Copy / Create = items the undo sent to the Recycle Bin (Started = when), to bring back; Recycle = items to delete again.
        /// </summary>
        readonly List<UndoStep> _redo = new List<UndoStep>();
        const int UndoLimit = 30;
        /// <summary>A redo is recording its own undo step: that mustn't clear the rest of the redo list.</summary>
        bool _redoing;

        void Remember(UndoStep step)
        {
            if (step.Pairs.Count == 0 && step.Paths.Count == 0) return;
            Operations.Raise();   // an Undo link in the panel may no longer be the last step
            _undo.Add(step);
            if (_undo.Count > UndoLimit) _undo.RemoveAt(0);
            if (!_redoing) _redo.Clear();   // something new: what was undone can't be redone on top of it
        }

        /// <summary>Note what an operation did so Ctrl+Z can take it back.</summary>
        UndoStep RememberOperation(UndoKind kind, FileOps.Result r)
        {
            var step = new UndoStep { Kind = kind, Started = r.Started };
            switch (kind)
            {
                case UndoKind.Rename: step.Pairs.AddRange(r.Pairs); step.What = "rename"; break;
                case UndoKind.Move: step.Pairs.AddRange(r.Pairs); step.What = "move"; break;
                case UndoKind.Copy: step.Paths.AddRange(r.Pairs.Select(p => p.Item2)); step.What = "copy"; break;
                case UndoKind.Recycle: step.Paths.AddRange(r.Deleted); step.What = "delete"; break;
            }
            Remember(step);
            return step;
        }

        void RememberCreated(IEnumerable<string> paths, string what)
        {
            var step = new UndoStep { Kind = UndoKind.Create, Started = DateTime.Now, What = what };
            step.Paths.AddRange(paths);
            Remember(step);
        }

        /// <summary>"Undo rename" for the menu, or null when there's nothing to undo.</summary>
        string UndoText { get { return _undo.Count == 0 ? null : "Undo " + _undo[_undo.Count - 1].What; } }

        void Undo()
        {
            if (_renaming != null) EndRename(true);
            if (_undo.Count == 0) { Status("There's nothing to undo."); return; }
            UndoStep s = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            // done: say so, and note how to do it again (redo)
            Action<FileOps.Result, UndoStep> done = (r, redo) =>
            {
                if (r.Error == null)
                {
                    Status("Undid the " + s.What + ". (Ctrl+Y does it again.)");
                    if (redo != null) _redo.Add(redo);
                }
                AfterUndo(r);
            };

            switch (s.Kind)
            {
                case UndoKind.Rename:
                {
                    Tuple<string, string> p = s.Pairs[0];
                    if (!Exists(p.Item2)) { Status("Can't undo the rename: " + Path.GetFileName(p.Item2) + " isn't there any more."); return; }
                    if (!Same(p.Item1, p.Item2) && Exists(p.Item1)) { Status("Can't undo the rename: there's already something called " + Path.GetFileName(p.Item1) + "."); return; }
                    _ops.Rename(p.Item2, Path.GetFileName(p.Item1), r => done(r, new UndoStep { Kind = UndoKind.Rename, What = s.What, Pairs = { p } }));
                    break;
                }
                case UndoKind.Move:
                {
                    var still = s.Pairs.Where(p => Exists(p.Item2) && Directory.Exists(Path.GetDirectoryName(p.Item1))).ToList();
                    var back = still.Select(p => Tuple.Create(p.Item2, Path.GetDirectoryName(p.Item1), Path.GetFileName(p.Item1))).ToList();
                    if (back.Count == 0) { Status("Can't undo the move: those items aren't there any more."); return; }
                    var redo = new UndoStep { Kind = UndoKind.Move, What = s.What };
                    redo.Pairs.AddRange(still);
                    _ops.MoveBack(back, r => done(r, redo));
                    break;
                }
                case UndoKind.Copy:
                case UndoKind.Create:
                {
                    // as Explorer: the copies / new items go to the Recycle Bin (redo brings them back from there)
                    List<string> made = s.Paths.Where(Exists).ToList();
                    if (made.Count == 0) { Status("Can't undo: those items aren't there any more."); return; }
                    _ops.Delete(made, false, r =>
                    {
                        var redo = new UndoStep { Kind = s.Kind, What = s.What, Started = r.Started };
                        redo.Paths.AddRange(r.Deleted.Count > 0 ? r.Deleted : made);
                        done(r, redo);
                    });
                    break;
                }
                case UndoKind.Recycle:
                    _ops.Restore(s.Paths, s.Started, r =>
                    {
                        var redo = new UndoStep { Kind = UndoKind.Recycle, What = s.What };
                        redo.Paths.AddRange(r.Created.Count > 0 ? r.Created : s.Paths);
                        done(r, redo);
                    });
                    break;
            }
        }

        /// <summary>"Redo rename" for the menu, or null when there's nothing to redo.</summary>
        string RedoText { get { return _redo.Count == 0 ? null : "Redo " + _redo[_redo.Count - 1].What; } }

        /// <summary>Ctrl+Y: do the last undone operation again; it goes back on the undo list (Ctrl+Z undoes it again).</summary>
        void Redo()
        {
            if (_renaming != null) EndRename(true);
            if (_redo.Count == 0) { Status("There's nothing to redo."); return; }
            UndoStep s = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            Action<FileOps.Result, Action<FileOps.Result>> finish = (r, record) =>
            {
                if (r.Error == null)
                {
                    _redoing = true;
                    try { record(r); } finally { _redoing = false; }
                    Status("Did the " + s.What + " again.");
                }
                AfterUndo(r);
            };

            switch (s.Kind)
            {
                case UndoKind.Rename:
                {
                    Tuple<string, string> p = s.Pairs[0];
                    if (!Exists(p.Item1)) { Status("Can't redo the rename: " + Path.GetFileName(p.Item1) + " isn't there any more."); return; }
                    _ops.Rename(p.Item1, Path.GetFileName(p.Item2), r => finish(r, x => { RememberOperation(UndoKind.Rename, x); _undo[_undo.Count - 1].What = s.What; }));
                    break;
                }
                case UndoKind.Move:
                {
                    var again = s.Pairs.Where(p => Exists(p.Item1) && Directory.Exists(Path.GetDirectoryName(p.Item2)))
                                       .Select(p => Tuple.Create(p.Item1, Path.GetDirectoryName(p.Item2), Path.GetFileName(p.Item2))).ToList();
                    if (again.Count == 0) { Status("Can't redo the move: those items aren't there any more."); return; }
                    _ops.MoveBack(again, r => finish(r, x => RememberOperation(UndoKind.Move, x)));
                    break;
                }
                case UndoKind.Copy:
                case UndoKind.Create:
                    // bring the copies / new items back out of the Recycle Bin
                    _ops.Restore(s.Paths, s.Started, r => finish(r, x =>
                    {
                        var step = new UndoStep { Kind = s.Kind, What = s.What, Started = DateTime.Now };
                        step.Paths.AddRange(x.Created.Count > 0 ? x.Created : s.Paths.Where(Exists));
                        Remember(step);
                    }));
                    break;
                case UndoKind.Recycle:
                {
                    List<string> again = s.Paths.Where(Exists).ToList();
                    if (again.Count == 0) { Status("Can't redo the delete: those items aren't there any more."); return; }
                    _ops.Delete(again, false, r => finish(r, x => RememberOperation(UndoKind.Recycle, x)));
                    break;
                }
            }
        }

        void AfterUndo(FileOps.Result r)
        {
            if (r.Error != null) Status(r.Error);
            if (_current == null) return;
            List<string> show = r.Created.Where(p => Searching || Same(Path.GetDirectoryName(p), _current)).ToList();
            if (show.Count > 0) _selectNext = PathSet(show);
            Refresh();
        }

        static bool Exists(string p) { return File.Exists(p) || Directory.Exists(p); }
    }
}
