using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>
    /// Search: the box in the toolbar looks through the open folder and every folder under it for names containing what's typed (each
    /// word, in any order; * and ? work as wildcards). Results show up as they're found, in the list with a Folder column, and work like
    /// any other items (open, rename, delete, drag, preview, Ctrl+Z). Esc, the x, Back or opening another folder ends the search.
    /// </summary>
    public partial class MainWindow
    {
        sealed class SearchRun
        {
            public int Version;
            public string Root;
            public readonly CancellationTokenSource Cancel = new CancellationTokenSource();
            public readonly ConcurrentQueue<FileItem> Found = new ConcurrentQueue<FileItem>();
            /// <summary>The folders have all been looked through (set by the search thread).</summary>
            public volatile bool Done;
            /// <summary>Everything found is in the list.</summary>
            public bool Finished;
            public HashSet<string> Select;
            public int Count;
            public bool Capped;
            public readonly Stopwatch Clock = Stopwatch.StartNew();
        }

        const int SearchLimit = 10000;

        /// <summary>What's being searched for, or null when the list shows the folder.</summary>
        string _searchQuery;
        SearchRun _search;
        readonly DispatcherTimer _searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        readonly DispatcherTimer _searchPump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        bool _settingSearchText;
        GridViewColumn _folderColumn;

        bool Searching { get { return _searchQuery != null; } }

        void InitSearch()
        {
            _searchDelay.Tick += (s, e) => { _searchDelay.Stop(); StartSearch(SearchBox.Text); };
            _searchPump.Tick += (s, e) => PumpResults();
        }

        // ---- the box ----

        void UpdateSearchHint()
        {
            SearchHint.Text = SearchBox.Text.Length > 0 || _current == null ? "" : InPlace ? "Open a folder to search it" : "Search " + Native.DisplayName(_current);
            SearchBox.IsEnabled = !InPlace;
            SearchClear.Visibility = SearchBox.Text.Length > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        void SearchCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            FocusSearch();
            e.Handled = true;
        }

        void FocusSearch()
        {
            if (InPlace) { Status("Open a drive or folder to search it."); return; }
            SearchBox.Focus();
            SearchBox.SelectAll();
        }

        void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSearchHint();
            if (_settingSearchText) return;
            _searchDelay.Stop();
            if (SearchBox.Text.Trim().Length == 0) { if (Searching) EndSearch(); return; }
            _searchDelay.Start();   // search once typing pauses
        }

        void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _searchDelay.Stop();
                StartSearch(SearchBox.Text);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                ClearSearch();
            }
            else if (e.Key == Key.Down && _items.Count > 0)
            {
                // down into the results
                e.Handled = true;
                if (Files.SelectedItem == null) Files.SelectedItem = _items[0];
                Files.ScrollIntoView(Files.SelectedItem);
                FocusSelected();
            }
        }

        void SearchClear_Click(object sender, RoutedEventArgs e) { ClearSearch(); }

        /// <summary>Empty the box and show the folder again.</summary>
        void ClearSearch()
        {
            _searchDelay.Stop();
            bool was = Searching;
            SetSearchText("");
            if (was) EndSearch();
            Files.Focus();
        }

        void SetSearchText(string text)
        {
            _settingSearchText = true;
            SearchBox.Text = text;
            _settingSearchText = false;
            UpdateSearchHint();
        }

        // ---- running a search ----

        void StartSearch(string text, HashSet<string> select = null)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) { if (Searching) EndSearch(); return; }
            if (_current == null || InPlace) return;
            if (_renaming != null) EndRename(true);
            if (_search != null) _search.Cancel.Cancel();

            var run = new SearchRun { Version = ++_loadVersion, Select = select, Root = _current.TrimEnd('\\') };
            _search = run;
            _searchQuery = text;
            ShowFolderColumn(true);
            _items = new ObservableCollection<FileItem>();
            Files.ItemsSource = _items;
            EmptyText.Visibility = System.Windows.Visibility.Collapsed;
            Title = "Search results in " + Native.DisplayName(_current) + " - vXplorer";

            Func<string, bool> match = Matcher(text);
            string root = _current;
            bool hidden = _settings.ShowHidden;
            Task.Run(() => SearchFolders(root, match, hidden, run)).ContinueWith(t => run.Done = true);
            _searchPump.Start();
            UpdateNavButtons();
            UpdateStatus();
            SchedulePreview();
        }

        /// <summary>The search again (F5, or after a file operation), keeping the selection.</summary>
        void RerunSearch()
        {
            HashSet<string> select = _selectNext ?? PathSet(Files.SelectedItems.Cast<FileItem>().Select(f => f.Path));
            _selectNext = null;
            StartSearch(_searchQuery, select);
        }

        /// <summary>Stop searching (another folder is opening): no results, an empty box.</summary>
        void StopSearch()
        {
            _searchDelay.Stop();
            if (_search != null) { _search.Cancel.Cancel(); _search = null; }
            _searchPump.Stop();
            if (SearchBox.Text.Length > 0) SetSearchText("");
            if (!Searching) return;
            _searchQuery = null;
            ShowFolderColumn(false);
            UpdateNavButtons();
        }

        /// <summary>Back from the results to the folder's contents.</summary>
        void EndSearch()
        {
            var keep = PathSet(Files.SelectedItems.Cast<FileItem>().Select(f => f.Path).Where(p => Same(Path.GetDirectoryName(p), _current)));
            StopSearch();
            if (_current == null) return;
            Title = Native.DisplayName(_current) + " - vXplorer";
            Load(keep, false);
        }

        /// <summary>After making something here (new folder, new file, shortcuts): the folder, with it selected.</summary>
        void ShowFolderAgain()
        {
            if (Searching) EndSearch(); else Refresh();
        }

        /// <summary>Every folder under root, breadth first (nearer results come first), skipping links so nothing is visited twice.</summary>
        static void SearchFolders(string root, Func<string, bool> match, bool hidden, SearchRun run)
        {
            var opts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                ReturnSpecialDirectories = false,
                AttributesToSkip = hidden ? 0 : FileAttributes.Hidden | FileAttributes.System
            };
            CancellationToken stop = run.Cancel.Token;
            var pending = new Queue<string>();
            pending.Enqueue(root);
            while (pending.Count > 0 && !stop.IsCancellationRequested)
            {
                string dir = pending.Dequeue();
                try
                {
                    foreach (FileSystemInfo fsi in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", opts))
                    {
                        if (stop.IsCancellationRequested) return;
                        if (fsi is DirectoryInfo && (fsi.Attributes & FileAttributes.ReparsePoint) == 0) pending.Enqueue(fsi.FullName);
                        if (match(fsi.Name)) run.Found.Enqueue(FolderReader.Make(fsi));
                    }
                }
                catch { }   // a folder that can't be read: skip it
                while (run.Found.Count > 5000 && !stop.IsCancellationRequested) Thread.Sleep(20);   // the list is catching up
            }
        }

        /// <summary>Each word must be in the name (any order, any case); a word with * or ? is a wildcard for the whole name.</summary>
        static Func<string, bool> Matcher(string query)
        {
            string q = query.Trim();
            string[] terms = q.Length > 1 && q.StartsWith("\"") && q.EndsWith("\"")
                ? new[] { q.Substring(1, q.Length - 2) }
                : q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var tests = new List<Func<string, bool>>();
            foreach (string t in terms)
            {
                string term = t;
                if (term.IndexOfAny(new[] { '*', '?' }) >= 0)
                {
                    var re = new Regex("^" + Regex.Escape(term).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                                       RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    tests.Add(n => re.IsMatch(n));
                }
                else tests.Add(n => n.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            return n => tests.All(f => f(n));
        }

        /// <summary>Move what the search found into the list (in sort order), a batch at a time.</summary>
        void PumpResults()
        {
            SearchRun run = _search;
            if (run == null || run.Version != _loadVersion) { _searchPump.Stop(); return; }
            int v = run.Version;
            FileItem it;
            int n = 0;
            while (n < 1500 && run.Found.TryDequeue(out it))
            {
                n++;
                if (run.Count >= SearchLimit) { run.Capped = true; run.Cancel.Cancel(); break; }
                run.Count++;
                string folder = it.Folder ?? "";
                it.Where = folder.Length > run.Root.Length ? folder.Substring(run.Root.Length).TrimStart('\\') : "(this folder)";
                if (_cutPaths.Count > 0) it.IsCut = _cutPaths.Contains(it.Path);
                InsertSorted(it);
                FileItem item = it;
                _icons.Request(item.Path, item.IconKey, () => v == _loadVersion, img => item.Icon = img);
                if (run.Select != null && run.Select.Contains(it.Path))
                {
                    Files.SelectedItems.Add(it);
                    if (Files.SelectedItems.Count == 1) { Files.ScrollIntoView(it); FocusSelected(); }
                }
            }
            if (run.Capped || (run.Done && run.Found.IsEmpty))
            {
                run.Finished = true;
                _searchPump.Stop();
                run.Clock.Stop();
                if (_items.Count == 0)
                {
                    EmptyText.Text = "Nothing here matches “" + _searchQuery + "”.";
                    EmptyText.Visibility = System.Windows.Visibility.Visible;
                }
            }
            if (n > 0 || run.Finished) UpdateStatus();
        }

        void InsertSorted(FileItem it)
        {
            int lo = 0, hi = _items.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (CompareItems(_items[mid], it) <= 0) lo = mid + 1; else hi = mid;
            }
            _items.Insert(lo, it);
        }

        string SearchStatus()
        {
            SearchRun run = _search;
            int n = _items.Count;
            string count = n == 1 ? "1 result" : n.ToString("N0") + " results";
            if (run == null) return count;
            if (!run.Finished) return "Searching…  " + count + " so far";
            if (run.Capped) return "Showing the first " + SearchLimit.ToString("N0") + " results - type more to narrow it down";
            double s = run.Clock.Elapsed.TotalSeconds;
            return (n == 0 ? "No results" : count) + " in " + Native.DisplayName(_current) + " and its subfolders (" + (s < 10 ? s.ToString("0.0") : s.ToString("0")) + " s)";
        }

        /// <summary>The Folder column (where each result is), shown in Details while searching.</summary>
        void ShowFolderColumn(bool on)
        {
            var gv = _detailsView as GridView;
            if (gv == null) return;
            if (_folderColumn == null)
            {
                var text = new FrameworkElementFactory(typeof(TextBlock));
                text.SetBinding(TextBlock.TextProperty, new Binding("Where"));
                text.SetBinding(FrameworkElement.ToolTipProperty, new Binding("Folder"));
                text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
                text.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
                _folderColumn = new GridViewColumn { Header = "Folder", Width = 200, CellTemplate = new DataTemplate { VisualTree = text } };
            }
            bool shown = gv.Columns.Contains(_folderColumn);
            if (on && !shown) gv.Columns.Insert(Math.Min(1, gv.Columns.Count), _folderColumn);   // next to the name, as in Explorer
            else if (!on && shown) gv.Columns.Remove(_folderColumn);
        }
    }
}
