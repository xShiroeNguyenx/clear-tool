using ClearTool.Core.Rules.Matchers;

namespace ClearTool.Core.Rules;

/// <summary>
/// Catalog mặc định — mã hóa kiến thức thực tế từ các phiên dọn máy dev.
/// Priority: KEEP=1000, SAFE=100, CAUTION=50.
/// </summary>
public static class RuleCatalog
{
    public const int KeepPriority = 1000;
    public const int SafePriority = 100;
    public const int CautionPriority = 50;

    public static IReadOnlyList<CleanupRule> CreateDefault()
    {
        var rules = new List<CleanupRule>();

        // ── 🔴 KEEP / SYSTEM — không bao giờ gợi ý xóa ─────────────────────
        AddKeepEnv(rules, "keep-windows", "%WINDIR%");
        AddKeepEnv(rules, "keep-program-files", "%ProgramFiles%");
        AddKeepEnv(rules, "keep-program-files-x86", "%ProgramFiles(x86)%");
        AddKeepEnv(rules, "keep-programdata", "%ProgramData%");
        AddKeepGlob(rules, "keep-recovery", @"?:\Recovery");
        AddKeepGlob(rules, "keep-svi", @"?:\System Volume Information");
        AddKeepGlob(rules, "keep-winreagent", @"?:\$WinREAgent");
        AddKeepGlob(rules, "keep-perflogs", @"?:\PerfLogs");
        AddKeepGlob(rules, "keep-system-repair", @"?:\System Repair");
        AddKeepGlob(rules, "keep-pagefile", @"?:\pagefile.sys", directoriesOnly: false);
        AddKeepGlob(rules, "keep-hiberfil", @"?:\hiberfil.sys", directoriesOnly: false);
        AddKeepGlob(rules, "keep-swapfile", @"?:\swapfile.sys", directoriesOnly: false);

        // ── 🟢 SAFE — cache/temp tái tạo được ──────────────────────────────
        AddSafeEnv(rules, "safe-user-temp", "Thư mục Temp của user", "%TEMP%",
            "File tạm của ứng dụng — Windows và app tự tạo lại khi cần.",
            deleteContentsOnly: true);
        AddSafeEnv(rules, "safe-localappdata-temp", "LocalAppData\\Temp", @"%LOCALAPPDATA%\Temp",
            "File tạm của ứng dụng — tự tạo lại khi cần.",
            deleteContentsOnly: true);
        AddSafeEnv(rules, "safe-windows-temp", "Windows\\Temp", @"%WINDIR%\Temp",
            "File tạm của hệ thống — an toàn để dọn.",
            deleteContentsOnly: true, needsAdmin: true);
        AddSafeEnv(rules, "safe-windows-update-cache", "Windows Update cache", @"%WINDIR%\SoftwareDistribution\Download",
            "Gói cập nhật đã tải về — Windows Update tự tải lại nếu cần.",
            deleteContentsOnly: true, needsAdmin: true);
        AddSafeEnv(rules, "safe-npm-cache", "npm cache", @"%LOCALAPPDATA%\npm-cache",
            "Cache gói npm — npm tự tải lại khi install.");
        AddSafeEnv(rules, "safe-pip-cache", "pip cache", @"%LOCALAPPDATA%\pip\cache",
            "Cache gói pip — pip tự tải lại khi install.");
        AddSafeEnv(rules, "safe-playwright-browsers", "Playwright browsers", @"%LOCALAPPDATA%\ms-playwright",
            "Browser binary của Playwright — 'npx playwright install' tải lại được.");

        rules.Add(new CleanupRule
        {
            Id = "safe-recycle-bin",
            DisplayName = "Thùng rác (Recycle Bin)",
            Level = SafetyLevel.Safe,
            Priority = SafePriority,
            Matcher = new GlobMatcher(@"?:\$Recycle.Bin"),
            Reason = "Nội dung Thùng rác — dọn bằng SHEmptyRecycleBin (không khôi phục được sau khi dọn).",
            DeleteContentsOnly = true,
        });

        AddSafeGlob(rules, "safe-browser-cache", "Cache trình duyệt/app", @"**\Cache",
            "Cache của trình duyệt/ứng dụng (Chrome, Edge, Electron app) — tự tạo lại.");
        AddSafeGlob(rules, "safe-code-cache", "Code Cache", @"**\Code Cache",
            "Cache mã đã biên dịch của trình duyệt/Electron app — tự tạo lại.");
        AddSafeGlob(rules, "safe-gpu-cache", "GPU Cache", @"**\GPUCache",
            "Cache shader GPU của trình duyệt/Electron app — tự tạo lại.");
        AddSafeGlob(rules, "safe-cached-data", "CachedData (VS Code/Cursor...)", @"**\CachedData",
            "Cache V8 của VS Code/Cursor/Antigravity — tự tạo lại.");
        AddSafeGlob(rules, "safe-app-logs", "Log ứng dụng (AppData)", @"**\AppData\Roaming\*\logs",
            "File log của ứng dụng — chỉ phục vụ debug, an toàn để xóa.");
        AddSafeGlob(rules, "safe-browser-recordings", "Antigravity browser_recordings", @"**\browser_recordings",
            "Bản ghi browser của Antigravity — artifact tạm, an toàn để xóa.");

        // ── 🟡 CAUTION — xóa được nhưng cần cân nhắc ───────────────────────
        AddCautionGlob(rules, "caution-node-modules", "node_modules", @"**\node_modules",
            "Dependencies của project — 'npm install' tải lại được, nhưng mất thời gian.");
        AddCautionGlob(rules, "caution-backup-dirs", "Thư mục *-backup", @"**\*-backup",
            "Thư mục backup thủ công — kiểm tra nội dung trước khi xóa.");
        AddCautionEnv(rules, "caution-dot-gemini", ".gemini", @"%USERPROFILE%\.gemini",
            "Dữ liệu Gemini CLI — không phải pure cache, có thể chứa config/history.");
        AddCautionEnv(rules, "caution-dot-vscode", ".vscode (extensions)", @"%USERPROFILE%\.vscode",
            "Extension VS Code — cài lại được nhưng mất thời gian.");
        AddCautionEnv(rules, "caution-dot-codex", ".codex", @"%USERPROFILE%\.codex",
            "Dữ liệu Codex CLI — không phải pure cache, có thể chứa config/history.");
        AddCautionEnv(rules, "caution-maven", ".m2 (Maven)", @"%USERPROFILE%\.m2",
            "Repository Maven local — build sau sẽ tải lại dependencies.");
        AddCautionEnv(rules, "caution-gradle", ".gradle", @"%USERPROFILE%\.gradle",
            "Cache Gradle — build sau sẽ tải lại dependencies.");
        AddCautionEnv(rules, "caution-eclipse", ".eclipse", @"%USERPROFILE%\.eclipse",
            "Dữ liệu Eclipse — tải/cài lại được.");

        return rules;
    }

