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
    // "Path2Hash" tab.
    public partial class Path2HashTab : UserControl
    {
        private static MainWindow Main => MainWindow.Instance;

        public Path2HashTab()
        {
            InitializeComponent();
        }

        private void Path2HashButton_Click(object sender, RoutedEventArgs e)
        {
            string inputPath = Path2HashInputTextBox.Text;
            int hash = BarHash(inputPath);
            Path2HashOutputTextBox.Text = hash.ToString("X8").ToUpper();
            Main.LogDebugInfo($"Path hashed: {inputPath}, Hash (Hex): {hash.ToString("X8").ToUpper()}, Hash (Int): {hash}");

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
                        Main.LogDebugInfo($"Path hashed: {modifiedPath}, Hash (Hex): {pathHash.ToString("X8").ToUpper()}, Hash (Int): {pathHash}");
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
            Main.LogDebugInfo("Path to Hash: List Cleared");
        }

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
                // Paths to mapper helper files
                string sceneHelperFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies\\scene_file_mapper_helper.txt");
                string coreDataHelperFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies\\core_data_mapper_helper.txt");

                // Method to process helper files
                void ProcessHelperFile(string helperFilePath)
                {
                    using (var writer = new StreamWriter(helperFilePath, true)) // Append mode
                    {
                        // Process paths from text boxes
                        var uniquePaths = new HashSet<string>();
                        ProcessTextBoxForUniquePaths(Path2HashOutputExtraTextBox.Text, uniquePaths);
                        ProcessTextBoxForUniquePaths(Path2HashInputTextBox.Text, uniquePaths, true);

                        foreach (var path in uniquePaths)
                        {
                            if (!string.IsNullOrWhiteSpace(path))
                            {
                                int hash = BarHash(path);
                                string hashString = hash.ToString("X8").ToUpper();
                                string newEntry = $"{hashString}:{path.ToUpper()}";
                                writer.WriteLine(newEntry);
                            }
                        }
                    }
                }

                // Process both helper files
                ProcessHelperFile(sceneHelperFilePath);
                ProcessHelperFile(coreDataHelperFilePath);

                // After adding paths, update the text and use a delay to revert the message.
                Path2HashDragAreaText.Text = "Paths added to mapper helper files";
                await Task.Delay(1000); // Wait for one second
                Path2HashDragAreaText.Text = "Drag a folder or TXT/XML here to hash all paths";
            }
        }

        private void ProcessTextBoxForUniquePaths(string textBoxContent, HashSet<string> uniquePaths, bool isInput = false)
        {
            var lines = textBoxContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                string path = isInput ? line : line.Contains(":") ? line.Split(':')[1] : line;
                path = path.Trim();
                if (!uniquePaths.Contains(path.ToUpper()) && !string.IsNullOrWhiteSpace(path))
                {
                    uniquePaths.Add(path.ToUpper());
                }
            }
        }




        internal void Path2HashTabItem_Drop(object sender, DragEventArgs e)
        {
            // Attached to nested elements and to the tab header too; handle each drop once.
            e.Handled = true;

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
    }
}
