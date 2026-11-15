using System;
using Microsoft.Win32;

namespace vXplorer.Core
{
    /// <summary>
    /// "Open folders with vXplorer": per-user registry keys (HKCU only - no admin, nothing for other users) that make vXplorer the
    /// default for folders and drives (double-click on the desktop, "Open folder" from other apps, folder shortcuts) and for Win+E.
    /// Turning it off removes exactly what was added and puts back any default that was there before (kept under Software\vXplorer).
    /// The roots can be changed for tests, so a test never touches the real keys.
    /// </summary>
    public static class DefaultFileManager
    {
        /// <summary>The "computer" that Win+E opens a window of (Home / This PC).</summary>
        const string WinE = @"CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}";
        const string Verb = "vxplorer";
        static readonly string[] Types = { "Directory", "Drive" };

        public static string ClassesRoot = @"Software\Classes";
        public static string StashRoot = @"Software\vXplorer\PreviousDefaults";

        public static bool IsOn()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(ClassesRoot + @"\Directory\shell"))
                    return k != null && string.Equals(k.GetValue("") as string, Verb, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static void TurnOn(string exe)
        {
            string quoted = "\"" + exe + "\"";
            using (RegistryKey stash = Registry.CurrentUser.CreateSubKey(StashRoot))
                foreach (string type in Types)
                {
                    using (RegistryKey shell = Registry.CurrentUser.CreateSubKey(ClassesRoot + @"\" + type + @"\shell"))
                    {
                        string before = shell.GetValue("") as string;
                        if (!string.Equals(before, Verb, StringComparison.OrdinalIgnoreCase) && stash.GetValue(type) == null)
                            stash.SetValue(type, before ?? "");   // "" = there was none
                        shell.SetValue("", Verb);
                        using (RegistryKey verb = shell.CreateSubKey(Verb))
                        {
                            verb.SetValue("", "Open in vXplorer");
                            using (RegistryKey cmd = verb.CreateSubKey("command")) cmd.SetValue("", quoted + " \"%1\"");
                        }
                    }
                }
            // Win+E: a new vXplorer window (empty DelegateExecute = run the command rather than Explorer's handler)
            using (RegistryKey cmd = Registry.CurrentUser.CreateSubKey(ClassesRoot + @"\" + WinE + @"\shell\opennewwindow\command"))
            {
                cmd.SetValue("", quoted);
                cmd.SetValue("DelegateExecute", "");
            }
            Log.Write("Default file manager: on (" + exe + ")");
        }

        public static void TurnOff()
        {
            using (RegistryKey stash = Registry.CurrentUser.OpenSubKey(StashRoot, true))
                foreach (string type in Types)
                {
                    using (RegistryKey shell = Registry.CurrentUser.OpenSubKey(ClassesRoot + @"\" + type + @"\shell", true))
                    {
                        if (shell == null) continue;
                        try { shell.DeleteSubKeyTree(Verb, false); } catch { }
                        string before = stash == null ? null : stash.GetValue(type) as string;
                        if (string.Equals(shell.GetValue("") as string, Verb, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(before)) shell.SetValue("", before);
                            else shell.DeleteValue("", false);
                        }
                    }
                    if (stash != null) stash.DeleteValue(type, false);
                }
            try { Registry.CurrentUser.DeleteSubKeyTree(ClassesRoot + @"\" + WinE + @"\shell\opennewwindow", false); } catch { }
            DeleteIfEmpty(ClassesRoot + @"\" + WinE + @"\shell");
            DeleteIfEmpty(ClassesRoot + @"\" + WinE);
            DeleteIfEmpty(StashRoot);
            Log.Write("Default file manager: off");
        }

        static void DeleteIfEmpty(string path)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(path))
                {
                    if (k == null || k.SubKeyCount > 0 || k.ValueCount > 0) return;
                }
                Registry.CurrentUser.DeleteSubKey(path, false);
            }
            catch { }
        }
    }
}
