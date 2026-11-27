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
    // Debug log buffering and LogDebugInfo.
    public partial class MainWindow
    {
        private List<string> logBuffer = new List<string>();
        internal string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs\\debug.log");
        private System.Threading.Timer logFlushTimer;
        private readonly object logLock = new object();

        private void FlushLogBuffer()
        {
            lock (logLock)
            {
                if (logBuffer.Count > 0)
                {
                    try
                    {
                        using (StreamWriter sw = new StreamWriter(logFilePath, true)) // true to append data to the file
                        {
                            foreach (var message in logBuffer)
                            {
                                sw.WriteLine(message);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Error writing to log file: " + ex.Message);
                    }
                    logBuffer.Clear();
                }
                logFlushTimer.Change(Timeout.Infinite, Timeout.Infinite); // Stop the timer
            }
        }

        public void LogDebugInfo(string message)
        {
            if (_settings.SaveDebugLogToggle == SaveDebugLog.True)
            {
                lock (logLock)
                {
                    logBuffer.Add($"{message}");

                    if (logBuffer.Count >= 100) // Flush every 10 messages, adjust as needed
                    {
                        FlushLogBuffer();
                    }
                    else
                    {
                        logFlushTimer.Change(1000, Timeout.Infinite); // Reset the timer to 1 second
                    }
                }
            }
            // If SaveDebugLogToggle is False, do nothing
        }
    }
}
