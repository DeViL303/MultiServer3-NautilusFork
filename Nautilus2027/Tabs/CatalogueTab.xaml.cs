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
    // "Catalogue" tab.
    public partial class CatalogueTab : UserControl
    {
        private static MainWindow Main => MainWindow.Instance;

        public CatalogueTab()
        {
            InitializeComponent();
        }

        internal async void InitializeWebView2()
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, null, new CoreWebView2EnvironmentOptions("--disable-web-security --user-data-dir=C:\\temp"));
            await WebView2Control.EnsureCoreWebView2Async(environment);

            // Set the source or load the custom HTML as per your existing logic
            if (Main._settings.UseOnlineCatalogue == false)
            {
                string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string htmlFileName = Main._settings.LiteCatalogueEnabled == false ? "index_Lite.html" : "index.html";
                string htmlFilePath = Path.Combine(exeDirectory, "dependencies", "psho.me", htmlFileName);

                if (File.Exists(htmlFilePath))
                {
                    string htmlFileUri = new Uri(htmlFilePath).AbsoluteUri;
                    WebView2Control.Source = new Uri(htmlFileUri);
                }
                else
                {
                    // Load the custom HTML if the file is not found
                    string customHtml = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {
            background-color: #111111;
            color: #fff;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            font-family: Arial, sans-serif;
            text-align: center;
            margin: 0;
        }
        .container {
            max-width: 600px;
            padding: 20px;
            border: 2px solid #fff;
            border-radius: 10px;
            box-shadow: 0 0 10px rgba(255, 255, 255, 0.2);
        }
        h1 {
            font-size: 24px;
            margin-bottom: 10px;
        }
        p {
            font-size: 16px;
            line-height: 1.5;
        }
        a {
            color: #00f;
            text-decoration: underline;
            cursor: pointer;
        }
    </style>
    <script>
        function openGithub() {
            window.chrome.webview.postMessage('openGithub');
        }
    </script>
</head>
<body>
    <div class='container'>
        <h1>Files Not Found!</h1>
        <p>Local database files could not be found.</p>
        <p>To use a local catalogue, You must provide your own database.</p>
        <p>Please ensure the extracted files are placed in the 'dependencies/psho.me/' directory.</p>
        <p>For information on how to create your own database, See Multiserver 3 Nautilus Fork on Github <a onclick='openGithub()'>HERE</a></p>
    </div>
</body>
</html>";

                    WebView2Control.NavigateToString(customHtml);
                }
            }
            else
            {
                WebView2Control.Source = new Uri("http://psho.me/index.html");
            }

            WebView2Control.ZoomFactor = 0.80;
            WebView2Control.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            WebView2Control.CoreWebView2.DownloadStarting += CoreWebView2_DownloadStarting; // Add this line
        }

        private void CoreWebView2_DownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
            // Define the desired download folder relative to the application
            string downloadFolder = Main._settings.CatalogueDownloadsDirectory;

            // Ensure the download folder exists
            Directory.CreateDirectory(downloadFolder);

            // Set the initial download path to the specified folder
            string fileName = Path.GetFileName(e.ResultFilePath); // Extract the file name from the default path
            string newFilePath = Path.Combine(downloadFolder, fileName);

            // Check if the file already exists, and adjust the filename accordingly
            newFilePath = GetUniqueFilePath(newFilePath);

            // Set the new file path
            e.ResultFilePath = newFilePath;

            // Automatically approve the download without asking the user
            e.Handled = true;
            Main.LogDebugInfo($"Catalogue: downloading {fileName} to {newFilePath}");


            // Handle download completion event to open the folder after download completes
            e.DownloadOperation.StateChanged += (s, args) =>
            {
                if (e.DownloadOperation.State == CoreWebView2DownloadState.Interrupted)
                    Main.LogDebugInfo($"Catalogue: download of {fileName} failed ({e.DownloadOperation.InterruptReason})");
                else if (e.DownloadOperation.State == CoreWebView2DownloadState.Completed)
                {
                    // Open the folder after download completes successfully
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = downloadFolder,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
            };
        }

        // Helper method to generate a unique file path by appending _2, _3, etc., if the file already exists
        private string GetUniqueFilePath(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);
            int fileIndex = 1;

            // Generate a unique file path
            while (File.Exists(filePath))
            {
                filePath = Path.Combine(directory, $"{fileNameWithoutExtension}_{fileIndex}{extension}");
                fileIndex++;
            }

            return filePath;
        }


        // The only message left is the GitHub link on the "local files not found" page; the catalogue itself is just a web page.
        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string message;
            try { message = e.TryGetWebMessageAsString(); }
            catch { return; }   // not a string message
            if (message == "openGithub") OpenUrlInDefaultBrowser("https://github.com/DeViL303/MultiServer3-NuatilusFork");
        }

        private void OpenUrlInDefaultBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open URL: {ex.Message}");
            }
        }


    }
}
