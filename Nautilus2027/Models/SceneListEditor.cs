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
    public class SceneListEditor : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged(string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string id;
        public string ID
        {
            get { return id; }
            set { if (id != value) { id = value; NotifyPropertyChanged(nameof(ID)); } }
        }

        private string type;
        public string Type
        {
            get { return type; }
            set { if (type != value) { type = value; NotifyPropertyChanged(nameof(Type)); } }
        }

        private string description;
        public string Description
        {
            get { return description; }
            set { if (description != value) { description = value; NotifyPropertyChanged(nameof(Description)); } }
        }

        private string config;
        public string Config
        {
            get { return config; }
            set { if (config != value) { config = value; NotifyPropertyChanged(nameof(Config)); } }
        }

        private string sceneID;
        public string SceneID
        {
            get { return sceneID; }
            set { if (sceneID != value) { sceneID = value; NotifyPropertyChanged(nameof(SceneID)); } }
        }

        private string version;
        public string Version
        {
            get { return version; }
            set { if (version != value) { version = value; NotifyPropertyChanged(nameof(Version)); } }
        }

        private string sha1;
        public string SHA1
        {
            get { return sha1; }
            set { if (sha1 != value) { sha1 = value; NotifyPropertyChanged(nameof(SHA1)); } }
        }

        private string flags;
        public string Flags
        {
            get { return flags; }
            set { if (flags != value) { flags = value; NotifyPropertyChanged(nameof(Flags)); } }
        }

        private string softcap;
        public string Softcap
        {
            get { return softcap; }
            set { if (softcap != value) { softcap = value; NotifyPropertyChanged(nameof(Softcap)); } }
        }

        private string capacity;
        public string Capacity
        {
            get { return capacity; }
            set { if (capacity != value) { capacity = value; NotifyPropertyChanged(nameof(Capacity)); } }
        }

        private string host;
        public string Host
        {
            get { return host; }
            set { if (host != value) { host = value; NotifyPropertyChanged(nameof(Host)); } }
        }

        private string homeUID;
        public string HomeUID
        {
            get { return homeUID; }
            set { if (homeUID != value) { homeUID = value; NotifyPropertyChanged(nameof(HomeUID)); } }
        }
    }
}
