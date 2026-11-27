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
    // "SceneList" tab.
    public partial class SceneListTab : UserControl
    {
        private static MainWindow Main => MainWindow.Instance;

        public SceneListTab()
        {
            InitializeComponent();
        }

        // Fill this tab's controls from the saved settings (called once at startup).
        internal void ApplySettings(AppSettings settings)
        {
            SceneListSavePathtextbox.Text = settings.SceneListSavePath;
            SceneListPathURLtextbox2.Text = settings.SceneListPathURL;
        }

        public ObservableCollection<SceneListEditor> Scenes { get; set; } = new ObservableCollection<SceneListEditor>();

        private void SceneListPathURLtextbox2_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Main._settings != null)
            {
                Main._settings.SceneListPathURL = SceneListPathURLtextbox2.Text;
                SettingsManager.SaveSettings(Main._settings);
            }
        }

        private void SceneListSavePathtextbox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Main._settings != null)
            {
                Main._settings.SceneListSavePath = SceneListSavePathtextbox.Text;
                SettingsManager.SaveSettings(Main._settings);
            }
        }

        private void SceneListSavePathtextbox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Create a FolderBrowserDialog to allow the user to select a folder
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog();

            // Show the dialog and check if the user selected a folder
            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // Set the selected folder path in the TextBox
                SceneListSavePathtextbox.Text = folderDialog.SelectedPath;
            }
        }



        private void SceneListPathURLtextbox2_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Create an OpenFileDialog to allow the user to select an XML file
            var openFileDialog = new OpenFileDialog
            {
                Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
                Title = "Select an XML file"
            };

            // Show the dialog and check if the user selected a file
            if (openFileDialog.ShowDialog() == true)
            {
                // Set the selected file path in the TextBox
                SceneListPathURLtextbox2.Text = openFileDialog.FileName;
            }
        }


        internal async void LoadSceneListXMLv2intoEditorButton_Click(object sender, RoutedEventArgs e)
        {
            Scenes.Clear();
            string xmlPath = SceneListPathURLtextbox2.Text;
            scenesList.ItemsSource = Scenes; // Bind the observable collection to the ItemsControl

            try
            {
                int count = 0;
                foreach (var scene in ParseXmlToSceneList(xmlPath))
                {
                    Scenes.Add(scene);
                    count++;
                    if (count % 2 == 0)
                    {
                        await Task.Delay(1); // Give UI a chance to update and stay responsive
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load XML: " + ex.Message);
            }
        }

        public static IEnumerable<SceneListEditor> ParseXmlToSceneList(string xmlPath)
        {
            using (XmlReader reader = XmlReader.Create(xmlPath))
            {
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.Name == "SCENE")
                    {
                        yield return new SceneListEditor
                        {
                            ID = reader.GetAttribute("ID") ?? reader.GetAttribute("Name"),
                            Type = reader.GetAttribute("Type"),
                            Description = reader.GetAttribute("desc"),
                            Config = reader.GetAttribute("config"),
                            SceneID = reader.GetAttribute("SceneID") ?? reader.GetAttribute("ChannelID"),
                            Version = reader.GetAttribute("version"),
                            SHA1 = reader.GetAttribute("sha1"),
                            Flags = reader.GetAttribute("flags"),
                            Softcap = reader.GetAttribute("softcap"),
                            Capacity = reader.GetAttribute("capacity"),
                            Host = reader.GetAttribute("host"),
                            HomeUID = reader.GetAttribute("HOMEUID")
                        };
                    }
                }
            }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox != null)
            {
                textBox.Foreground = Brushes.Yellow;

                var unsavedChangesTextBlock = this.FindName("UnsavedSceneListChanges") as TextBlock;
                if (unsavedChangesTextBlock != null)
                {
                    unsavedChangesTextBlock.Visibility = Visibility.Visible;
                }
            }
        }

        private void AddNewScenelistItem_Button_Click(object sender, RoutedEventArgs e)
        {
            var newScene = new SceneListEditor
            {
                ID = txtSdcName.Text,
                SceneID = txtChannelID.Text,
                Type = (txtSceneType.SelectedItem as ComboBoxItem)?.Content.ToString(),
                Host = (txtdHost.SelectedItem as ComboBoxItem)?.Content.ToString(),
                Config = txtsceneconfig.Text,
                Description = txtSdcPath.Text,
                SHA1 = txtSdcsha1Digest.Text,
                Version = txtSDCversion.Text,
                Flags = txtflags.Text,
                HomeUID = txthomeuuid.Text
            };

            Scenes.Insert(0, newScene); // Add the new scene at the beginning of the ObservableCollection

            var unsavedChangesTextBlock = this.FindName("UnsavedSceneListChanges") as TextBlock;
            if (unsavedChangesTextBlock != null)
            {
                unsavedChangesTextBlock.Visibility = Visibility.Visible;
            }
        }

        private async void SaveSceneListXmlButton_Click(object sender, RoutedEventArgs e)
        {
            // Reset the text color of all TextBox elements to white
            ResetTextBoxColors();

            MainWindow.DeploySceneListFlag = false;

            // Define the order for sorting and group names for comments
            var typeOrder = new List<string>
    {
        "PublicLobby", "GlobalSpace", "ArcadeSpace", "Game16Space", "Game32Space", "Game60Space",
        "ClubHouse", "PublicNoVideo", "Home", "PrivateNoVideo"
    };

            // Sort the Scenes collection according to the specified order
            var sortedScenes = Scenes
                .OrderBy(scene =>
                {
                    // Find the index of the scene type in the typeOrder list
                    // If not found, give it a high index to sort it later
                    int index = typeOrder.FindIndex(t => string.Equals(t, scene.Type, StringComparison.OrdinalIgnoreCase));
                    if (index == -1)
                    {
                        // Special handling for types containing "Home" but not exactly "Home"
                        if (scene.Type.Contains("Home", StringComparison.OrdinalIgnoreCase) && !string.Equals(scene.Type, "Home", StringComparison.OrdinalIgnoreCase))
                            return typeOrder.IndexOf("Home");
                        return int.MaxValue; // Put unknown types at the end
                    }
                    return index;
                })
                .ThenBy(scene => scene.ID) // Additional sorting by ID if needed
                .ToList();

            // Create a new XElement for the SCENELIST
            var sceneListElement = new XElement("SCENELIST");

            // Track the current group for inserting comments
            string currentGroup = null;

            foreach (var scene in sortedScenes)
            {
                // Determine the group name (type) in a case-insensitive manner
                string sceneType = typeOrder.FirstOrDefault(t => string.Equals(t, scene.Type, StringComparison.OrdinalIgnoreCase)) ?? "Other";

                // If the group changes, add a comment
                if (sceneType != currentGroup)
                {
                    currentGroup = sceneType;
                    sceneListElement.Add(new XComment($" {currentGroup} "));
                }

                // Add the scene element
                sceneListElement.Add(new XElement("SCENE",
                    string.IsNullOrEmpty(scene.ID) ? null : new XAttribute("ID", scene.ID),
                    string.IsNullOrEmpty(scene.Type) ? null : new XAttribute("Type", scene.Type),
                    string.IsNullOrEmpty(scene.Description) ? null : new XAttribute("desc", scene.Description),
                    string.IsNullOrEmpty(scene.Config) ? null : new XAttribute("config", scene.Config),
                    string.IsNullOrEmpty(scene.SceneID) ? null : new XAttribute("SceneID", scene.SceneID),
                    string.IsNullOrEmpty(scene.Version) ? null : new XAttribute("version", scene.Version),
                    string.IsNullOrEmpty(scene.SHA1) ? null : new XAttribute("sha1", scene.SHA1),
                    string.IsNullOrEmpty(scene.Flags) ? null : new XAttribute("flags", scene.Flags),
                    string.IsNullOrEmpty(scene.Softcap) ? null : new XAttribute("softcap", scene.Softcap),
                    string.IsNullOrEmpty(scene.Capacity) ? null : new XAttribute("capacity", scene.Capacity),
                    string.IsNullOrEmpty(scene.Host) ? null : new XAttribute("host", scene.Host),
                    string.IsNullOrEmpty(scene.HomeUID) ? null : new XAttribute("HOMEUID", scene.HomeUID)
                ));
            }

            // Create the new XML document with the SCENELIST element
            var doc = new XDocument(sceneListElement);

            try
            {
                // Get the full path from SceneListPathURLtextbox2
                string fullPath = SceneListPathURLtextbox2.Text;

                // Extract the filename from the full path
                string filename = Path.GetFileName(fullPath);
                if (string.IsNullOrEmpty(filename))
                {
                    MessageBox.Show("Failed to extract the filename from the path specified in SceneListPathURLtextbox2.");
                    return;
                }

                // Get the save directory path from SceneListSavePathtextbox
                string saveDirectory = SceneListSavePathtextbox.Text.Trim();

                // Ensure the save directory is valid
                if (string.IsNullOrWhiteSpace(saveDirectory))
                {
                    MessageBox.Show("Please specify a valid directory path to save the SceneList XML.");
                    return;
                }

                // Combine the directory path and the filename correctly
                string savePath = Path.Combine(saveDirectory.TrimEnd('\\', '/'), filename);

                // Save the plain XML document to the constructed path
                doc.Save(savePath);

                // Compute the SHA1 hash of the XML content
                string sha1Hash = ComputeSHA1(savePath);

                // Display the SHA1 hash in the textbox
                Main.TssTabView.LatestSceneListSHA1textbox.Text = sha1Hash;

                // Change the visibility of the panel to visible
                Main.TssTabView.LatestSceneListSHA1panel.Visibility = Visibility.Visible;

                // Define the output file path for encryption
                string exeLocation = System.AppDomain.CurrentDomain.BaseDirectory;
                Directory.CreateDirectory(Main._settings.SceneListDeployDirectory);
                string outputFilePath = System.IO.Path.Combine(Main._settings.SceneListDeployDirectory, filename);

                // Check if encryption is enabled
                if (CheckBoxEncryptSceneList.IsChecked == true)
                {
                    // Encrypt the saved XML file
                    bool encryptionSuccess = await EncryptAndSaveFileAsync(savePath, outputFilePath);

                    if (encryptionSuccess)
                    {
                        var unsavedChangesTextBlock = this.FindName("UnsavedSceneListChanges") as TextBlock;
                        if (unsavedChangesTextBlock != null)
                        {
                            unsavedChangesTextBlock.Visibility = Visibility.Collapsed;
                        }
                        MessageBox.Show("XML saved and encrypted successfully.");
                        // Call the method to save TSS XML
                        Main.TssTabView.LoadTSSXMLButton_Click(sender, e);
                    }
                    else
                    {
                        MessageBox.Show("Failed to encrypt XML");
                    }
                }
                else
                {
                    // Copy the file without encryption
                    var unsavedChangesTextBlock = this.FindName("UnsavedSceneListChanges") as TextBlock;
                    if (unsavedChangesTextBlock != null)
                    {
                        unsavedChangesTextBlock.Visibility = Visibility.Collapsed;
                    }
                    File.Copy(savePath, outputFilePath, overwrite: true);
                    MessageBox.Show("XML saved successfully without encryption.");
                    // Call the method to save TSS XML
                    Main.TssTabView.LoadTSSXMLButton_Click(sender, e);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save XML: " + ex.Message);
            }
        }


        private void ResetTextBoxColors()
        {
            foreach (var item in scenesList.Items)
            {
                var container = scenesList.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
                if (container != null)
                {
                    var textBoxes = FindVisualChildren<TextBox>(container);
                    foreach (var textBox in textBoxes)
                    {
                        textBox.Foreground = Brushes.White;
                    }
                }
            }
        }

        private IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T)
                    {
                        yield return (T)child;
                    }

                    foreach (T childOfChild in FindVisualChildren<T>(child))
                    {
                        yield return childOfChild;
                    }
                }
            }
        }


        public async Task<bool> EncryptAndSaveFileAsync(string filePath, string outputFilePath)
        {
            bool isProcessed = true;
            string filename = Path.GetFileName(filePath);
            try
            {
                byte[] fileContent = await File.ReadAllBytesAsync(filePath);
                byte[] encryptedContent = null;
                string inputSHA1 = "";

                // Generate SHA1 hash from file content before encryption
                using (SHA1 sha1 = SHA1.Create())
                {
                    byte[] SHA1Data = sha1.ComputeHash(fileContent);
                    inputSHA1 = BitConverter.ToString(SHA1Data).Replace("-", "").ToLowerInvariant();
                    Main.LogDebugInfo($"Input SHA1 for {filename}: {inputSHA1}");

                    // Encrypt the file content
                    string computedSha1 = inputSHA1.Substring(0, 16);
                    encryptedContent = CDSProcess.CDSEncrypt_Decrypt(fileContent, computedSha1, (int)CdnMode.RETAIL);
                }

             
                if (encryptedContent != null)
                {
                    // Ensure the output directory exists
                    string outputDirectory = Path.GetDirectoryName(outputFilePath);
                    if (!Directory.Exists(outputDirectory))
                    {
                        Directory.CreateDirectory(outputDirectory);
                        Main.LogDebugInfo($"Output directory {outputDirectory} created.");
                    }
                    await File.WriteAllBytesAsync(outputFilePath, encryptedContent);
                    Main.LogDebugInfo($"File {filename} encrypted and written to {outputFilePath}.");
                }
                else
                {
                    isProcessed = false;
                    Main.LogDebugInfo($"Encryption failed for {filename}, no data written.");
                }
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error encrypting {filename}: {ex.Message}");
                isProcessed = false;
            }
            return isProcessed;
        }


        private string ComputeSHA1(string filePath)
        {
            using (var sha1 = System.Security.Cryptography.SHA1.Create())
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] hash = sha1.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
                }
            }
        }

        private void DeleteSceneListXMLLineButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            SceneListEditor itemToRemove = button.DataContext as SceneListEditor;
            if (itemToRemove != null)
            {
                Scenes.Remove(itemToRemove); // Removes the item from the ObservableCollection
                var unsavedChangesTextBlock = this.FindName("UnsavedSceneListChanges") as TextBlock;
                if (unsavedChangesTextBlock != null)
                {
                    unsavedChangesTextBlock.Visibility = Visibility.Visible;
                }
            }

        }

        private void Border_DragSDCEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    string fileExtension = Path.GetExtension(files[0]).ToLower();
                    if (fileExtension == ".xml" || fileExtension == ".sdc")
                    {
                        e.Effects = DragDropEffects.Copy;
                        e.Handled = true;
                    }
                }
            }
        }

        private void Border_SDCDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    string filePath = files[0];
                    string fileExtension = Path.GetExtension(filePath).ToLower();
                    if (fileExtension == ".xml" || fileExtension == ".sdc")
                    {
                        ProcessDroppedSDCFile(filePath);
                    }
                }
            }
        }

        private void Border_SDCMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Create an OpenFileDialog to allow the user to select an .sdc or .xml file
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "SDC files (*.sdc)|*.sdc|XML files (*.xml)|*.xml|All files (*.*)|*.*",
                Title = "Select an SDC or XML file"
            };

            // Show the dialog and check if the user selected a file
            if (openFileDialog.ShowDialog() == true)
            {
                string selectedFilePath = openFileDialog.FileName;
                string fileExtension = Path.GetExtension(selectedFilePath).ToLower();

                // Ensure the selected file is either .sdc or .xml
                if (fileExtension == ".sdc" || fileExtension == ".xml")
                {
                    ProcessDroppedSDCFile(selectedFilePath);
                }
                else
                {
                    MessageBox.Show("Only SDC or XML files are supported.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }


        private async void ProcessDroppedSDCFile(string filePath)
        {
            Main.LogDebugInfo($"Processing dropped SDC file: {filePath}");

            try
            {
                if (!TryProcessSDCFile(filePath))
                {
                    Main.LogDebugInfo($"Failed to process file normally, attempting decryption: {filePath}");
                    string baseOutputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "work");
                    bool decrypted = await DecryptSDCFileAsync(filePath, baseOutputDirectory);
                    if (decrypted)
                    {
                        string decryptedFilePath = Path.Combine(baseOutputDirectory, Path.GetFileName(filePath));
                        if (!TryProcessSDCFile(decryptedFilePath))
                        {
                            Main.LogDebugInfo("Failed to process the decrypted file.");
                        }
                        else
                        {
                            SetSceneType(decryptedFilePath);
                        }
                    }
                    else
                    {
                        Main.LogDebugInfo("Failed to decrypt the file.");
                    }
                }
                else
                {
                    SetSceneType(filePath);
                }
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"An error occurred while processing the file: {ex.Message}");
            }
        }

        private bool TryProcessSDCFile(string filePath)
        {
            try
            {
                Main.LogDebugInfo($"Trying to process SDC file: {filePath}");
                string fileContent = File.ReadAllText(filePath);

                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(fileContent);

                var archiveNode = xmlDoc.SelectSingleNode("//ARCHIVES/ARCHIVE");
                if (archiveNode != null)
                {
                    string archivePath = archiveNode.InnerText.Replace("[CONTENT_SERVER_ROOT]", "").TrimStart('/');
                    string folderName = Path.GetFileName(Path.GetDirectoryName(archivePath));
                    string fileName = Path.GetFileName(archivePath);

                    string sdcPath = $"{folderName}/{fileName}".Replace(".sdat", ".sdc", StringComparison.OrdinalIgnoreCase)
                                                             .Replace(".BAR", ".sdc", StringComparison.OrdinalIgnoreCase);

                    txtSdcPath.Text = sdcPath;
                    txtSdcName.Text = folderName;
                    txtsceneconfig.Text = "ADD SCENE FILE PATH HERE";

                    var match = Regex.Match(fileName, @"_T(\d{3})", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string version = match.Groups[1].Value.TrimStart('0'); // Trim leading zeros
                        txtSDCversion.Text = version;
                    }

                    Main.EncryptAndSetChannelID();

                    // Calculate SHA1 checksum after successfully processing the file
                    string sha1Digest = CalculateSHA1Checksum(filePath);
                    txtSdcsha1Digest.Text = sha1Digest;
                    Main.LogDebugInfo($"SHA1 checksum: {sha1Digest}");

                    Main.LogDebugInfo("Successfully processed the SDC file.");
                    return true;
                }
                else
                {
                    Main.LogDebugInfo("ARCHIVE node not found.");
                }
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error processing file: {ex.Message}");
            }

            return false;
        }


        private string CalculateSHA1Checksum(string filePath)
        {
            using (var sha1 = System.Security.Cryptography.SHA1.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    var hash = sha1.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
                }
            }
        }


        private async Task<bool> DecryptSDCFileAsync(string filePath, string baseOutputDirectory)
        {
            bool allFilesProcessed = true;
            string filename = Path.GetFileName(filePath);

            try
            {
                Main.LogDebugInfo($"Decrypting file: {filePath}");
                byte[] fileContent = await File.ReadAllBytesAsync(filePath);

                BruteforceProcess proc = new BruteforceProcess(fileContent);
                byte[] decryptedContent = proc.StartBruteForce((int)CdnMode.RETAIL);

                if (decryptedContent != null)
                {
                    string outputDirectory = baseOutputDirectory;
                    if (!Directory.Exists(outputDirectory))
                    {
                        Directory.CreateDirectory(outputDirectory);
                        Main.LogDebugInfo($"Created output directory: {outputDirectory}");
                    }
                    string outputPath = Path.Combine(outputDirectory, filename);
                    await File.WriteAllBytesAsync(outputPath, decryptedContent);
                    Main.LogDebugInfo($"Decrypted content written to: {outputPath}");

                    if (!IsValidDecryptedSDCFile(outputPath))
                    {
                        File.Delete(outputPath);
                        Main.LogDebugInfo($"Invalid decrypted SDC file, deleted: {outputPath}");
                        allFilesProcessed = false;
                    }
                }
                else
                {
                    allFilesProcessed = false;
                    Main.LogDebugInfo("Decryption failed.");
                }
            }
            catch (Exception ex)
            {
                allFilesProcessed = false;
                Main.LogDebugInfo($"Error decrypting file: {ex.Message}");
            }

            return allFilesProcessed;
        }

        private bool IsValidDecryptedSDCFile(string filePath)
        {
            try
            {
                Main.LogDebugInfo($"Validating decrypted SDC file: {filePath}");
                XDocument doc = XDocument.Load(filePath);
                bool isValid = doc.Root.Element("SDC_VERSION") != null;
                Main.LogDebugInfo($"Validation result: {isValid}");
                return isValid;
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error in IsValidDecryptedSDCFile: {ex.Message}");
                return false;
            }
        }

        private void txtChannelID_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Main.EncryptAndSetChannelID();
        }

        private void ResetSDCInfo_Click(object sender, RoutedEventArgs e)
        {
            txtSdcsha1Digest.Text = string.Empty;
            txtSdcName.Text = string.Empty;
            txtSdcPath.Text = string.Empty;
            txtSDCversion.Text = string.Empty;
            txtChannelID.Text = string.Empty;
            txtsceneconfig.Text = string.Empty;
            txthomeuuid.Text = string.Empty;
            txtflags.Text = string.Empty;

            // Reset the ComboBoxes
            txtSceneType.SelectedItem = null;
            txtdHost.SelectedItem = null;

            Main.LogDebugInfo("SDC information reset.");
        }


        private void SetSceneType(string filePath)
        {
            try
            {
                Main.LogDebugInfo($"Setting scene type and host based on file content: {filePath}");
                string fileContent = File.ReadAllText(filePath).ToLower();

                if (fileContent.Contains("apartment") || fileContent.Contains("private"))
                {
                    txtSceneType.SelectedItem = txtSceneType.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Content.ToString() == "Home");
                    txtdHost.SelectedItem = txtdHost.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Content.ToString() == "");
                    Main.LogDebugInfo("Scene type set to Home and host set to nothing.");
                }
                else
                {
                    txtSceneType.SelectedItem = txtSceneType.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Content.ToString() == "GlobalSpace");
                    txtdHost.SelectedItem = txtdHost.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Content.ToString() == "en-US");
                    Main.LogDebugInfo("Scene type set to GlobalSpace and host set to en-US.");
                }
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error setting scene type and host: {ex.Message}");
            }
        }

        private void txtSceneType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (txtSceneType.SelectedItem is ComboBoxItem selectedItem)
            {
                switch (selectedItem.Content.ToString())
                {
                    case "Home":
                        txtdHost.SelectedItem = null;
                        txtdHost.IsEnabled = false;
                        txthomeuuid.Text = string.Empty;
                        txthomeuuid.IsEnabled = true;
                        break;
                    case "PrivateNoVideo":
                        txtdHost.SelectedItem = null;
                        txtdHost.IsEnabled = false;
                        txthomeuuid.Text = string.Empty;
                        txthomeuuid.IsEnabled = true;
                        break;
                    case "GlobalSpace":
                        txtdHost.SelectedItem = txtdHost.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Content.ToString() == "en-US");
                        txtdHost.IsEnabled = true;
                        txthomeuuid.Text = string.Empty;
                        txthomeuuid.IsEnabled = false;
                        break;
                    default:
                        txtdHost.SelectedItem = null;
                        txtdHost.IsEnabled = true;
                        txthomeuuid.Text = string.Empty;
                        txthomeuuid.IsEnabled = false;
                        break;
                }
            }
        }

        // Handlers used in this tab's XAML that live elsewhere.
        private void txtObjectId_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Main.txtObjectId_MouseDoubleClick(sender, e);
    }
}
