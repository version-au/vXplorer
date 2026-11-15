using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>
    /// Details columns you choose (right-click a column header): Date modified, Type, Size, Date created, Dimensions, Length - Name is
    /// always there. Dimensions and Length come from Windows' file properties, read in the background (FileProps) while they're shown.
    /// Group by (Sort menu): Name, Date modified, Type or Size, with a header over each group, as in Explorer; kept per folder like the
    /// sort. Search results aren't grouped.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Each Details column and the key it sorts by (the search's Folder column isn't in here).</summary>
        readonly Dictionary<GridViewColumn, string> _colKey = new Dictionary<GridViewColumn, string>();
        readonly Dictionary<string, GridViewColumn> _colByKey = new Dictionary<string, GridViewColumn>();
        FileProps _props;

        /// <summary>The four columns from the XAML and the three extra ones, then the ones you chose.</summary>
        void InitColumns()
        {
            var gv = _detailsView as GridView;
            if (gv == null) return;
            for (int i = 0; i < Math.Min(4, gv.Columns.Count); i++) { _colKey[gv.Columns[i]] = SortKeys[i]; _colByKey[SortKeys[i]] = gv.Columns[i]; }
            AddColumn("Created", "CreatedText", 160, false);
            AddColumn("Dimensions", "Dimensions", 110, false);
            AddColumn("Length", "LengthText", 80, true);
            _props = new FileProps(Dispatcher);
            Closed += (s, e) => _props.Dispose();
            ApplyColumns();
        }

        void AddColumn(string key, string binding, double width, bool right)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding(binding));
            text.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            if (right) { text.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right); text.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 6, 0)); }
            var col = new GridViewColumn { Width = width, CellTemplate = new DataTemplate { VisualTree = text } };
            _colKey[col] = key;
            _colByKey[key] = col;
        }

        /// <summary>Name, then the chosen columns in their usual order (the search's Folder column stays next to Name).</summary>
        void ApplyColumns()
        {
            var gv = _detailsView as GridView;
            if (gv == null || _colByKey.Count == 0) return;
            bool folderCol = _folderColumn != null && gv.Columns.Contains(_folderColumn);
            gv.Columns.Clear();
            gv.Columns.Add(_colByKey["Name"]);
            if (folderCol) gv.Columns.Add(_folderColumn);
            foreach (string k in SortKeys.Skip(1)) if (_settings.Columns.Contains(k)) gv.Columns.Add(_colByKey[k]);
            UpdateSortHeaders();
            RequestProps();
        }

        bool PropsWanted { get { return _settings.Columns.Contains("Dimensions") || _settings.Columns.Contains("Length") || _settings.SortKey == "Dimensions" || _settings.SortKey == "Length"; } }

        /// <summary>Read the pictures' / videos' dimensions and the media's length for the list, if those columns are showing.</summary>
        void RequestProps()
        {
            if (_props == null || !PropsWanted || _items == null || _items.Count == 0 || InPlace) return;
            int v = _loadVersion;
            _props.Request(_items.ToList(), () => v == _loadVersion);
        }

        /// <summary>Right-click a column header: tick the columns to show.</summary>
        void ShowColumnMenu()
        {
            var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
            var name = new MenuItem { Header = "Name", IsCheckable = true, IsChecked = true, IsEnabled = false };
            menu.Items.Add(name);
            for (int i = 1; i < SortKeys.Length; i++)
            {
                string k = SortKeys[i];
                var mi = new MenuItem { Header = Headers[i], IsCheckable = true, IsChecked = _settings.Columns.Contains(k), StaysOpenOnClick = true };
                mi.Click += (s, e) =>
                {
                    if (mi.IsChecked && !_settings.Columns.Contains(k)) _settings.Columns.Add(k);
                    if (!mi.IsChecked) _settings.Columns.Remove(k);
                    _settings.Save();
                    foreach (MainWindow w in Application.Current.Windows.OfType<MainWindow>()) w.ApplyColumns();
                };
                menu.Items.Add(mi);
                if (i == 3) menu.Items.Add(new Separator());
            }
            menu.IsOpen = true;
        }

        // ---- group by ----

        static readonly string[] GroupKeys = { "None", "Name", "Date", "Type", "Size" };
        static readonly string[] GroupNames = { "(None)", "Name", "Date modified", "Type", "Size" };

        bool Grouped { get { return _settings.GroupBy != "None" && !Searching && !InPlace; } }

        void SetGroupBy(string key)
        {
            if (_settings.GroupBy == key) return;
            _settings.GroupBy = key;
            RememberFolderView();
            ApplyView();   // the icon views lay grouped tiles out differently
            var keep = Files.SelectedItems.Cast<FileItem>().ToList();
            ShowItems();
            foreach (FileItem f in keep) Files.SelectedItems.Add(f);
        }

        /// <summary>The Group by items, for the Sort menus.</summary>
        void AddGroupItems(ItemCollection items)
        {
            var group = new MenuItem { Header = "Group by" };
            for (int i = 0; i < GroupKeys.Length; i++)
            {
                string k = GroupKeys[i];
                group.Items.Add(Check(GroupNames[i], _settings.GroupBy == k, () => SetGroupBy(k)));
            }
            items.Add(group);
        }

        /// <summary>Each item's group name and the groups' order, by what the list is grouped by.</summary>
        void SetGroups(List<FileItem> list)
        {
            string by = _settings.GroupBy;
            if (by == "Type")
            {
                // folders first, then the types A-Z
                var types = list.Where(f => !f.IsFolder).Select(f => f.TypeName ?? "").Distinct(StringComparer.CurrentCultureIgnoreCase)
                                .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase).ToList();
                foreach (FileItem f in list)
                {
                    f.Group = f.IsFolder ? "File folder" : (f.TypeName ?? "");
                    f.GroupOrder = f.IsFolder ? 0 : 1 + types.FindIndex(t => string.Equals(t, f.TypeName ?? "", StringComparison.CurrentCultureIgnoreCase));
                }
                return;
            }
            DateTime today = DateTime.Today;
            int dow = ((int)today.DayOfWeek + 6) % 7;   // Monday = 0
            DateTime weekStart = today.AddDays(-dow), lastWeek = weekStart.AddDays(-7), monthStart = new DateTime(today.Year, today.Month, 1);
            foreach (FileItem f in list)
            {
                string g;
                int o;
                if (by == "Name")
                {
                    char c = string.IsNullOrEmpty(f.Name) ? ' ' : char.ToUpperInvariant(f.Name[0]);
                    if (c >= 'A' && c <= 'H') { g = "A - H"; o = 1; }
                    else if (c >= 'I' && c <= 'P') { g = "I - P"; o = 2; }
                    else if (c >= 'Q' && c <= 'Z') { g = "Q - Z"; o = 3; }
                    else if (char.IsDigit(c)) { g = "0 - 9"; o = 0; }
                    else { g = "Other"; o = 4; }
                }
                else if (by == "Date")
                {
                    DateTime d = f.Modified.Date;
                    if (f.Modified == DateTime.MinValue) { g = "A long time ago"; o = 7; }
                    else if (d >= today) { g = "Today"; o = 0; }
                    else if (d == today.AddDays(-1)) { g = "Yesterday"; o = 1; }
                    else if (d >= weekStart) { g = "Earlier this week"; o = 2; }
                    else if (d >= lastWeek) { g = "Last week"; o = 3; }
                    else if (d >= monthStart) { g = "Earlier this month"; o = 4; }
                    else if (d >= monthStart.AddMonths(-1)) { g = "Last month"; o = 5; }
                    else if (d.Year == today.Year) { g = "Earlier this year"; o = 6; }
                    else { g = "A long time ago"; o = 7; }
                    if (_settings.SortDescending == false && _settings.SortKey == "Date") o = 7 - o;   // oldest first when sorted that way
                }
                else   // Size
                {
                    long s = f.Size;
                    if (f.IsFolder) { g = "Folders"; o = 0; }
                    else if (s == 0) { g = "Empty"; o = 1; }
                    else if (s < 16 * 1024) { g = "Tiny (under 16 KB)"; o = 2; }
                    else if (s < 1024 * 1024) { g = "Small (16 KB - 1 MB)"; o = 3; }
                    else if (s < 128L * 1024 * 1024) { g = "Medium (1 - 128 MB)"; o = 4; }
                    else if (s < 1024L * 1024 * 1024) { g = "Large (128 MB - 1 GB)"; o = 5; }
                    else if (s < 4L * 1024 * 1024 * 1024) { g = "Huge (1 - 4 GB)"; o = 6; }
                    else { g = "Gigantic (over 4 GB)"; o = 7; }
                }
                f.Group = g;
                f.GroupOrder = o;
            }
        }
    }
}
