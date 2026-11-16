using System;
using System.IO;

namespace vXplorer.Core
{
    /// <summary>Path helpers with no window (used everywhere through a global using static - see GlobalUsings.cs).</summary>
    public static class Paths
    {
        /// <summary>A full path, or null. Drive roots keep their backslash ("C:\"), other folders lose a trailing one.</summary>
        public static string Clean(string p)
        {
            try
            {
                p = Environment.ExpandEnvironmentVariables(p.Trim().Trim('"'));
                if (p.Length == 2 && p[1] == ':') p += "\\";
                string full = Path.GetFullPath(p);
                string root = Path.GetPathRoot(full);
                if (full.Length > (root ?? "").Length) full = full.TrimEnd('\\');
                return full;
            }
            catch { return null; }
        }

        static readonly System.Collections.Generic.Dictionary<string, DateTime> _away = new System.Collections.Generic.Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The network place a path is on ("\\nas\media", or "S:\" for a network drive); null for this PC's own drives.</summary>
        public static string NetworkRoot(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return null;
                if (path.StartsWith(@"\\"))
                {
                    string[] parts = path.Substring(2).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                    return parts.Length == 0 ? null : @"\\" + parts[0] + (parts.Length > 1 ? "\\" + parts[1] : "");
                }
                if (path.Length >= 2 && path[1] == ':')
                {
                    string root = path.Substring(0, 2) + "\\";
                    return new DriveInfo(root).DriveType == DriveType.Network ? root : null;     // doesn't touch the network
                }
            }
            catch { }
            return null;
        }

        /// <summary>That network place didn't answer a moment ago (asked again after a quarter of a minute).</summary>
        public static bool Away(string path)
        {
            string root = NetworkRoot(path);
            if (root == null) return false;
            lock (_away) { DateTime until; return _away.TryGetValue(root, out until) && until > DateTime.UtcNow; }
        }

        /// <summary>
        /// Is the folder there? On this PC's drives: asked straight. On the network: a NAS that's off or a VPN that's down makes
        /// Windows wait many seconds for every path asked - with pinned folders and saved tabs there, the window stood still for
        /// a minute at start. So the answer is waited for 4 seconds; after that the place counts as away for a quarter of a minute
        /// and everything on it says "not there" at once.
        /// </summary>
        public static bool FolderThere(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string root = NetworkRoot(path);
            if (root == null) return Directory.Exists(path);
            lock (_away) { DateTime until; if (_away.TryGetValue(root, out until) && until > DateTime.UtcNow) return false; }
            var t = System.Threading.Tasks.Task.Run(() => Directory.Exists(path));
            try { if (t.Wait(4000)) { lock (_away) _away.Remove(root); return t.Result; } } catch { return false; }
            lock (_away) _away[root] = DateTime.UtcNow.AddSeconds(15);
            Log.Write(root + " didn't answer within 4 seconds - treating it as away for now");
            return false;
        }

        /// <summary>The same path, ignoring case.</summary>
        public static bool Same(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }
}
