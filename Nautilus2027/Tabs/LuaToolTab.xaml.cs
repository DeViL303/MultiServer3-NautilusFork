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
    // "LUA / LUAC" tab.
    public partial class LuaToolTab : UserControl
    {
        private static MainWindow Main => MainWindow.Instance;

        public LuaToolTab()
        {
            InitializeComponent();
        }

        // TAB 6: Logic for Compiling LUA to LUAC

        private bool isParseOnly = false;
        private bool isStripDebug = false;
        private CancellationTokenSource _luaCompilerCancellationTokenSource;


        private async void LUACompilerExecuteButtonClick(object sender, RoutedEventArgs e)
        {
            _luaCompilerCancellationTokenSource?.Cancel();
            _luaCompilerCancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = _luaCompilerCancellationTokenSource.Token;

            Main.LogDebugInfo("LUA Compilation to LUAC: Process Started");
            AppendTextToLUATextBox(Environment.NewLine + Environment.NewLine);

            string filesToCompile = string.Join(Environment.NewLine, LogBox.TakeLines(LUACompilerTextBox));   // the list moves into memory; the box shows the log
            if (!string.IsNullOrWhiteSpace(filesToCompile))
            {
                string[] allLines = filesToCompile.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                var validFilePaths = allLines
                    .Where(line => !line.TrimStart().StartsWith("--") &&
                                   !line.TrimStart().StartsWith("##") &&
                                   (line.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ||
                                    line.EndsWith(".LUA", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                var existingFiles = validFilePaths.Where(File.Exists).ToList();
                var nonExistingFiles = validFilePaths.Except(existingFiles).ToList();

                int totalSuccessCount = 0;
                int totalFailureCount = 0;

                string compilerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "HomeLuaC.exe");



                if (!isParseOnly && !Directory.Exists(Main._settings.LuacOutputDirectory))
                {
                    Directory.CreateDirectory(Main._settings.LuacOutputDirectory);
                    Main.LogDebugInfo($"LUA Compilation to LUAC: Output directory created at {Main._settings.LuacOutputDirectory}");
                    TemporaryMessageHelper.ShowTemporaryMessage(LUACompilerDragAreaText, $"Created {Main._settings.LuacOutputDirectory}", 1000);

                }

                if (existingFiles.Any())
                {
                    foreach (string file in existingFiles)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            LUACompilerTextBox.Clear();
                            break;
                        }

                        if (isParseOnly)
                        {
                            TemporaryMessageHelper.ShowTemporaryMessage(LUACompilerDragAreaText, $"Parsing {Path.GetFileName(file)}...", 1000);

                            var (successCount, failureCount, parseResult) = await LUACompilerParseOnlyAsync(compilerPath, file, cancellationToken);
                            totalSuccessCount += successCount;
                            totalFailureCount += failureCount;
                            AppendTextToLUATextBox(parseResult);
                        }
                        else
                        {
                            TemporaryMessageHelper.ShowTemporaryMessage(LUACompilerDragAreaText, $"Compiling {Path.GetFileName(file)}...", 1000);

                            var compileResult = await LUACompilerCompileFileAsync(file, isStripDebug, cancellationToken);
                            AppendTextToLUATextBox(compileResult);

                            if (compileResult.Contains("-- Compile Success:"))
                            {
                                totalSuccessCount++;
                            }
                            else if (compileResult.Contains("--- ERROR:"))
                            {
                                totalFailureCount++;
                            }
                        }
                    }

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        string summaryMessage = isParseOnly ?
                            $"{totalSuccessCount} File(s) Parsed Successfully, {totalFailureCount} File(s) Failed to Parse" :
                            $"{totalSuccessCount} File(s) Compiled Successfully, {totalFailureCount} File(s) Failed to Compile";
                        TemporaryMessageHelper.ShowTemporaryMessage(LUACompilerDragAreaText, summaryMessage, 3000);
                    }
                }

                if (nonExistingFiles.Any() && !cancellationToken.IsCancellationRequested)
                {
                    string missingFilesMessage = string.Join(Environment.NewLine, nonExistingFiles.Select(file => "--- ERROR: File Not Found " + file));
                    Main.LogDebugInfo("LUA Compilation to LUAC: Some files could not be found");
                    TemporaryMessageHelper.ShowTemporaryMessage(LUACompilerDragAreaText, "Some files could not be found", 3000);
                    AppendTextToLUATextBox(missingFilesMessage);
                }
            }
            else
            {
                Main.LogDebugInfo("LUA Compilation to LUAC: Failed Initialisation as no input files received");
                TemporaryMessageHelper.ShowTemporaryMessage(LUACompilerDragAreaText, "No LUA files listed for compilation.", 3000);
            }
        }




        private async void AppendTextToLUATextBox(string text)
        {
            // If called from a non-UI thread, use Dispatcher
            await Dispatcher.InvokeAsync(async () =>
            {
                LogBox.Append(LUACompilerTextBox, text, log: true);

                // Introduce a short delay
                await Task.Delay(20); // Delay for 100 milliseconds

                // Scrolls the text box to the end after appending text
                LUACompilerTextBox.ScrollToEnd();
            });
        }

        private async void StopandClearLUAListHandler(object sender, RoutedEventArgs e)
        {
            // Cancel the ongoing process
            _luaCompilerCancellationTokenSource?.Cancel();

            // Clear the LUACompilerTextBox
            LUACompilerTextBox.Clear();

            // Reset UI elements if necessary
            TemporaryMessageHelper.ShowTemporaryMessage(LUACompilerDragAreaText, "List Cleared", 2000);
        }


        private async Task<string> LUACompilerCompileFileAsync(string file, bool stripDebug, CancellationToken cancellationToken)
        {
            StringBuilder compileResult = new StringBuilder();

            string outputDirectory = Main._settings.LuacOutputDirectory;
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            string compilerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "HomeLuaC.exe");


            if (!File.Exists(compilerPath))
            {
                compileResult.AppendLine("Error: Compiler executable not found at " + compilerPath);
                return compileResult.ToString();
            }

            if (string.IsNullOrWhiteSpace(file) || file.TrimStart().StartsWith("--") || file.TrimStart().StartsWith("##"))
            {
                return "";
            }

            if (!Path.GetExtension(file).Equals(".lua", StringComparison.OrdinalIgnoreCase))
            {
                return $"--- Warning: {Path.GetFileName(file)} is not a LUA file. Skipped.";
            }

            string outputFileName = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(file) + ".LUAC");
            bool wasRenamed = false;

            switch (Main._settings.FileOverwriteBehavior)
            {
                case OverwriteBehavior.Rename:
                    int counter = 1;
                    string baseOutputFileName = outputFileName;
                    while (File.Exists(baseOutputFileName))
                    {
                        baseOutputFileName = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(file)}_{counter:D2}.LUAC");
                        counter++;
                    }
                    if (baseOutputFileName != outputFileName)
                    {
                        wasRenamed = true;
                        outputFileName = baseOutputFileName;
                    }
                    break;
                case OverwriteBehavior.Skip:
                    if (File.Exists(outputFileName))
                    {
                        return $"\n-- Skipping {Path.GetFileName(file)} as output already exists";
                    }
                    break;
                case OverwriteBehavior.Overwrite:
                default:
                    break;
            }

            string arguments = $"-o \"{outputFileName}\" \"{file}\"";
            if (stripDebug)
            {
                arguments = "-s " + arguments;
            }

            compileResult.AppendLine($"-- Compiling: {Path.GetFileName(file)}...");

            ProcessStartInfo startInfo = new ProcessStartInfo()
            {
                FileName = compilerPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (Process process = new Process { StartInfo = startInfo })
            {
                process.Start();
                await process.WaitForExitAsync();

                if (cancellationToken.IsCancellationRequested)
                {
                    return ""; // Exit if cancellation is requested
                }

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                if (File.Exists(outputFileName))
                {
                    string successMessage = $"-- Compile Success: '{outputFileName}' Created.";
                    if (wasRenamed)
                    {
                        successMessage += " (Renamed)";
                    }
                    compileResult.AppendLine(successMessage);
                }
                else
                {
                    string formattedError = ProcessCompileLUAErrorMessage(error);
                    compileResult.AppendLine($"--- ERROR: Compiling '{Path.GetFileName(file)}' failed. {formattedError}");
                }
            }

            return compileResult.ToString();
        }


        private string ProcessCompileLUAErrorMessage(string errorMessage)
        {
            int lastIndex = errorMessage.LastIndexOf(':');
            if (lastIndex >= 0)
            {
                int secondLastIndex = errorMessage.LastIndexOf(':', lastIndex - 1);
                if (secondLastIndex >= 0)
                {
                    string errorPart = errorMessage.Substring(secondLastIndex + 1).Trim();
                    return $"at Line {errorPart}";
                }
            }
            return errorMessage;
        }



        private async Task<(int successCount, int failureCount, string resultText)> LUACompilerParseOnlyAsync(string compilerPath, string file, CancellationToken cancellationToken)
        {
            Main.LogDebugInfo($"LUA Compilation to LUAC (Parse Only): Processing {file}");

            int successCount = 0;
            int failureCount = 0;
            StringBuilder compileResults = new StringBuilder();

            ProcessStartInfo startInfo = new ProcessStartInfo()
            {
                FileName = compilerPath,
                Arguments = $"-p \"{file}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (Process process = new Process { StartInfo = startInfo })
            {
                process.Start();
                await process.WaitForExitAsync();

                if (cancellationToken.IsCancellationRequested)
                {
                    return (0, 0, ""); // Exit if cancellation is requested
                }

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                // Log the full error message for debugging purposes
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Main.LogDebugInfo($"LUA Compilation to LUAC (Parse Only): Error while parsing {file}: {error}");
                }

                string standardVersionMessage = "Playstation Home version of luac compiled at 14:36:10 on Jan 26 2009.";
                string[] outputLines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                if (outputLines.Length == 1 && outputLines[0].Trim() == standardVersionMessage && string.IsNullOrWhiteSpace(error))
                {
                    // Successful parsing
                    successCount++;
                    compileResults.AppendLine($"-- Parse Success: {Path.GetFileName(file)} No syntax errors found.");
                }
                else
                {
                    // Failure detected
                    failureCount++;

                    // Process error message to remove the full file path and shorten the message
                    string shortErrorMessage = ProcessLUAPARSEErrorMessage(error);
                    compileResults.AppendLine($"--- ERROR Parsing {Path.GetFileName(file)} {shortErrorMessage}");
                }
            }

            return (successCount, failureCount, compileResults.ToString());
        }


        private string ProcessLUAPARSEErrorMessage(string errorMessage)
        {
            // Find the last colon
            int lastIndex = errorMessage.LastIndexOf(':');

            if (lastIndex >= 0)
            {
                // Find the second-to-last colon before the last colon
                int secondLastIndex = errorMessage.LastIndexOf(':', lastIndex - 1);

                if (secondLastIndex >= 0)
                {
                    // Extract the relevant part of the error message
                    string errorPart = errorMessage.Substring(secondLastIndex + 1).Trim();

                    // Return the formatted error message
                    return $"at Line {errorPart}";
                }
            }

            // If the format is not as expected, return the original message
            return errorMessage;
        }




        private void LuaDragDropHandler(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedItems = (string[])e.Data.GetData(DataFormats.FileDrop);
                List<string> validFiles = new List<string>();
                string message = string.Empty;
                int displayTime = 2000; // Default to 2 seconds

                int newFilesCount = 0; // Declare outside of Dispatcher.Invoke
                int duplicatesCount = 0; // Declare outside of Dispatcher.Invoke

                foreach (var item in droppedItems)
                {
                    if (Directory.Exists(item))
                    {
                        var filesInDirectory = Directory.GetFiles(item, "*.lua", SearchOption.AllDirectories).ToList();
                        if (filesInDirectory.Count > 0)
                        {
                            validFiles.AddRange(filesInDirectory);
                        }
                    }
                    else if (File.Exists(item) && Path.GetExtension(item).ToLowerInvariant() == ".lua")
                    {
                        validFiles.Add(item);
                    }
                }

                if (validFiles.Count > 0)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(LUACompilerTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;
                        existingFilesSet.UnionWith(validFiles);
                        newFilesCount = existingFilesSet.Count - initialCount;
                        duplicatesCount = validFiles.Count - newFilesCount;

                        LUACompilerTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);
                    });
                    displayTime = 2000; // Change display time since files were added
                }
                else
                {
                    Main.LogDebugInfo("LUA Compilation to LUAC: Dropped Files/Folders scanned - No LUA files found.");
                    message = "No LUA files found.";
                }

                // Construct the message after Dispatcher.Invoke has executed
                string addedFilesMessage = newFilesCount > 0 ? $"{newFilesCount} LUA files added" : "No new LUA files added";
                string duplicatesMessage = duplicatesCount > 1 ? $", {duplicatesCount} duplicates filtered" : "";
                message = addedFilesMessage + duplicatesMessage;
                string logPrefix = "LUA Compilation to LUAC: Drag and Drop - ";
                Main.LogDebugInfo(logPrefix + addedFilesMessage); // This will log "Drag and Drop Info: X LUA files added" or "Drag and Drop Info: No new LUA files added"
                if (duplicatesCount > 0)
                {
                    // Ensure we clean up the message by trimming any leading comma and space
                    string cleanDuplicatesMessage = duplicatesMessage.TrimStart(',', ' ').Trim();
                    Main.LogDebugInfo(logPrefix + cleanDuplicatesMessage); // This will log "Drag and Drop Info: X duplicates filtered" if there are any duplicates
                }

                TemporaryMessageHelper.ShowTemporaryMessage(LUACompilerDragAreaText, message, 2000);
            }
        }


        private async void ClickToBrowseHandlerLUACompiler(object sender, RoutedEventArgs e)
        {
            Main.LogDebugInfo("LUA Compilation: Browsing for LUA files Initiated");

            // Disable the main window to prevent interactions while the dialog is open
            Main.IsEnabled = false;

            var openFileDialog = new VistaOpenFileDialog
            {
                Filter = "LUA files (*.lua)|*.lua",
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

                if (selectedFiles.Any())
                {
                    int newFilesCount = 0; // Declare outside of Dispatcher.Invoke
                    int duplicatesCount = 0; // Declare outside of Dispatcher.Invoke

                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(LUACompilerTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;
                        existingFilesSet.UnionWith(selectedFiles);
                        newFilesCount = existingFilesSet.Count - initialCount;
                        duplicatesCount = selectedFiles.Length - newFilesCount;

                        LUACompilerTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);
                    });

                    // Construct the message outside of the Dispatcher.Invoke block using the counts
                    string addedFilesMessage = newFilesCount > 0 ? $"{newFilesCount} LUA files added" : "No new LUA files added";
                    string duplicatesMessage = duplicatesCount > 1 ? $", {duplicatesCount} duplicates filtered" : "";
                    message = addedFilesMessage + duplicatesMessage;

                    displayTime = 1000; // Change display time since files were added
                    string logPrefix = "LUA Compilation to LUAC: Click to Browse - ";
                    Main.LogDebugInfo(logPrefix + addedFilesMessage); // Logs added files
                    if (duplicatesCount > 0)
                    {
                        Main.LogDebugInfo(logPrefix + $"{duplicatesCount} duplicates filtered"); // Logs duplicates if any
                    }
                }
                else
                {
                    Main.LogDebugInfo("LUA Compilation to LUAC: Click to Browse - No LUA files were selected.");
                    message = "No LUA files selected.";
                }
            }
            else
            {
                Main.LogDebugInfo("LUA Compilation to LUAC: File Browser - Dialog Cancelled.");
                message = "Dialog cancelled.";
            }

            TemporaryMessageHelper.ShowTemporaryMessage(LUACompilerDragAreaText, message, 2000);
        }


        private void LUACompilerTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Assuming LUACOutputDirectoryTextBox is the name of your TextBox containing the output directory path
            string outputDirectory = Main._settings.LuacOutputDirectory;

            if (!string.IsNullOrEmpty(outputDirectory) && Directory.Exists(outputDirectory))
            {
                try
                {
                    // Open the directory in File Explorer
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                    {
                        FileName = outputDirectory,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
                catch (Exception ex)
                {
                    // Handle any exceptions (e.g., directory does not exist)
                    Main.LogDebugInfo($"LUA Compilation to LUAC: Unable to open directory: {ex.Message}");
                    MessageBox.Show($"Unable to open directory: {ex.Message}");
                }
            }
            else
            {
                Main.LogDebugInfo($"LUA Compilation to LUAC: Output directory {outputDirectory} is not set or does not exist.");
                MessageBox.Show("Output directory is not set or does not exist.");
            }
        }

        private void CheckBoxLUACParseOnly_Checked(object sender, RoutedEventArgs e)
        {
            // Update the 'isParseOnly' property when the checkbox is checked
            isParseOnly = true;
            // If Parse Only is checked, we want to uncheck Strip Debug
            isStripDebug = false;
            CheckBoxStripDebug.IsChecked = false;
        }

        private void CheckBoxLUACParseOnly_Unchecked(object sender, RoutedEventArgs e)
        {
            // Update the 'isParseOnly' property when the checkbox is unchecked
            isParseOnly = false;
        }

        private void CheckBoxLUACStripDebug_Checked(object sender, RoutedEventArgs e)
        {
            // Update the 'isStripDebug' property when the checkbox is checked
            isStripDebug = true;
            // If Strip Debug is checked, we want to uncheck Parse Only
            isParseOnly = false;
            CheckBoxParseOnly.IsChecked = false;
        }

        private void CheckBoxLUACStripDebug_Unchecked(object sender, RoutedEventArgs e)
        {
            // Update the 'isStripDebug' property when the checkbox is unchecked
            isStripDebug = false;
        }




        // TAB 6: Logic for Decompiling LUAC to LUA

        private bool isNewTask = true; // Flag to check if a new task has started
        private string unluac122Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "unluac122.jar");
        private string unluac2023Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "unluac_2023_12_24.jar");
        private string unluacNETPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "unluac.exe");
        // Built-in decompiler (BackendProcess\LuaDecompiler); no external program needed.
        private const string MetawormEngine = "builtin:metaworm";


        private string currentUnluacJarPath = "";


        // Event handlers for the radio buttons
        private void LUACDecompilerRadioButtonMetaworm_Checked(object sender, RoutedEventArgs e)
        {
            currentUnluacJarPath = MetawormEngine;
        }

        private void LUACDecompilerRadioButtonUnluac122_Checked(object sender, RoutedEventArgs e)
        {
            currentUnluacJarPath = unluac122Path;
        }

        private void LUACDecompilerRadioButtonUnluac2023_Checked(object sender, RoutedEventArgs e)
        {
            currentUnluacJarPath = unluac2023Path;
        }

        private void LUACDecompilerRadioButtonUnluacNET_Checked(object sender, RoutedEventArgs e)
        {
            currentUnluacJarPath = unluacNETPath;
        }

        // Modified LUACDecompilerExecuteButtonClick method
        private async void LUACDecompilerExecuteButtonClick(object sender, RoutedEventArgs e)
        {
            isNewTask = true;
            Main.LogDebugInfo("LUAC Decompilation to LUA: Process Started");
            TemporaryMessageHelper.ShowTemporaryMessage(LUACDecompilerDragAreaText, "Initializing...", 3000);

            string unluacJarPath = currentUnluacJarPath;
            bool isMetaworm = unluacJarPath.Equals(MetawormEngine);
            if (!isMetaworm && !File.Exists(unluacJarPath) && !unluacJarPath.Equals(unluacNETPath))
            {
                Main.LogDebugInfo($"LUAC Decompilation to LUA: Error: {unluacJarPath} was not found.");
                TemporaryMessageHelper.ShowTemporaryMessage(LUACDecompilerDragAreaText, $"Error: {unluacJarPath} was not found.", 3000);
                return;
            }

            if (!Directory.Exists(Main._settings.LuaOutputDirectory))
            {
                Directory.CreateDirectory(Main._settings.LuaOutputDirectory);
                Main.LogDebugInfo($"LUAC Decompilation to LUA: Output directory created at {Main._settings.LuaOutputDirectory}");
                TemporaryMessageHelper.ShowTemporaryMessage(LUACDecompilerDragAreaText, $"Created {Main._settings.LuaOutputDirectory}", 1000);
            }

            string filesToDecompile = string.Join(Environment.NewLine, LogBox.TakeLines(LUACDecompilerTextBox));   // the list moves into memory; the box shows the log
            if (!string.IsNullOrWhiteSpace(filesToDecompile))
            {
                string[] filePaths = filesToDecompile
                    .Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(line => !line.TrimStart().StartsWith("--") && !line.TrimStart().StartsWith("##") && line.EndsWith(".luac", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                int successCount = 0, failureCount = 0;

                foreach (string file in filePaths)
                {
                    TemporaryMessageHelper.ShowTemporaryMessage(LUACDecompilerDragAreaText, $"Decompiling {Path.GetFileName(file)}...", 10000);

                    var (isSuccess, message) =
                        isMetaworm ? await DecompileLuacFileWithMetawormAsync(file) :
                        unluacJarPath.Equals(unluacNETPath) ? await DecompileLuacFileWithExeAsync(file, unluacJarPath) :
                        await DecompileLuacFileAsync(file, unluacJarPath);

                    AppendTextToLUACDecompilerTextBox(message);

                    if (isSuccess)
                    {
                        successCount++;
                    }
                    else
                    {
                        failureCount++;
                    }
                }

                string summaryMessage = $"-- {successCount} file(s) decompiled successfully, {failureCount} file(s) failed/skipped.";
                AppendTextToLUACDecompilerTextBox(summaryMessage);
                Main.LogDebugInfo($"LUAC Decompilation to LUA: {summaryMessage}");
                TemporaryMessageHelper.ShowTemporaryMessage(LUACDecompilerDragAreaText, summaryMessage, 6000);
            }
            else
            {
                Main.LogDebugInfo("LUAC Decompilation to LUA: No input files were provided for decompilation.");
                TemporaryMessageHelper.ShowTemporaryMessage(LUACDecompilerDragAreaText, "No LUAC files listed for decompilation.", 3000);
            }
        }


        private async Task<(bool isSuccess, string message)> DecompileLuacFileWithMetawormAsync(string inputFile)
        {
            Main.LogDebugInfo($"LUAC Decompilation (Metaworm): Starting decompilation for {inputFile}");

            string outputFileName = Path.Combine(Main._settings.LuaOutputDirectory, Path.GetFileNameWithoutExtension(inputFile) + ".lua");
            bool renamed = false;

            switch (Main._settings.FileOverwriteBehavior)
            {
                case OverwriteBehavior.Rename:
                    int counter = 1;
                    while (File.Exists(outputFileName))
                    {
                        outputFileName = Path.Combine(Main._settings.LuaOutputDirectory, $"{Path.GetFileNameWithoutExtension(inputFile)}_{counter:D2}.lua");
                        counter++;
                        renamed = true;
                    }
                    break;
                case OverwriteBehavior.Skip:
                    if (File.Exists(outputFileName))
                    {
                        Main.LogDebugInfo($"LUAC Decompilation: SKIPPED '{inputFile}' because '{outputFileName}' already exists.");
                        return (false, $"-- SKIPPED: {Path.GetFileName(inputFile)} output already exists - See settings to change");
                    }
                    break;
            }

            try
            {
                byte[] bytecode = await File.ReadAllBytesAsync(inputFile);
                string source = await LuaDecompiler.Luadec.DecompileAsync(bytecode, TimeSpan.FromSeconds(15));
                await File.WriteAllTextAsync(outputFileName, source);

                bool partial = source.Contains("-- luadec: could not decompile");
                Main.LogDebugInfo($"LUAC Decompilation (Metaworm): Decompiled {inputFile} to {outputFileName}" + (partial ? " (some functions could not be decompiled)" : ""));
                return (true, $"-- Success: Decompiled '{Path.GetFileName(inputFile)}' to '{outputFileName}'"
                    + (renamed ? " (Renamed)" : "")
                    + (partial ? " - some functions could not be decompiled, see comments in the file." : "."));
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"LUAC Decompilation (Metaworm): Failed for {inputFile}: {ex.Message}");
                return (false, $"--- ERROR: Decompiling '{Path.GetFileName(inputFile)}' failed. Error: {ex.Message}");
            }
        }

        private async Task<(bool isSuccess, string message)> DecompileLuacFileWithExeAsync(string inputFile, string unluacExePath)
        {
            Main.LogDebugInfo($"LUAC Decompilation: Starting decompilation for {inputFile}");

            string baseOutputFileName = Path.Combine(Main._settings.LuaOutputDirectory, Path.GetFileNameWithoutExtension(inputFile) + ".lua");
            string outputFileName = baseOutputFileName;
            bool renamed = false;

            switch (Main._settings.FileOverwriteBehavior)
            {
                case OverwriteBehavior.Rename:
                    int counter = 1;
                    while (File.Exists(outputFileName))
                    {
                        outputFileName = Path.Combine(Main._settings.LuaOutputDirectory, $"{Path.GetFileNameWithoutExtension(inputFile)}_{counter:D2}.lua");
                        counter++;
                        renamed = true;
                    }
                    break;
                case OverwriteBehavior.Skip:
                    if (File.Exists(outputFileName))
                    {
                        Main.LogDebugInfo($"LUAC Decompilation: SKIPPED '{inputFile}' because '{outputFileName}' already exists.");
                        return (false, $"-- SKIPPED: {Path.GetFileName(inputFile)} output already exists - See settings to change");
                    }
                    break;
                case OverwriteBehavior.Overwrite:
                default:
                    break;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = unluacExePath,
                Arguments = $"\"{inputFile}\" \"{outputFileName}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                using (Process process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    bool exited = await Task.Run(() => process.WaitForExit(3000)); // 3-second timeout

                    if (!exited)
                    {
                        process.Kill();
                        Main.LogDebugInfo($"LUAC Decompilation: Timeout occurred for {inputFile}");
                        return (false, $"--- ERROR: Decompilation timed out for '{Path.GetFileName(inputFile)}'.");
                    }

                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();

                    if (process.ExitCode == 0)
                    {
                        // Check if the output file exists
                        if (File.Exists(outputFileName))
                        {
                            Main.LogDebugInfo($"LUAC Decompilation: Successfully decompiled {inputFile} to {outputFileName}");
                            return (true, $"-- Success: Decompiled '{Path.GetFileName(inputFile)}' to '{outputFileName}'" + (renamed ? " (Renamed)." : "."));
                        }
                        else
                        {
                            Main.LogDebugInfo($"LUAC Decompilation: Output file '{outputFileName}' not found after decompilation for {inputFile}");
                            return (false, $"--- ERROR: Output file '{outputFileName}' not found after decompilation.");
                        }
                    }
                    else
                    {
                        Main.LogDebugInfo($"LUAC Decompilation: Decompilation failed for {inputFile}");
                        return (false, $"--- ERROR: Decompiling '{Path.GetFileName(inputFile)}' failed. Error: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"LUAC Decompilation: Exception occurred for {inputFile}: {ex.Message}");
                return (false, $"--- ERROR: Exception while decompiling '{Path.GetFileName(inputFile)}'. Exception: {ex.Message}");
            }
        }



        private async Task<(bool isSuccess, string message)> DecompileLuacFileAsync(string inputFile, string unluacJarPath)
        {
            Main.LogDebugInfo($"LUAC Decompilation: Starting decompilation for {inputFile}");

            string baseOutputFileName = Path.Combine(Main._settings.LuaOutputDirectory, Path.GetFileNameWithoutExtension(inputFile) + ".lua");
            string outputFileName = baseOutputFileName;
            bool renamed = false;

            switch (Main._settings.FileOverwriteBehavior)
            {
                case OverwriteBehavior.Rename:
                    int counter = 1;
                    while (File.Exists(outputFileName))
                    {
                        outputFileName = Path.Combine(Main._settings.LuaOutputDirectory, $"{Path.GetFileNameWithoutExtension(inputFile)}_{counter:D2}.lua");
                        counter++;
                        renamed = true;
                    }
                    break;
                case OverwriteBehavior.Skip:
                    if (File.Exists(outputFileName))
                    {
                        return (false, $"-- Decompiling: {Path.GetFileName(inputFile)}... Skipped '{outputFileName}' already exists.");
                    }
                    break;
                case OverwriteBehavior.Overwrite:
                default:
                    break;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "java",
                Arguments = $"-jar \"{unluacJarPath}\" \"{inputFile}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                using (Process process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    bool exited = await Task.Run(() => process.WaitForExit(2000)); // 1-second timeout

                    if (!exited)
                    {
                        process.Kill();
                        Main.LogDebugInfo($"LUAC Decompilation: Timeout occurred for {inputFile}");
                        return (false, $"--- ERROR: Decompilation timed out for '{Path.GetFileName(inputFile)}'.");
                    }

                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();

                    if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    {
                        await File.WriteAllTextAsync(outputFileName, output);
                        if (File.Exists(outputFileName))
                        {
                            Main.LogDebugInfo($"LUAC Decompilation: Successfully decompiled {inputFile} to {outputFileName}");
                            return (true, $"-- Success: Decompiled '{Path.GetFileName(inputFile)}' to '{outputFileName}'" + (renamed ? " (Renamed)." : "."));
                        }
                        else
                        {
                            Main.LogDebugInfo($"LUAC Decompilation: Output file not found after decompilation for {inputFile}");
                            return (false, $"--- ERROR: Output file '{outputFileName}' not found after decompilation.");
                        }
                    }
                    else
                    {
                        Main.LogDebugInfo($"LUAC Decompilation: Decompilation failed for {inputFile}");
                        return (false, $"--- ERROR: Decompiling '{Path.GetFileName(inputFile)}' failed. Error: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"LUAC Decompilation: Exception occurred for {inputFile}: {ex.Message}");
                return (false, $"--- ERROR: Exception while decompiling '{Path.GetFileName(inputFile)}'. Exception: {ex.Message}");
            }
        }



        private void AppendTextToLUACDecompilerTextBox(string text)
        {
            // If called from a non-UI thread, use Dispatcher
            Dispatcher.InvokeAsync(async () =>
            {
                // Check if this is a new task and prepend two new lines if it is
                if (isNewTask && !string.IsNullOrEmpty(LUACDecompilerTextBox.Text))
                {
                    LogBox.Append(LUACDecompilerTextBox, Environment.NewLine + Environment.NewLine, log: true);
                    isNewTask = false; // Reset flag as this is no longer a new task
                }

                LogBox.Append(LUACDecompilerTextBox, text + Environment.NewLine, log: true);

                // Introduce a short delay
                await Task.Delay(20); // Delay for 20 milliseconds

                // Scrolls the text box to the end after appending text
                LUACDecompilerTextBox.ScrollToEnd();
            });
        }





        private void LuacDragDropHandler(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedItems = (string[])e.Data.GetData(DataFormats.FileDrop);
                List<string> validFiles = new List<string>();
                string message = string.Empty;
                int displayTime = 2000; // Default to 2 seconds

                foreach (var item in droppedItems)
                {
                    if (Directory.Exists(item))
                    {
                        var filesInDirectory = Directory.GetFiles(item, "*.luac", SearchOption.AllDirectories).ToList();
                        if (filesInDirectory.Count > 0)
                        {
                            validFiles.AddRange(filesInDirectory);
                        }
                    }
                    else if (File.Exists(item) && Path.GetExtension(item).ToLowerInvariant() == ".luac")
                    {
                        validFiles.Add(item);
                    }
                }

                int newFilesCount = 0;
                int duplicatesCount = 0;

                if (validFiles.Count > 0)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(LUACDecompilerTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;
                        existingFilesSet.UnionWith(validFiles);
                        newFilesCount = existingFilesSet.Count - initialCount;
                        duplicatesCount = validFiles.Count - newFilesCount;

                        LUACDecompilerTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);
                    });
                    displayTime = 2000; // Change display time since files were added
                    Main.LogDebugInfo($"LUAC Decompilation to LUA: {newFilesCount} new files added for decompilation.");
                    if (duplicatesCount > 0)
                    {
                        Main.LogDebugInfo($"LUAC Decompilation to LUA: {duplicatesCount} duplicate files ignored.");
                    }
                }
                else
                {
                    Main.LogDebugInfo("LUAC Decompilation to LUA: No valid .luac files found to decompile.");
                    message = "No LUAC files found";
                }

                // Construct the message after Dispatcher.Invoke has executed
                string addedFilesMessage = newFilesCount > 0 ? $"{newFilesCount} LUAC files added" : "No new LUAC files added";
                string duplicatesMessage = duplicatesCount > 1 ? $", {duplicatesCount} duplicates filtered" : "";
                message = addedFilesMessage + duplicatesMessage;

                // Log final message
                Main.LogDebugInfo("LUAC Decompilation to LUA: Drag and Drop - " + message);

                TemporaryMessageHelper.ShowTemporaryMessage(LUACDecompilerDragAreaText, message, 2000);
            }
        }


        private async void ClickToBrowseHandlerLUACDecompiler(object sender, RoutedEventArgs e)
        {
            Main.LogDebugInfo("LUAC Decompilation to LUA: Browsing for LUAC files Initiated");

            // Disable the main window to prevent interactions while the dialog is open
            Main.IsEnabled = false;

            var openFileDialog = new VistaOpenFileDialog
            {
                Filter = "LUAC files (*.luac)|*.luac",
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

                if (selectedFiles.Any())
                {
                    int newFilesCount = 0;
                    int duplicatesCount = 0;

                    Dispatcher.Invoke(() =>
                    {
                        var existingFilesSet = new HashSet<string>(LUACDecompilerTextBox.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
                        int initialCount = existingFilesSet.Count;
                        existingFilesSet.UnionWith(selectedFiles);
                        newFilesCount = existingFilesSet.Count - initialCount;
                        duplicatesCount = selectedFiles.Length - newFilesCount;

                        LUACDecompilerTextBox.Text = string.Join(Environment.NewLine, existingFilesSet);
                    });

                    string addedFilesMessage = newFilesCount > 0 ? $"{newFilesCount} LUAC files added" : "No new LUAC files added";
                    string duplicatesMessage = duplicatesCount > 1 ? $", {duplicatesCount} duplicates filtered" : "";
                    message = addedFilesMessage + duplicatesMessage;
                    displayTime = 1000; // Change display time since files were added

                    string logPrefix = "LUAC Decompilation to LUA: Click to Browse - ";
                    Main.LogDebugInfo(logPrefix + addedFilesMessage);
                    if (duplicatesCount > 0)
                    {
                        Main.LogDebugInfo(logPrefix + $"{duplicatesCount} duplicates filtered");
                    }
                }
                else
                {
                    Main.LogDebugInfo("LUAC Decompilation to LUA: Click to Browse - No LUAC files were selected.");
                    message = "No LUAC files selected.";
                }
            }
            else
            {
                message = "Dialog cancelled.";
                Main.LogDebugInfo("LUAC Decompilation to LUA: File Browser - Dialog Cancelled.");
            }

            TemporaryMessageHelper.ShowTemporaryMessage(LUACDecompilerDragAreaText, message, 2000);
        }


        private void LUACDecompilerTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Assuming LUAOutputDirectoryTextBox is the name of your TextBox containing the output directory path
            string outputDirectory = Main._settings.LuaOutputDirectory;

            if (!string.IsNullOrEmpty(outputDirectory) && Directory.Exists(outputDirectory))
            {
                try
                {
                    // Open the directory in File Explorer
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                    {
                        FileName = outputDirectory,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                    Main.LogDebugInfo($"LUAC Decompilation to LUA: Opened output directory {outputDirectory}");
                }
                catch (Exception ex)
                {
                    // Handle any exceptions
                    Main.LogDebugInfo($"LUAC Decompilation to LUA: ERROR: Unable to open directory - {ex.Message}");
                    MessageBox.Show($"Unable to open directory: {ex.Message}");
                }
            }
            else
            {
                Main.LogDebugInfo($"LUAC Decompilation to LUA: ERROR: Directory {outputDirectory} is not set or does not exist.");
                MessageBox.Show("Output directory is not set or does not exist.");
            }
        }

        // Handlers used in this tab's XAML that live elsewhere.
        private void ClearListHandler(object sender, RoutedEventArgs e) => Main.ClearListHandler(sender, e);
    }
}
