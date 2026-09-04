using System.Globalization;
using Tapybara.Core.Diagnostics;

namespace Tapybara.Core.Calls;

/// <summary>В каком состоянии застали запись.</summary>
public enum CallState
{
    /// <summary>Пишется прямо сейчас.</summary>
    Recording,

    /// <summary>Распознаётся прямо сейчас.</summary>
    Transcribing,

    /// <summary>Транскрипт готов.</summary>
    Ready,

    /// <summary>Звук есть, транскрипта нет.</summary>
    NotTranscribed,

    /// <summary>Папка есть, звука в ней нет.</summary>
    Damaged,
}

/// <summary>Одна запись в списке звонков.</summary>
public sealed record CallEntry
{
    public required CallSession Session { get; init; }

    public required CallState State { get; init; }

    /// <summary>Сколько весят обе дорожки вместе.</summary>
    public long AudioBytes { get; init; }

    /// <summary>Есть ли рядом заметка о звонке.</summary>
    public bool HasNote { get; init; }

    public string Directory => Session.Directory;

    public string FolderName => Path.GetFileName(Session.Directory);
}

/// <summary>
/// Что лежит в папке записей.
/// </summary>
/// <remarks>
/// Состояние выводится из файлов на диске, а не хранится отдельно. Отдельный
/// «реестр звонков» пришлось бы чинить после каждого падения и он расходился
/// бы с реальностью при любой правке папки из Проводника — а папку правят,
/// это её назначение.
/// <para>
/// Про идущую запись и идущее распознавание диск знать не может: это состояние
/// живёт в памяти приложения и приходит сюда параметром.
/// </para>
/// </remarks>
public static class CallLibrary
{
    /// <summary>Имя файла с заметкой о звонке.</summary>
    public const string NoteFileName = "note.md";

    /// <summary>
    /// Собрать список записей, свежие сверху.
    /// </summary>
    /// <param name="root">Папка со звонками.</param>
    /// <param name="liveState">
    /// Что приложение прямо сейчас делает с этой папкой, или <c>null</c>,
    /// если ничего.
    /// </param>
    public static IReadOnlyList<CallEntry> Scan(string root, Func<string, CallState?>? liveState = null)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        var entries = new List<CallEntry>();

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Не удалось прочитать папку звонков.", ex);
            return [];
        }

        foreach (string directory in directories)
        {
            CallEntry? entry = Describe(directory, liveState?.Invoke(directory));
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return [.. entries.OrderByDescending(e => e.Session.StartedAt)];
    }

    /// <summary>Описать одну папку, или <c>null</c>, если это не звонок.</summary>
    public static CallEntry? Describe(string directory, CallState? liveState = null)
    {
        string folder = Path.GetFileName(directory);

        // Мета — источник правды, но её может не быть: приложение могли убить
        // между созданием папки и первой записью меты. Тогда всё, что мы знаем
        // о звонке, зашито в имя папки, и это лучше, чем не показать его вовсе.
        CallSession? session = CallMeta.Load(directory);
        if (session is null)
        {
            if (!TryParseFolderName(folder, out DateTimeOffset startedAt, out string? trigger))
            {
                return null; // посторонняя папка внутри папки звонков
            }

            session = new CallSession
            {
                Directory = directory,
                StartedAt = startedAt,
                Trigger = trigger,
            };
        }

        long micBytes = SizeOf(session.MicPath);
        long systemBytes = SizeOf(session.SystemPath);
        bool hasTranscript = File.Exists(session.TranscriptPath);

        // Заголовок WAV — 44 байта, и файл такого размера означает «дорожка
        // открылась и не получила ни одного сэмпла». Считать это звуком нельзя:
        // пользователь увидел бы запись, которую невозможно распознать.
        bool hasAudio = micBytes > 1024 || systemBytes > 1024;

        CallState state = liveState ?? (hasTranscript
            ? CallState.Ready
            : hasAudio ? CallState.NotTranscribed : CallState.Damaged);

        return new CallEntry
        {
            Session = session,
            State = state,
            AudioBytes = micBytes + systemBytes,
            HasNote = File.Exists(Path.Combine(directory, NoteFileName)),
        };
    }

    /// <summary>
    /// Разобрать имя папки, собранное <see cref="CallSession.BuildFolderName"/>.
    /// </summary>
    /// <remarks>
    /// Точный формат, а не «вытащить что-нибудь похожее на дату»: в папке
    /// звонков лежат и посторонние папки, и принять чужую за запись — значит
    /// показать её пользователю рядом с кнопкой «удалить».
    /// </remarks>
    internal static bool TryParseFolderName(string folder, out DateTimeOffset startedAt, out string? trigger)
    {
        startedAt = default;
        trigger = null;

        const string stampFormat = "yyyy-MM-dd HH-mm-ss";
        if (folder.Length < stampFormat.Length)
        {
            return false;
        }

        string stamp = folder[..stampFormat.Length];
        if (!DateTime.TryParseExact(
                stamp, stampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
        {
            return false;
        }

        startedAt = new DateTimeOffset(parsed, TimeZoneInfo.Local.GetUtcOffset(parsed));

        string rest = folder[stampFormat.Length..].Trim();
        if (rest.StartsWith('(') && rest.EndsWith(')') && rest.Length > 2)
        {
            trigger = rest[1..^1];
        }

        return true;
    }

    private static long SizeOf(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file.Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
