using System.IO;

namespace ClearTool.App.Services;

/// <summary>Log lỗi đơn giản ra file — %LOCALAPPDATA%\ClearTool\cleartool.log.</summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string LogFile { get; } = Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClearTool", "cleartool.log");

    public static void Error(string context, Exception exception) =>
        Write($"[ERROR] {context}: {exception}");

    public static void Info(string message) =>
        Write($"[INFO] {message}");

    private static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
                File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging không bao giờ được làm app sập
        }
    }
}
