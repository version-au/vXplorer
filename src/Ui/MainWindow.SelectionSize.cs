using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using vXplorer.Core;

namespace vXplorer.Ui
{
    /// <summary>
    /// The status bar's size of the selection: files from the list, folders counted in the background (SizeCounter) - the count starts a
    /// moment after the selection settles and stops as soon as it changes.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>The selected folders being (or last) counted, as one string; null = none.</summary>
        string _sizeKey;
        List<string> _sizeFolders;
        CancellationTokenSource _sizeCancel;
        long _sizeBytes;
        bool _sizeStarted, _sizeDone;
        DispatcherTimer _sizeTimer;
        /// <summary>Until then a Status() message is showing: progress doesn't write over it.</summary>
        DateTime _messageUntil;

        /// <summary>"1.2 GB", "1.2 GB (counting…)" or "" - what the selected items add up to.</summary>
        string SelectionSize(List<FileItem> sel)
        {
            long fileBytes = sel.Where(f => !f.IsFolder && f.Size > 0).Sum(f => f.Size);
            bool anyFiles = sel.Any(f => !f.IsFolder);
            // folders inside a zip, This PC's or the network's places have nothing to count here
            List<string> folders = _settings.CountFolderSizes && !InPlace
                ? sel.Where(f => f.IsFolder).Select(f => f.Path).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();
            if (folders.Count == 0)
            {
                StopSizeCount();
                return anyFiles ? Format.Size(fileBytes) : "";
            }
            string key = string.Join("|", folders);
            if (!string.Equals(key, _sizeKey, StringComparison.OrdinalIgnoreCase))
            {
                StopSizeCount();
                _sizeKey = key;
                _sizeFolders = folders;
                if (_sizeTimer == null)
                {
                    _sizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                    _sizeTimer.Tick += (s, e) => { _sizeTimer.Stop(); StartSizeCount(); };
                }
                _sizeTimer.Start();   // arrowing through folders doesn't start a count for each one
            }
            if (!_sizeStarted) return anyFiles ? Format.Size(fileBytes) : "";
            if (_sizeDone) return Format.Size(fileBytes + _sizeBytes);
            return fileBytes + _sizeBytes > 0 ? Format.Size(fileBytes + _sizeBytes) + " (counting…)" : "(counting…)";
        }

        async void StartSizeCount()
        {
            string key = _sizeKey;
            List<string> folders = _sizeFolders;
            if (key == null || folders == null) return;
            var cancel = new CancellationTokenSource();
            _sizeCancel = cancel;
            _sizeStarted = true;
            _sizeBytes = 0;
            ShowSizeProgress();
            try
            {
                var r = await Task.Run(() => SizeCounter.Count(folders, cancel.Token, (bytes, files) =>
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (cancel.IsCancellationRequested || _sizeKey != key) return;
                        _sizeBytes = bytes;
                        ShowSizeProgress();
                    }))), cancel.Token);
                if (cancel.IsCancellationRequested || _sizeKey != key) return;
                _sizeBytes = r.Bytes;
                _sizeDone = true;
                ShowSizeProgress();
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>The selection moved on (or went): no more counting.</summary>
        void StopSizeCount()
        {
            if (_sizeTimer != null) _sizeTimer.Stop();
            if (_sizeCancel != null) { _sizeCancel.Cancel(); _sizeCancel = null; }
            _sizeKey = null;
            _sizeFolders = null;
            _sizeBytes = 0;
            _sizeStarted = _sizeDone = false;
        }

        void ShowSizeProgress()
        {
            if (DateTime.Now < _messageUntil) return;   // a message is showing; its timer puts the status back
            UpdateStatus();
        }
    }
}
