namespace ClearTool.Core.Deletion;

/// <summary>
/// Hàng rào an toàn ĐỘC LẬP với rules engine (phòng thủ nhiều lớp).
/// Dù rule gắn nhãn sai hay UI tick nhầm, DeletionService vẫn từ chối
/// xóa thư mục hệ thống / profile user / root ổ đĩa.
/// </summary>
public sealed class ProtectedRoots
{
    private static readonly StringComparison Cmp = StringComparison.OrdinalIgnoreCase;

    /// <summary>Từ chối path BẰNG hoặc NẰM DƯỚI các root này.</summary>
    private readonly List<string> _fullyProtected = [];

    /// <summary>Từ chối CHÍNH path đó (con bên trong vẫn xóa được — vd cache trong %USERPROFILE%).</summary>
    private readonly List<string> _selfProtected = [];

    /// <summary>Ngoại lệ trong vùng fully-protected: CON bên trong xóa được, chính nó thì không (vd %WINDIR%\Temp).</summary>
    private readonly List<string> _allowedSubtrees = [];

    private static readonly string[] ProtectedFileNames =
        ["pagefile.sys", "hiberfil.sys", "swapfile.sys"];

    public ProtectedRoots()
    {
        AddFullyProtected("%WINDIR%");
        AddFullyProtected("%ProgramFiles%");
        AddFullyProtected("%ProgramFiles(x86)%");
        AddFullyProtected("%ProgramData%");

        AddSelfProtected("%USERPROFILE%");
        // C:\Users (mọi ổ): thêm theo từng ổ hiện có
        foreach (var drive in DriveInfo.GetDrives())
        {
            _selfProtected.Add(Canonical(Path.Join(drive.Name, "Users")));
            _fullyProtected.Add(Canonical(Path.Join(drive.Name, "Recovery")));
            _fullyProtected.Add(Canonical(Path.Join(drive.Name, "System Volume Information")));
            _fullyProtected.Add(Canonical(Path.Join(drive.Name, "$WinREAgent")));
            _fullyProtected.Add(Canonical(Path.Join(drive.Name, "PerfLogs")));
            _fullyProtected.Add(Canonical(Path.Join(drive.Name, "System Repair")));
        }

        AddAllowedSubtree(@"%WINDIR%\Temp");
        AddAllowedSubtree(@"%WINDIR%\SoftwareDistribution\Download");
    }

    /// <summary>Ctor cho unit test — tự cấu hình danh sách, không đọc môi trường.</summary>
    public ProtectedRoots(
        IEnumerable<string> fullyProtected,
        IEnumerable<string> selfProtected,
        IEnumerable<string> allowedSubtrees)
    {
        _fullyProtected.AddRange(fullyProtected.Select(Canonical));
        _selfProtected.AddRange(selfProtected.Select(Canonical));
        _allowedSubtrees.AddRange(allowedSubtrees.Select(Canonical));
    }

    /// <summary>
    /// true = path bị bảo vệ, KHÔNG được xóa. Luôn canonicalize trước khi so.
    /// </summary>
    public bool IsProtected(string path, out string reason)
    {
        var canonical = Canonical(path);

        // Root ổ đĩa ("C:\")
        var pathRoot = Path.GetPathRoot(canonical + Path.DirectorySeparatorChar);
        if (pathRoot is not null &&
            canonical.Equals(Path.TrimEndingDirectorySeparator(pathRoot), Cmp))
        {
            reason = "Root ổ đĩa — không bao giờ xóa.";
            return true;
        }

        // pagefile.sys / hiberfil.sys / swapfile.sys ở bất kỳ root ổ nào
        var fileName = Path.GetFileName(canonical);
        var parent = Path.GetDirectoryName(canonical);
        if (parent is not null && pathRoot is not null
            && parent.Equals(Path.TrimEndingDirectorySeparator(pathRoot), Cmp)
            && ProtectedFileNames.Any(f => f.Equals(fileName, Cmp)))
        {
            reason = $"{fileName} — file hệ thống (paging/hibernation).";
            return true;
        }

        foreach (var self in _selfProtected)
        {
            if (canonical.Equals(self, Cmp))
            {
                reason = $"'{self}' — root được bảo vệ (chỉ xóa được nội dung bên trong).";
                return true;
            }
        }

        foreach (var root in _fullyProtected)
        {
            if (!canonical.Equals(root, Cmp) && !IsUnder(canonical, root))
                continue;

            // Ngoại lệ: con bên trong allowed-subtree xóa được, chính nó thì không
            foreach (var allowed in _allowedSubtrees)
            {
                if (IsUnder(canonical, allowed))
                {
                    reason = "";
                    return false;
                }
            }

            reason = $"Nằm trong vùng hệ thống được bảo vệ '{root}'.";
            return true;
        }

        reason = "";
        return false;
    }

    /// <summary>path nằm THẬT SỰ dưới root (không tính bằng nhau).</summary>
    private static bool IsUnder(string canonicalPath, string canonicalRoot) =>
        canonicalPath.Length > canonicalRoot.Length + 1 &&
        canonicalPath.StartsWith(canonicalRoot, Cmp) &&
        canonicalPath[canonicalRoot.Length] == Path.DirectorySeparatorChar;

    private void AddFullyProtected(string template) => AddExpanded(_fullyProtected, template);
    private void AddSelfProtected(string template) => AddExpanded(_selfProtected, template);
    private void AddAllowedSubtree(string template) => AddExpanded(_allowedSubtrees, template);

    private static void AddExpanded(List<string> list, string template)
    {
        var expanded = Environment.ExpandEnvironmentVariables(template);
        if (!expanded.Contains('%'))
            list.Add(Canonical(expanded));
    }

    /// <summary>
    /// "C:" (thiếu "\") là drive-relative path: GetFullPath sẽ resolve theo
    /// thư mục hiện hành của process trên ổ đó — kết quả phụ thuộc cwd.
    /// Với mục đích an toàn, hiểu "C:" là root ổ đĩa.
    /// </summary>
    internal static string Canonical(string path)
    {
        if (path.Length == 2 && path[1] == ':' && char.IsAsciiLetter(path[0]))
            path += Path.DirectorySeparatorChar;
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
