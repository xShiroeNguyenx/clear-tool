using ClearTool.Core.Model;

namespace ClearTool.Core.Tests;

/// <summary>Dựng cây TreeNode giả từ path bịa — không đụng ổ đĩa.</summary>
internal static class TestTreeBuilder
{
    public static TreeNode Root(string rootPath) =>
        new() { Name = rootPath, Kind = NodeKind.Directory };

    public static TreeNode Dir(this TreeNode parent, string name)
    {
        var dir = new TreeNode { Name = name, Kind = NodeKind.Directory, Parent = parent };
        parent.Children.Add(dir);
        return dir;
    }

    public static TreeNode File(this TreeNode parent, string name, long size)
    {
        var file = new TreeNode { Name = name, Kind = NodeKind.File, Parent = parent, OwnSize = size };
        parent.Children.Add(file);
        return file;
    }

    /// <summary>Dựng chuỗi thư mục theo path tuyệt đối có thật (vd %TEMP%) và trả về node lá.</summary>
    public static TreeNode EnsurePath(TreeNode root, string absolutePath)
    {
        var rootName = Path.TrimEndingDirectorySeparator(root.Name);
        var relative = Path.GetRelativePath(rootName, absolutePath);
        var current = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
        {
            var existing = current.Children.FirstOrDefault(c =>
                c.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
            current = existing ?? current.Dir(segment);
        }
        return current;
    }
}
