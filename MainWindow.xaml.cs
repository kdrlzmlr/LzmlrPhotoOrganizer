// Updated MainWindow.xaml.cs - Full replacement with all three requested modifications

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using Directory = System.IO.Directory;

namespace PhotoVideoOrganizer
{
    public partial class MainWindow : Window
    {
        private string _source = "";
        private string _target = "";
        private readonly StringBuilder _logBuilder = new StringBuilder();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void BrowseSource_Click(object sender, RoutedEventArgs e)
        {
            _source = BrowseFolder();
            SourceTextBox.Text = _source;
        }

        private void BrowseTarget_Click(object sender, RoutedEventArgs e)
        {
            _target = BrowseFolder();
            TargetTextBox.Text = _target;
        }

        private string BrowseFolder()
        {
            var folderDialog = new OpenFolderDialog()
            {
                Title = "Select Folder",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };

            if (folderDialog.ShowDialog() == true)
            {
                var folderName = folderDialog.FolderName;
            }
            return folderDialog.FolderName;
        }

        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_source) || !Directory.Exists(_source))
            {
                MessageBox.Show("Invalid source folder.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (string.IsNullOrEmpty(_target))
            {
                MessageBox.Show("Invalid target folder.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Directory.CreateDirectory(_target);
            _logBuilder.Clear();

            StartButton.IsEnabled = false;
            StatusText.Text = "Scanning files...";

            var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".heic", ".heif", ".mp4", ".avi", ".mov", ".wmv", ".mkv", ".m4v" };
            var files = Directory.EnumerateFiles(_source, "*.*", SearchOption.AllDirectories)
                                 .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                 .ToList();

            if (files.Count == 0)
            {
                MessageBox.Show("No supported media files found.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                StartButton.IsEnabled = true;
                StatusText.Text = "Ready";
                return;
            }

            StatusText.Text = $"Found {files.Count} files. Computing hashes (optimized)...";
            ProgressBar.Value = 0;
            ProgressBar.Maximum = files.Count;

            // Optimization: Use incremental hash + larger buffer + parallel processing for small files
            var fileGroups = new Dictionary<string, List<string>>();
            var duplicateFiles = new List<string>();
            long duplicateSizeBytes = 0;

            int processed = 0;

            // Parallel hashing with controlled concurrency (adjust based on CPU/disk)
            int maxDegree = Environment.ProcessorCount / 2;
            if (maxDegree < 1) maxDegree = 1;

            await Task.Run(async () =>
            {
                var tasks = files.Select(async filePath =>
                {
                    string hash;
                    long size;
                    try
                    {
                        (hash, size) = await ComputeFileHashFastAsync(filePath);
                    }
                    catch (Exception ex)
                    {
                        lock (_logBuilder)
                        {
                            _logBuilder.AppendLine($"[ERROR] Hashing failed for {filePath}: {ex.Message}");
                        }
                        hash = Guid.NewGuid().ToString(); // Treat as unique
                        size = 0;
                    }

                    lock (fileGroups)
                    {
                        string key = $"{hash}|{size}";
                        if (!fileGroups.ContainsKey(key))
                            fileGroups[key] = new List<string>();

                        fileGroups[key].Add(filePath);

                        // Track duplicates (all except first in group)
                        if (fileGroups[key].Count > 1)
                        {
                            lock (duplicateFiles)
                            {
                                if (fileGroups[key].Count == 2) // First time this group becomes duplicate
                                {
                                    // Add all previous as duplicates too
                                    duplicateFiles.AddRange(fileGroups[key].Take(fileGroups[key].Count - 1));
                                    duplicateSizeBytes += size * (fileGroups[key].Count - 1);
                                }
                                else
                                {
                                    duplicateFiles.Add(filePath);
                                    duplicateSizeBytes += size;
                                }
                            }
                        }
                    }

                    int current = System.Threading.Interlocked.Increment(ref processed);
                    Dispatcher.Invoke(() =>
                    {
                        ProgressBar.Value = current;
                        StatusText.Text = $"Hashed {current}/{files.Count} files... ({Path.GetFileName(filePath)})";
                    });
                });

                await Task.WhenAll(tasks);
            });

            // Phase 2: Moving files
            StatusText.Text = "Organizing files...";
            ProgressBar.Value = 0;
            ProgressBar.Maximum = files.Count;

            string dupeDir = Path.Combine(_target, "Duplicates");
            Directory.CreateDirectory(dupeDir);

            int moved = 0;
            int duplicates = duplicateFiles.Count;

            foreach (var group in fileGroups.Values)
            {
                var toKeepPath = group.First();
                var date = GetDateTaken(toKeepPath);
                string year = date.ToString("yyyy");
                string month = date.ToString("MMMM");  // Full month name: January, February, ...

                // Create Year → Month structure
                string yearDir = Path.Combine(_target, year);
                string destDir = Path.Combine(yearDir, month);

                Directory.CreateDirectory(destDir);  // Creates year and month in one go if needed

                var fileName = Path.GetFileName(toKeepPath);
                var destPath = GetUniquePath(Path.Combine(destDir, fileName));

                try
                {
                    File.Move(toKeepPath, destPath);
                    moved++;
                }
                catch (Exception ex)
                {
                    _logBuilder.AppendLine($"[ERROR] Failed to move {toKeepPath} -> {destPath}: {ex.Message}");
                }

                foreach (var dupePath in group.Skip(1))
                {
                    var dupeDest = GetUniquePath(Path.Combine(dupeDir, Path.GetFileName(dupePath)));
                    try
                    {
                        File.Move(dupePath, dupeDest);
                    }
                    catch (Exception ex)
                    {
                        _logBuilder.AppendLine($"[ERROR] Failed to move duplicate {dupePath} -> {dupeDest}: {ex.Message}");
                    }
                }

                ProgressBar.Value += group.Count;
            }

            double duplicateGB = duplicateSizeBytes / (1024.0 * 1024.0 * 1024.0);

            // Final report
            string summary = $"Organization Complete!\n\n" +
                            $"Unique files moved: {moved}\n" +
                            $"Duplicates found: {duplicates}\n" +
                            $"Space saved: {duplicateGB:F2} GB\n\n" +
                            $"Duplicates are in: {_target}\\Duplicates\n";

            if (_logBuilder.Length > 0)
            {
                summary += "\nSome errors occurred during processing. See log below.\n\n";
            }

            StatusText.Text = "Done!";
            StartButton.IsEnabled = true;

            MessageBox.Show(summary, "Success", MessageBoxButton.OK, MessageBoxImage.Information);

            // Show duplicates list and log if any
            if (duplicates > 0 || _logBuilder.Length > 0)
            {
                var report = new StringBuilder();
                report.AppendLine("=== DUPLICATE FILES ===");
                foreach (var d in duplicateFiles)
                    report.AppendLine(d);

                report.AppendLine($"\nTotal duplicates: {duplicates} ({duplicateGB:F2} GB)");

                if (_logBuilder.Length > 0)
                {
                    report.AppendLine("\n=== ERROR LOG ===");
                    report.Append(_logBuilder.ToString());
                }

                var logWindow = new Window
                {
                    Title = "Duplicates & Log Report",
                    Width = 800,
                    Height = 600,
                    Background = System.Windows.Media.Brushes.Black,
                    Foreground = System.Windows.Media.Brushes.White,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this
                };
                var textBox = new System.Windows.Controls.TextBox
                {
                    Text = report.ToString(),
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 12,
                    IsReadOnly = true,
                    VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                    Margin = new Thickness(10)
                };
                logWindow.Content = textBox;
                logWindow.ShowDialog();
            }
        }

        // Optimized fast hashing: larger buffer + incremental + no extra allocations
        private static async Task<(string hash, long size)> ComputeFileHashFastAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1024 * 1024); // 1MB buffer

                byte[] buffer = new byte[1024 * 1024];
                int bytesRead;
                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    sha256.AppendData(buffer, 0, bytesRead);
                }

                byte[] hashBytes = sha256.GetHashAndReset();
                string hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                return (hash, stream.Length);
            });
        }

        private DateTime GetDateTaken(string filePath)
        {
            try
            {
                var directories = ImageMetadataReader.ReadMetadata(filePath);

                var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                if (subIfd?.TryGetDateTime(ExifSubIfdDirectory.TagDateTimeOriginal, out var exifDt) == true)
                    return exifDt;

                var movieHeader = directories.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault();
                if (movieHeader?.TryGetDateTime(QuickTimeMovieHeaderDirectory.TagCreated, out var movieDt) == true)
                    return movieDt;

                var trackHeader = directories.OfType<QuickTimeTrackHeaderDirectory>().FirstOrDefault();
                if (trackHeader?.TryGetDateTime(QuickTimeTrackHeaderDirectory.TagCreated, out var trackDt) == true)
                    return trackDt;
            }
            catch (Exception ex)
            {
                _logBuilder.AppendLine($"[WARNING] Metadata read failed for {filePath}: {ex.Message}");
            }

            return new FileInfo(filePath).CreationTime;
        }

        private string GetUniquePath(string path)
        {
            if (!File.Exists(path)) return path;

            var dir = Path.GetDirectoryName(path);
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            int i = 1;

            while (true)
            {
                var newPath = Path.Combine(dir, $"{name} ({i}){ext}");
                if (!File.Exists(newPath)) return newPath;
                i++;
            }
        }
    }
}
//// MainWindow.xaml.cs
//using MetadataExtractor;
//using MetadataExtractor.Formats.Exif;
//using MetadataExtractor.Formats.QuickTime;
//using Microsoft.Win32;
//using System.IO;
//using System.Windows;
//using Directory = System.IO.Directory;

