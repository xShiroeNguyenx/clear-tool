using System.Text;
using System.Text.RegularExpressions;
using ClearTool.Core.Model;

namespace ClearTool.Core.Rules.Matchers;

/// <summary>
/// Khớp glob trên full path: "**" = chuỗi bất kỳ (kể cả "\"),
/// "*" = chuỗi bất kỳ trong một segment, "?" = một ký tự.
/// Vd: "**\node_modules", "**\*-backup", "**\Code Cache".
/// </summary>
public sealed class GlobMatcher : IPathMatcher
{
    private readonly Regex _regex;
    private readonly bool _directoriesOnly;

    public GlobMatcher(string glob, bool directoriesOnly = true)
    {
        _regex = new Regex(GlobToRegex(glob), RegexOptions.IgnoreCase | RegexOptions.Compiled);
        _directoriesOnly = directoriesOnly;
    }

    public bool Matches(TreeNode node, string fullPath)
    {
        if (_directoriesOnly && node.Kind != NodeKind.Directory)
            return false;
        return _regex.IsMatch(fullPath);
    }

    private static string GlobToRegex(string glob)
    {
        var sb = new StringBuilder("^");
        for (int i = 0; i < glob.Length; i++)
        {
            char c = glob[i];
            switch (c)
            {
                case '*':
                    if (i + 1 < glob.Length && glob[i + 1] == '*')
                    {
                        sb.Append(".*");
                        i++;
                        // "**\" — cho phép khớp cả zero segment
                        if (i + 1 < glob.Length && glob[i + 1] == '\\')
                        {
                            sb.Append(@"\\?");
                            i++;
                        }
                    }
                    else
                    {
                        sb.Append(@"[^\\]*");
                    }
                    break;
                case '?':
                    sb.Append(@"[^\\]");
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
        sb.Append('$');
        return sb.ToString();
    }
}
