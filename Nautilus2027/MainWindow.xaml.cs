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
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private Dictionary<int, string> uuidMappings;
        internal static bool DeployHCDBFlag = false;
        internal static bool DeploySceneListFlag = false;
        private FileScannerStandalone fileScanner;
        // The single main window; tabs use this to reach shared state.
        internal static MainWindow Instance { get; private set; } = null!;

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();

            CatalogueTabView.InitializeWebView2();

            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            LoggerAccessor.SetupLogger("Nautilus2027", Directory.GetCurrentDirectory());
            fileScanner = new FileScannerStandalone(CheckerTabView.Sha1TextBox);

            DataContext = this;
            // Load settings when the window initializes
            _settings = SettingsManager.LoadSettings();

            // Convert the theme color from settings to a Color object
            Color themeColor = (Color)ColorConverter.ConvertFromString(_settings.ThemeColor);

            // Convert the theme color to a SolidColorBrush
            _selectedThemeColor = new SolidColorBrush(themeColor);

            // Apply the theme color to the UI
            ApplySettingsToUI();

            // Update the taskbar icon with the theme color from settings
            SettingsTabView.UpdateTaskbarIconWithTint(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies\\icon.png"), themeColor);

            AFSClass.MapperHelperFolder = Directory.GetCurrentDirectory();

            _ = new Timer(AFSClass.ScheduledUpdate, null, TimeSpan.Zero, TimeSpan.FromMinutes(1440));

            logFlushTimer = new Timer(_ => FlushLogBuffer(), null, Timeout.Infinite, Timeout.Infinite);

            DbEditorTabView.InitializeComboBoxEventHandlers();
            this.Loaded += ArchiveToolTabView.MainWindow_Loaded;
            this.Loaded += (s, e) => ApplyUiScale();

            SceneListTabView.txtSceneType.SelectedItem = SceneListTabView.txtSceneType.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Content.ToString() == "GlobalSpace");
            SceneListTabView.txtdHost.SelectedItem = SceneListTabView.txtdHost.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Content.ToString() == "en-US");
        }




        public static Dictionary<string, string> SceneFileMappings { get; private set; } = new Dictionary<string, string>();


        // Title BAR Controls

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = System.Windows.WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {

        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                this.DragMove();
        }

        public event PropertyChangedEventHandler? PropertyChanged = delegate { };

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl && e.AddedItems.Count > 0)
            {
                // Assuming each TabItem's Tag property is set to the corresponding enum value
                var selectedTab = (TabItem)e.AddedItems[0];
                if (Enum.TryParse<RememberLastTabUsed>(selectedTab.Tag.ToString(), out var lastTabUsed))
                {
                    _settings.LastTabUsed = lastTabUsed;
                    SettingsManager.SaveSettings(_settings);
                }
            }
        }

        private bool isAddToMapperButtonClickedOnce = false;

        // Tab-level handlers (attached to the TabItem itself) forwarded to the tab.
        private void Path2HashTabItem_Drop(object sender, DragEventArgs e) => Path2HashTabView.Path2HashTabItem_Drop(sender, e);
        private void SceneIDTabItem_Drop(object sender, DragEventArgs e) => SceneIdsTabView.SceneIDTabItem_Drop(sender, e);
    }
}
