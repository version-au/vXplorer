using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace vXplorer.Core
{
    /// <summary>
    /// How much is inside some folders (every file in them and their subfolders), for the status bar's selection size. Runs on a worker,
    /// reports as it goes and stops when cancelled; folders it can't read are skipped.
    /// </summary>
    public static class SizeCounter
    {
        /// <summary>The total bytes and files under the folders (a file path among them counts as itself).</summary>
        /// <param name="progress">Called with the bytes and files so far, at most every <paramref name="every"/> (on the worker's thread).</param>
        public static (long Bytes, long Files) Count(IEnumerable<string> paths, CancellationToken cancel, Action<long, long> progress = null,
                                                     TimeSpan? every = null)
        {
            long bytes = 0, files = 0;
            var clock = Stopwatch.StartNew();
            long next = (long)(every ?? TimeSpan.FromMilliseconds(250)).TotalMilliseconds, step = next;
            // hidden and system files count too (as in Explorer's Properties); reparse points (junctions) are not followed - no loops
            var opts = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
            foreach (string p in paths)
            {
                cancel.ThrowIfCancellationRequested();
                try
                {
                    if (File.Exists(p)) { bytes += new FileInfo(p).Length; files++; continue; }
                    if (!Directory.Exists(p)) continue;
                    foreach (FileInfo f in new DirectoryInfo(p).EnumerateFiles("*", opts))
                    {
                        cancel.ThrowIfCancellationRequested();
                        bytes += f.Length;
                        files++;
                        if (progress != null && clock.ElapsedMilliseconds >= next)
                        {
                            progress(bytes, files);
                            next = clock.ElapsedMilliseconds + step;
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Log.Write("Size of " + p + ": " + ex.Message); }
            }
            return (bytes, files);
        }
    }
}
