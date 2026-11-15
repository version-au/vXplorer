using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using vXplorer.Core;
using WpfToolkit.Controls;

namespace vXplorer.Ui
{
    /// <summary>
    /// The list's views (Details; Medium, Large and Extra large icons with thumbnails) and the preview pane (images, text, Windows'
    /// thumbnail for anything else, and the file's details).
    /// </summary>
    public partial class MainWindow
    {
        static readonly string[] ViewModes = { "ExtraLarge", "Large", "Medium", "Details" };
        static readonly string[] ViewNames = { "Extra large icons", "Large icons", "Medium icons", "Details" };
        static readonly string[] ViewKeys = { "Ctrl+Shift+1", "Ctrl+Shift+2", "Ctrl+Shift+3", "Ctrl+Shift+6" };

        ViewBase _detailsView;
        ItemsPanelTemplate _detailsPanel;

        void InitViews()
        {
            _detailsView = Files.View;
            _detailsPanel = Files.ItemsPanel;
            InitColumns();
            ApplyView();
            SetPreviewPane(_settings.PreviewPane);
            _previewTimer.Tick += (s, e) => { _previewTimer.Stop(); UpdatePreview(); };
        }

        /// <summary>Icon size (DIPs) of an icon view; 0 for Details.</summary>
        static double TileIconSize(string mode)
        {
            return mode == "Medium" ? 48 : mode == "Large" ? 96 : mode == "ExtraLarge" ? 192 : 0;
        }

        int TilePixels()
        {
            return (int)Math.Round(TileIconSize(_settings.ViewMode) * VisualTreeHelper.GetDpi(this).DpiScaleX);
        }

        void SetView(string mode)
        {
            if (_settings.ViewMode == mode) return;
            _settings.ViewMode = mode;
            ApplyView();
            RememberFolderView();
        }

        void ApplyView()
        {
            if (_renaming != null) EndRename(true);
            List<FileItem> keep = Files.SelectedItems.Cast<FileItem>().ToList();
            string mode = _settings.ViewMode;
            double icon = TileIconSize(mode);
            if (icon == 0)
            {
                Files.ItemContainerStyle = null;
                Files.ItemsPanel = _detailsPanel;
                Files.View = _detailsView;
                VirtualizingPanel.SetVirtualizationMode(Files, VirtualizationMode.Recycling);
                ScrollViewer.SetHorizontalScrollBarVisibility(Files, ScrollBarVisibility.Auto);
            }
            else
            {
                double w = Math.Max(icon + 28, 100), h = icon + 56;
                Resources["vx.TileIcon"] = icon;
                FrameworkElementFactory panel;
                if (Grouped)
                {
                    // grouped: each group's tiles in a plain wrap panel (the virtualising one doesn't sit inside groups)
                    panel = new FrameworkElementFactory(typeof(WrapPanel));
                    panel.SetValue(WrapPanel.ItemWidthProperty, w);
                    panel.SetValue(WrapPanel.ItemHeightProperty, h);
                }
                else
                {
                    panel = new FrameworkElementFactory(typeof(VirtualizingWrapPanel));
                    panel.SetValue(VirtualizingWrapPanel.ItemSizeProperty, new Size(w, h));
                    panel.SetValue(VirtualizingWrapPanel.SpacingModeProperty, SpacingMode.None);
                    panel.SetValue(VirtualizingWrapPanel.StretchItemsProperty, false);
                }
                Files.View = null;
                Files.ItemContainerStyle = (Style)FindResource("vx.TileItem");
                Files.ItemsPanel = new ItemsPanelTemplate(panel);
                // tiles must be unloaded when they scroll away (their thumbnails are let go), so no container recycling here
                VirtualizingPanel.SetVirtualizationMode(Files, VirtualizationMode.Standard);
                ScrollViewer.SetHorizontalScrollBarVisibility(Files, ScrollBarVisibility.Disabled);
            }
            foreach (FileItem f in keep) Files.SelectedItems.Add(f);
            if (Files.SelectedItem != null) Files.ScrollIntoView(Files.SelectedItem);
        }

        /// <summary>Ctrl+wheel: bigger / smaller, through Details, Medium, Large and Extra large.</summary>
        void Files_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            e.Handled = true;
            int i = Array.IndexOf(ViewModes, _settings.ViewMode);
            i += e.Delta > 0 ? -1 : 1;
            SetView(ViewModes[Math.Max(0, Math.Min(ViewModes.Length - 1, i))]);
        }

        bool HandleViewKeys(Key key, ModifierKeys mods)
        {
            if (mods == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                if (key == Key.D1) { SetView("ExtraLarge"); return true; }
                if (key == Key.D2) { SetView("Large"); return true; }
                if (key == Key.D3) { SetView("Medium"); return true; }
                if (key == Key.D6) { SetView("Details"); return true; }
            }
            if (mods == ModifierKeys.Alt && key == Key.P) { SetPreviewPane(!_settings.PreviewPane); return true; }
            return false;
        }

