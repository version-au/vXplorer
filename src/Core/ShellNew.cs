using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace vXplorer.Core
{
    /// <summary>A file type Explorer's New menu offers (Text Document, Compressed (zipped) Folder …).</summary>
    public sealed class NewItemType
    {
        public string Extension;
        /// <summary>What the New menu shows ("Text Document").</summary>
        public string Name;
        /// <summary>The new file's name without the extension ("New Text Document").</summary>
        public string FileBase;
        /// <summary>A template file to copy, or null.</summary>
        public string Template;
        /// <summary>Bytes to write (ShellNew "Data"), or null; neither = an empty file.</summary>
        public byte[] Data;
    }

    /// <summary>
    /// The New menu's file types, read the way Explorer does: the extensions in the PostSetup\ShellNew "Classes" list, each with a ShellNew
    /// key (NullFile, FileName or Data; types that run a Command or a Handler are left out - those come from Show more options).
    /// </summary>
    public static class ShellNew
    {
        static List<NewItemType> _cache;
        static readonly object Gate = new object();

        public static List<NewItemType> Types()
        {
            lock (Gate)
            {
                if (_cache == null)
                {
                    try { _cache = Read(); }
                    catch (Exception ex) { Log.Write("ShellNew: " + ex.Message); _cache = new List<NewItemType>(); }
                }
                return _cache;
            }
        }

        static List<NewItemType> Read()
        {
            var exts = new List<string>();
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Discardable\PostSetup\ShellNew"))
            {
                var v = k == null ? null : k.GetValue("Classes") as string[];
                if (v != null) exts.AddRange(v);
            }
            if (exts.Count == 0) exts.AddRange(new[] { ".txt", ".zip", ".rtf", ".bmp" });   // Explorer hasn't built its list yet

            var list = new List<NewItemType>();
            foreach (string ext in exts.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!ext.StartsWith(".") || ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || ext.Equals(".library-ms", StringComparison.OrdinalIgnoreCase))
                    continue;
                NewItemType t = FromKey(ext);
                if (t != null) list.Add(t);
            }
            // Text Document is always offered, even when Windows' own entry for it is missing (Store Notepad can remove it)
            if (!list.Any(t => t.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)))
            {
                string name = Native.TypeName("new.txt", false);
                list.Add(new NewItemType { Extension = ".txt", Name = name, FileBase = "New " + name });
            }
            return list.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static NewItemType FromKey(string ext)
        {
            using (RegistryKey extKey = Registry.ClassesRoot.OpenSubKey(ext))
            {
                if (extKey == null) return null;
                RegistryKey sn = FindShellNew(extKey);
                if (sn == null) return null;
                using (sn)
                {
                    if (sn.GetValue("Command") != null || sn.GetValue("Handler") != null) return null;
                    // the names: MenuText / ItemName when the type gives them ("@file.exe,-1000" resources), else its type name
                    string typeName = Native.TypeName("new" + ext, false);
                    string menuText = Native.IndirectString(sn.GetValue("MenuText") as string);
                    string itemName = Native.IndirectString(sn.GetValue("ItemName") as string);
                    var t = new NewItemType
                    {
                        Extension = ext,
                        Name = menuText ?? typeName,
                        FileBase = itemName ?? "New " + (menuText ?? typeName)
                    };
                    foreach (char c in Path.GetInvalidFileNameChars()) t.FileBase = t.FileBase.Replace(c.ToString(), "");
                    object data = sn.GetValue("Data");
                    object file = sn.GetValue("FileName");
                    if (data is byte[]) t.Data = (byte[])data;
                    else if (data is string) t.Data = Encoding.Default.GetBytes((string)data);
                    else if (file is string)
                    {
                        t.Template = FindTemplate((string)file);
                        if (t.Template == null) return null;   // a template that isn't there: Explorer leaves it out too
                    }
                    else if (sn.GetValue("NullFile") == null) return null;
                    return t;
                }
            }
        }

        /// <summary>HKCR\.ext\ShellNew, HKCR\.ext\&lt;ProgId&gt;\ShellNew, or any HKCR\.ext\*\ShellNew.</summary>
        static RegistryKey FindShellNew(RegistryKey extKey)
        {
            RegistryKey k = extKey.OpenSubKey("ShellNew");
            if (k != null) return k;
            var progId = extKey.GetValue("") as string;
            if (!string.IsNullOrEmpty(progId))
            {
                k = extKey.OpenSubKey(progId + "\\ShellNew");
                if (k != null) return k;
            }
            foreach (string sub in extKey.GetSubKeyNames())
            {
                k = extKey.OpenSubKey(sub + "\\ShellNew");
                if (k != null) return k;
            }
            return null;
        }

        static string FindTemplate(string name)
        {
            name = Environment.ExpandEnvironmentVariables(name);
            if (Path.IsPathRooted(name)) return File.Exists(name) ? name : null;
            string[] places =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Templates),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonTemplates),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "ShellNew")
            };
            foreach (string dir in places)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                string p = Path.Combine(dir, name);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        /// <summary>Make "New &lt;type&gt;.ext" (or "(2)" …) in folder; returns its path.</summary>
        public static string Create(string folder, NewItemType t)
        {
            string path = Unique(folder, t.FileBase, t.Extension);
            if (t.Template != null)
            {
                File.Copy(t.Template, path, false);
                DateTime now = DateTime.Now;   // a new file, not the template's dates
                File.SetCreationTime(path, now);
                File.SetLastWriteTime(path, now);
            }
            else File.WriteAllBytes(path, t.Data ?? new byte[0]);
            return path;
        }

        /// <summary>folder\name.ext, or folder\name (2).ext … - the first that doesn't exist.</summary>
        public static string Unique(string folder, string name, string ext)
        {
            string path = Path.Combine(folder, name + ext);
            for (int n = 2; File.Exists(path) || Directory.Exists(path); n++) path = Path.Combine(folder, name + " (" + n + ")" + ext);
            return path;
        }
    }
}
