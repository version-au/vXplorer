using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>
    /// Keeping the session file up to date (Core/Session: every window's tabs, saved a moment after they change; deleted on a normal exit),
    /// and after an unexpected end, offering to reopen what was open.
    /// </summary>
    public partial class MainWindow
    {
        static DispatcherTimer _sessionTimer;

        /// <summary>Something about the windows / tabs changed: save the session in a couple of seconds (once, however many changes).</summary>
        static void TouchSession()
        {
            if (_sessionTimer == null)
            {
                _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _sessionTimer.Tick += (s, e) =>
                {
                    _sessionTimer.Stop();
                    var windows = Application.Current.Windows.OfType<MainWindow>().Where(w => w.IsLoaded).Select(w => w.SessionWindow()).ToList();
                    if (windows.Count > 0) Session.Save(windows);
                };
            }
            _sessionTimer.Stop();
            _sessionTimer.Start();
        }

        SessionWindow SessionWindow()
        {
            Rect r = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            return new SessionWindow
            {
                Tabs = _tabs.Select(t => t.Path).ToList(), Active = Math.Max(0, _tabs.IndexOf(_tab)),
                Left = r.Left, Top = r.Top, Width = r.Width, Height = r.Height
            };
        }

        /// <summary>The last run ended unexpectedly: say so, and offer to put its windows and tabs back.</summary>
        public void OfferSessionBack(SessionState last)
        {
            List<SessionWindow> wins = last.Windows.Where(w => w.Tabs.Count > 0).ToList();
            int tabs = wins.Sum(w => w.Tabs.Count);
            if (tabs == 0) { Status("vXplorer closed unexpectedly last time - what happened is in vxplorer.log."); return; }
            string what = tabs == 1 ? "the folder" : wins.Count == 1 ? "the " + tabs + " tabs" : "the " + tabs + " tabs in " + wins.Count + " windows";
            if (_settings.StartWith == "Restore")
            {
                // vXplorer opens the last tabs anyway: no question - just put back what was open when it stopped (newer than the last
                // normal close's tabs, and every window, not only the first)
                bool same = wins.Count == 1 && wins[0].Tabs.Count == _tabs.Count &&
                            wins[0].Tabs.Zip(_tabs, (p, t) => Same(NormalPlace(p) ?? p, t.Path)).All(x => x);
                if (!same) ReopenSession(wins);
                Status("vXplorer closed unexpectedly last time - " + (same ? "your tabs are as they were." : "your tabs are back.") +
                       " What happened is in vxplorer.log.");
                return;
            }
            bool back = Ask.YesNo(this, "vXplorer closed unexpectedly",
                "vXplorer closed unexpectedly last time. Open " + what + " you had open?\n\nWhat happened is in vxplorer.log " +
                "(Options > About > Open settings folder) - send it along if it keeps happening.", "Reopen", "Not now", false);
            if (back) ReopenSession(wins);
        }

        /// <summary>The session's windows and tabs: this window takes the first one's tabs, the others open as new windows where they were.</summary>
        void ReopenSession(List<SessionWindow> wins)
        {
            for (int i = 0; i < wins.Count; i++)
            {
                SessionWindow sw = wins[i];
                MainWindow w = i == 0 ? this : OpenWindowAt(sw.Tabs[0]);
                if (w == null) continue;
                if (i > 0 && sw.Width > 200 && sw.Height > 150) { w.Left = sw.Left; w.Top = sw.Top; w.Width = sw.Width; w.Height = sw.Height; }
                w.ReplaceTabs(sw.Tabs, sw.Active);
            }
        }

        /// <summary>This window's tabs become these (the ones that are still there).</summary>
        void ReplaceTabs(List<string> paths, int active)
        {
            var keep = paths.Select(NormalPlace).Where(p => p != null && PlaceExists(p)).ToList();
            if (keep.Count == 0) return;
            if (_renaming != null) EndRename(true);
            _tab = null;
            _tabs.Clear();
            foreach (string p in keep) _tabs.Add(NewTab(p));
            SwitchTo(_tabs[Math.Max(0, Math.Min(active, _tabs.Count - 1))]);
        }
    }
}
