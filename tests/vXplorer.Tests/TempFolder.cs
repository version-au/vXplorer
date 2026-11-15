using System;
using System.IO;

namespace vXplorer.Tests
{
    /// <summary>A scratch folder under %TEMP% for one test, deleted afterwards.</summary>
    sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vXplorer.Tests", Guid.NewGuid().ToString("N"));

        public TempFolder() { Directory.CreateDirectory(Path); }

        public string Folder(string relative)
        {
            string p = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(p);
            return p;
        }

        public string File(string relative, int bytes)
        {
            string p = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p));
            System.IO.File.WriteAllBytes(p, new byte[bytes]);
            return p;
        }

        public void Dispose()
        {
            try
            {
                foreach (string f in Directory.EnumerateFileSystemEntries(Path, "*", SearchOption.AllDirectories))
                    System.IO.File.SetAttributes(f, FileAttributes.Normal & ~FileAttributes.ReadOnly);
                Directory.Delete(Path, true);
            }
            catch { }
        }
    }
}
