using System;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Xml.Linq;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using ColorPicker;
using System.Windows.Media.Imaging;
using System.Threading;
using Newtonsoft.Json;
using Ookii.Dialogs.Wpf;
using System.Collections.Concurrent;
using System.Globalization;
using System.Xml;
using HomeTools.AFS;
using HomeTools.BARFramework;
using HomeTools.ChannelID;
using HomeTools.UnBAR;
using CustomLogger;
using System.IO.Compression;
using System.Net.Http;
using System.Net;
using Microsoft.Web.WebView2.Core;
using System.Net.NetworkInformation;
using Newtonsoft.Json.Linq;
using System.Data;
using System.Data.SQLite;
using System.Buffers;
using CsvHelper;
using System.Collections.ObjectModel;
using HomeTools.Crypto;
using HomeTools.CDS;
using Nautilus2027.Formats;

namespace Nautilus2027
{
    // "Media" tab.
    public partial class MediaTab : UserControl
    {
        private static MainWindow Main => MainWindow.Instance;

        public MediaTab()
        {
            InitializeComponent();
        }

        private async Task ProcessVideoFiles(string[] videoFiles, CancellationToken cancellationToken)
        {
            VideoTextBox.Clear();
            int fileCount = 0;
            await Task.Run(() =>
            {
                foreach (string file in videoFiles)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    if (File.Exists(file) &&
                        (file.EndsWith(".mp4") || file.EndsWith(".avi") || file.EndsWith(".mov") ||
                         file.EndsWith(".mpeg") || file.EndsWith(".mpg") || file.EndsWith(".mkv") ||
                         file.EndsWith(".wmv") || file.EndsWith(".flv") || file.EndsWith(".webm") ||
                         file.EndsWith(".ogv") || file.EndsWith(".3gp"))) // Add other video formats as needed

                    {
                        Dispatcher.Invoke(() => LogBox.Append(VideoTextBox, $"Video file: {file}{Environment.NewLine}", log: true));
                        fileCount++;
                    }
                }
            });

            Dispatcher.Invoke(() =>
                TemporaryMessageHelper.ShowTemporaryMessage(VideoDragAreaText, $"{fileCount} video files processed.", 1000)
            );
        }

