using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>
    /// The file operations panel popped out (Options > Files, or the panel's pop-out button): one window for the whole app that can be
    /// moved anywhere, independently of vXplorer's windows. It shows itself when something runs (without taking the focus), and its
    /// close / fold button hides it into the status bar's chip, as the docked panel folds. Remembers where it was put.
    /// </summary>
    public sealed class OpsWindow : Window
    {
        static OpsWindow _one;
        static MainWindow _host;
        readonly OpsPanel _panel;
        bool _closingForGood;

        /// <summary>The vXplorer window the floating panel works with (Open folder, Try again, the settings): the last one used.</summary>
        public static MainWindow Host
        {
            get
            {
                if (_host != null && _host.IsLoaded) return _host;
                return Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => w.IsLoaded);
            }
            set { _host = value; }
        }

        /// <summary>The popped-out window is on screen (tests: opsdump).</summary>
        public static bool Shown { get { return _one != null && _one.IsVisible; } }

        /// <summary>Shows or hides the window: popped out, not folded away, and something to show.</summary>
        public static void Sync()
        {
            MainWindow host = Host;
            bool want = host != null && host.Settings.OpsPopOut && !host.Settings.OpsFolded && !host.Settings.WindowsCopyDialog
                        && Operations.All.Any(o => !o.Hidden);
            if (want)
            {
                if (_one == null) _one = new OpsWindow(host);
                if (!_one.IsVisible) _one.Show();
            }
            else if (_one != null && _one.IsVisible) _one.Hide();
        }

        /// <summary>A vXplorer window's theme changed.</summary>
        public static void ThemeChanged(MainWindow from)
        {
            if (_one != null && _one.IsLoaded && from.CurrentTheme != null) _one.Look(from);
        }

        /// <summary>The last vXplorer window is closing: this one goes too (a hidden window would keep the app running).</summary>
        public static void CloseForGood()
        {
            if (_one == null) return;
            _one._closingForGood = true;
            _one._panel.Detach();
            _one.SaveBounds();
            _one.Close();
            _one = null;
        }

        OpsWindow(MainWindow host)
        {
            Title = "File operations";
            ShowInTaskbar = true;
            ShowActivated = false;   // a copy starting doesn't take the keyboard away from the list
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12.5;
            UseLayoutRounding = true;
            Background = Brushes.Transparent;
            SetResourceReference(ForegroundProperty, "vx.Fg");
            MinWidth = 380;
            Width = 560;
            SizeToContent = SizeToContent.Height;   // as tall as its rows; the width is yours
            ResizeMode = ResizeMode.CanResize;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 44,   // the panel's title row: drag it from there
                ResizeBorderThickness = new Thickness(6),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });

            AppSettings s = host.Settings;
            if (s.OpsWidth >= MinWidth)
            {
                var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                      SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                if (screen.IntersectsWith(new Rect(s.OpsLeft, s.OpsTop, s.OpsWidth, 100)))
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = s.OpsLeft; Top = s.OpsTop; Width = s.OpsWidth;
                }
                else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            else
            {
                // first time: the bottom right of the vXplorer window it came from
                WindowStartupLocation = WindowStartupLocation.Manual;
                Rect r = host.WindowState == WindowState.Normal ? new Rect(host.Left, host.Top, host.ActualWidth, host.ActualHeight) : host.RestoreBounds;
                Left = Math.Max(r.Left, r.Right - Width - 24);
                Top = Math.Max(r.Top, r.Bottom - 260);
            }

            var root = new Border { Padding = new Thickness(8) };
            root.SetResourceReference(Border.BackgroundProperty, "vx.Bg");
            _panel = new OpsPanel(true);
            root.Child = _panel;
            Content = root;

            SourceInitialized += (o, e) => Look(Host ?? host);
            SizeChanged += (o, e) =>
            {
                // dragging the bottom edge would end "as tall as its rows"
                if (SizeToContent != SizeToContent.Height) SizeToContent = SizeToContent.Height;
            };
            LocationChanged += (o, e) => SaveBoundsSoon();
            Closing += OnClosing;
        }

        void Look(MainWindow from)
        {
            if (from == null || from.CurrentTheme == null) return;
            WindowEffects.Look(this, from.CurrentTheme, from.Blurred);
            try { Icon = AppIcon.WindowIcon(from.CurrentTheme); }
            catch (Exception ex) { Log.Write("Icon: " + ex.Message); }
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            if (_closingForGood) return;
            // the close button / Alt+F4: fold it away into the status bar, as the panel's own fold button does
            e.Cancel = true;
            SaveBounds();
            _panel.SetFolded(true);
        }

        System.Windows.Threading.DispatcherTimer _saveTimer;

        void SaveBoundsSoon()
        {
            if (_saveTimer == null)
            {
                _saveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _saveTimer.Tick += (o, e) => { _saveTimer.Stop(); SaveBounds(); };
            }
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        void SaveBounds()
        {
            MainWindow host = Host;
            if (host == null || WindowState != WindowState.Normal || !IsLoaded) return;
            AppSettings s = host.Settings;
            s.OpsLeft = Left; s.OpsTop = Top; s.OpsWidth = ActualWidth;
            s.Save();
        }
    }
}
