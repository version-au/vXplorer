using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using vXplorer.Core;
using Xunit;

namespace vXplorer.Tests
{
    public class SizeCounterTests
    {
        [Fact]
        public void Counts_every_file_in_folders_and_subfolders()
        {
            using (var t = new TempFolder())
            {
                t.File(@"a\one.bin", 1000);
                t.File(@"a\deep\er\two.bin", 2500);
                string hidden = t.File(@"a\hidden.bin", 500);
                File.SetAttributes(hidden, FileAttributes.Hidden);   // counted, as in Explorer's Properties
                t.File(@"b\three.bin", 7);
                string loose = t.File("loose.bin", 30);

                var r = SizeCounter.Count(new[] { Path.Combine(t.Path, "a"), Path.Combine(t.Path, "b"), loose }, CancellationToken.None);
                Assert.Equal(1000 + 2500 + 500 + 7 + 30, r.Bytes);
                Assert.Equal(5, r.Files);
            }
        }

        [Fact]
        public void A_missing_folder_counts_as_nothing()
        {
            var r = SizeCounter.Count(new[] { @"C:\no such folder 7f3a" }, CancellationToken.None);
            Assert.Equal(0, r.Bytes);
            Assert.Equal(0, r.Files);
        }

        [Fact]
        public void Stops_when_cancelled()
        {
            using (var t = new TempFolder())
            {
                t.File(@"a\one.bin", 10);
                var cancel = new CancellationTokenSource();
                cancel.Cancel();
                Assert.ThrowsAny<OperationCanceledException>(() => SizeCounter.Count(new[] { Path.Combine(t.Path, "a") }, cancel.Token));
            }
        }

        [Fact]
        public void Reports_progress_along_the_way()
        {
            using (var t = new TempFolder())
            {
                for (int i = 0; i < 50; i++) t.File(@"a\f" + i + ".bin", 10);
                long last = -1;
                SizeCounter.Count(new[] { Path.Combine(t.Path, "a") }, CancellationToken.None, (b, n) => last = b, TimeSpan.Zero);
                Assert.True(last > 0 && last <= 500);
            }
        }
    }

    public class FolderWatcherTests
    {
        [Fact]
        public void A_hidden_item_is_unseen_only_while_hidden_items_are_hidden_and_it_isnt_listed()
        {
            using (var t = new TempFolder())
            {
                string h = t.File("ntuser.dat.LOG1", 1);
                File.SetAttributes(h, FileAttributes.Hidden);
                string plain = t.File("plain.txt", 1);
                var none = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                Assert.True(FolderWatcher.Unseen(h, false, none));
                Assert.False(FolderWatcher.Unseen(h, true, none));   // hidden items shown
                Assert.False(FolderWatcher.Unseen(h, false, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { h }));   // just made hidden: has to leave the list
                Assert.False(FolderWatcher.Unseen(plain, false, none));
                Assert.False(FolderWatcher.Unseen(Path.Combine(t.Path, "gone.txt"), false, none));   // gone: seen (the list drops it)
            }
        }
    }

    public class FormatTests
    {
        [Theory]
        [InlineData(0, "0 bytes")]
        [InlineData(1023, "1,023 bytes")]
        [InlineData(1024, "1.0 KB")]
        [InlineData(1536, "1.5 KB")]
        [InlineData(10L * 1024 * 1024, "10 MB")]
        [InlineData(3L * 1024 * 1024 * 1024 * 1024, "3.0 TB")]
        public void Size(long bytes, string expected)
        {
            Assert.Equal(expected, Format.Size(bytes));
        }

        [Theory]
        [InlineData(-1, "")]
        [InlineData(0, "0 KB")]
        [InlineData(1, "1 KB")]
        [InlineData(1025, "2 KB")]
        public void KiloBytes_rounds_up_like_explorer(long bytes, string expected)
        {
            Assert.Equal(expected, Format.KiloBytes(bytes));
        }
    }

