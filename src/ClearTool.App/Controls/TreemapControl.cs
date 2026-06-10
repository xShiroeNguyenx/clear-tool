using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClearTool.App.Converters;
using ClearTool.Core.Model;
using ClearTool.Core.Rules;
using ClearTool.Core.Treemap;
using Wpf.Ui.Appearance;

namespace ClearTool.App.Controls;

/// <summary>
/// Treemap squarified render bằng DrawingVisual.
/// Chỉ rebuild khi Root đổi hoặc resize (throttle 150ms).
/// </summary>
public sealed class TreemapControl : FrameworkElement
{
    private const int DepthClamp = 12; // chỉ để chặn key cache brush
    private const double Padding = 1.0;
    private const int SoftMaxTiles = 30_000; // nâng MinTileArea thích ứng để không vượt

    private readonly TreemapVisualHost _host = new();
    private readonly DispatcherTimer _resizeTimer;
    private readonly Dictionary<TreeNode, TreemapVisual> _visualByNode = [];
    private TreemapVisual? _selectedVisual;
    private TreeNode? _hoverNode;
    private double _pixelsPerDip = 1.0;

    public TreemapControl()
    {
        AddVisualChild(_host);
        _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _resizeTimer.Tick += (_, _) =>
        {
            _resizeTimer.Stop();
            Rebuild();
        };
        SizeChanged += (_, _) =>
        {
            _resizeTimer.Stop();
            _resizeTimer.Start();
        };
        ClipToBounds = true;

        // Vẽ lại khi đổi theme sáng/tối. Event static — PHẢI detach khi
        // Unloaded, không thì leak control.
        Loaded += (_, _) =>
        {
            ApplicationThemeManager.Changed += OnThemeChanged;
            Rebuild(); // theme có thể đã đổi trong lúc unloaded
        };
        Unloaded += (_, _) => ApplicationThemeManager.Changed -= OnThemeChanged;
    }

    private void OnThemeChanged(ApplicationTheme theme, Color accent) => Rebuild();

    // ── Dependency properties ─────────────────────────────────────────────

    public static readonly DependencyProperty RootProperty = DependencyProperty.Register(
        nameof(Root), typeof(TreeNode), typeof(TreemapControl),
        new PropertyMetadata(null, (d, _) => ((TreemapControl)d).Rebuild()));

    public TreeNode? Root
    {
        get => (TreeNode?)GetValue(RootProperty);
        set => SetValue(RootProperty, value);
    }

    public static readonly DependencyProperty MaxDepthProperty = DependencyProperty.Register(
        nameof(MaxDepth), typeof(int), typeof(TreemapControl),
        new PropertyMetadata(6, (d, _) => ((TreemapControl)d).Rebuild()));

    /// <summary>Số cấp hiển thị từ root hiện tại (4–8 hợp lý).</summary>
    public int MaxDepth
    {
        get => (int)GetValue(MaxDepthProperty);
        set => SetValue(MaxDepthProperty, value);
    }

    public static readonly DependencyProperty SelectedNodeProperty = DependencyProperty.Register(
        nameof(SelectedNode), typeof(TreeNode), typeof(TreemapControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((TreemapControl)d).UpdateSelectionVisual()));

    public TreeNode? SelectedNode
    {
        get => (TreeNode?)GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    /// <summary>Double-click vào thư mục — drill-down (re-root).</summary>
    public event EventHandler<TreeNode>? NodeActivated;

    // ── Visual tree plumbing ──────────────────────────────────────────────

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _host;

    protected override Size MeasureOverride(Size availableSize)
    {
        _host.Measure(availableSize);
        return new Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _host.Arrange(new Rect(finalSize));
        return finalSize;
    }

    // ── Build ─────────────────────────────────────────────────────────────

    private void Rebuild()
    {
        _host.Clear();
        _visualByNode.Clear();
        _selectedVisual = null;
        _hoverNode = null;
        ToolTip = null;

        var root = Root;
        double w = ActualWidth, h = ActualHeight;
        if (root is null || w < 16 || h < 16)
            return;

        _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Nền (bắt mouse event cho cả vùng trống)
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
            dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, w, h));
        _host.Add(background);

