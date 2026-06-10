using System.Diagnostics.CodeAnalysis;
using ClearTool.Core.Deletion;

namespace ClearTool.Core.Admin;

/// <summary>
/// Mô tả MỘT tác vụ admin được truyền qua command line khi relaunch nâng quyền.
/// Instance nâng quyền chỉ chạy đúng tác vụ này rồi thoát.
/// Format: --elevated-task=clean|&lt;ruleId&gt;|&lt;mode&gt; hoặc --elevated-task=winsxs
/// </summary>
public sealed record ElevatedTask(string Kind, string? RuleId, DeleteMode Mode)
{
    public const string ArgumentPrefix = "--elevated-task=";
    public const string CleanKind = "clean";
    public const string WinSxsKind = "winsxs";

    public static ElevatedTask CleanRule(string ruleId, DeleteMode mode) =>
        new(CleanKind, ruleId, mode);

    public static ElevatedTask WinSxs() => new(WinSxsKind, null, DeleteMode.Permanent);

    public string ToArgumentString() => Kind == WinSxsKind
        ? $"{ArgumentPrefix}{WinSxsKind}"
        : $"{ArgumentPrefix}{CleanKind}|{RuleId}|{Mode}";

    public static bool TryParse(IEnumerable<string> commandLineArgs, [NotNullWhen(true)] out ElevatedTask? task)
    {
        task = null;
        var raw = commandLineArgs.FirstOrDefault(a => a.StartsWith(ArgumentPrefix, StringComparison.Ordinal));
        if (raw is null)
            return false;

        var payload = raw[ArgumentPrefix.Length..];
        if (payload == WinSxsKind)
        {
            task = WinSxs();
            return true;
        }

        var parts = payload.Split('|');
        if (parts.Length == 3 && parts[0] == CleanKind
            && parts[1].Length > 0
            && Enum.TryParse<DeleteMode>(parts[2], out var mode))
        {
            task = CleanRule(parts[1], mode);
            return true;
        }
        return false;
    }
}
