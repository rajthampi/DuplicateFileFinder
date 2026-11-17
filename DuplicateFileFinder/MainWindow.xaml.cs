using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using WinRT.Interop;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DuplicateFileFinder
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window, INotifyPropertyChanged
    {
        public ObservableCollection<DuplicateFilePair> DuplicateFiles { get; } = new();
        public GridLength SourceFileColumnWidth { get; private set; } = new(420);
        public GridLength SourceSizeColumnWidth { get; private set; } = new(120);
        public GridLength SourceHashColumnWidth { get; private set; } = new(280);
        public GridLength DuplicateFileColumnWidth { get; private set; } = new(420);
        public GridLength DuplicateSizeColumnWidth { get; private set; } = new(120);
        public GridLength DuplicateHashColumnWidth { get; private set; } = new(280);
        public GridLength DeleteColumnWidth { get; private set; } = new(90);
        private bool _updatingSelection;
        private CheckBox? _selectAllCheckBox;
        private readonly SemaphoreSlim _thumbnailSemaphore = new(4);

        public event PropertyChangedEventHandler? PropertyChanged;

        //public MainWindow()
        //{
        //    InitializeComponent();
        //    RootGrid.DataContext = this;
        //    _selectAllCheckBox = (Content as FrameworkElement)?.FindName("SelectAllCheckBox") as CheckBox;
        //}

        public MainWindow()
        {
            InitializeComponent();

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Gemini_256.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }

            RootGrid.DataContext = this;
            _selectAllCheckBox = (Content as FrameworkElement)?.FindName("SelectAllCheckBox") as CheckBox;
        }

        private async Task LoadThumbnailsAsync(IEnumerable<DuplicateFilePair> files)
        {
            await Task.WhenAll(files.Select(async file =>
            {
                await _thumbnailSemaphore.WaitAsync();
                try
                {
                    await file.LoadThumbnailsAsync();
                }
                finally
                {
                    _thumbnailSemaphore.Release();
                }
            }));
        }

        private void FileThumbnail_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is not Image image || image.Tag is not string path || !File.Exists(path))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            e.Handled = true;
        }

        private void ColumnResize_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is not Thumb thumb || thumb.Tag is not string column)
            {
                return;
            }

            var width = column switch
            {
                "SourceFile" => SourceFileColumnWidth,
                "SourceSize" => SourceSizeColumnWidth,
                "SourceHash" => SourceHashColumnWidth,
                "DuplicateFile" => DuplicateFileColumnWidth,
                "DuplicateSize" => DuplicateSizeColumnWidth,
                "DuplicateHash" => DuplicateHashColumnWidth,
                _ => DeleteColumnWidth
            };

            var newWidth = Math.Max(80, width.Value + e.HorizontalChange);
            switch (column)
            {
                case "SourceFile": SourceFileColumnWidth = new GridLength(newWidth); break;
                case "SourceSize": SourceSizeColumnWidth = new GridLength(newWidth); break;
                case "SourceHash": SourceHashColumnWidth = new GridLength(newWidth); break;
                case "DuplicateFile": DuplicateFileColumnWidth = new GridLength(newWidth); break;
                case "DuplicateSize": DuplicateSizeColumnWidth = new GridLength(newWidth); break;
                case "DuplicateHash": DuplicateHashColumnWidth = new GridLength(newWidth); break;
                default: DeleteColumnWidth = new GridLength(newWidth); break;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs($"{column}ColumnWidth"));
        }

        private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                FolderBox.Text = folder.Path;
            }
        }

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            var folder = FolderBox.Text.Trim();
            if (!Directory.Exists(folder))
            {
                StatusText.Text = "Select an existing folder before scanning.";
                return;
            }

            var patterns = PatternBox.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(pattern => pattern.Trim()).Where(pattern => pattern.Length > 0).ToArray();
            if (patterns.Length == 0)
            {
                StatusText.Text = "Enter at least one file pattern.";
                return;
            }

            ScanProgress.IsActive = true;
            ScanProgressBar.Value = 0;
            ScanPercentageText.Text = "0%";
            DeleteButton.IsEnabled = false;
            DuplicateFiles.Clear();
            StatusText.Text = "Scanning files...";
            StatusBarText.Text = "Scanning files...";
            DuplicateCountText.Text = "Files: 0";
            DuplicateSizeText.Text = "Total size: 0 bytes";

            try
            {
                var progress = new Progress<int>(percentage =>
                {
                    ScanProgressBar.Value = percentage;
                    ScanPercentageText.Text = $"{percentage}%";
                });
                var files = await Task.Run(() => FindDuplicatesAsync(folder, patterns, progress));
                DuplicateList.ItemsSource = null;
                DuplicateFiles.Clear();
                DuplicateList.ItemsSource = DuplicateFiles;

                for (var index = 0; index < files.Count; index++)
                {
                    var file = files[index];
                    file.PropertyChanged += DuplicateFile_PropertyChanged;
                    DuplicateFiles.Add(file);
                    StatusBarText.Text = $"Loading thumbnails {index + 1:N0} of {files.Count:N0}...";
                    await file.LoadThumbnailsAsync();
                }
                UpdateSelectionControls();
                var totalSize = files.Sum(file => file.DuplicateSize);
                DuplicateCountText.Text = $"Files: {files.Count:N0}";
                DuplicateSizeText.Text = $"Total size: {FormatSize(totalSize)}";
                ScanProgressBar.Value = 100;
                ScanPercentageText.Text = "100%";
                StatusBarText.Text = "Scan complete";

                StatusText.Text = files.Count == 0
                    ? "No duplicate files found."
                    : $"Found {files.Count} duplicate files in {files.Select(file => file.GroupLabel).Distinct().Count()} groups.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusText.Text = $"The scan could not be completed: {ex.Message}";
                StatusBarText.Text = "Scan failed";
            }
            finally
            {
                ScanProgress.IsActive = false;
            }
        }

        private static async Task<List<DuplicateFilePair>> FindDuplicatesAsync(string folder, string[] patterns, IProgress<int> progress)
        {
            var paths = patterns.SelectMany(pattern => Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            progress.Report(5);

            var files = paths.Select(path => new FileInfo(path))
                .Where(file => file.Exists)
                .GroupBy(file => file.Length)
                .Where(group => group.Count() > 1)
                .SelectMany(group => group)
                .ToList();

            var hashedFiles = new List<(FileInfo File, string Hash)>();
            var totalBytes = files.Sum(file => Math.Max(1L, file.Length));
            var processedBytes = 0L;
            var lastPercentage = 5;

            for (var index = 0; index < files.Count; index++)
            {
                var file = files[index];
                var fileSize = Math.Max(1L, file.Length);
                var hash = await ComputeHashAsync(
                    file.FullName,
                    bytesRead =>
                    {
                        var percentage = 5 + (int)Math.Min(90L, (processedBytes + bytesRead) * 90L / totalBytes);
                        if (percentage > lastPercentage)
                        {
                            lastPercentage = percentage;
                            progress.Report(percentage);
                        }
                    });

                hashedFiles.Add((file, hash));
                processedBytes += fileSize;
                progress.Report(5 + (int)Math.Min(90L, processedBytes * 90L / totalBytes));
            }

            var result = new List<DuplicateFilePair>();
            var groupNumber = 1;
            foreach (var group in hashedFiles.GroupBy(item => item.Hash).Where(group => group.Count() > 1))
            {
                var label = $"Group {groupNumber++}";
                var source = group.First().File;
                result.AddRange(group.Skip(1).Select(item => new DuplicateFilePair(source, item.File, label, group.Key)));
            }
            return result;
        }

        private static async Task<string> ComputeHashAsync(string path, Action<long> progress)
        {
            const int bufferSize = 1024 * 1024;
            var buffer = new byte[bufferSize];

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long bytesReadTotal = 0;
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
            {
                hash.AppendData(buffer, 0, bytesRead);
                bytesReadTotal += bytesRead;
                progress(bytesReadTotal);
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }

        private static string FormatSize(long size)
        {
            string[] units = { "bytes", "KB", "MB", "GB", "TB" };
            var value = (double)size;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return $"{value:N1} {units[unit]}";
        }

        private static string ComputeHash(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            var selected = DuplicateFiles
                .Where(file => file.IsSelected)
                .ToList();
            if (selected.Count == 0)
            {
                StatusText.Text = "Select one or more files to delete.";
                return;
            }

            var moveToRecycleBin = RecycleBinCheckBox.IsChecked == true;
            var deletedCount = 0;
            StatusBarText.Text = "Deleting selected files...";
            ScanProgressBar.Value = 0;
            ScanPercentageText.Text = "0%";
            await Task.Yield();

            for (var index = 0; index < selected.Count; index++)
            {
                var item = selected[index];
                if (!item.IsSelected)
                {
                    continue;
                }

                try
                {
                    StatusBarText.Text = $"Deleting file {index + 1:N0} of {selected.Count:N0}...";
                    await Task.Run(() => DeleteDuplicateFile(item, moveToRecycleBin));
                    DuplicateFiles.Remove(item);
                    deletedCount++;
                    var percentage = (index + 1) * 100 / selected.Count;
                    ScanProgressBar.Value = percentage;
                    ScanPercentageText.Text = $"{percentage}%";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    StatusText.Text = $"Could not delete {item.DuplicateFileName}: {ex.Message}";
                }
            }

            DeleteButton.IsEnabled = false;
            UpdateSelectionControls();
            ScanProgressBar.Value = 100;
            ScanPercentageText.Text = "100%";
            StatusBarText.Text = "Delete complete";
            StatusText.Text = $"Deleted {deletedCount:N0} duplicate file{(deletedCount == 1 ? "" : "s")}.";
        }

        private void DeleteDuplicateFile(DuplicateFilePair item, bool moveToRecycleBin)
        {
            var duplicatePath = item.DuplicatePath;
            if (string.Equals(duplicatePath, item.SourcePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("The duplicate path matches the source path and was not deleted.");
            }

            if (moveToRecycleBin)
            {
                MoveToRecycleBin(duplicatePath);
            }
            else
            {
                File.Delete(duplicatePath);
            }
        }

        private void Delete_AcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            Delete_Click(sender, new RoutedEventArgs());
            args.Handled = true;
        }

        private void SelectAllCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_updatingSelection)
            {
                return;
            }

            _updatingSelection = true;
            _selectAllCheckBox ??= sender as CheckBox;
            var isSelected = _selectAllCheckBox?.IsChecked == true;
            foreach (var file in DuplicateFiles)
            {
                file.IsSelected = isSelected;
            }
            _updatingSelection = false;
            UpdateSelectionControls();
        }

        private void FileSelection_Changed(object sender, RoutedEventArgs e)
        {
            if (!_updatingSelection)
            {
                UpdateSelectionControls();
            }
        }

        private void DuplicateFile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DuplicateFilePair.IsSelected) && !_updatingSelection)
            {
                UpdateSelectionControls();
            }
        }

        private void UpdateSelectionControls()
        {
            var selectedCount = DuplicateFiles.Count(file => file.IsSelected);
            DeleteButton.IsEnabled = selectedCount > 0;

            _updatingSelection = true;
            if (_selectAllCheckBox != null)
            {
                _selectAllCheckBox.IsChecked = DuplicateFiles.Count > 0 && selectedCount == DuplicateFiles.Count;
            }
            _updatingSelection = false;
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => Close();

        private async void About_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Duplicate File Finder",
                Content = "Find and safely remove duplicate files.",
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT fileOperation);

        private static void MoveToRecycleBin(string path)
        {
            var operation = new SHFILEOPSTRUCT
            {
                wFunc = 3,
                pFrom = path + '\0' + '\0',
                fFlags = 0x0040 | 0x0010
            };
            if (SHFileOperation(ref operation) != 0)
            {
                throw new IOException("Windows could not move the file to the Recycle Bin.");
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public nint hwnd;
            public uint wFunc;
            public string pFrom;
            public string? pTo;
            public ushort fFlags;
            public int fAnyOperationsAborted;
            public nint hNameMappings;
            public string? lpszProgressTitle;
        }
    }

    public sealed class DuplicateFilePair : INotifyPropertyChanged
    {
        private bool _isSelected;

        public DuplicateFilePair(FileInfo source, FileInfo duplicate, string groupLabel, string hash)
        {
            SourcePath = source.FullName;
            SourceFileName = source.Name;
            SourceSizeText = $"{source.Length:N0} bytes";
            DuplicatePath = duplicate.FullName;
            DuplicateFileName = duplicate.Name;
            DuplicateSize = duplicate.Length;
            DuplicateSizeText = $"{duplicate.Length:N0} bytes";
            GroupLabel = groupLabel;
            SourceHash = hash;
            DuplicateHash = hash;
        }

        public string SourcePath { get; }
        public ImageSource? SourceThumbnail { get; private set; }
        public string SourceFileName { get; }
        public string SourceSizeText { get; }
        public string SourceHash { get; }
        public string DuplicatePath { get; }
        public ImageSource? DuplicateThumbnail { get; private set; }
        public string DuplicateFileName { get; }
        public string DuplicateSizeText { get; }
        public long DuplicateSize { get; }
        public string DuplicateHash { get; }
        public string GroupLabel { get; }
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public async Task LoadThumbnailsAsync()
        {
            SourceThumbnail = await LoadThumbnailAsync(SourcePath);
            OnPropertyChanged(nameof(SourceThumbnail));

            DuplicateThumbnail = await LoadThumbnailAsync(DuplicatePath);
            OnPropertyChanged(nameof(DuplicateThumbnail));
        }

        private static async Task<ImageSource?> LoadThumbnailAsync(string path)
        {
            StorageFile file;
            try
            {
                file = await StorageFile.GetFileFromPathAsync(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or COMException)
            {
                return null;
            }

            try
            {
                using var thumbnail = await file.GetThumbnailAsync(
                    ThumbnailMode.ListView,
                    64,
                    ThumbnailOptions.ResizeThumbnail);
                if (thumbnail != null)
                {
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(thumbnail);
                    return bitmap;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or COMException)
            {
            }

            try
            {
                using var thumbnail = await file.GetThumbnailAsync(
                    ThumbnailMode.SingleItem,
                    64,
                    ThumbnailOptions.ResizeThumbnail);
                if (thumbnail != null)
                {
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(thumbnail);
                    return bitmap;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or COMException)
            {
            }

            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is ".bmp" or ".gif" or ".jpeg" or ".jpg" or ".png" or ".tif" or ".tiff" or ".webp")
            {
                try
                {
                    using var stream = await file.OpenReadAsync();
                    var bitmap = new BitmapImage { DecodePixelWidth = 64 };
                    await bitmap.SetSourceAsync(stream);
                    return bitmap;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or COMException)
                {
                }
            }

            return null;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