        double minArea = Math.Max(4.0, w * h / SoftMaxTiles);
        var tiles = TreemapLayout.Compute(root, w, h, new TreemapOptions
        {
            MaxDepth = MaxDepth,
            MinTileArea = minArea,
            Padding = Padding,
        });

        foreach (var tile in tiles)
        {
            var visual = new TreemapVisual(tile);
            DrawTile(visual, selected: false);
            _host.Add(visual);
            _visualByNode[tile.Node] = visual;
        }

        UpdateSelectionVisual();
    }

    private void DrawTile(TreemapVisual visual, bool selected)
    {
        var r = visual.Tile.Rect;
        var rect = new Rect(r.X, r.Y, r.Width, r.Height);
        using var dc = visual.RenderOpen();
        dc.DrawRectangle(
            GetFillBrush(visual.Tile.Node.Safety, visual.Tile.Depth),
            selected ? SelectionPen : BorderPen,
            rect);

        if (rect.Width > 72 && rect.Height > 17)
        {
            var text = new FormattedText(
                visual.Tile.Node.Name,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                11,
                GetLabelBrush(visual.Tile.Depth),
                _pixelsPerDip)
            {
                MaxTextWidth = rect.Width - 6,
                MaxTextHeight = rect.Height - 4,
                Trimming = TextTrimming.CharacterEllipsis,
                MaxLineCount = 1,
            };
            dc.PushClip(new RectangleGeometry(rect));
            dc.DrawText(text, new Point(rect.X + 3, rect.Y + 2));
            dc.Pop();
        }
    }

    // ── Selection ─────────────────────────────────────────────────────────

    private void UpdateSelectionVisual()
    {
        if (_selectedVisual is not null)
        {
            DrawTile(_selectedVisual, selected: false);
            _selectedVisual = null;
        }
        if (SelectedNode is not null && _visualByNode.TryGetValue(SelectedNode, out var visual))
        {
            DrawTile(visual, selected: true);
            _selectedVisual = visual;
        }
    }

    // ── Input ─────────────────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var tile = HitTile(e.GetPosition(this));
        if (tile is null)
            return;

        if (e.ClickCount == 2)
        {
            var node = tile.Tile.Node;
            if (node.Kind == NodeKind.Directory && node.Children.Count > 0)
            {
                NodeActivated?.Invoke(this, node);
                e.Handled = true;
                return;
            }
        }
        SelectedNode = tile.Tile.Node;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var tile = HitTile(e.GetPosition(this));
        var node = tile?.Tile.Node;
        if (ReferenceEquals(node, _hoverNode))
            return;
        _hoverNode = node;
        ToolTip = node is null
            ? null
            : $"{node.GetFullPath()}\n{BytesToHumanReadableConverter.Format(node.AggregateSize)}";
    }

    /// <summary>Topmost visual = tile sâu nhất (con vẽ sau cha).</summary>
    private TreemapVisual? HitTile(Point point)
    {
        TreemapVisual? result = null;
        VisualTreeHelper.HitTest(
            _host,
            null,
            hit =>
            {
                if (hit.VisualHit is TreemapVisual tv)
                {
                    result = tv;
                    return HitTestResultBehavior.Stop;
                }
                return HitTestResultBehavior.Continue;
            },
            new PointHitTestParameters(point));
        return result;
    }

    // ── Brushes / pens (frozen, cache theo theme) ─────────────────────────

    private static readonly Pen SelectionPen = CreateFrozenPen(Color.FromRgb(0x1E, 0x88, 0xE5), 2.5);
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private static readonly Dictionary<(ApplicationTheme, SafetyLevel, int), Brush> FillCache = [];
    private static readonly Dictionary<(ApplicationTheme, int), Brush> LabelCache = [];
    private static readonly Dictionary<ApplicationTheme, (Brush Background, Pen Border)> ChromeCache = [];

    private static Brush BackgroundBrush => GetChrome().Background;
    private static Pen BorderPen => GetChrome().Border;

    private static (Brush Background, Pen Border) GetChrome()
    {
        var theme = ApplicationThemeManager.GetAppTheme();
        if (ChromeCache.TryGetValue(theme, out var cached))
            return cached;
        (Brush Background, Pen Border) chrome = theme == ApplicationTheme.Dark
            ? (CreateFrozen(Color.FromRgb(0x1E, 0x1E, 0x1E)),
               CreateFrozenPen(Color.FromRgb(0x0F, 0x0F, 0x0F), 0.6))
            : (CreateFrozen(Color.FromRgb(0xF5, 0xF5, 0xF5)),
               CreateFrozenPen(Color.FromRgb(0xFF, 0xFF, 0xFF), 0.6));
        ChromeCache[theme] = chrome;
        return chrome;
    }

    private static Brush GetFillBrush(SafetyLevel level, int depth)
    {
        var theme = ApplicationThemeManager.GetAppTheme();
        var key = (theme, level, Math.Min(depth, DepthClamp));
        if (FillCache.TryGetValue(key, out var cached))
            return cached;

        bool dark = theme == ApplicationTheme.Dark;
        var baseColor = level switch
        {
            SafetyLevel.Safe => dark ? Color.FromRgb(0x43, 0xA0, 0x47) : Color.FromRgb(0x2E, 0x7D, 0x32),
            SafetyLevel.Caution => dark ? Color.FromRgb(0xFF, 0xB3, 0x00) : Color.FromRgb(0xF9, 0xA8, 0x25),
            SafetyLevel.Keep => dark ? Color.FromRgb(0xE5, 0x39, 0x35) : Color.FromRgb(0xC6, 0x28, 0x28),
            _ => dark ? Color.FromRgb(0x4F, 0x6A, 0x78) : Color.FromRgb(0x54, 0x6E, 0x7A),
        };
        // Light: nhạt dần về trắng theo độ sâu; Dark: chìm dần về nền tối
        double t = Math.Min(0.12 + 0.11 * (key.Item3 - 1), 0.75);
        var fadeTarget = dark ? Color.FromRgb(0x12, 0x12, 0x12) : Colors.White;
        var brush = CreateFrozen(Blend(baseColor, fadeTarget, t));
        FillCache[key] = brush;
        return brush;
    }

    private static Brush GetLabelBrush(int depth)
    {
        var theme = ApplicationThemeManager.GetAppTheme();
        var key = (theme, Math.Min(depth, DepthClamp));
        if (LabelCache.TryGetValue(key, out var cached))
            return cached;
        // Light: cấp nông nền đậm → chữ trắng, cấp sâu nền nhạt → chữ tối.
        // Dark: nền luôn tối → chữ luôn sáng.
        var color = theme == ApplicationTheme.Dark
            ? (key.Item2 <= 2 ? Colors.White : Color.FromRgb(0xE0, 0xE0, 0xE0))
            : (key.Item2 <= 2 ? Colors.White : Color.FromRgb(0x21, 0x21, 0x21));
        var brush = CreateFrozen(color);
        LabelCache[key] = brush;
        return brush;
    }

    private static Color Blend(Color from, Color to, double t) => Color.FromRgb(
        (byte)(from.R + (to.R - from.R) * t),
        (byte)(from.G + (to.G - from.G) * t),
        (byte)(from.B + (to.B - from.B) * t));

    private static SolidColorBrush CreateFrozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreateFrozenPen(Color color, double thickness)
    {
        var pen = new Pen(CreateFrozen(color), thickness);
        pen.Freeze();
        return pen;
    }

    private sealed class TreemapVisual(TreemapTile tile) : DrawingVisual
    {
        public TreemapTile Tile { get; } = tile;
    }
}
