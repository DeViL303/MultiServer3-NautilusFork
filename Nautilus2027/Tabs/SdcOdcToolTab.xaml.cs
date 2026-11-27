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
    // "SDC / ODC" tab.
    public partial class SdcOdcToolTab : UserControl
    {
        private static MainWindow Main => MainWindow.Instance;

        public SdcOdcToolTab()
        {
            InitializeComponent();
        }

        // TAB 8: Logic for SDC Creation

        private async void CreateSdcButton_Click(object sender, RoutedEventArgs e)
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

            // Get the text from sdcServerArchivePathTextBox
            string sdcServerArchivePath = sdcServerArchivePathTextBox.Text;

            // Trim "Scenes/" off the start if it exists
            if (sdcServerArchivePath.StartsWith("Scenes/", StringComparison.OrdinalIgnoreCase))
            {
                sdcServerArchivePath = sdcServerArchivePath.Substring(7);
            }

            // Replace ".BAR" or ".sdat" with ".sdc" (case insensitive)
            sdcServerArchivePath = Regex.Replace(sdcServerArchivePath, @"(\.BAR|\.sdat)$", ".sdc", RegexOptions.IgnoreCase);

            // Write the result to txtSdcPath
            Main.SceneListTabView.txtSdcPath.Text = sdcServerArchivePath;

            // Extract the name for txtSdcName
            string sdcName = string.Empty;

            // Find the first occurrence of '/' or '\'
            int slashIndex = sdcServerArchivePath.IndexOfAny(new char[] { '/', '\\' });
            if (slashIndex != -1)
            {
                sdcName = sdcServerArchivePath.Substring(0, slashIndex);
            }
            else
            {
                // If no '/' or '\' is found, find the first occurrence of '.'
                int dotIndex = sdcServerArchivePath.IndexOf('.');
                if (dotIndex != -1)
                {
                    sdcName = sdcServerArchivePath.Substring(0, dotIndex);
                }
            }

            // Write the result to txtSdcName
            Main.SceneListTabView.txtSdcName.Text = sdcName;
            Main.EncryptAndSetChannelID();

            // Pad the thumbnail suffix to 3 digits if it is not empty
            if (!string.IsNullOrEmpty(thumbnailSuffix))
            {
                thumbnailSuffix = thumbnailSuffix.PadLeft(3, '0');
            }

            // Write the version to txtVersion with leading zeros trimmed off
            if (string.IsNullOrEmpty(thumbnailSuffix))
            {
                Main.SceneListTabView.txtSDCversion.Text = "0";
            }
            else
            {
                Main.SceneListTabView.txtSDCversion.Text = thumbnailSuffix.TrimStart('0');
            }
            // Construct the thumbnail image file names
            string makerImageSuffix = string.IsNullOrEmpty(thumbnailSuffix) ? "" : $"_T{thumbnailSuffix}";
            string smallImageSuffix = string.IsNullOrEmpty(thumbnailSuffix) ? "" : $"_T{thumbnailSuffix}";
            string largeImageSuffix = string.IsNullOrEmpty(thumbnailSuffix) ? "" : $"_T{thumbnailSuffix}";

            string makerImage = $"[THUMBNAIL_ROOT]maker{makerImageSuffix}.png";
            string smallImage = $"[THUMBNAIL_ROOT]small{smallImageSuffix}.png";
            string largeImage = $"[THUMBNAIL_ROOT]large{largeImageSuffix}.png";

            // Extract the SDC file name from the archive path and handle special suffix
            string archiveFileName = System.IO.Path.GetFileNameWithoutExtension(archivePath);
            string sdcFileName = ConvertToSdcFileName(archiveFileName, thumbnailSuffix);

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
                            new XElement("ARCHIVES", CreateArchiveElement(archivePath, archiveSize, timestamp)) : null
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

                    new XElement("age_rating", new XAttribute("minimum_age", "0"), new XAttribute("parental_control_level", "1"))
                )
            );

            // Save the XML to a temporary file
            string tempFilePath = Path.Combine(Path.GetTempPath(), sdcFileName);
            sdcXml.Save(tempFilePath);

            // Calculate SHA1 hash and update the text box
            string sha1Hash = await Task.Run(() => Main.CalculateSha1Hash(tempFilePath));
            Createdsdcsha1TextBox.Text = sha1Hash;
            Main.SceneListTabView.txtSdcsha1Digest.Text = sha1Hash;
            // Read the content of the temporary file and set it to the sdcCreatorTextbox
            string sdcContent = await File.ReadAllTextAsync(tempFilePath);
            SDCCreatorTextbox.Text = sdcContent;

            byte[] fileContent;

            // Check if encryption is needed
            if (EncryptSDCCheckBox.IsChecked == true)
            {
                fileContent = await EncryptSDCODCFileAsync(tempFilePath);
            }
            else
            {
                fileContent = await File.ReadAllBytesAsync(tempFilePath);
            }

            // Show save file dialog for final file
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "SDC files (*.sdc)|*.sdc",
                DefaultExt = "sdc",
                FileName = sdcFileName
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                await File.WriteAllBytesAsync(saveFileDialog.FileName, fileContent);
            }
            Main.SceneListTabView.LoadSceneListXMLv2intoEditorButton_Click(sender, e);
            // Clean up temporary file
            File.Delete(tempFilePath);
        }

        private string ConvertToSdcFileName(string archiveFileName, string thumbnailSuffix)
        {
            // Remove _TXXX suffix if it exists
            string sdcFileName = Regex.Replace(archiveFileName, "_T\\d{3}$", "");

            // Replace .sdat with .sdc
            sdcFileName = sdcFileName + ".sdc";

            // Append thumbnail suffix if it exists
            if (!string.IsNullOrEmpty(thumbnailSuffix))
            {
                sdcFileName = sdcFileName.Replace(".sdc", $"_T{thumbnailSuffix}.sdc");
            }

            return sdcFileName;
        }



        private XElement CreateArchiveElement(string archivePath, string archiveSize, string timestamp)
        {
            var archiveElement = new XElement("ARCHIVE", $"[CONTENT_SERVER_ROOT]{archivePath}");

            if (!string.IsNullOrEmpty(archiveSize))
            {
                archiveElement.Add(new XAttribute("size", archiveSize));
            }

            if (!string.IsNullOrEmpty(timestamp))
            {
                archiveElement.Add(new XAttribute("timestamp", timestamp));
            }

            return archiveElement;
        }

        private async Task<byte[]> EncryptSDCODCFileAsync(string filePath)
        {
            try
            {
                byte[] fileContent = await File.ReadAllBytesAsync(filePath);

                // Generate SHA1 hash from file content before encryption
                using (SHA1 sha1 = SHA1.Create())
                {
                    byte[] SHA1Data = sha1.ComputeHash(fileContent);
                    string inputSHA1 = BitConverter.ToString(SHA1Data).Replace("-", "").ToLowerInvariant();

                    // Encrypt the file content
                    string computedSha1 = inputSHA1.Substring(0, 16); // Use first 16 characters for encryption key
                    byte[] encryptedContent = CDSProcess.CDSEncrypt_Decrypt(fileContent, computedSha1, (int)CdnMode.RETAIL);

                    return encryptedContent;
                }
            }
            catch (Exception)
            {
                return null;
            }
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

        // TAB 8: Logic for SDC Creation

        private async void ODCCreateODC_Click(object sender, RoutedEventArgs e)
        {

            // Generate a UUID if odcUUIDTextBox is empty
            if (string.IsNullOrEmpty(odcUUIDTextBox.Text))
            {
                odcUUIDTextBox.Text = Main.GenerateUUID();
            }
            Main.DbEditorTabView.txtObjectId.Text = odcUUIDTextBox.Text;
            Main.DbEditorTabView.txtArchiveTimeStampHex.Text = odcTimestampTextBox.Text;
            // Gather data from the form
            string name = odcNameTextBox.Text;
            string description = odcDescriptionTextBox.Text;
            string maker = odcMakerTextBox.Text;
            string hdkVersion = odcHDKVersionTextBox.Text;
            string thumbnailSuffix = odcThumbnailSuffixTextBox.Text;
            string uuid = odcUUIDTextBox.Text;
            string timestamp = odcTimestampTextBox.Text;

            // Pad the thumbnail suffix to 3 digits if it is not empty
            if (!string.IsNullOrEmpty(thumbnailSuffix))
            {
                thumbnailSuffix = thumbnailSuffix.PadLeft(3, '0');
            }

            // Write the version to txtVersion with leading zeros trimmed off
            if (string.IsNullOrEmpty(thumbnailSuffix))
            {
                Main.DbEditorTabView.txtVersion.Text = "0";
            }
            else
            {
                Main.DbEditorTabView.txtVersion.Text = thumbnailSuffix.TrimStart('0');
            }
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
                    // Conditionally add the timestamp element
                    !string.IsNullOrEmpty(timestamp) ? new XElement("timestamp", timestamp) : null
                )
            );

            // Construct the output file name
            string outputFileName = $"{uuid}{(string.IsNullOrEmpty(thumbnailSuffix) ? "" : $"_T{thumbnailSuffix}")}.odc";

            // Save the XML to a temporary file
            string tempFilePath = Path.Combine(Path.GetTempPath(), outputFileName);
            odcXml.Save(tempFilePath);

            // Calculate SHA1 hash and update the text box
            string sha1Hash = await Task.Run(() => CalculateSDCODCSha1Hash(tempFilePath));
            Createdodcsha1TextBox.Text = sha1Hash;
            Main.DbEditorTabView.txtOdcSha1Digest.Text = sha1Hash;
            // Read the content of the temporary file and set it to the ODCCreatorTextbox
            string odcContent = await File.ReadAllTextAsync(tempFilePath);
            ODCCreatorTextbox.Text = odcContent;

            byte[] fileContent;

            // Check if encryption is needed
            if (EncryptODCCheckBox.IsChecked == true)
            {
                fileContent = await EncryptSDCODCFileAsync(tempFilePath);
            }
            else
            {
                fileContent = await File.ReadAllBytesAsync(tempFilePath);
            }

            // Show save file dialog for final file
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "ODC files (*.odc)|*.odc",
                DefaultExt = "odc",
                FileName = outputFileName
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                await File.WriteAllBytesAsync(saveFileDialog.FileName, fileContent);
            }

            // Clean up temporary file
            File.Delete(tempFilePath);
        }

        private string CalculateSDCODCSha1Hash(string filePath)
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

        private void CreateOdcClearButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear each TextBox by setting its Text property to an empty string
            odcNameTextBox.Text = string.Empty;
            odcDescriptionTextBox.Text = string.Empty;
            odcMakerTextBox.Text = "Sony Computer Entertainment"; // Reset to default text
            odcHDKVersionTextBox.Text = "1.86.0.17.PUB"; // Reset to default text
            odcThumbnailSuffixTextBox.Text = string.Empty;
            odcUUIDTextBox.Text = string.Empty;
            odcTimestampTextBox.Text = "FFFFFFFF"; // Reset to default text
            Createdodcsha1TextBox.Text = string.Empty;
            ODCCreatorTextbox.Text = string.Empty;
        }
        private void CreateSdcClearButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear each TextBox by setting its Text property to an empty string
            sdcNameTextBox.Text = string.Empty;
            sdcDescriptionTextBox.Text = string.Empty;
            sdcMakerTextBox.Text = "Sony Computer Entertainment"; // Reset to default text
            sdcHDKVersionTextBox.Text = "1.86.0.17.PUB"; // Reset to default text
            sdcThumbnailSuffixTextBox.Text = string.Empty;
            sdcServerArchivePathTextBox.Text = "Scenes/"; // Reset to default text
            sdcTimestampTextBox.Text = "FFFFFFFF"; // Reset to default text
            sdcArchiveSizeTextBox.Text = string.Empty;
            Createdsdcsha1TextBox.Text = string.Empty;
            SDCCreatorTextbox.Text = string.Empty;

            // Reset CheckBox states
            offlineSDCCheckBox.IsChecked = false;
            EncryptSDCCheckBox.IsChecked = false;
        }

        private async void SDCCopySHA1Button_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(Createdsdcsha1TextBox.Text))
            {
                string originalText = Createdsdcsha1TextBox.Text;
                Clipboard.SetText(originalText);
                Createdsdcsha1TextBox.Text = "SHA1 Copied";
                await Task.Delay(500);
                Createdsdcsha1TextBox.Text = originalText;
            }
        }

        private async void ODCCopySHA1Button_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(Createdodcsha1TextBox.Text))
            {
                string originalText = Createdodcsha1TextBox.Text;
                Clipboard.SetText(originalText);
                Createdodcsha1TextBox.Text = "SHA1 Copied";
                await Task.Delay(500);
                Createdodcsha1TextBox.Text = originalText;
            }
        }

        private void ODCGenerateUUIDButton_Click(object sender, RoutedEventArgs e)
        {
            string uuid = Main.GenerateUUID();
            odcUUIDTextBox.Text = uuid;
            Main.DbEditorTabView.txtObjectId.Text = uuid;  // Assuming 'anotherUUIDTextBox' is the name of the second textbox
        }

        private void CheckBoxOfflineSDC_Checked(object sender, RoutedEventArgs e)
        {

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
                // This handler is attached to nested panels too; handle each drop once.
                e.Handled = true;
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                // Left half of the tab = SDC, right half = ODC (measured on the whole tab, so it works
                // whichever panel or text box the files were dropped on)
                Point dropPosition = e.GetPosition(this);
                int column = dropPosition.X < ActualWidth / 2 ? 0 : 1;

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

        private async void HandleSdcFileDrop(string[] files)
        {
            string filePath = files.FirstOrDefault();
            Main.LogDebugInfo("HandleSdcFileDrop: File dropped - " + filePath);

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                MessageBox.Show("Invalid file. Please drop a valid SDC XML file.");
                Main.LogDebugInfo("HandleSdcFileDrop: Invalid file path or file does not exist.");
                return;
            }

            try
            {
                // Try to load the SDC XML file
                Main.LogDebugInfo("HandleSdcFileDrop: Attempting to load SDC XML file.");
                XDocument sdcXml = XDocument.Load(filePath);
                ProcessSdcXml(sdcXml);
                Main.LogDebugInfo("HandleSdcFileDrop: SDC XML file loaded successfully.");
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo("HandleSdcFileDrop: Failed to load SDC XML file. Attempting decryption. Exception: " + ex.Message);

                string tempDirectory = Path.Combine(Path.GetTempPath(), "DecryptedFiles");
                Directory.CreateDirectory(tempDirectory);

                bool decryptionSuccess = await Main.DecryptFilesAsync(new[] { filePath }, tempDirectory);
                if (decryptionSuccess)
                {
                    string parentFolderName = Path.GetFileName(Path.GetDirectoryName(filePath));
                    string decryptedFilePath = Path.Combine(tempDirectory, parentFolderName, Path.GetFileName(filePath));
                    Main.LogDebugInfo("HandleSdcFileDrop: Decryption successful. Decrypted file path - " + decryptedFilePath);

                    if (File.Exists(decryptedFilePath))
                    {
                        try
                        {
                            XDocument sdcXml = XDocument.Load(decryptedFilePath);
                            ProcessSdcXml(sdcXml);
                            Main.LogDebugInfo("HandleSdcFileDrop: Decrypted SDC XML file loaded successfully.");

                            // Compute and display the SHA1 hash
                            string sha1Hash = CalculateSDCODCSha1Hash(decryptedFilePath);
                            Createdsdcsha1TextBox.Text = sha1Hash;
                            Main.LogDebugInfo("HandleSdcFileDrop: SHA1 hash computed - " + sha1Hash);
                        }
                        catch (Exception ex2)
                        {
                            MessageBox.Show($"Error reading decrypted SDC file: {ex2.Message}");
                            Main.LogDebugInfo("HandleSdcFileDrop: Error reading decrypted SDC file. Exception: " + ex2.Message);
                        }
                    }
                }
                else
                {
                    MessageBox.Show("Error reading or decrypting SDC file.");
                    Main.LogDebugInfo("HandleSdcFileDrop: Decryption failed.");
                }
            }
        }


        private void ProcessSdcXml(XDocument sdcXml)
        {
            Main.LogDebugInfo("ProcessSdcXml: Processing SDC XML file.");
            var englishLanguageElement = sdcXml.Root.Elements("LANGUAGE").FirstOrDefault(el => el.Attribute("REGION")?.Value == "en-GB");

            if (englishLanguageElement != null)
            {
                sdcNameTextBox.Text = englishLanguageElement.Element("NAME")?.Value ?? "";
                sdcDescriptionTextBox.Text = englishLanguageElement.Element("DESCRIPTION")?.Value ?? "";
                sdcMakerTextBox.Text = englishLanguageElement.Element("MAKER")?.Value ?? "";

                var archiveElement = englishLanguageElement.Element("ARCHIVES")?.Element("ARCHIVE");
                if (archiveElement != null)
                {
                    sdcServerArchivePathTextBox.Text = (archiveElement.Value ?? "").Replace("[CONTENT_SERVER_ROOT]", string.Empty);
                    sdcArchiveSizeTextBox.Text = archiveElement.Attribute("size")?.Value ?? "";
                    sdcTimestampTextBox.Text = archiveElement.Attribute("timestamp")?.Value ?? "";
                }

                var makerImagePath = englishLanguageElement.Element("MAKER_IMAGE")?.Value ?? "";
                sdcThumbnailSuffixTextBox.Text = ExtractSDCThumbnailSuffix(makerImagePath);

                offlineSDCCheckBox.IsChecked = englishLanguageElement.Element("ARCHIVES") == null;
            }

            sdcHDKVersionTextBox.Text = sdcXml.Root.Attribute("hdk_version")?.Value ?? "";
            Main.LogDebugInfo("ProcessSdcXml: SDC XML file processed successfully.");
        }


        private string ExtractSDCThumbnailSuffix(string imagePath)
        {
            // Assuming the thumbnail suffix is in the format "_Txxx.png"
            var match = Regex.Match(imagePath, @"_T(\d+)\.png");
            return match.Success ? match.Groups[1].Value : "";
        }


        private async void HandleOdcFileDrop(string[] files)
        {
            string filePath = files.FirstOrDefault();
            Main.LogDebugInfo("HandleOdcFileDrop: File dropped - " + filePath);

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                MessageBox.Show("Invalid file. Please drop a valid ODC XML file.");
                Main.LogDebugInfo("HandleOdcFileDrop: Invalid file path or file does not exist.");
                return;
            }

            try
            {
                // Try to load the ODC XML file
                Main.LogDebugInfo("HandleOdcFileDrop: Attempting to load ODC XML file.");
                XDocument odcXml = XDocument.Load(filePath);
                ProcessOdcXml(odcXml);
                Main.LogDebugInfo("HandleOdcFileDrop: ODC XML file loaded successfully.");
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo("HandleOdcFileDrop: Failed to load ODC XML file. Attempting decryption. Exception: " + ex.Message);

                string tempDirectory = Path.Combine(Path.GetTempPath(), "DecryptedFiles");
                Directory.CreateDirectory(tempDirectory);

                bool decryptionSuccess = await Main.DecryptFilesAsync(new[] { filePath }, tempDirectory);
                if (decryptionSuccess)
                {
                    // Adjust decryptedFilePath to reflect the expected folder structure
                    string parentFolderName = Path.GetFileName(Path.GetDirectoryName(filePath));
                    string decryptedFilePath = Path.Combine(tempDirectory, parentFolderName, Path.GetFileName(filePath));
                    Main.LogDebugInfo("HandleOdcFileDrop: Decryption successful. Decrypted file path - " + decryptedFilePath);

                    if (File.Exists(decryptedFilePath))
                    {
                        try
                        {
                            // Compute SHA1 of the decrypted file using the existing function
                            string sha1Hash = CalculateSDCODCSha1Hash(decryptedFilePath);
                            Createdodcsha1TextBox.Text = sha1Hash;

                            XDocument odcXml = XDocument.Load(decryptedFilePath);
                            ProcessOdcXml(odcXml);
                            Main.LogDebugInfo("HandleOdcFileDrop: Decrypted ODC XML file loaded successfully.");
                        }
                        catch (Exception ex2)
                        {
                            MessageBox.Show($"Error reading decrypted ODC file: {ex2.Message}");
                            Main.LogDebugInfo("HandleOdcFileDrop: Error reading decrypted ODC file. Exception: " + ex2.Message);
                        }
                    }
                }
                else
                {
                    MessageBox.Show("Error reading or decrypting ODC file.");
                    Main.LogDebugInfo("HandleOdcFileDrop: Decryption failed.");
                }
            }
        }

        private void ProcessOdcXml(XDocument odcXml)
        {
            Main.LogDebugInfo("ProcessOdcXml: Processing ODC XML file.");
            odcNameTextBox.Text = odcXml.Root.Elements("name").FirstOrDefault()?.Value ?? "";
            odcDescriptionTextBox.Text = odcXml.Root.Elements("description").FirstOrDefault()?.Value ?? "";
            odcMakerTextBox.Text = odcXml.Root.Elements("maker").FirstOrDefault()?.Value ?? "";
            odcHDKVersionTextBox.Text = odcXml.Root.Attribute("hdk_version")?.Value ?? "";
            odcUUIDTextBox.Text = odcXml.Root.Element("uuid")?.Value ?? "";
            odcTimestampTextBox.Text = odcXml.Root.Element("timestamp")?.Value ?? "";

            var makerImagePath = odcXml.Root.Elements("maker_image").FirstOrDefault()?.Value ?? "";
            odcThumbnailSuffixTextBox.Text = ExtractODCThumbnailSuffix(makerImagePath);
            Main.LogDebugInfo("ProcessOdcXml: ODC XML file processed successfully.");
        }


        private string ExtractODCThumbnailSuffix(string imagePath)
        {
            // Assuming the thumbnail suffix is in the format "_Txxx.png"
            var match = Regex.Match(imagePath, @"_T(\d+)\.png");
            return match.Success ? match.Groups[1].Value : "";
        }
    }
}
