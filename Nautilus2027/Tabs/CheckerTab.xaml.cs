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
    // "Checker" tab.
    public partial class CheckerTab : UserControl
    {
        private static MainWindow Main => MainWindow.Instance;

        public CheckerTab()
        {
            InitializeComponent();
        }

        private int previousFileCount = 0;

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
                        Main.cancellationTokenSource?.Cancel();
                        Main.cancellationTokenSource = new CancellationTokenSource();
                        ProcessFiles(droppedItems, Main.cancellationTokenSource.Token);
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
                    Main.cancellationTokenSource?.Cancel();
                    Main.cancellationTokenSource = new CancellationTokenSource();
                    ProcessFiles(droppedItems, Main.cancellationTokenSource.Token);
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
            Main.LogDebugInfo($"SHA1 Generator: Processing file: {file}");
            try
            {
                string sha1Hash = ComputeSha1(file);
                string fileName = Path.GetFileName(file);
                Dispatcher.Invoke(() => LogBox.Append(Sha1TextBox, $"SHA1: {sha1Hash} - File: {file}{Environment.NewLine}"));
                Main.LogDebugInfo($"SHA1 Generator: {fileName} = {sha1Hash}");
            }
            catch (Exception ex)
            {
                string fileName = Path.GetFileName(file);
                Dispatcher.Invoke(() => LogBox.Append(Sha1TextBox, $"Error processing {file}: {ex.Message}{Environment.NewLine}"));
                Main.LogDebugInfo($"SHA1 Generator: Error processing {fileName}: {ex.Message}");
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
            Main.cancellationTokenSource?.Cancel();


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
                        Main.cancellationTokenSource?.Cancel();
                        Main.cancellationTokenSource = new CancellationTokenSource();
                        ProcessFiles(selectedFiles, Main.cancellationTokenSource.Token);
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
                    Main.cancellationTokenSource?.Cancel();
                    Main.cancellationTokenSource = new CancellationTokenSource();
                    ProcessFiles(selectedFiles, Main.cancellationTokenSource.Token);
                }
            }
        }

        private async void ArchiveUnpackerVerifyTestButtonClick(object sender, RoutedEventArgs e)
        {
            Main.LogDebugInfo("Archive Unpacker: Browsing for directory initiated");

            // Disable the main window to prevent interactions while the dialog is open
            Main.IsEnabled = false;

            var folderDialog = new VistaFolderBrowserDialog();

            bool? result = folderDialog.ShowDialog();

            // Await a delay to absorb any unintentional clicks that happen after the dialog closes
            await Task.Delay(10); // 10 ms delay

            // Re-enable the main window after the delay
            Main.IsEnabled = true;

            if (result == true && !string.IsNullOrEmpty(folderDialog.SelectedPath))
            {
                string selectedDirectory = folderDialog.SelectedPath;
                await ScanDirectoryAsync(selectedDirectory);
            }
            else
            {
                Main.LogDebugInfo("Archive Unpacker: No directory selected in folder browser.");
                MessageBox.Show("No directory selected.");
            }
        }



        private async void VerifyDragArea_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedItems = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (droppedItems.Length > 0)
                {
                    string firstItem = droppedItems[0];
                    FileScannerStandalone scanner = new FileScannerStandalone(Sha1TextBox);

                    if (System.IO.Directory.Exists(firstItem))
                    {
                        await scanner.ScanDirectoryAsync(firstItem);
                    }
                    else if (System.IO.File.Exists(firstItem))
                    {
                        await scanner.ScanFileAsync(firstItem);
                    }
                    else
                    {
                        MessageBox.Show("Please drop a valid folder or file.");
                    }
                }
            }
        }

        private async Task ScanDirectoryAsync(string directoryPath)
        {
            try
            {
                FileScannerStandalone scanner = new FileScannerStandalone(Sha1TextBox);
                await scanner.ScanDirectoryAsync(directoryPath);
                MessageBox.Show("Scan completed!");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}");
            }
        }
    }
}
