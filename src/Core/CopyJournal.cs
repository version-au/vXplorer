using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace vXplorer.Core
{
    /// <summary>
    /// A note of a copy or move while it runs (Log.Folder\unfinished\&lt;id&gt;.json): what it was copying where, the choices made for
    /// items that were already there, which top-level items are done and which file was in progress. Deleted when the operation ends;
    /// one found at start = vXplorer (or Windows) stopped part way, and the rest can be finished (MainWindow.Operations: ResumeJournal).
    /// </summary>
    public sealed class CopyJournal
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        /// <summary>"copy" or "move".</summary>
        public string Kind { get; set; } = "copy";
        public List<string> Sources { get; set; } = new List<string>();
        public string Dest { get; set; } = "";
        /// <summary>Sources that were given a new name because something was already there ("Keep both").</summary>
        public List<string> KeepBoth { get; set; } = new List<string>();
        public List<string> Done { get; set; } = new List<string>();
        /// <summary>The file being copied when it was last saved (its destination path) - half-written if it stopped then.</summary>
        public string Current { get; set; } = "";
        public DateTime Started { get; set; } = DateTime.Now;

        static string Folder { get { return Path.Combine(Log.Folder, "unfinished"); } }
        string FilePath { get { return Path.Combine(Folder, Id + ".json"); } }

        readonly object _lock = new object();

        public void MarkDone(string source)
        {
            lock (_lock) { if (!Done.Contains(source, StringComparer.OrdinalIgnoreCase)) Done.Add(source); }
        }

        public void Save()
        {
            try
            {
                string json;
                lock (_lock) json = JsonSerializer.Serialize(this);
                Directory.CreateDirectory(Folder);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, FilePath, true);
            }
            catch (Exception ex) { Log.Write("Copy journal: " + ex.Message); }
        }

        public void Delete()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch (Exception ex) { Log.Write("Copy journal delete: " + ex.Message); }
        }

        /// <summary>The journals left by operations that didn't finish (oldest first).</summary>
        public static List<CopyJournal> Unfinished()
        {
            var list = new List<CopyJournal>();
            try
            {
                if (!Directory.Exists(Folder)) return list;
                foreach (string f in Directory.GetFiles(Folder, "*.json"))
                {
                    try
                    {
                        var j = JsonSerializer.Deserialize<CopyJournal>(File.ReadAllText(f));
                        if (j != null && j.Sources.Count > 0 && !string.IsNullOrEmpty(j.Dest)) list.Add(j);
                        else File.Delete(f);
                    }
                    catch (Exception ex) { Log.Write("Copy journal " + f + ": " + ex.Message); }
                }
            }
            catch (Exception ex) { Log.Write("Copy journals: " + ex.Message); }
            return list.OrderBy(j => j.Started).ToList();
        }

        /// <summary>The top-level items it hadn't finished (and that are still there to copy / move).</summary>
        public List<string> Left()
        {
            return Sources.Where(s => !Done.Contains(s, StringComparer.OrdinalIgnoreCase) && (File.Exists(s) || Directory.Exists(s))).ToList();
        }
    }
}
