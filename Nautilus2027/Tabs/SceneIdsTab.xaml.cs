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
    // "SceneIDs" tab.
    public partial class SceneIdsTab : UserControl
    {
        private static MainWindow Main => MainWindow.Instance;

        public SceneIdsTab()
        {
            InitializeComponent();
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
                LogBox.Append(encryptedSceneIDTextBox, $"Error during encryption: {ex.Message}\n", log: true);
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
                LogBox.Append(DecryptedSceneIDTextBox, $"Error during decryption: {ex.Message}\n", log: true);
            }
        }


        internal void SceneIDTabItem_Drop(object sender, DragEventArgs e)
        {
            // Attached to nested elements and to the tab header too; handle each drop once.
            e.Handled = true;

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
            Main.LogDebugInfo("SceneID Decrypter: List Cleared");
        }

        // Handlers used in this tab's XAML that live elsewhere.
        private void ClearListHandler(object sender, RoutedEventArgs e) => Main.ClearListHandler(sender, e);
    }
}