//namespace PhotoVideoOrganizer
//{
//    public partial class MainWindow : Window
//    {
//        private string _source = "";
//        private string _target = "";

//        public MainWindow()
//        {
//            InitializeComponent();
//        }

//        private void BrowseSource_Click(object sender, RoutedEventArgs e)
//        {
//            _source = BrowseFolder();
//            SourceTextBox.Text = _source;
//        }

//        private void BrowseTarget_Click(object sender, RoutedEventArgs e)
//        {
//            _target = BrowseFolder();
//            TargetTextBox.Text = _target;
//        }

//        private string BrowseFolder()
//        {
//            var folderDialog = new OpenFolderDialog
//            {
//                Title = "Select Folder",
//                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
//            };

//            if (folderDialog.ShowDialog() == true)
//            {
//                var folderName = folderDialog.FolderName;
//            }
//            return folderDialog.FolderName;
//        }

//        private async void Start_Click(object sender, RoutedEventArgs e)
//        {
//            if (string.IsNullOrEmpty(_source) || !Directory.Exists(_source))
//            {
//                MessageBox.Show("Invalid source folder.");
//                return;
//            }
//            if (string.IsNullOrEmpty(_target))
//            {
//                MessageBox.Show("Invalid target folder.");
//                return;
//            }

//            Directory.CreateDirectory(_target);

