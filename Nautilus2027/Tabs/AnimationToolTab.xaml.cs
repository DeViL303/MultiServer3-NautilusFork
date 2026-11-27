#nullable disable
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Nautilus2027.Formats;

namespace Nautilus2027
{
    // "ANI / SKN" tab: Home .ANI clips and .SKN skeletons to GLB / FBX, and GLB / FBX back to ANI / SKN
    // (Formats/AnimationConverter.cs, Formats/Home/HmAnimation.cs, HmSkeleton.cs).
    public partial class AnimationToolTab : UserControl
    {
        private static MainWindow Main => MainWindow.Instance;

        private static readonly string[] AnimationExtensions = AnimationConverter.AnimationExtensions;
        private static readonly string[] SkeletonExtensions = AnimationConverter.SkeletonExtensions;

        private static string AnimationOutputDirectory(string format) => Path.Combine(Main._settings.AnimationToolDirectory, format);
        private static string SkeletonOutputDirectory(string format) => Path.Combine(Main._settings.SkeletonToolDirectory, format);

        private string _animationTarget = "ANI";
        private string _skeletonTarget = "SKN";

        public AnimationToolTab()
        {
            InitializeComponent();
        }

        // ------------------------------------------------------------------ animation side

        private void AniDragDropHandler(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                FileListTools.AddFilesOrObjects((string[])e.Data.GetData(DataFormats.FileDrop), AnimationExtensions, ANIToolTextBox, ANIToolDragAreaText, "Animation Converter");
        }

        private void ClickToBrowseAniHandler(object sender, MouseButtonEventArgs e) =>
            FileListTools.BrowseForFiles($"Animation files|{FileListTools.ExtensionFilter(AnimationExtensions)}", AnimationExtensions, ANIToolTextBox, ANIToolDragAreaText, "Animation Converter", objects: true);

        private void AnimationTargetRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string format)
                _animationTarget = format;
        }

        private async void AnimationConvertButton_Click(object sender, RoutedEventArgs e)
        {
            string target = _animationTarget;
            var overwrite = Main._settings.FileOverwriteBehavior;
            await ModelToolTab.RunConversion(ANIToolTextBox, ANIToolDragAreaText, AnimationExtensions, "Animation Converter", target,
                file => AnimationConverter.ConvertAnimation(file, target, AnimationOutputDirectory(target), overwrite));
        }

        private async void AnimationInspectButton_Click(object sender, RoutedEventArgs e) =>
            await Inspect(ANIToolTextBox, ANIToolDragAreaText, AnimationExtensions);

        private void AnimationPreviewButton_Click(object sender, RoutedEventArgs e) =>
            ModelToolTab.OpenPreview(ANIToolTextBox, ANIToolDragAreaText, AnimationExtensions, "Animation Preview", AnimationPreview.ForAnimation);

        // ------------------------------------------------------------------ skeleton side

        private void SknDragDropHandler(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                FileListTools.AddFilesOrObjects((string[])e.Data.GetData(DataFormats.FileDrop), SkeletonExtensions, SKNToolTextBox, SKNToolDragAreaText, "Skeleton Converter");
        }

        private void ClickToBrowseSknHandler(object sender, MouseButtonEventArgs e) =>
            FileListTools.BrowseForFiles($"Skeleton files|{FileListTools.ExtensionFilter(SkeletonExtensions)}", SkeletonExtensions, SKNToolTextBox, SKNToolDragAreaText, "Skeleton Converter", objects: true);

        private void SkeletonTargetRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string format)
                _skeletonTarget = format;
        }

        private async void SkeletonConvertButton_Click(object sender, RoutedEventArgs e)
        {
            string target = _skeletonTarget;
            var overwrite = Main._settings.FileOverwriteBehavior;
            await ModelToolTab.RunConversion(SKNToolTextBox, SKNToolDragAreaText, SkeletonExtensions, "Skeleton Converter", target,
                file => AnimationConverter.ConvertSkeleton(file, target, SkeletonOutputDirectory(target), overwrite));
        }

        private async void SkeletonInspectButton_Click(object sender, RoutedEventArgs e) =>
            await Inspect(SKNToolTextBox, SKNToolDragAreaText, SkeletonExtensions);

        private void SkeletonPreviewButton_Click(object sender, RoutedEventArgs e) =>
            ModelToolTab.OpenPreview(SKNToolTextBox, SKNToolDragAreaText, SkeletonExtensions, "Skeleton Preview", AnimationPreview.ForSkeleton);

        // ------------------------------------------------------------------ shared

        private static async Task Inspect(TextBox list, TextBlock dragText, string[] extensions)
        {
            var files = FileListTools.ListedFiles(list, extensions);
            if (files.Count == 0)
            {
                TemporaryMessageHelper.ShowTemporaryMessage(dragText, "No files listed.", 2000);
                return;
            }

            Main.LogDebugInfo($"ANI / SKN: Inspecting {files.Count} file(s)");
            string report = await Task.Run(() =>
            {
                var sb = new StringBuilder();
                foreach (var file in files)
                {
                    try
                    {
                        sb.Append(Describe(file));
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

        private static string Describe(string path) => AnimationConverter.Describe(path);

        private void ToolTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox box)
                FileListTools.ShowInExplorer(FileListTools.FileOnClickedLine(box),
                    box == ANIToolTextBox ? AnimationOutputDirectory(_animationTarget) : SkeletonOutputDirectory(_skeletonTarget), "ANI / SKN");
        }

        // Handler used in this tab's XAML that lives in MainWindow.
        private void ClearListHandler(object sender, RoutedEventArgs e) => Main.ClearListHandler(sender, e);
    }
}
