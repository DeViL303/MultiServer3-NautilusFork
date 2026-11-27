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
    // "HCDB / SQL" tab.
    public partial class HcdbToolTab : UserControl
    {
        private static MainWindow Main => MainWindow.Instance;

        public HcdbToolTab()
        {
            InitializeComponent();
        }

        // TAB 3: Logic for packing SQL to HCDB

        // HCDB Encrypter execute button click
        private async void HCDBEncrypterExecuteButtonClick(object sender, RoutedEventArgs e)
        {
            Main.LogDebugInfo("HCDB Conversion: Process Initiated");
            TemporaryMessageHelper.ShowTemporaryMessage(HCDBEncrypterDragAreaText, "Processing....", 1000000);

            if (!Directory.Exists(Main._settings.HcdbOutputDirectory))
            {
                Directory.CreateDirectory(Main._settings.HcdbOutputDirectory);
                Main.LogDebugInfo($"HCDB Conversion: Output directory created at {Main._settings.HcdbOutputDirectory}");
            }

            string filesToConvert = string.Join(Environment.NewLine, LogBox.TakeLines(HCDBEncrypterTextBox));   // the list moves into memory; the box shows the log
            if (!string.IsNullOrWhiteSpace(filesToConvert))
            {
                string[] filePaths = filesToConvert.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                string[] segsPaths = filePaths.Select(fp => fp + ".segs").ToArray(); // Assuming LZMA outputs files with .segs extension

                Main.LogDebugInfo($"HCDB Conversion: Starting conversion for {filePaths.Length} files");
                bool conversionSuccess = await ConvertSqlToHcdbAsync(filePaths);

                if (conversionSuccess)
                {
                    bool encryptionSuccess = await EncryptHCDBFilesAsync(segsPaths, Main._settings.HcdbOutputDirectory); // Encrypt the .segs files
                    string message = encryptionSuccess ? $"Success: {filePaths.Length} Files Converted and Encrypted" : "Encryption Failed";
                    TemporaryMessageHelper.ShowTemporaryMessage(HCDBEncrypterDragAreaText, message, 2000);
                    Main.LogDebugInfo($"HCDB Conversion: Result - {message}");
                }
                else
                {
                    string message = "Conversion Failed";
                    TemporaryMessageHelper.ShowTemporaryMessage(HCDBEncrypterDragAreaText, message, 2000);
                    Main.LogDebugInfo($"HCDB Conversion: Result - {message}");
                }
            }
            else
            {
                Main.LogDebugInfo("HCDB Conversion: Aborted - No SQL files listed for conversion.");
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
                        Main.LogDebugInfo($"Conversion failed for {filename}.");
                        allFilesProcessed = false;
                    }
                    else
                    {
                        Main.LogDebugInfo($"Conversion successful for {filename}, output likely written to {expectedOutputPath}.");
                    }
                }
                catch (Exception ex)
                {
                    Main.LogDebugInfo($"Error processing {filename}: {ex.Message}");
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
                        Main.LogDebugInfo($"LZMA compression successful for {Path.GetFileName(inputFilePath)}");
                        return true;
                    }
                    else
                    {
                        Main.LogDebugInfo($"LZMA compression failed for {Path.GetFileName(inputFilePath)}: {errors}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error during LZMA compression: {ex.Message}");
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
                        Main.LogDebugInfo($"Input SHA1 for {cleanFilename}: {inputSHA1}");

                        string computedSha1 = inputSHA1.Substring(0, 16);
                        encryptedContent = CDSProcess.CDSEncrypt_Decrypt(fileContent, computedSha1, (int)CdnMode.RETAIL);
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
                        Main.LogDebugInfo($"File {cleanFilename} encrypted and written to {outputPath}.");

                        // After successful encryption, delete the .segs file
                        File.Delete(filePath);
                        Main.LogDebugInfo($"Temporary file {filePath} deleted after successful encryption.");
                    }
                    else
                    {
                        allFilesProcessed = false;
                        Main.LogDebugInfo($"Encryption failed for {cleanFilename}, no data written.");
                    }
                }
                catch (Exception ex)
                {
                    Main.LogDebugInfo($"Error encrypting {cleanFilename}: {ex.Message}");
                    allFilesProcessed = false;
                }
            }
            return allFilesProcessed;
        }



        private void HCDBEncrypterDragDropHandler(object sender, DragEventArgs e)
        {
            Main.LogDebugInfo("HCDB Conversion: Drag and Drop Initiated");
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

                        Main.LogDebugInfo($"HCDB Conversion: {newFilesCount} SQL files added, {duplicatesCount} duplicates filtered");
                    });
                    displayTime = 1000; // Change display time since files were added
                }
                else
                {
                    message = "No SQL files found.";
                    Main.LogDebugInfo("HCDB Conversion: No SQL files found in Drag and Drop.");
                }

                TemporaryMessageHelper.ShowTemporaryMessage(HCDBEncrypterDragAreaText, message, 2000);
            }
            else
            {
                Main.LogDebugInfo("HCDB Conversion: Drag and Drop - No Data Present.");
                TemporaryMessageHelper.ShowTemporaryMessage(HCDBEncrypterDragAreaText, "Drag and Drop operation failed - No Data Present.", 2000);
            }
        }



        private async void ClickToBrowseHCDBEncryptHandler(object sender, RoutedEventArgs e)
        {
            Main.LogDebugInfo("HCDB Conversion: Browsing for SQL files Initiated");

            // Disable the main window to prevent interactions while the dialog is open
            Main.IsEnabled = false;

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
            Main.IsEnabled = true;

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

                        Main.LogDebugInfo($"HCDB Conversion: {newFilesCount} SQL files added, {duplicatesCount} duplicates filtered via File Browser");
                    });
                    displayTime = 1000; // Change display time since files were added
                }
                else
                {
                    message = "No SQL files selected.";
                    Main.LogDebugInfo("HCDB Conversion: No SQL files selected in File Browser.");
                }
            }
            else
            {
                Main.LogDebugInfo("HCDB Conversion: File Browser - Dialog Cancelled.");
            }

            TemporaryMessageHelper.ShowTemporaryMessage(HCDBEncrypterDragAreaText, message, 2000);
        }



        // TAB 3: Logic for packing HCDB to SQL

        // HCDB Decrypter execute button click
        private async void HCDBDecrypterExecuteButtonClick(object sender, RoutedEventArgs e)
        {
            Main.LogDebugInfo("HCDB Decryption: Process Initiated");
            TemporaryMessageHelper.ShowTemporaryMessage(HCDBDecrypterDragAreaText, "Processing...", 2000);

            string outputDirectory = Main._settings.SqlOutputDirectory;
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
                Main.LogDebugInfo($"HCDB Decryption: Output directory created at {outputDirectory}");
            }

            string filesToDecrypt = string.Join(Environment.NewLine, LogBox.TakeLines(HCDBDecrypterTextBox));   // the list moves into memory; the box shows the log
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
                        Main.LogDebugInfo($"Using SHA1 for {filename}: {effectiveSha1}. Starting decryption.");
                        decryptedData = await Main.DecryptHCDBFilesSHA1Async(filePath, outputDirectory, effectiveSha1);
                    }
                    else
                    {
                        Main.LogDebugInfo($"No valid SHA1 detected or provided for {filename}. Using standard decryption.");
                        decryptedData = await Main.DecryptHCDBFilesAsync(filePath, outputDirectory);
                    }

                    if (decryptedData != null)
                    {
                        if (!await ProcessDecryptedHCDB(filePath, decryptedData, outputDirectory, cleanFilename))
                        {
                            decryptionSuccess = false;
                            Main.LogDebugInfo($"Post-decryption processing failed for {filename}.");
                        }
                        else
                        {
                            Main.LogDebugInfo($"Post-decryption processing succeeded for {filename}.");
                        }
                    }
                    else
                    {
                        decryptionSuccess = false;
                        Main.LogDebugInfo($"Decryption failed for {filename}.");
                    }
                }

                string message = decryptionSuccess ? "Success: All HCDB files decrypted and processed" : "Decryption or processing failed for one or more files";
                TemporaryMessageHelper.ShowTemporaryMessage(HCDBDecrypterDragAreaText, message, 2000);
                Main.LogDebugInfo($"HCDB Decryption: Result - {message}");
            }
            else
            {
                Main.LogDebugInfo("HCDB Decryption: Aborted - No HCDB files listed for decryption.");
                TemporaryMessageHelper.ShowTemporaryMessage(HCDBDecrypterDragAreaText, "No HCDB files listed for decryption.", 2000);
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

                byte[]? processedData = MainWindow.HCDBUnpack(decryptedData, Main.LogDebugInfo);
                if (processedData != null)
                {
                    string outputPath = Path.Combine(outputDirectory, cleanFilename);
                    await File.WriteAllBytesAsync(outputPath, processedData);
                    Main.LogDebugInfo($"Processed HCDB file successfully written to {outputPath}.");

                    // Validate the processed SQL file
                    if (Main.IsValidSqlFile(outputPath))
                    {
                        Main.LogDebugInfo($"Validation passed for SQL file at {outputPath}.");
                        return true;
                    }
                    else
                    {
                        File.Delete(outputPath);
                        Main.LogDebugInfo($"Invalid SQL file deleted at {outputPath}.");
                        return false;
                    }
                }
                else
                {
                    Main.LogDebugInfo($"Failed to process decrypted data for {filename}.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error processing decrypted data for {filename}: {ex.Message}");
                return false;
            }
        }



        // HCDB Decrypter drag and drop handler
        private void HCDBDecrypterDragDropHandler(object sender, DragEventArgs e)
        {
            Main.LogDebugInfo("HCDB to SQL Conversion: Drag and Drop Initiated");
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

                        Main.LogDebugInfo($"HCDB to SQL Conversion: {newFilesCount} HCDB files added, {duplicatesCount} duplicates filtered");
                    });
                    displayTime = 1000; // Change display time since files were added
                }
                else
                {
                    message = "No HCDB files found.";
                    Main.LogDebugInfo("HCDB to SQL Conversion: No HCDB files found in Drag and Drop.");
                }

                TemporaryMessageHelper.ShowTemporaryMessage(HCDBDecrypterDragAreaText, message, 2000);
            }
            else
            {
                Main.LogDebugInfo("HCDB to SQL Conversion: Drag and Drop - No Data Present.");
                TemporaryMessageHelper.ShowTemporaryMessage(HCDBDecrypterDragAreaText, "Drag and Drop operation failed - No Data Present.", 2000);
            }
        }


        private async void ClickToBrowseHCDBDecryptHandler(object sender, RoutedEventArgs e)
        {
            Main.LogDebugInfo("HCDB to SQL Conversion: Browsing for HCDB files Initiated");

            // Disable the main window to prevent interactions while the dialog is open
            Main.IsEnabled = false;

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
            Main.IsEnabled = true;

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

                        Main.LogDebugInfo($"HCDB to SQL Conversion: {newFilesCount} HCDB files added{duplicatesMessage} via File Browser");
                    });
                    displayTime = 1000; // Change display time since files were added
                }
                else
                {
                    message = "No HCDB files selected.";
                    Main.LogDebugInfo("HCDB to SQL Conversion: No HCDB files selected in File Browser.");
                }
            }
            else
            {
                message = "Dialog cancelled.";
                Main.LogDebugInfo("HCDB to SQL Conversion: File Browser - Dialog Cancelled.");
            }

            TemporaryMessageHelper.ShowTemporaryMessage(HCDBDecrypterDragAreaText, message, 2000);
        }

        private void HCDBEncrypterTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Get the text from the HcdbOutputDirectoryTextBox
            string path = Main._settings.HcdbOutputDirectory;

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

        private void HCDBDecrypterTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Get the text from the sqlOutputDirectoryTextBox
            string path = Main._settings.SqlOutputDirectory;

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

        // Handlers used in this tab's XAML that live elsewhere.
        private void ClearListHandler(object sender, RoutedEventArgs e) => Main.ClearListHandler(sender, e);
    }
}