    public class NetworkPathTests
    {
        [Theory]
        [InlineData(@"\\NAS", true)]
        [InlineData(@"\\NAS\", true)]
        [InlineData(@"\\NAS\share", false)]
        [InlineData(@"\\", false)]
        [InlineData(@"C:\", false)]
        [InlineData(null, false)]
        public void IsServer(string p, bool expected) { Assert.Equal(expected, NetworkPlaces.IsServer(p)); }

        [Theory]
        [InlineData(@"\\NAS\share", true)]
        [InlineData(@"\\NAS\share\", true)]
        [InlineData(@"\\NAS\share\folder", false)]
        [InlineData(@"\\NAS", false)]
        public void IsShareRoot(string p, bool expected) { Assert.Equal(expected, NetworkPlaces.IsShareRoot(p)); }

        [Fact]
        public void Network_by_name_or_path()
        {
            Assert.True(NetworkPlaces.IsNetwork("network"));
            Assert.True(NetworkPlaces.IsNetwork(NetworkPlaces.NetworkPath));
            Assert.False(NetworkPlaces.IsNetwork(@"\\NAS"));
        }
    }

    public class ZipPathTests
    {
        [Fact]
        public void A_path_inside_a_zip_splits_into_the_zip_and_the_inner_path()
        {
            using (var t = new TempFolder())
            {
                string zip = Path.Combine(t.Path, "photos.zip");
                using (ZipArchive a = ZipFile.Open(zip, ZipArchiveMode.Create)) a.CreateEntry("2024/a.jpg");
                string z, inner;
                Assert.True(ZipFolders.TrySplit(zip + @"\2024", out z, out inner));
                Assert.Equal(zip, z);
                Assert.Equal("2024", inner);
                Assert.True(ZipFolders.TrySplit(zip, out z, out inner));
                Assert.Equal("", inner);
                Assert.False(ZipFolders.TrySplit(t.Path, out z, out inner));   // an ordinary folder
                Assert.False(ZipFolders.TrySplit("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", out z, out inner));   // This PC
            }
        }

        [Fact]
        public void Extract_all_never_writes_beside_its_folder()
        {
            using (var t = new TempFolder())
            {
                string zip = Path.Combine(t.Path, "photos.zip");
                using (ZipArchive a = ZipFile.Open(zip, ZipArchiveMode.Create))
                {
                    using (var w = new StreamWriter(a.CreateEntry("in.txt").Open())) w.Write("in");
                    using (var w = new StreamWriter(a.CreateEntry("../photos-private/out.txt").Open())) w.Write("out");
                    using (var w = new StreamWriter(a.CreateEntry("../photos.pdf.exe").Open())) w.Write("out");
                }
                string folder = ZipFolders.ExtractAll(zip);
                Assert.True(File.Exists(Path.Combine(folder, "in.txt")));
                Assert.False(Directory.Exists(Path.Combine(t.Path, "photos-private")));   // its name starts like the folder's
                Assert.False(File.Exists(Path.Combine(t.Path, "photos.pdf.exe")));
                Assert.True(ZipFolders.Within(@"C:\x\photos\a.txt", @"C:\x\photos"));
                Assert.True(ZipFolders.Within(@"C:\x\photos", @"C:\x\photos\"));
                Assert.False(ZipFolders.Within(@"C:\x\photos-private\a.txt", @"C:\x\photos"));
            }
        }
    }

    public class NetworkAwayTests
    {
        [Fact]
        public void A_network_place_that_does_not_answer_is_only_waited_for_once()
        {
            Assert.Null(vXplorer.Core.Paths.NetworkRoot(Path.GetTempPath()));                         // this PC's own drive
            Assert.Equal(@"\\nas\media", vXplorer.Core.Paths.NetworkRoot(@"\\nas\media\films\a"));
            Assert.True(vXplorer.Core.Paths.FolderThere(Path.GetTempPath()));

            string gone = @"\\192.0.2.1\nothing\folder";                                              // an address nothing answers on
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Assert.False(vXplorer.Core.Paths.FolderThere(gone));
            Assert.True(clock.Elapsed.TotalSeconds < 8, "the first look took " + clock.Elapsed.TotalSeconds + " s");
            if (vXplorer.Core.Paths.Away(gone))                                                       // it timed out (not refused at once)
            {
                clock.Restart();
                Assert.False(vXplorer.Core.Paths.FolderThere(@"\\192.0.2.1\nothing\another"));
                Assert.True(clock.Elapsed.TotalMilliseconds < 500, "the second look waited again");
            }
        }
    }

    public class ResumeTests
    {
        [Fact]
        public void Only_a_really_half_written_file_is_removed_when_a_copy_is_finished_later()
        {
            using (var t = new TempFolder())
            {
                string src = Path.Combine(t.Path, "src"), dest = Path.Combine(t.Path, "dest");
                Directory.CreateDirectory(Path.Combine(src, "Album"));
                Directory.CreateDirectory(Path.Combine(dest, "Album"));
                string a = Path.Combine(src, "a.txt"), inAlbum = Path.Combine(src, "Album", "p.jpg");
                File.WriteAllText(a, "the whole file");
                File.WriteAllText(inAlbum, "the whole picture");
                var j = new CopyJournal { Kind = "move", Dest = dest, Sources = { a, Path.Combine(src, "Album") } };

                // half there: shorter than its source
                j.Current = Path.Combine(dest, "a.txt");
                File.WriteAllText(j.Current, "the wh");
                Assert.True(vXplorer.Ui.MainWindow.HalfWritten(j));

                // finished since the journal was last saved (a move: the source has gone) - the only copy, never removed
                File.Delete(a);
                File.WriteAllText(j.Current, "the whole file");
                Assert.False(vXplorer.Ui.MainWindow.HalfWritten(j));

                // inside a folder: compared with the file at the same place in the source folder
                j.Current = Path.Combine(dest, "Album", "p.jpg");
                File.WriteAllText(j.Current, "the who");
                Assert.True(vXplorer.Ui.MainWindow.HalfWritten(j));
                File.Copy(inAlbum, j.Current, true);
                Assert.False(vXplorer.Ui.MainWindow.HalfWritten(j));                     // the same file: complete

                // a Keep both item got a new name: the file of its own name is the one that was kept
                string b = Path.Combine(src, "b.txt");
                File.WriteAllText(b, "new");
                File.WriteAllText(Path.Combine(dest, "b.txt"), "the one to keep");
                j.Sources.Add(b); j.KeepBoth.Add(b);
                j.Current = Path.Combine(dest, "b.txt");
                Assert.False(vXplorer.Ui.MainWindow.HalfWritten(j));

                j.Current = Path.Combine(t.Path, "elsewhere.txt");                       // not in the destination at all
                File.WriteAllText(j.Current, "x");
                Assert.False(vXplorer.Ui.MainWindow.HalfWritten(j));
            }
        }
    }

    public class SettingsTests
    {
        [Fact]
        public void Normalize_puts_bad_values_right()
        {
            var s = JsonSerializer.Deserialize<AppSettings>(
                "{\"ViewMode\":\"Huge\",\"SortKey\":\"Colour\",\"SidebarWidth\":5,\"TextPreviewKB\":7,\"Columns\":[\"Name\",\"Size\",\"Nope\"],\"GroupBy\":\"x\"}");
            s.Normalize();
            Assert.Equal("Details", s.ViewMode);
            Assert.Equal("Name", s.SortKey);
            Assert.Equal(230, s.SidebarWidth);
            Assert.Equal(128, s.TextPreviewKB);
            Assert.Equal(new[] { "Size" }, s.Columns);
            Assert.Equal("None", s.GroupBy);
        }

        [Fact]
        public void A_new_setting_missing_from_an_old_file_takes_its_default()
        {
            var s = JsonSerializer.Deserialize<AppSettings>("{}");
            s.Normalize();
            Assert.True(s.CountFolderSizes);
            Assert.Equal(new[] { "Date", "Type", "Size" }, s.Columns);
        }
    }
}
