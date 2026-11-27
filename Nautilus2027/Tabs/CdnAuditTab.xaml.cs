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
    // "CDN Audit" tab.
    public partial class CdnAuditTab : UserControl
    {
        private static MainWindow Main => MainWindow.Instance;

        public CdnAuditTab()
        {
            InitializeComponent();
        }

        private void XMLClearListHandler(object sender, RoutedEventArgs e)
        {
            XMLOutputTextBox.Clear();
        }

        private string Sha1ForHCDB;
        private string ContentServerURL;
        private string SceneListSHA1;



        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(Main._settings.CdnAuditDirectory);
            string tssUrl = TSSUrlTextBox.Text;
            if (string.IsNullOrEmpty(tssUrl))
            {
                MessageBox.Show("Please enter a TSS URL.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string selectedTerritory = ((ComboBoxItem)TerritoryComboBox.SelectedItem).Content.ToString();

            // Log starting message
            LogBox.Append(XMLOutputTextBox, $"Starting CDN audit with TSS URL: {tssUrl}.... ", log: true);

            // Download the TSS XML
            bool tssDownloadSuccess = await AuditDownloadFileAsync(tssUrl, "TSS.xml");
            XMLOutputTextBox.Text += tssDownloadSuccess
                ? "TSS XML downloaded successfully.\n"
                : "Failed to download TSS XML.\n";

            if (!tssDownloadSuccess)
            {
                return;
            }

            // Parse the TSS XML to find the SHA1 for the ObjectCatalogue_5_<territory>.hcdb
            string tssFilePath = Path.Combine(Main._settings.CdnAuditDirectory, "TSS.xml");
            Sha1ForHCDB = ParseSha1FromTssXml(tssFilePath, $"Objects/ObjectCatalogue_5_{selectedTerritory}.hcdb");

            if (string.IsNullOrEmpty(Sha1ForHCDB))
            {
                LogBox.Append(XMLOutputTextBox, $"SHA1 for ObjectCatalogue_5_{selectedTerritory}.hcdb not found in TSS XML.\n", log: true);
            }
            else
            {
                LogBox.Append(XMLOutputTextBox, $"SHA1 found for ObjectCatalogue_5_{selectedTerritory}.hcdb: {Sha1ForHCDB}\n", log: true);
            }

            // Parse the TSS XML to find the SHA1 for the SceneList.xml
            SceneListSHA1 = ParseSha1FromTssXml(tssFilePath, "Environments/SceneList.xml");

            if (string.IsNullOrEmpty(SceneListSHA1))
            {
                LogBox.Append(XMLOutputTextBox, "SHA1 for SceneList.xml not found in TSS XML.\n", log: true);
            }
            else
            {
                LogBox.Append(XMLOutputTextBox, $"SHA1 found for SceneList.xml: {SceneListSHA1}\n", log: true);
            }

            // Parse the TSS XML to find the Content Server URL
            ContentServerURL = ParseContentServerUrlFromTssXml(tssFilePath, tssUrl);

            if (string.IsNullOrEmpty(ContentServerURL))
            {
                LogBox.Append(XMLOutputTextBox, "Content Server URL not found in TSS XML.\n", log: true);
            }
            else
            {
                LogBox.Append(XMLOutputTextBox, $"Content Server URL Found and Set to: {ContentServerURL}\n", log: true);
            }

            // Extract the domain from the TSS URL
            Uri tssUri = new Uri(tssUrl);
            string domain = $"{tssUri.Scheme}://{tssUri.Host}";

            // Construct the URL for the ObjectCatalogue file
            string objectCatalogueUrl = $"{domain}/Objects/ObjectCatalogue_5_{selectedTerritory}.hcdb";

            // Log starting message
            LogBox.Append(XMLOutputTextBox, $"Starting download of ObjectCatalogue_5_{selectedTerritory}.hcdb.... \n", log: true);

            // Download the ObjectCatalogue file
            bool catalogueDownloadSuccess = await DownloadFileWithProgressAsync(objectCatalogueUrl, $"ObjectCatalogue_5_{selectedTerritory}.hcdb");
            XMLOutputTextBox.Text += catalogueDownloadSuccess
                ? $"ObjectCatalogue_5_{selectedTerritory}.hcdb downloaded successfully.\n"
                : $"Failed to download ObjectCatalogue_5_{selectedTerritory}.hcdb.\n";

            if (catalogueDownloadSuccess)
            {
                string hcdbFilePath = Path.Combine(Main._settings.CdnAuditDirectory, $"ObjectCatalogue_5_{selectedTerritory}.hcdb");
                string outputDirectory = Main._settings.CdnAuditDirectory;

                // Decrypt and convert the HCDB file
                bool decryptionSuccess = await DecryptAndConvertHcdbAsync(hcdbFilePath, outputDirectory);
                XMLOutputTextBox.Text += decryptionSuccess
                    ? $"ObjectCatalogue_5_{selectedTerritory}.hcdb decrypted and converted to SQL successfully.\n"
                    : $"Failed to decrypt and convert ObjectCatalogue_5_{selectedTerritory}.hcdb.\n";
            }

            // Construct the URL for the SceneList.xml file
            string sceneListUrl = $"{domain}/Environments/SceneList.xml";

            // Log starting message
            LogBox.Append(XMLOutputTextBox, $"Starting download of SceneList.xml from {sceneListUrl}.... \n", log: true);

            // Download the SceneList.xml file
            bool sceneListDownloadSuccess = await DownloadFileWithProgressAsync(sceneListUrl, "SceneList.xml");
            XMLOutputTextBox.Text += sceneListDownloadSuccess
                ? "SceneList.xml downloaded successfully.\n"
                : "Failed to download SceneList.xml.\n";

            if (sceneListDownloadSuccess)
            {
                string sceneListFilePath = Path.Combine(Main._settings.CdnAuditDirectory, "SceneList.xml");
                string outputDirectory = Main._settings.CdnAuditDirectory;

                // Decrypt the SceneList.xml file
                bool sceneListDecryptionSuccess = await DecryptSceneListAsync(sceneListFilePath, outputDirectory);
                XMLOutputTextBox.Text += sceneListDecryptionSuccess
                    ? ""
                    : "Failed to decrypt SceneList.xml.\n";
            }
        }


        private async Task<bool> DownloadFileWithProgressAsync(string url, string fileName)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(10); // Increase timeout to 10 minutes

                    var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    string downloadDirectory = Main._settings.CdnAuditDirectory;
                    Directory.CreateDirectory(downloadDirectory);
                    string downloadPath = Path.Combine(downloadDirectory, fileName);

                    using (var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var httpStream = await response.Content.ReadAsStreamAsync())
                    {
                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        var canReportProgress = totalBytes != -1;
                        var totalRead = 0L;
                        var buffer = new byte[8192];
                        var isMoreToRead = true;

                        while (isMoreToRead)
                        {
                            var read = await httpStream.ReadAsync(buffer, 0, buffer.Length);
                            if (read == 0)
                            {
                                isMoreToRead = false;
                            }
                            else
                            {
                                await fileStream.WriteAsync(buffer, 0, read);
                                totalRead += read;

                                if (canReportProgress)
                                {
                                    UpdateLastLine($"Downloaded {totalRead} of {totalBytes} bytes ({(double)totalRead / totalBytes:P}).... ");
                                }
                                else
                                {
                                    UpdateLastLine($"Downloaded {totalRead} bytes.... ");
                                }
                            }
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                LogBox.Append(XMLOutputTextBox, $"Error downloading file from {url}: {ex.Message}\n", log: true);
                return false;
            }
        }

        private void UpdateLastLine(string text)
        {
            Dispatcher.Invoke(() =>
            {
                var lines = XMLOutputTextBox.Text.Split('\n').ToList();
                if (lines.Count > 1)
                {
                    lines[lines.Count - 1] = text;
                }
                else
                {
                    lines.Add(text);
                }
                XMLOutputTextBox.Text = string.Join("\n", lines);
                LogBox.Trim(XMLOutputTextBox);
                XMLOutputTextBox.ScrollToEnd();
            });
        }

        private string ParseSha1FromTssXml(string filePath, string targetFile)
        {
            try
            {
                XDocument doc = XDocument.Load(filePath);
                var sha1Element = doc.Descendants("SHA1")
                                     .FirstOrDefault(e => e.Attribute("file")?.Value == targetFile);
                return sha1Element?.Attribute("digest")?.Value;
            }
            catch (Exception ex)
            {
                LogBox.Append(XMLOutputTextBox, $"Error parsing TSS XML: {ex.Message}\n", log: true);
                return null;
            }
        }

        private string ParseContentServerUrlFromTssXml(string filePath, string tssUrl)
        {
            try
            {
                XDocument doc = XDocument.Load(filePath);
                var contentServerElement = doc.Descendants("contentserver")
                                              .FirstOrDefault();
                if (contentServerElement != null)
                {
                    string contentServerUrl = contentServerElement.Value;

                    // Extract the domain from the TSS URL
                    Uri tssUri = new Uri(tssUrl);
                    string domain = $"{tssUri.Scheme}://{tssUri.Host}";

                    // Replace the domain in the content server URL
                    Uri contentServerUri = new Uri(contentServerUrl);
                    string newContentServerUrl = $"{domain}{contentServerUri.PathAndQuery}";

                    return newContentServerUrl;
                }
                return null;
            }
            catch (Exception ex)
            {
                LogBox.Append(XMLOutputTextBox, $"Error parsing TSS XML for content server URL: {ex.Message}\n", log: true);
                return null;
            }
        }

        private async Task<bool> DecryptAndConvertHcdbAsync(string hcdbFilePath, string outputDirectory)
        {
            LogBox.Append(XMLOutputTextBox, $"Starting decryption of HCDB file.... ", log: true);

            // Attempt decryption with SHA1
            byte[]? decryptedData = await Main.DecryptHCDBFilesSHA1Async(hcdbFilePath, outputDirectory, Sha1ForHCDB);
            if (decryptedData == null)
            {
                LogBox.Append(XMLOutputTextBox, $"SHA1 decryption failed, trying brute force for file: {hcdbFilePath}\n", log: true);
                // Attempt brute force decryption
                decryptedData = await Main.DecryptHCDBFilesAsync(hcdbFilePath, outputDirectory);
            }

            if (decryptedData != null)
            {
                LogBox.Append(XMLOutputTextBox, $"Decryption successful for HCDB file: {hcdbFilePath}\n", log: true);
                string fileName = Path.GetFileName(hcdbFilePath);
                bool processSuccess = await AuditProcessDecryptedHCDB(hcdbFilePath, decryptedData, outputDirectory, fileName);
                if (processSuccess)
                {
                    string sqlFilePath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(fileName) + ".SQL");
                    LogBox.Append(XMLOutputTextBox, $"HCDB file converted to SQL successfully: {sqlFilePath}\n", log: true);
                    return true;
                }
                else
                {
                    LogBox.Append(XMLOutputTextBox, $"Failed to process decrypted HCDB file to SQL: {hcdbFilePath}\n", log: true);
                    return false;
                }
            }
            else
            {
                LogBox.Append(XMLOutputTextBox, $"Failed to decrypt HCDB file: {hcdbFilePath}\n", log: true);
                return false;
            }
        }



        private async Task<bool> AuditProcessDecryptedHCDB(string filePath, byte[] decryptedData, string outputDirectory, string filename)
        {
            try
            {
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

                        // Export to CSV
                        string csvFilePath = Path.Combine(outputDirectory, cleanFilename.Replace(".SQL", ".csv"));
                        bool csvExportSuccess = await AuditExportToCSV(outputPath, csvFilePath);
                        if (!csvExportSuccess)
                        {
                            csvExportSuccess = await AuditExportToCSV(outputPath, csvFilePath); // Retry once
                        }

                        return csvExportSuccess;
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

        private async Task<bool> AuditExportToCSV(string sqlFilePath, string csvFilePath)
        {
            try
            {
                Main.csvFilePath = csvFilePath; // Save the CSV file path

                using (var writer = new StreamWriter(csvFilePath))
                using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                {
                    string connectionString = $"Data Source={sqlFilePath};Version=3;";
                    using (SQLiteConnection connection = new SQLiteConnection(connectionString))
                    {
                        await connection.OpenAsync();

                        string query = @"
                SELECT 
                    o.ObjectIndex, 
                    o.ObjectId, 
                    o.Version, 
                    o.Location, 
                    o.InventoryEntryType, 
                    o.ArchiveTimeStamp, 
                    o.OdcSha1Digest, 
                    o.EntitlementIndex, 
                    o.RewardIndex, 
                    o.UserLocation, 
                    o.UserDateLastUsed, 
                    m.KeyName, 
                    m.Value
                FROM Objects o
                LEFT JOIN Metadata m ON o.ObjectIndex = m.ObjectIndex";

                        using (SQLiteCommand command = new SQLiteCommand(query, connection))
                        {
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                bool headerWritten = false;

                                while (await reader.ReadAsync())
                                {
                                    var sqliteReader = (SQLiteDataReader)reader;

                                    if (!headerWritten)
                                    {
                                        for (int i = 0; i < sqliteReader.FieldCount; i++)
                                        {
                                            csv.WriteField(sqliteReader.GetName(i));
                                        }
                                        csv.NextRecord();
                                        headerWritten = true;
                                    }

                                    for (int i = 0; i < sqliteReader.FieldCount; i++)
                                    {
                                        var value = sqliteReader.GetValue(i);

                                        if (value is DBNull)
                                        {
                                            csv.WriteField(string.Empty);
                                        }
                                        else if (value is byte[] blob)
                                        {
                                            csv.WriteField(Main.ConvertBlobToSha1(blob));
                                        }
                                        else if (value is int && (sqliteReader.GetName(i) == "ArchiveTimeStamp" || sqliteReader.GetName(i) == "UserDateLastUsed"))
                                        {
                                            // Convert integer timestamps to hexadecimal strings
                                            csv.WriteField(Main.ConvertTimestampToHex(value.ToString()));
                                        }
                                        else
                                        {
                                            csv.WriteField(value);
                                        }
                                    }
                                    csv.NextRecord();
                                }
                            }
                        }
                    }
                }
                LogBox.Append(XMLOutputTextBox, $"Data exported to CSV successfully: {csvFilePath}\n", log: true);
                return true;
            }
            catch (Exception ex)
            {
                // Suppress error logging for the first failure
                return false;
            }
        }

        private async Task<bool> AuditDownloadFileAsync(string url, string fileName)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(5); // Increase timeout to 5 minutes

                    var response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    string downloadDirectory = Main._settings.CdnAuditDirectory;
                    Directory.CreateDirectory(downloadDirectory);
                    string downloadPath = Path.Combine(downloadDirectory, fileName);

                    using (var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fileStream);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                LogBox.Append(XMLOutputTextBox, $"Error downloading file from {url}: {ex.Message}\n", log: true);
                return false;
            }
        }

        private List<string> failedDownloads = new List<string>();
        private List<string> sha1VerificationFails = new List<string>();

        private async Task<bool> DecryptSceneListAsync(string sceneListFilePath, string outputDirectory)
        {
            LogBox.Append(XMLOutputTextBox, $"Starting decryption of SceneList.xml... ", log: true);

            try
            {
                byte[] fileContent = await File.ReadAllBytesAsync(sceneListFilePath);
                BruteforceProcess proc = new BruteforceProcess(fileContent);
                byte[] decryptedContent = proc.StartBruteForce((int)CdnMode.RETAIL);

                if (decryptedContent != null)
                {
                    string outputPath = Path.Combine(outputDirectory, "Decrypted_SceneList.xml");
                    await File.WriteAllBytesAsync(outputPath, decryptedContent);

                    // Parse the decrypted XML
                    XDocument doc = XDocument.Load(new MemoryStream(decryptedContent));
                    var scenes = doc.Descendants("SCENE").Select(scene => new Scene
                    {
                        Url = $"{ContentServerURL}Scenes/{scene.Attribute("desc").Value}",
                        Sha1 = scene.Attribute("sha1").Value,
                        XElement = scene
                    }).ToList();
                    int sceneCount = scenes.Count;

                    LogBox.Append(XMLOutputTextBox, $"Decryption successful for SceneList file: {sceneListFilePath} with {sceneCount} SCENE items.\n", log: true);

                    int totalPaths = scenes.Count;
                    int downloadedCount = 0;
                    int failedCount = 0;

                    // Log total SDC paths
                    UpdateSDCDownloadStatus(totalPaths, downloadedCount, failedCount);

                    // Download SDC files concurrently
                    await DownloadFilesConcurrently(scenes, outputDirectory, 6, (scene, success) =>
                    {
                        if (success)
                        {
                            downloadedCount++;
                        }
                        else
                        {
                            failedCount++;
                            failedDownloads.Add(scene.Url);
                        }

                        UpdateSDCDownloadStatus(totalPaths, downloadedCount, failedCount);
                        // Pause for 20ms to allow status update
                        Task.Delay(20).Wait();
                    });

                    // Log the total number of download failures
                    LogBox.Append(XMLOutputTextBox, $"\nTotal download failures: {failedDownloads.Count}", log: true);

                    // Write download failures to log
                    string auditFailsLogPath = Path.Combine(outputDirectory, "AuditFails.log");
                    await File.AppendAllLinesAsync(auditFailsLogPath, failedDownloads.Select(url => $"Download Failure: {url}"));

                    // Now verify the SHA1 of downloaded SDC files
                    var successfulDownloads = scenes.Where(scene => !failedDownloads.Contains(scene.Url)).ToList();
                    int sha1VerifiedCount = 0;
                    int sha1FailedCount = 0;
                    int totalFilesToVerify = successfulDownloads.Count;

                    foreach (var scene in successfulDownloads)
                    {
                        bool sha1VerificationSuccess = await DecryptAndVerifySDC(scene.Url, outputDirectory, scene.Sha1);
                        if (sha1VerificationSuccess)
                        {
                            sha1VerifiedCount++;
                        }
                        else
                        {
                            sha1FailedCount++;
                            sha1VerificationFails.Add(scene.Url);
                        }
                        UpdateSha1Status(totalFilesToVerify, sha1VerifiedCount, sha1FailedCount);
                    }

                    // Log the total number of SHA1 verification failures
                    LogBox.Append(XMLOutputTextBox, $"\nTotal SHA1 verification failures: {sha1VerificationFails.Count}\n", log: true);

                    // Write SHA1 verification failures to log
                    await File.AppendAllLinesAsync(auditFailsLogPath, sha1VerificationFails.Select(url => $"SHA1 Verification Failure: {url}"));

                    // Remove failed downloads from the SceneList
                    foreach (var scene in scenes.Where(scene => failedDownloads.Contains(scene.Url)).ToList())
                    {
                        scene.XElement.Remove();
                    }

                    // Update incorrect SHA1 hashes in the SceneList
                    foreach (var scene in successfulDownloads.Where(scene => sha1VerificationFails.Contains(scene.Url)))
                    {
                        // Extract the path without the domain and protocol
                        Uri contentServerUri = new Uri(ContentServerURL);
                        string relativePath = scene.Url.Substring(contentServerUri.Scheme.Length + 3 + contentServerUri.Host.Length);

                        // Construct the full path to the decrypted SDC file with the "Decrypted_" prefix
                        string sdcFileName = "Decrypted_" + Path.GetFileName(relativePath);
                        string sdcFilePath = Path.Combine(outputDirectory, Path.GetDirectoryName(relativePath).TrimStart(Path.DirectorySeparatorChar), sdcFileName);

                        if (File.Exists(sdcFilePath))
                        {
                            byte[] decryptedSdcContent = await File.ReadAllBytesAsync(sdcFilePath);
                            using (SHA1 sha1 = SHA1.Create())
                            {
                                byte[] computedHash = sha1.ComputeHash(decryptedSdcContent);
                                string computedSha1 = BitConverter.ToString(computedHash).Replace("-", "");
                                scene.XElement.Attribute("sha1").Value = computedSha1;
                            }
                        }
                        else
                        {
                            string warningMessage = $"Warning: SDC file not found for SHA1 verification: {sdcFilePath}\n";
                            LogBox.Append(XMLOutputTextBox, warningMessage, log: true);
                            await File.AppendAllLinesAsync(auditFailsLogPath, new[] { warningMessage });
                        }
                    }

                    // Save the modified SceneList as Fixed_SceneList.xml
                    string fixedSceneListPath = Path.Combine(outputDirectory, "Fixed_SceneList.xml");
                    doc.Save(fixedSceneListPath);
                    LogBox.Append(XMLOutputTextBox, $"Fixed SceneList saved as: {fixedSceneListPath}\n", log: true);

                    // Call the method to count unique ObjectIndex values in the CSV file
                    await CountUniqueObjectIndexesAndGenerateUrls();

                    return true;
                }
                else
                {
                    LogBox.Append(XMLOutputTextBox, $"Failed to decrypt SceneList file: {sceneListFilePath}\n", log: true);
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogBox.Append(XMLOutputTextBox, $"Error decrypting SceneList file: {ex.Message}\n", log: true);
                return false;
            }
        }

        private async Task CountUniqueObjectIndexesAndGenerateUrls()
        {
            if (string.IsNullOrEmpty(Main.csvFilePath))
            {
                LogBox.Append(XMLOutputTextBox, "CSV file path is not set. Cannot count unique ObjectIndexes.\n", log: true);
                return;
            }

            try
            {
                var uniqueObjectIndexes = new HashSet<string>();
                var objectIdUrls = new Dictionary<string, (string url, string sha1)>();

                using (var reader = new StreamReader(Main.csvFilePath))
                using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                {
                    var records = csv.GetRecords<dynamic>().ToList();

                    for (int i = 1; i < records.Count; i++) // Skip header (i = 0)
                    {
                        var record = records[i];
                        string objectIndex = record.ObjectIndex;
                        string objectId = record.ObjectId;
                        string version = record.Version;
                        string sha1 = record.OdcSha1Digest;

                        if (!string.IsNullOrEmpty(objectIndex) && objectIndex != "0")
                        {
                            if (!uniqueObjectIndexes.Contains(objectIndex))
                            {
                                uniqueObjectIndexes.Add(objectIndex);

                                // Pad the version to 3 characters with leading zeros
                                string paddedVersion = version.PadLeft(3, '0');

                                // Build the new URL
                                string newUrl = $"{ContentServerURL}Objects/{objectId}/object";

                                if (paddedVersion != "000")
                                {
                                    newUrl += $"_T{paddedVersion}.odc";
                                }
                                else
                                {
                                    newUrl += ".odc";
                                }

                                objectIdUrls[objectIndex] = (newUrl, sha1);
                            }
                        }
                    }

                    LogBox.Append(XMLOutputTextBox, $"Scanning Catalogue.... Total UUIDs Found: {uniqueObjectIndexes.Count}\n", log: true);
                }

                string urlsFilePath = Path.Combine(Path.GetDirectoryName(Main.csvFilePath), "GeneratedUrls.txt");
                using (var writer = new StreamWriter(urlsFilePath))
                {
                    foreach (var urlInfo in objectIdUrls.Values)
                    {
                        await writer.WriteLineAsync($"{urlInfo.url},{urlInfo.sha1}");
                    }
                }

                LogBox.Append(XMLOutputTextBox, $"Total URLs generated: {objectIdUrls.Count}. URLs saved to {urlsFilePath}\n", log: true);

                // Start downloading the URLs
                await DownloadGeneratedUrls(urlsFilePath);
            }
            catch (Exception ex)
            {
                LogBox.Append(XMLOutputTextBox, $"Error counting unique ObjectIndexes in CSV file: {ex.Message}\n", log: true);
            }
        }


        private async Task DownloadGeneratedUrls(string urlsFilePath)
        {
            try
            {
                var urlLines = await File.ReadAllLinesAsync(urlsFilePath);
                int totalUrls = urlLines.Length;
                int downloadedCount = 0;
                int failedCount = 0;

                List<string> failedDownloads = new List<string>();
                List<(string url, string fileName, string expectedSha1)> successfulDownloads = new List<(string, string, string)>();
                string failedDownloadsFilePath = Path.Combine(Path.GetDirectoryName(urlsFilePath), "AuditFails.log");

                // Use a SemaphoreSlim to limit the number of concurrent downloads
                using (SemaphoreSlim concurrencySemaphore = new SemaphoreSlim(6))
                {
                    List<Task> downloadTasks = new List<Task>();

                    foreach (var line in urlLines)
                    {
                        await concurrencySemaphore.WaitAsync();

                        downloadTasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                var parts = line.Split(',');
                                var url = parts[0];
                                var expectedSha1 = parts[1];

                                // Extract path and file name
                                Uri uri = new Uri(url);
                                string path = uri.AbsolutePath.Substring(1); // Remove leading slash
                                string fileName = Path.GetFileName(path);
                                string directory = Path.GetDirectoryName(path);

                                // Create directory if it doesn't exist
                                string fullDirectoryPath = Path.Combine(Path.GetDirectoryName(urlsFilePath), directory);
                                Directory.CreateDirectory(fullDirectoryPath);

                                string decryptedFilePath = Path.Combine(fullDirectoryPath, "Decrypted_" + fileName);
                                if (File.Exists(decryptedFilePath))
                                {
                                    // Decrypted file exists, skip download and verify SHA1
                                    bool verificationSuccess = await VerifyFileSha1(decryptedFilePath, expectedSha1);
                                    if (verificationSuccess)
                                    {
                                        downloadedCount++;
                                    }
                                    else
                                    {
                                        failedCount++;
                                        failedDownloads.Add($"SHA1 Verification Failure: {url}");
                                    }
                                }
                                else
                                {
                                    bool success = await DownloadFileWithoutProgressAsync(url, Path.Combine(fullDirectoryPath, fileName));
                                    if (success)
                                    {
                                        successfulDownloads.Add((url, Path.Combine(fullDirectoryPath, fileName), expectedSha1));
                                    }
                                    else
                                    {
                                        failedCount++;
                                        failedDownloads.Add($"Download Failure: {url}");
                                    }
                                }

                                // Additional downloads for URLs containing "object"
                                if (url.Contains("object"))
                                {
                                    string baseObjectUrl = url.Replace("object", "{0}").Replace(".odc", ".png");
                                    string[] objectSizes = { "small", "large" };
                                    foreach (var size in objectSizes)
                                    {
                                        string sizeUrl = string.Format(baseObjectUrl, size);
                                        string sizeFileName = Path.GetFileName(sizeUrl);
                                        string sizeDecryptedFilePath = Path.Combine(fullDirectoryPath, "Decrypted_" + sizeFileName);

                                        if (File.Exists(sizeDecryptedFilePath))
                                        {
                                            bool verificationSuccess = await VerifyFileSha1(sizeDecryptedFilePath, expectedSha1);
                                            if (verificationSuccess)
                                            {
                                                downloadedCount++;
                                            }
                                            else
                                            {
                                                failedCount++;
                                                failedDownloads.Add($"SHA1 Verification Failure: {sizeUrl}");
                                            }
                                        }
                                        else
                                        {
                                            bool success = await DownloadFileWithoutProgressAsync(sizeUrl, Path.Combine(fullDirectoryPath, sizeFileName));
                                            if (!success)
                                            {
                                                failedCount++;
                                                failedDownloads.Add($"Download Failure: {sizeUrl}");
                                            }
                                        }
                                    }
                                }

                                UpdateODCDownloadStatus(totalUrls, downloadedCount, failedCount);
                            }
                            finally
                            {
                                concurrencySemaphore.Release();
                            }
                        }));
                    }

                    await Task.WhenAll(downloadTasks);
                }

                // Log failed downloads to AuditFails.log
                await File.AppendAllLinesAsync(failedDownloadsFilePath, failedDownloads);

                // Now verify the SHA1 of downloaded ODC files
                int sha1VerifiedCount = 0;
                int sha1FailedCount = 0;
                int totalFilesToVerify = successfulDownloads.Count;

                foreach (var download in successfulDownloads)
                {
                    bool sha1VerificationSuccess = await DecryptAndVerifyODC(download.url, download.fileName, download.expectedSha1);
                    if (sha1VerificationSuccess)
                    {
                        sha1VerifiedCount++;
                        downloadedCount++;
                    }
                    else
                    {
                        sha1FailedCount++;
                        failedDownloads.Add($"SHA1 Verification Failure: {download.url}");
                    }
                    UpdateSha1Status(totalFilesToVerify, sha1VerifiedCount, sha1FailedCount);
                }

                // Log SHA1 verification failures to AuditFails.log
                await File.AppendAllLinesAsync(failedDownloadsFilePath, failedDownloads);

                LogBox.Append(XMLOutputTextBox, $"\nDownload completed. Total URLs: {totalUrls}, Successful: {downloadedCount}, Failed: {failedCount}\n", log: true);
                LogBox.Append(XMLOutputTextBox, $"\nSHA1 verification completed. Total Files: {totalFilesToVerify}, Successful: {sha1VerifiedCount}, Failed: {sha1FailedCount}\n", log: true);
            }
            catch (Exception ex)
            {
                LogBox.Append(XMLOutputTextBox, $"Error downloading generated URLs: {ex.Message}\n", log: true);
            }
        }



        private async Task<bool> DecryptAndVerifyODC(string url, string odcFilePath, string expectedSha1)
        {
            try
            {
                byte[] fileContent = await File.ReadAllBytesAsync(odcFilePath);
                BruteforceProcess proc = new BruteforceProcess(fileContent);
                byte[] decryptedContent = proc.StartBruteForce((int)CdnMode.RETAIL);

                if (decryptedContent != null)
                {
                    string outputDirectory = Path.GetDirectoryName(odcFilePath);
                    string outputPath = Path.Combine(outputDirectory, "OBJECT.ODC");
                    await File.WriteAllBytesAsync(outputPath, decryptedContent);

                    // Delete the original input file
                    File.Delete(odcFilePath);

                    return await VerifyFileSha1(outputPath, expectedSha1);
                }
                else
                {
                    await File.AppendAllLinesAsync("AuditFails.log", new[] { $"Decryption Failure: {url}" });
                    return false;
                }
            }
            catch (Exception ex)
            {
                await File.AppendAllLinesAsync("AuditFails.log", new[] { $"Exception: {url} - {ex.Message}" });
                return false;
            }
        }

        private async Task<bool> DecryptAndVerifySDC(string url, string outputDirectory, string expectedSha1)
        {
            try
            {
                // Extract path and file name
                Uri uri = new Uri(url);
                string path = uri.AbsolutePath.Substring(1); // Remove leading slash
                string fileName = Path.GetFileName(path);
                string directory = Path.GetDirectoryName(path);

                string sdcFilePath = Path.Combine(outputDirectory, directory, fileName);

                byte[] fileContent = await File.ReadAllBytesAsync(sdcFilePath);
                BruteforceProcess proc = new BruteforceProcess(fileContent);
                byte[] decryptedContent = proc.StartBruteForce((int)CdnMode.RETAIL);

                if (decryptedContent != null)
                {
                    string outputPath = Path.Combine(outputDirectory, directory, "Decrypted_" + fileName);
                    await File.WriteAllBytesAsync(outputPath, decryptedContent);

                    return await VerifyFileSha1(outputPath, expectedSha1);
                }
                else
                {
                    await File.AppendAllLinesAsync("AuditFails.log", new[] { $"Decryption Failure: {url}" });
                    return false;
                }
            }
            catch (Exception ex)
            {
                await File.AppendAllLinesAsync("AuditFails.log", new[] { $"Exception: {url} - {ex.Message}" });
                return false;
            }
        }



        private void UpdateODCDownloadStatus(int total, int downloaded, int failed)
        {
            Dispatcher.Invoke(() =>
            {
                var lines = XMLOutputTextBox.Text.Split('\n').ToList();
                string status = $"Total ODC paths found: {total} - Downloaded {downloaded} of {total} - Fails {failed}";
                if (lines.Count > 1 && lines.Last().StartsWith("Total ODC paths found:"))
                {
                    lines[lines.Count - 1] = status;
                }
                else
                {
                    lines.Add(status);
                }
                XMLOutputTextBox.Text = string.Join("\n", lines);
                LogBox.Trim(XMLOutputTextBox);
                XMLOutputTextBox.ScrollToEnd();
            });
        }

        private void UpdateSDCDownloadStatus(int total, int downloaded, int failed)
        {
            Dispatcher.Invoke(() =>
            {
                var lines = XMLOutputTextBox.Text.Split('\n').ToList();
                string status = $"Total SDC paths found: {total} - Downloaded {downloaded} of {total} - Fails {failed}";
                if (lines.Count > 1 && lines.Last().StartsWith("Total SDC paths found:"))
                {
                    lines[lines.Count - 1] = status;
                }
                else
                {
                    lines.Add(status);
                }
                XMLOutputTextBox.Text = string.Join("\n", lines);
                LogBox.Trim(XMLOutputTextBox);
                XMLOutputTextBox.ScrollToEnd();
            });
        }

        private async Task<bool> VerifyFileSha1(string filePath, string expectedSha1)
        {
            try
            {
                byte[] fileContent = await File.ReadAllBytesAsync(filePath);
                using (SHA1 sha1 = SHA1.Create())
                {
                    byte[] computedHash = sha1.ComputeHash(fileContent);
                    string computedSha1 = BitConverter.ToString(computedHash).Replace("-", "");

                    return computedSha1.Equals(expectedSha1, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }


        private void UpdateSha1Status(int total, int verified, int failed)
        {
            Dispatcher.Invoke(() =>
            {
                var lines = XMLOutputTextBox.Text.Split('\n').ToList();
                string status = $"Total SHA1 checks: {total} - Verified {verified} of {total} - Fails {failed}";
                if (lines.Count > 1 && lines.Last().StartsWith("Total SHA1 checks:"))
                {
                    lines[lines.Count - 1] = status;
                }
                else
                {
                    lines.Add(status);
                }
                XMLOutputTextBox.Text = string.Join("\n", lines);
                LogBox.Trim(XMLOutputTextBox);
                XMLOutputTextBox.ScrollToEnd();
            });
        }

        private async Task<bool> DownloadFileWithoutProgressAsync(string url, string fileName)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(10); // Increase timeout to 10 minutes

                    var response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    using (var fileStream = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fileStream);
                    }
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }


        private async Task DownloadFilesConcurrently(List<Scene> sceneList, string outputDirectory, int maxConcurrentDownloads, Action<Scene, bool> onDownloadComplete)
        {
            using (SemaphoreSlim concurrencySemaphore = new SemaphoreSlim(maxConcurrentDownloads))
            {
                List<Task> downloadTasks = new List<Task>();

                foreach (var scene in sceneList)
                {
                    await concurrencySemaphore.WaitAsync();

                    downloadTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            // Extract path and file name
                            Uri uri = new Uri(scene.Url);
                            string path = uri.AbsolutePath.Substring(1); // Remove leading slash
                            string fileName = Path.GetFileName(path);
                            string directory = Path.GetDirectoryName(path);

                            // Create directory if it doesn't exist
                            string fullDirectoryPath = Path.Combine(outputDirectory, directory);
                            Directory.CreateDirectory(fullDirectoryPath);

                            string decryptedFilePath = Path.Combine(fullDirectoryPath, "Decrypted_" + fileName);
                            if (File.Exists(decryptedFilePath))
                            {
                                // Decrypted file exists, skip download and verify SHA1
                                bool verificationSuccess = await VerifyFileSha1(decryptedFilePath, scene.Sha1);
                                onDownloadComplete(scene, verificationSuccess);
                            }
                            else
                            {
                                // Download file
                                bool downloadSuccess = await DownloadSDCFileAsync(scene.Url, Path.Combine(fullDirectoryPath, fileName));
                                onDownloadComplete(scene, downloadSuccess);
                            }
                        }
                        catch
                        {
                            onDownloadComplete(scene, false);
                        }
                        finally
                        {
                            concurrencySemaphore.Release();
                        }
                    }));
                }

                await Task.WhenAll(downloadTasks);
            }
        }

        private async Task<bool> DownloadSDCFileAsync(string url, string fileName)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(10); // Increase timeout to 10 minutes

                    var response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    using (var fileStream = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fileStream);
                    }
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