        /// <summary>The View items, for the toolbar's View button and the folder's right-click menu.</summary>
        void AddViewItems(ItemCollection items)
        {
            for (int i = 0; i < ViewModes.Length; i++)
            {
                string m = ViewModes[i];
                items.Add(Check(ViewNames[i], _settings.ViewMode == m, () => SetView(m), ViewKeys[i]));
            }
            items.Add(new Separator());
            items.Add(Check("Preview pane", _settings.PreviewPane, () => SetPreviewPane(!_settings.PreviewPane), "Alt+P"));
            if (_settings.ViewPerFolder && !InPlace && !Searching)
            {
                items.Add(new Separator());
                items.Add(Check("Use this view for all sub-folders", ViewForSubfolders(), ToggleViewForSubfolders));
                items.Add(Item("Use this view for all folders", null, null, UseViewForAllFolders));
            }
        }

        void ViewButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = ViewBtn, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            AddViewItems(menu.Items);
            menu.IsOpen = true;
        }

        void PreviewButton_Click(object sender, RoutedEventArgs e) { SetPreviewPane(!_settings.PreviewPane); }

        // ---- thumbnails for the tiles: asked for when a tile comes on screen, let go when it leaves ----

        void Tile_Loaded(object sender, RoutedEventArgs e)
        {
            var f = (sender as FrameworkElement)?.DataContext as FileItem;
            if (f == null) return;
            f.InView = true;
            int px = TilePixels();
            if (px <= 0 || f.ThumbPending || (f.ThumbPx == px && f.Thumb != null)) return;
            f.ThumbPending = true;
            int v = _loadVersion;
            _icons.RequestThumb(f.Path, px, () => f.InView && v == _loadVersion, img =>
            {
                f.ThumbPending = false;
                f.ThumbPx = px;
                f.Thumb = img;
            });
        }

        void Tile_Unloaded(object sender, RoutedEventArgs e)
        {
            var f = (sender as FrameworkElement)?.DataContext as FileItem;
            if (f == null) return;
            f.InView = false;
            f.ThumbPending = false;
            if (f.ThumbPx > 64) { f.Thumb = null; f.ThumbPx = 0; }   // big thumbnails take memory: load again if it comes back
        }

        // =================================================================
        // Preview pane
        // =================================================================

        readonly DispatcherTimer _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        int _previewVersion;

        static readonly HashSet<string> ImageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".webp", ".heic", ".heif", ".avif", ".jxr", ".wdp", ".dds"
        };

        static readonly HashSet<string> TextExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".markdown", ".log", ".ini", ".cfg", ".conf", ".config", ".json", ".jsonc", ".xml", ".xaml", ".csproj", ".props",
            ".targets", ".sln", ".yml", ".yaml", ".toml", ".csv", ".tsv", ".cs", ".vb", ".fs", ".c", ".h", ".cpp", ".hpp", ".java", ".kt", ".go",
            ".rs", ".py", ".rb", ".php", ".js", ".mjs", ".ts", ".tsx", ".jsx", ".css", ".scss", ".less", ".html", ".htm", ".svg", ".sql",
            ".ps1", ".psm1", ".psd1", ".bat", ".cmd", ".sh", ".bash", ".zsh", ".reg", ".gitignore", ".gitattributes", ".editorconfig",
            ".env", ".dockerfile", ".lua", ".swift", ".dart", ".r", ".tex", ".srt", ".nfo", ".iss", ".manifest"
        };

        void SetPreviewPane(bool on)
        {
            _settings.PreviewPane = on;
            PreviewCard.Visibility = on ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            PreviewSplitter.Visibility = PreviewCard.Visibility;
            if (on)
            {
                PreviewCol.MinWidth = 200;
                PreviewCol.Width = new GridLength(_settings.PreviewWidth);
                UpdatePreview();
            }
            else
            {
                HideHandler();
                if (PreviewCol.ActualWidth >= 200) _settings.PreviewWidth = PreviewCol.ActualWidth;
                PreviewCol.MinWidth = 0;
                PreviewCol.Width = new GridLength(0);
            }
        }

        /// <summary>The selection changed: show it shortly (not on every step of a Shift+arrow run).</summary>
        void SchedulePreview()
        {
            if (!_settings.PreviewPane) return;
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        async void UpdatePreview()
        {
            if (!_settings.PreviewPane || _current == null) return;
            int v = ++_previewVersion;
            HideHandler();
            PreviewImage.Source = null;
            PreviewImage.Visibility = System.Windows.Visibility.Collapsed;
            PreviewText.Visibility = System.Windows.Visibility.Collapsed;
            PreviewText.Text = "";
            PreviewNote.Visibility = System.Windows.Visibility.Collapsed;
            var props = new List<KeyValuePair<string, string>>();
            if (InThisPc) { PreviewThisPc(v, props); return; }

            List<FileItem> sel = Files.SelectedItems.Cast<FileItem>().ToList();
            if (sel.Count > 1)
            {
                PreviewName.Text = sel.Count.ToString("N0") + " items selected";
                PreviewKind.Text = "";
                var files = sel.Where(f => !f.IsFolder).ToList();
                int folders = sel.Count - files.Count;
                if (files.Count > 0) props.Add(Prop("Files", files.Count.ToString("N0") + "  (" + Format.Size(files.Sum(f => f.Size)) + ")"));
                if (folders > 0) props.Add(Prop("Folders", folders.ToString("N0")));
                PreviewProps.ItemsSource = props;
                ShowNote("Select a single item to preview it.");
                return;
            }

            string path = sel.Count == 1 ? sel[0].Path : _current;
            bool folder = sel.Count == 0 || sel[0].IsFolder;
            PreviewName.Text = sel.Count == 1 ? sel[0].Name : NameOf(_current);
            PreviewKind.Text = sel.Count == 1 ? sel[0].TypeName : "This folder";

            // the details
            try
            {
                if (folder)
                {
                    var di = new DirectoryInfo(path);
                    props.Add(Prop("Modified", Format.Stamp(di.LastWriteTime)));
                    props.Add(Prop("Created", Format.Stamp(di.CreationTime)));
                }
                else
                {
                    var fi = new FileInfo(path);
                    props.Add(Prop("Size", Format.Size(fi.Length) + (fi.Length >= 1024 ? "  (" + fi.Length.ToString("N0") + " bytes)" : "")));
                    props.Add(Prop("Modified", Format.Stamp(fi.LastWriteTime)));
                    props.Add(Prop("Created", Format.Stamp(fi.CreationTime)));
                    if (fi.IsReadOnly) props.Add(Prop("Attributes", "Read-only"));
                }
            }
            catch { }
            PreviewProps.ItemsSource = props;
            if (folder) CountFolder(path, v, props);

            string ext = Path.GetExtension(path);
            int px = (int)Math.Round(Math.Max(256, Math.Min(1024, PreviewStage.ActualWidth)) * VisualTreeHelper.GetDpi(this).DpiScaleX);

            // Options > Preview pane: Windows' previewers for pictures and text too, when it has one for the type
            if (!folder && _settings.PreviewWindowsForAll && _ops.Busy == 0 && (ImageExts.Contains(ext) || TextExts.Contains(ext)))
            {
                Guid? any = PreviewHost.HandlerFor(ext);
                if (any != null)
                {
                    _icons.RequestThumb(path, px, () => v == _previewVersion, s => { if (v == _previewVersion) ShowImage(s); });
                    LoadHandler(path, any.Value, v);
                    return;
                }
            }

            // an image: the picture itself (scaled down if it's big), with its size
            if (!folder && ImageExts.Contains(ext))
            {
                Tuple<BitmapSource, int, int> img = await Task.Run(() => LoadPicture(path, px));
                if (v != _previewVersion) return;
                if (img != null)
                {
                    ShowImage(img.Item1);
                    props.Insert(1, Prop("Dimensions", img.Item2 + " × " + img.Item3));
                    PreviewProps.ItemsSource = null;
                    PreviewProps.ItemsSource = props;
                    return;
                }
            }

            // text and code: the start of the file (ours rather than Windows' handler, so it's in the theme)
            bool textType = !folder && TextExts.Contains(ext);
            // a type Windows has a preview handler for (PDF, Office, fonts, video …) - not while a file operation is running, as
            // the handler can keep the file open
            Guid? handler = folder || textType || _ops.Busy > 0 ? null : PreviewHost.HandlerFor(ext);
            if (textType || (!folder && handler == null && await Task.Run(() => LooksLikeText(path))))
            {
                if (v != _previewVersion) return;
                string text = await Task.Run(() => ReadText(path));
                if (v != _previewVersion) return;
                if (text != null)
                {
                    PreviewText.Text = text;
                    PreviewText.ScrollToHome();
                    PreviewText.Visibility = System.Windows.Visibility.Visible;
                    return;
                }
            }

            // anything else: Windows' thumbnail (or the big icon) - and over it, once it has loaded, the preview handler's view
            _icons.RequestThumb(path, px, () => v == _previewVersion, s => { if (v == _previewVersion) ShowImage(s); });
            if (handler != null) LoadHandler(path, handler.Value, v);
        }

        void LoadHandler(string path, Guid clsid, int v)
        {
            Color bg = PreviewBackground(), fg = _theme != null ? _theme.Fg : Colors.White;
            PreviewWin.SetBackground(bg);
            double fontPx = 12.5 * VisualTreeHelper.GetDpi(this).DpiScaleY;
            PreviewWin.Load(path, clsid, bg, fg, fontPx, ok =>
            {
                if (ok && v == _previewVersion && _settings.PreviewPane) PreviewWin.Visibility = System.Windows.Visibility.Visible;
            });
        }

        /// <summary>The pane's colour, solid (a window can't be see-through): the panel tint over the background.</summary>
        Color PreviewBackground()
        {
            if (_theme == null) return Color.FromRgb(0x1E, 0x1E, 0x2E);
            Color b = _theme.Bg, f = _theme.Fg;
            const double a = 0x0A / 255.0;
            return Color.FromRgb((byte)(b.R + (f.R - b.R) * a), (byte)(b.G + (f.G - b.G) * a), (byte)(b.B + (f.B - b.B) * a));
        }

        void HideHandler()
        {
            PreviewWin.Visibility = System.Windows.Visibility.Hidden;
            PreviewWin.Unload();
        }

        /// <summary>
        /// Before something may change or move the previewed file (a delete, rename, move, Windows' menu, a drag): close it in the
        /// preview handler and wait for that, so Windows doesn't say the file is in use. The thumbnail stays.
        /// </summary>
        void ReleasePreview()
        {
            if (!PreviewWin.Active) return;
            PreviewWin.Visibility = System.Windows.Visibility.Hidden;
            PreviewWin.Release();
        }

        void ShowImage(ImageSource s)
        {
            PreviewImage.Source = s;
            PreviewImage.Visibility = System.Windows.Visibility.Visible;
        }

        void ShowNote(string text)
        {
            PreviewNote.Text = text;
            PreviewNote.Visibility = System.Windows.Visibility.Visible;
        }

        static KeyValuePair<string, string> Prop(string k, string v) { return new KeyValuePair<string, string>(k, v); }

        async void CountFolder(string path, int v, List<KeyValuePair<string, string>> props)
        {
            int count = await Task.Run(() =>
            {
                try
                {
                    var opts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = _settings.ShowHidden ? 0 : FileAttributes.Hidden | FileAttributes.System };
                    return Directory.EnumerateFileSystemEntries(path, "*", opts).Take(100000).Count();
                }
                catch { return -1; }
            });
            if (v != _previewVersion || count < 0) return;
            props.Insert(0, Prop("Contains", count == 1 ? "1 item" : count.ToString("N0") + " items"));
            PreviewProps.ItemsSource = null;
            PreviewProps.ItemsSource = props;
        }

        /// <summary>The picture, decoded at most px wide / high, and its real size; null if Windows can't read it.</summary>
        static Tuple<BitmapSource, int, int> LoadPicture(string path, int px)
        {
            try
            {
                int w, h;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    BitmapFrame frame = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                    w = frame.PixelWidth;
                    h = frame.PixelHeight;
                }
                var bmp = new BitmapImage();
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    if (w >= h && w > px) bmp.DecodePixelWidth = px;
                    else if (h > w && h > px) bmp.DecodePixelHeight = px;
                    bmp.StreamSource = fs;
                    bmp.EndInit();
                }
                bmp.Freeze();
                return Tuple.Create((BitmapSource)bmp, w, h);
            }
            catch { return null; }
        }

        /// <summary>A small file of an unknown type with no zero bytes in its first 4 KB is probably text.</summary>
        static bool LooksLikeText(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (fi.Length == 0 || fi.Length > 4 * 1024 * 1024) return false;
                var buf = new byte[4096];
                int n;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    n = fs.Read(buf, 0, buf.Length);
                if (n >= 2 && ((buf[0] == 0xFF && buf[1] == 0xFE) || (buf[0] == 0xFE && buf[1] == 0xFF))) return true;   // UTF-16
                for (int i = 0; i < n; i++) if (buf[i] == 0) return false;
                return true;
            }
            catch { return false; }
        }

        /// <summary>How much of a text file the preview shows (Options > Preview pane: 64 KB, 128 KB or 1 MB).</summary>
        static int PreviewTextLimit = 128 * 1024;

        static string ReadText(string path)
        {
            int limit = PreviewTextLimit;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                {
                    var buf = new char[limit];
                    int n = sr.ReadBlock(buf, 0, buf.Length);
                    string s = new string(buf, 0, n);
                    if (!sr.EndOfStream) s += "\n\n… (the first " + (limit >= 1024 * 1024 ? "1 MB" : limit / 1024 + " KB") + ")";
                    return s;
                }
            }
            catch { return null; }
        }
    }
}
