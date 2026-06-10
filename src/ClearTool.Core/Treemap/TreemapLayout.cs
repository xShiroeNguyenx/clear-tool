using ClearTool.Core.Model;

namespace ClearTool.Core.Treemap;

/// <summary>
/// Treemap squarified (Bruls et al.) — giữ tỉ lệ tile gần 1:1.
/// Thuần logic: nhận TreeNode + viewport, trả danh sách tile (pre-order:
/// cha trước con — đúng thứ tự painter khi render).
/// </summary>
public static class TreemapLayout
{
    public static List<TreemapTile> Compute(TreeNode root, double width, double height, TreemapOptions options)
    {
        var tiles = new List<TreemapTile>();
        if (width <= 0 || height <= 0 || root.AggregateSize <= 0)
            return tiles;

        LayoutChildren(root, new LayoutRect(0, 0, width, height), depth: 1, options, tiles);
        return tiles;
    }

    private static void LayoutChildren(
        TreeNode node, LayoutRect rect, int depth, TreemapOptions o, List<TreemapTile> output)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var items = node.Children
            .Where(c => c.AggregateSize > 0)
            .OrderByDescending(c => c.AggregateSize)
            .ToList();
        if (items.Count == 0)
            return;

        long total = items.Sum(c => c.AggregateSize);
        double scale = rect.Area / total;

        // Vùng còn trống, thu hẹp dần sau mỗi row
        double x = rect.X, y = rect.Y, w = rect.Width, h = rect.Height;

        var row = new List<(TreeNode Node, double Area)>();
        int i = 0;
        while (i < items.Count)
        {
            double area = items[i].AggregateSize * scale;
            double shortSide = Math.Min(w, h);
            if (shortSide <= 0)
                break;

            if (row.Count == 0 || Worst(row, area, shortSide) <= Worst(row, null, shortSide))
            {
                row.Add((items[i], area));
                i++;
            }
            else
            {
                FlushRow(row, ref x, ref y, ref w, ref h, depth, o, output);
                row.Clear();
            }
        }
        if (row.Count > 0)
            FlushRow(row, ref x, ref y, ref w, ref h, depth, o, output);
    }

    /// <summary>
    /// Tỉ lệ khung hình xấu nhất của row nếu thêm <paramref name="extraArea"/>
    /// (null = giữ nguyên row), khi xếp dọc theo cạnh ngắn <paramref name="side"/>.
    /// </summary>
    private static double Worst(List<(TreeNode Node, double Area)> row, double? extraArea, double side)
    {
        double total = row.Sum(r => r.Area) + (extraArea ?? 0);
        if (total <= 0 || side <= 0)
            return double.MaxValue;

        double min = extraArea ?? double.MaxValue;
        double max = extraArea ?? 0;
        foreach (var (_, a) in row)
        {
            if (a < min) min = a;
            if (a > max) max = a;
        }

        double sideSq = side * side;
        double totalSq = total * total;
        return Math.Max(sideSq * max / totalSq, totalSq / (sideSq * min));
    }

    /// <summary>Xếp row dọc theo cạnh ngắn của vùng trống rồi thu hẹp vùng trống.</summary>
    private static void FlushRow(
        List<(TreeNode Node, double Area)> row,
        ref double x, ref double y, ref double w, ref double h,
        int depth, TreemapOptions o, List<TreemapTile> output)
    {
        double rowArea = row.Sum(r => r.Area);
        if (rowArea <= 0)
            return;

        bool horizontal = w >= h; // row là dải DỌC bên trái khi vùng rộng ngang
        double side = horizontal ? h : w;
        double thickness = rowArea / side;

        double offset = 0;
        foreach (var (child, area) in row)
        {
            double length = area / thickness;
            var tileRect = horizontal
                ? new LayoutRect(x, y + offset, thickness, length)
                : new LayoutRect(x + offset, y, length, thickness);
            offset += length;

            Emit(child, tileRect, depth, o, output);
        }

        if (horizontal)
        {
            x += thickness;
            w -= thickness;
        }
        else
        {
            y += thickness;
            h -= thickness;
        }
    }

    private static void Emit(TreeNode child, LayoutRect rect, int depth, TreemapOptions o, List<TreemapTile> output)
    {
        // Culling: tile quá nhỏ — không emit, không đệ quy (đòn bẩy hiệu năng lớn nhất)
        if (rect.Area < o.MinTileArea)
            return;

        output.Add(new TreemapTile(child, rect, depth));

        if (child.Kind != NodeKind.Directory || depth >= o.MaxDepth || child.Children.Count == 0)
            return;

        var inner = new LayoutRect(
            rect.X + o.Padding,
            rect.Y + o.Padding,
            rect.Width - 2 * o.Padding,
            rect.Height - 2 * o.Padding);
        LayoutChildren(child, inner, depth + 1, o, output);
    }
}
