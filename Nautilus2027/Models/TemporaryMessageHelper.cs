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
    public static class TemporaryMessageHelper
    {
        private static Dictionary<TextBlock, (DispatcherTimer Timer, string OriginalText)> messageTimers = new Dictionary<TextBlock, (DispatcherTimer, string)>();

        public static void ShowTemporaryMessage(TextBlock textBlock, string message, int displayTimeInMilliseconds)
        {
            if (messageTimers.ContainsKey(textBlock))
            {
                var (timer, _) = messageTimers[textBlock];
                timer.Stop();  // Stop the existing timer if it's running
            }
            else
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(displayTimeInMilliseconds) };
                timer.Tick += (sender, args) =>
                {
                    timer.Stop();
                    textBlock.Text = messageTimers[textBlock].OriginalText;
                };
                messageTimers[textBlock] = (timer, textBlock.Text); // Save the original text and timer
            }

            textBlock.Text = message;
            messageTimers[textBlock].Timer.Start();
        }
    }
}