        internal async void VideoBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Video files (*.mp4; *.avi; *.mov; *.mpeg; *.mpg; *.mkv; *.wmv; *.flv; *.webm; *.ogv; *.3gp)|*.mp4;*.avi;*.mov;*.mpeg;*.mpg;*.mkv;*.wmv;*.flv;*.webm;*.ogv;*.3gp|All files (*.*)|*.*",

                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var selectedFiles = openFileDialog.FileNames;
                Main.cancellationTokenSource?.Cancel();
                Main.cancellationTokenSource = new CancellationTokenSource();
                await ProcessVideoFiles(selectedFiles, Main.cancellationTokenSource.Token);
            }
        }


        private void ClearVideoButton_Click(object sender, RoutedEventArgs e)
        {
            Main.cancellationTokenSource?.Cancel();
            VideoTextBox.Clear();
            Dispatcher.Invoke(() =>
                TemporaryMessageHelper.ShowTemporaryMessage(VideoDragAreaText, "Video list cleared", 1000)
            );
        }

        private async void ConvertVideoButton_Click(object sender, RoutedEventArgs e)
        {
            // Gather all input from UI
            string videoUrls = VideoYoutubeURLTextBox.Text.Trim();
            string localVideoEntries = VideoTextBox.Text.Trim(); // This should contain lines that may start with "Video file:"
            string outputDirectory = Main._settings.VideoOutputDirectory.Trim();

            // Ensure there's meaningful input
            if (string.IsNullOrEmpty(videoUrls) && string.IsNullOrEmpty(localVideoEntries))
            {
                Dispatcher.Invoke(() => TemporaryMessageHelper.ShowTemporaryMessage(VideoDragAreaText, "Please enter a video URL or check for local video paths.", 2000));
                return;
            }

            // Prepare the output directory
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            // Define paths to dependencies
            string dependenciesPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "dependencies");
            string ytDlpPath = Path.Combine(dependenciesPath, "yt-dlp.exe");
            string ffmpegPath = Path.Combine(dependenciesPath, "ffmpeg.exe");

            // Check for required dependencies
            await EnsureDependencies(ytDlpPath, ffmpegPath);

            // Process local video files detected in the VideoTextBox
            var localVideoPaths = localVideoEntries.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Where(line => line.StartsWith("Video file:"))
                                        .Select(line => line.Replace("Video file:", "").Trim());

            foreach (string localPath in localVideoPaths)
            {
                await ConvertLocalVideoFile(localPath, outputDirectory);
            }

            // Process each URL for download and conversion
            var urls = videoUrls.Split(new[] { ' ', ',', '\n', '\r', '|' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var url in urls)
            {
                await DownloadVideo(url, outputDirectory, ytDlpPath);
            }
        }

        private async Task ConvertLocalVideoFile(string filePath, string outputDirectory)
        {
            var (selectedResolution, selectedBitrate) = GetResolutionAndBitrate();
            string exeLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string ffmpegPath = Path.Combine(Path.GetDirectoryName(exeLocation), "dependencies\\ffmpeg.exe");

            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string outputFilePath = $"{outputDirectory}\\{fileNameWithoutExtension}_{selectedResolution.Replace(':', 'x')}.mp4";

            string audioBitrate = "160k"; // Default audio bitrate

            if (Audio160kRadioButton.IsChecked == true)
                audioBitrate = "160k";
            else if (Audio256kRadioButton.IsChecked == true)
                audioBitrate = "256k";
            else if (Audio320kRadioButton.IsChecked == true)
                audioBitrate = "320k";

            int audioBoost = 0;
            if (VideoAudioBoost3CheckBox.IsChecked == true) audioBoost += 3;
            if (VideoAudioBoost6CheckBox.IsChecked == true) audioBoost += 6;
            string audioFilter = audioBoost > 0 ? $",volume={audioBoost}dB" : "";

            // Set video codec and additional arguments for MPEG-4
            string videoCodec = "mpeg4";
            string additionalArgs = "";
            string aspectRatio = AspectRatioFourThreeCheckBox.IsChecked ?? false ? "4/3" : "16/9";

            string ffmpegArguments = $"-i \"{filePath}\" " +
                $"-c:v {videoCodec} {additionalArgs} -b:v {selectedBitrate} -minrate {selectedBitrate} -maxrate {selectedBitrate} -bufsize {int.Parse(selectedBitrate.Replace("k", "")) * 2}k " +
                $"-vf setdar={aspectRatio} -s {selectedResolution} -r 29.970 " +
                $"-c:a aac -b:a {audioBitrate} -ar 48000 -ac 2 " +
                $"-af \"loudnorm=I=-23:LRA=7:tp=-2{audioFilter}\" " +
                $"-movflags +faststart " +
                $"-metadata:s:a title=\"Stereo\" " +
                $"\"{outputFilePath}\"";

            ProcessStartInfo ffmpegStartInfo = new ProcessStartInfo()
            {
                FileName = ffmpegPath,
                Arguments = ffmpegArguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (Process ffmpegProcess = Process.Start(ffmpegStartInfo))
            {
                ffmpegProcess.OutputDataReceived += (s, args) =>
                {
                    if (args.Data != null)
                    {
                        Dispatcher.Invoke(() => LogBox.Append(VideoTextBox, args.Data + Environment.NewLine, log: true));
                    }
                };
                ffmpegProcess.BeginOutputReadLine();
                ffmpegProcess.ErrorDataReceived += (s, args) =>
                {
                    if (args.Data != null)
                        Dispatcher.Invoke(() => LogBox.Append(VideoTextBox, "Conversion: " + args.Data + Environment.NewLine, log: true));
                };
                ffmpegProcess.BeginErrorReadLine();

                await ffmpegProcess.WaitForExitAsync();
                if (ffmpegProcess.ExitCode == 0)
                {
                    Dispatcher.Invoke(() => LogBox.Append(VideoTextBox, "Conversion successful. Converted video located at: " + outputFilePath + Environment.NewLine, log: true));
                    File.Delete(filePath); // Optionally delete the original file after successful conversion
                }
                else
                {
                    Dispatcher.Invoke(() => TemporaryMessageHelper.ShowTemporaryMessage(VideoDragAreaText, "Conversion failed, check output for details.", 2000));
                }
            }
        }




        private (string resolution, string bitrate) GetResolutionAndBitrate()
        {
            // Check if the 4:3 aspect ratio option is selected
            bool isFourThree = AspectRatioFourThreeCheckBox.IsChecked == true;

            if (Video360pRadioButton.IsChecked == true)
                return isFourThree ? ("480:360", "1800k") : ("640:360", "1800k");
            else if (Video480pRadioButton.IsChecked == true)
                return isFourThree ? ("640:480", "2500k") : ("854:480", "2500k");
            else if (Video576pRadioButton.IsChecked == true)
                return isFourThree ? ("768:576", "3500k") : ("1024:576", "3500k");
            else if (Video720pRadioButton.IsChecked == true)
                return isFourThree ? ("960:720", "4500k") : ("1280:720", "4500k");
            else if (Video1080pRadioButton.IsChecked == true)
                return isFourThree ? ("1440:1080", "6000k") : ("1920:1080", "7000k");
            else
                return isFourThree ? ("768:576", "2500k") : ("1024:576", "2500k"); // Default case if none is selected
        }




        private async Task EnsureDependencies(string ytDlpPath, string ffmpegPath)
        {
            // Ensure yt-dlp is downloaded
            if (!File.Exists(ytDlpPath))
            {
                if (MessageBox.Show("yt-dlp.exe was not found. You can manually add it to the dependencies folder and try again, OR Would you like to download it from https://github.com/yt-dlp/yt-dlp/releases/ now?", "Download yt-dlp", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    await DownloadFileAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", ytDlpPath);
                }
                else
                {
                    throw new InvalidOperationException("yt-dlp is required to proceed.");
                }
            }

            // Ensure ffmpeg is downloaded
            if (!File.Exists(ffmpegPath))
            {
                if (MessageBox.Show("ffmpeg.exe was not found. You can manually add it to the dependencies folder and try again, OR Would you like to download it from https://github.com/yt-dlp/FFmpeg-Builds/releases/ now?", "Download ffmpeg", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    await DownloadAndExtractFfmpeg(Path.GetDirectoryName(ffmpegPath), ffmpegPath);
                }
                else
                {
                    throw new InvalidOperationException("ffmpeg is required to proceed.");
                }
            }
        }


        private async Task DownloadFileAsync(string url, string path)
        {
            Dispatcher.Invoke(() => LogBox.Append(VideoTextBox, $"Starting download from {url}...\n", log: true));
            using (var client = new WebClient())
            {
                await client.DownloadFileTaskAsync(new Uri(url), path);
            }
            Dispatcher.Invoke(() => LogBox.Append(VideoTextBox, "Download completed.\n", log: true));
        }

        private async Task DownloadAndExtractFfmpeg(string dependenciesPath, string ffmpegPath)
        {
            string zipPath = Path.Combine(dependenciesPath, "ffmpeg.zip");
            string extractPath = Path.Combine(dependenciesPath, "ffmpeg-extract");

            // Download ffmpeg
            Dispatcher.Invoke(() => LogBox.Append(VideoTextBox, "Downloading ffmpeg...\n", log: true));
            using (var client = new WebClient())
            {
                await client.DownloadFileTaskAsync(new Uri("https://github.com/yt-dlp/FFmpeg-Builds/releases/download/autobuild-2024-06-30-14-06/ffmpeg-n7.0.1-11-g40ddddca45-win64-gpl-7.0.zip"), zipPath);
            }
            Dispatcher.Invoke(() => LogBox.Append(VideoTextBox, "ffmpeg downloaded. Extracting...\n", log: true));

            // Clean up existing extraction directory if it exists
            if (Directory.Exists(extractPath))
            {
                Directory.Delete(extractPath, true);
            }

            // Extract ffmpeg
            try
            {
                ZipFile.ExtractToDirectory(zipPath, extractPath);
                File.Move(Path.Combine(extractPath, "ffmpeg-n7.0.1-11-g40ddddca45-win64-gpl-7.0", "bin", "ffmpeg.exe"), ffmpegPath);
                Dispatcher.Invoke(() => LogBox.Append(VideoTextBox, "ffmpeg extracted successfully.\n", log: true));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => LogBox.Append(VideoTextBox, $"Error extracting ffmpeg: {ex.Message}\n", log: true));
            }

            // Cleanup
            try
            {
                Directory.Delete(extractPath, true);
                File.Delete(zipPath);
                Dispatcher.Invoke(() => LogBox.Append(VideoTextBox, "Cleanup completed.\n", log: true));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => LogBox.Append(VideoTextBox, $"Error during cleanup: {ex.Message}\n", log: true));
            }
        }


        private async Task DownloadVideo(string videoUrls, string outputDirectory, string ytDlpPath)
        {
            if (string.IsNullOrEmpty(videoUrls))
            {
                Dispatcher.Invoke(() => TemporaryMessageHelper.ShowTemporaryMessage(VideoDragAreaText, "Please enter a video URL.", 2000));
                return;
            }

            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            var urls = videoUrls.Split(new[] { ' ', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var videoUrl in urls)
            {
                string downloadArguments = $"-f bestvideo+bestaudio/best {videoUrl} -o \"{outputDirectory}\\%(title)s.%(ext)s\"";
                ProcessStartInfo startInfo = new ProcessStartInfo()
                {
                    FileName = ytDlpPath,
                    Arguments = downloadArguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(startInfo))
                {
                    process.OutputDataReceived += (s, args) =>
                    {
                        if (args.Data != null)
                        {
                            Dispatcher.Invoke(() => LogBox.Append(VideoTextBox, args.Data + Environment.NewLine, log: true));
                        }
                    };
                    process.BeginOutputReadLine();

                    process.ErrorDataReceived += (s, args) =>
                    {
                        if (args.Data != null)
                        {
                            Dispatcher.Invoke(() => LogBox.Append(VideoTextBox, "Error: " + args.Data + Environment.NewLine, log: true));
                        }
                    };
                    process.BeginErrorReadLine();

                    await process.WaitForExitAsync();

                    if (process.ExitCode == 0)
                    {
                        Dispatcher.Invoke(() => TemporaryMessageHelper.ShowTemporaryMessage(VideoDragAreaText, "Download successful.", 2000));
                        await ScanAndConvertWebmFiles(outputDirectory); // Scan and convert any webm files after successful download
                    }
                    else
                    {
                        Dispatcher.Invoke(() => TemporaryMessageHelper.ShowTemporaryMessage(VideoDragAreaText, "Download may have failed, check the output for details.", 2000));
                    }
                }
            }
        }


        private async Task ScanAndConvertWebmFiles(string directory)
        {
            var webmFiles = Directory.GetFiles(directory, "*.webm");
            foreach (var file in webmFiles)
            {
                await ConvertLocalVideoFile(file, directory);
                File.Delete(file); // Delete the webm file after conversion
            }
        }


        // BNKUnpacker Handlers
        private static bool IsBnkItem(string f) =>
            f.EndsWith(".bnk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".bnk.json", StringComparison.OrdinalIgnoreCase);

        /// <summary>Adds banks / unpacked .bnk.json lists (and those inside dropped folders) to the list, without repeats.</summary>
        private void AddBnkItems(IEnumerable<string> items)
        {
            var found = new List<string>();
            foreach (var item in items)
            {
                if (File.Exists(item) && IsBnkItem(item)) found.Add(item);
                else if (Directory.Exists(item))
                    try { found.AddRange(Directory.EnumerateFiles(item, "*.*", SearchOption.AllDirectories).Where(IsBnkItem).Where(f => !Path.GetFileName(f).Equals(BnkConverter.OriginalName, StringComparison.OrdinalIgnoreCase))); }
                    catch (Exception ex) { Main.LogDebugInfo($"BNK: could not read {item}: {ex.Message}"); }
            }
            var have = new HashSet<string>(BNKUnpackerTextBox.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()), StringComparer.OrdinalIgnoreCase);
            var add = found.Where(f => have.Add(f)).ToList();
            if (add.Count > 0)
            {
                string text = BNKUnpackerTextBox.Text;
                BNKUnpackerTextBox.Text = (text.Length > 0 && !text.EndsWith("\n") ? text + Environment.NewLine : text) + string.Join(Environment.NewLine, add);
                BNKUnpackerTextBox.ScrollToEnd();
                TemporaryMessageHelper.ShowTemporaryMessage(BNKUnpackerDragAreaText, $"{add.Count} items added", 2000);
                Main.LogDebugInfo($"BNK: {add.Count} item(s) added");
            }
            else
                TemporaryMessageHelper.ShowTemporaryMessage(BNKUnpackerDragAreaText, found.Count > 0 ? "Already listed" : "No BNK or .bnk.json files found", 2000);
        }

        private void BNKUnpackerDragDropHandler(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                AddBnkItems((string[])e.Data.GetData(DataFormats.FileDrop));
        }

        private void ClickToBrowseHandlerBNKUnpacker(object sender, MouseButtonEventArgs e)
        {
            var openFileDialog = new VistaOpenFileDialog
            {
                Filter = "Sound banks and unpacked lists (*.bnk;*.bnk.json)|*.bnk;*.json|BNK files (*.bnk)|*.bnk",
                Multiselect = true
            };
            if (openFileDialog.ShowDialog() == true)
                AddBnkItems(openFileDialog.FileNames);
        }

        private string BnkOutputDirectory()
        {
            string dir = Main._settings.AudioOutputDirectory;
            if (string.IsNullOrWhiteSpace(dir)) dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "Audio");
            return dir;
        }

        private async void BNKRepackButtonClick(object sender, RoutedEventArgs e)
        {
            var lists = LogBox.TakeLines(BNKUnpackerTextBox).Where(f => f.EndsWith(".bnk.json", StringComparison.OrdinalIgnoreCase) && File.Exists(f)).ToList();
            if (lists.Count == 0)
            {
                TemporaryMessageHelper.ShowTemporaryMessage(BNKUnpackerDragAreaText, "No unpacked .bnk.json lists listed", 2000);
                return;
            }
            string outDir = Path.Combine(BnkOutputDirectory(), "Repacked");
            var overwrite = Main._settings.FileOverwriteBehavior;
            Main.LogDebugInfo($"BNK Repack: {lists.Count} bank(s)");
            int ok = 0;
            foreach (var l in lists)
            {
                var lines = await Task.Run(() =>
                {
                    try { var r = BnkConverter.Pack(l, outDir, overwrite); return r; }
                    catch (Exception ex) { return new List<string> { $"-- ERROR: {Path.GetFileName(l)}: {ex.Message}" }; }
                });
                if (lines.Count > 0 && lines[0].StartsWith("-- Success")) ok++;
                LogBox.Append(BNKUnpackerTextBox, string.Join(Environment.NewLine, lines) + Environment.NewLine, log: true);
            }
            TemporaryMessageHelper.ShowTemporaryMessage(BNKUnpackerDragAreaText, $"{ok} of {lists.Count} bank(s) repacked", 3000);
        }

        private async void BNKInspectButtonClick(object sender, RoutedEventArgs e)
        {
            var items = BNKUnpackerTextBox.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim())
                                               .Where(f => LogBox.LooksLikePath(f) && IsBnkItem(f) && File.Exists(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (items.Count == 0)
            {
                TemporaryMessageHelper.ShowTemporaryMessage(BNKUnpackerDragAreaText, "No banks listed", 2000);
                return;
            }
            Main.LogDebugInfo($"BNK Inspect: {items.Count} file(s)");
            string report = await Task.Run(() =>
            {
                var sb = new StringBuilder();
                foreach (var f in items)
                    try { sb.Append(BnkConverter.Describe(f)); }
                    catch (Exception ex) { sb.AppendLine($"-- ERROR: {Path.GetFileName(f)}: {ex.Message}"); }
                return sb.ToString();
            });
            FileListTools.AppendReport(BNKUnpackerTextBox, report);
        }

        private async void BNKUnpackerExecuteButtonClick(object sender, RoutedEventArgs e)
        {
            Main.LogDebugInfo("BNK Unpacker: Process Started");

            var inputFiles = LogBox.TakeLines(BNKUnpackerTextBox).ToList();   // the list moves into memory; the box shows the log
            if (!inputFiles.Any())
            {
                TemporaryMessageHelper.ShowTemporaryMessage(BNKUnpackerDragAreaText, "No files to unpack", 2000);
                return;
            }

            string outputDirectory = Main._settings.AudioOutputDirectory;
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                TemporaryMessageHelper.ShowTemporaryMessage(BNKUnpackerDragAreaText, "Invalid output directory", 2000);
                return;
            }

            // Create the output directory if it doesn't exist
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
                TemporaryMessageHelper.ShowTemporaryMessage(BNKUnpackerDragAreaText, $"Created output directory: {outputDirectory}", 2000);
            }

            foreach (var inputFile in inputFiles)
            {
                await UnpackBNKFile(inputFile, outputDirectory);
            }

            Main.LogDebugInfo("BNK Unpacker: Process Completed");
            TemporaryMessageHelper.ShowTemporaryMessage(BNKUnpackerDragAreaText, "Unpacking completed", 2000);
        }

        private void ClearBNKUnpackerListHandler(object sender, RoutedEventArgs e)
        {
            BNKUnpackerTextBox.Clear();
            TemporaryMessageHelper.ShowTemporaryMessage(BNKUnpackerDragAreaText, "List cleared", 2000);
        }

        private void BNKUnpackerTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            string outputDirectory = Main._settings.AudioOutputDirectory;

            if (!string.IsNullOrEmpty(outputDirectory) && Directory.Exists(outputDirectory))
            {
                try
                {
                    // Open the directory in File Explorer
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                    {
                        FileName = outputDirectory,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                    Main.LogDebugInfo($"BNK Unpacker: Opened output directory {outputDirectory}");
                }
                catch (Exception ex)
                {
                    // Handle any exceptions
                    Main.LogDebugInfo($"BNK Unpacker: ERROR: Unable to open directory - {ex.Message}");
                    MessageBox.Show($"Unable to open directory: {ex.Message}");
                }
            }
            else
            {
                Main.LogDebugInfo($"BNK Unpacker: ERROR: Directory {outputDirectory} is not set or does not exist.");
                MessageBox.Show("Output directory is not set or does not exist.");
            }
        }

        /// <summary>Unpacks one bank natively (Formats/BnkConverter.cs) into the audio output folder.</summary>
        private async Task UnpackBNKFile(string inputFile, string outputDirectory)
        {
            if (!inputFile.EndsWith(".bnk", StringComparison.OrdinalIgnoreCase)) return;
            var lines = await Task.Run(() =>
            {
                try { return BnkConverter.Unpack(inputFile, outputDirectory); }
                catch (Exception ex) { return new List<string> { $"-- ERROR: {Path.GetFileName(inputFile)}: {ex.Message}" }; }
            });
            AppendTextToBNKUnpackerTextBox(string.Join(Environment.NewLine, lines));
        }

        private void AppendTextToBNKUnpackerTextBox(string text)
        {
            // If called from a non-UI thread, use Dispatcher
            Dispatcher.Invoke(() =>
            {
                LogBox.Append(BNKUnpackerTextBox, text + Environment.NewLine, log: true);

                // Scrolls the text box to the end after appending text
                BNKUnpackerTextBox.ScrollToEnd();
            });
        }
    }
}
