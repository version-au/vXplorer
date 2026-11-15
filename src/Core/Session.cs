using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace vXplorer.Core
{
    /// <summary>One window in the session file: its tabs (paths), which one was showing, and where the window was.</summary>
    public sealed class SessionWindow
    {
        public List<string> Tabs { get; set; } = new List<string>();
        public int Active { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public sealed class SessionState
    {
        public int Pid { get; set; }
        public DateTime Started { get; set; }
        public List<SessionWindow> Windows { get; set; } = new List<SessionWindow>();
    }

    /// <summary>
    /// The safety net for crashes: while vXplorer runs, session.json (in the settings folder) holds every window's tabs, saved a moment
    /// after they change; a normal exit deletes it. Finding it at start-up means the last run ended unexpectedly (a crash, or it was
    /// ended in Task Manager) - vXplorer then copies what Windows recorded about the crash into the log and offers to reopen the tabs.
    /// </summary>
    public static class Session
    {
        static string FilePath { get { return Path.Combine(Log.Folder, "session.json"); } }
        static readonly DateTime StartedAt = DateTime.Now;

        /// <summary>The last run's session, if it didn't end normally (taken: the file is gone after this), else null.</summary>
        public static SessionState TakeUnfinished()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var s = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(FilePath));
                File.Delete(FilePath);
                return s;
            }
            catch (Exception ex) { Log.Write("Session: " + ex.Message); try { File.Delete(FilePath); } catch { } return null; }
        }

        public static void Save(List<SessionWindow> windows)
        {
            try
            {
                Directory.CreateDirectory(Log.Folder);
                var s = new SessionState { Pid = Environment.ProcessId, Started = StartedAt, Windows = windows };
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(s));
                File.Move(tmp, FilePath, true);
            }
            catch (Exception ex) { Log.Write("Session save: " + ex.Message); }
        }

        /// <summary>vXplorer is closing normally.</summary>
        public static void End()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
        }

        /// <summary>
        /// What Windows' Application log says about vXplorer ending since 'since' (Application Error = the fault, .NET Runtime = the
        /// managed stack) - written into vxplorer.log so there's something to look at (or send).
        /// </summary>
        public static void LogCrashDetails(DateTime since)
        {
            try
            {
                string time = since.ToUniversalTime().AddSeconds(-5).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                string xpath = "*[System[(Provider[@Name='Application Error'] or Provider[@Name='.NET Runtime'] or Provider[@Name='Windows Error Reporting'])" +
                               " and TimeCreated[@SystemTime>='" + time + "']]]";
                var query = new System.Diagnostics.Eventing.Reader.EventLogQuery("Application", System.Diagnostics.Eventing.Reader.PathType.LogName, xpath);
                int found = 0;
                using (var reader = new System.Diagnostics.Eventing.Reader.EventLogReader(query))
                    for (var e = reader.ReadEvent(); e != null && found < 4; e = reader.ReadEvent())
                        using (e)
                        {
                            string text = "";
                            try { text = e.FormatDescription() ?? ""; } catch { }
                            if (text.IndexOf("vXplorer", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            found++;
                            if (text.Length > 6000) text = text.Substring(0, 6000) + " …";
                            Log.Write("Last run ended unexpectedly - Windows' event log (" + e.ProviderName + ", " + e.TimeCreated + "):\r\n" + text);
                        }
                if (found == 0) Log.Write("Last run ended unexpectedly - nothing about it in Windows' event log (ended in Task Manager, or the PC went off?).");
            }
            catch (Exception ex) { Log.Write("Last run ended unexpectedly - couldn't read Windows' event log: " + ex.Message); }
        }
    }
}