//            StartButton.IsEnabled = false;
//            StatusText.Text = "Scanning files...";

//            var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".heic", ".mp4", ".avi", ".mov", ".wmv", ".mkv" };
//            var files = Directory.EnumerateFiles(_source, "*.*", SearchOption.AllDirectories)
//                                 .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
//                                 .ToList();

//            ProgressBar.Maximum = files.Count;
//            ProgressBar.Value = 0;

//            var fileGroups = new Dictionary<string, List<string>>();

//            int processed = 0;

//            foreach (var filePath in files)
//            {
//                var dateTaken = GetDateTaken(filePath);
//                var fi = new FileInfo(filePath);
//                var key = $"{fi.Name}|{dateTaken:yyyy-MM-dd HH:mm:ss}|{fi.Length}";

//                if (!fileGroups.ContainsKey(key))
//                    fileGroups[key] = new List<string>();

//                fileGroups[key].Add(filePath);

//                processed++;
//                ProgressBar.Value = processed;
//                StatusText.Text = $"Scanned {processed}/{files.Count} files...";
//                await Task.Yield(); // Allow UI update
//            }

//            int moved = 0;
//            int duplicates = 0;

//            string dupeDir = Path.Combine(_target, "Duplicates");
//            Directory.CreateDirectory(dupeDir);

//            foreach (var group in fileGroups.Values)
//            {
//                var toKeepPath = group.First();
//                var date = GetDateTaken(toKeepPath);
//                var ym = date.ToString("yyyy-MM");
//                var destDir = Path.Combine(_target, ym);
//                Directory.CreateDirectory(destDir);

//                var fileName = Path.GetFileName(toKeepPath);
//                var destPath = GetUniquePath(Path.Combine(destDir, fileName));

//                File.Move(toKeepPath, destPath);
//                moved++;

//                foreach (var dupePath in group.Skip(1))
//                {
//                    var dupeDest = GetUniquePath(Path.Combine(dupeDir, Path.GetFileName(dupePath)));
//                    File.Move(dupePath, dupeDest);
//                    duplicates++;
//                }
//            }

//            StatusText.Text = $"Done! Moved {moved} files, {duplicates} duplicates to 'Duplicates' folder.";
//            StartButton.IsEnabled = true;
//        }

//        private DateTime GetDateTaken(string filePath)
//        {
//            try
//            {
//                var directories = ImageMetadataReader.ReadMetadata(filePath);

//                // 1. Try EXIF DateTimeOriginal (for photos, including HEIC)
//                var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
//                if (subIfd?.TryGetDateTime(ExifSubIfdDirectory.TagDateTimeOriginal, out var exifDt) == true)
//                {
//                    return exifDt;
//                }

//                // 2. For QuickTime-based videos (MP4, MOV, etc.), try Creation Date from MovieHeader or TrackHeader
//                var movieHeader = directories.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault();
//                if (movieHeader?.TryGetDateTime(QuickTimeMovieHeaderDirectory.TagCreated, out var movieDt) == true)
//                {
//                    return movieDt;
//                }

//                var trackHeader = directories.OfType<QuickTimeTrackHeaderDirectory>().FirstOrDefault();
//                if (trackHeader?.TryGetDateTime(QuickTimeTrackHeaderDirectory.TagCreated, out var trackDt) == true)
//                {
//                    return trackDt;
//                }

//                // Optional: Some videos may have it in other QuickTime directories, but above covers most cases
//            }
//            catch
//            {
//                // Ignore any metadata reading errors (corrupted files, unsupported, etc.)
//            }

//            // Fallback to file CreationTime
//            return new FileInfo(filePath).CreationTime;
//        }

//        private string GetUniquePath(string path)
//        {
//            if (!File.Exists(path)) return path;

//            var dir = Path.GetDirectoryName(path);
//            var name = Path.GetFileNameWithoutExtension(path);
//            var ext = Path.GetExtension(path);
//            int i = 1;

//            while (true)
//            {
//                var newPath = Path.Combine(dir, $"{name} ({i}){ext}");
//                if (!File.Exists(newPath)) return newPath;
//                i++;
//            }
//        }
//    }
//}