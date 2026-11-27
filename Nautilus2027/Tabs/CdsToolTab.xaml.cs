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
    // "CDS Tool" tab.
    public partial class CdsToolTab : UserControl
    {
        private static MainWindow Main => MainWindow.Instance;

        public CdsToolTab()
        {
            InitializeComponent();
        }

        // TAB 2: Logic for CDS Encryption 

        private async void CDSEncrypterExecuteButtonClick(object sender, RoutedEventArgs e)
        {
            Main.LogDebugInfo("CDS Encryption: Process Initiated");
            TemporaryMessageHelper.ShowTemporaryMessage(CDSEncrypterDragAreaText, "Processing...", 2000);

            string baseOutputDirectory = Main._settings.CdsEncryptOutputDirectory; // Assume _settings.CdsOutputDirectory is already set
            if (!Directory.Exists(baseOutputDirectory))
            {
                Directory.CreateDirectory(baseOutputDirectory);
                Main.LogDebugInfo($"CDS Encryption: Output directory created at {baseOutputDirectory}");
            }

            string filesToEncrypt = string.Join(Environment.NewLine, LogBox.TakeLines(CDSEncrypterTextBox));   // the list moves into memory; the box shows the log // Assuming CDSEncrypterTextBox contains file paths separated by new lines
            bool addSha1ToFilename = CDSAddSHA1CheckBox.IsChecked ?? false; // Check if the checkbox for adding SHA1 to filename is checked

            if (!string.IsNullOrWhiteSpace(filesToEncrypt))
            {
                string[] filePaths = filesToEncrypt.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                Main.LogDebugInfo($"CDS Encryption: Starting encryption for {filePaths.Length} files");
                bool encryptionSuccess = await EncryptFilesAsync(filePaths, baseOutputDirectory, addSha1ToFilename); // Pass the state of the checkbox to the encryption method

                string message = encryptionSuccess ? $"Success: {filePaths.Length} Files Encrypted" : "Encryption Failed";
                TemporaryMessageHelper.ShowTemporaryMessage(CDSEncrypterDragAreaText, message, 2000);
                Main.LogDebugInfo($"CDS Encryption: Result - {message}");
            }
            else
            {
                Main.LogDebugInfo("CDS Encryption: Aborted - No files listed for Encryption.");
                TemporaryMessageHelper.ShowTemporaryMessage(CDSEncrypterDragAreaText, "No files listed for Encryption.", 2000);
            }
        }


        private void CDSEncrypterDragDropHandler(object sender, DragEventArgs e)
        {
            Main.LogDebugInfo("CDS Encryption: Drag and Drop Initiated");
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

                        Main.LogDebugInfo($"CDS Encryption: {newFilesCount} files added, {duplicatesCount} duplicates filtered from Drag and Drop.");
                    });
                }
                else
                {
                    Main.LogDebugInfo("CDS Encryption: Drag and Drop - No valid files found.");
                    message = "No valid files found.";
                    TemporaryMessageHelper.ShowTemporaryMessage(CDSEncrypterDragAreaText, message, 2000);
                }
            }
            else
            {
                Main.LogDebugInfo("CDS Encryption: Drag and Drop - No Data Present.");
                TemporaryMessageHelper.ShowTemporaryMessage(CDSEncrypterDragAreaText, "Drag and Drop operation failed - No Data Present.", 2000);
            }
        }




        private async void ClickToBrowseCDSEncryptHandler(object sender, RoutedEventArgs e)
        {
            Main.LogDebugInfo("CDS Encryption: Browsing for files Initiated");

            // Disable the main window to prevent interactions while the dialog is open
            Main.IsEnabled = false;

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
            Main.IsEnabled = true;

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

                        Main.LogDebugInfo($"CDS Encryption: {newFilesCount} files added{duplicatesMessage} via File Browser.");
                    });
                }
                else
                {
                    message = "No compatible files selected.";
                    Main.LogDebugInfo("CDS Encryption: File Browser - No compatible files were selected.");
                }
            }
            else
            {
                message = "Dialog cancelled.";
                Main.LogDebugInfo("CDS Encryption: File Browser - Dialog Cancelled.");
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
                        inputSHA1 = BitConverter.ToString(SHA1Data).Replace("-", "").ToLowerInvariant(); // Full SHA1 of input content
                        Main.LogDebugInfo($"Input SHA1 for {filename}: {inputSHA1}");

                        // Encrypt the file content
                        string computedSha1 = inputSHA1.Substring(0, 16); // Use first 16 characters for some process, if needed
                        encryptedContent = CDSProcess.CDSEncrypt_Decrypt(fileContent, computedSha1, (int)CdnMode.RETAIL);
                    }

                    // Log the success message and the SHA1 of the input file
                    Dispatcher.Invoke(() =>
                    {
                        LogBox.Append(CDSEncrypterTextBox, $"{Environment.NewLine}CDS: Success file encrypted for {filename}", log: true);
                        LogBox.Append(CDSEncrypterTextBox, $"{Environment.NewLine}File: {filename} SHA1: {inputSHA1}", log: true);
                        CDSEncrypterTextBox.ScrollToEnd();
                    });

                    // Append SHA1 of the original content to filename if requested
                    if (addSha1ToFilename && encryptedContent != null)
                    {
                        string extension = Path.GetExtension(filename);
                        string filenameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
                        filename = $"{filenameWithoutExtension}_{inputSHA1}{extension}"; // Using inputSHA1 for naming
                    }

                    // Rename for CDN if the checkbox is checked and conditions are met
                    if (CDSRenameForCDNCheckBox.IsChecked == true && Path.GetExtension(filename).ToLower() == ".odc" &&
                        System.Text.RegularExpressions.Regex.IsMatch(filename, @"^[a-fA-F0-9]{8}-[a-fA-F0-9]{8}-[a-fA-F0-9]{8}-[a-fA-F0-9]{8}"))
                    {
                        string uuidPart = filename.Substring(0, 35); // Extract the 35-character UUID part
                        string extraPart = filename.Substring(35); // Extract the rest of the filename after the UUID
                        string newFolderName = uuidPart;
                        string newFileName = $"object{extraPart}";

                        string outputDirectory = Path.Combine(baseOutputDirectory, "Objects", newFolderName);
                        if (!Directory.Exists(outputDirectory))
                        {
                            Directory.CreateDirectory(outputDirectory);
                            Main.LogDebugInfo($"Output directory {outputDirectory} created.");
                        }
                        string outputPath = Path.Combine(outputDirectory, newFileName);
                        await File.WriteAllBytesAsync(outputPath, encryptedContent);
                        Main.LogDebugInfo($"File {newFileName} encrypted and written to {outputPath}.");
                    }
                    else if (encryptedContent != null)
                    {
                        string outputDirectory = Path.Combine(baseOutputDirectory, Path.GetFileName(Path.GetDirectoryName(filePath)));
                        if (!Directory.Exists(outputDirectory))
                        {
                            Directory.CreateDirectory(outputDirectory);
                            Main.LogDebugInfo($"Output directory {outputDirectory} created.");
                        }
                        string outputPath = Path.Combine(outputDirectory, filename);
                        await File.WriteAllBytesAsync(outputPath, encryptedContent);
                        Main.LogDebugInfo($"File {filename} encrypted and written to {outputPath}.");
                    }
                    else
                    {
                        allFilesProcessed = false;
                        Main.LogDebugInfo($"Encryption failed for {filename}, no data written.");
                    }
                }
                catch (Exception ex)
                {
                    Main.LogDebugInfo($"Error encrypting {filename}: {ex.Message}");
                    allFilesProcessed = false;
                }
            }
            return allFilesProcessed;
        }

        // TAB 2: Logic for CDS Decryption

        private async void CDSDecrypterExecuteButtonClick(object sender, RoutedEventArgs e)
        {
            TemporaryMessageHelper.ShowTemporaryMessage(CDSDecrypterDragAreaText, "Processing....", 2000);

            string baseOutputDirectory = Main._settings.CdsDecryptOutputDirectory;
            if (!Directory.Exists(baseOutputDirectory))
            {
                Directory.CreateDirectory(baseOutputDirectory);
            }

            string filesToDecrypt = string.Join(Environment.NewLine, LogBox.TakeLines(CDSDecrypterTextBox));   // the list moves into memory; the box shows the log
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
                        decryptionSuccess &= await DecryptFilesSHA1Async(new string[] { filePath }, baseOutputDirectory, manualSha1);
                    }
                    else if (!string.IsNullOrWhiteSpace(detectedSha1))
                    {
                        decryptionSuccess &= await DecryptFilesSHA1Async(new string[] { filePath }, baseOutputDirectory, detectedSha1);
                    }
                    else
                    {
                        decryptionSuccess &= await Main.DecryptFilesAsync(new string[] { filePath }, baseOutputDirectory);
                    }
                }

                string message = decryptionSuccess ? "Success: All files decrypted" : "Decryption Failed for one or more files";
                TemporaryMessageHelper.ShowTemporaryMessage(CDSDecrypterDragAreaText, message, 2000);
            }
            else
            {
                TemporaryMessageHelper.ShowTemporaryMessage(CDSDecrypterDragAreaText, "No files listed for Decryption.", 2000);
            }
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
                        Main.LogDebugInfo($"SHA1 {useSha1} detected in filename for {filename}.");
                    }
                    else if (!string.IsNullOrWhiteSpace(sha1Hash))
                    {
                        useSha1 = sha1Hash;
                        Main.LogDebugInfo($"SHA1 {useSha1} provided manually for {filename}.");
                    }
                    else
                    {
                        using (SHA1 sha1 = SHA1.Create())
                        {
                            byte[] SHA1Data = sha1.ComputeHash(fileContent);
                            useSha1 = BitConverter.ToString(SHA1Data).Replace("-", "").Substring(0, 16);
                            Main.LogDebugInfo($"No SHA1 found in filename or provided. Generated SHA1 for {filename}: {useSha1}");
                        }
                    }

                    processedContent = CDSProcess.CDSEncrypt_Decrypt(fileContent, useSha1.Substring(0, 16), (int)CdnMode.RETAIL);
                    if (processedContent != null)
                    {
                        string outputDirectory = Path.Combine(baseOutputDirectory, parentFolderName);
                        if (!Directory.Exists(outputDirectory))
                        {
                            Directory.CreateDirectory(outputDirectory);
                        }
                        string outputPath = Path.Combine(outputDirectory, filename);
                        await File.WriteAllBytesAsync(outputPath, processedContent);
                        Main.LogDebugInfo($"Decryption successful for {filename}. Output written to {outputPath}.");

                        // Validate the decrypted file content
                        if (!Main.IsValidDecryptedFile(outputPath, extension))
                        {
                            File.Delete(outputPath);
                            Main.LogDebugInfo($"Validation failed for {filename}. File deleted.");
                            allFilesProcessed = false;
                        }
                        else
                        {
                            Main.LogDebugInfo($"Validation passed for {filename}. Decryption successful and file content is valid.");
                        }
                    }
                    else
                    {
                        allFilesProcessed = false;
                        Main.LogDebugInfo($"Decryption failed for {filename}. No output written.");
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


        private void CDSDecrypterDragDropHandler(object sender, DragEventArgs e)
        {
            Main.LogDebugInfo("CDS Decryption: Drag and Drop Initiated");
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

                        string message = $"{newFilesCount} file(s) added";
                        if (duplicatesCount > 0)
                        {
                            message += $", {duplicatesCount} duplicate(s) filtered";
                        }

                        TemporaryMessageHelper.ShowTemporaryMessage(CDSDecrypterDragAreaText, message, 2000);

                        Main.LogDebugInfo($"CDS Decryption: {newFilesCount} files added, {duplicatesCount} duplicates filtered from Drag and Drop.");
                    });
                }
                else
                {
                    Main.LogDebugInfo("CDS Decryption: Drag and Drop - No valid files found.");
                    string message = "No valid files found.";
                    TemporaryMessageHelper.ShowTemporaryMessage(CDSDecrypterDragAreaText, message, 2000);
                }
            }
            else
            {
                Main.LogDebugInfo("CDS Decryption: Drag and Drop - No Data Present.");
                TemporaryMessageHelper.ShowTemporaryMessage(CDSDecrypterDragAreaText, "Drag and Drop operation failed - No Data Present.", 2000);
            }
        }



        private async void ClickToBrowseCDSDecryptHandler(object sender, RoutedEventArgs e)
        {
            Main.LogDebugInfo("CDS Decryption: Browsing for files Initiated");

            // Disable the main window to prevent interactions while the dialog is open
            Main.IsEnabled = false;

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
            Main.IsEnabled = true;

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

                        Main.LogDebugInfo($"CDS Decryption: {message} via File Browser");
                    });
                }
                else
                {
                    message = "No compatible files selected.";
                    Main.LogDebugInfo("CDS Decryption: File Browser - No compatible files were selected.");
                }
            }
            else
            {
                message = "Dialog cancelled.";
                Main.LogDebugInfo("CDS Decryption: File Browser - Dialog Cancelled.");
            }

            TemporaryMessageHelper.ShowTemporaryMessage(CDSDecrypterDragAreaText, message, 2000);
        }

        private void CDSEncrypterTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Get the text from the CdsOutputDirectoryTextBox
            string path = Main._settings.CdsEncryptOutputDirectory;

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

        private void CDSDecrypterTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Get the text from the CdsOutputDirectoryTextBox
            string path = Main._settings.CdsDecryptOutputDirectory;

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
