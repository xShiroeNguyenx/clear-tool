using ClearTool.Core.Rules;
using ClearTool.Core.Rules.Matchers;
using FluentAssertions;
using Xunit;

namespace ClearTool.Core.Tests;

public class RuleCatalogTests
{
    private readonly IReadOnlyList<CleanupRule> _rules = RuleCatalog.CreateDefault();

    [Fact]
    public void AllRuleIds_AreUnique()
    {
        _rules.Select(r => r.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void KeepRules_AlwaysHaveHighestPriority()
    {
        var maxNonKeep = _rules.Where(r => r.Level != SafetyLevel.Keep).Max(r => r.Priority);
        _rules.Where(r => r.Level == SafetyLevel.Keep)
            .Should().OnlyContain(r => r.Priority > maxNonKeep);
    }

    [Fact]
    public void CoreSystemRoots_ArePresent()
    {
        string[] expected =
        [
            "keep-windows", "keep-program-files", "keep-programdata",
            "keep-recovery", "keep-svi", "keep-pagefile", "keep-hiberfil", "keep-swapfile",
        ];
        _rules.Select(r => r.Id).Should().Contain(expected);
    }

    [Fact]
    public void AdminRules_AreFlagged()
    {
        _rules.Single(r => r.Id == "safe-windows-temp").NeedsAdmin.Should().BeTrue();
        _rules.Single(r => r.Id == "safe-windows-update-cache").NeedsAdmin.Should().BeTrue();
        _rules.Single(r => r.Id == "safe-user-temp").NeedsAdmin.Should().BeFalse();
    }

    [Fact]
    public void TempRules_OnlyDeleteContents()
    {
        _rules.Single(r => r.Id == "safe-user-temp").DeleteContentsOnly.Should().BeTrue();
        _rules.Single(r => r.Id == "safe-windows-temp").DeleteContentsOnly.Should().BeTrue();
        _rules.Single(r => r.Id == "safe-recycle-bin").DeleteContentsOnly.Should().BeTrue();
    }

    [Fact]
    public void EnvRules_ResolveOnThisMachine()
    {
        // Các vị trí cốt lõi phải giải biến được trên máy Windows bất kỳ
        string[] mustResolve = ["safe-user-temp", "safe-windows-temp", "safe-windows-update-cache"];
        foreach (var id in mustResolve)
        {
            var rule = _rules.Single(r => r.Id == id);
            rule.Matcher.Should().BeOfType<EnvKnownLocationMatcher>()
                .Which.ResolvedPath.Should().NotBeNull($"rule '{id}' phải giải được env var");
        }
    }
}
