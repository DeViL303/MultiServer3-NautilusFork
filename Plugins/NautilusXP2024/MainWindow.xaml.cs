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
using HomeTools.Crypto;
using HomeTools.UnBAR;
using CyberBackendLibrary.HTTP;
using WebAPIService.CDS;
using CustomLogger;
using HttpMultipartParser;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CompressionLibrary.Custom;
using CyberBackendLibrary.DataTypes;
using System.IO;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using System.Net;


namespace NautilusXP2024
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private AppSettings _settings;

        private SolidColorBrush _selectedThemeColor;

        private Dictionary<int, string> uuidMappings;

        public SolidColorBrush SelectedThemeColor
        {
            get { return _selectedThemeColor; }
            set
            {
                if (_selectedThemeColor != value)
                {
                    _selectedThemeColor = value;
                    OnPropertyChanged(nameof(SelectedThemeColor));
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();


            LoggerAccessor.SetupLogger("NautilusXP2024");

            DataContext = this;
            // Load settings when the window initializes
            _settings = SettingsManager.LoadSettings();

            // Convert the theme color from settings to a Color object
            Color themeColor = (Color)ColorConverter.ConvertFromString(_settings.ThemeColor);

            // Convert the theme color to a SolidColorBrush
            _selectedThemeColor = new SolidColorBrush(themeColor);

            // Apply the theme color to the UI
            ApplySettingsToUI();

            // Update the taskbar icon with the theme color from settings
            UpdateTaskbarIconWithTint(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies\\icon.png"), themeColor);

            AFSClass.MapperHelperFolder = Directory.GetCurrentDirectory();

            _ = new Timer(AFSClass.ScheduledUpdate, null, TimeSpan.Zero, TimeSpan.FromMinutes(1440));

            logFlushTimer = new System.Threading.Timer(_ => FlushLogBuffer(), null, Timeout.Infinite, Timeout.Infinite);

        }

        public Color ThemeColor
        {
            get => (Color)ColorConverter.ConvertFromString(_settings.ThemeColor);
            set
            {
                if (_settings.ThemeColor != value.ToString())
                {
                    _settings.ThemeColor = value.ToString();
                    SettingsManager.SaveSettings(_settings);
                    OnPropertyChanged(nameof(ThemeColor));
                }
            }
        }


        private void ApplySettingsToUI()
        {
            // Applying settings to UI elements
            CdsOutputDirectoryTextBox.Text = _settings.CdsOutputDirectory;
            BarSdatSharcOutputDirectoryTextBox.Text = _settings.BarSdatSharcOutputDirectory;
            MappedOutputDirectoryTextBox.Text = _settings.MappedOutputDirectory;
            HcdbOutputDirectoryTextBox.Text = _settings.HcdbOutputDirectory;
            sqlOutputDirectoryTextBox.Text = _settings.SqlOutputDirectory;
            TicketListOutputDirectoryTextBox.Text = _settings.TicketListOutputDirectory;
            LUACOutputDirectoryTextBox.Text = _settings.LuacOutputDirectory;
            LUAOutputDirectoryTextBox.Text = _settings.LuaOutputDirectory;
            InfToolOutputDirectoryTextBox.Text = _settings.InfToolOutputDirectory;
            CacheOutputDirectoryTextBox.Text = _settings.CacheOutputDirectory;
            VideoOutputDirectoryTextBox.Text = _settings.VideoOutputDirectory;
            cpuPercentageTextBox.Text = _settings.CpuPercentage.ToString();
            MappingThreadsTextbox.Text = _settings.MappingThreads.ToString();
            ThemeColorPicker.SelectedColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_settings.ThemeColor);
            switch (_settings.FileOverwriteBehavior)
            {
                case OverwriteBehavior.Skip:
                    RadioButtonFileSkip.IsChecked = true;
                    break;
                case OverwriteBehavior.Rename:
                    RadioButtonFileRename.IsChecked = true;
                    break;
                case OverwriteBehavior.Overwrite:
                    RadioButtonFileOverwrite.IsChecked = true;
                    break;
            };
            switch (_settings.ArchiveTypeSettingRem)
            {
                case ArchiveTypeSetting.BAR:
                    RadioButtonArchiveCreatorBAR.IsChecked = true;
                    break;
                case ArchiveTypeSetting.BAR_S:
                    RadioButtonArchiveCreatorBAR_S.IsChecked = true;
                    break;
                case ArchiveTypeSetting.SDAT:
                    RadioButtonArchiveCreatorSDAT.IsChecked = true;
                    break;
                case ArchiveTypeSetting.SDAT_SHARC:
                    RadioButtonArchiveCreatorSDAT_SHARC.IsChecked = true;
                    break;
                case ArchiveTypeSetting.CORE_SHARC:
                    RadioButtonArchiveCreatorCORE_SHARC.IsChecked = true;
                    break;
                // Added setting for creating config sharcs
                case ArchiveTypeSetting.CONFIG_SHARC:
                    RadioButtonArchiveCreatorCONFIG_SHARC.IsChecked = true;
                    break;

            };
            switch (_settings.ArchiveMapperSettingRem)
            {
                case ArchiveMapperSetting.NORM:
                    CheckBoxArchiveMapperFAST.IsChecked = false;

                    break;
                case ArchiveMapperSetting.FAST:
                    CheckBoxArchiveMapperFAST.IsChecked = true;
                    break;
                case ArchiveMapperSetting.EXP:
                    CheckBoxArchiveMapperEXP.IsChecked = true;
                    break;

            };
            switch (_settings.SaveDebugLogToggle)
            {
                case SaveDebugLog.True:
                    ToggleSwitchDebugLogEnable.IsChecked = true;
                    break;
                case SaveDebugLog.False:
                    ToggleSwitchDebugLogEnable.IsChecked = false;
                    break;
            }

            SelectLastUsedTab(_settings.LastTabUsed);
        }

        public static Dictionary<string, string> SceneFileMappings { get; private set; } = new Dictionary<string, string>();

        private void CdsOutputDirectoryTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.CdsOutputDirectory = CdsOutputDirectoryTextBox.Text;
                SettingsManager.SaveSettings(_settings);
            }
        }


        private void BarSdatSharcOutputDirectoryTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null && BarSdatSharcOutputDirectoryTextBox != null)
            {
                _settings.BarSdatSharcOutputDirectory = BarSdatSharcOutputDirectoryTextBox.Text;
                SettingsManager.SaveSettings(_settings);
            }
        }

        private void MappedOutputDirectoryTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null && MappedOutputDirectoryTextBox != null)
            {
                _settings.MappedOutputDirectory = MappedOutputDirectoryTextBox.Text;
                SettingsManager.SaveSettings(_settings);
            }
        }

        private void HcdbOutputDirectoryTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null && HcdbOutputDirectoryTextBox != null)
            {
                _settings.HcdbOutputDirectory = HcdbOutputDirectoryTextBox.Text;
                SettingsManager.SaveSettings(_settings);
            }
        }

        private void SqlOutputDirectoryTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null && sqlOutputDirectoryTextBox != null)
            {
                _settings.SqlOutputDirectory = sqlOutputDirectoryTextBox.Text;
                SettingsManager.SaveSettings(_settings);
            }
        }

        private void TicketListOutputDirectoryTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null && TicketListOutputDirectoryTextBox != null)
            {
                _settings.TicketListOutputDirectory = TicketListOutputDirectoryTextBox.Text;
                SettingsManager.SaveSettings(_settings);
            }
        }

        private void LuacOutputDirectoryTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null && LUACOutputDirectoryTextBox != null)
            {
                _settings.LuacOutputDirectory = LUACOutputDirectoryTextBox.Text;
                SettingsManager.SaveSettings(_settings);
            }
        }

        private void LuaOutputDirectoryTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null && LUAOutputDirectoryTextBox != null)
            {
                _settings.LuaOutputDirectory = LUAOutputDirectoryTextBox.Text;
                SettingsManager.SaveSettings(_settings);
            }
        }

        private void InfToolOutputDirectoryTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null && InfToolOutputDirectoryTextBox != null)
            {
                _settings.InfToolOutputDirectory = InfToolOutputDirectoryTextBox.Text;
                SettingsManager.SaveSettings(_settings);
            }
        }

        private void CacheOutputDirectoryTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null && CacheOutputDirectoryTextBox != null)
            {
                _settings.CacheOutputDirectory = CacheOutputDirectoryTextBox.Text;
                SettingsManager.SaveSettings(_settings);
            }
        }
        private void VideoOutputDirectoryTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null && VideoOutputDirectoryTextBox != null)
            {
                _settings.VideoOutputDirectory = VideoOutputDirectoryTextBox.Text;
                SettingsManager.SaveSettings(_settings);
            }
        }

        private void RadioButtonFileSkip_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.FileOverwriteBehavior = OverwriteBehavior.Skip;
                SettingsManager.SaveSettings(_settings);
            }
        }

        private void RadioButtonFileRename_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.FileOverwriteBehavior = OverwriteBehavior.Rename;
                SettingsManager.SaveSettings(_settings);
            }
        }

        private void RadioButtonFileOverwrite_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.FileOverwriteBehavior = OverwriteBehavior.Overwrite;
                SettingsManager.SaveSettings(_settings);
            }
        }

        private void CheckBoxDebugLogsEnable_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.SaveDebugLogToggle = SaveDebugLog.True;
                SettingsManager.SaveSettings(_settings);
            }
        }

        private void CheckBoxDebugLogsEnable_UnChecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.SaveDebugLogToggle = SaveDebugLog.False;
                SettingsManager.SaveSettings(_settings);
            }
        }

        private void RadioButtonArchiveCreatorBAR_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.ArchiveTypeSettingRem = ArchiveTypeSetting.BAR;
                SettingsManager.SaveSettings(_settings);
            }
        }


        private void RadioButtonArchiveCreatorBAR_S_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.ArchiveTypeSettingRem = ArchiveTypeSetting.BAR_S;
                SettingsManager.SaveSettings(_settings);
            }
        }

        private void RadioButtonArchiveCreatorSDAT_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.ArchiveTypeSettingRem = ArchiveTypeSetting.SDAT;
                SettingsManager.SaveSettings(_settings);
            }
        }

        private void RadioButtonArchiveCreatorSDAT_SHARC_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.ArchiveTypeSettingRem = ArchiveTypeSetting.SDAT_SHARC;
                SettingsManager.SaveSettings(_settings);
            }
        }

        private void RadioButtonArchiveCreatorCORE_SHARC_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.ArchiveTypeSettingRem = ArchiveTypeSetting.CORE_SHARC;
                SettingsManager.SaveSettings(_settings);
            }
        }


        private void RadioButtonArchiveCreatorCONFIG_SHARC_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.ArchiveTypeSettingRem = ArchiveTypeSetting.CONFIG_SHARC;
                SettingsManager.SaveSettings(_settings);
            }
        }

        private List<string> logBuffer = new List<string>();
        private string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs\\debug.log");
        private System.Threading.Timer logFlushTimer;
        private readonly object logLock = new object();

        private void FlushLogBuffer()
        {
            lock (logLock)
            {
                if (logBuffer.Count > 0)
                {
                    try
                    {
                        using (StreamWriter sw = new StreamWriter(logFilePath, true)) // true to append data to the file
                        {
                            foreach (var message in logBuffer)
                            {
                                sw.WriteLine(message);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Error writing to log file: " + ex.Message);
                    }
                    logBuffer.Clear();
                }
                logFlushTimer.Change(Timeout.Infinite, Timeout.Infinite); // Stop the timer
            }
        }

        public void LogDebugInfo(string message)
        {
            if (_settings.SaveDebugLogToggle == SaveDebugLog.True)
            {
                lock (logLock)
                {
                    logBuffer.Add($"{message}");

                    if (logBuffer.Count >= 100) // Flush every 10 messages, adjust as needed
                    {
                        FlushLogBuffer();
                    }
                    else
                    {
                        logFlushTimer.Change(1000, Timeout.Infinite); // Reset the timer to 1 second
                    }
                }
            }
            // If SaveDebugLogToggle is False, do nothing
        }


        // Title BAR Controls

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = System.Windows.WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {

        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                this.DragMove();
        }

        // Theme Controls

        private void ThemeColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e)
        {
            if (e.NewValue.HasValue)
            {
                SelectedThemeColor = new SolidColorBrush(e.NewValue.Value);
                _settings.ThemeColor = e.NewValue.Value.ToString();
                SettingsManager.SaveSettings(_settings);
            }
        }

        private void NewColorPicker_ColorChanged(object sender, EventArgs e)
        {
            var colorPicker = sender as StandardColorPicker;
            if (colorPicker != null)
            {
                // Directly use SelectedColor as it is not nullable
                Color newColor = colorPicker.SelectedColor;
                SelectedThemeColor = new SolidColorBrush(newColor);
                _settings.ThemeColor = newColor.ToString();
                SettingsManager.SaveSettings(_settings);

                // Assuming your icon is a resource in the project, otherwise provide a full path
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies\\icon.png");
                UpdateTaskbarIconWithTint(iconPath, newColor);
            }
        }


        private void UpdateTaskbarIconWithTint(string iconPath, System.Windows.Media.Color tint)
        {
            // Load the existing icon
            BitmapImage originalIcon = new BitmapImage(new Uri(iconPath));

            // Create a WriteableBitmap from the original icon
            WriteableBitmap writeableBitmap = new WriteableBitmap(originalIcon);

            // Iterate over all pixels to apply the tint only on non-transparent pixels
            writeableBitmap.Lock();
            unsafe
            {
                // Pointer to the back buffer
                IntPtr buffer = writeableBitmap.BackBuffer;
                int stride = writeableBitmap.BackBufferStride;

                // Tint strength (0-255)
                byte tintStrength = 255; // Maximum strength

                for (int y = 0; y < writeableBitmap.PixelHeight; y++)
                {
                    for (int x = 0; x < writeableBitmap.PixelWidth; x++)
                    {
                        // Calculate the pixel's position
                        int position = y * stride + x * 4;

                        // Apply tint only to non-transparent pixels
                        byte* pixel = (byte*)buffer.ToPointer() + position;
                        byte alpha = pixel[3];
                        if (alpha > 0) // This checks if the pixel is not fully transparent
                        {
                            pixel[0] = (byte)((pixel[0] * (255 - tintStrength) + tint.B * tintStrength) / 255);
                            pixel[1] = (byte)((pixel[1] * (255 - tintStrength) + tint.G * tintStrength) / 255);
                            pixel[2] = (byte)((pixel[2] * (255 - tintStrength) + tint.R * tintStrength) / 255);
                        }
                    }
                }
            }
            writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, writeableBitmap.PixelWidth, writeableBitmap.PixelHeight));
            writeableBitmap.Unlock();

            // Use the WriteableBitmap as the window's icon
            this.Icon = writeableBitmap;
        }

        private ImageSource BitmapToImageSource(RenderTargetBitmap bitmapSource)
        {
            // Convert the bitmap source to a PNG byte array
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            MemoryStream stream = new MemoryStream();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            encoder.Save(stream);
            stream.Position = 0;

            // Create a new BitmapImage from the PNG byte array
            BitmapImage bImg = new BitmapImage();
            bImg.BeginInit();
            bImg.StreamSource = stream;
            bImg.EndInit();

            return bImg;
        }



        private void CpuPercentageTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null && int.TryParse(cpuPercentageTextBox.Text, out int cpuPercentage))
            {
                _settings.CpuPercentage = cpuPercentage;
                SettingsManager.SaveSettings(_settings);
            }
        }

        private void MappingThreadsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null && int.TryParse(MappingThreadsTextbox.Text, out int MappingThreads))
            {
                _settings.MappingThreads = MappingThreads;
                SettingsManager.SaveSettings(_settings);
            }
        }


        private void ClearListHandler(object sender, RoutedEventArgs e)
        {
            var textBox = GetAssociatedTextBox(sender as FrameworkElement);
            if (textBox != null)
            {
                textBox.Clear();

                // Derive the TextBlock name from the TextBox name
                string textBlockName = textBox.Name.Replace("TextBox", "DragAreaText");

                // Find the TextBlock in the current context
                var textBlock = this.FindName(textBlockName) as TextBlock;
                if (textBlock != null)
                {
                    // Show the temporary message
                    TemporaryMessageHelper.ShowTemporaryMessage(textBlock, "List Cleared", 500); // 1000 milliseconds = 1 second
                }
            }
        }

        private TextBox? GetAssociatedTextBox(FrameworkElement? element)
        {
            if (element == null) return null;
            var baseName = element.Name;
            var suffixes = new string[] { "DragArea", "ClearButton" };
            foreach (var suffix in suffixes)
            {
                if (baseName.EndsWith(suffix))
                {
                    baseName = baseName.Substring(0, baseName.Length - suffix.Length);
                    break;
                }
            }

            var textBoxName = baseName + "TextBox";
            var textBox = this.FindName(textBoxName) as TextBox;

            if (textBox == null)
            {
                throw new InvalidOperationException($"A TextBox with name '{textBoxName}' could not be found.");
            }

            return textBox;
        }

        public static class TemporaryMessageHelper
        {
            private static Dictionary<TextBlock, (DispatcherTimer Timer, string OriginalText)> messageTimers = new Dictionary<TextBlock, (DispatcherTimer, string)>();

            public static void ShowTemporaryMessage(TextBlock textBlock, string message, int displayTimeInMilliseconds)
            {
                if (messageTimers.ContainsKey(textBlock))
                {
                    var (timer, _) = messageTimers[textBlock];
                    timer.Stop();  // Stop the existing timer if it's running
                }
                else
                {
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(displayTimeInMilliseconds) };
                    timer.Tick += (sender, args) =>
                    {
                        timer.Stop();
                        textBlock.Text = messageTimers[textBlock].OriginalText;
                    };
                    messageTimers[textBlock] = (timer, textBlock.Text); // Save the original text and timer
                }

                textBlock.Text = message;
                messageTimers[textBlock].Timer.Start();
            }
        }



        // TAB 1: Logic for Archive Creation

        private async void ArchiveCreatorExecuteButtonClick(object sender, RoutedEventArgs e)
        {
            LogDebugInfo("Archive Creation: Process Initiated");
            TemporaryMessageHelper.ShowTemporaryMessage(ArchiveCreatorDragAreaText, "Processing....", 1000000);

            // Check and create output directory if it doesn't exist
            if (!Directory.Exists(_settings.BarSdatSharcOutputDirectory))
            {
                Directory.CreateDirectory(_settings.BarSdatSharcOutputDirectory);
                LogDebugInfo($"Archive Creation: Output directory created at {_settings.BarSdatSharcOutputDirectory}");
            }

            string itemsToArchive = ArchiveCreatorTextBox.Text;
            if (!string.IsNullOrWhiteSpace(itemsToArchive))
            {
                string[] itemsPaths = itemsToArchive.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                LogDebugInfo($"Archive Creation: Starting creation for {itemsPaths.Length} items");
                bool archiveCreationSuccess = await CreateArchiveAsync(itemsPaths, _settings.ArchiveTypeSettingRem);

                string message = archiveCreationSuccess ? "Success: Archives Created" : "Archive Creation Failed";
                TemporaryMessageHelper.ShowTemporaryMessage(ArchiveCreatorDragAreaText, message, 2000);

                LogDebugInfo($"Archive Creation: Result - {message}");
            }
            else
            {
                LogDebugInfo("Archive Creation: Aborted - No items listed for Archive Creation.");
                TemporaryMessageHelper.ShowTemporaryMessage(ArchiveCreatorDragAreaText, "No items listed for Archive Creation.", 2000);
            }
        }



        private void ArchiveCreatorDragDropHandler(object sender, DragEventArgs e)
        {
            LogDebugInfo("Archive Creation: Drag and Drop Initiated");
            TemporaryMessageHelper.ShowTemporaryMessage(ArchiveCreatorDragAreaText, "Processing....", 1000000);

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedItems = (string[])e.Data.GetData(DataFormats.FileDrop);
                List<string> itemsToAdd = new List<string>();

                foreach (var item in droppedItems)
                {
                    LogDebugInfo($"Processing item: {item}");

                    if (Directory.Exists(item))
                    {
                        string itemWithTrailingSlash = item.EndsWith(Path.DirectorySeparatorChar.ToString()) ? item : item + Path.DirectorySeparatorChar;
                        itemsToAdd.Add(itemWithTrailingSlash);
                        LogDebugInfo($"Directory added with trailing slash: {itemWithTrailingSlash}");
                    }
                    else if (File.Exists(item) && Path.GetExtension(item).ToLowerInvariant() == ".zip")
                    {
                        itemsToAdd.Add(item);
                        LogDebugInfo($"ZIP file added: {item}");
                    }
                }

                if (itemsToAdd.Count > 0)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var existingItemsSet = new HashSet<string>(ArchiveCreatorTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingItemsSet.Count;

                        existingItemsSet.UnionWith(itemsToAdd);
                        int newItemsCount = existingItemsSet.Count - initialCount;
                        int duplicatesCount = itemsToAdd.Count - newItemsCount;

                        ArchiveCreatorTextBox.Text = string.Join(Environment.NewLine, existingItemsSet);

                        string message = $"{newItemsCount} item(s) added";
                        if (duplicatesCount > 0)
                        {
                            message += $", {duplicatesCount} duplicate(s) filtered";
                        }
                        TemporaryMessageHelper.ShowTemporaryMessage(ArchiveCreatorDragAreaText, message, 2000);

                        LogDebugInfo($"Archive Creation: {newItemsCount} items added, {duplicatesCount} duplicates filtered from Drag and Drop.");
                    });
                }
                else
                {
                    LogDebugInfo("Archive Creation: Drag and Drop - No valid ZIP files or folders found.");
                    TemporaryMessageHelper.ShowTemporaryMessage(ArchiveCreatorDragAreaText, "No valid ZIP files or folders found.", 2000);
                }
            }
            else
            {
                LogDebugInfo("Archive Creation: Drag and Drop - No Data Present.");
                TemporaryMessageHelper.ShowTemporaryMessage(ArchiveCreatorDragAreaText, "Drag and Drop operation failed - No Data Present.", 2000);
            }
        }



        private async void ClickToBrowseArchiveCreatorHandler(object sender, RoutedEventArgs e)
        {
            LogDebugInfo("Archive Creation: Browsing for files Initiated");

            // Disable the main window to prevent interactions while the dialog is open
            this.IsEnabled = false;

            var openFileDialog = new VistaOpenFileDialog
            {
                Filter = "ZIP files (*.zip)|*.zip",
                Multiselect = true
            };

            string message = "No items selected.";
            int displayTime = 2000; // Default to 2 seconds for the message display.

            bool? result = openFileDialog.ShowDialog();

            // Await a delay to absorb any unintentional clicks that happen after the dialog closes
            await Task.Delay(10); // 500 ms delay

            // Re-enable the main window after the delay
            this.IsEnabled = true;

            if (result == true)
            {
                var selectedItems = openFileDialog.FileNames;

                if (selectedItems.Any())
                {
                    LogDebugInfo($"Archive Creation: {selectedItems.Length} items selected via File Browser.");

                    Dispatcher.Invoke(() =>
                    {
                        var existingItemsSet = new HashSet<string>(ArchiveCreatorTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        existingItemsSet.UnionWith(selectedItems); // Adds selected zip files, removes duplicates
                        ArchiveCreatorTextBox.Text = string.Join(Environment.NewLine, existingItemsSet);

                        message = $"{selectedItems.Length} items added to the list";
                        displayTime = 1000; // Change display time since items were added
                    });
                }
                else
                {
                    LogDebugInfo("Archive Creation: File Browser - No ZIP files were selected.");
                    message = "No ZIP files selected.";
                }
            }
            else
            {
                LogDebugInfo("Archive Creation: File Browser - Dialog Cancelled.");
            }

            TemporaryMessageHelper.ShowTemporaryMessage(ArchiveCreatorDragAreaText, message, displayTime);
        }



        private void CheckBoxArchiveMapperFAST_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.ArchiveMapperSettingRem = ArchiveMapperSetting.FAST;
                SettingsManager.SaveSettings(_settings);
            }
            // Uncheck the CORE CheckBox if it's checked

            CheckBoxArchiveMapperEXP.IsChecked = false;
        }

        private void CheckBoxArchiveMapperFAST_Unchecked(object sender, RoutedEventArgs e)
        {

            {
                _settings.ArchiveMapperSettingRem = ArchiveMapperSetting.NORM;
                SettingsManager.SaveSettings(_settings);
            }
        }

        private void CheckBoxArchiveMapperEXP_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.ArchiveMapperSettingRem = ArchiveMapperSetting.EXP;
                SettingsManager.SaveSettings(_settings);
            }
            // Uncheck the SLOW CheckBox if it's checked
            CheckBoxArchiveMapperFAST.IsChecked = false;

        }

        private void CheckBoxArchiveMapperEXP_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null && !CheckBoxArchiveMapperFAST.IsChecked.Value)
            {
                _settings.ArchiveMapperSettingRem = ArchiveMapperSetting.NORM;
                SettingsManager.SaveSettings(_settings);
            }
        }




        // Placeholder method for the archive creation process
        private Task<bool> CreateArchiveAsync(string[] itemPaths, ArchiveTypeSetting type)
        {
            // Here you would log the start of the archive creation process
            LogDebugInfo($"Archive Creation: Beginning Archive Creation for {itemPaths.Length} items with type {type}.");

            int i = 0;

            foreach (string itemPath in itemPaths)
            {
                LogDebugInfo($"Archive Creation: Processing item {i + 1}: {itemPath}");
                if (itemPath.ToLower().EndsWith(".zip"))
                {

                    string filename = Path.GetFileNameWithoutExtension(itemPath);
                    LogDebugInfo($"Archive Creation: Processing item {i + 1}: Extracting ZIP: {filename}");

                    // Combine the temporary folder path with the unique folder name
                    string temppath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                    LogDebugInfo($"Archive Creation: Processing item {i + 1}: Temporary extraction path: {temppath}");

                    UncompressFile(itemPath, temppath);

                    bool sdat = false;
                    IEnumerable<string> enumerable = Directory.EnumerateFiles(temppath, "*.*", SearchOption.AllDirectories);
                    BARArchive? bararchive = null;

                    // Declare the fileExtension variable
                    string fileExtension = "";

                    switch (_settings.ArchiveTypeSettingRem)
                    {
                        case ArchiveTypeSetting.BAR:
                            bararchive = new BARArchive($"{_settings.BarSdatSharcOutputDirectory}/{filename}.BAR", temppath, Convert.ToInt32(ArchiveCreatorTimestampTextBox.Text, 16), false, true);
                            fileExtension = ".BAR"; // Set file extension
                            break;
                        case ArchiveTypeSetting.BAR_S:
                            bararchive = new BARArchive($"{_settings.BarSdatSharcOutputDirectory}/{filename}.bar", temppath, Convert.ToInt32(ArchiveCreatorTimestampTextBox.Text, 16), true, true);
                            fileExtension = ".bar"; // Set file extension
                            break;
                        case ArchiveTypeSetting.SDAT:
                            bararchive = new BARArchive($"{_settings.BarSdatSharcOutputDirectory}/{filename}.BAR", temppath, Convert.ToInt32(ArchiveCreatorTimestampTextBox.Text, 16), false, true);
                            sdat = true;
                            fileExtension = ".sdat";  // Set file extension
                            break;
                        case ArchiveTypeSetting.CORE_SHARC:
                            bararchive = new BARArchive($"{_settings.BarSdatSharcOutputDirectory}/{filename}.SHARC", temppath, Convert.ToInt32(ArchiveCreatorTimestampTextBox.Text, 16), true, true, ToolsImpl.base64DefaultSharcKey);
                            fileExtension = ".SHARC"; // Set file extension
                            break;
                        case ArchiveTypeSetting.SDAT_SHARC:
                            sdat = true;
                            bararchive = new BARArchive($"{_settings.BarSdatSharcOutputDirectory}/{filename}.SHARC", temppath, Convert.ToInt32(ArchiveCreatorTimestampTextBox.Text, 16), true, true, ToolsImpl.base64CDNKey2);
                            fileExtension = ".sdat"; // Set file extension
                            break;
                        case ArchiveTypeSetting.CONFIG_SHARC:
                            bararchive = new BARArchive($"{_settings.BarSdatSharcOutputDirectory}/{filename}.sharc", temppath, Convert.ToInt32(ArchiveCreatorTimestampTextBox.Text, 16), true, true, ToolsImpl.base64CDNKey2);
                            fileExtension = ".sharc"; // Set file extension
                            break;
                    }

                    bararchive.AllowWhitespaceInFilenames = true;

                    foreach (string path in enumerable)
                    {
                        var fullPath = Path.Combine(temppath, path);
                        bararchive.AddFile(fullPath);
                        LogDebugInfo($"Archive Creation: Processing item {i + 1}: Added file to archive: {fullPath}");
                    }

                    // Get the name of the directory
                    string directoryName = new DirectoryInfo(temppath).Name;
                    LogDebugInfo($"Archive Creation: Processing item {i + 1}: Processing directory: {directoryName}");

                    // Create a text file to write the paths to
                    StreamWriter writer = new(temppath + @"/files.txt");
                    LogDebugInfo($"Archive Creation: Processing item {i + 1}: Creating file list text file at: {temppath}files.txt");

                    // Get all files in the directory and its immediate subdirectories
                    string[] files = Directory.GetFiles(temppath, "*.*", SearchOption.AllDirectories);

                    // Loop through the files and write their paths to the text file
                    foreach (string file in files)
                    {
                        string relativePath = $"file=\"{file.Replace(temppath, "").TrimStart(Path.DirectorySeparatorChar)}\"";
                        writer.WriteLine(relativePath.Replace(@"\", "/"));
                        LogDebugInfo($"Archive Creation: Processing item {i + 1}: Writing file path to text: {relativePath.Replace(@"\", "/")}");
                    }

                    LogDebugInfo("Archive Creation: Completed writing file paths to text file.");

                    writer.Close();

                    bararchive.AddFile(temppath + @"/files.txt");

                    bararchive.CreateManifest();

                    bararchive.Save();

                    bararchive = null;

                    if (sdat && File.Exists(_settings.BarSdatSharcOutputDirectory + $"/{filename}.SHARC"))
                    {
                        LogDebugInfo($"Archive Creation: Starting SDAT encryption for SHARC file: {filename}.SHARC");
                        RunUnBAR.RunEncrypt(_settings.BarSdatSharcOutputDirectory + $"/{filename}.SHARC", _settings.BarSdatSharcOutputDirectory + $"/{filename}.sdat");
                        File.Delete(_settings.BarSdatSharcOutputDirectory + $"/{filename}.SHARC");
                        LogDebugInfo($"Archive Creation: SDAT encryption completed and original SHARC file deleted for: {filename}.SHARC");
                    }
                    else if (sdat && File.Exists(_settings.BarSdatSharcOutputDirectory + $"/{filename}.BAR"))
                    {
                        LogDebugInfo($"Archive Creation: Starting SDAT encryption for BAR file: {filename}.BAR");
                        RunUnBAR.RunEncrypt(_settings.BarSdatSharcOutputDirectory + $"/{filename}.BAR", _settings.BarSdatSharcOutputDirectory + $"/{filename}.sdat");
                        File.Delete(_settings.BarSdatSharcOutputDirectory + $"/{filename}.BAR");
                        LogDebugInfo($"Archive Creation: SDAT encryption completed and original BAR file deleted for: {filename}.BAR");
                    }

                }
                else
                {
                    string? folderPath = Path.GetDirectoryName(itemPath);

                    string? filename = Path.GetFileName(folderPath);

                    bool sdat = false;
                    IEnumerable<string> enumerable = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories);
                    BARArchive? bararchive = null;

                    string fileExtension = "";

                    switch (_settings.ArchiveTypeSettingRem)
                    {
                        case ArchiveTypeSetting.BAR:
                            bararchive = new BARArchive($"{_settings.BarSdatSharcOutputDirectory}/{filename}.BAR", folderPath, Convert.ToInt32(ArchiveCreatorTimestampTextBox.Text, 16), false, true);
                            fileExtension = ".BAR"; // Set the file extension
                            break;
                        case ArchiveTypeSetting.BAR_S:
                            bararchive = new BARArchive($"{_settings.BarSdatSharcOutputDirectory}/{filename}.bar", folderPath, Convert.ToInt32(ArchiveCreatorTimestampTextBox.Text, 16), true, true);
                            fileExtension = ".bar"; // Set the file extension
                            break;
                        case ArchiveTypeSetting.SDAT:
                            bararchive = new BARArchive($"{_settings.BarSdatSharcOutputDirectory}/{filename}.BAR", folderPath, Convert.ToInt32(ArchiveCreatorTimestampTextBox.Text, 16), false, true);
                            sdat = true;
                            fileExtension = ".sdat"; // Set the file extension
                            break;
                        case ArchiveTypeSetting.CORE_SHARC:
                            bararchive = new BARArchive($"{_settings.BarSdatSharcOutputDirectory}/{filename}.SHARC", folderPath, Convert.ToInt32(ArchiveCreatorTimestampTextBox.Text, 16), true, true, ToolsImpl.base64DefaultSharcKey);
                            fileExtension = ".SHARC"; // Set the file extension
                            break;
                        case ArchiveTypeSetting.SDAT_SHARC:
                            sdat = true;
                            bararchive = new BARArchive($"{_settings.BarSdatSharcOutputDirectory}/{filename}.SHARC", folderPath, Convert.ToInt32(ArchiveCreatorTimestampTextBox.Text, 16), true, true, ToolsImpl.base64CDNKey2);
                            fileExtension = ".sdat"; // Set the file extension
                            break;
                        case ArchiveTypeSetting.CONFIG_SHARC:
                            bararchive = new BARArchive($"{_settings.BarSdatSharcOutputDirectory}/{filename}.sharc", folderPath, Convert.ToInt32(ArchiveCreatorTimestampTextBox.Text, 16), true, true, ToolsImpl.base64CDNKey2);
                            fileExtension = ".sharc"; // Set the file extension
                            break;
                    }


                    bararchive.AllowWhitespaceInFilenames = true;

                    foreach (string path in enumerable)
                    {
                        var fullPath = Path.Combine(folderPath, path);
                        bararchive.AddFile(fullPath);
                        LogDebugInfo($"Archive Creation: Processing item {i + 1}: Added file to archive from directory: {fullPath}");
                    }

                    // Get the name of the directory
                    string directoryName = new DirectoryInfo(folderPath).Name;
                    LogDebugInfo($"Archive Creation: Processing item {i + 1}: Processing directory into archive: {directoryName}");

                    // Create a text file to write the paths to
                    StreamWriter writer = new(folderPath + @"/files.txt");
                    LogDebugInfo($"Archive Creation: Processing item {i + 1}: Creating list of files at: {folderPath}files.txt for archive manifest.");

                    // Get all files in the directory and its immediate subdirectories
                    string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);

                    // Loop through the files and write their paths to the text file
                    foreach (string file in files)
                    {
                        string relativePath = $"file=\"{file.Replace(folderPath, "").TrimStart(Path.DirectorySeparatorChar)}\"";
                        writer.WriteLine(relativePath.Replace(@"\", "/"));
                        LogDebugInfo($"Archive Creation: Processing item {i + 1}: Logging file path for archive manifest: {relativePath.Replace(@"\", "/")}");
                    }

                    writer.Close();
                    LogDebugInfo($"Archive Creation: Processing item {i + 1}: File list for archive manifest completed and file closed.");

                    bararchive.AddFile(folderPath + @"/files.txt");
                    LogDebugInfo($"Archive Creation: Processing item {i + 1}: Added file list to archive: {folderPath}files.txt");

                    bararchive.CreateManifest();
                    LogDebugInfo("Archive Creation: Manifest created for archive.");

                    bararchive.Save();

                    bararchive = null;
                    LogDebugInfo($"Archive Creation: New Archive Saved at: {_settings.BarSdatSharcOutputDirectory}\\{filename}{fileExtension}.");

                    if (sdat && File.Exists(_settings.BarSdatSharcOutputDirectory + $"/{filename}.SHARC"))
                    {
                        RunUnBAR.RunEncrypt(_settings.BarSdatSharcOutputDirectory + $"/{filename}.SHARC", _settings.BarSdatSharcOutputDirectory + $"/{filename}.sdat");
                        File.Delete(_settings.BarSdatSharcOutputDirectory + $"/{filename}.SHARC");
                        LogDebugInfo($"Archive Creation: SDAT encryption completed and original SHARC file deleted for: {filename}.SHARC");
                    }
                    else if (sdat && File.Exists(_settings.BarSdatSharcOutputDirectory + $"/{filename}.BAR"))
                    {
                        RunUnBAR.RunEncrypt(_settings.BarSdatSharcOutputDirectory + $"/{filename}.BAR", _settings.BarSdatSharcOutputDirectory + $"/{filename}.sdat");
                        File.Delete(_settings.BarSdatSharcOutputDirectory + $"/{filename}.BAR");
                        LogDebugInfo($"Archive Creation: SDAT encryption completed and original BAR file deleted for: {filename}.BAR");
                    }

                    LogDebugInfo("Archive Creation: Completed processing item for archive creation.");

                }

                i++;
            }

            // Log the completion and result of the archive creation process
            LogDebugInfo("Archive Creation: Process Success");

            return Task.FromResult(true);
        }




        // TAB 1: Logic for Unpacking Archives

        private async void ArchiveUnpackerExecuteButtonClick(object sender, RoutedEventArgs e)
        {
            LogDebugInfo("Archive Unpacker: Process Initiated");
            TemporaryMessageHelper.ShowTemporaryMessage(ArchiveUnpackerDragAreaText, "Processing....", 2000);

            Directory.CreateDirectory(_settings.MappedOutputDirectory);
            LogDebugInfo($"Archive Unpacker: Output directory created at {_settings.MappedOutputDirectory}");

            string filesToUnpack = ArchiveUnpackerTextBox.Text;
            if (!string.IsNullOrWhiteSpace(filesToUnpack))
            {
                string[] filePaths = filesToUnpack.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                LogDebugInfo($"Archive Unpacker: Starting unpacking for {filePaths.Length} files");
                bool unpackingSuccess = await UnpackFilesAsync(filePaths);

                string message = unpackingSuccess ? $"Success: {filePaths.Length} Files Unpacked" : "Unpacking Failed";
                TemporaryMessageHelper.ShowTemporaryMessage(ArchiveUnpackerDragAreaText, message, 2000);

                LogDebugInfo($"Archive Unpacker: Result - {message}");
            }
            else
            {
                LogDebugInfo("Archive Unpacker: Aborted - No files listed for Unpacking.");
                TemporaryMessageHelper.ShowTemporaryMessage(ArchiveUnpackerDragAreaText, "No files listed for Unpacking.", 2000);
            }
        }



        private void ArchiveUnpackerDragDropHandler(object sender, DragEventArgs e)
        {
            LogDebugInfo("Archive Unpacker: Drag and Drop Initiated");
            TemporaryMessageHelper.ShowTemporaryMessage(ArchiveUnpackerDragAreaText, "Processing....", 2000);

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedItems = (string[])e.Data.GetData(DataFormats.FileDrop);
                List<string> validFiles = new List<string>();

                var validExtensions = new[] { ".bar", ".sdat", ".sharc", ".dat" };
                foreach (var item in droppedItems)
                {
                    if (Directory.Exists(item))
                    {
                        var filesInDirectory = Directory.GetFiles(item, "*.*", SearchOption.AllDirectories)
                            .Where(file => validExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                            .ToList();
                        if (filesInDirectory.Count > 0)
                        {
                            validFiles.AddRange(filesInDirectory);
                        }
                    }
                    else if (File.Exists(item) && validExtensions.Contains(Path.GetExtension(item).ToLowerInvariant()))
                    {
                        validFiles.Add(item);
                    }
                }

                int newFilesCount = 0;
                int duplicatesCount = 0;
                string message = string.Empty;
                int displayTime = 2000;

                if (validFiles.Count > 0)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(ArchiveUnpackerTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;

                        existingFilesSet.UnionWith(validFiles); // Automatically removes duplicates
                        newFilesCount = existingFilesSet.Count - initialCount;
                        duplicatesCount = validFiles.Count - newFilesCount;

                        ArchiveUnpackerTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);

                        message = $"{newFilesCount} file(s) added, {duplicatesCount} duplicate(s) filtered";
                        TemporaryMessageHelper.ShowTemporaryMessage(ArchiveUnpackerDragAreaText, message, 2000);

                        LogDebugInfo($"Archive Unpacker: {newFilesCount} files added, {duplicatesCount} duplicates filtered from Drag and Drop.");
                    });
                }
                else
                {
                    LogDebugInfo("Archive Unpacker: Drag and Drop - No valid files found.");
                    message = "No valid files found.";
                    TemporaryMessageHelper.ShowTemporaryMessage(ArchiveUnpackerDragAreaText, message, 2000);
                }
            }
            else
            {
                LogDebugInfo("Archive Unpacker: Drag and Drop - No Data Present.");
                TemporaryMessageHelper.ShowTemporaryMessage(ArchiveUnpackerDragAreaText, "Drag and Drop operation failed - No Data Present.", 3000);
            }
        }



        private async void ClickToBrowseArchiveUnpackerHandler(object sender, RoutedEventArgs e)
        {
            LogDebugInfo("Archive Unpacker: Browsing for files Initiated");

            // Disable the main window to prevent interactions while the dialog is open
            this.IsEnabled = false;

            var openFileDialog = new VistaOpenFileDialog
            {
                Filter = "Supported files (*.bar;*.sdat;*.sharc;*.dat)|*.bar;*.sdat;*.sharc;*.dat",
                Multiselect = true
            };

            string message = "No files selected.";
            int displayTime = 2000; // Default to 2 seconds for the message display.

            bool? result = openFileDialog.ShowDialog();

            // Await a delay to absorb any unintentional clicks that happen after the dialog closes
            await Task.Delay(10); // 500 ms delay

            // Re-enable the main window after the delay
            this.IsEnabled = true;

            if (result == true)
            {
                var selectedFiles = openFileDialog.FileNames;

                if (selectedFiles.Any())
                {
                    LogDebugInfo($"Archive Unpacker: {selectedFiles.Length} files selected via File Browser.");

                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(ArchiveUnpackerTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;
                        existingFilesSet.UnionWith(selectedFiles);
                        int newFilesCount = existingFilesSet.Count - initialCount;
                        int duplicatesCount = selectedFiles.Length - newFilesCount;

                        ArchiveUnpackerTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);

                        string addedFilesMessage = newFilesCount > 0 ? $"{newFilesCount} files added" : "No new files added";
                        string duplicatesMessage = duplicatesCount > 1 ? $", {duplicatesCount} duplicates filtered" : "";
                        message = addedFilesMessage + duplicatesMessage;

                        TemporaryMessageHelper.ShowTemporaryMessage(ArchiveUnpackerDragAreaText, message, displayTime);
                    });
                }
                else
                {
                    LogDebugInfo("Archive Unpacker: File Browser - No compatible files were selected.");
                    message = "No compatible files selected.";
                }
            }
            else
            {
                LogDebugInfo("Archive Unpacker: File Browser - Dialog Cancelled.");
            }

            TemporaryMessageHelper.ShowTemporaryMessage(ArchiveUnpackerDragAreaText, message, displayTime);
        }



        private string currentFilePath;
        private string CurrentWorkFolder;
        private string UserSuppliedPathPrefix = string.Empty;
        private string UUIDFoundInPath = string.Empty;
        private string CurrentUUID = string.Empty;
        private string GuessedUUID = string.Empty;

        private async Task<bool> UnpackFilesAsync(string[] filePaths)
        {
            Stopwatch TotalJobStopwatch = Stopwatch.StartNew();
            LogDebugInfo($"Archive Unpacker: Beginning unpacking process for {filePaths.Length} files");
            string ogfilename = string.Empty;
            string Outputpath = MappedOutputDirectoryTextBox.Text;

            Directory.CreateDirectory(Outputpath);

            foreach (string filePath in filePaths)
            {
                Stopwatch extractionStopwatch = Stopwatch.StartNew();
                currentFilePath = filePath;
                LogDebugInfo($"Archive Unpacker: Now processing file: {currentFilePath}");
                if (File.Exists(filePath))
                {
                    string filename = Path.GetFileName(filePath);

                    // Determine the action based on file extension
                    if (filename.ToLower().EndsWith(".bar") || filename.ToLower().EndsWith(".dat"))
                    {
                        string barfile = Outputpath + $"/{filename}";
                        // Copy the file to the output directory for processing
                        File.WriteAllBytes(barfile, File.ReadAllBytes(filePath));
                        await RunUnBAR.Run(Directory.GetCurrentDirectory(), barfile, Outputpath, false);
                        ogfilename = filename;
                        filename = filename[..^4];

                        extractionStopwatch.Stop();
                        LogDebugInfo($"Archive Unpacker: Extraction Complete for {filename} (Time Taken {extractionStopwatch.ElapsedMilliseconds}ms)");
                    }
                    else if (filename.ToLower().EndsWith(".sharc"))
                    {
                        string barfile = Outputpath + $"/{filename}";
                        File.WriteAllBytes(barfile, File.ReadAllBytes(filePath));
                        await RunUnBAR.Run(Directory.GetCurrentDirectory(), barfile, Outputpath, false);
                        ogfilename = filename;
                        filename = filename[..^6];

                        extractionStopwatch.Stop();
                        LogDebugInfo($"Archive Unpacker: Extraction Complete for {filename} (Time Taken {extractionStopwatch.ElapsedMilliseconds}ms)");
                    }
                    else if (filename.ToLower().EndsWith(".sdat"))
                    {
                        await RunUnBAR.Run(Directory.GetCurrentDirectory(), filePath, Outputpath, true);
                        ogfilename = filename;
                        filename = filename[..^5];

                        extractionStopwatch.Stop();
                        LogDebugInfo($"Archive Unpacker: Extraction Complete for {filename} (Time Taken {extractionStopwatch.ElapsedMilliseconds}ms)");
                    }
                    else if (filename.ToLower().EndsWith(".zip"))
                    {
                        string barfile = Outputpath + $"/{filename}";
                        UncompressFile(barfile, Outputpath);
                        ogfilename = filename;
                        filename = filename[..^4];
                        extractionStopwatch.Stop();
                        LogDebugInfo($"Archive Unpacker: Extraction Complete for {filename} (Time Taken {extractionStopwatch.ElapsedMilliseconds}ms)");
                    }
                    else
                    {
                        // Skip the file if it doesn't match any known types
                        continue;
                    }

                    // Determine if the directory exists and has files for mapping
                    string directoryToMap = Directory.Exists(Outputpath + $"/{filename}") ? Outputpath + $"/{filename}" : Outputpath;
                    CurrentWorkFolder = directoryToMap;
                    int fileCount = Directory.GetFiles(directoryToMap).Length;

                    if (fileCount > 0)
                    {
                        // Check if the CheckBoxArchiveMapperEXP is checked for experimental mapping
                        if (CheckBoxArchiveMapperEXP.IsChecked == true)
                        {
                            await ExperimentalMapperAsync(directoryToMap, ogfilename);
                        }
                        else
                        {
                            // Proceed with standard mapping logic
                            if (CheckBoxArchiveMapperFAST.IsChecked == true)
                                await AFSClass.AFSMapStart(directoryToMap, ArchiveUnpackerPathTextBox.Text, string.Empty);
                            else
                            {
                                LegacyMapper? map = new();
                                await map.MapperStart(directoryToMap, Directory.GetCurrentDirectory(), ArchiveUnpackerPathTextBox.Text, string.Empty);
                                map = null;
                            }
                        }
                    }
                }
            }
            TotalJobStopwatch.Stop();
            LogDebugInfo($"Archive Unpacker: Job Complete - All files processed (Time Taken {TotalJobStopwatch.ElapsedMilliseconds}ms)");
            return true;
        }



        public async Task ExperimentalMapperAsync(string directoryPath, string ogfilename)
        {
            CurrentUUID = string.Empty;
            GuessedUUID = string.Empty;
            UUIDFoundInPath = string.Empty;
            UserSuppliedPathPrefix = string.Empty;
            Stopwatch MappingStopwatch = Stopwatch.StartNew();
            CheckForUUID();
            if (CheckBoxArchiveMapperUUIDGuesser.IsChecked == true)
            {
                await UUIDguesserCheckAsync(directoryPath); // Now an async call
            }

            LogDebugInfo($"Archive Unpacker: Starting experimental mapping for directory: {directoryPath}");
            var allFilePaths = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
            var filePaths = allFilePaths
                .Where(path =>
                    !path.EndsWith(".dds") &&
                    !path.EndsWith(".mp3") &&
                    !path.EndsWith(".mp4"))
                .ToArray();

            var hashesToPaths = await InitialScanForPaths(filePaths);
            LogDebugInfo($"Archive Unpacker: Total unique hashes and paths found: {hashesToPaths.Count}");

            try
            {
                await RenameFiles(allFilePaths, new ConcurrentDictionary<string, string>(hashesToPaths), directoryPath);

                // Check for unmapped files and attempt to map scene files
                bool hasUnmappedFiles = CheckForUnmappedFiles(directoryPath);
                if (hasUnmappedFiles)
                {
                    await TryMapSceneFile(directoryPath);
                }
                FinalFolderCheckAsync(directoryPath);
            }
            catch (Exception ex)
            {
                LogDebugInfo($"Error in mapping operation: {ex.Message}");
            }

            MappingStopwatch.Stop();
            LogDebugInfo($"Archive Unpacker: Mapping Completed (Time Taken {MappingStopwatch.ElapsedMilliseconds}ms)");
        }

        private static SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

        private async Task UUIDguesserCheckAsync(string directoryPath)
        {
            await semaphore.WaitAsync();
            try
            {
                string exeLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string dependencyPath = Path.Combine(Path.GetDirectoryName(exeLocation), "dependencies", "uuid_helper.txt");

                // Dictionary to store prefix to UUID mappings
                Dictionary<string, string> prefixToUUID = new Dictionary<string, string>();

                if (File.Exists(dependencyPath))
                {
                    var fileLines = await File.ReadAllLinesAsync(dependencyPath);
                    foreach (var line in fileLines)
                    {
                        var parts = line.Split(':');
                        if (parts.Length == 2)
                        {
                            // Store each prefix and its corresponding UUID
                            prefixToUUID[parts[0]] = parts[1];
                        }
                    }

                    LogDebugInfo($"UUID Guesser Check: uuid_helper.txt found with {fileLines.Length} lines.");
                }
                else
                {
                    LogDebugInfo("UUID Guesser Check: uuid_helper.txt not found.");
                    return;
                }

                // Get all files in the root of the directory path, excluding subdirectories
                var rootFiles = Directory.GetFiles(directoryPath, "*", SearchOption.TopDirectoryOnly);

                // Check for files that start with any of the prefixes
                foreach (var filePath in rootFiles)
                {
                    string filenameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
                    foreach (var entry in prefixToUUID)
                    {
                        if (filenameWithoutExtension.StartsWith(entry.Key))
                        {
                            // Format and set the GuessedUUID with prefix, suffix and make it lowercase
                            string formattedUUID = $"objects/{entry.Value.ToLower()}/";
                            LogDebugInfo($"UUID Guesser Check: Guessed UUID = {formattedUUID}");

                            // Only set CurrentUUID if it's currently empty
                            if (string.IsNullOrEmpty(CurrentUUID))
                            {
                                CurrentUUID = entry.Value.ToUpper(); // Set CurrentUUID to the raw UUID value from the file
                                LogDebugInfo($"UUID Guesser Check: CurrentUUID set to {CurrentUUID}");
                            }

                            // Update GuessedUUID regardless of CurrentUUID's state
                            GuessedUUID = formattedUUID;
                            return; // Stop checking after the first match
                        }
                    }
                }

                // If no UUID is guessed
                if (string.IsNullOrEmpty(GuessedUUID))
                {
                    LogDebugInfo("UUID Guesser Check: No matching UUID guessed from file names.");
                }
            }
            finally
            {
                semaphore.Release();
            }
        }


        private async Task RenameFiles(string[] allFilePaths, ConcurrentDictionary<string, string> hashesToPaths, string directoryPath)
        {
            Stopwatch RenameFilesStopwatch = Stopwatch.StartNew();
            var options = new ParallelOptions { MaxDegreeOfParallelism = 2 };
            Parallel.ForEach(allFilePaths, options, filePath =>
            {
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath); // Convert filename to uppercase to match hex hash
                if (hashesToPaths.TryGetValue(fileNameWithoutExtension, out var newPath))
                {
                    // Ensure newPath is uppercase
                    newPath = newPath.ToUpper();

                    // Determine if newPath already contains a file extension
                    var newPathHasExtension = !string.IsNullOrEmpty(Path.GetExtension(newPath));
                    var fileExtension = newPathHasExtension ? string.Empty : Path.GetExtension(filePath).ToUpper(); // Add original extension only if newPath doesn't have one

                    var newFileName = $"{newPath}{fileExtension}";
                    var newFilePath = Path.Combine(Path.GetDirectoryName(filePath), newFileName);

                    // Ensure the target directory exists
                    var newFileDirectory = Path.GetDirectoryName(newFilePath);
                    Directory.CreateDirectory(newFileDirectory);

                    try
                    {
                        File.Move(filePath, newFilePath, true); // Overwrites the file if it already exists
                                                                // LogDebugInfo($"Archive Unpacker: Renamed {filePath} to {newFilePath}");
                    }
                    catch (Exception ex)
                    {
                        LogDebugInfo($"Archive Unpacker: Failed to rename {filePath} to {newFilePath}: {ex.Message}");
                    }
                }
            });
            RenameFilesStopwatch.Stop();

            LogDebugInfo($"Archive Unpacker: Mapping Stage 3 Complete - Renamed files by Hash (Time Taken {RenameFilesStopwatch.ElapsedMilliseconds}ms)");
        }


        private bool CheckForUnmappedFiles(string directoryPath)
        {
            Stopwatch CheckForUnmappedStopwatch = Stopwatch.StartNew();
            // LogDebugInfo($"Archive Unpacker: Checking for unmapped files in {directoryPath}");

            string pattern = @"^[A-F0-9]{8}(\..+)?$";
            bool hasUnmappedFiles = Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly)
                                              .Any(file => Regex.IsMatch(Path.GetFileNameWithoutExtension(file), pattern));

            // LogDebugInfo(hasUnmappedFiles ? "Archive Unpacker: Unmapped files found." : "Archive Unpacker: No unmapped files found.");
            CheckForUnmappedStopwatch.Stop();
            return hasUnmappedFiles;
        }

        private async Task FinalFolderCheckAsync(string directoryPath)
        {
            LogDebugInfo($"Archive Unpacker: Starting directory final check: {directoryPath}");

            string directoryName = Path.GetFileName(directoryPath);
            string parentDirectoryPath = Directory.GetParent(directoryPath)?.FullName ?? string.Empty;
            string newDirectoryName = directoryPath;

            // Handling when CurrentUUID is not set and the directory ends with "_DAT"
            if (string.IsNullOrEmpty(CurrentUUID) && directoryName.EndsWith("_DAT"))
            {
                newDirectoryName = await FindAndRenameForSceneFile(directoryPath, directoryName, parentDirectoryPath);
                if (newDirectoryName != directoryPath)
                {
                    Directory.Move(directoryPath, newDirectoryName);
                    LogDebugInfo($"Directory renamed from '{directoryPath}' to '{newDirectoryName}'.");
                    directoryPath = newDirectoryName; // Update directoryPath for subsequent operations
                }
            }

            // Handling when CurrentUUID is set
            if (!string.IsNullOrEmpty(CurrentUUID))
            {
                if (directoryName.StartsWith("object_"))
                {
                    newDirectoryName = Path.Combine(parentDirectoryPath, CurrentUUID + directoryName.Substring("object".Length));
                }
                else if (directoryName.Equals("object", StringComparison.OrdinalIgnoreCase))
                {
                    newDirectoryName = Path.Combine(parentDirectoryPath, CurrentUUID + "_T000");
                }
                if (directoryName.EndsWith("_DAT"))
                {
                    newDirectoryName = Path.Combine(parentDirectoryPath, CurrentUUID + "_UNKN_" + directoryName);
                }

                // Check if new directory name already exists and create a new name if necessary
                string baseNewDirectoryName = newDirectoryName;
                int counter = 2;
                while (Directory.Exists(newDirectoryName))
                {
                    newDirectoryName = $"{baseNewDirectoryName} ({counter++})";
                }

                // Rename directory if new name is different
                if (newDirectoryName != directoryPath)
                {
                    Directory.Move(directoryPath, newDirectoryName);
                    LogDebugInfo($"Directory renamed from '{directoryPath}' to '{newDirectoryName}'.");
                    directoryPath = newDirectoryName; // Update directoryPath for subsequent operations
                }
            }

            // Remaining logic for scanning and moving directory to "checked" folder
            if (CheckBoxArchiveMapperVerify.IsChecked ?? false)
            {
                Stopwatch scanStopwatch = Stopwatch.StartNew();
                FileScanner scanner = new FileScanner();
                await scanner.ScanDirectoryAsync(directoryPath);
                scanStopwatch.Stop();
                LogDebugInfo($"FileScanner: Scanned directory {directoryPath} (Time Taken: {scanStopwatch.ElapsedMilliseconds}ms)");
            }

            // Handle directory naming conflicts in 'checked' folder
            bool hasUnmappedFiles = CheckForUnmappedFiles(directoryPath);
            string checkedFolder = Path.Combine(parentDirectoryPath, "Complete");
            Directory.CreateDirectory(checkedFolder);
            string finalDirectoryPath = Path.Combine(checkedFolder, Path.GetFileName(directoryPath));
            if (hasUnmappedFiles)
            {
                finalDirectoryPath += "_CHECK";
            }

            int suffixCounter = 2;
            while (Directory.Exists(finalDirectoryPath))
            {
                finalDirectoryPath = $"{finalDirectoryPath} ({suffixCounter++})";
            }

            Directory.Move(directoryPath, finalDirectoryPath);
            LogDebugInfo($"Archive Unpacker: Directory moved to 'checked' at '{finalDirectoryPath}'.");

            // Copy JobReport.txt if exists
            string sourceFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "JobReport.txt");
            string destinationFile = Path.Combine(checkedFolder, "JobReport.txt");
            if (File.Exists(sourceFile))
            {
                File.Copy(sourceFile, destinationFile, overwrite: true);
                LogDebugInfo($"JobReport.txt was copied to '{destinationFile}'.");
            }
        }


        private async Task<string> FindAndRenameForSceneFile(string directoryPath, string directoryName, string parentDirectoryPath)
        {
            string newDirectoryName = directoryPath;  // Default to the original directory path if no .SCENE file is found

            try
            {
                // Get all .SCENE files in the directory, including subdirectories
                var sceneFiles = Directory.GetFiles(directoryPath, "*.SCENE", SearchOption.AllDirectories);
                string sceneFilePath = sceneFiles.FirstOrDefault();

                if (!string.IsNullOrEmpty(sceneFilePath))
                {
                    string sceneFileName = Path.GetFileNameWithoutExtension(sceneFilePath);
                    newDirectoryName = Path.Combine(parentDirectoryPath, sceneFileName + "_" + directoryName);
                    LogDebugInfo($"Found .SCENE file: {sceneFileName}, renaming directory to '{newDirectoryName}'.");
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo($"Error finding .SCENE file: {ex.Message}");
            }

            return newDirectoryName;
        }


        private async Task CreateHDKFolderStructure(string directoryPath)
        {
            // Define a local function to recursively search for resources.xml
            string FindResourcesXml(string path)
            {
                foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
                {
                    string filePath = Path.Combine(directory, "resources.xml");
                    if (File.Exists(filePath))
                    {
                        return filePath;
                    }
                }
                return null; // Return null if not found
            }

            // Use the local function to find resources.xml
            string resourcesXmlPath = FindResourcesXml(directoryPath);
            if (!string.IsNullOrEmpty(resourcesXmlPath))
            {
                LogDebugInfo($"resources.xml found at: {resourcesXmlPath}");
                // Move files listed in resources.xml
                await MoveAllExceptSpecificFiles(resourcesXmlPath, directoryPath);
            }
            else
            {
                LogDebugInfo("resources.xml not found in the directory structure.");
            }
        }

        private async Task MoveAllExceptSpecificFiles(string resourcesXmlPath, string targetDirectoryPath)
        {
            // Determine the source directory from the resources.xml file path
            string sourceDirectoryPath = Path.GetDirectoryName(resourcesXmlPath);

            // List of files to exclude from moving, using a case-insensitive comparison
            var excludeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "resources.xml",
        "object.xml",
        "localisation.xml",
        "editor.oxml",
        "object.odc",
        "catalogueentry.xml",
        "validation.xml",
        "main.lua",
        "large.png",
        "small.png"
    };

            // Move all files from source directory to target directory, except the excluded ones
            var files = Directory.EnumerateFiles(sourceDirectoryPath);
            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file);
                if (!excludeFiles.Contains(fileName))
                {
                    string destFile = Path.Combine(targetDirectoryPath, fileName);
                    File.Move(file, destFile);
                }
            }

            // Move all directories from source directory to target directory
            var directories = Directory.EnumerateDirectories(sourceDirectoryPath);
            foreach (var dir in directories)
            {
                string dirName = Path.GetFileName(dir);
                string destDir = Path.Combine(targetDirectoryPath, dirName);
                Directory.Move(dir, destDir);
            }

            // Delete the timestamp.txt file in targetDirectoryPath if it exists
            string timestampFilePath = Path.Combine(targetDirectoryPath, "timestamp.txt");
            if (File.Exists(timestampFilePath))
            {
                File.Delete(timestampFilePath);
            }

            // Ensure that the operation does not block the main thread
            await Task.CompletedTask;
        }

        private async Task<ConcurrentDictionary<string, string>> InitialScanForPaths(IEnumerable<string> filePaths)
        {
            Stopwatch FilescanforpathsStopwatch = Stopwatch.StartNew();
            LogDebugInfo($"Archive Unpacker: Starting initial scan of {filePaths.Count()} files.");

            var hashesToPaths = new ConcurrentDictionary<string, string>();
            var options = new ParallelOptions { MaxDegreeOfParallelism = 4 };
            var allProcessedMatches = new ConcurrentBag<string>();

            string[] specialPrefixes1 = { "ptools", "tenvi", "xenvi", "natgp", "hatgp", "tatgp" };
            string[] specialPrefixes2 = { "file:///resource_root/build/", "file://resource_root/build/" };
            string[] staticPaths = { "resources.xml", "object.xml", "localisation.xml", "catalogueentry.xml", "object.odc", "editor.oxml", "large.png", "small.png", "maker.png", "files.txt", "__$manifest$__" };
            string ddsPattern = @"[a-z0-9][a-z0-9_. \\/-]*\.dds";
            string quotedPattern = @"(?:file|source|efx_filename)=""([a-z0-9_.() :\\/-]*)""";

            Parallel.ForEach(filePaths, options, filePath =>
            {
                try
                {
                    var contentString = File.ReadAllText(filePath).Replace("\\", "/");
                    var matches = new List<string>();

                    // Add dynamically found paths from regex searches
                    matches.AddRange(Regex.Matches(contentString, ddsPattern, RegexOptions.IgnoreCase).Select(m => m.Value.ToLower()));
                    matches.AddRange(Regex.Matches(contentString, quotedPattern, RegexOptions.IgnoreCase).Select(m => m.Groups[1].Value.ToLower()));

                    // Process all matches, regardless of content
                    foreach (var match in matches)
                    {
                        ProcessAndAddMatches(match, allProcessedMatches, specialPrefixes1, specialPrefixes2);
                    }

                    // Add static paths directly to allProcessedMatches without condition
                    foreach (var staticPath in staticPaths)
                    {
                        ProcessAndAddMatches(staticPath, allProcessedMatches, specialPrefixes1, specialPrefixes2);
                    }
                }
                catch (Exception ex)
                {
                    LogDebugInfo($"Archive Unpacker: Error processing file {filePath}: {ex.Message}");
                }
            });

            // Now process each unique path for hashing
            foreach (var processedMatch in allProcessedMatches.Distinct())
            {
                int hashOfPath = ComputeAFSHash(processedMatch);
                string hexHash = hashOfPath.ToString("X8");
                hashesToPaths.TryAdd(hexHash, processedMatch.ToUpper());
            }

            FilescanforpathsStopwatch.Stop();
            LogDebugInfo($"Archive Unpacker: Mapping Stage 2 - Path Scan Complete. Total matches found {hashesToPaths.Count} (Time Taken {FilescanforpathsStopwatch.ElapsedMilliseconds}ms)");

            return hashesToPaths;
        }





        private void ProcessAndAddMatches(string match, ConcurrentBag<string> allProcessedMatches, string[] specialPrefixes1, string[] specialPrefixes2)
        {
            string processedMatch = match;

            // Trim 1 byte from the start if the match starts with any of the specialPrefixes1
            foreach (var prefix in specialPrefixes1)
            {
                if (processedMatch.StartsWith(prefix))
                {
                    processedMatch = processedMatch.Substring(1); // Trim 1 byte from the start
                    break;
                }
            }

            // Trim the entire prefix for specialPrefixes2
            foreach (var prefix in specialPrefixes2)
            {
                if (processedMatch.StartsWith(prefix))
                {
                    processedMatch = processedMatch.Substring(prefix.Length); // Trim the whole prefix
                    break;
                }
            }

            var processedMatches = new List<string> { processedMatch };

            // Additional processing for specific endings
            if (processedMatch.EndsWith(".probe"))
            {
                processedMatches.Add(processedMatch.Replace(".probe", ".scene"));
                processedMatches.Add(processedMatch.Replace(".probe", ".ocean"));
            }
            else if (processedMatch.EndsWith(".atmos"))
            {
                processedMatches.Add(processedMatch.Replace(".atmos", ".cdata"));
            }

            // Append UUID or UserSuppliedPathPrefix if present
            if (!string.IsNullOrEmpty(UUIDFoundInPath))
            {
                processedMatches.AddRange(processedMatches.ToList().Select(pm => UUIDFoundInPath + pm));
            }
            if (!string.IsNullOrEmpty(UserSuppliedPathPrefix))
            {
                processedMatches.AddRange(processedMatches.ToList().Select(pm => UserSuppliedPathPrefix + pm));
            }

            // Append GuessedUUID if not empty and the checkbox is checked
            if (!string.IsNullOrEmpty(GuessedUUID))
            {
                processedMatches.AddRange(processedMatches.ToList().Select(pm => GuessedUUID + pm));
            }

            // Add all processed matches to the concurrent bag
            foreach (var finalMatch in processedMatches)
            {
                allProcessedMatches.Add(finalMatch);
            }
        }




        private async Task TryMapSceneFile(string directoryPath)
        {
            // Check if CurrentUUID is already set; if yes, skip the function
            if (!string.IsNullOrEmpty(CurrentUUID))
            {
                LogDebugInfo("Archive Unpacker: CurrentUUID is already set, skipping scene file mapping.");
                return;  // Exit the function early
            }

            Stopwatch SceneFileLookupStopwatch = Stopwatch.StartNew();
            LogDebugInfo($"Archive Unpacker: Unmapped file detected in: {directoryPath} - Checking if it's a known scene file in lookup table");

            string helperFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies\\scene_file_mapper_helper.txt");

            if (!File.Exists(helperFilePath))
            {
                LogDebugInfo("Archive Unpacker: Helper file not found.");
                return;
            }

            LogDebugInfo("Archive Unpacker: Helper file found, processing mappings.");

            var fileMappings = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(helperFilePath))
            {
                var parts = line.Split(new[] { ':' }, 2);
                if (parts.Length == 2)
                {
                    string hashPart = parts[0].Trim();
                    string pathPart = parts[1].Trim();
                    fileMappings[hashPart] = pathPart;
                }
            }

            var filesInRootDirectory = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly);

            foreach (var filePath in filesInRootDirectory)
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                if (fileMappings.TryGetValue(fileName, out var newRelativePath))
                {
                    var newFilePath = Path.Combine(directoryPath, newRelativePath);
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(newFilePath)); // Ensure the directory exists
                        File.Move(filePath, newFilePath); // Attempt to move the file
                    }
                    catch (Exception ex)
                    {
                        LogDebugInfo($"Archive Unpacker: Error renaming file from {filePath} to {newFilePath}: {ex.Message}");
                    }
                }
            }

            SceneFileLookupStopwatch.Stop(); // Stop timing the extraction
            LogDebugInfo($"Archive Unpacker: Mapping Stage 4 Complete - Scene filename lookup (Time Taken {SceneFileLookupStopwatch.ElapsedMilliseconds}ms)");
        }


        public void CheckForUUID()
        {
            // Define a regex pattern for the UUID format 8-8-8-8
            string uuidPattern = @"\b[a-f0-9]{8}-[a-f0-9]{8}-[a-f0-9]{8}-[a-f0-9]{8}\b";

            // Initialize a variable to hold any found UUID temporarily
            string tempUUID = string.Empty;

            // Check ArchiveUnpackerPathTextBox for a UUID
            Match match = Regex.Match(ArchiveUnpackerPathTextBox.Text.ToLower(), uuidPattern);
            if (!string.IsNullOrEmpty(ArchiveUnpackerPathTextBox.Text) && !match.Success)
            {
                // If there's a string but no UUID, save it as UserSuppliedPath in lowercase
                UserSuppliedPathPrefix = ArchiveUnpackerPathTextBox.Text.ToLower();
            }

            if (match.Success)
            {
                tempUUID = match.Value.ToLower(); // Save the found UUID
                                                  // If a UUID is found, save it as UserSuppliedUUID in lowercase, prepended and appended as specified
                UserSuppliedPathPrefix = "objects/" + tempUUID + "/";
            }

            // Check currentFilePath for a UUID in lowercase
            match = Regex.Match(currentFilePath.ToLower(), uuidPattern);
            if (match.Success)
            {
                tempUUID = match.Value.ToLower(); // Save the found UUID
                                                  // If a UUID is found in currentFilePath, save it as UUIDFoundInPath in lowercase, prepended and appended as specified
                UUIDFoundInPath = "objects/" + tempUUID + "/";
            }

            // If any UUID was found, save it to CurrentUUID
            if (!string.IsNullOrEmpty(tempUUID))
            {
                CurrentUUID = tempUUID.ToUpper();

            }
            LogDebugInfo($"UUID Check: UUIDFoundInPath set to {UUIDFoundInPath}, CurrentUUID set to {CurrentUUID}, UserSuppliedPathPrefix set to {UserSuppliedPathPrefix}");

        }

        private int ComputeAFSHash(string text)
        {
            unchecked
            {
                int hashOfPath = 0;
                foreach (char ch in text)
                    hashOfPath = hashOfPath * 37 + ch;
              LogDebugInfo($"Computed Hash: {hashOfPath} for Text: {text}");

                return hashOfPath;
            }
        }


        // TAB 2: Logic for CDS Encryption 

        private async void CDSEncrypterExecuteButtonClick(object sender, RoutedEventArgs e)
        {
            LogDebugInfo("CDS Encryption: Process Initiated");
            TemporaryMessageHelper.ShowTemporaryMessage(CDSEncrypterDragAreaText, "Processing...", 2000);

            string baseOutputDirectory = _settings.CdsOutputDirectory; // Assume _settings.CdsOutputDirectory is already set
            if (!Directory.Exists(baseOutputDirectory))
            {
                Directory.CreateDirectory(baseOutputDirectory);
                LogDebugInfo($"CDS Encryption: Output directory created at {baseOutputDirectory}");
            }

            string filesToEncrypt = CDSEncrypterTextBox.Text; // Assuming CDSEncrypterTextBox contains file paths separated by new lines
            bool addSha1ToFilename = CDSAddSHA1CheckBox.IsChecked ?? false; // Check if the checkbox for adding SHA1 to filename is checked

            if (!string.IsNullOrWhiteSpace(filesToEncrypt))
            {
                string[] filePaths = filesToEncrypt.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                LogDebugInfo($"CDS Encryption: Starting encryption for {filePaths.Length} files");
                bool encryptionSuccess = await EncryptFilesAsync(filePaths, baseOutputDirectory, addSha1ToFilename); // Pass the state of the checkbox to the encryption method

                string message = encryptionSuccess ? $"Success: {filePaths.Length} Files Encrypted" : "Encryption Failed";
                TemporaryMessageHelper.ShowTemporaryMessage(CDSEncrypterDragAreaText, message, 2000);
                LogDebugInfo($"CDS Encryption: Result - {message}");
            }
            else
            {
                LogDebugInfo("CDS Encryption: Aborted - No files listed for Encryption.");
                TemporaryMessageHelper.ShowTemporaryMessage(CDSEncrypterDragAreaText, "No files listed for Encryption.", 2000);
            }
        }


        private void CDSEncrypterDragDropHandler(object sender, DragEventArgs e)
        {
            LogDebugInfo("CDS Encryption: Drag and Drop Initiated");
            TemporaryMessageHelper.ShowTemporaryMessage(CDSEncrypterDragAreaText, "Processing....", 1000000);

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedItems = (string[])e.Data.GetData(DataFormats.FileDrop);
                List<string> validFiles = new List<string>();
                var validExtensionsForFolders = new[] { ".sdc", ".odc", ".xml" }; // Extensions to look for within folders
                var validExtensionsForFiles = new[] { ".sdc", ".odc", ".xml", ".bar" }; // Extensions accepted for individual files

                foreach (var item in droppedItems)
                {
                    if (Directory.Exists(item))
                    {
                        // Scan the folder for specific file types excluding .bar files
                        var filesInDirectory = Directory.GetFiles(item, "*.*", SearchOption.AllDirectories)
                            .Where(file => validExtensionsForFolders.Contains(Path.GetExtension(file).ToLowerInvariant()))
                            .ToList();
                        validFiles.AddRange(filesInDirectory);
                    }
                    else if (File.Exists(item) && validExtensionsForFiles.Contains(Path.GetExtension(item).ToLowerInvariant()))
                    {
                        // Accept .bar files if they are dropped individually
                        validFiles.Add(item);
                    }
                }

                int newFilesCount = 0;
                int duplicatesCount = 0;
                string message = string.Empty;
                int displayTime = 2000;

                if (validFiles.Count > 0)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(CDSEncrypterTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;

                        existingFilesSet.UnionWith(validFiles); // Automatically removes duplicates
                        newFilesCount = existingFilesSet.Count - initialCount;
                        duplicatesCount = validFiles.Count - newFilesCount;

                        CDSEncrypterTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);

                        message = $"{newFilesCount} file(s) added, {duplicatesCount} duplicate(s) filtered";
                        TemporaryMessageHelper.ShowTemporaryMessage(CDSEncrypterDragAreaText, message, 2000);

                        LogDebugInfo($"CDS Encryption: {newFilesCount} files added, {duplicatesCount} duplicates filtered from Drag and Drop.");
                    });
                }
                else
                {
                    LogDebugInfo("CDS Encryption: Drag and Drop - No valid files found.");
                    message = "No valid files found.";
                    TemporaryMessageHelper.ShowTemporaryMessage(CDSEncrypterDragAreaText, message, 2000);
                }
            }
            else
            {
                LogDebugInfo("CDS Encryption: Drag and Drop - No Data Present.");
                TemporaryMessageHelper.ShowTemporaryMessage(CDSEncrypterDragAreaText, "Drag and Drop operation failed - No Data Present.", 2000);
            }
        }




        private async void ClickToBrowseCDSEncryptHandler(object sender, RoutedEventArgs e)
        {
            LogDebugInfo("CDS Encryption: Browsing for files Initiated");

            // Disable the main window to prevent interactions while the dialog is open
            this.IsEnabled = false;

            var openFileDialog = new VistaOpenFileDialog
            {
                Filter = "Supported files (*.sdc;*.odc;*.xml;*.bar)|*.sdc;*.odc;*.xml;*.bar",
                Multiselect = true
            };

            string message = "No files selected.";
            int displayTime = 2000;  // Keep your original display time setting

            bool? result = openFileDialog.ShowDialog();

            // Await a delay to absorb any unintentional clicks that happen after the dialog closes
            await Task.Delay(10); // Corrected delay to 10 ms

            // Re-enable the main window after the delay
            this.IsEnabled = true;

            if (result == true)
            {
                var selectedFiles = openFileDialog.FileNames;
                var validExtensions = new[] { ".sdc", ".odc", ".xml", ".bar" }
                    .Select(ext => ext.ToLowerInvariant()).ToArray();

                List<string> validFiles = selectedFiles
                    .Where(file => validExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    .ToList();

                if (validFiles.Any())
                {
                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(CDSEncrypterTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;

                        existingFilesSet.UnionWith(validFiles);
                        int newFilesCount = existingFilesSet.Count - initialCount;
                        int duplicatesCount = validFiles.Count - newFilesCount;

                        CDSEncrypterTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);

                        string duplicatesMessage = duplicatesCount > 1 ? $", {duplicatesCount} duplicate(s) filtered" : "";
                        message = $"{newFilesCount} file(s) added" + duplicatesMessage;
                        TemporaryMessageHelper.ShowTemporaryMessage(CDSEncrypterDragAreaText, message, 2000);

                        LogDebugInfo($"CDS Encryption: {newFilesCount} files added{duplicatesMessage} via File Browser.");
                    });
                }
                else
                {
                    message = "No compatible files selected.";
                    LogDebugInfo("CDS Encryption: File Browser - No compatible files were selected.");
                }
            }
            else
            {
                message = "Dialog cancelled.";
                LogDebugInfo("CDS Encryption: File Browser - Dialog Cancelled.");
            }

            TemporaryMessageHelper.ShowTemporaryMessage(CDSEncrypterDragAreaText, message, 2000);
        }


        public async Task<bool> EncryptFilesAsync(string[] filePaths, string baseOutputDirectory, bool addSha1ToFilename)
        {
            bool allFilesProcessed = true;
            foreach (var filePath in filePaths)
            {
                string filename = Path.GetFileName(filePath);
                try
                {
                    byte[] fileContent = await File.ReadAllBytesAsync(filePath);
                    byte[]? encryptedContent = null;
                    string inputSHA1 = "";

                    // Generate SHA1 hash from file content before encryption
                    using (SHA1 sha1 = SHA1.Create())
                    {
                        byte[] SHA1Data = sha1.ComputeHash(fileContent);
                        inputSHA1 = BitConverter.ToString(SHA1Data).Replace("-", ""); // Full SHA1 of input content
                        LogDebugInfo($"Input SHA1 for {filename}: {inputSHA1}");

                        // Encrypt the file content
                        string computedSha1 = inputSHA1.Substring(0, 16); // Use first 16 characters for some process, if needed
                        encryptedContent = CDSProcess.CDSEncrypt_Decrypt(fileContent, computedSha1);
                    }

                    // Append SHA1 of the original content to filename if requested
                    if (addSha1ToFilename && encryptedContent != null)
                    {
                        string extension = Path.GetExtension(filename);
                        string filenameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
                        filename = $"{filenameWithoutExtension}_{inputSHA1}{extension}"; // Using inputSHA1 for naming
                    }

                    if (encryptedContent != null)
                    {
                        string outputDirectory = Path.Combine(baseOutputDirectory, Path.GetFileName(Path.GetDirectoryName(filePath)));
                        if (!Directory.Exists(outputDirectory))
                        {
                            Directory.CreateDirectory(outputDirectory);
                            LogDebugInfo($"Output directory {outputDirectory} created.");
                        }
                        string outputPath = Path.Combine(outputDirectory, filename);
                        await File.WriteAllBytesAsync(outputPath, encryptedContent);
                        LogDebugInfo($"File {filename} encrypted and written to {outputPath}.");
                    }
                    else
                    {
                        allFilesProcessed = false;
                        LogDebugInfo($"Encryption failed for {filename}, no data written.");
                    }
                }
                catch (Exception ex)
                {
                    LogDebugInfo($"Error encrypting {filename}: {ex.Message}");
                    allFilesProcessed = false;
                }
            }
            return allFilesProcessed;
        }






        // TAB 2: Logic for CDS Decryption

        private async void CDSDecrypterExecuteButtonClick(object sender, RoutedEventArgs e)
        {
            LogDebugInfo("CDS Decryption: Process Initiated");
            TemporaryMessageHelper.ShowTemporaryMessage(CDSDecrypterDragAreaText, "Processing....", 2000);

            string baseOutputDirectory = _settings.CdsOutputDirectory;
            if (!Directory.Exists(baseOutputDirectory))
            {
                Directory.CreateDirectory(baseOutputDirectory);
                LogDebugInfo($"CDS Decryption: Output directory created at {baseOutputDirectory}");
            }

            string filesToDecrypt = CDSDecrypterTextBox.Text;
            string manualSha1 = CDSDecrypterSha1TextBox.Text.Trim();
            bool manualSha1IsValid = !string.IsNullOrWhiteSpace(manualSha1) && manualSha1.Length == 40;

            if (!string.IsNullOrWhiteSpace(filesToDecrypt))
            {
                string[] filePaths = filesToDecrypt.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                bool decryptionSuccess = true;

                foreach (string filePath in filePaths)
                {
                    string filename = Path.GetFileName(filePath);
                    string detectedSha1 = Regex.Match(filename, @"[a-fA-F0-9]{40}").Value;
                    string effectiveSha1 = manualSha1IsValid ? manualSha1 : detectedSha1;

                    if (manualSha1IsValid)
                    {
                        LogDebugInfo($"Using manually provided SHA1 for {filename}: {manualSha1}. Overriding any detected SHA1.");
                        decryptionSuccess &= await DecryptFilesSHA1Async(new string[] { filePath }, baseOutputDirectory, manualSha1);
                    }
                    else if (!string.IsNullOrWhiteSpace(detectedSha1))
                    {
                        LogDebugInfo($"SHA1 detected in filename for {filename}: {detectedSha1}. Using SHA1-assisted decryption.");
                        decryptionSuccess &= await DecryptFilesSHA1Async(new string[] { filePath }, baseOutputDirectory, detectedSha1);
                    }
                    else
                    {
                        LogDebugInfo($"No valid SHA1 detected or provided for {filename}. Using standard decryption.");
                        decryptionSuccess &= await DecryptFilesAsync(new string[] { filePath }, baseOutputDirectory);
                    }
                }

                string message = decryptionSuccess ? "Success: All files decrypted" : "Decryption Failed for one or more files";
                TemporaryMessageHelper.ShowTemporaryMessage(CDSDecrypterDragAreaText, message, 2000);
                LogDebugInfo($"CDS Decryption: Result - {message}");
            }
            else
            {
                LogDebugInfo("CDS Decryption: Aborted - No files listed for Decryption.");
                TemporaryMessageHelper.ShowTemporaryMessage(CDSDecrypterDragAreaText, "No files listed for Decryption.", 2000);
            }
        }



        public async Task<bool> DecryptFilesAsync(string[] filePaths, string baseOutputDirectory)
        {
            bool allFilesProcessed = true;
            foreach (var filePath in filePaths)
            {
                string filename = Path.GetFileName(filePath);
                string extension = Path.GetExtension(filename);
                try
                {
                    string parentFolderName = Path.GetFileName(Path.GetDirectoryName(filePath));
                    LogDebugInfo($"Reading file content for {filename}.");

                    byte[] fileContent = await File.ReadAllBytesAsync(filePath);
                    LogDebugInfo($"File content read successfully for {filename}, starting decryption.");

                    BruteforceProcess? proc = new BruteforceProcess(fileContent);
                    byte[]? decryptedContent = proc.StartBruteForce();

                    if (decryptedContent != null)
                    {
                        string outputDirectory = Path.Combine(baseOutputDirectory, parentFolderName);
                        if (!Directory.Exists(outputDirectory))
                        {
                            Directory.CreateDirectory(outputDirectory);
                        }
                        string outputPath = Path.Combine(outputDirectory, filename);
                        await File.WriteAllBytesAsync(outputPath, decryptedContent);

                        if (!IsValidDecryptedFile(outputPath, extension))
                        {
                            File.Delete(outputPath);
                            LogDebugInfo($"Validation failed for {filename}. File deleted.");
                            allFilesProcessed = false;
                        }
                        else
                        {
                            LogDebugInfo($"Validation passed for {filename}. Decryption successful and file is valid.");
                        }
                    }
                    else
                    {
                        allFilesProcessed = false;
                        LogDebugInfo($"Decryption process returned null for {filename}. No data written.");
                    }
                }
                catch (Exception ex)
                {
                    LogDebugInfo($"Error processing {filename}: {ex.Message}");
                    allFilesProcessed = false;
                }
            }
            if (allFilesProcessed)
            {
                LogDebugInfo("All files processed successfully.");
            }
            else
            {
                LogDebugInfo("One or more files failed to process correctly.");
            }
            return allFilesProcessed;
        }


        public async Task<bool> DecryptFilesSHA1Async(string[] filePaths, string baseOutputDirectory, string sha1Hash)
        {
            bool allFilesProcessed = true;
            foreach (var filePath in filePaths)
            {
                string filename = Path.GetFileName(filePath);
                string extension = Path.GetExtension(filename);
                try
                {
                    string parentFolderName = Path.GetFileName(Path.GetDirectoryName(filePath));
                    byte[] fileContent = await File.ReadAllBytesAsync(filePath);
                    string fileSha1 = Regex.Match(filename, @"\b[0-9a-fA-F]{40}\b").Value;
                    byte[]? processedContent = null;
                    string useSha1;

                    if (!string.IsNullOrWhiteSpace(fileSha1))
                    {
                        useSha1 = fileSha1;
                        LogDebugInfo($"SHA1 {useSha1} detected in filename for {filename}.");
                    }
                    else if (!string.IsNullOrWhiteSpace(sha1Hash))
                    {
                        useSha1 = sha1Hash;
                        LogDebugInfo($"SHA1 {useSha1} provided manually for {filename}.");
                    }
                    else
                    {
                        using (SHA1 sha1 = SHA1.Create())
                        {
                            byte[] SHA1Data = sha1.ComputeHash(fileContent);
                            useSha1 = BitConverter.ToString(SHA1Data).Replace("-", "").Substring(0, 16);
                            LogDebugInfo($"No SHA1 found in filename or provided. Generated SHA1 for {filename}: {useSha1}");
                        }
                    }

                    processedContent = CDSProcess.CDSEncrypt_Decrypt(fileContent, useSha1.Substring(0, 16));
                    if (processedContent != null)
                    {
                        string outputDirectory = Path.Combine(baseOutputDirectory, parentFolderName);
                        if (!Directory.Exists(outputDirectory))
                        {
                            Directory.CreateDirectory(outputDirectory);
                        }
                        string outputPath = Path.Combine(outputDirectory, filename);
                        await File.WriteAllBytesAsync(outputPath, processedContent);
                        LogDebugInfo($"Decryption successful for {filename}. Output written to {outputPath}.");

                        // Validate the decrypted file content
                        if (!IsValidDecryptedFile(outputPath, extension))
                        {
                            File.Delete(outputPath);
                            LogDebugInfo($"Validation failed for {filename}. File deleted.");
                            allFilesProcessed = false;
                        }
                        else
                        {
                            LogDebugInfo($"Validation passed for {filename}. Decryption successful and file content is valid.");
                        }
                    }
                    else
                    {
                        allFilesProcessed = false;
                        LogDebugInfo($"Decryption failed for {filename}. No output written.");
                    }
                }
                catch (Exception ex)
                {
                    LogDebugInfo($"Error processing {filename}: {ex.Message}");
                    allFilesProcessed = false;
                }
            }
            return allFilesProcessed;
        }


        private bool IsValidDecryptedFile(string filePath, string extension)
        {
            // Automatically pass validation for HCDB files
            if (extension == ".hcdb")
            {
                LogDebugInfo($"HCDB file validation passed automatically for {Path.GetFileName(filePath)}.");
                return true;
            }

            try
            {
                var doc = XDocument.Load(filePath);
                if (extension == ".sdc" && doc.Descendants("NAME").Any())
                {
                    LogDebugInfo($"Validation passed for {Path.GetFileName(filePath)}: Found <NAME> element.");
                    return true;
                }
                else if (extension == ".odc" && doc.Descendants("uuid").Any())
                {
                    LogDebugInfo($"Validation passed for {Path.GetFileName(filePath)}: Found <uuid> element.");
                    return true;
                }
                else if (extension == ".xml" && doc.Descendants("SCENELIST").Any())
                {
                    LogDebugInfo($"Validation passed for {Path.GetFileName(filePath)}: Found <SCENELIST> element.");
                    return true;
                }
                else
                {
                    LogDebugInfo($"Validation failed for {Path.GetFileName(filePath)}: Required element not found.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo($"Failed to validate file {Path.GetFileName(filePath)}: {ex.Message}");
                return false;
            }
        }



        private void CDSDecrypterDragDropHandler(object sender, DragEventArgs e)
        {
            LogDebugInfo("CDS Decryption: Drag and Drop Initiated");
            TemporaryMessageHelper.ShowTemporaryMessage(CDSDecrypterDragAreaText, "Processing....", 1000000);

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedItems = (string[])e.Data.GetData(DataFormats.FileDrop);
                List<string> validFiles = new List<string>();
                var validExtensionsForFolders = new[] { ".sdc", ".odc", ".xml" }; // Extensions for files in folders
                var validExtensionsForFiles = new[] { ".sdc", ".odc", ".xml", ".bar" }; // Extensions for individual files

                foreach (var item in droppedItems)
                {
                    if (Directory.Exists(item))
                    {
                        var filesInDirectory = Directory.GetFiles(item, "*.*", SearchOption.AllDirectories)
                            .Where(file => validExtensionsForFolders.Contains(Path.GetExtension(file).ToLowerInvariant()))
                            .ToList();
                        validFiles.AddRange(filesInDirectory);
                    }
                    else if (File.Exists(item) && validExtensionsForFiles.Contains(Path.GetExtension(item).ToLowerInvariant()))
                    {
                        validFiles.Add(item);
                    }
                }

                int newFilesCount = 0;
                int duplicatesCount = 0;
                string message = string.Empty;
                int displayTime = 2000;

                if (validFiles.Count > 0)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(CDSDecrypterTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;

                        existingFilesSet.UnionWith(validFiles);
                        newFilesCount = existingFilesSet.Count - initialCount;
                        duplicatesCount = validFiles.Count - newFilesCount;

                        CDSDecrypterTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);

                        message = $"{newFilesCount} file(s) added, {duplicatesCount} duplicate(s) filtered";
                        TemporaryMessageHelper.ShowTemporaryMessage(CDSDecrypterDragAreaText, message, 2000);

                        LogDebugInfo($"CDS Decryption: {newFilesCount} files added, {duplicatesCount} duplicates filtered from Drag and Drop.");
                    });
                }
                else
                {
                    LogDebugInfo("CDS Decryption: Drag and Drop - No valid files found.");
                    message = "No valid files found.";
                    TemporaryMessageHelper.ShowTemporaryMessage(CDSDecrypterDragAreaText, message, 2000);
                }
            }
            else
            {
                LogDebugInfo("CDS Decryption: Drag and Drop - No Data Present.");
                TemporaryMessageHelper.ShowTemporaryMessage(CDSDecrypterDragAreaText, "Drag and Drop operation failed - No Data Present.", 2000);
            }
        }


        private async void ClickToBrowseCDSDecryptHandler(object sender, RoutedEventArgs e)
        {
            LogDebugInfo("CDS Decryption: Browsing for files Initiated");

            // Disable the main window to prevent interactions while the dialog is open
            this.IsEnabled = false;

            var openFileDialog = new VistaOpenFileDialog
            {
                Filter = "Supported files (*.sdc;*.odc;*.xml;*.bar)|*.sdc;*.odc;*.xml;*.bar",
                Multiselect = true
            };

            string message = "No files selected.";
            int displayTime = 2000; // Default to 2 seconds for the message display.

            bool? result = openFileDialog.ShowDialog();

            // Await a delay to absorb any unintentional clicks that happen after the dialog closes
            await Task.Delay(10); // 10 ms delay

            // Re-enable the main window after the delay
            this.IsEnabled = true;

            if (result == true)
            {
                var selectedFiles = openFileDialog.FileNames;
                var validExtensions = new[] { ".sdc", ".odc", ".xml", ".bar" }
                    .Select(ext => ext.ToLowerInvariant()).ToArray();

                List<string> validFiles = selectedFiles
                    .Where(file => validExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    .ToList();

                if (validFiles.Any())
                {
                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(CDSDecrypterTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;
                        existingFilesSet.UnionWith(validFiles);
                        int newFilesCount = existingFilesSet.Count - initialCount;
                        int duplicatesCount = validFiles.Count - newFilesCount;

                        CDSDecrypterTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);

                        string addedFilesMessage = newFilesCount > 0 ? $"{newFilesCount} file(s) added" : "No new files added";
                        string duplicatesMessage = duplicatesCount > 1 ? $", {duplicatesCount} duplicate(s) filtered" : "";
                        message = addedFilesMessage + duplicatesMessage;

                        LogDebugInfo($"CDS Decryption: {message} via File Browser");
                    });
                }
                else
                {
                    message = "No compatible files selected.";
                    LogDebugInfo("CDS Decryption: File Browser - No compatible files were selected.");
                }
            }
            else
            {
                message = "Dialog cancelled.";
                LogDebugInfo("CDS Decryption: File Browser - Dialog Cancelled.");
            }

            TemporaryMessageHelper.ShowTemporaryMessage(CDSDecrypterDragAreaText, message, 2000);
        }


        private Task<bool> DecryptFilesAsync(string[] filePaths)
        {
            LogDebugInfo($"CDS Decryption: Starting decryption for {filePaths.Length} file(s)");

            // TODO: Implement the actual decryption logic here
            // The following line is just a placeholder to simulate successful decryption.
            bool decryptionResult = true; // Simulate success for now

            if (decryptionResult)
            {
                LogDebugInfo("CDS Decryption: Decryption process completed successfully");
            }
            else
            {
                LogDebugInfo("CDS Decryption: Decryption process failed");
            }

            // Return true if decryption is successful, false otherwise
            return Task.FromResult(decryptionResult);
        }

        // TAB 3: Logic for packing SQL to HCDB

        // HCDB Encrypter execute button click
        private async void HCDBEncrypterExecuteButtonClick(object sender, RoutedEventArgs e)
        {
            LogDebugInfo("HCDB Conversion: Process Initiated");
            TemporaryMessageHelper.ShowTemporaryMessage(HCDBEncrypterDragAreaText, "Processing....", 1000000);

            if (!Directory.Exists(_settings.HcdbOutputDirectory))
            {
                Directory.CreateDirectory(_settings.HcdbOutputDirectory);
                LogDebugInfo($"HCDB Conversion: Output directory created at {_settings.HcdbOutputDirectory}");
            }

            string filesToConvert = HCDBEncrypterTextBox.Text;
            if (!string.IsNullOrWhiteSpace(filesToConvert))
            {
                string[] filePaths = filesToConvert.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                string[] segsPaths = filePaths.Select(fp => fp + ".segs").ToArray(); // Assuming LZMA outputs files with .segs extension

                LogDebugInfo($"HCDB Conversion: Starting conversion for {filePaths.Length} files");
                bool conversionSuccess = await ConvertSqlToHcdbAsync(filePaths);

                if (conversionSuccess)
                {
                    bool encryptionSuccess = await EncryptHCDBFilesAsync(segsPaths, _settings.HcdbOutputDirectory); // Encrypt the .segs files
                    string message = encryptionSuccess ? $"Success: {filePaths.Length} Files Converted and Encrypted" : "Encryption Failed";
                    TemporaryMessageHelper.ShowTemporaryMessage(HCDBEncrypterDragAreaText, message, 2000);
                    LogDebugInfo($"HCDB Conversion: Result - {message}");
                }
                else
                {
                    string message = "Conversion Failed";
                    TemporaryMessageHelper.ShowTemporaryMessage(HCDBEncrypterDragAreaText, message, 2000);
                    LogDebugInfo($"HCDB Conversion: Result - {message}");
                }
            }
            else
            {
                LogDebugInfo("HCDB Conversion: Aborted - No SQL files listed for conversion.");
                TemporaryMessageHelper.ShowTemporaryMessage(HCDBEncrypterDragAreaText, "No SQL files listed for conversion.", 2000);
            }
        }


        private async Task<bool> ConvertSqlToHcdbAsync(string[] filePaths)
        {
            bool allFilesProcessed = true;
            string lzmaPath = @"dependencies\lzma_segs.exe";

            foreach (var filePath in filePaths)
            {
                string filename = Path.GetFileName(filePath);
                string expectedOutputPath = filePath + ".segs";  // Expecting LZMA to append ".segs" to the input file's name

                try
                {
                    // Process the file with LZMA without explicitly specifying the output path
                    if (!ExecuteLzmaProcess(lzmaPath, filePath))
                    {
                        LogDebugInfo($"Conversion failed for {filename}.");
                        allFilesProcessed = false;
                    }
                    else
                    {
                        LogDebugInfo($"Conversion successful for {filename}, output likely written to {expectedOutputPath}.");
                    }
                }
                catch (Exception ex)
                {
                    LogDebugInfo($"Error processing {filename}: {ex.Message}");
                    allFilesProcessed = false;
                }
            }

            return allFilesProcessed;
        }

        private bool ExecuteLzmaProcess(string lzmaPath, string inputFilePath)
        {
            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo.FileName = lzmaPath;
                    process.StartInfo.Arguments = $"\"{inputFilePath}\"";  
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.CreateNoWindow = true;
                    process.Start();

                    // To log output or errors (optional but useful for debugging)
                    string output = process.StandardOutput.ReadToEnd();
                    string errors = process.StandardError.ReadToEnd();

                    process.WaitForExit();

                    // Check if the process exited successfully and the expected output file was created
                    if (process.ExitCode == 0 && File.Exists(inputFilePath + ".segs"))
                    {
                        LogDebugInfo($"LZMA compression successful for {Path.GetFileName(inputFilePath)}");
                        return true;
                    }
                    else
                    {
                        LogDebugInfo($"LZMA compression failed for {Path.GetFileName(inputFilePath)}: {errors}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo($"Error during LZMA compression: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> EncryptHCDBFilesAsync(string[] filePaths, string baseOutputDirectory)
        {
            bool addSha1ToFilename = HCDBAddSHA1CheckBox.IsChecked ?? false;
            bool allFilesProcessed = true;
            foreach (var filePath in filePaths)
            {
                string filename = Path.GetFileName(filePath);
                // Remove ".sql" and ".segs" if present, then append ".hcdb"
                string cleanFilename = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(filename)) + ".hcdb";

                try
                {
                    byte[] fileContent = await File.ReadAllBytesAsync(filePath);
                    byte[]? encryptedContent = null;
                    string inputSHA1 = "";

                    using (SHA1 sha1 = SHA1.Create())
                    {
                        byte[] SHA1Data = sha1.ComputeHash(fileContent);
                        inputSHA1 = BitConverter.ToString(SHA1Data).Replace("-", ""); // Full SHA1 of input content
                        LogDebugInfo($"Input SHA1 for {cleanFilename}: {inputSHA1}");

                        string computedSha1 = inputSHA1.Substring(0, 16);
                        encryptedContent = CDSProcess.CDSEncrypt_Decrypt(fileContent, computedSha1);
                    }

                    // Append SHA1 to the filename without extensions if requested
                    if (addSha1ToFilename && encryptedContent != null)
                    {
                        string filenameWithoutExtension = Path.GetFileNameWithoutExtension(cleanFilename);
                        cleanFilename = $"{filenameWithoutExtension}_{inputSHA1}.hcdb";  // Append SHA1 and new extension
                    }

                    if (encryptedContent != null)
                    {
                        string outputPath = Path.Combine(baseOutputDirectory, cleanFilename);
                        await File.WriteAllBytesAsync(outputPath, encryptedContent);
                        LogDebugInfo($"File {cleanFilename} encrypted and written to {outputPath}.");

                        // After successful encryption, delete the .segs file
                        File.Delete(filePath);
                        LogDebugInfo($"Temporary file {filePath} deleted after successful encryption.");
                    }
                    else
                    {
                        allFilesProcessed = false;
                        LogDebugInfo($"Encryption failed for {cleanFilename}, no data written.");
                    }
                }
                catch (Exception ex)
                {
                    LogDebugInfo($"Error encrypting {cleanFilename}: {ex.Message}");
                    allFilesProcessed = false;
                }
            }
            return allFilesProcessed;
        }



        private void HCDBEncrypterDragDropHandler(object sender, DragEventArgs e)
        {
            LogDebugInfo("HCDB Conversion: Drag and Drop Initiated");
            TemporaryMessageHelper.ShowTemporaryMessage(HCDBEncrypterDragAreaText, "Processing....", 1000000);

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedItems = (string[])e.Data.GetData(DataFormats.FileDrop);
                List<string> validFiles = new List<string>();
                string message = string.Empty;
                int displayTime = 2000; // Default to 2 seconds

                foreach (var item in droppedItems)
                {
                    if (Directory.Exists(item))
                    {
                        var filesInDirectory = Directory.GetFiles(item, "*.sql", SearchOption.AllDirectories).ToList();
                        validFiles.AddRange(filesInDirectory);
                    }
                    else if (File.Exists(item) && Path.GetExtension(item).ToLowerInvariant() == ".sql")
                    {
                        validFiles.Add(item);
                    }
                }

                if (validFiles.Count > 0)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(HCDBEncrypterTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;
                        existingFilesSet.UnionWith(validFiles); // Automatically removes duplicates
                        int newFilesCount = existingFilesSet.Count - initialCount;
                        int duplicatesCount = validFiles.Count - newFilesCount;

                        HCDBEncrypterTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);

                        string addedFilesMessage = newFilesCount > 0 ? $"{newFilesCount} SQL files added" : "No new SQL files added";
                        string duplicatesMessage = duplicatesCount > 0 ? $", {duplicatesCount} duplicates filtered" : "";
                        message = addedFilesMessage + duplicatesMessage;

                        LogDebugInfo($"HCDB Conversion: {newFilesCount} SQL files added, {duplicatesCount} duplicates filtered");
                    });
                    displayTime = 1000; // Change display time since files were added
                }
                else
                {
                    message = "No SQL files found.";
                    LogDebugInfo("HCDB Conversion: No SQL files found in Drag and Drop.");
                }

                TemporaryMessageHelper.ShowTemporaryMessage(HCDBEncrypterDragAreaText, message, 2000);
            }
            else
            {
                LogDebugInfo("HCDB Conversion: Drag and Drop - No Data Present.");
                TemporaryMessageHelper.ShowTemporaryMessage(HCDBEncrypterDragAreaText, "Drag and Drop operation failed - No Data Present.", 2000);
            }
        }



        private async void ClickToBrowseHCDBEncryptHandler(object sender, RoutedEventArgs e)
        {
            LogDebugInfo("HCDB Conversion: Browsing for SQL files Initiated");

            // Disable the main window to prevent interactions while the dialog is open
            this.IsEnabled = false;

            var openFileDialog = new VistaOpenFileDialog
            {
                Filter = "SQL files (*.sql)|*.sql",
                Multiselect = true
            };

            string message = "No files selected.";
            int displayTime = 2000; // Default to 2 seconds for the message display.

            bool? result = openFileDialog.ShowDialog();

            // Await a delay to absorb any unintentional clicks that happen after the dialog closes
            await Task.Delay(10); // 10 ms delay

            // Re-enable the main window after the delay
            this.IsEnabled = true;

            if (result == true)
            {
                var selectedFiles = openFileDialog.FileNames;

                if (selectedFiles.Any())
                {
                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(HCDBEncrypterTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;
                        existingFilesSet.UnionWith(selectedFiles); // Automatically removes duplicates
                        int newFilesCount = existingFilesSet.Count - initialCount;
                        int duplicatesCount = selectedFiles.Length - newFilesCount;

                        HCDBEncrypterTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);

                        string addedFilesMessage = newFilesCount > 0 ? $"{newFilesCount} SQL files added" : "No new SQL files added";
                        string duplicatesMessage = duplicatesCount > 1 ? $", {duplicatesCount} duplicates filtered" : "";
                        message = addedFilesMessage + duplicatesMessage;

                        LogDebugInfo($"HCDB Conversion: {newFilesCount} SQL files added, {duplicatesCount} duplicates filtered via File Browser");
                    });
                    displayTime = 1000; // Change display time since files were added
                }
                else
                {
                    message = "No SQL files selected.";
                    LogDebugInfo("HCDB Conversion: No SQL files selected in File Browser.");
                }
            }
            else
            {
                LogDebugInfo("HCDB Conversion: File Browser - Dialog Cancelled.");
            }

            TemporaryMessageHelper.ShowTemporaryMessage(HCDBEncrypterDragAreaText, message, 2000);
        }



        // TAB 3: Logic for packing HCDB to SQL

        // HCDB Decrypter execute button click
        private async void HCDBDecrypterExecuteButtonClick(object sender, RoutedEventArgs e)
        {
            LogDebugInfo("HCDB Decryption: Process Initiated");
            TemporaryMessageHelper.ShowTemporaryMessage(HCDBDecrypterDragAreaText, "Processing...", 2000);

            string outputDirectory = HcdbOutputDirectoryTextBox.Text;
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
                LogDebugInfo($"HCDB Decryption: Output directory created at {outputDirectory}");
            }

            string filesToDecrypt = HCDBDecrypterTextBox.Text;
            string manualSha1 = HCDBDecrypterSha1TextBox.Text.Trim();
            bool manualSha1IsValid = !string.IsNullOrWhiteSpace(manualSha1) && manualSha1.Length == 40;

            if (!string.IsNullOrWhiteSpace(filesToDecrypt))
            {
                string[] filePaths = filesToDecrypt.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                bool decryptionSuccess = true;

                foreach (string filePath in filePaths)
                {
                    string filename = Path.GetFileName(filePath);
                    string detectedSha1 = Regex.Match(filename, @"[a-fA-F0-9]{40}").Value;
                    string effectiveSha1 = manualSha1IsValid ? manualSha1 : detectedSha1;
                    string cleanFilename = manualSha1IsValid ? filename.Replace(effectiveSha1, "") : filename;  // Remove SHA1 from filename if used

                    byte[]? decryptedData = null;
                    if (!string.IsNullOrWhiteSpace(effectiveSha1) && effectiveSha1.Length == 40)
                    {
                        LogDebugInfo($"Using SHA1 for {filename}: {effectiveSha1}. Starting decryption.");
                        decryptedData = await DecryptHCDBFilesSHA1Async(filePath, outputDirectory, effectiveSha1);
                    }
                    else
                    {
                        LogDebugInfo($"No valid SHA1 detected or provided for {filename}. Using standard decryption.");
                        decryptedData = await DecryptHCDBFilesAsync(filePath, outputDirectory);
                    }

                    if (decryptedData != null)
                    {
                        if (!await ProcessDecryptedHCDB(filePath, decryptedData, outputDirectory, cleanFilename))
                        {
                            decryptionSuccess = false;
                            LogDebugInfo($"Post-decryption processing failed for {filename}.");
                        }
                        else
                        {
                            LogDebugInfo($"Post-decryption processing succeeded for {filename}.");
                        }
                    }
                    else
                    {
                        decryptionSuccess = false;
                        LogDebugInfo($"Decryption failed for {filename}.");
                    }
                }

                string message = decryptionSuccess ? "Success: All HCDB files decrypted and processed" : "Decryption or processing failed for one or more files";
                TemporaryMessageHelper.ShowTemporaryMessage(HCDBDecrypterDragAreaText, message, 2000);
                LogDebugInfo($"HCDB Decryption: Result - {message}");
            }
            else
            {
                LogDebugInfo("HCDB Decryption: Aborted - No HCDB files listed for decryption.");
                TemporaryMessageHelper.ShowTemporaryMessage(HCDBDecrypterDragAreaText, "No HCDB files listed for decryption.", 2000);
            }
        }


        public async Task<byte[]?> DecryptHCDBFilesAsync(string filePath, string outputDirectory)
        {
            string filename = Path.GetFileName(filePath);
            try
            {
                byte[] fileContent = await File.ReadAllBytesAsync(filePath);
                LogDebugInfo($"File content read successfully for {filename}, starting decryption.");

                // Assuming decryption process
                BruteforceProcess? proc = new BruteforceProcess(fileContent);
                byte[]? decryptedContent = proc.StartBruteForce();  // Simplify, assuming this method does the decryption

                if (decryptedContent != null)
                {
                    LogDebugInfo($"Decryption successful for {filename}.");
                    return decryptedContent;
                }
                else
                {
                    LogDebugInfo($"Decryption process returned null for {filename}. No data written.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo($"Error processing {filename}: {ex.Message}");
                return null;
            }
        }



        public async Task<byte[]?> DecryptHCDBFilesSHA1Async(string filePath, string outputDirectory, string sha1Hash)
        {
            string filename = Path.GetFileName(filePath);
            try
            {
                byte[] fileContent = await File.ReadAllBytesAsync(filePath);
                LogDebugInfo($"Reading file content for {filename}.");

                // Using SHA1 hash to decrypt the file
                byte[]? processedContent = CDSProcess.CDSEncrypt_Decrypt(fileContent, sha1Hash.Substring(0, 16));

                if (processedContent != null)
                {
                    LogDebugInfo($"Decryption successful for {filename} using SHA1.");
                    return processedContent;
                }
                else
                {
                    LogDebugInfo($"Decryption failed for {filename}. No output written.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo($"Error processing {filename}: {ex.Message}");
                return null;
            }
        }



        private async Task<bool> ProcessDecryptedHCDB(string filePath, byte[] decryptedData, string outputDirectory, string filename)
        {
            try
            {
                // If SHA1 was part of the filename, remove it before saving
                string baseFilename = Path.GetFileNameWithoutExtension(filename);
                string cleanFilename = Regex.Replace(baseFilename, "[a-fA-F0-9]{40}", ""); // Remove SHA1 hash
                cleanFilename = cleanFilename.TrimEnd('_') + ".SQL"; // Ensure it ends with ".sql"

                byte[]? processedData = HCDBUnpack(decryptedData, LogDebugInfo);
                if (processedData != null)
                {
                    string outputPath = Path.Combine(outputDirectory, cleanFilename);
                    await File.WriteAllBytesAsync(outputPath, processedData);
                    LogDebugInfo($"Processed HCDB file successfully written to {outputPath}.");

                    // Validate the processed SQL file
                    if (IsValidSqlFile(outputPath))
                    {
                        LogDebugInfo($"Validation passed for SQL file at {outputPath}.");
                        return true;
                    }
                    else
                    {
                        File.Delete(outputPath);
                        LogDebugInfo($"Invalid SQL file deleted at {outputPath}.");
                        return false;
                    }
                }
                else
                {
                    LogDebugInfo($"Failed to process decrypted data for {filename}.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo($"Error processing decrypted data for {filename}: {ex.Message}");
                return false;
            }
        }


        public static byte[]? HCDBUnpack(byte[] decryptedData, Action<string> log)
        {
            try
            {
                byte[]? decompressedData = new EdgeLZMA().Decompress(decryptedData, true);

                if (decompressedData != null && decompressedData.Length >= 4 &&
                    decompressedData[0] != 0x73 && decompressedData[1] != 0x65 &&
                    decompressedData[2] != 0x67 && decompressedData[3] != 0x73)
                {
                    return decompressedData; // Returns the decompressed data array
                }
                else
                {
                    log("Decompression failed or data did not start with the expected header.");
                    return null; // Returns null if decompression fails or header is incorrect
                }
            }
            catch (Exception ex)
            {
                log($"Error during HCDB unpacking: {ex.Message}");
                return null;
            }
        }

        private bool IsValidSqlFile(string filePath)
        {
            // Expected header bytes for "SQLite" files
            byte[] expectedHeader = { 0x53, 0x51, 0x4C, 0x69, 0x74, 0x65 };

            try
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    byte[] fileHeader = new byte[expectedHeader.Length];
                    int bytesRead = fs.Read(fileHeader, 0, fileHeader.Length);

                    // Check if read bytes match the expected header
                    if (bytesRead == expectedHeader.Length)
                    {
                        for (int i = 0; i < expectedHeader.Length; i++)
                        {
                            if (fileHeader[i] != expectedHeader[i])
                            {
                                LogDebugInfo($"SQL file validation failed: Header does not match at {filePath}.");
                                return false;
                            }
                        }
                        return true;  // Header matches
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo($"Error validating SQL file at {filePath}: {ex.Message}");
            }

            LogDebugInfo($"SQL file validation failed: Insufficient header bytes at {filePath}.");
            return false;  // Header does not match or an error occurred
        }



        // HCDB Decrypter drag and drop handler
        private void HCDBDecrypterDragDropHandler(object sender, DragEventArgs e)
        {
            LogDebugInfo("HCDB to SQL Conversion: Drag and Drop Initiated");
            TemporaryMessageHelper.ShowTemporaryMessage(HCDBDecrypterDragAreaText, "Processing....", 1000000);

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedItems = (string[])e.Data.GetData(DataFormats.FileDrop);
                List<string> validFiles = new List<string>();
                string message = string.Empty;
                int displayTime = 2000; // Default to 2 seconds

                foreach (var item in droppedItems)
                {
                    if (Directory.Exists(item))
                    {
                        var filesInDirectory = Directory.GetFiles(item, "*.hcdb", SearchOption.AllDirectories).ToList();
                        validFiles.AddRange(filesInDirectory);
                    }
                    else if (File.Exists(item) && Path.GetExtension(item).ToLowerInvariant() == ".hcdb")
                    {
                        validFiles.Add(item);
                    }
                }

                if (validFiles.Count > 0)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(HCDBDecrypterTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;
                        existingFilesSet.UnionWith(validFiles);
                        int newFilesCount = existingFilesSet.Count - initialCount;
                        int duplicatesCount = validFiles.Count - newFilesCount;

                        HCDBDecrypterTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);

                        string addedFilesMessage = newFilesCount > 0 ? $"{newFilesCount} HCDB files added" : "No new HCDB files added";
                        string duplicatesMessage = duplicatesCount > 0 ? $", {duplicatesCount} duplicates filtered" : "";
                        message = addedFilesMessage + duplicatesMessage;

                        LogDebugInfo($"HCDB to SQL Conversion: {newFilesCount} HCDB files added, {duplicatesCount} duplicates filtered");
                    });
                    displayTime = 1000; // Change display time since files were added
                }
                else
                {
                    message = "No HCDB files found.";
                    LogDebugInfo("HCDB to SQL Conversion: No HCDB files found in Drag and Drop.");
                }

                TemporaryMessageHelper.ShowTemporaryMessage(HCDBDecrypterDragAreaText, message, 2000);
            }
            else
            {
                LogDebugInfo("HCDB to SQL Conversion: Drag and Drop - No Data Present.");
                TemporaryMessageHelper.ShowTemporaryMessage(HCDBDecrypterDragAreaText, "Drag and Drop operation failed - No Data Present.", 2000);
            }
        }


        private async void ClickToBrowseHCDBDecryptHandler(object sender, RoutedEventArgs e)
        {
            LogDebugInfo("HCDB to SQL Conversion: Browsing for HCDB files Initiated");

            // Disable the main window to prevent interactions while the dialog is open
            this.IsEnabled = false;

            var openFileDialog = new VistaOpenFileDialog
            {
                Filter = "HCDB files (*.hcdb)|*.hcdb",
                Multiselect = true
            };

            string message = "No files selected.";
            int displayTime = 2000; // Default to 2 seconds for the message display.

            bool? result = openFileDialog.ShowDialog();

            // Await a short delay to absorb any unintentional clicks that happen after the dialog closes
            await Task.Delay(10); // Using a 10 ms delay 

            // Re-enable the main window after the delay
            this.IsEnabled = true;

            if (result == true)
            {
                var selectedFiles = openFileDialog.FileNames;

                if (selectedFiles.Any())
                {
                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(HCDBDecrypterTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;
                        existingFilesSet.UnionWith(selectedFiles); // Automatically removes duplicates
                        int newFilesCount = existingFilesSet.Count - initialCount;
                        int duplicatesCount = selectedFiles.Length - newFilesCount;

                        HCDBDecrypterTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);

                        string addedFilesMessage = newFilesCount > 0 ? $"{newFilesCount} HCDB files added" : "No new HCDB files added";
                        string duplicatesMessage = duplicatesCount > 1 ? $", {duplicatesCount} duplicates filtered" : "";
                        message = addedFilesMessage + duplicatesMessage;

                        LogDebugInfo($"HCDB to SQL Conversion: {newFilesCount} HCDB files added{duplicatesMessage} via File Browser");
                    });
                    displayTime = 1000; // Change display time since files were added
                }
                else
                {
                    message = "No HCDB files selected.";
                    LogDebugInfo("HCDB to SQL Conversion: No HCDB files selected in File Browser.");
                }
            }
            else
            {
                message = "Dialog cancelled.";
                LogDebugInfo("HCDB to SQL Conversion: File Browser - Dialog Cancelled.");
            }

            TemporaryMessageHelper.ShowTemporaryMessage(HCDBDecrypterDragAreaText, message, 2000);
        }
        private Task<bool> ConvertHcdbToSqlAsync(string[] filePaths)
        {
            LogDebugInfo($"HCDB to SQL Conversion: Starting conversion for {filePaths.Length} HCDB file(s)");

            // TODO: Implement the actual conversion logic here
            bool conversionResult = true; // Simulate success for now

            if (conversionResult)
            {
                LogDebugInfo("HCDB to SQL Conversion: Conversion process completed successfully");
            }
            else
            {
                LogDebugInfo("HCDB to SQL Conversion: Conversion process failed");
            }

            return Task.FromResult(conversionResult);
        }





        // TAB 4: Logic for  Ticket LST

        private async void TicketLSTDecrypterExecuteButtonClick(object sender, RoutedEventArgs e)
        {
            TemporaryMessageHelper.ShowTemporaryMessage(TicketLSTDecrypterDragAreaText, "Processing...", 1000000);
            string filesToDecrypt = TicketLSTDecrypterTextBox.Text;
            if (!string.IsNullOrWhiteSpace(filesToDecrypt))
            {
                string[] filePaths = filesToDecrypt.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                await Task.Delay(100); // Simulate some asynchronous operation
                bool decryptionSuccess = await DecryptLstFilesAsync(filePaths);
                string message = decryptionSuccess ? $"Success: {filePaths.Length} LST Files Decrypted (Simulated)" : "Decryption Failed";
                TemporaryMessageHelper.ShowTemporaryMessage(TicketLSTDecrypterDragAreaText, message, 2000);
            }
            else
            {
                TemporaryMessageHelper.ShowTemporaryMessage(TicketLSTDecrypterDragAreaText, "No LST files listed for decryption.", 2000);
            }
        }

        private void TicketLSTDecrypterDragDropHandler(object sender, DragEventArgs e)
        {
            LogDebugInfo("Ticket LST Encryption: Drag and Drop Initiated");
            TemporaryMessageHelper.ShowTemporaryMessage(TicketLSTDecrypterDragAreaText, "Processing...", 1000000);

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedItems = (string[])e.Data.GetData(DataFormats.FileDrop);
                List<string> validFiles = new List<string>();
                string message = string.Empty;
                int displayTime = 2000; // Default to 2 seconds

                foreach (var item in droppedItems)
                {
                    if (Directory.Exists(item))
                    {
                        var filesInDirectory = Directory.GetFiles(item, "*.lst", SearchOption.AllDirectories).ToList();
                        validFiles.AddRange(filesInDirectory);
                    }
                    else if (File.Exists(item) && Path.GetExtension(item).ToLowerInvariant() == ".lst")
                    {
                        validFiles.Add(item);
                    }
                }

                if (validFiles.Count > 0)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(TicketLSTDecrypterTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;
                        existingFilesSet.UnionWith(validFiles); // Automatically removes duplicates
                        int newFilesCount = existingFilesSet.Count - initialCount;
                        int duplicatesCount = validFiles.Count - newFilesCount;

                        TicketLSTDecrypterTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);

                        string addedFilesMessage = newFilesCount > 0 ? $"{newFilesCount} LST files added" : "No new LST files added";
                        string duplicatesMessage = duplicatesCount > 0 ? $", {duplicatesCount} duplicates filtered" : "";
                        message = addedFilesMessage + duplicatesMessage;

                        LogDebugInfo($"Ticket LST Encryption: {newFilesCount} LST files added, {duplicatesCount} duplicates filtered");
                    });
                    displayTime = 1000; // Change display time since files were added
                }
                else
                {
                    message = "No LST files found.";
                    LogDebugInfo("Ticket LST Encryption: No LST files found in Drag and Drop.");
                }

                TemporaryMessageHelper.ShowTemporaryMessage(TicketLSTDecrypterDragAreaText, message, 2000);
            }
            else
            {
                LogDebugInfo("Ticket LST Encryption: Drag and Drop - No Data Present.");
                TemporaryMessageHelper.ShowTemporaryMessage(TicketLSTDecrypterDragAreaText, "Drag and Drop operation failed - No Data Present.", 2000);
            }
        }



        private async void ClickToBrowseTicketLSTEncryptHandler(object sender, RoutedEventArgs e)
        {
            LogDebugInfo("Ticket LST Decryption: Browsing for LST files Initiated");

            // Disable the main window to prevent interactions while the dialog is open
            this.IsEnabled = false;

            var openFileDialog = new VistaOpenFileDialog
            {
                Filter = "LST files (*.lst)|*.lst",
                Multiselect = true
            };

            string message = "No files selected.";
            int displayTime = 2000; // Default to 2 seconds for the message display.

            bool? result = openFileDialog.ShowDialog();

            // Await a delay to absorb any unintentional clicks that happen after the dialog closes
            await Task.Delay(10); // 10 ms delay

            // Re-enable the main window after the delay
            this.IsEnabled = true;

            if (result == true)
            {
                var selectedFiles = openFileDialog.FileNames;

                if (selectedFiles.Any())
                {
                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(TicketLSTEncrypterTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;
                        existingFilesSet.UnionWith(selectedFiles); // Automatically removes duplicates
                        int newFilesCount = existingFilesSet.Count - initialCount;
                        int duplicatesCount = selectedFiles.Length - newFilesCount;

                        TicketLSTEncrypterTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);

                        string addedFilesMessage = newFilesCount > 0 ? $"{newFilesCount} LST files added" : "No new LST files added";
                        string duplicatesMessage = duplicatesCount > 1 ? $", {duplicatesCount} duplicates filtered" : "";
                        message = addedFilesMessage + duplicatesMessage;

                        LogDebugInfo($"Ticket LST Decryption: {newFilesCount} LST files added{duplicatesMessage} via File Browser");
                    });
                    displayTime = 1000; // Change display time since files were added
                }
                else
                {
                    message = "No LST files selected.";
                    LogDebugInfo("Ticket LST Decryption: No LST files selected in File Browser.");
                }
            }
            else
            {
                message = "Dialog cancelled.";
                LogDebugInfo("Ticket LST Decryption: File Browser - Dialog Cancelled.");
            }

            TemporaryMessageHelper.ShowTemporaryMessage(TicketLSTEncrypterDragAreaText, message, 2000);
        }

        // Not working

        private async Task<bool> DecryptLstFilesAsync(string[] filePaths)
        {
            LogDebugInfo($"Ticket LST Decryption: Starting decryption for {filePaths.Length} LST file(s)");
            bool allDecryptedSuccessfully = true;

            string outputDirectory = TicketListOutputDirectoryTextBox.Text;
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            foreach (string filePath in filePaths)
            {
                try
                {
                    byte[] fileData = File.ReadAllBytes(filePath);
                    byte[] decryptedData = null;

                    // Check for decryption header and version
                    if (fileData.Length > 8 && fileData[0] == 0xBE && fileData[1] == 0xE5 && fileData[2] == 0xBE && fileData[3] == 0xE5)
                    {
                        int version = BitConverter.ToInt32(fileData, 4);
                        byte[] dataToDecrypt = new byte[fileData.Length - 8];
                        Buffer.BlockCopy(fileData, 8, dataToDecrypt, 0, dataToDecrypt.Length);

                        // Handle decryption based on version
                        decryptedData = DecryptDataByVersion(dataToDecrypt, version);
                    }

                    if (decryptedData != null)
                    {
                        string outputFilePath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(filePath) + "_decrypted.lst");
                        File.WriteAllBytes(outputFilePath, decryptedData);
                        LogDebugInfo($"Decrypted file saved: {outputFilePath}");
                    }
                    else
                    {
                        LogDebugInfo($"Decryption failed for file: {filePath}");
                        allDecryptedSuccessfully = false;
                    }
                }
                catch (Exception ex)
                {
                    LogDebugInfo($"Error processing file {filePath}: {ex.Message}");
                    allDecryptedSuccessfully = false;
                }
            }

            return allDecryptedSuccessfully;
        }

        private byte[] DecryptDataByVersion(byte[] data, int version)
        {
            switch (version)
            {
                case 1:
                    return LIBSECURE.InitiateBlowfishBuffer(data, ToolsImpl.TicketListV1Key, ToolsImpl.TicketListV1IV, "CTR");
                case 0:
                    return LIBSECURE.InitiateBlowfishBuffer(data, ToolsImpl.TicketListV0Key, ToolsImpl.TicketListV0IV, "CTR");
                default:
                    LogDebugInfo($"Unsupported version: {version}");
                    return null;
            }
        }


        // TAB 4: Logic for Ticket LST Encryption

        private async void TicketLSTEncrypterExecuteButtonClick(object sender, RoutedEventArgs e)
        {
            LogDebugInfo("Ticket LST Encryption: Process Initiated");
            TemporaryMessageHelper.ShowTemporaryMessage(TicketLSTEncrypterDragAreaText, "Processing...", 1000000);

            string filesToEncrypt = TicketLSTEncrypterTextBox.Text;
            if (!string.IsNullOrWhiteSpace(filesToEncrypt))
            {
                string[] filePaths = filesToEncrypt.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                LogDebugInfo($"Ticket LST Encryption: Starting encryption for {filePaths.Length} files");
                bool encryptionSuccess = await EncryptLstFilesAsync(filePaths);

                string message = encryptionSuccess ? $"Success: {filePaths.Length} LST Files Encrypted (Simulated)" : "Encryption Failed";
                TemporaryMessageHelper.ShowTemporaryMessage(TicketLSTEncrypterDragAreaText, message, 2000);

                LogDebugInfo($"Ticket LST Encryption: Result - {message}");
            }
            else
            {
                LogDebugInfo("Ticket LST Encryption: Aborted - No LST files listed for encryption.");
                TemporaryMessageHelper.ShowTemporaryMessage(TicketLSTEncrypterDragAreaText, "No LST files listed for encryption.", 2000);
            }
        }

        private void TicketLSTEncrypterDragDropHandler(object sender, DragEventArgs e)
        {
            LogDebugInfo("Ticket LST Decryption: Drag and Drop Initiated");
            TemporaryMessageHelper.ShowTemporaryMessage(TicketLSTEncrypterDragAreaText, "Processing...", 1000000);

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedItems = (string[])e.Data.GetData(DataFormats.FileDrop);
                List<string> validFiles = new List<string>();
                string message = string.Empty;
                int displayTime = 2000; // Default to 2 seconds

                foreach (var item in droppedItems)
                {
                    if (Directory.Exists(item))
                    {
                        var filesInDirectory = Directory.GetFiles(item, "*.lst", SearchOption.AllDirectories).ToList();
                        validFiles.AddRange(filesInDirectory);
                    }
                    else if (File.Exists(item) && Path.GetExtension(item).ToLowerInvariant() == ".lst")
                    {
                        validFiles.Add(item);
                    }
                }

                if (validFiles.Count > 0)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(TicketLSTEncrypterTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;
                        existingFilesSet.UnionWith(validFiles); // Automatically removes duplicates
                        int newFilesCount = existingFilesSet.Count - initialCount;
                        int duplicatesCount = validFiles.Count - newFilesCount;

                        TicketLSTEncrypterTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);

                        string addedFilesMessage = newFilesCount > 0 ? $"{newFilesCount} LST files added" : "No new LST files added";
                        string duplicatesMessage = duplicatesCount > 0 ? $", {duplicatesCount} duplicates filtered" : "";
                        message = addedFilesMessage + duplicatesMessage;

                        LogDebugInfo($"Ticket LST Decryption: {newFilesCount} LST files added, {duplicatesCount} duplicates filtered");
                    });
                    displayTime = 1000; // Change display time since files were added
                }
                else
                {
                    message = "No LST files found.";
                    LogDebugInfo("Ticket LST Decryption: No LST files found in Drag and Drop.");
                }

                TemporaryMessageHelper.ShowTemporaryMessage(TicketLSTEncrypterDragAreaText, message, 2000);
            }
            else
            {
                LogDebugInfo("Ticket LST Decryption: Drag and Drop - No Data Present.");
                TemporaryMessageHelper.ShowTemporaryMessage(TicketLSTEncrypterDragAreaText, "Drag and Drop operation failed - No Data Present.", 2000);
            }
        }



        private async void ClickToBrowseTicketLSTDecryptHandler(object sender, RoutedEventArgs e)
        {
            LogDebugInfo("Ticket LST Encryption: Browsing for LST files Initiated");

            // Disable the main window to prevent interactions while the dialog is open
            this.IsEnabled = false;

            var openFileDialog = new VistaOpenFileDialog
            {
                Filter = "LST files (*.lst)|*.lst",
                Multiselect = true
            };

            string message = "No files selected.";
            int displayTime = 2000; // Default to 2 seconds for the message display.

            bool? result = openFileDialog.ShowDialog();

            // Await a delay to absorb any unintentional clicks that happen after the dialog closes
            await Task.Delay(10); // 10 ms delay

            // Re-enable the main window after the delay
            this.IsEnabled = true;

            if (result == true)
            {
                var selectedFiles = openFileDialog.FileNames;

                if (selectedFiles.Any())
                {
                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(TicketLSTDecrypterTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;
                        existingFilesSet.UnionWith(selectedFiles); // Automatically removes duplicates
                        int newFilesCount = existingFilesSet.Count - initialCount;
                        int duplicatesCount = selectedFiles.Length - newFilesCount;

                        TicketLSTDecrypterTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);

                        string addedFilesMessage = newFilesCount > 0 ? $"{newFilesCount} LST files added" : "No new LST files added";
                        string duplicatesMessage = duplicatesCount > 1 ? $", {duplicatesCount} duplicates filtered" : "";
                        message = addedFilesMessage + duplicatesMessage;

                        LogDebugInfo($"Ticket LST Encryption: {newFilesCount} LST files added{duplicatesMessage} via File Browser");
                    });
                    displayTime = 1000; // Change display time since files were added
                }
                else
                {
                    message = "No LST files selected.";
                    LogDebugInfo("Ticket LST Encryption: No LST files selected in File Browser.");
                }
            }
            else
            {
                message = "Dialog cancelled.";
                LogDebugInfo("Ticket LST Encryption: File Browser - Dialog Cancelled.");
            }

            TemporaryMessageHelper.ShowTemporaryMessage(TicketLSTDecrypterDragAreaText, message, 2000);
        }



        // Placeholder method for the LST file encryption process
        private Task<bool> EncryptLstFilesAsync(string[] filePaths)
        {
            LogDebugInfo($"Ticket LST Encryption: Starting encryption for {filePaths.Length} LST file(s)");

            // TODO: Implement the actual encryption logic here
            bool encryptionResult = true; // Simulate success for now

            if (encryptionResult)
            {
                LogDebugInfo("Ticket LST Encryption: Encryption process completed successfully");
            }
            else
            {
                LogDebugInfo("Ticket LST Encryption: Encryption process failed");
            }

            return Task.FromResult(encryptionResult);
        }



        // TAB 5: Scene IDs 

        private void SceneIDEncrypt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string input = SceneIDnumberInputTextBox.Text;
                bool isLegacyMode = legacyModeCheckBox.IsChecked ?? false;
                StringBuilder output = new StringBuilder();

                if (input.Contains('-'))
                {
                    var parts = input.Split('-');
                    if (parts.Length == 2 &&
                        ushort.TryParse(parts[0], out ushort start) &&
                        ushort.TryParse(parts[1], out ushort end) &&
                        start >= 1 && end <= 65535 && start <= end)
                    {
                        for (ushort i = start; i <= end; i++)
                        {
                            SceneKey key = isLegacyMode ? SIDKeyGenerator.Instance.Generate(i)
                                                         : SIDKeyGenerator.Instance.GenerateNewerType(i);
                            output.AppendLine($"{i}: {key.ToString()}");
                        }
                        encryptedSceneIDTextBox.Text = output.ToString();
                    }
                    else
                    {
                        encryptedSceneIDTextBox.Text = "Please enter a valid range (e.g., 1-50).\n";
                    }
                }
                else if (ushort.TryParse(input, out ushort sceneID) && sceneID >= 1 && sceneID <= 65535)
                {
                    SceneKey key = isLegacyMode ? SIDKeyGenerator.Instance.Generate(sceneID)
                                                 : SIDKeyGenerator.Instance.GenerateNewerType(sceneID);
                    encryptedSceneIDTextBox.Text = $"{sceneID}: {key.ToString()}";
                }
                else
                {
                    encryptedSceneIDTextBox.Text = "Please enter a valid number between 1 and 65535\nor a range (e.g., 1-5000).\n";
                }
            }
            catch (Exception ex)
            {
                encryptedSceneIDTextBox.Text += $"Error during encryption: {ex.Message}\n";
            }
        }



        private void SceneIDDecrypt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string input = SceneIDDecryptInputTextBox.Text;
                bool isLegacyMode = decrypterLegacyModeCheckBox.IsChecked ?? false;
                StringBuilder output = new StringBuilder();

                // Regular expression to match GUID format
                string guidPattern = @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b";
                MatchCollection matches = Regex.Matches(input, guidPattern);

                foreach (Match match in matches)
                {
                    string encryptedID = match.Value;
                    if (Guid.TryParse(encryptedID, out Guid sceneGuid))
                    {
                        SceneKey key = new SceneKey(sceneGuid);
                        ushort sceneID = isLegacyMode ? SIDKeyGenerator.Instance.ExtractSceneID(key)
                                                      : SIDKeyGenerator.Instance.ExtractSceneIDNewerType(key);
                        output.AppendLine($"{encryptedID}: {sceneID}");
                    }
                    else
                    {
                        output.AppendLine($"{encryptedID}: Invalid SceneID");
                    }
                }

                DecryptedSceneIDTextBox.Text = output.ToString();
            }
            catch (Exception ex)
            {
                DecryptedSceneIDTextBox.Text += $"Error during decryption: {ex.Message}\n";
            }
        }


        private void SceneIDTabItem_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                StringBuilder inputText = new StringBuilder();

                foreach (string file in files)
                {
                    if (Path.GetExtension(file).Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetExtension(file).Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetExtension(file).Equals(".lua", StringComparison.OrdinalIgnoreCase))
                    {
                        string fileContent = File.ReadAllText(file);
                        inputText.AppendLine(fileContent);
                    }
                }

                SceneIDDecryptInputTextBox.Text = inputText.ToString();

                // Optionally, automatically click the Decrypt button
                SceneIDDecrypt_Click(this, new RoutedEventArgs());
            }
        }

        private void ClearSceneIDTextHandler(object sender, RoutedEventArgs e)
        {
            // Clear the input and output text boxes
            SceneIDDecryptInputTextBox.Text = string.Empty;
            DecryptedSceneIDTextBox.Text = string.Empty;
            LogDebugInfo("SceneID Decrypter: List Cleared");
        }




        // TAB 6: Logic for Compiling LUA to LUAC

        private bool isParseOnly = false;
        private bool isStripDebug = false;
        private CancellationTokenSource _luaCompilerCancellationTokenSource;


        private async void LUACompilerExecuteButtonClick(object sender, RoutedEventArgs e)
        {
            _luaCompilerCancellationTokenSource?.Cancel();
            _luaCompilerCancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = _luaCompilerCancellationTokenSource.Token;

            LogDebugInfo("LUA Compilation to LUAC: Process Started");
            AppendTextToLUATextBox(Environment.NewLine + Environment.NewLine);

            string filesToCompile = LUACompilerTextBox.Text;
            if (!string.IsNullOrWhiteSpace(filesToCompile))
            {
                string[] allLines = filesToCompile.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                var validFilePaths = allLines
                    .Where(line => !line.TrimStart().StartsWith("--") &&
                                   !line.TrimStart().StartsWith("##") &&
                                   (line.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ||
                                    line.EndsWith(".LUA", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                var existingFiles = validFilePaths.Where(File.Exists).ToList();
                var nonExistingFiles = validFilePaths.Except(existingFiles).ToList();

                int totalSuccessCount = 0;
                int totalFailureCount = 0;

                string compilerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "HomeLuaC.exe");



                if (!isParseOnly && !Directory.Exists(_settings.LuacOutputDirectory))
                {
                    Directory.CreateDirectory(_settings.LuacOutputDirectory);
                    LogDebugInfo($"LUA Compilation to LUAC: Output directory created at {_settings.LuacOutputDirectory}");
                    TemporaryMessageHelper.ShowTemporaryMessage(LUACompilerDragAreaText, $"Created {_settings.LuacOutputDirectory}", 1000);

                }

                if (existingFiles.Any())
                {
                    foreach (string file in existingFiles)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            LUACompilerTextBox.Clear();
                            break;
                        }

                        if (isParseOnly)
                        {
                            TemporaryMessageHelper.ShowTemporaryMessage(LUACompilerDragAreaText, $"Parsing {Path.GetFileName(file)}...", 1000);

                            var (successCount, failureCount, parseResult) = await LUACompilerParseOnlyAsync(compilerPath, file, cancellationToken);
                            totalSuccessCount += successCount;
                            totalFailureCount += failureCount;
                            AppendTextToLUATextBox(parseResult);
                        }
                        else
                        {
                            TemporaryMessageHelper.ShowTemporaryMessage(LUACompilerDragAreaText, $"Compiling {Path.GetFileName(file)}...", 1000);

                            var compileResult = await LUACompilerCompileFileAsync(file, isStripDebug, cancellationToken);
                            AppendTextToLUATextBox(compileResult);

                            if (compileResult.Contains("-- Compile Success:"))
                            {
                                totalSuccessCount++;
                            }
                            else if (compileResult.Contains("--- ERROR:"))
                            {
                                totalFailureCount++;
                            }
                        }
                    }

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        string summaryMessage = isParseOnly ?
                            $"{totalSuccessCount} File(s) Parsed Successfully, {totalFailureCount} File(s) Failed to Parse" :
                            $"{totalSuccessCount} File(s) Compiled Successfully, {totalFailureCount} File(s) Failed to Compile";
                        TemporaryMessageHelper.ShowTemporaryMessage(LUACompilerDragAreaText, summaryMessage, 3000);
                    }
                }

                if (nonExistingFiles.Any() && !cancellationToken.IsCancellationRequested)
                {
                    string missingFilesMessage = string.Join(Environment.NewLine, nonExistingFiles.Select(file => "--- ERROR: File Not Found " + file));
                    LogDebugInfo("LUA Compilation to LUAC: Some files could not be found");
                    TemporaryMessageHelper.ShowTemporaryMessage(LUACompilerDragAreaText, "Some files could not be found", 3000);
                    AppendTextToLUATextBox(missingFilesMessage);
                }
            }
            else
            {
                LogDebugInfo("LUA Compilation to LUAC: Failed Initialisation as no input files received");
                TemporaryMessageHelper.ShowTemporaryMessage(LUACompilerDragAreaText, "No LUA files listed for compilation.", 3000);
            }
        }




        private async void AppendTextToLUATextBox(string text)
        {
            // If called from a non-UI thread, use Dispatcher
            await Dispatcher.InvokeAsync(async () =>
            {
                LUACompilerTextBox.AppendText(text);

                // Introduce a short delay
                await Task.Delay(20); // Delay for 100 milliseconds

                // Scrolls the text box to the end after appending text
                LUACompilerTextBox.ScrollToEnd();
            });
        }

        private async void StopandClearLUAListHandler(object sender, RoutedEventArgs e)
        {
            // Cancel the ongoing process
            _luaCompilerCancellationTokenSource?.Cancel();

            // Clear the LUACompilerTextBox
            LUACompilerTextBox.Clear();

            // Reset UI elements if necessary
            TemporaryMessageHelper.ShowTemporaryMessage(LUACompilerDragAreaText, "List Cleared", 2000);
        }


        private async Task<string> LUACompilerCompileFileAsync(string file, bool stripDebug, CancellationToken cancellationToken)
        {
            StringBuilder compileResult = new StringBuilder();

            string outputDirectory = _settings.LuacOutputDirectory;
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            string compilerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "HomeLuaC.exe");


            if (!File.Exists(compilerPath))
            {
                compileResult.AppendLine("Error: Compiler executable not found at " + compilerPath);
                return compileResult.ToString();
            }

            if (string.IsNullOrWhiteSpace(file) || file.TrimStart().StartsWith("--") || file.TrimStart().StartsWith("##"))
            {
                return "";
            }

            if (!Path.GetExtension(file).Equals(".lua", StringComparison.OrdinalIgnoreCase))
            {
                return $"--- Warning: {Path.GetFileName(file)} is not a LUA file. Skipped.";
            }

            string outputFileName = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(file) + ".LUAC");
            bool wasRenamed = false;

            switch (_settings.FileOverwriteBehavior)
            {
                case OverwriteBehavior.Rename:
                    int counter = 1;
                    string baseOutputFileName = outputFileName;
                    while (File.Exists(baseOutputFileName))
                    {
                        baseOutputFileName = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(file)}_{counter:D2}.LUAC");
                        counter++;
                    }
                    if (baseOutputFileName != outputFileName)
                    {
                        wasRenamed = true;
                        outputFileName = baseOutputFileName;
                    }
                    break;
                case OverwriteBehavior.Skip:
                    if (File.Exists(outputFileName))
                    {
                        return $"\n-- Skipping {Path.GetFileName(file)} as output already exists";
                    }
                    break;
                case OverwriteBehavior.Overwrite:
                default:
                    break;
            }

            string arguments = $"-o \"{outputFileName}\" \"{file}\"";
            if (stripDebug)
            {
                arguments = "-s " + arguments;
            }

            compileResult.AppendLine($"-- Compiling: {Path.GetFileName(file)}...");

            ProcessStartInfo startInfo = new ProcessStartInfo()
            {
                FileName = compilerPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (Process process = new Process { StartInfo = startInfo })
            {
                process.Start();
                await process.WaitForExitAsync();

                if (cancellationToken.IsCancellationRequested)
                {
                    return ""; // Exit if cancellation is requested
                }

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                if (File.Exists(outputFileName))
                {
                    string successMessage = $"-- Compile Success: '{outputFileName}' Created.";
                    if (wasRenamed)
                    {
                        successMessage += " (Renamed)";
                    }
                    compileResult.AppendLine(successMessage);
                }
                else
                {
                    string formattedError = ProcessCompileLUAErrorMessage(error);
                    compileResult.AppendLine($"--- ERROR: Compiling '{Path.GetFileName(file)}' failed. {formattedError}");
                }
            }

            return compileResult.ToString();
        }


        private string ProcessCompileLUAErrorMessage(string errorMessage)
        {
            int lastIndex = errorMessage.LastIndexOf(':');
            if (lastIndex >= 0)
            {
                int secondLastIndex = errorMessage.LastIndexOf(':', lastIndex - 1);
                if (secondLastIndex >= 0)
                {
                    string errorPart = errorMessage.Substring(secondLastIndex + 1).Trim();
                    return $"at Line {errorPart}";
                }
            }
            return errorMessage;
        }



        private async Task<(int successCount, int failureCount, string resultText)> LUACompilerParseOnlyAsync(string compilerPath, string file, CancellationToken cancellationToken)
        {
            LogDebugInfo($"LUA Compilation to LUAC (Parse Only): Processing {file}");

            int successCount = 0;
            int failureCount = 0;
            StringBuilder compileResults = new StringBuilder();

            ProcessStartInfo startInfo = new ProcessStartInfo()
            {
                FileName = compilerPath,
                Arguments = $"-p \"{file}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (Process process = new Process { StartInfo = startInfo })
            {
                process.Start();
                await process.WaitForExitAsync();

                if (cancellationToken.IsCancellationRequested)
                {
                    return (0, 0, ""); // Exit if cancellation is requested
                }

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                // Log the full error message for debugging purposes
                if (!string.IsNullOrWhiteSpace(error))
                {
                    LogDebugInfo($"LUA Compilation to LUAC (Parse Only): Error while parsing {file}: {error}");
                }

                string standardVersionMessage = "Playstation Home version of luac compiled at 14:36:10 on Jan 26 2009.";
                string[] outputLines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                if (outputLines.Length == 1 && outputLines[0].Trim() == standardVersionMessage && string.IsNullOrWhiteSpace(error))
                {
                    // Successful parsing
                    successCount++;
                    compileResults.AppendLine($"-- Parse Success: {Path.GetFileName(file)} No syntax errors found.");
                }
                else
                {
                    // Failure detected
                    failureCount++;

                    // Process error message to remove the full file path and shorten the message
                    string shortErrorMessage = ProcessLUAPARSEErrorMessage(error);
                    compileResults.AppendLine($"--- ERROR Parsing {Path.GetFileName(file)} {shortErrorMessage}");
                }
            }

            return (successCount, failureCount, compileResults.ToString());
        }


        private string ProcessLUAPARSEErrorMessage(string errorMessage)
        {
            // Find the last colon
            int lastIndex = errorMessage.LastIndexOf(':');

            if (lastIndex >= 0)
            {
                // Find the second-to-last colon before the last colon
                int secondLastIndex = errorMessage.LastIndexOf(':', lastIndex - 1);

                if (secondLastIndex >= 0)
                {
                    // Extract the relevant part of the error message
                    string errorPart = errorMessage.Substring(secondLastIndex + 1).Trim();

                    // Return the formatted error message
                    return $"at Line {errorPart}";
                }
            }

            // If the format is not as expected, return the original message
            return errorMessage;
        }




        private void LuaDragDropHandler(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedItems = (string[])e.Data.GetData(DataFormats.FileDrop);
                List<string> validFiles = new List<string>();
                string message = string.Empty;
                int displayTime = 2000; // Default to 2 seconds

                int newFilesCount = 0; // Declare outside of Dispatcher.Invoke
                int duplicatesCount = 0; // Declare outside of Dispatcher.Invoke

                foreach (var item in droppedItems)
                {
                    if (Directory.Exists(item))
                    {
                        var filesInDirectory = Directory.GetFiles(item, "*.lua", SearchOption.AllDirectories).ToList();
                        if (filesInDirectory.Count > 0)
                        {
                            validFiles.AddRange(filesInDirectory);
                        }
                    }
                    else if (File.Exists(item) && Path.GetExtension(item).ToLowerInvariant() == ".lua")
                    {
                        validFiles.Add(item);
                    }
                }

                if (validFiles.Count > 0)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(LUACompilerTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;
                        existingFilesSet.UnionWith(validFiles);
                        newFilesCount = existingFilesSet.Count - initialCount;
                        duplicatesCount = validFiles.Count - newFilesCount;

                        LUACompilerTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);
                    });
                    displayTime = 2000; // Change display time since files were added
                }
                else
                {
                    LogDebugInfo("LUA Compilation to LUAC: Dropped Files/Folders scanned - No LUA files found.");
                    message = "No LUA files found.";
                }

                // Construct the message after Dispatcher.Invoke has executed
                string addedFilesMessage = newFilesCount > 0 ? $"{newFilesCount} LUA files added" : "No new LUA files added";
                string duplicatesMessage = duplicatesCount > 1 ? $", {duplicatesCount} duplicates filtered" : "";
                message = addedFilesMessage + duplicatesMessage;
                string logPrefix = "LUA Compilation to LUAC: Drag and Drop - ";
                LogDebugInfo(logPrefix + addedFilesMessage); // This will log "Drag and Drop Info: X LUA files added" or "Drag and Drop Info: No new LUA files added"
                if (duplicatesCount > 0)
                {
                    // Ensure we clean up the message by trimming any leading comma and space
                    string cleanDuplicatesMessage = duplicatesMessage.TrimStart(',', ' ').Trim();
                    LogDebugInfo(logPrefix + cleanDuplicatesMessage); // This will log "Drag and Drop Info: X duplicates filtered" if there are any duplicates
                }

                TemporaryMessageHelper.ShowTemporaryMessage(LUACompilerDragAreaText, message, 2000);
            }
        }


        private async void ClickToBrowseHandlerLUACompiler(object sender, RoutedEventArgs e)
        {
            LogDebugInfo("LUA Compilation: Browsing for LUA files Initiated");

            // Disable the main window to prevent interactions while the dialog is open
            this.IsEnabled = false;

            var openFileDialog = new VistaOpenFileDialog
            {
                Filter = "LUA files (*.lua)|*.lua",
                Multiselect = true
            };

            string message = "No files selected.";
            int displayTime = 2000; // Default to 2 seconds for the message display.

            bool? result = openFileDialog.ShowDialog();

            // Await a delay to absorb any unintentional clicks that happen after the dialog closes
            await Task.Delay(10); // 10 ms delay

            // Re-enable the main window after the delay
            this.IsEnabled = true;

            if (result == true)
            {
                var selectedFiles = openFileDialog.FileNames;

                if (selectedFiles.Any())
                {
                    int newFilesCount = 0; // Declare outside of Dispatcher.Invoke
                    int duplicatesCount = 0; // Declare outside of Dispatcher.Invoke

                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(LUACompilerTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;
                        existingFilesSet.UnionWith(selectedFiles);
                        newFilesCount = existingFilesSet.Count - initialCount;
                        duplicatesCount = selectedFiles.Length - newFilesCount;

                        LUACompilerTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);
                    });

                    // Construct the message outside of the Dispatcher.Invoke block using the counts
                    string addedFilesMessage = newFilesCount > 0 ? $"{newFilesCount} LUA files added" : "No new LUA files added";
                    string duplicatesMessage = duplicatesCount > 1 ? $", {duplicatesCount} duplicates filtered" : "";
                    message = addedFilesMessage + duplicatesMessage;

                    displayTime = 1000; // Change display time since files were added
                    string logPrefix = "LUA Compilation to LUAC: Click to Browse - ";
                    LogDebugInfo(logPrefix + addedFilesMessage); // Logs added files
                    if (duplicatesCount > 0)
                    {
                        LogDebugInfo(logPrefix + $"{duplicatesCount} duplicates filtered"); // Logs duplicates if any
                    }
                }
                else
                {
                    LogDebugInfo("LUA Compilation to LUAC: Click to Browse - No LUA files were selected.");
                    message = "No LUA files selected.";
                }
            }
            else
            {
                LogDebugInfo("LUA Compilation to LUAC: File Browser - Dialog Cancelled.");
                message = "Dialog cancelled.";
            }

            TemporaryMessageHelper.ShowTemporaryMessage(LUACompilerDragAreaText, message, 2000);
        }


        private void LUACompilerTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Assuming LUACOutputDirectoryTextBox is the name of your TextBox containing the output directory path
            string outputDirectory = LUACOutputDirectoryTextBox.Text;

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
                }
                catch (Exception ex)
                {
                    // Handle any exceptions (e.g., directory does not exist)
                    LogDebugInfo($"LUA Compilation to LUAC: Unable to open directory: {ex.Message}");
                    MessageBox.Show($"Unable to open directory: {ex.Message}");
                }
            }
            else
            {
                LogDebugInfo($"LUA Compilation to LUAC: Output directory {outputDirectory} is not set or does not exist.");
                MessageBox.Show("Output directory is not set or does not exist.");
            }
        }

        private void CheckBoxLUACParseOnly_Checked(object sender, RoutedEventArgs e)
        {
            // Update the 'isParseOnly' property when the checkbox is checked
            isParseOnly = true;
            // If Parse Only is checked, we want to uncheck Strip Debug
            isStripDebug = false;
            CheckBoxStripDebug.IsChecked = false;
        }

        private void CheckBoxLUACParseOnly_Unchecked(object sender, RoutedEventArgs e)
        {
            // Update the 'isParseOnly' property when the checkbox is unchecked
            isParseOnly = false;
        }

        private void CheckBoxLUACStripDebug_Checked(object sender, RoutedEventArgs e)
        {
            // Update the 'isStripDebug' property when the checkbox is checked
            isStripDebug = true;
            // If Strip Debug is checked, we want to uncheck Parse Only
            isParseOnly = false;
            CheckBoxParseOnly.IsChecked = false;
        }

        private void CheckBoxLUACStripDebug_Unchecked(object sender, RoutedEventArgs e)
        {
            // Update the 'isStripDebug' property when the checkbox is unchecked
            isStripDebug = false;
        }




        // TAB 6: Logic for Decompiling LUAC to LUA

        private bool isNewTask = true; // Flag to check if a new task has started
        private string unluac122Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "unluac122.jar");
        private string unluac2023Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "unluac_2023_12_24.jar");
        private string unluacNETPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "unluac.exe");


        private string currentUnluacJarPath = "";


        // Event handlers for the radio buttons
        private void LUACDecompilerRadioButtonUnluac122_Checked(object sender, RoutedEventArgs e)
        {
            currentUnluacJarPath = unluac122Path;
        }

        private void LUACDecompilerRadioButtonUnluac2023_Checked(object sender, RoutedEventArgs e)
        {
            currentUnluacJarPath = unluac2023Path;
        }

        private void LUACDecompilerRadioButtonUnluacNET_Checked(object sender, RoutedEventArgs e)
        {
            currentUnluacJarPath = unluacNETPath;
        }

        // Modified LUACDecompilerExecuteButtonClick method
        private async void LUACDecompilerExecuteButtonClick(object sender, RoutedEventArgs e)
        {
            isNewTask = true;
            LogDebugInfo("LUAC Decompilation to LUA: Process Started");
            TemporaryMessageHelper.ShowTemporaryMessage(LUACDecompilerDragAreaText, "Initializing...", 3000);

            string unluacJarPath = currentUnluacJarPath;
            if (!File.Exists(unluacJarPath) && !unluacJarPath.Equals(unluacNETPath))
            {
                LogDebugInfo($"LUAC Decompilation to LUA: Error: {unluacJarPath} was not found.");
                TemporaryMessageHelper.ShowTemporaryMessage(LUACDecompilerDragAreaText, $"Error: {unluacJarPath} was not found.", 3000);
                return;
            }

            if (!Directory.Exists(_settings.LuaOutputDirectory))
            {
                Directory.CreateDirectory(_settings.LuaOutputDirectory);
                LogDebugInfo($"LUAC Decompilation to LUA: Output directory created at {_settings.LuaOutputDirectory}");
                TemporaryMessageHelper.ShowTemporaryMessage(LUACDecompilerDragAreaText, $"Created {_settings.LuaOutputDirectory}", 1000);
            }

            string filesToDecompile = LUACDecompilerTextBox.Text;
            if (!string.IsNullOrWhiteSpace(filesToDecompile))
            {
                string[] filePaths = filesToDecompile
                    .Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(line => !line.TrimStart().StartsWith("--") && !line.TrimStart().StartsWith("##") && line.EndsWith(".luac", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                int successCount = 0, failureCount = 0;

                foreach (string file in filePaths)
                {
                    TemporaryMessageHelper.ShowTemporaryMessage(LUACDecompilerDragAreaText, $"Decompiling {Path.GetFileName(file)}...", 10000);

                    var (isSuccess, message) = unluacJarPath.Equals(unluacNETPath) ?
                        await DecompileLuacFileWithExeAsync(file, unluacJarPath) :
                        await DecompileLuacFileAsync(file, unluacJarPath);

                    AppendTextToLUACDecompilerTextBox(message);

                    if (isSuccess)
                    {
                        successCount++;
                    }
                    else
                    {
                        failureCount++;
                    }
                }

                string summaryMessage = $"-- {successCount} file(s) decompiled successfully, {failureCount} file(s) failed/skipped.";
                AppendTextToLUACDecompilerTextBox(summaryMessage);
                LogDebugInfo($"LUAC Decompilation to LUA: {summaryMessage}");
                TemporaryMessageHelper.ShowTemporaryMessage(LUACDecompilerDragAreaText, summaryMessage, 6000);
            }
            else
            {
                LogDebugInfo("LUAC Decompilation to LUA: No input files were provided for decompilation.");
                TemporaryMessageHelper.ShowTemporaryMessage(LUACDecompilerDragAreaText, "No LUAC files listed for decompilation.", 3000);
            }
        }


        private async Task<(bool isSuccess, string message)> DecompileLuacFileWithExeAsync(string inputFile, string unluacExePath)
        {
            LogDebugInfo($"LUAC Decompilation: Starting decompilation for {inputFile}");

            string baseOutputFileName = Path.Combine(_settings.LuaOutputDirectory, Path.GetFileNameWithoutExtension(inputFile) + ".lua");
            string outputFileName = baseOutputFileName;
            bool renamed = false;

            switch (_settings.FileOverwriteBehavior)
            {
                case OverwriteBehavior.Rename:
                    int counter = 1;
                    while (File.Exists(outputFileName))
                    {
                        outputFileName = Path.Combine(_settings.LuaOutputDirectory, $"{Path.GetFileNameWithoutExtension(inputFile)}_{counter:D2}.lua");
                        counter++;
                        renamed = true;
                    }
                    break;
                case OverwriteBehavior.Skip:
                    if (File.Exists(outputFileName))
                    {
                        LogDebugInfo($"LUAC Decompilation: SKIPPED '{inputFile}' because '{outputFileName}' already exists.");
                        return (false, $"-- SKIPPED: {Path.GetFileName(inputFile)} output already exists - See settings to change");
                    }
                    break;
                case OverwriteBehavior.Overwrite:
                default:
                    break;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = unluacExePath,
                Arguments = $"\"{inputFile}\" \"{outputFileName}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                using (Process process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    bool exited = await Task.Run(() => process.WaitForExit(3000)); // 3-second timeout

                    if (!exited)
                    {
                        process.Kill();
                        LogDebugInfo($"LUAC Decompilation: Timeout occurred for {inputFile}");
                        return (false, $"--- ERROR: Decompilation timed out for '{Path.GetFileName(inputFile)}'.");
                    }

                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();

                    if (process.ExitCode == 0)
                    {
                        // Check if the output file exists
                        if (File.Exists(outputFileName))
                        {
                            LogDebugInfo($"LUAC Decompilation: Successfully decompiled {inputFile} to {outputFileName}");
                            return (true, $"-- Success: Decompiled '{Path.GetFileName(inputFile)}' to '{outputFileName}'" + (renamed ? " (Renamed)." : "."));
                        }
                        else
                        {
                            LogDebugInfo($"LUAC Decompilation: Output file '{outputFileName}' not found after decompilation for {inputFile}");
                            return (false, $"--- ERROR: Output file '{outputFileName}' not found after decompilation.");
                        }
                    }
                    else
                    {
                        LogDebugInfo($"LUAC Decompilation: Decompilation failed for {inputFile}");
                        return (false, $"--- ERROR: Decompiling '{Path.GetFileName(inputFile)}' failed. Error: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo($"LUAC Decompilation: Exception occurred for {inputFile}: {ex.Message}");
                return (false, $"--- ERROR: Exception while decompiling '{Path.GetFileName(inputFile)}'. Exception: {ex.Message}");
            }
        }



        private async Task<(bool isSuccess, string message)> DecompileLuacFileAsync(string inputFile, string unluacJarPath)
        {
            LogDebugInfo($"LUAC Decompilation: Starting decompilation for {inputFile}");

            string baseOutputFileName = Path.Combine(_settings.LuaOutputDirectory, Path.GetFileNameWithoutExtension(inputFile) + ".lua");
            string outputFileName = baseOutputFileName;
            bool renamed = false;

            switch (_settings.FileOverwriteBehavior)
            {
                case OverwriteBehavior.Rename:
                    int counter = 1;
                    while (File.Exists(outputFileName))
                    {
                        outputFileName = Path.Combine(_settings.LuaOutputDirectory, $"{Path.GetFileNameWithoutExtension(inputFile)}_{counter:D2}.lua");
                        counter++;
                        renamed = true;
                    }
                    break;
                case OverwriteBehavior.Skip:
                    if (File.Exists(outputFileName))
                    {
                        return (false, $"-- Decompiling: {Path.GetFileName(inputFile)}... Skipped '{outputFileName}' already exists.");
                    }
                    break;
                case OverwriteBehavior.Overwrite:
                default:
                    break;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "java",
                Arguments = $"-jar \"{unluacJarPath}\" \"{inputFile}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                using (Process process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    bool exited = await Task.Run(() => process.WaitForExit(2000)); // 1-second timeout

                    if (!exited)
                    {
                        process.Kill();
                        LogDebugInfo($"LUAC Decompilation: Timeout occurred for {inputFile}");
                        return (false, $"--- ERROR: Decompilation timed out for '{Path.GetFileName(inputFile)}'.");
                    }

                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();

                    if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    {
                        await File.WriteAllTextAsync(outputFileName, output);
                        if (File.Exists(outputFileName))
                        {
                            LogDebugInfo($"LUAC Decompilation: Successfully decompiled {inputFile} to {outputFileName}");
                            return (true, $"-- Success: Decompiled '{Path.GetFileName(inputFile)}' to '{outputFileName}'" + (renamed ? " (Renamed)." : "."));
                        }
                        else
                        {
                            LogDebugInfo($"LUAC Decompilation: Output file not found after decompilation for {inputFile}");
                            return (false, $"--- ERROR: Output file '{outputFileName}' not found after decompilation.");
                        }
                    }
                    else
                    {
                        LogDebugInfo($"LUAC Decompilation: Decompilation failed for {inputFile}");
                        return (false, $"--- ERROR: Decompiling '{Path.GetFileName(inputFile)}' failed. Error: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo($"LUAC Decompilation: Exception occurred for {inputFile}: {ex.Message}");
                return (false, $"--- ERROR: Exception while decompiling '{Path.GetFileName(inputFile)}'. Exception: {ex.Message}");
            }
        }



        private void AppendTextToLUACDecompilerTextBox(string text)
        {
            // If called from a non-UI thread, use Dispatcher
            Dispatcher.InvokeAsync(async () =>
            {
                // Check if this is a new task and prepend two new lines if it is
                if (isNewTask && !string.IsNullOrEmpty(LUACDecompilerTextBox.Text))
                {
                    LUACDecompilerTextBox.AppendText(Environment.NewLine + Environment.NewLine);
                    isNewTask = false; // Reset flag as this is no longer a new task
                }

                LUACDecompilerTextBox.AppendText(text + Environment.NewLine);

                // Introduce a short delay
                await Task.Delay(20); // Delay for 20 milliseconds

                // Scrolls the text box to the end after appending text
                LUACDecompilerTextBox.ScrollToEnd();
            });
        }





        private void LuacDragDropHandler(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedItems = (string[])e.Data.GetData(DataFormats.FileDrop);
                List<string> validFiles = new List<string>();
                string message = string.Empty;
                int displayTime = 2000; // Default to 2 seconds

                foreach (var item in droppedItems)
                {
                    if (Directory.Exists(item))
                    {
                        var filesInDirectory = Directory.GetFiles(item, "*.luac", SearchOption.AllDirectories).ToList();
                        if (filesInDirectory.Count > 0)
                        {
                            validFiles.AddRange(filesInDirectory);
                        }
                    }
                    else if (File.Exists(item) && Path.GetExtension(item).ToLowerInvariant() == ".luac")
                    {
                        validFiles.Add(item);
                    }
                }

                int newFilesCount = 0;
                int duplicatesCount = 0;

                if (validFiles.Count > 0)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(LUACDecompilerTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;
                        existingFilesSet.UnionWith(validFiles);
                        newFilesCount = existingFilesSet.Count - initialCount;
                        duplicatesCount = validFiles.Count - newFilesCount;

                        LUACDecompilerTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);
                    });
                    displayTime = 2000; // Change display time since files were added
                    LogDebugInfo($"LUAC Decompilation to LUA: {newFilesCount} new files added for decompilation.");
                    if (duplicatesCount > 0)
                    {
                        LogDebugInfo($"LUAC Decompilation to LUA: {duplicatesCount} duplicate files ignored.");
                    }
                }
                else
                {
                    LogDebugInfo("LUAC Decompilation to LUA: No valid .luac files found to decompile.");
                    message = "No LUAC files found";
                }

                // Construct the message after Dispatcher.Invoke has executed
                string addedFilesMessage = newFilesCount > 0 ? $"{newFilesCount} LUAC files added" : "No new LUAC files added";
                string duplicatesMessage = duplicatesCount > 1 ? $", {duplicatesCount} duplicates filtered" : "";
                message = addedFilesMessage + duplicatesMessage;

                // Log final message
                LogDebugInfo("LUAC Decompilation to LUA: Drag and Drop - " + message);

                TemporaryMessageHelper.ShowTemporaryMessage(LUACDecompilerDragAreaText, message, 2000);
            }
        }


        private async void ClickToBrowseHandlerLUACDecompiler(object sender, RoutedEventArgs e)
        {
            LogDebugInfo("LUAC Decompilation to LUA: Browsing for LUAC files Initiated");

            // Disable the main window to prevent interactions while the dialog is open
            this.IsEnabled = false;

            var openFileDialog = new VistaOpenFileDialog
            {
                Filter = "LUAC files (*.luac)|*.luac",
                Multiselect = true
            };

            string message = "No files selected.";
            int displayTime = 2000; // Default to 2 seconds for the message display.

            bool? result = openFileDialog.ShowDialog();

            // Await a delay to absorb any unintentional clicks that happen after the dialog closes
            await Task.Delay(10); // 10 ms delay

            // Re-enable the main window after the delay
            this.IsEnabled = true;

            if (result == true)
            {
                var selectedFiles = openFileDialog.FileNames;

                if (selectedFiles.Any())
                {
                    int newFilesCount = 0;
                    int duplicatesCount = 0;

                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(LUACDecompilerTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;
                        existingFilesSet.UnionWith(selectedFiles);
                        newFilesCount = existingFilesSet.Count - initialCount;
                        duplicatesCount = selectedFiles.Length - newFilesCount;

                        LUACDecompilerTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);
                    });

                    string addedFilesMessage = newFilesCount > 0 ? $"{newFilesCount} LUAC files added" : "No new LUAC files added";
                    string duplicatesMessage = duplicatesCount > 1 ? $", {duplicatesCount} duplicates filtered" : "";
                    message = addedFilesMessage + duplicatesMessage;
                    displayTime = 1000; // Change display time since files were added

                    string logPrefix = "LUAC Decompilation to LUA: Click to Browse - ";
                    LogDebugInfo(logPrefix + addedFilesMessage);
                    if (duplicatesCount > 0)
                    {
                        LogDebugInfo(logPrefix + $"{duplicatesCount} duplicates filtered");
                    }
                }
                else
                {
                    LogDebugInfo("LUAC Decompilation to LUA: Click to Browse - No LUAC files were selected.");
                    message = "No LUAC files selected.";
                }
            }
            else
            {
                message = "Dialog cancelled.";
                LogDebugInfo("LUAC Decompilation to LUA: File Browser - Dialog Cancelled.");
            }

            TemporaryMessageHelper.ShowTemporaryMessage(LUACDecompilerDragAreaText, message, 2000);
        }


        private void LUACDecompilerTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Assuming LUAOutputDirectoryTextBox is the name of your TextBox containing the output directory path
            string outputDirectory = LUAOutputDirectoryTextBox.Text;

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
                    LogDebugInfo($"LUAC Decompilation to LUA: Opened output directory {outputDirectory}");
                }
                catch (Exception ex)
                {
                    // Handle any exceptions
                    LogDebugInfo($"LUAC Decompilation to LUA: ERROR: Unable to open directory - {ex.Message}");
                    MessageBox.Show($"Unable to open directory: {ex.Message}");
                }
            }
            else
            {
                LogDebugInfo($"LUAC Decompilation to LUA: ERROR: Directory {outputDirectory} is not set or does not exist.");
                MessageBox.Show("Output directory is not set or does not exist.");
            }
        }

        // TAB 8: Logic for SDC Creation

        private void CreateSdcButton_Click(object sender, RoutedEventArgs e)
        {
            // Gather data from the form
            string name = sdcNameTextBox.Text;
            string description = sdcDescriptionTextBox.Text;
            string maker = sdcMakerTextBox.Text;
            string hdkVersion = sdcHDKVersionTextBox.Text;
            string archivePath = sdcServerArchivePathTextBox.Text;
            string archiveSize = sdcArchiveSizeTextBox.Text;
            string timestamp = sdcTimestampTextBox.Text;
            string thumbnailSuffix = sdcThumbnailSuffixTextBox.Text;

            // Construct the thumbnail image file names
            string makerImageSuffix = string.IsNullOrEmpty(thumbnailSuffix) ? "" : $"_T{thumbnailSuffix}";
            string smallImageSuffix = string.IsNullOrEmpty(thumbnailSuffix) ? "" : $"_T{thumbnailSuffix}";
            string largeImageSuffix = string.IsNullOrEmpty(thumbnailSuffix) ? "" : $"_T{thumbnailSuffix}";

            string makerImage = $"[THUMBNAIL_ROOT]maker{makerImageSuffix}.png";
            string smallImage = $"[THUMBNAIL_ROOT]small{smallImageSuffix}.png";
            string largeImage = $"[THUMBNAIL_ROOT]large{largeImageSuffix}.png";

            // Create the XML document
            XDocument sdcXml = new XDocument(
                new XElement("XML", new XAttribute("hdk_version", hdkVersion),
                    new XElement("SDC_VERSION", "1.0"),
                    new XElement("LANGUAGE", new XAttribute("REGION", "en-GB"),
                        new XElement("NAME", name),
                        new XElement("DESCRIPTION", description),
                        new XElement("MAKER", maker),
                        new XElement("MAKER_IMAGE", makerImage),
                        new XElement("SMALL_IMAGE", smallImage),
                        new XElement("LARGE_IMAGE", largeImage),

                        // Conditionally add the ARCHIVES element
                        (offlineSDCCheckBox.IsChecked != true) ?
                            new XElement("ARCHIVES",
                                new XElement("ARCHIVE", new XAttribute("size", archiveSize), new XAttribute("timestamp", timestamp), $"[CONTENT_SERVER_ROOT]{archivePath}")
                            ) : null
                    ),

                    // Duplicated language sections
                    AddDuplicatedLanguageSection("en-US", name, description, archivePath, archiveSize, timestamp),
                    AddDuplicatedLanguageSection("en-SG", name, description, archivePath, archiveSize, timestamp),
                    AddDuplicatedLanguageSection("en-CA", name, description, archivePath, archiveSize, timestamp),

                    AddDuplicatedLanguageSection("fr-FR", name, description, archivePath, archiveSize, timestamp),
                    AddDuplicatedLanguageSection("de-DE", name, description, archivePath, archiveSize, timestamp),
                    AddDuplicatedLanguageSection("it-IT", name, description, archivePath, archiveSize, timestamp),
                    AddDuplicatedLanguageSection("es-ES", name, description, archivePath, archiveSize, timestamp),
                    AddDuplicatedLanguageSection("ja-JP", name, description, archivePath, archiveSize, timestamp),
                    AddDuplicatedLanguageSection("ko-KR", name, description, archivePath, archiveSize, timestamp),
                    AddDuplicatedLanguageSection("zh-TW", name, description, archivePath, archiveSize, timestamp),
                    AddDuplicatedLanguageSection("zh-HK", name, description, archivePath, archiveSize, timestamp),

                    // Placeholder languages
                    // AddLanguagePlaceholder("fr-FR"),
                    // AddLanguagePlaceholder("de-DE"),
                    // AddLanguagePlaceholder("it-IT"),
                    // AddLanguagePlaceholder("es-ES"),
                    // AddLanguagePlaceholder("ja-JP"),
                    // AddLanguagePlaceholder("ko-KR"),
                    // AddLanguagePlaceholder("zh-TW"),
                    // AddLanguagePlaceholder("zh-HK"),
                    new XElement("age_rating", new XAttribute("minimum_age", "0"), new XAttribute("parental_control_level", "1"))
                    )
            );

            // Show save file dialog
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "SDC files (*.sdc)|*.sdc",
                DefaultExt = "sdc",
                FileName = "NewSDCFile"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                // Save the XML to the selected file path
                sdcXml.Save(saveFileDialog.FileName);
            }
        }

        // Function to add a language placeholder
        private XElement AddLanguagePlaceholder(string languageCode)
        {
            // Create the archive element conditionally
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
            XElement archivesElement = (offlineSDCCheckBox.IsChecked != true) ?
                new XElement("ARCHIVES",
                    new XElement("ARCHIVE",
                        new XAttribute("size", sdcArchiveSizeTextBox.Text),
                        new XAttribute("timestamp", sdcTimestampTextBox.Text),
                        $"[CONTENT_SERVER_ROOT]{sdcServerArchivePathTextBox.Text}")
                ) : null;
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.

            // Create and return the language element
            return new XElement("LANGUAGE", new XAttribute("REGION", languageCode),
                new XElement("NAME", ""),
                new XElement("DESCRIPTION", ""),
                archivesElement
            );
        }

        private XElement AddDuplicatedLanguageSection(string languageCode, string name, string description, string archivePath, string archiveSize, string timestamp)
        {
            // Create the archive element conditionally
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
            XElement archivesElement = (offlineSDCCheckBox.IsChecked != true) ?
                new XElement("ARCHIVES",
                    new XElement("ARCHIVE",
                        new XAttribute("size", archiveSize),
                        new XAttribute("timestamp", timestamp),
                        $"[CONTENT_SERVER_ROOT]{archivePath}")
                ) : null;
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.

            // Create and return the language element
            return new XElement("LANGUAGE",
                new XAttribute("REGION", languageCode),
                new XElement("NAME", name),
                new XElement("DESCRIPTION", description),
                archivesElement
            );
        }

        public event PropertyChangedEventHandler? PropertyChanged = delegate { };

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl && e.AddedItems.Count > 0)
            {
                // Assuming each TabItem's Tag property is set to the corresponding enum value
                var selectedTab = (TabItem)e.AddedItems[0];
                if (Enum.TryParse<RememberLastTabUsed>(selectedTab.Tag.ToString(), out var lastTabUsed))
                {
                    _settings.LastTabUsed = lastTabUsed;
                    SettingsManager.SaveSettings(_settings);
                }
            }
        }


        private void SelectLastUsedTab(RememberLastTabUsed lastTabUsed)
        {
            switch (lastTabUsed)
            {
                case RememberLastTabUsed.ArchiveTool:
                    MainTabControl.SelectedIndex = 0; // Index of ArchiveTool tab
                    break;
                case RememberLastTabUsed.CDSTool:
                    MainTabControl.SelectedIndex = 1; // Index of CDSTool tab
                    break;
                case RememberLastTabUsed.HCDBTool:
                    MainTabControl.SelectedIndex = 2; // Index of HCDBTool tab
                    break;
                case RememberLastTabUsed.TickLSTTool:
                    MainTabControl.SelectedIndex = 3; // Index of TickLSTTool tab
                    break;
                case RememberLastTabUsed.SceneIDTool:
                    MainTabControl.SelectedIndex = 4; // Index of SceneIDTool tab
                    break;
                case RememberLastTabUsed.LUACTool:
                    MainTabControl.SelectedIndex = 5; // Index of LUACTool tab
                    break;
                case RememberLastTabUsed.SDCODCTool:
                    MainTabControl.SelectedIndex = 6; // Index of SDCODCTool tab
                    break;
                case RememberLastTabUsed.Path2Hash:
                    MainTabControl.SelectedIndex = 7; // Index of Path2Hash tab
                    break;
                case RememberLastTabUsed.EbootPatcher:
                    MainTabControl.SelectedIndex = 8; // Index of EbootPatcher tab
                    break;
                case RememberLastTabUsed.SHAChecker:
                    MainTabControl.SelectedIndex = 9; // Index of SHAChecker tab
                    break;
                case RememberLastTabUsed.Video:
                    MainTabControl.SelectedIndex = 10; // Index of Video tab
                    break;
                default:
                    MainTabControl.SelectedIndex = 0; // Default to ArchiveTool tab if none is matched
                    break;
            }
        }




        private void sdcServerArchivePathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void sdcHDKVersionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void sdcMakerTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void sdcDescriptionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void sdcNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void TabContent_IsVisibleChanged(object sender, TextChangedEventArgs e)
        {

        }

        // TAB 8: Logic for SDC Creation

        private void ODCCreateODC_Click(object sender, RoutedEventArgs e)
        {
            // Gather data from the form
            string name = odcNameTextBox.Text;
            string description = odcDescriptionTextBox.Text;
            string maker = odcMakerTextBox.Text;
            string hdkVersion = odcHDKVersionTextBox.Text;
            string thumbnailSuffix = odcThumbnailSuffixTextBox.Text;
            string uuid = odcUUIDTextBox.Text;
            string timestamp = odcTimestampTextBox.Text;

            // Construct the thumbnail image file names
            string makerImage = $"[THUMBNAIL_ROOT]maker{(string.IsNullOrEmpty(thumbnailSuffix) ? "" : $"_T{thumbnailSuffix}")}.png";
            string smallImage = $"[THUMBNAIL_ROOT]small{(string.IsNullOrEmpty(thumbnailSuffix) ? "" : $"_T{thumbnailSuffix}")}.png";
            string largeImage = $"[THUMBNAIL_ROOT]large{(string.IsNullOrEmpty(thumbnailSuffix) ? "" : $"_T{thumbnailSuffix}")}.png";

            // Define the language codes and default values for name and description
            string[] languageCodes = new string[] { "en-GB", "fr-FR", "de-DE", "it-IT", "es-ES", "ja-JP", "ko-KR", "en-SG", "zh-HK", "zh-TW", "en-US" };
            bool[] defaultValues = new bool[] { true, false, false, false, false, false, false, false, false, false, false };

            // Create the legal section
            XElement legalSection = new XElement("legal",
                new XElement("age_rating", new XAttribute("minimum_age", "0"), new XAttribute("parental_control_level", "1")),
                new XElement("age_rating", new XAttribute("country", "US"), new XAttribute("minimum_age", "0"), new XAttribute("parental_control_level", "1")),
                new XElement("age_rating", new XAttribute("country", "JP"), new XAttribute("minimum_age", "0"), new XAttribute("parental_control_level", "1")),
                new XElement("age_rating", new XAttribute("country", "KR"), new XAttribute("minimum_age", "0"), new XAttribute("parental_control_level", "1")),
                new XElement("age_rating", new XAttribute("country", "SG"), new XAttribute("minimum_age", "0"), new XAttribute("parental_control_level", "1")),
                new XElement("age_rating", new XAttribute("country", "CA"), new XAttribute("minimum_age", "0"), new XAttribute("parental_control_level", "1"))
            );

            // Create the XML document
            XDocument odcXml = new XDocument(
                new XElement("odc",
                    new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
                    new XAttribute(XNamespace.Xmlns + "xsd", "http://www.w3.org/2001/XMLSchema"),
                    new XAttribute("hdk_version", hdkVersion),
                    new XAttribute("version", "1.0"),
                    new XElement("version", "1.0"),
                    new XElement("uuid", uuid),
                    // Add name elements for different languages
                    languageCodes.Select((code, index) =>
                        new XElement("name",
                            new XAttribute("lang", code),
                            new XAttribute("default", defaultValues[index].ToString().ToLower()),
                            name)),
                    // Add description elements for different languages
                    languageCodes.Select((code, index) =>
                        new XElement("description",
                            new XAttribute("lang", code),
                            new XAttribute("default", defaultValues[index].ToString().ToLower()),
                            description)),
                    new XElement("maker", new XAttribute("lang", "en-GB"), new XAttribute("default", "true"), maker),
                    new XElement("maker_image", new XAttribute("lang", "en-GB"), new XAttribute("default", "true"), makerImage),
                    new XElement("small_image", new XAttribute("lang", "en-GB"), new XAttribute("default", "true"), smallImage),
                    new XElement("large_image", new XAttribute("lang", "en-GB"), new XAttribute("default", "true"), largeImage),
                    new XElement("entitlements", string.Empty),
                    legalSection,
                    new XElement("timestamp", timestamp)
                )
            );

            // Show save file dialog
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {

                Filter = "ODC files (.odc)|.odc",
                DefaultExt = "odc",
                FileName = "NewODCFile"
            };
            if (saveFileDialog.ShowDialog() == true)
            {
                // Save the XML to the selected file path
                odcXml.Save(saveFileDialog.FileName);
            }
        }

        private string GenerateUUID()
        {
            Random random = new Random();
            Func<int, string> randomHex = length => new string(Enumerable.Repeat("0123456789ABCDEF", length)
                                                                   .Select(s => s[random.Next(s.Length)]).ToArray());

            return $"{randomHex(8)}-{randomHex(8)}-{randomHex(8)}-{randomHex(8)}";
        }

        private void ODCGenerateUUIDButton_Click(object sender, RoutedEventArgs e)
        {
            odcUUIDTextBox.Text = GenerateUUID();
        }



        private void odcUUIDTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void odcNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void odcDescriptionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void odcHDKVersionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void odcEntitlementIdTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void odcTimestampTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void SDCCreateSDCButton_Click_4(object sender, RoutedEventArgs e)
        {

        }

        private void RadioButtonBAR_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void RadioButtonBARSECURE_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void RadioButtonSDAT_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void RadioButtonSHARC_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void RadioButtonCORE_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBoxBruteforceUUID_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBoxSceneIDEncrypterLegacy_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBoxSceneIDDecrypterLegacy_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBoxTicketLSTLegacy_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void ArchiveCreatorTimestamp_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void MapperPathPrefixTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void CheckBoxMapCoredata_Checked(object sender, RoutedEventArgs e)
        {

        }


        private void sdcServerArchivePathTextBox_TextChanged_1(object sender, TextChangedEventArgs e)
        {

        }

        private void IncreaseCpuCores_Click(object sender, RoutedEventArgs e)
        {

        }

        private void DecreaseCpuCores_Click(object sender, RoutedEventArgs e)
        {

        }

        private void Path2HashButton_Click(object sender, RoutedEventArgs e)
        {
            string inputPath = Path2HashInputTextBox.Text;
            int hash = BarHash(inputPath);
            Path2HashOutputTextBox.Text = hash.ToString("X8").ToUpper();

            if (!string.IsNullOrWhiteSpace(Path2HashOutputExtraTextBox.Text))
            {
                var paths = Path2HashOutputExtraTextBox.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                Path2HashOutputExtraTextBox.Clear();

                foreach (var path in paths)
                {
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        string modifiedPath = path.Contains(":") ? path.Substring(path.IndexOf(":") + 1) : path;
                        int pathHash = BarHash(modifiedPath);
                        Path2HashOutputExtraTextBox.AppendText($"{pathHash.ToString("X8").ToUpper()}:{modifiedPath.ToUpper()}\n");
                    }
                }
            }
        }

        public static int BarHash(string path)
        {

            path = path.Replace('\\', '/').ToLower();

            int crc = 0;
            foreach (char c in path)
            {
                crc *= 0x25;
                crc += c;
            }
            return crc;
        }

        private void Path2HashClearListHandler(object sender, RoutedEventArgs e)
        {
            // Clear the input and output text boxes
            Path2HashInputTextBox.Text = string.Empty;
            Path2HashOutputTextBox.Text = string.Empty;
            Path2HashOutputExtraTextBox.Text = string.Empty;
            LogDebugInfo("Path to Hash: List Cleared");
        }

        private bool isAddToMapperButtonClickedOnce = false;

        // Field to keep track of the button click count.
        private int addToMapperButtonClickCount = 0;

        private async void AddToMapperButton_Click(object sender, RoutedEventArgs e)
        {
            // Handle the first click.
            if (addToMapperButtonClickCount == 0)
            {
                Path2HashDragAreaText.Text = "Add these paths to the mapper helper? Click again to confirm";
                addToMapperButtonClickCount++;
                return;
            }
            // Handle the second click.
            else if (addToMapperButtonClickCount == 1)
            {
                Path2HashDragAreaText.Text = "Adding too many useless paths here could slow down mapping, Are you sure?";
                addToMapperButtonClickCount++;
                return;
            }

            // Handle the third click - Proceed with adding paths.
            if (addToMapperButtonClickCount >= 2)
            {
                string helperFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scene_file_mapper_helper.txt");
                HashSet<string> existingPaths = new HashSet<string>();

                if (File.Exists(helperFilePath))
                {
                    // Read existing content and remove trailing new lines
                    var existingContent = File.ReadAllText(helperFilePath).TrimEnd('\r', '\n');
                    using (var writer = new StreamWriter(helperFilePath, false)) // Overwrite to remove trailing new lines
                    {
                        writer.Write(existingContent);
                        if (!string.IsNullOrEmpty(existingContent))
                        {
                            // Ensure there's a new line after existing content if it's not empty
                            writer.WriteLine();
                        }

                        // Process paths from text boxes
                        var uniquePaths = new HashSet<string>();
                        ProcessTextBoxForUniquePaths(Path2HashOutputExtraTextBox.Text, existingPaths, uniquePaths);
                        ProcessTextBoxForUniquePaths(Path2HashInputTextBox.Text, existingPaths, uniquePaths, true);

                        foreach (var path in uniquePaths)
                        {
                            if (!string.IsNullOrWhiteSpace(path))
                            {
                                int hash = BarHash(path);
                                string hashString = hash.ToString("X8").ToUpper();
                                if (hashString != "00000000")
                                {
                                    writer.WriteLine($"{hashString}:{path.ToUpper()}");
                                }
                            }
                        }
                    }
                }
                else
                {
                    // File doesn't exist, create new file and add paths
                    using (var writer = new StreamWriter(helperFilePath))
                    {
                        var uniquePaths = new HashSet<string>();
                        ProcessTextBoxForUniquePaths(Path2HashOutputExtraTextBox.Text, existingPaths, uniquePaths);
                        ProcessTextBoxForUniquePaths(Path2HashInputTextBox.Text, existingPaths, uniquePaths, true);

                        foreach (var path in uniquePaths)
                        {
                            if (string.IsNullOrWhiteSpace(path)) continue;
                            int hash = BarHash(path);
                            writer.WriteLine($"{hash.ToString("X8").ToUpper()}:{path.ToUpper()}");
                        }
                    }
                }

                // After adding paths, update the text and use a delay to revert the message.
                Path2HashDragAreaText.Text = "Paths added to scene_file_mapper_helper.txt";
                await Task.Delay(1000); // Wait for one second
                Path2HashDragAreaText.Text = "Drag a folder or TXT/XML here to hash all paths";


            }
        }



        private void ProcessTextBoxForUniquePaths(string textBoxContent, HashSet<string> existingPaths, HashSet<string> uniquePaths, bool isInput = false)
        {
            var lines = textBoxContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                string path = isInput ? line : line.Contains(":") ? line.Split(':')[1] : line;
                path = path.Trim();
                if (!existingPaths.Contains(path.ToUpper()) && !uniquePaths.Contains(path.ToUpper()) && !string.IsNullOrWhiteSpace(path))
                {
                    uniquePaths.Add(path.ToUpper());
                }
            }
        }


        private void Path2HashTabItem_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedObjects = (string[])e.Data.GetData(DataFormats.FileDrop);
                StringBuilder outputText = new StringBuilder();
                HashSet<string> uniquePaths = new HashSet<string>();

                foreach (string droppedObject in droppedObjects)
                {
                    if (Directory.Exists(droppedObject))
                    {
                        // Process each file in the directory and add to uniquePaths
                        IEnumerable<string> fileEntries = Directory.EnumerateFiles(droppedObject, "*", SearchOption.AllDirectories);
                        foreach (string fullFileName in fileEntries)
                        {
                            string relativePath = fullFileName.Substring(droppedObject.Length + 1).ToUpper(); // +1 to remove the leading backslash and convert to uppercase
                            uniquePaths.Add(relativePath);
                        }
                    }
                    else if (File.Exists(droppedObject))
                    {
                        // Check if the file is of a specific type
                        var extension = Path.GetExtension(droppedObject).ToLower();
                        if (extension == ".txt" || extension == ".xml")
                        {
                            // Search within the file for paths
                            var fileContent = File.ReadAllText(droppedObject);
                            Regex regex = new Regex("\"([^\"]*\\.scene)\"");
                            var matches = regex.Matches(fileContent);

                            foreach (Match match in matches)
                            {
                                // Extract the path from the match, convert to uppercase, and add to uniquePaths
                                string path = match.Groups[1].Value.ToUpper();
                                uniquePaths.Add(path);
                            }
                        }
                    }
                }

                // Process unique paths, hash them, and format the output in uppercase
                foreach (string path in uniquePaths)
                {
                    int hash = BarHash(path);
                    outputText.AppendLine($"{hash:X8}:{path}");
                }

                Path2HashOutputExtraTextBox.Text = outputText.ToString();
            }
        }



        private void TabItem_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }




        private int previousFileCount = 0;
        private CancellationTokenSource cancellationTokenSource;

        private async void Sha1DragArea_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedItems = (string[])e.Data.GetData(DataFormats.FileDrop);

                int fileCount = droppedItems.Select(item =>
                    Directory.Exists(item) ? Directory.GetFiles(item, "*", SearchOption.AllDirectories).Length : 1)
                    .Sum();

                if (fileCount > 2000)
                {
                    if (previousFileCount == fileCount)
                    {
                        cancellationTokenSource?.Cancel();
                        cancellationTokenSource = new CancellationTokenSource();
                        ProcessFiles(droppedItems, cancellationTokenSource.Token);
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                            TemporaryMessageHelper.ShowTemporaryMessage(SHA1DragAreaText, "Over 2000 files - Drag same files again to confirm?", 5000)
                        );
                        previousFileCount = fileCount;
                    }
                }
                else
                {
                    cancellationTokenSource?.Cancel();
                    cancellationTokenSource = new CancellationTokenSource();
                    ProcessFiles(droppedItems, cancellationTokenSource.Token);
                }
            }
        }

        private async void ProcessFiles(string[] droppedItems, CancellationToken cancellationToken)
        {
            Sha1TextBox.Clear();
            Dispatcher.Invoke(() =>
                TemporaryMessageHelper.ShowTemporaryMessage(SHA1DragAreaText, "Generating SHA1s...", 10000)
            );

            int fileCount = 0;
            await Task.Run(() =>
            {
                foreach (string item in droppedItems)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    if (Directory.Exists(item))
                    {
                        string[] files = Directory.GetFiles(item, "*", SearchOption.AllDirectories);
                        foreach (string file in files)
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                return;
                            }
                            ComputeAndAppendSha1(file);
                            fileCount++;
                            UpdateMessageWithCount(fileCount);
                        }
                    }
                    else if (File.Exists(item))
                    {
                        ComputeAndAppendSha1(item);
                        fileCount++;
                        UpdateMessageWithCount(fileCount);
                    }
                }
            });

            Dispatcher.Invoke(() =>
                TemporaryMessageHelper.ShowTemporaryMessage(SHA1DragAreaText, $"{fileCount} Files Done", 1000)
            );
            previousFileCount = 0;
        }

        private void UpdateMessageWithCount(int count)
        {
            Dispatcher.Invoke(() =>
            {
                TemporaryMessageHelper.ShowTemporaryMessage(SHA1DragAreaText, $"Generating SHA1s... {count} files done", 10000);
            });
        }

        private void ComputeAndAppendSha1(string file)
        {
            LogDebugInfo($"SHA1 Generator: Processing file: {file}");
            try
            {
                string sha1Hash = ComputeSha1(file);
                string fileName = Path.GetFileName(file);
                Dispatcher.Invoke(() => Sha1TextBox.AppendText($"SHA1: {sha1Hash} - File: {file}{Environment.NewLine}"));
                LogDebugInfo($"SHA1 Generator: {fileName} = {sha1Hash}");
            }
            catch (Exception ex)
            {
                string fileName = Path.GetFileName(file);
                Dispatcher.Invoke(() => Sha1TextBox.AppendText($"Error processing {file}: {ex.Message}{Environment.NewLine}"));
                LogDebugInfo($"SHA1 Generator: Error processing {fileName}: {ex.Message}");
            }
        }

        private string ComputeSha1(string filePath)
        {
            using (var sha1 = System.Security.Cryptography.SHA1.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] checksum = sha1.ComputeHash(stream);
                    return BitConverter.ToString(checksum).Replace("-", String.Empty);
                }
            }
        }

        private async void ClearSHA1Button_Click(object sender, RoutedEventArgs e)
        {
            cancellationTokenSource?.Cancel();


            await Task.Delay(20);


            Sha1TextBox.Clear();
            Dispatcher.Invoke(() =>
            {
                TemporaryMessageHelper.ShowTemporaryMessage(SHA1DragAreaText, "SHA1 list cleared", 1000);
            });
        }


        private void CopySHA1Button_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(Sha1TextBox.Text);
            Dispatcher.Invoke(() =>
            {
                TemporaryMessageHelper.ShowTemporaryMessage(SHA1DragAreaText, "List Copied to Clipboard", 2000);
            });
        }

        private async void SHA1BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "All files (*.*)|*.*", // You can adjust this to specific file types
                Multiselect = true // Allow multiple file selection
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var selectedFiles = openFileDialog.FileNames;

                // Following the same logic as your Sha1DragArea_Drop event
                int fileCount = selectedFiles.Select(item =>
                    Directory.Exists(item) ? Directory.GetFiles(item, "*", SearchOption.AllDirectories).Length : 1)
                    .Sum();

                if (fileCount > 2000)
                {
                    if (previousFileCount == fileCount)
                    {
                        cancellationTokenSource?.Cancel();
                        cancellationTokenSource = new CancellationTokenSource();
                        ProcessFiles(selectedFiles, cancellationTokenSource.Token);
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                            TemporaryMessageHelper.ShowTemporaryMessage(SHA1DragAreaText, "Over 2000 files - Drag same files again to confirm?", 5000)
                        );
                        previousFileCount = fileCount;
                    }
                }
                else
                {
                    cancellationTokenSource?.Cancel();
                    cancellationTokenSource = new CancellationTokenSource();
                    ProcessFiles(selectedFiles, cancellationTokenSource.Token);
                }
            }
        }


        // Tab 9 video converter

        private async void VideoDragArea_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedItems = (string[])e.Data.GetData(DataFormats.FileDrop);
                cancellationTokenSource?.Cancel();
                cancellationTokenSource = new CancellationTokenSource();
                await ProcessVideoFiles(droppedItems, cancellationTokenSource.Token);
            }
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
                        Dispatcher.Invoke(() => VideoTextBox.AppendText($"Video file: {file}{Environment.NewLine}"));
                        fileCount++;
                    }
                }
            });

            Dispatcher.Invoke(() =>
                TemporaryMessageHelper.ShowTemporaryMessage(VideoDragAreaText, $"{fileCount} video files processed.", 1000)
            );
        }

        private async void VideoBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Video files (*.mp4; *.avi; *.mov; *.mpeg; *.mpg; *.mkv; *.wmv; *.flv; *.webm; *.ogv; *.3gp)|*.mp4;*.avi;*.mov;*.mpeg;*.mpg;*.mkv;*.wmv;*.flv;*.webm;*.ogv;*.3gp|All files (*.*)|*.*",

                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var selectedFiles = openFileDialog.FileNames;
                cancellationTokenSource?.Cancel();
                cancellationTokenSource = new CancellationTokenSource();
                await ProcessVideoFiles(selectedFiles, cancellationTokenSource.Token);
            }
        }


        private void ClearVideoButton_Click(object sender, RoutedEventArgs e)
        {
            cancellationTokenSource?.Cancel();
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
            string outputDirectory = VideoOutputDirectoryTextBox.Text.Trim();

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
            if (Audio128kRadioButton.IsChecked == true)
                audioBitrate = "128k";
            else if (Audio160kRadioButton.IsChecked == true)
                audioBitrate = "160k";
            else if (Audio256kRadioButton.IsChecked == true)
                audioBitrate = "256k";
            else if (Audio320kRadioButton.IsChecked == true)
                audioBitrate = "320k";

            int audioBoost = 0;
            if (VideoAudioBoost3CheckBox.IsChecked == true) audioBoost += 3;
            if (VideoAudioBoost6CheckBox.IsChecked == true) audioBoost += 6;
            string audioFilter = audioBoost > 0 ? $",volume={audioBoost}dB" : "";

            string videoCodec = VideoAVCh264CheckBox.IsChecked ?? false ? "libx264" : "mpeg4";
            string additionalArgs = VideoAVCh264CheckBox.IsChecked ?? false ? "-profile:v high -preset slow" : "";
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
                        Dispatcher.Invoke(() => VideoTextBox.AppendText(args.Data + Environment.NewLine));
                    }
                };
                ffmpegProcess.BeginOutputReadLine();
                ffmpegProcess.ErrorDataReceived += (s, args) =>
                {
                    if (args.Data != null)
                        Dispatcher.Invoke(() => VideoTextBox.AppendText("Conversion: " + args.Data + Environment.NewLine));
                };
                ffmpegProcess.BeginErrorReadLine();

                await ffmpegProcess.WaitForExitAsync();
                if (ffmpegProcess.ExitCode == 0)
                {
                    Dispatcher.Invoke(() => VideoTextBox.AppendText("Conversion successful. Converted video located at: " + outputFilePath + Environment.NewLine));
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

            if (Video240pRadioButton.IsChecked == true)
                return isFourThree ? ("320:240", "1000k") : ("426:240", "1000k");
            else if (Video360pRadioButton.IsChecked == true)
                return isFourThree ? ("480:360", "1800k") : ("640:360", "1800k");
            else if (Video480pRadioButton.IsChecked == true)
                return isFourThree ? ("640:480", "2500k") : ("854:480", "2500k");
            else if (Video576pRadioButton.IsChecked == true)
                return isFourThree ? ("768:576", "3500k") : ("1024:576", "3500k");
            else if (Video720pRadioButton.IsChecked == true)
                return isFourThree ? ("960:720", "4500k") : ("1280:720", "4500k");
            else if (Video1080pRadioButton.IsChecked == true)
                return isFourThree ? ("1440:1080", "6000k") : ("1920:1080", "7000k");
            else if (Video4KRadioButton.IsChecked == true)
                return isFourThree ? ("2880:2160", "15000k") : ("3840:2160", "20000k");
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
            Dispatcher.Invoke(() => VideoTextBox.AppendText($"Starting download from {url}...\n"));
            using (var client = new WebClient())
            {
                await client.DownloadFileTaskAsync(new Uri(url), path);
            }
            Dispatcher.Invoke(() => VideoTextBox.AppendText("Download completed.\n"));
        }

        private async Task DownloadAndExtractFfmpeg(string dependenciesPath, string ffmpegPath)
        {
            string zipPath = Path.Combine(dependenciesPath, "ffmpeg.zip");
            string extractPath = Path.Combine(dependenciesPath, "ffmpeg-extract");

            // Download ffmpeg
            Dispatcher.Invoke(() => VideoTextBox.AppendText("Downloading ffmpeg...\n"));
            using (var client = new WebClient())
            {
                await client.DownloadFileTaskAsync(new Uri("https://github.com/yt-dlp/FFmpeg-Builds/releases/download/autobuild-2024-05-12-14-08/ffmpeg-n7.0-28-ge7d2238ad7-win64-gpl-7.0.zip"), zipPath);
            }
            Dispatcher.Invoke(() => VideoTextBox.AppendText("ffmpeg downloaded. Extracting...\n"));

            // Extract ffmpeg
            ZipFile.ExtractToDirectory(zipPath, extractPath);
            File.Move(Path.Combine(extractPath, "ffmpeg-n7.0-28-ge7d2238ad7-win64-gpl-7.0", "bin", "ffmpeg.exe"), ffmpegPath);
            Dispatcher.Invoke(() => VideoTextBox.AppendText("ffmpeg extracted successfully.\n"));

            // Cleanup
            Directory.Delete(extractPath, true);
            File.Delete(zipPath);
            Dispatcher.Invoke(() => VideoTextBox.AppendText("Cleanup completed.\n"));
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
                            Dispatcher.Invoke(() => VideoTextBox.AppendText(args.Data + Environment.NewLine));
                        }
                    };
                    process.BeginOutputReadLine();

                    process.ErrorDataReceived += (s, args) =>
                    {
                        if (args.Data != null)
                        {
                            Dispatcher.Invoke(() => VideoTextBox.AppendText("Error: " + args.Data + Environment.NewLine));
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


        private string ParseForMergedFilePath(Queue<string> lastFewLines)
        {
            string mergePrefix = "[Merger] Merging formats into ";
            foreach (var line in lastFewLines)
            {
                if (line.StartsWith(mergePrefix))
                {
                    return line.Substring(mergePrefix.Length).Trim('"');
                }
            }
            return null;
        }




        private void CacheTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void CacheButton_Click(object sender, RoutedEventArgs e)
        {

        }

        private void CacheOutputTextBox(object sender, TextChangedEventArgs e)
        {

        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBox_Checked_1(object sender, RoutedEventArgs e)
        {

        }

        private void InfTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void CheckBoxOfflineSDC_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void CacheZipExtractandRename_Click(object sender, RoutedEventArgs e)
        {

        }

        private void CacheZipClearFileList_Click(object sender, RoutedEventArgs e)
        {

        }

        private void CacheZipTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void INFEncryptDecryptFileList_Click(object sender, RoutedEventArgs e)
        {

        }

        private void CacheZipDeleteReservedCheckBox_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void CacheZipRenameCheckBox_Checked(object sender, RoutedEventArgs e)
        {

        }

        // Settings Tab - Set directories
        private void CdsBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var folderPath = OpenFolderDialog(CdsOutputDirectoryTextBox.Text);
            if (folderPath != null)
            {
                CdsOutputDirectoryTextBox.Text = folderPath;
            }
        }

        private void CdsOpenButton_Click(object sender, RoutedEventArgs e)
        {
            string outputDirectory = CdsOutputDirectoryTextBox.Text;

            try
            {
                // Check if the directory exists
                if (!Directory.Exists(outputDirectory))
                {
                    // Create the directory if it does not exist
                    Directory.CreateDirectory(outputDirectory);
                    MessageBox.Show($"Directory created: {outputDirectory}");
                }

                // Open the directory in File Explorer
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = outputDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                // Handle any exceptions (e.g., invalid path or lack of permissions)
                MessageBox.Show($"Error opening directory: {ex.Message}");
            }
        }


        private void BarSdatSharcBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var folderPath = OpenFolderDialog(BarSdatSharcOutputDirectoryTextBox.Text);
            if (folderPath != null)
            {
                BarSdatSharcOutputDirectoryTextBox.Text = folderPath;
            }
        }

        private void BarSdatSharcOpenButton_Click(object sender, RoutedEventArgs e)
        {
            string outputDirectory = BarSdatSharcOutputDirectoryTextBox.Text;

            try
            {
                // Check if the directory exists
                if (!Directory.Exists(outputDirectory))
                {
                    // Create the directory if it does not exist
                    Directory.CreateDirectory(outputDirectory);
                    MessageBox.Show($"Directory created: {outputDirectory}");
                }

                // Open the directory in File Explorer
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = outputDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                // Handle any exceptions (e.g., invalid path or lack of permissions)
                MessageBox.Show($"Error opening directory: {ex.Message}");
            }
        }

        private void MappedBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var folderPath = OpenFolderDialog(MappedOutputDirectoryTextBox.Text);
            if (folderPath != null)
            {
                MappedOutputDirectoryTextBox.Text = folderPath;
            }
        }

        private void MappedOpenButton_Click(object sender, RoutedEventArgs e)
        {
            string outputDirectory = MappedOutputDirectoryTextBox.Text;

            try
            {
                // Check if the directory exists
                if (!Directory.Exists(outputDirectory))
                {
                    // Create the directory if it does not exist
                    Directory.CreateDirectory(outputDirectory);
                    MessageBox.Show($"Directory created: {outputDirectory}");
                }

                // Open the directory in File Explorer
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = outputDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                // Handle any exceptions (e.g., invalid path or lack of permissions)
                MessageBox.Show($"Error opening directory: {ex.Message}");
            }
        }


        private void HcdbBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var folderPath = OpenFolderDialog(HcdbOutputDirectoryTextBox.Text);
            if (folderPath != null)
            {
                HcdbOutputDirectoryTextBox.Text = folderPath;
            }
        }

        private void HcdbOpenButton_Click(object sender, RoutedEventArgs e)
        {
            string outputDirectory = HcdbOutputDirectoryTextBox.Text;

            try
            {
                // Check if the directory exists
                if (!Directory.Exists(outputDirectory))
                {
                    // Create the directory if it does not exist
                    Directory.CreateDirectory(outputDirectory);
                    MessageBox.Show($"Directory created: {outputDirectory}");
                }

                // Open the directory in File Explorer
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = outputDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                // Handle any exceptions (e.g., invalid path or lack of permissions)
                MessageBox.Show($"Error opening directory: {ex.Message}");
            }
        }

        private void sqlBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var folderPath = OpenFolderDialog(sqlOutputDirectoryTextBox.Text);
            if (folderPath != null)
            {
                sqlOutputDirectoryTextBox.Text = folderPath;
            }
        }

        private void sqlOpenButton_Click(object sender, RoutedEventArgs e)
        {
            string outputDirectory = sqlOutputDirectoryTextBox.Text;

            try
            {
                // Check if the directory exists
                if (!Directory.Exists(outputDirectory))
                {
                    // Create the directory if it does not exist
                    Directory.CreateDirectory(outputDirectory);
                    MessageBox.Show($"Directory created: {outputDirectory}");
                }

                // Open the directory in File Explorer
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = outputDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                // Handle any exceptions (e.g., invalid path or lack of permissions)
                MessageBox.Show($"Error opening directory: {ex.Message}");
            }
        }

        private void InfToolBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var folderPath = OpenFolderDialog(InfToolOutputDirectoryTextBox.Text);
            if (folderPath != null)
            {
                InfToolOutputDirectoryTextBox.Text = folderPath;
            }
        }

        private void InfToolOpenButton_Click(object sender, RoutedEventArgs e)
        {
            string outputDirectory = InfToolOutputDirectoryTextBox.Text;

            try
            {
                // Check if the directory exists
                if (!Directory.Exists(outputDirectory))
                {
                    // Create the directory if it does not exist
                    Directory.CreateDirectory(outputDirectory);
                    MessageBox.Show($"Directory created: {outputDirectory}");
                }

                // Open the directory in File Explorer
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = outputDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                // Handle any exceptions (e.g., invalid path or lack of permissions)
                MessageBox.Show($"Error opening directory: {ex.Message}");
            }
        }

        private void CacheBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var folderPath = OpenFolderDialog(CacheOutputDirectoryTextBox.Text);
            if (folderPath != null)
            {
                CacheOutputDirectoryTextBox.Text = folderPath;
            }
        }

        private void CacheOpenButton_Click(object sender, RoutedEventArgs e)
        {
            string outputDirectory = CacheOutputDirectoryTextBox.Text;

            try
            {
                // Check if the directory exists
                if (!Directory.Exists(outputDirectory))
                {
                    // Create the directory if it does not exist
                    Directory.CreateDirectory(outputDirectory);
                    MessageBox.Show($"Directory created: {outputDirectory}");
                }

                // Open the directory in File Explorer
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = outputDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                // Handle any exceptions (e.g., invalid path or lack of permissions)
                MessageBox.Show($"Error opening directory: {ex.Message}");
            }
        }

        private void VideoOpenButton_Click(object sender, RoutedEventArgs e)
        {
            string outputDirectory = VideoOutputDirectoryTextBox.Text;

            try
            {
                // Check if the directory exists
                if (!Directory.Exists(outputDirectory))
                {
                    // Create the directory if it does not exist
                    Directory.CreateDirectory(outputDirectory);
                    MessageBox.Show($"Directory created: {outputDirectory}");
                }

                // Open the directory in File Explorer
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = outputDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                // Handle any exceptions (e.g., invalid path or lack of permissions)
                MessageBox.Show($"Error opening directory: {ex.Message}");
            }
        }

        private void TicketListBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var folderPath = OpenFolderDialog(TicketListOutputDirectoryTextBox.Text);
            if (folderPath != null)
            {
                TicketListOutputDirectoryTextBox.Text = folderPath;
            }
        }

        private void TicketListOpenButton_Click(object sender, RoutedEventArgs e)
        {
            string outputDirectory = TicketListOutputDirectoryTextBox.Text;

            try
            {
                // Check if the directory exists
                if (!Directory.Exists(outputDirectory))
                {
                    // Create the directory if it does not exist
                    Directory.CreateDirectory(outputDirectory);
                    MessageBox.Show($"Directory created: {outputDirectory}");
                }

                // Open the directory in File Explorer
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = outputDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                // Handle any exceptions (e.g., invalid path or lack of permissions)
                MessageBox.Show($"Error opening directory: {ex.Message}");
            }
        }

        private void LUACBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var folderPath = OpenFolderDialog(LUACOutputDirectoryTextBox.Text);
            if (folderPath != null)
            {
                LUACOutputDirectoryTextBox.Text = folderPath;
            }
        }

        private void LUACOpenButton_Click(object sender, RoutedEventArgs e)
        {
            string outputDirectory = LUACOutputDirectoryTextBox.Text;

            try
            {
                // Check if the directory exists
                if (!Directory.Exists(outputDirectory))
                {
                    // Create the directory if it does not exist
                    Directory.CreateDirectory(outputDirectory);
                    MessageBox.Show($"Directory created: {outputDirectory}");
                }

                // Open the directory in File Explorer
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = outputDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                // Handle any exceptions (e.g., invalid path or lack of permissions)
                MessageBox.Show($"Error opening directory: {ex.Message}");
            }
        }

        private void LUABrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var folderPath = OpenFolderDialog(LUAOutputDirectoryTextBox.Text);
            if (folderPath != null)
            {
                LUAOutputDirectoryTextBox.Text = folderPath;
            }
        }

        private void LUAOpenButton_Click(object sender, RoutedEventArgs e)
        {
            string outputDirectory = LUAOutputDirectoryTextBox.Text;

            try
            {
                // Check if the directory exists
                if (!Directory.Exists(outputDirectory))
                {
                    // Create the directory if it does not exist
                    Directory.CreateDirectory(outputDirectory);
                    MessageBox.Show($"Directory created: {outputDirectory}");
                }

                // Open the directory in File Explorer
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = outputDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                // Handle any exceptions (e.g., invalid path or lack of permissions)
                MessageBox.Show($"Error opening directory: {ex.Message}");
            }
        }


        // Helper method to open the folder dialog and return the selected path
        private string OpenFolderDialog(string initialDirectory)
        {
            using (var dialog = new CommonOpenFileDialog())
            {
                dialog.IsFolderPicker = true;
                dialog.InitialDirectory = initialDirectory;
                dialog.EnsurePathExists = true;

                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
#pragma warning disable CS8603 // Possible null reference return.
                    return dialog.FileName;
#pragma warning restore CS8603 // Possible null reference return.
                }
            }
