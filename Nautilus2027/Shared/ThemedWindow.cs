#nullable disable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace Nautilus2027
{
    /// <summary>
    /// A pop-up window dressed like the main window: no Windows frame, the app's own title bar (icon, bold title,
    /// minimise / maximise / close), the app's styles, and the theme colour (exposed as <see cref="SelectedThemeColor"/>
    /// so the app's button / tick-box styles, which look it up on their Window, pick it up and follow changes live).
    /// Put the window's content in <see cref="Body"/>.
    /// </summary>
    internal class ThemedWindow : Window
    {
        public static readonly DependencyProperty SelectedThemeColorProperty =
            DependencyProperty.Register(nameof(SelectedThemeColor), typeof(Brush), typeof(ThemedWindow),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xE6, 0x1E, 0x1E))));

        /// <summary>The app's theme colour (bound to the main window's).</summary>
        public Brush SelectedThemeColor
        {
            get => (Brush)GetValue(SelectedThemeColorProperty);
            set => SetValue(SelectedThemeColorProperty, value);
        }

        public static readonly Brush WindowBack = Frozen(Color.FromRgb(0x24, 0x24, 0x24));
        public static readonly Brush PanelBack = Frozen(Color.FromRgb(0x1B, 0x1B, 0x1B));
        public static readonly Brush DimText = Frozen(Color.FromRgb(0xA0, 0xA0, 0xA0));

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private readonly ContentControl _body = new ContentControl { Focusable = false };
        private readonly Button _max;

        /// <summary>What the window shows under its title bar.</summary>
        public object Body
        {
            get => _body.Content;
            set => _body.Content = value;
        }

        public ThemedWindow()
        {
            try
            {
                Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Nautilus2027;component/Themes/Styles.xaml", UriKind.Relative) });
            }
            catch (Exception) { }   // the window still works unstyled
            if (MainWindow.Instance != null)
            {
                SetBinding(SelectedThemeColorProperty, new Binding(nameof(MainWindow.SelectedThemeColor)) { Source = MainWindow.Instance });
                Icon = MainWindow.Instance.Icon;
            }

            WindowStyle = WindowStyle.None;
            Background = WindowBack;
            Foreground = Brushes.White;
            BorderBrush = Brushes.Black;
            BorderThickness = new Thickness(1);
            ShowInTaskbar = false;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 33,
                ResizeBorderThickness = new Thickness(5),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false,
            });

            var frame = new Grid();
            frame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(33) });
            frame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // title bar, as in MainWindow.xaml
            var bar = new Grid { Background = Brushes.Transparent };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new Image { Width = 20, Height = 20, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            icon.SetBinding(Image.SourceProperty, new Binding(nameof(Icon)) { Source = this });
            bar.Children.Add(icon);
            var title = new TextBlock
            {
                Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            title.SetBinding(TextBlock.TextProperty, new Binding(nameof(Title)) { Source = this });
            Grid.SetColumn(title, 1);
            bar.Children.Add(title);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(buttons, 2);
            buttons.Children.Add(CaptionButton("", (s, e) => WindowState = WindowState.Minimized, new Thickness(0, 0, 10, 0)));
            _max = CaptionButton("", (s, e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, new Thickness(0, 0, 10, 0));
            buttons.Children.Add(_max);
            buttons.Children.Add(CaptionButton("", (s, e) => Close(), new Thickness(0, 0, 7, 0), close: true));
            bar.Children.Add(buttons);
            bar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2 && ResizeMode != ResizeMode.NoResize) _max.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            frame.Children.Add(bar);

            Grid.SetRow(_body, 1);
            frame.Children.Add(_body);
            Content = frame;

            // the app's interface size (Settings), as on the main window
            SourceInitialized += (s, e) =>
            {
                double sc = MainWindow.Instance?.UiScale ?? 1;
                if (Math.Abs(sc - 1) < 0.001) return;
                frame.LayoutTransform = new ScaleTransform(sc, sc);
                if (!double.IsNaN(Width)) Width *= sc;
                if (!double.IsNaN(Height)) Height *= sc;
                MinWidth *= sc;
                MinHeight *= sc;
            };

            StateChanged += (s, e) =>
            {
                _max.Content = WindowState == WindowState.Maximized ? "" : "";
                // a maximised frameless window spills past the screen edge by the resize border
                frame.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            };
            Loaded += (s, e) =>
            {
                _max.Visibility = ResizeMode == ResizeMode.NoResize ? Visibility.Collapsed : Visibility.Visible;
                if (ResizeMode == ResizeMode.NoResize) ((WindowChrome)WindowChrome.GetWindowChrome(this)).ResizeBorderThickness = new Thickness(0);
            };
        }

        private Button CaptionButton(string glyph, RoutedEventHandler click, Thickness margin, bool close = false)
        {
            var b = new Button
            {
                Content = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                Width = 24,
                Height = 24,
                Margin = margin,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Foreground = Brushes.White,
                Focusable = false,
                Cursor = Cursors.Hand,
            };
            // flat, with the theme colour on hover
            var t = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border)) { Name = "bd" };
            bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            t.VisualTree = bd;
            var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(ForegroundProperty, new Binding(nameof(SelectedThemeColor)) { Source = this }));
            if (close) hover.Setters.Add(new Setter(Border.BackgroundProperty, Frozen(Color.FromRgb(0x18, 0x18, 0x18)), "bd"));
            t.Triggers.Add(hover);
            b.Template = t;
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            b.Click += click;
            return b;
        }

        /// <summary>One of the app's styles (from Themes/Styles.xaml), or null.</summary>
        public Style AppStyle(string key) => TryFindResource(key) as Style;

        /// <summary>A button in the app's style (dark, bold, theme-coloured text).</summary>
        public Button AppButton(string text, RoutedEventHandler click)
        {
            var b = new Button { Content = text };
            if (AppStyle("DarkModeButtonStyle") is Style st) b.Style = st;
            else { b.Background = PanelBack; b.Foreground = SelectedThemeColor; b.Padding = new Thickness(10, 4, 10, 4); }
            if (click != null) b.Click += click;
            return b;
        }

        /// <summary>A tick box in the app's style (dark box, theme-coloured tick).</summary>
        public CheckBox AppCheckBox(object content, bool? isChecked)
        {
            var c = new CheckBox { Content = content, IsChecked = isChecked, Foreground = Brushes.White };
            if (AppStyle("DarkCheckBoxStyle") is Style st) c.Style = st;
            return c;
        }

        /// <summary>Text in the theme colour (follows theme changes).</summary>
        public void ThemeText(TextBlock t) => t.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(SelectedThemeColor)) { Source = this });
    }
}
