using ClearTool.Core.Admin;
using ClearTool.Core.Deletion;
using FluentAssertions;
using Xunit;

namespace ClearTool.Core.Tests;

public class ElevatedTaskTests
{
    [Theory]
    [InlineData("safe-windows-temp", DeleteMode.RecycleBin)]
    [InlineData("safe-windows-update-cache", DeleteMode.Permanent)]
    public void CleanTask_RoundTripsThroughArgumentString(string ruleId, DeleteMode mode)
    {
        var original = ElevatedTask.CleanRule(ruleId, mode);

        var parsed = Parse(original.ToArgumentString());

        parsed.Should().Be(original);
    }

    [Fact]
    public void WinSxsTask_RoundTripsThroughArgumentString()
    {
        Parse(ElevatedTask.WinSxs().ToArgumentString()).Should().Be(ElevatedTask.WinSxs());
    }

    [Fact]
    public void TryParse_FindsArgAmongOthers()
    {
        string[] args = ["--something-else", "--elevated-task=winsxs", "extra"];

        ElevatedTask.TryParse(args, out var task).Should().BeTrue();
        task!.Kind.Should().Be(ElevatedTask.WinSxsKind);
    }

    [Theory]
    [InlineData("--elevated-task=")]
    [InlineData("--elevated-task=clean")]
    [InlineData("--elevated-task=clean|")]
    [InlineData("--elevated-task=clean||RecycleBin")]
    [InlineData("--elevated-task=clean|rule|NotAMode")]
    [InlineData("--elevated-task=bogus|x|RecycleBin")]
    public void TryParse_RejectsMalformedPayloads(string arg)
    {
        ElevatedTask.TryParse([arg], out _).Should().BeFalse(arg);
    }

    [Fact]
    public void TryParse_NoArg_ReturnsFalse()
    {
        ElevatedTask.TryParse(["app.exe", "--other"], out _).Should().BeFalse();
    }

    private static ElevatedTask Parse(string argumentString)
    {
        ElevatedTask.TryParse([argumentString], out var task).Should().BeTrue();
        return task!;
    }
}
