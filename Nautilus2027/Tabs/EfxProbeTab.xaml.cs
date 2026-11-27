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
    // "EFX / PROBE" tab: particle effects (.EFX, and where scenes place them) and light probes (.PROBE) to JSON / GLB
    // and back (Formats/AtmosEfxConverter.cs, Home/HmEfx.cs, Home/HmProbe.cs, HomeScene.cs).
    public partial class EfxProbeTab : UserControl
    {
        private static MainWindow Main => MainWindow.Instance;

        private static readonly string[] EfxExtensions = AtmosEfxConverter.EfxExtensions;
        private static readonly string[] ProbeExtensions = AtmosEfxConverter.ProbeExtensions;

        private static string EfxOutputDirectory(string format) => Path.Combine(Main._settings.EfxToolDirectory, format);
        private static string ProbeOutputDirectory(string format) => Path.Combine(Main._settings.ProbeToolDirectory, format);

        private string _efxTarget = "EFX";
        private string _probeTarget = "PROBE";

        public EfxProbeTab()
        {
            InitializeComponent();
        }

        // ------------------------------------------------------------------ EFX side

        private void EfxDragDropHandler(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                FileListTools.AddFilesOrObjects((string[])e.Data.GetData(DataFormats.FileDrop), EfxExtensions, EFXToolTextBox, EFXToolDragAreaText, "EFX Converter", AtmosEfxConverter.AcceptsEfx);
        }

        private void ClickToBrowseEfxHandler(object sender, MouseButtonEventArgs e) =>
            FileListTools.BrowseForFiles($"Particle effects|{FileListTools.ExtensionFilter(EfxExtensions)}", EfxExtensions, EFXToolTextBox, EFXToolDragAreaText, "EFX Converter", objects: true, accept: AtmosEfxConverter.AcceptsEfx);

        private void EfxTargetRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string format)
                _efxTarget = format;
        }

        private async void EfxConvertButton_Click(object sender, RoutedEventArgs e)
        {
            string target = _efxTarget;
            var overwrite = Main._settings.FileOverwriteBehavior;
            await ModelToolTab.RunConversion(EFXToolTextBox, EFXToolDragAreaText, EfxExtensions, "EFX Converter", target,
                file => AtmosEfxConverter.ConvertEfx(file, target, EfxOutputDirectory(target), overwrite));
        }

        private async void EfxInspectButton_Click(object sender, RoutedEventArgs e) =>
            await Inspect(EFXToolTextBox, EFXToolDragAreaText, EfxExtensions, AtmosEfxConverter.DescribeEfx);

        private void EfxPreviewButton_Click(object sender, RoutedEventArgs e) =>
            ModelToolTab.OpenPreview(EFXToolTextBox, EFXToolDragAreaText, EfxExtensions, "EFX Preview", AtmosEfxConverter.PreviewEfx);

        // ------------------------------------------------------------------ PROBE side

        private void ProbeDragDropHandler(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                FileListTools.AddFilesOrObjects((string[])e.Data.GetData(DataFormats.FileDrop), ProbeExtensions, PROBEToolTextBox, PROBEToolDragAreaText, "PROBE Converter", AtmosEfxConverter.AcceptsProbe);
        }

        private void ClickToBrowseProbeHandler(object sender, MouseButtonEventArgs e) =>
            FileListTools.BrowseForFiles($"Light probes|{FileListTools.ExtensionFilter(ProbeExtensions)}", ProbeExtensions, PROBEToolTextBox, PROBEToolDragAreaText, "PROBE Converter", objects: true, accept: AtmosEfxConverter.AcceptsProbe);

        private void ProbeTargetRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string format)
                _probeTarget = format;
        }

        private async void ProbeConvertButton_Click(object sender, RoutedEventArgs e)
        {
            string target = _probeTarget;
            var overwrite = Main._settings.FileOverwriteBehavior;
            await ModelToolTab.RunConversion(PROBEToolTextBox, PROBEToolDragAreaText, ProbeExtensions, "PROBE Converter", target,
                file => AtmosEfxConverter.ConvertProbe(file, target, ProbeOutputDirectory(target), overwrite));
        }

        private async void ProbeInspectButton_Click(object sender, RoutedEventArgs e) =>
            await Inspect(PROBEToolTextBox, PROBEToolDragAreaText, ProbeExtensions, AtmosEfxConverter.DescribeProbe);

        private void ProbePreviewButton_Click(object sender, RoutedEventArgs e) =>
            ModelToolTab.OpenPreview(PROBEToolTextBox, PROBEToolDragAreaText, ProbeExtensions, "PROBE Preview", AtmosEfxConverter.PreviewProbe);

        // ------------------------------------------------------------------ shared

        private static async Task Inspect(TextBox list, TextBlock dragText, string[] extensions, Func<string, string> describe)
        {
            var files = FileListTools.ListedFiles(list, extensions);
            if (files.Count == 0)
            {
                TemporaryMessageHelper.ShowTemporaryMessage(dragText, "No files listed.", 2000);
                return;
            }
            Main.LogDebugInfo($"EFX / PROBE: Inspecting {files.Count} file(s)");
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
                    box == EFXToolTextBox ? EfxOutputDirectory(_efxTarget) : ProbeOutputDirectory(_probeTarget), "EFX / PROBE");
        }

        // Handler used in this tab's XAML that lives in MainWindow.
        private void ClearListHandler(object sender, RoutedEventArgs e) => Main.ClearListHandler(sender, e);
    }
}
