using System;
using System.Reflection;
using System.Windows;
using vXplorer.Core;
using vXplorer.Ui;

namespace vXplorer
{
    static class Program
    {
        static System.Threading.Mutex _running;

        [STAThread]
        static int Main(string[] args)
        {
            // the shared library: which app this is (settings folder, log, test variables, releases), and its icon drawing
            vSuite.App.Init("vXplorer", "VXPLORER", "https://vsuite.vrsion.com");
            AppIcon.Drawer = Brand.Draw;
            // an update closes every window: not while a copy / move / delete is running (closing would cut it off part way)
            Updater.CanSwap = () => vXplorer.Ui.Operations.AnyActive ? "A copy is still running - install the update once it has finished." : null;
            if (args.Length == 2 && args[0] == "--make-icon")   // used once to make vxplorer.ico
            {
                AppIcon.Save(args[1]);
                return 0;
            }
            if (args.Length == 2 && args[0] == "--selftest-registry")   // tests "Open folders with vXplorer" on throwaway keys only
            {
                System.IO.File.WriteAllText(args[1], DefaultFileManagerSelfTest());
                return 0;
            }
            if (args.Length == 3 && args[0] == "--update-watchdog")   // the old exe after an update: start the new one, go back if it fails
                return Updater.RunWatchdog(args);
            if (args.Length == 2 && args[0] == "--default-file-manager")   // the installer: "Open folders with vXplorer" on / off
            {
                try
                {
                    if (args[1] == "on") DefaultFileManager.TurnOn(Environment.ProcessPath);
                    else DefaultFileManager.TurnOff();
                    return 0;
                }
                catch (Exception ex) { Log.Write("Default file manager (" + args[1] + "): " + ex.Message); return 1; }
            }
            // for the installer: "vXplorer is running" (AppMutex) - every vXplorer holds it, whatever its settings folder
            _running = new System.Threading.Mutex(false, "vXplorer.Running");
            // already running (same settings folder): it opens the folder in a new window, and this one stops here
            try { if (Instance.HandOff(args.Length > 0 ? args[0] : "")) return 0; }
            catch (Exception ex) { Log.Write("Hand-off: " + ex.Message); }
            Log.Write("vXplorer " + Assembly.GetExecutingAssembly().GetName().Version + " starting");
            // tabs dragged out make more windows: run until the last one closes
            var app = new Application { ShutdownMode = ShutdownMode.OnLastWindowClose };
            app.DispatcherUnhandledException += (s, e) =>
            {
                Log.Write("Unhandled: " + e.Exception);
                Log.Flush();
                e.Handled = true;
            };
            // errors on other threads: into the log too (one that isn't caught still ends vXplorer, but now it says why) - written
            // straight away, as the process may be about to end
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { Log.Write("Unhandled (" + (e.IsTerminating ? "ending" : "carrying on") + "): " + e.ExceptionObject); Log.Flush(); };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => { Log.Write("Unobserved task error: " + e.Exception); e.SetObserved(); };

            // the last run didn't end normally (its session file is still there): note what Windows recorded, offer its tabs back
            SessionState crashed = Session.TakeUnfinished();
            if (crashed != null)
            {
                Log.Write("The last run (started " + crashed.Started.ToString("d") + " " + crashed.Started.ToString("h:mm tt") + ") didn't close normally.");
                DateTime since = crashed.Started;
                System.Threading.Tasks.Task.Run(() => Session.LogCrashDetails(since));
            }
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/vSuite.Core;component/Ui/Controls.xaml", UriKind.Absolute)
            });

            var settings = AppSettings.Load();
            System.Threading.Tasks.Task.Run(() => ZipFolders.CleanTemp());   // copies taken out of zips days ago
            var theme = new VShellThemeSource();
            string start = args.Length > 0 ? args[0] : null;
            if (UpdateCenter.HandleCommand(start)) start = null;   // vXplorer.exe --update (vShell's Settings): open as usual, update now
            var window = new MainWindow(settings, theme, start);
            // update checks (and telling a waiting update watchdog we started fine) once the first window is up
            window.ContentRendered += (s, e) => UpdateCenter.Start(settings);
            if (crashed != null)
                window.ContentRendered += (s, e) => window.Dispatcher.BeginInvoke(new Action(() => window.OfferSessionBack(crashed)),
                                                                                  System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            // copies / moves that stopped part way last time (their journals are still there): offer to finish them
            window.ContentRendered += (s, e) => window.Dispatcher.BeginInvoke(new Action(window.OfferUnfinishedCopies),
                                                                              System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            int code = app.Run(window);
            Session.End();   // a normal exit: no "closed unexpectedly" next time
            theme.Dispose();
            return code;
        }

        /// <summary>
        /// DefaultFileManager on and off under HKCU\Software\vXplorerTest (never the real Software\Classes): a made-up earlier default for
        /// Directory must come back after off, and nothing of ours may be left. Returns a report.
        /// </summary>
        static string DefaultFileManagerSelfTest()
        {
            var r = new System.Text.StringBuilder();
            const string test = @"Software\vXplorerTest";
            DefaultFileManager.ClassesRoot = test + @"\Classes";
            DefaultFileManager.StashRoot = test + @"\Stash";
            var hk = Microsoft.Win32.Registry.CurrentUser;
            try
            {
                hk.DeleteSubKeyTree(test, false);
                using (var k = hk.CreateSubKey(test + @"\Classes\Directory\shell")) k.SetValue("", "earlierverb");
                r.AppendLine("on before: " + DefaultFileManager.IsOn());
                DefaultFileManager.TurnOn(@"C:\Test\vXplorer.exe");
                r.AppendLine("on after TurnOn: " + DefaultFileManager.IsOn());
                using (var k = hk.OpenSubKey(test + @"\Classes\Directory\shell\vxplorer\command")) r.AppendLine("directory command: " + k?.GetValue(""));
                using (var k = hk.OpenSubKey(test + @"\Classes\Drive\shell")) r.AppendLine("drive default: " + k?.GetValue(""));
                using (var k = hk.OpenSubKey(test + @"\Classes\CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}\shell\opennewwindow\command"))
                    r.AppendLine("win+e command: " + k?.GetValue("") + " | DelegateExecute=[" + k?.GetValue("DelegateExecute") + "]");
                DefaultFileManager.TurnOn(@"C:\Test\vXplorer.exe");   // twice must not lose the earlier default
                DefaultFileManager.TurnOff();
                r.AppendLine("on after TurnOff: " + DefaultFileManager.IsOn());
                using (var k = hk.OpenSubKey(test + @"\Classes\Directory\shell")) r.AppendLine("directory default restored: " + k?.GetValue("") + " | subkeys: " + string.Join(",", k?.GetSubKeyNames() ?? new string[0]));
                using (var k = hk.OpenSubKey(test + @"\Classes\Drive\shell")) r.AppendLine("drive default after: [" + k?.GetValue("") + "] subkeys: " + string.Join(",", k?.GetSubKeyNames() ?? new string[0]));
                using (var k = hk.OpenSubKey(test + @"\Classes\CLSID")) r.AppendLine("CLSID left: " + string.Join(",", k?.GetSubKeyNames() ?? new string[0]));
                using (var k = hk.OpenSubKey(test + @"\Stash")) r.AppendLine("stash left: " + (k == null ? "none" : "values " + k.ValueCount));
            }
            catch (Exception ex) { r.AppendLine("FAILED: " + ex); }
            finally { try { hk.DeleteSubKeyTree(test, false); } catch { } }
            r.AppendLine("test key removed: " + (hk.OpenSubKey(test) == null));
            return r.ToString();
        }
    }
}
