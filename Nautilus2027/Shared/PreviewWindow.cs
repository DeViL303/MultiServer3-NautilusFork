#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Nautilus2027.Formats;
using Nautilus2027.Formats.Home;

namespace Nautilus2027
{
    /// <summary>
    /// 3D preview of models and collisions (WPF Viewport3D, no extra packages).
    /// Mouse: left-drag orbit, right / middle-drag pan, wheel zoom. Keys: W/S forward / back, A/D strafe, arrows strafe
    /// left / right and up / down, Q/E down / up, J/L and I/K turn, Shift faster, Ctrl slower, R or Home reset, T textures,
    /// B back faces, G grid.
    /// </summary>
    internal sealed class PreviewWindow : ThemedWindow
    {
        private readonly List<string> _files;
        private readonly Func<string, PreviewData> _load;

        private readonly Viewport3D _viewport = new Viewport3D { ClipToBounds = true, IsHitTestVisible = false };
        private readonly PerspectiveCamera _camera = new PerspectiveCamera { FieldOfView = 50 };
        private readonly DirectionalLight _headlight = new DirectionalLight(Color.FromRgb(0xD8, 0xD8, 0xD8), new Vector3D(0, 0, -1));
        private readonly ModelVisual3D _sceneRoot = new ModelVisual3D();
        private readonly Border _surface;
        private readonly TextBlock _status = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        private readonly TextBlock _loading = new TextBlock { Foreground = Brushes.White, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly WrapPanel _layerBar = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        private readonly CheckBox _textures = new CheckBox { Content = "Textures (T)", IsChecked = true, Foreground = Brushes.White, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Focusable = false };
        private readonly CheckBox _backFaces = new CheckBox { Content = "Back faces (B)", IsChecked = true, Foreground = Brushes.White, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Focusable = false };
        private readonly CheckBox _grid = new CheckBox { Content = "Grid (G)", IsChecked = true, Foreground = Brushes.White, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Focusable = false };

        private sealed class DrawMesh
        {
            public GeometryModel3D Model;
            public Material Textured, Plain, Back;
        }

        private sealed class DrawLayer
        {
            public string Name;
            public ModelVisual3D Visual = new ModelVisual3D();
            public List<DrawMesh> Meshes = new List<DrawMesh>();
            public bool Visible;
        }

        private readonly List<DrawLayer> _layers = new List<DrawLayer>();
        private ModelVisual3D _gridVisual;
        private int _loadId;

        // camera: orbit around a target point
        private Point3D _target;
        private double _yaw = Math.PI - 0.6, _pitch = -0.35, _dist = 5, _size = 1;
        private Point3D _homeTarget;
        private double _homeDist = 5;
        private Point _lastMouse;
        private bool _orbiting, _panning;
        private readonly HashSet<Key> _held = new HashSet<Key>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastFrame;

        // animation (particles, a skeleton playing a clip): redrawn when the time or the camera changes
        private PreviewAnimation _anim;
        private double _animTime, _animSpeed = 1;
        private bool _playing, _animDirty, _sliderSync;
        private readonly ModelVisual3D _animVisual = new ModelVisual3D();
        private readonly Dictionary<object, Brush> _animTextures = new Dictionary<object, Brush>();
        private readonly Dictionary<(object, uint, bool), Material> _animMaterials = new Dictionary<(object, uint, bool), Material>();
        private readonly DockPanel _transport = new DockPanel { Margin = new Thickness(0, 6, 0, 0), LastChildFill = true, Visibility = Visibility.Collapsed };
        private readonly Slider _timeSlider = new Slider { Minimum = 0, Maximum = 1, Focusable = false, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        private readonly TextBlock _timeText = new TextBlock { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, MinWidth = 110, FontFamily = new FontFamily("Consolas") };
        private readonly ComboBox _speedCombo = new ComboBox { Width = 70, Focusable = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        private readonly CheckBox _animShow = new CheckBox { Content = "Show", IsChecked = true, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Focusable = false, Margin = new Thickness(0, 0, 8, 0) };
        private Button _playButton;

        public PreviewWindow(string title, List<string> files, int start, Func<string, PreviewData> load)
        {
            _files = files;
            _load = load;
            Title = title;
            Width = 1100;
            Height = 780;
            MinWidth = 600;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            foreach (var c in new[] { _textures, _backFaces, _grid })
                if (AppStyle("DarkCheckBoxStyle") is Style cbs) c.Style = cbs;

            _viewport.Camera = _camera;
            var lights = new Model3DGroup();
            lights.Children.Add(new AmbientLight(Color.FromRgb(0x5A, 0x5A, 0x5A)));
            lights.Children.Add(_headlight);
            lights.Children.Add(new DirectionalLight(Color.FromRgb(0x30, 0x30, 0x34), new Vector3D(0.3, -1, 0.2)));
            _viewport.Children.Add(new ModelVisual3D { Content = lights });
            _viewport.Children.Add(_sceneRoot);

            // top bar: file chooser, layers, options
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };
            if (files.Count > 1)
            {
                var combo = new ComboBox { MinWidth = 260, MaxWidth = 420, Margin = new Thickness(0, 0, 10, 0), Focusable = false, VerticalAlignment = VerticalAlignment.Center };
                foreach (var f in files) combo.Items.Add(Path.GetFileName(f));
                combo.SelectedIndex = Math.Clamp(start, 0, files.Count - 1);
                combo.SelectionChanged += (s, e) => { if (combo.SelectedIndex >= 0) Load(_files[combo.SelectedIndex]); };
                if (AppStyle("DarkModeComboBoxStyle") is Style cs) combo.Style = cs;
                DockPanel.SetDock(combo, Dock.Left);
                top.Children.Add(combo);
            }
            var reset = AppButton("Reset view (R)", (s, e) => ResetView());
            reset.Focusable = false;
            reset.FontSize = 12;
            reset.Margin = new Thickness(4, 0, 0, 0);
            reset.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(reset, Dock.Right);
            top.Children.Add(reset);
            var opts = new StackPanel { Orientation = Orientation.Horizontal };
            opts.Children.Add(_textures);
            opts.Children.Add(_backFaces);
            opts.Children.Add(_grid);
            DockPanel.SetDock(opts, Dock.Right);
            top.Children.Add(opts);
            top.Children.Add(_layerBar);
            _textures.Click += (s, e) => ApplyMaterials();
            _backFaces.Click += (s, e) => ApplyMaterials();
            _grid.Click += (s, e) => ApplyGrid();

            // the 3D surface (the Border takes the mouse: a Viewport3D only hit-tests where there is geometry)
            var stack = new Grid();
            stack.Children.Add(_viewport);
            stack.Children.Add(_loading);
            _surface = new Border
            {
                Child = stack,
                Background = new LinearGradientBrush(Color.FromRgb(0x30, 0x34, 0x3A), Color.FromRgb(0x14, 0x14, 0x16), 90),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Focusable = true,
                Cursor = Cursors.Cross,
            };
            _surface.MouseDown += OnMouseDown;
            _surface.MouseUp += OnMouseUp;
            _surface.MouseMove += OnMouseMove;
            _surface.MouseWheel += OnMouseWheel;
            _surface.LostMouseCapture += (s, e) => { _orbiting = _panning = false; };

            var help = new TextBlock
            {
                Text = "Left-drag: orbit   Right / middle-drag: pan   Wheel: zoom   W/S: forward / back   A/D or Left/Right: strafe   Up/Down or E/Q: up / down   " +
                       "J/L, I/K: turn   Shift: faster   Ctrl: slower   F or R: reset view   Space: play / pause",
                Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            };

            // play bar (shown for files that have an animation)
            _playButton = AppButton("Play", (s, e) => TogglePlay());
            _playButton.Focusable = false;
            _playButton.FontSize = 12;
            _playButton.MinWidth = 70;
            _playButton.Margin = new Thickness(0, 0, 4, 0);
            _playButton.VerticalAlignment = VerticalAlignment.Center;
            var restart = AppButton("|<", (s, e) => SeekAnimation(0));
            restart.Focusable = false;
            restart.FontSize = 12;
            restart.MinWidth = 34;
            restart.Margin = new Thickness(0, 0, 8, 0);
            restart.VerticalAlignment = VerticalAlignment.Center;
            restart.ToolTip = "Back to the start (Home)";
            foreach (var sp in new[] { "0.1x", "0.25x", "0.5x", "1x", "2x", "4x" }) _speedCombo.Items.Add(sp);
            _speedCombo.SelectedIndex = 3;
            _speedCombo.SelectionChanged += (s, e) =>
            {
                if (_speedCombo.SelectedItem is string sp && double.TryParse(sp.TrimEnd('x'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) _animSpeed = v;
            };
            if (AppStyle("DarkModeComboBoxStyle") is Style scs) _speedCombo.Style = scs;
            if (AppStyle("DarkCheckBoxStyle") is Style acs) _animShow.Style = acs;
            _animShow.Click += (s, e) => { _animDirty = true; RebuildScene(); };
            _timeSlider.ValueChanged += (s, e) => { if (!_sliderSync && _anim != null) SeekAnimation(_timeSlider.Value); };
            foreach (var el in new UIElement[] { _playButton, restart, _speedCombo, _animShow }) { DockPanel.SetDock(el, Dock.Left); _transport.Children.Add(el); }
            DockPanel.SetDock(_timeText, Dock.Right);
            _transport.Children.Add(_timeText);
            _transport.Children.Add(_timeSlider);

            var root = new DockPanel { Margin = new Thickness(10) };
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);
            var bottom = new StackPanel();
            bottom.Children.Add(_transport);
            bottom.Children.Add(_status);
            bottom.Children.Add(help);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);
            root.Children.Add(_surface);
            Body = root;

            PreviewKeyDown += OnKeyDown;
            PreviewKeyUp += (s, e) => { _held.Remove(KeyOf(e)); };
            Deactivated += (s, e) => _held.Clear();
            CompositionTarget.Rendering += OnFrame;
            Closed += (s, e) => CompositionTarget.Rendering -= OnFrame;
            Loaded += (s, e) =>
            {
                _surface.Focus();
                if (_files.Count > 0) Load(_files[Math.Clamp(start, 0, _files.Count - 1)]);
            };
        }

        // ------------------------------------------------------------------ loading

        private sealed class Built
        {
            public PreviewData Data;
            public List<(PreviewLayer Layer, List<(MeshGeometry3D Geo, Material Textured, Material Plain, Material Back)> Meshes)> Layers =
                new List<(PreviewLayer, List<(MeshGeometry3D, Material, Material, Material)>)>();
            public Rect3D Bounds = Rect3D.Empty;
        }

        private async void Load(string file)
        {
            int id = ++_loadId;
            _loading.Text = "Loading " + Path.GetFileName(file) + " ...";
            _loading.Visibility = Visibility.Visible;
            _status.Text = "";
            Built built;
            Exception failed = null;
            // the error is caught inside the task, so a file that can't be shown never looks like an unhandled exception
            built = await Task.Run(() =>
            {
                try { return Build(_load(file)); }
                catch (Exception ex) { failed = ex; return null; }
            });
            if (failed != null || built == null)
            {
                var ex = failed ?? new InvalidDataException("nothing to show");
                MainWindow.Instance?.LogDebugInfo($"Preview: could not preview {file}: {ex.Message}");
                if (id != _loadId) return;
                _loading.Text = "Could not preview " + Path.GetFileName(file) + ":\n" + ex.Message;
                return;
            }
            if (id != _loadId) return;   // a newer file was picked meanwhile
            ShowBuilt(built);
            _loading.Visibility = Visibility.Collapsed;
            Title = "Preview - " + built.Data.Title;
        }

        /// <summary>Geometry and materials, made and frozen on a worker thread.</summary>
        private static Built Build(PreviewData data)
        {
            var b = new Built { Data = data };
            var texCache = new Dictionary<object, Brush>();
            foreach (var layer in data.Layers)
            {
                var list = new List<(MeshGeometry3D, Material, Material, Material)>();
                foreach (var m in layer.Meshes)
                {
                    int n = m.P.Length / 3;
                    if (n == 0 || m.Tris.Length < 3) continue;
                    var geo = new MeshGeometry3D();
                    var pos = new Point3DCollection(n);
                    for (int i = 0; i < n; i++)
                    {
                        var p = new Point3D(m.P[3 * i], m.P[3 * i + 1], m.P[3 * i + 2]);
                        pos.Add(p);
                        b.Bounds.Union(p);
                    }
                    geo.Positions = pos;
                    if (m.N != null && m.N.Length >= n * 3)
                    {
                        var nn = new Vector3DCollection(n);
                        for (int i = 0; i < n; i++) nn.Add(new Vector3D(m.N[3 * i], m.N[3 * i + 1], m.N[3 * i + 2]));
                        geo.Normals = nn;
                    }
                    if (m.UV != null && m.UV.Length >= n * 2)
                    {
                        var uv = new PointCollection(n);
                        for (int i = 0; i < n; i++) uv.Add(new Point(m.UV[2 * i], m.UV[2 * i + 1]));
                        geo.TextureCoordinates = uv;
                    }
                    var idx = new Int32Collection(m.Tris.Length);
                    for (int i = 0; i + 2 < m.Tris.Length; i += 3)
                    {
                        int a = m.Tris[i], c = m.Tris[i + 1], d = m.Tris[i + 2];
                        if (a < 0 || c < 0 || d < 0 || a >= n || c >= n || d >= n) continue;
                        idx.Add(a); idx.Add(c); idx.Add(d);
                    }
                    geo.TriangleIndices = idx;
                    geo.Freeze();

                    uint col = layer.Tint ?? m.Colour;
                    var plainColour = Color.FromArgb((byte)(col >> 24), (byte)(col >> 16), (byte)(col >> 8), (byte)col);
                    Material plain = MakeMaterial(new SolidColorBrush(plainColour), m.Emissive && layer.Tint == null);
                    Material textured = plain;
                    if (layer.Tint == null && geo.TextureCoordinates != null && geo.TextureCoordinates.Count == n)
                    {
                        object key = (object)m.ImageBytes ?? m.Image;
                        if (key != null)
                        {
                            if (!texCache.TryGetValue(key, out var brush))
                            {
                                brush = MakeTexture(m);
                                texCache[key] = brush;
                            }
                            if (brush != null) textured = MakeMaterial(brush, m.Emissive);
                        }
                    }
                    var back = MakeMaterial(new SolidColorBrush(Color.FromArgb(plainColour.A, (byte)(plainColour.R * 0.45), (byte)(plainColour.G * 0.45), (byte)(plainColour.B * 0.55))), false);
                    list.Add((geo, textured, plain, back));
                }
                b.Layers.Add((layer, list));
            }
            return b;
        }

        private static Material MakeMaterial(Brush brush, bool emissive)
        {
            Material mat;
            if (emissive)
            {
                var g = new MaterialGroup();
                g.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20))));
                g.Children.Add(new EmissiveMaterial(brush));
                mat = g;
            }
            else mat = new DiffuseMaterial(brush);
            mat.Freeze();
            return mat;
        }

        private static Brush MakeTexture(PreviewMesh m)
        {
            try
            {
                BitmapSource src;
                if (m.ImageBytes != null)
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = new MemoryStream(m.ImageBytes);
                    bi.EndInit();
                    src = bi;
                }
                else
                {
                    var img = m.Image;
                    if (img == null || img.Width <= 0 || img.Height <= 0 || img.Data == null) return null;
                    var bgra = new byte[img.Width * img.Height * 4];
                    for (int i = 0; i + 3 < bgra.Length && i + 3 < img.Data.Length; i += 4)
                    {
                        bgra[i] = img.Data[i + 2]; bgra[i + 1] = img.Data[i + 1]; bgra[i + 2] = img.Data[i]; bgra[i + 3] = img.Data[i + 3];
                    }
                    src = BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Bgra32, null, bgra, img.Width * 4);
                }
                src.Freeze();
                // UVs outside 0..1 tile, as in the game
                var brush = new ImageBrush(src)
                {
                    ViewportUnits = BrushMappingMode.Absolute,
                    Viewport = new Rect(0, 0, 1, 1),
                    TileMode = TileMode.Tile,
                    Stretch = Stretch.Fill,
                };
                brush.Freeze();
                return brush;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void ShowBuilt(Built built)
        {
            _sceneRoot.Children.Clear();
            _layers.Clear();
            _layerBar.Children.Clear();
            foreach (var (layer, meshes) in built.Layers)
            {
                var dl = new DrawLayer { Name = layer.Name, Visible = layer.Visible };
                foreach (var (geo, textured, plain, back) in meshes)
                {
                    var gm = new GeometryModel3D(geo, textured);
                    dl.Meshes.Add(new DrawMesh { Model = gm, Textured = textured, Plain = plain, Back = back });
                    var group = dl.Visual.Content as Model3DGroup ?? new Model3DGroup();
                    group.Children.Add(gm);
                    dl.Visual.Content = group;
                }
                _layers.Add(dl);
                var cb = new CheckBox
                {
                    Content = $"{layer.Name} ({meshes.Sum(m => m.Geo.TriangleIndices.Count / 3):N0} tris)",
                    IsChecked = dl.Visible,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Focusable = false,
                };
                if (AppStyle("DarkCheckBoxStyle") is Style lcs) cb.Style = lcs;
                cb.Click += (s, e) => { dl.Visible = cb.IsChecked == true; RebuildScene(); };
                _layerBar.Children.Add(cb);
            }
            ApplyMaterials();

            var bounds = built.Bounds;
            SetupAnimation(built.Data.Animation, ref bounds);
            if (bounds.IsEmpty) bounds = new Rect3D(-0.5, -0.5, -0.5, 1, 1, 1);
            _size = Math.Max(1e-3, Math.Sqrt(bounds.SizeX * bounds.SizeX + bounds.SizeY * bounds.SizeY + bounds.SizeZ * bounds.SizeZ));
            _homeTarget = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
            _homeDist = _size * 1.15;
            _gridVisual = MakeGrid(bounds);
            RebuildScene();
            ResetView();

            var lines = new List<string>(built.Data.Info);
            if (_anim != null) lines.InsertRange(0, _anim.Info);
            if (built.Data.Spawn != null) lines.Add($"spawn point {built.Data.Spawn[0]:0.##}, {built.Data.Spawn[1]:0.##}, {built.Data.Spawn[2]:0.##}");
            _status.Text = string.Join("\n", lines.Distinct().Take(_anim != null ? 12 : 10));
        }

        private void RebuildScene()
        {
            _sceneRoot.Children.Clear();
            if (_gridVisual != null && _grid.IsChecked == true) _sceneRoot.Children.Add(_gridVisual);
            // solid layers first, see-through ones after, so they blend over what's behind them
            foreach (var l in _layers.Where(l => l.Visible).OrderBy(l => l.Meshes.Any(m => m.Plain is DiffuseMaterial d && d.Brush is SolidColorBrush sb && sb.Color.A < 255) ? 1 : 0))
                _sceneRoot.Children.Add(l.Visual);
            if (_anim != null && _animShow.IsChecked == true) _sceneRoot.Children.Add(_animVisual);
        }

        private void ApplyMaterials()
        {
            bool tex = _textures.IsChecked == true, back = _backFaces.IsChecked == true;
            _animDirty = true;
            foreach (var l in _layers)
                foreach (var m in l.Meshes)
                {
                    m.Model.Material = tex ? m.Textured : m.Plain;
                    m.Model.BackMaterial = back ? m.Back : null;
                }
        }

        private void ApplyGrid() => RebuildScene();

        /// <summary>A floor grid under the model (thin quads; WPF 3D has no lines), spacing a power of ten of the model's size.</summary>
        private static ModelVisual3D MakeGrid(Rect3D b)
        {
            double size = Math.Max(b.SizeX, b.SizeZ);
            if (size <= 0) size = 1;
            double step = Math.Pow(10, Math.Floor(Math.Log10(size / 2)));
            int half = (int)Math.Ceiling(size * 0.75 / step);
            if (half > 40) half = 40;
            double cx = Math.Round((b.X + b.SizeX / 2) / step) * step, cz = Math.Round((b.Z + b.SizeZ / 2) / step) * step, y = b.Y;
            double w = step * 0.02, ext = half * step;
            var geo = new MeshGeometry3D();
            var pos = new Point3DCollection();
            var idx = new Int32Collection();
            void Quad(Point3D a, Point3D c, Point3D d, Point3D e)
            {
                int o = pos.Count;
                pos.Add(a); pos.Add(c); pos.Add(d); pos.Add(e);
                idx.Add(o); idx.Add(o + 1); idx.Add(o + 2); idx.Add(o); idx.Add(o + 2); idx.Add(o + 3);
            }
            for (int i = -half; i <= half; i++)
            {
                double x = cx + i * step, z = cz + i * step;
                Quad(new Point3D(x - w, y, cz - ext), new Point3D(x - w, y, cz + ext), new Point3D(x + w, y, cz + ext), new Point3D(x + w, y, cz - ext));
                Quad(new Point3D(cx - ext, y, z - w), new Point3D(cx + ext, y, z - w), new Point3D(cx + ext, y, z + w), new Point3D(cx - ext, y, z + w));
            }
            geo.Positions = pos;
            geo.TriangleIndices = idx;
            geo.Freeze();
            var mat = new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(0x70, 0x80, 0x80, 0x88)));
            mat.Freeze();
            return new ModelVisual3D { Content = new GeometryModel3D(geo, mat) { BackMaterial = mat } };
        }

        // ------------------------------------------------------------------ camera

        private Vector3D Forward => new Vector3D(Math.Sin(_yaw) * Math.Cos(_pitch), Math.Sin(_pitch), Math.Cos(_yaw) * Math.Cos(_pitch));

        private Vector3D Right
        {
            get
            {
                var r = Vector3D.CrossProduct(Forward, new Vector3D(0, 1, 0));
                if (r.Length < 1e-9) r = new Vector3D(1, 0, 0);
                r.Normalize();
                return r;
            }
        }

        private void ResetView()
        {
            _target = _homeTarget;
            _dist = _homeDist;
            _yaw = Math.PI - 0.6;
            _pitch = -0.35;
            UpdateCamera();
        }

        private void UpdateCamera()
        {
            var f = Forward;
            _camera.Position = _target - f * _dist;
            _camera.LookDirection = f * _dist;
            _camera.UpDirection = Vector3D.CrossProduct(Right, f);
            _camera.NearPlaneDistance = Math.Max(_size * 1e-4, _dist * 0.002);
            _camera.FarPlaneDistance = _size * 50 + _dist * 4;
            _headlight.Direction = f + Right * 0.25 - _camera.UpDirection * 0.35;
            _animDirty = true;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            _surface.Focus();
            _lastMouse = e.GetPosition(_surface);
            if (e.ChangedButton == MouseButton.Left) _orbiting = true;
            else if (e.ChangedButton == MouseButton.Right || e.ChangedButton == MouseButton.Middle) _panning = true;
            _surface.CaptureMouse();
            e.Handled = true;
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) _orbiting = false;
            else _panning = false;
            if (!_orbiting && !_panning) _surface.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_orbiting && !_panning) return;
            var p = e.GetPosition(_surface);
            double dx = p.X - _lastMouse.X, dy = p.Y - _lastMouse.Y;
            _lastMouse = p;
            if (_orbiting)
            {
                _yaw -= dx * 0.008;
                _pitch = Math.Clamp(_pitch - dy * 0.008, -1.55, 1.55);
            }
            else
            {
                double k = _dist * Math.Tan(_camera.FieldOfView * Math.PI / 360) * 2 / Math.Max(1, _surface.ActualHeight);
                var up = Vector3D.CrossProduct(Right, Forward);
                _target += -Right * dx * k + up * dy * k;
            }
            UpdateCamera();
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double f = Math.Pow(0.85, e.Delta / 120.0);
            double newDist = Math.Clamp(_dist * f, _size * 1e-3, _size * 40);
            // keep the camera moving when zoomed right in: push the target forward instead of stopping
            if (f < 1 && newDist <= _size * 1e-3 * 1.01) _target += Forward * _size * 0.02;
            _dist = newDist;
            UpdateCamera();
            e.Handled = true;
        }

        private static Key KeyOf(KeyEventArgs e) => e.Key == Key.System ? e.SystemKey : e.Key;

        private static readonly HashSet<Key> MoveKeys = new HashSet<Key>
        {
            Key.W, Key.A, Key.S, Key.D, Key.Q, Key.E, Key.Up, Key.Down, Key.Left, Key.Right, Key.PageUp, Key.PageDown,
            Key.I, Key.J, Key.K, Key.L,
        };

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            var k = KeyOf(e);
            if (Keyboard.FocusedElement is ComboBox cb && cb.IsDropDownOpen) return;
            if (MoveKeys.Contains(k)) { _held.Add(k); e.Handled = true; return; }
            switch (k)
            {
                case Key.Home: if (_anim != null) SeekAnimation(0); else ResetView(); e.Handled = true; break;
                case Key.R: case Key.F: ResetView(); e.Handled = true; break;
                case Key.T: _textures.IsChecked = _textures.IsChecked != true; ApplyMaterials(); e.Handled = true; break;
                case Key.B: _backFaces.IsChecked = _backFaces.IsChecked != true; ApplyMaterials(); e.Handled = true; break;
                case Key.G: _grid.IsChecked = _grid.IsChecked != true; ApplyGrid(); e.Handled = true; break;
                case Key.Space: if (_anim != null) { TogglePlay(); e.Handled = true; } break;
                case Key.OemComma: if (_anim != null) { Pause(); SeekAnimation(_animTime - 1 / 30.0); e.Handled = true; } break;
                case Key.OemPeriod: if (_anim != null) { Pause(); SeekAnimation(_animTime + 1 / 30.0); e.Handled = true; } break;
                case Key.Escape: Close(); e.Handled = true; break;
            }
        }

        // ------------------------------------------------------------------ animation

        private void SetupAnimation(PreviewAnimation anim, ref Rect3D bounds)
        {
            _anim = anim != null && anim.Frame != null ? anim : null;
            _animVisual.Content = null;
            _animMaterials.Clear();
            _animTextures.Clear();
            _animTime = 0;
            _transport.Visibility = _anim != null && _anim.ShowControls ? Visibility.Visible : Visibility.Collapsed;
            if (_anim == null) { _playing = false; return; }
            if (!(_anim.Duration > 0) || double.IsInfinity(_anim.Duration)) _anim.Duration = 1;
            _sliderSync = true;
            _timeSlider.Maximum = _anim.Duration;
            _timeSlider.Value = 0;
            _sliderSync = false;
            var b = _anim.Bounds;
            if (b != null && b.Length >= 6 && b.All(double.IsFinite) && b[3] >= b[0] && b[4] >= b[1] && b[5] >= b[2])
            {
                bounds.Union(new Point3D(b[0], b[1], b[2]));
                bounds.Union(new Point3D(b[3], b[4], b[5]));
            }
            _playing = _anim.AutoPlay;
            _playButton.Content = _playing ? "Pause" : "Play";
            _animDirty = true;
            UpdateTimeText();
        }

        private void TogglePlay()
        {
            if (_anim == null || !_anim.ShowControls) return;
            if (!_playing && !_anim.Loop && _animTime >= _anim.Duration - 1e-6) _animTime = 0;
            _playing = !_playing;
            _playButton.Content = _playing ? "Pause" : "Play";
            _animDirty = true;
        }

        private void Pause()
        {
            _playing = false;
            _playButton.Content = "Play";
        }

        private void SeekAnimation(double t)
        {
            if (_anim == null) return;
            double d = _anim.Duration;
            _animTime = _anim.Loop ? ((t % d) + d) % d : Math.Clamp(t, 0, d);
            _animDirty = true;
            SyncSlider();
        }

        private void SyncSlider()
        {
            _sliderSync = true;
            _timeSlider.Value = Math.Clamp(_animTime, 0, _timeSlider.Maximum);
            _sliderSync = false;
            UpdateTimeText();
        }

        private void UpdateTimeText() =>
            _timeText.Text = _anim == null ? "" : $"{_animTime,6:0.00} / {_anim.Duration:0.00} s";

        private void TickAnimation(double dt)
        {
            if (_playing)
            {
                _animTime += dt * _animSpeed;
                if (_animTime >= _anim.Duration)
                {
                    if (_anim.Loop) _animTime %= _anim.Duration;
                    else { _animTime = _anim.Duration; Pause(); }
                }
                _animDirty = true;
                SyncSlider();
            }
            if (!_animDirty) return;
            _animDirty = false;
            if (_animShow.IsChecked != true) return;
            DrawAnimation();
        }

        private void DrawAnimation()
        {
            List<PreviewMesh> meshes;
            try
            {
                var f = Forward;
                var r = Right;
                var u = Vector3D.CrossProduct(r, f);
                var eye = _target - f * _dist;
                var cam = new PreviewCamera
                {
                    Eye = new[] { eye.X, eye.Y, eye.Z },
                    Forward = new[] { f.X, f.Y, f.Z },
                    Right = new[] { r.X, r.Y, r.Z },
                    Up = new[] { u.X, u.Y, u.Z },
                };
                meshes = _anim.Frame(_animTime, cam);
            }
            catch (Exception ex)
            {
                Pause();
                MainWindow.Instance?.LogDebugInfo($"Preview: animation stopped: {ex}");
                _status.Text = "The animation stopped: " + ex.Message + "\n" + _status.Text;
                _anim = null;
                _transport.Visibility = Visibility.Collapsed;
                _animVisual.Content = null;
                return;
            }
            var group = new Model3DGroup();
            foreach (var m in meshes ?? new List<PreviewMesh>())
            {
                var geo = AnimGeometry(m);
                if (geo == null) continue;
                var mat = AnimMaterial(m, geo.TextureCoordinates != null && geo.TextureCoordinates.Count == geo.Positions.Count);
                group.Children.Add(new GeometryModel3D(geo, mat) { BackMaterial = mat });
            }
            _animVisual.Content = group;
        }

        private static MeshGeometry3D AnimGeometry(PreviewMesh m)
        {
            if (m?.P == null || m.Tris == null) return null;
            int n = m.P.Length / 3;
            if (n == 0 || m.Tris.Length < 3) return null;
            var geo = new MeshGeometry3D();
            var pos = new Point3DCollection(n);
            for (int i = 0; i < n; i++) pos.Add(new Point3D(m.P[3 * i], m.P[3 * i + 1], m.P[3 * i + 2]));
            geo.Positions = pos;
            if (m.N != null && m.N.Length >= n * 3)
            {
                var nn = new Vector3DCollection(n);
                for (int i = 0; i < n; i++) nn.Add(new Vector3D(m.N[3 * i], m.N[3 * i + 1], m.N[3 * i + 2]));
                geo.Normals = nn;
            }
            if (m.UV != null && m.UV.Length >= n * 2)
            {
                var uv = new PointCollection(n);
                for (int i = 0; i < n; i++) uv.Add(new Point(m.UV[2 * i], m.UV[2 * i + 1]));
                geo.TextureCoordinates = uv;
            }
            var idx = new Int32Collection(m.Tris.Length);
            for (int i = 0; i + 2 < m.Tris.Length; i += 3)
            {
                int a = m.Tris[i], c = m.Tris[i + 1], d = m.Tris[i + 2];
                if (a < 0 || c < 0 || d < 0 || a >= n || c >= n || d >= n) continue;
                idx.Add(a); idx.Add(c); idx.Add(d);
            }
            geo.TriangleIndices = idx;
            geo.Freeze();
            return geo;
        }

        /// <summary>Material for an animated mesh, cached by texture, colour (its alpha fades it) and emissive.</summary>
        private Material AnimMaterial(PreviewMesh m, bool hasUv)
        {
            object texKey = hasUv && _textures.IsChecked == true ? (object)m.ImageBytes ?? m.Image : null;
            var key = (texKey, m.Colour, m.Emissive);
            if (_animMaterials.TryGetValue(key, out var mat)) return mat;
            byte a = (byte)(m.Colour >> 24);
            Brush brush = null;
            if (texKey != null)
            {
                if (!_animTextures.TryGetValue(texKey, out var tex)) { tex = MakeTexture(m); _animTextures[texKey] = tex; }
                if (tex != null)
                {
                    if (a < 255) { tex = tex.Clone(); tex.Opacity = a / 255.0; tex.Freeze(); }
                    brush = tex;
                }
            }
            if (brush == null)
            {
                brush = new SolidColorBrush(Color.FromArgb(a, (byte)(m.Colour >> 16), (byte)(m.Colour >> 8), (byte)m.Colour));
                brush.Freeze();
            }
            mat = MakeMaterial(brush, m.Emissive && a == 255);
            if (_animMaterials.Count > 4096) _animMaterials.Clear();
            _animMaterials[key] = mat;
            return mat;
        }

        private void OnFrame(object sender, EventArgs e)
        {
            double now = _clock.Elapsed.TotalSeconds;
            double dt = Math.Min(0.1, now - _lastFrame);
            _lastFrame = now;
            if (_anim != null) TickAnimation(dt);
            if (_held.Count == 0) return;
            // Keyboard.IsKeyDown guards against a key-up lost while another window had focus
            _held.RemoveWhere(k => !Keyboard.IsKeyDown(k));
            if (_held.Count == 0) return;

            double speed = Math.Max(_size, _dist) * 0.6;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) speed *= 4;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) speed *= 0.25;
            double step = speed * dt;
            var f = Forward;
            var r = Right;
            var worldUp = new Vector3D(0, 1, 0);
            var move = new Vector3D();
            if (_held.Contains(Key.W)) move += f;
            if (_held.Contains(Key.S)) move -= f;
            if (_held.Contains(Key.A) || _held.Contains(Key.Left)) move -= r;
            if (_held.Contains(Key.D) || _held.Contains(Key.Right)) move += r;
            if (_held.Contains(Key.E) || _held.Contains(Key.Up) || _held.Contains(Key.PageUp)) move += worldUp;
            if (_held.Contains(Key.Q) || _held.Contains(Key.Down) || _held.Contains(Key.PageDown)) move -= worldUp;
            if (move.Length > 0) { move.Normalize(); _target += move * step; }
            // J/L, I/K turn the camera where it stands (the point it orbits moves with the view)
            double turn = 1.6 * dt;
            bool turning = _held.Contains(Key.J) || _held.Contains(Key.L) || _held.Contains(Key.I) || _held.Contains(Key.K);
            if (turning)
            {
                var eye = _target - Forward * _dist;
                if (_held.Contains(Key.J)) _yaw += turn;
                if (_held.Contains(Key.L)) _yaw -= turn;
                if (_held.Contains(Key.I)) _pitch = Math.Clamp(_pitch + turn, -1.55, 1.55);
                if (_held.Contains(Key.K)) _pitch = Math.Clamp(_pitch - turn, -1.55, 1.55);
                _target = eye + Forward * _dist;
            }
            UpdateCamera();
        }
    }
}
