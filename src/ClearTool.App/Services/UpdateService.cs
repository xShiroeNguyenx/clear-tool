using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using ClearTool.Core.Update;

namespace ClearTool.App.Services;

/// <summary>Thông tin một bản release mới hơn tìm thấy trên GitHub.</summary>
public sealed record UpdateInfo(
    ReleaseVersion Version,
    string TagName,
    string DownloadUrl,
    string ReleasePageUrl,
    long AssetSizeBytes);

public interface IUpdateService
{
    /// <summary>Phiên bản đang chạy (đọc từ assembly).</summary>
    ReleaseVersion CurrentVersion { get; }

    /// <summary>
    /// Hỏi GitHub release mới nhất. Trả null nếu đã là bản mới nhất, không có
    /// mạng, hoặc bất kỳ lỗi nào — kiểm tra cập nhật KHÔNG bao giờ làm phiền user.
    /// </summary>
    Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default);

    /// <summary>
    /// Tải exe mới về temp rồi khởi chạy một script trung gian: chờ tiến trình
    /// hiện tại thoát → ghi đè file đang chạy → mở lại app. Trả true nếu script
    /// đã được khởi chạy (caller PHẢI thoát app để mở khóa file đang chạy).
    /// </summary>
    Task<bool> DownloadAndApplyAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct = default);
}

/// <summary>
/// Tự cập nhật dựa trên GitHub Releases. App phát hành dạng 1 file exe
/// self-contained nên không thể tự ghi đè khi đang chạy — phải nhờ một script
/// .cmd chờ tiến trình thoát rồi mới đổi file và mở lại.
/// </summary>
public sealed class UpdateService : IUpdateService, IDisposable
{
    private const string Owner = "xShiroeNguyenx";
    private const string Repo = "clear-tool";
    private const string AssetName = "ClearTool.App.exe";

    private static readonly string LatestReleaseApi =
        $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
    private static readonly string ReleasesPage =
        $"https://github.com/{Owner}/{Repo}/releases/latest";
    private static readonly string LatestAssetUrl =
        $"https://github.com/{Owner}/{Repo}/releases/latest/download/{AssetName}";

    private readonly HttpClient _http;

    public ReleaseVersion CurrentVersion { get; }

    public UpdateService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        // GitHub API bắt buộc có User-Agent, nếu không sẽ trả 403.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ClearTool-Updater");
        CurrentVersion = ResolveCurrentVersion();
    }

    private static ReleaseVersion ResolveCurrentVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(UpdateService).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (ReleaseVersion.TryParse(info, out var v))
            return v;
        if (asm.GetName().Version is { } av && ReleaseVersion.TryParse(av.ToString(), out v))
            return v;
        return new ReleaseVersion(0, 0, 0);
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                AppLog.Info($"Update check: HTTP {(int)resp.StatusCode}");
                return null;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            // releases/latest đã loại prerelease, nhưng draft thì phòng thêm.
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                return null;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (!ReleaseVersion.TryParse(tag, out var latest) || latest <= CurrentVersion)
                return null;

            var pageUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() : null;

            string? downloadUrl = null;
            long size = 0;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (!string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    downloadUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (asset.TryGetProperty("size", out var s))
                        size = s.GetInt64();
                    break;
                }
            }

            return new UpdateInfo(
                latest,
                tag!,
                downloadUrl ?? LatestAssetUrl,
                pageUrl ?? ReleasesPage,
                size);
        }
        catch (Exception ex)
        {
            AppLog.Error("CheckForUpdate", ex);
            return null;
        }
    }

    public async Task<bool> DownloadAndApplyAsync(
        UpdateInfo update, IProgress<double>? progress, CancellationToken ct = default)
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe))
        {
            AppLog.Info("Update: Environment.ProcessPath null — không thể tự thay file");
            return false;
        }

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClearTool", "update");
        Directory.CreateDirectory(dir);
        var newExe = Path.Combine(dir, "ClearTool.App.new.exe");

        // 1) Tải exe mới (stream + báo tiến độ)
        using (var resp = await _http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? update.AssetSizeBytes;

            await using var source = await resp.Content.ReadAsStreamAsync(ct);
            await using var dest = new FileStream(newExe, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long readTotal = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, read), ct);
                readTotal += read;
                if (total > 0)
                    progress?.Report(Math.Min(1.0, (double)readTotal / total));
            }
        }

        // 2) Kiểm tra sơ bộ: exe self-contained ~75 MB, file quá nhỏ chắc chắn hỏng.
        if (new FileInfo(newExe).Length < 1_000_000)
        {
            TryDelete(newExe);
            throw new InvalidOperationException("File tải về không hợp lệ (quá nhỏ).");
        }

        // 3) Script trung gian: chờ app thoát → đổi file → mở lại bản mới.
        var script = Path.Combine(dir, "apply-update.cmd");
        await File.WriteAllTextAsync(script, BuildSwapScript(Environment.ProcessId, newExe, currentExe), ct);

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            // /c ""path"" — bọc hai lớp nháy để cmd xử lý đúng path có dấu cách.
            Arguments = $"/c \"\"{script}\"\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });

        return true;
    }

    private static string BuildSwapScript(int pid, string newExe, string targetExe)
    {
        // ping dùng làm "sleep ~1s" — đáng tin hơn 'timeout' khi không có console.
        string[] lines =
        [
            "@echo off",
            "setlocal",
            $"set \"PID={pid}\"",
            $"set \"NEW={newExe}\"",
            $"set \"TARGET={targetExe}\"",
            "",
            ":wait",
            "tasklist /FI \"PID eq %PID%\" 2>nul | find \"%PID%\" >nul",
            "if not errorlevel 1 (",
            "    ping -n 2 127.0.0.1 >nul",
            "    goto wait",
            ")",
            "ping -n 2 127.0.0.1 >nul",
            "",
            "move /y \"%NEW%\" \"%TARGET%\" >nul",
            "start \"\" \"%TARGET%\"",
            "(goto) 2>nul & del \"%~f0\"",
        ];
        return string.Join("\r\n", lines);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* dọn dẹp best-effort */ }
    }

    public void Dispose() => _http.Dispose();
}
