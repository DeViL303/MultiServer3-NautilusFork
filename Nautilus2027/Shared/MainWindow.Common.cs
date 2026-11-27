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
    // Helpers used by more than one tab (decryption, validation, theme, reward types, etc).
    public partial class MainWindow
    {
        internal AppSettings _settings;

        private SolidColorBrush _selectedThemeColor;
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
            // Each tab fills its own controls from the saved settings.
            SettingsTabView.ApplySettings(_settings);
            SceneListTabView.ApplySettings(_settings);
            TssTabView.ApplySettings(_settings);
            ArchiveToolTabView.ApplySettings(_settings);

            SelectLastUsedTab(_settings.LastTabUsed);
        }


        internal void ClearListHandler(object sender, RoutedEventArgs e)
        {
            var textBox = GetAssociatedTextBox(sender as FrameworkElement);
            if (textBox != null)
            {
                textBox.Clear();

                // Derive the TextBlock name from the TextBox name
                string textBlockName = textBox.Name.Replace("TextBox", "DragAreaText");

                // Find the TextBlock next to the TextBox (searches the tab the TextBox lives in)
                var textBlock = textBox.FindName(textBlockName) as TextBlock;
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
            // Search from the clicked element, so the lookup happens inside the tab it belongs to
            var textBox = element.FindName(textBoxName) as TextBox;

            if (textBox == null)
            {
                throw new InvalidOperationException($"A TextBox with name '{textBoxName}' could not be found.");
            }

            return textBox;
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

                    byte[] fileContent = await File.ReadAllBytesAsync(filePath);

                    BruteforceProcess proc = new BruteforceProcess(fileContent);
                    byte[] decryptedContent = proc.StartBruteForce((int)CdnMode.RETAIL);

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
                            allFilesProcessed = false;
                        }
                    }
                    else
                    {
                        allFilesProcessed = false;
                    }
                }
                catch (Exception)
                {
                    allFilesProcessed = false;
                }
            }

            return allFilesProcessed;
        }


        internal bool IsValidDecryptedFile(string filePath, string extension)
        {
            if (string.Equals(extension, ".hcdb", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                if (string.Equals(extension, ".sdc", StringComparison.OrdinalIgnoreCase))
                {
                    return ValidateSdcFile(filePath);
                }
                else if (string.Equals(extension, ".odc", StringComparison.OrdinalIgnoreCase))
                {
                    return ValidateOdcFile(filePath);
                }
                else if (string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase))
                {
                    return ValidateXmlFile(filePath);
                }

                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool ValidateSdcFile(string filePath)
        {
            try
            {
                var doc = XDocument.Load(filePath);
                var nameElement = doc.Descendants("NAME").FirstOrDefault();
                if (nameElement != null)
                {
                    string elementValue = nameElement.Value;
                    string sha1Hash = CalculateSha1Hash(filePath);
                    string fileName = Path.GetFileName(filePath);

                    if (Path.GetFileName(filePath).ToUpper().Contains("_DAT.SDC"))
                    {
                        var archiveElement = doc.Descendants("ARCHIVE").FirstOrDefault();
                        if (archiveElement != null)
                        {
                            string archiveValue = archiveElement.Value;
                            string trimmedFileName = archiveValue.Replace("[CONTENT_SERVER_ROOT]", "").Replace(".sdat", ".sdc");
                        }
                    }

                    return true;
                }

                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal string CalculateSha1Hash(string filePath)
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

        private bool ValidateOdcFile(string filePath)
        {
            try
            {
                var doc = XDocument.Load(filePath);
                var uuidElement = doc.Descendants("uuid").FirstOrDefault();
                if (uuidElement != null)
                {
                    string uuidValue = uuidElement.Value;
                    string sha1Hash = CalculateSha1Hash(filePath);
                    string fileName = Path.GetFileName(filePath);

                    if (Path.GetFileName(filePath).ToUpper().Contains("_DAT.ODC"))
                    {
                        var imageElement = doc.Descendants("small_image").FirstOrDefault();
                        if (imageElement == null)
                        {
                            imageElement = doc.Descendants("large_image").FirstOrDefault();
                        }

                        if (imageElement != null)
                        {
                            string imageValue = imageElement.Value;
                            string trimmedImageValue = imageValue.Replace("[THUMBNAIL_ROOT]small", "").Replace("[THUMBNAIL_ROOT]large", "").Replace(".png", "");
                            string finalName = $"Objects/{uuidValue}/object{trimmedImageValue}.odc";
                        }
                    }

                    return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool ValidateXmlFile(string filePath)
        {
            try
            {
                var doc = XDocument.Load(filePath);

                // For now, just return true without any specific checks
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }


        internal Task<bool> DecryptFilesAsync(string[] filePaths)
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


        public async Task<byte[]?> DecryptHCDBFilesAsync(string filePath, string outputDirectory)
        {
            string filename = Path.GetFileName(filePath);
            try
            {
                byte[] fileContent = await File.ReadAllBytesAsync(filePath);
                LogDebugInfo($"File content read successfully for {filename}, starting decryption.");

                // Assuming decryption process
                BruteforceProcess? proc = new BruteforceProcess(fileContent);
                byte[]? decryptedContent = proc.StartBruteForce((int)CdnMode.RETAIL);  // Simplify, assuming this method does the decryption

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
                byte[]? processedContent = CDSProcess.CDSEncrypt_Decrypt(fileContent, sha1Hash.Substring(0, 16), (int)CdnMode.RETAIL);

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


        public static byte[]? HCDBUnpack(byte[] decryptedData, Action<string> log)
        {
            try
            {
                byte[]? decompressedData = CompressionLibrary.Edge.LZMA.Decompress(decryptedData, true);

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

        internal bool IsValidSqlFile(string filePath)
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


        private void SelectLastUsedTab(RememberLastTabUsed lastTabUsed)
        {
            // Pick the tab whose Tag matches the saved value. (The old hard-coded indexes had drifted
            // out of step with the tab order, so some tabs reopened as a different tab.)
            var tab = MainTabControl.Items.OfType<TabItem>()
                .FirstOrDefault(t => string.Equals(t.Tag?.ToString(), lastTabUsed.ToString(), StringComparison.Ordinal));
            MainTabControl.SelectedIndex = tab != null && tab.Visibility == Visibility.Visible
                ? MainTabControl.Items.IndexOf(tab)
                : 0;
        }


        internal string GenerateUUID()
        {
            Random random = new Random();
            Func<int, string> randomHex = length => new string(Enumerable.Repeat("0123456789ABCDEF", length)
                                                                    .Select(s => s[random.Next(s.Length)]).ToArray());

            return $"{randomHex(8)}-{randomHex(8)}-{randomHex(8)}-{randomHex(8)}";
        }
        internal CancellationTokenSource cancellationTokenSource;
        internal string sqlFilePath;

        internal int ConvertHexToInt(string hex)
        {
            try
            {
                string h = (hex ?? "").Trim();
                if (h.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) h = h.Substring(2);
                return unchecked((int)uint.Parse(h, System.Globalization.NumberStyles.HexNumber));
            }
            catch (Exception ex)
            {
                LogDebugInfo($"Format error converting hex to int: {ex.Message}");
                throw;
            }
        }

        internal string csvFilePath;


        internal string ConvertTimestampToHex(string timestamp)
        {
            if (long.TryParse(timestamp, out long timestampLong))
            {
                return unchecked((uint)timestampLong).ToString("X8");
            }
            return timestamp; // Return the original value if conversion fails
        }

        /// <summary>A timestamp cell from SQLite as 8 hex digits, whatever type it was stored as
        /// (signed or unsigned integer, real, decimal or hex text, or a 4-byte blob).</summary>
        internal string TimestampHex(object value)
        {
            try
            {
                switch (value)
                {
                    case null: return "";
                    case DBNull _: return "";
                    case long l: return unchecked((uint)l).ToString("X8");
                    case int i: return unchecked((uint)i).ToString("X8");
                    case double d: return unchecked((uint)(long)d).ToString("X8");
                    case byte[] b when b.Length > 0 && b.Length <= 4:
                        uint v = 0; foreach (byte x in b) v = (v << 8) | x;
                        return v.ToString("X8");
                    case string s:
                        s = s.Trim();
                        if (long.TryParse(s, out long sl)) return unchecked((uint)sl).ToString("X8");
                        string h = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s.Substring(2) : s;
                        if (h.Length <= 8 && uint.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out uint hv)) return hv.ToString("X8");
                        return s;
                }
                return unchecked((uint)Convert.ToInt64(value)).ToString("X8");
            }
            catch { return value?.ToString() ?? ""; }
        }

        internal string ConvertBlobToSha1(byte[] blob)
        {
            if (blob != null && blob.Length == 20) // Ensure it's a 20-byte SHA1 blob
            {
                return BitConverter.ToString(blob).Replace("-", "").ToUpper();
            }
            return string.Empty;
        }

        internal void txtObjectId_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            DbEditorTabView.txtObjectId.Text = GenerateUUID();
        }

        internal void EncryptAndSetChannelID()
        {
            try
            {
                Random random = new Random();
                int randomNumber = random.Next(30303, 50505);

                ushort sceneID = (ushort)randomNumber;

                bool isLegacyMode = SceneIdsTabView.legacyModeCheckBox.IsChecked ?? false;
                SceneKey key = isLegacyMode ? SIDKeyGenerator.Instance.Generate(sceneID)
                                            : SIDKeyGenerator.Instance.GenerateNewerType(sceneID);

                SceneListTabView.txtChannelID.Text = key.ToString();
                LogDebugInfo($"Channel ID set: {SceneListTabView.txtChannelID.Text}");
            }
            catch (Exception ex)
            {
                LogDebugInfo($"Error during encryption: {ex.Message}");
                SceneListTabView.txtChannelID.Text = $"Error during encryption: {ex.Message}";
            }
        }
    }
}
