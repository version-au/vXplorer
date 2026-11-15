using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using Vanara.Windows.Shell;

namespace vXplorer.Core
{
    /// <summary>
    /// The Network place: the computers Windows can see (its Network folder - network discovery: WSD, NetBIOS), and a computer's shared
    /// folders. Both can take seconds, so they're only looked up when asked for (Network opened or expanded, a computer opened).
    /// </summary>
    public static class NetworkPlaces
    {
        /// <summary>The shell's Network folder.</summary>
        public const string NetworkPath = "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}";

        public static bool IsNetwork(string p)
        {
            if (p == null) return false;
            p = p.Trim();
            return string.Equals(p, NetworkPath, StringComparison.OrdinalIgnoreCase) || string.Equals(p, "Network", StringComparison.OrdinalIgnoreCase)
                || string.Equals(p, "shell:NetworkPlacesFolder", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A computer on the network: \\NAME (no share after it).</summary>
        public static bool IsServer(string p)
        {
            if (p == null) return false;
            p = p.Trim().TrimEnd('\\');
            return p.StartsWith(@"\\") && p.Length > 2 && p.IndexOf('\\', 2) < 0;
        }

        /// <summary>\\NAME, tidied ("\\name\" -> "\\name").</summary>
        public static string ServerPath(string p) { return p.Trim().TrimEnd('\\'); }

        /// <summary>A share's root: \\NAME\share (its Up is the computer).</summary>
        public static bool IsShareRoot(string p)
        {
            if (p == null || !p.StartsWith(@"\\")) return false;
            string[] parts = p.Trim().TrimEnd('\\').Substring(2).Split('\\');
            return parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        static extern int WNetGetConnection(string local, System.Text.StringBuilder remote, ref int length);

        /// <summary>
        /// Computers vXplorer knows without searching: the ones behind mapped network drives (S: = \\NAS\share -> \\NAS). Quick, and many
        /// NAS boxes never show up in network discovery at all.
        /// </summary>
        public static List<Tuple<string, string>> KnownComputers()
        {
            var r = new List<Tuple<string, string>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in System.IO.DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != System.IO.DriveType.Network) continue;
                    var sb = new System.Text.StringBuilder(512);
                    int len = sb.Capacity;
                    if (WNetGetConnection(d.Name.Substring(0, 2), sb, ref len) != 0) continue;
                    string unc = sb.ToString();
                    if (!unc.StartsWith(@"\\")) continue;
                    int cut = unc.IndexOf('\\', 2);
                    string server = cut < 0 ? unc : unc.Substring(0, cut);
                    if (seen.Add(server)) r.Add(Tuple.Create(server.Substring(2), server));
                }
                catch { }
            }
            r.Sort((a, b) => Native.CompareNatural(a.Item1, b.Item1));
            return r;
        }

        /// <summary>The known computers plus the ones network discovery finds, as (name, \\NAME), without repeats.</summary>
        public static async Task<List<Tuple<string, string>>> AllComputers()
        {
            var known = await Task.Run(() => KnownComputers());
            var found = await Computers();
            var r = new List<Tuple<string, string>>(known);
            foreach (var f in found)
                if (!r.Any(k => string.Equals(k.Item2, f.Item2, StringComparison.OrdinalIgnoreCase))) r.Add(f);
            r.Sort((a, b) => Native.CompareNatural(a.Item1, b.Item1));
            return r;
        }

        /// <summary>The computers in Windows' Network folder, as (name, \\NAME). Media devices and the like (not \\ paths) are left out.</summary>
        public static Task<List<Tuple<string, string>>> Computers()
        {
            var done = new TaskCompletionSource<List<Tuple<string, string>>>();
            var t = new Thread(() =>   // the shell's namespace wants an STA thread
            {
                var r = new List<Tuple<string, string>>();
                try
                {
                    using (var net = new ShellFolder(Shell32.KNOWNFOLDERID.FOLDERID_NetworkFolder))
                        foreach (ShellItem item in net)
                        {
                            using (item)
                            {
                                string parsing = null, name = null;
                                try { parsing = item.ParsingName; name = item.Name; } catch { }
                                if (parsing == null || !IsServer(parsing)) continue;
                                r.Add(Tuple.Create(string.IsNullOrEmpty(name) ? parsing.Substring(2) : name, ServerPath(parsing)));
                            }
                        }
                }
                catch (Exception ex) { Log.Write("Network: " + ex.Message); }
                r.Sort((a, b) => Native.CompareNatural(a.Item1, b.Item1));
                done.TrySetResult(r);
            }) { IsBackground = true, Name = "Network" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            return done.Task;
        }

        // ---- a computer's shares (NetShareEnum, level 1) ----

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHARE_INFO_1 { public string Name; public uint Type; public string Remark; }

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        static extern int NetShareEnum(string server, int level, out IntPtr buf, int prefMaxLen, out int read, out int total, ref int resume);

        [DllImport("netapi32.dll")]
        static extern int NetApiBufferFree(IntPtr buf);

        const uint STYPE_MASK = 0xFF, STYPE_DISKTREE = 0, STYPE_SPECIAL = 0x80000000;

        /// <summary>The computer's shared folders, as (share, \\NAME\share) - not printers or the hidden admin shares (C$, IPC$). Throws if it can't be read.</summary>
        public static List<Tuple<string, string>> Shares(string server)
        {
            server = ServerPath(server);
            var r = new List<Tuple<string, string>>();
            IntPtr buf;
            int read, total, resume = 0;
            int err = NetShareEnum(server, 1, out buf, -1, out read, out total, ref resume);
            if (err != 0 && err != 234)
            {
                if (buf != IntPtr.Zero) NetApiBufferFree(buf);
                throw new System.ComponentModel.Win32Exception(err == 53 ? 53 : err);
            }
            try
            {
                int size = Marshal.SizeOf<SHARE_INFO_1>();
                for (int i = 0; i < read; i++)
                {
                    var s = Marshal.PtrToStructure<SHARE_INFO_1>(buf + i * size);
                    if ((s.Type & STYPE_MASK) != STYPE_DISKTREE || (s.Type & STYPE_SPECIAL) != 0 || s.Name.EndsWith("$")) continue;
                    r.Add(Tuple.Create(s.Name, server + "\\" + s.Name));
                }
            }
            finally { NetApiBufferFree(buf); }
            r.Sort((a, b) => Native.CompareNatural(a.Item1, b.Item1));
            return r;
        }
    }
}