#pragma warning disable CS8603 // Possible null reference return.
            return null;
#pragma warning restore CS8603 // Possible null reference return.
        }

        private void IncreaseCpuPercentage_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(cpuPercentageTextBox.Text, out int value))
            {
                value = Math.Min(value + 1, 90); // Assuming 90 is the max value
                cpuPercentageTextBox.Text = value.ToString();
            }
        }

        private void DecreaseCpuPercentage_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(cpuPercentageTextBox.Text, out int value))
            {
                value = Math.Max(value - 1, 25); // Assuming 25 is the min value
                cpuPercentageTextBox.Text = value.ToString();
            }
        }

        private void IncreaseMappingThreads_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(MappingThreadsTextbox.Text, out int value))
            {
                value = Math.Min(value + 1, 64); // Assuming 90 is the max value
                MappingThreadsTextbox.Text = value.ToString();
            }
        }

        private void DecreaseMappingThreads_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(MappingThreadsTextbox.Text, out int value))
            {
                value = Math.Max(value - 1, 1); // Assuming 25 is the min value
                MappingThreadsTextbox.Text = value.ToString();
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }

        private void Grid_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy; // Allows the drop
            }
            else
            {
                e.Effects = DragDropEffects.None; // Disallows the drop
            }
            e.Handled = true; // Mark the event as handled
        }


        private void Grid_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                // Determine which half of the grid the file was dropped on based on the drop position
                Point dropPosition = e.GetPosition((IInputElement)sender);
                var grid = sender as Grid;
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                int column = dropPosition.X < grid.ActualWidth / 2 ? 0 : 1;
#pragma warning restore CS8602 // Dereference of a possibly null reference.

                // Call different functions based on which column the file was dropped
                if (column == 0)
                {
                    HandleSdcFileDrop(files);
                }
                else
                {
                    HandleOdcFileDrop(files);
                }
            }
        }

        private void HandleSdcFileDrop(string[] files)
        {
            // Assuming only one file can be dropped at a time
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
            string filePath = files.FirstOrDefault();
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                MessageBox.Show("Invalid file. Please drop a valid SDC XML file.");
                return;
            }

            try
            {
                // Load the SDC XML file
                XDocument sdcXml = XDocument.Load(filePath);

                // Assuming English is the default language for the application
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                var englishLanguageElement = sdcXml.Root.Elements("LANGUAGE").FirstOrDefault(el => el.Attribute("REGION")?.Value == "en-GB");
#pragma warning restore CS8602 // Dereference of a possibly null reference.

                if (englishLanguageElement != null)
                {
                    sdcNameTextBox.Text = englishLanguageElement.Element("NAME")?.Value ?? "";
                    sdcDescriptionTextBox.Text = englishLanguageElement.Element("DESCRIPTION")?.Value ?? "";
                    sdcMakerTextBox.Text = englishLanguageElement.Element("MAKER")?.Value ?? "";

                    var archiveElement = englishLanguageElement.Element("ARCHIVES")?.Element("ARCHIVE");
                    sdcServerArchivePathTextBox.Text = archiveElement?.Value ?? "";
                    sdcArchiveSizeTextBox.Text = archiveElement?.Attribute("size")?.Value ?? "";
                    sdcTimestampTextBox.Text = archiveElement?.Attribute("timestamp")?.Value ?? "";

                    // Extract the thumbnail suffix from the image path
                    var makerImagePath = englishLanguageElement.Element("MAKER_IMAGE")?.Value ?? "";
                    sdcThumbnailSuffixTextBox.Text = ExtractSDCThumbnailSuffix(makerImagePath);

                    // Check if offline checkbox should be checked
                    offlineSDCCheckBox.IsChecked = englishLanguageElement.Element("ARCHIVES") == null;
                }

                // Set HDK version
                sdcHDKVersionTextBox.Text = sdcXml.Root.Attribute("hdk_version")?.Value ?? "";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading SDC file: {ex.Message}");
            }
        }

        private string ExtractSDCThumbnailSuffix(string imagePath)
        {
            // Assuming the thumbnail suffix is in the format "_Txxx.png"
            var match = Regex.Match(imagePath, @"_T(\d+)\.png");
            return match.Success ? match.Groups[1].Value : "";
        }


        private void HandleOdcFileDrop(string[] files)
        {
            // Assuming only one file can be dropped at a time
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
            string filePath = files.FirstOrDefault();
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                MessageBox.Show("Invalid file. Please drop a valid ODC XML file.");
                return;
            }

            try
            {
                // Load the ODC XML file
                XDocument odcXml = XDocument.Load(filePath);

                // Parse and set the values to the form
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                odcNameTextBox.Text = odcXml.Root.Elements("name").FirstOrDefault()?.Value ?? "";
#pragma warning restore CS8602 // Dereference of a possibly null reference.
                odcDescriptionTextBox.Text = odcXml.Root.Elements("description").FirstOrDefault()?.Value ?? "";
                odcMakerTextBox.Text = odcXml.Root.Elements("maker").FirstOrDefault()?.Value ?? "";
                odcHDKVersionTextBox.Text = odcXml.Root.Attribute("hdk_version")?.Value ?? "";
                odcUUIDTextBox.Text = odcXml.Root.Element("uuid")?.Value ?? "";
                odcTimestampTextBox.Text = odcXml.Root.Element("timestamp")?.Value ?? "";

                // Extract the thumbnail suffix from the image path
                var makerImagePath = odcXml.Root.Elements("maker_image").FirstOrDefault()?.Value ?? "";
                odcThumbnailSuffixTextBox.Text = ExtractODCThumbnailSuffix(makerImagePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading ODC file: {ex.Message}");
            }
        }

        private string ExtractODCThumbnailSuffix(string imagePath)
        {
            // Assuming the thumbnail suffix is in the format "_Txxx.png"
            var match = Regex.Match(imagePath, @"_T(\d+)\.png");
            return match.Success ? match.Groups[1].Value : "";
        }

        private void RadioButtonEBOOTBIN(object sender, RoutedEventArgs e)
        {

        }

        private void RadioButtonEBOOTELF(object sender, RoutedEventArgs e)
        {

        }

        private void RadioButtonEBOOTHDSELF(object sender, RoutedEventArgs e)
        {

        }

        private void RadioButtonEBOOTSELF(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBoxPSPLUS_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBoxSkipEula_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBoxCMDConsole_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBoxBlockProfanity_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBoxDrawDistanceHack_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void EbootPatcherDragDropHandler(object sender, DragEventArgs e)
        {
            LogDebugInfo("EBOOT Patcher: Drag and Drop initiated.");

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                LogDebugInfo("EBOOT Patcher: Data format is FileDrop.");

                string[] droppedItems = (string[])e.Data.GetData(DataFormats.FileDrop);
                string message = "Invalid file format.";

                if (droppedItems.Length > 0)
                {
                    LogDebugInfo($"EBOOT Patcher: Number of items dropped: {droppedItems.Length}");

                    if (Directory.Exists(droppedItems[0]))
                    {
                        LogDebugInfo("EBOOT Patcher: Dropped item is a directory.");
                        message = "This tool only supports one file at a time.";
                    }
                    else if (File.Exists(droppedItems[0]))
                    {
                        LogDebugInfo("EBOOT Patcher: Dropped item is a file.");
                        string file = droppedItems[0];

                        if (new[] { ".bin", ".elf", ".self" }.Contains(Path.GetExtension(file).ToLowerInvariant()))
                        {
                            LogDebugInfo("EBOOT Patcher: File format is valid.");
                            LoadEBOOT(file);
                            string fileName = Path.GetFileName(file);
                            string dirPath = Path.GetDirectoryName(file);
                            string shortenedPath = dirPath.Length > 17 ? "..." + dirPath.Substring(dirPath.Length - 17) : dirPath;
                            message = $"{shortenedPath}\\{fileName} Loaded";
                            LogDebugInfo($"EBOOT Patcher: {message}");
                        }
                        else
                        {
                            LogDebugInfo("EBOOT Patcher: File format is not supported.");
                            message = "This tool only supports bin, elf, and self files.";
                        }
                    }
                    else
                    {
                        LogDebugInfo("EBOOT Patcher: Dropped item is neither a file nor a directory.");
                    }
                }

                Dispatcher.Invoke(() =>
                {
                });
            }
            else
            {
                LogDebugInfo("EBOOT Patcher: Drag and Drop data format is not FileDrop.");
            }
        }





        private async void ClickToBrowseHandlerEbootPatcher(object sender, RoutedEventArgs e)
        {
            LogDebugInfo("EBOOT Patcher: Click to Browse EBOOT File selected - Checking...");
            this.IsEnabled = false;  // Disable the main window to prevent interactions

            var openFileDialog = new VistaOpenFileDialog
            {
                Filter = "Eboot files (*.bin;*.elf;*.self)|*.bin;*.elf;*.self",
                Multiselect = false
            };
            string message = "No file selected.";

            bool? result = openFileDialog.ShowDialog();

            // Await a delay to absorb any unintentional clicks that happen after the dialog closes
            await Task.Delay(10); // 10 ms delay

            this.IsEnabled = true;  // Re-enable the main window after the delay

            if (result == true)
            {
                var selectedFile = openFileDialog.FileName;
                LoadEBOOT(selectedFile);

                if (!string.IsNullOrEmpty(selectedFile))
                {
                    string fileName = Path.GetFileName(selectedFile);
                    string dirPath = Path.GetDirectoryName(selectedFile);
                    string shortenedPath = dirPath.Length > 17 ? "..." + dirPath.Substring(dirPath.Length - 17) : dirPath;
                    message = $"{shortenedPath}\\{fileName} Loaded";
                }
                else
                {
                    message = "No Eboot file selected.";
                }
            }
            else
            {
                message = "File selection was canceled.";
            }

            Dispatcher.Invoke(() =>
            {
                EbootPatcherDragAreaText.Text = message;
            });
        }



        private void LoadEBOOT(string ebootFilePath)
        {
            LoadedEbootTitleID.Text = "";
            LoadedEbootServiceID.Text = "";
            LoadedEbootNPCommID.Text = "";
            LoadedEbootMuisVersion.Text = "";
            LoadedEbootAppID.Text = "";
            LoadedEbootTSSURL.Text = "";
            LoadedEbootOfflineName.Text = "";
            LogDebugInfo("EBOOT Patcher: Loading EBOOT " + ebootFilePath);

            // Update LoadedEbootFilePath TextBox
            LoadedEbootFilePath.Text = ebootFilePath;

            string destinationFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "temp");
            string destinationFile = Path.Combine(destinationFolder, Path.GetFileName(ebootFilePath));

            // Check if the destination folder exists, create if not
            if (!Directory.Exists(destinationFolder))
            {
                Directory.CreateDirectory(destinationFolder);
            }

            // Clean the folder (delete all files in the folder)
            foreach (var file in Directory.GetFiles(destinationFolder))
            {
                File.Delete(file);
            }

            // Copy the Eboot file
            File.Copy(ebootFilePath, destinationFile, true);

            // Determine the file type and handle accordingly
            string extension = Path.GetExtension(ebootFilePath).ToLowerInvariant();
            switch (extension)
            {
                case ".bin":
                    HandleBINFile(destinationFile);
                    break;
                case ".elf":
                    HandleELFFile(destinationFile);
                    break;
                case ".self":
                    HandleSELFFile(destinationFile);
                    break;
                default:
                    // Handle unknown file type or add logging
                    break;
            }
        }


        private void HandleBINFile(string filePath)
        {
            string scetoolDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "scetool");
            string scetoolPath = Path.Combine(scetoolDirectory, "scetool.exe");
            string tempDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "temp");
            string originalFileName = Path.GetFileName(filePath);
            string copiedFilePath = Path.Combine(scetoolDirectory, originalFileName);
            string outputFilePath = Path.Combine(scetoolDirectory, "EBOOT.ELF");
            string message;
            int displayTime = 3000; // Default to 3 seconds for the message display

            // Check if scetool exists
            if (!File.Exists(scetoolPath))
            {
                message = "scetool.exe not found. Please ensure it is installed at " + scetoolDirectory;
                LogDebugInfo("EBOOT Patcher: " + message);
                Dispatcher.Invoke(() =>
                {
                    TemporaryMessageHelper.ShowTemporaryMessage(EbootPatcherDragAreaText, message, 2000);
                });
                return;
            }

            // Copy EBOOT.BIN to the scetool directory
            try
            {
                File.Copy(filePath, copiedFilePath, overwrite: true);
            }
            catch (IOException ioEx)
            {
                message = "Failed to copy EBOOT.BIN to scetool directory: " + ioEx.Message;
                LogDebugInfo("EBOOT Patcher: " + message);
                Dispatcher.Invoke(() =>
                {
                    TemporaryMessageHelper.ShowTemporaryMessage(EbootPatcherDragAreaText, message, 2000);
                });
                return;
            }

            // Prepare the process start info
            var startInfo = new ProcessStartInfo
            {
                FileName = scetoolPath,
                Arguments = $"--decrypt \"{originalFileName}\" \"{outputFilePath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = scetoolDirectory
            };

            // Set the CYGWIN environment variable to not warn about DOS-style paths
            startInfo.EnvironmentVariables["CYGWIN"] = "nodosfilewarning";

            // Execute the scetool command
            using (var process = Process.Start(startInfo))
            {
                process.WaitForExit();

                // Read the standard error stream to capture any error messages
                string stderr = process.StandardError.ReadToEnd();
                if (process.ExitCode != 0 || !string.IsNullOrEmpty(stderr))
                {
                    message = $"scetool failed to decrypt EBOOT.BIN: {stderr}";
                    LogDebugInfo("EBOOT Patcher: " + message);
                }
                else
                {
                    // Check if the EBOOT.ELF file exists before moving it
                    if (!File.Exists(outputFilePath))
                    {
                        message = "EBOOT failed to decrypt.";
                        LogDebugInfo("EBOOT Patcher: " + message);
                        Dispatcher.Invoke(() =>
                        {
                            TemporaryMessageHelper.ShowTemporaryMessage(EbootPatcherDragAreaText, message, 2000);
                        });
                        return; // Early return to skip the file moving process
                    }

                    message = $"Eboot decrypted successfully to: {outputFilePath}";
                    LogDebugInfo("EBOOT Patcher: " + message);

                    // Move the ELF file to the temp directory
                    string finalOutputPath = Path.Combine(tempDirectory, "EBOOT.ELF");
                    if (File.Exists(finalOutputPath)) File.Delete(finalOutputPath); // Ensure there's no conflict
                    File.Move(outputFilePath, finalOutputPath);

                    // Delete the copied EBOOT.BIN from scetool directory
                    File.Delete(copiedFilePath);

                    message = $"Eboot.ELF moved to: {finalOutputPath}";
                    LogDebugInfo("EBOOT Patcher: " + message);
                }

                // Assuming ParseEbootInfo() is a method you want to call after processing
                ParseEbootInfo();

                Dispatcher.Invoke(() =>
                {
                });
            }
        }

        private void HandleELFFile(string unusedFilePath)
        {
            string sourceFilePath = LoadedEbootFilePath.Text; // Get the path from the TextBox
            string tempDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "temp");
            string finalOutputPath = Path.Combine(tempDirectory, "EBOOT.ELF");
            string message;
            int displayTime = 3000; // Default to 3 seconds for the message display

            LogDebugInfo("EBOOT Patcher: Handling ELF file.");
            LogDebugInfo("EBOOT Patcher: Source ELF file path from TextBox: " + sourceFilePath);

            // Check if the source ELF file exists
            if (!File.Exists(sourceFilePath))
            {
                message = "Failed to load ELF file: Source file does not exist.";
                LogDebugInfo("EBOOT Patcher: " + message);
                Dispatcher.Invoke(() =>
                {
                    TemporaryMessageHelper.ShowTemporaryMessage(EbootPatcherDragAreaText, message, 2000);
                });
                return;
            }

            // Check if the temp directory exists, create if not
            if (!Directory.Exists(tempDirectory))
            {
                LogDebugInfo("EBOOT Patcher: Temp directory does not exist, creating...");
                Directory.CreateDirectory(tempDirectory);
            }

            // Clean the temp folder (delete all files in the folder)
            foreach (var file in Directory.GetFiles(tempDirectory))
            {
                File.Delete(file);
            }

            try
            {
                // Move the ELF file to the temp directory (renamed to EBOOT.ELF)
                File.Copy(sourceFilePath, finalOutputPath);

                message = $"ELF file moved successfully to: {finalOutputPath}";
                LogDebugInfo("EBOOT Patcher: " + message);

                // Assuming ParseEbootInfo() is a method you want to call after processing
                ParseEbootInfo();
            }
            catch (IOException ioEx)
            {
                message = "Failed to move ELF file to temp directory: " + ioEx.Message;
                LogDebugInfo("EBOOT Patcher: " + message);
            }

            // Display the message in the GUI
            Dispatcher.Invoke(() =>
            {
            });
        }


        private void LoadEbootDefinitions()
        {
            string jsonFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "ebootdefs.json");
            try
            {
                string jsonContent = File.ReadAllText(jsonFilePath);
                signatureToInfoMap = JsonConvert.DeserializeObject<Dictionary<string, EbootInfo>>(jsonContent);

                if (signatureToInfoMap == null)
                {
                    // Log an error or throw an exception
                    LogDebugInfo("Failed to deserialize the JSON content into a dictionary. JSON content might be empty.");
                }
                else if (signatureToInfoMap.Count == 0)
                {
                    // The JSON deserialized but the dictionary is empty
                    LogDebugInfo("EBOOT Patcher: The JSON content deserialized into an empty dictionary. Check the JSON content.");
                }
            }
            catch (Exception ex)
            {
                // Log the exception details
                LogDebugInfo($"EBOOT Patcher: Error loading EBOOT definitions: {ex.Message}");
                // Consider rethrowing the exception or handling it to prevent further execution
            }
        }




        private string UniqueSig;

        private void ParseEbootInfo()
        {
            string ebootElfPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "temp", "EBOOT.ELF");
            LoadEbootDefinitions();

            try
            {
                using (FileStream fs = new FileStream(ebootElfPath, FileMode.Open, FileAccess.Read))
                {
                    // Read the first 500KB of the file
                    const int bytesToRead = 500 * 1024; // 500KB
                    byte[] buffer = new byte[bytesToRead];
                    int bytesRead = fs.Read(buffer, 0, bytesToRead);

                    // Compute the SHA1 hash of the first 500KB
                    using (SHA1 sha1 = SHA1.Create())
                    {
                        byte[] hashBytes = sha1.ComputeHash(buffer, 0, bytesRead);
                        string sha1Hash = BitConverter.ToString(hashBytes).Replace("-", "");

                        // Log the SHA1 hash
                        LogDebugInfo($"EBOOT Patcher: SHA1 Hash: {sha1Hash}");

                        // Use the SHA1 hash for identification instead of unique signature
                        UniqueSig = sha1Hash; // Assuming you want to keep using UniqueSig for compatibility
                        CheckUniqueSigAndUpdateFields();
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo("EBOOT Patcher: Error parsing EBOOT.ELF: " + ex.Message);
            }
        }

        public class EbootInfo
        {
            public string Version { get; set; }
            public string Type { get; set; }
            public Dictionary<string, string> Offsets { get; set; } // Changed from long to string

            public EbootInfo(string version, string type, Dictionary<string, string> offsets)
            {
                Version = version;
                Type = type;
                Offsets = offsets;
            }
        }


        private Dictionary<string, EbootInfo> signatureToInfoMap;


        private void CheckUniqueSigAndUpdateFields()
        {
            if (signatureToInfoMap.TryGetValue(UniqueSig, out var ebootInfo))
            {
                string ebootElfPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "temp", "EBOOT.ELF");


                using (FileStream fs = new FileStream(ebootElfPath, FileMode.Open, FileAccess.Read))
                {
                    string firstPartOfMuis = "";
                    string secondPartOfMuis = "";

                    foreach (var offsetKey in ebootInfo.Offsets.Keys)
                    {
                        var offsetValue = ebootInfo.Offsets[offsetKey];

                        if (offsetValue != "0x0")
                        {
                            long offset = Convert.ToInt64(offsetValue, 16);
                            fs.Seek(offset, SeekOrigin.Begin);

                            var dataList = new List<byte>();
                            int nextByte;

                            if (offsetKey.Equals("AppIdOffset"))
                            {
                                byte[] appIdData = new byte[5];
                                fs.Read(appIdData, 0, appIdData.Length);
                                string appId = Encoding.UTF8.GetString(appIdData);
                                Dispatcher.Invoke(() => {
                                    UpdateGUIElement(offsetKey, appId);
                                });
                            }
                            else
                            {
                                while ((nextByte = fs.ReadByte()) != 0 && nextByte != -1)
                                {
                                    dataList.Add((byte)nextByte);
                                }
                                string data = Encoding.UTF8.GetString(dataList.ToArray());

                                if (offsetKey.Equals("MuisVersionOffset1"))
                                {
                                    firstPartOfMuis = data;
                                }
                                else if (offsetKey.Equals("MuisVersionOffset2"))
                                {
                                    secondPartOfMuis = data;
                                }
                                else
                                {
                                    Dispatcher.Invoke(() => {
                                        UpdateGUIElement(offsetKey, data);
                                    });
                                }
                            }
                        }
                        else
                        {
                            Dispatcher.Invoke(() => {
                                UpdateGUIElement(offsetKey, null);
                            });
                        }
                    }

                    string fullMuisVersion = secondPartOfMuis != "" ? $"{firstPartOfMuis}.{secondPartOfMuis}" : firstPartOfMuis;
                    Dispatcher.Invoke(() => {
                        LoadedEbootMuisVersion.Text = fullMuisVersion;

                        // Determine the text based on whether the version matches
                        string displayText = ebootInfo.Version == fullMuisVersion
                            ? $"EBOOT Loaded: {ebootInfo.Type} {ebootInfo.Version}"
                            : $"EBOOT Loaded: {ebootInfo.Type} {ebootInfo.Version} (Spoofed to {fullMuisVersion})";

                        EbootPatcherDragAreaText.Text = displayText;

                        foreach (var key in new[] { "TitleIdOffset", "ServiceIdOffset", "NPCommIDOffset", "TssUrlOffset", "AppIdOffset", "OfflineNameOffset", "PSplusOffset", "EulaOffset", "CMDConsoleOffset", "ProfFilterOffset", "DrawDistOffset" })
                        {
                            if (!ebootInfo.Offsets.ContainsKey(key))
                            {
                                UpdateGUIElement(key, null);
                            }
                        }
                    });
                }
            }
            else
            {
                Dispatcher.Invoke(() => {
                    EbootPatcherDragAreaText.Text = "EBOOT version/type unknown. Please ensure you're using a supported EBOOT file.";
                });
            }
        }





        private void UpdateGUIElement(string key, string value)
        {
            switch (key)
            {
                case "TitleIdOffset":
                    LoadedEbootTitleID.Text = !string.IsNullOrEmpty(value) ? value : "Title ID Not Available";
                    break;
                case "ServiceIdOffset":
                    LoadedEbootServiceID.Text = !string.IsNullOrEmpty(value) ? value : "Service ID Not Available";
                    break;
                case "NPCommIDOffset":
                    LoadedEbootNPCommID.Text = !string.IsNullOrEmpty(value) ? value : "NP Comm ID Not Available";
                    break;
                case "TssUrlOffset":
                    LoadedEbootTSSURL.Text = !string.IsNullOrEmpty(value) ? value : "TSS URL Not Available";
                    break;
                case "AppIdOffset":
                    LoadedEbootAppID.Text = !string.IsNullOrEmpty(value) ? value : "App ID Not Found";
                    break;
                case "OfflineNameOffset":
                    LoadedEbootOfflineName.Text = !string.IsNullOrEmpty(value) ? value : "N/A (Online Only)";
                    break;
                case "PSplusOffset":
                    CheckBoxPSPLUS.IsChecked = value == "1" ? true : false; // Assuming unchecked if not "1"
                    break;
                case "CMDConsoleOffset":
                    CheckBoxCMDConsole.IsChecked = value == "1" ? true : false; // Assuming unchecked if not "1"
                    break;
                case "ProfFilterOffset":
                    CheckBoxBlockProfanity.IsChecked = value == "1" ? true : false; // Assuming unchecked if not "1"
                    break;
                case "DrawDistOffset":
                    CheckBoxDrawDistanceHack.IsChecked = value == "1" ? true : false; // Assuming unchecked if not "1"
                    break;
                    // Add additional cases as necessary with their own default messages or values
            }
        }

        private void PatchButton_Click(object sender, RoutedEventArgs e)
        {
            string ebootElfPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "temp", "EBOOT.ELF");
            PatchTitleID(ebootElfPath);
            PatchAppID(ebootElfPath);
            PatchNPCommID(ebootElfPath);
            PatchServiceID(ebootElfPath);
            PatchMuisVersion(ebootElfPath);
            PatchTSSURL(ebootElfPath);
            PatchOfflineName(ebootElfPath);
            PatchPSPLUS(ebootElfPath);
            PatchCMDConsole(ebootElfPath);
            PatchProfanity(ebootElfPath);
            PatchDrawDistance1(ebootElfPath);
            PatchDrawDistance2(ebootElfPath);

            // Check the radio button setting and act accordingly
            if (RadioButtonEBOOTBINitem.IsChecked == true)
            {
                EncryptEbootWithScetool();
            }
            else if (RadioButtonEBOOTELFitem.IsChecked == true)
            {
                SaveEbootElfCopy();
            }
            else
            {
                LogDebugInfo("EBOOT Patcher: No output format selected.");
            }

            TemporaryMessageHelper.ShowTemporaryMessage(EbootPatcherDragAreaText, "Eboot patched successfully.", 2000);
        }


        private void EncryptEbootWithScetool()
        {
            string elfFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "temp", "EBOOT.ELF");
            string scetoolDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "scetool");
            string scetoolPath = Path.Combine(scetoolDirectory, "scetool.exe");

            if (!File.Exists(scetoolPath))
            {
                string message = "scetool.exe not found. Please ensure it is installed at " + scetoolDirectory;
                LogDebugInfo(message);
                Dispatcher.Invoke(() =>
                {
                    TemporaryMessageHelper.ShowTemporaryMessage(EbootPatcherDragAreaText, message, 2000);
                });
                return;
            }

            string arguments = $"-l 72F990788F9CFF745725F08E4C128387 --sce-type=SELF --compress-data=TRUE --skip-sections=FALSE --key-revision=04 --self-ctrl-flags=4000000000000000000000000000000000000000000000000000000000000002 --self-auth-id=1010000001000003 --self-add-shdrs=TRUE --self-vendor-id=01000002 --self-app-version=0001008600000000 --self-type=NPDRM --self-fw-version=0003004000000000 --np-license-type=FREE --np-content-id=EP9000-NPIA00005_00-HOME000000000001 --np-app-type=EXEC --np-real-fname=\"EBOOT.BIN\" --encrypt \"{elfFilePath}\" \"EBOOT.BIN\"";


            var startInfo = new ProcessStartInfo
            {
                FileName = scetoolPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = scetoolDirectory
            };

            using (var process = Process.Start(startInfo))
            {
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    string error = process.StandardError.ReadToEnd();
                    LogDebugInfo($"scetool failed: {error}");
                    return;
                }
            }

            Dispatcher.Invoke(() =>
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "EBOOT File (*.BIN)|*.BIN",
                    Title = "Save the EBOOT.BIN file",
                    FileName = "EBOOT.BIN" // This sets the default file name in the dialog
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    string savePath = saveFileDialog.FileName;
                    string binPath = Path.Combine(scetoolDirectory, "EBOOT.BIN");

                    if (File.Exists(binPath))
                    {
                        File.Move(binPath, savePath, overwrite: true);
                        LogDebugInfo($"EBOOT.BIN saved successfully to: {savePath}");
                    }
                    else
                    {
                        LogDebugInfo("EBOOT.BIN was not found after scetool process.");
                    }
                }
            });
        }

        private void SaveEbootElfCopy()
        {
            string elfFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "temp", "EBOOT.ELF");

            // Check if the ELF file exists
            if (!File.Exists(elfFilePath))
            {
                Dispatcher.Invoke(() =>
                {
                    TemporaryMessageHelper.ShowTemporaryMessage(EbootPatcherDragAreaText, "EBOOT.ELF not found for saving.", 2000);
                });
                return;
            }

            // Prompt the user to save the EBOOT.ELF file
            Dispatcher.Invoke(() =>
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "ELF File (*.ELF)|*.ELF",
                    Title = "Save the EBOOT.ELF file",
                    FileName = "EBOOT.elf" // Pre-fill the file name
                };

                // Show the Save File Dialog. If the user clicked OK, save the file
                if (saveFileDialog.ShowDialog() == true)
                {
                    string savePath = saveFileDialog.FileName;
                    // Copy the EBOOT.ELF to the user's chosen path
                    File.Copy(elfFilePath, savePath, overwrite: true);
                    LogDebugInfo($"EBOOT.ELF saved successfully to: {savePath}");
                }
            });
        }


        private void PatchPSPLUS(string ebootElfPath)
        {
            // Prepare the byte arrays for both states
            byte[] enabledValue = Encoding.ASCII.GetBytes("xxxxxxxxxx"); // "xxxxxxxxxx" for checkbox enabled
            byte[] disabledValue = Encoding.ASCII.GetBytes("PSPlusOnly"); // "PSPlusOnly" for checkbox disabled

            try
            {
                using (FileStream fs = new FileStream(ebootElfPath, FileMode.Open, FileAccess.Write))
                {
                    if (signatureToInfoMap.TryGetValue(UniqueSig, out var ebootInfo))
                    {
                        // Define the list of keys for PS Plus offsets
                        var psPlusOffsetKeys = new List<string>
                {
                    "PSplusOffset",
                    "PSplusOffset2",
                };

                        foreach (var offsetKey in psPlusOffsetKeys)
                        {
                            // Check if the dictionary contains the offset
                            if (ebootInfo.Offsets.TryGetValue(offsetKey, out var offsetValue))
                            {
                                long offset = Convert.ToInt64(offsetValue, 16); // Convert hex string to long
                                fs.Seek(offset, SeekOrigin.Begin);

                                // Write the appropriate byte array based on the checkbox state
                                byte[] valueToWrite = CheckBoxPSPLUS.IsChecked == true ? enabledValue : disabledValue;
                                fs.Write(valueToWrite, 0, valueToWrite.Length);
                            }
                        }

                        LogDebugInfo("EBOOT Patcher: PS Plus patched successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo($"EBOOT Patcher: Error patching PS Plus: {ex.Message}");
            }
        }


        private void PatchCMDConsole(string ebootElfPath)
        {
            // Prepare the byte arrays for both states
            byte[] enabledValue = Encoding.ASCII.GetBytes("xxxxxxx"); // "xxxxxxx" for checkbox enabled
            byte[] disabledValue = Encoding.ASCII.GetBytes("**DEV**"); // "**DEV**" for checkbox disabled

            try
            {
                using (FileStream fs = new FileStream(ebootElfPath, FileMode.Open, FileAccess.Write))
                {
                    if (signatureToInfoMap.TryGetValue(UniqueSig, out var ebootInfo))
                    {
                        // Corrected variable name for CMD Console offsets
                        var cmdConsoleOffsetKeys = new List<string>
                {
                    "CMDConsoleOffset",
                };

                        foreach (var offsetKey in cmdConsoleOffsetKeys)
                        {
                            // Check if the dictionary contains the offset
                            if (ebootInfo.Offsets.TryGetValue(offsetKey, out var offsetValue))
                            {
                                long offset = Convert.ToInt64(offsetValue, 16); // Convert hex string to long
                                fs.Seek(offset, SeekOrigin.Begin);

                                // Write the appropriate byte array based on the checkbox state
                                byte[] valueToWrite = CheckBoxCMDConsole.IsChecked == true ? enabledValue : disabledValue;
                                fs.Write(valueToWrite, 0, valueToWrite.Length);
                            }
                        }

                        LogDebugInfo("EBOOT Patcher: CMD Console patched successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo($"EBOOT Patcher: Error patching CMD Console: {ex.Message}");
            }
        }


        private void PatchProfanity(string ebootElfPath)
        {
            // Prepare the byte arrays for both states
            byte[] enabledValue = Encoding.ASCII.GetBytes("xxxxxxxxxxxxxxx"); // "xxxxxxxxxxxxxxx" for checkbox enabled
            byte[] disabledValue = Encoding.ASCII.GetBytes("ProfanityFilter"); // "ProfanityFilter" for checkbox disabled

            try
            {
                using (FileStream fs = new FileStream(ebootElfPath, FileMode.Open, FileAccess.Write))
                {
                    if (signatureToInfoMap.TryGetValue(UniqueSig, out var ebootInfo))
                    {
                        // Corrected variable name for CMD Console offsets
                        var ProfanityOffsetKeys = new List<string>
                {
                    "ProfFilterOffset",
                };

                        foreach (var offsetKey in ProfanityOffsetKeys)
                        {
                            // Check if the dictionary contains the offset
                            if (ebootInfo.Offsets.TryGetValue(offsetKey, out var offsetValue))
                            {
                                long offset = Convert.ToInt64(offsetValue, 16); // Convert hex string to long
                                fs.Seek(offset, SeekOrigin.Begin);

                                // Write the appropriate byte array based on the checkbox state
                                byte[] valueToWrite = CheckBoxBlockProfanity.IsChecked == true ? enabledValue : disabledValue;
                                fs.Write(valueToWrite, 0, valueToWrite.Length);
                            }
                        }

                        LogDebugInfo("EBOOT Patcher: Profanity patched successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo($"EBOOT Patcher: Error patching Profanity filter: {ex.Message}");
            }
        }



        private void PatchDrawDistance1(string ebootElfPath)
        {
            // Prepare the byte arrays for both states
            byte[] enabledValue = Encoding.ASCII.GetBytes("lod1"); // "lod1" for checkbox enabled
            byte[] disabledValue = Encoding.ASCII.GetBytes("lod2"); // "lod2" for checkbox disabled

            try
            {
                using (FileStream fs = new FileStream(ebootElfPath, FileMode.Open, FileAccess.Write))
                {
                    if (signatureToInfoMap.TryGetValue(UniqueSig, out var ebootInfo))
                    {

                        var DDOffsetKeys = new List<string>
                {
                    "DrawDistOffset",
                    "DrawDistOffset3",
                    "DrawDistOffset5",
                    "DrawDistOffset7",
                };

                        foreach (var offsetKey in DDOffsetKeys)
                        {
                            // Check if the dictionary contains the offset
                            if (ebootInfo.Offsets.TryGetValue(offsetKey, out var offsetValue))
                            {
                                long offset = Convert.ToInt64(offsetValue, 16); // Convert hex string to long
                                fs.Seek(offset, SeekOrigin.Begin);

                                // Write the appropriate byte array based on the checkbox state
                                byte[] valueToWrite = CheckBoxDrawDistanceHack.IsChecked == true ? enabledValue : disabledValue;
                                fs.Write(valueToWrite, 0, valueToWrite.Length);
                            }
                        }

                        LogDebugInfo("EBOOT Patcher: PS Plus patched successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo($"EBOOT Patcher: Error patching PS Plus: {ex.Message}");
            }
        }

        private void PatchDrawDistance2(string ebootElfPath)
        {
            // Prepare the byte arrays for both states
            byte[] enabledValue = Encoding.ASCII.GetBytes("lod1"); // "lod1" for checkbox enabled
            byte[] disabledValue = Encoding.ASCII.GetBytes("lod3"); // "lod3" for checkbox disabled

            try
            {
                using (FileStream fs = new FileStream(ebootElfPath, FileMode.Open, FileAccess.Write))
                {
                    if (signatureToInfoMap.TryGetValue(UniqueSig, out var ebootInfo))
                    {
                        // Define the list of keys for PS Plus offsets
                        var DD2OffsetKeys = new List<string>
                {
                    "DrawDistOffset2",
                    "DrawDistOffset4",
                    "DrawDistOffset6",
                    "DrawDistOffset8",
                };

                        foreach (var offsetKey in DD2OffsetKeys)
                        {
                            // Check if the dictionary contains the offset
                            if (ebootInfo.Offsets.TryGetValue(offsetKey, out var offsetValue))
                            {
                                long offset = Convert.ToInt64(offsetValue, 16); // Convert hex string to long
                                fs.Seek(offset, SeekOrigin.Begin);

                                // Write the appropriate byte array based on the checkbox state
                                byte[] valueToWrite = CheckBoxDrawDistanceHack.IsChecked == true ? enabledValue : disabledValue;
                                fs.Write(valueToWrite, 0, valueToWrite.Length);
                            }
                        }

                        LogDebugInfo("EBOOT Patcher: PS Plus patched successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo($"EBOOT Patcher: Error patching PS Plus: {ex.Message}");
            }
        }


        private void PatchTitleID(string ebootElfPath)
        {
            // Retrieve the Title ID value from the textbox and ensure it's 9 bytes long, padding with '0' if necessary
            string titleIdValue = LoadedEbootTitleID.Text.PadRight(9, '0');

            // Reflect the padded value back in the GUI, so it matches what gets written to the file
            Dispatcher.Invoke(() => {
                LoadedEbootTitleID.Text = titleIdValue;
            });

            byte[] bytesToWrite = Encoding.UTF8.GetBytes(titleIdValue); // Convert to bytes

            try
            {
                using (FileStream fs = new FileStream(ebootElfPath, FileMode.Open, FileAccess.Write))
                {
                    if (signatureToInfoMap.TryGetValue(UniqueSig, out var ebootInfo))
                    {
                        // List of possible Title ID offset keys
                        var titleIdOffsetKeys = new List<string>
                {
                    "TitleIdOffset",
                    "TitleIdOffset2",
                    "TitleIdOffset3",
                    "TitleIdOffset4",
                    "TitleIdOffset5",
                    "TitleIdOffset6",
                };

                        foreach (var offsetKey in titleIdOffsetKeys)
                        {
                            // Check if the offset exists in the dictionary
                            if (ebootInfo.Offsets.TryGetValue(offsetKey, out var offsetValue))
                            {
                                long offset = Convert.ToInt64(offsetValue, 16); // Convert hex string to long
                                fs.Seek(offset, SeekOrigin.Begin);
                                fs.Write(bytesToWrite, 0, bytesToWrite.Length); // Write exactly 9 bytes
                            }
                        }

                        LogDebugInfo("EBOOT Patcher: Title ID(s) patched successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo($"EBOOT Patcher: Error patching Title ID(s): {ex.Message}");
            }
        }

        private void PatchAppID(string ebootElfPath)
        {
            // Retrieve the App ID value from the textbox and ensure it's 5 bytes long, padding with '0' if necessary
            string appIdValue = LoadedEbootAppID.Text.PadRight(5, '0');

            // Reflect the padded value back in the GUI, so it matches what gets written to the file
            Dispatcher.Invoke(() => {
                LoadedEbootAppID.Text = appIdValue;
            });

            byte[] bytesToWrite = Encoding.UTF8.GetBytes(appIdValue); // Convert to bytes

            try
            {
                using (FileStream fs = new FileStream(ebootElfPath, FileMode.Open, FileAccess.Write))
                {
                    if (signatureToInfoMap.TryGetValue(UniqueSig, out var ebootInfo))
                    {
                        // Check if the AppIdOffset exists in the dictionary
                        if (ebootInfo.Offsets.TryGetValue("AppIdOffset", out var offsetValue))
                        {
                            long offset = Convert.ToInt64(offsetValue, 16); // Convert hex string to long
                            fs.Seek(offset, SeekOrigin.Begin);
                            fs.Write(bytesToWrite, 0, bytesToWrite.Length); // Write exactly 5 bytes
                        }

                        LogDebugInfo("EBOOT Patcher: App ID patched successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo($"EBOOT Patcher: Error patching App ID: {ex.Message}");
            }
        }

        private void PatchNPCommID(string ebootElfPath)
        {
            // Retrieve the NPCommID value from the textbox, ensure it's at least 9 chars long, padding with '\0' (null) if necessary
            string npCommIdValue = LoadedEbootNPCommID.Text;
            // Ensure the string does not exceed 15 characters
            npCommIdValue = npCommIdValue.Length > 15 ? npCommIdValue.Substring(0, 15) : npCommIdValue.PadRight(9, '\0');

            // Reflect the padded/truncated value back in the GUI, so it matches what gets written to the file
            Dispatcher.Invoke(() => {
                LoadedEbootNPCommID.Text = npCommIdValue.Replace("\0", ""); // Display without nulls in the GUI for clarity
            });

            byte[] bytesToWrite = Encoding.UTF8.GetBytes(npCommIdValue); // Convert to bytes

            try
            {
                using (FileStream fs = new FileStream(ebootElfPath, FileMode.Open, FileAccess.Write))
                {
                    if (signatureToInfoMap.TryGetValue(UniqueSig, out var ebootInfo))
                    {
                        // Check if the NPCommIDOffset exists in the dictionary
                        if (ebootInfo.Offsets.TryGetValue("NPCommIDOffset", out var offsetValue))
                        {
                            long offset = Convert.ToInt64(offsetValue, 16); // Convert hex string to long
                            fs.Seek(offset, SeekOrigin.Begin);
                            fs.Write(bytesToWrite, 0, bytesToWrite.Length); // Write the bytes, padded/truncated as necessary
                        }

                        LogDebugInfo("EBOOT Patcher: NPCommID patched successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo($"EBOOT Patcher: Error patching NPCommID: {ex.Message}");
            }
        }

        private void PatchServiceID(string ebootElfPath)
        {
            // Retrieve the Service ID value from the textbox and ensure it does not exceed 19 characters, padding with '0' if necessary
            string serviceIdValue = LoadedEbootServiceID.Text.PadRight(19, '0');

            // Reflect the padded value back in the GUI, so it matches what gets written to the file
            Dispatcher.Invoke(() => {
                LoadedEbootServiceID.Text = serviceIdValue;
            });

            byte[] bytesToWrite = Encoding.UTF8.GetBytes(serviceIdValue); // Convert to bytes

            try
            {
                using (FileStream fs = new FileStream(ebootElfPath, FileMode.Open, FileAccess.Write))
                {
                    if (signatureToInfoMap.TryGetValue(UniqueSig, out var ebootInfo))
                    {
                        // Check if the ServiceIdOffset exists in the dictionary
                        if (ebootInfo.Offsets.TryGetValue("ServiceIdOffset", out var offsetValue))
                        {
                            long offset = Convert.ToInt64(offsetValue, 16); // Convert hex string to long
                            fs.Seek(offset, SeekOrigin.Begin);
                            fs.Write(bytesToWrite, 0, bytesToWrite.Length); // Write the bytes, padded as necessary
                        }

                        LogDebugInfo("EBOOT Patcher: Service ID patched successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo($"EBOOT Patcher: Error patching Service ID: {ex.Message}");
            }
        }

        private void PatchMuisVersion(string ebootElfPath)
        {
            // Ensure the MuisVersion input is exactly 8 characters long, padding with '0' and '.00' as necessary
            string muisVersionInput = LoadedEbootMuisVersion.Text;
            if (muisVersionInput.Length < 8)
            {
                muisVersionInput = muisVersionInput.PadRight(5, '0') + ".00";
            }

            // Split the processed input into parts for MuisVersionOffset1 and MuisVersionOffset2
            string muisVersionPart1 = muisVersionInput.Substring(0, 5); // First 5 characters for MuisVersionOffset1
            string muisVersionPart2 = muisVersionInput.Substring(5, 3); // Last 3 characters for MuisVersionOffset2, includes the period

            // If muisVersionPart2 starts with a period, remove it
            if (muisVersionPart2.StartsWith("."))
            {
                muisVersionPart2 = muisVersionPart2.TrimStart('.');
            }

            // Reflect the processed value back in the GUI
            Dispatcher.Invoke(() =>
            {
                LoadedEbootMuisVersion.Text = muisVersionInput;
            });

            try
            {
                using (FileStream fs = new FileStream(ebootElfPath, FileMode.Open, FileAccess.Write))
                {
                    if (signatureToInfoMap.TryGetValue(UniqueSig, out var ebootInfo))
                    {
                        // Patch MuisVersionOffset1
                        if (ebootInfo.Offsets.TryGetValue("MuisVersionOffset1", out var offsetValue1))
                        {
                            PatchOffsetWithBytes(fs, offsetValue1, muisVersionPart1);
                        }

                        // Patch MuisVersionOffset2, if it exists
                        if (ebootInfo.Offsets.TryGetValue("MuisVersionOffset2", out var offsetValue2))
                        {
                            PatchOffsetWithBytes(fs, offsetValue2, muisVersionPart2);
                        }

                        LogDebugInfo("EBOOT Patcher: MuisVersion patched successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo($"EBOOT Patcher: Error patching MuisVersion: {ex.Message}");
            }
        }

        private void PatchOffsetWithBytes(FileStream fs, string hexOffset, string value)
        {
            long offset = Convert.ToInt64(hexOffset, 16); // Convert hex string to long
            byte[] bytesToWrite = Encoding.UTF8.GetBytes(value); // Convert string value to bytes
            fs.Seek(offset, SeekOrigin.Begin);
            fs.Write(bytesToWrite, 0, bytesToWrite.Length); // Write the bytes to the specified offset
        }


        private void PatchTSSURL(string ebootElfPath)
        {
            string tssUrlValue = LoadedEbootTSSURL.Text;

            try
            {
                using (FileStream fs = new FileStream(ebootElfPath, FileMode.Open, FileAccess.ReadWrite))
                {
                    if (signatureToInfoMap.TryGetValue(UniqueSig, out var ebootInfo))
                    {
                        if (ebootInfo.Offsets.TryGetValue("TssUrlOffset", out var offsetValue))
                        {
                            long offset = Convert.ToInt64(offsetValue, 16); // Convert hex string to long
                            byte[] urlBytes = Encoding.UTF8.GetBytes(tssUrlValue);
                            byte[] padding = new byte[1] { 0 }; // Null byte for padding

                            fs.Seek(offset, SeekOrigin.Begin);
                            fs.Write(urlBytes, 0, urlBytes.Length); // Write the URL bytes

                            // Now pad with nulls until the first null byte in the file,
                            // indicating the previous URL's end or the maximum length allowed.
                            long nextBytePosition = fs.Position; // Get the current position after writing URL
                            int nextByte = fs.ReadByte(); // Read the next byte to check if it's null
                            while (nextByte != 0 && nextByte != -1) // -1 is EOF
                            {
                                fs.Seek(nextBytePosition, SeekOrigin.Begin); // Move back to the position to overwrite with null
                                fs.Write(padding, 0, padding.Length); // Write a null byte for padding
                                nextBytePosition++; // Move to the next position
                                fs.Seek(nextBytePosition, SeekOrigin.Begin); // Move the pointer to the next byte to check
                                nextByte = fs.ReadByte(); // Read the next byte to check if it's null
                            }

                            LogDebugInfo("EBOOT Patcher: TSS URL patched and padded successfully.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo($"EBOOT Patcher: Error patching TSS URL: {ex.Message}");
            }
        }

        private void PatchOfflineName(string ebootElfPath)
        {
            string offlineNameValue = LoadedEbootOfflineName.Text;

            try
            {
                using (FileStream fs = new FileStream(ebootElfPath, FileMode.Open, FileAccess.ReadWrite))
                {
                    if (signatureToInfoMap.TryGetValue(UniqueSig, out var ebootInfo))
                    {
                        if (ebootInfo.Offsets.TryGetValue("OfflineNameOffset", out var offsetValue))
                        {
                            long offset = Convert.ToInt64(offsetValue, 16); // Convert hex string to long
                            fs.Seek(offset, SeekOrigin.Begin);

                            int existingLength = 0;
                            while (fs.ReadByte() != 0)
                            {
                                existingLength++;
                            }

                            int totalAvailableSpace = existingLength + CalculateAvailableNulls(fs);
                            int maxLength = Math.Min(offlineNameValue.Length, totalAvailableSpace);
                            offlineNameValue = offlineNameValue.Substring(0, maxLength);

                            // Update the textbox with the potentially trimmed string
                            Dispatcher.Invoke(() => {
                                LoadedEbootOfflineName.Text = offlineNameValue;
                            });

                            byte[] nameBytes = Encoding.UTF8.GetBytes(offlineNameValue);
                            fs.Seek(offset, SeekOrigin.Begin); // Reset position to the start of the string
                            fs.Write(nameBytes, 0, nameBytes.Length); // Write the new name

                            // Pad the remaining space with null bytes
                            for (int i = nameBytes.Length; i < totalAvailableSpace; i++)
                            {
                                fs.WriteByte(0);
                            }

                            LogDebugInfo("EBOOT Patcher: Offline name patched and textbox updated successfully.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebugInfo($"EBOOT Patcher: Error patching Offline Name: {ex.Message}");
            }
        }


        private int CalculateAvailableNulls(FileStream fs)
        {
            int nullCount = 0;
            int nextByte = fs.ReadByte();

            // Count consecutive null bytes from the current position
            while (nextByte == 0)
            {
                nullCount++;
                nextByte = fs.ReadByte();
            }

            // Rewind the stream back to the start of the null sequence
            fs.Seek(-(nullCount + 1), SeekOrigin.Current);

            return nullCount;
        }

        private static void UncompressFile(string compressedFilePath, string extractionFolderPath)
        {
            try
            {
                ZipFile.ExtractToDirectory(compressedFilePath, extractionFolderPath);
            }
            catch (Exception ex)
            {
                CustomLogger.LoggerAccessor.LogInfo($"[File Uncompress] - An error occurred: {ex}");
            }
        }

        private void HandleSELFFile(string filePath)
        {
            // Processing specific to .self files
            // ...
        }




        private void CheckBoxLSTEncrypterLegacyMode(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBoxLSTDecrypterLegacyMode(object sender, RoutedEventArgs e)
        {

        }



        private void CheckBoxSaveDecInfs_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBoxSaveDecInfs_Unchecked(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBoxSaveDecInfsCACHE_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBoxSaveDecInfsCACHE_Unchecked(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBoxArchiveMapperVerify_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBoxArchiveMapperVerify_Unchecked(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBoxTTYSpam_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBoxMLAA_Checked(object sender, RoutedEventArgs e)
        {

        }

        private async void ArchiveUnpackerVerifyTestButtonClick(object sender, RoutedEventArgs e)
        {
            // You might want to add a dialog to select the directory or set a fixed directory
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog();
            System.Windows.Forms.DialogResult result = folderDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                string selectedDirectory = folderDialog.SelectedPath;

                FileScanner scanner = new FileScanner();
                await scanner.ScanDirectoryAsync(selectedDirectory);

                MessageBox.Show("Scan completed!");
            }
        }

        private void ArchiveUnpackerFastMapCcheButtonClick(object sender, RoutedEventArgs e)
        {
            // Open a folder browser dialog to select a folder
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog();
            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string selectedFolder = folderDialog.SelectedPath;
                // Read the text file next to the exe in the dependencies folder
                string exeLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string logFilePath = Path.Combine(Path.GetDirectoryName(exeLocation), "dependencies", "logs_ALL.txt");
                Dictionary<string, string> fileMappings = new Dictionary<string, string>();

                if (File.Exists(logFilePath))
                {
                    foreach (string line in File.ReadAllLines(logFilePath))
                    {
                        var parts = line.Split('|');
                        if (parts.Length == 2)
                        {
                            fileMappings[parts[0]] = parts[1];
                        }
                    }

                    // Scan the selected folder for _DAT.* files and process them
                    foreach (string filePath in Directory.GetFiles(selectedFolder, "*_DAT.*", SearchOption.AllDirectories))
                    {
                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        string id = fileName.Split('_')[0];

                        if (fileMappings.TryGetValue(id, out string mappedPath))
                        {
                            string outputDirectory = CacheOutputDirectoryTextBox.Text;
                            string newFileName = Path.Combine(outputDirectory, mappedPath.Replace('/', Path.DirectorySeparatorChar));
                            string newFileDirectory = Path.GetDirectoryName(newFileName);

                            if (!Directory.Exists(newFileDirectory))
                            {
                                Directory.CreateDirectory(newFileDirectory);
                            }

                            File.Copy(filePath, newFileName, overwrite: true);
                        }
                    }
                }
                else
                {
                    MessageBox.Show("Log file not found.");
                }
            }
        }


    }
}
