using System.Diagnostics;
using ClearTool.Core.Model;
using ClearTool.Core.Treemap;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ClearTool.Core.Tests;

public class TreemapLayoutTests(ITestOutputHelper output)
{
    private static readonly TreemapOptions Default = new() { MaxDepth = 6, MinTileArea = 4, Padding = 1 };

    [Fact]
    public void Areas_AreProportionalToSizes()
    {
        var root = TestTreeBuilder.Root(@"X:\");
        root.File("big.bin", 300);
        root.File("small.bin", 100);
        root.ComputeAggregateSizes();

        var tiles = TreemapLayout.Compute(root, 400, 300, Default);

        tiles.Should().HaveCount(2);
        var big = tiles.Single(t => t.Node.Name == "big.bin");
        var small = tiles.Single(t => t.Node.Name == "small.bin");
        (big.Rect.Area / small.Rect.Area).Should().BeApproximately(3.0, 0.01);
        (big.Rect.Area + small.Rect.Area).Should().BeApproximately(400 * 300, 0.5);
    }

    [Fact]
    public void AllTiles_StayWithinViewport()
    {
        var root = BuildFakeTree(branching: 8, filesPerDir: 12, depth: 3);

        var tiles = TreemapLayout.Compute(root, 1200, 800, Default);

        tiles.Should().NotBeEmpty();
        foreach (var t in tiles)
        {
            t.Rect.X.Should().BeGreaterThanOrEqualTo(-0.01);
            t.Rect.Y.Should().BeGreaterThanOrEqualTo(-0.01);
            t.Rect.Right.Should().BeLessThanOrEqualTo(1200.01);
            t.Rect.Bottom.Should().BeLessThanOrEqualTo(800.01);
        }
    }

    [Fact]
    public void Children_StayWithinParentTile()
    {
        var root = BuildFakeTree(branching: 4, filesPerDir: 6, depth: 3);

        var tiles = TreemapLayout.Compute(root, 1000, 700, Default);

        var rectByNode = tiles.ToDictionary(t => t.Node, t => t.Rect);
        foreach (var tile in tiles)
        {
            if (tile.Node.Parent is null || !rectByNode.TryGetValue(tile.Node.Parent, out var parentRect))
                continue;
            tile.Rect.X.Should().BeGreaterThanOrEqualTo(parentRect.X - 0.01);
            tile.Rect.Y.Should().BeGreaterThanOrEqualTo(parentRect.Y - 0.01);
            tile.Rect.Right.Should().BeLessThanOrEqualTo(parentRect.Right + 0.01);
            tile.Rect.Bottom.Should().BeLessThanOrEqualTo(parentRect.Bottom + 0.01);
        }
    }

    [Fact]
    public void DepthLimit_IsRespected()
    {
        var root = BuildFakeTree(branching: 2, filesPerDir: 2, depth: 10);

        var tiles = TreemapLayout.Compute(root, 1200, 800, new TreemapOptions
        {
            MaxDepth = 3,
            MinTileArea = 0.0001,
            Padding = 0,
        });

        tiles.Max(t => t.Depth).Should().BeLessThanOrEqualTo(3);
    }

    [Fact]
    public void TinyTiles_AreCulled_IncludingTheirSubtrees()
    {
        var root = TestTreeBuilder.Root(@"X:\");
        root.File("huge.bin", 1_000_000);
        var tinyDir = root.Dir("tiny");
        tinyDir.File("t.bin", 1); // ~0.96 px² trên viewport 800x600 → bị cull
        root.ComputeAggregateSizes();

        var tiles = TreemapLayout.Compute(root, 800, 600, new TreemapOptions { MinTileArea = 4 });

        tiles.Should().ContainSingle(t => t.Node.Name == "huge.bin");
        tiles.Should().NotContain(t => t.Node.Name == "tiny");
        tiles.Should().NotContain(t => t.Node.Name == "t.bin");
    }

    [Fact]
    public void ParentsAreEmittedBeforeChildren_PainterOrder()
    {
        var root = BuildFakeTree(branching: 3, filesPerDir: 3, depth: 3);

        var tiles = TreemapLayout.Compute(root, 1000, 700, Default);

        var indexByNode = tiles.Select((t, i) => (t.Node, i)).ToDictionary(p => p.Node, p => p.i);
        foreach (var tile in tiles)
            if (tile.Node.Parent is not null && indexByNode.TryGetValue(tile.Node.Parent, out var parentIndex))
                indexByNode[tile.Node].Should().BeGreaterThan(parentIndex);
    }

    [Fact]
    public void Benchmark_200kNodes_CompletesFast()
    {
        // ~155k node: 111 thư mục (3 cấp, mỗi cấp 10 dir con) × 1400 file/dir
        var root = BuildFakeTree(branching: 20, filesPerDir: 70, depth: 3);
        int nodeCount = CountNodes(root);
        nodeCount.Should().BeGreaterThan(150_000, "cây giả phải đủ lớn để benchmark có ý nghĩa");

        // Warm-up (JIT)
        TreemapLayout.Compute(root, 1200, 800, Default);

        var sw = Stopwatch.StartNew();
        var tiles = TreemapLayout.Compute(root, 1200, 800, Default);
        sw.Stop();

        output.WriteLine($"Nodes: {nodeCount:N0} · Tiles emitted: {tiles.Count:N0} · Layout: {sw.ElapsedMilliseconds} ms");
        tiles.Should().NotBeEmpty();
        // Smoke-benchmark, không phải perf gate chặt: ngưỡng RẤT rộng vì suite
        // chạy song song với test IO khác — chạy riêng thực tế ~350-750ms
        sw.ElapsedMilliseconds.Should().BeLessThan(5000);
    }

    /// <summary>Cây giả deterministic: mỗi dir có <paramref name="branching"/> dir con + file.</summary>
    private static TreeNode BuildFakeTree(int branching, int filesPerDir, int depth)
    {
        var root = TestTreeBuilder.Root(@"X:\");
        var seed = 12345;
        Grow(root, depth, ref seed);
        root.ComputeAggregateSizes();
        return root;

        void Grow(TreeNode dir, int remaining, ref int s)
        {
            for (int f = 0; f < filesPerDir * branching; f++)
            {
                s = unchecked(s * 1103515245 + 12345);
                long size = 1000 + Math.Abs(s % 5_000_000);
                dir.File($"f{f}.bin", size);
            }
            if (remaining <= 1)
                return;
            for (int d = 0; d < branching / 2; d++)
            {
                var sub = dir.Dir($"d{d}");
                Grow(sub, remaining - 1, ref s);
            }
        }
    }

    private static int CountNodes(TreeNode root)
    {
        int count = 0;
        var stack = new Stack<TreeNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            count++;
            foreach (var c in n.Children) stack.Push(c);
        }
        return count;
    }
}
