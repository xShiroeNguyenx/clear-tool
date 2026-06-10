using ClearTool.Core.Model;
using ClearTool.Core.Rules;
using ClearTool.Core.Rules.Matchers;
using FluentAssertions;
using Xunit;

namespace ClearTool.Core.Tests;

public class RuleEngineTests
{
    private static CleanupRule Rule(string id, SafetyLevel level, IPathMatcher matcher,
        int priority, bool needsAdmin = false) =>
        new()
        {
            Id = id,
            DisplayName = id,
            Level = level,
            Matcher = matcher,
            Priority = priority,
            NeedsAdmin = needsAdmin,
        };

    [Fact]
    public void SafeRule_MatchingDirectory_EmitsOneSuggestionWithAggregateSize()
    {
        var root = TestTreeBuilder.Root(@"X:\");
        var cache = root.Dir("projects").Dir("Cache");
        cache.File("a.bin", 100);
        cache.Dir("sub").File("b.bin", 50);
        root.ComputeAggregateSizes();

        var engine = new RuleEngine([
            Rule("safe-cache", SafetyLevel.Safe, new GlobMatcher(@"**\Cache"), 100),
        ]);

        var groups = engine.Evaluate(root);

        var safe = groups.Should().ContainSingle(g => g.Level == SafetyLevel.Safe).Subject;
        var suggestion = safe.Suggestions.Should().ContainSingle().Subject;
        suggestion.FullPath.Should().Be(@"X:\projects\Cache");
        suggestion.ReclaimableBytes.Should().Be(150);
        cache.Safety.Should().Be(SafetyLevel.Safe);
    }

    [Fact]
    public void MatchedDirectory_DoesNotDescend_NoNestedSuggestions()
    {
        var root = TestTreeBuilder.Root(@"X:\");
        var outer = root.Dir("node_modules");
        outer.Dir("pkg").Dir("node_modules").File("x.js", 10);
        root.ComputeAggregateSizes();

        var engine = new RuleEngine([
            Rule("caution-nm", SafetyLevel.Caution, new GlobMatcher(@"**\node_modules"), 50),
        ]);

        var groups = engine.Evaluate(root);

        // Chỉ node_modules NGOÀI CÙNG được gợi ý — không gợi ý lồng nhau
        groups.SelectMany(g => g.Suggestions).Should().ContainSingle()
            .Which.FullPath.Should().Be(@"X:\node_modules");
    }

    [Fact]
    public void KeepWinsTie_AtSamePriority_NoSuggestion()
    {
        var root = TestTreeBuilder.Root(@"X:\");
        root.Dir("Windows").File("a.dll", 10);
        root.ComputeAggregateSizes();

        var engine = new RuleEngine([
            Rule("safe", SafetyLevel.Safe, new ExactRootMatcher(@"X:\Windows"), 100),
            Rule("keep", SafetyLevel.Keep, new ExactRootMatcher(@"X:\Windows"), 100),
        ]);

        var groups = engine.Evaluate(root);

        groups.SelectMany(g => g.Suggestions).Should().BeEmpty();
        root.Children.Single().Safety.Should().Be(SafetyLevel.Keep);
    }

    [Fact]
    public void HigherPriority_Wins()
    {
        var root = TestTreeBuilder.Root(@"X:\");
        root.Dir("Cache");
        root.ComputeAggregateSizes();

        var engine = new RuleEngine([
            Rule("low-keep", SafetyLevel.Keep, new ExactRootMatcher(@"X:\Cache"), 10),
            Rule("high-safe", SafetyLevel.Safe, new ExactRootMatcher(@"X:\Cache"), 100),
        ]);

        var groups = engine.Evaluate(root);

        groups.SelectMany(g => g.Suggestions).Should().ContainSingle()
            .Which.Level.Should().Be(SafetyLevel.Safe);
    }

    [Fact]
    public void KeepZone_DoesNotDescend_GlobTargetsInsideAreNotSuggested()
    {
        var root = TestTreeBuilder.Root(@"X:\");
        var windows = root.Dir("Windows");
        windows.Dir("Cache").File("a.bin", 100); // glob khớp nhưng nằm trong KEEP
        root.ComputeAggregateSizes();

        var engine = new RuleEngine([
            Rule("keep-win", SafetyLevel.Keep, new ExactRootMatcher(@"X:\Windows"), 1000),
            Rule("safe-cache", SafetyLevel.Safe, new GlobMatcher(@"**\Cache"), 100),
        ]);

        var groups = engine.Evaluate(root);

        groups.SelectMany(g => g.Suggestions).Should().BeEmpty();
        windows.Safety.Should().Be(SafetyLevel.Keep);
    }

    [Fact]
    public void KeepZone_TunnelsToEnvKnownLocation_AndInheritsKeepElsewhere()
    {
        // Dùng %TEMP% thật của máy để EnvKnownLocationMatcher giải biến được,
        // nhưng cây vẫn là cây GIẢ — không đụng ổ đĩa.
        var temp = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Environment.GetEnvironmentVariable("TEMP")!));
        var driveRoot = Path.GetPathRoot(temp)!;
        var userProfileTopDir = temp.Substring(driveRoot.Length).Split(Path.DirectorySeparatorChar)[0];

        var root = TestTreeBuilder.Root(driveRoot);
        var tempNode = TestTreeBuilder.EnsurePath(root, temp);
        tempNode.File("junk.tmp", 500);
        // Nhánh "hàng xóm" ngoài tunnel, trong vùng KEEP — không được duyệt/gợi ý
        var sibling = TestTreeBuilder.EnsurePath(root, Path.Join(driveRoot, userProfileTopDir, "Hangxom"));
        sibling.File("data.bin", 999);
        root.ComputeAggregateSizes();

        var engine = new RuleEngine([
            // Toàn bộ thư mục cấp 1 chứa %TEMP% bị KEEP (mô phỏng C:\Windows chứa %WINDIR%\Temp)
            Rule("keep-top", SafetyLevel.Keep,
                new ExactRootMatcher(Path.Join(driveRoot, userProfileTopDir)), 1000),
            Rule("safe-temp", SafetyLevel.Safe, new EnvKnownLocationMatcher("%TEMP%"), 100, needsAdmin: true),
        ]);

        var groups = engine.Evaluate(root);

        var suggestion = groups.SelectMany(g => g.Suggestions).Should().ContainSingle().Subject;
        suggestion.FullPath.Should().BeEquivalentTo(temp);
        suggestion.ReclaimableBytes.Should().Be(500);
        suggestion.NeedsAdmin.Should().BeTrue();
        // Node ngoài tunnel kế thừa Keep (mặc định Unknown chỉ khi chưa duyệt tới)
        sibling.Safety.Should().NotBe(SafetyLevel.Safe);
    }

    [Fact]
    public void FileLevelKeepRule_MatchesFiles()
    {
        var root = TestTreeBuilder.Root(@"X:\");
        var pagefile = root.File("pagefile.sys", 8_000_000_000);
        root.ComputeAggregateSizes();

        var engine = new RuleEngine([
            Rule("keep-pagefile", SafetyLevel.Keep,
                new GlobMatcher(@"?:\pagefile.sys", directoriesOnly: false), 1000),
        ]);

        engine.Evaluate(root);

        pagefile.Safety.Should().Be(SafetyLevel.Keep);
    }

    [Fact]
    public void Output_GroupedByLevel_SortedBySizeDescending()
    {
        var root = TestTreeBuilder.Root(@"X:\");
        root.Dir("a").Dir("Cache").File("1", 10);
        root.Dir("b").Dir("Cache").File("2", 200);
        root.Dir("c").Dir("node_modules").File("3", 50);
        root.ComputeAggregateSizes();

        var engine = new RuleEngine([
            Rule("safe-cache", SafetyLevel.Safe, new GlobMatcher(@"**\Cache"), 100),
            Rule("caution-nm", SafetyLevel.Caution, new GlobMatcher(@"**\node_modules"), 50),
        ]);

        var groups = engine.Evaluate(root);

        groups.Should().HaveCount(2);
        var safe = groups.Single(g => g.Level == SafetyLevel.Safe);
        safe.Suggestions.Should().HaveCount(2);
        safe.Suggestions[0].ReclaimableBytes.Should().Be(200); // lớn trước
        safe.TotalReclaimableBytes.Should().Be(210);
        groups.Single(g => g.Level == SafetyLevel.Caution).Suggestions.Should().ContainSingle();
    }
}