    private static void AddKeepEnv(List<CleanupRule> rules, string id, string template) =>
        rules.Add(new CleanupRule
        {
            Id = id,
            DisplayName = template,
            Level = SafetyLevel.Keep,
            Priority = KeepPriority,
            Matcher = new EnvKnownLocationMatcher(template),
            Reason = "Thư mục hệ thống — không bao giờ xóa.",
        });

    private static void AddKeepGlob(List<CleanupRule> rules, string id, string glob, bool directoriesOnly = true) =>
        rules.Add(new CleanupRule
        {
            Id = id,
            DisplayName = glob,
            Level = SafetyLevel.Keep,
            Priority = KeepPriority,
            Matcher = new GlobMatcher(glob, directoriesOnly),
            Reason = "Thành phần hệ thống — không bao giờ xóa.",
        });

    private static void AddSafeEnv(List<CleanupRule> rules, string id, string displayName, string template,
        string reason, bool deleteContentsOnly = false, bool needsAdmin = false) =>
        rules.Add(new CleanupRule
        {
            Id = id,
            DisplayName = displayName,
            Level = SafetyLevel.Safe,
            Priority = SafePriority,
            Matcher = new EnvKnownLocationMatcher(template),
            Reason = reason,
            DeleteContentsOnly = deleteContentsOnly,
            NeedsAdmin = needsAdmin,
        });

    private static void AddSafeGlob(List<CleanupRule> rules, string id, string displayName, string glob, string reason) =>
        rules.Add(new CleanupRule
        {
            Id = id,
            DisplayName = displayName,
            Level = SafetyLevel.Safe,
            Priority = SafePriority,
            Matcher = new GlobMatcher(glob),
            Reason = reason,
        });

    private static void AddCautionGlob(List<CleanupRule> rules, string id, string displayName, string glob, string reason) =>
        rules.Add(new CleanupRule
        {
            Id = id,
            DisplayName = displayName,
            Level = SafetyLevel.Caution,
            Priority = CautionPriority,
            Matcher = new GlobMatcher(glob),
            Reason = reason,
        });

    private static void AddCautionEnv(List<CleanupRule> rules, string id, string displayName, string template, string reason) =>
        rules.Add(new CleanupRule
        {
            Id = id,
            DisplayName = displayName,
            Level = SafetyLevel.Caution,
            Priority = CautionPriority,
            Matcher = new EnvKnownLocationMatcher(template),
            Reason = reason,
        });
}
