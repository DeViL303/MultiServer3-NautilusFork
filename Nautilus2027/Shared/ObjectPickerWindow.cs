#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nautilus2027.Formats;

namespace Nautilus2027
{
    /// <summary>
    /// Asks which files of the dropped Home object(s) to add to a tool's list: one tick box per model / skeleton / clip /
    /// collision, grouped by object, with Select all / None / Best LODs only. Built in code (dark, like the app's dialogs).
    /// </summary>
    internal sealed class ObjectPickerWindow : ThemedWindow
    {
        private readonly List<(CheckBox Box, ObjectAsset Asset)> _boxes = new List<(CheckBox, ObjectAsset)>();
        public List<string> SelectedPaths { get; private set; } = new List<string>();

        private static readonly Brush PanelBrush = PanelBack;
        private static readonly Brush Dim = DimText;

        public ObjectPickerWindow(string toolName, IList<(HomeObjectInfo Info, List<ObjectAsset> Assets)> objects)
        {
            Title = toolName + " - choose files from the object";
            Width = 860;
            Height = Math.Min(720, 190 + objects.Sum(o => 34 + o.Assets.Count * 40));
            MinHeight = 260;
            MinWidth = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;

            var root = new DockPanel { Margin = new Thickness(12) };

            var intro = new TextBlock
            {
                Text = objects.Count == 1
                    ? "This object has more than one file this tool can use. Tick the ones to add to the list."
                    : $"These {objects.Count} objects have files this tool can use. Tick the ones to add to the list.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                FontSize = 13,
            };
            DockPanel.SetDock(intro, Dock.Top);
            root.Children.Add(intro);

            // buttons
            var buttons = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
            DockPanel.SetDock(buttons, Dock.Bottom);
            Button Btn(string text, RoutedEventHandler click, Dock dock)
            {
                var b = AppButton(text, click);
                b.MinWidth = 90;
                b.Margin = new Thickness(4, 0, 4, 0);
                DockPanel.SetDock(b, dock);
                buttons.Children.Add(b);
                return b;
            }
            Btn("Select all", (s, e) => SetAll(_ => true), Dock.Left);
            Btn("None", (s, e) => SetAll(_ => false), Dock.Left);
            if (objects.Any(o => o.Assets.Any(a => Regex.IsMatch(a.Label ?? "", "_LOD\\d+\\.", RegexOptions.IgnoreCase))))
                Btn("Best LODs only", (s, e) => SetAll(a => !Regex.IsMatch(a.Label ?? "", "_LOD\\d+\\.", RegexOptions.IgnoreCase) || Regex.IsMatch(a.Label, "_LOD1\\.", RegexOptions.IgnoreCase)), Dock.Left);
            var cancel = Btn("Cancel", (s, e) => { DialogResult = false; }, Dock.Right);
            cancel.IsCancel = true;
            var add = Btn("Add selected", (s, e) =>
            {
                SelectedPaths = _boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Asset.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                DialogResult = true;
            }, Dock.Right);
            add.IsDefault = true;
            root.Children.Add(buttons);

            // the list
            var list = new StackPanel();
            foreach (var (info, assets) in objects)
            {
                var heading = new TextBlock
                {
                    Text = info.Heading,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, list.Children.Count == 0 ? 0 : 12, 0, 4),
                    TextWrapping = TextWrapping.Wrap,
                };
                ThemeText(heading);
                list.Children.Add(heading);
                foreach (var n in info.Notes)
                    list.Children.Add(new TextBlock { Text = n, Foreground = Dim, FontStyle = FontStyles.Italic, Margin = new Thickness(4, 0, 0, 2), TextWrapping = TextWrapping.Wrap });
                foreach (var a in assets)
                {
                    var content = new StackPanel();
                    content.Children.Add(new TextBlock { Text = $"[{a.Kind}]  {a.Label}" + (a.BuiltIn ? "   (built in)" : ""), Foreground = Brushes.White });
                    content.Children.Add(new TextBlock { Text = a.Details, Foreground = Dim, FontSize = 11, TextWrapping = TextWrapping.Wrap });
                    var cb = AppCheckBox(content, a.Selected);
                    cb.Margin = new Thickness(4, 2, 0, 4);
                    cb.ToolTip = a.Path;
                    _boxes.Add((cb, a));
                    list.Children.Add(cb);
                }
            }
            var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = PanelBrush, Padding = new Thickness(8) };
            root.Children.Add(scroll);
            Body = root;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { SetAll(_ => true); e.Handled = true; }
            };
        }

        private void SetAll(Func<ObjectAsset, bool> pick)
        {
            foreach (var (box, asset) in _boxes) box.IsChecked = pick(asset);
        }
    }
}
