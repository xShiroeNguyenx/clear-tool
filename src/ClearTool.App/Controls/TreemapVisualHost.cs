using System.Windows;
using System.Windows.Media;

namespace ClearTool.App.Controls;

/// <summary>
/// Host chứa VisualCollection các DrawingVisual — primitive retained-mode nhẹ,
/// KHÔNG phải UIElement per tile (hàng chục nghìn tile sẽ sập nếu dùng element).
/// </summary>
public sealed class TreemapVisualHost : FrameworkElement
{
    private readonly VisualCollection _visuals;

    public TreemapVisualHost()
    {
        _visuals = new VisualCollection(this);
    }

    public void Add(Visual visual) => _visuals.Add(visual);

    public void Clear() => _visuals.Clear();

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];
}
