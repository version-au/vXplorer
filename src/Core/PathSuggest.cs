using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace vXplorer.Core
{
    /// <summary>
    /// What the address bar suggests while you type a path, as (name, path): the folders in the folder typed so far whose names start
    /// with what comes after its last backslash; a computer's shares after \\NAME\; the known computers after \\; and before any
    /// backslash, the places the caller gives (This PC, Network, Quick access, drives). Slow ones (network) - call it on a worker.
    /// </summary>
    public static class PathSuggest
    {
        public const int Max = 12;

        /// <summary>The folder to look in and the start of the name typed after it ("C:\Us" -> "C:\", "Us"); false with no folder yet.</summary>
        public static bool Split(string text, out string folder, out string prefix)
        {
            folder = prefix = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = Environment.ExpandEnvironmentVariables(text.TrimStart().Trim('"')).Replace('/', '\\');
            int cut = t.LastIndexOf('\\');
            if (cut < 0) return false;
            if (t.StartsWith(@"\\") && cut < 2) { folder = @"\\"; prefix = t.Substring(2); return true; }   // a computer's name
            folder = t.Substring(0, cut + 1);
            prefix = t.Substring(cut + 1);
            // "C:\…" or "\\server\…" - not "..\x" or "\x", which would depend on vXplorer's own working folder
            return folder.StartsWith(@"\\") || (folder.Length >= 3 && char.IsLetter(folder[0]) && folder[1] == ':' && folder[2] == '\\');
        }

        /// <summary>
        /// The candidates whose name starts with the prefix (a drive also by its letter: "c" finds "Local Disk (C:)"), in Explorer's name
        /// order, at most <paramref name="max"/>.
        /// </summary>
        public static List<Tuple<string, string>> Filter(IEnumerable<Tuple<string, string>> all, string prefix, int max = Max)
        {
            prefix = prefix ?? "";
            return all.Where(c => c.Item1.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                               || (c.Item2.Length <= 3 && c.Item2.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                      .OrderBy(c => c.Item1, Comparer<string>.Create(Native.CompareNatural))
                      .Take(max)
                      .ToList();
        }

        /// <summary>The suggestions for the text typed so far (empty when there are none or the folder can't be read).</summary>
        public static List<Tuple<string, string>> For(string text, bool showHidden, IEnumerable<Tuple<string, string>> places, int max = Max)
        {
            string folder, prefix;
            try
            {
                if (!Split(text, out folder, out prefix)) return Filter(places ?? Enumerable.Empty<Tuple<string, string>>(), (text ?? "").Trim(), max);
                if (folder == @"\\") return Filter(NetworkPlaces.KnownComputers(), prefix, max);   // no network search just for this
                if (NetworkPlaces.IsServer(folder)) return Filter(NetworkPlaces.Shares(folder), prefix, max);
                var opts = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    AttributesToSkip = showHidden ? 0 : FileAttributes.Hidden | FileAttributes.System
                };
                // only the names that match are kept, so a folder with thousands of subfolders costs one listing, not a sort of them all
                var found = new List<Tuple<string, string>>();
                foreach (string d in Directory.EnumerateDirectories(folder, string.IsNullOrEmpty(prefix) ? "*" : prefix + "*", opts))
                    found.Add(Tuple.Create(Path.GetFileName(d), d));
                return Filter(found, prefix, max);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is System.ComponentModel.Win32Exception)
            {
                return new List<Tuple<string, string>>();
            }
        }
    }
}
