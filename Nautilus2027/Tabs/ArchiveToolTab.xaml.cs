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

namespace Nautilus2027
{
    // "BAR / SDAT / SHARC" tab.
    public partial class ArchiveToolTab : UserControl
    {
        private static MainWindow Main => MainWindow.Instance;

        public ArchiveToolTab()
        {
            InitializeComponent();
        }

        // The archive timestamp box: up to 8 hex digits, FFFFFFFF when empty or not valid hex.
        private int ArchiveCreatorTimestamp()
        {
            string t = (ArchiveCreatorTimestampTextBox.Text ?? "").Trim();
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
            if (t.Length > 0 && t.Length <= 8 && uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out uint v))
                return unchecked((int)v);
            Main.LogDebugInfo($"Archive Creator: timestamp '{ArchiveCreatorTimestampTextBox.Text}' isn't 1-8 hex digits, using FFFFFFFF");
            return -1;
        }

        // Fill this tab's controls from the saved settings (called once at startup).
        internal void ApplySettings(AppSettings settings)
        {
            switch (settings.ArchiveTypeSettingRem)
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
            switch (settings.ArchiveMapperSettingRem)
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
        }

        private static SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

        private static Dictionary<string, string> hashToPath = new Dictionary<string, string>();
        private static bool isHelperLoaded = false;
        internal void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Set the default state for CheckBoxArchiveCreatorRenameCDN
            CheckBoxArchiveCreatorRenameCDN.IsChecked = true;
            CheckBoxArchiveMapperDeleteFilestxt.IsChecked = true;
        }

        private void RadioButtonArchiveCreatorBAR_Checked(object sender, RoutedEventArgs e)
        {
            if (Main._settings != null)
            {
                Main._settings.ArchiveTypeSettingRem = ArchiveTypeSetting.BAR;
                SettingsManager.SaveSettings(Main._settings);
               
            }
        }

        private void RadioButtonArchiveCreatorBAR_S_Checked(object sender, RoutedEventArgs e)
        {
            if (Main._settings != null)
            {
                Main._settings.ArchiveTypeSettingRem = ArchiveTypeSetting.BAR_S;
                SettingsManager.SaveSettings(Main._settings);
                
            }
        }

        private void RadioButtonArchiveCreatorSDAT_Checked(object sender, RoutedEventArgs e)
        {
            if (Main._settings != null)
            {
                Main._settings.ArchiveTypeSettingRem = ArchiveTypeSetting.SDAT;
                SettingsManager.SaveSettings(Main._settings);
               
            }
        }

        private void RadioButtonArchiveCreatorSDAT_SHARC_Checked(object sender, RoutedEventArgs e)
        {
            if (Main._settings != null)
            {
                Main._settings.ArchiveTypeSettingRem = ArchiveTypeSetting.SDAT_SHARC;
                SettingsManager.SaveSettings(Main._settings);
                
            }
        }

        private void RadioButtonArchiveCreatorCORE_SHARC_Checked(object sender, RoutedEventArgs e)
        {
            if (Main._settings != null)
            {
                Main._settings.ArchiveTypeSettingRem = ArchiveTypeSetting.CORE_SHARC;
                SettingsManager.SaveSettings(Main._settings);
                
            }
        }

        private void RadioButtonArchiveCreatorCONFIG_SHARC_Checked(object sender, RoutedEventArgs e)
        {
            if (Main._settings != null)
            {
                Main._settings.ArchiveTypeSettingRem = ArchiveTypeSetting.CONFIG_SHARC;
                SettingsManager.SaveSettings(Main._settings);
               
            }
        }



        // TAB 1: Logic for Archive Creation

        private async void ArchiveCreatorExecuteButtonClick(object sender, RoutedEventArgs e)
        {
            Main.LogDebugInfo("Archive Creation: Process Initiated");
            TemporaryMessageHelper.ShowTemporaryMessage(ArchiveCreatorDragAreaText, "Processing....", 1000000);

            // Check and create output directory if it doesn't exist
            if (!Directory.Exists(Main._settings.BarSdatSharcOutputDirectory))
            {
                Directory.CreateDirectory(Main._settings.BarSdatSharcOutputDirectory);
                Main.LogDebugInfo($"Archive Creation: Output directory created at {Main._settings.BarSdatSharcOutputDirectory}");
            }

            string itemsToArchive = string.Join(Environment.NewLine, LogBox.TakeLines(ArchiveCreatorTextBox));   // the list moves into memory; the box shows the log
            if (!string.IsNullOrWhiteSpace(itemsToArchive))
            {
                string[] itemsPaths = itemsToArchive.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                Main.LogDebugInfo($"Archive Creation: Starting creation for {itemsPaths.Length} items");
                bool archiveCreationSuccess = await CreateArchiveAsync(itemsPaths, Main._settings.ArchiveTypeSettingRem);

                string message = archiveCreationSuccess ? "Success: Archives Created" : "Archive Creation Failed";
                TemporaryMessageHelper.ShowTemporaryMessage(ArchiveCreatorDragAreaText, message, 2000);

                Main.LogDebugInfo($"Archive Creation: Result - {message}");
            }
            else
            {
                Main.LogDebugInfo("Archive Creation: Aborted - No items listed for Archive Creation.");
                TemporaryMessageHelper.ShowTemporaryMessage(ArchiveCreatorDragAreaText, "No items listed for Archive Creation.", 2000);
            }
        }



        private void ArchiveCreatorDragDropHandler(object sender, DragEventArgs e)
        {
            Main.LogDebugInfo("Archive Creation: Drag and Drop Initiated");
            TemporaryMessageHelper.ShowTemporaryMessage(ArchiveCreatorDragAreaText, "Processing....", 1000000);

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedItems = (string[])e.Data.GetData(DataFormats.FileDrop);
                List<string> itemsToAdd = new List<string>();

                foreach (var item in droppedItems)
                {
                    Main.LogDebugInfo($"Processing item: {item}");

                    if (Directory.Exists(item))
                    {
                        string itemWithTrailingSlash = item.EndsWith(Path.DirectorySeparatorChar.ToString()) ? item : item + Path.DirectorySeparatorChar;
                        itemsToAdd.Add(itemWithTrailingSlash);
                        Main.LogDebugInfo($"Directory added with trailing slash: {itemWithTrailingSlash}");
                    }
                    else if (File.Exists(item) && Path.GetExtension(item).ToLowerInvariant() == ".zip")
                    {
                        itemsToAdd.Add(item);
                        Main.LogDebugInfo($"ZIP file added: {item}");
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

                        Main.LogDebugInfo($"Archive Creation: {newItemsCount} items added, {duplicatesCount} duplicates filtered from Drag and Drop.");
                    });
                }
                else
                {
                    Main.LogDebugInfo("Archive Creation: Drag and Drop - No valid ZIP files or folders found.");
                    TemporaryMessageHelper.ShowTemporaryMessage(ArchiveCreatorDragAreaText, "No valid ZIP files or folders found.", 2000);
                }
            }
            else
            {
                Main.LogDebugInfo("Archive Creation: Drag and Drop - No Data Present.");
                TemporaryMessageHelper.ShowTemporaryMessage(ArchiveCreatorDragAreaText, "Drag and Drop operation failed - No Data Present.", 2000);
            }
        }



        private async void ClickToBrowseArchiveCreatorHandler(object sender, RoutedEventArgs e)
        {
            Main.LogDebugInfo("Archive Creation: Browsing for files Initiated");

            // Disable the main window to prevent interactions while the dialog is open
            Main.IsEnabled = false;

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
            Main.IsEnabled = true;

            if (result == true)
            {
                var selectedItems = openFileDialog.FileNames;

                if (selectedItems.Any())
                {
                    Main.LogDebugInfo($"Archive Creation: {selectedItems.Length} items selected via File Browser.");

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
                    Main.LogDebugInfo("Archive Creation: File Browser - No ZIP files were selected.");
                    message = "No ZIP files selected.";
                }
            }
            else
            {
                Main.LogDebugInfo("Archive Creation: File Browser - Dialog Cancelled.");
            }

            TemporaryMessageHelper.ShowTemporaryMessage(ArchiveCreatorDragAreaText, message, displayTime);
        }



        private void CheckBoxArchiveMapperFAST_Checked(object sender, RoutedEventArgs e)
        {
            if (Main._settings != null)
            {
                Main._settings.ArchiveMapperSettingRem = ArchiveMapperSetting.FAST;
                SettingsManager.SaveSettings(Main._settings);
            }
            // Uncheck the CORE CheckBox if it's checked

            CheckBoxArchiveMapperEXP.IsChecked = false;
        }

        private void CheckBoxArchiveMapperFAST_Unchecked(object sender, RoutedEventArgs e)
        {

            {
                Main._settings.ArchiveMapperSettingRem = ArchiveMapperSetting.NORM;
                SettingsManager.SaveSettings(Main._settings);
            }
        }

        private void CheckBoxArchiveMapperEXP_Checked(object sender, RoutedEventArgs e)
        {
            if (Main._settings != null)
            {
                Main._settings.ArchiveMapperSettingRem = ArchiveMapperSetting.EXP;
                SettingsManager.SaveSettings(Main._settings);
            }
            // Uncheck the SLOW CheckBox if it's checked
            CheckBoxArchiveMapperFAST.IsChecked = false;

        }

        private void CheckBoxArchiveMapperEXP_Unchecked(object sender, RoutedEventArgs e)
        {
            if (Main._settings != null && !CheckBoxArchiveMapperFAST.IsChecked.Value)
            {
                Main._settings.ArchiveMapperSettingRem = ArchiveMapperSetting.NORM;
                SettingsManager.SaveSettings(Main._settings);
            }
        }




        private async Task<bool> CreateArchiveAsync(string[] itemPaths, ArchiveTypeSetting type)
        {
            // Check if the "Ignore timestamp.txt" checkbox is enabled and delete the timestamp.txt file if it exists
            if (CheckBoxArchiveCreatorDeleteTimestamp.IsChecked == true)
            {
                foreach (string itemPath in itemPaths)
                {
                    string directoryPath = Path.GetDirectoryName(itemPath);
                    if (directoryPath != null)
                    {
                        string timestampFilePath = Path.Combine(directoryPath, "timestamp.txt");
                        if (File.Exists(timestampFilePath))
                        {
                            File.Delete(timestampFilePath);
                            Main.LogDebugInfo($"timestamp.txt file ignored in directory: {directoryPath}");
                            await Dispatcher.InvokeAsync(() =>
                            {
                                LogBox.Append(ArchiveCreatorTextBox, $"{Environment.NewLine}timestamp.txt file ignored in directory: {directoryPath}");
                                ArchiveCreatorTextBox.ScrollToEnd();
                            });
                        }
                    }
                }
            }

            // Filter out lines starting with "Archive:" and log the start of the archive creation process
            var validItemPaths = itemPaths.Where(p => !p.Trim().StartsWith("Archive Creator:")).ToArray();
            Main.LogDebugInfo($"Archive Creation: Beginning Archive Creation for {validItemPaths.Length} items with type {type}.");
            await Dispatcher.InvokeAsync(() =>
            {
                LogBox.Append(ArchiveCreatorTextBox, $"{Environment.NewLine}Archive Creator: Beginning Archive Creation for {validItemPaths.Length} items with type {type}.");
                ArchiveCreatorTextBox.ScrollToEnd();
            });

            bool allItemsProcessed = true;
            int i = 0;

            foreach (string itemPath in validItemPaths)
            {
                try
                {
                    Main.LogDebugInfo($"Archive Creation: Processing item {i + 1}: {itemPath}");
                    await Dispatcher.InvokeAsync(() =>
                    {
                        LogBox.Append(ArchiveCreatorTextBox, $"{Environment.NewLine}Archive Creator: Processing item {i + 1}: {itemPath}");
                        ArchiveCreatorTextBox.ScrollToEnd();
                    });

                    if (itemPath.ToLower().EndsWith(".zip"))
                    {
                        string filename = Path.GetFileNameWithoutExtension(itemPath);
                        Main.LogDebugInfo($"Archive Creation: Processing item {i + 1}: Extracting ZIP: {filename}");

                        await Dispatcher.InvokeAsync(() =>
                        {
                            LogBox.Append(ArchiveCreatorTextBox, $"{Environment.NewLine}Archive Creator: Processing item {i + 1}: Extracting ZIP: {filename}");
                            ArchiveCreatorTextBox.ScrollToEnd();
                        });

                        // Combine the temporary folder path with the unique folder name
                        string temppath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                        Main.LogDebugInfo($"Archive Creation: Processing item {i + 1}: Temporary extraction path: {temppath}");

                        await Dispatcher.InvokeAsync(() =>
                        {
                            LogBox.Append(ArchiveCreatorTextBox, $"{Environment.NewLine}Archive Creator: Processing item {i + 1}: Temporary extraction path: {temppath}");
                            ArchiveCreatorTextBox.ScrollToEnd();
                        });

                        UncompressFile(itemPath, temppath);

                        bool sdat = false;
                        IEnumerable<string> enumerable = Directory.EnumerateFiles(temppath, "*.*", SearchOption.AllDirectories);
                        BARArchive? bararchive = null;

                        // Declare the fileExtension variable
                        string fileExtension = "";

                        switch (Main._settings.ArchiveTypeSettingRem)
                        {
                            case ArchiveTypeSetting.BAR:
                                bararchive = new BARArchive($"{Main._settings.BarSdatSharcOutputDirectory}/{filename}.BAR", temppath, (int)CdnMode.RETAIL, ArchiveCreatorTimestamp(), false, true);
                                fileExtension = ".BAR"; // Set file extension
                                break;
                            case ArchiveTypeSetting.BAR_S:
                                bararchive = new BARArchive($"{Main._settings.BarSdatSharcOutputDirectory}/{filename}.bar", temppath, (int)CdnMode.RETAIL, ArchiveCreatorTimestamp(), true, true);
                                fileExtension = ".bar"; // Set file extension
                                break;
                            case ArchiveTypeSetting.SDAT:
                                bararchive = new BARArchive($"{Main._settings.BarSdatSharcOutputDirectory}/{filename}.BAR", temppath, (int)CdnMode.RETAIL, ArchiveCreatorTimestamp(), false, true);
                                sdat = true;
                                fileExtension = ".sdat";  // Set file extension
                                break;
                            case ArchiveTypeSetting.CORE_SHARC:
                                bararchive = new BARArchive($"{Main._settings.BarSdatSharcOutputDirectory}/{filename}.SHARC", temppath, (int)CdnMode.RETAIL, ArchiveCreatorTimestamp(), true, true, ToolsImplementation.base64DefaultSharcKey);
                                fileExtension = ".SHARC"; // Set file extension
                                break;
                            case ArchiveTypeSetting.SDAT_SHARC:
                                sdat = true;
                                bararchive = new BARArchive($"{Main._settings.BarSdatSharcOutputDirectory}/{filename}.SHARC", temppath, (int)CdnMode.RETAIL, ArchiveCreatorTimestamp(), true, true, ToolsImplementation.base64CDNKey2);
                                fileExtension = ".sdat"; // Set file extension
                                break;
                            case ArchiveTypeSetting.CONFIG_SHARC:
                                bararchive = new BARArchive($"{Main._settings.BarSdatSharcOutputDirectory}/{filename}.sharc", temppath, (int)CdnMode.RETAIL, ArchiveCreatorTimestamp(), true, true, ToolsImplementation.base64CDNKey2);
                                fileExtension = ".sharc"; // Set file extension
                                break;
                        }

                        if (File.Exists(temppath + "/timestamp.txt"))
                            bararchive.BARHeader.UserData = Convert.ToInt32(File.ReadAllText(temppath + "/timestamp.txt"), 16);

                        bararchive.AllowWhitespaceInFilenames = true;

                        foreach (string path in enumerable)
                        {
                            var fullPath = Path.Combine(temppath, path);
                            bararchive.AddFile(fullPath);
                            Main.LogDebugInfo($"Archive Creation: Processing item {i + 1}: Added file to archive: {fullPath}");
                        }

                        // Get the name of the directory
                        string directoryName = new DirectoryInfo(temppath).Name;
                        Main.LogDebugInfo($"Archive Creation: Processing item {i + 1}: Processing directory: {directoryName}");

                        // Create a text file to write the paths to
                        StreamWriter writer = new(temppath + @"/files.txt");
                        Main.LogDebugInfo($"Archive Creation: Processing item {i + 1}: Creating file list text file at: {temppath}files.txt");

                        // Get all files in the directory and its immediate subdirectories
                        string[] files = Directory.GetFiles(temppath, "*.*", SearchOption.AllDirectories);

                        // Loop through the files and write their paths to the text file
                        foreach (string file in files)
                        {
                            string relativePath = $"file=\"{file.Replace(temppath, "").TrimStart(Path.DirectorySeparatorChar)}\"";
                            writer.WriteLine(relativePath.Replace(@"\", "/"));
                            Main.LogDebugInfo($"Archive Creation: Processing item {i + 1}: Writing file path to text: {relativePath.Replace(@"\", "/")}");
                        }

                        Main.LogDebugInfo("Archive Creation: Completed writing file paths to text file.");

                        writer.Close();

                        bararchive.AddFile(temppath + @"/files.txt");

                        bararchive.CreateManifest();

                        bararchive.Save();

                        bararchive = null;

                        if (sdat && File.Exists(Main._settings.BarSdatSharcOutputDirectory + $"/{filename}.SHARC"))
                        {
                            Main.LogDebugInfo($"Archive Creation: Starting SDAT encryption for SHARC file: {filename}.SHARC");
                            RunUnBAR.RunEncrypt(Main._settings.BarSdatSharcOutputDirectory + $"/{filename}.SHARC", Main._settings.BarSdatSharcOutputDirectory + $"/{filename}.sdat");
                            File.Delete(Main._settings.BarSdatSharcOutputDirectory + $"/{filename}.SHARC");
                            Main.LogDebugInfo($"Archive Creation: SDAT encryption completed and original SHARC file deleted for: {filename}.SHARC");
                        }
                        else if (sdat && File.Exists(Main._settings.BarSdatSharcOutputDirectory + $"/{filename}.BAR"))
                        {
                            Main.LogDebugInfo($"Archive Creation: Starting SDAT encryption for BAR file: {filename}.BAR");
                            RunUnBAR.RunEncrypt(Main._settings.BarSdatSharcOutputDirectory + $"/{filename}.BAR", Main._settings.BarSdatSharcOutputDirectory + $"/{filename}.sdat");
                            File.Delete(Main._settings.BarSdatSharcOutputDirectory + $"/{filename}.BAR");
                            Main.LogDebugInfo($"Archive Creation: SDAT encryption completed and original BAR file deleted for: {filename}.BAR");
                        }

                        // Log to the textbox after creating the archive
                        await Dispatcher.InvokeAsync(() =>
                        {
                            LogBox.Append(ArchiveCreatorTextBox, $"{Environment.NewLine}Archive Creator: Success {filename}{fileExtension} Created.");
                            ArchiveCreatorTextBox.ScrollToEnd();
                        });

                        // Allow UI to process its queue
                        await LogBox.Breathe();   // let the window redraw now and then (was a 50 ms wait per item)
                    }
                    else
                    {
                        string? folderPath = Path.GetDirectoryName(itemPath);
                        string? filename = Path.GetFileName(folderPath);

                        bool sdat = false;
                        IEnumerable<string> enumerable = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories);
                        BARArchive? bararchive = null;

                        string fileExtension = "";

                        switch (Main._settings.ArchiveTypeSettingRem)
                        {
                            case ArchiveTypeSetting.BAR:
                                bararchive = new BARArchive($"{Main._settings.BarSdatSharcOutputDirectory}/{filename}.BAR", folderPath, (int)CdnMode.RETAIL, ArchiveCreatorTimestamp(), false, true);
                                fileExtension = ".BAR"; // Set the file extension
                                break;
                            case ArchiveTypeSetting.BAR_S:
                                bararchive = new BARArchive($"{Main._settings.BarSdatSharcOutputDirectory}/{filename}.bar", folderPath, (int)CdnMode.RETAIL, ArchiveCreatorTimestamp(), true, true);
                                fileExtension = ".bar"; // Set the file extension
                                break;
                            case ArchiveTypeSetting.SDAT:
                                bararchive = new BARArchive($"{Main._settings.BarSdatSharcOutputDirectory}/{filename}.BAR", folderPath, (int)CdnMode.RETAIL, ArchiveCreatorTimestamp(), false, true);
                                sdat = true;
                                fileExtension = ".sdat"; // Set the file extension
                                break;
                            case ArchiveTypeSetting.CORE_SHARC:
                                bararchive = new BARArchive($"{Main._settings.BarSdatSharcOutputDirectory}/{filename}.SHARC", folderPath, (int)CdnMode.RETAIL, ArchiveCreatorTimestamp(), true, true, ToolsImplementation.base64DefaultSharcKey);
                                fileExtension = ".SHARC"; // Set the file extension
                                break;
                            case ArchiveTypeSetting.SDAT_SHARC:
                                sdat = true;
                                bararchive = new BARArchive($"{Main._settings.BarSdatSharcOutputDirectory}/{filename}.SHARC", folderPath, (int)CdnMode.RETAIL, ArchiveCreatorTimestamp(), true, true, ToolsImplementation.base64CDNKey2);
                                fileExtension = ".sdat"; // Set the file extension
                                break;
                            case ArchiveTypeSetting.CONFIG_SHARC:
                                bararchive = new BARArchive($"{Main._settings.BarSdatSharcOutputDirectory}/{filename}.sharc", folderPath, (int)CdnMode.RETAIL, ArchiveCreatorTimestamp(), true, true, ToolsImplementation.base64CDNKey2);
                                fileExtension = ".sharc"; // Set the file extension
                                break;
                        }

                        if (File.Exists(folderPath + "/timestamp.txt"))
                            bararchive.BARHeader.UserData = Convert.ToInt32(File.ReadAllText(folderPath + "/timestamp.txt"), 16);

                        bararchive.AllowWhitespaceInFilenames = true;

                        foreach (string path in enumerable)
                        {
                            var fullPath = Path.Combine(folderPath, path);
                            bararchive.AddFile(fullPath);
                            Main.LogDebugInfo($"Archive Creation: Processing item {i + 1}: Added file to archive from directory: {fullPath}");
                        }

                        // Get the name of the directory
                        string directoryName = new DirectoryInfo(folderPath).Name;
                        Main.LogDebugInfo($"Archive Creation: Processing item {i + 1}: Processing directory into archive: {directoryName}");

                        // Create a text file to write the paths to
                        StreamWriter writer = new(folderPath + @"/files.txt");
                        Main.LogDebugInfo($"Archive Creation: Processing item {i + 1}: Creating list of files at: {folderPath}files.txt for archive manifest.");

                        // Get all files in the directory and its immediate subdirectories
                        string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);

                        // Loop through the files and write their paths to the text file
                        foreach (string file in files)
                        {
                            string relativePath = $"file=\"{file.Replace(folderPath, "").TrimStart(Path.DirectorySeparatorChar)}\"";
                            writer.WriteLine(relativePath.Replace(@"\", "/"));
                            Main.LogDebugInfo($"Archive Creation: Processing item {i + 1}: Logging file path for archive manifest: {relativePath.Replace(@"\", "/")}");
                        }

                        writer.Close();
                        Main.LogDebugInfo($"Archive Creation: Processing item {i + 1}: File list for archive manifest completed and file closed.");

                        bararchive.AddFile(folderPath + @"/files.txt");
                        Main.LogDebugInfo($"Archive Creation: Processing item {i + 1}: Added file list to archive: {folderPath}files.txt");

                        bararchive.CreateManifest();
                        Main.LogDebugInfo("Archive Creation: Manifest created for archive.");

                        bararchive.Save();

                        bararchive = null;
                        Main.LogDebugInfo($"Archive Creation: New Archive Saved at: {Main._settings.BarSdatSharcOutputDirectory}\\{filename}{fileExtension}.");

                        if (sdat && File.Exists(Main._settings.BarSdatSharcOutputDirectory + $"/{filename}.SHARC"))
                        {
                            RunUnBAR.RunEncrypt(Main._settings.BarSdatSharcOutputDirectory + $"/{filename}.SHARC", Main._settings.BarSdatSharcOutputDirectory + $"/{filename}.sdat");
                            File.Delete(Main._settings.BarSdatSharcOutputDirectory + $"/{filename}.SHARC");
                            Main.LogDebugInfo($"Archive Creation: SDAT encryption completed and original SHARC file deleted for: {filename}.SHARC");
                        }
                        else if (sdat && File.Exists(Main._settings.BarSdatSharcOutputDirectory + $"/{filename}.BAR"))
                        {
                            RunUnBAR.RunEncrypt(Main._settings.BarSdatSharcOutputDirectory + $"/{filename}.BAR", Main._settings.BarSdatSharcOutputDirectory + $"/{filename}.sdat");
                            File.Delete(Main._settings.BarSdatSharcOutputDirectory + $"/{filename}.BAR");
                            Main.LogDebugInfo($"Archive Creation: SDAT encryption completed and original BAR file deleted for: {filename}.BAR");
                        }

                        // Log to the textbox after creating the archive
                        await Dispatcher.InvokeAsync(() =>
                        {
                            LogBox.Append(ArchiveCreatorTextBox, $"{Environment.NewLine}Archive Creator: Success {filename}{fileExtension} Created.");
                            ArchiveCreatorTextBox.ScrollToEnd();
                        });

                        // Allow UI to process its queue
                        await LogBox.Breathe();   // let the window redraw now and then (was a 50 ms wait per item)
                    }

                    i++;
                }
                catch (Exception ex)
                {
                    Main.LogDebugInfo($"Archive Creation: Error processing item {i + 1}: {itemPath}, {ex.Message}");
                    await Dispatcher.InvokeAsync(() =>
                    {
                        LogBox.Append(ArchiveCreatorTextBox, $"{Environment.NewLine}Archive Creator: Error processing item {i + 1}: {itemPath}, {ex.Message}");
                        ArchiveCreatorTextBox.ScrollToEnd();
                    });
                    allItemsProcessed = false;
                }
            }

            // Check if renaming is enabled
            if (CheckBoxArchiveCreatorRenameCDN.IsChecked == true &&
                (RadioButtonArchiveCreatorBAR.IsChecked == true || RadioButtonArchiveCreatorSDAT.IsChecked == true || RadioButtonArchiveCreatorSDAT_SHARC.IsChecked == true))
            {
                Main.LogDebugInfo("Rename for CDN is enabled.");

                // Get all files in the output directory
                var outputFiles = Directory.GetFiles(Main._settings.BarSdatSharcOutputDirectory);

                foreach (var outputFilePath in outputFiles)
                {
                    string outputFileName = Path.GetFileName(outputFilePath);
                    Main.LogDebugInfo($"Processing file: {outputFileName}");

                    // Delete .map files found in the root directory
                    if (Path.GetExtension(outputFileName).Equals(".map", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            File.Delete(outputFilePath);
                            Main.LogDebugInfo($"Deleted .map file: {outputFileName}");
                            await Dispatcher.InvokeAsync(() =>

                            {
                                LogBox.Append(ArchiveCreatorTextBox, $"{Environment.NewLine}Archive Creator: Deleted {outputFileName}");
                                ArchiveCreatorTextBox.ScrollToEnd();
                            });
                        }
                        catch (Exception ex)
                        {
                            Main.LogDebugInfo($"Failed to delete .map file: {outputFileName}, {ex.Message}");
                            await Dispatcher.InvokeAsync(() =>
                            {
                                LogBox.Append(ArchiveCreatorTextBox, $"{Environment.NewLine}Archive Creator: Failed to delete {outputFileName}: {ex.Message}");
                                ArchiveCreatorTextBox.ScrollToEnd();
                            });
                        }

                        continue; // Skip further processing for this file
                    }

                    string pattern = @"^[A-F0-9]{8}-[A-F0-9]{8}-[A-F0-9]{8}-[A-F0-9]{8}_T\d{3}\..+$";

                    Main.LogDebugInfo($"Checking if file matches pattern: {pattern}");

                    if (Regex.IsMatch(outputFileName, pattern, RegexOptions.IgnoreCase))
                    {
                        Main.LogDebugInfo($"File matches pattern: {outputFileName}");
                        string newFileName = Regex.Replace(outputFileName, @"(_T\d{3})(\..+)$", "/object$1$2", RegexOptions.IgnoreCase);

                        // Remove '_T000' from the file name if present
                        newFileName = Regex.Replace(newFileName, @"_T000(?=\..+$)", "", RegexOptions.IgnoreCase);

                        string newFilePath = Path.Combine(Main._settings.BarSdatSharcOutputDirectory, "Objects", newFileName);

                        // Ensure the target directory exists
                        Directory.CreateDirectory(Path.GetDirectoryName(newFilePath));

                        // Ensure unique filename
                        int counter = 1;
                        string uniqueFilePath = newFilePath;
                        while (File.Exists(uniqueFilePath))
                        {
                            uniqueFilePath = Path.Combine(Path.GetDirectoryName(newFilePath), $"{Path.GetFileNameWithoutExtension(newFilePath)}({counter}){Path.GetExtension(newFilePath)}");
                            counter++;
                        }

                        // Additional debug check to avoid incorrect naming
                        while (File.Exists(uniqueFilePath))
                        {
                            // Log each step of the filename modification
                            Main.LogDebugInfo($"File exists, generating new filename to avoid conflict: {uniqueFilePath}");

                            uniqueFilePath = Path.Combine(Path.GetDirectoryName(newFilePath), $"{Path.GetFileNameWithoutExtension(newFilePath)}({counter}){Path.GetExtension(newFilePath)}");
                            counter++;
                        }

                        try
                        {
                            File.Move(outputFilePath, uniqueFilePath);
                            Main.LogDebugInfo($"Renamed file: {outputFileName} to {Path.GetFileName(uniqueFilePath)}");
                            await Dispatcher.InvokeAsync(() =>
                            {
                                LogBox.Append(ArchiveCreatorTextBox, $"{Environment.NewLine}Archive Creator: Renamed {outputFileName} to {Path.GetFileName(uniqueFilePath)}");
                                ArchiveCreatorTextBox.ScrollToEnd();
                            });
                        }
                        catch (Exception ex)
                        {
                            Main.LogDebugInfo($"Failed to rename file: {outputFileName} to {Path.GetFileName(uniqueFilePath)}, {ex.Message}");
                            await Dispatcher.InvokeAsync(() =>
                            {
                                LogBox.Append(ArchiveCreatorTextBox, $"{Environment.NewLine}Archive Creator: Failed to rename {outputFileName} to {Path.GetFileName(uniqueFilePath)}: {ex.Message}");
                                ArchiveCreatorTextBox.ScrollToEnd();
                            });
                        }
                    }
                    else
                    {
                        Main.LogDebugInfo($"File does not match pattern: {outputFileName}");

                        // Check for a $ in the filename if it doesn't match the pattern
                        if (outputFileName.Contains("$"))
                        {
                            string newFileName = outputFileName.Replace("$", "\\");

                            string newFilePath = Path.Combine(Main._settings.BarSdatSharcOutputDirectory, "Scenes", newFileName);

                            // Ensure the target directory exists
                            Directory.CreateDirectory(Path.GetDirectoryName(newFilePath));

                            // Ensure unique filename
                            int counter = 1;
                            string uniqueFilePath = newFilePath;
                            while (File.Exists(uniqueFilePath))
                            {
                                uniqueFilePath = Path.Combine(Path.GetDirectoryName(newFilePath), $"{Path.GetFileNameWithoutExtension(newFilePath)}({counter}){Path.GetExtension(newFilePath)}");
                                counter++;
                            }

                            try
                            {
                                File.Move(outputFilePath, uniqueFilePath);
                                Main.LogDebugInfo($"Moved file: {outputFileName} to {Path.GetFileName(uniqueFilePath)}");
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    LogBox.Append(ArchiveCreatorTextBox, $"{Environment.NewLine}Archive Creator: Moved {outputFileName} to {Path.GetFileName(uniqueFilePath)}");
                                    ArchiveCreatorTextBox.ScrollToEnd();
                                });
                            }
                            catch (Exception ex)
                            {
                                Main.LogDebugInfo($"Failed to move file: {outputFileName} to {Path.GetFileName(uniqueFilePath)}, {ex.Message}");
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    LogBox.Append(ArchiveCreatorTextBox, $"{Environment.NewLine}Archive Creator: Failed to move {outputFileName} to {Path.GetFileName(uniqueFilePath)}: {ex.Message}");
                                    ArchiveCreatorTextBox.ScrollToEnd();
                                });
                            }
                        }
                    }
                }
            }

            if (CheckBoxArchiveCreatorRenameLocal.IsChecked == true &&
                (RadioButtonArchiveCreatorBAR.IsChecked == true || RadioButtonArchiveCreatorCORE_SHARC.IsChecked == true))
            {
                var outputFiles = Directory.GetFiles(Main._settings.BarSdatSharcOutputDirectory);

                foreach (var outputFilePath in outputFiles)
                {
                    string outputFileName = Path.GetFileName(outputFilePath);

                    // Delete .map files found in the root directory
                    if (Path.GetExtension(outputFileName).Equals(".map", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            File.Delete(outputFilePath);
                            Main.LogDebugInfo($"Archive Creation: Deleted {outputFileName}");
                            await Dispatcher.InvokeAsync(() =>
                            {
                                LogBox.Append(ArchiveCreatorTextBox, $"{Environment.NewLine}Archive Creator: Deleted {outputFileName}");
                                ArchiveCreatorTextBox.ScrollToEnd();
                            });
                        }
                        catch (Exception ex)
                        {
                            Main.LogDebugInfo($"Archive Creation: Failed to delete {outputFileName}: {ex.Message}");
                            await Dispatcher.InvokeAsync(() =>
                            {
                                LogBox.Append(ArchiveCreatorTextBox, $"{Environment.NewLine}Archive Creator: Failed to delete {outputFileName}: {ex.Message}");
                                ArchiveCreatorTextBox.ScrollToEnd();
                            });
                        }

                        continue; // Skip further processing for this file
                    }

                    string pattern = @"^[A-F0-9]{8}-[A-F0-9]{8}-[A-F0-9]{8}-[A-F0-9]{8}_T\d{3}\..+$";

                    if (Regex.IsMatch(outputFileName, pattern, RegexOptions.IgnoreCase))
                    {
                        string uuid = Regex.Match(outputFileName, @"^[A-F0-9]{8}-[A-F0-9]{8}-[A-F0-9]{8}-[A-F0-9]{8}").Value;

                        // Create the new folder path with the parent OBJECTS folder
                        string newFolder = Path.Combine(Main._settings.BarSdatSharcOutputDirectory, "OBJECTS", uuid);

                        // Ensure the target directory exists
                        Directory.CreateDirectory(newFolder);

                        // Define the new file path
                        string newFilePath = Path.Combine(newFolder, $"{uuid}{Path.GetExtension(outputFileName)}");

                        // Ensure unique filename
                        int counter = 1;
                        string uniqueFilePath = newFilePath;
                        while (File.Exists(uniqueFilePath))
                        {
                            uniqueFilePath = Path.Combine(newFolder, $"{uuid}({counter}){Path.GetExtension(outputFileName)}");
                            counter++;
                        }

                        try
                        {
                            File.Move(outputFilePath, uniqueFilePath);
                            Main.LogDebugInfo($"Archive Creation: Renamed {outputFileName} to {Path.GetFileName(uniqueFilePath)}");
                            await Dispatcher.InvokeAsync(() =>
                            {
                                LogBox.Append(ArchiveCreatorTextBox, $"{Environment.NewLine}Archive Creator: Renamed {outputFileName} to {Path.GetFileName(uniqueFilePath)}");
                                ArchiveCreatorTextBox.ScrollToEnd();
                            });
                        }
                        catch (Exception ex)
                        {
                            Main.LogDebugInfo($"Archive Creation: Failed to rename {outputFileName} to {Path.GetFileName(uniqueFilePath)}: {ex.Message}");
                            await Dispatcher.InvokeAsync(() =>
                            {
                                LogBox.Append(ArchiveCreatorTextBox, $"{Environment.NewLine}Archive Creator: Failed to rename {outputFileName} to {Path.GetFileName(uniqueFilePath)}: {ex.Message}");
                                ArchiveCreatorTextBox.ScrollToEnd();
                            });
                        }
                    }
                }
            }

            // Log the completion and result of the archive creation process
            Main.LogDebugInfo("Archive Creation: Process Success");
            await Dispatcher.InvokeAsync(() =>
            {
                LogBox.Append(ArchiveCreatorTextBox, $"{Environment.NewLine}Archive Creator: Process Success\nArchive Creator: Double click here to open the output folder");
                ArchiveCreatorTextBox.ScrollToEnd();
            });

            return allItemsProcessed;
        }



        // TAB 1: Logic for Unpacking Archives

        private async void ArchiveUnpackerExecuteButtonClick(object sender, RoutedEventArgs e)
        {
            Main.LogDebugInfo("Archive Unpacker: Process Initiated");
            TemporaryMessageHelper.ShowTemporaryMessage(ArchiveUnpackerDragAreaText, "Processing....", 2000);

            Directory.CreateDirectory(Main._settings.MappedOutputDirectory);
            Main.LogDebugInfo($"Archive Unpacker: Output directory created at {Main._settings.MappedOutputDirectory}");

            string filesToUnpack = string.Join(Environment.NewLine, LogBox.TakeLines(ArchiveUnpackerTextBox));   // the list moves into memory; the box shows the log
            if (!string.IsNullOrWhiteSpace(filesToUnpack))
            {
                string[] filePaths = filesToUnpack.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                Main.LogDebugInfo($"Archive Unpacker: Starting unpacking for {filePaths.Length} files");
                bool unpackingSuccess = await UnpackFilesAsync(filePaths);

                string message = unpackingSuccess ? $"Success: {filePaths.Length} Files Unpacked" : "Unpacking Failed";
                TemporaryMessageHelper.ShowTemporaryMessage(ArchiveUnpackerDragAreaText, message, 2000);

                Main.LogDebugInfo($"Archive Unpacker: Result - {message}");
            }
            else
            {
                Main.LogDebugInfo("Archive Unpacker: Aborted - No files listed for Unpacking.");
                TemporaryMessageHelper.ShowTemporaryMessage(ArchiveUnpackerDragAreaText, "No files listed for Unpacking.", 2000);
            }
        }



       private void ArchiveUnpackerDragDropHandler(object sender, DragEventArgs e)
{
    if (e.Data.GetDataPresent(DataFormats.FileDrop))
    {
        string[] droppedItems = (string[])e.Data.GetData(DataFormats.FileDrop);
        List<string> validFiles = new List<string>();
        var validExtensions = new HashSet<string>(new[] { ".bar", ".sdat", ".sharc", ".dat" }, StringComparer.OrdinalIgnoreCase);

        // Use parallel processing to speed up file discovery
        Parallel.ForEach(droppedItems, item =>
        {
            if (Directory.Exists(item))
            {
                var filesInDirectory = Directory.EnumerateFiles(item, "*.*", SearchOption.AllDirectories)
                    .Where(file => validExtensions.Contains(Path.GetExtension(file)))
                    .ToList();

                lock (validFiles) // Locking to avoid race condition when adding to the shared list
                {
                    validFiles.AddRange(filesInDirectory);
                }
            }
            else if (File.Exists(item) && validExtensions.Contains(Path.GetExtension(item)))
            {
                lock (validFiles) // Locking for thread safety
                {
                    validFiles.Add(item);
                }
            }
        });

        if (validFiles.Count > 0)
        {
            Dispatcher.Invoke(() =>
            {
                var existingFilesSet = new HashSet<string>(ArchiveUnpackerTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                int initialCount = existingFilesSet.Count;

                existingFilesSet.UnionWith(validFiles); // Automatically removes duplicates

                // Use StringBuilder for efficient string concatenation
                ArchiveUnpackerTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);
            });
        }
    }
}





        private async void ClickToBrowseArchiveUnpackerHandler(object sender, RoutedEventArgs e)
        {
            Main.LogDebugInfo("Archive Unpacker: Browsing for files Initiated");

            // Disable the main window to prevent interactions while the dialog is open
            Main.IsEnabled = false;

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
            Main.IsEnabled = true;

            if (result == true)
            {
                var selectedFiles = openFileDialog.FileNames;

                if (selectedFiles.Any())
                {
                    Main.LogDebugInfo($"Archive Unpacker: {selectedFiles.Length} files selected via File Browser.");

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
                    Main.LogDebugInfo("Archive Unpacker: File Browser - No compatible files were selected.");
                    message = "No compatible files selected.";
                }
            }
            else
            {
                Main.LogDebugInfo("Archive Unpacker: File Browser - Dialog Cancelled.");
            }

            TemporaryMessageHelper.ShowTemporaryMessage(ArchiveUnpackerDragAreaText, message, displayTime);
        }



        private string currentFilePath;
        private string CurrentWorkFolder;
        private string UserSuppliedPathPrefix = string.Empty;
        private string UUIDFoundInPath = string.Empty;
        private string CurrentUUID = string.Empty;
        private string SceneFolderAkaID = string.Empty;
        private string GuessedUUID = string.Empty;

        private async Task<bool> UnpackFilesAsync(string[] filePaths)
        {
            Stopwatch TotalJobStopwatch = Stopwatch.StartNew();
            Main.LogDebugInfo($"Archive Unpacker: Beginning unpacking process for {filePaths.Length} files");

            string ogfilename = string.Empty;
            string Outputpath = Main._settings.MappedOutputDirectory;

            Directory.CreateDirectory(Outputpath);

            // Filter out lines starting with "Archive Unpacker:"
            filePaths = filePaths.Where(path => !path.StartsWith("Archive Unpacker:")).ToArray();

            foreach (string filePath in filePaths)
            {
                Stopwatch extractionStopwatch = Stopwatch.StartNew();
                currentFilePath = filePath;

                if (filePath.IndexOf(@"\Scenes\", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string[] pathParts = filePath.Split(new[] { @"\Scenes\" }, StringSplitOptions.None);
                    if (pathParts.Length > 1)
                    {
                        string[] subPathParts = pathParts[1].Split('\\');
                        if (subPathParts.Length > 0)
                        {
                            SceneFolderAkaID = subPathParts[0];
                            Main.LogDebugInfo($"SceneFolderAkaID: {SceneFolderAkaID}");
                        }
                    }
                }


                Main.LogDebugInfo($"Archive Unpacker: Now processing file: {currentFilePath}");

                // Log start of processing to ArchiveUnpackerTextBox
                await Dispatcher.InvokeAsync(() =>
                {
                  
                });

                if (File.Exists(filePath))
                {
                    string filename = Path.GetFileName(filePath);

                    // Determine the action based on file extension
                    if (filename.ToLower().EndsWith(".bar") || filename.ToLower().EndsWith(".dat"))
                    {
                        string barfile = Outputpath + $"/{filename}";
                        // Copy the file to the output directory for processing
                        File.WriteAllBytes(barfile, File.ReadAllBytes(filePath));
                        await RunUnBAR.Run(Directory.GetCurrentDirectory(), barfile, Outputpath, false, (int)CdnMode.RETAIL);
                        ogfilename = filename;
                        filename = filename[..^4];

                        extractionStopwatch.Stop();
                        Main.LogDebugInfo($"Archive Unpacker: Extraction Complete for {filename} (Time Taken {extractionStopwatch.ElapsedMilliseconds}ms)");
                    }
                    else if (filename.ToLower().EndsWith(".sharc"))
                    {
                        string barfile = Outputpath + $"/{filename}";
                        File.WriteAllBytes(barfile, File.ReadAllBytes(filePath));
                        await RunUnBAR.Run(Directory.GetCurrentDirectory(), barfile, Outputpath, false, (int)CdnMode.RETAIL);
                        ogfilename = filename;
                        filename = filename[..^6];

                        extractionStopwatch.Stop();
                        Main.LogDebugInfo($"Archive Unpacker: Extraction Complete for {filename} (Time Taken {extractionStopwatch.ElapsedMilliseconds}ms)");
                    }
                    else if (filename.ToLower().EndsWith(".sdat"))
                    {
                        await RunUnBAR.Run(Directory.GetCurrentDirectory(), filePath, Outputpath, true, (int)CdnMode.RETAIL);
                        ogfilename = filename;
                        filename = filename[..^5];

                        extractionStopwatch.Stop();
                        Main.LogDebugInfo($"Archive Unpacker: Extraction Complete for {filename} (Time Taken {extractionStopwatch.ElapsedMilliseconds}ms)");
                    }
                    else if (filename.ToLower().EndsWith(".zip"))
                    {
                        string barfile = Outputpath + $"/{filename}";
                        UncompressFile(barfile, Outputpath);
                        ogfilename = filename;
                        filename = filename[..^4];
                        extractionStopwatch.Stop();
                        Main.LogDebugInfo($"Archive Unpacker: Extraction Complete for {filename} (Time Taken {extractionStopwatch.ElapsedMilliseconds}ms)");
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

                    // Log completion of processing to ArchiveUnpackerTextBox
                    await Dispatcher.InvokeAsync(() =>
                    {
                        
                    });

                    // Allow UI to process its queue
                    await LogBox.Breathe();   // let the window redraw now and then (was a 50 ms wait per item)
                }
            }

            TotalJobStopwatch.Stop();
            Main.LogDebugInfo($"Archive Unpacker: Job Complete - All files processed (Time Taken {TotalJobStopwatch.ElapsedMilliseconds}ms)");

            // Log completion of all files to ArchiveUnpackerTextBox
            await Dispatcher.InvokeAsync(() =>
            {
               
            });

            return true;
        }


        public async Task ExperimentalMapperAsync(string directoryPath, string ogfilename)
        {
            // Check if the Coredata checkbox is checked and call MapCoreData041 if it is
            if (CheckBoxArchiveMapperCoredata.IsChecked == true)
            {
                await MapCoreData041(directoryPath, ogfilename);
                return;
            }

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

            Main.LogDebugInfo($"Archive Unpacker: Starting experimental mapping for directory: {directoryPath}");
            var allFilePaths = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
            var filePaths = allFilePaths
                .Where(path =>
                    !path.EndsWith(".dds") &&
                    !path.EndsWith(".mp3") &&
                    !path.EndsWith(".mp4"))
                .ToArray();

            var hashesToPaths = await InitialScanForPaths(filePaths);
            Main.LogDebugInfo($"Archive Unpacker: Total unique hashes and paths found: {hashesToPaths.Count}");

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
                Main.LogDebugInfo($"Error in mapping operation: {ex.Message}");
            }

            MappingStopwatch.Stop();
            Main.LogDebugInfo($"Archive Unpacker: Mapping Completed (Time Taken {MappingStopwatch.ElapsedMilliseconds}ms)");
        }

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

                    Main.LogDebugInfo($"UUID Guesser Check: uuid_helper.txt found with {fileLines.Length} lines.");
                }
                else
                {
                    Main.LogDebugInfo("UUID Guesser Check: uuid_helper.txt not found.");
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
                            Main.LogDebugInfo($"UUID Guesser Check: Guessed UUID = {formattedUUID}");

                            // Only set CurrentUUID if it's currently empty
                            if (string.IsNullOrEmpty(CurrentUUID))
                            {
                                CurrentUUID = entry.Value.ToUpper(); // Set CurrentUUID to the raw UUID value from the file
                                Main.LogDebugInfo($"UUID Guesser Check: CurrentUUID set to {CurrentUUID}");
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
                    Main.LogDebugInfo("UUID Guesser Check: No matching UUID guessed from file names.");
                }
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task MapCoreData041(string directoryPath, string ogfilename)
        {
            // Logging messages at the start
            string logMessage = "Map CoreData: NOTE: COREDATA Mode enabled. This does not scan for paths in the usual way.\nMap CoreData: Instead it uses a preset list of paths to rename the files. \nMap CoreData: Use this mode for COREDATA/DEVARCHIVE/CONFIG/COREOBJECTS/NPBOOT etc.\nMap CoreData: Also can be used for 0.41 era BARs\nMap CoreData: Discovered a filename for an Unmapped file? Add to Mapper in the Path2Hash Tool!\nMap CoreData: COREDATA Mode Static Path Processing started";
            Main.LogDebugInfo(logMessage);
            Dispatcher.Invoke(() =>
            {
                LogBox.Append(ArchiveUnpackerTextBox, $"{Environment.NewLine}{logMessage}");
                ArchiveUnpackerTextBox.ScrollToEnd();
            });

            string directoryName = new DirectoryInfo(directoryPath).Name;
            logMessage = $"Map CoreData: Handling directory: {directoryName} with original filename: {ogfilename}";
            Main.LogDebugInfo(logMessage);
            Dispatcher.Invoke(() =>
            {
                LogBox.Append(ArchiveUnpackerTextBox, $"{Environment.NewLine}{logMessage}");
                ArchiveUnpackerTextBox.ScrollToEnd();
            });

            // Load the helper file if not already loaded
            if (!isHelperLoaded)
            {
                logMessage = "Map CoreData: Loading core_data_mapper_helper.txt. Please wait...";
                Main.LogDebugInfo(logMessage);
                Dispatcher.Invoke(() =>
                {
                    LogBox.Append(ArchiveUnpackerTextBox, $"{Environment.NewLine}{logMessage}");
                    ArchiveUnpackerTextBox.ScrollToEnd();
                });

                string exeLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string dependencyPath = Path.Combine(Path.GetDirectoryName(exeLocation), "dependencies", "core_data_mapper_helper.txt");

                if (File.Exists(dependencyPath))
                {
                    var fileLines = await File.ReadAllLinesAsync(dependencyPath);
                    foreach (var line in fileLines)
                    {
                        var parts = line.Split(':');
                        if (parts.Length == 2)
                        {
                            // Store each hash and its corresponding new path
                            hashToPath[parts[0].Trim()] = parts[1].Trim();
                        }
                    }

                    isHelperLoaded = true;
                    logMessage = $"Map CoreData: core_data_mapper_helper.txt loaded successfully with {fileLines.Length} lines.";
                    Main.LogDebugInfo(logMessage);
                    Dispatcher.Invoke(() =>
                    {
                        LogBox.Append(ArchiveUnpackerTextBox, $"{Environment.NewLine}{logMessage}");
                        ArchiveUnpackerTextBox.ScrollToEnd();
                    });
                }
                else
                {
                    logMessage = "Map CoreData: core_data_mapper_helper.txt not found.";
                    Main.LogDebugInfo(logMessage);
                    Dispatcher.Invoke(() =>
                    {
                        LogBox.Append(ArchiveUnpackerTextBox, $"{Environment.NewLine}{logMessage}");
                        ArchiveUnpackerTextBox.ScrollToEnd();
                    });
                    return;
                }
            }

            // Get all files in the root of the directory path, excluding subdirectories
            var rootFiles = Directory.GetFiles(directoryPath, "*", SearchOption.TopDirectoryOnly);

            int filesRenamedCount = 0;

            foreach (var filePath in rootFiles)
            {
                string filenameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
                if (hashToPath.TryGetValue(filenameWithoutExtension, out var newPath))
                {
                    string newFilePath = Path.Combine(directoryPath, newPath);

                    // Ensure the target directory exists
                    Directory.CreateDirectory(Path.GetDirectoryName(newFilePath));

                    try
                    {
                        // Rename the file to the new path
                        File.Move(filePath, newFilePath);
                        Main.LogDebugInfo($"Map CoreData: Renamed {filePath} to {newFilePath}");
                        filesRenamedCount++;
                    }
                    catch (Exception ex)
                    {
                        Main.LogDebugInfo($"Map CoreData: Failed to rename {filePath} to {newFilePath}: {ex.Message}");
                    }

                    // Allow the UI to update
                    await Task.Yield();
                }
            }

            logMessage = $"Map CoreData: {filesRenamedCount} files renamed.";
            Main.LogDebugInfo(logMessage);
            Dispatcher.Invoke(() =>
            {
                LogBox.Append(ArchiveUnpackerTextBox, $"{Environment.NewLine}{logMessage}");
                ArchiveUnpackerTextBox.ScrollToEnd();
            });

            // Rescan the directory for unmapped files
            rootFiles = Directory.GetFiles(directoryPath, "*", SearchOption.TopDirectoryOnly);

            // Check for unmapped files
            var unmappedFiles = rootFiles.Where(filePath =>
            {
                string filenameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
                return Regex.IsMatch(filenameWithoutExtension, @"^[A-F0-9]{8}$", RegexOptions.IgnoreCase);
            }).ToList();

            if (unmappedFiles.Any())
            {
                logMessage = $"Map CoreData: {unmappedFiles.Count} unmapped files found:";
                Main.LogDebugInfo(logMessage);
                Dispatcher.Invoke(() =>
                {
                    LogBox.Append(ArchiveUnpackerTextBox, $"{Environment.NewLine}{logMessage}");
                    ArchiveUnpackerTextBox.ScrollToEnd();
                });

                foreach (var unmappedFile in unmappedFiles)
                {
                    logMessage = $"Map CoreData: Unmapped file - {unmappedFile}";
                    Main.LogDebugInfo(logMessage);


                    // Allow the UI to update
                    await Task.Yield();
                }
            }
            else
            {
                logMessage = "Map CoreData: No unmapped files found.";
                Main.LogDebugInfo(logMessage);
                Dispatcher.Invoke(() =>
                {
                    LogBox.Append(ArchiveUnpackerTextBox, $"{Environment.NewLine}{logMessage}");
                    ArchiveUnpackerTextBox.ScrollToEnd();
                });
            }

            logMessage = "Map CoreData: Completed handling the directory.\nMap CoreData: Double click here to open Output folder";
            Main.LogDebugInfo(logMessage);
            Dispatcher.Invoke(() =>
            {
                LogBox.Append(ArchiveUnpackerTextBox, $"{Environment.NewLine}{logMessage}");
                ArchiveUnpackerTextBox.ScrollToEnd();
            });
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
                        Main.LogDebugInfo($"Archive Unpacker: Failed to rename {filePath} to {newFilePath}: {ex.Message}");
                    }
                }
            });
            RenameFilesStopwatch.Stop();

            Main.LogDebugInfo($"Archive Unpacker: Mapping Stage 3 Complete - Renamed files by Hash (Time Taken {RenameFilesStopwatch.ElapsedMilliseconds}ms)");
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
            Main.LogDebugInfo($"Archive Unpacker: Starting directory final check: {directoryPath}");

            string directoryName = Path.GetFileName(directoryPath);
            string parentDirectoryPath = Directory.GetParent(directoryPath)?.FullName ?? string.Empty;
            string newDirectoryName = directoryPath;

            // Handling when CurrentUUID is not set and the directory ends with "_DAT"
            if (string.IsNullOrEmpty(CurrentUUID) && directoryName.EndsWith("_DAT"))
            {
                newDirectoryName = await FindAndRenameForSceneFile(directoryPath, directoryName, parentDirectoryPath);
                if (newDirectoryName != directoryPath && !Directory.Exists(newDirectoryName))
                {
                    Directory.Move(directoryPath, newDirectoryName);
                    Main.LogDebugInfo($"Directory renamed from '{directoryPath}' to '{newDirectoryName}'.");
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

                // Refined conflict check
                if (newDirectoryName != directoryPath && !Directory.Exists(newDirectoryName))
                {
                    Directory.Move(directoryPath, newDirectoryName);
                    Main.LogDebugInfo($"Directory renamed from '{directoryPath}' to '{newDirectoryName}'.");
                    directoryPath = newDirectoryName; // Update directoryPath for subsequent operations
                }
            }
            else if (!directoryName.StartsWith("object_", StringComparison.OrdinalIgnoreCase) && !directoryName.Contains("object", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(SceneFolderAkaID))
                {
                    newDirectoryName = Path.Combine(parentDirectoryPath, SceneFolderAkaID + "$" + directoryName);
                    SceneFolderAkaID = string.Empty;

                    // Enhanced conflict check
                    if (newDirectoryName != directoryPath && !Directory.Exists(newDirectoryName))
                    {
                        try
                        {
                            Directory.Move(directoryPath, newDirectoryName);
                            Main.LogDebugInfo($"Directory renamed from '{directoryPath}' to '{newDirectoryName}'.");
                            directoryPath = newDirectoryName; // Update directoryPath for subsequent operations
                        }
                        catch (Exception ex)
                        {
                            Main.LogDebugInfo($"Failed to rename directory from '{directoryPath}' to '{newDirectoryName}'. Exception: {ex.Message}");
                        }
                    }
                    else
                    {
                        Main.LogDebugInfo($"New directory name '{newDirectoryName}' already exists or is the same as the original. No renaming necessary.");
                    }
                }
                else
                {
                    Main.LogDebugInfo($"SceneFolderAkaID is not set. Skipping renaming.");
                }
            }

            // File handling and deletion logic remains unchanged
            string file1 = Path.Combine(directoryPath, "1BA97CA6");
            string file2 = Path.Combine(directoryPath, "7AB93954");
            string targetFile1 = Path.Combine(directoryPath, "__$manifest$__");
            string targetFile2 = Path.Combine(directoryPath, "files.txt");

            if (CheckBoxArchiveMapperDeleteFilestxt.IsChecked == true)
            {
                if (File.Exists(file1)) File.Delete(file1);
                if (File.Exists(file2)) File.Delete(file2);
                if (File.Exists(targetFile1)) File.Delete(targetFile1);
                if (File.Exists(targetFile2)) File.Delete(targetFile2);
            }
            else
            {
                if (File.Exists(file1))
                {
                    if (File.Exists(targetFile1)) File.Delete(targetFile1); // Overwrite if it exists
                    File.Move(file1, targetFile1);
                }
                if (File.Exists(file2))
                {
                    if (File.Exists(targetFile2)) File.Delete(targetFile2); // Overwrite if it exists
                    File.Move(file2, targetFile2);
                }
            }

            // Check and patch files if enabled
            if (CheckBoxArchiveMapperPatchMDLs.IsChecked == true)
            {
                var mdlFiles = Directory.GetFiles(directoryPath, "*.MDL", SearchOption.AllDirectories);
                foreach (var mdlFile in mdlFiles)
                {
                    byte[] fileBytes = File.ReadAllBytes(mdlFile);
                    if (fileBytes.Length > 4 && fileBytes[3] == 0x04)
                    {
                        fileBytes[3] = 0x03;
                        File.WriteAllBytes(mdlFile, fileBytes);
                    }
                }

                var sceneFiles = Directory.GetFiles(directoryPath, "*.scene", SearchOption.AllDirectories);
                foreach (var sceneFile in sceneFiles)
                {
                    string fileContent = File.ReadAllText(sceneFile);
                    if (fileContent.Contains("file:///"))
                    {
                        fileContent = fileContent.Replace("file:///", "file://");
                        File.WriteAllText(sceneFile, fileContent);
                    }
                }
            }

            // Scanning logic remains unchanged
            if (CheckBoxArchiveMapperVerify.IsChecked ?? false)
            {
                Stopwatch scanStopwatch = Stopwatch.StartNew();
                FileScanner scanner = new FileScanner();
                await scanner.ScanDirectoryAsync(directoryPath);
                scanStopwatch.Stop();
                Main.LogDebugInfo($"FileScanner: Scanned directory {directoryPath} (Time Taken: {scanStopwatch.ElapsedMilliseconds}ms)");
            }

            // Moving directory to "Complete" folder with enhanced conflict resolution
            bool hasUnmappedFiles = CheckForUnmappedFiles(directoryPath);
            string checkedFolder = Path.Combine(parentDirectoryPath, "Complete");
            Directory.CreateDirectory(checkedFolder);
            string finalDirectoryPath = Path.Combine(checkedFolder, Path.GetFileName(directoryPath));
            if (hasUnmappedFiles)
            {
                finalDirectoryPath += "_CHECK";
            }

            // Enhanced conflict resolution when moving to "Complete"
            if (Directory.Exists(finalDirectoryPath))
            {
                Main.LogDebugInfo($"Conflict detected: '{finalDirectoryPath}' already exists. Checking contents...");

                // Check if the existing directory is empty or contains expected contents
                var existingFiles = Directory.GetFiles(finalDirectoryPath);
                var existingDirs = Directory.GetDirectories(finalDirectoryPath);

                // If empty, consider removing it before moving
                if (!existingFiles.Any() && !existingDirs.Any())
                {
                    Main.LogDebugInfo($"Existing directory '{finalDirectoryPath}' is empty. Deleting it to avoid conflict.");
                    Directory.Delete(finalDirectoryPath);
                }
                else
                {
                    // Add a suffix only if a real conflict is found
                    int suffixCounter = 2;
                    string baseNewDirectoryName = finalDirectoryPath;
                    while (Directory.Exists(finalDirectoryPath))
                    {
                        Main.LogDebugInfo($"Attempting to resolve conflict by renaming to '{finalDirectoryPath} ({suffixCounter})'.");
                        finalDirectoryPath = $"{baseNewDirectoryName} ({suffixCounter++})";
                    }
                }
            }

            // Final move operation with updated path
            Directory.Move(directoryPath, finalDirectoryPath);
            Main.LogDebugInfo($"Archive Unpacker: Directory moved to 'checked' at '{finalDirectoryPath}'.");

            // Copy JobReport.txt if exists
            string sourceFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "JobReport.txt");
            string destinationFile = Path.Combine(checkedFolder, "JobReport.txt");
            if (File.Exists(sourceFile))
            {
                File.Copy(sourceFile, destinationFile, overwrite: true);
                Main.LogDebugInfo($"JobReport.txt was copied to '{destinationFile}'.");
            }

            // Check if repacking is enabled
            if (CheckBoxArchiveMapperRepackAll.IsChecked == true)
            {
                Main.LogDebugInfo($"Repacker: Starting repacking for folder {finalDirectoryPath}");

                // Call the repacker and wait for it to finish
                bool repackSuccess = await StartRepackingAsync(finalDirectoryPath);

                if (repackSuccess)
                {
                    Main.LogDebugInfo($"Repacker: Repacking completed successfully for folder {finalDirectoryPath}");
                }
                else
                {
                    Main.LogDebugInfo($"Repacker: Repacking failed for folder {finalDirectoryPath}");
                }
            }
            else
            {
                Main.LogDebugInfo("Repacker: Repacking is disabled, skipping.");
            }

            // Call CreateHDKFolderStructure if CheckBoxArchiveMapperOfflineMode is checked
            if (CheckBoxArchiveMapperOfflineMode.IsChecked ?? false)
            {
                await CreateHDKFolderStructure(finalDirectoryPath);
            }
        }


        private async Task<bool> StartRepackingAsync(string folderPath)
        {
            try
            {
                // Simulate the drag-and-drop process by adding the folder path to the items list
                List<string> itemsToAdd = new List<string>();
                string itemWithTrailingSlash = folderPath.EndsWith(Path.DirectorySeparatorChar.ToString()) ? folderPath : folderPath + Path.DirectorySeparatorChar;
                itemsToAdd.Add(itemWithTrailingSlash);

                if (itemsToAdd.Count > 0)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        // Retrieve existing items from ArchiveCreatorTextBox and add new items
                        var existingItemsSet = new HashSet<string>(ArchiveCreatorTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingItemsSet.Count;

                        existingItemsSet.UnionWith(itemsToAdd);
                        int newItemsCount = existingItemsSet.Count - initialCount;
                        int duplicatesCount = itemsToAdd.Count - newItemsCount;

                        ArchiveCreatorTextBox.Text = string.Join(Environment.NewLine, existingItemsSet);

                        // Display a message about the items added
                        string message = $"{newItemsCount} item(s) added";
                        if (duplicatesCount > 0)
                        {
                            message += $", {duplicatesCount} duplicate(s) filtered";
                        }
                        TemporaryMessageHelper.ShowTemporaryMessage(ArchiveCreatorDragAreaText, message, 2000);

                        Main.LogDebugInfo($"Archive Creation: {newItemsCount} items added, {duplicatesCount} duplicates filtered from simulated Drag and Drop.");
                    });
                }
                else
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        Main.LogDebugInfo("Simulated Drag and Drop - No valid ZIP files or folders found.");
                        TemporaryMessageHelper.ShowTemporaryMessage(ArchiveCreatorDragAreaText, "No valid ZIP files or folders found.", 2000);
                    });

                    return false; // Indicate failure due to no valid items
                }

                // Simulate clicking the create button after the drag-and-drop operation
                await Task.Delay(100); // Give time for UI to update
                ArchiveCreatorExecuteButtonClick(this, new RoutedEventArgs());

                return true; // Indicate success
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Repacker: An error occurred during repacking: {ex.Message}");
                return false; // Indicate failure
            }
        }

        private void CheckBoxArchiveMapperRepackAll_Checked(object sender, RoutedEventArgs e)
        {
            // Check and enable the CheckBoxArchiveMapperVerify checkbox
            CheckBoxArchiveMapperVerify.IsChecked = true;


            // Disable the other specified checkboxes
            CheckBoxArchiveMapperOfflineMode.IsChecked = false;
            CheckBoxArchiveMapperCoredata.IsChecked = false;
        }

        private void CheckBoxArchiveMapperOfflineMode_Checked(object sender, RoutedEventArgs e)
        {
            // Check and enable the CheckBoxArchiveMapperVerify checkbox
            CheckBoxArchiveMapperVerify.IsChecked = true;


            // Disable the other specified checkboxes
            CheckBoxArchiveMapperRepackAll.IsChecked = false;
            CheckBoxArchiveMapperCoredata.IsChecked = false;
        }

        private void CheckBoxArchiveMapperCoredata_Checked(object sender, RoutedEventArgs e)
        {
            // Check and enable the CheckBoxArchiveMapperVerify checkbox


            // Disable the other specified checkboxes
            CheckBoxArchiveMapperRepackAll.IsChecked = false;
            CheckBoxArchiveMapperOfflineMode.IsChecked = false;
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
                    Main.LogDebugInfo($"Found .SCENE file: {sceneFileName}, renaming directory to '{newDirectoryName}'.");
                }
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error finding .SCENE file: {ex.Message}");
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
                Main.LogDebugInfo($"resources.xml found at: {resourcesXmlPath}");
                // Move files listed in resources.xml
                await MoveAllExceptSpecificFiles(resourcesXmlPath, directoryPath);
            }
            else
            {
                Main.LogDebugInfo("resources.xml not found in the directory structure.");
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
            Main.LogDebugInfo($"Archive Unpacker: Starting initial scan of {filePaths.Count()} files.");

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
                    Main.LogDebugInfo($"Archive Unpacker: Error processing file {filePath}: {ex.Message}");
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
            Main.LogDebugInfo($"Archive Unpacker: Mapping Stage 2 - Path Scan Complete. Total matches found {hashesToPaths.Count} (Time Taken {FilescanforpathsStopwatch.ElapsedMilliseconds}ms)");

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
                Main.LogDebugInfo("Archive Unpacker: CurrentUUID is already set, skipping scene file mapping.");
                return;  // Exit the function early
            }

            Stopwatch SceneFileLookupStopwatch = Stopwatch.StartNew();
            Main.LogDebugInfo($"Archive Unpacker: Unmapped file detected in: {directoryPath} - Checking if it's a known scene file in lookup table");

            string helperFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies\\scene_file_mapper_helper.txt");

            if (!File.Exists(helperFilePath))
            {
                Main.LogDebugInfo("Archive Unpacker: Helper file not found.");
                return;
            }

            Main.LogDebugInfo("Archive Unpacker: Helper file found, processing mappings.");

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
                        Main.LogDebugInfo($"Archive Unpacker: Error renaming file from {filePath} to {newFilePath}: {ex.Message}");
                    }
                }
            }

            SceneFileLookupStopwatch.Stop(); // Stop timing the extraction
            Main.LogDebugInfo($"Archive Unpacker: Mapping Stage 4 Complete - Scene filename lookup (Time Taken {SceneFileLookupStopwatch.ElapsedMilliseconds}ms)");
        }


        public void CheckForUUID()
        {
            // Define a regex pattern for the UUID format 8-8-8-8, possibly followed by underscores and other characters
            string uuidPattern = @"\b([a-f0-9]{8}-[a-f0-9]{8}-[a-f0-9]{8}-[a-f0-9]{8})(?:_[\w]*)?\b";

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
                // Save the found UUID, ignoring any suffix
                tempUUID = match.Groups[1].Value.ToLower();
                // If a UUID is found, save it as UserSuppliedUUID in lowercase, prepended and appended as specified
                UserSuppliedPathPrefix = "objects/" + tempUUID + "/";
            }

            // Check currentFilePath for a UUID in lowercase
            match = Regex.Match(currentFilePath.ToLower(), uuidPattern);
            if (match.Success)
            {
                // Save the found UUID, ignoring any suffix
                tempUUID = match.Groups[1].Value.ToLower();
                // If a UUID is found in currentFilePath, save it as UUIDFoundInPath in lowercase, prepended and appended as specified
                UUIDFoundInPath = "objects/" + tempUUID + "/";
            }

            // If any UUID was found, save it to CurrentUUID
            if (!string.IsNullOrEmpty(tempUUID))
            {
                CurrentUUID = tempUUID.ToUpper();
            }

            Main.LogDebugInfo($"UUID Check: UUIDFoundInPath set to {UUIDFoundInPath}, CurrentUUID set to {CurrentUUID}, UserSuppliedPathPrefix set to {UserSuppliedPathPrefix}");
        }


        private int ComputeAFSHash(string text)
        {
            unchecked
            {
                int hashOfPath = 0;
                foreach (char ch in text)
                    hashOfPath = hashOfPath * 37 + ch;
                Main.LogDebugInfo($"Computed Hash: {hashOfPath} for Text: {text}");

                return hashOfPath;
            }
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


        private void ArchiveUnpackerTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Get the text from the MappedOutputDirectoryTextBox
            string path = Main._settings.MappedOutputDirectory;

            // Check if the path is a valid directory, if not create it
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            // Open the directory in File Explorer
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open"
            });
        }

        private void ArchiveCreatorTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Get the text from the BarSdatSharcOutputDirectoryTextBox
            string path = Main._settings.BarSdatSharcOutputDirectory;

            // Check if the path is a valid directory, if not create it
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            // Open the directory in File Explorer
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open"
            });
        }

        private void CheckBoxArchiveCreatorRenameCDN_Checked(object sender, RoutedEventArgs e)
        {
            if (CheckBoxArchiveCreatorRenameLocal.IsChecked == true)
            {
                CheckBoxArchiveCreatorRenameLocal.IsChecked = false;
            }
        }

        private void CheckBoxArchiveCreatorRenameCDN_Unchecked(object sender, RoutedEventArgs e)
        {
            // Optionally handle when the CDN checkbox is unchecked
        }

        private void CheckBoxArchiveCreatorRenameLocal_Checked(object sender, RoutedEventArgs e)
        {
            if (CheckBoxArchiveCreatorRenameCDN.IsChecked == true)
            {
                CheckBoxArchiveCreatorRenameCDN.IsChecked = false;
            }
        }

        private void CheckBoxArchiveCreatorRenameLocal_Unchecked(object sender, RoutedEventArgs e)
        {
            // Optionally handle when the Local checkbox is unchecked
        }

        // Handlers used in this tab's XAML that live elsewhere.
        private void ClearListHandler(object sender, RoutedEventArgs e) => Main.ClearListHandler(sender, e);
    }
}
