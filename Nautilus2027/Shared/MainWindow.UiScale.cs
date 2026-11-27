#nullable disable
using System;
using System.Windows;
using System.Windows.Media;

namespace Nautilus2027
{
    // Interface size. Windows scales the app by the screen's DPI setting (a 4K screen at 200-300% makes every pixel 2-3x),
    // so on such screens the window can be shown smaller: the whole window is scaled by UiScale (Settings: Interface size).
    public partial class MainWindow
    {
        private const double DesignWidth = 1480, DesignHeight = 850;
        private bool _uiScaleCentred;

        /// <summary>The size factor in use (1 = as designed). Pop-up windows use it too.</summary>
        internal double UiScale { get; private set; } = 1;

        internal event Action UiScaleChanged;

        /// <summary>The factor from the setting; Auto keeps the window at most 1.5x its design size in screen pixels.</summary>
        internal double ResolveUiScale()
        {
            int pct = _settings?.UiScalePercent ?? 0;
            if (pct >= 40 && pct <= 200) return pct / 100.0;
            double dpi = 1;
            try { dpi = VisualTreeHelper.GetDpi(this).DpiScaleX; } catch (Exception) { }
            return dpi > 1.5 ? Math.Round(1.5 / dpi, 3) : 1.0;
        }

        internal void ApplyUiScale()
        {
            double s = ResolveUiScale();
            UiScale = s;
            if (Content is FrameworkElement root)
                root.LayoutTransform = Math.Abs(s - 1) < 0.001 ? Transform.Identity : new ScaleTransform(s, s);
            MaxWidth = DesignWidth * s;
            MaxHeight = DesignHeight * s;
            Width = DesignWidth * s;
            Height = DesignHeight * s;
            if (!_uiScaleCentred && IsLoaded)
            {
                // centre on the screen's work area once, after the first resize
                var area = SystemParameters.WorkArea;
                Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
                Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
                _uiScaleCentred = true;
            }
            UiScaleChanged?.Invoke();
            SettingsTabView?.ShowUiScaleInfo();
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            if ((_settings?.UiScalePercent ?? 0) == 0) ApplyUiScale();
        }
    }
}
