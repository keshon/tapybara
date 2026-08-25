using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TapRecorder.Core.Calls;

/// <summary>
/// Папка одного звонка и всё, что в ней лежит.
/// </summary>
/// <remarks>
/// Раскладка повторяет macOS-оригинал: папка на звонок, внутри аудио, мета и
/// (позже) транскрипт со скриншотами. Смысл в том, что такую папку можно
/// целиком отдать на разбор — аудио плюс контекст, а не голый файл.
/// <para>
/// Имена файлов английские и не зависят от языка интерфейса: их читает не
/// только человек, но и код, а переименование при смене языка сломало бы
/// уже записанные звонки.
/// </para>
/// </remarks>
public sealed record CallSession
{
    public const string MicFileName = "mic.wav";
    public const string SystemFileName = "system.wav";
    public const string MetaFileName = "meta.json";
    public const string TranscriptFileName = "transcript.md";

    public required string Directory { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Приложение, из-за которого запись началась. Пусто при ручном старте.</summary>
    public string? Trigger { get; init; }

    public TimeSpan Duration { get; init; }

    /// <summary>Участники разговора, кроме владельца микрофона.</summary>
    /// <remarks>
    /// Читается распознаванием: один собеседник — диаризация не нужна вовсе,
    /// его именем подписывается весь системный канал.
    /// </remarks>
    public IReadOnlyList<string> Participants { get; init; } = [];

    [JsonIgnore]
    public string MicPath => Path.Combine(Directory, MicFileName);

    [JsonIgnore]
    public string SystemPath => Path.Combine(Directory, SystemFileName);

    [JsonIgnore]
    public string MetaPath => Path.Combine(Directory, MetaFileName);

    [JsonIgnore]
    public string TranscriptPath => Path.Combine(Directory, TranscriptFileName);

    /// <summary>Имя папки: время начала и, если известно, приложение-триггер.</summary>
    public static string BuildFolderName(DateTimeOffset startedAt, string? trigger)
    {
        string stamp = startedAt.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(trigger) ? stamp : $"{stamp} ({Sanitize(trigger)})";
    }

    /// <summary>Убрать из имени приложения символы, недопустимые в пути.</summary>
    private static string Sanitize(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string([.. name.Where(c => !invalid.Contains(c))]).Trim();
    }
}

/// <summary>Чтение и запись <c>meta.json</c>.</summary>
public static class CallMeta
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Сохранить мету звонка.
    /// </summary>
    /// <remarks>
    /// Через временный файл и переименование: обрыв на середине оставил бы
    /// битый JSON, и звонок стал бы неопознаваемым для дальнейшей обработки.
    /// </remarks>
    public static void Save(CallSession session)
    {
        System.IO.Directory.CreateDirectory(session.Directory);

        string temp = session.MetaPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(session, Options));
        File.Move(temp, session.MetaPath, overwrite: true);
    }

    /// <summary>Прочитать мету, или null, если её нет или она испорчена.</summary>
    public static CallSession? Load(string callDirectory)
    {
        string path = Path.Combine(callDirectory, CallSession.MetaFileName);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            CallSession? session = JsonSerializer.Deserialize<CallSession>(File.ReadAllText(path), Options);

            // Папку могли переименовать или перенести — доверяем месту на диске,
            // а не тому, что записано в файле.
            return session is null ? null : session with { Directory = callDirectory };
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
