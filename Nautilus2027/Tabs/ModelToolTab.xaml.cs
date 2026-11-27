#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nautilus2027.Formats;
using Ookii.Dialogs.Wpf;

namespace Nautilus2027
{
    // "MDL / HKX" tab: convert models, convert collisions, and turn models into collisions.
    public partial class ModelToolTab : UserControl
    {
        private static MainWindow Main => MainWindow.Instance;

        internal static string OutputRoot => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");
        private static string ModelOutputDirectory(string format) => Path.Combine(Main._settings.ModelToolDirectory, format);
        private static string CollisionOutputDirectory(string format) => Path.Combine(Main._settings.CollisionToolDirectory, format);

        public ModelToolTab()
        {
            InitializeComponent();
            MaxTextureSizeComboBox.ItemsSource = MaxTextureSizeOptions;
            MaxTextureSizeComboBox.SelectedIndex = 0;
            CollisionComplexityComboBox.ItemsSource = CollisionTools.ComplexityNames;
            CollisionComplexityComboBox.SelectedIndex = 1;   // 2 - Simple: the preset verified in Home
        }

        // Output formats chosen with the radio buttons (each RadioButton's Tag is the format).
        private string _modelTarget = "MDL";
        private string _collisionTarget = "HKX";

        private void ModelTargetRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string format)
                _modelTarget = format;
        }

        private void CollisionTargetRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string format)
                _collisionTarget = format;
        }

        /// <summary>Collision complexity preset 1 (fewest, biggest boxes) .. 5 (exact, largest files).</summary>
        private int SelectedComplexity => Math.Max(0, CollisionComplexityComboBox.SelectedIndex) + 1;

        private static readonly string[] MaxTextureSizeOptions = { "Don't change", "2048", "1536", "1024", "512", "256" };

        /// <summary>Selected max texture size in pixels, or null for "Don't change".</summary>
        private int? SelectedMaxTextureSize =>
            int.TryParse(MaxTextureSizeComboBox.SelectedItem as string, out int size) ? size : (int?)null;

        // ------------------------------------------------------------------ model side

        private void MdlDragDropHandler(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                FileListTools.AddFilesOrObjects((string[])e.Data.GetData(DataFormats.FileDrop), ModelConverter.ModelExtensions, MDLToolTextBox, MDLToolDragAreaText, "Model Converter");
        }

        private void ClickToBrowseMdlHandler(object sender, MouseButtonEventArgs e) =>
            FileListTools.BrowseForFiles($"Model files|{FileListTools.ExtensionFilter(ModelConverter.ModelExtensions)}", ModelConverter.ModelExtensions, MDLToolTextBox, MDLToolDragAreaText, "Model Converter", objects: true);

        private async void ModelConvertButton_Click(object sender, RoutedEventArgs e)
        {
            string target = _modelTarget;
            int maxTextureSize = SelectedMaxTextureSize ?? 0;
            var overwrite = Main._settings.FileOverwriteBehavior;
            await RunConversion(MDLToolTextBox, MDLToolDragAreaText, ModelConverter.ModelExtensions, "Model Converter", target,
                file => ModelConverter.ConvertModel(file, target, ModelOutputDirectory(target), overwrite, maxTextureSize));
        }

        private async void ModelInspectButton_Click(object sender, RoutedEventArgs e) =>
            await RunInspect(MDLToolTextBox, MDLToolDragAreaText, ModelConverter.ModelExtensions, "Model Converter");

        // ------------------------------------------------------------------ collision side

        private void HkxDragDropHandler(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                FileListTools.AddFilesOrObjects((string[])e.Data.GetData(DataFormats.FileDrop), ModelConverter.CollisionExtensions, HKXToolTextBox, HKXToolDragAreaText, "Collision Converter");
        }

        private void ClickToBrowseHkxHandler(object sender, MouseButtonEventArgs e) =>
            FileListTools.BrowseForFiles($"Collision and model files|{FileListTools.ExtensionFilter(ModelConverter.CollisionExtensions)}", ModelConverter.CollisionExtensions, HKXToolTextBox, HKXToolDragAreaText, "Collision Converter", objects: true);

        private async void CollisionConvertButton_Click(object sender, RoutedEventArgs e)
        {
            string target = _collisionTarget;
            var overwrite = Main._settings.FileOverwriteBehavior;
            int complexity = SelectedComplexity;
            await RunConversion(HKXToolTextBox, HKXToolDragAreaText, ModelConverter.CollisionExtensions, "Collision Converter", target,
                file => ModelConverter.ConvertToCollision(file, target, CollisionOutputDirectory(target), overwrite, complexity));
        }

        private async void CollisionInspectButton_Click(object sender, RoutedEventArgs e) =>
            await RunInspect(HKXToolTextBox, HKXToolDragAreaText, ModelConverter.CollisionExtensions, "Collision Converter");

        // ------------------------------------------------------------------ shared actions

        internal static async Task RunConversion(TextBox list, TextBlock dragText, string[] extensions, string toolName, string target,
            Func<string, ModelConverter.Result> convert)
        {
            var files = FileListTools.TakeFiles(list, extensions);
            if (files.Count == 0)
            {
                TemporaryMessageHelper.ShowTemporaryMessage(dragText, "No files listed.", 2000);
                return;
            }

            Main.LogDebugInfo($"{toolName}: Converting {files.Count} file(s) to {target}");
            LogBox.Line(list, $"-- Converting {files.Count} file(s) to {target}...");
            TemporaryMessageHelper.ShowTemporaryMessage(dragText, $"Converting {files.Count} file(s) to {target}...", 2000);

            var (report, converted) = await Task.Run(() =>
            {
                var sb = new StringBuilder();
                int ok = 0;
                foreach (var file in files)
                {
                    string name = Path.GetFileName(file);
                    try
                    {
                        var result = convert(file);
                        if (result == null)
                        {
                            sb.AppendLine($"-- SKIPPED: {name} output already exists - See settings to change");
                            continue;
                        }
                        ok++;
                        sb.AppendLine($"-- Success: {name} -> {result.OutputPath}" + (result.Renamed ? " (Renamed)" : "") + $" ({result.Summary})");
                        foreach (var detail in result.Details)
                            sb.AppendLine($"     {detail}");
                    }
                    catch (NotSupportedException ex)
                    {
                        sb.AppendLine($"-- Not supported: {name}: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"-- ERROR: {name}: {ex.Message}");
                    }
                }
                sb.AppendLine($"-- {ok} of {files.Count} file(s) converted to {target}.");
                return (sb.ToString(), ok);
            });

            FileListTools.AppendReport(list, report);
            Main.LogDebugInfo($"{toolName}: {converted} of {files.Count} file(s) converted to {target}");
            TemporaryMessageHelper.ShowTemporaryMessage(dragText, $"{converted} of {files.Count} file(s) converted", 3000);
        }

        private static async Task RunInspect(TextBox list, TextBlock dragText, string[] extensions, string toolName)
        {
            var files = FileListTools.ListedFiles(list, extensions);
            if (files.Count == 0)
            {
                TemporaryMessageHelper.ShowTemporaryMessage(dragText, "No files listed.", 2000);
                return;
            }

            Main.LogDebugInfo($"{toolName}: Inspecting {files.Count} file(s)");
            string report = await Task.Run(() =>
            {
                var sb = new StringBuilder();
                foreach (var file in files)
                {
                    try
                    {
                        sb.Append(ModelConverter.Describe(file));
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"-- ERROR: {Path.GetFileName(file)}: {ex.Message}");
                    }
                }
                return sb.ToString();
            });

            FileListTools.AppendReport(list, report);
            TemporaryMessageHelper.ShowTemporaryMessage(dragText, $"Inspected {files.Count} file(s)", 3000);
        }

        // ------------------------------------------------------------------ preview

        private void ModelPreviewButton_Click(object sender, RoutedEventArgs e) =>
            OpenPreview(MDLToolTextBox, MDLToolDragAreaText, ModelConverter.ModelExtensions, "Model Preview", ModelPreview.ForModel);

        private void CollisionPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            int complexity = SelectedComplexity;
            OpenPreview(HKXToolTextBox, HKXToolDragAreaText, ModelConverter.CollisionExtensions, "Collision Preview", f => ModelPreview.ForCollision(f, complexity));
        }

        /// <summary>
        /// The 3D viewer for a tool's listed files (and converted outputs on result lines), starting with the file on the
        /// line last clicked.
        /// </summary>
        internal static void OpenPreview(TextBox list, TextBlock dragText, string[] extensions, string title, Func<string, PreviewData> load)
        {
            var files = FileListTools.ListedFiles(list, extensions);
            string clicked = FileListTools.FileOnClickedLine(list);
            if (clicked != null && FileListTools.HasExtension(clicked, extensions) && !files.Contains(clicked, StringComparer.OrdinalIgnoreCase))
                files.Insert(0, clicked);
            if (files.Count == 0)
            {
                TemporaryMessageHelper.ShowTemporaryMessage(dragText, "No files listed to preview.", 2000);
                return;
            }
            int start = clicked != null ? Math.Max(0, files.FindIndex(f => string.Equals(f, clicked, StringComparison.OrdinalIgnoreCase))) : 0;
            try
            {
                Main.LogDebugInfo($"{title}: opening the viewer on {files.Count} file(s)");
                var w = new PreviewWindow(title, files, start, load) { Owner = Main };
                w.Show();
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"{title}: could not open the viewer: {ex.Message}");
                TemporaryMessageHelper.ShowTemporaryMessage(dragText, "Could not open the viewer", 2000);
            }
        }

        /// <summary>Double-click a listed file or result to show it in Explorer; elsewhere opens the output folder.</summary>
        private void ToolTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is TextBox box))
                return;
            string folder = box == MDLToolTextBox ? ModelOutputDirectory(_modelTarget) : CollisionOutputDirectory(_collisionTarget);
            FileListTools.ShowInExplorer(FileListTools.FileOnClickedLine(box), folder, "MDL / HKX");
        }

        // Handler used in this tab's XAML that lives in MainWindow.
        private void ClearListHandler(object sender, RoutedEventArgs e) => Main.ClearListHandler(sender, e);
    }
}
