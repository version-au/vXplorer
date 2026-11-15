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

        /// <summary>The same path, ignoring case.</summary>
        public static bool Same(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }
}
