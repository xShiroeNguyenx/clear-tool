using System.IO;
using ClearTool.Core.Admin;
using ClearTool.Core.Deletion;
using ClearTool.Core.Rules;
using ClearTool.Core.Rules.Matchers;

namespace ClearTool.App.Services;

/// <summary>
/// Chạy trong instance ĐÃ NÂNG QUYỀN: thực thi đúng một <see cref="ElevatedTask"/>
/// rồi thoát. Chỉ chấp nhận rule có NeedsAdmin trong catalog mặc định —
/// không bao giờ nhận path tùy ý từ command line.
/// </summary>
public static class ElevatedTaskRunner
{
    public const int ExitOk = 0;
    public const int ExitNotElevated = 10;
    public const int ExitUnknownTask = 11;
    public const int ExitTaskFailed = 12;

    public static async Task<int> RunAsync(ElevatedTask task)
    {
        if (!ElevationHelper.IsAdministrator())
            return ExitNotElevated;

        try
        {
            return task.Kind switch
            {
                ElevatedTask.WinSxsKind =>
                    await SystemCleanupTasks.RunWinSxsCleanupAsync(CancellationToken.None) == 0
                        ? ExitOk
                        : ExitTaskFailed,
                ElevatedTask.CleanKind => await RunCleanAsync(task),
                _ => ExitUnknownTask,
            };
        }
        catch (Exception)
        {
            return ExitTaskFailed;
        }
    }

    private static async Task<int> RunCleanAsync(ElevatedTask task)
    {
        // Whitelist: chỉ rule NeedsAdmin + known-location trong catalog mặc định
        var rule = RuleCatalog.CreateDefault().FirstOrDefault(r =>
            r.Id == task.RuleId && r.NeedsAdmin &&
            r.Matcher is EnvKnownLocationMatcher { ResolvedPath: not null });
        if (rule is null)
            return ExitUnknownTask;

        var target = ((EnvKnownLocationMatcher)rule.Matcher).ResolvedPath!;
        if (!Directory.Exists(target))
            return ExitOk; // không có gì để dọn

        IEnumerable<string> paths;
        if (rule.DeleteContentsOnly)
        {
            try
            {
                paths = Directory.EnumerateFileSystemEntries(target).ToList();
            }
            catch (Exception)
            {
                return ExitTaskFailed;
            }
        }
        else
        {
            paths = [target];
        }

        var service = new DeletionService(new ProtectedRoots());
        var results = await service.DeleteAsync(paths,
            new DeleteOptions { Mode = task.Mode }, null, CancellationToken.None);

        // File đang bị hệ thống khóa trong %WINDIR%\Temp là chuyện bình thường —
        // chỉ coi là fail khi KHÔNG xóa được mục nào trong khi có mục để xóa
        return results.Count == 0 || results.Any(r => r.Success) ? ExitOk : ExitTaskFailed;
    }
}
