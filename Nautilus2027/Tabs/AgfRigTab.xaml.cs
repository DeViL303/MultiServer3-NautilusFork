#nullable disable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Nautilus2027.Formats;

namespace Nautilus2027
{
    // "AGF / RIG" tab: avatar TexGen layers (.AGF) to / from PNG and other images, and head face rigs (.RIG) to / from JSON
    // (Formats/AgfRigConverter.cs, Home/HmAgf.cs, Home/HmRig.cs).
    public partial class AgfRigTab : UserControl
    {
        private static MainWindow Main => MainWindow.Instance;

        private static readonly string[] AgfExtensions = AgfRigConverter.AgfExtensions;
        private static readonly string[] RigExtensions = AgfRigConverter.RigExtensions;

        private static string AgfOutputDirectory(string format) => Path.Combine(Main._settings.AgfToolDirectory, format);
        private static string RigOutputDirectory(string format) => Path.Combine(Main._settings.RigToolDirectory, format);

        private string _agfTarget = "AGF";
        private string _rigTarget = "RIG";

        public AgfRigTab()
        {
            InitializeComponent();
        }

        // ------------------------------------------------------------------ AGF side

        private void AgfDragDropHandler(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                FileListTools.AddFilesOrObjects((string[])e.Data.GetData(DataFormats.FileDrop), AgfExtensions, AGFToolTextBox, AGFToolDragAreaText, "AGF Converter");
        }

        private void ClickToBrowseAgfHandler(object sender, MouseButtonEventArgs e) =>
            FileListTools.BrowseForFiles($"TexGen layers and images|{FileListTools.ExtensionFilter(AgfExtensions)}", AgfExtensions, AGFToolTextBox, AGFToolDragAreaText, "AGF Converter", objects: true);

        private void AgfTargetRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string format)
                _agfTarget = format;
        }

        private async void AgfConvertButton_Click(object sender, RoutedEventArgs e)
        {
            string target = _agfTarget;
            var overwrite = Main._settings.FileOverwriteBehavior;
            await ModelToolTab.RunConversion(AGFToolTextBox, AGFToolDragAreaText, AgfExtensions, "AGF Converter", target,
                file => AgfRigConverter.ConvertAgf(file, target, AgfOutputDirectory(target), overwrite));
        }

        private async void AgfInspectButton_Click(object sender, RoutedEventArgs e) =>
            await Inspect(AGFToolTextBox, AGFToolDragAreaText, AgfExtensions, AgfRigConverter.DescribeAgf);

        private void AgfPreviewButton_Click(object sender, RoutedEventArgs e) =>
            ModelToolTab.OpenPreview(AGFToolTextBox, AGFToolDragAreaText, AgfExtensions, "AGF Preview", AgfRigConverter.PreviewAgf);

        // ------------------------------------------------------------------ RIG side

        private void RigDragDropHandler(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                FileListTools.AddFilesOrObjects((string[])e.Data.GetData(DataFormats.FileDrop), RigExtensions, RIGToolTextBox, RIGToolDragAreaText, "RIG Converter");
        }

        private void ClickToBrowseRigHandler(object sender, MouseButtonEventArgs e) =>
            FileListTools.BrowseForFiles($"Face rigs|{FileListTools.ExtensionFilter(RigExtensions)}", RigExtensions, RIGToolTextBox, RIGToolDragAreaText, "RIG Converter", objects: true);

        private void RigTargetRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string format)
                _rigTarget = format;
        }

        private async void RigConvertButton_Click(object sender, RoutedEventArgs e)
        {
            string target = _rigTarget;
            var overwrite = Main._settings.FileOverwriteBehavior;
            await ModelToolTab.RunConversion(RIGToolTextBox, RIGToolDragAreaText, RigExtensions, "RIG Converter", target,
                file => AgfRigConverter.ConvertRig(file, target, RigOutputDirectory(target), overwrite));
        }

        private async void RigInspectButton_Click(object sender, RoutedEventArgs e) =>
            await Inspect(RIGToolTextBox, RIGToolDragAreaText, RigExtensions, AgfRigConverter.DescribeRig);

        // ------------------------------------------------------------------ shared

        private static async Task Inspect(TextBox list, TextBlock dragText, string[] extensions, Func<string, string> describe)
        {
            var files = FileListTools.ListedFiles(list, extensions);
            if (files.Count == 0)
            {
                TemporaryMessageHelper.ShowTemporaryMessage(dragText, "No files listed.", 2000);
                return;
            }
            Main.LogDebugInfo($"AGF / RIG: Inspecting {files.Count} file(s)");
            string report = await Task.Run(() =>
            {
                var sb = new StringBuilder();
                foreach (var file in files)
                {
                    try { sb.Append(describe(file)); }
                    catch (Exception ex) { sb.AppendLine($"-- ERROR: {Path.GetFileName(file)}: {ex.Message}"); }
                }
                return sb.ToString();
            });
            FileListTools.AppendReport(list, report);
            TemporaryMessageHelper.ShowTemporaryMessage(dragText, $"Inspected {files.Count} file(s)", 3000);
        }

        private void ToolTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox box)
                FileListTools.ShowInExplorer(FileListTools.FileOnClickedLine(box),
                    box == AGFToolTextBox ? AgfOutputDirectory(_agfTarget) : RigOutputDirectory(_rigTarget), "AGF / RIG");
        }

        // Handler used in this tab's XAML that lives in MainWindow.
        private void ClearListHandler(object sender, RoutedEventArgs e) => Main.ClearListHandler(sender, e);
    }
}
