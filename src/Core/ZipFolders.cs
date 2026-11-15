using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace vXplorer.Core
{
    /// <summary>
    /// Zip files as folders: a path like C:\x\photos.zip\2024\a.jpg is "inside" photos.zip. Listing reads the zip's directory (.NET's
    /// ZipArchive - no shell); files come out by extracting (to open, copy or drag them), and Extract all / Compress make real files.
    /// Zips are read-only here: nothing is added to or changed inside one.
    /// </summary>
    public static class ZipFolders
    {
        public static bool IsZipFile(string path)
        {
            return path != null && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
        }

        /// <summary>A path in or at a zip: the zip file and the path inside it ("2024/a.jpg", "" for its top). False for ordinary paths.</summary>
        public static bool TrySplit(string path, out string zip, out string inner)
        {
            zip = inner = null;
            if (string.IsNullOrEmpty(path) || path.StartsWith("::")) return false;
            try
            {
                if (Directory.Exists(path)) return false;   // the common case, quickly
                for (string p = path.TrimEnd('\\'); !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
                {
                    if (p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
                    {
                        zip = p;
                        inner = path.TrimEnd('\\').Substring(p.Length).Trim('\\').Replace('\\', '/');
                        return true;
                    }
                    if (Directory.Exists(p)) return false;   // got to a real folder without passing a zip
                }
            }
            catch { }
            return false;
        }

        /// <summary>A path in a zip folder (not the zip file itself).</summary>
        public static bool IsZipPath(string path) { string z, i; return TrySplit(path, out z, out i); }

        /// <summary>What's directly inside a folder of the zip (inner "" = its top): folders (from the entries' paths) and files.</summary>
        public static List<FileItem> List(string zipPath, string inner)
        {
            string prefix = inner.Length == 0 ? "" : inner.TrimEnd('/') + "/";
            var folders = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            var files = new List<FileItem>();
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    string full = e.FullName.Replace('\\', '/');
                    if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    string rest = full.Substring(prefix.Length);
                    if (rest.Length == 0) continue;
                    int slash = rest.IndexOf('/');
                    if (slash >= 0)
                    {
                        string name = rest.Substring(0, slash);
                        if (name.Length > 0 && !folders.ContainsKey(name)) folders[name] = e.LastWriteTime.LocalDateTime;
                        continue;
                    }
                    string here = Path.Combine(zipPath, (prefix + rest).Replace('/', '\\'));
                    files.Add(new FileItem
                    {
                        Name = rest, Path = here, IsFolder = false, Size = e.Length, Modified = e.LastWriteTime.LocalDateTime,
                        Created = DateTime.MinValue, TypeName = Native.TypeName(rest, false), IconKey = "ext:" + Path.GetExtension(rest).ToLowerInvariant()
                    });
                }
            var list = folders.Select(f => new FileItem
            {
                Name = f.Key, Path = Path.Combine(zipPath, (prefix + f.Key).Replace('/', '\\')), IsFolder = true, Size = -1, Modified = f.Value,
                Created = DateTime.MinValue, TypeName = Native.TypeName(f.Key, true), IconKey = "<folder>"
            }).ToList();
            list.AddRange(files);
            return list;
        }

        /// <summary>
        /// Extract items of the zip (paths inside it, files or folders) into dest, keeping their names. Returns the top-level paths made.
        /// Something already there with the same name gets " (2)" etc.
        /// </summary>
        public static List<string> Extract(string zipPath, IList<string> inners, string dest, Action<double> progress = null)
        {
            var made = new List<string>();
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                var entries = zip.Entries.ToList();
                long total = Math.Max(1, entries.Where(e => inners.Any(i => Under(e.FullName, i))).Sum(e => e.Length)), done = 0;
                foreach (string inner in inners)
                {
                    string name = inner.TrimEnd('/').Split('/').Last();
                    string target = Unique(Path.Combine(dest, name));
                    made.Add(target);
                    string prefix = inner.TrimEnd('/');
                    foreach (ZipArchiveEntry e in entries)
                    {
                        string full = e.FullName.Replace('\\', '/');
                        if (!Under(full, prefix)) continue;
                        string rest = full.Length > prefix.Length ? full.Substring(prefix.Length).TrimStart('/') : "";
                        string to = rest.Length == 0 ? target : Path.Combine(target, rest.Replace('/', '\\'));
                        // never outside the target (a zip with "..\" names)
                        string fullTo = Path.GetFullPath(to);
                        if (!fullTo.StartsWith(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) continue;
                        if (full.EndsWith("/")) { Directory.CreateDirectory(fullTo); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(fullTo));
                        e.ExtractToFile(fullTo, false);
                        try { File.SetLastWriteTime(fullTo, e.LastWriteTime.LocalDateTime); } catch { }
                        done += e.Length;
                        if (progress != null) progress(done / (double)total);
                    }
                    if (!File.Exists(target) && !Directory.Exists(target)) Directory.CreateDirectory(target);   // an empty folder
                }
            }
            return made;
        }

        static bool Under(string entry, string inner)
        {
            entry = entry.Replace('\\', '/');
            inner = inner.TrimEnd('/');
            return string.Equals(entry.TrimEnd('/'), inner, StringComparison.OrdinalIgnoreCase) ||
                   entry.StartsWith(inner + "/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A whole zip into a new folder next to it, named after it ("photos.zip" -> "photos", or "photos (2)").</summary>
        public static string ExtractAll(string zipPath, Action<double> progress = null)
        {
            string folder = Unique(Path.Combine(Path.GetDirectoryName(zipPath), Path.GetFileNameWithoutExtension(zipPath)));
            Directory.CreateDirectory(folder);
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                long total = Math.Max(1, zip.Entries.Sum(e => e.Length)), done = 0;
                string root = Path.GetFullPath(folder);
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    string to = Path.GetFullPath(Path.Combine(folder, e.FullName.Replace('/', '\\')));
                    if (!to.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                    if (e.FullName.EndsWith("/") || e.FullName.EndsWith("\\")) { Directory.CreateDirectory(to); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    e.ExtractToFile(to, true);
                    try { File.SetLastWriteTime(to, e.LastWriteTime.LocalDateTime); } catch { }
                    done += e.Length;
                    if (progress != null) progress(done / (double)total);
                }
            }
            return folder;
        }

        /// <summary>The items (files and folders, with everything in them) into a new zip at zipPath.</summary>
        public static void Compress(IList<string> paths, string zipPath, Action<double> progress = null)
        {
            var all = new List<Tuple<string, string>>();   // (file on disk or folder, name in the zip)
            foreach (string p in paths)
            {
                string top = Path.GetFileName(p.TrimEnd('\\'));
                if (Directory.Exists(p))
                {
                    all.Add(Tuple.Create(p, top + "/"));
                    foreach (string d in Directory.EnumerateDirectories(p, "*", SearchOption.AllDirectories))
                        all.Add(Tuple.Create(d, top + "/" + d.Substring(p.TrimEnd('\\').Length + 1).Replace('\\', '/') + "/"));
                    foreach (string f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                        all.Add(Tuple.Create(f, top + "/" + f.Substring(p.TrimEnd('\\').Length + 1).Replace('\\', '/')));
                }
                else all.Add(Tuple.Create(p, top));
            }
            long total = Math.Max(1, all.Where(a => File.Exists(a.Item1)).Sum(a => new FileInfo(a.Item1).Length)), done = 0;
            string tmp = zipPath + ".partial";
            try
            {
                using (ZipArchive zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
                    foreach (Tuple<string, string> a in all)
                    {
                        if (a.Item2.EndsWith("/")) { zip.CreateEntry(a.Item2); continue; }
                        zip.CreateEntryFromFile(a.Item1, a.Item2, CompressionLevel.Optimal);
                        done += new FileInfo(a.Item1).Length;
                        if (progress != null) progress(done / (double)total);
                    }
                File.Move(tmp, zipPath);
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
                throw;
            }
        }

        /// <summary>The path, or "name (2).ext", "name (3).ext" … if something's already there.</summary>
        public static string Unique(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path), name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
            if (Directory.Exists(path)) { name = Path.GetFileName(path); ext = ""; }
            for (int n = 2; ; n++)
            {
                string p = Path.Combine(dir, name + " (" + n + ")" + ext);
                if (!File.Exists(p) && !Directory.Exists(p)) return p;
            }
        }

        /// <summary>A fresh folder under %TEMP%\vXplorer for files taken out of a zip (to open, copy or drag them).</summary>
        public static string TempFolder()
        {
            string d = Path.Combine(Path.GetTempPath(), "vXplorer", "zip", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(d);
            return d;
        }

        /// <summary>Old extracted copies (from earlier days) - tidied at start-up.</summary>
        public static void CleanTemp()
        {
            try
            {
                string root = Path.Combine(Path.GetTempPath(), "vXplorer", "zip");
                if (!Directory.Exists(root)) return;
                foreach (string d in Directory.EnumerateDirectories(root))
                    if ((DateTime.Now - Directory.GetCreationTime(d)).TotalDays > 2) { try { Directory.Delete(d, true); } catch { } }
            }
            catch { }
        }
    }
}
