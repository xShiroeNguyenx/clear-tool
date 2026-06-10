using ClearTool.Core.Model;

namespace ClearTool.Core.Treemap;

/// <summary>Hình chữ nhật thuần (không dính WPF) để Core test được layout.</summary>
public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public double Area => Width * Height;
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

public sealed record TreemapTile(TreeNode Node, LayoutRect Rect, int Depth);

public sealed class TreemapOptions
{
    /// <summary>Số cấp tối đa từ root hiện tại (4–6 theo plan).</summary>
    public int MaxDepth { get; init; } = 6;

    /// <summary>Tile nhỏ hơn ngưỡng này (px²) bị bỏ — không emit, không đệ quy tiếp.</summary>
    public double MinTileArea { get; init; } = 4.0;

    /// <summary>Inset mỗi cấp để thấy viền cha (px).</summary>
    public double Padding { get; init; } = 1.0;
}
