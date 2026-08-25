using System.Globalization;
using System.Text;
using Tapybara.Core.Settings;

namespace Tapybara.Core.Diagnostics;

/// <summary>
/// Журнал приложения — файл рядом с настройками.
/// </summary>
/// <remarks>
/// <para>
/// Приложение живёт в трее и окон в норме не показывает. Без журнала любая
/// ошибка выглядит как «оно просто исчезло»: ни сообщения, ни следа, ни
/// возможности спросить пользователя, что он делал. Один файл решает это
/// полностью.
/// </para>
/// <para>
/// Класс не бросает исключений НИКОГДА. Журнал — вспомогательная вещь, и
/// падение из-за того, что не удалось записать про падение, — худший из
/// возможных исходов.
/// </para>
/// </remarks>
public static class AppLog
{
    /// <summary>Размер, после которого файл уходит в <c>.1</c>, а запись начинается заново.</summary>
    private const long MaxBytes = 1024 * 1024;

    private static readonly Lock Gate = new();
    private static bool _failed;

    /// <summary>Папка с журналами.</summary>
    public static string LogDirectory => Path.Combine(AppPaths.DataDirectory, "logs");

    /// <summary>Файл текущего журнала.</summary>
    public static string LogPath => Path.Combine(LogDirectory, "tapybara.log");

    public static void Info(string message) => Write("INFO ", message, null);

    public static void Warn(string message, Exception? error = null) => Write("WARN ", message, error);

    public static void Error(string message, Exception? error = null) => Write("ERROR", message, error);

    /// <summary>Последние строки журнала — для окна «О программе».</summary>
    public static IReadOnlyList<string> Tail(int lines)
    {
        try
        {
            if (!File.Exists(LogPath))
            {
                return [];
            }

            lock (Gate)
            {
                // Открываем с ReadWrite-шарингом: файл в этот момент открыт
                // на запись нами же.
                using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                var ring = new Queue<string>(lines);
                while (reader.ReadLine() is { } line)
                {
                    if (ring.Count == lines)
                    {
                        ring.Dequeue();
                    }

                    ring.Enqueue(line);
                }

                return [.. ring];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            return [];
        }
    }

    private static void Write(string level, string message, Exception? error)
    {
        if (_failed)
        {
            return; // один раз не смогли — больше не пытаемся на каждой строке
        }

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                Roll();

                var line = new StringBuilder()
                    .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                    .Append(' ').Append(level).Append(' ').Append(message);

                if (error is not null)
                {
                    line.AppendLine().Append("    ").Append(error.GetType().Name)
                        .Append(": ").Append(error.Message);

                    if (error.StackTrace is { Length: > 0 } stack)
                    {
                        line.AppendLine().Append(stack);
                    }
                }

                File.AppendAllText(LogPath, line.AppendLine().ToString(), Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _failed = true;
        }
    }

    /// <summary>Один старый файл про запас: разбор ошибки почти всегда требует «что было до».</summary>
    private static void Roll()
    {
        var info = new FileInfo(LogPath);
        if (!info.Exists || info.Length < MaxBytes)
        {
            return;
        }

        string previous = LogPath + ".1";
        File.Delete(previous);
        File.Move(LogPath, previous);
    }
}
