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
    // "TSS" tab.
    public partial class TssTab : UserControl
    {
        private static MainWindow Main => MainWindow.Instance;

        public TssTab()
        {
            InitializeComponent();
        }

        // Fill this tab's controls from the saved settings (called once at startup).
        internal void ApplySettings(AppSettings settings)
        {
            TSSURLtextbox.Text = settings.TSSURL;
            TSSeditorSavePathtextbox.Text = settings.TSSeditorSavePath;
        }

        private XElement loadedXmlData;

        private void TSSURLtextbox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Main._settings != null)
            {
                Main._settings.TSSURL = TSSURLtextbox.Text;
                SettingsManager.SaveSettings(Main._settings);
            }
        }

        private void TSSeditorSavePathtextbox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Main._settings != null)
            {
                Main._settings.TSSeditorSavePath = TSSeditorSavePathtextbox.Text;
                SettingsManager.SaveSettings(Main._settings);
            }
        }

        private void TSSeditorSavePathtextbox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Create a FolderBrowserDialog to allow the user to select a folder
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog();

            // Show the dialog and check if the user selected a folder
            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // Set the selected folder path in the TextBox
                TSSeditorSavePathtextbox.Text = folderDialog.SelectedPath;
            }
        }

        private void TSSURLtextbox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Create an OpenFileDialog to allow the user to select an XML file
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
                Title = "Select an XML file"
            };

            // Show the dialog and check if the user selected a file
            if (openFileDialog.ShowDialog() == true)
            {
                // Set the selected file path in the TextBox
                TSSURLtextbox.Text = openFileDialog.FileName;
            }
        }

        internal async void LoadTSSXMLButton_Click(object sender, RoutedEventArgs e)
        {
            string pathOrUrl = TSSURLtextbox.Text;  // Retrieve the path or URL from the textbox
            string xmlString = null;

            try
            {
                if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uriResult) &&
                    (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
                {
                    using (var client = new HttpClient())
                    {
                        var response = await client.GetAsync(pathOrUrl);
                        if (response.IsSuccessStatusCode)
                        {
                            xmlString = await response.Content.ReadAsStringAsync();
                        }
                        else
                        {
                            MessageBox.Show("Failed to load XML from URL: " + response.StatusCode);
                            return;
                        }
                    }
                }
                else
                {
                    if (File.Exists(pathOrUrl))
                    {
                        xmlString = File.ReadAllText(pathOrUrl);
                    }
                    else
                    {
                        MessageBox.Show("File does not exist: " + pathOrUrl);
                        return;
                    }
                }

                if (!string.IsNullOrEmpty(xmlString))
                {
                    loadedXmlData = XElement.Parse(xmlString);
                    GenerateXmlControls(loadedXmlData);

                    string exeLocation = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    string savePath = Path.Combine(exeLocation, "TemporaryTSS.xml");
                    File.WriteAllText(savePath, xmlString);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error processing XML: " + ex.Message);
            }
        }

        private void SaveNewTSSXMLButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {

                if (loadedXmlData == null)
                {
                    MessageBox.Show("No TSS loaded, cannot save.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                // Get the directory path from the textbox and append "tss" subfolder
                string originalDirectoryPath = TSSeditorSavePathtextbox.Text;
                string directoryPath = Path.Combine(originalDirectoryPath, "tss");

                if (string.IsNullOrWhiteSpace(directoryPath))
                {
                    MessageBox.Show("Please specify a valid directory path.");
                    return;
                }

                // Ensure the directory exists
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                string defaultFileName = "coreHztFmpQrx0002_en-US.xml";
                string baseFileName = Path.GetFileNameWithoutExtension(defaultFileName);
                string fileExtension = Path.GetExtension(defaultFileName);
                string basePath = Path.Combine(directoryPath, defaultFileName);

                // Update the VERSION field with the current date and time
                var versionElement = loadedXmlData.Descendants("VERSION").FirstOrDefault();
                if (versionElement != null)
                {
                    versionElement.Value = DateTime.Now.ToString("MM/dd/yyyy hh:mm:ss tt").ToUpper();
                }

                if (CheckBoxTSSAllregions.IsChecked == true)
                {
                    string[] regionCodes = {
                // List of region codes
                "en-GB", "fr-FR", "it-IT", "de-DE", "es-ES", "ja-JP", "ko-KR", "zh-TW", "zh-HK", "en-SG", "en-ID", "en-MY", "en-TH",
"af-ZA", "ar-AE", "ar-BH", "ar-DZ", "ar-EG", "ar-IQ", "ar-JO", "ar-KW", "ar-LB", "ar-LY", "ar-MA", "ar-OM", "ar-QA",
"ar-SA", "ar-SY", "ar-TN", "ar-YE", "az-AZ", "be-BY", "bg-BG", "bs-BA", "ca-ES", "cs-CZ", "cy-GB", "da-DK", "de-AT",
"de-CH", "de-LI", "de-LU", "dv-MV", "el-GR", "en-AU", "en-BZ", "en-CA", "en-CB", "en-IE", "en-JM", "en-NZ", "en-PH",
"en-TT", "en-ZA", "en-ZW", "es-AR", "es-BO", "es-CL", "es-CO", "es-CR", "es-DO", "es-EC", "es-GT", "es-HN", "es-MX",
"es-NI", "es-PA", "es-PE", "es-PR", "es-PY", "es-SV", "es-UY", "es-VE", "et-EE", "eu-ES", "fa-IR", "fi-FI", "fo-FO",
"fr-BE", "fr-CA", "fr-CH", "fr-LU", "fr-MC", "gl-ES", "gu-IN", "he-IL", "hi-IN", "hr-BA", "hr-HR", "hu-HU", "hy-AM",
"id-ID", "is-IS", "it-CH", "ka-GE", "kk-KZ", "kn-IN", "ky-KG", "lt-LT", "lv-LV", "mi-NZ", "mk-MK", "mn-MN", "mr-IN",
"ms-BN", "ms-MY", "mt-MT", "nb-NO", "nl-BE", "nl-NL", "nn-NO", "ns-ZA", "pa-IN", "pl-PL", "ps-AR", "pt-BR", "pt-PT",
"qu-BO", "qu-EC", "qu-PE", "ro-RO", "ru-RU", "sa-IN", "se-FI", "se-NO", "se-SE", "sk-SK", "sl-SI", "sq-AL", "sr-BA",
"sr-SP", "sv-FI", "sv-SE", "sw-KE", "ta-IN", "te-IN", "th-TH", "tl-PH", "tn-ZA", "tr-TR", "tt-RU", "uk-UA", "ur-PK",
"uz-UZ", "vi-VN", "xh-ZA", "zh-CN", "zh-MO", "zh-SG", "en-DK", "en-FI", "en-NO", "no-NO", "en-SE", "en-AE", "en-CZ",
"en-SA", "en-PL", "en-GR", "en-HK", "en-TW", "en-TR", "en-RO", "en-QA", "en-HU", "en-IS", "en-BG", "en-HR", "en-US",
"en-DK", "en-FI", "en-NO", "no-NO", "en-SE", "en-AE", "en-CZ", "en-SA", "en-PL", "en-GR", "en-HK", "en-TW", "en-TR",
"en-RO", "en-QA", "en-HU", "en-IS", "en-BG", "en-HR", "en-DZ", "en-AF", "en-AL", "en-AD", "en-AO", "en-AG", "en-AR",
"en-AM", "en-AT", "en-AZ", "en-BD", "en-BB", "en-BJ", "en-BT", "en-CV", "en-CF", "en-TD", "en-CG", "en-CD", "en-DJ",
"en-DM", "en-GQ", "en-ER", "en-GA", "en-GM", "en-GN", "en-GW", "en-GY", "en-HT", "en-KI", "en-LS", "en-LR", "en-MG",
"en-MW", "en-MV", "en-ML", "en-MR", "en-MU", "en-MC", "en-ME", "en-MZ", "en-MM", "en-NA", "en-NR", "en-NP", "en-NE",
"en-PW", "en-PA", "en-PG", "en-ST", "en-SM", "en-SN", "en-RS", "en-SC", "en-SL", "en-SB", "en-LK", "en-SD", "en-SR",
"en-SZ", "en-SY", "en-TJ", "en-TZ", "en-TL", "en-TG", "en-TO", "en-TM", "en-TV", "en-UG", "en-VU"

            };

                    baseFileName = baseFileName.Replace("_en-US", "");

                    foreach (var code in regionCodes)
                    {
                        string regionalFileName = $"{baseFileName}_{code}{fileExtension}";
                        string fullRegionalPath = Path.Combine(directoryPath, regionalFileName);
                        loadedXmlData.Save(fullRegionalPath);
                    }

                    // Set the visibility of the warning text block to collapsed
                    TextBlock tssSaveWarningTextblock = (TextBlock)FindName("TSSsaveWarningTextblock");
                    if (tssSaveWarningTextblock != null)
                    {
                        tssSaveWarningTextblock.Visibility = Visibility.Collapsed;
                    }

                    LoadTSSXMLButton_Click(sender, e);
                }
                else
                {
                    loadedXmlData.Save(basePath);

                    // Set the visibility of the warning text block to collapsed
                    TextBlock tssSaveWarningTextblock = (TextBlock)FindName("TSSsaveWarningTextblock");
                    if (tssSaveWarningTextblock != null)
                    {
                        tssSaveWarningTextblock.Visibility = Visibility.Collapsed;
                    }

                    LoadTSSXMLButton_Click(sender, e);
                }

                // Extra stuff if DeployHCDBFlag is true
                if (MainWindow.DeployHCDBFlag)
                {
                    string hcdbOutputDirectory = Main._settings.HcdbOutputDirectory;
                    string databaseEditsPath = Path.Combine(hcdbOutputDirectory, "Database_Edits");
                    string[] filesToMove = {
                "ObjectCatalogue_5_SCEAsia.hcdb",
                "ObjectCatalogue_5_SCEJ.hcdb",
                "ObjectCatalogue_5_SCEA.hcdb",
                "ObjectCatalogue_5_SCEE.hcdb"
            };

                    string targetDirectoryPath = Path.Combine(TSSeditorSavePathtextbox.Text, "Objects");

                    // Ensure the target directory exists
                    if (!Directory.Exists(targetDirectoryPath))
                    {
                        Directory.CreateDirectory(targetDirectoryPath);
                    }

                    foreach (var fileName in filesToMove)
                    {
                        string sourceFilePath = Path.Combine(databaseEditsPath, fileName);
                        string targetFilePath = Path.Combine(targetDirectoryPath, fileName);

                        if (File.Exists(sourceFilePath))
                        {
                            File.Copy(sourceFilePath, targetFilePath, true);
                            File.Delete(sourceFilePath);
                        }
                    }

                    // Reset the flag
                    MainWindow.DeployHCDBFlag = false;

                    // UI updates using Dispatcher.Invoke
                    Dispatcher.Invoke(() =>
                    {
                        // Find the LatestHCDBSHA1panel and set its visibility to Collapsed
                        var latestHCDBSHA1panel = (StackPanel)FindName("LatestHCDBSHA1panel");
                        if (latestHCDBSHA1panel != null)
                        {
                            latestHCDBSHA1panel.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            MessageBox.Show("LatestHCDBSHA1panel not found!");
                        }

                        // Clear the LatestHCDBSHA1textbox
                        var latestHCDBSHA1textbox = (TextBox)FindName("LatestHCDBSHA1textbox");
                        if (latestHCDBSHA1textbox != null)
                        {
                            latestHCDBSHA1textbox.Clear();
                        }
                        else
                        {
                            MessageBox.Show("LatestHCDBSHA1textbox not found!");
                        }

                       
                    });
                }

                // Extra stuff if DeploySceneListFlag is true
                if (MainWindow.DeploySceneListFlag)
                {
                    string sourceSceneListPath = Path.Combine(Main._settings.SceneListDeployDirectory, "SceneList.xml");
                    string targetSceneListPath = Path.Combine(TSSeditorSavePathtextbox.Text, "Environments", "SceneList.xml");

                    // Ensure the target directory exists
                    string targetSceneListDirectory = Path.GetDirectoryName(targetSceneListPath);
                    if (!Directory.Exists(targetSceneListDirectory))
                    {
                        Directory.CreateDirectory(targetSceneListDirectory);
                    }

                    if (File.Exists(sourceSceneListPath))
                    {
                        File.Move(sourceSceneListPath, targetSceneListPath, true);
                        
                    }
                    // Find the LatestSceneListSHA1panel and set its visibility to Collapsed
                    var latestSceneListSHA1panel = (StackPanel)FindName("LatestSceneListSHA1panel");
                    if (latestSceneListSHA1panel != null)
                    {
                        latestSceneListSHA1panel.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        MessageBox.Show("LatestSceneListSHA1panel not found!");
                    }

                    // Clear the LatestSceneListSHA1textbox
                    var latestSceneListSHA1textbox = (TextBox)FindName("LatestSceneListSHA1textbox");
                    if (latestSceneListSHA1textbox != null)
                    {
                        latestSceneListSHA1textbox.Clear();
                    }
                    else
                    {
                        MessageBox.Show("LatestSceneListSHA1textbox not found!");
                    }
                    MainWindow.DeploySceneListFlag = false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save XML data: " + ex.Message);
            }
        }




        private void GenerateXmlControls(XElement xmlElement)
        {
            TSSXMLControlsPanel.Children.Clear();

            // Handle specific tags
            var versionElement = xmlElement.Element("VERSION");
            if (versionElement != null) { GenerateVersionPanel(versionElement); }
            GenerateSHA1Section(xmlElement);
            var connectionElement = xmlElement.Element("connection");
            if (connectionElement != null) { GenerateConnectionPanel(connectionElement); }

            GenerateSecureRootUrlsPanel(xmlElement);

            var dnsOverrides = xmlElement.Elements("DNSOverride");
            if (dnsOverrides.Any()) { GenerateDNSOverridePanel(xmlElement); }

            // New elements handling
            GenerateAdminObjectIdPanel(xmlElement);
            GenerateMaxServiceIdsPanel(xmlElement);
            GenerateDisableBarPanel(xmlElement);
            GenerateSecureCommercePointsPanel(xmlElement);
            GenerateUseRegionalServiceIdsPanel(xmlElement);
            GenerateHttpCompressionControl(xmlElement);

            var objectsElement = xmlElement.Element("objects");
            if (objectsElement != null) { GenerateObjectsPanel(objectsElement); }

           

            var ssfwElement = xmlElement.Element("ssfw");
            if (ssfwElement != null) { GenerateSSFWPanel(ssfwElement); }

            var profanityFilterElement = xmlElement.Element("profanityfilter");
            if (profanityFilterElement != null) { GenerateProfanityFilterPanel(profanityFilterElement); }

            var dataCaptureElement = xmlElement.Element("datacapture");
            if (dataCaptureElement != null) { GenerateDataCapturePanel(dataCaptureElement); }

            GenerateSceneRedirectPanel(xmlElement);

            var ageRestrictionsElement = xmlElement.Element("agerestrictions");
            if (ageRestrictionsElement != null) { GenerateAgeRestrictionsPanel(ageRestrictionsElement); }

            var globalElement = xmlElement.Element("global");
            if (globalElement != null) { GenerateGlobalPanel(globalElement); }

            // Handle REGIONINFO and its sub-elements
            var regionInfoElement = xmlElement.Element("REGIONINFO");
            if (regionInfoElement != null)
            {
                GenerateInstanceTypesPanel(regionInfoElement.Element("INSTANCE_TYPES"));
                GenerateRegionTypesPanel(regionInfoElement.Element("REGION_TYPES"));
                GenerateRegionMapPanel(regionInfoElement.Element("REGION_MAP"));
                GenerateLocalisationsPanel(regionInfoElement.Element("LOCALISATIONS"));
            }

            // Define a set of element names to exclude from normal XML element processing
            var excludeElements = new HashSet<string> {
        "SHA1", "datacapture", "VERSION", "objects", "profanityfilter", "SecureContentRoot",
        "ScreenContentRoot", "secure_lua_object_resources_root", "secure_lua_scene_resources_root",
        "SceneRedirect", "ssfw", "disablebar", "useregionalserviceids", "http_compression", "secure_commerce_points", "commerce", "connection", "REGIONINFO", "DNSOverride", "global", "agerestrictions",
        "AdminObjectId", "maxserviceids"
    };

            // Process other elements
            GenerateXmlElementControls(xmlElement, TSSXMLControlsPanel, excludeElements);
        }

        private void GenerateHttpCompressionControl(XElement xmlElement)
        {
            // Create or retrieve the http_compression element
            var httpCompressionElement = xmlElement.Element("http_compression");

            // Main vertical panel to hold each setting on a new line
            StackPanel mainPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 5, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Panel for HTTP Compression and its radio buttons
            StackPanel compressionPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };

            compressionPanel.Children.Add(new TextBlock
            {
                Text = "Enable HTTP Compression: ",
                FontSize = 14,
                Width = 180,
                Margin = new Thickness(20, 0, 5, 0),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });

            // Add the "True" label and radio button
            compressionPanel.Children.Add(new TextBlock
            {
                Text = "True:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });

            RadioButton trueButton = new RadioButton
            {
                IsChecked = (httpCompressionElement?.Attribute("encodings")?.Value == "gzip"),
                Margin = new Thickness(5, 0, 20, 0),
                GroupName = "http_compression",
                Style = (Style)FindResource("ModernRadioButtonStyle"),
                Tag = httpCompressionElement
            };
            trueButton.Checked += (sender, args) =>
            {
                if (trueButton.Tag is XElement element)
                {
                    // Remove existing http_compression element if it exists
                    var existingElement = xmlElement.Element("http_compression");
                    if (existingElement != null)
                    {
                        existingElement.Remove();
                    }

                    // Add a new http_compression element
                    XElement newElement = new XElement("http_compression");
                    newElement.SetAttributeValue("encodings", "gzip");
                    xmlElement.Add(newElement);
                }
            };
            compressionPanel.Children.Add(trueButton);

            // Add the "False" label and radio button
            compressionPanel.Children.Add(new TextBlock
            {
                Text = "False:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });

            RadioButton falseButton = new RadioButton
            {
                IsChecked = (httpCompressionElement == null || httpCompressionElement.Attribute("encodings") == null),
                Margin = new Thickness(5, 0, 0, 0),
                GroupName = "http_compression",
                Style = (Style)FindResource("ModernRadioButtonStyle"),
                Tag = httpCompressionElement
            };
            falseButton.Checked += (sender, args) =>
            {
                if (falseButton.Tag is XElement element)
                {
                    // Remove the http_compression element if it exists
                    var existingElement = xmlElement.Element("http_compression");
                    if (existingElement != null)
                    {
                        existingElement.Remove();
                    }
                }
            };
            compressionPanel.Children.Add(falseButton);

            // Add the compression panel to the main panel
            mainPanel.Children.Add(compressionPanel);

            // Add the main panel to the controls panel
            TSSXMLControlsPanel.Children.Add(mainPanel);
        }

        private void GenerateAdminObjectIdPanel(XElement xmlElement)
        {
            var adminObjectIdElement = xmlElement.Element("AdminObjectId");
            if (adminObjectIdElement != null)
            {
                // Main vertical panel to hold each setting on a new line
                StackPanel mainPanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(0, 15, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                // Panel for "Other Settings" label
                StackPanel otherSettingsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center
                };
                otherSettingsPanel.Children.Add(new TextBlock
                {
                    Text = "Other Settings",
                    FontSize = 16,
                    Foreground = new SolidColorBrush(Colors.White),
                    Margin = new Thickness(0, 0, 5, 7),
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                });

                // Adding the "Other Settings" panel to the main panel
                mainPanel.Children.Add(otherSettingsPanel);

                // Panel for "Admin IGA UUID" and its textbox
                StackPanel adminIdPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                adminIdPanel.Children.Add(new TextBlock
                {
                    Text = "In-Game-Admin UUID: ",
                    FontSize = 14,
                    Width = 180,
                    Margin = new Thickness(20, 0, 0, 7),
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White),
                    VerticalAlignment = VerticalAlignment.Center
                });

                var adminObjectIdTextBox = new TextBox
                {
                    Text = adminObjectIdElement.Value,
                    FontSize = 14,
                    Width = 300,
                    Style = (Style)FindResource("SmallTextBoxStyle"),
                    Margin = new Thickness(0, 0, 10, 7),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = adminObjectIdElement
                };
                adminObjectIdTextBox.TextChanged += TSSTextBox_TextChanged;
                adminIdPanel.Children.Add(adminObjectIdTextBox);

                // Adding the Admin ID panel to the main panel
                mainPanel.Children.Add(adminIdPanel);

                // Add the main panel to the controls panel
                TSSXMLControlsPanel.Children.Add(mainPanel);
            }
        }

        private void GenerateMaxServiceIdsPanel(XElement xmlElement)
        {
            var maxServiceIdsElement = xmlElement.Element("maxserviceids");
            if (maxServiceIdsElement != null)
            {
                StackPanel maxServiceIdsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                maxServiceIdsPanel.Children.Add(new TextBlock
                {
                    Text = "Max Service IDs: ",
                    FontSize = 14,
                    Width = 180,
                    Margin = new Thickness(20, 0, 0, 0),
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White),
                    VerticalAlignment = VerticalAlignment.Center
                });

                var maxServiceIdsTextBox = new TextBox
                {
                    Text = maxServiceIdsElement.Value,
                    FontSize = 14,
                    Width = 40,
                    Style = (Style)FindResource("SmallTextBoxStyle"),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = maxServiceIdsElement
                };
                maxServiceIdsTextBox.TextChanged += TSSTextBox_TextChanged;
                maxServiceIdsPanel.Children.Add(maxServiceIdsTextBox);

                TSSXMLControlsPanel.Children.Add(maxServiceIdsPanel);
            }
        }




        private void GenerateRegionMapPanel(XElement xmlElement)
        {
            // Main vertical panel to hold the header and items
            StackPanel mainPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 7, 0, 7),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Header for the region map
            TextBlock header = new TextBlock
            {
                Text = "Region Map",
                FontSize = 16,
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 7)
            };
            mainPanel.Children.Add(header);

            // WrapPanel for map items
            WrapPanel regionMapPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Width = 1340, // Set the maximum width for the WrapPanel (adjust as needed)
                Margin = new Thickness(0)
            };

            var mapElements = xmlElement.Elements("MAP");
            foreach (var map in mapElements)
            {
                // Panel for each map item
                StackPanel mapPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 435 // Set a fixed width for each item to fit three items per row
                };

                // Code label and textbox
                mapPanel.Children.Add(new TextBlock
                {
                    Text = "Code: ",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Colors.White),
                    Margin = new Thickness(20, 0, 5, 0),
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var codeTextBox = new TextBox
                {
                    Text = map.Attribute("code")?.Value ?? "Not found",
                    FontSize = 14,
                    Width = 70,
                    Style = (Style)FindResource("SmallTextBoxStyle"),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = map.Attribute("code")
                };
                codeTextBox.TextChanged += TSSTextBox_TextChanged;
                mapPanel.Children.Add(codeTextBox);

                // Location label and textbox
                mapPanel.Children.Add(new TextBlock
                {
                    Text = "Location: ",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Colors.White),
                    Margin = new Thickness(0, 0, 5, 0),
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var locTextBox = new TextBox
                {
                    Text = map.Attribute("loc")?.Value ?? "Not found",
                    FontSize = 14,
                    Width = 30,
                    Style = (Style)FindResource("SmallTextBoxStyle"),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = map.Attribute("loc")
                };
                locTextBox.TextChanged += TSSTextBox_TextChanged;
                mapPanel.Children.Add(locTextBox);

                // Region label and textbox
                mapPanel.Children.Add(new TextBlock
                {
                    Text = "Map: ",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Colors.White),
                    Margin = new Thickness(0, 0, 5, 0),
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var mapTextBox = new TextBox
                {
                    Text = map.Value,
                    FontSize = 14,
                    Width = 120,
                    Style = (Style)FindResource("SmallTextBoxStyle"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = map
                };
                mapTextBox.TextChanged += TSSTextBox_TextChanged;
                mapPanel.Children.Add(mapTextBox);

                // Add the mapPanel to the WrapPanel
                regionMapPanel.Children.Add(mapPanel);
            }

            // Add the WrapPanel to the main panel
            mainPanel.Children.Add(regionMapPanel);

            // Add the main panel to the controls panel
            TSSXMLControlsPanel.Children.Add(mainPanel);
        }

        // Event handler for all TextBox changes
        private void TSSTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            if (textBox.Tag is XAttribute attribute)
            {
                attribute.Value = textBox.Text;
            }
            else if (textBox.Tag is XElement element)
            {
                element.Value = textBox.Text;
            }
        }


        private void GenerateLocalisationsPanel(XElement xmlElement)
        {
            // Main vertical panel to hold header and items
            StackPanel mainPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 7, 0, 7),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Header for the localisations
            TextBlock header = new TextBlock
            {
                Text = "Localisations",
                FontSize = 16,
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 7)
            };
            mainPanel.Children.Add(header);

            // WrapPanel for localisations items
            WrapPanel localisationsPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Width = 1300, // Set the maximum width for the WrapPanel
                Margin = new Thickness(0)
            };

            var refElements = xmlElement.Elements("REF");
            foreach (var refEl in refElements)
            {
                // Panel for each reference item
                StackPanel refPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 320 // Set a fixed width for each item to fit four items per row
                };

                // Language label and textbox
                refPanel.Children.Add(new TextBlock
                {
                    Text = "Language: ",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Colors.White),
                    Margin = new Thickness(10, 0, 5, 0),
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var languageTextBox = new TextBox
                {
                    Text = refEl.Attribute("language")?.Value ?? "Not found",
                    FontSize = 14,
                    Width = 60,
                    Style = (Style)FindResource("SmallTextBoxStyle"),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = refEl.Attribute("language")
                };
                languageTextBox.TextChanged += TSSTextBox_TextChanged;
                refPanel.Children.Add(languageTextBox);

                // Reference label and textbox
                refPanel.Children.Add(new TextBlock
                {
                    Text = "References: ",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Colors.White),
                    Margin = new Thickness(0, 0, 5, 0),
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var refTextBox = new TextBox
                {
                    Text = refEl.Value,
                    FontSize = 14,
                    Width = 60,
                    Style = (Style)FindResource("SmallTextBoxStyle"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = refEl
                };
                refTextBox.TextChanged += TSSTextBox_TextChanged;
                refPanel.Children.Add(refTextBox);

                // Add the refPanel to the WrapPanel
                localisationsPanel.Children.Add(refPanel);
            }

            // Add the WrapPanel to the main panel
            mainPanel.Children.Add(localisationsPanel);

            // Add the main panel to the controls panel
            TSSXMLControlsPanel.Children.Add(mainPanel);
        }


        private void GenerateInstanceTypesPanel(XElement xmlElement)
        {
            // Main vertical panel to hold the header and items
            StackPanel mainPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 7, 0, 7),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Header for the instance types
            TextBlock header = new TextBlock
            {
                Text = "Instance Types",
                FontSize = 16,
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 7)
            };
            mainPanel.Children.Add(header);

            // WrapPanel for instance type items
            WrapPanel instancePanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 1300 // Adjust as needed to fit the layout
            };

            foreach (var type in xmlElement.Elements("TYPE"))
            {
                StackPanel typePanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(2),
                    VerticalAlignment = VerticalAlignment.Center
                };

                // Name label and textbox
                typePanel.Children.Add(new TextBlock
                {
                    Text = "Name: ",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Colors.White),
                    Margin = new Thickness(20, 0, 5, 0),
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var nameTextBox = new TextBox
                {
                    Text = type.Attribute("name")?.Value ?? "Not found",
                    FontSize = 14,
                    Width = 60,
                    Style = (Style)FindResource("SmallTextBoxStyle"),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = type.Attribute("name")
                };
                nameTextBox.TextChanged += TSSTextBox_TextChanged;
                typePanel.Children.Add(nameTextBox);

                instancePanel.Children.Add(typePanel);
            }

            // Add the WrapPanel to the main panel
            mainPanel.Children.Add(instancePanel);

            // Add the main panel to the controls panel
            TSSXMLControlsPanel.Children.Add(mainPanel);
        }




        private void GenerateRegionTypesPanel(XElement xmlElement)
        {
            // Main vertical panel to hold the header and items
            StackPanel mainPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 10, 0, 10),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Header for the region types panel
            TextBlock header = new TextBlock
            {
                Text = "Region Types",
                FontSize = 16,
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 7)
            };
            mainPanel.Children.Add(header);

            // WrapPanel for region type items
            WrapPanel regionTypePanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 1330 // Set max width to control wrapping
            };

            foreach (var type in xmlElement.Elements("TYPE"))
            {
                StackPanel typePanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(2),
                    VerticalAlignment = VerticalAlignment.Center
                };

                // Name label and textbox
                typePanel.Children.Add(new TextBlock
                {
                    Text = "Name: ",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Colors.White),
                    Margin = new Thickness(15, 0, 5, 0),
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var nameTextBox = new TextBox
                {
                    Text = type.Attribute("name")?.Value ?? "Not found",
                    FontSize = 14,
                    Width = 120,
                    Style = (Style)FindResource("SmallTextBoxStyle"),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = type.Attribute("name")
                };
                nameTextBox.TextChanged += TSSTextBox_TextChanged;
                typePanel.Children.Add(nameTextBox);

                // Territory ComboBox
                typePanel.Children.Add(new TextBlock
                {
                    Text = "Territory: ",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Colors.White),
                    Margin = new Thickness(0, 0, 5, 0),
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var territoryComboBox = new ComboBox
                {
                    Width = 80,
                    Height = 22,
                    Style = (Style)FindResource("DarkModeComboBoxStyle"),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ItemsSource = new[] { "SCEA", "SCEE", "SCEJ", "SCEAsia" }
                };
                territoryComboBox.SelectedIndex = 0; // Default to first item
                typePanel.Children.Add(territoryComboBox);

                // Instance ComboBox
                typePanel.Children.Add(new TextBlock
                {
                    Text = "Instance: ",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Colors.White),
                    Margin = new Thickness(0, 0, 5, 0),
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var instanceComboBox = new ComboBox
                {
                    Width = 80,
                    Height = 22,
                    Style = (Style)FindResource("DarkModeComboBoxStyle"),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ItemsSource = new[] { "EU", "US", "Japan", "Asia" }
                };
                instanceComboBox.SelectedIndex = 0; // Default to first item
                typePanel.Children.Add(instanceComboBox);

                // Type label and textbox
                typePanel.Children.Add(new TextBlock
                {
                    Text = "Type: ",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Colors.White),
                    Margin = new Thickness(0, 0, 5, 0),
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var typeTextBox = new TextBox
                {
                    Text = type.Value,
                    FontSize = 14,
                    Width = 50,
                    Margin = new Thickness(0, 0, 50, 0),
                    Style = (Style)FindResource("SmallTextBoxStyle"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = type
                };
                typeTextBox.TextChanged += TSSTextBox_TextChanged;
                typePanel.Children.Add(typeTextBox);

                // Add the panel for this type to the WrapPanel
                regionTypePanel.Children.Add(typePanel);
            }

            // Add the WrapPanel to the main panel
            mainPanel.Children.Add(regionTypePanel);

            // Add the main panel to the controls panel
            TSSXMLControlsPanel.Children.Add(mainPanel);
        }


        private void GenerateAgeRestrictionsPanel(XElement ageRestrictionsElement)
        {
            // Main vertical panel to hold the header and items
            StackPanel mainPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Header for the age restrictions
            TextBlock header = new TextBlock
            {
                Text = "Age Restrictions",
                FontSize = 16,
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 7)
            };
            mainPanel.Children.Add(header);

            // WrapPanel for age restriction items
            WrapPanel ageRestrictionsPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 1300 // Adjust as needed to fit the layout
            };

            foreach (var ageElement in ageRestrictionsElement.Elements("age"))
            {
                StackPanel agePanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(2),
                    VerticalAlignment = VerticalAlignment.Center
                };

                TextBlock regionLabel = new TextBlock
                {
                    Text = "Region: " + ageElement.Attribute("region")?.Value + " ",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Colors.White),
                    Margin = new Thickness(20, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                TextBox ageTextBox = new TextBox
                {
                    Text = ageElement.Value,
                    Width = 30,
                    Style = (Style)FindResource("SmallTextBoxStyle"),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = ageElement  // Linking TextBox to the XElement
                };
                ageTextBox.TextChanged += TSSTextBox_TextChanged; // Event handler to update XML
                agePanel.Children.Add(regionLabel);
                agePanel.Children.Add(ageTextBox);

                ageRestrictionsPanel.Children.Add(agePanel);
            }

            // Add the WrapPanel to the main panel
            mainPanel.Children.Add(ageRestrictionsPanel);

            // Add the main panel to the controls panel
            TSSXMLControlsPanel.Children.Add(mainPanel);
        }


        private void GenerateGlobalPanel(XElement globalElement)
        {
            // Main vertical panel to hold the header and items
            StackPanel mainPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Header for the global panel
            TextBlock header = new TextBlock
            {
                Text = "Global",
                FontSize = 16,
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            mainPanel.Children.Add(header);

            // WrapPanel for mode items
            WrapPanel globalPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 1330 // Adjust as needed for layout
            };

            foreach (var modeElement in globalElement.Elements("mode"))
            {
                StackPanel modePanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(2),
                    VerticalAlignment = VerticalAlignment.Center
                };

                // Mode value label and text
                TextBlock modeLabel = new TextBlock
                {
                    Text = "Mode: " + modeElement.Value,
                    FontSize = 14,
                    Width = 70,
                    Foreground = new SolidColorBrush(Colors.White),
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(35, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                modePanel.Children.Add(modeLabel);

                // Attributes (regions)
                foreach (var attribute in modeElement.Attributes())
                {
                    TextBlock regionLabel = new TextBlock
                    {
                        Text = attribute.Name.LocalName + ": ",
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Colors.White),
                        Margin = new Thickness(0, 0, 5, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    TextBox regionTextBox = new TextBox
                    {
                        Text = attribute.Value,
                        Width = 25,
                        Style = (Style)FindResource("SmallTextBoxStyle"),
                        Margin = new Thickness(0, 0, 10, 0),
                        Tag = attribute  // Linking TextBox to the XAttribute
                    };
                    regionTextBox.TextChanged += TSSTextBox_TextChanged; // Event handler to update XML
                    modePanel.Children.Add(regionLabel);
                    modePanel.Children.Add(regionTextBox);
                }

                globalPanel.Children.Add(modePanel);
            }

            // Add the WrapPanel to the main panel
            mainPanel.Children.Add(globalPanel);

            // Add the main panel to the controls panel
            TSSXMLControlsPanel.Children.Add(mainPanel);
        }


        private void GenerateDNSOverridePanel(XElement xmlElement)
        {
            StackPanel dnsOverridePanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 10, 0, 10),
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock header = new TextBlock
            {
                Text = "DNS Overrides",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(0, 0, 0, 10)
            };
            dnsOverridePanel.Children.Add(header);

            StackPanel entriesPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center
            };

            foreach (var dnsOverride in xmlElement.Elements("DNSOverride"))
            {
                entriesPanel.Children.Add(CreateDNSOverrideEntry(dnsOverride, entriesPanel));
            }

            dnsOverridePanel.Children.Add(entriesPanel);

            Button addButton = new Button
            {
                Content = "Add",
                Width = 40,
                FontSize = 10,
                Foreground = new SolidColorBrush(Colors.Lime),
                FontWeight = FontWeights.Bold,
                Style = (Style)FindResource("DarkModeButtonStyle2"),
                Height = 21,
                Margin = new Thickness(25, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            addButton.Click += (sender, args) => AddNewDNSOverride(xmlElement, entriesPanel);
            dnsOverridePanel.Children.Add(addButton);

            TSSXMLControlsPanel.Children.Add(dnsOverridePanel);
        }

        private StackPanel CreateDNSOverrideEntry(XElement dnsOverride, Panel entriesPanel)
        {
            StackPanel entryPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center
            };

            // DNS IP textbox
            var dnsIpTextBox = new TextBox
            {
                Text = dnsOverride.Value ?? "Enter DNS IP",
                FontSize = 14,
                Width = 110,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)FindResource("SmallTextBoxStyle")
            };
            dnsIpTextBox.TextChanged += (sender, args) =>
            {
                dnsOverride.Value = dnsIpTextBox.Text;  // Update the XML when the textbox text changes
            };
            entryPanel.Children.Add(new TextBlock
            {
                Text = "Primary DNS IP: ",
                FontSize = 14,
                Margin = new Thickness(20, 0, 0, 0),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });
            entryPanel.Children.Add(dnsIpTextBox);

            AddLabelAndComboBox(entryPanel, "Action:", new List<string> { "allow", "deny" }, dnsOverride.Attribute("action")?.Value, dnsOverride, "action");
            AddLabelAndComboBox(entryPanel, "Report:", new List<string> { "on", "off" }, dnsOverride.Attribute("report")?.Value, dnsOverride, "report");

            // Delete button
            Button deleteButton = new Button
            {
                Content = "Delete",
                Width = 50,
                Height = 22,
                Margin = new Thickness(5, 0, 0, 0),
                FontSize = 10,
                Foreground = new SolidColorBrush(Colors.Red),
                FontWeight = FontWeights.Bold,
                Style = (Style)FindResource("DarkModeButtonStyle2")
            };
            deleteButton.Click += (sender, args) =>
            {
                // Remove the element completely from the XML
                dnsOverride.Remove();

                // Remove from GUI
                entriesPanel.Children.Remove(entryPanel);
            };

            entryPanel.Children.Add(deleteButton);

            return entryPanel;
        }



        private void AddNewDNSOverride(XElement parentElement, Panel entriesPanel)
        {
            XElement newDnsOverride = new XElement("DNSOverride",
                new XAttribute("action", "allow"),
                new XAttribute("report", "on"),
                "");

            // Find the last DNSOverride in the XML
            var lastDnsOverride = parentElement.Elements("DNSOverride").LastOrDefault();

            if (lastDnsOverride != null)
            {
                // Add the new element directly after the last existing DNSOverride element
                lastDnsOverride.AddAfterSelf(newDnsOverride);
            }
            else
            {
                // If no DNSOverride elements exist, just add to the parent element
                parentElement.Add(newDnsOverride);
            }

            // Add to GUI
            var dnsOverrideEntry = CreateDNSOverrideEntry(newDnsOverride, entriesPanel);
            entriesPanel.Children.Add(dnsOverrideEntry);  // Add new entry to the GUI
        }

        private void AddLabelAndComboBox(StackPanel panel, string labelText, List<string> options, string currentValue, XElement element, string attributeName)
        {
            panel.Children.Add(new TextBlock
            {
                Text = labelText,
                FontSize = 14,
                Margin = new Thickness(0, 0, 5, 0),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });

            ComboBox comboBox = new ComboBox
            {
                Width = 60,
                Height = 24,
                Style = (Style)FindResource("DarkModeComboBoxStyle"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Add ComboBoxItems with uppercase labels for display and store the actual value as lowercase in the Tag
            foreach (string option in options)
            {
                ComboBoxItem item = new ComboBoxItem
                {
                    Content = option.ToUpper(),  // Uppercase display
                    Tag = option.ToLower()       // Lowercase value for XML update
                };
                comboBox.Items.Add(item);
                // Set selected item based on current value, ensuring comparison is case-insensitive
                if (!string.IsNullOrEmpty(currentValue) && currentValue.ToLower() == option.ToLower())
                {
                    comboBox.SelectedItem = item;
                }
            }

            if (comboBox.SelectedItem == null && comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0; // Default to first item if no match found
            }

            comboBox.SelectionChanged += (sender, args) =>
            {
                if (comboBox.SelectedItem is ComboBoxItem selected)
                {
                    // Update the XML attribute to the lowercase value stored in Tag
                    element.SetAttributeValue(attributeName, selected.Tag.ToString());
                }
            };

            panel.Children.Add(comboBox);
        }

        private void GenerateConnectionPanel(XElement xmlElement)
        {
            StackPanel connectionPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 10, 0, 10),
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock header = new TextBlock
            {
                Text = "Connection",
                FontSize = 16,
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            connectionPanel.Children.Add(header);

            // Process the connection details
            var muisElement = xmlElement.Element("muis");
            var contentServerElement = xmlElement.Element("contentserver");

            // MUIS IP
            AddLabelAndTextbox(connectionPanel, "MUIS IP:", muisElement?.Value ?? "Not found", 165, 120, muisElement);

            // Content Decryption Key
            // Correct use for Content Decryption Key
            AddLabelAndTextbox(connectionPanel, "Content Decryption Key:", contentServerElement?.Attribute("key")?.Value ?? "Not found", 165, 400, contentServerElement.Attribute("key"));


            // Content Server URL
            AddLabelAndTextbox(connectionPanel, "Content Server URL:", contentServerElement?.Value ?? "Not found", 165, 400, contentServerElement);

            TSSXMLControlsPanel.Children.Add(connectionPanel);
        }


        private void GenerateSecureRootUrlsPanel(XElement xmlElement)
        {
            StackPanel secureUrlsPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 10, 0, 10),
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock header = new TextBlock
            {
                Text = "Secure Root URLs",
                FontSize = 16,
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            secureUrlsPanel.Children.Add(header);

            // List of URL elements to process
            var urlElements = new List<string>
    {
        "SecureContentRoot",
        "ScreenContentRoot",
        "secure_lua_object_resources_root",
        "secure_lua_scene_resources_root"
    };

            // Process each element and create a label and textbox
            foreach (var elementName in urlElements)
            {
                StackPanel urlPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                TextBlock label = new TextBlock
                {
                    Text = $"{elementName}:",
                    FontSize = 14,
                    Margin = new Thickness(20, 0, 5, 0),
                    Width = 225,
                    Foreground = new SolidColorBrush(Colors.White),
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                urlPanel.Children.Add(label);

                var urlElement = xmlElement.Element(elementName);
                TextBox textBox = new TextBox
                {
                    Width = 400,
                    Style = (Style)FindResource("SmallTextBoxStyle"),
                    Margin = new Thickness(2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Text = urlElement != null ? urlElement.Value : "Not found",
                    Tag = urlElement  // Linking TextBox to the XElement
                };
                textBox.TextChanged += TSSTextBox_TextChanged;  // Shared event handler to update XML
                urlPanel.Children.Add(textBox);
                secureUrlsPanel.Children.Add(urlPanel);
            }

            TSSXMLControlsPanel.Children.Add(secureUrlsPanel);
        }


        private void GenerateVersionPanel(XElement versionElement)
        {
            StackPanel versionPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0,12,0,0),
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock label = new TextBlock
            {
                Text = "TSS Date:",
                FontSize = 16,
                Width = 80,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBox textBox = new TextBox
            {
                Text = versionElement.Value,
                Margin = new Thickness(2),
                FontSize = 15,
                Style = (Style)FindResource("SmallTextBoxStyle"),
                Width = 200,
                Tag = versionElement  // Linking TextBox to the XElement
            };

            textBox.TextChanged += TSSTextBox_TextChanged;  // Using a shared event handler

            versionPanel.Children.Add(label);
            versionPanel.Children.Add(textBox);
            TSSXMLControlsPanel.Children.Add(versionPanel);
        }


        private void GenerateSHA1Section(XElement xmlElement)
        {
            StackPanel sha1Panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(2)
            };

            TextBlock sha1Header = new TextBlock
            {
                Text = "SHA1 Encrypted Files",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 15, 0, 10)
            };
            sha1Panel.Children.Add(sha1Header);

            StackPanel entriesPanel = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            var sha1Elements = xmlElement.Descendants("SHA1").ToList();
            foreach (var sha1Element in sha1Elements)
            {
                entriesPanel.Children.Add(CreateSHA1Panel(sha1Element));
            }

            sha1Panel.Children.Add(entriesPanel);

            Button addButton = new Button
            {
                Content = "Add",
                Width = 40,
                Height = 21,
                FontSize = 10,
                Foreground = new SolidColorBrush(Colors.Lime),
                Style = (Style)FindResource("DarkModeButtonStyle2"),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(20, 10, 0, 2),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            addButton.Click += (sender, args) => AddNewSHA1Entry(xmlElement, entriesPanel);
            sha1Panel.Children.Add(addButton);

            TSSXMLControlsPanel.Children.Add(sha1Panel);
        }

        private void AddNewSHA1Entry(XElement parentElement, Panel entriesPanel)
        {
            XElement newSha1 = new XElement("SHA1",
                new XAttribute("file", ""),
                new XAttribute("digest", ""));

            // Find the last SHA1 element in the parent XML element
            var lastSha1Element = parentElement.Elements("SHA1").LastOrDefault();

            if (lastSha1Element != null)
            {
                // If there is at least one SHA1 element, add the new one after the last one
                lastSha1Element.AddAfterSelf(newSha1);
            }
            else
            {
                // If there are no SHA1 elements, just add the new one to the parent
                parentElement.Add(newSha1);
            }

            var newSha1Panel = CreateSHA1Panel(newSha1);
            entriesPanel.Children.Add(newSha1Panel);
        }


        private StackPanel CreateSHA1Panel(XElement sha1Element)
        {
            StackPanel panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0)
            };

            // Reuse the existing GenerateSHA1Panel logic here
            GenerateSHA1Panel(sha1Element, panel);

            // Add a Delete button
            Button deleteButton = new Button
            {
                Content = "Delete",
                Width = 50,
                FontSize = 10,
                Foreground = new SolidColorBrush(Colors.Red),
                Style = (Style)FindResource("DarkModeButtonStyle2"),
                Height = 22,
                Margin = new Thickness(5, 0, 0, 0)
            };
            deleteButton.Click += (sender, args) =>
            {
                // Remove the element completely from the XML
                sha1Element.Remove();
                // Remove from GUI
                ((Panel)panel.Parent).Children.Remove(panel);
            };
            panel.Children.Add(deleteButton);

            return panel;
        }


        private void GenerateSHA1Panel(XElement sha1Element, StackPanel panel)
        {
            TextBlock fileLabel = new TextBlock
            {
                Text = "File Path:",
                FontSize = 14,
                Width = 60,
                Margin = new Thickness(20, 0, 5, 0),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBox fileTextBox = new TextBox
            {
                Text = sha1Element.Attribute("file").Value,
                Margin = new Thickness(2),
                Style = (Style)FindResource("SmallTextBoxStyle"),
                Width = 300,
                Tag = sha1Element.Attribute("file")  // Linking TextBox to the file attribute
            };
            fileTextBox.TextChanged += TextBox_TSSAttributeChanged;  // Generalized event handler

            Button downloadButton = new Button
            {
                Content = "Download",
                Width = 70,
                FontSize = 10,
                Style = (Style)FindResource("DarkModeButtonStyle2"),
                Height = 22,
                Margin = new Thickness(5, 0, 0, 0)
            };
            downloadButton.Click += (sender, args) => DownloadFileAction(fileTextBox.Text);

            TextBlock digestLabel = new TextBlock
            {
                Text = "SHA1 Digest:",
                FontSize = 14,
                Width = 90,
                Margin = new Thickness(20, 0, 5, 0),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBox digestTextBox = new TextBox
            {
                Text = sha1Element.Attribute("digest").Value,
                Margin = new Thickness(0),
                Style = (Style)FindResource("SmallTextBoxStyle"),
                Width = 400,
                Tag = sha1Element.Attribute("digest")  // Linking TextBox to the digest attribute
            };
            digestTextBox.TextChanged += TextBox_TSSAttributeChanged;  // Generalized event handler

            Button copyButton = new Button
            {
                Content = "Copy SHA1",
                Width = 75,
                FontSize = 10,
                Style = (Style)FindResource("DarkModeButtonStyle2"),
                Height = 22,
                Margin = new Thickness(10, 0, 0, 0)
            };
            copyButton.Click += (sender, args) =>
            {
                Clipboard.SetText(digestTextBox.Text);
                MessageBox.Show("SHA1 Digest copied to clipboard!");
            };

            panel.Children.Add(fileLabel);
            panel.Children.Add(fileTextBox);
            panel.Children.Add(downloadButton);
            panel.Children.Add(digestLabel);
            panel.Children.Add(digestTextBox);
            panel.Children.Add(copyButton);
        }

        // Generalized event handler for attribute changes
        // Generalized event handler for attribute changes
        private void TextBox_TSSAttributeChanged(object sender, TextChangedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            if (textBox != null)
            {
                // Change the text color to yellow
                textBox.Foreground = new SolidColorBrush(Colors.Yellow);

                // Update the attribute value if the TextBox's Tag is an XAttribute
                if (textBox.Tag is XAttribute attribute)
                {
                    attribute.Value = textBox.Text;
                }

                // Set the visibility of the warning text block to visible
                TextBlock tssSaveWarningTextblock = (TextBlock)FindName("TSSsaveWarningTextblock");
                if (tssSaveWarningTextblock != null)
                {
                    tssSaveWarningTextblock.Visibility = Visibility.Visible;
                }
            }
        }



            private async void DownloadFileAction(string filePath)
        {
            TextBox tssUrlTextBox = (TextBox)FindName("TSSURLtextbox");
            if (tssUrlTextBox != null)
            {
                Uri baseUri = new Uri(tssUrlTextBox.Text);
                string baseUrl = baseUri.GetLeftPart(UriPartial.Authority); // Gets the base URL up to the first '/'
                string fullUrl = baseUrl + "/" + filePath.TrimStart('/');

                // Download the file
                try
                {
                    using (var client = new HttpClient())
                    {
                        var response = await client.GetAsync(fullUrl);
                        if (response.IsSuccessStatusCode)
                        {
                            var data = await response.Content.ReadAsByteArrayAsync();

                            Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
                            saveFileDialog.FileName = Path.GetFileName(filePath); // Default file name
                            saveFileDialog.DefaultExt = Path.GetExtension(filePath); // Default file extension
                            saveFileDialog.Filter = "All files (*.*)|*.*"; // Filter files by extension

                            if (saveFileDialog.ShowDialog() == true)
                            {
                                File.WriteAllBytes(saveFileDialog.FileName, data);
                                MessageBox.Show("File downloaded successfully!");
                            }
                        }
                        else
                        {
                            MessageBox.Show("Failed to download file: " + response.StatusCode);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error downloading file: " + ex.Message);
                }
            }
            else
            {
                MessageBox.Show("TSS URL Text Box not found");
            }
        }

        private void GenerateObjectsPanel(XElement objectsElement)
        {
            StackPanel objectsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 15),
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock label = new TextBlock
            {
                Text = "Object Catalogue Format:",
                FontSize = 14,
                Width = 180,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(20, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            objectsPanel.Children.Add(label);

            // Dictionary to map the GUI strings to XML tags
            Dictionary<string, string> options = new Dictionary<string, string>
    {
        {"HCDB", "prepared_database"},
        {"XML", "hierarchical_layout"}
    };

            foreach (var option in options)
            {
                StackPanel radioButtonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 0)
                };

                TextBlock radioButtonLabel = new TextBlock
                {
                    Text = option.Key + ":",
                    FontSize = 12,
                    Width = 50,
                    Margin = new Thickness(0, 0, 0, 0),
                    Foreground = new SolidColorBrush(Colors.White),
                    VerticalAlignment = VerticalAlignment.Center
                };
                radioButtonPanel.Children.Add(radioButtonLabel);

                RadioButton radioButton = new RadioButton
                {
                    GroupName = "ObjectFormat",
                    Width = 48,
                    Margin = new Thickness(-25, 0, 25, 0),
                    Style = (Style)FindResource("ModernRadioButtonStyle"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                radioButtonPanel.Children.Add(radioButton);

                // Set the current selection based on the XML content
                if (objectsElement.Elements(option.Value).Any())
                {
                    radioButton.IsChecked = true;
                }

                radioButton.Checked += (sender, args) =>
                {
                    objectsElement.RemoveAll(); // Clear existing elements
                    objectsElement.Add(new XElement(option.Value)); // Add the new element based on the selected radio button
                };

                objectsPanel.Children.Add(radioButtonPanel);
            }

            TSSXMLControlsPanel.Children.Add(objectsPanel);
        }



        private void GenerateDisableBarPanel(XElement xmlElement)
        {
            StackPanel disableBarPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            disableBarPanel.Children.Add(new TextBlock
            {
                Text = "Home Archive Support:",
                FontSize = 14,
                Width = 180,
                Margin = new Thickness(20, 0, 5, 0),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });

            // Radio button for "Sharc Only"
            StackPanel sharcOnlyPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 0)
            };

            TextBlock sharcOnlyLabel = new TextBlock
            {
                Text = "Sharc Only:",
                FontSize = 12,
                Width = 60,
                Margin = new Thickness(0, 0, 3, 0),
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            sharcOnlyPanel.Children.Add(sharcOnlyLabel);

            RadioButton sharcOnlyButton = new RadioButton
            {
                GroupName = "ArchiveSecurity",
                Width = 48,
                Margin = new Thickness(-15, 0, 5, 0),
                Style = (Style)FindResource("ModernRadioButtonStyle"),
                VerticalAlignment = VerticalAlignment.Center
            };
            sharcOnlyButton.IsChecked = xmlElement.Elements("disablebar").Any() && !xmlElement.Nodes().OfType<XComment>().Any(c => c.Value.Contains("disablebar"));
            sharcOnlyPanel.Children.Add(sharcOnlyButton);

            // Radio button for "Allow BAR/Non-Sharc SDAT"
            StackPanel allowBarPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(15, 0, 0, 0)
            };

            TextBlock allowBarLabel = new TextBlock
            {
                Text = "All Types:",
                FontSize = 12,
                Width = 60,
                Margin = new Thickness(-15, 0, 0, 0),
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            allowBarPanel.Children.Add(allowBarLabel);

            RadioButton allowBarButton = new RadioButton
            {
                GroupName = "ArchiveSecurity",
                Width = 50,
                Margin = new Thickness(-15, 0, 20, 0),
                Style = (Style)FindResource("ModernRadioButtonStyle"),
                VerticalAlignment = VerticalAlignment.Center
            };
            allowBarButton.IsChecked = !sharcOnlyButton.IsChecked.Value;
            allowBarPanel.Children.Add(allowBarButton);

            // Event handling for radio button changes
            sharcOnlyButton.Checked += (sender, args) =>
            {
                var connectionElement = xmlElement.Element("connection");
                var disableBar = connectionElement.ElementsAfterSelf("disablebar").FirstOrDefault();
                var disableBarComment = connectionElement.NodesAfterSelf().OfType<XComment>().FirstOrDefault(c => c.Value.Contains("<disablebar />"));

                if (disableBar == null && disableBarComment == null)
                {
                    connectionElement.AddAfterSelf(new XElement("disablebar"));
                }
                else if (disableBarComment != null)
                {
                    disableBarComment.ReplaceWith(new XElement("disablebar"));
                }
            };

            allowBarButton.Checked += (sender, args) =>
            {
                var disableBar = xmlElement.Elements("disablebar").FirstOrDefault();
                if (disableBar != null)
                {
                    disableBar.ReplaceWith(new XComment(disableBar.ToString()));
                }
            };

            // Add panels to the main panel

            disableBarPanel.Children.Add(allowBarPanel);
            disableBarPanel.Children.Add(sharcOnlyPanel);

            TSSXMLControlsPanel.Children.Add(disableBarPanel);
        }

        private void GenerateSecureCommercePointsPanel(XElement xmlElement)
        {
            var commerceElement = xmlElement.Element("commerce");
            if (commerceElement == null)
            {
                commerceElement = new XElement("commerce");
                xmlElement.Add(commerceElement);
            }

            StackPanel commercePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            commercePanel.Children.Add(new TextBlock
            {
                Text = "Secure Commerce Points:",
                FontSize = 14,
                Width = 180,
                Margin = new Thickness(20, 0, 5, 0),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });

            // Radio button for "True"
            StackPanel truePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 0)
            };

            TextBlock trueLabel = new TextBlock
            {
                Text = "True:",
                FontSize = 12,
                Width = 50,
                Margin = new Thickness(0, 0, 0, 0),

                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            truePanel.Children.Add(trueLabel);

            RadioButton trueButton = new RadioButton
            {
                GroupName = "SecureCommerce",
                Width = 48,
                Margin = new Thickness(-25, 0, 15, 0),
                Style = (Style)FindResource("ModernRadioButtonStyle"),
                VerticalAlignment = VerticalAlignment.Center
            };
            truePanel.Children.Add(trueButton);

            // Radio button for "False"
            StackPanel falsePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(15, 0, 0, 0)
            };

            TextBlock falseLabel = new TextBlock
            {
                Text = "False:",
                FontSize = 12,
                Width = 40,
                Margin = new Thickness(-5, 0, 0, 0),

                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            falsePanel.Children.Add(falseLabel);

            RadioButton falseButton = new RadioButton
            {
                GroupName = "SecureCommerce",
                Width = 50,
                Margin = new Thickness(-15, 0, 15, 0),
                Style = (Style)FindResource("ModernRadioButtonStyle"),
                VerticalAlignment = VerticalAlignment.Center
            };
            falsePanel.Children.Add(falseButton);

            // Initial states for radio buttons
            var securePoints = commerceElement.Element("secure_commerce_points");
            var securePointsComment = commerceElement.Nodes().OfType<XComment>().FirstOrDefault(c => c.Value.Contains("<secure_commerce_points />"));

            trueButton.IsChecked = (securePoints != null);
            falseButton.IsChecked = !trueButton.IsChecked.Value;

            // Event handling for radio button changes
            trueButton.Checked += (sender, args) =>
            {
                var currentElement = commerceElement.Element("secure_commerce_points");
                var currentComment = commerceElement.Nodes().OfType<XComment>().FirstOrDefault(c => c.Value.Trim() == "<secure_commerce_points />");

                if (currentElement == null && currentComment != null)
                {
                    currentComment.ReplaceWith(new XElement("secure_commerce_points"));
                }
                else if (currentElement == null)
                {
                    commerceElement.Add(new XElement("secure_commerce_points"));
                }
            };


            falseButton.Checked += (sender, args) =>
            {
                var currentElement = commerceElement.Element("secure_commerce_points");
                if (currentElement != null)
                {
                    currentElement.ReplaceWith(new XComment(currentElement.ToString()));
                }
            };

            // Add panels to the main panel
            commercePanel.Children.Add(truePanel);
            commercePanel.Children.Add(falsePanel);

            TSSXMLControlsPanel.Children.Add(commercePanel);
        }


        private void GenerateUseRegionalServiceIdsPanel(XElement xmlElement)
        {
            StackPanel serviceIdsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            serviceIdsPanel.Children.Add(new TextBlock
            {
                Text = "Use Regional Service IDs:",
                FontSize = 14,
                Width = 180,
                Margin = new Thickness(20, 0, 5, 0),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });

            // Radio button for "True"
            StackPanel truePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 0)
            };

            TextBlock trueLabel = new TextBlock
            {
                Text = "True:",
                FontSize = 12,
                Width = 50,
                Margin = new Thickness(0, 0, 0, 0),
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            truePanel.Children.Add(trueLabel);

            RadioButton trueButton = new RadioButton
            {
                GroupName = "RegionalService",
                Width = 48,
                Margin = new Thickness(-25, 0, 15, 0),
                Style = (Style)FindResource("ModernRadioButtonStyle"),
                VerticalAlignment = VerticalAlignment.Center
            };
            truePanel.Children.Add(trueButton);

            // Radio button for "False"
            StackPanel falsePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(15, 0, 0, 0)
            };

            TextBlock falseLabel = new TextBlock
            {
                Text = "False:",
                FontSize = 12,
                Width = 40,
                Margin = new Thickness(-5, 0, 0, 0),
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            falsePanel.Children.Add(falseLabel);

            RadioButton falseButton = new RadioButton
            {
                GroupName = "RegionalService",
                Width = 50,
                Margin = new Thickness(-15, 0, 15, 0),
                Style = (Style)FindResource("ModernRadioButtonStyle"),
                VerticalAlignment = VerticalAlignment.Center
            };
            falsePanel.Children.Add(falseButton);

            // Initial state setup and event handling
            var regionalServiceNode = xmlElement.Element("useregionalserviceids");
            var regionalServiceComment = xmlElement.Nodes().OfType<XComment>().FirstOrDefault(c => c.Value.Contains("<useregionalserviceids />"));

            trueButton.IsChecked = (regionalServiceNode != null);
            falseButton.IsChecked = !trueButton.IsChecked.Value;

            trueButton.Checked += (sender, args) =>
            {
                var currentElement = xmlElement.Element("useregionalserviceids");
                var currentComment = xmlElement.Nodes().OfType<XComment>().FirstOrDefault(c => c.Value.Trim() == "<useregionalserviceids />");

                if (currentElement == null && currentComment != null)
                {
                    currentComment.ReplaceWith(new XElement("useregionalserviceids"));
                }
                else if (currentElement == null)
                {
                    xmlElement.Add(new XElement("useregionalserviceids"));
                }
            };


            falseButton.Checked += (sender, args) =>
            {
                var currentElement = xmlElement.Element("useregionalserviceids");
                if (currentElement != null)
                {
                    currentElement.ReplaceWith(new XComment(currentElement.ToString()));
                }
            };

            // Add panels to the main panel
            serviceIdsPanel.Children.Add(truePanel);
            serviceIdsPanel.Children.Add(falsePanel);

            TSSXMLControlsPanel.Children.Add(serviceIdsPanel);
        }

        private void GenerateXmlElementControls(XElement element, Panel parentPanel, HashSet<string> excludeElements)
        {
            foreach (var subElement in element.Elements().Where(x => !excludeElements.Contains(x.Name.LocalName)))
            {
                StackPanel panel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(2),
                    VerticalAlignment = VerticalAlignment.Center
                };

                TextBlock label = new TextBlock
                {
                    Text = $"{subElement.Name.LocalName}:",
                    FontSize = 14,
                    Width = 150,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White),
                    VerticalAlignment = VerticalAlignment.Center
                };
                panel.Children.Add(label);

                TextBox textBox = new TextBox
                {
                    Text = subElement.Value,
                    Margin = new Thickness(2),
                    Width = 300,
                    Tag = subElement // Store the reference to the XElement
                };

                textBox.TextChanged += (sender, args) =>
                {
                    var tb = sender as TextBox;
                    if (tb != null)
                    {
                        XElement linkedElement = tb.Tag as XElement;
                        if (linkedElement != null)
                        {
                            linkedElement.Value = tb.Text;
                        }
                    }
                };

                panel.Children.Add(textBox);
                parentPanel.Children.Add(panel);
            }
        }


        private void GenerateSceneRedirectPanel(XElement xmlElement)
        {
            StackPanel sceneRedirectPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 10, 0, 10),
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock header = new TextBlock
            {
                Text = "Scene Redirects",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(0, 0, 0, 10)
            };
            sceneRedirectPanel.Children.Add(header);

            StackPanel entriesPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center
            };

            foreach (var redirect in xmlElement.Elements("SceneRedirect"))
            {
                entriesPanel.Children.Add(CreateSceneRedirectEntry(redirect, entriesPanel, xmlElement));
            }

            sceneRedirectPanel.Children.Add(entriesPanel);

            // Add button to create new scene redirects
            Button addButton = new Button
            {
                Content = "Add",
                Width = 35,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.Lime),
                Style = (Style)FindResource("DarkModeButtonStyle2"),
                Height = 20,
                Margin = new Thickness(20, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            addButton.Click += (sender, args) => AddNewSceneRedirect(xmlElement, entriesPanel);
            sceneRedirectPanel.Children.Add(addButton);

            TSSXMLControlsPanel.Children.Add(sceneRedirectPanel);
        }

        private StackPanel CreateSceneRedirectEntry(XElement redirect, Panel entriesPanel, XElement xmlElement)
        {
            StackPanel redirectPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };

            AddLabelAndTextbox(redirectPanel, "Original Destination: ", redirect.Attribute("src")?.Value ?? "", 140, 250, redirect.Attribute("src"));
            AddLabelAndTextbox(redirectPanel, "Redirect To: ", redirect.Attribute("dest")?.Value ?? "", 80, 250, redirect.Attribute("dest"));

            // Region as a ComboBox instead of a textbox
            AddLabelAndSceneRedirectComboBox(redirectPanel, "Region: ", new List<string> { "SCEA", "SCEE", "SCEJ", "SCEAsia" }, redirect.Attribute("region")?.Value, redirect, "region");

            // Delete button to remove scene redirects
            Button deleteButton = new Button
            {
                Content = "Delete",
                Width = 50,
                Height = 21,
                Foreground = new SolidColorBrush(Colors.Red),
                Margin = new Thickness(0),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Style = (Style)FindResource("DarkModeButtonStyle2")
            };
            deleteButton.Click += (sender, args) =>
            {
                // Remove element from XML and GUI
                redirect.Remove();
                entriesPanel.Children.Remove(redirectPanel);
            };
            redirectPanel.Children.Add(deleteButton);

            return redirectPanel;
        }

        private void AddLabelAndSceneRedirectComboBox(StackPanel panel, string labelText, List<string> options, string currentValue, XElement element, string attributeName)
        {
            panel.Children.Add(new TextBlock
            {
                Text = labelText,
                FontSize = 14,
                Margin = new Thickness(0, 0, 5, 0),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });

            ComboBox comboBox = new ComboBox
            {
                Width = 75,
                Height = 24,
                Style = (Style)FindResource("DarkModeComboBoxStyle"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            options.ForEach(option => comboBox.Items.Add(option));
            comboBox.SelectedItem = currentValue ?? options.First();
            comboBox.SelectionChanged += (sender, args) =>
            {
                if (comboBox.SelectedItem != null)
                {
                    element.SetAttributeValue(attributeName, comboBox.SelectedItem.ToString());
                }
            };

            panel.Children.Add(comboBox);
        }

        private void AddNewSceneRedirect(XElement parentElement, Panel entriesPanel)
        {
            XElement newRedirect = new XElement("SceneRedirect",
                new XAttribute("src", ""),
                new XAttribute("dest", ""),
                new XAttribute("region", ""));

            // Find the last SceneRedirect element in the parent XML element
            var lastRedirectElement = parentElement.Elements("SceneRedirect").LastOrDefault();

            if (lastRedirectElement != null)
            {
                // If there is at least one SceneRedirect element, add the new one after the last one
                lastRedirectElement.AddAfterSelf(newRedirect);
            }
            else
            {
                // If there are no SceneRedirect elements, just add the new one to the parent
                parentElement.Add(newRedirect);
            }

            var redirectEntry = CreateSceneRedirectEntry(newRedirect, entriesPanel, parentElement);
            entriesPanel.Children.Add(redirectEntry);  // Add new entry to the GUI
        }



        private void AddLabelAndTextbox(StackPanel parentPanel, string labelText, string textBoxText, int labelWidth, int textBoxWidth, XObject xObject = null)
        {
            StackPanel linePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock label = new TextBlock
            {
                Text = labelText,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Width = labelWidth,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20, 0, 5, 0)
            };
            linePanel.Children.Add(label);

            TextBox textBox = new TextBox
            {
                Text = textBoxText,
                Style = (Style)FindResource("SmallTextBoxStyle"),
                Width = textBoxWidth,
                Margin = new Thickness(5, 0, 5, 0),
                Tag = xObject  // Can be null
            };

            // Only attach event handler if xObject is not null
            if (xObject != null)
            {
                textBox.TextChanged += (sender, args) =>
                {
                    var tb = sender as TextBox;
                    if (tb != null && tb.Tag is XAttribute attribute)
                    {
                        attribute.Value = tb.Text;
                    }
                    else if (tb != null && tb.Tag is XElement element)
                    {
                        element.Value = tb.Text;
                    }
                };
            }

            linePanel.Children.Add(textBox);
            parentPanel.Children.Add(linePanel);
        }





        private void GenerateProfanityFilterPanel(XElement profanityFilterElement)
        {
            StackPanel profanityFilterPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 10, 0, 10),
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock header = new TextBlock
            {
                Text = "Profanity Filter",
                FontSize = 16,
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            };

            profanityFilterPanel.Children.Add(header);

            // Generate controls for each attribute
            GenerateAttributeControl(profanityFilterElement, "apiKey", "API Key:", profanityFilterPanel);
            GenerateBooleanAttributeControl(profanityFilterElement, "forceOffline", "Force Offline:", profanityFilterPanel);
            GenerateAttributeControl(profanityFilterElement, "privateKey", "Private Key:", profanityFilterPanel);
            GenerateAttributeControl(profanityFilterElement, "updaterOverrideUrl", "Updater Override URL:", profanityFilterPanel);

            TSSXMLControlsPanel.Children.Add(profanityFilterPanel);
        }

        private void GenerateAttributeControl(XElement element, string attributeName, string labelContent, Panel parentPanel)
        {
            StackPanel attributePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock label = new TextBlock
            {
                Text = labelContent,
                FontSize = 14,
                Margin = new Thickness(20, 0, 5, 0),
                Width = 160,
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Retrieve the attribute and use it directly in the TextBox
            XAttribute attribute = element.Attribute(attributeName);
            TextBox textBox = new TextBox
            {
                Text = attribute?.Value ?? "Attribute not found",
                Style = (Style)FindResource("SmallTextBoxStyle"),
                Width = 320,
                Margin = new Thickness(0),
                Tag = attribute  // Store the reference to the XAttribute
            };

            textBox.TextChanged += (sender, args) =>
            {
                var tb = sender as TextBox;
                if (tb != null && tb.Tag is XAttribute attr)
                {
                    attr.Value = tb.Text;  // Update the XML attribute directly
                }
            };

            attributePanel.Children.Add(label);
            attributePanel.Children.Add(textBox);
            parentPanel.Children.Add(attributePanel);
        }

        private void GenerateBooleanAttributeControl(XElement element, string attributeName, string labelContent, Panel parentPanel)
        {
            // Create a horizontal stack panel to contain the label and radio buttons
            StackPanel booleanPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Create and configure the label for the whole control
            TextBlock mainLabel = new TextBlock
            {
                Text = labelContent,
                FontSize = 14,
                Margin = new Thickness(20, 0, 5, 0),
                Width = 160,
                Foreground = new SolidColorBrush(Colors.White), // Ensure the main label's text color is white
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Add the main label to the panel first
            booleanPanel.Children.Add(mainLabel);

            // Retrieve the current attribute value from the element
            XAttribute attribute = element.Attribute(attributeName);

            // Create labels for each radio button
            TextBlock trueLabel = new TextBlock
            {
                Text = "True:",
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Create and configure the "True" radio button
            RadioButton trueButton = new RadioButton
            {
                IsChecked = (attribute?.Value.ToLower() == "true"),
                Margin = new Thickness(5, -3, 20, -3),
                GroupName = attributeName,
                Style = (Style)FindResource("ModernRadioButtonStyle"),
                Tag = attribute
            };
            trueButton.Checked += (sender, args) =>
            {
                if (trueButton.Tag is XAttribute attr)
                {
                    attr.Value = "true";
                }
            };

            // Create a label for the "False" radio button
            TextBlock falseLabel = new TextBlock
            {
                Text = "False:",
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Create and configure the "False" radio button
            RadioButton falseButton = new RadioButton
            {
                IsChecked = (attribute?.Value.ToLower() == "false"),
                Margin = new Thickness(5, -3, 0, -3),
                Style = (Style)FindResource("ModernRadioButtonStyle"),
                GroupName = attributeName,
                Tag = attribute
            };
            falseButton.Checked += (sender, args) =>
            {
                if (falseButton.Tag is XAttribute attr)
                {
                    attr.Value = "false";
                }
            };

            // Add the true label and button to the panel
            booleanPanel.Children.Add(trueLabel);
            booleanPanel.Children.Add(trueButton);

            // Add the false label and button to the panel
            booleanPanel.Children.Add(falseLabel);
            booleanPanel.Children.Add(falseButton);

            // Add the complete boolean control panel to the parent panel
            parentPanel.Children.Add(booleanPanel);
        }



        private void GenerateDataCapturePanel(XElement dataCaptureElement)
        {
            // Check if the dataCaptureElement contains the url element
            var urlElement = dataCaptureElement.Element("url");
            if (urlElement == null) return; // Exit if no url element is found

            StackPanel dataCapturePanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 10, 0, 10),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Add header
            TextBlock header = new TextBlock
            {
                Text = "Data Capture",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(0, 0, 0, 10)
            };
            dataCapturePanel.Children.Add(header);

            // Add Mode label and textbox
            StackPanel modePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock modeLabel = new TextBlock
            {
                Text = "Data Capture Mode:",
                FontSize = 14,
                Width = 140,
                Margin = new Thickness(20, 0, 0, 0),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            modePanel.Children.Add(modeLabel);

            XAttribute modeAttribute = urlElement.Attribute("mode");
            TextBox modeTextBox = new TextBox
            {
                Text = modeAttribute?.Value ?? "Not found",
                Width = 30,
                Margin = new Thickness(0, 0, 0, 0),
                Style = (Style)FindResource("SmallTextBoxStyle"),
                Tag = modeAttribute  // Store the reference to the XAttribute
            };

            modeTextBox.TextChanged += (sender, args) =>
            {
                var tb = sender as TextBox;
                if (tb != null && tb.Tag is XAttribute attr)
                {
                    attr.Value = tb.Text;  // Update mode attribute directly
                }
            };
            modePanel.Children.Add(modeTextBox);
            dataCapturePanel.Children.Add(modePanel);

            // Add URL label and textbox
            StackPanel urlPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock urlLabel = new TextBlock
            {
                Text = "Data Capture URL:",
                FontSize = 14,
                Width = 140,
                Margin = new Thickness(20, 0, 0, 0),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            urlPanel.Children.Add(urlLabel);

            TextBox urlTextBox = new TextBox
            {
                Text = urlElement.Value,
                Width = 600,
                Style = (Style)FindResource("SmallTextBoxStyle"),
                Margin = new Thickness(0),
                Tag = urlElement  // Store the reference to the XElement
            };

            urlTextBox.TextChanged += (sender, args) =>
            {
                var tb = sender as TextBox;
                if (tb != null && tb.Tag is XElement el)
                {
                    el.Value = tb.Text;  // Update URL text directly
                }
            };
            urlPanel.Children.Add(urlTextBox);
            dataCapturePanel.Children.Add(urlPanel);

            // Add the complete panel to the main GUI container
            TSSXMLControlsPanel.Children.Add(dataCapturePanel);
        }


        private void GenerateSSFWPanel(XElement ssfwElement)
        {
            StackPanel ssfwPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 10, 0, 10),
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock header = new TextBlock
            {
                Text = "SSFW",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(0, 0, 0, 10)
            };
            ssfwPanel.Children.Add(header);

            // Special handling for identity element with multiple attributes
            var identityElement = ssfwElement.Element("identity");
            if (identityElement != null)
            {
                AddLabelAndTextbox(ssfwPanel, "Identity TTL: ", identityElement.Attribute("ttl")?.Value ?? "Not found", 130, 60, identityElement.Attribute("ttl"));
                AddLabelAndTextbox(ssfwPanel, "Identity Secret: ", identityElement.Attribute("secret")?.Value ?? "Not found", 130, 150, identityElement.Attribute("secret"));
                AddLabelAndTextbox(ssfwPanel, "Identity URL: ", identityElement.Value, 130, 700, identityElement);
            }

            // Process all other elements in ssfw, except identity
            foreach (var element in ssfwElement.Elements().Where(e => e.Name != "identity"))
            {
                string label = "SSFW " + element.Name.LocalName + ":";
                AddLabelAndTextbox(ssfwPanel, label, element.Value, 130, 700, element);
            }

            TSSXMLControlsPanel.Children.Add(ssfwPanel);
        }
        private void ApplyNewHCDBSHA1Button_Click(object sender, RoutedEventArgs e)
        {
            // Get the SHA1 value from LatestHCDBSHA1textbox
            var latestHCDBSHA1textbox = (TextBox)FindName("LatestHCDBSHA1textbox");
            if (latestHCDBSHA1textbox == null)
            {
                MessageBox.Show("LatestHCDBSHA1textbox not found!");
                return;
            }
            string newSha1 = latestHCDBSHA1textbox.Text;

            // Get the entriesPanel that contains all SHA1 entries
            var sha1Panel = TSSXMLControlsPanel.Children.OfType<StackPanel>()
                              .FirstOrDefault(p => p.Children.OfType<TextBlock>()
                              .Any(tb => tb.Text == "SHA1 Encrypted Files"));
            if (sha1Panel == null)
            {
                MessageBox.Show("SHA1 Panel not found!");
                return;
            }

            var entriesPanel = sha1Panel.Children.OfType<StackPanel>()
                               .FirstOrDefault(p => p.Orientation == Orientation.Vertical);

            if (entriesPanel == null)
            {
                MessageBox.Show("Entries Panel not found!");
                return;
            }

            // Iterate through all SHA1 entries and update the ones that match the condition
            foreach (var entry in entriesPanel.Children.OfType<StackPanel>())
            {
                var fileTextBox = entry.Children.OfType<TextBox>()
                    .FirstOrDefault(tb => tb.Tag is XAttribute attribute && attribute.Name == "file");
                var digestTextBox = entry.Children.OfType<TextBox>()
                    .FirstOrDefault(tb => tb.Tag is XAttribute attribute && attribute.Name == "digest");

                if (fileTextBox != null && digestTextBox != null && ((XAttribute)fileTextBox.Tag).Value.StartsWith("Objects/ObjectCatalogue_5_SCE"))
                {
                    digestTextBox.Text = newSha1;
                }
            }

            // Set the global flag to true
            MainWindow.DeployHCDBFlag = true;
        }

        private void ApplyNewSceneListSHA1Button_Click(object sender, RoutedEventArgs e)
        {
            // Get the SHA1 value from LatestSceneListSHA1textbox
            var latestSceneListSHA1textbox = (TextBox)FindName("LatestSceneListSHA1textbox");
            if (latestSceneListSHA1textbox == null)
            {
                MessageBox.Show("LatestSceneListSHA1textbox not found!");
                return;
            }
            string newSha1 = latestSceneListSHA1textbox.Text;

            // Get the entriesPanel that contains all SHA1 entries
            var sha1Panel = TSSXMLControlsPanel.Children.OfType<StackPanel>()
                              .FirstOrDefault(p => p.Children.OfType<TextBlock>()
                              .Any(tb => tb.Text == "SHA1 Encrypted Files"));
            if (sha1Panel == null)
            {
                MessageBox.Show("SHA1 Panel not found!");
                return;
            }

            var entriesPanel = sha1Panel.Children.OfType<StackPanel>()
                               .FirstOrDefault(p => p.Orientation == Orientation.Vertical);

            if (entriesPanel == null)
            {
                MessageBox.Show("Entries Panel not found!");
                return;
            }

            // Iterate through all SHA1 entries and update the ones that match the condition
            foreach (var entry in entriesPanel.Children.OfType<StackPanel>())
            {
                var fileTextBox = entry.Children.OfType<TextBox>()
                    .FirstOrDefault(tb => tb.Tag is XAttribute attribute && attribute.Name == "file");
                var digestTextBox = entry.Children.OfType<TextBox>()
                    .FirstOrDefault(tb => tb.Tag is XAttribute attribute && attribute.Name == "digest");

                if (fileTextBox != null && digestTextBox != null && ((XAttribute)fileTextBox.Tag).Value.StartsWith("Environments/SceneList"))
                {
                    digestTextBox.Text = newSha1;
                }
            }

            MainWindow.DeploySceneListFlag = true;
        }
    }
}
