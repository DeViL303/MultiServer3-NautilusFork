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
    // "ATMOS / CDATA" tab: Home sky files (.ATMOS + .CDATA) to JSON / GLB / FBX and back, and the cloud sprite data
    // (.CDATA) on its own to JSON / CSV / PLY and back (Formats/AtmosEfxConverter.cs, CdataConverter.cs, Home/HmAtmos.cs).
    public partial class AtmosEffectsTab : UserControl
    {
        private static MainWindow Main => MainWindow.Instance;

        private static readonly string[] AtmosExtensions = AtmosEfxConverter.AtmosExtensions;
        private static readonly string[] CdataExtensions = AtmosEfxConverter.CdataExtensions;

        private static string AtmosOutputDirectory(string format) => Path.Combine(Main._settings.AtmosToolDirectory, format);
        private static string CdataOutputDirectory(string format) => Path.Combine(Main._settings.CdataToolDirectory, format);

        private string _atmosTarget = "ATMOS";
        private string _cdataTarget = "CDATA";

        public AtmosEffectsTab()
        {
            InitializeComponent();
        }

        // ------------------------------------------------------------------ ATMOS side

        private void AtmosDragDropHandler(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                FileListTools.AddFilesOrObjects((string[])e.Data.GetData(DataFormats.FileDrop), AtmosExtensions, ATMOSToolTextBox, ATMOSToolDragAreaText, "ATMOS Converter", AtmosEfxConverter.AcceptsAtmos);
        }

        private void ClickToBrowseAtmosHandler(object sender, MouseButtonEventArgs e) =>
            FileListTools.BrowseForFiles($"Sky files|{FileListTools.ExtensionFilter(AtmosExtensions)}", AtmosExtensions, ATMOSToolTextBox, ATMOSToolDragAreaText, "ATMOS Converter", objects: true, accept: AtmosEfxConverter.AcceptsAtmos);

        private void AtmosTargetRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string format)
                _atmosTarget = format;
        }

        private async void AtmosConvertButton_Click(object sender, RoutedEventArgs e)
        {
            string target = _atmosTarget;
            var overwrite = Main._settings.FileOverwriteBehavior;
            await ModelToolTab.RunConversion(ATMOSToolTextBox, ATMOSToolDragAreaText, AtmosExtensions, "ATMOS Converter", target,
                file => AtmosEfxConverter.ConvertAtmos(file, target, AtmosOutputDirectory(target), overwrite));
        }

        private async void AtmosInspectButton_Click(object sender, RoutedEventArgs e) =>
            await Inspect(ATMOSToolTextBox, ATMOSToolDragAreaText, AtmosExtensions, AtmosEfxConverter.DescribeAtmos);

        private void AtmosPreviewButton_Click(object sender, RoutedEventArgs e) =>
            ModelToolTab.OpenPreview(ATMOSToolTextBox, ATMOSToolDragAreaText, AtmosExtensions, "ATMOS Preview", AtmosEfxConverter.PreviewAtmos);

        // ------------------------------------------------------------------ CDATA side

        private void CdataDragDropHandler(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                FileListTools.AddFilesOrObjects((string[])e.Data.GetData(DataFormats.FileDrop), CdataExtensions, CDATAToolTextBox, CDATAToolDragAreaText, "CDATA Converter", AtmosEfxConverter.AcceptsCdata);
        }

        private void ClickToBrowseCdataHandler(object sender, MouseButtonEventArgs e) =>
            FileListTools.BrowseForFiles($"Cloud sprite data|{FileListTools.ExtensionFilter(CdataExtensions)}", CdataExtensions, CDATAToolTextBox, CDATAToolDragAreaText, "CDATA Converter", objects: true, accept: AtmosEfxConverter.AcceptsCdata);

        private void CdataTargetRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string format)
                _cdataTarget = format;
        }

        private async void CdataConvertButton_Click(object sender, RoutedEventArgs e)
        {
            string target = _cdataTarget;
            var overwrite = Main._settings.FileOverwriteBehavior;
            await ModelToolTab.RunConversion(CDATAToolTextBox, CDATAToolDragAreaText, CdataExtensions, "CDATA Converter", target,
                file => AtmosEfxConverter.ConvertCdata(file, target, CdataOutputDirectory(target), overwrite));
        }

        private async void CdataInspectButton_Click(object sender, RoutedEventArgs e) =>
            await Inspect(CDATAToolTextBox, CDATAToolDragAreaText, CdataExtensions, AtmosEfxConverter.DescribeCdata);

        private void CdataPreviewButton_Click(object sender, RoutedEventArgs e) =>
            ModelToolTab.OpenPreview(CDATAToolTextBox, CDATAToolDragAreaText, CdataExtensions, "CDATA Preview", AtmosEfxConverter.PreviewCdata);

        // ------------------------------------------------------------------ shared

        private static async Task Inspect(TextBox list, TextBlock dragText, string[] extensions, Func<string, string> describe)
        {
            var files = FileListTools.ListedFiles(list, extensions);
            if (files.Count == 0)
            {
                TemporaryMessageHelper.ShowTemporaryMessage(dragText, "No files listed.", 2000);
                return;
            }
            Main.LogDebugInfo($"ATMOS / CDATA: Inspecting {files.Count} file(s)");
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
                    box == ATMOSToolTextBox ? AtmosOutputDirectory(_atmosTarget) : CdataOutputDirectory(_cdataTarget), "ATMOS / CDATA");
        }

        // Handler used in this tab's XAML that lives in MainWindow.
        private void ClearListHandler(object sender, RoutedEventArgs e) => Main.ClearListHandler(sender, e);
    }
}
