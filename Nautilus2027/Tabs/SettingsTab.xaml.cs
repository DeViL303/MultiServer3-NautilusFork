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
    // "Settings" tab.
    public partial class SettingsTab : UserControl
    {
        private static MainWindow Main => MainWindow.Instance;

        public SettingsTab()
        {
            InitializeComponent();
        }

        // Fill this tab's controls from the saved settings (called once at startup).
        internal void ApplySettings(AppSettings settings)
        {
            BuildOutputFolderRows(settings);
            _loadingScale = true;
            UiScaleCombo.SelectedItem = UiScaleCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == settings.UiScalePercent.ToString()) ?? UiScaleCombo.Items[0];
            _loadingScale = false;
            ToggleSwitchLiteCatalogue.IsChecked = settings.LiteCatalogueEnabled;
            ThemeColorPicker.SelectedColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.ThemeColor);
            switch (settings.SaveDebugLogToggle)
            {
                case SaveDebugLog.True:
                    ToggleSwitchDebugLogEnable.IsChecked = true;
                    break;
                case SaveDebugLog.False:
                    ToggleSwitchDebugLogEnable.IsChecked = false;
                    break;
            }
            ToggleSwitchOfflinePshome.IsChecked = settings.UseOnlineCatalogue; // Apply offline mode setting
        }

        private void ToggleSwitchLiteCatalogue_Checked(object sender, RoutedEventArgs e)
        {
            Main._settings.LiteCatalogueEnabled = ToggleSwitchLiteCatalogue.IsChecked == true;
            SettingsManager.SaveSettings(Main._settings);
        }


        private void ToggleSwitchOfflineCheckedChanged(object sender, RoutedEventArgs e)
        {
            // Update the setting based on the toggle switch state
            Main._settings.UseOnlineCatalogue = ToggleSwitchOfflinePshome.IsChecked == true;

            // Save settings to persist the change
            SettingsManager.SaveSettings(Main._settings);

        }


        private void CheckBoxDebugLogsEnable_Checked(object sender, RoutedEventArgs e)
        {
            if (Main._settings != null)
            {
                Main._settings.SaveDebugLogToggle = SaveDebugLog.True;
                SettingsManager.SaveSettings(Main._settings);
            }
        }

        private void CheckBoxDebugLogsEnable_UnChecked(object sender, RoutedEventArgs e)
        {
            if (Main._settings != null)
            {
                Main._settings.SaveDebugLogToggle = SaveDebugLog.False;
                SettingsManager.SaveSettings(Main._settings);
            }
        }

        private void NewColorPicker_ColorChanged(object sender, EventArgs e)
        {
            var colorPicker = sender as StandardColorPicker;
            if (colorPicker != null)
            {
                // Directly use SelectedColor as it is not nullable
                Color newColor = colorPicker.SelectedColor;
                Main.SelectedThemeColor = new SolidColorBrush(newColor);
                Main._settings.ThemeColor = newColor.ToString();
                SettingsManager.SaveSettings(Main._settings);

                // Assuming your icon is a resource in the project, otherwise provide a full path
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies\\icon.png");
                UpdateTaskbarIconWithTint(iconPath, newColor);
            }
        }


        internal void UpdateTaskbarIconWithTint(string iconPath, System.Windows.Media.Color tint)
        {
            // Load the existing icon
            BitmapImage originalIcon = new BitmapImage(new Uri(iconPath));

            // Create a WriteableBitmap from the original icon
            WriteableBitmap writeableBitmap = new WriteableBitmap(originalIcon);

            // Iterate over all pixels to apply the tint only on non-transparent pixels
            writeableBitmap.Lock();
            unsafe
            {
                // Pointer to the back buffer
                IntPtr buffer = writeableBitmap.BackBuffer;
                int stride = writeableBitmap.BackBufferStride;

                // Tint strength (0-255)
                byte tintStrength = 255; // Maximum strength

                for (int y = 0; y < writeableBitmap.PixelHeight; y++)
                {
                    for (int x = 0; x < writeableBitmap.PixelWidth; x++)
                    {
                        // Calculate the pixel's position
                        int position = y * stride + x * 4;

                        // Apply tint only to non-transparent pixels
                        byte* pixel = (byte*)buffer.ToPointer() + position;
                        byte alpha = pixel[3];
                        if (alpha > 0) // This checks if the pixel is not fully transparent
                        {
                            pixel[0] = (byte)((pixel[0] * (255 - tintStrength) + tint.B * tintStrength) / 255);
                            pixel[1] = (byte)((pixel[1] * (255 - tintStrength) + tint.G * tintStrength) / 255);
                            pixel[2] = (byte)((pixel[2] * (255 - tintStrength) + tint.R * tintStrength) / 255);
                        }
                    }
                }
            }
            writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, writeableBitmap.PixelWidth, writeableBitmap.PixelHeight));
            writeableBitmap.Unlock();

            // Use the WriteableBitmap as the window's icon
            Main.Icon = writeableBitmap;
        }


        private void ViewLogButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Determine the path to the log file next to the executable
                string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string logFilePath = Path.Combine(exeDirectory, "logs", "debug.log");

                // Check if the log file exists
                if (File.Exists(logFilePath))
                {
                    // Use Process.Start to open the log file with the default associated application
                    Process.Start(new ProcessStartInfo(logFilePath) { UseShellExecute = true });
                }
                else
                {
                    // Show a message if the log file does not exist
                    MessageBox.Show("Log file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                // Log the exception and display an error message
                Main.LogDebugInfo($"Error opening log file: {ex.Message}");
                MessageBox.Show($"Failed to open the log file. Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Settings Tab - Set directories


        // ------------------------------------------------------------------ interface size

        private bool _loadingScale;

        private void UiScaleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Main?._settings == null || !(UiScaleCombo.SelectedItem is ComboBoxItem item) || !int.TryParse(item.Tag as string, out int pct)) return;
            if (!_loadingScale)
            {
                Main._settings.UiScalePercent = pct;
                SettingsManager.SaveSettings(Main._settings);
                if (Main.IsLoaded) Main.ApplyUiScale();
            }
            ShowUiScaleInfo();
        }

        internal void ShowUiScaleInfo()
        {
            if (Main == null) return;
            double dpi = 1;
            try { dpi = VisualTreeHelper.GetDpi(Main).DpiScaleX; } catch (Exception) { }
            UiScaleInfo.Text = $"Windows display scale {dpi * 100:0}%, interface {Main.ResolveUiScale() * 100:0}%";
        }

        // ------------------------------------------------------------------ output folders (one per tab)

        private sealed class OutputFolderRow
        {
            public string Label, Tab, Tools;
            public Func<AppSettings, string> Get;
            public Action<AppSettings, string> Set;
        }

        private static readonly OutputFolderRow[] OutputFolderRows =
        {
            new OutputFolderRow { Label = "BAR / SDAT / SHARC:", Tab = "BAR_SDAT_SHARC", Tools = "Created, Unpacked", Get = s => s.ArchiveOutputRoot, Set = (s, v) => s.ArchiveOutputRoot = v },
            new OutputFolderRow { Label = "CDS Tool:", Tab = "CDS", Tools = "Encrypted, Decrypted", Get = s => s.CdsOutputRoot, Set = (s, v) => s.CdsOutputRoot = v },
            new OutputFolderRow { Label = "HCDB / SQL:", Tab = "HCDB_SQL", Tools = "HCDB, SQL, LST (DB Editor too)", Get = s => s.HcdbOutputRoot, Set = (s, v) => s.HcdbOutputRoot = v },
            new OutputFolderRow { Label = "SceneList:", Tab = "SceneList", Tools = "ToDeploy (TSS too)", Get = s => s.SceneListOutputRoot, Set = (s, v) => s.SceneListOutputRoot = v },
            new OutputFolderRow { Label = "LUA / LUAC:", Tab = "LUA_LUAC", Tools = "LUAC (compiler), LUA (decompiler)", Get = s => s.LuaOutputRoot, Set = (s, v) => s.LuaOutputRoot = v },
            new OutputFolderRow { Label = "MDL / HKX:", Tab = "MDL_HKX", Tools = "MDL, HKX", Get = s => s.ModelOutputRoot, Set = (s, v) => s.ModelOutputRoot = v },
            new OutputFolderRow { Label = "ANI / SKN:", Tab = "ANI_SKN", Tools = "ANI, SKN", Get = s => s.AnimationOutputRoot, Set = (s, v) => s.AnimationOutputRoot = v },
            new OutputFolderRow { Label = "ATMOS / CDATA:", Tab = "ATMOS_CDATA", Tools = "ATMOS, CDATA", Get = s => s.AtmosOutputRoot, Set = (s, v) => s.AtmosOutputRoot = v },
            new OutputFolderRow { Label = "EFX / PROBE:", Tab = "EFX_PROBE", Tools = "EFX (and EFX\\Scenes), PROBE", Get = s => s.EfxOutputRoot, Set = (s, v) => s.EfxOutputRoot = v },
            new OutputFolderRow { Label = "AGF / RIG:", Tab = "AGF_RIG", Tools = "AGF, RIG", Get = s => s.AgfRigOutputRoot, Set = (s, v) => s.AgfRigOutputRoot = v },
            new OutputFolderRow { Label = "CDN Audit:", Tab = "CDN_Audit", Tools = "Audit", Get = s => s.CdnAuditOutputRoot, Set = (s, v) => s.CdnAuditOutputRoot = v },
            new OutputFolderRow { Label = "Catalogue:", Tab = "Catalogue", Tools = "Downloads", Get = s => s.CatalogueOutputRoot, Set = (s, v) => s.CatalogueOutputRoot = v },
            new OutputFolderRow { Label = "Media:", Tab = "Media", Tools = "Video, Audio", Get = s => s.MediaOutputRoot, Set = (s, v) => s.MediaOutputRoot = v },
        };

        private void BuildOutputFolderRows(AppSettings settings)
        {
            OutputFoldersPanel.Children.Clear();
            var boxStyle = TryFindResource("SettingsTextBoxStyle") as Style;
            var buttonStyle = TryFindResource("DarkModeButtonSmallStyle") as Style;
            foreach (var row in OutputFolderRows)
            {
                var r = row;
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) };
                panel.Children.Add(new TextBlock { Text = r.Label, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Width = 160, Margin = new Thickness(0, 0, 5, 0) });
                string current = r.Get(settings);
                if (string.IsNullOrWhiteSpace(current)) current = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", r.Tab);
                var box = new TextBox { Width = 450, Text = current, ToolTip = "Tools write to: " + r.Tools };
                if (boxStyle != null) box.Style = boxStyle;
                box.TextChanged += (s, e) =>
                {
                    if (Main?._settings == null) return;
                    r.Set(Main._settings, box.Text);
                    SettingsManager.SaveSettings(Main._settings);
                };
                panel.Children.Add(box);
                var change = new Button { Content = "Change", Cursor = Cursors.Hand, FontSize = 14, Margin = new Thickness(5, 0, 0, 0), Width = 70 };
                if (buttonStyle != null) change.Style = buttonStyle;
                change.Click += (s, e) =>
                {
                    var folder = OpenFolderDialog(Directory.Exists(box.Text) ? box.Text : AppDomain.CurrentDomain.BaseDirectory);
                    if (folder != null) box.Text = folder;
                };
                panel.Children.Add(change);
                var open = new Button { Content = "Open", Cursor = Cursors.Hand, FontSize = 14, Margin = new Thickness(10, 0, 0, 0), Width = 60 };
                if (buttonStyle != null) open.Style = buttonStyle;
                open.Click += (s, e) => OpenOutputFolder(box.Text);
                panel.Children.Add(open);
                panel.Children.Add(new TextBlock { Text = "-> " + r.Tools, Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontSize = 11 });
                OutputFoldersPanel.Children.Add(panel);
            }
        }

        private void OpenOutputFolder(string folder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder)) return;
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true, Verb = "open" });
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Settings: could not open {folder}: {ex.Message}");
                MessageBox.Show($"Error opening directory: {ex.Message}");
            }
        }

        // Helper method to open the folder dialog and return the selected path
        private string OpenFolderDialog(string initialDirectory)
        {
            using (var dialog = new CommonOpenFileDialog())
            {
                dialog.IsFolderPicker = true;
                dialog.InitialDirectory = initialDirectory;
                dialog.EnsurePathExists = true;

                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
#pragma warning disable CS8603 // Possible null reference return.
                    return dialog.FileName;
#pragma warning restore CS8603 // Possible null reference return.
                }
            }
#pragma warning disable CS8603 // Possible null reference return.
            return null;
#pragma warning restore CS8603 // Possible null reference return.
        }


        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }


        // Handlers used in this tab's XAML that live elsewhere.
    }
}
