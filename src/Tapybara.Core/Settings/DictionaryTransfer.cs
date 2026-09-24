using System.Text.Json;
using System.Text.Json.Serialization;
using Tapybara.Core.Calls;

namespace Tapybara.Core.Settings;

/// <summary>
/// Словарь в файле: замены, подсказка модели, знакомые люди и их голоса.
/// </summary>
/// <remarks>
/// <para>
/// Словарь копится месяцами — это десятки поправок к тому, как модель
/// слышит ваши имена и термины. На новом компьютере вбивать его заново
/// руками никто не станет, а без него распознавание снова коверкает всё
/// то же самое. Поэтому словарь переносится одним файлом.
/// </para>
/// <para>
/// Только словарь, а не все настройки. Горячие клавиши, микрофон и папки
/// привязаны к машине, и перенос их на другую чаще ломает, чем помогает.
/// </para>
/// </remarks>
public sealed record DictionaryFile
{
    /// <summary>Метка формата: по ней отличаем свой файл от любого другого JSON.</summary>
    public const string FormatName = "tapybara-dictionary";

    /// <summary>
    /// Метка формата. Ставится при выгрузке, а не значением по умолчанию:
    /// иначе любой JSON без метки получил бы её при чтении и сошёл за словарь.
    /// </summary>
    public string? Format { get; init; }

    public int Version { get; init; } = 1;

    public DateTimeOffset Exported { get; init; }

    public Dictionary<string, string> Replacements { get; init; } = [];

    public string? Prompt { get; init; }

    public List<string> People { get; init; } = [];

    /// <summary>
    /// Модель, которой сняты слепки голосов.
    /// </summary>
    /// <remarks>
    /// Слепки разных моделей несравнимы: принять их значило бы молча
    /// испортить книгу голосов, которая потом никого не узнаёт.
    /// </remarks>
    public string? VoiceModel { get; init; }

    public Dictionary<string, PersonVoice> Voices { get; init; } = [];
}

/// <summary>Что изменилось после загрузки словаря из файла.</summary>
/// <param name="Settings">Настройки с принятым словарём.</param>
/// <param name="ReplacementsAdded">Новых замен.</param>
/// <param name="ReplacementsChanged">Замен, у которых сменилось «как надо».</param>
/// <param name="PeopleAdded">Новых имён.</param>
/// <param name="PromptTaken">Взята ли подсказка из файла.</param>
public sealed record DictionaryMerge(
    AppSettings Settings,
    int ReplacementsAdded,
    int ReplacementsChanged,
    int PeopleAdded,
    bool PromptTaken);

/// <summary>Выгрузка словаря в файл и загрузка обратно.</summary>
public static class DictionaryTransfer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Собрать файл из текущих настроек.</summary>
    /// <param name="settings">Настройки, откуда берётся словарь.</param>
    /// <param name="voices">
    /// Книга голосов или <c>null</c>. Голоса попадают в файл, только если
    /// запоминание включено: выключенное запоминание значит «не хочу, чтобы
    /// мои голоса где-то лежали», и файл на флешке — тоже «где-то».
    /// </param>
    /// <param name="now">Время выгрузки.</param>
    public static DictionaryFile Export(
        AppSettings settings,
        IReadOnlyDictionary<string, PersonVoice>? voices,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);

        bool withVoices = settings.RememberVoices && voices is { Count: > 0 };
        return new DictionaryFile
        {
            Format = DictionaryFile.FormatName,
            Exported = now,
            Replacements = new Dictionary<string, string>(settings.Replacements),
            Prompt = string.IsNullOrWhiteSpace(settings.Prompt) ? null : settings.Prompt,
            People = [.. settings.KnownParticipants],
            VoiceModel = withVoices ? settings.VoiceEmbeddingModelFileName : null,
            Voices = withVoices ? new Dictionary<string, PersonVoice>(voices!) : [],
        };
    }

    public static string Serialize(DictionaryFile file) => JsonSerializer.Serialize(file, Options);

    /// <summary>Прочитать файл словаря.</summary>
    /// <exception cref="InvalidDataException">Это не словарь Tapybara или файл испорчен.</exception>
    public static DictionaryFile Parse(string json)
    {
        DictionaryFile? file;
        try
        {
            file = JsonSerializer.Deserialize<DictionaryFile>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Файл словаря испорчен.", ex);
        }

        // Метку проверяем явно: любой JSON-объект разобрался бы в пустой
        // словарь, и «загрузка» чужого файла тихо ничего бы не сделала.
        if (file is null || file.Format != DictionaryFile.FormatName)
        {
            throw new InvalidDataException("Это не файл словаря Tapybara.");
        }

        return file;
    }

    /// <summary>
    /// Влить словарь из файла в текущие настройки.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Сливаем, а не заменяем: на новой машине словарь мог уже начать
    /// расти, и загрузка старого файла не должна стирать свежие поправки.
    /// </para>
    /// <para>
    /// При споре о замене прав файл: его загружают нарочно, чтобы получить
    /// то, что в нём. Подсказку модели берём, только если своей нет или
    /// стоит подсказка по умолчанию, — свою, написанную руками, файл не
    /// затирает.
    /// </para>
    /// </remarks>
    public static DictionaryMerge Merge(AppSettings current, DictionaryFile file)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(file);

        var replacements = new Dictionary<string, string>(current.Replacements);
        int added = 0, changed = 0;
        foreach ((string heard, string correct) in file.Replacements)
        {
            string from = heard.Trim(), to = correct.Trim();
            if (from.Length == 0 || to.Length == 0)
            {
                continue;
            }

            if (!replacements.TryGetValue(from, out string? existing))
            {
                added++;
            }
            else if (existing != to)
            {
                changed++;
            }
            else
            {
                continue;
            }

            replacements[from] = to;
        }

        // Свои имена остаются впереди — это те, с кем говорили недавно на
        // этой машине; новые из файла встают в хвост.
        var people = new List<string>(current.KnownParticipants);
        int peopleAdded = 0;
        foreach (string raw in file.People)
        {
            string name = raw.Trim();
            if (name.Length > 0 && !people.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                people.Add(name);
                peopleAdded++;
            }
        }

        if (people.Count > KnownParticipants.MaxRemembered)
        {
            peopleAdded -= people.Count - KnownParticipants.MaxRemembered;
            people.RemoveRange(KnownParticipants.MaxRemembered, people.Count - KnownParticipants.MaxRemembered);
        }

        string? prompt = file.Prompt?.Trim();
        bool ownPrompt = !string.IsNullOrWhiteSpace(current.Prompt)
                         && current.Prompt != LanguageDefaults.DefaultPrompt(current.Language);
        bool takePrompt = !string.IsNullOrEmpty(prompt) && !ownPrompt && prompt != current.Prompt;

        AppSettings merged = current with
        {
            Replacements = replacements,
            KnownParticipants = people,
            Prompt = takePrompt ? prompt : current.Prompt,
        };

        return new DictionaryMerge(merged, added, changed, Math.Max(peopleAdded, 0), takePrompt);
    }

    /// <summary>Годятся ли голоса из файла для этой машины.</summary>
    public static bool VoicesFit(DictionaryFile file, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(settings);

        return settings.RememberVoices
               && file.Voices.Count > 0
               && string.Equals(file.VoiceModel, settings.VoiceEmbeddingModelFileName, StringComparison.OrdinalIgnoreCase);
    }
}
