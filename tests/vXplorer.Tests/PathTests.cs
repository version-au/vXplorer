using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using vXplorer.Core;
using Xunit;

namespace vXplorer.Tests
{
    public class PathsTests
    {
        [Theory]
        [InlineData("C:", @"C:\")]
        [InlineData(@"C:\", @"C:\")]
        [InlineData(@"C:\Windows\", @"C:\Windows")]
        [InlineData(@"  ""C:\Windows""  ", @"C:\Windows")]
        [InlineData(@"C:\Windows\..\Users", @"C:\Users")]
        public void Clean_tidies_paths(string typed, string expected)
        {
            Assert.Equal(expected, Paths.Clean(typed));
        }

        [Fact]
        public void Clean_expands_environment_variables()
        {
            Assert.Equal(Environment.GetEnvironmentVariable("SystemRoot").TrimEnd('\\'), Paths.Clean("%SystemRoot%"), ignoreCase: true);
        }

        [Fact]
        public void Clean_gives_null_for_nothing_or_nonsense()
        {
            Assert.Null(Paths.Clean(null));
            Assert.Null(Paths.Clean("   "));
        }

        [Fact]
        public void Same_ignores_case()
        {
            Assert.True(Paths.Same(@"C:\Users", @"c:\users"));
            Assert.False(Paths.Same(@"C:\Users", @"C:\Users\x"));
        }
    }

    public class PathSuggestTests
    {
        [Theory]
        [InlineData(@"C:\Us", @"C:\", "Us")]
        [InlineData(@"C:\", @"C:\", "")]
        [InlineData(@"C:\Users\", @"C:\Users\", "")]
        [InlineData(@"C:/Users/st", @"C:\Users\", "st")]
        [InlineData(@"\\", @"\\", "")]
        [InlineData(@"\\NA", @"\\", "NA")]
        [InlineData(@"\\NAS\", @"\\NAS\", "")]
        [InlineData(@"\\NAS\me", @"\\NAS\", "me")]
        public void Split_finds_the_folder_and_the_name_so_far(string text, string folder, string prefix)
        {
            string f, p;
            Assert.True(PathSuggest.Split(text, out f, out p));
            Assert.Equal(folder, f);
            Assert.Equal(prefix, p);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Docu")]
        [InlineData("C:")]
        [InlineData(@"..\x")]
        [InlineData(@"\Windows")]
        public void Split_says_no_without_a_full_folder(string text)
        {
            string f, p;
            Assert.False(PathSuggest.Split(text, out f, out p));
        }

        static readonly List<Tuple<string, string>> Places = new List<Tuple<string, string>>
        {
            Tuple.Create("This PC", "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"),
            Tuple.Create("Network", "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}"),
            Tuple.Create("Documents", @"C:\Users\me\Documents"),
            Tuple.Create("Downloads", @"C:\Users\me\Downloads"),
            Tuple.Create("Local Disk (C:)", @"C:\"),
            Tuple.Create("Data (D:)", @"D:\"),
        };

        [Fact]
        public void Filter_matches_names_from_the_start_ignoring_case()
        {
            var r = PathSuggest.Filter(Places, "do");
            Assert.Equal(new[] { "Documents", "Downloads" }, r.Select(x => x.Item1));
        }

        [Fact]
        public void Filter_finds_a_drive_by_its_letter_but_not_folders_by_their_path()
        {
            var r = PathSuggest.Filter(Places, "c");
            Assert.Equal(new[] { "Local Disk (C:)" }, r.Select(x => x.Item1));   // not Documents / Downloads (C:\Users\…)
        }

        [Fact]
        public void Filter_sorts_like_explorer_and_stops_at_max()
        {
            var all = Enumerable.Range(1, 20).Select(i => Tuple.Create("item" + i, @"C:\x\item" + i)).Reverse();
            var r = PathSuggest.Filter(all, "item", 5);
            Assert.Equal(new[] { "item1", "item2", "item3", "item4", "item5" }, r.Select(x => x.Item1));
        }

        [Fact]
        public void For_without_a_backslash_offers_the_places()
        {
            var r = PathSuggest.For("net", false, Places);
            Assert.Equal("Network", Assert.Single(r).Item1);
        }

        [Fact]
        public void For_lists_matching_subfolders_not_files_or_hidden_ones()
        {
            using (var t = new TempFolder())
            {
                t.Folder("alpha");
                t.Folder("Alps");
                t.Folder("beta");
                t.File("alfalfa.txt", 1);
                string hidden = t.Folder("alcove");
                File.SetAttributes(hidden, FileAttributes.Directory | FileAttributes.Hidden);

                var r = PathSuggest.For(t.Path + @"\al", false, null);
                Assert.Equal(new[] { "alpha", "Alps" }, r.Select(x => x.Item1));
                Assert.Equal(Path.Combine(t.Path, "alpha"), r[0].Item2);

                var all = PathSuggest.For(t.Path + @"\al", true, null);   // hidden items shown
                Assert.Equal(new[] { "alcove", "alpha", "Alps" }, all.Select(x => x.Item1));

                Assert.Equal(4, PathSuggest.For(t.Path + @"\", true, null).Count);
            }
        }

        [Fact]
        public void For_a_folder_that_isnt_there_is_empty()
        {
            Assert.Empty(PathSuggest.For(@"C:\no such folder 7f3a\x", false, null));
        }
    }
}
