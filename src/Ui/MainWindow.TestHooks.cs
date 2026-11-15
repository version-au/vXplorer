using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>
    /// Tests only: with VXPLORER_TEST_HOOKS=1 a test can drive a vXplorer window by WM_COPYDATA (kind TestKind, UTF-16 text) without it
    /// being in front - sending keystrokes needs the window focused, which takes the keyboard away from whoever is using the PC.
    /// Each command calls what the key or button calls. Without the variable these messages are ignored.
    /// </summary>
    static class TestHooks
    {
        public const int TestKind = 0x76580002;
        public static readonly bool On = Environment.GetEnvironmentVariable("VXPLORER_TEST_HOOKS") == "1";

        [StructLayout(LayoutKind.Sequential)]
        struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }

        /// <summary>The command in a WM_COPYDATA, or null (not a test command, or test hooks off).</summary>
        public static string Received(IntPtr lParam)
        {
            if (!On || lParam == IntPtr.Zero) return null;
            var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
            if (cds.dwData.ToInt64() != TestKind || cds.lpData == IntPtr.Zero) return null;
            return Marshal.PtrToStringUni(cds.lpData, Math.Max(0, cds.cbData / 2)).TrimEnd('\0');
        }
    }

    public partial class MainWindow
    {
        /// <summary>
        /// select &lt;name&gt; | undo | redo | delete | newfolder | rename &lt;new full name&gt; | go &lt;path&gt; | view &lt;mode&gt; |
        /// sort &lt;key&gt; &lt;asc|desc&gt; | dump &lt;file&gt; (the folder, the list in order, the selection and the status line).
        /// </summary>
        void RunTestCommand(string command)
        {
            string verb = command.Split(' ')[0].ToLowerInvariant();
            string arg = command.Length > verb.Length ? command.Substring(verb.Length + 1) : "";
            try
            {
                switch (verb)
                {
                    case "select":
                        Files.SelectedItems.Clear();
                        foreach (string name in arg.Split('|'))   // select a|b|c: several
                            foreach (FileItem f in _items.Where(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))) Files.SelectedItems.Add(f);
                        break;
                    case "updatecheck": UpdateCenter.CheckNow(true); break;
                    case "updateinstall": UpdateCenter.Install(UpdateCenter.Available); break;
                    case "undo": Undo(); break;
                    case "redo": Redo(); break;
                    case "delete": Delete(false); break;
                    case "newfolder": NewFolder(); break;
                    case "rename":
                        BeginRename(Files.SelectedItem as FileItem);
                        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                        {
                            if (_renameBox != null) { _renameBox.Text = arg; EndRename(true); }
                        }));
                        break;
                    case "go": Navigate(arg); break;
                    case "address":   // address <text>: type in the address bar (the suggestions follow)
                        if (AddressBox.Visibility != System.Windows.Visibility.Visible) BeginEditAddress();
                        AddressBox.Text = arg;
                        AddressBox.CaretIndex = arg.Length;
                        break;
                    case "addresskey":   // addresskey Down | Up | Tab | Escape | Enter: as pressed in the address bar
                    {
                        var key = (System.Windows.Input.Key)Enum.Parse(typeof(System.Windows.Input.Key), arg, true);
                        if (!_suggest.Key(key))
                        {
                            if (key == System.Windows.Input.Key.Enter) { string text = AddressBox.Text; EndEditAddress(); GoTo(text); }
                            else if (key == System.Windows.Input.Key.Escape) EndEditAddress();
                        }
                        break;
                    }
                    case "expandnode":   // expandnode <path>: open that folder in the sidebar (it must be showing - revealed or a top one)
                    {
                        Func<System.Collections.Generic.IEnumerable<FolderNode>, FolderNode> find = null;
                        find = nodes => { foreach (FolderNode n in nodes) { if (string.Equals(n.Path.TrimEnd('\\'), arg.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return n; FolderNode c = find(n.Children); if (c != null) return c; } return null; };
                        FolderNode hit = find(DriveTree.Items.OfType<FolderNode>());
                        if (hit != null) hit.IsExpanded = true; else Log.Write("expandnode: not found " + arg);
                        break;
                    }
                    case "view": SetView(arg); break;
                    case "sort":
                    {
                        string[] p = arg.Split(' ');
                        SetSort(p[0], p.Length > 1 && p[1] == "desc");
                        break;
                    }
                    case "group": SetGroupBy(arg); break;
                    case "open": Open(Files.SelectedItems.Cast<FileItem>().ToList()); break;
                    case "copy": ToClipboard(false); break;
                    case "extract": ExtractAll(); break;
                    case "compress": CompressSelection(); break;
                    case "up": GoUp(); break;
                    case "newtab": OpenTab(arg, true); break;
                    case "copyto": Transfer(SelectedPaths(), arg, false, false); break;   // copyto <folder>: the selection, as a paste would
                    case "moveto": Transfer(SelectedPaths(), arg, true, false); break;
                    case "pauseop": foreach (Operation o in Operations.All.Where(o => o.Active)) Operations.Pause(o, true); break;
                    case "resumeop": foreach (Operation o in Operations.All.Where(o => o.Active)) Operations.Pause(o, false); break;
                    case "cancelop": foreach (Operation o in Operations.All.Where(o => o.Active)) Operations.Cancel(o); break;
                    case "opsdump":
                    {
                        var sb = new StringBuilder();
                        foreach (Operation o in Operations.All)
                            sb.AppendLine(o.Kind + " " + o.State + " to=" + o.Target + " items=" + o.Items + "/" + o.ItemsTotal + " bytes=" + o.Bytes + "/" + o.BytesTotal +
                                          " pts=" + o.Points + "/" + o.PointsTotal + " speed=" + Math.Round(o.BytesPerSecond) + " cur=" + o.CurrentName + " fails=" +
                                          string.Join(";", o.Failures.Select(f => f.Name + ":" + f.Reason)) + " gate=" + o.Gate.IsSet);
                        sb.AppendLine("panel=" + OpsPanel.Visibility + " chip=" + OpsPanel.Chip.Visibility + " popout=" + OpsWindow.Shown);
                        File.WriteAllText(arg, sb.ToString());
                        break;
                    }
                    case "options":   // options [page name] - opens Options on that page
                        ShowOptions(false);
                        if (arg.Length > 0 && OptionsWindow.Current != null) OptionsWindow.Current.ShowPageNamed(arg);
                        break;
                    case "crash": Environment.FailFast("vXplorer test crash (test hooks)"); break;   // the crash safety net
                    case "columns":
                        _settings.Columns = arg.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                        ApplyColumns();
                        break;
                    case "dump":
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("folder: " + _current);
                        sb.AppendLine("view: " + _settings.ViewMode + "  sort: " + _settings.SortKey + (_settings.SortDescending ? " desc" : " asc"));
                        sb.AppendLine("items: " + string.Join(" | ", _items.Select(i => i.Name)));
                        sb.AppendLine("icons: " + _items.Count(i => i.Icon != null) + " of " + _items.Count); sb.AppendLine("thumbs: " + _items.Count(i => i.InView && i.Thumb != null) + " of " + _items.Count(i => i.InView) + " in view");
                        sb.AppendLine("groups: " + _settings.GroupBy + " - " + string.Join(" | ", _items.Select(i => i.Group).Distinct()));
                        sb.AppendLine("props: " + string.Join(" | ", _items.Where(i => i.Dimensions.Length > 0 || i.Length > TimeSpan.Zero).Select(i => i.Name + " " + i.Dimensions + " " + i.LengthText)));
                        sb.AppendLine("columns: " + string.Join(", ", ((System.Windows.Controls.GridView)_detailsView).Columns.Select(c => c.Header)));
                        sb.AppendLine("selected: " + string.Join(" | ", Files.SelectedItems.Cast<FileItem>().Select(i => i.Name)));
                        sb.AppendLine("status: " + StatusText.Text);
                        sb.AppendLine("address: " + (AddressBox.Visibility == System.Windows.Visibility.Visible ? AddressBox.Text : "(crumbs)") + "  suggest: " + _suggest.Describe());
                        sb.AppendLine("tabs: " + string.Join(" | ", _tabs.Select(t => t.Title)) + "  (showing " + (_tab != null ? _tab.Title : "-") + ")");
                        sb.AppendLine("windows: " + System.Windows.Application.Current.Windows.OfType<MainWindow>().Count());
                        File.WriteAllText(arg, sb.ToString());
                        break;
                    }
                }
            }
            catch (Exception ex) { Log.Write("Test command '" + command + "': " + ex); }
        }
    }
}
